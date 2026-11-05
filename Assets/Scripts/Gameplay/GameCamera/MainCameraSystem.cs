using Unity.Entities;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Unity.Transforms;
using UnityEngine;

namespace Unity.MP_FPS
{
    [DisallowMultipleComponent]
    public sealed class CameraResizePulseDriver : MonoBehaviour
    {
        private Camera m_Camera;

        private void Awake()
        {
            m_Camera = GetComponent<Camera>();
        }

        private void Update()
        {
            CameraResizePulseEffect.Update(m_Camera, Time.unscaledDeltaTime);
        }
    }

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

    public static class CameraResizePulseEffect
    {
        private const string k_VolumeProfileResource = "SelfResizePulseVolumeProfile";
        private const float k_VolumePriority = 10000f;
        private static float s_PeakIntensity;
        private static float s_Duration;
        private static float s_TimeRemaining;
        private static AnimationCurve s_PulseCurve;
        private static float s_ConfiguredIntensity = 0.35f;
        private static float s_ConfiguredDuration = 0.35f;
        private static AnimationCurve s_ConfiguredCurve;
        private static float s_ShrinkCenterY = 0.15f;
        private static float s_GrowCenterY = 0.85f;
        private static bool s_InvertDirection;
        private static Vector2 s_ActiveCenter = new(0.5f, 0.5f);
        private static Volume s_Volume;
        private static Volume s_BoundVolume;
        private static LensDistortion s_LensDistortion;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            s_PeakIntensity = 0f;
            s_Duration = 0f;
            s_TimeRemaining = 0f;
            s_PulseCurve = null;
            s_ConfiguredIntensity = 0.35f;
            s_ConfiguredDuration = 0.35f;
            s_ConfiguredCurve = null;
            s_ShrinkCenterY = 0.15f;
            s_GrowCenterY = 0.85f;
            s_InvertDirection = false;
            s_ActiveCenter = new Vector2(0.5f, 0.5f);
            s_Volume = null;
            s_BoundVolume = null;
            s_LensDistortion = null;
        }

        public static void Configure(float intensity, float duration,
            AnimationCurve pulseCurve, float shrinkCenterY, float growCenterY,
            bool invertDirection)
        {
            s_ConfiguredIntensity = Mathf.Clamp01(intensity);
            s_ConfiguredDuration = Mathf.Max(0.01f, duration);
            s_ConfiguredCurve = pulseCurve;
            s_ShrinkCenterY = Mathf.Clamp01(shrinkCenterY);
            s_GrowCenterY = Mathf.Clamp01(growCenterY);
            s_InvertDirection = invertDirection;
        }

        public static void TriggerConfigured(int resizeDirection)
        {
            if (resizeDirection == 0)
            {
                return;
            }

            var camera = MainCameraSingleton.Instance;
            EnsureDriver(camera);
            EnsureVolume(camera);
            s_ActiveCenter = new Vector2(
                0.5f,
                resizeDirection < 0 ? s_ShrinkCenterY : s_GrowCenterY);
            Trigger(
                // URP's positive lens distortion pulls the image inward, while
                // negative distortion pushes it outward.
                (s_InvertDirection ? 1f : -1f) *
                Mathf.Sign(resizeDirection) * s_ConfiguredIntensity,
                s_ConfiguredDuration,
                s_ConfiguredCurve);

            // Apply the peak value on the input frame itself. The driver only
            // handles returning it to zero afterwards.
            if (s_LensDistortion != null)
            {
                s_LensDistortion.active = true;
                s_LensDistortion.intensity.overrideState = true;
                s_LensDistortion.intensity.value = s_PeakIntensity;
                s_LensDistortion.center.overrideState = true;
                s_LensDistortion.center.value = s_ActiveCenter;
                Debug.Log($"[ResizeLensPulse] Applied {s_PeakIntensity:0.00} to " +
                          $"'{s_Volume.name}' at Y {s_ActiveCenter.y:0.00} " +
                          $"for {s_Duration:0.00}s.");
            }
            else
            {
                Debug.LogWarning("[ResizeLensPulse] Resize input was received, but no active " +
                                 "Global Volume with Lens Distortion was found.");
            }
        }

        public static void Trigger(float signedIntensity, float duration,
            AnimationCurve pulseCurve)
        {
            s_PeakIntensity = Mathf.Clamp(signedIntensity, -1f, 1f);
            s_Duration = Mathf.Max(0.01f, duration);
            s_TimeRemaining = s_Duration;
            s_PulseCurve = pulseCurve;
        }

        public static void Update(Camera camera, float deltaTime)
        {
            if (camera == null)
            {
                return;
            }

            EnsureVolume(camera);
            if (s_LensDistortion == null)
            {
                return;
            }

            if (s_TimeRemaining <= 0f)
            {
                s_LensDistortion.intensity.value = 0f;
                s_LensDistortion.center.value = new Vector2(0.5f, 0.5f);
                s_LensDistortion.active = false;
                return;
            }

            s_TimeRemaining = Mathf.Max(0f, s_TimeRemaining - deltaTime);
            float normalizedTime = 1f - s_TimeRemaining / s_Duration;
            float curveStrength = s_PulseCurve != null
                ? Mathf.Max(0f, s_PulseCurve.Evaluate(normalizedTime))
                : Mathf.Sin(normalizedTime * Mathf.PI);
            // Always begin at full strength, then allow the inspector curve to
            // shape the rest of the fade. This makes a single key press obvious.
            float pulseStrength = Mathf.Max(1f - normalizedTime, curveStrength);
            s_LensDistortion.active = true;
            s_LensDistortion.center.value = s_ActiveCenter;
            s_LensDistortion.intensity.value = s_PeakIntensity * pulseStrength;
        }

        public static void EnsureDriver(Camera camera)
        {
            if (camera != null && camera.GetComponent<CameraResizePulseDriver>() == null)
            {
                camera.gameObject.AddComponent<CameraResizePulseDriver>();
            }
        }

        private static void EnsureVolume(Camera camera)
        {
            var cameraData = camera.GetComponent<UniversalAdditionalCameraData>();
            if (s_Volume == null)
            {
                // The previous scene may have destroyed the Volume while leaving
                // its runtime profile object alive. Never animate that stale copy.
                s_BoundVolume = null;
                s_LensDistortion = null;

                var sharedProfile = Resources.Load<VolumeProfile>(k_VolumeProfileResource);
                if (sharedProfile == null)
                {
                    Debug.LogError($"Missing Resources/{k_VolumeProfileResource}.asset. " +
                                   "The self-resize camera pulse cannot be rendered.");
                    return;
                }

                // Prefer a scene Volume already assigned to the dedicated profile.
                // This keeps the override visible and editable on MainMenu/Global
                // Volume while still allowing a fallback for direct-to-map clients.
                int volumeLayerMask = cameraData != null
                    ? cameraData.volumeLayerMask.value
                    : ~0;
                Volume gameplayVolume = null;
                foreach (var sceneVolume in Object.FindObjectsByType<Volume>())
                {
                    int volumeLayer = 1 << sceneVolume.gameObject.layer;
                    if (!sceneVolume.isGlobal || !sceneVolume.enabled ||
                        sceneVolume.sharedProfile == null ||
                        (volumeLayerMask & volumeLayer) == 0)
                    {
                        continue;
                    }

                    if (sceneVolume.sharedProfile == sharedProfile)
                    {
                        s_Volume = sceneVolume;
                        break;
                    }

                    if (gameplayVolume == null &&
                        sceneVolume.sharedProfile.TryGet<LensDistortion>(out _))
                    {
                        gameplayVolume = sceneVolume;
                    }
                }

                s_Volume ??= gameplayVolume;
                if (s_Volume != null)
                {
                    s_Volume.priority = k_VolumePriority;
                }

                // Every client process owns its own camera and post-processing stack.
                // Keep this dedicated global volume alive across map loads so maps do
                // not need to contain or configure a resize-pulse volume themselves.
                if (s_Volume == null)
                {
                    var volumeObject = new GameObject("[Client] Self Resize Global Volume");
                    volumeObject.SetActive(false);
                    volumeObject.layer = GetFirstLayerInMask(volumeLayerMask);
                    Object.DontDestroyOnLoad(volumeObject);

                    s_Volume = volumeObject.AddComponent<Volume>();
                    s_Volume.isGlobal = true;
                    s_Volume.priority = k_VolumePriority;
                    s_Volume.weight = 1f;
                    s_Volume.sharedProfile = sharedProfile;
                    volumeObject.SetActive(true);
                }
            }

            if (s_Volume != s_BoundVolume)
            {
                s_BoundVolume = s_Volume;
                s_LensDistortion = null;
            }

            if (s_Volume != null && s_LensDistortion == null)
            {
                // Volume.profile gives us a runtime copy of the assigned global
                // profile. Pulsing it will never dirty the profile asset in Edit Mode.
                var runtimeProfile = s_Volume.profile;
                if (!runtimeProfile.TryGet(out s_LensDistortion))
                {
                    s_LensDistortion = runtimeProfile.Add<LensDistortion>(true);
                }

                s_LensDistortion.intensity.overrideState = true;
                s_LensDistortion.intensity.value = 0f;
                s_LensDistortion.active = false;
            }

            if (cameraData != null)
            {
                cameraData.renderPostProcessing = true;
            }
        }

        private static int GetFirstLayerInMask(int layerMask)
        {
            for (int layer = 0; layer < 32; layer++)
            {
                if ((layerMask & (1 << layer)) != 0)
                {
                    return layer;
                }
            }

            return 0;
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
                CameraResizePulseEffect.EnsureDriver(MainCameraSingleton.Instance);
            }
        }
    }
}
