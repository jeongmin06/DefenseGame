using Godot;

namespace DefenseGame.Client.Skills;

public enum SkillProfileSaveStatus
{
    Saved,
    Failed,
    BlockedProtectedData,
    MigrationRequired
}

[GlobalClass]
public partial class SkillProfileSaveResult : RefCounted
{
    public SkillProfileSaveStatus Status { get; set; }
    public string StatusCode => Status.ToString();
    public string Message { get; set; } = "";
}
