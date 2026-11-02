using UnityEngine;

namespace Unity.MP_FPS
{
    public class PlaceholderBloodHitEffect : MonoBehaviour
    {
        [SerializeField] private Material m_BloodMaterial;
        [SerializeField, Range(1, 12)] private int m_DropletCount = 6;
        [SerializeField, Min(0.05f)] private float m_Duration = 0.35f;
        [SerializeField, Min(0.01f)] private float m_DropletSize = 0.18f;
        [SerializeField, Min(0.01f)] private float m_BurstDistance = 1.2f;

        private Transform[] m_Droplets;
        private Vector3[] m_Directions;
        private Vector3[] m_InitialScales;
        private float m_Elapsed;

        private void Awake()
        {
            m_Droplets = new Transform[m_DropletCount];
            m_Directions = new Vector3[m_DropletCount];
            m_InitialScales = new Vector3[m_DropletCount];

            for (int i = 0; i < m_DropletCount; i++)
            {
                var droplet = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                droplet.name = "BloodDroplet";
                droplet.transform.SetParent(transform, false);
                var initialScale = Vector3.one * m_DropletSize *
                                   Random.Range(0.65f, 1.15f);
                droplet.transform.localScale = initialScale;

                if (droplet.TryGetComponent(out Collider dropletCollider))
                {
                    Destroy(dropletCollider);
                }

                if (m_BloodMaterial != null &&
                    droplet.TryGetComponent(out Renderer dropletRenderer))
                {
                    dropletRenderer.sharedMaterial = m_BloodMaterial;
                    dropletRenderer.shadowCastingMode =
                        UnityEngine.Rendering.ShadowCastingMode.Off;
                }

                m_Droplets[i] = droplet.transform;
                m_Directions[i] = Random.onUnitSphere.normalized;
                m_InitialScales[i] = initialScale;
            }
        }

        private void Update()
        {
            m_Elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(m_Elapsed / m_Duration);
            float distance = Mathf.Sin(progress * Mathf.PI * 0.5f) * m_BurstDistance;

            for (int i = 0; i < m_Droplets.Length; i++)
            {
                if (m_Droplets[i] == null)
                {
                    continue;
                }

                m_Droplets[i].localPosition = m_Directions[i] * distance;
                m_Droplets[i].localScale = m_InitialScales[i] * Mathf.Max(0f, 1f - progress);
            }

            if (progress >= 1f)
            {
                Destroy(gameObject);
            }
        }
    }
}
