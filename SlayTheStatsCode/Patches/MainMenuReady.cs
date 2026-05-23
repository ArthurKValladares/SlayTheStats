using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using SlayTheStats.SlayTheStatsCode.RunData;

namespace SlayTheStats.SlayTheStatsCode.Patches;

[HarmonyPatch(typeof(NMainMenu), nameof(NMainMenu._Ready))]
public static class MainMenuReadyPatch
{
    private static void Postfix()
    {
        Log.Info($"{MainFile.ModId}: Loading RunHistory data.");
        RunDataManager.Instance.LoadAllRuns();
    }
}