namespace MySamples
{
    [System.Serializable]
    public partial class ProjectileOrb : ProjectileData
    {
        [Tooltip("How frequently does the orb spawn the smaller projectiles")]
        public FP orbShootEvery = FP._1;
        [Tooltip("Reference to our orb projectile asset")]
        public AssetRefProjectileData orbProjectile;
        [Tooltip("Go crazy with smaller projectiles. If false it will shoot sub-projectiles in its current direction")]
        public QBoolean randomizedDirection = true;

        public override unsafe void Move(Frame f, Projectile* projectile, Transform2D* transform)
        {
            base.Move(f, projectile, transform);

            // Handle timed spawning
            if (projectile->tempFP < orbShootEvery)
            {
                projectile->tempFP += f.DeltaTime;
            }
            else
            {
                projectile->tempFP = FP._0;
                SpawnOrbProjectile(f, projectile);
            }
        }

        protected unsafe void SpawnOrbProjectile(Frame f, Projectile* orb)
        {
            if (f.Unsafe.TryGetPointer<Transform2D>(orb->entity, out var orbTransform))
            {
                if (orbProjectile != default)
                {
                    var data = f.FindAsset<ProjectileData>(orbProjectile.Id);
                    if (randomizedDirection) data.Spawn(f, orb->owner, orbTransform->Position, new FPVector2(f.RNG->Next(-FP._1, FP._1), f.RNG->Next(-FP._1, FP._1)).Normalized, default, orb->abilityId);
                    else
                    {
                        data.Spawn(f, orb->owner, orbTransform->Position, default, default, orb->abilityId);
                    }
                }
            }
        }
    }
}