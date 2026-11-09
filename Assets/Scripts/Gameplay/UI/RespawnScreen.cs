using Unity.Entities;
using System;
using System.Collections.Generic;
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

        private bool m_IsRespawning;
        private uint m_LastRespawnSequence;

        private float m_RespawnCountdown;
        private bool m_HasSeenLocalPlayer;
        private int m_SpectatorIndex;
        private int m_SpectatedNetworkId = -1;
        private AudioListener m_SpectatorAudioListener;
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
            m_RespawnScreen.style.display = DisplayStyle.None;
            m_IsRespawning = false;
            m_LastRespawnSequence = ClientRespawnState.Sequence;
            m_HasSeenLocalPlayer = false;
            m_SpectatorIndex = 0;
            m_SpectatedNetworkId = -1;
        }

        private void OnDisable()
        {
            m_IsRespawning = false;
            if (m_SpectatorAudioListener != null) m_SpectatorAudioListener.enabled = false;
            RespawnCamera.gameObject.SetActive(false);
        }

        void LateUpdate()
        {
            if (GameSettings.Instance.GameState != GlobalGameState.InGame)
            {
                m_RespawnScreen.style.display = DisplayStyle.None;
                RespawnCamera.gameObject.SetActive(false);
                if (m_SpectatorAudioListener != null) m_SpectatorAudioListener.enabled = false;
                m_IsRespawning = false;
                return;
            }

            bool isSpectating = UpdateSpectatorCamera();
            if (!ClientRespawnState.IsRespawning && !isSpectating)
            {
                m_IsRespawning = false;
                RespawnCamera.gameObject.SetActive(false);
                if (m_SpectatorAudioListener != null) m_SpectatorAudioListener.enabled = false;
                m_RespawnScreen.style.display = DisplayStyle.None;
                m_LastRespawnSequence = ClientRespawnState.Sequence;
                return;
            }

            RespawnCamera.gameObject.SetActive(true);
            if (m_SpectatorAudioListener != null) m_SpectatorAudioListener.enabled = isSpectating;
            if (isSpectating && GameManager.Instance?.SoundSystem != null)
            {
                GameManager.Instance.SoundSystem.SetListenerTransform(RespawnCamera.transform);
            }
            if (GameSettings.Instance != null)
            {
                RespawnCamera.fieldOfView = GameSettings.Instance.CameraFov;
            }

            if (ClientRespawnState.IsRespawning && (!m_IsRespawning ||
                m_LastRespawnSequence != ClientRespawnState.Sequence))
            {
                m_RespawnCountdown = ClientRespawnState.Duration;
                m_IsRespawning = true;
                m_LastRespawnSequence = ClientRespawnState.Sequence;
            }
            m_RespawnScreen.style.display = DisplayStyle.Flex;

            if (ClientRespawnState.IsRespawning)
            {
                m_RespawnCountdown = Mathf.Max(0f,
                    m_RespawnCountdown - Time.deltaTime);
            }

            if (m_RespawnTimerLabel != null)
            {
                if (isSpectating && m_SpectatorTargets.Count > 0)
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

        private bool UpdateSpectatorCamera()
        {
            World clientWorld = GhostBridgeBootstrap.Instance?.ClientWorld;
            if (clientWorld == null || !clientWorld.IsCreated)
            {
                return false;
            }

            EntityManager entityManager = clientWorld.EntityManager;
            using (EntityQuery localPlayerQuery = entityManager.CreateEntityQuery(
                       ComponentType.ReadOnly<PredictedPlayerGhost>(),
                       ComponentType.ReadOnly<GhostOwnerIsLocal>()))
            {
                if (!localPlayerQuery.IsEmptyIgnoreFilter)
                {
                    m_HasSeenLocalPlayer = true;
                    m_SpectatedNetworkId = -1;
                    return false;
                }
            }

            // Avoid flashing spectator mode during initial connection before this
            // client has ever owned a character.
            if (!m_HasSeenLocalPlayer)
            {
                return false;
            }

            RebuildSpectatorTargets(entityManager);
            if (m_SpectatorTargets.Count == 0)
            {
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
            FollowSpectatorTarget(selectedTarget.Player);
            return true;
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
                if (state.CurrentHealth <= 0f ||
                    (state.MaxHealth > 0f && state.CurrentHealth >= state.MaxHealth))
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

        private void FollowSpectatorTarget(PlayerGhost player)
        {
            if (player == null) return;
            Transform targetTransform = player.CameraTarget != null
                ? player.CameraTarget
                : player.transform;
            Vector3 focusPoint = targetTransform.position;
            Vector3 desiredPosition = focusPoint - player.transform.forward * 4f +
                                      Vector3.up * 1.5f;
            float blend = 1f - Mathf.Exp(-10f * Time.unscaledDeltaTime);
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
