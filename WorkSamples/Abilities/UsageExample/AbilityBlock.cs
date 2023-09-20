namespace MySamples
{
    [System.Serializable]
    public partial class AbilityBlock : AbilityData
    {
        [Header("Block")]
        [Tooltip("How much of the damage received do we block? 1 = 100%")]
        public FP blockPercentage = FP._1;
        [Tooltip("Are we immune to all negative effects such as CC during this?")]
        public QBoolean immuneToHarmfulEffects = false;
        [Tooltip("Does this alter our movement speed?")]
        public FP movementSpeedChange = FP._0;
        [Tooltip("Does it block projectiles?")]
        public QBoolean blockProjectiles = true;

        // StartAbility method called when the ability is activated
        public override unsafe void StartAbility(Frame f, EntityRef owner, Ability* ability)
        {
            base.StartAbility(f, owner, ability);

            if (f.Unsafe.TryGetPointer<CharacterProfile>(owner, out var profile))
            {
                // In case we want full immunity from all effects, set the flag
                if (immuneToHarmfulEffects)
                {
                    profile->harmfulEffectImmunity++;
                }
                // Modify movement speed if specified
                if (movementSpeedChange != FP._0)
                {
                    var speed = profile->GetStatWithoutTempInfluence(f, ProfileHelper.STAT_MOVESPEED);
                    profile->ChangeStat(f, ProfileHelper.STAT_MOVESPEED, ProfileHelper.VALUEID_EFFECT, speed * movementSpeedChange);
                }
            }

            // We need to listen to damage received callbacks, since that's what we're reacting to
            ability->abilityCallbacks |= AbilityCallbacks.AboutToReceiveDamage;
            if (f.Unsafe.TryGetPointer<AbilityInventory>(owner, out var inv))
            {
                inv->abilityCallbacks |= AbilityCallbacks.AboutToReceiveDamage;
            }
        }

        public override unsafe void OnAboutToReceiveAbilityDamage(Frame f, EntityRef owner, EntityRef dealer, DamageResult* damageResult, Ability* ability, AssetRefAbilityData abilityRef = default)
        {
            base.OnAboutToReceiveAbilityDamage(f, owner, dealer, damageResult, ability, abilityRef);

            // Check if the ability is active; if not, no blocking occurs
            // ability is default (null) only if function is called but ability has already expired
            if (ability == default) return;

            // Check if we are actually going to take damage
            if (damageResult->value <= 0) return;

            // Reduce damage based on blockPercentage
            damageResult->value -= FPMath.RoundToInt(damageResult->value * blockPercentage);
            damageResult->thereWasAnAbsorb = true;
        }

        public override unsafe void OnAboutToReceiveGenericDamage(Frame f, EntityRef owner, EntityRef dealer, DamageResult* damageResult, GenericDamageData genericData, Ability* ability = null)
        {
            base.OnAboutToReceiveGenericDamage(f, owner, dealer, damageResult, genericData, ability);

            if (ability == default) return;

            if (damageResult->value <= 0) return;

            // Check if the damage is caused by a projectile, otherwise it is not what we consider a blockable ability (non-blockable: status effects and generic or AOE damage sources).
            if (genericData.projectile != default)
            {
                // Reduce damage based on blockPercentage for projectile damage
                damageResult->value -= FPMath.RoundToInt(damageResult->value * blockPercentage);
                damageResult->thereWasAnAbsorb = true;
            }
        }

        public override unsafe void CleanUp(Frame f, Ability* ability, EntityRef owner)
        {
            base.CleanUp(f, ability, owner);

            if (f.Unsafe.TryGetPointer<CharacterProfile>(owner, out var profile))
            {
                // Undo setting our immunity flag
                if (immuneToHarmfulEffects)
                {
                    profile->harmfulEffectImmunity--;
                }
                // Restore movement speed if previously modified
                if (movementSpeedChange != FP._0)
                {
                    var speed = profile->GetStatWithoutTempInfluence(f, ProfileHelper.STAT_MOVESPEED);
                    profile->ChangeStat(f, ProfileHelper.STAT_MOVESPEED, ProfileHelper.VALUEID_EFFECT, speed * -movementSpeedChange);
                }
            }
        }
    }
}
