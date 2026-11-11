using System.Text.Json;

namespace Exile.Runtime.Visuals.Mapping;

/// <summary>技能皮肤（WoWCharConfig 的 skills.*）：只写要换的键，其余走 skills.json。</summary>
public sealed record SkillSkin(string? Action, Dictionary<string, int>? Spell, Dictionary<string, int>? Area,
    int Ray, double[]? RayColor, GroundRing? Ring);

/// <summary>持续效果皮肤（WoWCharConfig 的 durations.*）。</summary>
public sealed record DurationSkin(int? Hero, int? Start, GroundRing? Ring);

/// <summary>换了的范围圈：地面模型（法术 id + 原本半径），DQQ 距离档没写就用全局 auras 里的。</summary>
public sealed record GroundRing(int Area, double Native, string? Distance, double Radius);

/// <summary>
/// 一个角色自己的 WoW 外观配置（PLAN-0011，格式见 docs/char-config.md）：长相、幻化、技能皮肤。
/// 来源只有 DQQ 角色 DTO 的字符串字段 <c>WoWCharConfig</c>（梳妆台、幻化窗口保存进去）；空的 = 人类女性、只穿 DQQ 装备。
/// 写错的项跳过并记日志，其余照用。
/// </summary>
public sealed class CharConfig
{
    public int? Race, Gender, Skin, Face, Hair, HairColor, FacialHair;
    /// <summary>WoW 槽名（equipment 的槽名，规范写法）→ 外观（写法同 equipment.json 的物品）；null = 不显示。</summary>
    public Dictionary<string, JsonElement?> Transmog { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, SkillSkin> Skills { get; } = new();
    public Dictionary<string, DurationSkin> Durations { get; } = new();
    /// <summary>从哪来的（日志用）。</summary>
    public string Source { get; private set; } = "";

    private static readonly JsonDocumentOptions Options = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    /// <summary>
    /// 是否套用皮肤（插件的 Skins 按钮，用户 2026-11-11）：只管装备幻化和技能皮肤。关了以后自由模式和战斗都只显示
    /// DQQ 装备和默认的技能表现；种族、性别、长相（look）不受影响，照配置。只在这次运行里有效，默认打开。
    /// </summary>
    public static volatile bool Enabled = true;

    /// <summary>一个 DQQ 角色的配置（皮肤关了时只剩 look）；没有为 null。</summary>
    public static CharConfig? For(DQQ.Components.Stages.Actors.Characters.CharacterDTO? c)
    {
        if (c == null || RawFor(c) is not { } raw) return null;
        var cfg = Parse(raw.Text, raw.Source);
        if (cfg != null && !Enabled)
        {
            cfg.Transmog.Clear();
            cfg.Skills.Clear();
            cfg.Durations.Clear();
        }
        return cfg;
    }

    /// <summary>
    /// 一个 DQQ 角色的配置原文与来源（不管 <see cref="Enabled"/>）：只有 DTO 的 WoWCharConfig（用户 2026-11-11：
    /// 不再用本地测试文件）；空的为 null（人类女性、只穿 DQQ 装备）。
    /// </summary>
    public static (string Text, string Source)? RawFor(DQQ.Components.Stages.Actors.Characters.CharacterDTO c) =>
        string.IsNullOrWhiteSpace(c.WoWCharConfig) ? null : (c.WoWCharConfig, $"{c.DisplayName}（{c.Id}）的 WoWCharConfig");

    /// <summary>解析；整段不是 JSON 时为 null。</summary>
    public static CharConfig? Parse(string json, string where)
    {
        var errors = new List<string>();
        CharConfig? cfg = null;
        try
        {
            using var doc = JsonDocument.Parse(json, Options);
            cfg = Read(doc.RootElement, errors);
        }
        catch (JsonException e) { errors.Add($"JSON 格式错误（第 {e.LineNumber + 1} 行）：{e.Message}"); }
        if (cfg != null) cfg.Source = where;
        Console.WriteLine($"[char-config] {where}：" + (cfg == null ? "不可用" :
            $"外观 {(cfg.Race != null || cfg.Gender != null ? "有" : "无")}、幻化 {cfg.Transmog.Count}、技能皮肤 {cfg.Skills.Count}、持续效果皮肤 {cfg.Durations.Count}") +
            (errors.Count == 0 ? "" : $"，{errors.Count} 个问题"));
        foreach (var e in errors) Console.WriteLine($"[char-config]   问题：{e}");
        return cfg;
    }

    private static CharConfig? Read(JsonElement root, List<string> errors)
    {
        if (root.ValueKind != JsonValueKind.Object) { errors.Add("应为对象"); return null; }
        var cfg = new CharConfig();
        if (MappingFiles.Int(root, "v") is { } v && v != 1) errors.Add($"v = {v}：只认识版本 1，按 1 读");

        if (root.TryGetProperty("look", out var look) && look.ValueKind == JsonValueKind.Object)
        {
            if (look.TryGetProperty("race", out var r))
                cfg.Race = r.ValueKind == JsonValueKind.Number ? r.GetInt32() : MappingFiles.RaceId(r.GetString()) is { } rid ? rid : Bad($"look.race：不认识 \"{r.GetString()}\"");
            if (MappingFiles.Str(look, "gender") is { } g)
                cfg.Gender = g.ToLowerInvariant() switch { "male" => 0, "female" => 1, _ => Bad($"look.gender：应为 male 或 female，而不是 \"{g}\"") };
            cfg.Skin = MappingFiles.Int(look, "skin");
            cfg.Face = MappingFiles.Int(look, "face");
            cfg.Hair = MappingFiles.Int(look, "hair");
            cfg.HairColor = MappingFiles.Int(look, "hairColor");
            cfg.FacialHair = MappingFiles.Int(look, "facialHair");
        }
        int? Bad(string e) { errors.Add(e); return null; }

        if (root.TryGetProperty("transmog", out var tm) && tm.ValueKind == JsonValueKind.Object)
            foreach (var p in tm.EnumerateObject())
            {
                var at = $"transmog.{p.Name}";
                if (MappingFiles.WowSlotOf(p.Name) is not { } slot) { errors.Add($"{at}：不认识的槽位（DQQ 装备槽或 WoW 槽名）"); continue; }
                if (p.Value.ValueKind == JsonValueKind.Object && p.Value.TryGetProperty("hide", out var h) && h.ValueKind == JsonValueKind.True)
                { cfg.Transmog[slot] = null; continue; }
                if (MappingFiles.TransmogLook(p.Value, slot, at, errors) is { } item) cfg.Transmog[slot] = item;
            }

        if (root.TryGetProperty("skills", out var sk) && sk.ValueKind == JsonValueKind.Object)
            foreach (var p in sk.EnumerateObject())
            {
                var at = $"skills.{p.Name}";
                if (!Enum.TryParse<DQQ.Enums.EnumSkillNumber>(p.Name, out _)) errors.Add($"{at}：DQQ 没有这个技能（EnumSkillNumber），拼写？");
                if (p.Value.ValueKind != JsonValueKind.Object) { errors.Add($"{at}：应为对象"); continue; }
                var action = MappingFiles.Str(p.Value, "action");
                if (action != null && !SkillLooks.KnownActions.Contains(action)) { errors.Add($"{at}.action：不认识的动作 \"{action}\""); action = null; }
                var spell = p.Value.TryGetProperty("spell", out var sp) ? SpellMap(sp, $"{at}.spell", errors) : null;
                var area = p.Value.TryGetProperty("area", out var ar) ? SpellMap(ar, $"{at}.area", errors) : null;
                int ray = 0; double[]? color = null;
                if (p.Value.TryGetProperty("ray", out var ry))
                {
                    if (ry.ValueKind == JsonValueKind.Number) ray = ry.GetInt32();
                    else if (MappingFiles.Int(ry, "spell") is { } rs)
                    {
                        ray = rs;
                        if (ry.TryGetProperty("color", out var c) && c.ValueKind == JsonValueKind.Array && c.GetArrayLength() == 3)
                            color = c.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.Number ? x.GetDouble() : 1).ToArray();
                    }
                    else errors.Add($"{at}.ray：要写 WoW 法术 id，或 {{ \"spell\": id, \"color\": [r, g, b] }}");
                }
                var ring = p.Value.TryGetProperty("ring", out var rg) ? SkillLooks.ParseRingSkin(rg, $"{at}.ring", errors) : null;
                cfg.Skills[p.Name] = new SkillSkin(action, spell, area, ray, color, ring);
            }

        if (root.TryGetProperty("durations", out var ds) && ds.ValueKind == JsonValueKind.Object)
            foreach (var p in ds.EnumerateObject())
            {
                var at = $"durations.{p.Name}";
                if (!Enum.TryParse<DQQ.Enums.EnumDurationNumber>(p.Name, out _)) errors.Add($"{at}：DQQ 没有这个持续效果（EnumDurationNumber），拼写？");
                if (p.Value.ValueKind != JsonValueKind.Object) { errors.Add($"{at}：应为对象"); continue; }
                var ring = p.Value.TryGetProperty("ring", out var rg) ? SkillLooks.ParseRingSkin(rg, $"{at}.ring", errors) : null;
                cfg.Durations[p.Name] = new DurationSkin(MappingFiles.Int(p.Value, "hero"), MappingFiles.Int(p.Value, "start"), ring);
            }
        return cfg;
    }

    private static Dictionary<string, int>? SpellMap(JsonElement v, string at, List<string> errors)
    {
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var all)) return new() { ["*"] = all };
        if (v.ValueKind != JsonValueKind.Object) { errors.Add($"{at}：要写 WoW 法术 id，或按元素的 {{ \"Fire\": id, \"*\": id }}"); return null; }
        var map = new Dictionary<string, int>();
        foreach (var p in v.EnumerateObject())
            if (p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetInt32(out var id)) map[p.Name] = id;
            else errors.Add($"{at}.{p.Name}：要写 WoW 法术 id（数字）");
        return map;
    }

    /// <summary>长相换上配置里写了的项。</summary>
    public Protocol.Dress ApplyLook(Protocol.Dress d) => d with
    {
        Race = Race ?? d.Race, Gender = Gender ?? d.Gender, Skin = Skin ?? d.Skin, Face = Face ?? d.Face,
        Hair = Hair ?? d.Hair, HairColor = HairColor ?? d.HairColor, FacialHair = FacialHair ?? d.FacialHair,
    };

    /// <summary>
    /// 一个持续效果的皮肤：durations 里直接写的，加上产生它的技能（skills.json 的 skillDurations）在 skills 里写的范围圈。
    /// </summary>
    public DurationSkin? DurationFor(string duration)
    {
        Durations.TryGetValue(duration, out var own);
        var ring = own?.Ring;
        if (ring == null)
            foreach (var skill in SkillLooks.SkillsOf(duration))
                if (Skills.TryGetValue(skill, out var s) && s.Ring != null) { ring = s.Ring; break; }
        return own == null && ring == null ? null : new DurationSkin(own?.Hero, own?.Start, ring);
    }
}
