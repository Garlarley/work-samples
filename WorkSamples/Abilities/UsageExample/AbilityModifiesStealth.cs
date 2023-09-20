namespace MySamples
{
    [System.Serializable]
    public partial class AbilityModifiesStealth : AbilityData
    {
        [Header("Stealth")]
        [Tooltip("How long into the ability before the actual stealh effect occurs")]
        public FP modifyDelay;
        [Tooltip("Is this an enter or exit stealth?")]
        public QBoolean isEnterStealth = true;
        [Tooltip("Teleport to player aimed location?")]
        public QBoolean teleportToAbilityDirection = false;

        public override unsafe void UpdateAbility(Frame f, Ability* ability, AbilityData abilityData)
        {
            base.UpdateAbility(f, ability, abilityData);
            if (ability->timeElapsed > modifyDelay && ability->lastTimeDelayUsed <= modifyDelay)
            {
                ability->lastTimeDelayUsed = ability->timeElapsed;
                ModifyStealth(f, ability, abilityData);
            }
        }

        public virtual unsafe void ModifyStealth(Frame f, Ability* ability, AbilityData abilityData)
        {
            if (f.Unsafe.TryGetPointer<Stealth>(ability->owner, out var stealth))
            {
                if (stealth->stealthItems[0].stealthData != default)
                {
                    var data = f.FindAsset<StealthData>(stealth->stealthItems[0].stealthData.Id);
                    if (data != null)
                    {
                        data.SetStealthState(f, ability->owner, stealth, 0, isEnterStealth, augment);
                    }
                }
            }
            if(teleportToAbilityDirection && f.Unsafe.TryGetPointer<Transform2D>(ability->owner, out var transform) && f.Unsafe.TryGetPointer<CharacterController>(ability->owner, out var controller))
            {
                FP factor = FP._1;
                if(damage != null && damage[0].abilityDirectionBonus != default) factor = damage[0].abilityDirectionBonus;
                FPVector2 pos = transform->Position + (controller->state.abilityDirection * factor);
                pos = BRBotBrain.MovePositionToClosestNavmeshSpot(f, pos, ability->abilityDirection);
                transform->Position = pos;
            }
        }
    }
}