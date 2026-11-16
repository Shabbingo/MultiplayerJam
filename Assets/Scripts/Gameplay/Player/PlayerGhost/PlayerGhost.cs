using System;
using System.Collections.Generic;
using Unity.Cinemachine;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Serialization;
using static FirstPersonController;

namespace Unity.MP_FPS
{
    public partial class PlayerGhost : GhostMonoBehaviour, IUpdateClient, IUpdateServer
    {
        [field: SerializeField] public AssetReferenceGameObject ProjectilePrefabAR { get; private set; }
        [SerializeField] private Vector3 m_CameraRotation;
        [SerializeField] private GameObject m_OwnerVisuals;
        [SerializeField] private GameObject m_OtherPlayerVisuals;
        [SerializeField] private SoundDef m_SpawnSFX;
        [SerializeField, Range(0.1f, 1f)] private float m_MinimumPlayerScale = 0.5f;
        public float MinimumPlayerScale => m_MinimumPlayerScale;
        [SerializeField, Min(1f)] private float m_MaximumPlayerScale = 2f;
        public float MaximumPlayerScale => m_MaximumPlayerScale;

        [Header("Reflect Shield Glow")]
        [SerializeField] private Color m_ReflectGlowColour = new(0.2f, 0.75f, 1f, 1f);
        [SerializeField, Min(0f)] private float m_ReflectGlowIntensity = 4f;
        [SerializeField, Min(0.1f)] private float m_ReflectGlowRange = 5f;
        [SerializeField] private Color m_ReflectShieldColour = new(0.12f, 0.68f, 1f, 0.42f);
        [SerializeField] private Color m_NullFieldShieldColour = new(0.62f, 0.22f, 1f, 0.5f);
        [SerializeField] private Color m_SecondChanceGlowColour = new(1f, 0.68f, 0.08f, 1f);
        [Header("Overshield Silhouette")]
        [SerializeField] private Color m_GraceShieldColour = new(1f, 0.68f, 0.08f, 0.68f);
        [SerializeField, Range(0.001f, 0.08f)]
        private float m_GraceShieldShellThickness = 0.018f;
        [SerializeField] private Vector3 m_ReflectShieldOffset = new(0f, 1f, 0f);
        [SerializeField] private Vector3 m_ReflectShieldScale = new(1.5f, 2.4f, 1.5f);
        [field: SerializeField] public Transform CameraTarget { get; private set; }
        [field: SerializeField] public Transform ReticlePoint { get; private set; }
        [field: SerializeField] public Transform ShotOrigin { get; private set; }
        [field: FormerlySerializedAs("<VisualShotOrigin>k__BackingField")] [field: SerializeField] 
        public Transform VisualShotOrigin1P { get; private set; }
        [field: SerializeField] public Transform VisualShotOrigin3P { get; private set; }

        [Header("First-Person Wizard Hands")]
        [SerializeField, Tooltip("Hides the gun meshes while retaining the first- and third-person arms.")]
        private bool m_HideWeaponVisuals = true;
        [SerializeField, Tooltip("Move this transform in the player prefab to set where left-click projectiles and muzzle effects originate.")]
        private Transform m_LeftHandProjectileOrigin;
        [SerializeField, Tooltip("Move this transform in the player prefab to set where right-click projectiles and muzzle effects originate.")]
        private Transform m_RightHandProjectileOrigin;
        [SerializeField, Tooltip("Fallback camera-local origin used when no left-hand projectile origin is assigned.")]
        private Vector3 m_LeftHandShotOffset = new(-0.39f, -0.47f, 0.35f);
        [SerializeField, Tooltip("Fallback camera-local origin used when no right-hand projectile origin is assigned.")]
        private Vector3 m_RightHandShotOffset = new(0.39f, -0.47f, 0.35f);
        [SerializeField, Min(0f),
         Tooltip("Minimum guided-projectile origin height above the player's feet. Keep this above the projectile's 0.2 radius so tiny players do not spawn it in the floor.")]
        private float m_MinimumGuidedProjectileSpawnHeight = 0.25f;

        [Header("Manual Aiming Setup")]
        [SerializeField] private Animator m_Animator3P;
        private static readonly int AimPitchHash = Animator.StringToHash("AimPitch");
        
        public int PlayerIndex { get; private set; }
        public int InputUserId { get; set; } = -1;
        public PlayerInput ServerMovementInput { get; set; }
        public ControllerConsts ControllerConsts { get; private set; }

        #region Cached Player Components
        private FirstPersonController m_Controller;
        public FirstPersonController Controller => m_Controller;

        #endregion

        [field: SerializeField] public GameObject MainCameraPrefab { get; private set; }

        private Camera m_PlayerCamera;
        private Animator _animatorCharacter;
        private Vector3 m_InitialScale;
        private float m_ManualScaleMultiplier = 1f;
        private Vector3 m_ReticleVector;
        private Transform m_LeftHandShotOrigin;
        private Transform m_RightHandShotOrigin;
        private Transform m_ThirdPersonLeftHandShotOrigin;
        private Transform m_ThirdPersonRightHandShotOrigin;
        private Light m_ReflectGlow;
        private MeshRenderer m_ReflectShieldRenderer;
        private Material m_ReflectShieldMaterial;
        private Material m_GraceShieldMaterial;
        private readonly List<OvershieldRendererPair> m_GraceShieldRenderers = new();
        private readonly List<MirrorSnapshot> m_MirrorHistory = new();
        private readonly List<MirrorDecoy> m_MirrorDecoys = new();
        private bool m_WasReflectShieldActive;
        private float m_LastMirrorSnapshotTime = float.MinValue;

        private struct MirrorSnapshot
        {
            public float Time;
            public Vector3 Position;
            public Quaternion Rotation;
            public Vector3 Scale;
        }

        private sealed class MirrorDecoy
        {
            public GameObject Instance;
            public float MovementDelay;
            public float ExpiresAt;
        }

        private sealed class OvershieldRendererPair
        {
            public Renderer Source;
            public Renderer Shell;
        }

        private CinemachineTargetGroup m_TargetGroup;
        private CinemachinePositionComposer m_PositionComposer;
        private CinemachineCamera m_CinemachineCamera;

        private static readonly int s_BaseColorProperty = Shader.PropertyToID("_BaseColor");
        private static readonly int s_ColorProperty = Shader.PropertyToID("_Color");
        private const float GoldenRatioConjugate = 0.61803398875f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Init()
        {
            s_NextPredictionId = 1;
        }
        
        private static uint s_NextPredictionId = 1;

        public static uint GetNextPredictionID()
        {
            return s_NextPredictionId++;
        }

        public Camera GetPlayerCamera()
        {
            return m_PlayerCamera;
        }

        public CinemachineCamera GetPlayerCinemachineCamera()
        {
            return m_CinemachineCamera;
        }

        public CinemachinePositionComposer GetPositionComposer()
        {
            return m_PositionComposer;
        }

        public struct PlayerData : IComponentData
        {
            [GhostField] public FixedString128Bytes Name;
            public Entity ViewEntity;
            public Entity ControlledEntity;
        }

        public void Awake()
        {
            GetRequiredComponent(out m_Controller);
            m_ReticleVector = ReticlePoint.localPosition;
            m_InitialScale = transform.localScale;
            m_LeftHandShotOrigin = CreateHandShotOrigin("LeftHandShotOrigin",
                GetHandShotOffset(WeaponManager.PrimaryWeaponId));
            m_RightHandShotOrigin = CreateHandShotOrigin("RightHandShotOrigin",
                GetHandShotOffset(WeaponManager.SecondaryWeaponId));
        }

        private Transform CreateHandShotOrigin(string originName, Vector3 localPosition)
        {
            if (CameraTarget == null)
            {
                return null;
            }

            var origin = new GameObject(originName).transform;
            origin.SetParent(CameraTarget, false);
            origin.localPosition = localPosition;
            origin.localRotation = Quaternion.identity;
            return origin;
        }

        public Vector3 GetHandShotOffset(uint weaponId)
        {
            bool useRightHand = weaponId == WeaponManager.SecondaryWeaponId;
            Transform authoredOrigin = useRightHand
                ? m_RightHandProjectileOrigin
                : m_LeftHandProjectileOrigin;
            if (authoredOrigin != null)
            {
                return CameraTarget != null
                    ? CameraTarget.InverseTransformPoint(authoredOrigin.position)
                    : authoredOrigin.localPosition;
            }

            return useRightHand ? m_RightHandShotOffset : m_LeftHandShotOffset;
        }

        public Vector3 GetScaledHandShotOffset(uint weaponId)
        {
            float playerScale = Mathf.Max(0.01f, Mathf.Abs(transform.lossyScale.x));
            return GetHandShotOffset(weaponId) * playerScale;
        }

        public Vector3 ApplyShotOriginClearance(Vector3 shotOrigin, uint weaponId)
        {
            // Both hands now fire physical projectiles. Keep either hand's muzzle
            // above the collision floor, especially while the player is small.
            float minimumY = transform.position.y + m_MinimumGuidedProjectileSpawnHeight;
            shotOrigin.y = Mathf.Max(shotOrigin.y, minimumY);
            return shotOrigin;
        }

        public Transform GetVisualShotOrigin(uint weaponId, bool isFirstPerson)
        {
            if (!isFirstPerson)
            {
                bool useThirdPersonRightHand = weaponId == WeaponManager.SecondaryWeaponId;
                var thirdPersonOrigin = useThirdPersonRightHand
                    ? m_ThirdPersonRightHandShotOrigin
                    : m_ThirdPersonLeftHandShotOrigin;
                return thirdPersonOrigin != null ? thirdPersonOrigin : VisualShotOrigin3P;
            }

            bool useRightHand = weaponId == WeaponManager.SecondaryWeaponId;
            var origin = useRightHand ? m_RightHandShotOrigin : m_LeftHandShotOrigin;
            if (origin != null)
            {
                // Keep projectile-origin marker changes live while playing.
                origin.localPosition = GetHandShotOffset(weaponId);
                origin.position = ApplyShotOriginClearance(origin.position, weaponId);
                return origin;
            }

            return VisualShotOrigin1P;
        }

        private void OnDrawGizmosSelected()
        {
            DrawProjectileOriginGizmo(m_LeftHandProjectileOrigin,
                new Color(0.2f, 0.8f, 1f, 1f));
            DrawProjectileOriginGizmo(m_RightHandProjectileOrigin,
                new Color(1f, 0.55f, 0.15f, 1f));
        }

        private static void DrawProjectileOriginGizmo(Transform origin, Color colour)
        {
            if (origin == null) return;
            Gizmos.color = colour;
            Gizmos.DrawSphere(origin.position, 0.035f);
            Gizmos.DrawLine(origin.position, origin.position + origin.forward * 0.3f);
        }
        
        private void LateUpdate()
        {
            // This logic is for visual clients only
            if ((Role != MultiplayerRole.ClientProxy && Role != MultiplayerRole.ClientOwned) || m_Animator3P == null)
            {
                return;
            }

            var predictedPlayerGhost = ReadGhostComponentData<PredictedPlayerGhost>();
            var controllerState = predictedPlayerGhost.ControllerState;
            
            // matches vertical angle value in ClientInputReaderSystem
            float normalizedPitch = controllerState.PitchDegrees / 85f;
            
            m_Animator3P.SetFloat(AimPitchHash, normalizedPitch);
        }

        public override void OnGhostLinked()
        {
            bool isClientOwned = (Role == MultiplayerRole.ClientOwned);
            m_OwnerVisuals.SetActive(isClientOwned);
            m_OtherPlayerVisuals.SetActive(!isClientOwned);

            if (Role != MultiplayerRole.Server)
            {
                ConfigureWizardVisuals();
                CreateReflectGlow();
                CreateGraceShieldSilhouette();
            }

            if (Role != MultiplayerRole.Server &&
                TryReadGhostComponentData<GhostOwner>(out var ghostOwner))
            {
                ApplyPlayerColor(GetPlayerColor(ghostOwner.NetworkId));
            }

            if (Role != MultiplayerRole.Server)
            {
                _animatorCharacter = GetComponent<Animator>();
                // spawn SFX
                if (m_SpawnSFX != null)
                {
                    GameManager.Instance.SoundSystem.CreateEmitter(m_SpawnSFX, transform.position);
                }
            }

            if (isClientOwned)
            {
                var predictedPlayer = ReadGhostComponentData<PredictedPlayerGhost>();
                PlayerIndex = predictedPlayer.InputIndex;
                // create camera
                CreateClientCamera();

                // Add AudioListener to client position and ensure all other AudioListeners are disabled
                var audioListeners = Resources.FindObjectsOfTypeAll<AudioListener>();
                foreach (var a in audioListeners)
                {
                    a.enabled = false;
                }
                m_OwnerVisuals.AddComponent<AudioListener>();
                
                // Attach the listener to the player model rather than the camera
                GameManager.Instance.SoundSystem.SetListenerTransform(m_OwnerVisuals.transform);    
            }
            else if (Role == MultiplayerRole.ClientProxy)
            {
                // no physics required
                Controller.CharacterController.enabled = false;
            }

            gameObject.layer = (Role == MultiplayerRole.Server)
                ? (int)LayerIndex.ServerPlayer
                : (int)LayerIndex.ClientPlayer;

            // Host mode creates authoritative server and predicted client copies
            // in the same Unity physics scene. They occupy the same position and
            // must not collide with one another.
            if (Role == MultiplayerRole.Server)
            {
                Controller.SetExcludeLayers(LayerMask.GetMask("ClientPlayer"));
            }
            else if (Role == MultiplayerRole.ClientOwned)
            {
                Controller.SetExcludeLayers(LayerMask.GetMask("ServerPlayer"));
            }

            PlayerGhostManager.TryGetInstanceByRole(Role, out var playerManager);
            playerManager.Register(this);
        }

        private void ConfigureWizardVisuals()
        {
            m_ThirdPersonLeftHandShotOrigin = FindDescendant(
                m_OtherPlayerVisuals, "Left_Hand_End");
            m_ThirdPersonRightHandShotOrigin = FindDescendant(
                m_OtherPlayerVisuals, "Right_Hand_End");

            if (!m_HideWeaponVisuals)
            {
                return;
            }

            HideWeaponRoots(m_OwnerVisuals);
            HideWeaponRoots(m_OtherPlayerVisuals);
        }

        private void CreateReflectGlow()
        {
            var glowObject = new GameObject("Reflect Shield Glow");
            glowObject.transform.SetParent(transform, false);
            glowObject.transform.localPosition = Vector3.up;
            m_ReflectGlow = glowObject.AddComponent<Light>();
            m_ReflectGlow.type = LightType.Point;
            m_ReflectGlow.color = m_ReflectGlowColour;
            m_ReflectGlow.intensity = m_ReflectGlowIntensity;
            m_ReflectGlow.range = m_ReflectGlowRange;
            m_ReflectGlow.shadows = LightShadows.None;
            m_ReflectGlow.enabled = false;

            Shader shieldShader = Shader.Find("MultiplayerJam/Reflect Energy Shield");
            if (shieldShader == null)
            {
                Debug.LogWarning("Reflect energy shield shader could not be found.");
                return;
            }

            var shieldObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            shieldObject.name = "Reflect Energy Shield";
            shieldObject.transform.SetParent(transform, false);
            shieldObject.transform.localPosition = m_ReflectShieldOffset;
            shieldObject.transform.localScale = m_ReflectShieldScale;

            var shieldCollider = shieldObject.GetComponent<Collider>();
            if (shieldCollider != null)
            {
                shieldCollider.enabled = false;
                Destroy(shieldCollider);
            }

            m_ReflectShieldRenderer = shieldObject.GetComponent<MeshRenderer>();
            m_ReflectShieldRenderer.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;
            m_ReflectShieldRenderer.receiveShadows = false;
            m_ReflectShieldMaterial = new Material(shieldShader)
            {
                name = "Runtime Reflect Energy Shield Material"
            };
            m_ReflectShieldMaterial.SetColor("_ShieldColor", m_ReflectShieldColour);
            m_ReflectShieldRenderer.sharedMaterial = m_ReflectShieldMaterial;
            m_ReflectShieldRenderer.enabled = false;
        }

        private void CreateGraceShieldSilhouette()
        {
            Shader shieldShader = Shader.Find("MultiplayerJam/Reflect Energy Shield");
            if (shieldShader == null)
            {
                Debug.LogWarning("Overshield silhouette shader could not be found.");
                return;
            }

            m_GraceShieldMaterial = new Material(shieldShader)
            {
                name = "Runtime Yellow Overshield Silhouette Material"
            };
            m_GraceShieldMaterial.SetColor("_ShieldColor", m_GraceShieldColour);
            m_GraceShieldMaterial.SetFloat("_RimPower", 1.65f);
            m_GraceShieldMaterial.SetFloat("_PulseSpeed", 5f);
            m_GraceShieldMaterial.SetFloat("_WaveScale", 5f);
            m_GraceShieldMaterial.SetFloat("_ShellThickness",
                m_GraceShieldShellThickness);

            CreateGraceShieldSilhouette(m_OwnerVisuals);
            CreateGraceShieldSilhouette(m_OtherPlayerVisuals);
        }

        private void CreateGraceShieldSilhouette(GameObject visualRoot)
        {
            if (visualRoot == null || m_GraceShieldMaterial == null)
            {
                return;
            }

            // Take a snapshot before adding shells so they cannot be cloned again.
            Renderer[] sourceRenderers =
                visualRoot.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer sourceRenderer in sourceRenderers)
            {
                Renderer shellRenderer = sourceRenderer switch
                {
                    SkinnedMeshRenderer skinnedRenderer =>
                        CreateSkinnedOvershieldShell(skinnedRenderer),
                    MeshRenderer meshRenderer =>
                        CreateMeshOvershieldShell(meshRenderer),
                    _ => null
                };

                if (shellRenderer == null)
                {
                    continue;
                }

                ConfigureOvershieldShell(sourceRenderer, shellRenderer);
                m_GraceShieldRenderers.Add(new OvershieldRendererPair
                {
                    Source = sourceRenderer,
                    Shell = shellRenderer
                });
            }
        }

        private SkinnedMeshRenderer CreateSkinnedOvershieldShell(
            SkinnedMeshRenderer source)
        {
            if (source.sharedMesh == null)
            {
                return null;
            }

            GameObject shellObject = CreateOvershieldShellObject(source);
            var shell = shellObject.AddComponent<SkinnedMeshRenderer>();
            shell.sharedMesh = source.sharedMesh;
            shell.rootBone = source.rootBone;
            shell.bones = source.bones;
            shell.quality = source.quality;
            shell.updateWhenOffscreen = source.updateWhenOffscreen;
            Bounds shellBounds = source.localBounds;
            shellBounds.Expand(m_GraceShieldShellThickness * 2f + 0.04f);
            shell.localBounds = shellBounds;
            return shell;
        }

        private MeshRenderer CreateMeshOvershieldShell(MeshRenderer source)
        {
            if (!source.TryGetComponent(out MeshFilter sourceFilter) ||
                sourceFilter.sharedMesh == null)
            {
                return null;
            }

            GameObject shellObject = CreateOvershieldShellObject(source);
            var shellFilter = shellObject.AddComponent<MeshFilter>();
            shellFilter.sharedMesh = sourceFilter.sharedMesh;
            return shellObject.AddComponent<MeshRenderer>();
        }

        private static GameObject CreateOvershieldShellObject(Renderer source)
        {
            var shellObject = new GameObject(
                $"{source.gameObject.name} Overshield Silhouette");
            shellObject.layer = source.gameObject.layer;
            shellObject.transform.SetParent(source.transform, false);
            shellObject.transform.localPosition = Vector3.zero;
            shellObject.transform.localRotation = Quaternion.identity;
            shellObject.transform.localScale = Vector3.one;
            return shellObject;
        }

        private void ConfigureOvershieldShell(Renderer source, Renderer shell)
        {
            int materialCount = Mathf.Max(1, source.sharedMaterials.Length);
            var shellMaterials = new Material[materialCount];
            for (int i = 0; i < shellMaterials.Length; i++)
            {
                shellMaterials[i] = m_GraceShieldMaterial;
            }

            shell.sharedMaterials = shellMaterials;
            shell.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;
            shell.receiveShadows = false;
            shell.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            shell.reflectionProbeUsage =
                UnityEngine.Rendering.ReflectionProbeUsage.Off;
            shell.allowOcclusionWhenDynamic = false;
            shell.sortingLayerID = source.sortingLayerID;
            shell.sortingOrder = source.sortingOrder + 1;
            shell.enabled = false;
        }

        private void UpdateGraceShieldSilhouette(bool active,
            float shieldAmount, float shieldMaximum)
        {
            if (m_GraceShieldMaterial != null)
            {
                float strength = shieldMaximum > 0f
                    ? Mathf.Clamp01(shieldAmount / shieldMaximum)
                    : 1f;
                Color colour = m_GraceShieldColour;
                colour.a *= Mathf.Lerp(0.72f, 1f, strength);
                m_GraceShieldMaterial.SetColor("_ShieldColor", colour);
            }

            foreach (OvershieldRendererPair pair in m_GraceShieldRenderers)
            {
                if (pair.Shell == null)
                {
                    continue;
                }

                pair.Shell.enabled = active && pair.Source != null &&
                                     pair.Source.enabled &&
                                     pair.Source.gameObject.activeInHierarchy;
            }
        }

        private static Transform FindDescendant(GameObject root, string childName)
        {
            if (root == null)
            {
                return null;
            }

            foreach (var child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == childName)
                {
                    return child;
                }
            }

            return null;
        }

        private static void HideWeaponRoots(GameObject visualRoot)
        {
            if (visualRoot == null)
            {
                return;
            }

            // Keep the arm and hand meshes, hiding only the weapon roots.
            foreach (var child in visualRoot.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == "Robot_Weapon_A" || child.name == "Pfb_assaultRifle")
                {
                    child.gameObject.SetActive(false);
                }
            }
        }

        private static Color GetPlayerColor(int networkId)
        {
            // A golden-ratio hue step remains deterministic on every client while
            // keeping adjacent network IDs visually distinct without a fixed palette limit.
            float hue = Mathf.Repeat((Mathf.Max(1, networkId) - 1) *
                                     GoldenRatioConjugate, 1f);
            return Color.HSVToRGB(hue, 0.7f, 1f);
        }

        private void ApplyPlayerColor(Color playerColor)
        {
            ApplyPlayerColor(m_OwnerVisuals, playerColor);
            ApplyPlayerColor(m_OtherPlayerVisuals, playerColor);
        }

        private static void ApplyPlayerColor(GameObject visualRoot, Color playerColor)
        {
            if (visualRoot == null)
            {
                return;
            }

            var propertyBlock = new MaterialPropertyBlock();
            foreach (var visualRenderer in
                     visualRoot.GetComponentsInChildren<Renderer>(true))
            {
                var materials = visualRenderer.sharedMaterials;
                for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                {
                    var material = materials[materialIndex];
                    if (material == null ||
                        !material.name.StartsWith("M_Armature_", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    visualRenderer.GetPropertyBlock(propertyBlock, materialIndex);
                    if (material.HasProperty(s_BaseColorProperty))
                    {
                        propertyBlock.SetColor(s_BaseColorProperty, playerColor);
                    }
                    if (material.HasProperty(s_ColorProperty))
                    {
                        propertyBlock.SetColor(s_ColorProperty, playerColor);
                    }
                    visualRenderer.SetPropertyBlock(propertyBlock, materialIndex);
                    propertyBlock.Clear();
                }
            }
        }

        public override void OnGhostPreDestroy()
        {
            ClearMirrorDecoys();
            if (m_ReflectShieldMaterial != null)
            {
                Destroy(m_ReflectShieldMaterial);
            }
            if (m_GraceShieldMaterial != null)
            {
                Destroy(m_GraceShieldMaterial);
            }

            if (PlayerGhostManager.TryGetInstanceByRole(Role, out var playerManager))
            {
                playerManager.Unregister(this);
            }
        }

        void AttachPlayerViewCamera()
        {
            var playerCamera = GameObject.FindAnyObjectByType<Camera>();
            if (playerCamera != null)
            {
                playerCamera.transform.parent = transform.Find("ViewPoint");
                playerCamera.transform.localPosition = Vector3.zero;
                playerCamera.transform.localRotation = Quaternion.identity;
            }
        }

        private void CreateClientCamera()
        {
            // Disable the persistent menu camera specifically. An arbitrary camera
            // lookup can select and disable the active spectator camera while the
            // next-round player ghost is being created.
            var existingCamera = MainCameraSingleton.Instance;
            if (existingCamera != null)
            {
                existingCamera.enabled = false;
            }

            // spawn the camera
            var mainCameraInstance = Instantiate(MainCameraPrefab, CameraTarget.transform);
            mainCameraInstance.transform.localPosition = Vector3.zero;
            mainCameraInstance.name = $"MainCamera_{PlayerIndex}";

            m_PlayerCamera = mainCameraInstance.GetComponent<Camera>();

            var audioListener = mainCameraInstance.GetComponent<AudioListener>();
            if (audioListener != null)
            {
                GameManager.Instance.SoundSystem.SetListenerTransform(audioListener.transform);
            }

            Utils.SetCursorVisible(false);
        }

        public void UpdateServer(float deltaTime)
        {
            ApplyPlayerScale(deltaTime);
        }

        public void UpdateClient(float deltaTime)
        {
            ApplyPlayerScale(deltaTime);

            var predictedPlayerGhost = ReadGhostComponentData<PredictedPlayerGhost>();
            bool reflectShieldActive = predictedPlayerGhost.ReflectShieldTimeRemaining > 0f;
            bool graceShieldActive = predictedPlayerGhost.GraceShieldAmount > 0f;
            bool secondChanceProtected = !graceShieldActive &&
                predictedPlayerGhost.SecondChanceInvulnerabilityTimeRemaining > 0f;
            UpdateGraceShieldSilhouette(graceShieldActive,
                predictedPlayerGhost.GraceShieldAmount,
                predictedPlayerGhost.GraceShieldMaximum);
            if (m_ReflectGlow != null)
            {
                m_ReflectGlow.enabled = reflectShieldActive || secondChanceProtected;
                m_ReflectGlow.color = secondChanceProtected
                    ? m_SecondChanceGlowColour
                    : m_ReflectGlowColour;
                if (m_ReflectGlow.enabled)
                {
                    m_ReflectGlow.intensity = m_ReflectGlowIntensity *
                        (0.85f + 0.15f * Mathf.Sin(Time.time * 12f));
                }
            }

            if (m_ReflectShieldRenderer != null)
            {
                m_ReflectShieldRenderer.enabled = reflectShieldActive;
            }

            RecordMirrorSnapshot();
            Cards.PlayerModifierStacks modifiers =
                TryReadGhostComponentData<Cards.PlayerModifierStacks>(
                    out var replicatedModifiers)
                    ? replicatedModifiers
                    : default;
            if (m_ReflectShieldMaterial != null)
            {
                m_ReflectShieldMaterial.SetColor("_ShieldColor",
                    modifiers.NullField > 0
                        ? m_NullFieldShieldColour
                        : m_ReflectShieldColour);
            }
            if (reflectShieldActive && !m_WasReflectShieldActive &&
                modifiers.MirrorImage > 0)
            {
                SpawnMirrorDecoys(modifiers.MirrorImage);
            }
            UpdateMirrorDecoys();
            m_WasReflectShieldActive = reflectShieldActive;

            var controllerState = predictedPlayerGhost.ControllerState;
            if (Role == MultiplayerRole.ClientOwned)
            {
                CameraTarget.transform.rotation = Quaternion.Euler(controllerState.PitchDegrees,
                    Camera.main.transform.rotation.eulerAngles.y,
                    Camera.main.transform.rotation.eulerAngles.z);
            }

            var rot = Quaternion.Euler(controllerState.PitchDegrees, 0.0f, 0.0f);
            ReticlePoint.localPosition = rot * m_ReticleVector;

            //TODO: The following is a temporary fix for animation root moves (Robot Jump for example)
            m_OtherPlayerVisuals.transform.localPosition = Vector3.zero;
            m_OtherPlayerVisuals.transform.localRotation = Quaternion.identity;
        }

        private void RecordMirrorSnapshot()
        {
            float now = Time.time;
            if (now - m_LastMirrorSnapshotTime < 0.025f ||
                m_OtherPlayerVisuals == null)
            {
                return;
            }

            m_LastMirrorSnapshotTime = now;
            Transform visualTransform = m_OtherPlayerVisuals.transform;
            m_MirrorHistory.Add(new MirrorSnapshot
            {
                Time = now,
                Position = visualTransform.position,
                Rotation = visualTransform.rotation,
                Scale = visualTransform.lossyScale
            });

            const float historyDuration = 3f;
            float oldestAllowed = now - historyDuration;
            int removeCount = 0;
            while (removeCount < m_MirrorHistory.Count - 1 &&
                   m_MirrorHistory[removeCount].Time < oldestAllowed)
            {
                removeCount++;
            }
            if (removeCount > 0)
            {
                m_MirrorHistory.RemoveRange(0, removeCount);
            }
        }

        private void SpawnMirrorDecoys(int stackCount)
        {
            if (m_OtherPlayerVisuals == null || stackCount <= 0)
            {
                return;
            }

            var card = WeaponManager.Instance?.CardModifierCatalog?
                .FindByKind(Cards.CardModifierKind.MirrorImage);
            float lifetime = Mathf.Max(0.1f,
                card?.EffectDurationSeconds ?? 2f);
            float maximumDelay = Mathf.Min(2.4f,
                0.16f * Mathf.Max(1, stackCount));
            float now = Time.time;
            for (int i = 0; i < stackCount; i++)
            {
                var clone = Instantiate(m_OtherPlayerVisuals);
                clone.name = $"Mirror Image {i + 1}";
                clone.transform.SetParent(null, true);
                PrepareMirrorVisual(clone, i, stackCount);
                clone.SetActive(true);

                float delay = stackCount == 1
                    ? 0.18f
                    : Mathf.Lerp(0.12f, maximumDelay,
                        i / (float)(stackCount - 1));
                ApplyMirrorSnapshot(clone.transform, now - delay);
                m_MirrorDecoys.Add(new MirrorDecoy
                {
                    Instance = clone,
                    MovementDelay = delay,
                    ExpiresAt = now + lifetime
                });
            }
        }

        private static void PrepareMirrorVisual(GameObject clone, int index,
            int count)
        {
            foreach (var behaviour in clone.GetComponentsInChildren<MonoBehaviour>(true))
            {
                behaviour.enabled = false;
            }
            foreach (var collider in clone.GetComponentsInChildren<Collider>(true))
            {
                collider.enabled = false;
            }
            foreach (var audioSource in clone.GetComponentsInChildren<AudioSource>(true))
            {
                audioSource.enabled = false;
            }

            float hue = Mathf.Repeat(0.5f + index /
                (float)Mathf.Max(1, count) * 0.18f, 1f);
            Color tint = Color.HSVToRGB(hue, 0.65f, 1f);
            tint.a = 0.5f;
            var propertyBlock = new MaterialPropertyBlock();
            foreach (var visualRenderer in clone.GetComponentsInChildren<Renderer>(true))
            {
                visualRenderer.shadowCastingMode =
                    UnityEngine.Rendering.ShadowCastingMode.Off;
                visualRenderer.receiveShadows = false;
                var materials = visualRenderer.sharedMaterials;
                for (int materialIndex = 0; materialIndex < materials.Length;
                     materialIndex++)
                {
                    Material material = materials[materialIndex];
                    if (material == null) continue;
                    visualRenderer.GetPropertyBlock(propertyBlock, materialIndex);
                    if (material.HasProperty(s_BaseColorProperty))
                    {
                        propertyBlock.SetColor(s_BaseColorProperty, tint);
                    }
                    if (material.HasProperty(s_ColorProperty))
                    {
                        propertyBlock.SetColor(s_ColorProperty, tint);
                    }
                    visualRenderer.SetPropertyBlock(propertyBlock, materialIndex);
                    propertyBlock.Clear();
                }
            }
        }

        private void UpdateMirrorDecoys()
        {
            float now = Time.time;
            for (int i = m_MirrorDecoys.Count - 1; i >= 0; i--)
            {
                MirrorDecoy decoy = m_MirrorDecoys[i];
                if (decoy.Instance == null || now >= decoy.ExpiresAt)
                {
                    if (decoy.Instance != null)
                    {
                        Destroy(decoy.Instance);
                    }
                    m_MirrorDecoys.RemoveAt(i);
                    continue;
                }

                ApplyMirrorSnapshot(decoy.Instance.transform,
                    now - decoy.MovementDelay);
            }
        }

        private void ApplyMirrorSnapshot(Transform target, float sampleTime)
        {
            if (target == null || m_MirrorHistory.Count == 0)
            {
                return;
            }

            int newerIndex = 0;
            while (newerIndex < m_MirrorHistory.Count &&
                   m_MirrorHistory[newerIndex].Time < sampleTime)
            {
                newerIndex++;
            }

            if (newerIndex <= 0)
            {
                ApplyMirrorSnapshot(target, m_MirrorHistory[0]);
                return;
            }
            if (newerIndex >= m_MirrorHistory.Count)
            {
                ApplyMirrorSnapshot(target,
                    m_MirrorHistory[m_MirrorHistory.Count - 1]);
                return;
            }

            MirrorSnapshot older = m_MirrorHistory[newerIndex - 1];
            MirrorSnapshot newer = m_MirrorHistory[newerIndex];
            float t = Mathf.InverseLerp(older.Time, newer.Time, sampleTime);
            target.SetPositionAndRotation(
                Vector3.Lerp(older.Position, newer.Position, t),
                Quaternion.Slerp(older.Rotation, newer.Rotation, t));
            target.localScale = Vector3.Lerp(older.Scale, newer.Scale, t);
        }

        private static void ApplyMirrorSnapshot(Transform target,
            in MirrorSnapshot snapshot)
        {
            target.SetPositionAndRotation(snapshot.Position, snapshot.Rotation);
            target.localScale = snapshot.Scale;
        }

        private void ClearMirrorDecoys()
        {
            foreach (MirrorDecoy decoy in m_MirrorDecoys)
            {
                if (decoy.Instance != null)
                {
                    Destroy(decoy.Instance);
                }
            }
            m_MirrorDecoys.Clear();
            m_MirrorHistory.Clear();
        }

        private void ApplyPlayerScale(float deltaTime)
        {
            var predictedPlayerGhost = ReadGhostComponentData<PredictedPlayerGhost>();
            var targetScaleMultiplier = GetTargetScaleMultiplier(in predictedPlayerGhost);
            m_ManualScaleMultiplier = targetScaleMultiplier;

            var targetScale = m_InitialScale * m_ManualScaleMultiplier;

            // Avoid rebuilding the CharacterController's physics shape every frame.
            if ((transform.localScale - targetScale).sqrMagnitude > 0.000001f)
            {
                transform.localScale = targetScale;
            }
        }

        public float GetTargetScaleMultiplier(in PredictedPlayerGhost predictedPlayerGhost)
        {
            return predictedPlayerGhost.SizeState switch
            {
                FirstPersonController.SmallSizeState => m_MinimumPlayerScale,
                FirstPersonController.LargeSizeState => m_MaximumPlayerScale,
                _ => 1f
            };
        }

        public bool SetPlayerPositionFromRPC(float3 rpcPosition, float positionErrorSq)
        {
            var predictedPlayerGhost = ReadGhostComponentData<PredictedPlayerGhost>();
            var controllerState = predictedPlayerGhost.ControllerState;

            float positionError = math.distancesq(controllerState.CurrentPosition, rpcPosition);

            //allow the current player position to be altered by the client but only within a certain tolerance
            //(this is to avoid sliding during some position locked animations caused by the player predicting ahead of the server)
            if (positionError <= (positionErrorSq))
            {
                controllerState.CurrentPosition = rpcPosition;
                predictedPlayerGhost.ControllerState = controllerState;
                WriteGhostComponentData(predictedPlayerGhost);

                return true;
            }

            return false;
        }
    }
}
