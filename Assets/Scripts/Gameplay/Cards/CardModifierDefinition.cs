using System;
using UnityEngine;

namespace Unity.MP_FPS.Cards
{
    [Flags]
    public enum ModifierTarget : byte
    {
        None = 0,
        LeftHand = 1 << 0,
        RightHand = 1 << 1,
        Self = 1 << 2
    }

    public enum WeaponHand : byte
    {
        Left,
        Right
    }

    public enum CardCategory : byte
    {
        Unclassified,
        Foundational,
        BuildAround,
        Synergy,
        Utility,
        Wildcard
    }

    public enum CardRarity : byte
    {
        Common = 1,
        Rare = 2,
        Epic = 3
    }

    public enum CardModifierKind : byte
    {
        DamagePercent,
        ExtraSelfShrinkCharge,
        ExtraSelfGrowCharge,
        ExtraReflectCharge,
        HomingProjectiles,
        SlowTarget,
        ExplosionRadius,
        ScatterWeapon,
        ReloadSpeed,
        MagazineCapacity,
        WireGuided,
        Bleed,
        Bounce,
        LifeSiphon,
        BulletSize,
        BulletSpeed,
        BulletGrowth,
        ExtraResizeCharge,
        SlowShield,
        SwapOnReflect,
        ReflectHealing,
        ShieldSpeed,
        BigHealthCapacity,
        SmallHealthCapacity,
        MirrorImage,
        SecondChance,
        NullField,
        ArmourPiercing,
        RapidFire,
        MoreHealth,
        SmallTarget,
        ConservationOfMass,
        HeavyFooted,
        TinyTerror,
        GravityWell,
        LastWord,
        BloodReload,
        GrenadeLauncher,
        ActiveCamoUtility,
        ForwardChargeUtility,
        MegaJumpUtility,
        SmallDashUtility
    }

    [CreateAssetMenu(fileName = "CardModifier", menuName = "Multiplayer Jam/Card Modifier")]
    public sealed class CardModifierDefinition : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField, Min(1)] private int m_CardId = 1;
        [SerializeField] private string m_DisplayName = "New Modifier";
        [SerializeField, TextArea] private string m_Description;
        [SerializeField] private Sprite m_Icon;

        [Header("Drafting")]
        [SerializeField] private CardRarity m_Rarity = CardRarity.Common;
        [SerializeField, Min(0.01f)] private float m_BaseOfferWeight = 1f;
        [SerializeField] private CardCategory m_Category = CardCategory.Unclassified;

        [Header("Effect")]
        [SerializeField] private CardModifierKind m_Kind;
        [SerializeField] private ModifierTarget m_AllowedTargets = ModifierTarget.LeftHand;
        [SerializeField, Tooltip("Meaning depends on the modifier: 0.10 is 10% damage, 1 is one charge or one metre.")]
        private float m_ValuePerStack = 1f;

        [Header("Advanced Effect Tuning")]
        [SerializeField, Min(0f)] private float m_EffectDurationSeconds;
        [SerializeField, Min(0f), Tooltip("World-space radius used by area-based card effects.")]
        private float m_EffectRadiusMetres;
        [SerializeField, Min(0f), Tooltip("Optional inner radius used by effects with two distance bands.")]
        private float m_SecondaryEffectRadiusMetres;
        [SerializeField, Min(0f), Tooltip("Additional base-damage percentage per stack. 0.10 means +10%.")]
        private float m_DamagePercentPerStack;
        [SerializeField, Min(0f), Tooltip("Flat area damage dealt per stack by card-triggered shockwaves.")]
        private float m_AreaDamagePerStack;
        [SerializeField, Min(0f), Tooltip("Multiplier applied to base damage while this card is active.")]
        private float m_BaseDamageMultiplier = 1f;
        [SerializeField, Min(0f), Tooltip("Fire-interval multiplier applied per stack. Values below one increase fire rate.")]
        private float m_FireIntervalMultiplierPerStack = 1f;
        [SerializeField, Min(0f), Tooltip("Movement-speed multiplier applied per stack while the card condition is active.")]
        private float m_MovementSpeedMultiplierPerStack = 1f;
        [SerializeField, Min(0f), Tooltip("Incoming-damage multiplier applied per stack while the card condition is active.")]
        private float m_IncomingDamageMultiplierPerStack = 1f;
        [SerializeField, Min(0f), Tooltip("Reload-time multiplier applied per stack. 1.15 means +15% per stack.")]
        private float m_ReloadTimeMultiplierPerStack = 1f;
        [SerializeField, Range(0.01f, 1f)] private float m_MinimumReloadMultiplier = 0.25f;
        [SerializeField, Tooltip("Magazine rounds added or removed per stack.")]
        private int m_MagazineDeltaPerStack;
        [SerializeField, Min(0f), Tooltip("Projectile scale added per stack. 0.5 means +50% of base scale.")]
        private float m_ProjectileScalePerStack;
        [SerializeField, Min(0f), Tooltip("Projectile-speed multiplier applied per stack. 1.3 is 30% faster, 0.7 is 30% slower, and 1 leaves speed unchanged.")]
        private float m_ProjectileSpeedMultiplierPerStack = 1f;
        [SerializeField, Min(0f), Tooltip("Upward launch velocity added per stack to a lobbed projectile.")]
        private float m_LobUpwardSpeedPerStack;
        [SerializeField, Min(0f), Tooltip("Downward acceleration added per stack to a lobbed projectile.")]
        private float m_ProjectileGravityPerStack;
        [SerializeField, Min(0f), Tooltip("Projectile-scale multiplier per stack.")]
        private float m_ProjectileScaleMultiplierPerStack = 1f;
        [SerializeField, InspectorName("AOE Radius Adjustment Per Stack"),
         Tooltip("Metres added to the explosion radius per stack. Positive values increase AOE; negative values reduce it.")]
        private float m_AoeRadiusAdjustmentPerStack;
        [SerializeField, Min(0f), Tooltip("Explosion-radius multiplier per stack.")]
        private float m_ExplosionRadiusMultiplierPerStack = 1f;
        [SerializeField, Min(0f)] private float m_ScatterHalfAngleDegrees = 7.5f;
        [SerializeField, Range(0f, 1f)] private float m_BounceVelocityRetention = 1f;
        [SerializeField, Range(0f, 1f)] private float m_DamageReductionPercent;
        [SerializeField, Min(0f)] private float m_TeleportFreezeSeconds = 0.1f;

        public int CardId => m_CardId;
        public string DisplayName => m_DisplayName;
        public string Description => m_Description;
        public Sprite Icon => m_Icon;
        public CardRarity Rarity => m_Rarity;
        public int DraftPointCost => m_Rarity switch
        {
            CardRarity.Rare => 2,
            CardRarity.Epic => 3,
            _ => 1
        };
        public float BaseOfferWeight => m_BaseOfferWeight;
        public CardCategory Category => m_Category;
        public string CategoryDisplayName => m_Category switch
        {
            CardCategory.BuildAround => "Build Around",
            CardCategory.Unclassified => "General",
            _ => m_Category.ToString()
        };
        public CardModifierKind Kind => m_Kind;
        public ModifierTarget AllowedTargets => m_AllowedTargets;
        public float ValuePerStack => m_ValuePerStack;
        public float EffectDurationSeconds => m_EffectDurationSeconds;
        public float EffectRadiusMetres => m_EffectRadiusMetres;
        public float SecondaryEffectRadiusMetres => m_SecondaryEffectRadiusMetres;
        public float DamagePercentPerStack => m_DamagePercentPerStack;
        public float AreaDamagePerStack => m_AreaDamagePerStack;
        public float BaseDamageMultiplier => m_BaseDamageMultiplier;
        public float FireIntervalMultiplierPerStack =>
            m_FireIntervalMultiplierPerStack;
        public float MovementSpeedMultiplierPerStack =>
            m_MovementSpeedMultiplierPerStack;
        public float IncomingDamageMultiplierPerStack =>
            m_IncomingDamageMultiplierPerStack;
        public float ReloadTimeMultiplierPerStack => m_ReloadTimeMultiplierPerStack;
        public float MinimumReloadMultiplier => m_MinimumReloadMultiplier;
        public int MagazineDeltaPerStack => m_MagazineDeltaPerStack;
        public float ProjectileScalePerStack => m_ProjectileScalePerStack;
        public float ProjectileSpeedMultiplierPerStack =>
            m_ProjectileSpeedMultiplierPerStack;
        public float LobUpwardSpeedPerStack => m_LobUpwardSpeedPerStack;
        public float ProjectileGravityPerStack => m_ProjectileGravityPerStack;
        public float ProjectileScaleMultiplierPerStack =>
            m_ProjectileScaleMultiplierPerStack;
        public float AoeRadiusAdjustmentPerStack =>
            m_AoeRadiusAdjustmentPerStack;
        public float ExplosionRadiusMultiplierPerStack =>
            m_ExplosionRadiusMultiplierPerStack;
        public float ScatterHalfAngleDegrees => m_ScatterHalfAngleDegrees;
        public float BounceVelocityRetention => m_BounceVelocityRetention;
        public float DamageReductionPercent => m_DamageReductionPercent;
        public float TeleportFreezeSeconds => m_TeleportFreezeSeconds;

        public bool Allows(ModifierTarget target)
        {
            byte targetValue = (byte)target;
            return targetValue != 0 &&
                   (targetValue & (targetValue - 1)) == 0 &&
                   (m_AllowedTargets & target) != 0;
        }

        private void OnValidate()
        {
            m_CardId = Mathf.Max(1, m_CardId);
            if (!System.Enum.IsDefined(typeof(CardRarity), m_Rarity))
                m_Rarity = CardRarity.Common;
            m_BaseOfferWeight = Mathf.Max(0.01f, m_BaseOfferWeight);
            m_EffectDurationSeconds = Mathf.Max(0f, m_EffectDurationSeconds);
            m_EffectRadiusMetres = Mathf.Max(0f, m_EffectRadiusMetres);
            m_SecondaryEffectRadiusMetres = Mathf.Max(0f,
                m_SecondaryEffectRadiusMetres);
            m_DamagePercentPerStack = Mathf.Max(0f, m_DamagePercentPerStack);
            m_AreaDamagePerStack = Mathf.Max(0f, m_AreaDamagePerStack);
            m_BaseDamageMultiplier = Mathf.Max(0f, m_BaseDamageMultiplier);
            m_FireIntervalMultiplierPerStack = Mathf.Max(0f,
                m_FireIntervalMultiplierPerStack);
            m_MovementSpeedMultiplierPerStack = Mathf.Max(0f,
                m_MovementSpeedMultiplierPerStack);
            m_IncomingDamageMultiplierPerStack = Mathf.Max(0f,
                m_IncomingDamageMultiplierPerStack);
            m_ReloadTimeMultiplierPerStack = Mathf.Max(0f,
                m_ReloadTimeMultiplierPerStack);
            m_MinimumReloadMultiplier = Mathf.Clamp(m_MinimumReloadMultiplier,
                0.01f, 1f);
            m_ProjectileScalePerStack = Mathf.Max(0f,
                m_ProjectileScalePerStack);
            m_ProjectileSpeedMultiplierPerStack = Mathf.Max(0f,
                m_ProjectileSpeedMultiplierPerStack);
            m_LobUpwardSpeedPerStack = Mathf.Max(0f,
                m_LobUpwardSpeedPerStack);
            m_ProjectileGravityPerStack = Mathf.Max(0f,
                m_ProjectileGravityPerStack);
            m_ProjectileScaleMultiplierPerStack = Mathf.Max(0f,
                m_ProjectileScaleMultiplierPerStack);
            m_ExplosionRadiusMultiplierPerStack = Mathf.Max(0f,
                m_ExplosionRadiusMultiplierPerStack);
            m_ScatterHalfAngleDegrees = Mathf.Max(0f, m_ScatterHalfAngleDegrees);
            m_BounceVelocityRetention = Mathf.Clamp01(m_BounceVelocityRetention);
            m_DamageReductionPercent = Mathf.Clamp01(m_DamageReductionPercent);
            m_TeleportFreezeSeconds = Mathf.Max(0f, m_TeleportFreezeSeconds);

            // Weapon modifiers are assigned to one hand chosen by the player.
            if (m_Kind is CardModifierKind.BulletSize or
                CardModifierKind.BulletSpeed or
                CardModifierKind.BulletGrowth)
            {
                m_AllowedTargets = ModifierTarget.LeftHand |
                                   ModifierTarget.RightHand;
            }
            else if (m_Kind == CardModifierKind.ExtraResizeCharge)
            {
                m_AllowedTargets = ModifierTarget.Self;
            }
        }
    }
}
