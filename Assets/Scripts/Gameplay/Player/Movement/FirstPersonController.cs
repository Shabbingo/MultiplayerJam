//#define DEBUG_RENDER_MOVEMENT

using System;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Entities;
using Unity.MP_FPS;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Serialization;

[RequireComponent(typeof(CharacterController))]
public class FirstPersonController : MonoBehaviour
{
    public SoundDef PlayerHitSFX;
    [SerializeField, Range(0.05f, 1f)] private float m_ManualShrinkHealthMultiplier = 0.2f;

    [Header("Reflect")]
    [SerializeField, Min(0.01f), Tooltip("Seconds that incoming weapon hits are reflected after activating the ability.")]
    private float m_ReflectDuration = 0.25f;
    [SerializeField, Min(0.1f), Tooltip("Seconds before the reflect ability can be activated again.")]
    private float m_ReflectCooldown = 15f;

    public float ReflectDuration => Mathf.Max(0.01f, m_ReflectDuration);
    public float ReflectCooldown => Mathf.Max(0.1f, m_ReflectCooldown);

    [Header("Self Resize")]
    [SerializeField, Range(1, 10), Tooltip("Number of self-shrink uses available each life.")]
    private int m_SelfShrinkUses = 3;
    [SerializeField, Range(1, 10), Tooltip("Number of self-grow uses available each life.")]
    private int m_SelfGrowUses = 3;
    [SerializeField, Min(0.1f), Tooltip("Seconds required to regenerate one self-shrink use.")]
    private float m_SelfShrinkRechargeSeconds = 5f;
    [SerializeField, Min(0.1f), Tooltip("Seconds required to regenerate one self-grow use.")]
    private float m_SelfGrowRechargeSeconds = 5f;
    [SerializeField, Min(1f), Tooltip("Fixed percentage-point chunk applied immediately by each self-resize key press.")]
    private float m_SelfResizeChunkSize = 25f;
    [SerializeField, Min(0.01f), Tooltip("Distance kept from the lethal 0 and 200 boundaries. Self-resizing clamps here, but weapon hits can still kill.")]
    private float m_SelfResizeSafetyMargin = 1f;
    [SerializeField, Min(0f), Tooltip("Upward speed applied when self-growing while already airborne.")]
    private float m_AirGrowJumpSpeed = 5f;
    [SerializeField, Min(0f), Tooltip("Downward speed applied when self-shrinking while already airborne.")]
    private float m_AirShrinkPushSpeed = 7f;

    [Header("Self Resize Camera Pulse")]
    [SerializeField, Range(0f, 1f), Tooltip("Peak lens distortion used by a self-resize pulse. Grow and shrink use opposite distortion directions.")]
    private float m_SelfResizeLensDistortion = 0.35f;
    [SerializeField, Tooltip("Swaps the lens directions so shrinking pushes outward and growing pulls inward.")]
    private bool m_InvertSelfResizeLensDirection;
    [SerializeField, Min(0.01f), Tooltip("Duration in seconds of the self-resize lens distortion pulse.")]
    private float m_SelfResizeLensPulseDuration = 0.35f;
    [SerializeField, Range(0f, 1f), Tooltip("Vertical center of the shrink pinch. Lower values move the distortion toward the bottom of the screen.")]
    private float m_SelfShrinkLensCenterY = 0.15f;
    [SerializeField, Range(0f, 1f), Tooltip("Vertical center of the grow distortion. Higher values move it toward the top of the screen.")]
    private float m_SelfGrowLensCenterY = 0.85f;
    [SerializeField, Tooltip("Lens pulse strength over its duration. Time 0 is the key press and time 1 is the end of the pulse.")]
    private AnimationCurve m_SelfResizeLensPulseCurve = new(
        new Keyframe(0f, 0f),
        new Keyframe(0.12f, 1f),
        new Keyframe(1f, 0f));

    [Header("Shrink Resource")]
    [SerializeField, Min(0.1f), Tooltip("Maximum shrink resource. At a drain rate of 1, this is the number of seconds the player can remain small.")]
    private float m_MaxShrinkResource = 5f;
    [SerializeField, Min(0f), Tooltip("One-time shrink resource cost paid when shrinking is activated. The player must have at least this much resource to shrink.")]
    private float m_ShrinkActivationCost = 1f;
    [SerializeField, Min(0f), Tooltip("Shrink resource consumed per second while the player is small.")]
    private float m_ShrinkDrainRate = 1f;
    [SerializeField, Min(0f), Tooltip("Shrink resource restored per second while the player is normal-sized.")]
    private float m_ShrinkRefillRate = 1f;

    [Header("Size-Based Movement")]
    [SerializeField, Range(0f, 1f), Tooltip("How strongly player size affects movement speed. At 1, a 50% sized player moves at 50% speed; at 0, size has no effect.")]
    private float m_SizeMovementSpeedInfluence = 1f;
    [SerializeField, Range(0f, 1f), Tooltip("How strongly player size affects jump height. At 1, a 50% sized player jumps 50% as high; at 0, size has no effect.")]
    private float m_SizeJumpHeightInfluence = 1f;

    [Header("Full-Size Bhop")]
    [SerializeField, Min(0f), Tooltip("How long after returning from manually-shrunk to full size that a fresh jump press can trigger the resize bounce.")]
    private float m_ResizeBounceInputWindow = 0.2f;
    [SerializeField, Min(0f), Tooltip("Upward launch speed of the full-size resize bounce. Horizontal movement and airborne recoil are preserved.")]
    private float m_ResizeBounceSpeed = 7.5f;
    [SerializeField, Min(0f), Tooltip("Temporary field-of-view increase when the local player successfully performs a resize bounce.")]
    private float m_ResizeBounceFovKick = 6f;
    [SerializeField, Min(0.01f), Tooltip("Seconds taken for the resize-bounce field-of-view kick to settle back to normal.")]
    private float m_ResizeBounceFovDuration = 0.25f;

    [Header("Airborne Weapon Thruster")]
    [SerializeField, Min(0f), Tooltip("Thrust velocity added opposite the firing direction for every shot while airborne. This does not depend on the shot hitting anything.")]
    private float m_AirborneRecoilImpulse = 4f;
    [SerializeField, Tooltip("Thrust multiplier by player size. X is 0 at full size and 1 at minimum size; Y is the thrust multiplier.")]
    private AnimationCurve m_RecoilIntensityBySize = new(
        new Keyframe(0f, 0.15f),
        new Keyframe(0.7f, 0.5f),
        new Keyframe(1f, 3f));
    [SerializeField, Min(0f), Tooltip("Air resistance applied to airborne thrust momentum. Lower values allow the player to coast and fling farther.")]
    private float m_AirborneRecoilDamping = 0.35f;
    [SerializeField, Min(0.1f), Tooltip("Maximum thrust speed that can be accumulated by repeatedly firing while airborne.")]
    private float m_MaxAirborneRecoilSpeed = 60f;

    [Header("Forced Resize Clearance")]
    [SerializeField, Tooltip("Map layers that can crush a player when they are forced to full size.")]
    private LayerMask m_ResizeObstructionLayers = 129;
    [SerializeField, Range(0.8f, 1f), Tooltip("Slightly narrows the clearance probe to avoid false positives from touching walls. Vertical head clearance remains full size.")]
    private float m_FullSizeClearanceProbeScale = 0.9f;
    [SerializeField, Min(0f), Tooltip("How far the server may nudge a growing player sideways to find valid full-size space before gibbing them.")]
    private float m_FullSizeClearanceSearchDistance = 0.75f;
    [SerializeField, Range(1, 5), Tooltip("Number of distance steps tested in each horizontal direction.")]
    private int m_FullSizeClearanceSearchSteps = 3;

    private static readonly Vector3[] s_ClearanceSearchDirections =
    {
        Vector3.forward,
        Vector3.back,
        Vector3.left,
        Vector3.right,
        new Vector3(1f, 0f, 1f).normalized,
        new Vector3(1f, 0f, -1f).normalized,
        new Vector3(-1f, 0f, 1f).normalized,
        new Vector3(-1f, 0f, -1f).normalized
    };

    private static class AnimationParameters
    {
        //Speed, Strafe, Turn: float values
        //Verbs: triggers
        //Is-X: boolean values
        public static readonly int IsMoving = Animator.StringToHash("IsMoving"); //1P
        public static readonly int Speed = Animator.StringToHash("Speed"); //3P
        public static readonly int StrafeSpeed = Animator.StringToHash("SpeedStrafe"); //3P
        public static readonly int MovementPlaybackSpeed = Animator.StringToHash("MovementPlaybackSpeed"); //3P
        public static readonly int TurnSpeed = Animator.StringToHash("Turn"); //3P
        public static readonly int Shoot = Animator.StringToHash("Shoot");
        public static readonly int ShootGrenade = Animator.StringToHash("ShootGrenade");
        public static readonly int ShrinkCastHeld = Animator.StringToHash("ShrinkCastHeld");
        public static readonly int IsShooting = Animator.StringToHash("IsShooting");
        public static readonly int Reload = Animator.StringToHash("Reload");
        public static readonly int Jump = Animator.StringToHash("Jump");
        public static readonly int Fall = Animator.StringToHash("Fall");
        public static readonly int IsInAir = Animator.StringToHash("IsInAir");
        public static readonly int Land = Animator.StringToHash("Land");
        public static readonly int IsHit = Animator.StringToHash("IsHit");
        public static readonly int IsDead = Animator.StringToHash("IsDead"); //3P
    }

    private const float k_ResetMovementAdjustEpsilon = 1e-06f;

    public enum MovementType
    {
        Standing = 0,
        Jumping,
        Falling,
    }

    public struct ControllerState
    {
        public enum StateFlag
        {
            Jump = 1 << 0,
            Fall = 1 << 1,
            Land = 1 << 2,
            Shoot = 1 << 3,
            IsReloading = 1 << 4,
            IsHit = 1 << 5,
            JumpTrigger = 1 << 6,
            LandTrigger = 1 << 7,
            IsManualShrinkActive = 1 << 8,
            ShrinkTogglePressed = 1 << 9,
            IsForcedFullSize = 1 << 10
        }

        //WARNING WARNING: Adding more members to this struct might break network serialisation speak to Claire/Andy B

        // booleans
        public uint StateFlags;

        // Ammo for both weapon slots is packed into unused portions of StateFlags so adding
        // a secondary magazine does not change the existing NetCode ghost component layout.
        private const int PrimaryAmmoShift = 16;
        private const int SecondaryAmmoShift = 24;
        private const uint PackedAmmoMask = 0xffu;

        public int PrimaryWeaponAmmo
        {
            get => (int)((StateFlags >> PrimaryAmmoShift) & PackedAmmoMask);
            set => SetPackedAmmo(PrimaryAmmoShift, value);
        }

        public int SecondaryWeaponAmmo
        {
            get => (int)((StateFlags >> SecondaryAmmoShift) & PackedAmmoMask);
            set => SetPackedAmmo(SecondaryAmmoShift, value);
        }

        [GhostField(SendData = false)]
        public bool Jump
        {
            get => (StateFlags & (uint)StateFlag.Jump) != 0;
            set => SetFlag(StateFlag.Jump, value);
        }

        [GhostField(SendData = false)]
        public bool Fall
        {
            get => (StateFlags & (uint)StateFlag.Fall) != 0;
            set => SetFlag(StateFlag.Fall, value);
        }

        [GhostField(SendData = false)]
        public bool Land
        {
            get => (StateFlags & (uint)StateFlag.Land) != 0;
            set => SetFlag(StateFlag.Land, value);
        }

        [GhostField(SendData = false)]
        public bool Shoot
        {
            get => (StateFlags & (uint)StateFlag.Shoot) != 0;
            set => SetFlag(StateFlag.Shoot, value);
        }

        [GhostField(SendData = false)]
        public bool IsReloadingState
        {
            get => (StateFlags & (uint)StateFlag.IsReloading) != 0;
            set => SetFlag(StateFlag.IsReloading, value);
        }

        [GhostField(SendData = false)]
        public bool IsHit
        {
            get => (StateFlags & (uint)StateFlag.IsHit) != 0;
            set => SetFlag(StateFlag.IsHit, value);
        }

        [GhostField(SendData = false)]
        public bool JumpTriggered
        {
            get => (StateFlags & (uint)StateFlag.JumpTrigger) != 0;
            set => SetFlag(StateFlag.JumpTrigger, value);
        }

        [GhostField(SendData = false)]
        public bool LandTriggered
        {
            get => (StateFlags & (uint)StateFlag.LandTrigger) != 0;
            set => SetFlag(StateFlag.LandTrigger, value);
        }

        [GhostField(SendData = false)]
        public bool IsManualShrinkActive
        {
            get => (StateFlags & (uint)StateFlag.IsManualShrinkActive) != 0;
            set => SetFlag(StateFlag.IsManualShrinkActive, value);
        }

        [GhostField(SendData = false)]
        public bool ShrinkTogglePressed
        {
            get => (StateFlags & (uint)StateFlag.ShrinkTogglePressed) != 0;
            set => SetFlag(StateFlag.ShrinkTogglePressed, value);
        }

        [GhostField(SendData = false)]
        public bool IsForcedFullSize
        {
            get => (StateFlags & (uint)StateFlag.IsForcedFullSize) != 0;
            set => SetFlag(StateFlag.IsForcedFullSize, value);
        }

        public quaternion CurrentRotation;
        public float3 CurrentPosition;
        public float3 GroundNormal;

        public float3 MovementRequest;
        public MovementType MovementType;
        public MovementType PreviousMovementType;
        public float TimeInState;

        public float YawDegrees;
        public float PitchDegrees;
        public float MovementSpeed;
        public float JumpFallSpeed;
        public float AnimatorTargetSpeed; // _animIDSpeed
        public float AnimatorTargetSpeedChangeRate;
        public float JumpTimeoutDelta;
        public float FallTimeoutDelta;
        public float FallHeight;

        public float RotationVelocity;

        public float3 AnimatorMotion;
        public float AnimatorMotionChangeRate;
        public float AnimatorSmoothedMotionX;
        public float AnimatorMotionSpeed; // _animIDMotionSpeed

        public float TeleportFreeze;

        //WARNING WARNING: Adding more members to this struct might break network serialisation speak to Claire/Andy B

        private void SetFlag(StateFlag flag, bool set)
        {
            if (set)
            {
                StateFlags |= (uint)flag;
            }
            else
            {
                StateFlags &= ~(uint)flag;
            }
        }

        private void SetPackedAmmo(int shift, int value)
        {
            uint shiftedMask = PackedAmmoMask << shift;
            uint packedValue = (uint)Math.Clamp(value, 0, (int)PackedAmmoMask) << shift;
            StateFlags = (StateFlags & ~shiftedMask) | packedValue;
        }

        public void Init(in float3 worldPosition, in quaternion worldRotation)
        {
            CurrentPosition = worldPosition;
            CurrentRotation = worldRotation;

            Quaternion rot = worldRotation;
            PitchDegrees = rot.eulerAngles.y;
        }
    }

    public struct ControllerConsts
    {
        public struct StateConsts
        {
            public float Speed;
            public float SpeedChangeRate;
            public float RotationSmoothTime;
            public float LandingSpeedMult;
            public float AnimationMotionScale;
        }

        public StateConsts Walk;
        public StateConsts Sprint;

        public float JumpHeight;
        public float Gravity;
        public float StandingFallSpeed;
        public float JumpTimeout;
        public float FallTimeout;
        public float LandingTimeout;
        public float LandingAnimTimeout;
        public float StateChangeSafetyTimeout;
        public float GroundedOffset;
        public LayerMask GroundLayers;
        public float TerminalVelocity;
    }

    [field: Header("Cinemachine")]
    [field: SerializeField,
            Tooltip("The follow target set in the Cinemachine Virtual Camera that the camera will follow")]
    public GameObject CinemachineCameraTarget { get; private set; }

    [SerializeField, Tooltip("A small offset for the grounded spherecast. Should be a small positive value.")]
    private float GroundedOffset = 0.2f;

    public PhysicsMaterial GroundPhysicsMaterial { get; private set; }

    [field: SerializeField] public Vector3 ControllerOffset { get; private set; } = new Vector3(0f, 0f, 0f);

    [SerializeField] private Animator m_Animator_1P;
    [SerializeField] private Animator m_Animator_3P;
    [SerializeField] private bool m_EnableAnimationLogging = false;
    private DamageVisualsController m_DamageVisualsController;
    private uint _lastProcessedHitTick = 0;
    private uint _lastAnimatedShotTick = 0;
    private uint _lastAnimatedGrenadeShotTick = 0;
    private uint _lastAnimatedJumpTick = 0;
    private uint _lastAnimatedLandTick = 0;
    private uint _lastAnimatedReloadTick = 0;

    private CharacterController m_Controller;
    public CharacterController CharacterController => m_Controller;
    private Vector3 m_InitialLossyScale;

    private PlayerGhost m_PlayerGhost;
    private PlayerGhost PlayerGhost => m_PlayerGhost;

    private const int k_NumPhysicsResults = 8;
    private readonly RaycastHit[] m_GroundCheckRaycastResults = new RaycastHit[k_NumPhysicsResults];
    private const int k_NumResizePhysicsResults = 32;
    private readonly Collider[] m_ResizeOverlapResults = new Collider[k_NumResizePhysicsResults];
    private readonly RaycastHit[] m_ResizeCastResults = new RaycastHit[k_NumResizePhysicsResults];

    private const float k_DefaultSpeedChange = 20f;

#if DEBUG_RENDER_MOVEMENT || DEBUG_RENDER_CLIMBING_MOVEMENT
    private const float k_DebugRenderingTimeout = 5f;
#endif

    private static readonly float3 k_UpVector = math.up();
    private static readonly float3 k_ForwardVector = math.forward();

    public float CachedJumpFallSpeed { get; private set; }
    public float CachedFallHeight { get; private set; }

#if UNITY_EDITOR || DEBUG
    private MovementType m_PrevMovementType;
#endif

    private float footstepTriggerTimer = 0;
    private float footstepStartTimer = 0;

    private void Awake()
    {
        CameraResizePulseEffect.Configure(
            m_SelfResizeLensDistortion,
            m_SelfResizeLensPulseDuration,
            m_SelfResizeLensPulseCurve,
            m_SelfShrinkLensCenterY,
            m_SelfGrowLensCenterY,
            m_InvertSelfResizeLensDirection);

        TryGetComponent(out m_Controller);
        Debug.Assert(m_Controller, "[THIRDPERSONCONTROLLER] Player has no CharacterController component");
        m_InitialLossyScale = transform.lossyScale;

        TryGetComponent(out m_PlayerGhost);
        Debug.Assert(m_PlayerGhost, "[THIRDPERSONCONTROLLER] Player has no PlayerGhost component");

        TryGetComponent(out m_DamageVisualsController);
        Debug.Assert(m_DamageVisualsController,
            "[FIRSTPERSONCONTROLLER] Player has no DamageVisualsController component");
    }

    public void SetExcludeLayers(LayerMask excludeLayers)
    {
        m_Controller.excludeLayers = excludeLayers;
    }

    public void GetCurrentHitboxCapsule(Vector3 playerPosition, Quaternion playerRotation,
        out Vector3 point0, out Vector3 point1, out float radius)
    {
        var currentScale = new Vector3(
            Mathf.Abs(transform.lossyScale.x),
            Mathf.Abs(transform.lossyScale.y),
            Mathf.Abs(transform.lossyScale.z));
        var capsuleUp = playerRotation * Vector3.up;
        var worldCenter = playerPosition + playerRotation *
            Vector3.Scale(m_Controller.center, currentScale);
        radius = m_Controller.radius * Mathf.Max(currentScale.x, currentScale.z);
        float height = Mathf.Max(m_Controller.height * currentScale.y, radius * 2f);
        var capsuleAxis = capsuleUp * Mathf.Max(0f, height * 0.5f - radius);
        point0 = worldCenter - capsuleAxis;
        point1 = worldCenter + capsuleAxis;
    }

    public bool TryFindFullSizeClearance(Vector3 playerPosition,
        Quaternion playerRotation, out Vector3 positionAdjustment)
    {
        positionAdjustment = Vector3.zero;
        if (m_Controller == null)
        {
            return true;
        }

        var fullScale = new Vector3(
            Mathf.Abs(m_InitialLossyScale.x),
            Mathf.Abs(m_InitialLossyScale.y),
            Mathf.Abs(m_InitialLossyScale.z));
        var scaledCenter = Vector3.Scale(m_Controller.center, fullScale);
        float fullSizeSkin = m_Controller.skinWidth * fullScale.y;
        var capsuleUp = playerRotation * Vector3.up;
        var worldCenter = playerPosition + playerRotation * scaledCenter +
                          capsuleUp * (fullSizeSkin * 0.5f);

        float radius = m_Controller.radius * Mathf.Max(fullScale.x, fullScale.z) *
                       m_FullSizeClearanceProbeScale;
        float height = Mathf.Max(
            m_Controller.height * fullScale.y - fullSizeSkin,
            radius * 2f);
        float capsuleOffset = Mathf.Max(0f, height * 0.5f - radius);
        var capsuleAxis = capsuleUp * capsuleOffset;

        var currentScale = new Vector3(
            Mathf.Abs(transform.lossyScale.x),
            Mathf.Abs(transform.lossyScale.y),
            Mathf.Abs(transform.lossyScale.z));
        float currentSkin = m_Controller.skinWidth * currentScale.y;
        var currentCenter = playerPosition + playerRotation *
            Vector3.Scale(m_Controller.center, currentScale) +
            capsuleUp * (currentSkin * 0.5f);
        float currentRadius = m_Controller.radius *
                              Mathf.Max(currentScale.x, currentScale.z) *
                              m_FullSizeClearanceProbeScale;
        float currentHeight = Mathf.Max(
            m_Controller.height * currentScale.y - currentSkin,
            currentRadius * 2f);
        var currentCapsuleAxis = capsuleUp *
                                 Mathf.Max(0f, currentHeight * 0.5f - currentRadius);

        var currentTop = currentCenter + currentCapsuleAxis + capsuleUp * currentRadius;
        var fullSizeTop = worldCenter + capsuleAxis + capsuleUp * radius;
        float headProbeRadius = Mathf.Max(0.02f,
            Mathf.Min(currentRadius, radius) * 0.75f);

        // A low ceiling or window frame directly above the player's current head
        // must crush them rather than letting the horizontal clearance search push
        // them out. Keep this probe vertical so a wall in front is not mistaken for
        // an overhead obstruction as the scaled capsule center moves forward.
        float verticalHeadExpansion = Mathf.Max(0f,
            Vector3.Dot(fullSizeTop - currentTop, capsuleUp));
        var verticalFullSizeTop = currentTop + capsuleUp * verticalHeadExpansion;
        if (IsHeadExpansionBlocked(
                currentTop,
                verticalFullSizeTop,
                capsuleUp,
                headProbeRadius))
        {
            return false;
        }

        if (IsFullSizeCapsuleClear(worldCenter, capsuleAxis, radius) &&
            !IsHeadExpansionBlocked(currentTop, fullSizeTop, capsuleUp, headProbeRadius))
        {
            return true;
        }

        int searchSteps = Mathf.Max(1, m_FullSizeClearanceSearchSteps);
        for (int step = 1; step <= searchSteps; step++)
        {
            float distance = m_FullSizeClearanceSearchDistance * step / searchSteps;
            foreach (var localDirection in s_ClearanceSearchDirections)
            {
                var worldDirection = playerRotation * localDirection;
                worldDirection.y = 0f;
                worldDirection.Normalize();
                var candidateAdjustment = worldDirection * distance;

                if (CanMoveCapsuleToAdjustment(currentCenter, currentCapsuleAxis,
                        currentRadius, candidateAdjustment) &&
                    IsFullSizeCapsuleClear(
                        worldCenter + candidateAdjustment, capsuleAxis, radius) &&
                    !IsHeadExpansionBlocked(
                        currentTop,
                        fullSizeTop + candidateAdjustment,
                        capsuleUp,
                        headProbeRadius))
                {
                    positionAdjustment = candidateAdjustment;
                    return true;
                }
            }
        }

        return false;
    }

    private bool IsHeadExpansionBlocked(Vector3 currentTop, Vector3 fullSizeTop,
        Vector3 capsuleUp, float probeRadius)
    {
        var start = currentTop - capsuleUp * probeRadius;
        var end = fullSizeTop - capsuleUp * probeRadius;

        return HasExternalCapsuleOverlap(
            start,
            end,
            probeRadius);
    }

    private bool CanMoveCapsuleToAdjustment(Vector3 worldCenter, Vector3 capsuleAxis,
        float radius, Vector3 adjustment)
    {
        float distance = adjustment.magnitude;
        if (distance <= Mathf.Epsilon)
        {
            return true;
        }

        int hitCount = Physics.CapsuleCastNonAlloc(
            worldCenter - capsuleAxis,
            worldCenter + capsuleAxis,
            radius,
            adjustment / distance,
            m_ResizeCastResults,
            distance,
            m_ResizeObstructionLayers,
            QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hitCount; i++)
        {
            if (!IsOwnResizeCollider(m_ResizeCastResults[i].collider))
            {
                return false;
            }
        }

        return true;
    }

    private bool IsFullSizeCapsuleClear(Vector3 worldCenter, Vector3 capsuleAxis,
        float radius)
    {
        return !HasExternalCapsuleOverlap(
            worldCenter - capsuleAxis,
            worldCenter + capsuleAxis,
            radius);
    }

    private bool HasExternalCapsuleOverlap(Vector3 point0, Vector3 point1, float radius)
    {
        int overlapCount = Physics.OverlapCapsuleNonAlloc(
            point0,
            point1,
            radius,
            m_ResizeOverlapResults,
            m_ResizeObstructionLayers,
            QueryTriggerInteraction.Ignore);

        for (int i = 0; i < overlapCount; i++)
        {
            if (!IsOwnResizeCollider(m_ResizeOverlapResults[i]))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsOwnResizeCollider(Collider candidate)
    {
        return candidate != null &&
               (candidate.transform == transform || candidate.transform.IsChildOf(transform));
    }

    public void ApplyManualShrinkHealth(ref PredictedPlayerGhost predictedPlayer, bool shrinking)
    {
        if (predictedPlayer.MaxHealth <= 0f || predictedPlayer.CurrentHealth <= 0f)
        {
            return;
        }

        predictedPlayer.CurrentHealth = shrinking
            ? predictedPlayer.CurrentHealth * m_ManualShrinkHealthMultiplier
            : Mathf.Min(predictedPlayer.MaxHealth,
                predictedPlayer.CurrentHealth / m_ManualShrinkHealthMultiplier);
    }

    public bool CanRestoreHealth(in PredictedPlayerGhost predictedPlayer)
    {
        if (predictedPlayer.MaxHealth <= 0f || predictedPlayer.CurrentHealth <= 0f ||
            predictedPlayer.CurrentHealth >= predictedPlayer.MaxHealth)
        {
            return false;
        }

        float normalSizeHealth = predictedPlayer.NormalSizeHealth > 0f
            ? predictedPlayer.NormalSizeHealth
            : 100f;
        return !Mathf.Approximately(predictedPlayer.CurrentHealth, normalSizeHealth);
    }

    public void ProcessSelfResize(ref PredictedPlayerGhost predictedPlayer,
        in PlayerInput input, float deltaTime,
        in Unity.MP_FPS.Cards.PlayerModifierStacks modifiers)
    {
        InitializeSelfResizeAmmo(ref predictedPlayer, in modifiers);

        float chunkSize = Mathf.Max(1f, m_SelfResizeChunkSize);
        predictedPlayer.SelfResizeMaxCharge = chunkSize;
        predictedPlayer.SelfResizeChargeAmount = 0f;
        if (predictedPlayer.MaxHealth <= 0f || deltaTime <= 0f)
        {
            return;
        }

        bool hasResizePress = input.SelfShrinkPressed || input.SelfGrowPressed;
        if (!hasResizePress)
        {
            // Re-arm only after a neutral input command. NetCode can replay the
            // pressed command during prediction, so clearing this every call would
            // spend multiple uses for one physical key press.
            predictedPlayer.SelfResizeChargeDirection = 0;
            return;
        }

        if (predictedPlayer.SelfResizeChargeDirection != 0)
        {
            return;
        }

        // Ignore an ambiguous simultaneous press instead of applying two opposing uses.
        if (input.SelfShrinkPressed && input.SelfGrowPressed)
        {
            return;
        }

        int requestedDirection = input.SelfGrowPressed ? 1 : input.SelfShrinkPressed ? -1 : 0;
        predictedPlayer.SelfResizeChargeDirection = requestedDirection;

        if (!TryGetSelfResizeTargetHealth(
                in predictedPlayer, requestedDirection, out float resizedHealth))
        {
            return;
        }

        predictedPlayer.CurrentHealth = resizedHealth;
        if (requestedDirection > 0)
        {
            predictedPlayer.SelfGrowCharges = Mathf.Max(
                0, predictedPlayer.SelfGrowCharges - 1);
        }
        else
        {
            predictedPlayer.SelfShrinkCharges = Mathf.Max(
                0, predictedPlayer.SelfShrinkCharges - 1);
        }

        ApplyAirResizeImpulse(ref predictedPlayer, requestedDirection);
    }

    public bool CanSelfResize(in PredictedPlayerGhost predictedPlayer, int resizeDirection)
    {
        return TryGetSelfResizeTargetHealth(
            in predictedPlayer, resizeDirection, out _);
    }

    private bool TryGetSelfResizeTargetHealth(in PredictedPlayerGhost predictedPlayer,
        int resizeDirection, out float resizedHealth)
    {
        resizedHealth = predictedPlayer.CurrentHealth;
        if (resizeDirection == 0 || predictedPlayer.MaxHealth <= 0f ||
            predictedPlayer.CurrentHealth <= 0f)
        {
            return false;
        }

        bool hasAmmo = resizeDirection > 0
            ? predictedPlayer.SelfGrowCharges > 0
            : predictedPlayer.SelfShrinkCharges > 0;
        if (!hasAmmo)
        {
            return false;
        }

        // A press at a size boundary neither spends ammo nor plays resize feedback.
        float safeMinimum = Mathf.Min(
            m_SelfResizeSafetyMargin,
            predictedPlayer.MaxHealth * 0.5f);
        float safeMaximum = Mathf.Max(
            safeMinimum,
            predictedPlayer.MaxHealth - m_SelfResizeSafetyMargin);
        resizedHealth = Mathf.Clamp(
            predictedPlayer.CurrentHealth +
            Mathf.Max(1f, m_SelfResizeChunkSize) * resizeDirection,
            safeMinimum,
            safeMaximum);
        return !Mathf.Approximately(resizedHealth, predictedPlayer.CurrentHealth);
    }

    private void ApplyAirResizeImpulse(ref PredictedPlayerGhost predictedPlayer,
        int resizeDirection)
    {
        ref var state = ref predictedPlayer.ControllerState;
        if (state.MovementType == MovementType.Standing)
        {
            return;
        }

        if (resizeDirection > 0)
        {
            float upwardSpeed = Mathf.Max(0f, m_AirGrowJumpSpeed);
            state.JumpFallSpeed = Mathf.Max(state.JumpFallSpeed, upwardSpeed);
            SetMovementType(ref state, MovementType.Jumping);
            state.Jump = true;
            state.Fall = false;
            state.JumpTriggered = true;
        }
        else
        {
            float downwardSpeed = Mathf.Max(0f, m_AirShrinkPushSpeed);
            state.JumpFallSpeed = Mathf.Min(state.JumpFallSpeed, -downwardSpeed);
            SetMovementType(ref state, MovementType.Falling);
            state.Jump = true;
            state.Fall = true;
        }
    }

    public void TriggerSelfResizeCameraPulse(int resizeDirection)
    {
        if (resizeDirection == 0)
        {
            return;
        }

        CameraResizePulseEffect.Trigger(
            Mathf.Sign(resizeDirection) * m_SelfResizeLensDistortion,
            m_SelfResizeLensPulseDuration,
            m_SelfResizeLensPulseCurve);
    }

    public void UpdateSelfResizeAmmo(ref PredictedPlayerGhost predictedPlayer, float deltaTime,
        in Unity.MP_FPS.Cards.PlayerModifierStacks modifiers)
    {
        InitializeSelfResizeAmmo(ref predictedPlayer, in modifiers);
        if (deltaTime <= 0f)
        {
            return;
        }

        RegenerateSelfResizeCharge(
            ref predictedPlayer.SelfShrinkCharges,
            predictedPlayer.MaxSelfShrinkCharges,
            ref predictedPlayer.SelfShrinkRechargeTimer,
            m_SelfShrinkRechargeSeconds,
            deltaTime);
        RegenerateSelfResizeCharge(
            ref predictedPlayer.SelfGrowCharges,
            predictedPlayer.MaxSelfGrowCharges,
            ref predictedPlayer.SelfGrowRechargeTimer,
            m_SelfGrowRechargeSeconds,
            deltaTime);
    }

    private void InitializeSelfResizeAmmo(ref PredictedPlayerGhost predictedPlayer,
        in Unity.MP_FPS.Cards.PlayerModifierStacks modifiers)
    {
        if (predictedPlayer.SelfResizeAmmoInitialized)
        {
            return;
        }

        predictedPlayer.MaxSelfShrinkCharges = Mathf.Max(1,
            m_SelfShrinkUses + modifiers.ExtraSelfShrinkCharges + modifiers.ExtraResizeCharges);
        predictedPlayer.MaxSelfGrowCharges = Mathf.Max(1,
            m_SelfGrowUses + modifiers.ExtraSelfGrowCharges + modifiers.ExtraResizeCharges);
        predictedPlayer.SelfShrinkCharges = predictedPlayer.MaxSelfShrinkCharges;
        predictedPlayer.SelfGrowCharges = predictedPlayer.MaxSelfGrowCharges;
        predictedPlayer.SelfShrinkRechargeTimer = 0f;
        predictedPlayer.SelfGrowRechargeTimer = 0f;
        predictedPlayer.SelfResizeAmmoInitialized = true;
    }

    private static void RegenerateSelfResizeCharge(ref int charges, int maximumCharges,
        ref float rechargeTimer, float rechargeSeconds, float deltaTime)
    {
        if (charges >= maximumCharges)
        {
            charges = maximumCharges;
            rechargeTimer = 0f;
            return;
        }

        float interval = Mathf.Max(0.1f, rechargeSeconds);
        rechargeTimer += deltaTime;
        while (rechargeTimer >= interval && charges < maximumCharges)
        {
            rechargeTimer -= interval;
            charges++;
        }

        if (charges >= maximumCharges)
        {
            rechargeTimer = 0f;
        }
    }

    public bool TryRestoreHealth(ref PredictedPlayerGhost predictedPlayer, float fullSizeHealAmount)
    {
        if (!CanRestoreHealth(in predictedPlayer) || fullSizeHealAmount <= 0f)
        {
            return false;
        }

        float normalSizeHealth = predictedPlayer.NormalSizeHealth > 0f
            ? predictedPlayer.NormalSizeHealth
            : 100f;
        predictedPlayer.CurrentHealth = Mathf.MoveTowards(
            predictedPlayer.CurrentHealth, normalSizeHealth, fullSizeHealAmount);
        return true;
    }

    public void InitializeShrinkResource(ref PredictedPlayerGhost predictedPlayer)
    {
        if (predictedPlayer.MaxShrinkResource > 0f)
        {
            return;
        }

        predictedPlayer.MaxShrinkResource = m_MaxShrinkResource;
        predictedPlayer.CurrentShrinkResource = m_MaxShrinkResource;
    }

    public bool TryToggleManualShrink(ref PredictedPlayerGhost predictedPlayer)
    {
        bool shrinking = !predictedPlayer.ControllerState.IsManualShrinkActive;
        return TrySetManualShrink(ref predictedPlayer, shrinking);
    }

    public bool TrySetManualShrink(ref PredictedPlayerGhost predictedPlayer, bool shrinking)
    {
        InitializeShrinkResource(ref predictedPlayer);

        if (predictedPlayer.ControllerState.IsManualShrinkActive == shrinking)
        {
            return false;
        }

        if (shrinking &&
            (predictedPlayer.CurrentShrinkResource <= 0f ||
             predictedPlayer.CurrentShrinkResource < m_ShrinkActivationCost))
        {
            return false;
        }

        predictedPlayer.ControllerState.IsManualShrinkActive = shrinking;
        if (shrinking)
        {
            predictedPlayer.ResizeBounceWindowRemaining = 0f;
            predictedPlayer.CurrentShrinkResource = Mathf.Max(0f,
                predictedPlayer.CurrentShrinkResource - m_ShrinkActivationCost);
        }
        else
        {
            ArmResizeBounce(ref predictedPlayer);
        }

        ApplyManualShrinkHealth(ref predictedPlayer, shrinking);
        return true;
    }

    public void UpdateShrinkResource(ref PredictedPlayerGhost predictedPlayer, float deltaTime)
    {
        InitializeShrinkResource(ref predictedPlayer);

        if (predictedPlayer.ControllerState.IsManualShrinkActive &&
            !predictedPlayer.ControllerState.IsForcedFullSize)
        {
            predictedPlayer.CurrentShrinkResource = Mathf.Max(0f,
                predictedPlayer.CurrentShrinkResource - m_ShrinkDrainRate * deltaTime);

            if (predictedPlayer.CurrentShrinkResource <= 0f)
            {
                predictedPlayer.ControllerState.IsManualShrinkActive = false;
                ArmResizeBounce(ref predictedPlayer);
                ApplyManualShrinkHealth(ref predictedPlayer, false);
            }
        }
        else
        {
            predictedPlayer.CurrentShrinkResource = Mathf.Min(predictedPlayer.MaxShrinkResource,
                predictedPlayer.CurrentShrinkResource + m_ShrinkRefillRate * deltaTime);
        }
    }

    private void ArmResizeBounce(ref PredictedPlayerGhost predictedPlayer)
    {
        predictedPlayer.ResizeBounceWindowRemaining = Mathf.Max(0f, m_ResizeBounceInputWindow);
    }

    public void ProcessResizeBounce(ref PredictedPlayerGhost predictedPlayer,
        in PlayerInput input, float deltaTime)
    {
        if (predictedPlayer.ResizeBounceWindowRemaining <= 0f)
        {
            return;
        }

        if (input.Jump)
        {
            ref var state = ref predictedPlayer.ControllerState;
            float bounceSpeed = Mathf.Max(0f, m_ResizeBounceSpeed);
            if (bounceSpeed > 0f)
            {
                state.JumpFallSpeed = bounceSpeed;
                SetMovementType(ref state, MovementType.Jumping);
                state.Jump = true;
                state.JumpTriggered = true;

                if (m_PlayerGhost != null &&
                    m_PlayerGhost.Role == MultiplayerRole.ClientOwned)
                {
                    CameraFovEffect.Trigger(
                        m_ResizeBounceFovKick,
                        m_ResizeBounceFovDuration);
                }
            }

            // A resize can produce only one bounce, even if its height is set to zero.
            predictedPlayer.ResizeBounceWindowRemaining = 0f;
            return;
        }

        predictedPlayer.ResizeBounceWindowRemaining = Mathf.Max(0f,
            predictedPlayer.ResizeBounceWindowRemaining - deltaTime);
    }

    public void GetSizeMovementMultipliers(in PredictedPlayerGhost predictedPlayer,
        out float speedMultiplier, out float jumpHeightMultiplier)
    {
        float sizeMultiplier = m_PlayerGhost != null
            ? m_PlayerGhost.GetTargetScaleMultiplier(in predictedPlayer)
            : 1f;

        speedMultiplier = Mathf.Lerp(1f, sizeMultiplier, m_SizeMovementSpeedInfluence);
        if (predictedPlayer.MoveSlowTimeRemaining > 0f)
        {
            speedMultiplier *= 1f - Mathf.Clamp01(predictedPlayer.MoveSlowPercent);
        }
        jumpHeightMultiplier = Mathf.Lerp(1f, sizeMultiplier, m_SizeJumpHeightInfluence);
    }

    public void ApplyAirborneShotThrust(ref PredictedPlayerGhost predictedPlayer,
        float3 firingDirection)
    {
        float sizeMultiplier = m_PlayerGhost != null
            ? m_PlayerGhost.GetTargetScaleMultiplier(in predictedPlayer)
            : 1f;
        float minimumScale = m_PlayerGhost != null ? m_PlayerGhost.MinimumPlayerScale : 0.2f;
        float smallness = Mathf.InverseLerp(1f, minimumScale, sizeMultiplier);
        float recoilMultiplier = m_RecoilIntensityBySize != null
            ? Mathf.Max(0f, m_RecoilIntensityBySize.Evaluate(smallness))
            : 1f;
        float3 recoilDirection = -math.normalizesafe(firingDirection);

        predictedPlayer.RecoilVelocity += recoilDirection *
                                          (m_AirborneRecoilImpulse * recoilMultiplier);
        float recoilSpeed = math.length(predictedPlayer.RecoilVelocity);
        if (recoilSpeed > m_MaxAirborneRecoilSpeed)
        {
            predictedPlayer.RecoilVelocity = predictedPlayer.RecoilVelocity / recoilSpeed *
                                             m_MaxAirborneRecoilSpeed;
        }
    }

    public void AccumulateAirborneThrust(ref PredictedPlayerGhost predictedPlayer, float deltaTime)
    {
        if (predictedPlayer.ControllerState.MovementType == MovementType.Standing)
        {
            predictedPlayer.RecoilVelocity = float3.zero;
            return;
        }

        predictedPlayer.AccumulatedMovement += predictedPlayer.RecoilVelocity * deltaTime;
        predictedPlayer.RecoilVelocity *= math.exp(-m_AirborneRecoilDamping * deltaTime);
        if (math.lengthsq(predictedPlayer.RecoilVelocity) < 0.0001f)
        {
            predictedPlayer.RecoilVelocity = float3.zero;
        }
    }

    public void ApplyMovementUpdate(ref ControllerState state, in ControllerConsts consts,
        in float3 accumulatedMovement, float deltaTime)
    {
        ApplyMove(ref state, consts, accumulatedMovement, deltaTime);
        GroundedCheck(ref state, consts);

        // cache latest values for access outside of entity data
        CachedJumpFallSpeed = state.JumpFallSpeed;
        CachedFallHeight = state.FallHeight;
    }

    private static void SetMovementType(ref ControllerState state, MovementType type)
    {
        if (state.MovementType != type)
        {
#if ENABLE_MOVEMENT_DIAGNOSTICS
            Debug.Log($"SetMovementType to {(int)type} (from {(int)state.MovementType})");
#endif

            bool wasUpdatingFallHeight = ShouldUpdateFallHeight(state.MovementType);

            state.PreviousMovementType = state.MovementType;
            state.MovementType = type;
            state.TimeInState = 0;

            if (!wasUpdatingFallHeight && ShouldUpdateFallHeight(type))
            {
                state.FallHeight = 0f;
                state.FallTimeoutDelta = float.MaxValue;
                state.Fall = true;
            }

#if DEBUG_RENDER_MOVEMENT
            Debug.DrawLine(state.CurrentPosition - new float3(0.2f, 0f, 0f), 
                    state.CurrentPosition + new float3(0.2f, 0f, 0f), 
                    GetDebugColour(state.MovementType), k_DebugRenderingTimeout);
            Debug.DrawLine(state.CurrentPosition - new float3(0f, 0.2f, 0f), 
                    state.CurrentPosition + new float3(0f, 0.2f, 0f), 
                    GetDebugColour(state.PreviousMovementType), k_DebugRenderingTimeout);
#endif
        }
    }

    private static bool ShouldUpdateFallHeight(MovementType movementType)
    {
        return movementType == MovementType.Falling;
    }

    private struct GroundCollisionVariables
    {
        private RaycastHit m_ClosestHit;
        private RaycastHit m_FlattestHit;

        private readonly float m_FlattestHitDot;

        public Vector3 FlattestHitPoint => m_FlattestHit.point;
        public Vector3 FlattestHitNormal => m_FlattestHit.normal;
        public PhysicsMaterial ClosestHitSurfaceType => m_ClosestHit.collider.sharedMaterial;

        public GroundCollisionVariables(RaycastHit closestHit, RaycastHit flattestHit, float flattestHitDot)
        {
            m_ClosestHit = closestHit;
            m_FlattestHit = flattestHit;
            m_FlattestHitDot = flattestHitDot;
        }
    }

    private bool ShouldUpdateGround(MovementType movementType)
    {
        return movementType != MovementType.Jumping;
    }

    private static Vector3 GetGroundRaycastOrigin(in ControllerState state, in CharacterController controller)
    {
        var origin = state.CurrentPosition;
        var delta = origin - (float3)controller.bounds.center;
        delta.y = 0f;

        origin -= delta;

        return origin;
    }

    private bool UpdateGround(in ControllerState state, in ControllerConsts consts,
        out GroundCollisionVariables groundCollision)
    {
        if (ShouldUpdateGround(state.MovementType))
        {
            var currentPos = GetGroundRaycastOrigin(state, m_Controller);
            // set sphere position, with offset
            var lossyScale = transform.lossyScale;
            var scaledControllerOffset = Vector3.Scale(ControllerOffset, lossyScale);
            var controllerCentre = transform.rotation * scaledControllerOffset;
            var testRadius = m_Controller.radius * Mathf.Max(Mathf.Abs(lossyScale.x), Mathf.Abs(lossyScale.z));
            var scaledGroundedOffset = GroundedOffset * Mathf.Abs(lossyScale.y);
            var testStart = new Vector3(currentPos.x + controllerCentre.x, currentPos.y + +controllerCentre.y,
                currentPos.z + controllerCentre.z);
            var numHits = Physics.SphereCastNonAlloc(testStart, testRadius, Vector3.down,
                m_GroundCheckRaycastResults, scaledGroundedOffset, consts.GroundLayers,
                QueryTriggerInteraction.Ignore);


            // Choose the best hit
            float largestDot = float.MinValue;
            float closestDistSq = float.MaxValue;
            int flattestHitIndex = -1;
            int closestHitIndex = -1;

            for (int i = 0; i < numHits; ++i)
            {
                var raycastResult = m_GroundCheckRaycastResults[i];

                if (GhostGameObject.TryFindGhostGameObject(raycastResult.collider.gameObject, out var ghost) &&
                    !GhostGameObject.BroadClientServerRolesMatch(ghost.Role, PlayerGhost.Role))
                {
                    // not a valid ground for this player (server hitting client object, or vice versa)
                    continue;
                }
#if DEBUG_RENDER_MOVEMENT
                    Debug.DrawLine(raycastResult.point, raycastResult.point + raycastResult.normal, Color.red, k_DebugRenderingTimeout);
#endif

                float dot = math.dot(raycastResult.normal, k_UpVector);

                if (dot > largestDot) //select the most upright normal to avoid issues with corner collisions looking like a slope
                {
                    largestDot = dot;
                    flattestHitIndex = i;
                }

                float distSq = raycastResult.point.sqrMagnitude > 0f
                    ? math.distancesq(raycastResult.point, currentPos)
                    : 0f; //point will be (0,0,0) if the spherecast starts inside the collider

                if (distSq < closestDistSq)
                {
                    closestDistSq = distSq;
                    closestHitIndex = i;
                }
            }

            // Did we discard all of the collisions?
            if (flattestHitIndex >= 0)
            {
                Debug.Assert(closestHitIndex >= 0,
                    "[THIRDPERSONCONTROLLER] flattest hit is valid but closest hit isn't, this shouldn't be possible!");

                RaycastHit flattest = m_GroundCheckRaycastResults[flattestHitIndex];
                RaycastHit closest = m_GroundCheckRaycastResults[closestHitIndex];

                groundCollision = new GroundCollisionVariables(closestHit: closest, flattestHit: flattest,
                    flattestHitDot: largestDot);
                return true;
            }
        }

        groundCollision = new GroundCollisionVariables();
        return false;
    }

    public void UpdateGround(ref ControllerState state, in ControllerConsts consts)
    {
        if (UpdateGround(state, consts, out var groundCollision))
        {
            GroundPhysicsMaterial = groundCollision.ClosestHitSurfaceType;
            state.GroundNormal = groundCollision.FlattestHitNormal;
        }
        else
        {
            GroundPhysicsMaterial = null;
            state.GroundNormal = k_UpVector;
        }
    }

    public void GroundedCheck(ref ControllerState state, in ControllerConsts consts)
    {
        bool isGrounded = UpdateGround(state, consts, out var groundCollision);

        if (isGrounded && state.MovementType == MovementType.Falling)
        {
            state.JumpTimeoutDelta = consts.LandingTimeout;
            state.Land = true;
            state.LandTriggered = true;
            state.Jump = false;
            SetMovementType(ref state, MovementType.Standing);

            bool isClientOwned = (m_PlayerGhost.Role == MultiplayerRole.ClientOwned);
            if (isClientOwned)
            {
                Unity.MP_FPS.EventHandler eventHandler = m_Animator_1P.GetComponent<Unity.MP_FPS.EventHandler>();
                eventHandler.onFootDown = true;
            }
        }
        else if (!isGrounded && state.MovementType == MovementType.Standing)
        {
            SetMovementType(ref state, MovementType.Falling);
        }
        else if (isGrounded && state.MovementType == MovementType.Standing)
        {
            state.Land = false;
        }

        if (isGrounded)
        {
            state.GroundNormal = groundCollision.FlattestHitNormal;
            GroundPhysicsMaterial = groundCollision.ClosestHitSurfaceType;
        }
        else
        {
            state.GroundNormal = k_UpVector;
            GroundPhysicsMaterial = null;
        }
    }

    public void ApplyInterpolatedClientState(ref ControllerState state,
        in ControllerConsts consts, in LocalTransform localTransform, float deltaTime, bool applyAnimation = false)
    {
        // apply smoothed rotation
        state.CurrentPosition = localTransform.Position;
        state.CurrentRotation = localTransform.Rotation;

        transform.SetPositionAndRotation(state.CurrentPosition, state.CurrentRotation);
    }

    public void ApplyAnimatorState(in ControllerState state, in ControllerConsts consts, float deltaTime)
    {
        if (m_PlayerGhost == null || m_PlayerGhost.GhostGameObject == null)
        {
            return; //Return if the ghost has not been linked
        }

        var predictedPlayerGhost = m_PlayerGhost.GhostGameObject.ReadGhostComponentData<PredictedPlayerGhost>();
        bool isClientOwned = (m_PlayerGhost.Role == MultiplayerRole.ClientOwned);
        var animator = isClientOwned ? m_Animator_1P : m_Animator_3P;

        if (animator == null)
            return;

        // Handle one-shot events first
        HandleAnimationEvents(animator, predictedPlayerGhost);

        // Then handle continuous state parameters
        if (isClientOwned)
        {
            // ShrinkCastHeld only exists on the first-person wizard-hands
            // controller. Sending it to remote players' third-person
            // controllers causes an Animator warning every prediction frame.
            animator.SetBool(AnimationParameters.ShrinkCastHeld,
                predictedPlayerGhost.ShrinkCastHeld);

            ApplyFirstPersonMovementAnimation(animator, state);
        }
        else
        {
            ApplyThirdPersonMovementAnimation(animator, state, consts, predictedPlayerGhost.CurrentHealth);
        }
    }

    private void HandleAnimationEvents(Animator animator, in PredictedPlayerGhost ghostState)
    {
        // Shooting
        if (ghostState.LastShotTick > _lastAnimatedShotTick)
        {
            if (!ghostState.ControllerState.IsReloadingState && !animator.GetBool(AnimationParameters.IsShooting))
            {
                if (m_EnableAnimationLogging)
                {
                    Debug.Log($"[ANIMATION] Firing SHOOT trigger at Tick: {ghostState.LastShotTick.ToString()}");
                }

                animator.SetTrigger(AnimationParameters.Shoot);
            }

            _lastAnimatedShotTick = ghostState.LastShotTick;
        }

        // The grow-cast animation begins at windup start. Its projectile is released
        // later by the predicted/server timer at the weapon's configured hand pose.
        if (ghostState.LastGrenadeShotTick > _lastAnimatedGrenadeShotTick)
        {
            if (!ghostState.SecondaryIsReloading)
            {
                animator.SetTrigger(AnimationParameters.ShootGrenade);
            }

            _lastAnimatedGrenadeShotTick = ghostState.LastGrenadeShotTick;
        }

        // Reloading
        if (ghostState.LastReloadTick > _lastAnimatedReloadTick)
        {
            if (m_EnableAnimationLogging)
            {
                Debug.Log($"[ANIMATION] Firing RELOAD trigger at Tick: {ghostState.LastReloadTick.ToString()}");
            }

            animator.SetTrigger(AnimationParameters.Reload);


            var weaponData = WeaponManager.Instance.WeaponRegistry.GetWeaponData(ghostState.EquippedWeaponID);
            if (weaponData != null && weaponData.WeaponReloadSfx != null)
            {
                Unity.MP_FPS.EventHandler eventHandler = animator.GetComponent<Unity.MP_FPS.EventHandler>();
                eventHandler.reloadSFX = weaponData.WeaponReloadSfx;
            }

            _lastAnimatedReloadTick = ghostState.LastReloadTick;
        }

        // Jumping
        if (ghostState.LastJumpTick > _lastAnimatedJumpTick)
        {
            if (m_EnableAnimationLogging)
            {
                Debug.Log($"[ANIMATION] Firing JUMP trigger at Tick: {ghostState.LastJumpTick.ToString()}");
            }

            animator.SetTrigger(AnimationParameters.Jump);
            _lastAnimatedJumpTick = ghostState.LastJumpTick;
        }

        // Landing
        if (ghostState.LastLandTick > _lastAnimatedLandTick)
        {
            if (m_EnableAnimationLogging)
            {
                Debug.Log($"[ANIMATION] Firing LAND trigger at Tick: {ghostState.LastLandTick.ToString()}");
            }

            animator.SetTrigger(AnimationParameters.Land);
            _lastAnimatedLandTick = ghostState.LastLandTick;
        }

        // Hit Reaction
        if (ghostState.LastHitTick > _lastProcessedHitTick)
        {
            if (m_DamageVisualsController != null)
            {
                m_DamageVisualsController.SpawnBloodHitEffect();
            }

            if (m_PlayerGhost.Role == MultiplayerRole.ClientOwned)
            {
                if (m_EnableAnimationLogging)
                {
                    Debug.Log($"[ANIMATION] Firing 1P HIT trigger at Tick: {ghostState.LastHitTick.ToString()}");
                }

                // Handle the 1P visual-only damage vignette
                if (m_DamageVisualsController != null)
                {
                    m_DamageVisualsController.TriggerDamageEffect();
                }
            }
            else
            {
                if (m_EnableAnimationLogging)
                {
                    Debug.Log($"[ANIMATION] Firing 3P HIT trigger at Tick: {ghostState.LastHitTick.ToString()}");
                }

                // Handle the 3P hit animation
                animator.SetTrigger(AnimationParameters.IsHit);
            }

            if (PlayerHitSFX != null)
            {
                GameManager.Instance.SoundSystem.CreateEmitter(PlayerHitSFX, transform.position);
            }

            _lastProcessedHitTick = ghostState.LastHitTick;
        }
    }

    private void ApplyFirstPersonMovementAnimation(Animator animator, in ControllerState state)
    {
        float targetIsMovingValue = (state.AnimatorTargetSpeed != 0.0f) ? 1.0f : 0.0f;
        animator.SetFloat(AnimationParameters.IsMoving, targetIsMovingValue);
    }

    private void ApplyThirdPersonMovementAnimation(Animator animator, in ControllerState state,
        in ControllerConsts consts, float currentHealth)
    {
        float3 relativeSpeed = math.mul(math.inverse(state.CurrentRotation), state.MovementRequest);

        float speedZ = (math.abs(relativeSpeed.z) < 0.01f) ? 0.0f : math.sign(relativeSpeed.z);
        float speedX = (math.abs(relativeSpeed.x) < 0.01f) ? 0.0f : math.sign(relativeSpeed.x);
        animator.SetFloat(AnimationParameters.Speed, speedZ);
        animator.SetFloat(AnimationParameters.StrafeSpeed, speedX);

        // The blend tree parameters select a movement direction, while this parameter controls
        // the cadence of only the locomotion state. This keeps footsteps matched to the actual
        // walk/sprint/shrink-adjusted movement speed without accelerating weapon or hit animations.
        float playbackSpeed = state.MovementSpeed > 0.01f
            ? state.MovementSpeed / math.max(consts.Walk.Speed, 0.01f)
            : 1f;
        animator.SetFloat(AnimationParameters.MovementPlaybackSpeed, playbackSpeed);

        bool isDead = (currentHealth <= 0);
        animator.SetBool(AnimationParameters.IsDead, isDead);
    }

    private void ApplyPosImmediate(in ControllerState state)
    {
        if (m_Controller.enabled)
        {
            m_Controller.enabled = false;
            transform.position = state.CurrentPosition;
            m_Controller.enabled = true;

#if UNITY_EDITOR || DEBUG
            float deltaSqrd = math.distancesq(state.CurrentPosition, transform.position);
            Debug.Assert(deltaSqrd <= k_ResetMovementAdjustEpsilon,
                $"ApplyPosImmediate has failed to move the controller to the correct location (target {state.CurrentPosition} actual {(float3)transform.position} delta {math.sqrt(deltaSqrd)} {state.CurrentPosition - (float3)transform.position} state {state.MovementType} prev {m_PrevMovementType})");
#endif
        }
        else
        {
            // set directly, no physics required
            transform.position = state.CurrentPosition;
        }
    }

    public void ApplyPosRotImmediate(in ControllerState state)
    {
        ApplyPosImmediate(state);
        transform.rotation = state.CurrentRotation;
    }

    private static void GetStateConsts(out ControllerConsts.StateConsts stateConsts, ref ControllerState state,
        in PlayerInput input, in ControllerConsts consts, float deltaTime)
    {
        stateConsts.Speed = 0f;
        stateConsts.RotationSmoothTime = 0f;
        stateConsts.SpeedChangeRate = k_DefaultSpeedChange; //just a large number to make it snap to the 0 speed
        stateConsts.LandingSpeedMult = 1f;
        stateConsts.AnimationMotionScale = 0.4f;

        // Freeze after teleport for a second
        if (state.TeleportFreeze > 0f)
        {
            state.TeleportFreeze -= deltaTime;
        }
        else
        {
            switch (state.MovementType)
            {
                case MovementType.Standing:
                case MovementType.Jumping:
                case MovementType.Falling:
                    stateConsts = consts.Walk;
                    break;
                default:
                    Debug.LogError(
                        $"[THIRDPERSONCONTROLLER] GetStateConsts : Unhandled state {state.MovementType.ToString()}");
                    break;
            }
        }
    }

    private static float3 CalculateMovementFromInput(ref ControllerState state, in ControllerConsts consts,
        in ControllerConsts.StateConsts stateConsts, in PlayerInput input,
        float lookYawOffsetDegrees, bool updateRotation, float deltaTime)
    {
        if (updateRotation)
        {
            state.YawDegrees = input.LookYawPitchDegrees.x + lookYawOffsetDegrees;
            state.PitchDegrees = input.LookYawPitchDegrees.y;
            state.CurrentRotation = quaternion.RotateY(math.radians(state.YawDegrees));
        }

        var rotQuat = state.CurrentRotation; // Use the rotation already calculated above
        var localMove = new float3(input.MoveInput.x, 0f, input.MoveInput.y);

        // Normalize it to get a pure direction vector with a length of 1.
        // This is the crucial step.
        var localDir = math.normalizesafe(localMove);

        // Rotate the pure direction by the character's facing rotation.
        var worldDir = math.mul(rotQuat, localDir);

        // Multiply the pure direction by the final speed calculated in AccumulateMovement.
        return worldDir * state.MovementSpeed;
    }

    private static void AddMovementFromJumpFall(ref float3 moveDelta, in ControllerState state)
    {
        moveDelta.y += state.JumpFallSpeed;
    }

    public static void ProcessInputs(ref ControllerState state, in PlayerInput input, float deltaTime)
    {
    }

    public static void AccumulateMovement(ref ControllerState state,
        ref float3 accumulatedMovement, in PlayerInput input, in ControllerConsts consts,
        float lookYawOffsetDegrees, float speedMultiplier,
        float jumpHeightMultiplier, float deltaTime)
    {
        state.TimeInState += deltaTime;

        AccumulateJumpAndGravity(ref state, input, consts, jumpHeightMultiplier, deltaTime);

        GetStateConsts(out var stateConsts, ref state, in input, in consts, deltaTime);

        var updateRotation = true;

        float modifiedTargetMoveSpeed = stateConsts.Speed * Mathf.Max(0f, speedMultiplier);

        float inputMagnitude = math.length(input.MoveInput);

        // apply analog deadzone
        inputMagnitude = inputMagnitude >= 0.4f ? 1f : 0f;

        float applyTargetSpeed = modifiedTargetMoveSpeed * inputMagnitude;
        float blendAlpha = deltaTime * stateConsts.SpeedChangeRate;

        state.MovementSpeed = applyTargetSpeed;
        state.AnimatorTargetSpeedChangeRate = stateConsts.SpeedChangeRate;
        state.AnimatorTargetSpeed = modifiedTargetMoveSpeed * inputMagnitude;
        state.AnimatorMotionSpeed = inputMagnitude > 0f ? state.MovementSpeed : 1f; //play the idle at 1x

        state.AnimatorMotion = float3.zero;
        state.AnimatorMotionChangeRate = 0f;

        var moveDelta = float3.zero;
        updateRotation &= applyTargetSpeed > 0f;

        switch (state.MovementType)
        {
            case MovementType.Standing:
                {
                    moveDelta = CalculateMovementFromInput(ref state, consts,
                        stateConsts, input, lookYawOffsetDegrees, true, deltaTime);
                    AddMovementFromJumpFall(ref moveDelta, state);
                }
                break;

            case MovementType.Jumping:
            case MovementType.Falling:
                {
                    moveDelta = CalculateMovementFromInput(ref state, consts,
                        stateConsts, input, lookYawOffsetDegrees, true, deltaTime);
                    AddMovementFromJumpFall(ref moveDelta, state);
                }
                break;

            default:
                {
                    Debug.LogError(
                        $"[THIRDPERSONCONTROLLER] AccumulateMovement : Unhandled state {state.MovementType.ToString()}");
                }
                break;
        }

        accumulatedMovement += moveDelta * deltaTime;
        state.MovementRequest = accumulatedMovement;

#if DEBUG_RENDER_MOVEMENT
        Debug.DrawLine(state.CurrentPosition, state.CurrentPosition + accumulatedMovement, GetDebugColour(state.MovementType), k_DebugRenderingTimeout);
#endif
    }

    private void ApplyMove(ref ControllerState state, in ControllerConsts consts, in float3 accumulatedMovement,
        float deltaTime)
    {
        var posDelta = (Vector3)state.CurrentPosition - transform.position;
        if (posDelta.sqrMagnitude > 0f)
        {
            MovementLog($"{name} - Reset Teleport to {state.CurrentPosition} (from {(float3)transform.position})");
            ApplyPosImmediate(state);
        }

        bool allowMove = math.lengthsq(accumulatedMovement) > 0f;
        if (allowMove)
        {
            // apply movement
            var movementToApply = accumulatedMovement;

            // This can be called before the controller is enabled (e.g. on spawn)
            // prevents errors of moving when controller is disabled or GameObject inactive
            if (m_Controller != null && m_Controller.enabled && gameObject.activeInHierarchy)
            {
                // just apply our move as normal
                m_Controller.Move(movementToApply);
            }
        }

        // apply rotation
        transform.rotation = state.CurrentRotation;

        if (ShouldUpdateFallHeight(state.MovementType))
        {
            float fallDist = state.CurrentPosition.y - transform.position.y;
            state.FallHeight += fallDist;
        }

        HandleFirstPersonFootstepSFX(state);

        // store position
        state.CurrentPosition = transform.position;

#if UNITY_EDITOR || DEBUG
        m_PrevMovementType = state.MovementType;
#endif

#if ENABLE_MOVEMENT_DIAGNOSTICS
        if (ServerPlayerMovementSystem.PlayerMovementActive)
        {
            MovementLog($"{name} - server tick {ServerPlayerMovementSystem.PlayerMovementTick} with position {state.CurrentPosition}");
        }
        else
        {
            MovementLog($"{name} - prediction tick {PlayerPredictionSystem.PlayerMovementTick} with position {state.CurrentPosition}");
        }
#endif
    }

#if ENABLE_MOVEMENT_DIAGNOSTICS
    public void OnControllerColliderHit(ControllerColliderHit hit)
    {
        Debug.Log($"[{UnityEngine.Time.frameCount}] OnControllerColliderHit hit {hit.collider.name} ghost = {(GhostGameObject.TryFindGhostGameObject(hit.gameObject, out var ghost) ? ghost.name : "null")}");
    }

#endif

    private void HandleFirstPersonFootstepSFX(ControllerState state)
    {
        if (state.MovementType == MovementType.Standing && m_Controller != null && m_Controller.enabled && gameObject.activeInHierarchy)
        {
            bool isClientOwned = (m_PlayerGhost.Role == MultiplayerRole.ClientOwned);
            if (isClientOwned)
            {
                if (transform.position.x == state.CurrentPosition.x && transform.position.z == state.CurrentPosition.z)
                {
                    footstepTriggerTimer = 0;
                }
                else
                {
                    if (footstepTriggerTimer == 0)
                    {
                        footstepTriggerTimer = Time.time;  // This is called multiple times per update. So I can't use Time.delta
                        footstepStartTimer = footstepTriggerTimer;
                    }
                    footstepTriggerTimer = Time.time;

                    float t = footstepTriggerTimer - footstepStartTimer;
                    if (t >= 0.5f)
                    {
                        footstepStartTimer += 0.5f;
                        Unity.MP_FPS.EventHandler eventHandler = m_Animator_1P.GetComponent<Unity.MP_FPS.EventHandler>();
                        eventHandler.onFootDown = true;
                    }
                }
            }
        }
    }



    private static void AccumulateJumpAndGravity(ref ControllerState state, in PlayerInput input,
        in ControllerConsts consts, float jumpHeightMultiplier, float deltaTime)
    {
        switch (state.MovementType)
        {
            case MovementType.Standing:
                {
                    ClearFallingState(ref state);

                    if (AccumulateJump(ref state, in input, in consts, jumpHeightMultiplier, deltaTime) ||
                        state.TimeInState < consts.LandingTimeout)
                    {
                        //we've just started jumping or we're in the process of landing so apply gravity
                        AccumulateGravity(ref state, in consts, deltaTime);
                    }
                    else
                    {
                        //fully in standing state reset gravity to a minimal fall speed to keep player aligned on ground (necessary for uneven terrain)
                        state.JumpFallSpeed = consts.StandingFallSpeed;
                    }
                }
                break;

            case MovementType.Falling:
            case MovementType.Jumping:
                {
                    state.Jump = true;
                    state.JumpTimeoutDelta = math.max(consts.JumpTimeout, state.JumpTimeoutDelta);

                    // fall timeout
                    if (state.FallTimeoutDelta < consts.FallTimeout)
                    {
                        state.FallTimeoutDelta += deltaTime;
                    }
                    else
                    {
                        state.Fall = true;
                    }

                    AccumulateGravity(ref state, in consts, deltaTime);

                    var newMovementState = state.JumpFallSpeed >= 0f ? MovementType.Jumping : MovementType.Falling;
                    SetMovementType(ref state, newMovementState);
                }
                break;

            default:
                {
                    Debug.LogError(
                        $"[THIRDPERSONCONTROLLER] AccumulateJumpAndGravity : Unhandled state {state.MovementType.ToString()}");
                }
                break;
        }
    }

    private static void ClearFallingState(ref ControllerState state)
    {
        // reset the fall timeout timer
        state.FallTimeoutDelta = 0f;

        // update animator if using character
        state.Jump = false;
        state.Fall = false;
    }

    private static void AccumulateGravity(ref ControllerState state, in ControllerConsts consts, float deltaTime)
    {
        // apply gravity over time if we haven't reached terminal (multiply by delta time twice to linearly speed up over time)
        var terminalVelocity = consts.TerminalVelocity;

        if (state.JumpFallSpeed > terminalVelocity)
        {
            state.JumpFallSpeed += consts.Gravity * deltaTime;
        }

        // Handle the terminal velocity becoming much smaller due to umbrella opening
        else if (state.JumpFallSpeed <= terminalVelocity)
        {
            state.JumpFallSpeed = Mathf.Lerp(state.JumpFallSpeed, terminalVelocity, deltaTime);
        }
    }

    public async void SpawnPredictedProjectile(uint spawnTick, uint weaponId, Vector3 spawnPosition,
        Quaternion spawnRotation, Vector3 targetPosition,
        bool isHoming = false, float homingTurnSpeed = 0f,
        float projectileSpeed = 0f, float projectileScale = 1f,
        bool growsOverDistance = false, int maxBounces = 0,
        float growthPerMetre = 0f, float bounceVelocityRetention = 1f,
        bool hasHomingTarget = true)
    {
        try
        {
            var alreadyExists = false;
            for (var i = 0; i < Projectile.PredictedProjectiles.Count; i++)
            {
                var proj = Projectile.PredictedProjectiles[i];
                if (proj.SpawnTick == spawnTick)
                {
                    alreadyExists = true;
                    break;
                }
            }

            if (alreadyExists)
            {
                return;
            }

            var playerGhost = GetComponent<PlayerGhost>();
            var weaponData = WeaponManager.Instance.WeaponRegistry.GetWeaponData(weaponId);
            var projectilePrefabRef = weaponData?.ProjectileGhostPrefab?.GhostPrefab;

            if (projectilePrefabRef == null || !projectilePrefabRef.RuntimeKeyIsValid() ||
                playerGhost.CameraTarget == null)
            {
                Debug.LogWarning(
                    $"[CLIENT] Cannot spawn predicted projectile for weapon ID {weaponId.ToString()}: prefab ref or camera target is null");
                return;
            }

            // var spawnPosition = playerGhost.CameraTarget.position;
            // var spawnRotation = playerGhost.CameraTarget.rotation;
            //
            var predictedProjectile = await projectilePrefabRef.InstantiateAsync(spawnPosition, spawnRotation).Task;
            if (predictedProjectile == null)
            {
                Debug.LogWarning("[CLIENT] Failed to instantiate predicted projectile");
                return;
            }

            // Addressable instantiation is asynchronous. On a sufficiently fast
            // connection the authoritative ghost can already have its GameObject by
            // the time this predicted visual finishes loading. Keeping both produces
            // a double image/jitter immediately after firing.
            if (AuthoritativeProjectileAlreadyLinked(spawnTick, weaponId))
            {
                predictedProjectile.SetActive(false);
                Destroy(predictedProjectile);
                return;
            }

            predictedProjectile.transform.parent =
                GhostBridgeBootstrap.Instance.ClientGameObjectHierarchy.transform;

            var projectile = predictedProjectile.GetComponent<Projectile>();
            projectile.SetWeaponId(weaponId);
            projectile.SetGuidedTarget(targetPosition, hasHomingTarget);
            projectile.SetRuntimeMotion(isHoming, homingTurnSpeed);
            projectile.SetRuntimeProjectileProperties(projectileSpeed, projectileScale,
                growsOverDistance, maxBounces, growthPerMetre,
                bounceVelocityRetention);

            var projectileInfo = new Projectile.PredictedProjectileInfo
            {
                Instance = predictedProjectile,
                SpawnTick = spawnTick
            };

            Projectile.PredictedProjectiles.Add(projectileInfo);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }

    private static bool AuthoritativeProjectileAlreadyLinked(uint spawnTick, uint weaponId)
    {
        var clientWorld = GhostBridgeBootstrap.Instance?.ClientWorld;
        if (clientWorld == null || !clientWorld.IsCreated)
        {
            return false;
        }

        var entityManager = clientWorld.EntityManager;
        using var networkIdQuery = entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<NetworkId>());
        if (networkIdQuery.IsEmptyIgnoreFilter)
        {
            return false;
        }

        int localNetworkId = networkIdQuery.GetSingleton<NetworkId>().Value;
        using var projectileQuery = entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<Projectile.ProjectileData>(),
            ComponentType.ReadOnly<GhostGameObjectLink>());
        using var projectileData = projectileQuery.ToComponentDataArray<Projectile.ProjectileData>(
            Allocator.Temp);

        foreach (var data in projectileData)
        {
            if (data.SpawnTick == spawnTick && data.WeaponID == weaponId &&
                data.OwnerNetworkId == localNetworkId)
            {
                return true;
            }
        }

        return false;
    }

    private static bool AccumulateJump(ref ControllerState state, in PlayerInput input, in ControllerConsts consts,
        float jumpHeightMultiplier, float deltaTime)
    {
        bool jumped = false;

        if (input.Jump
            && state.JumpTimeoutDelta <= 0f)
        {
            // the square root of H * -2 * G = how much velocity needed to reach desired height
            float scaledJumpHeight = consts.JumpHeight * Mathf.Max(0f, jumpHeightMultiplier);
            state.JumpFallSpeed = math.sqrt(scaledJumpHeight * -2f * consts.Gravity); // 6
            SetMovementType(ref state, MovementType.Jumping);
            state.Jump = true;
            state.JumpTriggered = true;
            jumped = true;

#if DEBUG_RENDER_MOVEMENT
            Debug.DrawLine(state.CurrentPosition - new float3(0.2f, 0f, 0f), state.CurrentPosition + new float3(0.2f, 0f, 0f), Color.black, k_DebugRenderingTimeout);
            Debug.DrawLine(state.CurrentPosition - new float3(0f, 0f, 0.2f), state.CurrentPosition + new float3(0f, 0f, 0.2f), Color.black, k_DebugRenderingTimeout);
#endif
        }

        if (state.JumpTimeoutDelta > 0f)
        {
            state.JumpTimeoutDelta -= deltaTime;
        }

        return jumped;
    }

#if DEBUG_RENDER_MOVEMENT
    private static Color GetDebugColour(MovementType movement)
    {
        var colour = Color.white;

        switch (movement)
        {
            case MovementType.Standing:
                colour = Color.green;
                break;
            case MovementType.Jumping:
                colour = Color.cyan;
                break;
            case MovementType.Falling:
                colour = Color.blue;
                break;
            case MovementType.Sliding:
                colour = Color.yellow;
                break;
            case MovementType.Stationary:
                colour = Color.red;
                break;
            case MovementType.Swimming:
                colour = Color.magenta;
                break;
        }

        return colour;
    }

#endif

    // taken from DOTSSample MathHelper
    // Collection of converted classic Unity (Mathf, Vector3 etc.) + some homegrown math functions using Unity.Mathematics
    // These are made/converted for production and unlike a proper library they are lacking any tests, so use at your own peril!
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float SmoothDampAngle(
        float current,
        float target,
        ref float currentVelocity,
        float smoothTime,
        float maxSpeed,
        float deltaTime)
    {
        target = current + DeltaAngle(current, target);
        float result = SmoothDamp(current, target, ref currentVelocity, smoothTime, maxSpeed, deltaTime);
        result = Repeat360(result);

        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float SmoothDamp(
        float current,
        float target,
        ref float currentVelocity,
        float smoothTime,
        float maxSpeed,
        float deltaTime)
    {
        // Based on Game Programming Gems 4 Chapter 1.10
        smoothTime = math.max(0.0001F, smoothTime);
        float omega = 2F / smoothTime;

        float x = omega * deltaTime;
        float exp = 1F / (1F + x + (0.48F * x * x) + (0.235F * x * x * x));
        float change = current - target;
        float originalTo = target;

        // Clamp maximum speed
        float maxChange = maxSpeed * smoothTime;
        change = math.clamp(change, -maxChange, maxChange);
        target = current - change;

        float temp = (currentVelocity + (omega * change)) * deltaTime;
        currentVelocity = (currentVelocity - (omega * temp)) * exp;
        float result = target + ((change + temp) * exp);

        // Prevent overshooting
        if ((originalTo - current > 0.0F) == (result > originalTo))
        {
            result = originalTo;
            currentVelocity = (result - originalTo) / deltaTime;
        }

        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float DeltaAngle(float current, float target)
    {
        float delta = Repeat360(target - current);

        if (delta > 180.0F)
        {
            delta -= 360.0F;
        }

        float result = delta;

        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Repeat360(float t)
    {
        const float repeat_length = 360f;
        const float inverse_length = 1 / repeat_length;

        return math.clamp(t - (math.floor(t * inverse_length) * repeat_length), 0.0f, repeat_length);
    }

    [System.Diagnostics.Conditional("ENABLE_MOVEMENT_DIAGNOSTICS")]
    public static void MovementLog(string message)
    {
        Debug.Log($"[{UnityEngine.Time.frameCount}] {message}");
    }
}
