using UnityEngine;

namespace Unity.MP_FPS
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public class ShrinkResourcePickup : MonoBehaviour, INetworkPickupState
    {
        [Header("Pickup Settings")]
        [SerializeField, Min(0.01f), Tooltip("Amount of shrink resource restored when collected.")]
        private float m_RestoreAmount = 2.5f;

        [SerializeField, Min(0f), Tooltip("Seconds before the pickup can be collected again.")]
        private float m_RespawnDelay = 8f;

        [Header("Visuals")]
        [SerializeField] private float m_RotationSpeed = 90f;
        [SerializeField, Min(0f)] private float m_BobHeight = 0.15f;
        [SerializeField, Min(0f)] private float m_BobFrequency = 1.25f;

        private Renderer[] m_Renderers;
        private Vector3 m_StartPosition;
        private bool m_ServerAvailable = true;
        private float m_ServerRespawnTime;
        private float m_NextUnavailableSyncTime;
        private uint m_NetworkPickupId;

        private void Awake()
        {
            m_Renderers = GetComponentsInChildren<Renderer>(true);
            m_StartPosition = transform.localPosition;
            m_NetworkPickupId = PickupNetworkSync.CreateStableId(transform);
            PickupNetworkSync.Register(m_NetworkPickupId, this);

            if (TryGetComponent(out Collider pickupCollider))
            {
                pickupCollider.isTrigger = true;
            }
        }

        private void Update()
        {
            transform.Rotate(Vector3.up, m_RotationSpeed * Time.deltaTime, Space.Self);
            float bobOffset = Mathf.Sin(Time.time * m_BobFrequency * Mathf.PI * 2f) * m_BobHeight;
            transform.localPosition = m_StartPosition + Vector3.up * bobOffset;

            if (!m_ServerAvailable && Time.time >= m_ServerRespawnTime)
            {
                m_ServerAvailable = true;
                m_NextUnavailableSyncTime = 0f;
                SetVisualsVisible(true);
                PickupNetworkSync.BroadcastFromServer(m_NetworkPickupId, true);
            }
            else if (!m_ServerAvailable && Time.time >= m_NextUnavailableSyncTime)
            {
                // Refresh unavailable state for clients that joined after collection.
                m_NextUnavailableSyncTime = Time.time + 1f;
                PickupNetworkSync.BroadcastFromServer(m_NetworkPickupId, false);
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            var player = other.GetComponentInParent<PlayerGhost>();
            if (player == null || player.GhostGameObject == null)
            {
                return;
            }

            var entityManager = player.GhostGameObject.EntityManager;
            var playerEntity = player.GhostGameObject.LinkedEntity;
            if (!entityManager.Exists(playerEntity) ||
                !entityManager.HasComponent<PredictedPlayerGhost>(playerEntity))
            {
                return;
            }

            var playerData = entityManager.GetComponentData<PredictedPlayerGhost>(playerEntity);
            if (playerData.MaxShrinkResource <= 0f ||
                playerData.CurrentShrinkResource >= playerData.MaxShrinkResource)
            {
                return;
            }

            if (player.Role == MultiplayerRole.Server)
            {
                TryCollectOnServer(entityManager, playerEntity, playerData);
            }
        }

        private void TryCollectOnServer(Unity.Entities.EntityManager entityManager,
            Unity.Entities.Entity playerEntity, PredictedPlayerGhost playerData)
        {
            if (!m_ServerAvailable)
            {
                return;
            }

            playerData.CurrentShrinkResource = Mathf.Min(
                playerData.MaxShrinkResource,
                playerData.CurrentShrinkResource + m_RestoreAmount);
            entityManager.SetComponentData(playerEntity, playerData);

            m_ServerAvailable = false;
            m_ServerRespawnTime = Time.time + m_RespawnDelay;
            m_NextUnavailableSyncTime = Time.time + 1f;
            SetVisualsVisible(false);
            PickupNetworkSync.BroadcastFromServer(m_NetworkPickupId, false);
        }

        public void ApplyNetworkAvailability(bool available)
        {
            SetVisualsVisible(available);
        }

        private void SetVisualsVisible(bool visible)
        {
            foreach (var pickupRenderer in m_Renderers)
            {
                pickupRenderer.enabled = visible;
            }
        }

        private void OnValidate()
        {
            if (TryGetComponent(out Collider pickupCollider))
            {
                pickupCollider.isTrigger = true;
            }
        }

        private void OnDestroy()
        {
            PickupNetworkSync.Unregister(m_NetworkPickupId, this);
        }
    }
}
