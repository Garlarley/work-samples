using Photon.Deterministic;
using Quantum.Inspector;

namespace Quantum
{
    [System.Serializable]
    public partial class ProjectileControlled : ProjectileData
    {
        [Tooltip("Gain buffs or debuffs while controlling projectile")]
        public AssetRefEffectData[] selfEffects;
        [Tooltip("Most implementations will have a stuff effect placed on controlling entity. Place the id of that effect here")]
        public int removeOnTerminateEffectId;
        [Tooltip("In case controlling this projectile is synced with an active ability")]
        public int abilityIdToInterrupt = 0;

        public override unsafe void Initialize(Frame f, Projectile* projectile, Transform2D* transform, FPVector2 direction = default)
        {
            base.Initialize(f, projectile, transform, direction);
            projectile->tempFPVector2 = projectile->direction;
            if (selfEffects != null && projectile->owner != default && f.Unsafe.TryGetPointer<EffectHandler>(projectile->owner, out var handler))
            {
                handler->ApplyEffects(f, projectile->owner, selfEffects);
            }
        }

        /// <summary>
        /// Allows player / bot input to control projectile
        /// </summary>
        public unsafe override void Move(Frame f, Projectile* projectile, Transform2D* transform)
        {
            if (projectile->owner != default && f.Unsafe.TryGetPointer<Playable>(projectile->owner, out var playable))
            {
                {
                    var input = PlayableHelper.GetInput(f, playable);

                    projectile->direction = input->MovementDirection.Normalized;
                }
                if (projectile->direction.Magnitude <= FP._0_50)
                {
                    projectile->direction = projectile->tempFPVector2;
                }
                else
                {
                    projectile->tempFPVector2 = projectile->direction.Normalized;
                }
            }

            base.Move(f, projectile, transform);
        }

        /// <summary>
        /// To consume self imposed CC effect
        /// </summary>

        public override unsafe void Terminate(Frame f, Projectile* projectile, FPVector2 pointOfImpact, bool haveHitSomething = false, EntityRef hitTarget = default)
        {
            if (removeOnTerminateEffectId != 0 && projectile->original_owner != default && f.Unsafe.TryGetPointer<EffectHandler>(projectile->original_owner, out var handler))
            {
                handler->ConsumeEffect(f, removeOnTerminateEffectId, EffectConsumeType.ConsumeAll, projectile->original_owner);
            }

            if (projectile->owner != default && abilityIdToInterrupt > 0 && f.Unsafe.TryGetPointer<AbilityInventory>(projectile->owner, out var inv))
            {
                inv->InterruptAllAbilities(f, default, abilityIdToInterrupt);
            }

            base.Terminate(f, projectile, pointOfImpact, haveHitSomething, hitTarget);
        }
    }
}