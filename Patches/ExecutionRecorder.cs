using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx;
using HarmonyLib;
using Unity.Netcode;
using UnityEngine;

namespace SequenceGenerator.Patches;

[HarmonyPatch]
internal static class ExecutionRecorder
{
    [Serializable]
    public class ExecutionData
    {
        public List<ExecutionEvent> executionEvents = new List<ExecutionEvent>();
    }

    [Serializable]
    public record ExecutionEvent(string type, string method, bool prefix)
    {
        public string type = type;
        public string method = method;
        public bool prefix = prefix;
    }

    internal static ExecutionData data = new ExecutionData();

    private static Dictionary<string, List<string>> loggedAlready = [];

    private static bool _recording = false;
    public static bool Recording
    {
        get => _recording;
        set
        {
            if (value)
            {
                data.executionEvents.Clear();
                loggedAlready.Clear();
            }
            _recording = value;
        }
    }

    static IEnumerable<MethodBase> TargetMethods()
    {
        var typesToPatch = typeof(StartOfRound).Assembly
            .GetTypes()
            .Where(t => !t.IsGenericType && !t.IsValueType && !t.ContainsGenericParameters && !t.IsAbstract)
            .Where(t => t.IsSubclassOf(typeof(MonoBehaviour)) || t.IsSubclassOf(typeof(NetworkBehaviour)));

        var methodsToPatch = typesToPatch
            .SelectMany(t => t.GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            .Where(m =>
                !m.ContainsGenericParameters &&
                !m.IsConstructor &&
                !m.IsAbstract
            );

        Plugin.Log.LogWarning($"Found {methodsToPatch.Count()} methods to patch across {typesToPatch.Count()} types");

        return methodsToPatch;
    }

    static void Prefix(object[] __args, MethodBase __originalMethod, ref bool __state)
    {
        var type = __originalMethod.DeclaringType!.Name;
        var name = __originalMethod.Name;

        if (Ignore(type, name))
        {
            __state = true;
            return;
        }

        data.executionEvents.Add(new ExecutionEvent(type, __originalMethod.MethodSignature(), true));
    }

    static void Postfix(object[] __args, MethodBase __originalMethod, ref bool __state)
    {
        var type = __originalMethod.DeclaringType!.Name;

        if (__state)
            return;

        data.executionEvents.Add(new ExecutionEvent(type, __originalMethod.MethodSignature(), false));
    }

    static bool Ignore(string type, string name)
    {
        if (!Recording) return true;

        if (loggedAlready.TryGetValue(type, out var methods))
        {
            var contains = methods.Contains(name);
            if (contains)
            {
                return true;
            }
            else
            {
                methods.Add(name);
            }
        }
        else
        {
            loggedAlready.Add(type, [name]);
        }
        return false;
    }

    public static string ExportData()
    {
        var type = GameNetworkManager.Instance.isHostingGame ? "Server" : "Client";
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        // Export JSON
        string json = JsonConvert.SerializeObject(data, Formatting.Indented);
        string jsonPath = Path.Combine(Paths.CachePath, GeneratedPluginInfo.Name, $"{type}_{timestamp}_raw.json");
        Directory.CreateDirectory(Path.GetDirectoryName(jsonPath)!);
        File.WriteAllText(jsonPath, json);

        // Export Mermaid Sequence Diagram
        string mermaidDiagram = GenerateMermaidSequenceDiagram(data);
        string mmdPath = Path.Combine(Paths.CachePath, GeneratedPluginInfo.Name, $"{type}_{timestamp}_sequenceDiagram.mmd");
        Directory.CreateDirectory(Path.GetDirectoryName(mmdPath)!);
        File.WriteAllText(mmdPath, mermaidDiagram);

        return mmdPath;
    }

    private static string GenerateMermaidSequenceDiagram(ExecutionData data)
    {
        var diagram = new StringBuilder();
        diagram.AppendLine("sequenceDiagram");

        string previousType = null;

        foreach (var eventItem in data.executionEvents)
        {
            if (previousType != null && previousType != eventItem.type)
            {
                diagram.AppendLine($"    {previousType} -->> {eventItem.type}: ");
                if (eventItem.prefix)
                    diagram.AppendLine($"    note over {eventItem.type}: {eventItem.type}<br/>{eventItem.method}");
            }
            else if (eventItem.prefix)
            {
                diagram.AppendLine($"    note over {eventItem.type}: {eventItem.method}");
            }

            previousType = eventItem.type;
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
}
