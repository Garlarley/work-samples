namespace MySamples
{
    internal unsafe class BotGoals
    {
        #region Evaluation Entry Points

        /// <summary>
        /// Selects the priority for engaging an enemy based on bot type and game mode.
        /// </summary>
        public static FP EvaluateDefeatEnemy(Frame f, ref BRBotSystem.Filter filter)
        {
            if (f.RuntimeConfig.gameMode.HasFlag(GameMode.Tutorial) && f.Global->tutorialStage < 10)
            {
                return BotConstants.Priority_None;
            }

            switch (filter.bot->category)
            {
                case BotCategory.SurvivalEnemy:
                    return EvaluateDefeatEnemy_Survival(f, ref filter);
                case BotCategory.Minion:
                    return EvaluateDefeatEnemy_Minion(f, ref filter);
                case BotCategory.Champion:
                default:
                    if (f.RuntimeConfig.gameMode.HasFlag(GameMode.Survival) && filter.team->team != ProfileHelper.NPC_TEAM)
                    {
                        return EvaluateDefeatEnemy_ChampionSurvival(f, ref filter);
                    }
                    return EvaluateDefeatEnemy_Champion(f, ref filter);
            }
        }

        /// <summary>
        /// Evaluates priority to destroy the Nexus structure.
        /// </summary>
        public static FP EvaluateDestroyNexus(Frame f, ref BRBotSystem.Filter filter)
        {
            if (f.ComponentCount<SurvivalNexus>() == 0) return BotConstants.Priority_None;

            // Priority increases if we haven't been in combat recently
            bool isSafe = f.Global->time > filter.health->lastTimeHitByDirectAbility + FP._5;
            FP factor = isSafe ? FP._2 : FP._1;

            return BotConstants.Priority_Standard * factor;
        }

        /// <summary>
        /// Evaluates priority to revive a fallen teammate.
        /// </summary>
        public static FP EvaluateRevivePlayerSurvival(Frame f, ref BRBotSystem.Filter filter)
        {
            if (f.RuntimeConfig.gameMode.HasFlag(GameMode.Survival))
            {
                if (f.ComponentCount<Reviver>() > 0) return BotConstants.Priority_High;
            }
            else
            {
                var comps = f.Filter<Reviver, Team>();
                while (comps.NextUnsafe(out var entity, out var rev, out var t))
                {
                    if (t->team == filter.team->team) return BotConstants.Priority_High;
                }
            }
            return BotConstants.Priority_None;
        }

        /// <summary>
        /// Evaluates hiding in a brush for tactical advantage.
        /// </summary>
        public static FP EvaluateStayInBrush(Frame f, ref BRBotSystem.Filter filter)
        {
            if (!f.Unsafe.TryGetPointer<BrushUser>(filter.entity, out var user) || 
                !f.Unsafe.TryGetPointer<BRMemory>(filter.entity, out var memory))
            {
                return BotConstants.Priority_None;
            }

            if (!user->IsInBrush()) return BotConstants.Priority_None;

            // Don't hide if chasing someone or if RNG fails
            if (filter.bot->goal == BotGoal.DefeatEnemy) return BotConstants.Priority_None;
            if (filter.bot->goal != BotGoal.StayInBrush && f.RNG->Next() > BotConstants.Chance_StayInBrush) return BotConstants.Priority_None;

            // Scan for enemies sharing the brush
            foreach (var x in f.GetComponentIterator<BrushUser>())
            {
                if (x.Component.brush == user->brush && x.Entity != filter.entity && AIHelper.AreEnemies(f, filter.entity, x.Entity))
                {
                    return BotConstants.Priority_None; // Brush is compromised
                }
            }

            // Calculate priority based on enemy proximity and safety duration
            FP maxDist = FP._10 * FP._2;
            FP factor = FP._1 - (FPMath.Clamp(memory->closestEnemy, FP._0, maxDist) / maxDist);
            bool isUnderAttack = f.Global->time - filter.health->lastTimeHitByDirectAbility <= FP._2;

            if (memory->closestEnemy < BotConstants.Distance_OnScreen / 2 && !isUnderAttack)
            {
                FP timeInBrush = f.Global->time - user->enterTime;
                
                if (timeInBrush < FP._5) return BotConstants.Priority_Highest * factor;
                if (timeInBrush < FP._10) return BotConstants.Priority_High * factor;
                if (timeInBrush < 15) return BotConstants.Priority_Standard * factor;
                if (timeInBrush < 20) return BotConstants.Priority_Low * factor;
            }

            return BotConstants.Priority_None;
        }

        #endregion

        #region Movement & Survival Evaluation

        public static FP EvaluateRunaway(Frame f, ref BRBotSystem.Filter filter)
        {
            if (!f.Unsafe.TryGetPointer<BRMemory>(filter.entity, out var memory) || memory->enemy == default)
                return BotConstants.Priority_None;

            if (!f.Unsafe.TryGetPointer<Health>(memory->enemy, out var enemyHealth))
                return BotConstants.Priority_None;

            // Cowardice logic
            if (filter.bot->isCoward && filter.health->CurrentPercentage < FP._0_15 && enemyHealth->CurrentPercentage > FP._0_25)
            {
                return BotConstants.Priority_Must * (FP._1 - FP._0_10);
            }

            FP prio = BotConstants.Priority_None;
            FP healthThreshold = f.RNG->Next() * FP._0_75;

            if (filter.health->CurrentPercentage < healthThreshold && filter.health->CurrentPercentage > FP._0)
            {
                if (enemyHealth->CurrentPercentage > filter.health->CurrentPercentage * FP._1_50)
                {
                    prio += FPMath.Clamp(enemyHealth->CurrentPercentage / filter.health->CurrentPercentage, FP._0, BotConstants.Priority_High);
                    if (filter.bot->arsenal.HasFlag(ReasonForUse.RunawayFromTarget)) prio += FP._1;
                }
            }

            // Check ability cooldowns
            if (f.Unsafe.TryGetPointer<AbilityInventory>(filter.entity, out var inventory))
            {
                var list = inventory->GetActiveAbilities(f);
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].abilityInput >= 4 && list[i].abilityInput <= 6)
                    {
                         prio += FP._0_50;
                    }
                }
            }
            return prio;
        }

        public static FP EvaluateFollowOwner(Frame f, ref BRBotSystem.Filter filter)
        {
            if (!f.TryGet<OwnedByEntity>(filter.entity, out var obe)) return BotConstants.Priority_None;

            FP dist = QTools.Distance(f, obe.ownerEntity, filter.entity);
            
            if (dist > BotConstants.Distance_OnScreen) return BotConstants.Priority_Standard;
            if (dist > FP._3) return BotConstants.Priority_Low;
            
            return BotConstants.Priority_None;
        }

        public static FP EvaluateRunFromDeathZone(Frame f, ref BRBotSystem.Filter filter)
        {
            if (!GameModeHelper.UsesDeathZone(f)) return BotConstants.Priority_None;
            if (filter.bot->goal == BotGoal.GrabCollectible) return BotConstants.Priority_None;
            if (f.Global->brManager == default || !f.Unsafe.TryGetPointer<BRManager>(f.Global->brManager, out var manager)) return BotConstants.Priority_None;

            var pos = filter.transform->Position + manager->bounds.Center;

            // Check if outside bounds
            bool isOutside = pos.X < manager->bounds.Min.X || pos.X > manager->bounds.Max.X || 
                             pos.Y < manager->bounds.Min.Y || pos.Y > manager->bounds.Max.Y;

            if (isOutside)
            {
                if (filter.bot->goal == BotGoal.RunawayFromEnemy) return BotConstants.Priority_Must;
                return BotConstants.Priority_High;
            }

            // Calculate distance to closest edge
            FP distToEdgeX = FPMath.Min(FPMath.Abs(pos.X - manager->bounds.Min.X), FPMath.Abs(pos.X - manager->bounds.Max.X));
            FP distToEdgeY = FPMath.Min(FPMath.Abs(pos.Y - manager->bounds.Min.Y), FPMath.Abs(pos.Y - manager->bounds.Max.Y));
            FP minDist = FPMath.Min(distToEdgeX, distToEdgeY);

            if (minDist < FP._2) return BotConstants.Priority_Standard;
            if (minDist < FP._4) return BotConstants.Priority_Low;

            return BotConstants.Priority_None;
        }

        public static FP EvaluateExploreWorld(Frame f, ref BRBotSystem.Filter filter)
        {
            return BotConstants.Priority_Low;
        }

        #endregion

        #region Objective Evaluation

        public static FP EvaluateDeliverFlag(Frame f, ref BRBotSystem.Filter filter)
        {
            if (!f.Has<CarriedFlag>(filter.entity)) return BotConstants.Priority_None;

            // Optimization: Avoid full component scan if possible, or use broadphase query.
            foreach (var x in f.GetComponentIterator<Flag>())
            {
                if (x.Component.team == filter.team->team && x.Component.state == FlagState.OnBase)
                {
                    return BotConstants.Priority_Highest;
                }
            }
            return BotConstants.Priority_None;
        }

        public static FP EvaluateGoToATM(Frame f, ref BRBotSystem.Filter filter)
        {
            if (!f.Unsafe.TryGetPointer<BRMemory>(filter.entity, out var memory)) return BotConstants.Priority_None;

            var comps = f.Filter<PaydayATM, Transform2D>();
            EntityRef bestAtm = default;
            FP minDist = FP.UseableMax;

            while (comps.NextUnsafe(out var entity, out var atm, out var transform))
            {
                if (!atm->isActive && !atm->activating) continue;

                FP d = FPVector2.Distance(filter.transform->Position, transform->Position);
                if (d < minDist)
                {
                    minDist = d;
                    bestAtm = entity;
                }
            }

            if (bestAtm != default)
            {
                memory->closestModeEntity = bestAtm;
                return BotConstants.Priority_High;
            }

            return BotConstants.Priority_None;
        }

        public static FP EvaluateHillCapture(Frame f, ref BRBotSystem.Filter filter)
        {
            if (!f.Unsafe.TryGetPointer<BRMemory>(filter.entity, out var memory)) return BotConstants.Priority_None;

            var comps = f.Filter<Hill, Transform2D>();
            EntityRef bestHill = default;
            FP minDist = FP.UseableMax;

            while (comps.NextUnsafe(out var entity, out var hill, out var transform))
            {
                if (!hill->isActive) continue;
                if (hill->GetOwningTeam(f) == filter.team->team) continue;

                FP d = FPVector2.Distance(filter.transform->Position, transform->Position);
                if (d < minDist)
                {
                    minDist = d;
                    bestHill = entity;
                }
            }

            if (bestHill != default)
            {
                memory->closestModeEntity = bestHill;
                if (minDist < BotConstants.Distance_OnScreen / 2) return BotConstants.Priority_Highest;
                if (minDist < BotConstants.Distance_OnScreen) return BotConstants.Priority_High;
                return BotConstants.Priority_Standard;
            }

            return BotConstants.Priority_None;
        }

        public static FP EvaluateCollectible(Frame f, ref BRBotSystem.Filter filter)
        {
            if (!f.Unsafe.TryGetPointer<BRMemory>(filter.entity, out var memory)) return BotConstants.Priority_None;

            FP currentPrio = BotConstants.Priority_None;

            // General Collectibles (Powerups/Loot)
            FP lootPrio = EvaluateGeneralLoot(f, filter.transform, memory);
            if (lootPrio > currentPrio) currentPrio = lootPrio;

            // Health Packs
            FP hpPrio = EvaluateHealthPacks(f, filter.transform, filter.health, memory);
            if (hpPrio > currentPrio) currentPrio = hpPrio;

            // Flags (CTF)
            FP flagPrio = EvaluateCTFFlags(f, filter.transform, filter.team, memory);
            if (flagPrio > currentPrio) currentPrio = flagPrio;

            return currentPrio;
        }

        private static FP EvaluateGeneralLoot(Frame f, Transform2D* botTransform, BRMemory* memory)
        {
            var comps = f.Filter<BRCollectible, Transform2D>();
            EntityRef bestItem = default;
            FP minDist = FP.UseableMax;

            while (comps.NextUnsafe(out var entity, out var collectible, out var transform))
            {
                if (collectible->collected) continue;
                if (BattleRoyalSystem.IsPositionInsideDeathZone(f, transform->Position, FP._2)) continue;

                FP d = FPVector2.Distance(botTransform->Position, transform->Position);
                if (d < minDist)
                {
                    bestItem = entity;
                    minDist = d;
                }
            }

            if (bestItem != default)
            {
                memory->collectible = bestItem;
                if (minDist < FP._5) return BotConstants.Priority_Highest;
                if (minDist < FP._10) return BotConstants.Priority_High;
                if (minDist < BotConstants.Distance_OnScreen) return BotConstants.Priority_Standard;
                if (minDist < BotConstants.Distance_OnScreen + FP._5) return BotConstants.Priority_Low;
            }
            return BotConstants.Priority_None;
        }

        private static FP EvaluateHealthPacks(Frame f, Transform2D* botTransform, Health* botHealth, BRMemory* memory)
        {
            if (botHealth->CurrentPercentage >= FP._0_90) return BotConstants.Priority_None;

            var comps = f.Filter<Collectible, Transform2D>();
            EntityRef bestPack = default;
            FP minDist = FP.UseableMax;

            while (comps.NextUnsafe(out var entity, out var collectible, out var transform))
            {
                if (collectible->collected || collectible->id != CollectibleId.HealthPack) continue;
                if (BattleRoyalSystem.IsPositionInsideDeathZone(f, transform->Position, FP._2)) continue;

                FP d = FPVector2.Distance(botTransform->Position, transform->Position);
                if (d < minDist)
                {
                    bestPack = entity;
                    minDist = d;
                }
            }

            if (bestPack != default && minDist < BotConstants.Distance_OnScreen)
            {
                memory->collectible = bestPack;
                if (botHealth->CurrentPercentage > FP._0_75) return BotConstants.Priority_Low;
                if (botHealth->CurrentPercentage > FP._0_50) return BotConstants.Priority_Standard;
                if (botHealth->CurrentPercentage > FP._0_25) return BotConstants.Priority_High;
                return BotConstants.Priority_Highest;
            }
            return BotConstants.Priority_None;
        }

        private static FP EvaluateCTFFlags(Frame f, Transform2D* botTransform, Team* botTeam, BRMemory* memory)
        {
            if (!f.RuntimeConfig.gameMode.HasFlag(GameMode.CTF)) return BotConstants.Priority_None;

            var comps = f.Filter<Flag, Team, Transform2D>();
            EntityRef bestFlag = default;
            FP minDist = FP.UseableMax;

            while (comps.NextUnsafe(out var entity, out var flag, out var flagTeam, out var transform))
            {
                bool canPickup = (flagTeam->team != botTeam->team && (flag->state == FlagState.OnBase || flag->state == FlagState.Dropped)) ||
                                 (flagTeam->team == botTeam->team && flag->state == FlagState.Dropped);

                if (canPickup)
                {
                    FP d = FPVector2.Distance(botTransform->Position, transform->Position);
                    if (d < minDist)
                    {
                        bestFlag = entity;
                        minDist = d;
                    }
                }
            }

            if (bestFlag != default)
            {
                memory->collectible = bestFlag;
                if (minDist < BotConstants.Distance_OnScreen / 4) return BotConstants.Priority_Must;
                if (minDist < BotConstants.Distance_OnScreen / 2) return BotConstants.Priority_Highest;
                if (minDist < BotConstants.Distance_OnScreen) return BotConstants.Priority_High;
                return BotConstants.Priority_Standard;
            }
            return BotConstants.Priority_None;
        }

        #endregion

        #region Enemy Evaluation helpers

        // Note: Minion and Survival logic kept largely intact but cleaned for readability
        public static FP EvaluateDefeatEnemy_Minion(Frame f, ref BRBotSystem.Filter filter)
        {
            EntityRef owner = f.TryGet<OwnedByEntity>(filter.entity, out var obe) ? obe.ownerEntity : filter.entity;
            
            if (!f.Unsafe.TryGetPointer<BRTargets_Minion>(filter.entity, out var targets)) return BotConstants.Priority_None;

            // Validate existing target
            if (targets->enemy != default)
            {
                if (f.Unsafe.TryGetPointer<Health>(targets->enemy, out var enemyHealth) && 
                    enemyHealth->currentValue > 0 && 
                    QTools.Distance(f, owner, targets->enemy) <= BotConstants.Distance_OnScreen)
                {
                    return BotConstants.Priority_Standard;
                }
                targets->enemy = default;
            }

            // Aquire new target
            EntityRef newEnemy;
            if (owner != default && f.Unsafe.TryGetPointer<CharacterController>(owner, out var controller) && controller->abilityTarget != default)
            {
                newEnemy = controller->abilityTarget;
            }
            else
            {
                newEnemy = AIHelper.GetClosestHostile(f, owner, false, filter.transform, default, default, BotConstants.Distance_OnScreen);
            }

            if (newEnemy != default)
            {
                targets->enemy = newEnemy;
                return BotConstants.Priority_Standard;
            }

            return BotConstants.Priority_None;
        }

        public static FP EvaluateDefeatEnemy_Survival(Frame f, ref BRBotSystem.Filter filter)
        {
            EntityRef owner = f.TryGet<OwnedByEntity>(filter.entity, out var obe) ? obe.ownerEntity : filter.entity;
            
            if (!f.Unsafe.TryGetPointer<BRTargets_Minion>(filter.entity, out var targets)) return BotConstants.Priority_None;

            // Validate existing
            if (targets->enemy != default)
            {
                if (f.Unsafe.TryGetPointer<Health>(targets->enemy, out var enemyHealth) && enemyHealth->currentValue > 0)
                {
                    return CalculateCombatPriority(f, owner, targets->enemy);
                }
                targets->enemy = default;
            }

            // Acquire new
            var newEnemy = AIHelper.GetClosestHostile(f, owner, false, filter.transform, default, default, BotConstants.Distance_OnScreen);
            if (newEnemy != default)
            {
                targets->enemy = newEnemy;
                return CalculateCombatPriority(f, owner, newEnemy);
            }

            return BotConstants.Priority_None;
        }

        private static FP CalculateCombatPriority(Frame f, EntityRef owner, EntityRef enemy)
        {
            var dist = QTools.Distance(f, owner, enemy);
            if (dist < 6) return BotConstants.Priority_Highest;
            if (dist < 10) return BotConstants.Priority_High;
            if (dist <= BotConstants.Distance_OnScreen) return BotConstants.Priority_Standard;
            return BotConstants.Priority_None;
        }

        public static FP EvaluateDefeatEnemy_ChampionSurvival(Frame f, ref BRBotSystem.Filter filter)
        {
            if (!f.Unsafe.TryGetPointer<BRMemory>(filter.entity, out var targets)) return BotConstants.Priority_None;

            // Check existing
            if (targets->enemy != default && f.Unsafe.TryGetPointer<Health>(targets->enemy, out var enemyHealth))
            {
                if (enemyHealth->currentValue > 0) return BotConstants.Priority_High;
                targets->enemy = default;
            }

            var comps = f.Filter<Team, Health, Transform2D>();
            while (comps.NextUnsafe(out var entity, out var team, out var health, out var transform))
            {
                if (health->currentValue <= 0 || f.Has<SurvivalNexus>(entity)) continue;

                if (team->team != filter.team->team)
                {
                    targets->enemy = entity;
                    return BotConstants.Priority_High;
                }
            }
            return BotConstants.Priority_None;
        }

        public static FP EvaluateDefeatEnemy_Champion(Frame f, ref BRBotSystem.Filter filter)
        {
            if (!f.Unsafe.TryGetPointer<BRMemory>(filter.entity, out var targets)) return BotConstants.Priority_None;

            EvaluateDroppingEnemy(f, ref filter, targets);

            EntityRef bestTarget = default;
            EntityRef lastTarget = targets->enemy;
            FP targetDist = FP.UseableMax;

            bool requiresWithinRange = filter.team->team != ProfileHelper.NPC_TEAM || !f.RuntimeConfig.gameMode.HasFlag(GameMode.Survival);

            // Iterate players
            for (int i = 0; i < f.Global->pvpMatchInfo.players.Length; i++)
            {
                var p = f.Global->pvpMatchInfo.players[i];
                if (p.entity == default || p.entity == filter.entity || p.team == filter.team->team) continue;

                if (f.TryGet<Health>(p.entity, out var ph) && ph.currentValue <= 0) continue;

                FP d = QTools.Distance(f, filter.entity, p.entity);
                if (requiresWithinRange && d > BotConstants.Distance_OnScreen) continue;
                if (BotHelper.IsEntityStealthToUs(f, filter.entity, p.entity)) continue;

                if (d < targetDist)
                {
                    targetDist = d;
                    bestTarget = p.entity;
                }
            }

            targets->closestEnemy = targetDist;

            // Flag Carrier override
            if (f.RuntimeConfig.gameMode.HasFlag(GameMode.CTF) && bestTarget != default && f.Has<CarriedFlag>(bestTarget))
            {
                targets->enemy = bestTarget;
                return targetDist < BotConstants.Distance_OnScreen / 2 ? BotConstants.Priority_Must : BotConstants.Priority_Highest;
            }

            // Compare with last target stickiness
            if (lastTarget != default && bestTarget != default)
            {
                bestTarget = CompareTargets(f, ref filter, lastTarget, bestTarget);
            }
            else if (lastTarget != default)
            {
                bestTarget = lastTarget;
            }

            targets->enemy = bestTarget;
            targetDist = bestTarget != default ? QTools.Distance(f, filter.entity, bestTarget) : FP.UseableMax;

            if (targetDist < FP._5)
            {
                return filter.bot->goal == BotGoal.RunawayFromEnemy ? BotConstants.Priority_Standard : BotConstants.Priority_Highest;
            }

            // Check retaliation (Who hit us?)
            EvaluateRetaliation(f, filter.entity, filter.health, filter.team, ref bestTarget, ref targetDist);
            if (bestTarget != default) targets->enemy = bestTarget;

            FP priority = BotConstants.Priority_None;
            if (bestTarget != default)
            {
                if (targetDist < BotConstants.Distance_OnScreen / 2) priority = BotConstants.Priority_Highest;
                else if (targetDist < BotConstants.Distance_OnScreen) priority = BotConstants.Priority_High;
                else priority = BotConstants.Priority_Standard;
            }

            // Loot/Breakables Check (lower priority than players)
            if (priority < BotConstants.Priority_High)
            {
                FP lootPrio = EvaluateBreakables(f, ref filter, ref targets);
                if (lootPrio > priority) priority = lootPrio;
            }

            return filter.bot->goal == BotGoal.RunawayFromEnemy ? priority * FP._0_75 : priority;
        }

        private static void EvaluateRetaliation(Frame f, EntityRef us, Health* health, Team* team, ref EntityRef bestTarget, ref FP targetDist)
        {
            var list = health->GetHistory(f);
            for (int i = 0; i < list.Count; i++)
            {
                var attacker = list[i].owner;
                if (attacker == default || f.Global->time - list[i].timestamp > FP._5) continue;
                if (!f.TryGet<OwnedByEntity>(attacker, out _) || (f.TryGet<Team>(attacker, out var t) && t.team == team->team)) continue;

                FP d = QTools.Distance(f, us, attacker);
                if (d > BotConstants.Distance_OnScreen || d > targetDist) continue;
                if (f.TryGet<Health>(attacker, out var ah) && ah.currentValue <= 0) continue;
                if (f.TryGet<Turret>(attacker, out var tur) && tur.isDisabled) continue;
                if (BotHelper.IsEntityStealthToUs(f, us, attacker)) continue;

                targetDist = d;
                bestTarget = attacker;
            }
        }

        private static FP EvaluateBreakables(Frame f, ref BRBotSystem.Filter filter, ref BRMemory* targets)
        {
            var comps = f.Filter<SpawnsEntity, Health, Transform2D>();
            EntityRef bestBox = default;
            FP dist = FP.UseableMax;

            while (comps.NextUnsafe(out var entity, out var spawner, out var health, out var transform))
            {
                if (spawner->spawnerType != SpawnerType.BRLoot || health->currentValue <= 0) continue;
                
                FP d = FPVector2.Distance(filter.transform->Position, transform->Position);
                if (d >= BotConstants.Distance_OnScreen) continue;

                if (d < dist)
                {
                    bestBox = entity;
                    dist = d;
                }
            }

            if (bestBox != default)
            {
                targets->enemy = bestBox;
                if (dist < BotConstants.Distance_OnScreen / 3) return BotConstants.Priority_Highest;
                if (dist < BotConstants.Distance_OnScreen / 2) return BotConstants.Priority_High;
                return BotConstants.Priority_Standard;
            }
            return BotConstants.Priority_None;
        }

        public static void EvaluateDroppingEnemy(Frame f, ref BRBotSystem.Filter filter, BRMemory* memory)
        {
            if (memory->enemy == default || !f.Unsafe.TryGetPointer<Health>(memory->enemy, out var enemyHealth)) return;

            if (enemyHealth->currentValue <= 0)
            {
                memory->enemy = default;
                return;
            }

            // Commit to target if recently initialized or few players remain
            if (GameModeHelper.GetPlayerLimit(f) <= 2 || (f.Global->time - filter.bot->lastTimeGoalInitialized) < FP._6)
            {
                return;
            }

            // Drop if we haven't hit them recently
            if (!enemyHealth->WasAttackedBy(f, filter.entity, FP._8))
            {
                if (QTools.Distance(f, filter.entity, memory->enemy) > BotConstants.Distance_OnScreen)
                {
                    memory->enemy = default;
                }
            }
        }

        public static EntityRef CompareTargets(Frame f, ref BRBotSystem.Filter filter, EntityRef e1, EntityRef e2)
        {
            int score1 = 0;
            int score2 = 0;

            FP d1 = QTools.Distance(f, e1, filter.entity);
            FP d2 = QTools.Distance(f, e2, filter.entity);

            // Power Comparison (Social Info)
            int p1 = f.TryGet<PlayerSocialInfo>(e1, out var psi1) ? psi1.power : 0;
            int p2 = f.TryGet<PlayerSocialInfo>(e2, out var psi2) ? psi2.power : 0;

            if (p1 > 0 && p2 > 0)
            {
                if (p1 > p2) score2 += (p1 / p2 >= 2) ? 2 : 1;
                else score1 += (p2 / p1 >= 2) ? 2 : 1;
            }
            else if (p1 == 0) return e2;
            else if (p2 == 0) return e1;

            // Visibility Preference
            if (d1 > BotConstants.Distance_OnScreen && d2 <= BotConstants.Distance_OnScreen) return e2;
            if (d2 > BotConstants.Distance_OnScreen && d1 <= BotConstants.Distance_OnScreen) return e1;

            // Distance Preference
            if (d1 < d2) score1++; else score2++;

            // Health Preference (Lower is better, but Max HP matters too)
            if (f.TryGet<Health>(e1, out var h1) && f.TryGet<Health>(e2, out var h2))
            {
                if (h1.currentValue <= 0 && h2.currentValue > 0) return e2;
                if (h2.currentValue <= 0 && h1.currentValue > 0) return e1;

                score1 += (h1.maximumHealth < h2.maximumHealth) ? 1 : 0;
                score2 += (h2.maximumHealth < h1.maximumHealth) ? 1 : 0;

                score1 += (h1.currentValue < h2.currentValue) ? 1 : 0;
                score2 += (h2.currentValue < h1.currentValue) ? 1 : 0;
            }

            // Recency Preference (Stick to who we hit last)
            return score1 >= score2 ? e1 : e2;
        }

        #endregion
    }
}