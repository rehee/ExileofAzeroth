using System.Text.Json;
using Exile.Runtime.Protocol;

namespace Exile.Runtime.Visuals.Mapping;

/// <summary>
/// DQQ ↔ WoW 对应表的配置文件（<c>config/mapping/*.json</c>，用户 2026-11-07：以后频繁调整，用配置文件）。
/// 启动时读一次；<see cref="Refresh"/> 在文件改过时重读（`.lineup mobs`、每场回放开始时调用），不用重启或重编。
/// 读失败时保留上一次读好的表，并把错误记进 <see cref="Errors"/>（日志与 <c>--mapping-report</c> 都会显示）。
/// 文件格式见 docs/mapping-config.md。
/// </summary>
public static class MappingFiles
{
    private static readonly JsonDocumentOptions Options = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly object Gate = new();
    private static DateTime stamp;

    /// <summary>配置目录（<see cref="Init"/> 决定）。</summary>
    public static string? Dir { get; private set; }

    public static IReadOnlyDictionary<string, MobLook> Mobs { get; private set; } = new Dictionary<string, MobLook>();
    public static MobLook MobFallback { get; private set; } = new(26, null, "回退", null, false);
    public static IReadOnlyDictionary<string, (int ClassId, string Note)> Classes { get; private set; } =
        new Dictionary<string, (int, string)>();
    public static int ClassFallback { get; private set; } = 1;

    /// <summary>英雄（DQQ 角色）的统一外观（不含装备），equipment.json 的 hero。</summary>
    public static Dress HeroLook { get; private set; } = new(1, 1, 0, 0, 0, 0, []);
    /// <summary>DQQ 装备槽名 → WoW 装备槽名（equipment.json 的 slots）。</summary>
    public static IReadOnlyDictionary<string, string> EquipSlots { get; private set; } = new Dictionary<string, string>();
    /// <summary>DQQ 物品名 → 外观（槽位由 DQQ 装备槽决定，这里只存外观、放法、武器种类）。</summary>
    public static IReadOnlyDictionary<string, JsonElement> EquipItems { get; private set; } = new Dictionary<string, JsonElement>();
    /// <summary>DQQ 独特物品名（EnumUniqueItem）→ 外观，写法同 <see cref="EquipItems"/>；独特物品有自己的模型，盖过底子物品的外观。</summary>
    public static IReadOnlyDictionary<string, JsonElement> EquipUniques { get; private set; } = new Dictionary<string, JsonElement>();

    /// <summary>战场场景（maps.json 的 scenes）与 DQQ 地图 → 场景名。</summary>
    public static IReadOnlyDictionary<string, Battlefield> Scenes { get; private set; } = new Dictionary<string, Battlefield>();
    public static IReadOnlyDictionary<string, string> MapScenes { get; private set; } = new Dictionary<string, string>();
    public static string SceneFallback { get; private set; } = "forest";

    /// <summary>一张 DQQ 地图的战场；表里没有时用回退场景并记一次缺失，回退也没有时为 null（客户端用默认战场）。</summary>
    public static Battlefield? FieldFor(string map)
    {
        if (!MapScenes.TryGetValue(map, out var scene)) { MappingLog.Miss("EnumMapNumber", map); scene = SceneFallback; }
        return Scenes.TryGetValue(scene, out var b) ? b with { Map = map } : null;
    }

    /// <summary>最近一次读取的问题（文件缺失、JSON 错、引用了不存在的外观……）。</summary>
    public static IReadOnlyList<string> Errors { get; private set; } = [];

    /// <summary>
    /// 找配置目录并读取：<paramref name="dir"/>（<c>--mapping</c>）→ 仓库里的 <c>config/mapping</c>（从 runtime 所在目录往上找，
    /// 开发时改了马上生效）→ runtime 旁的 <c>mapping/</c>（编译时复制过去，发布用）。
    /// </summary>
    public static void Init(string? dir = null)
    {
        Dir = dir ?? FindRepoDir() ?? Path.Combine(AppContext.BaseDirectory, "mapping");
        Console.WriteLine($"[mapping] 对应表目录 {Path.GetFullPath(Dir)}");
        Refresh(force: true);
    }

    /// <summary>文件改过（或 <paramref name="force"/>）就重读。</summary>
    public static void Refresh(bool force = false)
    {
        if (Dir == null) { Init(); return; }
        lock (Gate)
        {
            var newest = Directory.Exists(Dir)
                ? Directory.EnumerateFiles(Dir, "*.json").Select(File.GetLastWriteTimeUtc).DefaultIfEmpty().Max()
                : default;
            if (!force && newest == stamp) return;
            stamp = newest;
            var errors = new List<string>();
            // 配置写错不能让 runtime 崩：每个文件单独兜住，问题记进 errors（保留上一次读好的表）。
            void Safe(string file, Action<string, List<string>> load)
            {
                try { load(Path.Combine(Dir!, file), errors); }
                catch (Exception e) { errors.Add($"{file}：读取出错（{e.GetType().Name}：{e.Message}）"); }
            }
            Safe("mobs.json", LoadMobs);
            Safe("classes.json", LoadClasses);
            Safe("equipment.json", LoadEquipment);
            Safe("maps.json", LoadMaps);
            Safe("skills.json", SkillLooks.Load);
            Errors = errors;
            Console.WriteLine($"[mapping] 已读取：怪物 {Mobs.Count}、战斗风格 {Classes.Count}、装备 {EquipItems.Count}、战场 {Scenes.Count}、技能表现 {SkillLooks.Summary}" +
                (errors.Count == 0 ? "" : $"，{errors.Count} 个问题"));
            foreach (var e in errors) Console.WriteLine($"[mapping] 问题：{e}");
        }
    }

    private static string? FindRepoDir()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var candidate = Path.Combine(d.FullName, "config", "mapping");
            if (Directory.Exists(candidate)) return candidate;
        }
        return null;
    }

    internal static JsonDocument? Read(string path, List<string> errors)
    {
        if (!File.Exists(path)) { errors.Add($"{Path.GetFileName(path)} 不存在"); return null; }
        try { return JsonDocument.Parse(File.ReadAllText(path), Options); }
        catch (JsonException e) { errors.Add($"{Path.GetFileName(path)}：JSON 格式错误（第 {e.LineNumber + 1} 行）：{e.Message}"); return null; }
    }

    private static void LoadMobs(string path, List<string> errors)
    {
        using var doc = Read(path, errors);
        if (doc == null) return;
        var root = doc.RootElement;
        var file = Path.GetFileName(path);
        var fallback = MobFallback;
        if (root.TryGetProperty("fallback", out var fb))
            fallback = new(Int(fb, "display") ?? 26, null, Str(fb, "note") ?? "回退", null, false);
        var dresses = new Dictionary<string, Dress>();
        if (root.TryGetProperty("dresses", out var ds) && ds.ValueKind == JsonValueKind.Object)
            foreach (var d in ds.EnumerateObject())
                if (ParseDress(d.Value, $"{file} dresses.{d.Name}", errors) is { } dress) dresses[d.Name] = dress;
        var mobs = new Dictionary<string, MobLook>();
        if (root.TryGetProperty("mobs", out var ms) && ms.ValueKind == JsonValueKind.Object)
            foreach (var m in ms.EnumerateObject())
            {
                var where = $"{file} mobs.{m.Name}";
                if (!Enum.TryParse<DQQ.Enums.EnumMob>(m.Name, out _)) errors.Add($"{where}：DQQ 没有这个怪物（EnumMob），拼写？");
                Dress? dress = null;
                if (m.Value.TryGetProperty("dress", out var dv))
                {
                    if (dv.ValueKind == JsonValueKind.String)
                    {
                        if (!dresses.TryGetValue(dv.GetString()!, out var named)) errors.Add($"{where}：dresses 里没有 \"{dv.GetString()}\"");
                        dress = named;
                    }
                    else dress = ParseDress(dv, $"{where}.dress", errors);
                }
                var display = Int(m.Value, "display");
                if (display == null && dress == null) { errors.Add($"{where}：既没有 display 也没有 dress，使用回退值"); continue; }
                mobs[m.Name] = new MobLook(display ?? 0, Str(m.Value, "model"), Str(m.Value, "note") ?? "", dress,
                    m.Value.TryGetProperty("boss", out var b) && b.ValueKind == JsonValueKind.True);
            }
        else errors.Add($"{file}：缺少 \"mobs\"");
        Mobs = mobs;
        MobFallback = fallback;
    }

    private static void LoadClasses(string path, List<string> errors)
    {
        using var doc = Read(path, errors);
        if (doc == null) return;
        var root = doc.RootElement;
        var classes = new Dictionary<string, (int, string)>();
        if (root.TryGetProperty("classes", out var cs) && cs.ValueKind == JsonValueKind.Object)
            foreach (var c in cs.EnumerateObject())
            {
                if (!Enum.TryParse<DQQ.Enums.EnumCombatArcheType>(c.Name, out _))
                    errors.Add($"classes.json classes.{c.Name}：DQQ 没有这个战斗风格（EnumCombatArcheType）");
                if (Int(c.Value, "class") is { } id) classes[c.Name] = (id, Str(c.Value, "note") ?? "");
                else errors.Add($"classes.json classes.{c.Name}：缺少 class");
            }
        Classes = classes;
        ClassFallback = root.TryGetProperty("fallback", out var f) && f.ValueKind == JsonValueKind.Number && f.TryGetInt32(out var fv) ? fv : 1;
    }

    private static void LoadEquipment(string path, List<string> errors)
    {
        using var doc = Read(path, errors);
        if (doc == null) return;
        var root = doc.RootElement;
        if (root.TryGetProperty("hero", out var h) && ParseDress(h, "equipment.json hero", errors) is { } hero) HeroLook = hero;
        var slots = new Dictionary<string, string>();
        if (root.TryGetProperty("slots", out var ss) && ss.ValueKind == JsonValueKind.Object)
            foreach (var p in ss.EnumerateObject())
            {
                if (!Enum.TryParse<DQQ.Enums.EnumEquipSlot>(p.Name, out _)) errors.Add($"equipment.json slots.{p.Name}：DQQ 没有这个装备槽（EnumEquipSlot）");
                var to = p.Value.GetString() ?? "";
                if (!Slots.ContainsKey(to)) { errors.Add($"equipment.json slots.{p.Name}：不认识的 WoW 装备槽 \"{to}\""); continue; }
                slots[p.Name] = to;
            }
        var items = new Dictionary<string, JsonElement>();
        if (root.TryGetProperty("items", out var its) && its.ValueKind == JsonValueKind.Object)
            foreach (var it in its.EnumerateObject())
            {
                if (!Enum.TryParse<DQQ.Enums.EnumItem>(it.Name, out _)) errors.Add($"equipment.json items.{it.Name}：DQQ 没有这个物品（EnumItem），拼写？");
                // 试解析一次，只为检查写法；真正的槽位在穿上时由 DQQ 装备槽决定（物品写了 slot 就用它的）。
                var own = Str(it.Value, "slot");
                if (own != null && !Slots.ContainsKey(own)) errors.Add($"equipment.json items.{it.Name}.slot：不认识的装备槽 \"{own}\"");
                ParseItem(own != null && Slots.ContainsKey(own) ? own : "mainHand", it.Value, $"equipment.json items.{it.Name}", errors);
                items[it.Name] = it.Value.Clone();
            }
        var uniques = new Dictionary<string, JsonElement>();
        if (root.TryGetProperty("uniqueItems", out var us) && us.ValueKind == JsonValueKind.Object)
            foreach (var it in us.EnumerateObject())
            {
                if (!Enum.TryParse<DQQ.Enums.EnumUniqueItem>(it.Name, out _)) errors.Add($"equipment.json uniqueItems.{it.Name}：DQQ 没有这个独特物品（EnumUniqueItem），拼写？");
                var own = Str(it.Value, "slot");
                if (own != null && !Slots.ContainsKey(own)) errors.Add($"equipment.json uniqueItems.{it.Name}.slot：不认识的装备槽 \"{own}\"");
                ParseItem(own != null && Slots.ContainsKey(own) ? own : "mainHand", it.Value, $"equipment.json uniqueItems.{it.Name}", errors);
                uniques[it.Name] = it.Value.Clone();
            }
        EquipSlots = slots;
        EquipItems = items;
        EquipUniques = uniques;
    }

    /// <summary>
    /// 英雄穿上 DQQ 装备后的外观：<see cref="HeroLook"/> 加每件装备（DQQ 槽 → WoW 槽，DQQ 物品 → 外观）。
    /// 表里没有的物品不显示，记一次缺失。
    /// </summary>
    public static Dress HeroDress(IEnumerable<(string Slot, string Item, string? Unique)> equips)
    {
        var items = new List<DressItem>();
        foreach (var (slot, item, unique) in equips)
        {
            if (!EquipSlots.TryGetValue(slot, out var wowSlot)) continue;  // 项链、戒指：不显示
            // 独特物品（用户 2026-11-07）有自己的模型：先找 uniqueItems，没填就用底子物品的外观并记一次缺失。
            JsonElement look;
            string at0;
            if (unique != null && EquipUniques.TryGetValue(unique, out var u)) { look = u; at0 = $"equipment.json uniqueItems.{unique}"; }
            else
            {
                if (unique != null) MappingLog.Miss("EnumUniqueItem", unique);
                if (!EquipItems.TryGetValue(item, out look)) { MappingLog.Miss("EnumItem", item); continue; }
                at0 = $"equipment.json items.{item}";
            }
            // 物品可以指定自己的 WoW 槽（如弓放进远程槽，WoW 才拿在左手），不按 DQQ 的槽。
            var at = Str(look, "slot") is { } own && Slots.ContainsKey(own) ? own : wowSlot;
            if (ParseItem(at, look, at0, []) is { } d) items.Add(d);
        }
        return HeroLook with { Items = items.ToArray() };
    }

    /// <summary>英雄主手武器的种类（equipment.json 的 weapon，独特物品优先）；没拿或没写为 null。技能动作判定用（弓 → 射击）。</summary>
    public static string? HeroWeapon(IDictionary<DQQ.Enums.EnumEquipSlot, DQQ.Entities.ItemEntity?>? equips)
    {
        if (equips == null || !equips.TryGetValue(DQQ.Enums.EnumEquipSlot.MainHand, out var main) || main?.Profile == null) return null;
        if (main.UniqueItemNumber != DQQ.Enums.EnumUniqueItem.None && EquipUniques.TryGetValue(main.UniqueItemNumber.ToString(), out var u))
            return Str(u, "weapon");
        return EquipItems.TryGetValue(main.Profile.ProfileNumber.ToString(), out var look) ? Str(look, "weapon") : null;
    }

    /// <summary>DQQ 角色 DTO 的装备（<c>Equips</c>）穿到英雄身上：物品名、独特物品名（没有为 null）。</summary>
    public static Dress HeroDress(IDictionary<DQQ.Enums.EnumEquipSlot, DQQ.Entities.ItemEntity?>? equips) =>
        HeroDress((equips ?? new Dictionary<DQQ.Enums.EnumEquipSlot, DQQ.Entities.ItemEntity?>())
            .Where(p => p.Value?.Profile != null)
            .Select(p => (p.Key.ToString(), p.Value!.Profile!.ProfileNumber.ToString(),
                p.Value.UniqueItemNumber == DQQ.Enums.EnumUniqueItem.None ? null : p.Value.UniqueItemNumber.ToString())));

    private static void LoadMaps(string path, List<string> errors)
    {
        using var doc = Read(path, errors);
        if (doc == null) return;
        var root = doc.RootElement;
        var scenes = new Dictionary<string, Battlefield>();
        if (root.TryGetProperty("scenes", out var ss) && ss.ValueKind == JsonValueKind.Object)
            foreach (var sc in ss.EnumerateObject())
            {
                var at = $"maps.json scenes.{sc.Name}";
                var map = Int(sc.Value, "map") ?? 0;
                if (map < 0) errors.Add($"{at}：map 应为 WoW 地图 id（Map.dbc：0 东部王国、1 卡利姆多……）");
                if (!sc.Value.TryGetProperty("center", out var c) || c.ValueKind != JsonValueKind.Array || c.GetArrayLength() < 2
                    || !c[0].TryGetDouble(out var x) || !c[1].TryGetDouble(out var y))
                { errors.Add($"{at}：center 应为 [x, y]"); continue; }
                double? z = c.GetArrayLength() > 2 && c[2].TryGetDouble(out var zv) ? zv : null;
                double? clear = sc.Value.TryGetProperty("clear", out var cl) && cl.TryGetDouble(out var cv) ? cv : null;
                if (clear < 0) { errors.Add($"{at}.clear：应为 0（不清）或正数（码）"); clear = null; }
                scenes[sc.Name] = new Battlefield("", sc.Name, map, [x, y, z ?? 0], Str(sc.Value, "note") ?? "", clear);
            }
        var maps = new Dictionary<string, string>();
        if (root.TryGetProperty("maps", out var ms) && ms.ValueKind == JsonValueKind.Object)
            foreach (var m in ms.EnumerateObject())
            {
                if (!Enum.TryParse<DQQ.Enums.EnumMapNumber>(m.Name, out _)) errors.Add($"maps.json maps.{m.Name}：DQQ 没有这张地图（EnumMapNumber），拼写？");
                var scene = m.Value.GetString() ?? "";
                if (!scenes.ContainsKey(scene)) errors.Add($"maps.json maps.{m.Name}：scenes 里没有 \"{scene}\"");
                maps[m.Name] = scene;
            }
        var fallback = Str(root, "fallback") ?? "forest";
        if (!scenes.ContainsKey(fallback)) errors.Add($"maps.json fallback：scenes 里没有 \"{fallback}\"");
        Scenes = scenes;
        MapScenes = maps;
        SceneFallback = fallback;
    }

    // ---- 穿装备的人形 ----

    private static readonly Dictionary<string, int> Races = new(StringComparer.OrdinalIgnoreCase)
    {
        ["human"] = 1, ["orc"] = 2, ["dwarf"] = 3, ["nightelf"] = 4, ["undead"] = 5,
        ["tauren"] = 6, ["gnome"] = 7, ["troll"] = 8,
    };

    /// <summary>装备槽名 → (槽位, 默认 InventoryType)。</summary>
    private static readonly Dictionary<string, (int Slot, int Type)> Slots = new(StringComparer.OrdinalIgnoreCase)
    {
        ["head"] = (0, 1), ["shoulder"] = (2, 3), ["shirt"] = (3, 4), ["chest"] = (4, 5), ["waist"] = (5, 6),
        ["legs"] = (6, 7), ["feet"] = (7, 8), ["wrist"] = (8, 9), ["hands"] = (9, 10), ["back"] = (14, 16),
        ["mainHand"] = (15, 13), ["offHand"] = (16, 22), ["ranged"] = (17, 15), ["tabard"] = (18, 19),
    };

    /// <summary>InventoryType 名（物品放在身上的方式）。</summary>
    private static readonly Dictionary<string, int> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        ["head"] = 1, ["shoulder"] = 3, ["shirt"] = 4, ["chest"] = 5, ["waist"] = 6, ["legs"] = 7, ["feet"] = 8,
        ["wrist"] = 9, ["hands"] = 10, ["oneHand"] = 13, ["shield"] = 14, ["ranged"] = 15, ["back"] = 16,
        ["twoHand"] = 17, ["tabard"] = 19, ["robe"] = 20, ["mainHand"] = 21, ["offHand"] = 22, ["holdable"] = 23,
        ["thrown"] = 25, ["rangedRight"] = 26,
    };

    /// <summary>武器种类名 → 物品子类（大类 2）。</summary>
    private static readonly Dictionary<string, int> Weapons = new(StringComparer.OrdinalIgnoreCase)
    {
        ["axe"] = 0, ["axe2h"] = 1, ["bow"] = 2, ["gun"] = 3, ["mace"] = 4, ["mace2h"] = 5, ["polearm"] = 6,
        ["sword"] = 7, ["sword2h"] = 8, ["staff"] = 10, ["fist"] = 13, ["dagger"] = 15, ["thrown"] = 16,
        ["crossbow"] = 18, ["wand"] = 19,
    };

    private static Dress? ParseDress(JsonElement e, string where, List<string> errors)
    {
        if (e.ValueKind != JsonValueKind.Object) { errors.Add($"{where}：应为对象"); return null; }
        var race = 1;
        if (e.TryGetProperty("race", out var r))
        {
            if (r.ValueKind == JsonValueKind.Number) race = r.GetInt32();
            else if (!Races.TryGetValue(r.GetString() ?? "", out race)) { errors.Add($"{where}.race：不认识 \"{r.GetString()}\""); race = 1; }
        }
        var gender = Str(e, "gender")?.ToLowerInvariant() switch { "female" => 1, "male" or null => 0, var g => Bad(g) };
        int Bad(string g) { errors.Add($"{where}.gender：应为 male 或 female，而不是 \"{g}\""); return 0; }
        var items = new List<DressItem>();
        if (e.TryGetProperty("items", out var its) && its.ValueKind == JsonValueKind.Object)
            foreach (var it in its.EnumerateObject())
            {
                var at = $"{where}.items.{it.Name}";
                if (ParseItem(it.Name, it.Value, at, errors) is { } d) items.Add(d);
            }
        return new Dress(race, gender, Int(e, "skin") ?? 0, Int(e, "face") ?? 0, Int(e, "hair") ?? 0, Int(e, "hairColor") ?? 0, items.ToArray());
    }

    /// <summary>一件装备：WoW 槽名 + 外观（数字，或 {display, type, weapon}）。</summary>
    private static DressItem? ParseItem(string slotName, JsonElement value, string at, List<string> errors)
    {
        if (!Slots.TryGetValue(slotName, out var slot)) { errors.Add($"{at}：不认识的装备槽 \"{slotName}\""); return null; }
        int? display; var type = slot.Type; string? weapon = null;
        if (value.ValueKind == JsonValueKind.Number) display = value.GetInt32();
        else
        {
            display = Int(value, "display");
            if (Str(value, "type") is { } t && !Types.TryGetValue(t, out type)) { errors.Add($"{at}.type：不认识 \"{t}\""); type = slot.Type; }
            weapon = Str(value, "weapon");
        }
        if (display is not > 0) { errors.Add($"{at}：缺少外观 id（display）"); return null; }
        var subclass = 0;
        if (weapon != null && !Weapons.TryGetValue(weapon, out subclass)) errors.Add($"{at}.weapon：不认识 \"{weapon}\"");
        var isWeapon = weapon != null || slot.Slot is 15 or 17 || (slot.Slot == 16 && type is not (14 or 23));
        return new DressItem(slot.Slot, display.Value, type, isWeapon ? 2 : 4, subclass);
    }

    internal static int? Int(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.TryGetInt32(out var i) ? i : null;

    internal static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
