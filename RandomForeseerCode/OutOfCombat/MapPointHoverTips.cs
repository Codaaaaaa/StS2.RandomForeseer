using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Runs;
using RandomForeseer.RandomForeseerCode.Common;

namespace RandomForeseer.RandomForeseerCode.OutOfCombat;

/// <summary>
/// Adds prediction tips to map point hovers, extending the vanilla map tooltip rather than adding a new window.
/// </summary>
/// <remarks>
/// Vanilla only builds a map point tooltip when that point already has visit history
/// (<c>NMapPoint.OnFocus</c> -> <c>NHoverTipSet.CreateAndShowMapPointHistory</c>), so an unvisited node normally
/// shows nothing at all. <see cref="MapPointHoverTipPatches"/> therefore creates a prediction-owned, initially empty
/// hover tip set for map points that have no vanilla set, which the shared
/// <see cref="ControlHoverTipPredictionPatch"/> then fills through this provider.
/// </remarks>
internal static class MapPointHoverTips
{
    public static IReadOnlyList<IHoverTip> GetHoverTips(Control owner)
    {
        if (owner is not NMapPoint mapPoint ||
            !RandomForeseerSettings.IsPredictionFeatureEnabled(RandomForeseerSettings.EnableMapPointForecast))
        {
            return [];
        }

        return mapPoint.Point?.PointType switch
        {
            MapPointType.Unknown => GetUnknownPointTips(),
            MapPointType.Shop => GetShopPointTips(),
            MapPointType.Elite => GetElitePointTips(),
            _ => []
        };
    }

    /// <remarks>
    /// Like shops, elite rewards are generated on entering the room rather than bound to a map node, so this
    /// previews the next elite the player fights rather than this specific node.
    /// </remarks>
    private static IReadOnlyList<IHoverTip> GetElitePointTips()
    {
        if (!RandomForeseerSettings.IsPredictionFeatureEnabled(RandomForeseerSettings.EnableEliteRewardForecast) ||
            LocalPlayerResolver.GetLocalPlayer() is not { } player)
        {
            return [];
        }

        var preview = CombatRewardForecast.PredictElite(player);

        var tips = new List<IHoverTip>
        {
            PredictionHoverTips.Text("map_point_elite_forecast", description =>
            {
                description.Add("HasPotion", preview.Potion != null);
                description.Add("Potion", preview.Potion == null ? string.Empty : PredictionHoverTips.GetModelName(preview.Potion));
                description.Add("HasRelic", preview.Relic != null);
                description.Add("Relic", preview.Relic == null ? string.Empty : PredictionHoverTips.GetModelName(preview.Relic));
            })
        };

        // Cards land in the card container (top-left); the relic and potion are ordinary model tips
        // and land in the text container (top-right) with their vanilla icons and descriptions.
        tips.AddRange(PredictionHoverTips.CardBundles([preview.Cards]));
        if (preview.Relic != null)
        {
            tips.AddRange(PredictionHoverTips.Relics([preview.Relic]));
        }

        if (preview.Potion != null)
        {
            tips.AddRange(PredictionHoverTips.Potions([preview.Potion]));
        }

        return tips;
    }

    /// <remarks>
    /// Shop inventories are generated per visit, not per map node, so this previews the next shop the player
    /// would walk into rather than claiming to describe this particular node.
    /// </remarks>
    private static IReadOnlyList<IHoverTip> GetShopPointTips()
    {
        if (!RandomForeseerSettings.IsPredictionFeatureEnabled(RandomForeseerSettings.EnableShopForecast) ||
            LocalPlayerResolver.GetLocalPlayer() is not { } player ||
            ShopForecast.Predict(player, 1) is not [var shop, ..])
        {
            return [];
        }

        var tips = new List<IHoverTip>
        {
            PredictionHoverTips.Text("map_point_shop_forecast", description =>
            {
                description.Add("Cards", DescribeCards(shop));
                description.Add("Relics", Describe(shop.Relics.Select(r => (PredictionHoverTips.GetModelName(r.Relic), r.Cost))));
                description.Add("Potions", Describe(shop.Potions.Select(p => (PredictionHoverTips.GetModelName(p.Potion), p.Cost))));
            })
        };

        // Cards, relics and potions all render through their own vanilla hover tips.
        tips.AddRange(PredictionHoverTips.CardBundles(
        [
            shop.CharacterCards.Select(c => c.Card).ToList(),
            shop.ColorlessCards.Select(c => c.Card).ToList()
        ]));
        tips.AddRange(PredictionHoverTips.Relics(shop.Relics.Select(r => r.Relic)));
        tips.AddRange(PredictionHoverTips.Potions(shop.Potions.Select(p => p.Potion)));

        return tips;
    }

    private static List<string> DescribeCards(ShopPreview shop)
    {
        return shop.CharacterCards.Concat(shop.ColorlessCards)
            .Select(entry => entry.IsOnSale
                ? $"{entry.Card.Title} {entry.Cost}G (SALE)"
                : $"{entry.Card.Title} {entry.Cost}G")
            .ToList();
    }

    private static List<string> Describe(IEnumerable<(string Name, int Cost)> entries)
    {
        return entries.Select(entry => $"{entry.Name} {entry.Cost}G").ToList();
    }

    private static IReadOnlyList<IHoverTip> GetUnknownPointTips()
    {
        if (RunManager.Instance is not { IsInProgress: true, State: { } runState } ||
            EventForecast.PredictNextEvent(runState) is not { } nextEvent)
        {
            return [];
        }

        // A "?" only commits to a room type on entry, so the copy promises the event identity, not the room.
        return
        [
            PredictionHoverTips.Text(
                "map_point_event_forecast",
                description => description.Add("Event", nextEvent.Title.GetFormattedText()))
        ];
    }
}

/// <summary>
/// Gives map points a prediction-owned hover tip set when vanilla does not create one.
/// </summary>
[HarmonyPatch(typeof(NMapPoint))]
internal static class MapPointHoverTipPatches
{
    [HarmonyPatch("OnFocus")]
    [HarmonyPostfix]
    private static void OnFocus(NMapPoint __instance)
    {
        try
        {
            // Returns null when vanilla already owns a set for this point, leaving the history tooltip untouched.
            PredictionHoverTipPlacement.Pin(PredictionHoverTipSetHelper.EnsureHoverTipSet(__instance));
        }
        catch (Exception ex)
        {
            Entry.Logger.Warn($"Map point prediction hover tip failed: {ex}");
        }
    }

    [HarmonyPatch("OnUnfocus")]
    [HarmonyPostfix]
    private static void OnUnfocus(NMapPoint __instance)
    {
        try
        {
            PredictionHoverTipSetHelper.RemoveOwnedHoverTipSet(__instance);
        }
        catch (Exception ex)
        {
            Entry.Logger.Warn($"Map point prediction hover tip cleanup failed: {ex}");
        }
    }
}
