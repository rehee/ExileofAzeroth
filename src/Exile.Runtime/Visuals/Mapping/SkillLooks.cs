using System.Text.Json;

namespace Exile.Runtime.Visuals.Mapping;

/// <summary>
/// 一个技能事件在 WoW 里怎么演（PLAN-0006 D，<c>config/mapping/skills.json</c>，规则见 docs/skill-presentation.md）：
/// 角色动作（<see cref="Action"/>）、借用的 WoW 法术（<see cref="Spell"/>，客户端播它的施法、飞行、命中视觉；0 = 不播法术，
/// 近战挥砍类由伤害事件带出挥砍）与伤害学派（<see cref="School"/>，伤害数字的颜色）。
/// </summary>
public sealed record SkillPlay(string Action, int Spell, int School);

/// <summary><c>skills.json</c>：元素 → 学派、动作 × 元素 → WoW 法术、个别技能覆盖、持续效果开始时的表现。</summary>
public static class SkillLooks
{
    /// <summary>远程武器（<c>equipment.json</c> 的 weapon）：攻击、无肉搏时射击（投掷武器为投掷）。</summary>
    private static readonly HashSet<string> Ranged = ["bow", "crossbow", "gun", "thrown"];  // 魔杖在 DQQ 是法系主手武器：按法术演（不射击）

    private static Dictionary<string, (int School, string Note)> elements = new();
    private static Dictionary<string, Dictionary<string, int>> actions = new();
    private static Dictionary<string, (string? Action, Dictionary<string, int>? Spell)> skills = new();
    private static Dictionary<string, int> durations = new();
    private static Dictionary<string, (Dictionary<string, int> Spell, double Radius)> areas = new();

    /// <summary>读取结果的一句话（runtime 日志）。</summary>
    public static string Summary => $"元素 {elements.Count}、动作 {actions.Count}、区域 {areas.Count}、技能覆盖 {skills.Count}、持续效果 {durations.Count}";

    public static IReadOnlyDictionary<string, (int School, string Note)> Elements => elements;
    public static IReadOnlyDictionary<string, (string? Action, Dictionary<string, int>? Spell)> Skills => skills;
    public static IReadOnlyDictionary<string, int> Durations => durations;
    public static IReadOnlyCollection<string> ActionNames => actions.Keys;

    /// <summary>所有动作名（规则可能给出的）；<c>skills.json</c> 的 actions 与 skills.*.action 只能用这些。</summary>
    public static readonly string[] KnownActions =
        ["melee", "mobAttack", "shoot", "throw", "projectile", "cast", "channel", "spin", "nova", "shout",
         "aura", "kick", "charge", "judgement", "none"];

    public static void Load(string path, List<string> errors)
    {
        using var doc = MappingFiles.Read(path, errors);
        if (doc == null) return;
        var root = doc.RootElement;
        var file = Path.GetFileName(path);

        var els = new Dictionary<string, (int, string)>();
        if (root.TryGetProperty("elements", out var es) && es.ValueKind == JsonValueKind.Object)
            foreach (var e in es.EnumerateObject())
            {
                if (MappingFiles.Int(e.Value, "school") is { } school) els[e.Name] = (school, MappingFiles.Str(e.Value, "note") ?? "");
                else errors.Add($"{file} elements.{e.Name}：缺少 school");
            }

        var acts = new Dictionary<string, Dictionary<string, int>>();
        if (root.TryGetProperty("actions", out var asx) && asx.ValueKind == JsonValueKind.Object)
            foreach (var a in asx.EnumerateObject())
            {
                if (!KnownActions.Contains(a.Name)) errors.Add($"{file} actions.{a.Name}：不认识的动作（可用：{string.Join(" ", KnownActions)}）");
                acts[a.Name] = SpellMap(a.Value, $"{file} actions.{a.Name}", els, errors);
            }

        var sks = new Dictionary<string, (string?, Dictionary<string, int>?)>();
        if (root.TryGetProperty("skills", out var ss) && ss.ValueKind == JsonValueKind.Object)
            foreach (var s in ss.EnumerateObject())
            {
                var at = $"{file} skills.{s.Name}";
                if (!Enum.TryParse<DQQ.Enums.EnumSkillNumber>(s.Name, out _)) errors.Add($"{at}：DQQ 没有这个技能（EnumSkillNumber），拼写？");
                var action = MappingFiles.Str(s.Value, "action");
                if (action != null && !KnownActions.Contains(action)) errors.Add($"{at}.action：不认识的动作 \"{action}\"");
                Dictionary<string, int>? spell = null;
                if (s.Value.TryGetProperty("spell", out var sp)) spell = SpellMap(sp, $"{at}.spell", els, errors);
                sks[s.Name] = (action, spell);
            }

        var durs = new Dictionary<string, int>();
        if (root.TryGetProperty("durations", out var ds) && ds.ValueKind == JsonValueKind.Object)
            foreach (var d in ds.EnumerateObject())
            {
                var at = $"{file} durations.{d.Name}";
                if (!Enum.TryParse<DQQ.Enums.EnumDurationNumber>(d.Name, out _)) errors.Add($"{at}：DQQ 没有这个持续效果（EnumDurationNumber），拼写？");
                if (MappingFiles.Int(d.Value, "start") is { } start) durs[d.Name] = start;
                else if (d.Value.ValueKind == JsonValueKind.Number && d.Value.TryGetInt32(out var n)) durs[d.Name] = n;
                else errors.Add($"{at}：要写 WoW 法术 id（数字，或 {{ \"start\": id }}）");
            }

        var ars = new Dictionary<string, (Dictionary<string, int>, double)>();
        if (root.TryGetProperty("areas", out var ar) && ar.ValueKind == JsonValueKind.Object)
            foreach (var a in ar.EnumerateObject())
            {
                var at = $"{file} areas.{a.Name}";
                if (!Enum.TryParse<DQQ.Enums.EnumSkillShape>(a.Name, out _)) errors.Add($"{at}：DQQ 没有这个技能形状（EnumSkillShape）");
                var radius = 8.0;
                var spells = new Dictionary<string, int>();
                if (a.Value.ValueKind == JsonValueKind.Object)
                    foreach (var p in a.Value.EnumerateObject())
                    {
                        if (p.Name == "radius") { if (p.Value.TryGetDouble(out var r) && r > 0) radius = r; else errors.Add($"{at}.radius：要写正数（码）"); continue; }
                        if (p.Name != "*" && !els.ContainsKey(p.Name)) errors.Add($"{at}.{p.Name}：不认识的元素");
                        if (p.Value.TryGetInt32(out var id)) spells[p.Name] = id; else errors.Add($"{at}.{p.Name}：要写 WoW 法术 id");
                    }
                else errors.Add($"{at}：要写 {{ \"Fire\": id, \"*\": id, \"radius\": 码 }}");
                ars[a.Name] = (spells, radius);
            }

        elements = els;
        areas = ars;
        actions = acts;
        skills = sks;
        durations = durs;
    }

    /// <summary>一个数字（任何元素都用它），或 <c>{ "元素": 法术, "*": 其余元素 }</c>。</summary>
    private static Dictionary<string, int> SpellMap(JsonElement v, string at, Dictionary<string, (int, string)> els, List<string> errors)
    {
        var map = new Dictionary<string, int>();
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var all)) { map["*"] = all; return map; }
        if (v.ValueKind != JsonValueKind.Object) { errors.Add($"{at}：要写 WoW 法术 id，或按元素的 {{ \"Fire\": id, \"*\": id }}"); return map; }
        foreach (var p in v.EnumerateObject())
        {
            if (p.Name != "*" && !els.ContainsKey(p.Name)) errors.Add($"{at}.{p.Name}：不认识的元素（elements 里没有）");
            if (p.Value.TryGetInt32(out var id)) map[p.Name] = id;
            else errors.Add($"{at}.{p.Name}：要写 WoW 法术 id（数字）");
        }
        return map;
    }

    /// <summary>伤害学派（0 物理 2 火焰 3 自然 4 冰霜 5 暗影 6 奥术）；表里没有的元素按物理。</summary>
    public static int School(string? element)
    {
        if (element != null && elements.TryGetValue(element, out var e)) return e.School;
        if (element != null) MappingLog.Miss("EnumDamageType", element);
        return 0;
    }

    /// <summary>持续效果开始时播的 WoW 法术；没配 = 不播（大多数持续效果）。</summary>
    public static int DurationSpell(string? duration) =>
        duration != null && durations.TryGetValue(duration, out var s) ? s : 0;

    /// <summary>
    /// 角色动作，按 docs/skill-presentation.md §2.1 / §2.1.1 的顺序判定（个别技能可在 skills.json 覆盖）。
    /// <paramref name="weapon"/>：施放者主手武器种类（equipment.json 的 weapon），怪物与召唤物为 null。
    /// </summary>
    public static string ActionFor(string? skill, IReadOnlyCollection<string> tags, string? weapon, bool enemy)
    {
        if (skill == null) return "none";                                       // 持续伤害/治疗、倒地：没有动作
        if (skills.TryGetValue(skill, out var o) && o.Action != null) return o.Action;
        bool Has(string t) => tags.Contains(t);
        if (tags.Count == 0) return enemy ? "mobAttack" : "melee";              // 怪物专用技能：怪物自己的攻击动画
        if (Has("Channel")) return "channel";                                   // 1 引导（旋风斩在 skills 覆盖为 spin）
        var ranged = weapon != null && Ranged.Contains(weapon);
        if (Has("Attack") && !Has("Melee") && ranged) return weapon == "thrown" ? "throw" : "shoot"; // 2 远程武器 + 非肉搏攻击
        if (Has("Project")) return Has("Attack") && Has("Melee") ? "melee" : "projectile";        // 3 投射物（刀光剑影类仍挥砍）
        if (Has("Attack")) return enemy ? "mobAttack" : "melee";                // 4 攻击（肉搏，或拿近战武器的非肉搏攻击）
        if (Has("Nova")) return "nova";
        if (Has("Spell")) return "cast";                                        // 5 法术
        if (Has("Warcry")) return "shout";                                      // 6 其他
        if (Has("Aura")) return "aura";
        if (Has("Shoryu")) return "kick";
        if (Has("Corpse") || Has("Minion")) return "judgement";
        return enemy ? "mobAttack" : "none";
    }

    /// <summary>动作 + 元素 → WoW 法术：先看技能覆盖，再看动作表（元素，再 "*"）；都没有为 0。</summary>
    public static int SpellFor(string action, string? skill, string? element)
    {
        static int Pick(Dictionary<string, int> m, string? el) =>
            el != null && m.TryGetValue(el, out var s) ? s : m.TryGetValue("*", out var any) ? any : 0;
        if (skill != null && skills.TryGetValue(skill, out var o) && o.Spell is { Count: > 0 } sp) return Pick(sp, element);
        return actions.TryGetValue(action, out var m) ? Pick(m, element) : 0;
    }

    /// <summary>技能演出用的元素：技能的元素标签优先（烈焰新星的伤害日志可能记成物理），其次技能定义的元素，最后实际伤害最多的元素。</summary>
    public static string ElementFor(IReadOnlyCollection<string> tags, string? defined, string fromDamage)
    {
        foreach (var (tag, el) in new[] { ("Fire", "Fire"), ("Cold", "Cold"), ("Lightning", "Lightning"), ("Chaos", "Chaos"), ("Physical", "Physics") })
            if (tags.Contains(tag)) return el;
        return defined ?? fromDamage;
    }

    /// <summary>
    /// 技能区域（<c>SkillRangeAndTarget</c> 日志：形状、半径、中心）的演法：在中心播 <c>areas</c> 里该形状 × 元素的法术，
    /// 按半径缩放（<see cref="SkillPlay"/> 的第二个值为法术，缩放另算见 <see cref="AreaScale"/>）。
    /// 新星、旋风斩、引导自己已有范围表现，不重复；没配的形状不播。
    /// </summary>
    public static SkillPlay Area(string? shape, string action, string? element)
    {
        if (shape == null || action is "nova" or "spin" or "channel" || !areas.TryGetValue(shape, out var a))
            return new SkillPlay("none", 0, School(element));
        var spell = element != null && a.Spell.TryGetValue(element, out var s) ? s : a.Spell.GetValueOrDefault("*");
        return new SkillPlay(spell > 0 ? "area" : "none", spell, School(element));
    }

    /// <summary>区域特效的缩放：DQQ 半径（单位，1 单位 = 2 码）÷ 特效本身的半径（码）。</summary>
    public static double AreaScale(string? shape, double range) =>
        shape != null && areas.TryGetValue(shape, out var a) ? Math.Clamp(range * 2 / a.Radius, 0.3, 4) : 1;

    /// <summary>一个技能事件的演法。</summary>
    public static SkillPlay Play(string? skill, IReadOnlyCollection<string> tags, string? element, string? weapon, bool enemy)
    {
        var action = ActionFor(skill, tags, weapon, enemy);
        return new SkillPlay(action, SpellFor(action, skill, element), School(element));
    }
}
