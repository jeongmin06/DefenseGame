using DefenseGame.Client.Data;

namespace DefenseGame.Client.Skills;

public sealed class UnitSkillEffectComposer
{
    public UnitSkillEffectSettings Compose(SkillLoadout? loadout)
    {
        float damageMultiplier = loadout?.ActiveSkill?.BaseDamageMultiplier ?? 1.0f;
        float addedHitDamage = 0.0f;
        float healingMultiplier = 1.0f;

        if (loadout is not null)
        {
            foreach (SkillDefinition support in loadout.SupportSkills)
            foreach (SkillEffectDefinition effect in support.Effects)
            {
                switch (effect.Type)
                {
                    case SkillEffectType.MultiplyDamage:
                        damageMultiplier *= effect.FloatValue;
                        break;
                    case SkillEffectType.AddFireDamage:
                        addedHitDamage += effect.FloatValue;
                        break;
                    case SkillEffectType.MultiplyHealing:
                        healingMultiplier *= effect.FloatValue;
                        break;
                }
            }
        }

        return new UnitSkillEffectSettings
        {
            DamageMultiplier = damageMultiplier,
            AddedHitDamage = addedHitDamage,
            HealingMultiplier = healingMultiplier
        };
    }
}
