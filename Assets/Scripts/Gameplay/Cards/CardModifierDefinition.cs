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

    public enum CardModifierKind : byte
    {
        DamagePercent,
        ExtraSelfShrinkCharge,
        ExtraSelfGrowCharge,
        ExtraReflectCharge,
        HomingProjectiles,
        SlowTarget,
        ExplosionRadius,
        ScatterWeapon
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

            if (m_Kind is CardModifierKind.HomingProjectiles or
                CardModifierKind.SlowTarget or
                CardModifierKind.ScatterWeapon)
            {
                m_StackLimit = 1;
            }
        }
    }
}
