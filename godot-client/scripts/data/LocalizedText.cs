using Godot;
using System;

namespace DefenseGame.Client.Data;

[GlobalClass]
public partial class LocalizedText : Resource
{
    [Export] public Godot.Collections.Dictionary<string, string> Values { get; set; } = new();

    public string Resolve(string localeCode)
    {
        string locale = Normalize(localeCode);
        if (TryGet(locale, out string exact)) return exact;
        int separator = locale.IndexOf('-');
        if (separator > 0 && TryGet(locale[..separator], out string language)) return language;
        if (TryGet("ko", out string korean)) return korean;
        if (TryGet("en", out string english)) return english;
        foreach (string value in Values.Values)
            if (!string.IsNullOrWhiteSpace(value)) return value;
        return "";
    }

    private bool TryGet(string key, out string value)
    {
        if (Values.TryGetValue(key, out string? found) && !string.IsNullOrWhiteSpace(found))
        {
            value = found;
            return true;
        }
        value = "";
        return false;
    }

    private static string Normalize(string localeCode) =>
        (localeCode ?? "").Trim().Replace('_', '-').ToLowerInvariant();
}
