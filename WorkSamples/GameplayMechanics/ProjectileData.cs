namespace MySamples
{
    public unsafe abstract partial class ProjectileData
    {
        [Flags]
        public enum ProjectileFlags : UInt32
        {
            None = 0,
            Bounces = 1,
            RandomizeBounceDirection = 2,
            DestroyOnCollision = 4,
            IsConsideredAnAttack = 8,
            DestroyOnSurfaceCollision = 16,
            DontDealDirectDamage = 32,
            InvincibleToDamageZone = 64,
        }

        [Header("Identity")]
        [Tooltip("Used by the visual simulation to render the correct projectile")]
        public int projectileId;

        [Tooltip("To distinguish what spell triggered this projectile.")]
        public byte inputId;

        [Header("Motion")]
        [Tooltip("The maximum distance the projectile is allowed to travel")]
        public FP distanceLimit = 15;

        [Tooltip("How fast does this projectile travel per second")]
        public FP speed = 10;

        public ProjectileFlags flags;

        [Tooltip("only used with targetting behaviors that use it")]
        public FPVector2 angleVector;

        [Header("Collision")]
        public bool isPlayer;

        [Tooltip("Is this unique to player controlled entities only?")]
        public FP thickness = 0;

        [Tooltip("Who can interact with the projectile")]
        public ProjectileCollision collisionTargets = ProjectileCollision.Enemies;

        [Header("Damage")]
        public int damageValue;
        public FP apRatio;
        public FP adRatio;
        public byte feedbackId;

        [Tooltip("Basically whether this is an attack or ability (for damage calculation purposes)")]
        public AssetRefEffectData[] effects;

        [Tooltip("Target will be marked as already hit by the damage zones if this true AND the target was hit.")]
        public AbilityDamage[] impactDamageZones;

        /// <summary>
        /// Instantiates and initializes a new projectile entity.
        /// </summary>
        /// <param name="f">Current frame</param>
        /// <param name="spawner">Spawning source: usually a player controlled entity or minion</param>
        /// <param name="position">Spawning position</param>
        /// <param name="direction">Initial direction. If default, calculates based on spawner orientation</param>
        /// <param name="dataRef">Asset reference for execution</param>
        /// <param name="abilityId">ID of the triggering ability</param>
        /// <returns>Pointer to the created Projectile component, or null if failed</returns>
        public virtual Projectile* Spawn(Frame f, EntityRef spawner, FPVector2 position, FPVector2 direction = default, AssetRefProjectileData dataRef = default, int abilityId = 0)
        {
            var prototype = f.FindAsset<EntityPrototype>(PrototypeHelper.PATH_PROJECTILE_BASE);
            EntityRef e = PrototypeHelper.CreateEntity(f, prototype, CreationSource.ProjectileAsset);

            if (!f.AddOrGet<Transform2D>(e, out var transform) || !f.AddOrGet<Projectile>(e, out var projectile))
            {
                return default;
            }

            transform->Position = position;
            if (f.Unsafe.TryGetPointer<Transform2D>(spawner, out var spawnerTransform))
            {
                transform->Rotation = spawnerTransform->Rotation;
            }

            projectile->owner = spawner;
            projectile->projectileId = projectileId;
            projectile->abilityId = abilityId;
            projectile->entity = e;
            projectile->projectileData = (dataRef != default) ? dataRef : this;
            projectile->isPlayer = isPlayer;

            Initialize(f, projectile, transform, direction);

            return projectile;
        }

        /// <summary>
        /// Configures the projectile immediately after spawning.
        /// </summary>
        public virtual void Initialize(Frame f, Projectile* projectile, Transform2D* transform, FPVector2 direction = default)
        {
            projectile->projectileId = projectileId;
            
            // Apply speed bonuses
            if (f.Unsafe.TryGetPointer<CharacterProfile>(projectile->owner, out var profile))
            {
                projectile->bonusSpeed = speed * profile->GetStat(f, ProfileHelper.STAT_PROJECTILE_SPEED);
            }

            // Determine Direction
            if (direction == default)
            {
                direction = ResolveDefaultDirection(f, projectile, transform);
            }

            projectile->direction = direction.Normalized;
            projectile->startPosition = transform->Position;
            projectile->original_owner = projectile->owner;

            SetupQueryRays(f, projectile);
            FireLifecycleEvents(f, projectile, transform->Position, ProjectileEventType.Fired);
        }

        /// <summary>
        /// Calculates the maximum travel distance, accounting for owner stats.
        /// </summary>
        protected virtual FP GetDistanceLimit(Frame f, Projectile* projectile)
        {
            if (f.Unsafe.TryGetPointer<CharacterProfile>(projectile->owner, out var profile))
            {
                return distanceLimit + profile->GetStat(f, ProfileHelper.STAT_PROJECTILE_RANGE);
            }
            return distanceLimit;
        }

        /// <summary>
        /// Handles the movement logic for the frame, including overshoot correction.
        /// </summary>
        public virtual void Move(Frame f, Projectile* projectile, Transform2D* transform)
        {
            FPVector2 delta = projectile->direction * (speed + projectile->bonusSpeed) * f.DeltaTime;
            transform->Position += delta;
            projectile->distanceTraveledLastFrame = delta.Magnitude;

            FP limit = GetDistanceLimit(f, projectile);

            // Correct overshoot to clamp to max distance
            if (projectile->distanceTraveled + projectile->distanceTraveledLastFrame > limit)
            {
                FP overshotAmount = (projectile->distanceTraveled + projectile->distanceTraveledLastFrame) - limit;
                transform->Position -= delta.Normalized * overshotAmount;
                projectile->distanceTraveledLastFrame -= overshotAmount;
            }
        }

        /// <summary>
        /// Checks if the projectile has reached its maximum distance and handles termination.
        /// </summary>
        public virtual void MonitorDistanceTraveled(Frame f, Projectile* projectile, Transform2D* transform)
        {
            if (projectile->onLastFrame)
            {
                // Create final impact zones before death
                CreateImpactZones(f, projectile, transform->Position, false);
                Terminate(f, projectile, transform->Position);
            }
            else
            {
                projectile->distanceTraveled += projectile->distanceTraveledLastFrame;
                if (projectile->distanceTraveled >= GetDistanceLimit(f, projectile))
                {
                    projectile->onLastFrame = true;
                }
            }
        }

        /// <summary>
        /// Main entry point for processing broadphase collision results.
        /// </summary>
        public virtual void ProcessCollision(Frame f, Projectile* projectile)
        {
            projectile->SetFrameHits(f);
            var hits = projectile->GetFrameHits(f);
            
            if (flags.HasFlag(ProjectileFlags.DontDealDirectDamage))
            {
                PruneEntityHits(hits);
            }

            if (hits.Count == 0) return;

            var alreadyHit = projectile->GetAlreadyHit(f);
            EntityRef lastOwner = projectile->owner;

            // Calculate average point of impact
            FPVector2 impactPoint = CalculateImpactCentroid(hits);

            bool surfaceHit = false;
            EntityRef hitTarget = default;
            int allyHitCount = 0;

            for (int i = 0; i < hits.Count; i++)
            {
                var currentHit = hits[i];
                var hitEntity = currentHit.Entity;

                if (hitEntity == projectile->owner) continue;

                // Handle Wall/Environment
                if (hitEntity == default)
                {
                    surfaceHit = true;
                    continue;
                }

                if (IsAlreadyHit(alreadyHit, hitEntity)) continue;

                // Handle Entity Interaction
                if (ShouldInteract(f, projectile, hitEntity))
                {
                    if (HitTarget(f, projectile, hitEntity))
                    {
                        hitTarget = hitEntity;
                    }

                    // Abort if ownership changed (reflection/stealing)
                    if (lastOwner != projectile->owner) return;

                    alreadyHit.Add(hitEntity);
                }
                else if (f.Has<Health>(hitEntity))
                {
                    // Track that we hit something alive but ignored it (ally)
                    allyHitCount++;
                }
            }

            // If we only hit allies (and ignored them), stop here.
            if (allyHitCount == hits.Count) return;

            projectile->lastHitPosition = impactPoint;

            // Bounce Logic
            if (hitTarget == default && surfaceHit && flags.HasFlag(ProjectileFlags.Bounces))
            {
                HandleBounce(f, projectile, hits[0], impactPoint);
                return;
            }

            // Post-Collision logic (Damage Zones & Termination)
            CreateImpactZones(f, projectile, impactPoint, true);

            if (hitTarget == default && flags.HasFlag(ProjectileFlags.DestroyOnSurfaceCollision) && surfaceHit)
            {
                FireLifecycleEvents(f, projectile, impactPoint, ProjectileEventType.Hit);
                Terminate(f, projectile, impactPoint, true, hitTarget);
                return;
            }

            if (flags.HasFlag(ProjectileFlags.DestroyOnCollision))
            {
                Terminate(f, projectile, impactPoint, true, hitTarget);
            }
        }

        /// <summary>
        /// Terminates the projectile, firing events and calling cleanup callbacks.
        /// </summary>
        public virtual void Terminate(Frame f, Projectile* projectile, FPVector2 pointOfImpact, bool haveHitSomething = false, EntityRef hitTarget = default)
        {
            if (projectile->isTerminated) return;

            projectile->isTerminated = true;
            var eventType = (hitTarget != default || haveHitSomething) ? ProjectileEventType.Hit : ProjectileEventType.Terminated;

            f.Events.ProjectileEvent(projectile->original_owner, projectile->entity, hitTarget, projectileId, pointOfImpact, eventType);
            f.Signals.ProjectileState(projectile->entity, hitTarget, pointOfImpact, eventType);

            if (f.Unsafe.TryGetPointer<Transform2D>(projectile->entity, out var t))
            {
                t->Position = pointOfImpact;
            }

            OnProjectileTerminated(f, projectile, pointOfImpact, haveHitSomething, hitTarget);
        }

        #region Physics & Math Helpers

        protected virtual FPVector2 ResolveDefaultDirection(Frame f, Projectile* projectile, Transform2D* transform)
        {
            if (f.Unsafe.TryGetPointer<CharacterController>(projectile->owner, out var controller) && controller->state.abilityDirection != default)
            {
                return controller->state.abilityDirection;
            }
            else if (projectile->owner != default && f.TryGet<Transform2D>(projectile->owner, out var ot))
            {
                return ot.Up;
            }
            return transform->Forward;
        }

        protected virtual void SetupQueryRays(Frame f, Projectile* projectile)
        {
            int count = 1;
            if (thickness > FP._0)
            {
                if (thickness > FP._2)
                {
                    count = 1 + FPMath.RoundToInt(thickness / FP._0_75);
                }
                else
                {
                    count = FPMath.CeilToInt(thickness / FP._0_50);
                    if (thickness <= FP._0_50) count = 1 + FPMath.RoundToInt(thickness / FP._0_50);
                }
            }

            var list = projectile->GetQueries(f);
            for (int i = 0; i < count; i++)
            {
                list.Add(0);
            }
        }

        protected FPVector2 RotateVector(FPVector2 v, FP degrees)
        {
            return RotateRadians(v, degrees * FP.Deg2Rad);
        }

        protected FPVector2 RotateRadians(FPVector2 v, FP radians)
        {
            var ca = FPMath.Cos(radians);
            var sa = FPMath.Sin(radians);
            return new FPVector2(ca * v.X - sa * v.Y, sa * v.X + ca * v.Y);
        }

        private FPVector2 CalculateImpactCentroid(System.Collections.Generic.List<Physics2D.Hit> hits)
        {
            if (hits.Count == 0) return default;
            FPVector2 point = hits[0].Point;
            if (hits.Count > 1)
            {
                for (int i = 1; i < hits.Count; i++) point += hits[i].Point;
                point /= hits.Count;
            }
            return point;
        }

        #endregion

        #region Collision Logic Helpers

        protected virtual void HandleBounce(Frame f, Projectile* projectile, Physics2D.Hit hit, FPVector2 impactPoint)
        {
            if (projectile->lastHitPosition != default && FPVector2.Distance(projectile->lastHitPosition, impactPoint) <= FP._1)
            {
                return;
            }

            projectile->lastHitPosition = impactPoint;
            projectile->direction = FPVector2.Reflect(projectile->direction, hit.Normal);
            
            if (flags.HasFlag(ProjectileFlags.RandomizeBounceDirection))
            {
                projectile->direction = FPVector2.Rotate(projectile->direction, f.RNG->Next());
            }

            projectile->distanceTraveledLastFrame = FP._0;
            if (f.Unsafe.TryGetPointer<Transform2D>(projectile->entity, out var transform))
            {
                transform->Position = hit.Point;
            }

            FireLifecycleEvents(f, projectile, hit.Point, ProjectileEventType.Hit);
        }

        private void PruneEntityHits(System.Collections.Generic.List<Physics2D.Hit> hits)
        {
            for (int i = hits.Count - 1; i >= 0; i--)
            {
                if (hits[i].Entity != default) hits.RemoveAt(i);
            }
        }

        private bool IsAlreadyHit(System.Collections.Generic.List<EntityRef> alreadyHit, EntityRef target)
        {
            for (int j = 0; j < alreadyHit.Count; j++)
            {
                if (alreadyHit[j] == target) return true;
            }
            return false;
        }

        private bool ShouldInteract(Frame f, Projectile* projectile, EntityRef target)
        {
            bool areEnemies = AIHelper.AreEnemies(f, target, projectile->owner);
            bool isAPet = f.Has<OwnedByEntity>(target);
            
            // Check pet ignore flags
            if (isAPet && collisionTargets.HasFlag(ProjectileCollision.IgnoreSpawns)) return false;

            // Check alignment flags
            if (areEnemies && collisionTargets.HasFlag(ProjectileCollision.Enemies)) return true;
            if (!areEnemies && collisionTargets.HasFlag(ProjectileCollision.Allies)) return true;

            return false;
        }

        /// <summary>
        /// Calculates the valid feedback ID for the hit event.
        /// </summary>
        public virtual int GetFeedbackId(Frame f, Projectile* projectile)
        {
            return feedbackId;
        }

        /// <summary>
        /// Logic for when the projectile strikes a valid entity target.
        /// </summary>
        public virtual bool HitTarget(Frame f, Projectile* projectile, EntityRef targetRef)
        {
            if (!CanStillHitTargets(f, projectile) || projectile->HasBeenHit(f, targetRef))
            {
                return false;
            }

            if (f.Unsafe.TryGetPointer<Health>(targetRef, out var targetHealth))
            {
                bool areEnemies = AIHelper.AreEnemies(f, targetRef, projectile->owner);

                // Prevent friendly fire unless explicitly allowed/intended
                if (!collisionTargets.HasFlag(ProjectileCollision.DontHurtAllies) || areEnemies)
                {
                    ApplyDamage(f, projectile, targetRef, targetHealth);
                }
                return true;
            }
            return false;
        }

        private void ApplyDamage(Frame f, Projectile* projectile, EntityRef targetRef, Health* targetHealth)
        {
            OnAboutToDamage(f, projectile, targetRef, targetHealth);
            projectile->AddToDamageHistory(f, targetRef);
            
            var category = flags.HasFlag(ProjectileFlags.IsConsideredAnAttack) ? DamageCategory.AttackDamage : DamageCategory.AbilityDamage;
            
            targetHealth->DealDamage(
                projectileId, 
                f, 
                projectile->owner, 
                damageValue + projectile->bonusDamage, 
                apRatio, 
                adRatio, 
                GenericDamageSource.Projectile, 
                GetFeedbackId(f, projectile), 
                false, 
                effects, 
                projectile, 
                category
            );

            OnDealtDamage(f, projectile, targetRef, targetHealth);
        }

        #endregion

        #region Damage Zones & Lifecycle Hooks

        public virtual void CreateImpactZones(Frame f, Projectile* projectile, FPVector2 impactCenter, bool useAlreadyHit)
        {
            if (impactDamageZones == null) return;

            for (int i = 0; i < impactDamageZones.Length; i++)
            {
                if (impactDamageZones[i] == null) continue;

                var damageZoneEntity = DamageZoneHelper.MaterializeDamageZone(projectileId, f, projectile->owner, impactDamageZones[i], impactCenter, projectile, this);
                
                // If projectile is invincible to its own zones, add existing hit targets to the zone's history immediately
                if (flags.HasFlag(ProjectileFlags.InvincibleToDamageZone) && 
                    damageZoneEntity != null && 
                    useAlreadyHit)
                {
                    var alreadyHit = projectile->GetAlreadyHit(f);
                    if (alreadyHit.Count > 0 && f.Unsafe.TryGetPointer<DamageZone>(damageZoneEntity, out var zone))
                    {
                        for (int j = 0; j < alreadyHit.Count; j++)
                        {
                            zone->AddToDamageHistory(f, alreadyHit[j]);
                        }
                    }
                }
            }
        }

        protected virtual void FireLifecycleEvents(Frame f, Projectile* projectile, FPVector2 position, ProjectileEventType type, EntityRef target = default)
        {
            f.Events.ProjectileEvent(projectile->owner, projectile->entity, target, projectileId, position, type);
            f.Signals.ProjectileState(projectile->entity, target, position, type);
        }

        public virtual bool CanStillHitTargets(Frame f, Projectile* projectile)
        {
            return !flags.HasFlag(ProjectileFlags.DontDealDirectDamage);
        }

        /// <summary>
        /// Callback executed when the projectile is destroyed.
        /// </summary>
        protected virtual void OnProjectileTerminated(Frame f, Projectile* projectile, FPVector2 pointOfImpact, bool haveHitSomething, EntityRef hitTarget) { }

        /// <summary>
        /// Callback executed immediately before damage is applied.
        /// </summary>
        public virtual void OnAboutToDamage(Frame f, Projectile* projectile, EntityRef target, Health* targetHealth) { }

        /// <summary>
        /// Callback executed immediately after damage is applied.
        /// </summary>
        public virtual void OnDealtDamage(Frame f, Projectile* projectile, EntityRef target, Health* targetHealth) { }

        #endregion
    }
}