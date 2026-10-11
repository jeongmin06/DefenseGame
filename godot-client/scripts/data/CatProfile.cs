using Godot;

namespace DefenseGame.Client.Data;

[GlobalClass]
public partial class CatProfile : Resource
{
    [Export] public string CharacterId { get; set; } = "";
    [Export] public string DisplayName { get; set; } = "";
    [Export] public LocalizedText Description { get; set; } = null!;
    [Export] public string UnitId { get; set; } = "";
    [Export] public UnitDefinition? Unit { get; set; }
}
