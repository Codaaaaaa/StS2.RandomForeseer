using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Random;

namespace RandomForeseer.RandomForeseerCode.OutOfCombat;

/// <summary>
/// Predicts the relics an upcoming Ancient will offer, without touching the live run.
/// </summary>
/// <remarks>
/// Ancient options are not drawn from a player RNG stream. <c>EventModel.BeginEvent</c> seeds a per-event
/// <see cref="Rng"/> purely from the run seed, the owner's seat index and the event id, then each Ancient's
/// <c>GenerateInitialOptions</c> draws from that local stream. The seed is therefore reproducible ahead of time,
/// and running the generator consumes nothing the real run depends on.
/// <para>
/// Rather than re-implementing every Ancient's bespoke option logic, this drives the real generator against a
/// mutable clone carrying its own owner and reconstructed RNG. The clone never enters run state, and
/// <c>AncientEventModel.RelicOption</c> only builds <c>EventOption</c> objects whose obtain callbacks stay
/// unexecuted, so no relic is granted and no save progress is marked.
/// </para>
/// </remarks>
internal static class AncientOfferForecast
{
    // Publicizer leaves virtual members alone, so the generator stays behind one reflection boundary,
    // the same one AncientEventDebugRerollPatch already uses.
    private static readonly System.Reflection.MethodInfo GenerateInitialOptionsWrapper =
        AccessTools.Method(typeof(AncientEventModel), "GenerateInitialOptionsWrapper");

    /// <summary>
    /// Returns the relics <paramref name="canonicalAncient"/> would offer <paramref name="player"/>,
    /// or an empty list when the Ancient offers none.
    /// </summary>
    public static IReadOnlyList<RelicModel> PredictOffers(Player player, AncientEventModel canonicalAncient)
    {
        if (canonicalAncient.ToMutable() is not AncientEventModel ancient)
        {
            return [];
        }

        ancient.Owner = player;
        ancient.Rng = CreateEventRng(player, ancient);

        if (GenerateInitialOptionsWrapper.Invoke(ancient, null) is not IEnumerable<EventOption> options)
        {
            return [];
        }

        return options
            .Select(option => option.Relic)
            .OfType<RelicModel>()
            .ToList();
    }

    /// <summary>
    /// Mirrors the <see cref="Rng"/> construction in <c>EventModel.BeginEvent</c>.
    /// </summary>
    /// <remarks>
    /// The unchecked integer arithmetic is deliberate: vanilla adds the seat offset and the id hash as
    /// <see cref="int"/> before narrowing to <see cref="uint"/>, so wrapping must be preserved exactly.
    /// </remarks>
    private static Rng CreateEventRng(Player player, EventModel ancient)
    {
        var runState = player.RunState;
        var seatOffset = ancient.IsShared ? 0 : runState.GetPlayerSlotIndex(player);
        var seed = (uint)((int)runState.Rng.Seed + seatOffset);
        return new Rng((uint)(seed + StringHelper.GetDeterministicHashCode(ancient.Id.Entry)));
    }
}
