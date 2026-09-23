using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Rooms;
using RandomForeseer.RandomForeseerCode.Common;

namespace RandomForeseer.RandomForeseerCode.OutOfCombat.Nodes;

/// <summary>
/// Top bar icon that reveals the upcoming normal combat rewards on hover or focus.
/// </summary>
/// <remarks>
/// Mirrors <see cref="NNextActPredictionIcon"/>: the same room-icon scene structure, the same vanilla room icon
/// texture lookup and the same vanilla HoverTip set, so the icon reads as part of the existing top bar row.
/// Predictions are rebuilt on each focus rather than per frame, so the tip always reflects live RNG state.
/// </remarks>
[ScriptPath("res://RandomForeseerCode/OutOfCombat/Nodes/NCombatRewardForecastIcon.cs")]
internal sealed partial class NCombatRewardForecastIcon : NClickableControl
{
    private const string ScenePath = $"{Entry.ResPath}/scenes/combat_reward_forecast_icon.tscn";

    private TextureRect _icon = null!;
    private TextureRect _outline = null!;

    public static NCombatRewardForecastIcon Create()
    {
        return GD.Load<PackedScene>(ScenePath).Instantiate<NCombatRewardForecastIcon>();
    }

    public override void _Ready()
    {
        // Mirrors res://scenes/ui/top_bar.tscn's BossIcon node structure.
        _icon = GetNode<TextureRect>("Icon");
        _outline = GetNode<TextureRect>("Icon/Outline");
        SetRoomIconAndOutline();
        ConnectSignals();
    }

    protected override void OnFocus()
    {
        IReadOnlyList<IHoverTip> tips;
        try
        {
            tips = CombatRewardForecast.GetHoverTips();
        }
        catch (Exception ex)
        {
            Entry.Logger.Warn($"Failed to forecast upcoming combat rewards: {ex}");
            tips = [PredictionHoverTips.Text("combat_reward_forecast_unavailable")];
        }

        if (tips.Count == 0)
        {
            return;
        }

        // Several fights of cards plus their potions do not fit under the icon, so the set is pinned:
        // cards top-left, potions and text top-right.
        PredictionHoverTipPlacement.Pin(NHoverTipSet.CreateAndShow(this, tips));
    }

    protected override void OnUnfocus()
    {
        NHoverTipSet.Remove(this);
    }

    private void SetRoomIconAndOutline()
    {
        // Reuses the vanilla monster room icon instead of shipping an icon asset with the mod.
        var roomIconPath = ImageHelper.GetRoomIconPath(MapPointType.Monster, RoomType.Monster, null);
        if (roomIconPath != null)
        {
            _icon.Texture = PreloadManager.Cache.GetTexture2D(roomIconPath);
        }
        else
        {
            // An untextured icon would still be hoverable but invisible, so make the cause diagnosable.
            Entry.Logger.Warn("Combat reward forecast icon has no vanilla monster room icon to reuse.");
        }

        var roomIconOutlinePath = ImageHelper.GetRoomIconOutlinePath(MapPointType.Monster, RoomType.Monster, null);
        if (roomIconOutlinePath != null)
        {
            _outline.Texture = PreloadManager.Cache.GetTexture2D(roomIconOutlinePath);
        }
    }
}
