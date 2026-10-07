using Godot;

namespace DefenseGame.Client.Data;

[GlobalClass]
public partial class StageCatalog : Resource
{
    [Export] public Godot.Collections.Array<StageDefinition> Stages { get; set; } = new();
}
