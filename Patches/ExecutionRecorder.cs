using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Unity.Netcode;
using UnityEngine;

namespace SequenceGenerator.Patches;

[HarmonyPatch]
internal class ExecutionRecorder
{
    [Serializable]
    public class ExecutionData
    {
        public List<ExecutionEvent> executionEvents = new List<ExecutionEvent>();
    }

    [Serializable]
    public class ExecutionEvent
    {
        public string type;
        public string method;

        public ExecutionEvent(string type, string method)
        {
            this.type = type;
            this.method = method;
        }
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

    static void Prefix(object[] __args, MethodBase __originalMethod)
    {
        var type = __originalMethod.DeclaringType.Name;
        var name = __originalMethod.Name;

        if (Ignore(type, name)) return;

        data.executionEvents.Add(new ExecutionEvent(__originalMethod.DeclaringType.Name, __originalMethod.Name));
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
        // Export JSON
        string json = JsonConvert.SerializeObject(data, Formatting.Indented);
        string jsonPath = Path.Combine(Paths.PluginPath, "raw.json");
        File.WriteAllText(jsonPath, json);

        // Export Mermaid Sequence Diagram
        string mermaidDiagram = GenerateMermaidSequenceDiagram(data);
        string mmdPath = Path.Combine(Paths.PluginPath, "sequenceDiagram.mmd");
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
                diagram.AppendLine($"    note over {eventItem.type}: {eventItem.type}<br/>{eventItem.method}");
            }
            else
            {
                diagram.AppendLine($"    note over {eventItem.type}: {eventItem.method}");
            }

            previousType = eventItem.type;
        }

        return diagram.ToString();
    }
}
