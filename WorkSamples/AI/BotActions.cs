namespace MySamples
{
    /// <summary>
    /// Stateless container for atomic bot behaviors.
    /// Handles physical movement, attacking, and social interactions.
    /// </summary>
    internal unsafe class BotActions
    {
        #region Movement Wrappers

        /// <summary>
        /// Moves the bot randomly within the world bounds.
        /// </summary>
        public static bool ExploreWorld(Frame f, ref BRBotSystem.Filter filter)
        {
            return MoveToPosition(f, ref filter, filter.bot->destination.XOY, BotConstants.StopDist_Explore, true);
        }

        /// <summary>
        /// Navigates the bot to the designated ATM point.
        /// </summary>
        public static bool GoToATM(Frame f, ref BRBotSystem.Filter filter)
        {
            return MoveToPosition(f, ref filter, filter.bot->destination.XOY, BotConstants.StopDist_ATM, true);
        }

        /// <summary>
        /// Moves the bot away from the shrinking death zone.
        /// </summary>
        public static bool AvoidDeathZone(Frame f, ref BRBotSystem.Filter filter)
        {
            return MoveToPosition(f, ref filter, filter.bot->destination.XOY, BotConstants.StopDist_DeathZone);
        }

        /// <summary>
        /// Approaches a collectible item to pick it up.
        /// </summary>
        public static bool GrabCollectible(Frame f, ref BRBotSystem.Filter filter)
        {
            return MoveToPosition(f, ref filter, filter.bot->destination.XOY, BotConstants.StopDist_Collectible, true);
        }

        /// <summary>
        /// Moves to the capture point to deliver a flag.
        /// </summary>
        public static bool DeliverFlag(Frame f, ref BRBotSystem.Filter filter)
        {
            return MoveToPosition(f, ref filter, filter.bot->destination.XOY, BotConstants.StopDist_FlagDelivery, true);
        }

        /// <summary>
        /// Moves to a King of the Hill objective.
        /// </summary>
        public static bool MoveToHill(Frame f, ref BRBotSystem.Filter filter)
        {
            return MoveToPosition(f, ref filter, filter.bot->destination.XOY, BotConstants.StopDist_Hill, true);
        }

        #endregion

        #region Target Movement

        /// <summary>
        /// Moves the bot towards its current locked target, adjusting for line of sight and stealth.
        /// </summary>
        public static bool MoveToTarget(Frame f, ref BRBotSystem.Filter filter)
        {
            if (filter.bot->target == default || !f.Unsafe.TryGetPointer<Transform2D>(filter.bot->target, out var targetTransform))
            {
                return false;
            }

            var pos = BotHelper.BestGuessPositionOnTarget(f, filter.entity, filter.transform, filter.bot->target, null, null, default);
            FP stopDist = filter.controller->attackRange;

            bool isObstructed = BotHelper.ViewToTargetIsObstructed(f, filter.controller, targetTransform);
            bool isStealthed = BotHelper.IsEntityStealthToUs(f, filter.entity, filter.bot->target);

            if (isObstructed || isStealthed)
            {
                stopDist = FP._0_33;
            }

            return MoveToPosition(f, ref filter, pos.XOY, stopDist, false);
        }

        /// <summary>
        /// Core navigation logic. Handles pathfinding queries and stopping conditions.
        /// </summary>
        /// <param name="allowMiddlePoints">If true, adds curved intermediate points for more organic movement.</param>
        public static bool MoveToPosition(Frame f, ref BRBotSystem.Filter filter, FPVector3 position, FP stopDist, bool allowMiddlePoints = false)
        {
            if (!f.Unsafe.TryGetPointer<Playable>(filter.entity, out var playable))
            {
                return false;
            }

            filter.bot->currentStopDist = stopDist;
            filter.bot->destination = position.XZ;

            bool destinationReached = FPVector2.Distance(position.XZ, filter.transform->Position) <= stopDist;

            if (destinationReached)
            {
                filter.navigator->Stop(f, filter.entity);
                filter.controller->StopRun(playable);
                return true;
            }

            if (f.Map.NavMeshes != null && f.Map.NavMeshes.Count > 0)
            {
                filter.navigator->SetTarget(f, position, f.Map.NavMeshes["navmesh"]);
                filter.bot->allowsMiddlePoint = allowMiddlePoints;

                if (filter.bot->forceRepath)
                {
                    filter.navigator->ForceRepath(f);
                    filter.bot->forceRepath = false;
                    filter.bot->middlePoint = default;
                }
            }

            return false;
        }

        /// <summary>
        /// Halts all movement input and navigation immediately.
        /// </summary>
        public static void StopMoving(Frame f, ref BRBotSystem.Filter filter, bool stopNavigatorToo = true)
        {
            if (stopNavigatorToo)
            {
                filter.navigator->Stop(f, filter.entity);
            }

            if (f.Unsafe.TryGetPointer<Playable>(filter.entity, out var playable))
            {
                filter.controller->StopRun(playable);
            }
        }

        #endregion

        #region Actions

        /// <summary>
        /// Attempts to execute an attack ability against the current enemy target.
        /// </summary>
        /// <returns>True if an ability or attack was successfully queued.</returns>
        public static bool AttackEnemy(Frame f, ref BRBotSystem.Filter filter)
        {
            EntityRef targetEnemy = default;

            switch (filter.bot->category)
            {
                case BotCategory.SurvivalEnemy:
                case BotCategory.Minion:
                    if (f.TryGet<BRTargets_Minion>(filter.entity, out var minionTargets))
                    {
                        targetEnemy = minionTargets.enemy;
                    }
                    break;
                case BotCategory.Champion:
                    if (f.TryGet<BRMemory>(filter.entity, out var memory))
                    {
                        targetEnemy = memory.enemy;
                    }
                    break;
            }

            if (targetEnemy != default)
            {
                return BotHelper.ActivateBestAbilityOption(f, filter.entity, targetEnemy, ReasonForUse.KillTarget, true);
            }

            return false;
        }

        /// <summary>
        /// Triggers a social emote based on RNG and context.
        /// </summary>
        public static void Emote(Frame f, EntityRef entity, EmoteOccasion reason)
        {
            if (f.RNG->Next() > BotConstants.Chance_Emote) return;
            f.Events.BotShouldUseEmoteEvent(entity, reason);
        }

        #endregion
    }
}