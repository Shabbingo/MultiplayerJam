using UnityEngine;

namespace Unity.MP_FPS
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public class HealthPickup : MonoBehaviour
    {
        [Header("Pickup Settings")]
        [SerializeField, Min(0.01f), Tooltip("Amount of full-size health restored when collected.")]
        private float m_HealAmount = 25f;

        [SerializeField, Min(0f), Tooltip("Seconds before the health pack can be collected again.")]
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
            if (player == null || player.Controller == null || player.GhostGameObject == null)
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
            if (!player.Controller.CanRestoreHealth(in playerData))
            {
                return;
            }

            if (player.Role == MultiplayerRole.Server)
            {
                TryCollectOnServer(entityManager, playerEntity, playerData, player.Controller);
            }
            else if (player.Role == MultiplayerRole.ClientOwned)
            {
                // The server grants health. The owning client only plays immediate feedback.
                HideUntilRespawn();
            }
        }

        private void TryCollectOnServer(Unity.Entities.EntityManager entityManager,
            Unity.Entities.Entity playerEntity, PredictedPlayerGhost playerData,
            FirstPersonController controller)
        {
            if (!m_ServerAvailable || !controller.TryRestoreHealth(ref playerData, m_HealAmount))
            {
                return;
            }

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
