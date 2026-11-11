using System.Text.Json;
using System.Text.Json.Nodes;
using DQQ.Components.Stages.Actors.Characters;

namespace Exile.Runtime.Visuals.Mapping;

/// <summary>
/// 梳妆台与幻化的编辑（PLAN-0011，插件的 Barber / Transmog 窗口）：每个角色一份正在改的配置（JSON），
/// 每次改动都套在它上面预览（不存），保存时整份写回 DQQ（ICharConfigService），取消就丢掉。
/// 起点是角色现在的配置（DTO 的 WoWCharConfig，没有就用本地测试文件），所以没改的部分（技能皮肤……）原样保留。
/// </summary>
public static class Wardrobe
{
    private static readonly Dictionary<string, JsonObject> editing = new();
    /// <summary>开始编辑时取的角色（预览用，不每次都问服务器）。</summary>
    private static readonly Dictionary<string, CharacterDTO> characters = new();

    /// <summary>正在编辑的角色（开始编辑时取的）；没在编辑为 null。</summary>
    public static CharacterDTO? Character(string? characterId)
    {
        lock (editing) return characters.GetValueOrDefault(characterId ?? "");
    }
    private static readonly JsonDocumentOptions ReadOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    /// <summary>这个角色正在改的配置（第一次取时从现在的配置复制）。</summary>
    private static JsonObject Working(CharacterDTO c)
    {
        var id = c.Id?.ToString() ?? "";
        lock (editing)
        {
            if (editing.TryGetValue(id, out var w)) return w;
            characters[id] = c;
            JsonObject start = new() { ["v"] = 1 };
            if (CharConfig.RawFor(c) is { } raw)
                try
                {
                    if (JsonNode.Parse(raw.Text, documentOptions: ReadOptions) is JsonObject o) start = o;
                }
                catch (JsonException e) { Console.WriteLine($"[wardrobe] {raw.Source} 不是有效 JSON，从空配置开始：{e.Message}"); }
            return editing[id] = start;
        }
    }

    /// <summary>
    /// 一次改动套到正在改的配置上：
    /// <c>{ "look": { "race": …, … } }</c>（null 的键去掉，回到默认），
    /// <c>{ "transmog": { 槽: 外观 } }</c>（null = 清掉，回到 DQQ 装备；<c>{ "hide": true }</c> = 不显示）。
    /// 返回套用后的配置（预览用）。
    /// </summary>
    public static CharConfig? Apply(CharacterDTO c, JsonObject change)
    {
        var w = Working(c);
        lock (editing)
        {
            if (change["look"] is JsonObject look)
            {
                var target = w["look"] as JsonObject ?? new JsonObject();
                foreach (var (k, v) in look.ToArray())
                    if (v == null) target.Remove(k); else target[k] = v.DeepClone();
                w["look"] = target;
            }
            if (change["transmog"] is JsonObject tm)
            {
                var target = w["transmog"] as JsonObject ?? new JsonObject();
                foreach (var (k, v) in tm.ToArray())
                {
                    var slot = MappingFiles.WowSlotOf(k);
                    if (slot == null) { Console.WriteLine($"[wardrobe] 不认识的槽位 {k}"); continue; }
                    // 同一个 WoW 槽的旧写法（MainHand、mainHand、Body / chest……）都去掉。
                    foreach (var old in target.Select(p => p.Key).Where(o => MappingFiles.WowSlotOf(o) == slot).ToArray()) target.Remove(old);
                    if (v != null) target[slot] = v.DeepClone();
                }
                w["transmog"] = target;
            }
            if (change["skills"] is JsonObject sk)
            {
                // 技能皮肤：{ 技能: { "ring": 模型名 } }，null = 去掉（用默认的）。
                var target = w["skills"] as JsonObject ?? new JsonObject();
                foreach (var (k, v) in sk.ToArray())
                    if (v == null) target.Remove(k); else target[k] = v.DeepClone();
                w["skills"] = target;
            }
            return CharConfig.Parse(w.ToJsonString(), $"{c.DisplayName}（编辑中）");
        }
    }

    /// <summary>
    /// 技能皮肤窗口的内容：每个能换皮肤的技能、现在用的（正在改的配置里的，没写为 null）、默认的和可选的。
    /// 现在只有范围圈（正义之火……），选项是地面模型库。
    /// </summary>
    public static object[] SkinOptions(CharacterDTO c)
    {
        var w = Working(c);
        lock (editing)
            return SkillLooks.RingSkills().Select(r => (object)new
            {
                skill = r.Skill,
                label = r.Skill.EndsWith("Profile") ? r.Skill[..^"Profile".Length] : r.Skill,
                kind = "ring",
                current = (w["skills"]?[r.Skill]?["ring"] as JsonValue)?.TryGetValue<string>(out var cur) == true ? cur : null,
                @default = r.Default,
                options = SkillLooks.GroundModelNames.ToArray(),
            }).ToArray();
    }

    /// <summary>正在改的配置（保存用）；没在改为 null。</summary>
    public static string? Pending(CharacterDTO c)
    {
        lock (editing) return editing.TryGetValue(c.Id?.ToString() ?? "", out var w) ? w.ToJsonString() : null;
    }

    /// <summary>不改了（保存后或取消）。</summary>
    public static void Drop(string? characterId)
    {
        lock (editing)
        {
            editing.Remove(characterId ?? "");
            characters.Remove(characterId ?? "");
        }
    }

    /// <summary>物品库（wow-items.json）的条目，给幻化窗口列在最前（带名字）。</summary>
    public static object[] NamedItems() =>
        MappingFiles.WowItems.Select(p => (object)new
        {
            key = p.Key,
            id = MappingFiles.Int(p.Value, "id"),
            name = p.Key,  // 英文客户端的字体没有中文：显示键名
            display = MappingFiles.Int(p.Value, "display"),
            type = MappingFiles.Str(p.Value, "type"),
            weapon = MappingFiles.Str(p.Value, "weapon"),
        }).ToArray();
}
