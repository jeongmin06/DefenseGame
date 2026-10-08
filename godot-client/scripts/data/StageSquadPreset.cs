using Godot;

namespace DefenseGame.Client.Data;

[GlobalClass]
public partial class StageSquadPreset : Resource
{
    [Export] public string StageId { get; set; } = "";
    [Export] public Godot.Collections.Array<string> CharacterIds { get; set; } = new();
    [Export] public string UpdatedAtUtc { get; set; } = "";
}
