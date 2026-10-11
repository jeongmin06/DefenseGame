using DefenseGame.Client.Data;
using System.Collections.Generic;
using System.Linq;

namespace DefenseGame.Client.UI;

public static class KoreanUiText
{
    public static string Role(UnitRole role) => role switch
    {
        UnitRole.Ranged => "궁수",
        UnitRole.Melee => "전사",
        UnitRole.Support => "치유사",
        UnitRole.Enemy => "적",
        _ => "알 수 없음"
    };

    public static string Tag(string tag) => tag switch
    {
        "ATTACK" => "공격",
        "BOW" => "활",
        "PROJECTILE" => "투사체",
        "PHYSICAL" => "물리",
        "HIT" => "적중",
        "FIRE" => "화염",
        "HEAL" => "치유",
        _ => tag
    };

    public static string Tags(IEnumerable<string> tags, string separator = " · ") =>
        string.Join(separator, tags.Select(Tag));
}
