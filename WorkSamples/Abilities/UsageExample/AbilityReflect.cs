namespace MySamples
{
    [System.Serializable]
    public partial class AbilityReflect : AbilityData
    {
        public enum ReflectionType
        {
            Reflect = 0,
            Block = 1,
            LetThrough = 2,
        }
        [Tooltip("How to handle getting hit by a projectile")]
        public ReflectionType projectiles = ReflectionType.Reflect;
        [Tooltip("How to handle getting hit by an ability")]
        public ReflectionType abilities = ReflectionType.Reflect;
        [Tooltip("In case we want to sequence into another ability on reflect. This ability will trigger on a successful reflect")]
        public AssetRefAbilityData onReflect;
        [Tooltip("Does reflecting fast forward our ability?")]
        public QBoolean terminateOnReflect = true;
        [Tooltip("Do want to auto terminate but with a forced delay")]
        public FP terminateDelay = FP._0;
        [Tooltip("How much energy to refund on a reflect")]
        public FP energyRestore = FP._0;

        public override unsafe void StartAbility(Frame f, EntityRef owner, Ability* ability)
        {
            base.StartAbility(f, owner, ability);

            ability->abilityCallbacks |= AbilityCallbacks.AboutToReceiveDamage;

            if (f.Unsafe.TryGetPointer<AbilityInventory>(owner, out var inv))
            {
                inv->abilityCallbacks |= AbilityCallbacks.AboutToReceiveDamage;
            }
        }

        public override unsafe void OnAboutToReceiveAbilityDamage(Frame f, EntityRef owner, EntityRef dealer, DamageResult* damageResult, Ability* ability = default, AssetRefAbilityData abilityRef = default)
        {
            base.OnAboutToReceiveAbilityDamage(f, owner, dealer, damageResult, ability, abilityRef);

            // we're not actively reflecting
            if (ability == default || abilities == ReflectionType.LetThrough) return;

            // not dealing damage
            if (damageResult->value <= FP._0) return;

            damageResult->wasImmuneToIt = true;

            if (abilities == ReflectionType.Block) return;

            if (damageResult->damageZone != default && dealer != default && f.Unsafe.TryGetPointer<Health>(dealer, out var dealerHealth))
            {
                dealerHealth->DealDamage(f, damageResult->damageZone, false);
                ReflectSuccess(f, ability);
            }
        }

        public override unsafe void OnAboutToReceiveGenericDamage(Frame f, EntityRef owner, EntityRef dealer, DamageResult* damageResult, GenericDamageData genericData, Ability* ability = null)
        {
            base.OnAboutToReceiveGenericDamage(f, owner, dealer, damageResult, genericData, ability);

            if (genericData.damageSource != GenericDamageSource.Projectile && genericData.damageSource != GenericDamageSource.AbilityRelated)
            {
                return;
            }

            // we're not actively reflecting
            if (ability == default || owner == default) return;

            // not dealing damage
            if (damageResult->value <= FP._0) return;

            // reflect projectile directly
            if (projectiles != ReflectionType.LetThrough && genericData.damageSource == GenericDamageSource.Projectile)
            {
                damageResult->wasImmuneToIt = true;
                ReflectSuccess(f, ability);
                // we have the projectile
                if (genericData.projectile != default && projectiles == ReflectionType.Reflect)
                {
                    // switcheroo!
                    genericData.projectile->owner = owner;
                    // TODO reflect it AT target
                    genericData.projectile->distanceTraveled = FP._0;
                    genericData.projectile->direction = FPVector2.Rotate(genericData.projectile->direction, FP.Deg2Rad * 180);
                    if (f.Unsafe.TryGetPointer<PlayerFlag>(owner, out var pf))
                    {
                        genericData.projectile->isPlayer = true;
                    }
                    else genericData.projectile->isPlayer = false;
                }
            }
        }

        protected virtual unsafe void ReflectSuccess(Frame f, Ability* ability)
        {
            ability->tempByte++;
            if (terminateOnReflect)
            {
                if (terminateDelay <= FP._0)
                {
                    FastForwardAbility(f, ability);
                }
                else if (ability->inAbilityTimer > terminateDelay)
                {
                    ability->inAbilityTimer = terminateDelay;
                }
            }
        }

        public override unsafe void CleanUp(Frame f, Ability* ability, EntityRef owner)
        {
            base.CleanUp(f, ability, owner);

            if (ability->tempByte > 0 && onReflect != default)
            {
                if (energyRestore > FP._0 && owner != default)
                {
                    if (f.Unsafe.TryGetPointer<Energy>(owner, out var energy))
                    {
                        energy->ChangeEnergy(f, energyRestore, EnergyChangeSource.Ability);
                    }
                }

                SequenceIntoAbility(f, ability, onReflect);
            }
        }
    }
}