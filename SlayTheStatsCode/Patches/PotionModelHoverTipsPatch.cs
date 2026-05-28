using HarmonyLib;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;

namespace SlayTheStats.SlayTheStatsCode.Patches;

[HarmonyPatch(typeof(PotionModel), nameof(PotionModel.HoverTips), MethodType.Getter)]
public static class PotionModelHoverTipsPatch
{
    public static void Postfix(PotionModel __instance, ref IEnumerable<IHoverTip> __result)
    {
        if (__instance.IsCanonical) return;

        // Construct HoverTip
        var tip = new HoverTip();
        object boxed = tip;

        AccessTools.Property(typeof(HoverTip), nameof(HoverTip.Title)).SetValue(boxed, "Debug");
        AccessTools.Property(typeof(HoverTip), nameof(HoverTip.Description)).SetValue(boxed, "Test");

        tip = (HoverTip)boxed;

        var list = __result.ToList();
        list.Add(tip);
        __result = list;
    }
}
