using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Runs;
using SlayTheStats.SlayTheStatsCode.LiveStats;
using SlayTheStats.SlayTheStatsCode.RunData;

namespace SlayTheStats.SlayTheStatsCode.Patches;

/// <summary>Game quit: persist current tracker state so it survives the restart.</summary>
[HarmonyPatch(typeof(NGame), nameof(NGame.Quit))]
public static class QuitPatch
{
    public static void Prefix()
    {
        if (!RunManager.Instance.IsInProgress) return;
        SupplementaryStatsManager.OnQuit(RunDataManager.CurrentAscension, RunDataManager.CurrentBuildId);
    }
}
