using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;
using Unity.MP_FPS;
using Unity.MP_FPS.Cards;
using Unity.MP_FPS.UI;

// The leaderboard ghost also owns the authoritative round and card-draft state.
// Keeping the state here means the draft remains available after a player's ghost
// has been destroyed for the rest of the round.
namespace Gameplay.Leaderboard
{
    [ResetOnPlayMode(resetMethod: "ResetStaticState")]
    public class LeaderboardManager : GhostMonoBehaviour, IUpdateServer, IUpdateClient
    {
        public static LeaderboardManager ServerInstance { get; private set; }
        public static LeaderboardManager ClientInstance { get; private set; }

        private const float FinalMatchRestartDelay = 8f;
        private const float PickRevealDuration = 1.15f;

        private RoundPhase _phase = RoundPhase.WaitingForPlayers;
        private int _currentRound;
        private bool _matchEnded;
        private float _matchRestartTimeRemaining;
        private FixedString64Bytes _winnerName;

        private readonly HashSet<int> _eliminatedThisRound = new();
        private readonly List<int> _finishingOrder = new();
        private struct DraftTurnEntry
        {
            public int NetworkId;
            public bool IsLateJoinCatchUp;
            public int CatchUpPickNumber;
            public int CatchUpPickTotal;
            public bool AllowsOffHandAdaptation;
            public FixedString64Bytes ReferenceLoserName;
        }

        private readonly List<DraftTurnEntry> _draftOrder = new();
        private readonly HashSet<int> _lateJoinersAwaitingCatchUp = new();
        private readonly List<CardModifierDefinition> _currentOffers = new();
        private int _draftIndex;
        private int _activeDraftNetworkId = -1;
        private float _draftSecondsRemaining;
        private float _draftSyncTimer;
        private float _pickRevealTimer;
        private bool _beginDraftPending;
        private bool _preparePlayersPending;
        private float _roundStartTimer;
        private bool _finalAnnouncementPending;
        private float _finalSyncTimer;
        private float _roundStateSyncTimer;

        // Client presentation state. RPCs update these even while the local player is dead.
        private int _localNetworkId = -1;
        private readonly int[] _offeredCardIds = { -1, -1, -1 };
        private FixedString64Bytes _activeDraftPlayerName;
        private bool _activeDraftIsLateJoinCatchUp;
        private int _activeCatchUpPickNumber;
        private int _activeCatchUpPickTotal;
        private bool _activeDraftAllowsOffHandAdaptation;
        private FixedString64Bytes _activeCatchUpReferenceLoserName;
        private FixedString128Bytes _lastPickMessage;
        private DraftPickRequestRpc? _pendingClientPick;
        private bool _pendingLobbyStartRequest;

        public bool IsMatchEnded => _matchEnded;
        public bool IsDrafting => _phase == RoundPhase.Drafting;
        public bool IsPreparingRound => _phase == RoundPhase.PreparingRound;
        public bool IsLocalDraftTurn => IsDrafting &&
                                        _activeDraftNetworkId == _localNetworkId;
        public RoundPhase Phase => _phase;
        public int CurrentRound => _currentRound;
        public int RoundWinsToWin => GetRules().RoundWinsToWin;
        public int ActiveDraftNetworkId => _activeDraftNetworkId;
        public string ActiveDraftPlayerName => _activeDraftPlayerName.ToString();
        public bool IsLateJoinCatchUpDraft => _activeDraftIsLateJoinCatchUp;
        public int CatchUpPickNumber => _activeCatchUpPickNumber;
        public int CatchUpPickTotal => _activeCatchUpPickTotal;
        public bool IsCatchUpAdaptationPick =>
            _activeDraftIsLateJoinCatchUp &&
            _activeDraftAllowsOffHandAdaptation;
        public ModifierTarget CurrentRoundHandTarget =>
            GetRoundHandTarget(_currentRound);
        public string CurrentRoundHandName =>
            CurrentRoundHandTarget == ModifierTarget.LeftHand ? "LEFT" : "RIGHT";
        public string CatchUpReferenceLoserName =>
            _activeCatchUpReferenceLoserName.ToString();
        public string LastPickMessage => _lastPickMessage.ToString();
        public float DraftSecondsRemaining => _draftSecondsRemaining;
        public float RoundStartTimeRemaining => _roundStartTimer;
        public string WinnerName => _winnerName.ToString();
        public float MatchRestartTimeRemaining => _matchRestartTimeRemaining;
        public float MatchTimeRemaining => 0f;
        public int LocalNetworkId => _localNetworkId;
        public bool IsWaitingInLobby => _phase == RoundPhase.WaitingForPlayers;
        public bool BlocksGameplayInput => _phase is RoundPhase.WaitingForPlayers
            or RoundPhase.PreparingRound;
        public int LobbyPlayerCount => GetLobbyPlayerCount();
        public bool IsLocalLobbyHost => IsWaitingInLobby &&
                                        _localNetworkId >= 0 &&
                                        _localNetworkId == GetLobbyHostNetworkId();
        public bool CanLocalPlayerStartLobby => IsLocalLobbyHost &&
                                                LobbyPlayerCount >= 2;

        private struct KillInfo
        {
            public int KillerId;
            public int VictimId;
        }

        private readonly Queue<KillInfo> _killQueue = new();
        private readonly Queue<FixedString64Bytes> _joinedQueue = new();
#pragma warning disable UDR0001
        private static Queue<(int networkId, FixedString64Bytes playerName)> _pendingPlayers =
            new();
#pragma warning restore UDR0001

        protected static void ResetStaticState()
        {
            ServerInstance = null;
            ClientInstance = null;
            _pendingPlayers = new Queue<(int, FixedString64Bytes)>();
        }

        public struct PlayerScoreEntry : IBufferElementData
        {
            [GhostField] public int NetworkId;
            [GhostField] public FixedString64Bytes PlayerName;
            [GhostField] public int Kills;
            [GhostField] public int Deaths;
            [GhostField] public int RoundWins;
        }

        public override void OnGhostLinked()
        {
            if (Role == MultiplayerRole.Server)
            {
                ServerInstance = this;
            }
            else
            {
                ClientInstance = this;
                RefreshLocalNetworkId();
            }
        }

        private void OnDestroy()
        {
            if (ServerInstance == this) ServerInstance = null;
            if (ClientInstance == this) ClientInstance = null;
        }

        public static void AddPlayer(int networkId, FixedString64Bytes playerName)
        {
            _pendingPlayers.Enqueue((networkId, playerName));
        }

        public static bool ShouldScheduleImmediateRespawn(int networkId)
        {
            return ServerInstance == null ||
                   ServerInstance._phase == RoundPhase.WaitingForPlayers ||
                   ServerInstance._phase == RoundPhase.Combat &&
                   !ServerInstance._eliminatedThisRound.Contains(networkId);
        }

        public int GetOfferedCardId(int index)
        {
            return index >= 0 && index < _offeredCardIds.Length
                ? _offeredCardIds[index]
                : -1;
        }

        public CardModifierDefinition GetCardDefinition(int cardId)
        {
            return WeaponManager.Instance != null
                ? WeaponManager.Instance.CardModifierCatalog?.FindById(cardId)
                : null;
        }

        public bool CanApplyDraftCardToTarget(CardModifierDefinition card,
            ModifierTarget target)
        {
            if (card == null || !card.Allows(target)) return false;
            if (target == ModifierTarget.Self) return true;
            if (target == GetRoundHandTarget(_currentRound)) return true;
            return _activeDraftIsLateJoinCatchUp &&
                   _activeDraftAllowsOffHandAdaptation &&
                   (target == ModifierTarget.LeftHand ||
                    target == ModifierTarget.RightHand);
        }

        public void RequestDraftPick(int cardId, ModifierTarget target)
        {
            if (Role == MultiplayerRole.Server || !IsLocalDraftTurn ||
                cardId <= 0 || target == ModifierTarget.None)
            {
                return;
            }

            _pendingClientPick = new DraftPickRequestRpc
            {
                RequesterNetworkId = _localNetworkId,
                CardId = cardId,
                Target = (byte)target
            };
        }

        public void RequestLobbyStart()
        {
            if (Role == MultiplayerRole.Server || !CanLocalPlayerStartLobby)
            {
                return;
            }

            _pendingLobbyStartRequest = true;
        }

        public void RemovePlayer(int networkId)
        {
            if (Role != MultiplayerRole.Server || GhostGameObject == null ||
                !GhostGameObject.IsGhostLinked() || !GhostGameObject.World.IsCreated)
            {
                return;
            }

            var buffer = GhostGameObject.GetGhostDynamicBuffer<PlayerScoreEntry>();
            for (int i = 0; i < buffer.Length; i++)
            {
                if (buffer[i].NetworkId != networkId) continue;
                buffer.RemoveAt(i);
                break;
            }

            _eliminatedThisRound.Remove(networkId);
            _finishingOrder.Remove(networkId);
            _draftOrder.RemoveAll(entry => entry.NetworkId == networkId);
            _lateJoinersAwaitingCatchUp.Remove(networkId);
        }

        public void AddKill(int killer, int victim)
        {
            if (Role != MultiplayerRole.Server || GhostGameObject == null ||
                !GhostGameObject.IsGhostLinked() || !GhostGameObject.World.IsCreated ||
                _phase != RoundPhase.Combat)
            {
                return;
            }

            var buffer = GhostGameObject.GetGhostDynamicBuffer<PlayerScoreEntry>();
            IncrementKills(buffer, killer);
            IncrementDeaths(buffer, victim);
            AnnounceKillFeed(killer, victim);
            RecordElimination(victim, killer, buffer);
        }

        public void AddDeath(int networkId)
        {
            if (Role != MultiplayerRole.Server || GhostGameObject == null ||
                !GhostGameObject.IsGhostLinked() || _phase != RoundPhase.Combat ||
                _eliminatedThisRound.Contains(networkId))
            {
                return;
            }

            var buffer = GhostGameObject.GetGhostDynamicBuffer<PlayerScoreEntry>();
            IncrementDeaths(buffer, networkId);
            RecordElimination(networkId, -1, buffer);
        }

        public void AnnouncePlayerJoined(FixedString64Bytes playerName)
        {
            _joinedQueue.Enqueue(playerName);
        }

        public List<PlayerScoreEntry> GetScores()
        {
            var scores = new List<PlayerScoreEntry>();
            if (GhostGameObject != null && GhostGameObject.IsGhostLinked())
            {
                var buffer = GhostGameObject.GetGhostDynamicBuffer<PlayerScoreEntry>();
                foreach (var entry in buffer) scores.Add(entry);
            }

            return scores;
        }

        public void UpdateServer(float deltaTime)
        {
            if (!GhostGameObject.IsGhostLinked()) return;

            var buffer = GhostGameObject.GetGhostDynamicBuffer<PlayerScoreEntry>();
            ProcessPendingPlayers(buffer);
            ProcessAnnouncements(buffer);

            while (ConsumeDirectedRPC(out DraftPickRequestRpc request))
            {
                TryApplyDraftPick(request.RequesterNetworkId, request.CardId,
                    (ModifierTarget)request.Target);
            }

            while (ConsumeDirectedRPC(out LobbyStartRequestRpc request))
            {
                TryStartLobbyMatch(request.RequesterNetworkId, buffer);
            }

            _roundStateSyncTimer -= deltaTime;
            if (_roundStateSyncTimer <= 0f)
            {
                BroadcastRoundState();
                _roundStateSyncTimer = 0.5f;
            }

            UpdateRoundState(deltaTime, buffer);
        }

        public void UpdateClient(float deltaTime)
        {
            if (!GhostGameObject.IsGhostLinked()) return;
            if (_localNetworkId < 0) RefreshLocalNetworkId();

            if (_pendingClientPick.HasValue)
            {
                SendDirectedRPC(_pendingClientPick.Value);
                _pendingClientPick = null;
            }

            if (_pendingLobbyStartRequest)
            {
                SendDirectedRPC(new LobbyStartRequestRpc
                {
                    RequesterNetworkId = _localNetworkId
                });
                _pendingLobbyStartRequest = false;
            }

            while (ConsumeRPC(out RoundStateSyncRpc stateRpc))
            {
                _phase = (RoundPhase)stateRpc.Phase;
                _currentRound = stateRpc.RoundNumber;
                if (_phase == RoundPhase.PreparingRound)
                {
                    _roundStartTimer = stateRpc.SecondsRemaining;
                }
            }

            while (ConsumeRPC(out KillFeedEntryRpc killFeedRpc))
            {
                ActionFeed.Instance?.AnnounceKill(killFeedRpc.KillerName.ToString(),
                    killFeedRpc.VictimName.ToString());
            }

            while (ConsumeRPC(out PlayerJoinedEntryRpc joinRpc))
            {
                ActionFeed.Instance?.AnnouncePlayerJoined(joinRpc.PlayerName.ToString());
            }

            while (ConsumeRPC(out DraftTurnRpc draftRpc))
            {
                _phase = RoundPhase.Drafting;
                _lastPickMessage = default;
                _currentRound = draftRpc.RoundNumber;
                _activeDraftNetworkId = draftRpc.ActiveNetworkId;
                _activeDraftPlayerName = draftRpc.ActivePlayerName;
                _activeDraftIsLateJoinCatchUp = draftRpc.IsLateJoinCatchUp != 0;
                _activeCatchUpPickNumber = draftRpc.CatchUpPickNumber;
                _activeCatchUpPickTotal = draftRpc.CatchUpPickTotal;
                _activeDraftAllowsOffHandAdaptation =
                    draftRpc.AllowsOffHandAdaptation != 0;
                _activeCatchUpReferenceLoserName =
                    draftRpc.ReferenceLoserName;
                _draftSecondsRemaining = draftRpc.SecondsRemaining;
                _offeredCardIds[0] = draftRpc.CardId0;
                _offeredCardIds[1] = draftRpc.CardId1;
                _offeredCardIds[2] = draftRpc.CardId2;
            }

            while (ConsumeRPC(out CardPickedRpc pickedRpc))
            {
                _lastPickMessage = pickedRpc.Message;
                _activeDraftNetworkId = -1;
                _draftSecondsRemaining = 0f;
            }

            while (ConsumeRPC(out RoundPreparingRpc preparingRpc))
            {
                _phase = RoundPhase.PreparingRound;
                _currentRound = preparingRpc.RoundNumber;
                _roundStartTimer = preparingRpc.Delay;
                ClearDraftPresentation();
            }

            while (ConsumeRPC(out RoundStartedRpc startedRpc))
            {
                _phase = RoundPhase.Combat;
                _currentRound = startedRpc.RoundNumber;
                _matchEnded = false;
                _winnerName = default;
                ClearDraftPresentation();
            }

            while (ConsumeRPC(out MatchEndedRpc matchEndedRpc))
            {
                _phase = RoundPhase.MatchEnded;
                _matchEnded = true;
                _winnerName = matchEndedRpc.WinnerName;
                _matchRestartTimeRemaining = matchEndedRpc.RestartDelay;
                ClearDraftPresentation();
            }

            while (ConsumeRPC(out MatchRestartedRpc _))
            {
                _matchEnded = false;
                _winnerName = default;
            }

            if (_phase == RoundPhase.Drafting && _activeDraftNetworkId >= 0)
            {
                _draftSecondsRemaining = Mathf.Max(0f,
                    _draftSecondsRemaining - deltaTime);
            }
            else if (_phase == RoundPhase.PreparingRound)
            {
                _roundStartTimer = Mathf.Max(0f, _roundStartTimer - deltaTime);
            }
            else if (_matchEnded)
            {
                _matchRestartTimeRemaining = Mathf.Max(0f,
                    _matchRestartTimeRemaining - deltaTime);
            }
        }

        private void ProcessPendingPlayers(DynamicBuffer<PlayerScoreEntry> buffer)
        {
            while (_pendingPlayers.Count > 0)
            {
                var (networkId, playerName) = _pendingPlayers.Dequeue();
                bool exists = false;
                for (int i = 0; i < buffer.Length; i++)
                {
                    if (buffer[i].NetworkId == networkId) exists = true;
                }

                if (exists) continue;
                buffer.Add(new PlayerScoreEntry
                {
                    NetworkId = networkId,
                    PlayerName = playerName
                });
                if (_phase == RoundPhase.Combat ||
                    _phase == RoundPhase.PreparingRound)
                {
                    _lateJoinersAwaitingCatchUp.Add(networkId);
                    Debug.Log($"[Server] Player {networkId} ({playerName}) joined " +
                              "in progress and will receive catch-up card picks after the round.");
                }
                else if (_phase == RoundPhase.Drafting)
                {
                    // A late joiner observes the in-progress draft and enters with
                    // everyone else when the following round starts. Append their
                    // catch-up event to the end of this draft.
                    _lateJoinersAwaitingCatchUp.Add(networkId);
                    QueueLateJoinCatchUpTurns(buffer);
                    _preparePlayersPending = true;
                }
                AnnouncePlayerJoined(playerName);
                Debug.Log($"[Server] Added player {networkId} ({playerName}) to the round match.");
            }
        }

        private bool TryStartLobbyMatch(int requesterNetworkId,
            DynamicBuffer<PlayerScoreEntry> buffer)
        {
            if (_phase != RoundPhase.WaitingForPlayers || buffer.Length < 2 ||
                requesterNetworkId != GetLobbyHostNetworkId(buffer))
            {
                return false;
            }

            _currentRound = 1;
            _matchEnded = false;
            _winnerName = default;
            _eliminatedThisRound.Clear();
            _finishingOrder.Clear();
            _draftOrder.Clear();
            _lateJoinersAwaitingCatchUp.Clear();
            _currentOffers.Clear();
            _activeDraftNetworkId = -1;
            _phase = RoundPhase.PreparingRound;
            _roundStartTimer = GetRules().RoundStartDelay;

            // Lobby participants already own fully spawned player ghosts. Reusing
            // them avoids a destroy/respawn race where a remote client can remain
            // spectating if its old ghost disappears before the replacement ghost
            // and respawn-state RPC arrive.
            BroadcastRPC(new RoundPreparingRpc
            {
                RoundNumber = _currentRound,
                Delay = _roundStartTimer
            });
            BroadcastRoundState();
            Debug.Log($"[Server] Lobby host {requesterNetworkId} started the match " +
                      $"with {buffer.Length} players.");
            return true;
        }

        private void BroadcastRoundState()
        {
            float secondsRemaining = _phase switch
            {
                RoundPhase.PreparingRound => Mathf.Max(0f, _roundStartTimer),
                RoundPhase.Drafting => Mathf.Max(0f, _draftSecondsRemaining),
                RoundPhase.MatchEnded => Mathf.Max(0f,
                    _matchRestartTimeRemaining),
                _ => 0f
            };
            BroadcastRPC(new RoundStateSyncRpc
            {
                Phase = (byte)_phase,
                RoundNumber = _currentRound,
                SecondsRemaining = secondsRemaining
            });
        }

        private void ProcessAnnouncements(DynamicBuffer<PlayerScoreEntry> buffer)
        {
            while (_killQueue.Count > 0)
            {
                KillInfo kill = _killQueue.Dequeue();
                var killerName = FindPlayerName(buffer, kill.KillerId, "[Unknown]");
                var victimName = FindPlayerName(buffer, kill.VictimId, "[Unknown]");
                BroadcastRPC(new KillFeedEntryRpc
                {
                    KillerName = killerName,
                    VictimName = victimName
                });
                ActionFeed.Instance?.AnnounceKill(killerName.ToString(), victimName.ToString());
            }

            while (_joinedQueue.Count > 0)
            {
                var playerName = _joinedQueue.Dequeue();
                BroadcastRPC(new PlayerJoinedEntryRpc { PlayerName = playerName });
                ActionFeed.Instance?.AnnouncePlayerJoined(playerName.ToString());
            }
        }

        private void RecordElimination(int victim, int preferredWinner,
            DynamicBuffer<PlayerScoreEntry> buffer)
        {
            // Keep solo development/testing usable; last-man-standing only begins
            // once at least two joined players are on the scoreboard.
            if (buffer.Length < 2) return;
            if (!_eliminatedThisRound.Add(victim)) return;
            _finishingOrder.Add(victim);
            int aliveCount = 0;
            int survivorId = -1;
            for (int i = 0; i < buffer.Length; i++)
            {
                if (_eliminatedThisRound.Contains(buffer[i].NetworkId)) continue;
                aliveCount++;
                survivorId = buffer[i].NetworkId;
            }

            if (aliveCount > 1) return;
            if (aliveCount == 0 && preferredWinner >= 0) survivorId = preferredWinner;
            EndRound(buffer, survivorId);
        }

        private void EndRound(DynamicBuffer<PlayerScoreEntry> buffer, int winnerId)
        {
            if (_phase != RoundPhase.Combat) return;

            if (winnerId >= 0 && !_finishingOrder.Contains(winnerId))
                _finishingOrder.Add(winnerId);

            _winnerName = FindPlayerName(buffer, winnerId, "No winner");
            int winnerRoundWins = 0;
            for (int i = 0; i < buffer.Length; i++)
            {
                if (buffer[i].NetworkId != winnerId) continue;
                var entry = buffer[i];
                entry.RoundWins++;
                buffer[i] = entry;
                winnerRoundWins = entry.RoundWins;
                break;
            }

            _preparePlayersPending = true;
            if (winnerRoundWins >= GetRules().RoundWinsToWin)
            {
                _phase = RoundPhase.MatchEnded;
                _matchEnded = true;
                _matchRestartTimeRemaining = FinalMatchRestartDelay;
                _finalAnnouncementPending = true;
                _lateJoinersAwaitingCatchUp.Clear();
                return;
            }

            _phase = RoundPhase.Drafting;
            _draftOrder.Clear();
            foreach (int networkId in _finishingOrder)
            {
                // Only the winner of this round sits out. Overall score and kills
                // never influence draft eligibility.
                if (networkId != winnerId)
                {
                    _draftOrder.Add(new DraftTurnEntry
                    {
                        NetworkId = networkId
                    });
                }
            }
            _draftIndex = 0;
            QueueLateJoinCatchUpTurns(buffer);
            _beginDraftPending = true;
        }

        private void UpdateRoundState(float deltaTime,
            DynamicBuffer<PlayerScoreEntry> buffer)
        {
            if (_preparePlayersPending)
            {
                EliminateRemainingPlayerGhosts();
                _preparePlayersPending = false;
            }

            switch (_phase)
            {
                case RoundPhase.Drafting:
                    UpdateDraft(deltaTime, buffer);
                    break;
                case RoundPhase.PreparingRound:
                    _roundStartTimer -= deltaTime;
                    if (_roundStartTimer <= 0f)
                    {
                        _phase = RoundPhase.Combat;
                        BroadcastRPC(new RoundStartedRpc { RoundNumber = _currentRound });
                    }
                    break;
                case RoundPhase.MatchEnded:
                    UpdateFinalMatch(deltaTime, buffer);
                    break;
            }
        }

        private void UpdateDraft(float deltaTime, DynamicBuffer<PlayerScoreEntry> buffer)
        {
            if (_beginDraftPending)
            {
                _beginDraftPending = false;
                BeginNextDraftTurn(buffer);
                return;
            }

            if (_pickRevealTimer > 0f)
            {
                _pickRevealTimer -= deltaTime;
                if (_pickRevealTimer <= 0f)
                {
                    _draftIndex++;
                    BeginNextDraftTurn(buffer);
                }
                return;
            }

            if (_activeDraftNetworkId < 0) return;
            _draftSecondsRemaining -= deltaTime;
            _draftSyncTimer -= deltaTime;
            if (_draftSyncTimer <= 0f)
            {
                BroadcastCurrentDraftTurn(buffer);
                _draftSyncTimer = 1f;
            }

            if (_draftSecondsRemaining <= 0f) AutoPickCurrentOffer();
        }

        private void BeginNextDraftTurn(DynamicBuffer<PlayerScoreEntry> buffer)
        {
            while (_draftIndex < _draftOrder.Count &&
                   !ContainsPlayer(buffer, _draftOrder[_draftIndex].NetworkId))
            {
                _draftIndex++;
            }

            if (_draftIndex >= _draftOrder.Count)
            {
                BeginNextRound();
                return;
            }

            DraftTurnEntry turn = _draftOrder[_draftIndex];
            _activeDraftNetworkId = turn.NetworkId;
            _activeDraftIsLateJoinCatchUp = turn.IsLateJoinCatchUp;
            _activeCatchUpPickNumber = turn.CatchUpPickNumber;
            _activeCatchUpPickTotal = turn.CatchUpPickTotal;
            _activeDraftAllowsOffHandAdaptation =
                turn.AllowsOffHandAdaptation;
            _activeCatchUpReferenceLoserName = turn.ReferenceLoserName;
            var store = World.GetExistingSystemManaged<PlayerRunModifierStoreSystem>();
            PlayerModifierStacks owned = store != null
                ? store.GetOrCreate(_activeDraftNetworkId)
                : default;
            var catalog = WeaponManager.Instance?.CardModifierCatalog;
            int finishingPosition = _finishingOrder.IndexOf(_activeDraftNetworkId);
            if (finishingPosition < 0) finishingPosition = _draftIndex;
            RoundCardRules rules = GetRules();
            var generated = CardOfferGenerator.Generate(catalog, in owned,
                finishingPosition, Mathf.Max(1, _finishingOrder.Count),
                rules.CardsPerOffer,
                (_currentRound * 73856093) ^ (_activeDraftNetworkId * 19349663) ^
                (_activeCatchUpPickNumber * 83492791),
                _currentRound, rules.FoundationalBoostRounds,
                rules.FoundationalStartingWeightMultiplier,
                rules.SelfCardRampStartRound,
                rules.SelfCardFullWeightRound,
                rules.SelfCardStartingWeightMultiplier,
                rules.SelfCardLateWeightMultiplier,
                rules.HandCardLateWeightMultiplier);

            _currentOffers.Clear();
            _currentOffers.AddRange(generated);
            if (_currentOffers.Count == 0)
            {
                _draftIndex++;
                BeginNextDraftTurn(buffer);
                return;
            }

            _draftSecondsRemaining = GetRules().SecondsPerDraftTurn;
            _draftSyncTimer = 1f;
            _pickRevealTimer = 0f;
            BroadcastCurrentDraftTurn(buffer);
        }

        private void BroadcastCurrentDraftTurn(DynamicBuffer<PlayerScoreEntry> buffer)
        {
            BroadcastRPC(new DraftTurnRpc
            {
                RoundNumber = _currentRound,
                ActiveNetworkId = _activeDraftNetworkId,
                ActivePlayerName = FindPlayerName(buffer, _activeDraftNetworkId, "Player"),
                IsLateJoinCatchUp = _activeDraftIsLateJoinCatchUp ? (byte)1 : (byte)0,
                CatchUpPickNumber = _activeCatchUpPickNumber,
                CatchUpPickTotal = _activeCatchUpPickTotal,
                AllowsOffHandAdaptation =
                    _activeDraftAllowsOffHandAdaptation ? (byte)1 : (byte)0,
                ReferenceLoserName = _activeCatchUpReferenceLoserName,
                SecondsRemaining = Mathf.Max(0f, _draftSecondsRemaining),
                CardId0 = GetServerOfferId(0),
                CardId1 = GetServerOfferId(1),
                CardId2 = GetServerOfferId(2)
            });
        }

        private bool TryApplyDraftPick(int requesterNetworkId, int cardId,
            ModifierTarget target)
        {
            if (_phase != RoundPhase.Drafting || _pickRevealTimer > 0f ||
                requesterNetworkId != _activeDraftNetworkId)
            {
                return false;
            }

            CardModifierDefinition card = null;
            foreach (var offered in _currentOffers)
            {
                if (offered != null && offered.CardId == cardId) card = offered;
            }

            if (!CanApplyDraftCardToTarget(card, target)) return false;
            var store = World.GetExistingSystemManaged<PlayerRunModifierStoreSystem>();
            if (store == null || !store.TryApplyCard(requesterNetworkId, card,
                    target, out _))
            {
                return false;
            }

            var buffer = GhostGameObject.GetGhostDynamicBuffer<PlayerScoreEntry>();
            string playerName = FindPlayerName(buffer, requesterNetworkId,
                $"Player {requesterNetworkId}").ToString();
            string targetName = target switch
            {
                ModifierTarget.LeftHand => "LEFT HAND",
                ModifierTarget.RightHand => "RIGHT HAND",
                _ => "SELF"
            };
            var message = new FixedString128Bytes(
                $"{playerName} PICKED {card.DisplayName.ToUpperInvariant()} → {targetName}");
            BroadcastRPC(new CardPickedRpc { Message = message });
            _activeDraftPlayerName = new FixedString64Bytes(playerName);
            _activeDraftNetworkId = -1;
            _draftSecondsRemaining = 0f;
            _pickRevealTimer = PickRevealDuration;
            return true;
        }

        private void AutoPickCurrentOffer()
        {
            ModifierTarget roundHand = GetRoundHandTarget(_currentRound);
            ModifierTarget otherHand = roundHand == ModifierTarget.LeftHand
                ? ModifierTarget.RightHand
                : ModifierTarget.LeftHand;
            for (int cardIndex = 0; cardIndex < _currentOffers.Count; cardIndex++)
            {
                var card = _currentOffers[cardIndex];
                foreach (var target in new[]
                         {
                             roundHand,
                             otherHand,
                             ModifierTarget.Self
                         })
                {
                    if (TryApplyDraftPick(_activeDraftNetworkId, card.CardId, target))
                        return;
                }
            }

            // The offers can only become invalid if state changed unexpectedly. Do not
            // leave the round stuck if that happens.
            _activeDraftNetworkId = -1;
            _pickRevealTimer = 0.05f;
        }

        private void BeginNextRound()
        {
            _currentRound++;
            _phase = RoundPhase.PreparingRound;
            _matchEnded = false;
            _activeDraftNetworkId = -1;
            _currentOffers.Clear();
            _eliminatedThisRound.Clear();
            _finishingOrder.Clear();
            _draftOrder.Clear();
            _roundStartTimer = GetRules().RoundStartDelay;
            QueueAllPlayersForRespawn(_roundStartTimer);
            BroadcastRPC(new RoundPreparingRpc
            {
                RoundNumber = _currentRound,
                Delay = _roundStartTimer
            });
        }

        private void UpdateFinalMatch(float deltaTime,
            DynamicBuffer<PlayerScoreEntry> buffer)
        {
            if (_finalAnnouncementPending)
            {
                BroadcastRPC(new MatchEndedRpc
                {
                    WinnerName = _winnerName,
                    RestartDelay = _matchRestartTimeRemaining
                });
                _finalAnnouncementPending = false;
                _finalSyncTimer = 1f;
            }

            _matchRestartTimeRemaining -= deltaTime;
            _finalSyncTimer -= deltaTime;
            if (_finalSyncTimer <= 0f && _matchRestartTimeRemaining > 0f)
            {
                BroadcastRPC(new MatchEndedRpc
                {
                    WinnerName = _winnerName,
                    RestartDelay = _matchRestartTimeRemaining
                });
                _finalSyncTimer = 1f;
            }

            if (_matchRestartTimeRemaining > 0f) return;
            for (int i = 0; i < buffer.Length; i++)
            {
                var entry = buffer[i];
                entry.Kills = 0;
                entry.Deaths = 0;
                entry.RoundWins = 0;
                buffer[i] = entry;
            }

            World.GetExistingSystemManaged<PlayerRunModifierStoreSystem>()?.ClearMatch();
            _lateJoinersAwaitingCatchUp.Clear();
            _currentRound = 0;
            _matchEnded = false;
            _winnerName = default;
            BroadcastRPC(new MatchRestartedRpc());
            BeginNextRound();
        }

        private void EliminateRemainingPlayerGhosts()
        {
            var entityManager = World.EntityManager;
            using var query = entityManager.CreateEntityQuery(
                ComponentType.ReadWrite<PredictedPlayerGhost>());
            using var entities = query.ToEntityArray(Allocator.Temp);
            foreach (var entity in entities)
            {
                var player = entityManager.GetComponentData<PredictedPlayerGhost>(entity);
                if (player.CurrentHealth <= 0f) continue;
                player.CurrentHealth = 0f;
                entityManager.SetComponentData(entity, player);
            }
        }

        private void QueueAllPlayersForRespawn(float delay)
        {
            var entityManager = World.EntityManager;
            using var query = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<NetworkId>(),
                ComponentType.ReadOnly<JoinedClient>());
            using var entities = query.ToEntityArray(Allocator.Temp);
            foreach (var entity in entities)
            {
                if (entityManager.HasComponent<PendingRespawn>(entity))
                {
                    entityManager.SetComponentData(entity,
                        new PendingRespawn { RespawnTimer = delay });
                }
                else
                {
                    entityManager.AddComponentData(entity,
                        new PendingRespawn { RespawnTimer = delay });
                }
            }
        }

        private void RefreshLocalNetworkId()
        {
            if (World == null || !World.IsCreated) return;
            using var query = World.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<NetworkId>(),
                ComponentType.ReadOnly<NetworkStreamConnection>());
            if (query.CalculateEntityCount() == 1)
                _localNetworkId = query.GetSingleton<NetworkId>().Value;
        }

        private void ClearDraftPresentation()
        {
            _activeDraftNetworkId = -1;
            _activeDraftPlayerName = default;
            _activeDraftIsLateJoinCatchUp = false;
            _activeCatchUpPickNumber = 0;
            _activeCatchUpPickTotal = 0;
            _activeDraftAllowsOffHandAdaptation = false;
            _activeCatchUpReferenceLoserName = default;
            _draftSecondsRemaining = 0f;
            _lastPickMessage = default;
            for (int i = 0; i < _offeredCardIds.Length; i++) _offeredCardIds[i] = -1;
        }

        private void AnnounceKillFeed(int killer, int victim)
        {
            _killQueue.Enqueue(new KillInfo { KillerId = killer, VictimId = victim });
        }

        private int GetServerOfferId(int index)
        {
            return index >= 0 && index < _currentOffers.Count &&
                   _currentOffers[index] != null
                ? _currentOffers[index].CardId
                : -1;
        }

        private static void IncrementKills(DynamicBuffer<PlayerScoreEntry> buffer,
            int networkId)
        {
            for (int i = 0; i < buffer.Length; i++)
            {
                if (buffer[i].NetworkId != networkId) continue;
                var entry = buffer[i];
                entry.Kills++;
                buffer[i] = entry;
                return;
            }
        }

        private static void IncrementDeaths(DynamicBuffer<PlayerScoreEntry> buffer,
            int networkId)
        {
            for (int i = 0; i < buffer.Length; i++)
            {
                if (buffer[i].NetworkId != networkId) continue;
                var entry = buffer[i];
                entry.Deaths++;
                buffer[i] = entry;
                return;
            }
        }

        private static bool ContainsPlayer(DynamicBuffer<PlayerScoreEntry> buffer,
            int networkId)
        {
            for (int i = 0; i < buffer.Length; i++)
                if (buffer[i].NetworkId == networkId) return true;
            return false;
        }

        private void QueueLateJoinCatchUpTurns(
            DynamicBuffer<PlayerScoreEntry> buffer)
        {
            if (_lateJoinersAwaitingCatchUp.Count == 0 ||
                _finishingOrder.Count == 0)
            {
                return;
            }

            int loserNetworkId = _finishingOrder[0];
            FixedString64Bytes loserName = FindPlayerName(buffer,
                loserNetworkId, "the loser");
            var store = World.GetExistingSystemManaged<PlayerRunModifierStoreSystem>();
            if (store == null) return;

            // During the result reveal, the current draft entry has already been
            // applied even though _draftIndex advances only after the reveal timer.
            int firstPendingDraftIndex = _pickRevealTimer > 0f
                ? _draftIndex + 1
                : _draftIndex;
            int loserProjectedCardCount = store.GetCardPickCount(loserNetworkId);
            for (int i = firstPendingDraftIndex; i < _draftOrder.Count; i++)
            {
                DraftTurnEntry pendingTurn = _draftOrder[i];
                if (pendingTurn.NetworkId == loserNetworkId &&
                    !pendingTurn.IsLateJoinCatchUp)
                {
                    loserProjectedCardCount++;
                }
            }

            foreach (int lateJoinerNetworkId in _lateJoinersAwaitingCatchUp)
            {
                if (!ContainsPlayer(buffer, lateJoinerNetworkId)) continue;

                int projectedJoinerCardCount =
                    store.GetCardPickCount(lateJoinerNetworkId);
                for (int i = firstPendingDraftIndex; i < _draftOrder.Count; i++)
                {
                    if (_draftOrder[i].NetworkId == lateJoinerNetworkId)
                        projectedJoinerCardCount++;
                }

                int catchUpPickTotal = Mathf.Max(0,
                    loserProjectedCardCount - projectedJoinerCardCount);
                for (int pick = 1; pick <= catchUpPickTotal; pick++)
                {
                    int adaptationInterval = GetRules()
                        .CatchUpAdaptationPickInterval;
                    _draftOrder.Add(new DraftTurnEntry
                    {
                        NetworkId = lateJoinerNetworkId,
                        IsLateJoinCatchUp = true,
                        CatchUpPickNumber = pick,
                        CatchUpPickTotal = catchUpPickTotal,
                        AllowsOffHandAdaptation =
                            pick % adaptationInterval == 0 ||
                            (catchUpPickTotal < adaptationInterval &&
                             pick == catchUpPickTotal),
                        ReferenceLoserName = loserName
                    });
                }

                if (catchUpPickTotal > 0)
                {
                    Debug.Log($"[Server] Queued {catchUpPickTotal} private " +
                              $"catch-up picks for late joiner {lateJoinerNetworkId} " +
                              $"to match {loserName}'s {loserProjectedCardCount} cards.");
                }
            }

            _lateJoinersAwaitingCatchUp.Clear();
        }

        private int GetLobbyPlayerCount()
        {
            if (GhostGameObject == null || !GhostGameObject.IsGhostLinked()) return 0;
            return GhostGameObject.GetGhostDynamicBuffer<PlayerScoreEntry>().Length;
        }

        private int GetLobbyHostNetworkId()
        {
            if (GhostGameObject == null || !GhostGameObject.IsGhostLinked()) return -1;
            return GetLobbyHostNetworkId(
                GhostGameObject.GetGhostDynamicBuffer<PlayerScoreEntry>());
        }

        private static int GetLobbyHostNetworkId(
            DynamicBuffer<PlayerScoreEntry> buffer)
        {
            if (buffer.Length == 0) return -1;
            int hostNetworkId = buffer[0].NetworkId;
            for (int i = 1; i < buffer.Length; i++)
            {
                hostNetworkId = Mathf.Min(hostNetworkId, buffer[i].NetworkId);
            }
            return hostNetworkId;
        }

        private static FixedString64Bytes FindPlayerName(
            DynamicBuffer<PlayerScoreEntry> buffer, int networkId, string fallback)
        {
            for (int i = 0; i < buffer.Length; i++)
                if (buffer[i].NetworkId == networkId) return buffer[i].PlayerName;
            return new FixedString64Bytes(fallback);
        }

        private static ModifierTarget GetRoundHandTarget(int roundNumber)
        {
            return Mathf.Max(1, roundNumber) % 2 == 1
                ? ModifierTarget.LeftHand
                : ModifierTarget.RightHand;
        }

        private static RoundCardRules GetRules()
        {
            return WeaponManager.Instance != null && WeaponManager.Instance.RoundCardRules != null
                ? WeaponManager.Instance.RoundCardRules
                : FallbackRoundCardRules.Instance;
        }

        // ScriptableObject rules are preferred. This in-memory fallback keeps a scene
        // playable if the reference is accidentally removed.
        private static class FallbackRoundCardRules
        {
            private static RoundCardRules s_Instance;
            public static RoundCardRules Instance
            {
                get
                {
                    if (s_Instance != null) return s_Instance;
                    s_Instance = ScriptableObject.CreateInstance<RoundCardRules>();
                    s_Instance.hideFlags = HideFlags.HideAndDontSave;
                    return s_Instance;
                }
            }
        }
    }

    public struct KillFeedEntryRpc : IRpcCommand
    {
        public FixedString64Bytes KillerName;
        public FixedString64Bytes VictimName;
    }

    public struct PlayerJoinedEntryRpc : IRpcCommand
    {
        public FixedString64Bytes PlayerName;
    }

    public struct DraftTurnRpc : IRpcCommand
    {
        public int RoundNumber;
        public int ActiveNetworkId;
        public FixedString64Bytes ActivePlayerName;
        public byte IsLateJoinCatchUp;
        public int CatchUpPickNumber;
        public int CatchUpPickTotal;
        public byte AllowsOffHandAdaptation;
        public FixedString64Bytes ReferenceLoserName;
        public float SecondsRemaining;
        public int CardId0;
        public int CardId1;
        public int CardId2;
    }

    public struct CardPickedRpc : IRpcCommand
    {
        public FixedString128Bytes Message;
    }

    public struct DraftPickRequestRpc : IRpcCommand, IGhostGameObjectDirectedRPC
    {
        public Unity.Entities.Hash128 TargetGuid;
        public int RequesterNetworkId;
        public int CardId;
        public byte Target;
    }

    public struct LobbyStartRequestRpc : IRpcCommand, IGhostGameObjectDirectedRPC
    {
        public Unity.Entities.Hash128 TargetGuid;
        public int RequesterNetworkId;
    }

    public struct RoundStateSyncRpc : IRpcCommand
    {
        public byte Phase;
        public int RoundNumber;
        public float SecondsRemaining;
    }

    public struct RoundPreparingRpc : IRpcCommand
    {
        public int RoundNumber;
        public float Delay;
    }

    public struct RoundStartedRpc : IRpcCommand
    {
        public int RoundNumber;
    }

    public struct MatchEndedRpc : IRpcCommand
    {
        public FixedString64Bytes WinnerName;
        public float RestartDelay;
    }

    public struct MatchRestartedRpc : IRpcCommand { }
}
