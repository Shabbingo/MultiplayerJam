using Unity.Entities;
using Gameplay.Leaderboard;
using System;
using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using Unity.NetCode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.UI
{
    [RequireComponent(typeof(UIDocument))]
    public class RespawnScreen : MonoBehaviour
    {
        public Camera RespawnCamera;
        private VisualElement m_RespawnScreen;
        private Label m_RespawnTimerLabel;
        private VisualElement m_RespawnContent;
        private VisualElement m_LobbyPanel;
        private Label m_LobbySessionLabel;
        private Label m_LobbyPlayerCountLabel;
        private Label m_LobbyPlayerListLabel;
        private Label m_LobbyStatusLabel;
        private Button m_LobbyStartButton;

        private bool m_IsRespawning;
        private uint m_LastRespawnSequence;

        private float m_RespawnCountdown;
        private int m_SpectatorIndex;
        private int m_SpectatedNetworkId = -1;
        private AudioListener m_SpectatorAudioListener;
        private Vector3 m_LastPlayerCameraPosition;
        private Quaternion m_LastPlayerCameraRotation = Quaternion.identity;
        private bool m_HasLastPlayerCameraPose;
        private bool m_WasSpectating;
        private bool m_WasShowingLobby;
        private readonly List<SpectatorTarget> m_SpectatorTargets = new();

        private readonly struct SpectatorTarget
        {
            public readonly int NetworkId;
            public readonly string Name;
            public readonly PlayerGhost Player;

            public SpectatorTarget(int networkId, string name, PlayerGhost player)
            {
                NetworkId = networkId;
                Name = name;
                Player = player;
            }
        }

        private void Awake()
        {
            RespawnCamera.cullingMask = ~0;
            RespawnCamera.farClipPlane = 5000f;
            m_SpectatorAudioListener = RespawnCamera.GetComponent<AudioListener>();
            if (m_SpectatorAudioListener == null)
                m_SpectatorAudioListener = RespawnCamera.gameObject.AddComponent<AudioListener>();
            RespawnCamera.gameObject.SetActive(false);
        }

        void OnEnable()
        {
            m_RespawnScreen = GetComponent<UIDocument>().rootVisualElement;
            m_RespawnTimerLabel = m_RespawnScreen.Q<Label>("RespawnMessage");
            m_RespawnContent = m_RespawnScreen.Q<VisualElement>("UIWrapper");
            m_LobbyPanel = m_RespawnScreen.Q<VisualElement>("LobbyPanel");
            m_LobbySessionLabel = m_RespawnScreen.Q<Label>("LobbySession");
            m_LobbyPlayerCountLabel = m_RespawnScreen.Q<Label>("LobbyPlayerCount");
            m_LobbyPlayerListLabel = m_RespawnScreen.Q<Label>("LobbyPlayerList");
            m_LobbyStatusLabel = m_RespawnScreen.Q<Label>("LobbyStatus");
            m_LobbyStartButton = m_RespawnScreen.Q<Button>("LobbyStartButton");
            if (m_LobbyStartButton != null)
            {
                m_LobbyStartButton.clicked += OnLobbyStartClicked;
            }
            m_RespawnScreen.style.display = DisplayStyle.None;
            m_IsRespawning = false;
            m_LastRespawnSequence = ClientRespawnState.Sequence;
            m_SpectatorIndex = 0;
            m_SpectatedNetworkId = -1;
        }

        private void OnDisable()
        {
            if (m_LobbyStartButton != null)
            {
                m_LobbyStartButton.clicked -= OnLobbyStartClicked;
            }
            m_IsRespawning = false;
            if (m_SpectatorAudioListener != null) m_SpectatorAudioListener.enabled = false;
            RespawnCamera.gameObject.SetActive(false);
            if (m_WasShowingLobby)
            {
                UnityEngine.Cursor.lockState = CursorLockMode.None;
                UnityEngine.Cursor.visible = true;
                m_WasShowingLobby = false;
            }
        }

        void LateUpdate()
        {
            if (GameSettings.Instance.GameState != GlobalGameState.InGame)
            {
                m_RespawnScreen.style.display = DisplayStyle.None;
                RespawnCamera.gameObject.SetActive(false);
                if (m_SpectatorAudioListener != null) m_SpectatorAudioListener.enabled = false;
                m_IsRespawning = false;
                m_WasSpectating = false;
                HideLobby(false);
                return;
            }

            LeaderboardManager roundManager = LeaderboardManager.ClientInstance;
            if (roundManager != null && roundManager.IsWaitingInLobby)
            {
                ShowLobby(roundManager);
                return;
            }

            HideLobby(true);

            CachePlayerCameraPose();
            bool isSpectating = UpdateSpectatorCamera();
            bool draftUiOwnsOverlay = roundManager != null &&
                                      roundManager.IsDrafting;
            if (!ClientRespawnState.IsRespawning && !isSpectating)
            {
                m_IsRespawning = false;
                RespawnCamera.gameObject.SetActive(false);
                if (m_SpectatorAudioListener != null) m_SpectatorAudioListener.enabled = false;
                m_RespawnScreen.style.display = DisplayStyle.None;
                m_LastRespawnSequence = ClientRespawnState.Sequence;
                m_WasSpectating = false;
                return;
            }

            RespawnCamera.gameObject.SetActive(true);
            // Player camera setup disables the camera that was active previously.
            // Reassert this explicitly so a camera disabled during a round transition
            // cannot leave the spectator UI rendering over an empty frame.
            RespawnCamera.enabled = true;
            if (m_SpectatorAudioListener != null) m_SpectatorAudioListener.enabled = isSpectating;
            if (isSpectating && GameManager.Instance?.SoundSystem != null)
            {
                GameManager.Instance.SoundSystem.SetListenerTransform(RespawnCamera.transform);
            }
            if (GameSettings.Instance != null)
            {
                RespawnCamera.fieldOfView = GameSettings.Instance.CameraFov;
            }

            if (isSpectating && !m_WasSpectating)
            {
                Debug.Log($"[Spectator] Camera activated with {m_SpectatorTargets.Count} available target(s).");
            }
            m_WasSpectating = isSpectating;

            if (ClientRespawnState.IsRespawning && (!m_IsRespawning ||
                m_LastRespawnSequence != ClientRespawnState.Sequence))
            {
                m_RespawnCountdown = ClientRespawnState.Duration;
                m_IsRespawning = true;
                m_LastRespawnSequence = ClientRespawnState.Sequence;
            }
            // The card draft is a full-screen interaction and must remain visible
            // for players who joined or died during the round. Keep their spectator
            // camera active as the background, but suppress this higher-priority
            // "waiting for next round" overlay while the draft UI owns the screen.
            m_RespawnScreen.style.display = draftUiOwnsOverlay
                ? DisplayStyle.None
                : DisplayStyle.Flex;

            if (ClientRespawnState.IsRespawning)
            {
                m_RespawnCountdown = Mathf.Max(0f,
                    m_RespawnCountdown - Time.deltaTime);
            }

            if (m_RespawnTimerLabel != null)
            {
                if (roundManager != null && roundManager.IsPreparingRound)
                {
                    m_RespawnTimerLabel.text =
                        $"ROUND {roundManager.CurrentRound} STARTING IN " +
                        $"{Mathf.CeilToInt(roundManager.RoundStartTimeRemaining)}";
                }
                else if (isSpectating && m_SpectatorTargets.Count > 0)
                {
                    SpectatorTarget target = m_SpectatorTargets[m_SpectatorIndex];
                    string prefix = ClientRespawnState.IsRespawning
                        ? $"RESPAWNING IN {Mathf.CeilToInt(m_RespawnCountdown)}\n"
                        : string.Empty;
                    m_RespawnTimerLabel.text = prefix +
                        $"SPECTATING {target.Name}\nLMB NEXT  •  RMB PREVIOUS";
                }
                else if (ClientRespawnState.IsRespawning)
                {
                    m_RespawnTimerLabel.text =
                        $"RESPAWNING IN {Mathf.CeilToInt(m_RespawnCountdown)}";
                }
                else
                {
                    m_RespawnTimerLabel.text = "WAITING FOR NEXT ROUND";
                }
            }
        }

        private void ShowLobby(LeaderboardManager roundManager)
        {
            m_RespawnScreen.style.display = DisplayStyle.Flex;
            if (m_RespawnContent != null)
            {
                m_RespawnContent.style.display = DisplayStyle.None;
            }
            if (m_LobbyPanel != null)
            {
                m_LobbyPanel.style.display = DisplayStyle.Flex;
            }

            RespawnCamera.gameObject.SetActive(false);
            if (m_SpectatorAudioListener != null)
            {
                m_SpectatorAudioListener.enabled = false;
            }

            // Reassert this every frame because the player camera/controller may
            // otherwise recapture the pointer after the lobby first appears.
            UnityEngine.Cursor.lockState = CursorLockMode.None;
            UnityEngine.Cursor.visible = true;
            m_WasShowingLobby = true;

            List<LeaderboardManager.PlayerScoreEntry> players =
                roundManager.GetScores();
            players.Sort((left, right) =>
                left.NetworkId.CompareTo(right.NetworkId));
            int hostNetworkId = players.Count > 0 ? players[0].NetworkId : -1;
            var roster = new StringBuilder();
            foreach (LeaderboardManager.PlayerScoreEntry player in players)
            {
                bool isHost = player.NetworkId == hostNetworkId;
                bool isLocal = player.NetworkId == roundManager.LocalNetworkId;
                roster.Append(isHost ? "★ " : "• ");
                roster.Append(player.PlayerName.ToString());
                if (isHost) roster.Append("  [HOST]");
                if (isLocal) roster.Append("  [YOU]");
                roster.AppendLine();
            }

            if (m_LobbyPlayerCountLabel != null)
            {
                m_LobbyPlayerCountLabel.text =
                    $"PLAYERS  {players.Count} / {GameManager.MaxPlayer}";
            }
            if (m_LobbyPlayerListLabel != null)
            {
                m_LobbyPlayerListLabel.text = players.Count > 0
                    ? roster.ToString().TrimEnd()
                    : "Waiting for players...";
            }

            string sessionCode = GameManager.GameConnection?.Session?.Code;
            if (string.IsNullOrWhiteSpace(sessionCode))
            {
                sessionCode = ConnectionSettings.Instance.SessionCode;
            }
            if (m_LobbySessionLabel != null)
            {
                m_LobbySessionLabel.text = string.IsNullOrWhiteSpace(sessionCode)
                    ? "DIRECT CONNECTION LOBBY"
                    : $"SESSION CODE  {sessionCode}";
            }

            bool isHostClient = roundManager.IsLocalLobbyHost;
            bool canStart = roundManager.CanLocalPlayerStartLobby;
            if (m_LobbyStartButton != null)
            {
                m_LobbyStartButton.style.display = isHostClient
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
                m_LobbyStartButton.SetEnabled(canStart);
            }
            if (m_LobbyStatusLabel != null)
            {
                m_LobbyStatusLabel.text = isHostClient
                    ? canStart
                        ? "ALL CONNECTED — START WHEN EVERYONE IS READY"
                        : "WAITING FOR AT LEAST 2 PLAYERS"
                    : "WAITING FOR THE HOST TO START THE MATCH";
            }
        }

        private void HideLobby(bool lockCursorForGame)
        {
            if (m_LobbyPanel != null)
            {
                m_LobbyPanel.style.display = DisplayStyle.None;
            }
            if (m_RespawnContent != null)
            {
                m_RespawnContent.style.display = DisplayStyle.Flex;
            }
            if (!m_WasShowingLobby) return;

            m_WasShowingLobby = false;
            if (lockCursorForGame)
            {
                UnityEngine.Cursor.lockState = CursorLockMode.Locked;
                UnityEngine.Cursor.visible = false;
            }
        }

        private static void OnLobbyStartClicked()
        {
            LeaderboardManager.ClientInstance?.RequestLobbyStart();
        }

        private bool UpdateSpectatorCamera()
        {
            bool playerCameraMissing = GetActivePlayerCamera() == null;
            World clientWorld = GetClientWorld();
            if (clientWorld == null || !clientWorld.IsCreated)
            {
                if (playerCameraMissing) ApplyLastPlayerCameraPose();
                return playerCameraMissing;
            }

            EntityManager entityManager = clientWorld.EntityManager;
            using (EntityQuery localPlayerQuery = entityManager.CreateEntityQuery(
                       ComponentType.ReadOnly<PredictedPlayerGhost>(),
                       ComponentType.ReadOnly<GhostOwnerIsLocal>()))
            {
                if (!localPlayerQuery.IsEmptyIgnoreFilter)
                {
                    using NativeArray<PredictedPlayerGhost> localPlayers =
                        localPlayerQuery.ToComponentDataArray<PredictedPlayerGhost>(Allocator.Temp);
                    foreach (PredictedPlayerGhost localPlayer in localPlayers)
                    {
                        bool isAlive = localPlayer.CurrentHealth > 0f;
                        if (isAlive)
                        {
                            m_SpectatedNetworkId = -1;
                            return false;
                        }
                    }
                }
            }

            RebuildSpectatorTargets(entityManager);
            if (m_SpectatorTargets.Count == 0)
            {
                ApplyLastPlayerCameraPose();
                return true;
            }

            int preservedIndex = m_SpectatorTargets.FindIndex(
                target => target.NetworkId == m_SpectatedNetworkId);
            if (preservedIndex >= 0)
            {
                m_SpectatorIndex = preservedIndex;
            }
            else
            {
                m_SpectatorIndex = Mathf.Clamp(m_SpectatorIndex, 0,
                    m_SpectatorTargets.Count - 1);
            }

            Mouse mouse = Mouse.current;
            if (mouse?.leftButton.wasPressedThisFrame == true)
            {
                m_SpectatorIndex = (m_SpectatorIndex + 1) % m_SpectatorTargets.Count;
            }
            else if (mouse?.rightButton.wasPressedThisFrame == true)
            {
                m_SpectatorIndex = (m_SpectatorIndex - 1 + m_SpectatorTargets.Count) %
                                   m_SpectatorTargets.Count;
            }

            SpectatorTarget selectedTarget = m_SpectatorTargets[m_SpectatorIndex];
            m_SpectatedNetworkId = selectedTarget.NetworkId;
            FollowSpectatorTarget(selectedTarget.Player, !m_WasSpectating);
            return true;
        }

        private static World GetClientWorld()
        {
            World bootstrapWorld = GhostBridgeBootstrap.Instance?.ClientWorld;
            if (bootstrapWorld != null && bootstrapWorld.IsCreated)
            {
                return bootstrapWorld;
            }

            // GameManager creates NetCode worlds directly, so GhostBridgeBootstrap's
            // own ClientWorld property is normally null during a hosted/joined match.
            for (int i = 0; i < World.All.Count; i++)
            {
                World world = World.All[i];
                if (world.IsCreated && world.IsClient())
                {
                    return world;
                }
            }

            return null;
        }

        private void CachePlayerCameraPose()
        {
            Camera playerCamera = GetActivePlayerCamera();
            if (playerCamera == null) return;

            m_LastPlayerCameraPosition = playerCamera.transform.position;
            m_LastPlayerCameraRotation = playerCamera.transform.rotation;
            m_HasLastPlayerCameraPose = true;
        }

        private Camera GetActivePlayerCamera()
        {
            Camera playerCamera = Camera.main;
            return playerCamera != null && playerCamera != RespawnCamera &&
                   playerCamera.enabled && playerCamera.gameObject.activeInHierarchy
                ? playerCamera
                : null;
        }

        private void ApplyLastPlayerCameraPose()
        {
            if (!m_HasLastPlayerCameraPose) return;
            RespawnCamera.transform.SetPositionAndRotation(
                m_LastPlayerCameraPosition, m_LastPlayerCameraRotation);
        }

        private void RebuildSpectatorTargets(EntityManager entityManager)
        {
            m_SpectatorTargets.Clear();
            using EntityQuery playerQuery = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<PredictedPlayerGhost>(),
                ComponentType.ReadOnly<GhostOwner>(),
                ComponentType.ReadOnly<GhostGameObjectLink>());
            using NativeArray<Entity> players = playerQuery.ToEntityArray(Allocator.Temp);
            foreach (Entity playerEntity in players)
            {
                PredictedPlayerGhost state =
                    entityManager.GetComponentData<PredictedPlayerGhost>(playerEntity);
                if (state.CurrentHealth <= 0f)
                {
                    continue;
                }

                GhostGameObjectLink link =
                    entityManager.GetComponentObject<GhostGameObjectLink>(playerEntity);
                PlayerGhost player = link?.LinkedInstance != null
                    ? link.LinkedInstance.GetComponent<PlayerGhost>()
                    : null;
                if (player == null) continue;

                int networkId = entityManager.GetComponentData<GhostOwner>(playerEntity).NetworkId;
                string playerName = entityManager.HasComponent<PlayerGhost.PlayerData>(playerEntity)
                    ? entityManager.GetComponentData<PlayerGhost.PlayerData>(playerEntity).Name.ToString()
                    : $"PLAYER {networkId}";
                m_SpectatorTargets.Add(new SpectatorTarget(networkId, playerName, player));
            }

            m_SpectatorTargets.Sort((left, right) =>
                left.NetworkId.CompareTo(right.NetworkId));
        }

        private void FollowSpectatorTarget(PlayerGhost player, bool snapToTarget)
        {
            if (player == null) return;
            Transform targetTransform = player.CameraTarget != null
                ? player.CameraTarget
                : player.transform;
            Vector3 focusPoint = targetTransform.position;
            Vector3 desiredPosition = focusPoint - player.transform.forward * 4f +
                                      Vector3.up * 1.5f;
            float blend = snapToTarget
                ? 1f
                : 1f - Mathf.Exp(-10f * Time.unscaledDeltaTime);
            RespawnCamera.transform.position = Vector3.Lerp(
                RespawnCamera.transform.position, desiredPosition, blend);
            Vector3 lookDirection = focusPoint - RespawnCamera.transform.position;
            if (lookDirection.sqrMagnitude > 0.001f)
            {
                RespawnCamera.transform.rotation = Quaternion.Slerp(
                    RespawnCamera.transform.rotation,
                    Quaternion.LookRotation(lookDirection.normalized, Vector3.up),
                    blend);
            }
        }
    }
}
