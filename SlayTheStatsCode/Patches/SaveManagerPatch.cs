using HarmonyLib;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using SlayTheStats.SlayTheStatsCode.RunData;

namespace SlayTheStats.SlayTheStatsCode.Patches;

[HarmonyPatch(typeof(SaveManager), nameof(SaveManager.SaveRunHistory))]
public static class SaveManagerPatch
{
    public static void Postfix(RunHistory history)
    {
        RunDataManager.Instance.AddRunToHistory(history);
    }
}