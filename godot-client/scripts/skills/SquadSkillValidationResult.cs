using Godot;

namespace DefenseGame.Client.Skills;

[GlobalClass]
public partial class SquadSkillValidationResult : RefCounted
{
    public bool IsValid => Issues.Count == 0;
    public int UsablePoints { get; set; }
    public Godot.Collections.Array<SquadSkillValidationIssue> Issues { get; set; } = new();
    public Godot.Collections.Array<CharacterSkillLoadout> CharacterLoadouts { get; set; } = new();

    public SkillLoadout? GetLoadout(string characterId)
    {
        foreach (CharacterSkillLoadout entry in CharacterLoadouts)
            if (entry.CharacterId == characterId) return entry.Loadout;
        return null;
    }
}
