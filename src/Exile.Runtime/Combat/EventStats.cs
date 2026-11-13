using Exile.Runtime.Protocol;
using Exile.Runtime.Replays;
using System.Text;

namespace Exile.Runtime.Combat;

/// <summary>
/// 后台重放录像目录里的每个录像，统计实际出现的技能事件（PLAN-0006 D：先看清 DQQ 给了什么，再建技能表）：
/// 每个技能出现的事件种类、标签、阶段、形状、元素、次数。
/// </summary>
public static class EventStats
{
    private sealed class Row
    {
        public int Count;
        public readonly HashSet<string> Kinds = [], Tags = [], Phases = [], Shapes = [], Elements = [], Casters = [], Plays = [];
    }

    public static async Task<string> Build(ReplayFiles files)
    {
        var rows = new SortedDictionary<string, Row>();
        var kinds = new SortedDictionary<string, int>();
        var raw = new SortedDictionary<string, int>();
        var projects = new SortedDictionary<string, (int Count, HashSet<string> Ids)>();
        CombatRunner.RawLog = l =>
        {
            var k = $"{l.LogType} | {l.Skill?.SkillNumber?.ToString() ?? l.TriggerSkill?.SkillNumber?.ToString() ?? "—"} | {l.Skill?.DurationNumber?.ToString() ?? ""}";
            raw[k] = raw.GetValueOrDefault(k) + 1;
        };
        var md = new StringBuilder("# 技能事件统计（`--event-stats`）\n\n");
        foreach (var (file, replay) in await files.List())
        {
            var who = new Dictionary<string, string>();
            var n = 0;
            var rawStart = raw.Values.Sum();
            try
            {
                var end = await CombatRunner.RunAsync(replay, watch: false, _ => Task.CompletedTask, f =>
                {
                    foreach (var a in f.Actors) who.TryAdd(a.Id, a.Kind == "player" ? "英雄" : a.Mob ?? a.Kind);
                    foreach (var pj in f.Projects)
                    {
                        var k = $"{file} | {pj.Name ?? "—"} | r={pj.Radius:0.##}";
                        var e = projects.TryGetValue(k, out var pv) ? pv : (Count: 0, Ids: new HashSet<string>());
                        e.Ids.Add(pj.Id);
                        projects[k] = (e.Count + 1, e.Ids);
                    }
                    foreach (var e in f.Events)
                    {
                        n++;
                        kinds[e.Kind] = kinds.GetValueOrDefault(e.Kind) + 1;
                        var key = e.Skill ?? (e.Duration != null ? $"（持续效果 {e.Duration}）" : "（无技能）");
                        if (!rows.TryGetValue(key, out var r)) rows[key] = r = new Row();
                        r.Count++;
                        r.Kinds.Add(e.Kind);
                        foreach (var t in e.Tags) r.Tags.Add(t);
                        if (e.Phase != null) r.Phases.Add(e.Phase);
                        if (e.Shape != null) r.Shapes.Add($"{e.Shape}({e.Range:0.#})");
                        if (e.Kind is "DamageTaken") r.Elements.Add(e.Element);
                        if (e.Action != null) r.Plays.Add(e.Spell > 0 ? $"{e.Action}:{e.Spell}" : e.Action);
                        if (e.From != null && who.TryGetValue(e.From, out var c)) r.Casters.Add(c);
                    }
                    return Task.CompletedTask;
                }, CancellationToken.None);
                var rawN = raw.Values.Sum() - rawStart;
                md.AppendLine($"- {file}：{end.Ticks} tick，{n} 个事件（原始日志 {rawN}），结果 {end.Result}");
            }
            catch (Exception ex) { md.AppendLine($"- {file}：重放失败 {ex.Message}"); }
        }
        CombatRunner.RawLog = null;
        md.AppendLine("\n## DQQ 投射物（录像 | 名字 | 半径：帧数、个数）\n");
        foreach (var (k, v) in projects) md.AppendLine($"- {k}：{v.Count} 帧，{v.Ids.Count} 个");
        md.AppendLine("\n## DQQ 日志种类（EnumLogType 全部）\n\n" + string.Join("、", Enum.GetNames<DQQ.Enums.EnumLogType>()));
        md.AppendLine("\n## DQQ 原始日志（过滤前：种类 | 技能 | 持续效果）\n\n| 种类 | 技能 | 持续效果 | 次数 |\n|---|---|---|---|");
        foreach (var (k, c) in raw) md.AppendLine($"| {k} | {c} |");
        md.AppendLine("\n## 事件种类\n\n| 种类 | 次数 |\n|---|---|");
        foreach (var (k, c) in kinds) md.AppendLine($"| {k} | {c} |");
        md.AppendLine("\n## 技能\n\n| 技能 | 次数 | 事件种类 | 标签 | 阶段 | 形状(范围) | 元素 | 施放者 | 演法（动作:WoW 法术） |\n|---|---|---|---|---|---|---|---|---|");
        static string J(IEnumerable<string> s) => string.Join(" ", s.Order());
        foreach (var (k, r) in rows)
            md.AppendLine($"| {k} | {r.Count} | {J(r.Kinds)} | {J(r.Tags)} | {J(r.Phases)} | {J(r.Shapes)} | {J(r.Elements)} | {J(r.Casters)} | {J(r.Plays)} |");
        md.AppendLine();
        Visuals.Mapping.MappingReport.SkillCatalog(md);
        return md.ToString();
    }
}
