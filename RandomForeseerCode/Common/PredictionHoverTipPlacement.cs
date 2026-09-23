using Godot;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.HoverTips;

namespace RandomForeseer.RandomForeseerCode.Common;

/// <summary>
/// Splits oversized prediction hover tips across the top corners instead of letting them trail their owner.
/// </summary>
/// <remarks>
/// Vanilla hover tips follow their owner control: <c>NHoverTipSet._Process</c> rewrites <c>GlobalPosition</c> from
/// the owner every frame, and the built-in overflow correction assumes a tip roughly the size of a card plus a short
/// text panel. Prediction tips are far larger - several fights of card rewards, or a whole shop inventory - so
/// anchoring them to a map node or a top bar icon pushes them off screen.
/// <para>
/// Pinning stops the follow behaviour and places the two vanilla containers in opposite top corners: predicted
/// cards on the left, and the text panel on the right. <c>NHoverTipSet.Init</c> already routes every
/// <c>CardHoverTip</c> into the card container and every other tip into the text container, so relic and potion
/// previews - which are ordinary model hover tips - land on the right with their descriptions.
/// </para>
/// <para>
/// Positioning is deferred by one frame so both containers have their final sizes before being measured, which
/// also puts it after the mod's own card bundle layout patches.
/// </para>
/// </remarks>
internal static class PredictionHoverTipPlacement
{
    private const float ScreenMargin = 32f;

    /// <summary>
    /// Pins a prediction-owned hover tip set: cards to the top-left, relics, potions and text to the top-right.
    /// </summary>
    /// <remarks>
    /// Safe to call with <see langword="null"/>, which happens when vanilla already owns the hover tip set for
    /// that control and the prediction layer deliberately left it alone.
    /// </remarks>
    public static void Pin(NHoverTipSet? tipSet)
    {
        if (tipSet == null)
        {
            return;
        }

        // Stops _Process from dragging the set back onto its owner every frame.
        tipSet._followOwner = false;

        Callable.From(() => Apply(tipSet)).CallDeferred();
    }

    private static void Apply(NHoverTipSet tipSet)
    {
        try
        {
            ApplyUnsafe(tipSet);
        }
        catch (Exception ex)
        {
            // Nothing catches a throw from a deferred callable, and a mispositioned tip beats a broken frame.
            Entry.Logger.Warn($"Failed to pin a prediction hover tip: {ex}");
        }
    }

    private static void ApplyUnsafe(NHoverTipSet tipSet)
    {
        if (!GodotObject.IsInstanceValid(tipSet) || NGame.Instance is not { } game)
        {
            return;
        }

        var cards = tipSet._cardHoverTipContainer;
        var text = tipSet._textHoverTipContainer;
        if (cards == null || text == null)
        {
            return;
        }

        var viewport = game.GetViewportRect().Size;

        // NHoverTipSet.SetAlignment only lays the card container out when the alignment is not None, and
        // prediction-owned sets are created with None. Without this call, several individual card tips - an elite
        // reward's three cards, for instance - keep their default position and stack on top of each other, leaving
        // only the last one visible. Card bundles are unaffected because their factory positions cards as it adds
        // them, and a single tip lays out to a zero offset, so running this for every prediction set is safe.
        cards.LayoutResizeAndReposition(new Vector2(ScreenMargin, ScreenMargin), HoverTipAlignment.Right);

        // Child positions from that pass are local, so moving the container afterwards keeps the spread.
        cards.GlobalPosition = new Vector2(ScreenMargin, ScreenMargin);

        // Right-aligned, but never pushed past the cards on a narrow window.
        var textX = Math.Max(viewport.X - ScreenMargin - text.Size.X, ScreenMargin);
        text.GlobalPosition = new Vector2(textX, ScreenMargin);
    }
}
