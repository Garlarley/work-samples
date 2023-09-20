namespace MySamples
{
    [System.Serializable]
    public partial class AbilityGrappel : AbilityProjectile
    {
        [Header("Grappel")]
        [Tooltip("How fast do we pull toward our hit target/surface")]
        public FP pullSpeed;
        [Tooltip("How far from the hit point do we stop")]
        public FP stopDist = FP._1_50;
        [Tooltip("Disable gravity while in motion?")]
        public bool gravityOnlyWhileShooting = true;
        [Tooltip("For the visual simulation to use")]
        public int grappelStartAbilityId;
        [Tooltip("Do we want to sequence into another ability if we reached a hit surface")]
        public AssetRefAbilityData onReachedSurface;
        [Tooltip("Do we want to sequence into another ability if we reached a target")]
        public AssetRefAbilityData onReachedTarget;
        [Tooltip("In seconds")]
        public FP terrainCDReductionSeconds;
        [Tooltip("Those effects would be triggered if the grapple was successful")]
        public AssetRefEffectData[] onSuccessfulGrapple;
        public int grappelProjectileId;
        [Tooltip("In case we want the grapple effect to reduce an ability remaining cooldown. Common use: reduce it's own cooldown on surface hit [Nautlis Q time!]")]
        public AssetRefAbilityData abilityIdToReduce;
        public override unsafe void StartAbility(Frame f, EntityRef owner, Ability* ability)
        {

            base.StartAbility(f, owner, ability);
            if (gravityOnlyWhileShooting && f.Unsafe.TryGetPointer<CharacterController>(ability->owner, out var controller))
            {
                controller->GravityActive(true);
            }
            ability->abilityCallbacks |= AbilityCallbacks.ProjectileEvent;

            if (f.Unsafe.TryGetPointer<AbilityInventory>(owner, out var inv))
            {
                inv->abilityCallbacks |= ability->abilityCallbacks;
            }
        }
        public override unsafe void OnProjectileEvent(Frame f, Projectile* projectile, EntityRef hitTarget, Ability* ability, FPVector2 hitPoint, ProjectileEventType type, bool abilityIsActive)
        {
            if (ability == default || abilityIsActive == false) return;
            // interrupted or something happened. shouldn't grappel anymore
            if (ability->hasEnded) return;
            if (projectile->projectileId != grappelProjectileId) return;
            if (ability->tempByte > 0) return;
            base.OnProjectileEvent(f, projectile, hitTarget, ability, hitPoint, type, abilityIsActive);
            if (type == ProjectileEventType.Terminated)
            {
                FastForwardAbility(f, ability);
            }
            if (type != ProjectileEventType.Hit) return;

            f.Unsafe.TryGetPointer<CharacterController>(ability->owner, out var controller);

            // our projectile has hit something, move us to next stage
            ability->tempByte = 1;
            ability->tempVector = hitPoint;
            if (hitTarget != default)
            {
                ability->tempBool = true;
            }
            else
            {
                // we've hit terrain
                ability->tempBool = false;

                if (terrainCDReductionSeconds > FP._0)
                {
                    if (f.Unsafe.TryGetPointer<AbilityInventory>(ability->owner, out var inventory))
                    {
                        inventory->ReduceActiveAbilityRemainingCooldown(f, abilityIdToReduce, terrainCDReductionSeconds);
                    }
                }
            }

            if (onSuccessfulGrapple != null && onSuccessfulGrapple.Length > 0)
            {
                if (f.Unsafe.TryGetPointer<EffectHandler>(projectile->owner, out var handler))
                {
                    handler->ApplyEffects(f, projectile->owner, onSuccessfulGrapple);
                }
            }

            if (controller != default && f.Unsafe.TryGetPointer<Transform2D>(ability->owner, out var transform))
            {
                controller->tempVector = transform->Position;
                if (gravityOnlyWhileShooting) controller->GravityActive(false);
            }
            f.Events.AbilityEvent(ability->owner, abilityInput, AbilityPlaytime.Start, ability->abilitySpeed, grappelStartAbilityId);
        }
        public override unsafe void CleanUp(Frame f, Ability* ability, EntityRef owner)
        {
            if (f.Unsafe.TryGetPointer<CharacterController>(ability->owner, out var controller))
            {
                controller->SetAbilityMotion(f, FPVector2.Zero);
            }
            if (ability->tempByte != 0)
            {
                f.Events.AbilityEvent(ability->owner, abilityInput, AbilityPlaytime.End, ability->abilitySpeed, grappelStartAbilityId);
            }
            if (gravityOnlyWhileShooting) controller->GravityActive(true);
            base.CleanUp(f, ability, owner);
        }

        public override unsafe void UpdateAbility(Frame f, Ability* ability, AbilityData abilityData)
        {
            base.UpdateAbility(f, ability, abilityData);

            if (ability->tempVector == default || ability->tempByte == 0) return;

            if (f.Unsafe.TryGetPointer<CharacterController>(ability->owner, out var controller) == false
                || f.Unsafe.TryGetPointer<Transform2D>(ability->owner, out var transform) == false) return;

            FPVector2 direction = (ability->tempVector - transform->Position).Normalized;
            // can't keep going
            bool overshotOrStop = (controller->tempVector.X > ability->tempVector.X && transform->Position.X < ability->tempVector.X)
                || (controller->tempVector.X < ability->tempVector.X && transform->Position.X > ability->tempVector.X)
                || FPVector2.Distance(transform->Position, ability->tempVector) <= stopDist;
            if (controller->state.IsColliding
                 || ability->tempByte > 4
                || overshotOrStop)
            {
                // place us where we're meant to stop
                if (overshotOrStop)
                {
                    if (controller->tempVector.X > ability->tempVector.X)
                    {
                        transform->Position = new FPVector2(ability->tempVector.X + stopDist, transform->Position.Y);
                    }
                    else
                    {
                        transform->Position = new FPVector2(ability->tempVector.X - stopDist, transform->Position.Y);
                    }
                }
                if (onReachedTarget != default && ability->tempBool) SequenceIntoAbility(f, ability, onReachedTarget);
                else if (onReachedSurface != default && ability->tempBool == false) SequenceIntoAbility(f, ability, onReachedSurface);
                else FastForwardAbility(f, ability);
                return;
            }

            controller->SetAbilityMotion(f, direction * pullSpeed);

            if (controller->Velocity().Magnitude <= FP._0_10)
            {
                ability->tempByte++;
            }
        }
    }
}