using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

namespace Unity.MP_FPS
{
    public static class CameraFovEffect
    {
        private static float s_Amount;
        private static float s_Duration;
        private static float s_TimeRemaining;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            s_Amount = 0f;
            s_Duration = 0f;
            s_TimeRemaining = 0f;
        }

        public static void Trigger(float amount, float duration)
        {
            s_Amount = Mathf.Max(0f, amount);
            s_Duration = Mathf.Max(0.01f, duration);
            s_TimeRemaining = s_Duration;
        }

        public static float UpdateAndGetOffset(float deltaTime)
        {
            if (s_TimeRemaining <= 0f)
            {
                return 0f;
            }

            s_TimeRemaining = Mathf.Max(0f, s_TimeRemaining - deltaTime);
            float normalizedTime = 1f - s_TimeRemaining / s_Duration;
            // Fast initial punch followed by a smooth settle back to the base FOV.
            float strength = 1f - Mathf.SmoothStep(0f, 1f, normalizedTime);
            return s_Amount * strength;
        }
    }

    /// <summary>
    /// Updates the <see cref="MainGameObjectCamera"/> postion to match the current player <see cref="MainCamera"/> component position if it exists.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class MainCameraSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<MainCamera>();
        }

        protected override void OnUpdate()
        {
            if (MainCameraSingleton.Instance != null)
            {
                EntityManager.CompleteAllTrackedJobs();
                Entity mainEntityCameraEntity = SystemAPI.GetSingletonEntity<MainCamera>();
                MainCamera mainCamera = SystemAPI.GetSingleton<MainCamera>();
                LocalToWorld targetLocalToWorld = SystemAPI.GetComponent<LocalToWorld>(mainEntityCameraEntity);
                MainCameraSingleton.Instance.transform.SetPositionAndRotation(targetLocalToWorld.Position,targetLocalToWorld.Rotation);
                float fovOffset = CameraFovEffect.UpdateAndGetOffset(SystemAPI.Time.DeltaTime);
                float baseFov = GameSettings.Instance != null
                    ? GameSettings.Instance.CameraFov
                    : mainCamera.BaseFov;
                MainCameraSingleton.Instance.fieldOfView =
                    mainCamera.CurrentFov = baseFov + fovOffset;
            }
        }
    }
}
