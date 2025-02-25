using BepInEx.Logging;
using LethalSettings.UI;
using LethalSettings.UI.Components;
using SequenceGenerator.Patches;
using System;
using System.Collections;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace SequenceGenerator;

[BepInDependency("com.willis.lc.lethalsettings", BepInDependency.DependencyFlags.HardDependency)]
[BepInPlugin(GeneratedPluginInfo.Identifier, GeneratedPluginInfo.Name, GeneratedPluginInfo.Version)]
public class Plugin : BaseUnityPlugin
{
    internal static ManualLogSource Log;

    private void Awake()
    {
        Log = Logger;
        
        Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), GeneratedPluginInfo.Identifier);

        var statusLabel = new LabelComponent
        {
            Text = ""
        };
        var recordButton = new ButtonComponent
        {
            Text = "Record",
            OnClick = (self) =>
            {
                if (ExecutionRecorder.Recording)
                {
                    // on stop recording
                    ExecutionRecorder.Recording = false;
                    ExecutionRecorder.Recording = false;
                    var outputMmdPath = ExecutionRecorder.ExportData();
                    GUIUtility.systemCopyBuffer = outputMmdPath;
                    self.Text = "Start Recording";
                    self.Enabled = false;
                    self.ShowCaret = false;
                    statusLabel.Text = "Exporting...";
                    GameNetworkManager.Instance.StartCoroutine(ExecuteAfter(2f, () =>
                    {
                        self.Enabled = true;
                        self.ShowCaret = true;
                        statusLabel.Text = "";
                    }));
                }
                else
                {
                    // on start recording
                    ExecutionRecorder.Recording = true;
                    self.Text = "Stop Recording";
                    statusLabel.Text = "Recording...";
                }
            }
        };

        ModMenu.RegisterMod(new ModMenu.ModSettingsConfig
        {
            Name = GeneratedPluginInfo.Name,
            Version = GeneratedPluginInfo.Version,
            Id = GeneratedPluginInfo.Identifier,
            Description = "A tool for generating sequence diagrams of in-game code execution order",
            MenuComponents = [ recordButton, statusLabel ]
        }, true, true);
    }

    private static IEnumerator ExecuteAfter(float delay, Action action)
    {
        yield return new WaitForSeconds(delay);
        action?.Invoke();
    }
}
