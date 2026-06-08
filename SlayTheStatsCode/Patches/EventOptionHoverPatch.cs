using SlayTheStats.SlayTheStatsCode.Config;
using HarmonyLib;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Events;
using SlayTheStats.SlayTheStatsCode.RunData;

namespace SlayTheStats.SlayTheStatsCode.Patches;

[HarmonyPatch(typeof(NEventOptionButton), "OnFocus")]
public static class EventOptionHoverPatch
{
    // Keyed by button instance so concurrent hovers (e.g. multiple players) stay isolated
    private static readonly Dictionary<NEventOptionButton, IEnumerable<IHoverTip>> _originalHoverTips = new();

    public static void Prefix(NEventOptionButton __instance)
    {
        if (!SlayTheStatsConfig.ShowStatsHoverTips) return;
        if (__instance.Option == null) return;

        ModelId eventId = __instance.Event.Id;
        string optionKey = __instance.Option.Title.LocEntryKey;

        RunDataManager rdm    = RunDataManager.GetInstance(RunDataManager.CurrentAscension, RunDataManager.CurrentBuildId);
        RunDataManager rdmAll = RunDataManager.GetInstance(RunDataManager.CurrentAscension, RunDataManager.AllPatches);

        float pickRate    = rdm.GetEventOptionPickRate(eventId, optionKey)    * 100f;
        float winRate     = rdm.GetEventOptionWinRate(optionKey)              * 100f;
        float pickRateAll = rdmAll.GetEventOptionPickRate(eventId, optionKey) * 100f;
        float winRateAll  = rdmAll.GetEventOptionWinRate(optionKey)           * 100f;

        var tip = new HoverTip();
        object boxed = tip;
        AccessTools.Property(typeof(HoverTip), nameof(HoverTip.Title)).SetValue(boxed, "Stats");
        AccessTools.Property(typeof(HoverTip), nameof(HoverTip.Description)).SetValue(boxed,
            $"Pick Rate: {pickRate:F1}% (all patches: {pickRateAll:F1}%)\n" +
            $"Win Rate when chosen: {winRate:F1}% (all: {winRateAll:F1}%)");
        tip = (HoverTip)boxed;

        // Save original and temporarily append our tip so OnFocus shows the combined set
        _originalHoverTips[__instance] = __instance.Option.HoverTips;
        __instance.Option.HoverTips = __instance.Option.HoverTips.Append(tip);
    }

    public static void Postfix(NEventOptionButton __instance)
    {
        if (!_originalHoverTips.TryGetValue(__instance, out var original)) return;
        __instance.Option.HoverTips = original;
        _originalHoverTips.Remove(__instance);
    }
}