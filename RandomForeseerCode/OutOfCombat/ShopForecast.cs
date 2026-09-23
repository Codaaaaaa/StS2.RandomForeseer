using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.TestSupport;
using RandomForeseer.RandomForeseerCode.Common;

namespace RandomForeseer.RandomForeseerCode.OutOfCombat;

internal sealed record ShopCardPreview(CardModel Card, int Cost, int FullCost, bool IsOnSale);

internal sealed record ShopRelicPreview(RelicModel Relic, int Cost);

internal sealed record ShopPotionPreview(PotionModel Potion, int Cost);

internal sealed record ShopPreview(
    IReadOnlyList<ShopCardPreview> CharacterCards,
    IReadOnlyList<ShopCardPreview> ColorlessCards,
    IReadOnlyList<ShopRelicPreview> Relics,
    IReadOnlyList<ShopPotionPreview> Potions);

/// <summary>
/// Predicts upcoming merchant inventories without advancing any live RNG.
/// </summary>
/// <remarks>
/// Mirrors <c>MerchantInventory.CreateForNormalMerchant</c>, which <c>MerchantRoom.EnterInternal</c> runs once per
/// player on entering a shop. Every player draws from their own streams, so only the forecast player matters.
/// <para>
/// The exact consumption order this mirrors, per shop:
/// <list type="number">
/// <item>Shops <c>NextInt(5)</c> picks the character-card sale slot.</item>
/// <item>Five character cards. Each one: Rewards rarity roll, Shops <c>NextItem</c>, Rewards upgrade roll, then
/// Shops <c>NextFloat(0.95, 1.05)</c> for its price. The sale slot runs <c>CalcCost</c> a second time, so it
/// consumes one extra price roll before its cost is halved.</item>
/// <item>Two colorless cards (Uncommon then Rare). Each one: Shops <c>NextItem</c>, Rewards upgrade roll, Shops
/// price roll. Their rarity is fixed, so no rarity roll.</item>
/// <item>Relics: two Rewards rarity rolls up front (the third slot is always <c>RelicRarity.Shop</c>), then one
/// Shops <c>NextFloat(0.85, 1.15)</c> price roll per relic. Relic identity comes from the grab bag, not RNG.</item>
/// <item>Potions: three potions drawn from Shops in one batch, then three Shops price rolls.</item>
/// </list>
/// </para>
/// </remarks>
internal static class ShopForecast
{
    // MerchantInventory._coloredCardTypes and _colorlessCardRarities.
    private static readonly CardType[] ColoredCardTypes =
        [CardType.Attack, CardType.Attack, CardType.Skill, CardType.Skill, CardType.Power];

    private static readonly CardRarity[] ColorlessCardRarities = [CardRarity.Uncommon, CardRarity.Rare];

    private const int RelicSlots = 3;
    private const int PotionSlots = 3;

    /// <summary>
    /// Predicts the next <paramref name="shopCount"/> merchant inventories for one player.
    /// </summary>
    public static IReadOnlyList<ShopPreview> Predict(Player player, int shopCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(shopCount);

        var context = new RunPredictionContext(player);
        var shops = new List<ShopPreview>(shopCount);
        for (var i = 0; i < shopCount; i++)
        {
            shops.Add(PredictOne(context));
        }

        return shops;
    }

    private static ShopPreview PredictOne(RunPredictionContext context)
    {
        var player = context.Player;
        var shopsRng = context.Rng.Shops;

        var saleIndex = shopsRng.NextInt(ColoredCardTypes.Length);

        // MerchantCardEntry.Populate blacklists every card already in the inventory, and
        // MerchantInventory.CardEntries is character entries followed by colorless entries,
        // so one running blacklist covers both loops.
        var blacklist = new HashSet<CardModel>();

        var characterPool = player.Character.CardPool
            .GetUnlockedCards(player.UnlockState, player.RunState.CardMultiplayerConstraint)
            .ToList();
        var characterCards = new List<ShopCardPreview>(ColoredCardTypes.Length);
        for (var i = 0; i < ColoredCardTypes.Length; i++)
        {
            var card = CreateForMerchantByType(context, characterPool.Except(blacklist), ColoredCardTypes[i]);
            blacklist.Add(card.CanonicalInstance);

            var fullCost = RollCardCost(context, card);
            var isOnSale = i == saleIndex;
            if (isOnSale)
            {
                // SetOnSale re-runs CalcCost, which rolls a second price before halving it.
                fullCost = RollCardCost(context, card);
            }

            characterCards.Add(new ShopCardPreview(card, isOnSale ? fullCost / 2 : fullCost, fullCost, isOnSale));
        }

        var colorlessPool = ModelDb.CardPool<ColorlessCardPool>()
            .GetUnlockedCards(player.UnlockState, player.RunState.CardMultiplayerConstraint)
            .ToList();
        var colorlessCards = new List<ShopCardPreview>(ColorlessCardRarities.Length);
        foreach (var rarity in ColorlessCardRarities)
        {
            var card = CreateForMerchantByRarity(context, colorlessPool.Except(blacklist), rarity);
            blacklist.Add(card.CanonicalInstance);

            var cost = RollCardCost(context, card);
            colorlessCards.Add(new ShopCardPreview(card, cost, cost, IsOnSale: false));
        }

        // PopulateRelicEntries builds all three rarities before creating any entry, so both Rewards
        // rolls happen ahead of the per-relic Shops price rolls.
        var relicRarities = new RelicRarity[RelicSlots];
        relicRarities[0] = RelicFactory.RollRarity(context.Rng.Rewards);
        relicRarities[1] = RelicFactory.RollRarity(context.Rng.Rewards);
        relicRarities[2] = RelicRarity.Shop;

        var relics = new List<ShopRelicPreview>(RelicSlots);
        foreach (var rarity in relicRarities)
        {
            // The initial fill passes no blacklist, so vanilla can stock the same relic twice.
            var relic = context.RelicGrabBag.PullFromBack(rarity, static r => r.IsAllowedInShops, context.RunState)
                ?? RelicFactory.FallbackRelic;
            var cost = (int)Math.Round(relic.MerchantCost * shopsRng.NextFloat(0.85f, 1.15f));
            relics.Add(new ShopRelicPreview(relic, cost));
        }

        var potionModels = PotionFactory.CreateRandomPotionsOutOfCombat(player, PotionSlots, shopsRng);
        var potions = potionModels
            .Select(potion => new ShopPotionPreview(potion, RollPotionCost(context, potion)))
            .ToList();

        return new ShopPreview(characterCards, colorlessCards, relics, potions);
    }

    // Mirrors CardFactory.CreateForMerchant with a CardType.
    private static CardModel CreateForMerchantByType(
        RunPredictionContext context,
        IEnumerable<CardModel> options,
        CardType type)
    {
        var player = context.Player;
        options = Hook.ModifyMerchantCardPool(player.RunState, player, options);
        options = options.Where(card => card.Rarity != CardRarity.Basic);
        options = CardFactory.FilterForPlayerCount(player.RunState, options);
        var optionsArr = options.ToArray();

        var rarity = Hook.ModifyMerchantCardRarity(
            player.RunState,
            player,
            context.CardRarityOdds.RollWithoutChangingFutureOdds(CardRarityOddsType.Shop));
        rarity = CardFactory.GetNextAllowedRarity(
            rarity,
            r => optionsArr.Any(card => card.Rarity == r && card.Type == type));
        if (rarity == CardRarity.None)
        {
            throw new InvalidOperationException($"Could not predict a merchant card rarity for type {type}.");
        }

        var candidates = optionsArr.Where(card => card.Rarity == rarity && card.Type == type).ToList();
        return CreateAndRollUpgrade(context, candidates);
    }

    // Mirrors CardFactory.CreateForMerchant with a CardRarity.
    private static CardModel CreateForMerchantByRarity(
        RunPredictionContext context,
        IEnumerable<CardModel> options,
        CardRarity rarity)
    {
        var player = context.Player;
        options = Hook.ModifyMerchantCardPool(player.RunState, player, options);
        options = options.Where(card => card.Rarity != CardRarity.Basic);
        options = CardFactory.FilterForPlayerCount(player.RunState, options);
        var source = options.ToArray();

        var modifiedRarity = Hook.ModifyMerchantCardRarity(player.RunState, player, rarity);
        var candidates = source.Where(card => card.Rarity == modifiedRarity).ToList();
        return CreateAndRollUpgrade(context, candidates);
    }

    private static CardModel CreateAndRollUpgrade(RunPredictionContext context, List<CardModel> candidates)
    {
        var canonical = context.Rng.Shops.NextItem(candidates)
            ?? throw new InvalidOperationException("Could not predict a merchant card.");
        var card = PredictionUtils.CreateCard(canonical, context.Player);

        // Vanilla always consumes this Rewards roll even though the -999999999 base chance keeps merchant
        // cards from being upgraded by it.
        CardRewardPrediction.RollForUpgrade(context.Player, card, -999999999m, context.Rng.Rewards);
        return card;
    }

    private static int RollCardCost(RunPredictionContext context, CardModel card)
    {
        return Mathf.RoundToInt(MerchantCardEntry.GetCost(card) * context.Rng.Shops.NextFloat(0.95f, 1.05f));
    }

    private static int RollPotionCost(RunPredictionContext context, PotionModel potion)
    {
        var cost = MerchantPotionEntry.GetCost(potion.Rarity);
        if (TestMode.IsOff)
        {
            cost = (int)Mathf.Round(cost * context.Rng.Shops.NextFloat(0.95f, 1.05f));
        }

        return cost;
    }
}
