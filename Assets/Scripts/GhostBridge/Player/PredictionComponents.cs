using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

using Unity.MP_FPS;

using static FirstPersonController;

public struct PredictedPlayerGhostState : IPredictedState
{
    public uint Tick { get; set; }

    public ControllerState PredictedControllerState;
    public float3 PredictedAccumulatedMovement;
}

public struct PredictedPlayerControllerConsts : IComponentData
{
    public ControllerConsts ControllerConsts;
}

public struct PredictedClientInput : IComponentData
{
    [GhostField]
    public bool SeenNewSnapshot;

    [GhostField]
    public uint LastProcessedServerTick;

    public int BeginInputIndex;
    public int InputCount;
}

public struct PredictedPlayerGhost : IComponentData
{
    public float2 LocalLookYawPitchDegrees;
    public float3 AccumulatedMovement;
    public float3 AppliedError;
    public float ErrorTimeout;
    public float RotationError;
    public float RotationErrorTimeout;
    public bool RequestApplyMovement;

    public float DisabledPredictionLerpFactor;

    public uint ClientPredictionEnabledTick;

    [GhostField] public bool ServerDisabledPrediction;

    [GhostField] public int InputIndex;

    [GhostField] public ControllerState ControllerState;
    [GhostField] public float CurrentHealth;
    [GhostField] public float MaxHealth;

    // Replicated and predicted so each player owns an authoritative shrink resource
    // while the local HUD can respond immediately.
    [GhostField] public float CurrentShrinkResource;
    [GhostField] public float MaxShrinkResource;
    [GhostField] public float ResizeBounceWindowRemaining;
    [GhostField] public float SelfResizeChargeAmount;
    [GhostField] public float SelfResizeMaxCharge;
    [GhostField] public int SelfResizeChargeDirection;
    [GhostField] public int SelfShrinkCharges;
    [GhostField] public int SelfGrowCharges;
    [GhostField] public int MaxSelfShrinkCharges;
    [GhostField] public int MaxSelfGrowCharges;
    [GhostField] public bool SelfResizeAmmoInitialized;
    [GhostField] public float SelfShrinkRechargeTimer;
    [GhostField] public float SelfGrowRechargeTimer;
    [GhostField] public float ReflectShieldTimeRemaining;
    [GhostField] public float ReflectCooldownTimeRemaining;
    [GhostField] public float ReflectCooldownDuration;
    [GhostField] public int ReflectCharges;
    [GhostField] public int MaxReflectCharges;
    [GhostField] public float MoveSlowPercent;
    [GhostField] public float MoveSlowTimeRemaining;
    [GhostField] public float3 RecoilVelocity;
    
    [GhostField] public uint EquippedWeaponID;
    [GhostField] public float WeaponCooldown;   // Timer to control rate of fire
    [GhostField] public float SecondaryWeaponCooldown;
    [GhostField] public float SecondaryReloadTimer;
    [GhostField] public bool SecondaryIsReloading;
    
    [GhostField] public int CurrentAmmo;
    [GhostField] public float LastDamageAmount;
    [GhostField] public uint LastHitTick;
    [GhostField] public uint LastShotTick;
    [GhostField] public bool ShrinkCastHeld;
    [GhostField] public bool GrowShotPending;
    [GhostField] public float GrowShotReleaseTimer;
    [GhostField] public uint LastJumpTick;
    [GhostField] public uint LastLandTick;
    [GhostField] public uint LastReloadTick;
    [GhostField] public uint LastGrenadeShotTick;
    [GhostField] public float ReloadTimer;
}
