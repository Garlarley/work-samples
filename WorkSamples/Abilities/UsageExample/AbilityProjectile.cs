namespace MySamples
{
    [System.Serializable]
    public partial class AbilityProjectile : AbilityData
    {
        [Header("Projectile")]
        [Tooltip("The projectile asset to use. Ability will not work without this")]
        public AssetRefProjectileData projectile;
        [Tooltip("How long into the ability before the actual projectile is fired")]
        public FP projectileDelay;
        [Tooltip("Firing offset - rotated by controller")]
        public FPVector2 offset;
        public override unsafe void StartAbility(Frame f, EntityRef owner, Ability* ability)
        {
            // in case this is a bot using the ability, auto-aim
            UpdateBotAiming(f, owner);

            base.StartAbility(f, owner, ability);
        }
        public override unsafe void UpdateAbility(Frame f, Ability* ability, AbilityData abilityData)
        {
            base.UpdateAbility(f, ability, abilityData);

            if (ability != default && ability->timeElapsed > projectileDelay && ability->lastTimeDelayUsed <= projectileDelay)
            {
                ability->lastTimeDelayUsed = ability->timeElapsed;
                Spawn(f, ability);
            }
        }

        protected unsafe void Spawn(Frame f, Ability* ability)
        {
            if (ability == default) return;

            if (projectile != null && f.Unsafe.TryGetPointer<CharacterController>(ability->owner, out var controller) && f.Unsafe.TryGetPointer<Transform2D>(ability->owner, out var transform))
            {
                var data = f.FindAsset<ProjectileData>(projectile.Id);
                FPVector2 off = FPVector2.Rotate(offset, FPVector2.RadiansSigned(FPVector2.Up, controller->state.direction));
                if (transform != default)
                {
                    CreateProjectile(f, data, ability, transform, off);
                }
            }
        }

        protected virtual unsafe void CreateProjectile(Frame f, ProjectileData data, Ability* ability, Transform2D* transform, FPVector2 offset)
        {
            if (data == null || ability == default) return;

            data.Spawn(f, ability->owner, transform->Position + offset, default, projectile, ability->abilityId);
        }


    }
}