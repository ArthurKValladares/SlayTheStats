using HarmonyLib;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using SlayTheStats.SlayTheStatsCode.RunData;

namespace SlayTheStats.SlayTheStatsCode.Patches;

[HarmonyPatch(typeof(CardModel), nameof(CardModel.HoverTips), MethodType.Getter)]
public static class CardModelHoverTipsPatch
{
    public static void Postfix(CardModel __instance, ref IEnumerable<IHoverTip> __result)
    {
        if (__instance.IsCanonical) return;

        // Construct HoverTip
        var tip = new HoverTip();
        object boxed = tip;
        
        AccessTools.Property(typeof(HoverTip), nameof(HoverTip.Title)).SetValue(boxed, "Test");
        int timesInDeck = RunDataManager.Instance.GetTimesInDeck(__instance.Id);
        AccessTools.Property(typeof(HoverTip), nameof(HoverTip.Description)).SetValue(boxed, $"Picked {timesInDeck} times");

        tip = (HoverTip)boxed;
        
        var list = __result.ToList();
        list.Add(tip);
        __result = list;
    }
}