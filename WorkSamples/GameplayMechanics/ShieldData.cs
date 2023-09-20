namespace MySamples
{
    public enum ShieldRemovalSource : byte
    {
        Expired = 0,
        ConsumedByDamage = 1,
        Dispelled = 2,
    }

    public unsafe abstract partial class ShieldData
    {
        [Header("Configuration")]
        [Tooltip("The ID you want to associate with the effect, this must match the visual id for the shield status effect to automatically be linked")]
        public int shieldId;

        [Tooltip("The flat value of the shield")]
        public int shieldValue;

        [Tooltip("Ability Power ratio")]
        public FP apRatio;

        [Tooltip("Attack Damage ratio")]
        public FP adRatio;

        [Tooltip("Base duration")]
        public FP shieldDuration;

        [Tooltip("Shield cannot exceed this % of max health")]
        public FP maxHealthLimit = FP._1;

        /// <summary>
        /// Calculates the base shield value for a specific context.
        /// </summary>
        /// <param name="f">Current frame</param>
        /// <param name="owner">The entity owning the shield</param>
        /// <param name="shield">Pointer to the shield component</param>
        /// <returns>The calculated shield integer value</returns>
        public virtual int GetShieldValue(Frame f, EntityRef owner, Shield* shield)
        {
            return shieldValue;
        }

        /// <summary>
        /// Adds or updates a shield on an entity.
        /// </summary>
        /// <param name="f">Current frame</param>
        /// <param name="owner">The entity to apply the shield to</param>
        /// <param name="shield">Pointer to the shield component</param>
        /// <param name="overrideShieldValue">Optional override for the flat amount</param>
        /// <param name="additiveValue">If true, adds to existing value; otherwise replaces it</param>
        public virtual void ApplyShield(Frame f, EntityRef owner, Shield* shield, int overrideShieldValue = 0, bool additiveValue = false)
        {
            shield->shieldId = shieldId;

            int baseAmount = (overrideShieldValue > 0) ? overrideShieldValue : GetShieldValue(f, owner, shield);

            if (additiveValue)
            {
                shield->shieldValue += baseAmount;
            }
            else
            {
                shield->shieldValue = baseAmount;
            }

            ApplyStatBonuses(f, shield);

            if (shield->caster != default && owner != default && owner != shield->caster)
            {
                if (f.Unsafe.TryGetPointer<Health>(owner, out var health))
                {
                    health->RegisterAttacker(f, shield->caster, true);
                }
            }

            shield->timeRemaining = shieldDuration > 0 ? shieldDuration : -FP._1;

            ApplyHealthCap(f, owner, shield);

            f.Events.ShieldEvent(owner, shield->caster, shieldId, ShieldEventType.ShieldAdded);
            OnShieldApplied(f, owner, shield);
        }

        /// <summary>
        /// Removes the shield and triggers the appropriate removal logic based on source.
        /// </summary>
        /// <param name="f">Current frame</param>
        /// <param name="owner">The shield owner</param>
        /// <param name="shield">Pointer to the shield component</param>
        /// <param name="remover">The entity causing the removal</param>
        /// <param name="source">The reason for removal</param>
        public virtual void RemoveShield(Frame f, EntityRef owner, Shield* shield, EntityRef remover, ShieldRemovalSource source)
        {
            f.Events.ShieldEvent(owner, shield->caster, shieldId, ShieldEventType.ShieldRemoved);

            switch (source)
            {
                case ShieldRemovalSource.Expired:
                    OnShieldExpired(f, owner, shield);
                    break;
                case ShieldRemovalSource.ConsumedByDamage:
                    OnShieldConsumed(f, owner, shield);
                    break;
                case ShieldRemovalSource.Dispelled:
                    OnShieldDispelled(f, owner, shield);
                    break;
            }

            OnShieldRemoved(f, owner, shield);
        }

        /// <summary>
        /// Attempts to absorb incoming damage using the shield.
        /// </summary>
        /// <param name="f">Current frame</param>
        /// <param name="owner">Shield owner</param>
        /// <param name="shield">Pointer to shield component</param>
        /// <param name="dealer">Damage dealer</param>
        /// <param name="damageValue">Incoming damage amount</param>
        /// <param name="damagePreMitigation">Raw damage before mitigation</param>
        /// <param name="isCrit">Was this a critical hit</param>
        /// <returns>Tuple containing the remaining unabsorbed damage and the amount absorbed</returns>
        public virtual (int remainder, int absorbed) AbsorbDamage(Frame f, EntityRef owner, Shield* shield, EntityRef dealer, int damageValue, int damagePreMitigation, bool isCrit)
        {
            if (shield->shieldValue < 0)
            {
                shield->shieldValue = 0;
                return (damageValue, 0);
            }

            // Full absorption
            if (shield->shieldValue >= damageValue)
            {
                shield->shieldValue -= damageValue;
                OnAbsorbedDamage(f, owner, shield, dealer, damageValue, damagePreMitigation, isCrit);
                return (0, damageValue);
            }

            // Partial absorption (Break)
            int remainder = damageValue - shield->shieldValue;
            int absorbed = shield->shieldValue;
            shield->shieldValue = 0;
            
            return (remainder, absorbed);
        }

        #region Helper Methods

        /// <summary>
        /// Calculates and applies bonuses from the caster's stats (AP, AD, Shielding Power).
        /// </summary>
        protected virtual void ApplyStatBonuses(Frame f, Shield* shield)
        {
            if (shield->caster == default || !f.Unsafe.TryGetPointer<CharacterProfile>(shield->caster, out var profile))
            {
                return;
            }

            if (apRatio > 0)
            {
                shield->shieldValue += FPMath.RoundToInt(profile->GetStat(f, ProfileHelper.STAT_ABILITY_DAMAGE) * apRatio);
            }

            if (adRatio > 0)
            {
                shield->shieldValue += FPMath.RoundToInt(profile->GetStat(f, ProfileHelper.STAT_ATTACK_DAMAGE) * adRatio);
            }

            // Multiplicative shielding power
            shield->shieldValue += FPMath.RoundToInt(profile->GetStat(f, ProfileHelper.STAT_SHIELDING_POWER) * shield->shieldValue);
        }

        /// <summary>
        /// Clamps the shield value based on the owner's maximum health.
        /// </summary>
        protected virtual void ApplyHealthCap(Frame f, EntityRef owner, Shield* shield)
        {
            if (!f.TryGet<Health>(owner, out var h)) return;

            FP limit = maxHealthLimit * h.maximumHealth;
            if (shield->shieldValue > limit)
            {
                shield->shieldValue = FPMath.RoundToInt(limit);
            }
        }

        #endregion

        #region Callbacks

        /// <summary>
        /// Called when the shield is successfully applied. 
        /// Updates inventories.
        /// </summary>
        protected virtual void OnShieldApplied(Frame f, EntityRef owner, Shield* shield)
        {
            if (f.Unsafe.TryGetPointer<ItemInventory>(owner, out var inventory))
            {
                inventory->OnReceivedShield(f, shield, this);
            }

            if (shield->caster != default && f.Unsafe.TryGetPointer<ItemInventory>(shield->caster, out var cinventory))
            {
                cinventory->OnCastShield(f, owner, shield, this);
            }
        }

        /// <summary>
        /// Called when the shield duration runs out.
        /// </summary>
        protected virtual void OnShieldExpired(Frame f, EntityRef owner, Shield* shield)
        {
            if (f.Unsafe.TryGetPointer<ItemInventory>(owner, out var inventory))
            {
                inventory->OnLostShield(f, shield, this, ShieldRemovalSource.Expired);
            }
        }

        /// <summary>
        /// Called when damage fully depletes the shield.
        /// </summary>
        protected virtual void OnShieldConsumed(Frame f, EntityRef owner, Shield* shield)
        {
            if (f.Unsafe.TryGetPointer<ItemInventory>(owner, out var inventory))
            {
                inventory->OnLostShield(f, shield, this, ShieldRemovalSource.ConsumedByDamage);
            }
        }

        /// <summary>
        /// Called when the shield is forcefully removed via dispelling.
        /// </summary>
        protected virtual void OnShieldDispelled(Frame f, EntityRef owner, Shield* shield)
        {
            if (f.Unsafe.TryGetPointer<ItemInventory>(owner, out var inventory))
            {
                inventory->OnLostShield(f, shield, this, ShieldRemovalSource.Dispelled);
            }
        }

        /// <summary>
        /// General cleanup called after any removal type. 
        /// Cleans up associated visual effects.
        /// </summary>
        protected virtual void OnShieldRemoved(Frame f, EntityRef owner, Shield* shield)
        {
            if (!f.Unsafe.TryGetPointer<EffectHandler>(owner, out var handler)) return;

            var list = handler->GetEffects(f);
            for (int i = 0; i < list.Count; i++)
            {
                if (FPMath.Abs(list[i].effectId - shieldId) <= 2)
                {
                    handler->ConsumeEffect(f, list[i].effectId, EffectConsumeType.ConsumeAll, owner);
                    break;
                }
            }
        }

        /// <summary>
        /// Called every frame the shield is active.
        /// </summary>
        protected virtual void OnShieldUpdate(Frame f, EntityRef owner, Shield* shield)
        {
        }

        /// <summary>
        /// Called when damage is absorbed.
        /// </summary>
        protected virtual void OnAbsorbedDamage(Frame f, EntityRef owner, Shield* shield, EntityRef dealer, int damageValue, int damagePreMitigation, bool isCrit)
        {
        }

        #endregion
    }
}