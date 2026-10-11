using DefenseGame.Client.Data;
using Godot;

namespace DefenseGame.Client.UI;

public static class UiLocalization
{
    public static string LocaleOverride { get; set; } = "";

    public static string LocaleCode => !string.IsNullOrWhiteSpace(LocaleOverride)
        ? LocaleOverride
        : (string)ProjectSettings.GetSetting("defense_game/ui/locale", "ko");

    public static string Description(LocalizedText? text) => text?.Resolve(LocaleCode) ?? "";
}
