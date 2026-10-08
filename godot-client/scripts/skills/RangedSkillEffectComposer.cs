using DefenseGame.Client.Combat;
using DefenseGame.Client.Data;
using Godot;
using System.Collections.Generic;

namespace DefenseGame.Client.Skills;

[GlobalClass]
public partial class RangedSkillEffectComposer : RefCounted
{
    public RangedAttackSettings Compose(SkillLoadout? loadout)
    {
        var result = new RangedAttackSettings();
        if (loadout is null || loadout.ActiveSkill is null)
            return result;

        SkillDefinition active = loadout.ActiveSkill;
        result.ProjectileCount = Mathf.Max(1, active.BaseProjectileCount);
        result.PierceCount = Mathf.Max(0, active.BasePierceCount);
        result.DamageMultiplier = active.BaseDamageMultiplier > 0.0f ? active.BaseDamageMultiplier : 1.0f;

        var tags = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (string tag in active.Tags)
            if (tags.Add(tag)) result.Tags.Add(tag);

        ApplyIntegerEffects(loadout, result);
        ApplyAddedDamage(loadout, result);
        ApplyDamageMultipliers(loadout, result);
        ApplyTags(loadout, result, tags);
        return result;
    }

    private static void ApplyIntegerEffects(SkillLoadout loadout, RangedAttackSettings result)
    {
        foreach (SkillDefinition support in loadout.SupportSkills)
            foreach (SkillEffectDefinition effect in support.Effects)
                switch (effect.Type)
                {
                    case SkillEffectType.AddProjectiles:
                        result.ProjectileCount += effect.IntValue;
                        break;
                    case SkillEffectType.AddPierce:
                        result.PierceCount += effect.IntValue;
                        break;
                }
    }

    private static void ApplyAddedDamage(SkillLoadout loadout, RangedAttackSettings result)
    {
        foreach (SkillDefinition support in loadout.SupportSkills)
            foreach (SkillEffectDefinition effect in support.Effects)
                if (effect.Type == SkillEffectType.AddFireDamage) result.AddedHitDamage += effect.FloatValue;
    }

    private static void ApplyDamageMultipliers(SkillLoadout loadout, RangedAttackSettings result)
    {
        foreach (SkillDefinition support in loadout.SupportSkills)
            foreach (SkillEffectDefinition effect in support.Effects)
                if (effect.Type == SkillEffectType.MultiplyDamage) result.DamageMultiplier *= effect.FloatValue;
    }

    private static void ApplyTags(SkillLoadout loadout, RangedAttackSettings result, HashSet<string> tags)
    {
        foreach (SkillDefinition support in loadout.SupportSkills)
            foreach (SkillEffectDefinition effect in support.Effects)
                if (effect.Type == SkillEffectType.AddTag && tags.Add(effect.TagValue)) result.Tags.Add(effect.TagValue);
    }
}
