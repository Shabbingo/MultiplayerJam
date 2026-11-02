using UnityEngine;

namespace Unity.MP_FPS
{
    /// <summary>
    /// Temporary three-ring explosion visual. The ghost is spawned with a uniform
    /// scale equal to the weapon's AOE radius, so a local radius of one marks the
    /// exact gameplay boundary.
    /// </summary>
    public class AoeRangeExplosionEffect : MonoBehaviour
    {
        [SerializeField, Min(0.05f)] private float m_Duration = 0.85f;
        [SerializeField, Range(0f, 1f)] private float m_StartRadius = 0.08f;
        [SerializeField, Min(0.005f)] private float m_RingWidth = 0.055f;
        [SerializeField, Range(12, 128)] private int m_RingSegments = 64;
        [SerializeField] private Color m_Colour = new(1f, 0.28f, 0.03f, 0.9f);

        private Transform m_RingRoot;
        private LineRenderer[] m_Rings;
        private Material m_Material;
        private float m_Elapsed;

        private void Awake()
        {
            var ringRootObject = new GameObject("AOE Range Rings");
            m_RingRoot = ringRootObject.transform;
            m_RingRoot.SetParent(transform, false);
            m_RingRoot.localScale = Vector3.one * m_StartRadius;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            }

            if (shader != null)
            {
                m_Material = new Material(shader)
                {
                    name = "Runtime Orange AOE Range Material"
                };
            }

            m_Rings = new LineRenderer[3];
            for (int ringIndex = 0; ringIndex < m_Rings.Length; ringIndex++)
            {
                m_Rings[ringIndex] = CreateRing(ringIndex);
            }
        }

        private LineRenderer CreateRing(int plane)
        {
            var ringObject = new GameObject($"Range Ring {plane + 1}");
            ringObject.transform.SetParent(m_RingRoot, false);
            var ring = ringObject.AddComponent<LineRenderer>();
            ring.useWorldSpace = false;
            ring.loop = true;
            ring.positionCount = m_RingSegments;
            ring.widthMultiplier = m_RingWidth;
            ring.numCornerVertices = 2;
            ring.numCapVertices = 2;
            ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ring.receiveShadows = false;
            ring.alignment = LineAlignment.View;

            if (m_Material != null)
            {
                ring.sharedMaterial = m_Material;
            }

            ring.startColor = m_Colour;
            ring.endColor = m_Colour;

            for (int i = 0; i < m_RingSegments; i++)
            {
                float angle = i * Mathf.PI * 2f / m_RingSegments;
                float firstAxis = Mathf.Cos(angle);
                float secondAxis = Mathf.Sin(angle);
                Vector3 point = plane switch
                {
                    0 => new Vector3(firstAxis, 0f, secondAxis),
                    1 => new Vector3(firstAxis, secondAxis, 0f),
                    _ => new Vector3(0f, firstAxis, secondAxis)
                };
                ring.SetPosition(i, point);
            }

            return ring;
        }

        private void Update()
        {
            m_Elapsed += Time.deltaTime;
            float normalizedTime = Mathf.Clamp01(m_Elapsed / m_Duration);
            float expansion = 1f - Mathf.Pow(1f - normalizedTime, 3f);
            m_RingRoot.localScale = Vector3.one * Mathf.Lerp(m_StartRadius, 1f, expansion);

            float fade = 1f - Mathf.InverseLerp(0.35f, 1f, normalizedTime);
            Color fadedColour = m_Colour;
            fadedColour.a *= fade;
            foreach (var ring in m_Rings)
            {
                ring.startColor = fadedColour;
                ring.endColor = fadedColour;
                ring.widthMultiplier = m_RingWidth * Mathf.Lerp(1f, 0.35f, normalizedTime);
            }

            if (normalizedTime >= 1f)
            {
                m_RingRoot.gameObject.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            if (m_Material != null)
            {
                Destroy(m_Material);
            }
        }
    }
}
