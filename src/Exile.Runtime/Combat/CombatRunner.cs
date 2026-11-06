using DQQ.Components.Stages.Actors.Characters;
using DQQ.Components.Stages.Maps;
using DQQ.DTOs;
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
    public static async Task<CombatEnd> RunAsync(CharacterDTO character, EnumMapNumber mapNumber, bool watch,
        Func<CombatBegin, Task> onBegin, Func<CombatFrame, Task>? onFrame, CancellationToken ct)
    {
        var combatId = Guid.NewGuid().ToString("N");
        var replay = character.NewCombatReplayForPlay(mapNumber, 1);
        using var request = replay.GetReplayRequestDTO()
            ?? throw new InvalidOperationException("无法从种子生成战斗请求。");
        request.RenderType = EnumCombatRenderType.OfflinePlay;
        request.ShowTimeLine = true;
        request.SkipDelay = true; // 节奏由 TickPacer 控制（见 TickPacer 注释）
        var pacer = watch ? new TickPacer(DQQ.Consts.DQQGeneral.TickPerSecond) : null;
        request.IsWasm = false;
        request.CancellationToken = ct;

        var focusId = request.CreatorId?.ToString();
        await onBegin(new CombatBegin(combatId, mapNumber.ToString(), watch ? "watch" : "background", DQQ.Consts.DQQGeneral.TickPerSecond, focusId));

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
                if (log.LogType is not (EnumLogType.CastSkill or EnumLogType.TriggerSpell
                    or EnumLogType.SkillRangeAndTarget or EnumLogType.DamageTaken or EnumLogType.HealingTaken
                    or EnumLogType.Killed or EnumLogType.ActorIsDown)) continue;
                events.Add(ToEvent(log, p.Tick));
            }
            foreach (var t in p.TimeLine ?? [])
            {
                var tick = t.ActionTick ?? p.Tick;
                lastTick = Math.Max(lastTick, tick);
                var frame = new CombatFrame(combatId, tick,
                    (t.Players ?? []).Select(a => ToActor(a, "player"))
                        .Concat((t.Enemies ?? []).Select(a => ToActor(a, "enemy"))).ToArray(),
                    (t.Projects ?? []).Select(a => ToActor(a, "project")).ToArray(),
                    events.ToArray(),
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                events.Clear();
                frames++;
                var bytes = JsonSerializer.SerializeToUtf8Bytes(frame, ProtocolJson.Options).Length;
                totalBytes += bytes; maxBytes = Math.Max(maxBytes, bytes);
                if (onFrame != null) await onFrame(frame);
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

    private static ActorState ToActor(TickLogActor a, string kind) => new(
        a.Id?.ToString() ?? $"{kind}:{a.X:F2},{a.Y:F2}", kind, a.X, a.Y, a.Radius, a.Angle,
        a.PercentageHP, a.CurrentLife, a.MaxLife, a.IsMoving, a.DisplayName, a.MobNumber?.ToString());

    private static CombatEvent ToEvent(TickLogItem log, int tick)
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
        return new CombatEvent(log.Id.ToString(), log.ActionTick ?? tick, log.LogType.ToString(),
            log.From?.Id?.ToString(), log.Target?.Id?.ToString(), skill?.SkillNumber?.ToString(),
            tags.Select(t => t.ToString()).ToArray(), skill?.RenderPhase, log.SkillShape?.SkillShape.ToString(),
            log.SkillShape?.Range ?? skill?.SkillArea ?? 0, log.SkillShape?.CenterX, log.SkillShape?.CenterY,
            top.Item2 > 0 ? top.Item1 : "Physics", amount, d?.IsCritical == true, d?.IsKilled == true, log.Success);
    }
}
