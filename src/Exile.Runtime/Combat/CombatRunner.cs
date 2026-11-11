using DQQ.Components.Stages.Actors.Characters;
using DQQ.Components.Stages.Maps;
using DQQ.DTOs;
using DQQ.Entities;
using DQQ.Enums;
using DQQ.Helper;
using DQQ.Pools;
using DQQ.Profiles.Skills;
using DQQ.TickLogs;
using Exile.Runtime.Protocol;
using System.Diagnostics;
using System.Text.Json;

namespace Exile.Runtime.Combat;

/// <summary>
/// 按种子运行一场 DQQ 战斗，每个 tick 把快照和日志转成渲染帧（D6、§5.3）。
/// watch：按 30 tick/s 实时推送（TickPacer 控制节奏）；background：不等待，整场直接算完（D19）。
/// 回调里立即复制纯值，不持有 DQQ 对象（Map 释放后日志会被回收）。
/// </summary>
public sealed class CombatRunner
{
    /// <summary>每条新日志（过滤前）的观察点，<c>--event-stats</c> 用来统计 DQQ 实际给了哪些日志。</summary>
    public static Action<TickLogItem>? RawLog;

    /// <summary>用角色快照开一场新战斗（新种子）。</summary>
    public static Task<CombatEnd> RunAsync(CharacterDTO character, EnumMapNumber mapNumber, bool watch,
        Func<CombatBegin, Task> onBegin, Func<CombatFrame, Task>? onFrame, CancellationToken ct,
        Func<Task>? waitReady = null)
        => RunAsync(character.NewCombatReplayForPlay(mapNumber, 1), watch, onBegin, onFrame, ct, waitReady);

    /// <summary>按录像（种子 + 快照 + 指令序列）重新模拟一场战斗，结果与原战斗一致（D6）。</summary>
    /// <param name="waitReady">开场等待：发完第一个 tick 的帧后等它完成（客户端的模型都出来了），再从下一个 tick 起按节奏推送。</param>
    public static async Task<CombatEnd> RunAsync(CombatReplayEntity replay, bool watch,
        Func<CombatBegin, Task> onBegin, Func<CombatFrame, Task>? onFrame, CancellationToken ct,
        Func<Task>? waitReady = null)
    {
        var combatId = Guid.NewGuid().ToString("N");
        using var request = replay.GetReplayRequestDTO()
            ?? throw new InvalidOperationException("无法从种子生成战斗请求。");
        request.RenderType = EnumCombatRenderType.OfflinePlay;
        request.ShowTimeLine = true;
        request.SkipDelay = true; // 节奏由 TickPacer 控制（见 TickPacer 注释）
        var pacer = watch ? new TickPacer(DQQ.Consts.DQQGeneral.TickPerSecond) : null;
        request.IsWasm = false;
        request.CancellationToken = ct;

        var focusId = request.CreatorId?.ToString();
        // 英雄主手武器种类（弓 → 射击，PLAN-0006 D）；怪物、召唤物没有。
        var weapons = (request.Creators ?? []).Where(c => c.Id != null)
            .ToDictionary(c => c.Id!.Value.ToString(), c => Visuals.Mapping.MappingFiles.HeroWeapon(c.Equips));
        var enemies = new HashSet<string>();
        // 技能 → 最近一次施放者（DQQ 投射物不带施放者：用它判定该技能的演法，例如弓射出的箭不另画）。
        var lastCaster = new Dictionary<string, string>();
        var durationsSeen = new HashSet<string>();
        // 英雄的外观按种子里的角色 DTO（录像当时的装备，config/mapping/equipment.json 对应）。
        var heroes = (request.Creators ?? []).Where(c => c.Id != null).ToDictionary(
            c => c.Id!.Value.ToString(),
            c => Visuals.Mapping.MappingFiles.HeroDress(c.Equips));
        foreach (var c in request.Creators ?? [])
            Console.WriteLine($"[combat] 种子英雄 {c.Id} {c.DisplayName}：DQQ 装备 " +
                string.Join("、", (c.Equips ?? []).Select(p => $"{p.Key}={p.Value?.Profile?.ProfileNumber.ToString() ?? "空"}{(p.Value is { UniqueItemNumber: not DQQ.Enums.EnumUniqueItem.None } v ? $"（独特 {v.UniqueItemNumber}）" : "")}")) +
                $"；显示 {(c.Id is { } id && heroes.TryGetValue(id.ToString(), out var d) ? d.Items.Length : 0)} 件");
        await onBegin(new CombatBegin(combatId, request.MapNumber.ToString(), watch ? "watch" : "background",
            DQQ.Consts.DQQGeneral.TickPerSecond, focusId, heroes));

        var frames = 0; long totalBytes = 0; var maxBytes = 0; var lastTick = 0;
        var gcBefore = GC.GetTotalPauseDuration();
        int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
        var sw = Stopwatch.StartNew();
        var seen = new HashSet<Guid>();

        request.TickRender = async p =>
        {
            if (pacer != null) await pacer.WaitForTick(p.Tick, ct);
            var events = new List<CombatEvent>();
            foreach (var log in p.TickLog ?? [])
            {
                if (!seen.Add(log.Id)) continue;
                RawLog?.Invoke(log);
                if (log.LogType is not (EnumLogType.CastSkill or EnumLogType.TriggerSpell
                    or EnumLogType.SkillRangeAndTarget or EnumLogType.DamageTaken or EnumLogType.HealingTaken
                    or EnumLogType.Killed or EnumLogType.ActorIsDown or EnumLogType.ActiveAura)) continue;
                events.Add(ToEvent(log, p.Tick, weapons, enemies));
                if (log.LogType == EnumLogType.CastSkill && log.Skill?.SkillNumber is { } cs && log.From?.Id is { } cf)
                    lastCaster[cs.ToString()] = cf.ToString();
            }
            foreach (var t in p.TimeLine ?? [])
            {
                var tick = t.ActionTick ?? p.Tick;
                lastTick = Math.Max(lastTick, tick);
                foreach (var e in t.Enemies ?? []) if (e.Id is { } eid) enemies.Add(eid.ToString());
                // 持续效果出现（没有施放日志的技能靠它：战吼、超级霸王电影弹……，docs/skill-presentation.md §2.1.1）。
                foreach (var a in (t.Players ?? []).Concat(t.Enemies ?? []))
                    foreach (var d in a.Durations ?? [])
                    {
                        if (a.Id == null || d.DurationNumber is not { } dn) continue;
                        var key = d.Id?.ToString() ?? $"{a.Id}:{dn}";
                        if (!durationsSeen.Add(key)) continue;
                        var name = dn.ToString();
                        events.Add(new CombatEvent(key, tick, "DurationStart", null, a.Id.ToString(), null, [], null, null, 0, null, null,
                            "Physics", 0, false, false, true, name, "duration", Visuals.Mapping.SkillLooks.DurationSpell(name), 0));
                    }
                var frame = new CombatFrame(combatId, tick,
                    (t.Players ?? []).Select(a => ToActor(a, "player"))
                        .Concat((t.Enemies ?? []).Select(a => ToActor(a, "enemy"))).ToArray(),
                    (t.Projects ?? []).Select(a =>
                    {
                        var (spell, spin) = ProjectSpell(a, lastCaster, weapons, enemies);
                        return ToActor(a, "project") with { Spell = spell, Spin = spin };
                    }).ToArray(),
                    events.ToArray(),
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                events.Clear();
                frames++;
                var bytes = JsonSerializer.SerializeToUtf8Bytes(frame, ProtocolJson.Options).Length;
                totalBytes += bytes; maxBytes = Math.Max(maxBytes, bytes);
                if (onFrame != null) await onFrame(frame);
            }
            if (waitReady != null && frames > 0)
            {
                var wait = waitReady;
                waitReady = null;
                var held = Stopwatch.StartNew();
                await wait();
                Console.WriteLine($"[combat] 开场等待客户端就绪 {held.Elapsed.TotalSeconds:F1} s");
                pacer?.Rebase();
            }
        };

        using var map = new Map();
        await map.Initialize(request);
        await map.Play(ct);
        sw.Stop();

        var result = CombatResultDTO.New(map, request.CreatorId, false);
        var stats = new CombatStats(frames, sw.Elapsed.TotalMilliseconds,
            frames == 0 ? 0 : (double)totalBytes / frames, maxBytes,
            (GC.GetTotalPauseDuration() - gcBefore).TotalMilliseconds,
            GC.CollectionCount(0) - g0, GC.CollectionCount(1) - g1, GC.CollectionCount(2) - g2);
        return new CombatEnd(combatId, map.CombatResult.ToString(), lastTick, result.XP, result.EnemyKilled,
            result.TotalDamage.ToString(), stats);
    }

    /// <summary>DQQ 技能显示名 → 技能（投射物只带技能的显示名）。</summary>
    private static readonly Lazy<Dictionary<string, SkillProfile>> SkillsByName = new(() =>
        Enum.GetValues<EnumSkillNumber>()
            .Select(n => { try { return DQQPool.TryGet<SkillProfile, EnumSkillNumber>(n); } catch { return null; } })
            .Where(p => p?.SkillName != null)
            .GroupBy(p => p!.SkillName!).ToDictionary(g => g.Key, g => g.First()!));

    /// <summary>
    /// DQQ 投射物借用的 WoW 飞行法术（PLAN-0006 D）：按技能的演法——投射物技能（火球、奥术球、充能弹、刀光剑影……）
    /// 用 skills.json 的 projectile 法术（按元素）；射击、投掷的箭和标枪由施放时的法术画，这里为 0。
    /// </summary>
    private static (int Spell, bool Spin) ProjectSpell(TickLogActor a, Dictionary<string, string> lastCaster,
        Dictionary<string, string?> weapons, HashSet<string> enemies)
    {
        if (a.DisplayName == null || !SkillsByName.Value.TryGetValue(a.DisplayName, out var p)) return (0, false);
        var skill = p.ProfileNumber.ToString();
        var tags = p.OriginalTag.Select(t => t.ToString()).ToArray();
        var element = Visuals.Mapping.SkillLooks.ElementFor(tags, p.DamageType?.ToString(), "Physics");
        lastCaster.TryGetValue(skill, out var caster);
        var play = Visuals.Mapping.SkillLooks.Play(skill, tags, element,
            caster != null && weapons.TryGetValue(caster, out var w) ? w : null, caster != null && enemies.Contains(caster));
        if (play.Action is "shoot" or "wandShoot" or "throw") return (0, false);
        // 旋转技能的投射物（用户 2026-11-10）照常飞（按元素的飞行法术）；Spin 只是标出它碰到敌人时会放旋风。
        return (Visuals.Mapping.SkillLooks.SpellFor("projectile", skill, element), play.Action is "spin" or "spinChannel");
    }

    private static ActorState ToActor(TickLogActor a, string kind) => new(
        a.Id?.ToString() ?? $"{kind}:{a.X:F2},{a.Y:F2}", kind, a.X, a.Y, a.Radius, a.Angle,
        a.PercentageHP, a.CurrentLife, a.MaxLife, a.IsMoving, a.DisplayName, a.MobNumber?.ToString(),
        kind == "player" ? Visuals.WowLook.HeroModel : Visuals.WowLook.MobModel(a.MobNumber?.ToString()),
        a.TargetX, a.TargetY, kind == "enemy" && Visuals.WowLook.Oversize(a.MobNumber?.ToString()),
        kind == "enemy" ? Visuals.Mapping.MobLooks.Get(a.MobNumber?.ToString()).Dress : null);

    private static CombatEvent ToEvent(TickLogItem log, int tick, Dictionary<string, string?> weapons, HashSet<string> enemies)
    {
        var skill = log.Skill;
        var tags = skill?.SkillTags ?? (skill?.SkillNumber is EnumSkillNumber n
            ? DQQPool.TryGet<SkillProfile, EnumSkillNumber>(n)?.OriginalTag : null) ?? [];
        var d = log.Damage;
        var elements = new[] {
            ("Physics", d?.PhysicDamage ?? 0), ("Fire", d?.FireDamage ?? 0), ("Cold", d?.ColdDamage ?? 0),
            ("Lightning", d?.Lightning ?? 0), ("Chaos", d?.ChaosDamage ?? 0) };
        var top = elements.OrderByDescending(e => e.Item2).First();
        var amount = log.LogType == EnumLogType.HealingTaken ? (double)(log.Healing?.ActualHeal ?? 0) : (d?.DamagePoint ?? 0);
        var from = log.From?.Id?.ToString();
        // 伤害数字按实际伤害的元素；演出按技能的元素（标签优先），见 SkillLooks.ElementFor。
        var element = top.Item2 > 0 ? top.Item1 : "Physics";
        var tagNames = tags.Select(t => t.ToString()).ToArray();
        var defined = skill?.SkillNumber is EnumSkillNumber sn2 ? DQQPool.TryGet<SkillProfile, EnumSkillNumber>(sn2)?.DamageType?.ToString() : null;
        var look = Visuals.Mapping.SkillLooks.ElementFor(tagNames, defined, element);
        var play = Visuals.Mapping.SkillLooks.Play(skill?.SkillNumber?.ToString(), tagNames, look,
            from != null && weapons.TryGetValue(from, out var w) ? w : null, from != null && enemies.Contains(from));
        // 技能区域日志：在区域中心播区域特效（火球爆炸、尸爆……，用户 2026-11-07）。
        var shape = log.SkillShape?.SkillShape.ToString();
        var scale = 1.0;
        // 旋转技能的投射物（用户 2026-11-10）碰到敌人时的区域日志出自投射物、不是单位：由投射物自己施放旋风斩
        // （projectileSpin：客户端找到这个投射物，让它施放；投射物已经没了才在那里另放）。单位自己转的旋风斩已有动作，不另播。
        var fromProjectile = from != null && !weapons.ContainsKey(from) && !enemies.Contains(from);
        if (log.LogType == EnumLogType.SkillRangeAndTarget && play.Action is "spin" or "spinChannel" && fromProjectile)
            play = new Visuals.Mapping.SkillPlay("projectileSpin", Visuals.Mapping.SkillLooks.SpellFor("spin", skill?.SkillNumber?.ToString(), look), play.School);
        else if (log.LogType == EnumLogType.SkillRangeAndTarget)
        {
            scale = Visuals.Mapping.SkillLooks.AreaScale(shape, log.SkillShape?.Range ?? 0);
            play = Visuals.Mapping.SkillLooks.Area(shape, play.Action, look, tagNames, log.SkillShape?.Range ?? 0);
        }
        // 投射物技能的发射只播施法（飞行物由 DQQ 投射物画，见 ProjectSpell）。
        else if (play.Action is "projectile" or "channel" or "spinChannel" && log.LogType == EnumLogType.CastSkill) play = play with { Hit = false };
        // 引导技能（瓦解射线、冰霜射线、奥术洪流）也只播施法：光束由它的区域日志（射线、散射）画。
        return new CombatEvent(log.Id.ToString(), log.ActionTick ?? tick, log.LogType.ToString(),
            log.From?.Id?.ToString(), log.Target?.Id?.ToString(), skill?.SkillNumber?.ToString(),
            tagNames, skill?.RenderPhase, log.SkillShape?.SkillShape.ToString(),
            log.SkillShape?.Range ?? skill?.SkillArea ?? 0, log.SkillShape?.CenterX, log.SkillShape?.CenterY,
            element, amount, d?.IsCritical == true, d?.IsKilled == true, log.Success,
            skill?.DurationNumber?.ToString(), play.Action, play.Spell,
            log.LogType == EnumLogType.DamageTaken ? Visuals.Mapping.SkillLooks.School(element) : play.School, scale, play.Hit)
        {
            // 散射的射线：DQQ 的散射日志不带中心，出发点是日志的 From（爆炸的投射物或施放者），结束处是 Target。
            CenterX = play.Action == "ray" ? log.From?.X : log.SkillShape?.CenterX ?? (fromProjectile ? log.From?.X : null),
            CenterY = play.Action == "ray" ? log.From?.Y : log.SkillShape?.CenterY ?? (fromProjectile ? log.From?.Y : null),
            EndX = play.Action == "ray" ? log.Target?.X : null,
            EndY = play.Action == "ray" ? log.Target?.Y : null,
            Color = play.Color,
            Channel = play.Channel,
        };
    }
}
