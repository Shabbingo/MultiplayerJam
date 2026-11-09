using Unity.Entities;
using Unity.NetCode;

namespace Unity.MP_FPS.Cards
{
    /// <summary>
    /// Replicated, compact card state for a living player. The server-side run store
    /// restores this component whenever the player's ghost is recreated.
    /// </summary>
    public struct PlayerModifierStacks : IComponentData
    {
        [GhostField] public byte LeftDamage;
        [GhostField] public byte RightDamage;
        [GhostField] public byte LeftHoming;
        [GhostField] public byte RightHoming;
        [GhostField] public byte LeftSlow;
        [GhostField] public byte RightSlow;
        [GhostField] public byte LeftExplosion;
        [GhostField] public byte RightExplosion;
        [GhostField] public byte LeftScatter;
        [GhostField] public byte RightScatter;
        [GhostField] public byte ExtraSelfShrinkCharges;
        [GhostField] public byte ExtraSelfGrowCharges;
        [GhostField] public byte ExtraReflectCharges;

        public int GetStacks(CardModifierKind kind, ModifierTarget target)
        {
            return (kind, target) switch
            {
                (CardModifierKind.DamagePercent, ModifierTarget.LeftHand) => LeftDamage,
                (CardModifierKind.DamagePercent, ModifierTarget.RightHand) => RightDamage,
                (CardModifierKind.HomingProjectiles, ModifierTarget.LeftHand) => LeftHoming,
                (CardModifierKind.HomingProjectiles, ModifierTarget.RightHand) => RightHoming,
                (CardModifierKind.SlowTarget, ModifierTarget.LeftHand) => LeftSlow,
                (CardModifierKind.SlowTarget, ModifierTarget.RightHand) => RightSlow,
                (CardModifierKind.ExplosionRadius, ModifierTarget.LeftHand) => LeftExplosion,
                (CardModifierKind.ExplosionRadius, ModifierTarget.RightHand) => RightExplosion,
                (CardModifierKind.ScatterWeapon, ModifierTarget.LeftHand) => LeftScatter,
                (CardModifierKind.ScatterWeapon, ModifierTarget.RightHand) => RightScatter,
                (CardModifierKind.ExtraSelfShrinkCharge, ModifierTarget.Self) => ExtraSelfShrinkCharges,
                (CardModifierKind.ExtraSelfGrowCharge, ModifierTarget.Self) => ExtraSelfGrowCharges,
                (CardModifierKind.ExtraReflectCharge, ModifierTarget.Self) => ExtraReflectCharges,
                _ => 0
            };
        }

        public bool TryAdd(CardModifierDefinition card, ModifierTarget target)
        {
            if (card == null || !card.Allows(target) ||
                GetStacks(card.Kind, target) >= card.StackLimit)
            {
                return false;
            }

            return TryIncrement(card.Kind, target, card.StackLimit);
        }

        public bool TryIncrement(CardModifierKind kind, ModifierTarget target,
            int stackLimit)
        {
            int limit = Unity.Mathematics.math.clamp(stackLimit, 1, 5);
            switch (kind, target)
            {
                case (CardModifierKind.DamagePercent, ModifierTarget.LeftHand):
                    return Increment(ref LeftDamage, limit);
                case (CardModifierKind.DamagePercent, ModifierTarget.RightHand):
                    return Increment(ref RightDamage, limit);
                case (CardModifierKind.HomingProjectiles, ModifierTarget.LeftHand):
                    return Increment(ref LeftHoming, limit);
                case (CardModifierKind.HomingProjectiles, ModifierTarget.RightHand):
                    return Increment(ref RightHoming, limit);
                case (CardModifierKind.SlowTarget, ModifierTarget.LeftHand):
                    return Increment(ref LeftSlow, limit);
                case (CardModifierKind.SlowTarget, ModifierTarget.RightHand):
                    return Increment(ref RightSlow, limit);
                case (CardModifierKind.ExplosionRadius, ModifierTarget.LeftHand):
                    return Increment(ref LeftExplosion, limit);
                case (CardModifierKind.ExplosionRadius, ModifierTarget.RightHand):
                    return Increment(ref RightExplosion, limit);
                case (CardModifierKind.ScatterWeapon, ModifierTarget.LeftHand):
                    return Increment(ref LeftScatter, limit);
                case (CardModifierKind.ScatterWeapon, ModifierTarget.RightHand):
                    return Increment(ref RightScatter, limit);
                case (CardModifierKind.ExtraSelfShrinkCharge, ModifierTarget.Self):
                    return Increment(ref ExtraSelfShrinkCharges, limit);
                case (CardModifierKind.ExtraSelfGrowCharge, ModifierTarget.Self):
                    return Increment(ref ExtraSelfGrowCharges, limit);
                case (CardModifierKind.ExtraReflectCharge, ModifierTarget.Self):
                    return Increment(ref ExtraReflectCharges, limit);
                default:
                    return false;
            }
        }

        private static bool Increment(ref byte value, int limit)
        {
            if (value >= limit)
            {
                return false;
            }

            value++;
            return true;
        }
    }
}
