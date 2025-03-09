using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using BepInEx;
using HarmonyLib;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using MonoMod.Utils;
using Unity.Netcode;
// ReSharper disable PossibleMultipleEnumeration

namespace SequenceGenerator.Patches;

[HarmonyPatch]
internal static class ExecutionRecorder
{
    [Serializable]
    public record ExecutionEvent(string type, string method, bool prefix)
    {
        public string type = type;
        public string method = method;
        public bool prefix = prefix;
    }

    private static readonly List<ExecutionEvent> Events = [];

    internal static readonly List<Assembly> TargetAssemblies = [];
    internal static readonly List<string> IgnoredMethods = [];

    private static bool _recording = false;
    public static bool Recording
    {
        get => _recording;
        set
        {
            if (value)
            {
                Events.Clear();
            }
            _recording = value;
        }
    }

    static IEnumerable<MethodBase> TargetMethods()
    {
        var typesToPatch = TargetAssemblies.SelectMany(a => a.GetTypes())
            .Where(t => !t.IsGenericType && !t.IsValueType && !t.ContainsGenericParameters && !t.IsAbstract);

        var methodsToPatch = typesToPatch
            .SelectMany(t => t.GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            .Where(m => !IgnoredMethods.Contains($"{m.DeclaringType!.FullName}.{m.Name}"))
            .Where(m =>
                !m.ContainsGenericParameters &&
                !m.IsConstructor &&
                !m.IsAbstract
            );

        SequenceGenerator.Log.LogWarning($"Found {methodsToPatch.Count()} methods to patch across {typesToPatch.Count()} types");

        return methodsToPatch;
    }

    static void Prefix(object[] __args, MethodBase __originalMethod)
    {
        if (!Recording)
            return;

        if (Thread.CurrentThread != SequenceGenerator.mainThread)
            return;

        Events.Add(new ExecutionEvent(__originalMethod.DeclaringType!.FullName.EscapeForMermaid(), __originalMethod.MethodSignature().EscapeForMermaid(), true));
    }

    static void Postfix(object[] __args, MethodBase __originalMethod)
    {
        if (!Recording)
            return;

        if (Thread.CurrentThread != SequenceGenerator.mainThread)
            return;

        Events.Add(new ExecutionEvent(__originalMethod.DeclaringType!.FullName.EscapeForMermaid(),  __originalMethod.MethodSignature().EscapeForMermaid(), false));
    }

    public static void ExportData()
    {
        var dataClone = Events.ToList();
        var type = NetworkManager.Singleton.IsServer ? "Server" : (NetworkManager.Singleton.IsClient ? "Client" : "Menu");
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Export Mermaid Sequence Diagram
        var mermaidDiagram = GenerateMermaidSequenceDiagram(dataClone);
        var mmdPath = Path.Combine(Paths.CachePath, MyPluginInfo.PLUGIN_NAME, $"{type}_{timestamp}.mmd");
        Directory.CreateDirectory(Path.GetDirectoryName(mmdPath)!);
        File.WriteAllText(mmdPath, mermaidDiagram);
    }

    // ReSharper disable once FieldCanBeMadeReadOnly.Local
    private static bool _keepRepetitions = false;

    private static string GenerateMermaidSequenceDiagram(List<ExecutionEvent> events)
    {
        var diagram = new StringBuilder();
        diagram.AppendLine("sequenceDiagram");

        var callStack = new Stack<ExecutionEvent>();
        var callChain = new StringBuilder();
        var typeStatus = new Dictionary<string, int>();

        HashSet<string> knownChains = [""];
        HashSet<string> shownTypes = [];

        foreach (var eventItem in events)
        {
            if (callStack.Count <=0 && !eventItem.prefix)
                continue;

            callStack.TryPeek(out var prec);
            var curr = eventItem;

            if (curr.prefix)
            {
                if (prec is not null && prec.type != curr.type)
                {
                    callChain.AppendLine($"    {prec.type} -->> {curr.type}: {curr.method}");
                }

                if ((!typeStatus.TryGetValue(curr.type, out var count) || count == 0) && shownTypes.Add(curr.type))
                    callChain.AppendLine($"    Note over {curr.type}: {curr.type}");

                typeStatus[curr.type] = count + 1;
                callChain.AppendLine($"    activate {curr.type}");
                callChain.AppendLine($"    Note right of {curr.type}: {curr.method}");
                callStack.Push(curr);
            }
            else
            {
                if (curr.type != prec.type)
                    throw new InvalidOperationException($"{curr.type} popped but {prec.type} was expected!");

                if (typeStatus.TryGetValue(curr.type, out var count) &&  count > 0)
                    typeStatus[curr.type] = count - 1;

                callStack.TryPop(out _);
                callStack.TryPeek(out prec);

                if (prec is not null && prec.type != curr.type)
                {
                    callChain.AppendLine($"    {curr.type} -->> {prec.type}: {prec.method}");
                }
                callChain.AppendLine($"    deactivate {curr.type}");

                if (prec is null)
                {
                    shownTypes.Clear();
                    var chain = callChain.ToString();
                    callChain.Clear();
                    if (knownChains.Add(chain) || _keepRepetitions)
                    {
                        diagram.AppendLine("%% new callStack");
                        diagram.AppendLine("  rect GhostWhite");
                        diagram.Append(chain);
                        diagram.AppendLine("  end");
                    }
                }
            }
        }

        var lastChain = callChain.ToString();
        callChain.Clear();
        if (knownChains.Add(lastChain) || _keepRepetitions)
        {
            diagram.AppendLine("%% new callStack");
            diagram.AppendLine("  rect GhostWhite");
            diagram.Append(lastChain);
            diagram.AppendLine("  end");
        }
        return diagram.ToString();
    }


    public static string MethodSignature(this MethodBase mi)
    {
        var param = mi.GetParameters()
            .Select(p => p.ParameterType.Name)
            .ToArray();

        var signature = $"{mi.Name}({string.Join(",", param)})";

        return signature;
    }

    public static string EscapeForMermaid(this string input)
    {
        return input.Replace("<", "\u02c2").Replace(">", "\u02c3");
    }
}
