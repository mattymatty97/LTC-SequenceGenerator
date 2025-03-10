using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using MonoMod.RuntimeDetour;
using SequenceGenerator.Patches;

namespace SequenceGenerator;

[BepInPlugin(GUID, NAME, VERSION)]
internal class SequenceGenerator : BaseUnityPlugin
{

	// ReSharper disable once CollectionNeverQueried.Global
	internal static readonly ISet<Hook> Hooks = new HashSet<Hook>();
	internal static readonly Harmony Harmony = new Harmony(GUID);

	public static SequenceGenerator INSTANCE { get; private set; }

	public const string GUID = MyPluginInfo.PLUGIN_GUID;
	public const string NAME = MyPluginInfo.PLUGIN_NAME;
	public const string VERSION = MyPluginInfo.PLUGIN_VERSION;

	internal static ManualLogSource Log;

	internal static Thread mainThread;

	public static RecordingStatus Status { get; private set; } = RecordingStatus.Idle;

	private void Awake()
	{
		mainThread = Thread.CurrentThread;

		INSTANCE = this;
		Log = Logger;
		try
		{
			Log.LogInfo("Initializing Configs");

			PluginConfig.Init();

			PatchMethods();

			if (PluginConfig.StartRecording.Value)
			{
				ToggleRecording();
			}

			Log.LogInfo(NAME + " v" + VERSION + " Loaded!");

		}
		catch (Exception ex)
		{
			Log.LogError("Exception while initializing: \n" + ex);
		}
	}

	private static void PatchMethods()
	{
		if (ExecutionRecorder.Recording)
		{
			Log.LogFatal("Cannot patch while already recording!");
			return;
		}

		Harmony.UnpatchSelf();

		var targetAssemblies = PluginConfig.AssemblyTypes.Value.Split(",");

		var assemblies = AppDomain.CurrentDomain.GetAssemblies()
			.Where(a => !a.IsDynamic)
			.Where(a => targetAssemblies.Contains(a.GetName().Name))
			.GroupBy(a => a.GetName().Name)
			.Select(ag => ag.OrderBy(a => a.Location).First());

		var ignored = PluginConfig.IgnoredMethods.Value.Split(",");

		ExecutionRecorder.TargetAssemblies.Clear();
		ExecutionRecorder.TargetAssemblies.AddRange(assemblies);

		ExecutionRecorder.IgnoredMethods.Clear();
		ExecutionRecorder.IgnoredMethods.AddRange(ignored);

		if (ExecutionRecorder.TargetAssemblies.Count <= 0)
		{
			Log.LogFatal("Failed to find assemblies to track");
			return;
		}

		Log.LogInfo($"Patching Methods from {ExecutionRecorder.TargetAssemblies.Count} assemblies!");
		Harmony.PatchAll(typeof(ExecutionRecorder.ActualPatch));
	}

	private static void ToggleRecording()
	{
		switch (Status)
		{
			case RecordingStatus.Idle:
				ExecutionRecorder.Recording = true;
				Status = RecordingStatus.Recording;
				Log.LogWarning("Started Recording!");
				break;
			case RecordingStatus.Recording:
				ExecutionRecorder.Recording = false;
				Status = RecordingStatus.Saving;
				Log.LogWarning($"Stopped Recording! Trying to save {ExecutionRecorder.EventCount} events");
				Task.Factory.StartNew(() =>
				{
					try
					{
						ExecutionRecorder.ExportData();
					}
					catch (Exception ex)
					{
						Log.LogFatal($"Exception while saving:\n{ex}");
					}
					finally
					{
						Status = RecordingStatus.Idle;
						Log.LogWarning("Finished Saving!");
					}
				});
				//TODO: start saving thread
				break;
			case RecordingStatus.Saving:
				Log.LogFatal("Please wait the saving to finish!");
				break;
			default:
				throw new ArgumentOutOfRangeException();
		}
	}


	internal static class PluginConfig
	{

		internal static void Init()
		{
			var config = INSTANCE.Config;

			config.SaveOnConfigSet = false;
			//Initialize Configs

			StartRecording = config.Bind("Recording", "start_recording", false, "start recording immediately");
			AssemblyTypes = config.Bind("Recording", "assembly_types", "Assembly-CSharp", "assemblies to track");
			IgnoredMethods = config.Bind("Recording", "ignored_methods", "", "methods to be ignored");

			config.SaveOnConfigSet = true;
			CleanAndSave();
		}

		internal static ConfigEntry<bool> StartRecording { get; private set; }
		internal static ConfigEntry<string> AssemblyTypes { get; private set; }
		internal static ConfigEntry<string> IgnoredMethods { get; private set; }

		internal static void CleanAndSave()
		{
			var config = INSTANCE.Config;
			//remove unused options
			var orphanedEntriesProp = AccessTools.Property(config.GetType(),"OrphanedEntries");

			var orphanedEntries = (Dictionary<ConfigDefinition, string>)orphanedEntriesProp!.GetValue(config, null);

			orphanedEntries.Clear(); // Clear orphaned entries (Unbinded/Abandoned entries)
			config.Save(); // Save the config file
		}

	}

	internal enum RecordingStatus
	{
		Idle,
		Recording,
		Saving
	}

}
