using UnityEngine;

namespace Unity.MP_FPS
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public class ShrinkResourcePickup : MonoBehaviour
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
        private float m_VisualRespawnTime;

        private void Awake()
        {
            m_Renderers = GetComponentsInChildren<Renderer>(true);
            m_StartPosition = transform.localPosition;

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
            }

            if (m_VisualRespawnTime > 0f && Time.time >= m_VisualRespawnTime)
            {
                m_VisualRespawnTime = 0f;
                SetVisualsVisible(true);
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
            else if (player.Role == MultiplayerRole.ClientOwned)
            {
                // The server grants the resource. The owning client only plays immediate
                // collection feedback; leaving the trigger active avoids host-mode races.
                HideUntilRespawn();
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
            HideUntilRespawn();
        }

        private void HideUntilRespawn()
        {
            SetVisualsVisible(false);
            m_VisualRespawnTime = Mathf.Max(m_VisualRespawnTime, Time.time + m_RespawnDelay);
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
    }
}
