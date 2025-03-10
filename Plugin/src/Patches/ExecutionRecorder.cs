using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using BepInEx;
using HarmonyLib;
using Unity.Netcode;
// ReSharper disable PossibleMultipleEnumeration

namespace SequenceGenerator.Patches;

internal static class ExecutionRecorder
{
    [Serializable]
    public record ExecutionEvent(string type, string method, bool prefix)
    {
        public string type = type;
        public string method = method;
        public bool prefix = prefix;

        private sealed class TypeMethodEqualityComparer : IEqualityComparer<ExecutionEvent>
        {
            public bool Equals(ExecutionEvent x, ExecutionEvent y)
            {
                if (ReferenceEquals(x, y)) return true;
                if (x is null) return false;
                if (y is null) return false;
                if (x.GetType() != y.GetType()) return false;
                return x.type == y.type && x.method == y.method;
            }

            public int GetHashCode(ExecutionEvent obj)
            {
                return HashCode.Combine(obj.type, obj.method);
            }
        }

        public static IEqualityComparer<ExecutionEvent> IdentityComparer { get; } = new TypeMethodEqualityComparer();

        public virtual bool Equals(ExecutionEvent other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return type == other.type && method == other.method && prefix == other.prefix;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(type, method, prefix);
        }

        public override string ToString()
        {
            return $"{type}.{method}";
        }
    }

    private static readonly List<ExecutionEvent> Events = [];

    internal static readonly List<Assembly> TargetAssemblies = [];
    internal static readonly List<string> IgnoredMethods = [];

    public static long EventCount => Events.Count;

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
    private class MethodContext : IDisposable
    {
        private readonly string _type;
        private readonly string _method;
        private readonly bool _run;

        public MethodContext(string type, string method)
        {
            _type = type.EscapeForMermaid();
            _method = method.EscapeForMermaid();

            if (!Recording)
                return;

            if (SequenceGenerator.mainThread != Thread.CurrentThread)
                return;

            _run = true;

            Events.Add(new ExecutionEvent(_type, _method, true));
        }

        public void Dispose()
        {
            if (!_run)
                return;

            Events.Add(new ExecutionEvent(_type, _method, false));
        }
    }

    [HarmonyPatch]
    internal static class ActualPatch
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            var typesToPatch = TargetAssemblies.SelectMany(a => a.GetTypes())
                .Where(t => !t.IsGenericType && !t.IsValueType && !t.ContainsGenericParameters && !t.IsAbstract);

            var methodsToPatch = typesToPatch
                .SelectMany(t => t.GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public |
                                              BindingFlags.NonPublic))
                .Where(m => !IgnoredMethods.Contains($"{m.DeclaringType!.FullName}.{m.Name}"))
                .Where(m =>
                    !m.ContainsGenericParameters &&
                    !m.IsConstructor &&
                    !m.IsAbstract
                );

            SequenceGenerator.Log.LogWarning(
                $"Found {methodsToPatch.Count()} methods to patch across {typesToPatch.Count()} types");

            return methodsToPatch;
        }

        [HarmonyPrefix]
        static void Prefix(MethodBase __originalMethod, ref MethodContext __state)
        {
            __state = new MethodContext(__originalMethod.DeclaringType!.FullName, __originalMethod.MethodSignature());
        }

        [HarmonyPostfix]
        static void Finalizer(ref MethodContext __state)
        {
            __state.Dispose();
        }
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

        HashSet<string> knownChains = [""];
        HashSet<string> shownTypes = [];

        foreach (var eventItem in events)
        {
            if(!callStack.TryPeek(out var prec) && !eventItem.prefix)
                continue;

            var curr = eventItem;

            if (curr.prefix)
            {
                if (prec is not null && prec.type != curr.type)
                {
                    callChain.AppendLine($"    {callStack.TabsFromStack()}{prec.type} -->> {curr.type}: {curr.method}");
                }

                if (shownTypes.Add(curr.type))
                    callChain.AppendLine($"    {callStack.TabsFromStack()}Note over {curr.type}: {curr.type}");

                callChain.AppendLine($"    {callStack.TabsFromStack()}activate {curr.type}");

                callChain.AppendLine($"      {callStack.TabsFromStack()}Note right of {curr.type}: {curr.method}");

                callStack.Push(curr);
            }
            else
            {
                if (!ExecutionEvent.IdentityComparer.Equals(curr, prec))
                    throw new InvalidOperationException($"{curr} popped but {prec} was expected!");

                callStack.Pop();
                callStack.TryPeek(out prec);

                if (prec is not null && prec.type != curr.type)
                {
                    callChain.AppendLine($"      {callStack.TabsFromStack()}{curr.type} -->> {prec.type}: {prec.method}");
                }
                callChain.AppendLine($"    {callStack.TabsFromStack()}deactivate {curr.type}");

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
        return diagram.ToString();
    }

    public static string TabsFromStack<T>(this Stack<T> stack)
    {
        return new string(' ', stack.Count * 2);
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
        return input.Replace("<", "\u02c2").Replace(">", "\u02c3").Replace("+", "\uff0b");
    }
}
