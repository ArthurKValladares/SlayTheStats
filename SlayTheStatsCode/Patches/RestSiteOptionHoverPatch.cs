using SlayTheStats.SlayTheStatsCode.Config;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.RestSite;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using SlayTheStats.SlayTheStatsCode.RunData;

namespace SlayTheStats.SlayTheStatsCode.Patches;

[HarmonyPatch(typeof(RestSiteSynchronizer), nameof(RestSiteSynchronizer.LocalOptionHovered))]
public static class RestSiteOptionHoverPatch
{
    private static NRestSiteButton? _lastHoveredButton;

    public static void Postfix(RestSiteOption? option)
    {
        if (!SlayTheStatsConfig.ShowStatsHoverTips) return;

        // Always remove the previous tip first
        if (_lastHoveredButton != null)
        {
            NHoverTipSet.Remove(_lastHoveredButton);
            _lastHoveredButton = null;
        }

        if (option == null) return;

        var button = NRestSiteRoom.Instance?.GetButtonForOption(option);
        if (button == null) return;

        RunDataManager rdm    = RunDataManager.GetInstance(RunDataManager.CurrentAscension, RunDataManager.CurrentBuildId);
        RunDataManager rdmAll = RunDataManager.GetInstance(RunDataManager.CurrentAscension, RunDataManager.AllPatches);

        float choiceRate    = rdm.GetRestSiteChoiceRate(option.OptionId)    * 100f;
        float choiceRateAll = rdmAll.GetRestSiteChoiceRate(option.OptionId) * 100f;
        float? avgHp        = rdm.GetRestSiteChoiceAvgHp(option.OptionId);
        string avgHpStr     = avgHp.HasValue ? $"{avgHp.Value:F1}" : "N/A";

        var tip = new HoverTip();
        object boxed = tip;
        AccessTools.Property(typeof(HoverTip), nameof(HoverTip.Title)).SetValue(boxed, "Stats");
        AccessTools.Property(typeof(HoverTip), nameof(HoverTip.Description)).SetValue(boxed,
            $"Chosen {choiceRate:F1}% of campfire visits (all patches: {choiceRateAll:F1}%)\n" +
            $"Avg HP when chosen: {avgHpStr}");
        tip = (HoverTip)boxed;

        var tipSet = NHoverTipSet.CreateAndShow(button, tip);

        // Position the tip to the right of the button at the cursor's current height,
        // matching how other static hover tips follow their owner node.
        Vector2 mousePos = button.GetViewport().GetMousePosition();
        tipSet.GlobalPosition = new Vector2(button.GlobalPosition.X + button.Size.X + 10f, mousePos.Y);
        tipSet.SetFollowOwner();

        _lastHoveredButton = button;
    }
}
