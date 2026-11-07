using DQQ.Components.Stages.Actors.Characters;
using DQQ.Enums;
using DQQ.Helper;
using Exile.Runtime.Combat;
using Exile.Runtime.Dqq;
using Exile.Runtime.Protocol;
using Exile.Runtime.Replays;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Exile.Runtime.Bridge;

/// <summary>
/// 把客户端消息转给 DQQ（UI 转接，D5）或战斗服务（D6）。
/// 每种 ui.* 消息对应一个 DQQ service 方法；以后新增 service 只需加一条分支。
/// </summary>
public sealed class MessageRouter
{
    private readonly DqqHost dqq;
    private readonly ReplayFiles files;
    private long seq;
    private CancellationTokenSource? combatCts;
    /// <summary>当前战斗的开场等待（combat.ready 到达时完成）。</summary>
    private TaskCompletionSource? ready;
    /// <summary>开场等待的上限：客户端没回 combat.ready 也照常开始。</summary>
    public static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(15);

    public MessageRouter(DqqHost dqq, ReplayFiles files)
    {
        this.dqq = dqq;
        this.files = files;
    }

    public async Task ServeAsync(BridgeConnection conn, CancellationToken ct)
    {
        await Send(conn, "hello", null, new { runtime = "Exile.Runtime", protocol = ProtocolJson.Version }, ct);
        while (conn.IsOpen && !ct.IsCancellationRequested)
        {
            var text = await conn.ReceiveAsync(ct);
            if (text == null) break;
            Envelope? msg = null;
            try
            {
                msg = JsonSerializer.Deserialize<Envelope>(text, ProtocolJson.Options);
                if (msg == null) continue;
                await Handle(conn, msg, ct);
            }
            catch (Exception e)
            {
                Console.WriteLine($"[router] 处理 {msg?.Type} 出错：{e}");
                await Send(conn, "error", msg?.Seq, new ErrorData(e.Message), ct);
            }
        }
        combatCts?.Cancel();
    }

    private async Task Handle(BridgeConnection conn, Envelope msg, CancellationToken ct)
    {
        switch (msg.Type)
        {
            case "hello":
                break;
            case "ui.character.list":
                Visuals.Mapping.MappingFiles.Refresh();
                await Send(conn, "ui.character.list.result", msg.Seq, (await dqq.GetCharacters()).Select(Summarize).ToArray(), ct);
                break;
            case "combat.start":
                Visuals.Mapping.MappingFiles.Refresh();
                var req = msg.Data?.Deserialize<CombatStartRequest>(ProtocolJson.Options) ?? new CombatStartRequest(null, null, null);
                StartCombat(conn, msg.Seq, req, ct);
                break;
            case "ui.replay.list":
                var lr = msg.Data?.Deserialize<ReplayListRequest>(ProtocolJson.Options);
                var replays = await dqq.GetReplays(Guid.TryParse(lr?.CharacterId, out var cid) ? cid : null);
                await Send(conn, "ui.replay.list.result", msg.Seq, replays.Select(r => new ReplaySummary(
                    r.Id.ToString(), r.PlayTime, r.Tick, r.CombatResult.ToString(), r.XP, r.TotalDamage,
                    r.Name, ReplayFiles.CreatorName(r))).ToArray(), ct);
                break;
            case "ui.replay.export":
                var er = msg.Data?.Deserialize<ReplayExportRequest>(ProtocolJson.Options);
                var toExport = await dqq.GetReplay(Guid.TryParse(er?.ReplayId, out var eid) ? eid : null, null)
                    ?? throw new InvalidOperationException("找不到这场录像。");
                var path = await files.Export(toExport);
                Console.WriteLine($"[replays] 导出 {path}");
                await Send(conn, "ui.replay.export.result", msg.Seq, new ReplayExportResult(Path.GetFileName(path), path), ct);
                break;
            case "ui.replay.files":
                await Send(conn, "ui.replay.files.result", msg.Seq, (await files.List()).Select(f => new ReplayFileSummary(
                    f.File, f.Replay.Id.ToString(), f.Replay.PlayTime, f.Replay.Tick, f.Replay.CombatResult.ToString(),
                    f.Replay.XP, f.Replay.TotalDamage, f.Replay.Name, ReplayFiles.CreatorName(f.Replay))).ToArray(), ct);
                break;
            case "ui.mapping.mobs":
                Visuals.Mapping.MappingFiles.Refresh();
                // 怪物外观表的审阅：客户端 `.lineup mobs` 把每个怪物排成一排（PLAN-0006 B）。
                await Send(conn, "ui.mapping.mobs.result", msg.Seq, Visuals.Mapping.MobLooks.Table
                    .Select(kv => new MobLookEntry(kv.Key, kv.Value.DisplayId, Visuals.Mapping.MobLooks.IsBoss(kv.Key), kv.Value.Dress)).ToArray(), ct);
                break;
            case "combat.prepare":
                // 先问战场（PLAN-0006 E）：客户端传送到那里，加载完再发 combat.replay。
                Visuals.Mapping.MappingFiles.Refresh();
                var pr = msg.Data?.Deserialize<CombatPrepareRequest>(ProtocolJson.Options) ?? new CombatPrepareRequest(null, null);
                await Send(conn, "combat.prepare.result", msg.Seq, await Prepare(pr), ct);
                break;
            case "combat.replay":
                Visuals.Mapping.MappingFiles.Refresh();
                var rr = msg.Data?.Deserialize<CombatReplayRequest>(ProtocolJson.Options) ?? new CombatReplayRequest(null, null, null);
                StartReplay(conn, msg.Seq, rr, ct);
                break;
            case "combat.ready":
                ready?.TrySetResult();
                break;
            case "combat.stop":
                combatCts?.Cancel();
                break;
            default:
                if (msg.Type.StartsWith("cmd.", StringComparison.Ordinal))
                {
                    // D7：玩家指令只留口子，暂不转给 DQQ。
                    await Send(conn, "cmd.ack", msg.Seq, new { accepted = false, reason = "reserved" }, ct);
                    break;
                }
                await Send(conn, "error", msg.Seq, new ErrorData($"未知消息类型 {msg.Type}"), ct);
                break;
        }
    }

    private void StartCombat(BridgeConnection conn, long re, CombatStartRequest req, CancellationToken ct)
    {
        combatCts?.Cancel();
        combatCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = combatCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                var chars = await dqq.GetCharacters();
                var character = chars.FirstOrDefault(c => c.Id?.ToString() == req.CharacterId) ?? chars.FirstOrDefault()
                    ?? throw new InvalidOperationException("账号下没有角色。");
                var map = Enum.TryParse<EnumMapNumber>(req.Map, out var m) ? m : EnumMapNumber.Map_1_1;
                var watch = req.Mode != "background";
                var end = await CombatRunner.RunAsync(character, map, watch,
                    b => Send(conn, "combat.begin", re, b, token),
                    watch ? f => Send(conn, "combat.frame", null, f, token) : null,
                    token);
                await Send(conn, "combat.end", re, end, token);
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                Console.WriteLine($"[combat] 出错：{e}");
                await Send(conn, "error", re, new ErrorData(e.Message), CancellationToken.None);
            }
        }, token);
    }

    /// <summary>回放一场录像（D28）；找不到录像时用该角色开一场新战斗，便于验证。</summary>
    /// <summary>录像（或没有录像时的新战斗）的 DQQ 地图 → 战场。</summary>
    private async Task<Battlefield?> Prepare(CombatPrepareRequest req)
    {
        try
        {
            var charId = Guid.TryParse(req.CharacterId, out var c) ? c : (Guid?)null;
            var replay = !string.IsNullOrEmpty(req.File)
                ? await files.Load(req.File)
                : await dqq.GetReplay(Guid.TryParse(req.ReplayId, out var r) ? r : null, charId);
            using var request = replay?.GetReplayRequestDTO();
            var map = request?.MapNumber ?? EnumMapNumber.Map_1_1;
            var field = !string.IsNullOrEmpty(req.Scene) && Visuals.Mapping.MappingFiles.Scenes.TryGetValue(req.Scene, out var forced)
                ? forced with { Map = map.ToString() }
                : Visuals.Mapping.MappingFiles.FieldFor(map.ToString());
            if (!string.IsNullOrEmpty(req.Scene) && field?.Scene != req.Scene)
                Console.WriteLine($"[combat] 没有场景 {req.Scene}，按录像的地图选战场");
            Console.WriteLine($"[combat] 战场：DQQ 地图 {map} → {field?.Scene ?? "（默认）"}");
            return field;
        }
        catch (Exception e)
        {
            Console.WriteLine($"[combat] 取战场出错，用默认战场：{e.Message}");
            return null;
        }
    }

    private void StartReplay(BridgeConnection conn, long re, CombatReplayRequest req, CancellationToken ct)
    {
        combatCts?.Cancel();
        combatCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = combatCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                var charId = Guid.TryParse(req.CharacterId, out var c) ? c : (Guid?)null;
                var replay = !string.IsNullOrEmpty(req.File)
                    ? await files.Load(req.File)
                    : await dqq.GetReplay(Guid.TryParse(req.ReplayId, out var r) ? r : null, charId);
                var watch = req.Mode != "background";
                var hold = watch && req.Hold ? new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) : null;
                ready = hold;
                Func<Task>? waitReady = hold == null ? null
                    : () => hold.Task.WaitAsync(ReadyTimeout, token).ContinueWith(_ => { }, CancellationToken.None);
                Func<CombatBegin, Task> begin = b => Send(conn, "combat.begin", re, b, token);
                Func<CombatFrame, Task>? frame = watch ? f => Send(conn, "combat.frame", null, f, token) : null;
                CombatEnd end;
                if (replay != null)
                {
                    Console.WriteLine($"[combat] 回放录像 {replay.Id}（{replay.PlayTime}，{replay.Tick} tick{(string.IsNullOrEmpty(req.File) ? "" : $"，文件 {req.File}")}）");
                    end = await CombatRunner.RunAsync(replay, watch, begin, frame, token, waitReady);
                }
                else
                {
                    Console.WriteLine("[combat] 没有录像，改用角色开一场新战斗");
                    var chars = await dqq.GetCharacters();
                    var character = chars.FirstOrDefault(x => x.Id == charId) ?? chars.First();
                    end = await CombatRunner.RunAsync(character, EnumMapNumber.Map_1_1, watch, begin, frame, token, waitReady);
                }
                await Send(conn, "combat.end", re, end, token);
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                Console.WriteLine($"[combat] 回放出错：{e}");
                await Send(conn, "error", re, new ErrorData(e.Message), CancellationToken.None);
            }
        }, token);
    }

    public static CharacterSummary Summarize(CharacterDTO c) => new(
        c.Id?.ToString() ?? "", c.DisplayName ?? "", Convert.ToInt32(c.Level), c.CombatArcheTypeNumber.ToString(),
        (c.Equips ?? []).Where(p => p.Value != null)
            .ToDictionary(p => p.Key.ToString(), p => new EquipSummary(p.Value!.Profile?.ProfileNumber.ToString() ?? "", p.Value.Profile?.Name)),
        Visuals.Mapping.MappingFiles.HeroLook.Race, Visuals.WowLook.WowClass(c.CombatArcheTypeNumber.ToString()),
        Visuals.Mapping.MappingFiles.HeroLook.Gender, Visuals.WowLook.DisplayHumanFemale,
        // 英雄的统一外观穿上 DQQ 装备（config/mapping/equipment.json）：客户端用在自由模式与回放里的本体上。
        Visuals.Mapping.MappingFiles.HeroDress(c.Equips));

    private Task Send(BridgeConnection conn, string type, long? re, object? data, CancellationToken ct)
    {
        var env = new Envelope(ProtocolJson.Version, Interlocked.Increment(ref seq), type, re,
            data == null ? null : JsonSerializer.SerializeToNode(data, data.GetType(), ProtocolJson.Options));
        return conn.SendAsync(JsonSerializer.SerializeToUtf8Bytes(env, ProtocolJson.Options), ct);
    }
}
