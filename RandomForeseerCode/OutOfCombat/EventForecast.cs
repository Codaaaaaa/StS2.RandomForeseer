using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

namespace RandomForeseer.RandomForeseerCode.OutOfCombat;

/// <summary>
/// Predicts which event the current act will serve next, without advancing any state.
/// </summary>
/// <remarks>
/// Event identity is not rolled on entry. <c>ActModel.GenerateRooms</c> pre-shuffles the act's event order into
/// <c>RoomSet.events</c>, and <c>ActModel.PullNextEvent</c> only walks that list forward:
/// <c>RoomSet.EnsureNextEventIsValid</c> skips entries that fail <see cref="EventModel.IsAllowed"/> or already
/// appear in <see cref="RunState.VisitedEventIds"/>, then <c>RoomSet.NextEvent</c> returns the entry it stopped on.
/// None of that consumes RNG, so this forecast is a pure read of existing run state.
/// <para>
/// Two things still make it conditional, and the UI wording reflects both. A <c>?</c> map point does not commit to
/// being an event room until <c>RunManager.EnterMapPointInternal</c> rolls its room type from
/// <c>RunState.Odds.UnknownMapPoint</c>, and <c>Hook.ModifyNextEvent</c> lets other content replace the pulled event.
/// </para>
/// </remarks>
internal static class EventForecast
{
    /// <summary>
    /// Returns the event the current act would serve next, or <see langword="null"/> when none can be determined.
    /// </summary>
    /// <remarks>
    /// This mirrors <c>RoomSet.EnsureNextEventIsValid</c> read-only. Vanilla advances <c>eventsVisited</c> in place;
    /// this walks a local index instead so the live run state is untouched.
    /// </remarks>
    public static EventModel? PredictNextEvent(RunState runState)
    {
        var rooms = runState.Act?._rooms;
        var events = rooms?.events;
        if (events is not { Count: > 0 })
        {
            return null;
        }

        var index = rooms!.eventsVisited;
        var visitedIds = runState.VisitedEventIds;

        // Vanilla stops after a full pass and logs "All unique events exhausted, allowing repetition",
        // falling back to whatever entry the index lands on. The bounded loop reproduces that behavior.
        for (var step = 0; step < events.Count; step++)
        {
            var candidate = events[index % events.Count];
            if (candidate.IsAllowed(runState) && !visitedIds.Contains(candidate.Id))
            {
                break;
            }

            index++;
        }

        return events[index % events.Count];
    }
}
