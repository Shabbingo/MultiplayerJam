using System;
using UnityEngine;

namespace Unity.MP_FPS
{
    public class EventHandler : MonoBehaviour
    {
        public float minFootstepInterval = 0.2f;
        public SoundDef footstep;
        public SoundDef reloadSFX;

        [NonSerialized] public bool onFootDown;
        [NonSerialized] public bool onLand;
        [NonSerialized] public bool onDoubleJump;
        [NonSerialized] public bool onJumpStart;

        private float m_LastFootstepTime = float.NegativeInfinity;

        public void OnCharEvent(AnimationEvent e)
        {
            onFootDown = true;
        }

        public void OnLand(AnimationEvent e)
        {
            onFootDown = true;
        }

        public void ReloadAnimationSFXTrigger(AnimationEvent e)
        {
            if (reloadSFX != null)
            {
                GameManager.Instance.SoundSystem.CreateEmitter(reloadSFX, transform);
            }
        }

        void Update()
        {
            if (onFootDown)
            {
                onFootDown = false;

                if (footstep != null &&
                    Time.time - m_LastFootstepTime >= minFootstepInterval &&
                    GameManager.Instance?.SoundSystem != null)
                {
                    m_LastFootstepTime = Time.time;
                    GameManager.Instance.SoundSystem.CreateEmitter(footstep, transform);
                }
            }
        }
    }
}
