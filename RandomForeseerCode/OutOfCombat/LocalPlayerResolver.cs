using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Runs;

namespace RandomForeseer.RandomForeseerCode.OutOfCombat;

/// <summary>
/// Resolves the player whose RNG and pools prediction surfaces that are not owned by a player-specific
/// control must use.
/// </summary>
/// <remarks>
/// Most prediction entry points receive their player from the hovered vanilla node. Run-level surfaces such as
/// the top bar have no such owner, so they resolve the seated local player instead of defaulting to the host.
/// </remarks>
internal static class LocalPlayerResolver
{
    /// <summary>
    /// Returns the local player of the run in progress, or <see langword="null"/> outside a run.
    /// </summary>
    public static Player? GetLocalPlayer()
    {
        if (RunManager.Instance is not { IsInProgress: true, State: { } runState })
        {
            return null;
        }

        // Vanilla's own local-seat lookup, so a 3-player lobby's P3 resolves to P3 rather than the host.
        // RunState implements IPlayerCollection, which selects LocalContext's dedicated run overload.
        return LocalContext.GetMe(runState);
    }
}
