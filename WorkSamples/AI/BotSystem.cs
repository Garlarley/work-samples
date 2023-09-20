namespace MySamples
{
    public unsafe class BotSystem : SystemMainThreadFilter<BRBotSystem.Filter>
    {
        public struct Filter
        {
            public EntityRef entity;
            public Health* health;
            public CharacterController* controller;
            public Transform2D* transform;
            public Team* team;
            public NavMeshPathfinder* navigator;
            public BRBot* bot;
        }

        public override void Update(Frame f, ref Filter filter)
        {
            if (!f.IsVerified || filter.health->currentValue <= 0 || GameModeHelper.InWaitingMode(f))
            {
                return;
            }

            InitializeBotIfNeeded(f, ref filter);
            UpdateRates(filter.bot, out int updateRate, out int goalUpdateRate);

            if (f.Number % goalUpdateRate == 0 || filter.bot->updateGoal)
            {
                BRBotBrain.FindBestGoal(f, ref filter);
                filter.bot->updateGoal = false;
            }

            if (f.Number % updateRate == 0)
            {
                ExecuteCurrentGoal(f, ref filter);
            }
        }

        /// <summary>
        /// Handles one-time initialization logic for the bot entity.
        /// </summary>
        private void InitializeBotIfNeeded(Frame f, ref Filter filter)
        {
            if (filter.bot->initialized) return;

            BRBotBrain.InitializeBot(f, ref filter);
            filter.bot->initialized = true;
        }

        /// <summary>
        /// Calculates the frame intervals for updates based on bot category offsets.
        /// </summary>
        private void UpdateRates(BRBot* bot, out int updateRate, out int goalUpdateRate)
        {
            updateRate = BRBotBrain.GetUpdateRate(bot->category) + bot->updateOffset;
            if (updateRate < 1) updateRate = 1;

            goalUpdateRate = BRBotBrain.GetGoalUpdateRate(bot->category) + bot->updateOffset;
            if (goalUpdateRate < 1) goalUpdateRate = 1;
        }

        /// <summary>
        /// Directs the bot behavior based on the currently selected active goal.
        /// </summary>
        protected void ExecuteCurrentGoal(Frame f, ref Filter filter)
        {
            // Champion bots in standard modes handle auxiliary abilities (like healing/shielding) automatically
            if (filter.bot->category == BotCategory.Champion && !f.RuntimeConfig.gameMode.HasFlag(GameMode.Survival))
            {
                BRBotBrain.MonitorAuxiliarAbilityUsage(f, ref filter);
            }

            switch (filter.bot->goal)
            {
                case BotGoal.None: ExecuteGoal_None(f, ref filter); break;
                case BotGoal.DestroyNexus: ExecuteGoal_DestroyNexus(f, ref filter); break;
                case BotGoal.StayInBrush: ExecuteGoal_StayInBrush(f, ref filter); break;
                case BotGoal.AvoidDeathZone: ExecuteGoal_AvoidDeathZone(f, ref filter); break;
                case BotGoal.DefeatEnemy: ExecuteGoal_DefeatEnemy(f, ref filter); break;
                case BotGoal.FollowOwner: ExecuteGoal_FollowOwner(f, ref filter); break;
                case BotGoal.GoToATM: ExecuteGoal_GoToATM(f, ref filter); break;
                case BotGoal.RunawayFromEnemy: ExecuteGoal_Runaway(f, ref filter); break;
                case BotGoal.RevivePlayer: ExecuteGoal_RevivePlayer(f, ref filter); break;
                case BotGoal.ExploreWorld: ExecuteGoal_ExploreWorld(f, ref filter); break;
                case BotGoal.GrabCollectible: ExecuteGoal_GrabCollectible(f, ref filter); break;
                case BotGoal.DeliverFlag: ExecuteGoal_DeliverFlag(f, ref filter); break;
                case BotGoal.StayInHill: ExecuteGoal_StayInHill(f, ref filter); break;
            }
        }

        #region Goal Execution Handlers

        private void ExecuteGoal_None(Frame f, ref Filter filter)
        {
            if (f.Unsafe.TryGetPointer<Playable>(filter.entity, out var playable))
            {
                filter.controller->StopRun(playable);
            }
        }

        private void ExecuteGoal_DestroyNexus(Frame f, ref Filter filter)
        {
            if (f.Has<BRBotPathMidPoint>(filter.bot->target))
            {
                if (BRBotActions.MoveToPosition(f, ref filter, filter.bot->destination.XOY, FP._1))
                {
                    BRBotBrain.InitializeGoal(f, ref filter, BotGoal.DestroyNexus, false);
                }
                return;
            }

            FP dist = QTools.Distance(f, filter.entity, filter.bot->target);
            bool canAct = filter.controller->CanUseAbility() || filter.controller->CanAttack();

            if (dist > FP._5 || !canAct || !BRBotActions.AttackEnemy(f, ref filter))
            {
                BRBotActions.MoveToPosition(f, ref filter, filter.bot->destination.XOY, FPMath.Min(BotConstants.StopDist_Combat, filter.controller->attackRange));
            }
        }

        private void ExecuteGoal_StayInBrush(Frame f, ref Filter filter)
        {
            BRBotActions.StopMoving(f, ref filter, false);
        }

        private void ExecuteGoal_AvoidDeathZone(Frame f, ref Filter filter)
        {
            BRBotActions.AvoidDeathZone(f, ref filter);
        }

        private void ExecuteGoal_DefeatEnemy(Frame f, ref Filter filter)
        {
            bool canAct = filter.controller->CanUseAbility() || filter.controller->CanAttack();
            if (!canAct || !BRBotActions.AttackEnemy(f, ref filter))
            {
                BRBotActions.MoveToTarget(f, ref filter);
            }
        }

        private void ExecuteGoal_FollowOwner(Frame f, ref Filter filter)
        {
            BRBotActions.MoveToTarget(f, ref filter);
        }

        private void ExecuteGoal_GoToATM(Frame f, ref Filter filter)
        {
            if (BRBotActions.GoToATM(f, ref filter))
            {
                BRBotBrain.InitializeGoal(f, ref filter, BotGoal.GoToATM, false);
            }
        }

        private void ExecuteGoal_Runaway(Frame f, ref Filter filter)
        {
            if (BRBotActions.ExploreWorld(f, ref filter))
            {
                BRBotBrain.InitializeGoal(f, ref filter, BotGoal.RunawayFromEnemy, false);
            }
        }

        private void ExecuteGoal_RevivePlayer(Frame f, ref Filter filter)
        {
            BRBotActions.MoveToPosition(f, ref filter, filter.bot->destination.XOY, BotConstants.StopDist_Revive, false);
        }

        private void ExecuteGoal_ExploreWorld(Frame f, ref Filter filter)
        {
            if (BRBotActions.ExploreWorld(f, ref filter))
            {
                BRBotBrain.InitializeGoal(f, ref filter, BotGoal.ExploreWorld, false);
            }
        }

        private void ExecuteGoal_GrabCollectible(Frame f, ref Filter filter)
        {
            if (BRBotActions.GrabCollectible(f, ref filter))
            {
                BRBotBrain.InitializeGoal(f, ref filter, BotGoal.GrabCollectible, false);
            }
        }

        private void ExecuteGoal_DeliverFlag(Frame f, ref Filter filter)
        {
            if (BRBotActions.DeliverFlag(f, ref filter))
            {
                BRBotBrain.InitializeGoal(f, ref filter, BotGoal.DeliverFlag, false);
            }
        }

        private void ExecuteGoal_StayInHill(Frame f, ref Filter filter)
        {
            if (BRBotActions.MoveToHill(f, ref filter))
            {
                BRBotBrain.InitializeGoal(f, ref filter, BotGoal.StayInHill, false);
            }
        }

        #endregion
    }
}