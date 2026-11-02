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
        [SerializeField, Min(0.01f)] private float m_ManualScaleTransitionSpeed = 4f;
        [field: SerializeField] public Transform CameraTarget { get; private set; }
        [field: SerializeField] public Transform ReticlePoint { get; private set; }
        [field: SerializeField] public Transform ShotOrigin { get; private set; }
        [field: FormerlySerializedAs("<VisualShotOrigin>k__BackingField")] [field: SerializeField] 
        public Transform VisualShotOrigin1P { get; private set; }
        [field: SerializeField] public Transform VisualShotOrigin3P { get; private set; }

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
            //Disable current camera
            var existingCamera = FindAnyObjectByType<Camera>();
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
            m_ManualScaleMultiplier = Mathf.MoveTowards(
                m_ManualScaleMultiplier,
                targetScaleMultiplier,
                m_ManualScaleTransitionSpeed * deltaTime);

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

            var healthRatio = Mathf.Clamp01(
                predictedPlayerGhost.CurrentHealth / predictedPlayerGhost.MaxHealth);
            var healthScaleMultiplier = Mathf.Lerp(m_MinimumPlayerScale, 1f, healthRatio);
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
