using Unity.Entities;
using Unity.NetCode;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.UI
{
    [RequireComponent(typeof(UIDocument))]
    public class RespawnScreen : MonoBehaviour
    {
        public Camera RespawnCamera;
        private VisualElement m_RespawnScreen;
        private Label m_RespawnTimerLabel;

        private World m_ClientWorld;
        private EntityManager m_EntityManager;
        private EntityQuery m_LocalPlayerQuery;
        private bool m_HasLocalPlayerQuery;

        private float m_RespawnCountdown;
        private const float RESPAWN_DURATION = 5.0f;

        private void Awake()
        {
            RespawnCamera.gameObject.SetActive(false);
        }

        void OnEnable()
        {
            m_RespawnScreen = GetComponent<UIDocument>().rootVisualElement;
            m_RespawnTimerLabel = m_RespawnScreen.Q<Label>("RespawnMessage");
        }

        private void BindToClientWorld(World world)
        {
            if (world == null || !world.IsCreated || world == m_ClientWorld)
            {
                return;
            }

            if (m_HasLocalPlayerQuery)
            {
                m_LocalPlayerQuery.Dispose();
            }

            m_ClientWorld = world;
            m_EntityManager = world.EntityManager;
            m_LocalPlayerQuery = m_EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<PredictedPlayerGhost>(),
                ComponentType.ReadOnly<GhostOwnerIsLocal>());
            m_HasLocalPlayerQuery = true;
        }

        private bool TryBindToWorldWithLocalPlayer()
        {
            World fallbackWorld = null;

            foreach (var world in World.All)
            {
                if (!world.IsCreated || !world.IsClient())
                {
                    continue;
                }

                fallbackWorld ??= world;

                using var localPlayerQuery = world.EntityManager.CreateEntityQuery(
                    ComponentType.ReadOnly<PredictedPlayerGhost>(),
                    ComponentType.ReadOnly<GhostOwnerIsLocal>());
                if (!localPlayerQuery.IsEmptyIgnoreFilter)
                {
                    BindToClientWorld(world);
                    return true;
                }
            }

            if (m_ClientWorld == null || !m_ClientWorld.IsCreated)
            {
                BindToClientWorld(fallbackWorld);
            }

            return false;
        }

        private void OnDisable()
        {
            if (m_HasLocalPlayerQuery)
            {
                m_LocalPlayerQuery.Dispose();
                m_HasLocalPlayerQuery = false;
            }

            m_ClientWorld = null;
        }

        void LateUpdate()
        {
            if (GameSettings.Instance.GameState != GlobalGameState.InGame)
            {
                m_RespawnScreen.style.display = DisplayStyle.None;
                return;
            }

            if (!m_HasLocalPlayerQuery || m_ClientWorld == null || !m_ClientWorld.IsCreated)
            {
                TryBindToWorldWithLocalPlayer();
                if (!m_HasLocalPlayerQuery) return;
            }

            // A menu/previous-session client world can remain alive while the gameplay
            // client world is created. If the cached world has no player, look for the
            // world that actually owns one before showing the death screen.
            bool isPlayerAlive = !m_LocalPlayerQuery.IsEmptyIgnoreFilter;
            if (!isPlayerAlive && TryBindToWorldWithLocalPlayer())
            {
                isPlayerAlive = !m_LocalPlayerQuery.IsEmptyIgnoreFilter;
            }

            if (isPlayerAlive)
            {
                RespawnCamera.gameObject.SetActive(false);
                // Player is alive, hide the respawn screen
                if (m_RespawnScreen.style.display == DisplayStyle.Flex)
                {
                    m_RespawnScreen.style.display = DisplayStyle.None;
                }
            }
            else
            {
                RespawnCamera.gameObject.SetActive(true);
                // Player is dead, show the respawn screen and update the timer
                if (m_RespawnScreen.style.display == DisplayStyle.None)
                {
                    // This is the first frame death is detected, start the countdown
                    m_RespawnCountdown = RESPAWN_DURATION;
                    m_RespawnScreen.style.display = DisplayStyle.Flex;
                }

                m_RespawnCountdown -= Time.deltaTime;
                if (m_RespawnCountdown < 0)
                {
                    m_RespawnCountdown = 0;
                }

                m_RespawnTimerLabel.text = $"RESPAWNING IN {Mathf.CeilToInt(m_RespawnCountdown).ToString()}";
            }
        }
    }
}
