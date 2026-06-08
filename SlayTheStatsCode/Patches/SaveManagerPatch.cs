using HarmonyLib;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using SlayTheStats.SlayTheStatsCode.RunData;

namespace SlayTheStats.SlayTheStatsCode.Patches;

[HarmonyPatch(typeof(SaveManager), nameof(SaveManager.SaveRunHistory))]
public static class SaveManagerPatch
{
    public static void Postfix(RunHistory history)
    {
        ulong localPlayerId = PlatformUtil.GetLocalPlayerId(PlatformType.Steam);
        RunDataManager.GetInstance(history.Ascension, history.BuildId).AddRunToHistory(history, localPlayerId);
    }
}