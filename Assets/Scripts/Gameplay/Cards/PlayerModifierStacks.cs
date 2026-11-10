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
        [GhostField] public byte LeftWireGuided;
        [GhostField] public byte RightWireGuided;
        [GhostField] public byte LeftSlow;
        [GhostField] public byte RightSlow;
        [GhostField] public byte LeftExplosion;
        [GhostField] public byte RightExplosion;
        [GhostField] public byte LeftScatter;
        [GhostField] public byte RightScatter;
        [GhostField] public byte LeftReloadSpeed;
        [GhostField] public byte RightReloadSpeed;
        [GhostField] public byte LeftMagazineCapacity;
        [GhostField] public byte RightMagazineCapacity;
        [GhostField] public byte ExtraSelfShrinkCharges;
        [GhostField] public byte ExtraSelfGrowCharges;
        [GhostField] public byte ExtraReflectCharges;
        [GhostField] public byte LeftBleed;
        [GhostField] public byte RightBleed;
        [GhostField] public byte LeftBounce;
        [GhostField] public byte RightBounce;
        [GhostField] public byte LeftLifeSiphon;
        [GhostField] public byte RightLifeSiphon;
        [GhostField] public byte BulletSize;
        [GhostField] public byte BulletSpeed;
        [GhostField] public byte BulletGrowth;
        [GhostField] public byte ExtraResizeCharges;
        [GhostField] public byte SlowShield;
        [GhostField] public byte SwapOnReflect;
        [GhostField] public byte ReflectHealing;
        [GhostField] public byte ShieldSpeed;
        [GhostField] public byte BigHealthCapacity;
        [GhostField] public byte SmallHealthCapacity;

        public int GetStacks(CardModifierKind kind, ModifierTarget target)
        {
            return (kind, target) switch
            {
                (CardModifierKind.DamagePercent, ModifierTarget.LeftHand) => LeftDamage,
                (CardModifierKind.DamagePercent, ModifierTarget.RightHand) => RightDamage,
                (CardModifierKind.HomingProjectiles, ModifierTarget.LeftHand) => LeftHoming,
                (CardModifierKind.HomingProjectiles, ModifierTarget.RightHand) => RightHoming,
                (CardModifierKind.WireGuided, ModifierTarget.LeftHand) => LeftWireGuided,
                (CardModifierKind.WireGuided, ModifierTarget.RightHand) => RightWireGuided,
                (CardModifierKind.SlowTarget, ModifierTarget.LeftHand) => LeftSlow,
                (CardModifierKind.SlowTarget, ModifierTarget.RightHand) => RightSlow,
                (CardModifierKind.ExplosionRadius, ModifierTarget.LeftHand) => LeftExplosion,
                (CardModifierKind.ExplosionRadius, ModifierTarget.RightHand) => RightExplosion,
                (CardModifierKind.ScatterWeapon, ModifierTarget.LeftHand) => LeftScatter,
                (CardModifierKind.ScatterWeapon, ModifierTarget.RightHand) => RightScatter,
                (CardModifierKind.ReloadSpeed, ModifierTarget.LeftHand) => LeftReloadSpeed,
                (CardModifierKind.ReloadSpeed, ModifierTarget.RightHand) => RightReloadSpeed,
                (CardModifierKind.MagazineCapacity, ModifierTarget.LeftHand) => LeftMagazineCapacity,
                (CardModifierKind.MagazineCapacity, ModifierTarget.RightHand) => RightMagazineCapacity,
                (CardModifierKind.ExtraSelfShrinkCharge, ModifierTarget.Self) => ExtraSelfShrinkCharges,
                (CardModifierKind.ExtraSelfGrowCharge, ModifierTarget.Self) => ExtraSelfGrowCharges,
                (CardModifierKind.ExtraReflectCharge, ModifierTarget.Self) => ExtraReflectCharges,
                (CardModifierKind.Bleed, ModifierTarget.LeftHand) => LeftBleed,
                (CardModifierKind.Bleed, ModifierTarget.RightHand) => RightBleed,
                (CardModifierKind.Bounce, ModifierTarget.LeftHand) => LeftBounce,
                (CardModifierKind.Bounce, ModifierTarget.RightHand) => RightBounce,
                (CardModifierKind.LifeSiphon, ModifierTarget.LeftHand) => LeftLifeSiphon,
                (CardModifierKind.LifeSiphon, ModifierTarget.RightHand) => RightLifeSiphon,
                (CardModifierKind.BulletSize, ModifierTarget.BothHands) => BulletSize,
                (CardModifierKind.BulletSpeed, ModifierTarget.BothHands) => BulletSpeed,
                (CardModifierKind.BulletGrowth, ModifierTarget.BothHands) => BulletGrowth,
                (CardModifierKind.ReloadSpeed, ModifierTarget.BothHands) => LeftReloadSpeed,
                (CardModifierKind.MagazineCapacity, ModifierTarget.BothHands) => LeftMagazineCapacity,
                (CardModifierKind.ExtraResizeCharge, ModifierTarget.BothHands) => ExtraResizeCharges,
                (CardModifierKind.SlowShield, ModifierTarget.Self) => SlowShield,
                (CardModifierKind.SwapOnReflect, ModifierTarget.Self) => SwapOnReflect,
                (CardModifierKind.ReflectHealing, ModifierTarget.Self) => ReflectHealing,
                (CardModifierKind.ShieldSpeed, ModifierTarget.Self) => ShieldSpeed,
                (CardModifierKind.BigHealthCapacity, ModifierTarget.Self) => BigHealthCapacity,
                (CardModifierKind.SmallHealthCapacity, ModifierTarget.Self) => SmallHealthCapacity,
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
                case (CardModifierKind.WireGuided, ModifierTarget.LeftHand):
                    return Increment(ref LeftWireGuided, limit);
                case (CardModifierKind.WireGuided, ModifierTarget.RightHand):
                    return Increment(ref RightWireGuided, limit);
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
                case (CardModifierKind.ReloadSpeed, ModifierTarget.LeftHand):
                    return Increment(ref LeftReloadSpeed, limit);
                case (CardModifierKind.ReloadSpeed, ModifierTarget.RightHand):
                    return Increment(ref RightReloadSpeed, limit);
                case (CardModifierKind.MagazineCapacity, ModifierTarget.LeftHand):
                    return Increment(ref LeftMagazineCapacity, limit);
                case (CardModifierKind.MagazineCapacity, ModifierTarget.RightHand):
                    return Increment(ref RightMagazineCapacity, limit);
                case (CardModifierKind.ExtraSelfShrinkCharge, ModifierTarget.Self):
                    return Increment(ref ExtraSelfShrinkCharges, limit);
                case (CardModifierKind.ExtraSelfGrowCharge, ModifierTarget.Self):
                    return Increment(ref ExtraSelfGrowCharges, limit);
                case (CardModifierKind.ExtraReflectCharge, ModifierTarget.Self):
                    return Increment(ref ExtraReflectCharges, limit);
                case (CardModifierKind.Bleed, ModifierTarget.LeftHand):
                    return Increment(ref LeftBleed, limit);
                case (CardModifierKind.Bleed, ModifierTarget.RightHand):
                    return Increment(ref RightBleed, limit);
                case (CardModifierKind.Bounce, ModifierTarget.LeftHand):
                    return Increment(ref LeftBounce, limit);
                case (CardModifierKind.Bounce, ModifierTarget.RightHand):
                    return Increment(ref RightBounce, limit);
                case (CardModifierKind.LifeSiphon, ModifierTarget.LeftHand):
                    return Increment(ref LeftLifeSiphon, limit);
                case (CardModifierKind.LifeSiphon, ModifierTarget.RightHand):
                    return Increment(ref RightLifeSiphon, limit);
                case (CardModifierKind.BulletSize, ModifierTarget.BothHands):
                    return Increment(ref BulletSize, limit);
                case (CardModifierKind.BulletSpeed, ModifierTarget.BothHands):
                    return Increment(ref BulletSpeed, limit);
                case (CardModifierKind.BulletGrowth, ModifierTarget.BothHands):
                    return Increment(ref BulletGrowth, limit);
                case (CardModifierKind.ReloadSpeed, ModifierTarget.BothHands):
                    return IncrementBoth(ref LeftReloadSpeed, ref RightReloadSpeed, limit);
                case (CardModifierKind.MagazineCapacity, ModifierTarget.BothHands):
                    return IncrementBoth(ref LeftMagazineCapacity, ref RightMagazineCapacity, limit);
                case (CardModifierKind.ExtraResizeCharge, ModifierTarget.BothHands):
                    return Increment(ref ExtraResizeCharges, limit);
                case (CardModifierKind.SlowShield, ModifierTarget.Self):
                    return Increment(ref SlowShield, limit);
                case (CardModifierKind.SwapOnReflect, ModifierTarget.Self):
                    return Increment(ref SwapOnReflect, limit);
                case (CardModifierKind.ReflectHealing, ModifierTarget.Self):
                    return Increment(ref ReflectHealing, limit);
                case (CardModifierKind.ShieldSpeed, ModifierTarget.Self):
                    return Increment(ref ShieldSpeed, limit);
                case (CardModifierKind.BigHealthCapacity, ModifierTarget.Self):
                    return Increment(ref BigHealthCapacity, limit);
                case (CardModifierKind.SmallHealthCapacity, ModifierTarget.Self):
                    return Increment(ref SmallHealthCapacity, limit);
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

        private static bool IncrementBoth(ref byte left, ref byte right, int limit)
        {
            if (left >= limit || right >= limit) return false;
            left++;
            right++;
            return true;
        }
    }
}
