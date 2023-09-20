
namespace MySamples
{
    [System.Serializable]
    public partial class AbilityModifiesMount : AbilityData
    {
        public enum ToggleMountType : byte
        {
            Mount = 0,
            Dismount = 1,
            Toggle = 2,
        }
        [Header("Mount")]
        [Tooltip("How long before the actual mounting effect occurs")]
        public FP modifyDelay;
        [Tooltip("What kind of interaction will it trigger? Mount / dismount? or toggle")]
        public ToggleMountType toggleType = ToggleMountType.Mount;
        [Tooltip("In case we want the champion to be able to override their default mount with a different one with this ability")]
        public AssetRefMountData mountReplacement;
        [Tooltip("How much energy per second does this drain?")]
        public int energyConsumedWhenToggled = 0;
        public override unsafe void ClickedWhileOnCD(Frame f, Ability* ability, AbilityData abilityData)
        {
            base.ClickedWhileOnCD(f, ability, abilityData);
            if (toggleType == ToggleMountType.Toggle && f.Unsafe.TryGetPointer<Mount>(ability->owner, out var mount) && mount->IsMounted && mount->timer > FP._0_50)
            {
                ModifyMount(f, ability, abilityData);
            }
        }
        protected override unsafe void ConsumeAbilityCost(Frame f, EntityRef owner, Ability* ability, Energy* energy)
        {
            energy->ChangeEnergy(f, -energyConsumedWhenToggled, EnergyChangeSource.Ability);
        }
        public override unsafe void UpdateAbility(Frame f, Ability* ability, AbilityData abilityData)
        {
            base.UpdateAbility(f, ability, abilityData);
            if (ability->timeElapsed > modifyDelay && ability->lastTimeDelayUsed <= modifyDelay)
            {
                ability->lastTimeDelayUsed = ability->timeElapsed;
                ModifyMount(f, ability, abilityData);
            }
        }

        public virtual unsafe void ModifyMount(Frame f, Ability* ability, AbilityData abilityData)
        {
            if (f.Unsafe.TryGetPointer<Mount>(ability->owner, out var mount))
            {
                if (mountReplacement != default && mount->mountData != default)
                {
                    if (mountReplacement.Id != mount->mountData.Id)
                    {
                        mount->mountData = mountReplacement;
                    }
                }
                if (mount->mountData != default)
                {
                    var data = f.FindAsset<MountData>(mount->mountData.Id);
                    if (data != null)
                    {
                        switch (toggleType)
                        {
                            case ToggleMountType.Mount:
                                data.SetMountedState(f, ability->owner, mount, true);
                                break;
                            case ToggleMountType.Dismount:
                                data.SetMountedState(f, ability->owner, mount, false);
                                break;
                            case ToggleMountType.Toggle:
                                data.SetMountedState(f, ability->owner, mount, !mount->IsMounted);
                                break;
                        }
                    }
                }
            }
        }
    }
}