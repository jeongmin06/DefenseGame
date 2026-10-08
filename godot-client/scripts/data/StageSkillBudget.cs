using Godot;

namespace DefenseGame.Client.Data;

[GlobalClass]
public partial class StageSkillBudget : Resource
{
    [Export] public string StageId { get; set; } = "";
    [Export] public bool HasStageCap { get; set; }
    [Export] public int StageCap { get; set; }

    public int GetUsablePoints(int playerUnlockedPoints) =>
        HasStageCap ? Mathf.Min(Mathf.Max(0, playerUnlockedPoints), Mathf.Max(0, StageCap)) : Mathf.Max(0, playerUnlockedPoints);
}
