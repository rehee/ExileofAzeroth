using DQQ.Enums;
using System.Text;

namespace Exile.Runtime.Visuals.Mapping;

/// <summary>
/// <c>--mapping-report</c>：列出 DQQ 每个枚举值的对应结果与缺失项（PLAN-0006 A），写成 Markdown。
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
        Section(md, "战斗风格 EnumCombatArcheType → WoW 职业", Names<EnumCombatArcheType>(skip: "None"), ClassLooks.Table.ContainsKey,
            k => ClassLooks.Table.TryGetValue(k, out var c) ? $"{c.ClassId} | {c.Note}" : null, "职业 id | 说明");
        // 还没有表的类别：全部列为缺失，计数提醒进度。
        Pending(md, "技能 EnumSkillNumber", Names<EnumSkillNumber>(skip: "NotSpecified"));
        Pending(md, "技能标签 EnumSkillTag", Names<EnumSkillTag>(skip: "NotSpecified"));
        Pending(md, "技能形状 EnumSkillShape", Names<EnumSkillShape>(skip: "NotSpecified"));
        Pending(md, "元素 EnumDamageType", Names<EnumDamageType>());
        Pending(md, "持续效果 EnumDurationNumber", Names<EnumDurationNumber>());
        Pending(md, "地图 EnumMapNumber", Names<EnumMapNumber>(skip: "None"));
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

    private static void Pending(StringBuilder md, string title, string[] keys)
    {
        md.AppendLine($"## {title}（0/{keys.Length}，尚未建表）");
        md.AppendLine();
        md.AppendLine(string.Join("、", keys));
        md.AppendLine();
    }
}
