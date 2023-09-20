namespace MySamples
{
    [System.Serializable]
    public partial class AbilityFanProjectile : AbilityProjectile
    {
        [Header("Fan")]
        [Tooltip("How many projectiles?")]
        public byte projectileCount = 3;
        [Tooltip("The maximum angle to shoot the projectile fan in")]
        public FP fanAngle = 45;
        [Tooltip("to offset the beginning point of the arc")]
        public FP startAngle = FP._0;

        protected override unsafe void CreateProjectile(Frame f, ProjectileData data, Ability* ability, Transform2D* transform, FPVector2 offset)
        {
            f.Unsafe.TryGetPointer<CharacterController>(ability->owner, out var controller);
            // set initial direction
            FPVector2 direction = FPVector2.Zero;
            direction = controller->state.abilityDirection;

            if (direction == default)
            {
                direction = controller->state.direction;
            }

            if (projectileCount < 2 || fanAngle < 1) base.CreateProjectile(f, data, ability, transform, offset);
            else
            {
                // start from the beginning of the angle (top)
                if (controller != default && controller->state.IsFacingRight == false) direction = FPVector2.Rotate(direction, (-fanAngle + startAngle) * FP.Deg2Rad);
                else direction = FPVector2.Rotate(direction, (-fanAngle - startAngle) * FP.Deg2Rad);
                FP angle = (fanAngle * 2) / (projectileCount - 1);
                for (int i = 0; i < projectileCount; i++)
                {
                    data.Spawn(f, ability->owner, transform->Position + offset, direction, projectile, ability->abilityId);
                    // rotate in prep for next spawn
                    direction = FPVector2.Rotate(direction, angle * FP.Deg2Rad);
                }
            }
        }
    }
}