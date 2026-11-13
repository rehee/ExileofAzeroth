using DQQ.Enums;
using System.Text;

namespace Exile.Runtime.Visuals.Mapping;

/// <summary>
/// <c>--mapping-report</c>：列出 DQQ 每个枚举值的对应结果与缺失项（PLAN-0006 A），写成 Markdown。
/// 技能、标签、持续效果要读 DQQ 的技能定义，所以在 DqqHost 建好之后调用（不登录）。
/// 新增 DQQ 枚举值而表里没加时，它出现在"缺失"里，游戏照常用回退值。
/// </summary>
public static class MappingReport
{
    public static string Build()
    {
        var md = new StringBuilder();
        md.AppendLine("# DQQ ↔ WoW 对应表：现状");
        md.AppendLine();
        md.AppendLine($"生成于 {DateTime.Now:yyyy-MM-dd HH:mm}，由 `Exile.Runtime --mapping-report` 输出（PLAN-0006）。");
        md.AppendLine($"表的来源：`{MappingFiles.Dir}`（格式见 docs/mapping-config.md）。");
        md.AppendLine();
        if (MappingFiles.Errors.Count > 0)
        {
            md.AppendLine($"## 配置文件的问题（{MappingFiles.Errors.Count}）");
            md.AppendLine();
            foreach (var e in MappingFiles.Errors) md.AppendLine($"- {e}");
            md.AppendLine();
        }

        Section(md, "怪物 EnumMob → 生物外观", Names<EnumMob>(skip: "None"), MobLooks.Table.ContainsKey,
            k => MobLooks.Table.TryGetValue(k, out var l) ? Mob(l) : null,
            "外观 | 模型 | 说明");
        var rarityField = MobLooks.RarityProperty(typeof(DQQ.TickLogs.TickLogActor));
        Section(md, "怪物稀有度 EnumMobRarity → 名字颜色、头像边框、身上光环" +
                (rarityField != null ? $"（取自 TickLogActor.{rarityField.Name}）" : "（帧快照不带，战斗中从 DQQ 地图上的活单位读；读到多少见 event-stats 日志的“怪物稀有度”）"),
            Names<EnumMobRarity>(skip: "None"), MobLooks.Rarities.ContainsKey,
            k => MobLooks.Rarities.TryGetValue(k, out var r)
                ? $"{r.Color} | {r.Rank} | {(r.Auras.Length > 0 ? string.Join("、", r.Auras) : "")} | {r.Glow ?? ""} | {r.Note}" : null,
            "名字颜色 | 生物等级（边框） | 光环 | 外发光 | 说明");
        Section(md, "战斗风格 EnumCombatArcheType → WoW 职业", Names<EnumCombatArcheType>(skip: "None"), ClassLooks.Table.ContainsKey,
            k => ClassLooks.Table.TryGetValue(k, out var c) ? $"{c.ClassId} | {c.Note}" : null, "职业 id | 说明");
        Section(md, "装备 EnumItem → 装备外观（不含材料）", Enum.GetValues<EnumItem>().Where(i => (int)i >= 100000).Select(i => i.ToString()).ToArray(), MappingFiles.EquipItems.ContainsKey,
            k => MappingFiles.EquipItems.TryGetValue(k, out var e) ? $"{e} | equipment.json" : null, "外观 | 来源");
        Section(md, "独特物品 EnumUniqueItem → 装备外观（盖过底子物品）", Names<EnumUniqueItem>(skip: "None"), MappingFiles.EquipUniques.ContainsKey,
            k => MappingFiles.EquipUniques.TryGetValue(k, out var e) ? $"{e} | equipment.json" : null, "外观 | 来源");
        Section(md, "元素 EnumDamageType → WoW 学派", Names<EnumDamageType>(), SkillLooks.Elements.ContainsKey,
            k => SkillLooks.Elements.TryGetValue(k, out var e) ? $"{e.School} | {e.Note}" : null, "学派 | 说明");
        Skills(md);
        // 现在没有技能用的形状（DQQ 以后用上时再定）不算缺失。
        Section(md, "技能形状 EnumSkillShape → 区域、射线、锥形", Names<EnumSkillShape>(skip: "NotSpecified"),
            k => SkillLooks.ShapeLook(k) != null || !Defs().Values.Any(d => d.Shape == k),
            k => SkillLooks.ShapeLook(k) ?? (Defs().Values.Any(d => d.Shape == k) ? null : "未定（现在没有技能用）"),
            "画法（键 = 元素或技能标签，值 = 借用的 WoW 法术）");
        Tags(md);
        Durations(md);
        Section(md, "地图 EnumMapNumber → 战场场景", Names<EnumMapNumber>(skip: "None"), MappingFiles.MapScenes.ContainsKey,
            k => MappingFiles.MapScenes.TryGetValue(k, out var sc) && MappingFiles.Scenes.TryGetValue(sc, out var b)
                ? $"{sc} | ({b.Center[0]:F1}, {b.Center[1]:F1}) | {b.Note}" : null, "场景 | 中心 | 说明");
        return md.ToString();
    }

    private static string Mob(MobLook l) => l.Dress is { } d
        ? $"穿装备：种族 {d.Race} 性别 {d.Gender} 肤色 {d.Skin}；" +
          string.Join("、", d.Items.Select(i => $"槽 {i.Slot} = {i.DisplayId}")) + $" | 人形 | {l.Note}{(l.Boss ? "（首领）" : "")}"
        : $"{l.DisplayId} | {l.Model} | {l.Note}{(l.Boss ? "（首领）" : "")}";

    private static string[] Names<T>(string? skip = null) where T : struct, Enum =>
        Enum.GetNames<T>().Where(n => n != skip).ToArray();

    private static void Section(StringBuilder md, string title, string[] keys, Func<string, bool> has,
        Func<string, string?> row, string header)
    {
        var missing = keys.Where(k => !has(k)).ToArray();
        md.AppendLine($"## {title}（{keys.Length - missing.Length}/{keys.Length}）");
        md.AppendLine();
        md.AppendLine($"| DQQ | {header} |");
        md.AppendLine($"|-----|{string.Concat(Enumerable.Repeat("---|", header.Split('|').Length))}");
        foreach (var k in keys) md.AppendLine($"| {k} | {row(k) ?? "**缺失（回退）**"} |");
        md.AppendLine();
    }

    /// <summary>DQQ 全部技能的定义（标签、元素、形状、持续效果、是否怪物专用）；读不到的技能不在里面。要 DQQ 对象池（DqqHost 之后）。</summary>
    private sealed record SkillDef(string Name, string Title, string[] Tags, string? Element, string Shape, string? Duration, bool MobOnly);

    private static Dictionary<string, SkillDef>? defs;
    private static readonly Dictionary<string, string> defErrors = new();

    private static Dictionary<string, SkillDef> Defs()
    {
        if (defs != null) return defs;
        defs = new();
        foreach (var n in Enum.GetValues<EnumSkillNumber>())
        {
            if (n == EnumSkillNumber.NotSpecified) continue;
            try
            {
                var p = DQQ.Pools.DQQPool.TryGet<DQQ.Profiles.Skills.SkillProfile, EnumSkillNumber>(n);
                if (p == null) { defErrors[n.ToString()] = "DQQ 没有定义"; continue; }
                defs[n.ToString()] = new SkillDef(n.ToString(), p.SkillName ?? "", p.OriginalTag.Select(t => t.ToString()).ToArray(),
                    p.DamageType?.ToString(), p.SkillShape.ToString(), p.DurationNumber?.ToString(), p.NoPlayerSkill);
            }
            catch (Exception ex) { defErrors[n.ToString()] = $"读取失败：{ex.GetType().Name} {ex.Message}"; }
        }
        return defs;
    }

    /// <summary>辅助技能：改主技能的形状、数量或触发方式，本身不演（日志记的是主技能）。DQQ 没有单独的标记，按名字认。</summary>
    private static bool IsSupport(string n) =>
        n.EndsWith("Support") || n.StartsWith("Barrage") || n.EndsWith("MultipleProjectiles") || n is "MultiStrike" or "CastOnCriticalStrike" or "PhantomBlade";

    /// <summary>
    /// 技能：按 docs/skill-presentation.md §2.1 的规则（加 skills.json 的覆盖）算出的动作、借用的法术、元素与形状。
    /// 按英雄拿近战武器算（拿弓、魔杖时攻击改为射击，见 §2.1）；怪物专用技能按怪物算。
    /// 没有动作、没有法术、形状不画、也没有持续效果开始时的法术 → 缺失。
    /// </summary>
    private static void Skills(StringBuilder md)
    {
        var all = Names<EnumSkillNumber>(skip: "NotSpecified");
        var rows = new List<string>();
        var missing = 0;
        foreach (var name in all)
        {
            if (!Defs().TryGetValue(name, out var d))
            {
                missing++;
                rows.Add($"| {name} | | | | | | | | **{defErrors.GetValueOrDefault(name, "DQQ 没有定义")}** |");
                continue;
            }
            var element = SkillLooks.ElementFor(d.Tags, d.Element, "Physics");
            var play = SkillLooks.Play(name, d.Tags, element, null, d.MobOnly);
            var shape = d.Shape == "NotSpecified" ? null : d.Shape;
            var shapeLook = shape != null ? SkillLooks.ShapeLook(shape) : null;
            var start = SkillLooks.DurationSpell(d.Duration);
            string verdict;
            if (IsSupport(name)) verdict = "辅助：随主技能演" + (shape != null ? (shapeLook != null ? $"（形状 {shape}）" : $"（形状 {shape} **不画**）") : "");
            else if (play.Action == "none" && play.Spell == 0 && shapeLook == null && start == 0) { verdict = "**没有表现（回退）**"; missing++; }
            else verdict = (SkillLooks.Skills.ContainsKey(name) ? "skills.json 覆盖" : "规则")
                + (start > 0 ? $"；持续效果开始时 {start}" : "") + (shape != null && shapeLook == null ? $"；形状 {shape} **不画**" : "");
            var spell = IsSupport(name) ? "" : play.Spell > 0 ? $"{play.Spell}{(play.Hit ? "" : "（只施法）")}" : "—";
            rows.Add($"| {name} | {d.Title} | {(d.MobOnly ? "怪物" : "")} | {string.Join(" ", d.Tags)} | {(IsSupport(name) ? "" : play.Action)} | {spell} | {element} | {shape} | {verdict} |");
        }
        md.AppendLine($"## 技能 EnumSkillNumber → 动作、借用的法术（{all.Length - missing}/{all.Length}）");
        md.AppendLine();
        md.AppendLine("动作按英雄拿近战武器算（拿弓攻击 → shoot、拿魔杖 → wandShoot，见 docs/skill-presentation.md §2.1）；melee / mobAttack 由伤害事件带出挥砍，不播法术。");
        md.AppendLine();
        md.AppendLine("| DQQ | 名字 | 专用 | 标签 | 动作 | 法术 | 元素 | 形状 | 来源 |");
        md.AppendLine("|-----|---|---|---|---|---|---|---|---|");
        foreach (var r in rows) md.AppendLine(r);
        md.AppendLine();
    }

    /// <summary>每个技能标签在表现里的作用（SkillLooks.ActionFor、ElementFor 与 areas/rays 的键）。没写的标签不影响表现。</summary>
    private static readonly Dictionary<string, string> TagRoles = new()
    {
        ["Spin"] = "动作：旋转（spin；加 Channel 为 spinChannel，一直转）",
        ["Channel"] = "动作：引导（channel，向前指；光束由射线、散射日志画）",
        ["Attack"] = "动作：挥砍（melee）/ 怪物自己的攻击（mobAttack）；拿弓 shoot、拿魔杖 wandShoot",
        ["Melee"] = "攻击带它：拿弓、魔杖也挥砍；投射物 + 攻击 + 肉搏仍挥砍；areas 的键（雷霆一击）",
        ["Project"] = "动作：投射物（projectile）；拿弓、魔杖的攻击带它就射击",
        ["Nova"] = "动作：新星（nova）",
        ["Spell"] = "动作：施法（cast）",
        ["Warcry"] = "动作：战斗怒吼（shout）",
        ["Aura"] = "动作：开启光环（aura，只第一次激活）",
        ["Shoryu"] = "动作：脚踢（kick）",
        ["Corpse"] = "动作：审判式施法（judgement）",
        ["Minion"] = "动作：审判式施法（judgement）",
        ["Physical"] = "元素：物理（标签优先于技能定义的元素）",
        ["Fire"] = "元素：火焰",
        ["Cold"] = "元素：冰霜",
        ["Lightning"] = "元素：闪电",
        ["Chaos"] = "元素：混沌",
        ["Basic"] = "分级，不影响表现",
        ["Core"] = "分级，不影响表现",
        ["Master"] = "分级，不影响表现",
        ["Tactical"] = "分级，不影响表现",
        ["Ultimate"] = "分级，不影响表现",
        ["Trigger"] = "触发类辅助，不影响表现",
        ["Duration"] = "带持续效果：见下面的持续效果一节",
    };

    private static void Tags(StringBuilder md)
    {
        var keys = Names<EnumSkillTag>(skip: "NotSpecified");
        var used = keys.ToDictionary(k => k, k => Defs().Values.Where(d => d.Tags.Contains(k)).Select(d => d.Name).ToArray());
        // 有作用，或现在没有技能用（DQQ 以后用上时再定）都算有着落；有技能用却没有作用的列为缺失。
        Section(md, "技能标签 EnumSkillTag → 在表现里的作用", keys, k => TagRoles.ContainsKey(k) || used[k].Length == 0,
            k => TagRoles.TryGetValue(k, out var role) ? $"{role} | {used[k].Length}"
                : used[k].Length == 0 ? "未定（现在没有技能用） | 0" : null,
            "作用 | 用它的技能数");
    }

    private static void Durations(StringBuilder md)
    {
        var keys = Names<EnumDurationNumber>(skip: "None");
        var shown = keys.Count(k => SkillLooks.DurationSpell(k) > 0 || SkillLooks.AuraLookText(k) != null);
        md.AppendLine($"## 持续效果 EnumDurationNumber → 开始时的法术、常驻光环（显示 {shown}/{keys.Length}，其余不显示）");
        md.AppendLine();
        md.AppendLine("不显示是允许的（验收标准）：WoW 里没有看得出来的对应特效的持续效果就不画。");
        md.AppendLine();
        md.AppendLine("| DQQ | 开始时 | 常驻 | 产生它的技能 |");
        md.AppendLine("|-----|---|---|---|");
        foreach (var k in keys)
        {
            var start = SkillLooks.DurationSpell(k);
            var from = Defs().Values.Where(d => d.Duration == k).Select(d => d.Name).Concat(SkillLooks.SkillsOf(k)).Distinct();
            md.AppendLine($"| {k} | {(start > 0 ? start.ToString() : "")} | {SkillLooks.AuraLookText(k) ?? "不显示"} | {string.Join("、", from)} |");
        }
        md.AppendLine();
    }

    /// <summary>DQQ 全部技能的定义（标签、形状、元素、是否怪物专用），D 建技能表用：录像里没出现的技能也能先按规则分好类。
    /// 要 DQQ 对象池已建好（DqqHost 之后），所以由 <c>--event-stats</c> 输出。</summary>
    internal static void SkillCatalog(System.Text.StringBuilder md)
    {
        md.AppendLine("## 技能定义（DQQ SkillProfile，供 D 建表）");
        md.AppendLine();
        md.AppendLine("| 技能 | 名字 | 怪物专用 | 标签 | 形状 | 元素 | 投射物类型 | 持续效果 |");
        md.AppendLine("|---|---|---|---|---|---|---|---|");
        foreach (var n in Enum.GetValues<EnumSkillNumber>())
        {
            if (n == EnumSkillNumber.NotSpecified) continue;
            DQQ.Profiles.Skills.SkillProfile? p = null;
            try { p = DQQ.Pools.DQQPool.TryGet<DQQ.Profiles.Skills.SkillProfile, EnumSkillNumber>(n); } catch (Exception ex) { md.AppendLine($"| {n} | 读取失败：{ex.GetType().Name} {ex.Message} | | | | | | |"); continue; }
            if (p == null) { md.AppendLine($"| {n} | （没有定义） | | | | | | |"); continue; }
            string Safe(Func<string> f) { try { return f(); } catch { return "?"; } }
            md.AppendLine($"| {n} | {Safe(() => p.SkillName ?? "")} | {Safe(() => p.NoPlayerSkill ? "是" : "")} | " +
                $"{Safe(() => string.Join(" ", p.OriginalTag))} | {Safe(() => p.SkillShape.ToString())} | " +
                $"{Safe(() => p.DamageType?.ToString() ?? "")} | {Safe(() => p.ProjectType?.ToString() ?? "")} | {Safe(() => p.DurationNumber?.ToString() ?? "")} |");
        }
        md.AppendLine();
    }
}
