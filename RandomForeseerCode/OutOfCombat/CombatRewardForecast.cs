using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using RandomForeseer.RandomForeseerCode.Common;

namespace RandomForeseer.RandomForeseerCode.OutOfCombat;

/// <summary>One predicted normal combat reward.</summary>
/// <remarks>
/// Gold is intentionally not forecast. <see cref="OutOfCombatPredictionUtils.FastForwardBeforeMonsterCardReward"/>
/// mirrors the gold roll's RNG consumption, which is all the forecast needs to stay aligned with the live stream;
/// GoldReward's amount formula is not mirrored because no surface displays the value.
/// </remarks>
internal sealed record CombatRewardPreview(
    IReadOnlyList<CardModel> Cards,
    PotionModel? Potion,
    RelicModel? Relic = null);

/// <summary>
/// Predicts the next normal combat rewards from the current run state, without advancing any live RNG.
/// </summary>
/// <remarks>
/// Every roll runs against a <see cref="RunPredictionContext"/>, which owns cloned Rewards/Shops/Niche streams,
/// a cloned relic grab bag, a cloned deck and cloned rarity/potion odds. The live <c>PlayerRng</c> is never touched.
/// <para>
/// The forecast chains one monster room after another. Anything that consumes Rewards RNG between those fights
/// (an event reward, a relic pickup, a shop purchase) shifts the stream, so entries after the next fight are
/// conditional on nothing else intervening. Predictions are rebuilt on each hover, so the first entry always
/// reflects the current live state.
/// </para>
/// </remarks>
internal static class CombatRewardForecast
{
    // Vanilla offers three cards per normal combat reward, matching FastForwardMonsterRoomRewards.
    private const int CardsPerReward = 3;

    /// <summary>
    /// Predicts the next <paramref name="fightCount"/> normal combat rewards for one player.
    /// </summary>
    public static IReadOnlyList<CombatRewardPreview> Predict(Player player, int fightCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(fightCount);

        var context = new RunPredictionContext(player);
        var previews = new List<CombatRewardPreview>(fightCount);

        for (var i = 0; i < fightCount; i++)
        {
            // Mirrors a monster room end to end: AfterCombatEnd consumers first, then RewardsSet generation.
            CombatEndEffectPrediction.FastForwardMonsterRoomCombatEndHooks(context);

            var potion = OutOfCombatPredictionUtils.FastForwardBeforeMonsterCardReward(context);

            // Mirrors FastForwardMonsterRoomRewards' card reward options exactly.
            var options = CardCreationOptions.ForRoom(context.Player, RoomType.Monster)
                .WithFlags(CardCreationFlags.IsCardReward);
            var cards = CardRewardPrediction.PredictCards(context, CardsPerReward, options);

            previews.Add(new CombatRewardPreview(cards, potion));
        }

        return previews;
    }

    /// <summary>
    /// Predicts the reward for the next elite fight.
    /// </summary>
    /// <remarks>
    /// <c>RewardsSet.GenerateRewardsFor</c> gives an elite room the same gold, potion and card rewards as a monster
    /// room and then appends a <c>RelicReward</c>, so this reuses the shared pre-card fast-forward with
    /// <see cref="RoomType.Elite"/> and adds the relic afterwards. The relic costs one Rewards
    /// <c>RollRarity</c> draw and is then pulled from the front of the grab bag, which consumes no RNG.
    /// <para>
    /// Only one elite is forecast. Elites are not visited back to back, so chaining them would assume the player
    /// fights nothing else in between, which is almost never true.
    /// </para>
    /// </remarks>
    public static CombatRewardPreview PredictElite(Player player)
    {
        var context = new RunPredictionContext(player);

        CombatEndEffectPrediction.FastForwardMonsterRoomCombatEndHooks(context);

        var potion = OutOfCombatPredictionUtils.FastForwardBeforeMonsterCardReward(context, RoomType.Elite);

        var options = CardCreationOptions.ForRoom(context.Player, RoomType.Elite)
            .WithFlags(CardCreationFlags.IsCardReward);
        var cards = CardRewardPrediction.PredictCards(context, CardsPerReward, options);

        var relic = OutOfCombatPredictionUtils.PredictRelicRewards(context, 1).FirstOrDefault();

        return new CombatRewardPreview(cards, potion, relic);
    }

    /// <summary>
    /// Builds the top bar's forecast HoverTips for the local player, or an empty list when disabled or unavailable.
    /// </summary>
    public static IReadOnlyList<IHoverTip> GetHoverTips()
    {
        if (!IsEnabled() || LocalPlayerResolver.GetLocalPlayer() is not { } player)
        {
            return [];
        }

        var previews = Predict(player, RandomForeseerSettings.CombatRewardForecastCount);
        if (previews.Count == 0)
        {
            return [];
        }

        var tips = new List<IHoverTip>
        {
            PredictionHoverTips.Text(
                "combat_reward_forecast",
                description => description.Add("Count", previews.Count))
        };

        for (var i = 0; i < previews.Count; i++)
        {
            if (previews[i].Potion is not { } potion)
            {
                continue;
            }

            var fightNumber = i + 1;
            tips.Add(PredictionHoverTips.Text("combat_reward_forecast_potion", description =>
            {
                description.Add("Fight", fightNumber);
                description.Add("Potion", PredictionHoverTips.GetModelName(potion));
            }));
            tips.AddRange(PredictionHoverTips.Potions([potion]));
        }

        // One stack per fight, laid out in fight order by the shared prediction card bundle layout.
        tips.AddRange(PredictionHoverTips.CardBundles(previews.Select(preview => preview.Cards)));

        return tips;
    }

    /// <summary>
    /// Returns whether the forecast is enabled for the current game mode.
    /// </summary>
    public static bool IsEnabled()
    {
        return RandomForeseerSettings.IsPredictionFeatureEnabled(
            RandomForeseerSettings.EnableCombatRewardForecast);
    }
}
