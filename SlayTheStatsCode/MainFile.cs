using BaseLib.Config;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Debug;
using MegaCrit.Sts2.Core.Runs;
using SlayTheStats.SlayTheStatsCode.Config;
using SlayTheStats.SlayTheStatsCode.RunData;

namespace SlayTheStats.SlayTheStatsCode;

//You're recommended but not required to keep all your code in this package and all your assets in the SlayTheStats folder.
[ModInitializer(nameof(Initialize))]
public partial class MainFile : Node
{
    public const string ModId = "SlayTheStats"; //At the moment, this is used only for the Logger and harmony names.

    public static MegaCrit.Sts2.Core.Logging.Logger Logger { get; } =
        new(ModId, MegaCrit.Sts2.Core.Logging.LogType.Generic);

    public static void Initialize()
    {
        //If you want to use scripts defined in your mod for Godot scenes, uncomment the following line.
        //Godot.Bridge.ScriptManagerBridge.LookupScriptsInAssembly(Assembly.GetExecutingAssembly());

        ModConfigRegistry.Register(ModId, new SlayTheStatsConfig());

        string currentBuildId = ReleaseInfoManager.Instance.ReleaseInfo?.Version ?? "";
        RunDataManager.SetCurrentRun(0, currentBuildId);

        RunManager.Instance.RunStarted += state =>
            RunDataManager.SetCurrentRun(state.AscensionLevel, currentBuildId);

        Harmony harmony = new(ModId);

        harmony.PatchAll();
    }
}