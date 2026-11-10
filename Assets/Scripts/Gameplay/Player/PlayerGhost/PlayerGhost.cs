using System;
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
        [SerializeField] private Vector3 m_ReflectShieldOffset = new(0f, 1f, 0f);
        [SerializeField] private Vector3 m_ReflectShieldScale = new(1.5f, 2.4f, 1.5f);
        [FormerlySerializedAs("m_ManualScaleTransitionSpeed")]
        [SerializeField, Min(0.01f), Tooltip("How quickly the visible player scale catches up while shrinking.")]
        private float m_ShrinkScaleResponseSpeed = 8f;
        [SerializeField, Min(0.01f), Tooltip("How quickly the visible player scale catches up while growing.")]
        private float m_GrowScaleResponseSpeed = 8f;
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
            if (m_ReflectShieldMaterial != null)
            {
                Destroy(m_ReflectShieldMaterial);
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
            if (m_ReflectGlow != null)
            {
                m_ReflectGlow.enabled = reflectShieldActive;
                if (reflectShieldActive)
                {
                    m_ReflectGlow.intensity = m_ReflectGlowIntensity *
                        (0.85f + 0.15f * Mathf.Sin(Time.time * 12f));
                }
            }

            if (m_ReflectShieldRenderer != null)
            {
                m_ReflectShieldRenderer.enabled = reflectShieldActive;
            }

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

        private void ApplyPlayerScale(float deltaTime)
        {
            var predictedPlayerGhost = ReadGhostComponentData<PredictedPlayerGhost>();
            if (predictedPlayerGhost.MaxHealth <= 0f)
            {
                return;
            }

            var targetScaleMultiplier = GetTargetScaleMultiplier(in predictedPlayerGhost);
            float responseSpeed = targetScaleMultiplier < m_ManualScaleMultiplier
                ? m_ShrinkScaleResponseSpeed
                : m_GrowScaleResponseSpeed;
            m_ManualScaleMultiplier = Mathf.MoveTowards(
                m_ManualScaleMultiplier,
                targetScaleMultiplier,
                responseSpeed * deltaTime);

            var targetScale = m_InitialScale * m_ManualScaleMultiplier;

            // Avoid rebuilding the CharacterController's physics shape every frame.
            if ((transform.localScale - targetScale).sqrMagnitude > 0.000001f)
            {
                transform.localScale = targetScale;
            }
        }

        public float GetTargetScaleMultiplier(in PredictedPlayerGhost predictedPlayerGhost)
        {
            if (predictedPlayerGhost.MaxHealth <= 0f)
            {
                return 1f;
            }

            float normalSizeHealth = predictedPlayerGhost.NormalSizeHealth > 0f
                ? predictedPlayerGhost.NormalSizeHealth
                : 100f;
            float sizeHealth = Mathf.Clamp(
                predictedPlayerGhost.CurrentHealth, 0f, predictedPlayerGhost.MaxHealth);
            float healthScaleMultiplier = sizeHealth <= normalSizeHealth
                ? Mathf.Lerp(m_MinimumPlayerScale, 1f, sizeHealth / normalSizeHealth)
                : Mathf.Lerp(1f, m_MaximumPlayerScale,
                    (sizeHealth - normalSizeHealth) /
                    Mathf.Max(1f, predictedPlayerGhost.MaxHealth - normalSizeHealth));
            var controllerState = predictedPlayerGhost.ControllerState;

            return controllerState.IsForcedFullSize
                ? 1f
                : controllerState.IsManualShrinkActive
                    ? m_MinimumPlayerScale
                    : healthScaleMultiplier;
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
