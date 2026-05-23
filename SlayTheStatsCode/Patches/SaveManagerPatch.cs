using HarmonyLib;
using MegaCrit.Sts2.Core.Saves;
using SlayTheStats.SlayTheStatsCode.RunData;

namespace SlayTheStats.SlayTheStatsCode.Patches;

[HarmonyPatch(typeof(SaveManager), nameof(SaveManager.SaveRunHistory))]
public static class SaveManagerPatch
{
    public static void Postfix()
    {
        // TODO: Temp code, sketching out some stuff
        RunDataManager.Instance.TryLoadTest();
    }
}