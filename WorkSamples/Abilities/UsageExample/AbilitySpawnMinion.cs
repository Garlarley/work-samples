namespace MySamples
{
    [System.Serializable]
    public partial class AbilitySpawnMinion : AbilitySpawnEntity
    {
        [Header("Bot Data")]
        [Tooltip("If true, we'll use the owner's info, not ours")]
        public bool wereAMinion = false;
        [Tooltip("Use AI on this minion")]
        public bool makeBot = true;
        [Tooltip("Should it auto read the Ability power and Attack damage of it's spawner and use it for itself?")]
        public bool matchOurAPandAD = true;
        [Tooltip("Give minion a portion of our max hp")]
        public FP percentageOfOurMaxHP = FP._0;
        [Tooltip("Remains alive even if our spawner dies")]
        public bool survivesIfOwnerDies = false;

        public override unsafe void UpdateAbility(Frame f, Ability* ability, AbilityData abilityData)
        {
            base.UpdateAbility(f, ability, abilityData);

        }
        protected override unsafe EntityRef SpawnEntity(Frame f, Ability* ability)
        {
            var e = base.SpawnEntity(f, ability);

            if (e != default)
            {
                if (makeBot)
                {
                    if (f.Has<BRBot>(e) == false)
                    {
                        BRBot bot = new BRBot();
                        bot.category = BotCategory.Minion;
                        f.Set(e, bot);
                    }
                }
                if (wereAMinion && f.Unsafe.TryGetPointer<OwnedByEntity>(ability->owner, out var owner) && owner->ownerEntity != default)
                {
                    SetOwnerData(f, e, owner->ownerEntity);
                }
                else
                {
                    SetOwnerData(f, e, ability->owner);
                }
            }

            return e;
        }

        protected unsafe virtual void SetOwnerData(Frame f, EntityRef e, EntityRef owner)
        {
            if (f.Unsafe.TryGetPointer<Team>(e, out var trigger))
            {
                if (f.Unsafe.TryGetPointer<Team>(owner, out var ownerTrigger))
                {
                    trigger->team = ownerTrigger->team;
                }
            }
            if (f.AddOrGet<OwnedByEntity>(e, out var obe))
            {
                if (f.Unsafe.TryGetPointer<OwnedByEntity>(owner, out var ownersOwner) && ownersOwner->ownerEntity != default) obe->ownerEntity = ownersOwner->ownerEntity;
                else obe->ownerEntity = owner;
                obe->survivesIfOwnerDies = survivesIfOwnerDies;
                obe->matchOwnerADandAP = matchOurAPandAD;
                obe->ownerMaxHealth = percentageOfOurMaxHP;
                obe->spawnId = abilityId;
                // also add it here just in-case it happens first
                if ((matchOurAPandAD || obe->ownerMaxHealth > 0) && f.Unsafe.TryGetPointer<CharacterProfile>(e, out var profile) && f.Unsafe.TryGetPointer<CharacterProfile>(owner, out var op))
                {
                    profile->SetStat(f, ProfileHelper.STAT_ABILITY_DAMAGE, ProfileHelper.VALUEID_BASE, op->GetStat(f, ProfileHelper.STAT_ABILITY_DAMAGE));
                    profile->SetStat(f, ProfileHelper.STAT_ATTACK_DAMAGE, ProfileHelper.VALUEID_BASE, op->GetStat(f, ProfileHelper.STAT_ATTACK_DAMAGE));
                    profile->SetStat(f, ProfileHelper.STAT_DAMAGE, ProfileHelper.VALUEID_BASE, op->GetStat(f, ProfileHelper.STAT_DAMAGE));
                    profile->SetStat(f, ProfileHelper.STAT_DAMAGE_DEALT, ProfileHelper.VALUEID_BASE, op->GetStat(f, ProfileHelper.STAT_DAMAGE_DEALT));
                    if (obe->ownerMaxHealth > 0) profile->ChangeStat(f, ProfileHelper.STAT_HEALTH, ProfileHelper.VALUEID_BASE, op->GetHealth(f) * obe->ownerMaxHealth);
                }
            }
        }
    }
}