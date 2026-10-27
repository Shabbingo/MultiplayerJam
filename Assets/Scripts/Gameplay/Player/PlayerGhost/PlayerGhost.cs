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
        private UnityEngine.CharacterController m_CharacterController;

        #endregion

        [field: SerializeField] public GameObject MainCameraPrefab { get; private set; }

        private Camera m_PlayerCamera;
        private Animator _animatorCharacter;
        private Vector3 m_ReticleVector;
        private Vector3 m_InitialReticleScale;
        private Vector3 m_InitialCameraTargetPosition;
        private Vector3 m_InitialCameraTargetScale;
        private Vector3 m_InitialOwnerVisualsScale;
        private Vector3 m_InitialOtherPlayerVisualsScale;
        private float m_InitialControllerHeight;
        private float m_InitialControllerRadius;
        private float m_InitialControllerStepOffset;
        private float m_InitialControllerSkinWidth;
        private Vector3 m_InitialControllerCenter;

        public float CurrentScaleMultiplier { get; private set; } = 1f;

        private CinemachineTargetGroup m_TargetGroup;
        private CinemachinePositionComposer m_PositionComposer;
        private CinemachineCamera m_CinemachineCamera;

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
            GetRequiredComponent(out m_CharacterController);
            m_ReticleVector = ReticlePoint.localPosition;
            m_InitialReticleScale = ReticlePoint.localScale;
            m_InitialCameraTargetPosition = CameraTarget.localPosition;
            m_InitialCameraTargetScale = CameraTarget.localScale;
            m_InitialOwnerVisualsScale = m_OwnerVisuals.transform.localScale;
            m_InitialOtherPlayerVisualsScale = m_OtherPlayerVisuals.transform.localScale;
            m_InitialControllerHeight = m_CharacterController.height;
            m_InitialControllerRadius = m_CharacterController.radius;
            m_InitialControllerStepOffset = m_CharacterController.stepOffset;
            m_InitialControllerSkinWidth = m_CharacterController.skinWidth;
            m_InitialControllerCenter = m_CharacterController.center;
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

            PlayerGhostManager.TryGetInstanceByRole(Role, out var playerManager);
            playerManager.Register(this);
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
            ApplyHealthBasedScale();
        }

        public void UpdateClient(float deltaTime)
        {
            ApplyHealthBasedScale();

            var predictedPlayerGhost = ReadGhostComponentData<PredictedPlayerGhost>();
            var controllerState = predictedPlayerGhost.ControllerState;
            if (Role == MultiplayerRole.ClientOwned)
            {
                CameraTarget.transform.rotation = Quaternion.Euler(controllerState.PitchDegrees,
                    Camera.main.transform.rotation.eulerAngles.y,
                    Camera.main.transform.rotation.eulerAngles.z);
            }

            var rot = Quaternion.Euler(controllerState.PitchDegrees, 0.0f, 0.0f);
            ReticlePoint.localPosition = rot * m_ReticleVector * CurrentScaleMultiplier;

            //TODO: The following is a temporary fix for animation root moves (Robot Jump for example)
            m_OtherPlayerVisuals.transform.localPosition = Vector3.zero;
            m_OtherPlayerVisuals.transform.localRotation = Quaternion.identity;
        }

        private void ApplyHealthBasedScale()
        {
            var predictedPlayerGhost = ReadGhostComponentData<PredictedPlayerGhost>();
            if (predictedPlayerGhost.MaxHealth <= 0f)
            {
                return;
            }

            var healthRatio = Mathf.Clamp01(
                predictedPlayerGhost.CurrentHealth / predictedPlayerGhost.MaxHealth);
            var scaleMultiplier = Mathf.Lerp(m_MinimumPlayerScale, 1f, healthRatio);

            // Avoid rebuilding the CharacterController's physics shape every frame.
            if (Mathf.Abs(CurrentScaleMultiplier - scaleMultiplier) <= 0.0001f)
            {
                return;
            }

            CurrentScaleMultiplier = scaleMultiplier;

            // Resize the authoritative hitbox without scaling the network/movement root.
            // Keeping the root at scale one prevents prediction and server movement divergence.
            m_CharacterController.height = m_InitialControllerHeight * scaleMultiplier;
            m_CharacterController.radius = m_InitialControllerRadius * scaleMultiplier;
            m_CharacterController.center = m_InitialControllerCenter * scaleMultiplier;
            m_CharacterController.stepOffset = m_InitialControllerStepOffset * scaleMultiplier;
            m_CharacterController.skinWidth = m_InitialControllerSkinWidth * scaleMultiplier;

            m_OwnerVisuals.transform.localScale = m_InitialOwnerVisualsScale * scaleMultiplier;
            m_OtherPlayerVisuals.transform.localScale = m_InitialOtherPlayerVisualsScale * scaleMultiplier;
            CameraTarget.localPosition = m_InitialCameraTargetPosition * scaleMultiplier;
            CameraTarget.localScale = m_InitialCameraTargetScale * scaleMultiplier;
            ReticlePoint.localScale = m_InitialReticleScale * scaleMultiplier;
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
