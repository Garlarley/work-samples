using System;

namespace MySamples
{
    #region Enums

    [Flags]
    public enum AbilityBotUsage : uint
    {
        None = 0,
        DealsDamage = 1,
        ChaseTarget = 2,
        RunFromTarget = 4,
        CCTarget = 8,
        FinishTarget = 16,
        AvoidAttack = 32,
        ReviveDeadAlly = 64,
        InterruptAbility = 128,
        PassThroughTarget = 256,
        BreakCC = 512,
        UseOffCooldown = 1024,
        RecoverHealth = 2048,
        HealsAlly = 4096,
        AssistsAllyInCombat = 8192,
    }

    [Flags]
    public enum AbilityBotUniqueBehavior : uint
    {
        None = 0,
        KeepFacingTarget = 1,
        WaitStackingHandlerMax = 2,
        KeepMovingTowardTarget = 4,
        ControllableProjectile = 8,
        TryToBounceToTarget = 16,
        JumpIfTargetIsJumping = 32,
        JumpIfAboutToFallOrNotGrounded = 64,
        AimAtTargetFeet = 128,
        PrefersRootedTarget = 256,
        UsableWhileStealth = 512,
        CancelIfNoGround = 1024,
        DoesntRequireVision = 2048,
    }

    [Flags]
    public enum AbilityUserStateRestriction : uint
    {
        None = 0,
        NotCarryingFlag = 1,
        NotInCombat = 2,
        EndOfOptions = 4,
    }

    [Flags]
    public enum AbilitySpecialBehavior : uint
    {
        None = 0,
        DontInterruptAbilities = 1,
        IgnoreAttackSpeed = 2,
        ShowAimLine = 4,
        InvertFacingOnStart = 8,
        IsAlwaysAThreat = 16,
        IsAlwaysAThreatWithinRange = 32,
        CannotTurnDuring = 64,
        ShowUsableInUI = 128,
        ReduceDurationWithFlag = 256,
        UsableWhileDead = 512,
        CanChangeFacingDuring = 1024,
        NotUseableWhileKnockedback = 2048,
        ClickingOnCDInterrupts = 4096,
        DisabledCharacterCollision = 8192,
        CannotBeUsedIfRooted = 16384,
        NonInterruptable = 32768,
        CDScalesWithPetQuality = 65536,
        DoesntBreakBrushStealth = 131072,
        NoAutoAim = 262144,
    }

    [Flags]
    public enum AbilityDamageBehavior : uint
    {
        None = 0,
        FollowOwnerMovement = 1,
        DestroyOnAbilityEnd = 2,
        TerminatesAbility = 4,
        SpawnsOnAbilityEnd = 8,
        SpawnOnAbilityEndIfDealtDamage = 16,
    }

    public enum AbilityButtonType : byte
    {
        Standard = 0,
        Aimable = 1,
        Charged = 2,
        Hold = 3,
    }

    public enum DamageShape : byte
    {
        Box = 0,
        Circle = 1,
    }

    #endregion

    #region Data Structures

    [System.Serializable]
    public partial class AbilityDamage
    {
        public int value;
        public FP apRatio;
        public FP adRatio;
        public FP delay;
        public FP lifespan;
        public byte feedbackId;
        public FP invincibilityDuration;
        public DamageCategory damageCategory = DamageCategory.SameAsAbility;
        public AbilityDamageBehavior behavior;
        public QBoolean followOwnerMovement;
        public QBoolean destroyOnEnd;
        public DamageZoneTargets targetType;
        public DamageShape shape = DamageShape.Box;
        public FPBounds2 bounds;
        public AssetRefEffectData[] effects;
        public FP abilityDirectionBonus;
        [Tooltip("If player didn't provide an aim, what's the default aim")]
        public FP defaultAbilityDirectionBonus = FP._0_50;
    }

    [System.Serializable]
    public partial class MotionData
    {
        public FPVector2 force;
        [Tooltip("How much % of our force lingers after motion is naturally terminated")]
        public FP residualPercentage;
        public FP distance;
        public FP delay;
        public MotionFlags motionFlags;
        public FP stopDistBonus;
    }

    [System.Serializable]
    public class AbilitySequence
    {
        public SequenceDecider decider;
        public AssetRefAbilityData sequencedAbilityRef;
        public FP skippableAfter;
        [Range(0, 100)]
        [DrawIf("decider", 1)]
        public byte diceOdds;
    }

    #endregion

    public abstract partial class AbilityData
    {
        #region Config Fields

        [Header("Identity & Permissions")]
        public int abilityId;
        public CharacterPermissions permissions;
        
        [Header("Type & Input")]
        public AbilityButtonType buttonType;
        public AbilityType abilityType;
        public byte abilityInput;
        public int abilityCost;

        [Header("Timing")]
        public FP cooldown;
        public FP duration;

        [Header("Restrictions & Behaviors")]
        public AbilityUserStateRestriction restrictions;
        public AbilitySpecialBehavior specialBehavior;
        public QBoolean setsAbilityTarget = true;
        public QBoolean dismounts;
        
        [Header("Effects")]
        public AbilityDamage[] damage;
        public QBoolean damageZonesShareHistory;
        public MotionData[] motion;

        [Header("AI Configuration")]
        /// <summary>
        /// How far this ability can reach (used for Bot calculation).
        /// </summary>
        public FP maxReach;
        /// <summary>
        /// Minimum range required for AI to consider using this ability.
        /// </summary>
        public FP minReach;
        public QBoolean dontPredictReach;
        public QBoolean requiresTargetInVision;
        public AbilityBotUsage botUsage;
        public AbilityBotUniqueBehavior botBehavior;

        [Header("Sequencing")]
        public AbilitySequence[] sequence;

        #endregion

        #region Usage Checks

        /// <summary>
        /// Determines if the entity has sufficient resources (Energy) and meets the basic criteria to activate the ability.
        /// </summary>
        public virtual unsafe bool CanUse(Frame f, EntityRef entity)
        {
            // Initial checks for spawn time (prevent instant-cast on spawn)
            if (f.TryGet<OwnedByEntity>(entity, out var obe) && f.Global->time - obe.spawnTime < FP._0_20)
            {
                return false;
            }

            if (!f.Unsafe.TryGetPointer<CharacterController>(entity, out var controller))
            {
                // If there's no controller, we generally can't use an ability.
                return false;
            }

            if (!HasSufficientEnergy(f, entity)) return false;
            if (!CheckContextualRestrictions(f, entity, controller)) return false;
            if (!CheckControllerState(f, controller)) return false;
            
            // External condition check
            if (conditions != null && !MeetsConditions(f, entity, conditions, controller))
            {
                return false;
            }

            return true;
        }

        protected virtual unsafe bool HasSufficientEnergy(Frame f, EntityRef entity)
        {
            if (abilityType != AbilityType.Utility) return true;
            
            if (f.Unsafe.TryGetPointer<Energy>(entity, out var energy))
            {
                return energy->currentValue >= abilityCost;
            }
            return true;
        }

        protected virtual unsafe bool CheckContextualRestrictions(Frame f, EntityRef entity, CharacterController* controller)
        {
            if (restrictions.HasFlag(AbilityUserStateRestriction.NotCarryingFlag) && f.Has<CarriedFlag>(entity))
            {
                return false;
            }

            if (restrictions.HasFlag(AbilityUserStateRestriction.NotInCombat) && controller->IsInCombat(f))
            {
                return false;
            }

            return true;
        }

        protected virtual unsafe bool CheckControllerState(Frame f, CharacterController* controller)
        {
            if (specialBehavior.HasFlag(AbilitySpecialBehavior.CannotBeUsedIfRooted) && controller->parameters.cannotUseMotion)
            {
                return false;
            }

            if (controller->IsDead() && !specialBehavior.HasFlag(AbilitySpecialBehavior.UsableWhileDead))
            {
                return false;
            }

            if (specialBehavior.HasFlag(AbilitySpecialBehavior.UsableWhileDead))
            {
                return true;
            }

            return abilityType switch
            {
                AbilityType.Jump => controller->CanJump(),
                AbilityType.Ability => CanUseAbility(f, controller),
                AbilityType.Attack => CanUseAttack(f, controller),
                AbilityType.Dodge => controller->CanDodge(),
                AbilityType.Utility => CanUseUtility(f, controller),
                _ => false,
            };
        }

        protected virtual unsafe bool CanUseAbility(Frame f, CharacterController* controller)
        {
            bool ignoreKnockback = !specialBehavior.HasFlag(AbilitySpecialBehavior.NotUseableWhileKnockedback);
            return controller->CanUseAbility(ignoreKnockback);
        }

        protected virtual unsafe bool CanUseAttack(Frame f, CharacterController* controller)
        {
            bool ignoreKnockback = !specialBehavior.HasFlag(AbilitySpecialBehavior.NotUseableWhileKnockedback);
            return controller->CanAttack(ignoreKnockback);
        }

        protected virtual unsafe bool CanUseUtility(Frame f, CharacterController* controller)
        {
            bool ignoreKnockback = !specialBehavior.HasFlag(AbilitySpecialBehavior.NotUseableWhileKnockedback);
            return controller->CanUseUtility(ignoreKnockback);
        }

        #endregion

        #region Target Status Checks

        /// <summary>
        /// Cannot move
        /// </summary>
        protected virtual unsafe bool TargetIsImmobile(Frame f, EntityRef e) => TargetIsUnderEffect(f, e, 0);

        /// <summary>
        /// Is under a full stun effect
        /// </summary>
        protected virtual unsafe bool TargetIsStunned(Frame f, EntityRef e) => TargetIsUnderEffect(f, e, 1);

        /// <summary>
        /// Cannot initiate a dodge
        /// </summary>
        protected virtual unsafe bool TargetCannotDodge(Frame f, EntityRef e) => TargetIsUnderEffect(f, e, 2);

        /// <summary>
        /// target is knocked up in the air
        /// </summary>
        protected virtual unsafe bool TargetIsKnockedUp(Frame f, EntityRef e) => TargetIsUnderEffect(f, e, 10);

        protected virtual unsafe bool TargetIsUnderEffect(Frame f, EntityRef e, byte type)
        {
            if (e == default) return false;
            
            var entityToCheck = (target != default) ? target : e;

            if (f.Unsafe.TryGetPointer<CharacterController>(entityToCheck, out var controller))
            {
                return type switch
                {
                    0 => CheckImmobile(f, entityToCheck, controller),
                    1 => controller->IsStunned(),
                    2 => !controller->CanDodge(),
                    _ => false,
                };
            }
            return false;
        }

        private unsafe bool CheckImmobile(Frame f, EntityRef e, CharacterController* controller)
        {
            if (f.Unsafe.TryGetPointer<CharacterProfile>(e, out var profile))
            {
                if (profile->GetStat(f, ProfileHelper.STAT_MOVESPEED) <= FP._0) return true;
            }
            return !controller->CanMove();
        }

        #endregion

        #region Cooldown Management

        /// <summary>
        /// Calculates the effective cooldown considering player stats, ability type, and special behaviors.
        /// </summary>
        public virtual unsafe FP GetCooldown(Frame f, EntityRef owner)
        {
            if (f.RuntimeConfig.gameMode.HasFlag(GameMode.ChampionView))
            {
                return FP._1;
            }

            if (owner == default || !f.Unsafe.TryGetPointer<CharacterProfile>(owner, out var profile))
            {
                return cooldown;
            }

            FP cdrStat = CalculateBaseCdr(f, profile);

            // Cap CDR based on build config
#if !DEBUG
            if (cdrStat > FP._0_75 + FP._0_05) cdrStat = FP._0_75 + FP._0_05;
#else
            if (cdrStat > FP._0_99) cdrStat = FP._0_99;
#endif
            
            FP effectiveCooldown = cooldown * (1 - cdrStat);

            if (specialBehavior.HasFlag(AbilitySpecialBehavior.CDScalesWithPetQuality))
            {
                effectiveCooldown *= GetPetCooldownFactor(profile->petQuality);
            }

            return effectiveCooldown;
        }

        /// <summary>
        /// Calculates cooldown specifically for UI display purposes (no gameplay simulation side effects).
        /// </summary>
        public virtual unsafe FP GetCharacterUICooldown(Frame f, EntityRef owner)
        {
            if (owner != default && f.Unsafe.TryGetPointer<CharacterProfile>(owner, out var profile))
            {
                FP cdrStat = CalculateBaseCdr(f, profile);
                
                // Visual clamp for UI
                if (cdrStat > FP._0_75) cdrStat -= FP._1;
                if (cdrStat > 1) cdrStat = 1;

                return cooldown * (1 - cdrStat);
            }
            return cooldown;
        }

        private unsafe FP CalculateBaseCdr(Frame f, CharacterProfile* profile)
        {
            if (abilityType == AbilityType.Attack) return FP._0;

            FP cdr = profile->GetStat(f, ProfileHelper.STAT_CDR);
            if (abilityInput == 6) // Ultimate
            {
                cdr += profile->GetStat(f, ProfileHelper.STAT_ULTIMATE_CDR);
            }
            return cdr;
        }

        private FP GetPetCooldownFactor(int petQuality)
        {
            FP factor;
            switch (petQuality)
            {
                case 1: factor = FP._0_95; break; // 0.75 + 0.20
                case 2: factor = FP._0_90; break; // 0.75 + 0.20 - 0.05
                case 3: factor = FP._0_80; break; // 0.75 + 0.05
                case 4: factor = FP._0_70; break; // 0.50 + 0.20
                case 5: factor = FP._0_60; break; // 0.50 + 0.10
                case 6: factor = FP._0_55; break; // 0.50 + 0.05
                case 7: factor = FP._0_45; break; // 0.50 - 0.05
                case 8: factor = FP._0_40; break; // 0.50 - 0.10
                case 0: factor = 1; break;
                default: factor = FP._0_33; break;
            }

            return FPMath.Clamp(factor, FP._0_33, FP._1);
        }

        #endregion

        #region Execution Cycle (Start, Update, End)

        /// <summary>
        /// Initializes the ability, consumes costs, sets permissions, and triggers start events.
        /// </summary>
        public virtual unsafe void StartAbility(Frame f, EntityRef owner, Ability* ability)
        {
            if (ability->hasEnded) return;

            // Resource Consumption
            if (abilityType == AbilityType.Utility && f.Unsafe.TryGetPointer<Energy>(owner, out var energy))
            {
                ConsumeAbilityCost(f, owner, ability, energy);
            }

            InterruptPreviousAbilityBeforeStart(f, owner, ability);
            InitializeAbilityState(f, owner, ability);
            HandleOwnerControllerState(f, owner, ability);
            HandleMountState(f, owner, ability);

            // Create Zones
            CreateDamageZones(f, owner, ability);
            CreateMotionZones(f, owner, ability);
            LinkDamageZoneHistory(f, owner, ability);

            // Events & Signals
            f.Events.AbilityEvent(ability->owner, abilityInput, AbilityPlaytime.Start, ability->abilitySpeed, abilityId);
            f.Signals.AbilityStarted(ability, this);
            AbilityInventory.TriggerUpdateUIEvent(f, ability->owner);
            
            // Permissions & Logic
            ChangePermissions(f, ability, ability->owner, true);
            ConsiderBreakingBrushStealth(f, owner, ability);
            RegisterSequenceCallbacks(f, owner, ability);
        }

        private unsafe void InitializeAbilityState(Frame f, EntityRef owner, Ability* ability)
        {
            if (f.Unsafe.TryGetPointer<Playable>(owner, out var playable))
            {
                ability->isBot = playable->isBot;
            }

            // Speed & Timing
            ability->abilitySpeed = GetAbilitySpeed(f, owner, ability);
            
            if (abilityType == AbilityType.Attack && !specialBehavior.HasFlag(AbilitySpecialBehavior.IgnoreAttackSpeed) && f.Unsafe.TryGetPointer<CharacterProfile>(owner, out var profile))
            {
                ability->abilitySpeed = profile->GetStat(f, ProfileHelper.STAT_ATTACK_SPEED);
            }

            ability->cooldownTimer = GetCooldown(f, owner);
            
            bool reduceDuration = specialBehavior.HasFlag(AbilitySpecialBehavior.ReduceDurationWithFlag) && f.Has<CarriedFlag>(owner);
            ability->inAbilityTimer = reduceDuration ? (duration / FP._2_50) : duration;
            
            ability->lastTimeDelayUsed = 0;
            ability->timeElapsed = 0;
            ability->abilityId = abilityId;
            ability->abilityInput = abilityInput;
            ability->mark = AbilityMark.None;
            ability->owner = owner;
        }

        private unsafe void HandleOwnerControllerState(Frame f, EntityRef owner, Ability* ability)
        {
            if (f.Unsafe.TryGetPointer<CharacterController>(owner, out var controller))
            {
                controller->parameters.distanceCrossedDuringLastAbility = FP._0;

                if (specialBehavior.HasFlag(AbilitySpecialBehavior.DisabledCharacterCollision))
                {
                    controller->permissions.noCharacterCollision++;
                }

                FaceIntendedPosition(f, ability, owner, controller);

                // Lock turning AFTER facing the intended position
                if (specialBehavior.HasFlag(AbilitySpecialBehavior.CannotTurnDuring))
                {
                    controller->state.cannotTurn = true;
                }
            }
        }

        private unsafe void HandleMountState(Frame f, EntityRef owner, Ability* ability)
        {
            if (!dismounts) return;

            if (f.Unsafe.TryGetPointer<Mount>(ability->owner, out var mount) && mount->mountData.Id != 0)
            {
                var data = f.FindAsset<MountData>(mount->mountData.Id);
                data?.SetMountedState(f, owner, mount, false);
            }
        }

        private unsafe void RegisterSequenceCallbacks(Frame f, EntityRef owner, Ability* ability)
        {
            if (sequence == null) return;

            for (int i = 0; i < sequence.Length; i++)
            {
                if (sequence[i] == null) continue;

                if (sequence[i].decider == SequenceDecider.DealtDamage)
                {
                    ability->abilityCallbacks |= AbilityCallbacks.DealtDamage;
                    if (f.Unsafe.TryGetPointer<AbilityInventory>(owner, out var inv)) inv->abilityCallbacks |= AbilityCallbacks.DealtDamage;
                }
                else if (sequence[i].decider == SequenceDecider.ReceivedDamage)
                {
                    ability->abilityCallbacks |= AbilityCallbacks.ReceivedDamage;
                    if (f.Unsafe.TryGetPointer<AbilityInventory>(owner, out var inv)) inv->abilityCallbacks |= AbilityCallbacks.ReceivedDamage;
                }
            }
        }

        /// <summary>
        /// Updates the ability logic per frame. Handles bot behavior logic and sequencing checks.
        /// </summary>
        public virtual unsafe void UpdateAbility(Frame f, Ability* ability, AbilityData abilityData)
        {
            if (ability->hasEnded) return;

            if (ability->isBot)
            {
                if (f.Unsafe.TryGetPointer<CharacterController>(ability->owner, out var controller)
                    && f.Unsafe.TryGetPointer<Transform2D>(ability->owner, out var transform)
                    && f.Unsafe.TryGetPointer<Playable>(ability->owner, out var playable))
                {
                    HandleUniqueBotBehavior(f, ability, abilityData, controller, transform, playable);
                }
            }
            else
            {
                // Player Logic
                if (buttonType == AbilityButtonType.Hold)
                {
                    if (f.Unsafe.TryGetPointer<CharacterController>(ability->owner, out var controller) 
                        && f.Unsafe.TryGetPointer<Playable>(ability->owner, out var playable))
                    {
                        var input = PlayableHelper.GetInput(f, playable);
                        if (controller->CanRotate() && input->AbilityDirection != default)
                        {
                            controller->RotateController(f, input->AbilityDirection);
                            controller->state.abilityDirection = input->AbilityDirection;
                        }
                    }
                }
            }

            SequenceIfNeeded(f, ability, false);
        }

        /// <summary>
        /// Terminates the ability, triggering end events, cleaning up pointers, and creating end-of-ability damage zones.
        /// </summary>
        public virtual unsafe void EndAbility(Frame f, Ability* ability)
        {
            if (ability->hasEnded) return;

            f.Signals.AbilityEnded(ability, this);
            SequenceIfNeeded(f, ability, true);
        }

        /// <summary>
        /// Performs final cleanup of the ability state, resetting permissions and removing temporary effects.
        /// </summary>
        public virtual unsafe void CleanUp(Frame f, Ability* ability, EntityRef owner)
        {
            if (ability->hasEnded) return;
            ability->hasEnded = true;

            // Reset Bot Input
            if (f.Unsafe.TryGetPointer<Playable>(owner, out var playable) && playable->isBot)
            {
                playable->botData.botInput.AbilityDirection = FPVector2.Zero;
            }

            RecordUsageStats(f, owner);
            ChangePermissions(f, ability, owner, false);
            CreateDamageZonesAtEnd(f, owner, ability);

            // Cleanup Controller State
            if (f.Unsafe.TryGetPointer<CharacterController>(owner, out var controller))
            {
                if (specialBehavior.HasFlag(AbilitySpecialBehavior.DisabledCharacterCollision))
                {
                    controller->permissions.noCharacterCollision--;
                }
                if (specialBehavior.HasFlag(AbilitySpecialBehavior.CannotTurnDuring))
                {
                    controller->state.cannotTurn = false;
                }
                if (motion.Length > 0)
                {
                    controller->parameters.abilityMotion = FPVector2.Zero;
                }
                if (ability->gravityWasDisabled)
                {
                    controller->GravityActive(true);
                }

                // Handle distance traveled logic
                if (controller->parameters.distanceCrossedDuringLastAbility > FP._0)
                {
                    OnDistanceCrossedDuringAbility(f, owner, ability, controller->parameters.distanceCrossedDuringLastAbility);
                    if (f.Unsafe.TryGetPointer<ItemInventory>(owner, out var itemInventory))
                    {
                        itemInventory->OnDistanceCrossedDuringAbility(f, owner, ability, controller->parameters.distanceCrossedDuringLastAbility);
                    }
                }
            }

            CleanupEntitiesAndEffects(f, ability, owner, controller);
            AbilityInventory.TriggerUpdateUIEvent(f, ability->owner);
        }

        private unsafe void RecordUsageStats(Frame f, EntityRef owner)
        {
            for (int i = 0; i < f.Global->pvpMatchInfo.players.Length; i++)
            {
                if (f.Global->pvpMatchInfo.players[i].entity == owner)
                {
                    if (f.Unsafe.TryGetPointer<PlayerGameStats>(owner, out var pgs))
                    {
                        pgs->abilitiesUsed++;
                    }
                    break;
                }
            }
        }

        private unsafe void CleanupEntitiesAndEffects(Frame f, Ability* ability, EntityRef owner, CharacterController* controller)
        {
            // Destroy specific damage zones
            foreach (var zone in f.GetComponentIterator<DamageZone>())
            {
                if (zone.Component.owner == owner && zone.Component.destroyOnAbilityEnd == abilityId)
                {
                    f.Destroy(zone.Entity);
                }
            }

            // Remove ability-bound effects
            if (f.Unsafe.TryGetPointer<EffectHandler>(owner, out var handler))
            {
                var removeBehavior = abilityType switch
                {
                    AbilityType.Ability => CustomEffectBehavior.RemoveWhenOwnerExitAbility,
                    AbilityType.Attack => CustomEffectBehavior.RemoveWhenOwnerExitAttack,
                    AbilityType.ConsumeItem => CustomEffectBehavior.RemoveWhenOwnerExitConsumeItem,
                    _ => CustomEffectBehavior.RemoveWhenOwnerExitUtility // Default for Jump, Dodge, Utility
                };
                handler->RemoveByCustomBehavior(f, removeBehavior);
            }

            // Terminate Motion
            if (motion.Length > 0)
            {
                foreach (var m in f.GetComponentIterator<Motion>())
                {
                    var comp = m.Component;
                    if (comp.entity == owner && !comp.isHammock && (comp.abilityId == default || comp.abilityId == abilityId) && comp.terminated == 0)
                    {
                        if (f.Unsafe.TryGetPointer<Motion>(m.Entity, out var motionPtr))
                        {
                            MotionSystem.TerminateMotion(f, m.Entity, motionPtr, controller, false);
                        }
                    }
                }
            }
        }

        #endregion

        #region Damage & Motion Generation

        protected virtual unsafe EntityRef MaterializeDamageZone(Frame f, EntityRef entity, AbilityDamage abilityDamage, FPVector2 position, Ability* ability, int overrideDamageValue = 0, FPVector2 sizeOverride = default, byte damageIndexInAbility = 0)
        {
            return DamageZoneHelper.MaterializeDamageZone(abilityId, f, entity, abilityDamage, position, ability, this, overrideDamageValue, sizeOverride, damageIndexInAbility);
        }

        protected virtual unsafe void CreateDamageZones(Frame f, EntityRef owner, Ability* ability)
        {
            if (damage == null) return;

            for (int i = 0; i < damage.Length; i++)
            {
                var d = damage[i];
                if (d != null && !d.behavior.HasFlag(AbilityDamageBehavior.SpawnsOnAbilityEnd)
                    && !d.behavior.HasFlag(AbilityDamageBehavior.SpawnOnAbilityEndIfDealtDamage))
                {
                    MaterializeDamageZone(f, ability->owner, d, GetDamageZonePosition(f, ability), ability, 0, default, (byte)i);
                }
            }
        }

        protected virtual unsafe void CreateDamageZonesAtEnd(Frame f, EntityRef owner, Ability* ability)
        {
            if (damage == null) return;

            bool created = false;
            for (int i = 0; i < damage.Length; i++)
            {
                var d = damage[i];
                if (d != null && (d.behavior.HasFlag(AbilityDamageBehavior.SpawnsOnAbilityEnd)
                    || (ability->dealtDamage && d.behavior.HasFlag(AbilityDamageBehavior.SpawnOnAbilityEndIfDealtDamage))))
                {
                    created = true;
                    MaterializeDamageZone(f, ability->owner, d, GetDamageZonePosition(f, ability), ability, 0, default, (byte)i);
                }
            }

            if (created) LinkDamageZoneHistory(f, owner, ability);
        }

        protected virtual unsafe void LinkDamageZoneHistory(Frame f, EntityRef owner, Ability* ability)
        {
            if (!damageZonesShareHistory) return;

            // Link this ability's zones
            var iterator = f.GetComponentIterator<DamageZone>();
            foreach(var zone in iterator)
            {
                if(zone.Component.owner == owner && zone.Component.sourceAbility == ability->abilityData)
                {
                    zone.Component.shareDamageHistoryId = abilityId;
                }
            }

            // Sync history with other zones sharing the ID
            var allZones = f.Filter<DamageZone>();
            while (allZones.NextUnsafe(out var entity, out var dz))
            {
                if (dz->owner == owner && dz->shareDamageHistoryId == abilityId && dz->GetHistory(f).Count == 0)
                {
                    var targetList = dz->GetHistory(f);
                    foreach (var source in iterator)
                    {
                        if (source.Entity != entity && source.Component.owner == owner 
                            && source.Component.shareDamageHistoryId == abilityId 
                            && source.Component.GetHistory(f).Count > 0)
                        {
                            var sourceList = source.Component.GetHistory(f);
                            for (int i = 0; i < sourceList.Count; i++) targetList.Add(sourceList[i]);
                        }
                    }
                }
            }
        }

        protected virtual unsafe void CreateMotionZones(Frame f, EntityRef owner, Ability* ability)
        {
            ClearLastMotion(f, owner, ability);

            if (f.TryGet<CharacterController>(owner, out var c) && c.parameters.cannotUseMotion)
            {
                return;
            }

            if (motion != null)
            {
                for (int i = 0; i < motion.Length; i++)
                {
                    if (motion[i] != null) MaterializeMotion(f, ability->owner, motion[i], ability);
                }
            }
        }

        protected virtual unsafe void ClearLastMotion(Frame f, EntityRef owner, Ability* ability)
        {
            if (motion == null || motion.Length == 0) return;

            if (f.Unsafe.TryGetPointer<CharacterController>(owner, out var controller))
            {
                foreach (var m in f.GetComponentIterator<Motion>())
                {
                    if (m.Component.entity == owner && m.Component.terminated == 0 && !m.Component.isHammock)
                    {
                        if (f.Unsafe.TryGetPointer<Motion>(m.Entity, out var motionComp))
                        {
                            MotionSystem.TerminateMotion(f, m.Entity, motionComp, controller, false);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Spawns a Motion entity based on configuration data.
        /// </summary>
        public virtual unsafe void MaterializeMotion(Frame f, EntityRef entity, MotionData motionData, Ability* ability, FP distanceOverride = default)
        {
            if (motionData == null || entity == default) return;
            
            EntityRef r = PrototypeHelper.CreateEntity(f, CreationSource.AbilityMaterializeMotion);
            if (f.AddOrGet<Motion>(r, out var motion))
            {
                motion->entity = entity;
                motion->distance = distanceOverride == 0 ? motionData.distance : distanceOverride;
                motion->force = motionData.force;
                motion->stopDistance = motionData.stopDistBonus;
                motion->delay = (ability != default && ability->abilitySpeed > 0) ? motionData.delay / ability->abilitySpeed : motionData.delay;
                
                if (motionData.motionFlags.HasFlag(MotionFlags.DistanceByAbilityDirection) && ability != default)
                {
                    motion->force.X = FPMath.Lerp(FP._5, motion->force.X, ability->abilityDirection.Magnitude);
                }

                motion->ResidualForce = motionData.residualPercentage;
                motion->motionFlags = motionData.motionFlags;
                motion->abilityId = abilityId;

                if (motionData.motionFlags.HasFlag(MotionFlags.StopBehindTargetIfStealth))
                {
                    if (f.Unsafe.TryGetPointer<Stealth>(motion->entity, out var stealth) && stealth->IsStealth)
                    {
                         motion->motionFlags |= MotionFlags.StopBehindTarget;
                    }
                }

                motion->interruptId = motionData.interruptAbilityOnEnd ? abilityId : -1;
            }
            else
            {
                f.Destroy(r);
            }
        }

        /// <summary>
        /// Gets where we want to spawn the damageZone
        /// </summary>
        public virtual unsafe FPVector2 GetDamageZonePosition(Frame f, Ability* ability)
        {
            return f.Unsafe.GetPointer<Transform2D>(ability->owner)->Position;
        }

        #endregion

        #region Targeting & Auto-Aim

        /// <summary>
        /// Handles auto aiming abilities for both bots and players. When a player has manual aim, must pass valid currentAim, otherwise player input will be overriden.
        /// </summary>
        protected virtual unsafe (bool hadResult, FPVector2 direction, FP range, EntityRef target) AutoAim(Frame f, Ability* ability, EntityRef owner, Transform2D* transform, CharacterController* controller, FPVector2 currentAim)
        {
            if (transform == default) f.Unsafe.TryGetPointer<Transform2D>(owner, out transform);
            
            if (!f.Unsafe.TryGetPointer<Team>(owner, out var ourTeam) || transform == default)
            {
                return (false, currentAim, default, default);
            }

            EntityRef target = default;
            Transform2D* targetTransform = default;
            EntityRef autoAttackTarget = default;
            Transform2D* autoAttackTargetTransform = default;

            FP bestDistance = 9999;
            FP maxRange = (damage != null && damage.Length > 0) ? damage[0].bounds.Center.Y + damage[0].abilityDirectionBonus : FP._1;

            var comps = f.Filter<Transform2D, Health>();
            while (comps.NextUnsafe(out var e, out var t, out var health))
            {
                if (health->currentValue <= 0) continue; // Dead
                if (f.Unsafe.TryGetPointer<Team>(e, out var team) && team->team == ourTeam->team) continue; // Ally

                FP dist = FPVector2.Distance(t->Position, transform->Position);
                
                // Screen distance check
                if (dist > BotHelper.ONSCREEN_DIST + FP._2) continue;

                // Priority Check
                if (dist < bestDistance)
                {
                    // Basic Attack Range Check
                    if (abilityInput == 1 && dist > controller->attackRange + FP._2) continue;

                    // Angle Check (90 degrees)
                    if (FPVector2.Angle(t->Position - transform->Position, transform->Up) > 90)
                    {
                        // Special case: Auto attack redirect only works on characters, not static props
                        if (abilityInput == 1 && f.Has<CharacterController>(e))
                        {
                            autoAttackTarget = e;
                            autoAttackTargetTransform = t;
                        }
                        continue;
                    }

                    if (BotHelper.IsEntityStealthToUs(f, owner, e)) continue;

                    target = e;
                    targetTransform = t;
                    bestDistance = dist;
                }
            }

            // Fallback to auto-attack target if main target invalid
            if (target == default && autoAttackTarget != default)
            {
                target = autoAttackTarget;
                targetTransform = autoAttackTargetTransform;
            }

            if (target != default && targetTransform != default)
            {
                FPVector2 predictedPos = CalculatePredictedPosition(f, owner, target, targetTransform, ability, bestDistance);
                currentAim = (predictedPos - transform->Position);
                
                FP aimRange = (maxRange > FP._0) ? FPMath.Clamp(bestDistance / maxRange, FP._0_10, FP._1) : default;
                return (true, currentAim.Normalized, aimRange, target);
            }

            return (false, currentAim, default, default);
        }

        private unsafe FPVector2 CalculatePredictedPosition(Frame f, EntityRef owner, EntityRef target, Transform2D* targetTransform, Ability* ability, FP dist)
        {
            if (f.Has<BRBot>(owner) && f.Unsafe.TryGetPointer<CharacterController>(target, out var tc))
            {
                FP attackDelay = GetEstimatedAttackDelay(f, dist);
                
                // Bot Inaccuracy Logic
                if (f.RNG->Next() > FP._0_33)
                {
                    attackDelay += f.RNG->Next(-FP._0_25, FP._0_25);
                }

                if (attackDelay > FP._0)
                {
                    return tc->GetPredictedPosition(f, FPMath.CeilToInt(attackDelay * GlobalSystem.TickRate));
                }
            }
            return targetTransform->Position;
        }

        private unsafe FP GetEstimatedAttackDelay(Frame f, FP dist)
        {
            FP attackDelay = FP._0_50;
            
            if (this is AbilityProjectile projAbility)
            {
                attackDelay = projAbility.projectileDelay;
                if (projAbility.projectile != default && f.TryFindAsset<ProjectileData>(projAbility.projectile.Id, out var pdata) && pdata.speed > FP._0)
                {
                    attackDelay += dist / pdata.speed;
                }
            }
            else if (damage != null && damage.Length > 0)
            {
                attackDelay = damage[0].delay;
                for (int j = 1; j < damage.Length; j++)
                {
                    if (damage[j].delay < attackDelay) attackDelay = damage[j].delay;
                }
            }
            return attackDelay;
        }

        /// <summary>
        /// An optimized aiming function that removes anything but target selection. 
        /// Used in cases where you would want to know what auto aim would target, or want to take advantage of other functionality.
        /// </summary>
        protected virtual unsafe EntityRef GetIdealTarget(Frame f, Ability* ability, EntityRef owner, Transform2D* transform, CharacterController* controller, FPVector2 currentAim)
        {
            if (transform == default) f.Unsafe.TryGetPointer<Transform2D>(owner, out transform);
            if (!f.Unsafe.TryGetPointer<Team>(owner, out var ourTeam) || transform == default) return default;

            EntityRef target = default;
            FP bestDist = 9999;

            var comps = f.Filter<Transform2D, Health>();
            while (comps.NextUnsafe(out var e, out var t, out var health))
            {
                if (health->currentValue <= 0) continue;
                if (FPVector2.Distance(t->Position, transform->Position) > BotHelper.ONSCREEN_DIST + FP._2) continue;
                if (f.Unsafe.TryGetPointer<Team>(e, out var team) && team->team == ourTeam->team) continue;

                FP d = FPVector2.Distance(transform->Position, t->Position);
                if (d < bestDist)
                {
                    // Narrow angle check for ideal targeting
                    if (FPVector2.Angle(t->Position - transform->Position, currentAim) > 25) continue;
                    if (BotHelper.IsEntityStealthToUs(f, owner, e)) continue;

                    target = e;
                    bestDist = d;
                }
            }

            return target;
        }

        protected virtual unsafe void FaceIntendedPosition(Frame f, Ability* ability, EntityRef owner, CharacterController* controller = default, Playable* playable = default, bool isAPerFrameCall = false)
        {
            if (controller == default) f.Unsafe.TryGetPointer<CharacterController>(owner, out controller);
            if (controller == default) return;
            if (playable == default) f.Unsafe.TryGetPointer<Playable>(owner, out playable);
            if (playable == default) return;

            if (specialBehavior.HasFlag(AbilitySpecialBehavior.InvertFacingOnStart))
            {
                controller->RotateController(f, FPVector2.Rotate(controller->state.direction, FP.Rad_180));
            }

            var input = PlayableHelper.GetInput(f, playable);
            var inputDir = input->AbilityDirection;
            FP r = input->abilityRange / FP._100;

            if (setsAbilityTarget) controller->abilityTarget = default;

            bool alreadyAimed = false;
            if (f.Unsafe.TryGetPointer<Transform2D>(owner, out var transform) && !isAPerFrameCall)
            {
                if (inputDir == default || f.Has<BRBot>(owner))
                {
                    // Auto attack Movement Facing
                    if (abilityInput == 1 && buttonType == AbilityButtonType.Standard && input->MovementDirection != default)
                    {
                        controller->RotateController(f, input->MovementDirection);
                    }

                    // Auto Aim
                    if (!specialBehavior.HasFlag(AbilitySpecialBehavior.NoAutoAim))
                    {
                        var aim = AutoAim(f, ability, owner, transform, controller, inputDir);
                        alreadyAimed = true;
                        
                        if (setsAbilityTarget) controller->abilityTarget = aim.target;
                        if (aim.hadResult)
                        {
                            inputDir = aim.direction;
                            r = aim.range;
                        }
                    }
                }
                else if (setsAbilityTarget && abilityInput != 1)
                {
                    // Manual aim, but still find valid target for motion/homing
                    controller->abilityTarget = GetIdealTarget(f, ability, owner, transform, controller, inputDir);
                }
            }

            ability->abilityDirection = (inputDir != default) ? inputDir * r : controller->state.direction;
            controller->state.abilityDirection = ability->abilityDirection;

            if (ability->abilityDirection != default)
            {
                controller->RotateController(f, ability->abilityDirection);
            }

            // Fallback aim logic
            if (!alreadyAimed && !isAPerFrameCall && setsAbilityTarget && (abilityInput == 1 || inputDir == default || f.Has<BRBot>(owner)))
            {
                var aim = AutoAim(f, ability, owner, transform ?? f.Unsafe.GetPointer<Transform2D>(owner), controller, inputDir);
                controller->abilityTarget = aim.target;
            }

            if (!isAPerFrameCall) input->AbilityDirection = default;
        }

        public static bool IsFacingAway(FPVector2 position, FPVector2 direction, FPVector2 positionToCheck)
        {
            FPVector2 dir = positionToCheck - position;
            return FPVector2.Dot(dir, direction) < 0;
        }

        #endregion

        #region Sequencing

        /// <summary>
        /// Looks for an ability that matches nextAbilityData, then interrupts currentAbility and starts the new one.
        /// </summary>
        public virtual unsafe void SequenceIntoAbility(Frame f, Ability* currentAbility, AssetRefAbilityData nextAbilityRef)
        {
            if (nextAbilityRef == default) return;

            FastForwardAbility(f, currentAbility);

            if (currentAbility->owner != default && f.Unsafe.TryGetPointer<AbilityInventory>(currentAbility->owner, out var inventory))
            {
                inventory->sequencedAbility = nextAbilityRef;

                if (abilityType == AbilityType.Attack && sequence?.Length > 0 && sequence[0].decider == SequenceDecider.BufferedInput)
                {
                    inventory->lastAttack = nextAbilityRef;
                    inventory->lastAttackTimer = AbilityInventory.BUFFERED_ATTACK_WINDOW;
                }
            }
        }

        public virtual unsafe void SequenceIfNeeded(Frame f, Ability* ability, bool isCalledOnEnd)
        {
            if (sequence == null || sequence.Length == 0) return;

            for (int i = 0; i < sequence.Length; i++)
            {
                var seq = sequence[i];
                if (seq == null) continue;

                // Explicit sequencing
                if (seq.sequencedAbilityRef != null)
                {
                    if (SequenceConditionsMet(f, seq, ability, isCalledOnEnd))
                    {
                        SequenceIntoAbility(f, ability, seq.sequencedAbilityRef);
                        return;
                    }
                }

                // Movement interruptions (Animation canceling via move)
                if (seq.skippableAfter > 0 && ability->timeElapsed > seq.skippableAfter)
                {
                    if (seq.decider != SequenceDecider.DealtDamage && seq.decider != SequenceDecider.ReceivedDamage)
                    {
                        if (f.Unsafe.TryGetPointer<Playable>(ability->owner, out var inputData))
                        {
                            var input = PlayableHelper.GetInput(f, inputData);
                            if (input != default && input->MovementDirection != default)
                            {
                                InterruptAbility(f, ability);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Returns whether or not we're allowed to go to next ability.
        /// </summary>
        public unsafe virtual bool SequenceConditionsMet(Frame f, AbilitySequence sequence, Ability* ability, bool isCalledOnEnd = false)
        {
            switch (sequence.decider)
            {
                case SequenceDecider.AlwaysPlay:
                    return isCalledOnEnd;

                case SequenceDecider.DiceRoll:
                    if (!isCalledOnEnd) return false;
                    return f.RNG->NextInclusive(0, 100) <= sequence.diceOdds;

                case SequenceDecider.BufferedInput:
                    if (ability->timeElapsed > sequence.skippableAfter)
                    {
                        if (ability->owner != default && f.Unsafe.TryGetPointer<Playable>(ability->owner, out Playable* playable))
                        {
                            var input = PlayableHelper.GetInput(f, playable);
                            return input != null && input->abilityButton.IsDown && input->abilityID == abilityInput;
                        }
                    }
                    return false;

                case SequenceDecider.DealtDamage:
                    return ability->dealtDamage;

                case SequenceDecider.ReceivedDamage:
                    return ability->receivedDamage;

                case SequenceDecider.DidntDealDamage:
                    return isCalledOnEnd && !ability->dealtDamage;
            }
            return false;
        }

        #endregion

        #region Bot Logic

        public virtual unsafe void HandleUniqueBotBehavior(Frame f, Ability* ability, AbilityData abilityData, CharacterController* controller, Transform2D* transform, Playable* playable)
        {
            if (buttonType == AbilityButtonType.Charged)
            {
                HandleBotChargedAbility(f, ability, transform, playable);
            }
            if (botBehavior.HasFlag(AbilityBotUniqueBehavior.KeepFacingTarget))
            {
                var target = AIHelper.GetTarget(f, ability->owner);
                if (target != default && f.Unsafe.TryGetPointer<Transform2D>(target, out var targetTransform))
                {
                    transform->Rotation = FPMath.Lerp(transform->Rotation, FPVector2.RadiansSigned(FPVector2.Up, targetTransform->Position - transform->Position), f.DeltaTime * FP._3);
                }
            }
            if (botBehavior.HasFlag(AbilityBotUniqueBehavior.KeepMovingTowardTarget))
            {
                var target = AIHelper.GetTarget(f, ability->owner);
                if (target != default && f.Unsafe.TryGetPointer<Transform2D>(target, out var targetTransform))
                {
                    if (FPVector2.Distance(transform->Position, targetTransform->Position) > FP._1_50)
                    {
                        controller->MoveToward(f, targetTransform->Position, transform, playable);
                    }
                }
            }
            if (botBehavior.HasFlag(AbilityBotUniqueBehavior.ControllableProjectile))
            {
                HandleBotControllableProjectile(f, ability, transform, playable);
            }
        }

        private unsafe void HandleBotChargedAbility(Frame f, Ability* ability, Transform2D* transform, Playable* playable)
        {
            bool holdIt = true;

            // Random release if hit recently
            if (f.Unsafe.TryGetPointer<Health>(ability->owner, out var health) && f.Global->time < health->lastTimeHitByDirectAbility + FP._0_05)
            {
                if (f.RNG->Next() <= FP._0_50) holdIt = false;
            }

            if (holdIt)
            {
                EntityRef target = AIHelper.GetTarget(f, ability->owner);
                if (target != default && f.Unsafe.TryGetPointer<Transform2D>(target, out var targetTransform) 
                    && f.Unsafe.TryGetPointer<CharacterController>(target, out var targetController))
                {
                    bool targetLeaving = IsFacingAway(transform->Position, targetController->state.direction, targetTransform->Position);

                    // Release if target is leaving range
                    if (targetLeaving && FPVector2.Distance(targetTransform->Position, transform->Position) > maxReach * (FP._0_75 + FP._0_10))
                    {
                        holdIt = false;
                    }
                }
            }
            playable->botData.botInput.abilityButton = holdIt;
        }

        private unsafe void HandleBotControllableProjectile(Frame f, Ability* ability, Transform2D* transform, Playable* playable)
        {
            var target = AIHelper.GetTarget(f, ability->owner);
            if (target != default && f.Unsafe.TryGetPointer<Transform2D>(target, out var targetTransform) 
                && !BotHelper.IsEntityStealthToUs(f, ability->owner, target))
            {
                FPVector2 targetPos = BotHelper.BestGuessPositionOnTarget_NoNavmeshConsideration(f, ability->owner, target, targetTransform);
                if (ability->tempEntity != default && f.Unsafe.TryGetPointer<Transform2D>(ability->tempEntity, out var pt))
                {
                    playable->botData.botInput.MovementDirection = (targetPos - pt->Position).Normalized;
                }
                else
                {
                    playable->botData.botInput.MovementDirection = (targetPos - transform->Position).Normalized;
                }
            }
        }

        // only meant to be overriden -- has no default behavior. Default behavior is from AutoAim function.
        protected virtual unsafe void UpdateBotAiming(Frame f, EntityRef owner) { }

        /// <summary>
        /// Returns whether an ability poses a threat to given target.
        /// </summary>
        public unsafe virtual bool IsAThreatTo(Frame f, EntityRef owner, EntityRef target, Transform2D* targetTransform, CharacterController* targetController, bool updateAiming = false)
        {
            if (specialBehavior.HasFlag(AbilitySpecialBehavior.IsAlwaysAThreat)) return true;
            if (targetTransform == default || !f.Unsafe.TryGetPointer<Transform2D>(owner, out var transform)) return false;

            CalculateThreatParams(out FP attackDelay, out FP damageReach, out FP controllableRange, out FP damageCenter);

            // Bounds Checking
            FP min = (minReach == FP._0) ? -FP._1 : minReach;
            FP max = (maxReach <= min) ? min + FP._0_25 : maxReach;

            // Vision Checks
            if (f.TryGet<BRMemory>(owner, out var championTargets) && !botBehavior.HasFlag(AbilityBotUniqueBehavior.DoesntRequireVision))
            {
                if (BotHelper.ViewToTargetIsObstructed(f, owner, target))
                {
                    if (max > damageReach) max = damageReach;
                    if (max <= FP._0) return false;
                }
            }

            FPVector2 targetPositionAfterDelay = targetTransform->Position;
            if (targetController != default)
            {
                targetPositionAfterDelay = BotHelper.BestGuessPositionOnTarget_NoNavmeshConsideration(f, owner, target, targetTransform, targetController, attackDelay);
            }

            FP predictedDistance = FPVector2.Distance(transform->Position, targetPositionAfterDelay);

            if (updateAiming && f.Unsafe.TryGetPointer<Playable>(owner, out var playable))
            {
                playable->botData.botInput.AbilityDirection = (targetTransform->Position - transform->Position).Normalized;
                if (controllableRange > FP._0)
                {
                    FP d = predictedDistance - damageCenter;
                    d = FPMath.Clamp(d, FP._0, controllableRange);
                    FP range = (d / controllableRange) * FP._100;
                    if (range > FP._0 && range <= 250)
                    {
                        playable->botData.botInput.abilityRange = (byte)FPMath.RoundToInt(range);
                    }
                }
            }

            if (dontPredictReach)
            {
                FP dist = FPVector2.Distance(transform->Position, targetTransform->Position);
                return dist <= max && dist >= min;
            }

            return predictedDistance <= max && predictedDistance >= min;
        }

        private void CalculateThreatParams(out FP attackDelay, out FP damageReach, out FP controllableRange, out FP damageCenter)
        {
            attackDelay = FP._0_50;
            damageReach = FP._0;
            damageCenter = FP._0;
            controllableRange = FP._0;

            if (damage != null && damage.Length > 0)
            {
                attackDelay = damage[0].delay;
                for (int j = 0; j < damage.Length; j++)
                {
                    var d = damage[j];
                    FP reach = (d.bounds.Extents.Y + d.bounds.Center.Y) / FP._2;
                    
                    if (d.abilityDirectionBonus > controllableRange) controllableRange = d.abilityDirectionBonus;
                    if (damageCenter < d.bounds.Center.Y) damageCenter = d.bounds.Center.Y;
                    if (reach > damageReach) damageReach = reach;
                    if (d.delay < attackDelay) attackDelay = d.delay;
                }
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Determine if the ability triggers breaking stealth (specifically Brush Stealth).
        /// </summary>
        protected virtual unsafe void ConsiderBreakingBrushStealth(Frame f, EntityRef owner, Ability* ability)
        {
            if (specialBehavior.HasFlag(AbilitySpecialBehavior.DoesntBreakBrushStealth)) return;

            if (f.Unsafe.TryGetPointer<BrushUser>(owner, out var user) && !user->IsInBrush()) return;

            if (f.Unsafe.TryGetPointer<Stealth>(owner, out var stealth))
            {
                stealth->brushLockout = f.Global->time + FP._2;
                if (stealth->stealthItems[Stealth.ITEM_INDEX_BRUSH].isStealth)
                {
                    var stealthItem = stealth->stealthItems[Stealth.ITEM_INDEX_BRUSH];
                    if (stealthItem.stealthData.Id != default && f.TryFindAsset<StealthData>(stealthItem.stealthData.Id, out var data))
                    {
                        data.SetStealthState(f, owner, stealth, Stealth.ITEM_INDEX_BRUSH, false);
                    }
                }
            }
        }

        protected virtual unsafe void ConsumeAbilityCost(Frame f, EntityRef owner, Ability* ability, Energy* energy)
        {
            energy->ChangeEnergy(f, -abilityCost, EnergyChangeSource.Ability);
        }

        protected virtual unsafe void InterruptPreviousAbilityBeforeStart(Frame f, EntityRef owner, Ability* ability)
        {
            if (specialBehavior.HasFlag(AbilitySpecialBehavior.DontInterruptAbilities)) return;

            if (f.Unsafe.TryGetPointer<AbilityInventory>(owner, out var inventory))
            {
                inventory->InterruptAllAbilities(f, default, -1, abilityId, true);
            }
        }

        protected virtual unsafe void ChangePermissions(Frame f, Ability* ability, EntityRef owner, bool add)
        {
            if (owner != default && f.Unsafe.TryGetPointer<CharacterController>(owner, out var controller))
            {
                ApplyPermissionChange(ref controller->permissions, add);
                if (add) controller->inAbilityCount++;
                else controller->inAbilityCount--;
            }

            if (owner != default && f.Unsafe.TryGetPointer<AbilityInventory>(owner, out var inventory))
            {
                if (add) inventory->inAbilityCount++;
                else inventory->inAbilityCount--;
            }
        }

        private unsafe void ApplyPermissionChange(ref CharacterPermissions perm, bool add)
        {
            int modifier = add ? 1 : -1;
            if (!permissions.movement) perm.movement += (byte)modifier;
            if (!permissions.ability) perm.ability += (byte)modifier;
            if (!permissions.utility) perm.utility += (byte)modifier;
            if (!permissions.attack) perm.attack += (byte)modifier;
            if (permissions.noRotate) perm.noRotate += (byte)modifier;
            if (permissions.noCharacterCollision) perm.noCharacterCollision += (byte)modifier;
        }

        /// <summary>
        /// Interrupt an active ability.
        /// </summary>
        public virtual unsafe void InterruptAbility(Frame f, Ability* ability, FP lockout = default)
        {
            ability->mark |= AbilityMark.MarkedForInterruption;
            if (lockout > 0 && ability->cooldownTimer < lockout)
            {
                ability->cooldownTimer = lockout;
            }
        }

        /// <summary>
        /// Instantly ENDS an ability (this is not an interrupt, it fast forwards to completion).
        /// </summary>
        public virtual unsafe void FastForwardAbility(Frame f, Ability* ability)
        {
            ability->mark |= AbilityMark.MarkedForFastForward;
        }

        public virtual unsafe void ClickedWhileOnCD(Frame f, Ability* ability, AbilityData abilityData)
        {
            if (specialBehavior.HasFlag(AbilitySpecialBehavior.ClickingOnCDInterrupts))
            {
                if (ability != default && ability->timeElapsed < FP._0_33 && buttonType != AbilityButtonType.Hold) return;
                FastForwardAbility(f, ability);
            }
        }

        public virtual unsafe FP GetAbilitySpeed(Frame f, EntityRef owner, Ability* ability) => FP._1;

        public virtual unsafe void OnDistanceCrossedDuringAbility(Frame f, EntityRef owner, Ability* ability, FP distance) { }

        #endregion

        #region Callbacks

        public virtual unsafe void OnAboutToCalculateDealtDamage(Frame f, DamageCalculationData* damageData, Ability* ability = default, AssetRefAbilityData abilityRef = default) { }
        public virtual unsafe void OnAboutToCalculateReceivedDamage(Frame f, DamageCalculationData* damageData, Ability* ability = default, AssetRefAbilityData abilityRef = default) { }

        /// <summary>
        /// Player has dealt damage to a target. If ability* is not default, this was called while ability is active.
        /// </summary>
        public virtual unsafe void OnAboutToDealAbilityDamage(Frame f, EntityRef owner, EntityRef target, DamageResult* damageResult, Ability* ability = default, AssetRefAbilityData abilityRef = default, bool isFirstHit = true) { }

        /// <summary>
        /// Player is about to receive damage from a source.
        /// </summary>
        public virtual unsafe void OnAboutToReceiveAbilityDamage(Frame f, EntityRef owner, EntityRef dealer, DamageResult* damageResult, Ability* ability = default, AssetRefAbilityData abilityRef = default) { }

        /// <summary>
        /// Player has dealt damage to a target. Checks for ability termination flags on damage.
        /// </summary>
        public virtual unsafe void OnDealtAbilityDamage(Frame f, EntityRef owner, EntityRef target, DamageResult* damageResult, Ability* ability = default, AssetRefAbilityData abilityRef = default, bool isFirstHit = true)
        {
            if (ability != default)
            {
                ability->dealtDamage = true;
                if (damageResult != default && damageResult->damageZone != default)
                {
                    if (damage != null && damageResult->damageZone->damageIndexInAbility < damage.Length && damageResult->damageZone->damageIndexInAbility >= 0)
                    {
                        if (damage[damageResult->damageZone->damageIndexInAbility].behavior.HasFlag(AbilityDamageBehavior.TerminatesAbility))
                        {
                            FastForwardAbility(f, ability);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Player has received ability damage.
        /// </summary>
        public virtual unsafe void OnReceivedAbilityDamage(Frame f, EntityRef owner, EntityRef dealer, DamageResult* damageResult, Ability* ability = default, AssetRefAbilityData abilityRef = default)
        {
            if (ability != default)
            {
                ability->receivedDamage = true;
            }
        }

        public virtual unsafe void OnAboutToDealGenericDamage(Frame f, EntityRef owner, EntityRef target, DamageResult* damageResult, GenericDamageData genericData, Ability* ability = default) { }
        public virtual unsafe void OnAboutToReceiveGenericDamage(Frame f, EntityRef owner, EntityRef dealer, DamageResult* damageResult, GenericDamageData genericData, Ability* ability = default) { }
        public virtual unsafe void OnDealtGenericDamage(Frame f, EntityRef owner, EntityRef target, DamageResult* damageResult, GenericDamageData genericData, Ability* ability = default) { }
        public virtual unsafe void OnReceivedGenericDamage(Frame f, EntityRef owner, EntityRef dealer, DamageResult* damageResult, GenericDamageData genericData, Ability* ability = default) { }

        /// <summary>
        /// We've hit something with our projectile.
        /// </summary>
        public virtual unsafe void OnProjectileEvent(Frame f, Projectile* projectile, EntityRef hitTarget, Ability* ability, FPVector2 hitPoint, ProjectileEventType type, bool whileAbilityIsActive) { }

        /// <summary>
        /// If ability is not default, then it is active.
        /// </summary>
        public virtual unsafe void OnCollectedCollectible(Frame f, EntityRef entity, EntityRef collector, Collectible* collectible, Ability* ability) { }

        /// <summary>
        /// Killed or assisted in killed an entity.
        /// </summary>
        public virtual unsafe void OnTakedown(Frame f, EntityRef ourEntity, EntityRef deadEntity, EntityRef killer, bool isKiller, Ability* ability) { }

        /// <summary>
        /// We were killed this frame.
        /// </summary>
        public virtual unsafe void OnKilled(Frame f, EntityRef ourEntity, EntityRef killer, bool isSuicide, Ability* ability) { }

        #endregion
    }
}