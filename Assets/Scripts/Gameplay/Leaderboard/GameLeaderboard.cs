using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;
using Unity.MP_FPS;
using Unity.MP_FPS.UI;

// This is a GhostMonoBehaviour that will be spawned as a singleton manager for the leaderboard.
// It can be found on the client using FindObjectOfType<LeaderboardManager>()
// On the server, its entity can be found by querying for the PlayerScoreEntry buffer.
namespace Gameplay.Leaderboard
{
    [ResetOnPlayMode(resetMethod: "ResetStaticState")]
    public class LeaderboardManager : GhostMonoBehaviour, IUpdateServer, IUpdateClient
    {
        public static LeaderboardManager Instance { get; private set; }

        private const float MatchRestartDelay = 8f;
        private float _matchElapsed;
        private float _matchRestartTimeRemaining;
        private float _matchTimeRemaining;
        private float _matchTimerSyncTimer;
        private bool _matchEnded;
        private bool _matchEndAnnouncementPending;
        private bool _preparePlayersForRestartPending;
        private float _matchEndSyncTimer;
        private FixedString64Bytes _winnerName;

        public bool IsMatchEnded => _matchEnded;
        public string WinnerName => _winnerName.ToString();
        public float MatchRestartTimeRemaining => _matchRestartTimeRemaining;
        public float MatchTimeRemaining => _matchTimeRemaining;

        private struct KillInfo
        {
            public int KillerId;
            public int VictimId;
        }

        private Queue<KillInfo> _killQueue = new Queue<KillInfo>();
        private Queue<FixedString64Bytes> _joinedQueue = new Queue<FixedString64Bytes>();
#pragma warning disable UDR0001
        // This is reset from ResetOnPlayMode attribute
        private static Queue<(int networkId, FixedString64Bytes playerName)> _pendingPlayers =
            new Queue<(int, FixedString64Bytes)>();
#pragma warning restore UDR0001

        protected static void ResetStaticState()
        {
            _pendingPlayers.Clear();
            _pendingPlayers = null;
        }

        public struct PlayerScoreEntry : IBufferElementData
        {
            [GhostField] public int NetworkId; // Used as the unique key for a player
            [GhostField] public FixedString64Bytes PlayerName;
            [GhostField] public int Kills;
            [GhostField] public int Deaths;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this.gameObject);
                return;
            }

            Instance = this;
        }

        public static void AddPlayer(int networkId, FixedString64Bytes playerName)
        {
            _pendingPlayers.Enqueue((networkId, playerName));
        }

        public void RemovePlayer(int networkId)
        {
            if (Role != MultiplayerRole.Server)
            {
                Debug.LogWarning("RemovePlayer can only be called on the server.");
                return;
            }

            if (GhostGameObject == null || !GhostGameObject.IsGhostLinked() || !GhostGameObject.World.IsCreated)
            {
                return;
            }

            var buffer = GhostGameObject.GetGhostDynamicBuffer<PlayerScoreEntry>();
            for (int i = 0; i < buffer.Length; i++)
            {
                if (buffer[i].NetworkId == networkId)
                {
                    buffer.RemoveAt(i);
                    return;
                }
            }
        }

        public void AddKill(int killer, int victim)
        {
            if (Role != MultiplayerRole.Server)
            {
                Debug.LogWarning("AddKill can only be called on the server.");
                return;
            }

            if (GhostGameObject == null || !GhostGameObject.IsGhostLinked() || !GhostGameObject.World.IsCreated)
            {
                Debug.LogWarning("[SERVER] NOT LINKED!");
                return;
            }

            if (_matchEnded)
            {
                return;
            }

            var buffer = GhostGameObject.GetGhostDynamicBuffer<PlayerScoreEntry>();
            bool reachedKillLimit = false;
            int killLimit = Mathf.Max(1, GameSettings.Instance.MatchKillLimit);
            for (int i = 0; i < buffer.Length; i++)
            {
                if (buffer[i].NetworkId == killer)
                {
                    var entry = buffer[i];
                    entry.Kills++;
                    buffer[i] = entry;
                    reachedKillLimit = entry.Kills >= killLimit;
                    break;
                }
            }

            for (int i = 0; i < buffer.Length; i++)
            {
                if (buffer[i].NetworkId == victim)
                {
                    var entry = buffer[i];
                    entry.Deaths++;
                    buffer[i] = entry;
                    break;
                }
            }

            AnnounceKillFeed(killer, victim);

            if (reachedKillLimit)
            {
                EndMatch(buffer);
            }
        }

        public void AnnouncePlayerJoined(FixedString64Bytes playerName)
        {
            _joinedQueue.Enqueue(playerName);
        }

        private void AnnounceKillFeed(int killer, int victim)
        {
            _killQueue.Enqueue(new KillInfo { KillerId = killer, VictimId = victim });
        }

        public void AddDeath(int networkId)
        {
            if (Role != MultiplayerRole.Server)
            {
                Debug.LogWarning("AddDeath can only be called on the server.");
                return;
            }

            if (!GhostGameObject.IsGhostLinked() || _matchEnded) return;

            var buffer = GhostGameObject.GetGhostDynamicBuffer<PlayerScoreEntry>();
            for (int i = 0; i < buffer.Length; i++)
            {
                if (buffer[i].NetworkId == networkId)
                {
                    var entry = buffer[i];
                    entry.Deaths++;
                    buffer[i] = entry;
                    return;
                }
            }
        }


        public List<PlayerScoreEntry> GetScores()
        {
            var scores = new List<PlayerScoreEntry>();
            if (GhostGameObject.IsGhostLinked())
            {
                var buffer = GhostGameObject.GetGhostDynamicBuffer<PlayerScoreEntry>();
                foreach (var entry in buffer)
                {
                    scores.Add(entry);
                }
            }

            return scores;
        }


        public void UpdateServer(float deltaTime)
        {
            if (!GhostGameObject.IsGhostLinked())
            {
                Debug.Log("[Server] LeaderboardManager not linked yet.");
                return;
            }

            var buffer = GhostGameObject.GetGhostDynamicBuffer<PlayerScoreEntry>();

            while (_pendingPlayers.Count > 0)
            {
                var (networkId, playerName) = _pendingPlayers.Dequeue();

                bool alreadyExists = false;
                for (int i = 0; i < buffer.Length; i++)
                {
                    if (buffer[i].NetworkId == networkId)
                    {
                        alreadyExists = true;
                        Debug.LogWarning(
                            $"[Server] Player {networkId} already exists in leaderboard, skipping add from queue.");
                        break;
                    }
                }

                if (!alreadyExists)
                {
                    buffer.Add(new PlayerScoreEntry
                    {
                        NetworkId = networkId,
                        PlayerName = playerName,
                        Kills = 0,
                        Deaths = 0
                    });
                    Debug.Log($"[Server] Added player {networkId} ({playerName}) to leaderboard from queue.");

                    // Announce join *after* successfully adding to the buffer
                    AnnouncePlayerJoined(playerName);
                }
            }

            while (_killQueue.Count > 0)
            {
                KillInfo kill = _killQueue.Dequeue();

                FixedString64Bytes killerName = "[Unknown]";
                FixedString64Bytes victimName = "[Unknown]";

                // Find names from the buffer
                for (int i = 0; i < buffer.Length; i++)
                {
                    if (buffer[i].NetworkId == kill.KillerId)
                    {
                        killerName = buffer[i].PlayerName;
                    }

                    if (buffer[i].NetworkId == kill.VictimId)
                    {
                        victimName = buffer[i].PlayerName;
                    }
                }

                // Create and broadcast the RPC
                var killFeedRpc = new KillFeedEntryRpc
                {
                    KillerName = killerName,
                    VictimName = victimName
                };
                GhostGameObject.BroadcastRPC(killFeedRpc);
                ActionFeed.Instance.AnnounceKill(killerName.ToString(), victimName.ToString());
            }

            while (_joinedQueue.Count > 0)
            {
                var playerName = _joinedQueue.Dequeue();

                var joinRpc = new PlayerJoinedEntryRpc
                {
                    PlayerName = playerName,
                };
                GhostGameObject.BroadcastRPC(joinRpc);
                ActionFeed.Instance.AnnouncePlayerJoined(playerName.ToString());
            }

            UpdateMatchState(deltaTime, buffer);
        }

        public void UpdateClient(float deltaTime)
        {
            if (!GhostGameObject.IsGhostLinked()) return;

            // Consume any kill feed RPCs received this frame
            while (GhostGameObject.ConsumeRPC(out KillFeedEntryRpc killFeedRpc))
            {
                // Invoke the event for the UI to handle
                ActionFeed.Instance.AnnounceKill(killFeedRpc.KillerName.ToString(),
                    killFeedRpc.VictimName.ToString());
            }

            while (GhostGameObject.ConsumeRPC(out PlayerJoinedEntryRpc joinRpc))
            {
                ActionFeed.Instance.AnnouncePlayerJoined(joinRpc.PlayerName.ToString());
            }

            while (GhostGameObject.ConsumeRPC(out MatchEndedRpc matchEndedRpc))
            {
                _matchEnded = true;
                _winnerName = matchEndedRpc.WinnerName;
                _matchRestartTimeRemaining = matchEndedRpc.RestartDelay;
            }

            while (GhostGameObject.ConsumeRPC(out MatchRestartedRpc _))
            {
                _matchEnded = false;
                _winnerName = default;
                _matchRestartTimeRemaining = 0f;
            }

            while (GhostGameObject.ConsumeRPC(out MatchTimerRpc matchTimerRpc))
            {
                _matchTimeRemaining = Mathf.Max(0f, matchTimerRpc.RemainingSeconds);
            }

            if (_matchEnded)
            {
                _matchRestartTimeRemaining = Mathf.Max(0f,
                    _matchRestartTimeRemaining - deltaTime);
            }
            else if (_matchTimeRemaining > 0f)
            {
                _matchTimeRemaining = Mathf.Max(0f, _matchTimeRemaining - deltaTime);
            }
        }

        private void UpdateMatchState(float deltaTime,
            DynamicBuffer<PlayerScoreEntry> buffer)
        {
            if (_matchEndAnnouncementPending)
            {
                GhostGameObject.BroadcastRPC(new MatchEndedRpc
                {
                    WinnerName = _winnerName,
                    RestartDelay = MatchRestartDelay
                });
                _matchEndAnnouncementPending = false;
                _matchEndSyncTimer = 1f;
            }

            if (_preparePlayersForRestartPending)
            {
                PreparePlayersForNextRound();
                _preparePlayersForRestartPending = false;
            }

            if (_matchEnded)
            {
                _matchRestartTimeRemaining -= deltaTime;
                _matchEndSyncTimer -= deltaTime;
                if (_matchEndSyncTimer <= 0f && _matchRestartTimeRemaining > 0f)
                {
                    GhostGameObject.BroadcastRPC(new MatchEndedRpc
                    {
                        WinnerName = _winnerName,
                        RestartDelay = _matchRestartTimeRemaining
                    });
                    _matchEndSyncTimer = 1f;
                }
                if (_matchRestartTimeRemaining <= 0f)
                {
                    for (int i = 0; i < buffer.Length; i++)
                    {
                        var entry = buffer[i];
                        entry.Kills = 0;
                        entry.Deaths = 0;
                        buffer[i] = entry;
                    }

                    _matchElapsed = 0f;
                    _matchTimerSyncTimer = 0f;
                    _matchEnded = false;
                    _winnerName = default;
                    _matchRestartTimeRemaining = 0f;
                    _matchEndSyncTimer = 0f;
                    GhostGameObject.BroadcastRPC(new MatchRestartedRpc());
                }
                return;
            }

            _matchElapsed += deltaTime;
            float timeLimitSeconds = Mathf.Max(1,
                GameSettings.Instance.MatchTimeLimitMinutes) * 60f;
            _matchTimeRemaining = Mathf.Max(0f, timeLimitSeconds - _matchElapsed);
            _matchTimerSyncTimer -= deltaTime;
            if (_matchTimerSyncTimer <= 0f)
            {
                GhostGameObject.BroadcastRPC(new MatchTimerRpc
                {
                    RemainingSeconds = _matchTimeRemaining
                });
                _matchTimerSyncTimer = 1f;
            }
            if (_matchElapsed >= timeLimitSeconds)
            {
                EndMatch(buffer);
            }
        }

        private void EndMatch(DynamicBuffer<PlayerScoreEntry> buffer)
        {
            if (_matchEnded)
            {
                return;
            }

            _winnerName = GetWinnerName(buffer);
            _matchEnded = true;
            _matchTimeRemaining = 0f;
            _matchEndAnnouncementPending = true;
            _preparePlayersForRestartPending = true;
            _matchRestartTimeRemaining = MatchRestartDelay;
        }

        private void PreparePlayersForNextRound()
        {
            var entityManager = GhostGameObject.World.EntityManager;
            using var playerQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadWrite<PredictedPlayerGhost>());
            using var playerEntities = playerQuery.ToEntityArray(Allocator.Temp);
            foreach (var playerEntity in playerEntities)
            {
                var player = entityManager.GetComponentData<PredictedPlayerGhost>(playerEntity);
                if (player.CurrentHealth > 0f)
                {
                    player.CurrentHealth = 0f;
                    entityManager.SetComponentData(playerEntity, player);
                }
            }
        }

        private static FixedString64Bytes GetWinnerName(
            DynamicBuffer<PlayerScoreEntry> buffer)
        {
            if (buffer.Length == 0)
            {
                return new FixedString64Bytes("No winner");
            }

            var winner = buffer[0];
            for (int i = 1; i < buffer.Length; i++)
            {
                var candidate = buffer[i];
                if (candidate.Kills > winner.Kills ||
                    candidate.Kills == winner.Kills && candidate.Deaths < winner.Deaths)
                {
                    winner = candidate;
                }
            }

            return winner.PlayerName;
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

    public struct MatchEndedRpc : IRpcCommand
    {
        public FixedString64Bytes WinnerName;
        public float RestartDelay;
    }

    public struct MatchRestartedRpc : IRpcCommand
    {
    }

    public struct MatchTimerRpc : IRpcCommand
    {
        public float RemainingSeconds;
    }
}
