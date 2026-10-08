namespace DefenseGame.Client.Data;

using DefenseGame.Client.Skills;

public static class StageSelectionState
{
    public static StageDefinition? SelectedStage { get; set; }
    public static SkillLoadout? SelectedLoadout { get; set; }
    public static Godot.Collections.Array<CharacterSkillLoadout> SelectedCharacterLoadouts { get; set; } = new();
    public static Godot.Collections.Array<string> SelectedCharacterIds { get; set; } = new();
    public static Godot.Collections.Array<CatProfile> SelectedProfiles { get; set; } = new();
    public static string ActiveSquadStageId { get; set; } = "";
    public static string SkillProfileStoragePathOverride { get; set; } = "";
    public static string StageSquadStoragePathOverride { get; set; } = "";

    public static void BeginStage(StageDefinition stage)
    {
        SelectedStage = stage;
        SelectedLoadout = null;
        SelectedCharacterLoadouts = new();
        SelectedCharacterIds = new();
        SelectedProfiles = new();
        ActiveSquadStageId = "";
    }

    public static void ClearStage()
    {
        SelectedStage = null;
        SelectedLoadout = null;
        SelectedCharacterLoadouts = new();
        SelectedCharacterIds = new();
        SelectedProfiles = new();
        ActiveSquadStageId = "";
    }
}
