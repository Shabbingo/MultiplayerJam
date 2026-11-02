using UnityEngine;

namespace Unity.MP_FPS
{
    /// <summary>
    /// Temporary player-gib visual. Replace the prefab on VisualEffectManager when
    /// final explosion art is available.
    /// </summary>
    public class PlaceholderPlayerExplosion : MonoBehaviour
    {
        [SerializeField, Min(0.05f)] private float m_Duration = 0.65f;
        [SerializeField, Min(0f)] private float m_StartScale = 0.15f;
        [SerializeField, Min(0f)] private float m_EndScale = 3f;
        [SerializeField, Min(0f)] private float m_LightIntensity = 6f;
        [SerializeField, Min(0f)] private float m_LightRange = 7f;

        private Vector3 m_BaseScale;
        private Light m_FlashLight;
        private float m_Elapsed;

        private void Awake()
        {
            m_BaseScale = transform.localScale;
            transform.localScale = m_BaseScale * m_StartScale;

            m_FlashLight = gameObject.AddComponent<Light>();
            m_FlashLight.type = LightType.Point;
            m_FlashLight.color = new Color(1f, 0.15f, 0.02f);
            m_FlashLight.intensity = m_LightIntensity;
            m_FlashLight.range = m_LightRange;
            m_FlashLight.shadows = LightShadows.None;
        }

        private void Update()
        {
            m_Elapsed += Time.deltaTime;
            float normalizedTime = Mathf.Clamp01(m_Elapsed / m_Duration);
            float easedTime = 1f - Mathf.Pow(1f - normalizedTime, 3f);

            transform.localScale = m_BaseScale *
                                   Mathf.Lerp(m_StartScale, m_EndScale, easedTime);
            transform.Rotate(120f * Time.deltaTime, 180f * Time.deltaTime, 0f,
                Space.Self);
            m_FlashLight.intensity = Mathf.Lerp(m_LightIntensity, 0f, normalizedTime);

            if (normalizedTime >= 1f)
            {
                Destroy(gameObject);
            }
        }
    }
}
