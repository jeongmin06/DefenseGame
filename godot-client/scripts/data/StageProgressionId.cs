using System;
using System.Globalization;

namespace DefenseGame.Client.Data;

public static class StageProgressionId
{
    private const string Prefix = "stage_";

    public static bool TryParse(string contentId, out int progressionId)
    {
        progressionId = 0;
        return contentId.StartsWith(Prefix, StringComparison.Ordinal)
            && int.TryParse(contentId.AsSpan(Prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out progressionId)
            && progressionId > 0;
    }
}
