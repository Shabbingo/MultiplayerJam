namespace Unity.MP_FPS
{
    public enum UtilityAbility
    {
        None = 0,
        PingScan = 1,
        ActiveCamo = 2,
        ForwardCharge = 3,
        MegaJump = 4,
        SmallDash = 5
    }

    public static class UtilityAbilityCatalog
    {
        public static string GetDisplayName(UtilityAbility ability)
        {
            return ability switch
            {
                UtilityAbility.PingScan => "PING SCAN",
                UtilityAbility.ActiveCamo => "ACTIVE CAMO",
                UtilityAbility.ForwardCharge => "FORWARD CHARGE",
                UtilityAbility.MegaJump => "MEGA JUMP",
                UtilityAbility.SmallDash => "SMALL DASH",
                _ => "NO UTILITY"
            };
        }
    }
}
