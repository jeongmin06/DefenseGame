using System.Globalization;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

try
{
    var options = new Dictionary<string, string>();
    bool check = false;
    for (int i = 0; i < args.Length; i++)
    {
        if (args[i] == "--check") { check = true; continue; }
        if (args[i] is not ("--input" or "--output" or "--project-root") || i + 1 == args.Length)
            throw new InvalidDataException("arguments: use --input DIR --output DIR [--project-root DIR] [--check]");
        if (!options.TryAdd(args[i], args[++i])) throw new InvalidDataException("arguments: duplicate option");
    }
    string input = Path.GetFullPath(options.GetValueOrDefault("--input", "godot-client/balance-json"));
    string output = Path.GetFullPath(options.GetValueOrDefault("--output", "godot-client/data"));
    string project = Path.GetFullPath(options.GetValueOrDefault("--project-root", Path.GetDirectoryName(input)!));
    var pipeline = new Pipeline(project);
    var generated = pipeline.Generate(input); // All validation and rendering precede any writes.
    var existing = Directory.Exists(output) ? Directory.GetFiles(output, "*.tres", SearchOption.AllDirectories) : [];
    var stale = existing.Where(p => !generated.ContainsKey(Path.GetRelativePath(output, p).Replace('\\', '/'))).ToArray();
    if (check)
    {
        var differences = generated.Where(p => !File.Exists(Path.Combine(output, p.Key))
            || File.ReadAllText(Path.Combine(output, p.Key)) != p.Value).Select(p => p.Key).Concat(stale).ToArray();
        if (differences.Length > 0) throw new InvalidDataException("output: stale/missing files: " + string.Join(", ", differences));
        Console.WriteLine($"CHECK OK: {generated.Count} resources");
    }
    else
    {
        // Stage every byte in a sibling directory. A validation error cannot touch output.
        string staging = output + ".staging-" + Guid.NewGuid().ToString("N");
        string backup = output + ".backup-" + Guid.NewGuid().ToString("N");
        try
        {
            Directory.CreateDirectory(staging);
            if (Directory.Exists(output))
                foreach (string file in Directory.GetFiles(output, "*", SearchOption.AllDirectories))
                {
                    if (file.EndsWith(".tres", StringComparison.Ordinal)) continue;
                    string dest = Path.Combine(staging, Path.GetRelativePath(output, file));
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    File.Copy(file, dest);
                }
            foreach (var (path, content) in generated)
            {
                string dest = Path.Combine(staging, path);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.WriteAllText(dest, content, new UTF8Encoding(false));
            }
            if (Directory.Exists(output)) Directory.Move(output, backup);
            try { Directory.Move(staging, output); }
            catch { if (Directory.Exists(backup)) Directory.Move(backup, output); throw; }
            if (Directory.Exists(backup)) Directory.Delete(backup, true);
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
        Console.WriteLine($"GENERATED: {generated.Count} resources");
    }
    return 0;
}
catch (Exception ex) when (ex is InvalidDataException or JsonException or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

sealed class Pipeline(string project)
{
    static readonly JsonSerializerOptions GodotStringOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    static readonly string[] Roles = ["ranged", "melee", "support", "enemy"];
    static readonly string[] Placements = ["ground", "ground_or_path", "none"];
    static readonly string[] SkillRoles = ["active", "support"];
    static readonly string[] SkillTags = ["ATTACK", "BOW", "PROJECTILE", "PHYSICAL", "HIT", "FIRE", "HEAL"];
    static readonly string[] SkillEffectTypes = ["add_projectiles", "add_pierce", "add_fire_damage", "multiply_damage", "add_tag", "multiply_healing"];
    readonly Dictionary<string, JsonObject> units = new(StringComparer.Ordinal);
    [DoesNotReturn]
    static void Fail(string path, string message) => throw new InvalidDataException($"{path}: {message}");
    static JsonObject Obj(JsonNode? node, string path) => node as JsonObject ?? throw new InvalidDataException($"{path}: expected object");
    static JsonArray Arr(JsonNode? node, string path) => node as JsonArray ?? throw new InvalidDataException($"{path}: expected array");
    static string Str(JsonObject o, string key, string path)
    {
        if (o[key] is not JsonValue v || !v.TryGetValue<string>(out var s) || string.IsNullOrWhiteSpace(s))
            throw new InvalidDataException($"{path}.{key}: expected nonempty string");
        return s;
    }
    static double Num(JsonObject o, string key, string path, double min = 0, bool integer = false, bool positive = false)
    {
        if (o[key] is not JsonValue v || !v.TryGetValue<double>(out var n) || !double.IsFinite(n)
            || n < min || (positive && (n <= 0 || (float)n == 0)) || (integer && (n != Math.Truncate(n) || n > int.MaxValue)) || n > float.MaxValue)
            throw new InvalidDataException($"{path}.{key}: expected finite {(positive ? "positive" : "nonnegative")} {(integer ? "integer" : "number")}");
        return n;
    }
    static void Fields(JsonObject o, string path, params string[] allowed)
    {
        foreach (var (key, _) in o) if (!allowed.Contains(key)) Fail(path + "." + key, "unknown or inappropriate field");
    }
    static string Id(JsonObject o, string path)
    {
        string id = Str(o, "id", path);
        ValidateId(id, path + ".id");
        return id;
    }
    static string ValidateId(string id, string path)
    {
        if (!Regex.IsMatch(id, "^[a-z][a-z0-9_]*$")) Fail(path, "expected lowercase identifier");
        return id;
    }
    void ScenePath(JsonObject o, string key, string path)
    {
        string value = Str(o, key, path);
        if (!Regex.IsMatch(value, "^res://[a-zA-Z0-9_][a-zA-Z0-9_/-]*\\.tscn$") || value[6..].Contains("//"))
            Fail(path + "." + key, "expected safe res:// scene path");
        if (!File.Exists(Path.Combine(project, value[6..]))) Fail(path + "." + key, "scene does not exist");
    }
    static double[] Pair(JsonNode? node, string path, bool integer)
    {
        var a = Arr(node, path);
        if (a.Count != 2) Fail(path, "expected two coordinates");
        var result = new double[2];
        for (int i = 0; i < 2; i++)
        {
            if (a[i] is not JsonValue v || !v.TryGetValue<double>(out var n) || !double.IsFinite(n)
                || Math.Abs(n) > (integer ? 10000 : float.MaxValue) || (integer && n != Math.Truncate(n)))
                Fail($"{path}[{i}]", "invalid coordinate");
            result[i] = a[i]!.GetValue<double>();
        }
        return result;
    }
    static List<double[]> Points(JsonNode? node, string path)
    {
        var a = Arr(node, path);
        var points = new List<double[]>();
        for (int i = 0; i < a.Count; i++) points.Add(Pair(a[i], $"{path}[{i}]", true));
        return points;
    }
    static string[] Tags(JsonNode? node, string path)
    {
        var a = Arr(node, path);
        var result = new string[a.Count];
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < a.Count; i++)
        {
            string tag;
            if (a[i] is not JsonValue value || !value.TryGetValue<string>(out string? parsedTag))
            {
                Fail($"{path}[{i}]", "unknown tag");
                return result;
            }
            tag = parsedTag!;
            if (!SkillTags.Contains(tag))
                Fail($"{path}[{i}]", "unknown tag");
            if (!seen.Add(tag)) Fail($"{path}[{i}]", "duplicate tag");
            result[i] = tag;
        }
        return result;
    }
    static string[] Ids(JsonNode? node, string path)
    {
        var a = Arr(node, path);
        var result = new string[a.Count];
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < a.Count; i++)
        {
            if (a[i] is not JsonValue value || !value.TryGetValue<string>(out string? parsedId) || string.IsNullOrWhiteSpace(parsedId))
            {
                Fail($"{path}[{i}]", "expected identifier");
                return result;
            }
            result[i] = ValidateId(parsedId, $"{path}[{i}]");
            if (!seen.Add(result[i])) Fail($"{path}[{i}]", "duplicate ID");
        }
        return result;
    }
    static JsonArray Document(string file, string key)
    {
        JsonObject o;
        try { o = Obj(JsonNode.Parse(File.ReadAllText(file)), file); }
        catch (JsonException ex) { throw new InvalidDataException($"{file}{ex.Path}: {ex.Message}"); }
        Fields(o, file, "schemaVersion", key);
        if (Num(o, "schemaVersion", file, integer: true) != 1) Fail(file + ".schemaVersion", "only version 1 is supported");
        var a = Arr(o[key], file + "." + key);
        if (a.Count == 0) Fail(file + "." + key, "cannot be empty");
        return a;
    }
    static JsonObject ObjectDocument(string file, params string[] fields)
    {
        JsonObject o;
        try { o = Obj(JsonNode.Parse(File.ReadAllText(file)), file); }
        catch (JsonException ex) { throw new InvalidDataException($"{file}{ex.Path}: {ex.Message}"); }
        Fields(o, file, ["schemaVersion", .. fields]);
        if (Num(o, "schemaVersion", file, integer: true) != 1) Fail(file + ".schemaVersion", "only version 1 is supported");
        return o;
    }
    public SortedDictionary<string, string> Generate(string input)
    {
        var sourceUnits = Document(Path.Combine(input, "units.json"), "units");
        for (int i = 0; i < sourceUnits.Count; i++)
        {
            string p = $"units[{i}]";
            var o = Obj(sourceUnits[i], p);
            string id = Id(o, p), role = Str(o, "role", p);
            if (!units.TryAdd(id, o)) Fail(p + ".id", "duplicate ID");
            if (!Roles.Contains(role)) Fail(p + ".role", "unknown role");
            string[] common = ["id", "displayName", "role", "skillTags", "scenePath", "placement", "maxHealth", "actionPower", "actionInterval", "actionFrame", "targetLimit"];
            string[] extra = role switch
            {
                "ranged" => ["rangePixels", "projectileScenePath", "projectileSpeed", "projectileHitDistance", "projectileSpawnOffset"],
                "melee" => ["rangeCells", "attackCellOffsets", "blockCount"],
                "support" => ["rangePixels"],
                _ => ["moveSpeed"]
            };
            Fields(o, p, [.. common, .. extra]);
            Tags(o["skillTags"], p + ".skillTags");
            Str(o, "displayName", p); ScenePath(o, "scenePath", p);
            string expected = role == "enemy" ? "none" : role == "melee" ? "ground_or_path" : "ground";
            if (Str(o, "placement", p) != expected) Fail(p + ".placement", "invalid placement for role");
            Num(o, "maxHealth", p, positive: true); Num(o, "actionPower", p);
            Num(o, "actionInterval", p, positive: true);
            double frame = Num(o, "actionFrame", p, integer: true);
            if (role == "enemy" ? frame != 0 : frame > 7) Fail(p + ".actionFrame", "current scenes support frames 0..7; enemy uses 0");
            double limit = Num(o, "targetLimit", p, integer: true);
            if (role != "melee" && limit != 1) Fail(p + ".targetLimit", "current role supports exactly one target");
            if (role is "ranged" or "support") Num(o, "rangePixels", p, positive: true);
            if (role == "ranged")
            {
                ScenePath(o, "projectileScenePath", p);
                Num(o, "projectileSpeed", p, positive: true); Num(o, "projectileHitDistance", p, positive: true);
                Pair(o["projectileSpawnOffset"], p + ".projectileSpawnOffset", false);
            }
            if (role == "melee")
            {
                Num(o, "rangeCells", p, integer: true, positive: true);
                Num(o, "blockCount", p, integer: true);
                if (o.ContainsKey("attackCellOffsets")) Points(o["attackCellOffsets"], p + ".attackCellOffsets");
            }
            if (role == "enemy") Num(o, "moveSpeed", p, positive: true);
        }
        var stages = Document(Path.Combine(input, "stages.json"), "stages");
        var stageIds = new HashSet<string>();
        for (int i = 0; i < stages.Count; i++) ValidateStage(Obj(stages[i], $"stages[{i}]"), $"stages[{i}]", stageIds);
        var skills = Document(Path.Combine(input, "skills.json"), "skills");
        var skillIds = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < skills.Count; i++) ValidateSkill(Obj(skills[i], $"skills[{i}]"), $"skills[{i}]", skillIds);
        var playerDefaults = ObjectDocument(Path.Combine(input, "player_defaults.json"),
            "playerSkillProgress", "catProfiles", "catSkillPresets");
        ValidatePlayerDefaults(playerDefaults, skills);
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (id, unit) in units.OrderBy(p => p.Key, StringComparer.Ordinal)) result[$"units/{id}.tres"] = RenderUnit(unit);
        foreach (var stage in stages) { var s = stage!.AsObject(); result[$"stages/{S(s, "id")}.tres"] = RenderStage(s); }
        result["stages/catalog.tres"] = RenderStageCatalog(stages);
        foreach (var skill in skills) { var s = skill!.AsObject(); result[$"skills/{S(s, "id")}.tres"] = RenderSkill(s); }
        result["skills/catalog.tres"] = RenderSkillCatalog(skills);
        result["player/defaults.tres"] = RenderPlayerDefaults(playerDefaults);
        return result;
    }
    static void ValidateSkill(JsonObject skill, string path, HashSet<string> ids)
    {
        string role = Str(skill, "role", path);
        if (!SkillRoles.Contains(role)) Fail(path + ".role", "unknown skill role");
        string[] common = ["id", "displayName", "role", "tags", "requiredAnyTags", "requiredAllTags", "forbiddenTags", "linkCost", "effects"];
        string[] activeFields = ["baseProjectileCount", "basePierceCount", "baseDamageMultiplier"];
        Fields(skill, path, role == "active" ? [.. common, .. activeFields] : common);
        if (!ids.Add(Id(skill, path))) Fail(path + ".id", "duplicate ID");
        Str(skill, "displayName", path);
        string[] tags = Tags(skill["tags"], path + ".tags");
        string[] requiredAny = Tags(skill["requiredAnyTags"], path + ".requiredAnyTags");
        string[] requiredAll = Tags(skill["requiredAllTags"], path + ".requiredAllTags");
        string[] forbidden = Tags(skill["forbiddenTags"], path + ".forbiddenTags");
        foreach (string tag in requiredAny.Concat(requiredAll))
            if (forbidden.Contains(tag)) Fail(path + ".forbiddenTags", "required and forbidden tags overlap");
        int linkCost = (int)Num(skill, "linkCost", path, integer: true);
        var effects = Arr(skill["effects"], path + ".effects");
        if (role == "active")
        {
            if (tags.Length == 0) Fail(path + ".tags", "active skill requires at least one tag");
            if (requiredAny.Length != 0 || requiredAll.Length != 0 || forbidden.Length != 0)
                Fail(path, "active skill cannot declare compatibility requirements");
            if (linkCost != 0) Fail(path + ".linkCost", "active skill must cost zero link cores");
            Num(skill, "baseProjectileCount", path, integer: true, positive: true);
            Num(skill, "basePierceCount", path, integer: true);
            Num(skill, "baseDamageMultiplier", path, positive: true);
            if (effects.Count != 0) Fail(path + ".effects", "active skill uses base fields instead of support effects");
        }
        else
        {
            if (linkCost != 1) Fail(path + ".linkCost", "support skill must cost one link core");
            if (effects.Count == 0) Fail(path + ".effects", "support skill requires at least one effect");
        }
        for (int i = 0; i < effects.Count; i++) ValidateSkillEffect(Obj(effects[i], $"{path}.effects[{i}]"), $"{path}.effects[{i}]");
    }
    static void ValidateSkillEffect(JsonObject effect, string path)
    {
        string type = Str(effect, "type", path);
        if (!SkillEffectTypes.Contains(type)) Fail(path + ".type", "unknown skill effect");
        switch (type)
        {
            case "add_projectiles":
            case "add_pierce":
                Fields(effect, path, "type", "intValue");
                Num(effect, "intValue", path, integer: true, positive: true);
                break;
            case "add_fire_damage":
            case "multiply_damage":
            case "multiply_healing":
                Fields(effect, path, "type", "floatValue");
                Num(effect, "floatValue", path, positive: true);
                break;
            case "add_tag":
                Fields(effect, path, "type", "tagValue");
                string tag = Str(effect, "tagValue", path);
                if (!SkillTags.Contains(tag)) Fail(path + ".tagValue", "unknown tag");
                break;
        }
    }
    void ValidatePlayerDefaults(JsonObject root, JsonArray skillArray)
    {
        var skillMap = skillArray.Select(node => node!.AsObject())
            .ToDictionary(skill => S(skill, "id"), StringComparer.Ordinal);
        var progress = Obj(root["playerSkillProgress"], "playerSkillProgress");
        Fields(progress, "playerSkillProgress", "playerLevel", "unlockedPoints", "ownedSkillIds");
        Num(progress, "playerLevel", "playerSkillProgress", integer: true, positive: true);
        int unlockedPoints = (int)Num(progress, "unlockedPoints", "playerSkillProgress", integer: true);
        string[] ownedIds = Ids(progress["ownedSkillIds"], "playerSkillProgress.ownedSkillIds");
        if (ownedIds.Length == 0) Fail("playerSkillProgress.ownedSkillIds", "cannot be empty");
        foreach (string id in ownedIds)
            if (!skillMap.ContainsKey(id)) Fail("playerSkillProgress.ownedSkillIds", $"unknown skill reference '{id}'");
        var owned = new HashSet<string>(ownedIds, StringComparer.Ordinal);

        var profiles = Arr(root["catProfiles"], "catProfiles");
        if (profiles.Count == 0) Fail("catProfiles", "cannot be empty");
        var profileIds = new HashSet<string>(StringComparer.Ordinal);
        var profileUnits = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        for (int i = 0; i < profiles.Count; i++)
        {
            string path = $"catProfiles[{i}]";
            var profile = Obj(profiles[i], path);
            Fields(profile, path, "characterId", "displayName", "unitId");
            string characterId = ValidateId(Str(profile, "characterId", path), path + ".characterId");
            if (!profileIds.Add(characterId)) Fail(path + ".characterId", "duplicate character ID");
            Str(profile, "displayName", path);
            string unitId = ValidateId(Str(profile, "unitId", path), path + ".unitId");
            if (!units.TryGetValue(unitId, out JsonObject? unit) || S(unit, "role") == "enemy")
                Fail(path + ".unitId", "expected allied unit reference");
            profileUnits.Add(characterId, unit);
        }

        var presets = Arr(root["catSkillPresets"], "catSkillPresets");
        var presetIds = new HashSet<string>(StringComparer.Ordinal);
        int totalAllocated = 0;
        for (int i = 0; i < presets.Count; i++)
        {
            string path = $"catSkillPresets[{i}]";
            var preset = Obj(presets[i], path);
            Fields(preset, path, "characterId", "activeSkillId", "supportSkillIds", "allocatedPoints", "updatedAtUtc");
            string characterId = ValidateId(Str(preset, "characterId", path), path + ".characterId");
            if (!presetIds.Add(characterId)) Fail(path + ".characterId", "duplicate preset character ID");
            if (!profileUnits.TryGetValue(characterId, out JsonObject? unit)) Fail(path + ".characterId", "unknown character reference");
            string activeId = ValidateId(Str(preset, "activeSkillId", path), path + ".activeSkillId");
            string[] supportIds = Ids(preset["supportSkillIds"], path + ".supportSkillIds");
            if (supportIds.Length > 5) Fail(path + ".supportSkillIds", "maximum five support skills");
            int allocated = (int)Num(preset, "allocatedPoints", path, integer: true);
            if (allocated != supportIds.Length) Fail(path + ".allocatedPoints", "must equal support skill count");
            totalAllocated += allocated;
            string updatedAt = Str(preset, "updatedAtUtc", path);
            if (!DateTimeOffset.TryParse(updatedAt, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset parsed)
                || parsed.Offset != TimeSpan.Zero)
                Fail(path + ".updatedAtUtc", "expected UTC timestamp");

            if (!skillMap.TryGetValue(activeId, out JsonObject? active)) Fail(path + ".activeSkillId", "unknown skill reference");
            if (!owned.Contains(activeId)) Fail(path + ".activeSkillId", "skill is not owned");
            if (S(active!, "role") != "active" || Num(active!, "linkCost", path + ".activeSkillId", integer: true) != 0)
                Fail(path + ".activeSkillId", "expected free active skill");
            var unitTags = new HashSet<string>(Tags(unit!["skillTags"], path + ".unitId.skillTags"), StringComparer.Ordinal);
            foreach (string tag in Tags(active!["tags"], path + ".activeSkillId.tags"))
                if (!unitTags.Contains(tag)) Fail(path + ".activeSkillId", $"unit does not support tag '{tag}'");

            var activeTags = new HashSet<string>(Tags(active!["tags"], path + ".activeSkillId.tags"), StringComparer.Ordinal);
            foreach (string supportId in supportIds)
            {
                if (!skillMap.TryGetValue(supportId, out JsonObject? support)) Fail(path + ".supportSkillIds", $"unknown skill reference '{supportId}'");
                if (!owned.Contains(supportId)) Fail(path + ".supportSkillIds", $"skill '{supportId}' is not owned");
                if (S(support!, "role") != "support" || Num(support!, "linkCost", path + ".supportSkillIds", integer: true) != 1)
                    Fail(path + ".supportSkillIds", $"skill '{supportId}' is not a one-point support");
                string[] requiredAny = Tags(support!["requiredAnyTags"], path + ".supportSkillIds.requiredAnyTags");
                string[] requiredAll = Tags(support!["requiredAllTags"], path + ".supportSkillIds.requiredAllTags");
                string[] forbidden = Tags(support!["forbiddenTags"], path + ".supportSkillIds.forbiddenTags");
                if (requiredAny.Length > 0 && !requiredAny.Any(activeTags.Contains))
                    Fail(path + ".supportSkillIds", $"skill '{supportId}' lacks required-any tag");
                if (requiredAll.Any(tag => !activeTags.Contains(tag)))
                    Fail(path + ".supportSkillIds", $"skill '{supportId}' lacks required-all tag");
                if (forbidden.Any(activeTags.Contains))
                    Fail(path + ".supportSkillIds", $"skill '{supportId}' conflicts with forbidden tag");
            }
        }
        if (!presetIds.SetEquals(profileIds)) Fail("catSkillPresets", "must contain exactly one preset per cat profile");
        if (totalAllocated > unlockedPoints) Fail("catSkillPresets", "default presets exceed unlocked points");
    }
    void ValidateStage(JsonObject s, string p, HashSet<string> ids)
    {
        Fields(s, p, "id", "displayName", "maxSquadUnits", "skillPointCap", "baseHealth", "firstWaveDelay", "waveGap", "grid", "pathCorners", "blockedCells", "roster", "waves");
        if (!ids.Add(Id(s, p))) Fail(p + ".id", "duplicate ID");
        Str(s, "displayName", p); Num(s, "baseHealth", p, integer: true, positive: true);
        int maxSquadUnits = s.ContainsKey("maxSquadUnits")
            ? (int)Num(s, "maxSquadUnits", p, integer: true, positive: true) : 10;
        if (maxSquadUnits > 10) Fail(p + ".maxSquadUnits", "maximum squad size is 10");
        if (s.ContainsKey("skillPointCap")) Num(s, "skillPointCap", p, integer: true);
        Num(s, "firstWaveDelay", p, positive: true); Num(s, "waveGap", p, positive: true);
        var grid = Obj(s["grid"], p + ".grid");
        Fields(grid, p + ".grid", "columns", "rows", "cellSize", "origin");
        int cols = (int)Num(grid, "columns", p + ".grid", integer: true, positive: true);
        int rows = (int)Num(grid, "rows", p + ".grid", integer: true, positive: true);
        if ((long)cols * rows > 1000000) Fail(p + ".grid", "maximum one million cells");
        Num(grid, "cellSize", p + ".grid", positive: true); Pair(grid["origin"], p + ".grid.origin", false);
        var corners = Points(s["pathCorners"], p + ".pathCorners");
        if (corners.Count < 2) Fail(p + ".pathCorners", "need at least two points");
        var pathCells = new HashSet<(double, double)>();
        for (int i = 1; i < corners.Count; i++)
        {
            var a = corners[i - 1]; var b = corners[i];
            if ((a[0] != b[0] && a[1] != b[1]) || (a[0] == b[0] && a[1] == b[1]))
                Fail($"{p}.pathCorners[{i}]", "expected nonzero orthogonal segment");
            double x = a[0], y = a[1];
            while (true)
            {
                if (x >= 0 && x < cols && y >= 0 && y < rows) pathCells.Add((x, y));
                if (x == b[0] && y == b[1]) break;
                x += Math.Sign(b[0] - a[0]); y += Math.Sign(b[1] - a[1]);
            }
        }
        if (pathCells.Count == 0) Fail(p + ".pathCorners", "path must cross the grid");
        var blocked = Points(s["blockedCells"], p + ".blockedCells");
        var seen = new HashSet<(double, double)>();
        for (int i = 0; i < blocked.Count; i++)
        {
            var b = blocked[i];
            if (b[0] < 0 || b[0] >= cols || b[1] < 0 || b[1] >= rows || !seen.Add((b[0], b[1])) || pathCells.Contains((b[0], b[1])))
                Fail($"{p}.blockedCells[{i}]", "outside grid, duplicate, or overlaps enemy path");
        }
        var roster = Arr(s["roster"], p + ".roster");
        var roles = new HashSet<string>();
        long total = 0, ground = 0, melee = 0;
        for (int i = 0; i < roster.Count; i++)
        {
            string rp = $"{p}.roster[{i}]"; var r = Obj(roster[i], rp);
            Fields(r, rp, "unitId", "count");
            string id = Str(r, "unitId", rp);
            if (!units.TryGetValue(id, out var unit)) { Fail(rp + ".unitId", "unknown unit reference"); return; }
            string role = S(unit, "role");
            if (role == "enemy" || !roles.Add(role)) Fail(rp + ".unitId", "HUD requires one definition per allied role");
            int count = (int)Num(r, "count", rp, integer: true, positive: true);
            total += count; if (role != "melee") ground += count;
            if (role == "melee") melee += count;
        }
        if (!roles.SetEquals(["ranged", "melee", "support"])) Fail(p + ".roster", "HUD requires ranged, melee and support");
        if (total > (long)cols * rows - blocked.Count || ground > (long)cols * rows - blocked.Count - pathCells.Count)
            Fail(p + ".roster", "not enough legal deployment cells");
        if (maxSquadUnits > (long)cols * rows - blocked.Count
            || maxSquadUnits - melee > (long)cols * rows - blocked.Count - pathCells.Count)
            Fail(p + ".maxSquadUnits", "not enough legal deployment cells for maximum squad size");
        var waves = Arr(s["waves"], p + ".waves");
        if (waves.Count == 0) Fail(p + ".waves", "cannot be empty");
        for (int i = 0; i < waves.Count; i++)
        {
            string wp = $"{p}.waves[{i}]"; var w = Obj(waves[i], wp);
            Fields(w, wp, "enemyId", "count", "interval", "healthOverride", "speedOverride");
            string id = Str(w, "enemyId", wp);
            if (!units.TryGetValue(id, out var u) || S(u, "role") != "enemy") Fail(wp + ".enemyId", "expected enemy reference");
            Num(w, "count", wp, integer: true, positive: true); Num(w, "interval", wp, positive: true);
            foreach (string key in new[] { "healthOverride", "speedOverride" }) if (w.ContainsKey(key)) Num(w, key, wp, positive: true);
        }
    }
    static string S(JsonObject o, string k) => o[k]!.GetValue<string>();
    // Godot's text resource parser does not decode JSON-style \u escapes as Unicode.
    static string Q(string s) => JsonSerializer.Serialize(s, GodotStringOptions);
    static string N(JsonNode? n) => n!.GetValue<double>().ToString("R", CultureInfo.InvariantCulture);
    static string Vec(JsonNode? n, bool integer = false) => $"Vector2{(integer ? "i" : "")}({N(n![0])}, {N(n[1])})";
    static string PointsText(JsonNode? n) => "Array[Vector2i]([" + string.Join(", ", n!.AsArray().Select(v => Vec(v, true))) + "])";
    static string StringsText(JsonNode? n) => "Array[String]([" + string.Join(", ", n!.AsArray().Select(v => Q(v!.GetValue<string>()))) + "])";
    static string Script(string name, string id) => $"[ext_resource type=\"Script\" path=\"res://scripts/data/{name}.cs\" id=\"{id}\"]\n";
    static string Ext(string id) => $"ExtResource(\"{id}\")";
    static string Sub(string id) => $"SubResource(\"{id}\")";
    static string RenderUnit(JsonObject u)
    {
        bool ranged = S(u, "role") == "ranged";
        var b = new StringBuilder($"[gd_resource type=\"Resource\" load_steps={(ranged ? 4 : 3)} format=3]\n\n");
        b.Append(Script("UnitDefinition", "script"));
        b.AppendLine($"[ext_resource type=\"PackedScene\" path={Q(S(u, "scenePath"))} id=\"scene\"]");
        if (ranged) b.AppendLine($"[ext_resource type=\"PackedScene\" path={Q(S(u, "projectileScenePath"))} id=\"projectile\"]");
        b.AppendLine("\n[resource]\nscript = " + Ext("script"));
        b.AppendLine("Id = " + Q(S(u, "id"))); b.AppendLine("DisplayName = " + Q(S(u, "displayName")));
        b.AppendLine("Role = " + Array.IndexOf(Roles, S(u, "role"))); b.AppendLine("Placement = " + Array.IndexOf(Placements, S(u, "placement")));
        b.AppendLine("SkillTags = " + StringsText(u["skillTags"]));
        b.AppendLine("Scene = " + Ext("scene"));
        foreach (string key in new[] { "maxHealth", "actionPower", "actionInterval", "actionFrame", "targetLimit", "rangePixels", "rangeCells", "blockCount", "projectileSpeed", "projectileHitDistance", "moveSpeed" })
            if (u.ContainsKey(key)) b.AppendLine(char.ToUpperInvariant(key[0]) + key[1..] + " = " + N(u[key]));
        if (u.ContainsKey("attackCellOffsets")) b.AppendLine("AttackCellOffsets = " + PointsText(u["attackCellOffsets"]));
        if (ranged) { b.AppendLine("ProjectileScene = " + Ext("projectile")); b.AppendLine("ProjectileSpawnOffset = " + Vec(u["projectileSpawnOffset"])); }
        return b.ToString().Replace("\r\n", "\n");
    }
    static string RenderStage(JsonObject s)
    {
        var roster = s["roster"]!.AsArray(); var waves = s["waves"]!.AsArray();
        var refs = roster.Select(r => S(r!.AsObject(), "unitId")).Concat(waves.Select(w => S(w!.AsObject(), "enemyId"))).Distinct().Order(StringComparer.Ordinal).ToArray();
        var b = new StringBuilder($"[gd_resource type=\"Resource\" load_steps={8 + refs.Length + roster.Count + waves.Count} format=3]\n\n");
        foreach (string name in new[] { "StageDefinition", "StageSkillBudget", "GridDefinition", "RosterEntry", "WaveDefinition" }) b.Append(Script(name, name));
        foreach (string id in refs) b.AppendLine($"[ext_resource type=\"Resource\" path=\"res://data/units/{id}.tres\" id=\"unit_{id}\"]");
        b.AppendLine("\n[sub_resource type=\"Resource\" id=\"skill_budget\"]\nscript = " + Ext("StageSkillBudget"));
        b.AppendLine("StageId = " + Q(S(s, "id")));
        b.AppendLine("HasStageCap = " + (s.ContainsKey("skillPointCap") ? "true" : "false"));
        if (s.ContainsKey("skillPointCap")) b.AppendLine("StageCap = " + N(s["skillPointCap"]));
        b.AppendLine("\n[sub_resource type=\"Resource\" id=\"grid\"]\nscript = " + Ext("GridDefinition"));
        var grid = s["grid"]!.AsObject();
        foreach (string key in new[] { "columns", "rows", "cellSize" }) b.AppendLine(char.ToUpperInvariant(key[0]) + key[1..] + " = " + N(grid[key]));
        b.AppendLine("Origin = " + Vec(grid["origin"]));
        for (int i = 0; i < roster.Count; i++)
        {
            var r = roster[i]!.AsObject();
            b.AppendLine($"\n[sub_resource type=\"Resource\" id=\"roster_{i}\"]\nscript = {Ext("RosterEntry")}\nUnit = {Ext("unit_" + S(r, "unitId"))}\nCount = {N(r["count"])}");
        }
        for (int i = 0; i < waves.Count; i++)
        {
            var w = waves[i]!.AsObject();
            b.AppendLine($"\n[sub_resource type=\"Resource\" id=\"wave_{i}\"]\nscript = {Ext("WaveDefinition")}\nEnemy = {Ext("unit_" + S(w, "enemyId"))}\nCount = {N(w["count"])}\nInterval = {N(w["interval"])}");
            foreach (string key in new[] { "healthOverride", "speedOverride" }) if (w.ContainsKey(key)) b.AppendLine(char.ToUpperInvariant(key[0]) + key[1..] + " = " + N(w[key]));
        }
        b.AppendLine("\n[resource]\nscript = " + Ext("StageDefinition"));
        b.AppendLine("Id = " + Q(S(s, "id")) + "\nDisplayName = " + Q(S(s, "displayName")));
        b.AppendLine("MaxSquadUnits = " + (s.ContainsKey("maxSquadUnits") ? N(s["maxSquadUnits"]) : "10"));
        foreach (string key in new[] { "baseHealth", "firstWaveDelay", "waveGap" }) b.AppendLine(char.ToUpperInvariant(key[0]) + key[1..] + " = " + N(s[key]));
        b.AppendLine("SkillBudget = " + Sub("skill_budget"));
        b.AppendLine("Grid = " + Sub("grid") + "\nPathCorners = " + PointsText(s["pathCorners"]) + "\nBlockedCells = " + PointsText(s["blockedCells"]));
        b.AppendLine("Roster = Array[" + Ext("RosterEntry") + "]([" + string.Join(", ", Enumerable.Range(0, roster.Count).Select(i => Sub("roster_" + i))) + "])");
        b.AppendLine("Waves = Array[" + Ext("WaveDefinition") + "]([" + string.Join(", ", Enumerable.Range(0, waves.Count).Select(i => Sub("wave_" + i))) + "])");
        return b.ToString().Replace("\r\n", "\n");
    }

    static string RenderStageCatalog(JsonArray stages)
    {
        var b = new StringBuilder($"[gd_resource type=\"Resource\" load_steps={3 + stages.Count} format=3]\n\n");
        b.Append(Script("StageCatalog", "StageCatalog"));
        b.Append(Script("StageDefinition", "StageDefinition"));
        for (int i = 0; i < stages.Count; i++)
        {
            string id = S(stages[i]!.AsObject(), "id");
            b.AppendLine($"[ext_resource type=\"Resource\" path=\"res://data/stages/{id}.tres\" id=\"stage_{i}\"]");
        }
        b.AppendLine("\n[resource]\nscript = " + Ext("StageCatalog"));
        b.AppendLine("Stages = Array[" + Ext("StageDefinition") + "]([" + string.Join(", ", Enumerable.Range(0, stages.Count).Select(i => Ext("stage_" + i))) + "])");
        return b.ToString().Replace("\r\n", "\n");
    }
    static string RenderSkill(JsonObject skill)
    {
        var effects = skill["effects"]!.AsArray();
        var b = new StringBuilder($"[gd_resource type=\"Resource\" load_steps={3 + effects.Count} format=3]\n\n");
        b.Append(Script("SkillDefinition", "SkillDefinition"));
        b.Append(Script("SkillEffectDefinition", "SkillEffectDefinition"));
        for (int i = 0; i < effects.Count; i++)
        {
            var effect = effects[i]!.AsObject();
            b.AppendLine($"\n[sub_resource type=\"Resource\" id=\"effect_{i}\"]\nscript = {Ext("SkillEffectDefinition")}");
            b.AppendLine("Type = " + Array.IndexOf(SkillEffectTypes, S(effect, "type")));
            if (effect.ContainsKey("intValue")) b.AppendLine("IntValue = " + N(effect["intValue"]));
            if (effect.ContainsKey("floatValue")) b.AppendLine("FloatValue = " + N(effect["floatValue"]));
            if (effect.ContainsKey("tagValue")) b.AppendLine("TagValue = " + Q(S(effect, "tagValue")));
        }
        b.AppendLine("\n[resource]\nscript = " + Ext("SkillDefinition"));
        b.AppendLine("Id = " + Q(S(skill, "id")) + "\nDisplayName = " + Q(S(skill, "displayName")));
        b.AppendLine("Role = " + Array.IndexOf(SkillRoles, S(skill, "role")));
        foreach (string key in new[] { "tags", "requiredAnyTags", "requiredAllTags", "forbiddenTags" })
            b.AppendLine(char.ToUpperInvariant(key[0]) + key[1..] + " = " + StringsText(skill[key]));
        b.AppendLine("LinkCost = " + N(skill["linkCost"]));
        foreach (string key in new[] { "baseProjectileCount", "basePierceCount", "baseDamageMultiplier" })
            if (skill.ContainsKey(key)) b.AppendLine(char.ToUpperInvariant(key[0]) + key[1..] + " = " + N(skill[key]));
        b.AppendLine("Effects = Array[" + Ext("SkillEffectDefinition") + "]([" + string.Join(", ", Enumerable.Range(0, effects.Count).Select(i => Sub("effect_" + i))) + "])");
        return b.ToString().Replace("\r\n", "\n");
    }
    static string RenderSkillCatalog(JsonArray skills)
    {
        var b = new StringBuilder($"[gd_resource type=\"Resource\" load_steps={3 + skills.Count} format=3]\n\n");
        b.Append(Script("SkillCatalog", "SkillCatalog"));
        b.Append(Script("SkillDefinition", "SkillDefinition"));
        for (int i = 0; i < skills.Count; i++)
        {
            string id = S(skills[i]!.AsObject(), "id");
            b.AppendLine($"[ext_resource type=\"Resource\" path=\"res://data/skills/{id}.tres\" id=\"skill_{i}\"]");
        }
        b.AppendLine("\n[resource]\nscript = " + Ext("SkillCatalog"));
        b.AppendLine("Skills = Array[" + Ext("SkillDefinition") + "]([" + string.Join(", ", Enumerable.Range(0, skills.Count).Select(i => Ext("skill_" + i))) + "])");
        return b.ToString().Replace("\r\n", "\n");
    }

    static string RenderPlayerDefaults(JsonObject root)
    {
        var progress = root["playerSkillProgress"]!.AsObject();
        var profiles = root["catProfiles"]!.AsArray();
        var presets = root["catSkillPresets"]!.AsArray();
        var unitIds = profiles.Select(profile => S(profile!.AsObject(), "unitId"))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var b = new StringBuilder($"[gd_resource type=\"Resource\" load_steps={6 + unitIds.Length + profiles.Count + presets.Count} format=3]\n\n");
        foreach (string name in new[] { "PlayerSkillDefaults", "PlayerSkillProgress", "CatProfile", "CatSkillPreset" })
            b.Append(Script(name, name));
        foreach (string unitId in unitIds)
            b.AppendLine($"[ext_resource type=\"Resource\" path=\"res://data/units/{unitId}.tres\" id=\"unit_{unitId}\"]");

        b.AppendLine("\n[sub_resource type=\"Resource\" id=\"progress\"]\nscript = " + Ext("PlayerSkillProgress"));
        b.AppendLine("PlayerLevel = " + N(progress["playerLevel"]));
        b.AppendLine("UnlockedPoints = " + N(progress["unlockedPoints"]));
        b.AppendLine("OwnedSkillIds = " + StringsText(progress["ownedSkillIds"]));

        for (int i = 0; i < profiles.Count; i++)
        {
            var profile = profiles[i]!.AsObject();
            b.AppendLine($"\n[sub_resource type=\"Resource\" id=\"profile_{i}\"]\nscript = {Ext("CatProfile")}");
            b.AppendLine("CharacterId = " + Q(S(profile, "characterId")));
            b.AppendLine("DisplayName = " + Q(S(profile, "displayName")));
            b.AppendLine("UnitId = " + Q(S(profile, "unitId")));
            b.AppendLine("Unit = " + Ext("unit_" + S(profile, "unitId")));
        }
        for (int i = 0; i < presets.Count; i++)
        {
            var preset = presets[i]!.AsObject();
            b.AppendLine($"\n[sub_resource type=\"Resource\" id=\"preset_{i}\"]\nscript = {Ext("CatSkillPreset")}");
            b.AppendLine("CharacterId = " + Q(S(preset, "characterId")));
            b.AppendLine("ActiveSkillId = " + Q(S(preset, "activeSkillId")));
            b.AppendLine("SupportSkillIds = " + StringsText(preset["supportSkillIds"]));
            b.AppendLine("AllocatedPoints = " + N(preset["allocatedPoints"]));
            b.AppendLine("UpdatedAtUtc = " + Q(S(preset, "updatedAtUtc")));
        }

        b.AppendLine("\n[resource]\nscript = " + Ext("PlayerSkillDefaults"));
        b.AppendLine("Progress = " + Sub("progress"));
        b.AppendLine("CatProfiles = Array[" + Ext("CatProfile") + "]([" + string.Join(", ", Enumerable.Range(0, profiles.Count).Select(i => Sub("profile_" + i))) + "])");
        b.AppendLine("InitialPresets = Array[" + Ext("CatSkillPreset") + "]([" + string.Join(", ", Enumerable.Range(0, presets.Count).Select(i => Sub("preset_" + i))) + "])");
        return b.ToString().Replace("\r\n", "\n");
    }
}
