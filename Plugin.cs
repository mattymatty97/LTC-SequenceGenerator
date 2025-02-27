using BepInEx.Logging;
using LethalSettings.UI;
using LethalSettings.UI.Components;
using SequenceGenerator.Patches;
using System;
using System.Collections;
using System.Reflection;
using System.Threading;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace SequenceGenerator;

[BepInDependency("com.willis.lc.lethalsettings", BepInDependency.DependencyFlags.HardDependency)]
[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    internal static ManualLogSource Log;

    internal static Thread mainThread = Thread.CurrentThread;

    private void Awake()
    {
        Log = Logger;
        
        Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), MyPluginInfo.PLUGIN_GUID);

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
            Name = MyPluginInfo.PLUGIN_NAME,
            Version = MyPluginInfo.PLUGIN_VERSION,
            Id = MyPluginInfo.PLUGIN_GUID,
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
