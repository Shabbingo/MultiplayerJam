using Unity.Entities;
using Unity.NetCode;

namespace Unity.MP_FPS.Cards
{
    /// <summary>
    /// Network-ready round/draft state. A later director system owns transitions;
    /// clients only render the replicated values.
    /// </summary>
    public struct RoundCardState : IComponentData
    {
        [GhostField] public RoundPhase Phase;
        [GhostField] public int CurrentRound;
        [GhostField] public int RoundsPerMatch;
        [GhostField] public int ActiveDraftNetworkId;
        [GhostField] public float DraftSecondsRemaining;
    }
}
