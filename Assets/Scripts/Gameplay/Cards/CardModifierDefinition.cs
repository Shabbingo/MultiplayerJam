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
        Self = 1 << 2,
        BothHands = 1 << 3
    }

    public enum WeaponHand : byte
    {
        Left,
        Right
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
        SmallHealthCapacity
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
        [SerializeField, Range(1, 5)] private int m_StrengthScore = 1;
        [SerializeField, Min(0.01f)] private float m_BaseOfferWeight = 1f;

        [Header("Effect")]
        [SerializeField] private CardModifierKind m_Kind;
        [SerializeField] private ModifierTarget m_AllowedTargets = ModifierTarget.LeftHand;
        [SerializeField, Range(1, 5)] private int m_StackLimit = 1;
        [SerializeField, Tooltip("Meaning depends on the modifier: 0.10 is 10% damage, 1 is one charge or one metre.")]
        private float m_ValuePerStack = 1f;

        [Header("Advanced Effect Tuning")]
        [SerializeField, Min(0f)] private float m_EffectDurationSeconds;
        [SerializeField, Min(0f), Tooltip("Additional base-damage percentage per stack. 0.10 means +10%.")]
        private float m_DamagePercentPerStack;
        [SerializeField, Min(0f), Tooltip("Multiplier applied to base damage while this card is active.")]
        private float m_BaseDamageMultiplier = 1f;
        [SerializeField, Min(0f), Tooltip("Reload-time multiplier applied per stack. 1.15 means +15% per stack.")]
        private float m_ReloadTimeMultiplierPerStack = 1f;
        [SerializeField, Range(0.01f, 1f)] private float m_MinimumReloadMultiplier = 0.25f;
        [SerializeField, Tooltip("Magazine rounds added or removed per stack.")]
        private int m_MagazineDeltaPerStack;
        [SerializeField, Min(0f), Tooltip("Projectile scale added per stack. 0.5 means +50% of base scale.")]
        private float m_ProjectileScalePerStack;
        [SerializeField, Min(0f), Tooltip("Projectile-speed multiplier applied per stack. 1.3 is 30% faster, 0.7 is 30% slower, and 1 leaves speed unchanged.")]
        private float m_ProjectileSpeedMultiplierPerStack = 1f;
        [SerializeField, Min(0f), Tooltip("Projectile-scale multiplier per stack.")]
        private float m_ProjectileScaleMultiplierPerStack = 1f;
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
        public int StrengthScore => m_StrengthScore;
        public float BaseOfferWeight => m_BaseOfferWeight;
        public CardModifierKind Kind => m_Kind;
        public ModifierTarget AllowedTargets => m_AllowedTargets;
        public int StackLimit => m_StackLimit;
        public float ValuePerStack => m_ValuePerStack;
        public float EffectDurationSeconds => m_EffectDurationSeconds;
        public float DamagePercentPerStack => m_DamagePercentPerStack;
        public float BaseDamageMultiplier => m_BaseDamageMultiplier;
        public float ReloadTimeMultiplierPerStack => m_ReloadTimeMultiplierPerStack;
        public float MinimumReloadMultiplier => m_MinimumReloadMultiplier;
        public int MagazineDeltaPerStack => m_MagazineDeltaPerStack;
        public float ProjectileScalePerStack => m_ProjectileScalePerStack;
        public float ProjectileSpeedMultiplierPerStack =>
            m_ProjectileSpeedMultiplierPerStack;
        public float ProjectileScaleMultiplierPerStack =>
            m_ProjectileScaleMultiplierPerStack;
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
            m_StrengthScore = Mathf.Clamp(m_StrengthScore, 1, 5);
            m_BaseOfferWeight = Mathf.Max(0.01f, m_BaseOfferWeight);
            m_StackLimit = Mathf.Clamp(m_StackLimit, 1, 5);
            m_EffectDurationSeconds = Mathf.Max(0f, m_EffectDurationSeconds);
            m_DamagePercentPerStack = Mathf.Max(0f, m_DamagePercentPerStack);
            m_BaseDamageMultiplier = Mathf.Max(0f, m_BaseDamageMultiplier);
            m_ReloadTimeMultiplierPerStack = Mathf.Max(0f,
                m_ReloadTimeMultiplierPerStack);
            m_MinimumReloadMultiplier = Mathf.Clamp(m_MinimumReloadMultiplier,
                0.01f, 1f);
            m_ProjectileScalePerStack = Mathf.Max(0f,
                m_ProjectileScalePerStack);
            m_ProjectileSpeedMultiplierPerStack = Mathf.Max(0f,
                m_ProjectileSpeedMultiplierPerStack);
            m_ProjectileScaleMultiplierPerStack = Mathf.Max(0f,
                m_ProjectileScaleMultiplierPerStack);
            m_ExplosionRadiusMultiplierPerStack = Mathf.Max(0f,
                m_ExplosionRadiusMultiplierPerStack);
            m_ScatterHalfAngleDegrees = Mathf.Max(0f, m_ScatterHalfAngleDegrees);
            m_BounceVelocityRetention = Mathf.Clamp01(m_BounceVelocityRetention);
            m_DamageReductionPercent = Mathf.Clamp01(m_DamageReductionPercent);
            m_TeleportFreezeSeconds = Mathf.Max(0f, m_TeleportFreezeSeconds);

            if (m_Kind is CardModifierKind.HomingProjectiles or
                CardModifierKind.WireGuided or
                CardModifierKind.SlowTarget or
                CardModifierKind.ScatterWeapon or
                CardModifierKind.Bleed or
                CardModifierKind.LifeSiphon or
                CardModifierKind.BulletGrowth or
                CardModifierKind.SlowShield or
                CardModifierKind.SwapOnReflect or
                CardModifierKind.ReflectHealing or
                CardModifierKind.ShieldSpeed or
                CardModifierKind.BigHealthCapacity or
                CardModifierKind.SmallHealthCapacity)
            {
                m_StackLimit = 1;
            }
        }
    }
}
