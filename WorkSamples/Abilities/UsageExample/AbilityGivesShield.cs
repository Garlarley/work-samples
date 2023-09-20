namespace MySamples
{
    [System.Serializable]
    public partial class AbilityGivesShield : AbilityData
    {
        [Tooltip("Reference to the actual shield effect. This ability only places the shield.")]
        public AssetRefShieldData shieldRef;

        public override unsafe void StartAbility(Frame f, EntityRef owner, Ability* ability)
        {
            base.StartAbility(f, owner, ability);

            ShieldHelper.GiveShield(f, owner, shieldRef, owner);
        }
    }
}