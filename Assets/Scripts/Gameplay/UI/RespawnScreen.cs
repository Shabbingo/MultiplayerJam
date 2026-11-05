using Unity.Entities;
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

        private bool m_IsRespawning;
        private uint m_LastRespawnSequence;

        private float m_RespawnCountdown;

        private void Awake()
        {
            RespawnCamera.gameObject.SetActive(false);
        }

        void OnEnable()
        {
            m_RespawnScreen = GetComponent<UIDocument>().rootVisualElement;
            m_RespawnTimerLabel = m_RespawnScreen.Q<Label>("RespawnMessage");
            m_RespawnScreen.style.display = DisplayStyle.None;
            m_IsRespawning = false;
            m_LastRespawnSequence = ClientRespawnState.Sequence;
        }

        private void OnDisable()
        {
            m_IsRespawning = false;
            RespawnCamera.gameObject.SetActive(false);
        }

        void LateUpdate()
        {
            if (GameSettings.Instance.GameState != GlobalGameState.InGame)
            {
                m_RespawnScreen.style.display = DisplayStyle.None;
                RespawnCamera.gameObject.SetActive(false);
                m_IsRespawning = false;
                return;
            }

            if (!ClientRespawnState.IsRespawning)
            {
                m_IsRespawning = false;
                RespawnCamera.gameObject.SetActive(false);
                m_RespawnScreen.style.display = DisplayStyle.None;
                m_LastRespawnSequence = ClientRespawnState.Sequence;
                return;
            }

            RespawnCamera.gameObject.SetActive(true);
            if (!m_IsRespawning ||
                m_LastRespawnSequence != ClientRespawnState.Sequence)
            {
                m_RespawnCountdown = ClientRespawnState.Duration;
                m_IsRespawning = true;
                m_LastRespawnSequence = ClientRespawnState.Sequence;
            }
            m_RespawnScreen.style.display = DisplayStyle.Flex;

            m_RespawnCountdown = Mathf.Max(0f,
                m_RespawnCountdown - Time.deltaTime);

            if (m_RespawnTimerLabel != null)
            {
                m_RespawnTimerLabel.text =
                    $"RESPAWNING IN {Mathf.CeilToInt(m_RespawnCountdown).ToString()}";
            }
        }
    }
}
