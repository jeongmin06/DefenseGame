namespace DefenseGame.Client.Data;

using DefenseGame.Client.Skills;

public static class StageSelectionState
{
    public static StageDefinition? SelectedStage { get; set; }
    public static SkillLoadout? SelectedLoadout { get; set; }
}
