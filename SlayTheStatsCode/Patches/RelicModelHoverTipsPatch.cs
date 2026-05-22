using HarmonyLib;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;

namespace SlayTheStats.SlayTheStatsCode.Patches;

[HarmonyPatch(typeof(RelicModel), nameof(RelicModel.HoverTips), MethodType.Getter)]
public static class RelicModelHoverTipsPatch
{
    public static void Postfix(RelicModel __instance, ref IEnumerable<IHoverTip> __result)
    {
        if (__instance.IsCanonical) return;

        // Construct HoverTip
        var tip = new HoverTip();
        object boxed = tip;
        AccessTools.Property(typeof(HoverTip), nameof(HoverTip.Title)).SetValue(boxed, "Test");
        AccessTools.Property(typeof(HoverTip), nameof(HoverTip.Description)).SetValue(boxed, "This is a test");
        tip = (HoverTip)boxed;
        
        var list = __result.ToList();
        list.Add(tip);
        __result = list;
    }
}