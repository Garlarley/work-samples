namespace MySamples
{
    [System.Serializable]
    public partial class AbilityTeleport : AbilityData
    {
        [Header("Teleport")]
        [Tooltip("How long while ability is active before we actually trigger the movement effect")]
        public FP teleportDelay = FP._0;
        [Tooltip("Offset from the appearing location. This is rotated based on the controller facing direction")]
        public FPVector2 locationOffset = FPVector2.Zero;
        [Tooltip("Overrides ability speed")]
        public FP teleportSpeed = FP._1;
        [Tooltip("Should we utilize raycasting to ensure a safe location (prevent teleporting into things)")]
        public bool useRaycasting;
        [Tooltip("To tell the visual simulation to run a specific feedback, otherwise keep 0")]
        public int appearGenericFeedbackId = 0;
        [Tooltip("An enforced bonus range added to player aim. Read as minimum possible teleport distance")]
        public FP rangeBonus = FP._5;

        public override unsafe void StartAbility(Frame f, EntityRef owner, Ability* ability)
        {
            base.StartAbility(f, owner, ability);

            if (f.Unsafe.TryGetPointer<Transform2D>(ability->owner, out var transform))
            {
                FPVector2 off = locationOffset;
                if (rangeBonus != default)
                {
                    off.Y += rangeBonus * ability->abilityDirection.Magnitude;
                }
                CharacterController* controller = default;
                f.Unsafe.TryGetPointer<CharacterController>(ability->owner, out controller);
                if (controller != default)
                {
                    off = controller->RotateByCharacterDirection(off);
                }
                if (f.Has<CarriedFlag>(ability->owner))
                {
                    off *= FP._0_50;
                }
                ability->abilitySpeed = teleportSpeed;
                ability->tempVector = transform->Position + off;

                if (useRaycasting && controller != default)
                {
                    FPVector2 pos = transform->Position + off;
                    pos = BRBotBrain.MovePositionToClosestNavmeshSpot(f, pos, ability->abilityDirection);
                    ability->tempVector = pos;
                }

                f.Events.FeedbackEvent(owner, appearGenericFeedbackId, true, true, ability->tempVector);
            }
        }

        public override unsafe void UpdateAbility(Frame f, Ability* ability, AbilityData abilityData)
        {
            base.UpdateAbility(f, ability, abilityData);
            if (ability->timeElapsed > teleportDelay && ability->lastTimeDelayUsed <= teleportDelay)
            {
                ability->lastTimeDelayUsed = ability->timeElapsed;

                if (f.Unsafe.TryGetPointer<Transform2D>(ability->owner, out var transform))
                {
                    transform->Position = ability->tempVector;
                }
            }
        }
    }
}