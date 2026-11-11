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
            case "auth.login":
                // 玩家在 WoW 登录界面输入的 DQQ 账号（EoALauncher）：在线登录。密码只在内存里，不记日志、不落盘。
                var al = msg.Data?.Deserialize<AuthLoginRequest>(ProtocolJson.Options);
                AuthLoginResult answer;
                try
                {
                    await dqq.LoginOnline(al?.User ?? "", al?.Password ?? "");
                    Console.WriteLine($"[auth] 在线登录成功：{al?.User}");
                    answer = new AuthLoginResult(true, null, null);
                }
                catch (InvalidOperationException e)
                {
                    Console.WriteLine($"[auth] 登录被拒：{al?.User}（{e.Message}）");
                    answer = new AuthLoginResult(false, "credentials", e.Message);
                }
                catch (Exception e)
                {
                    Console.WriteLine($"[auth] 连不上 DQQ 服务器：{e.Message}");
                    answer = new AuthLoginResult(false, "network", e.Message);
                }
                await Send(conn, "auth.login.result", msg.Seq, answer, ct);
                break;
            case "ui.character.list":
                Visuals.Mapping.MappingFiles.Refresh();
                // 显示 / 隐藏角色自己的外观（PLAN-0011：幻化与技能皮肤，插件的 Skins 按钮，用户 2026-11-11）。
                if (msg.Data is JsonObject lo && lo["skins"] is JsonValue sv && sv.TryGetValue<bool>(out var skinsOn))
                {
                    Visuals.Mapping.CharConfig.Enabled = skinsOn;
                    Console.WriteLine($"[char-config] 幻化与技能皮肤：{(skinsOn ? "显示" : "隐藏")}");
                }
                await Send(conn, "ui.character.list.result", msg.Seq, (await dqq.GetCharacters()).Select(c => Summarize(c)).ToArray(), ct);
                break;
            // 梳妆台与幻化（PLAN-0011）：改动先预览（不存），保存时写回 DQQ，取消就丢掉。回答都是只含这个角色的角色列表，
            // 客户端照 Reload Gear 的办法穿上。
            case "ui.character.edit":
            case "ui.character.save":
            case "ui.character.cancel":
                Visuals.Mapping.MappingFiles.Refresh();
                var eo = msg.Data as JsonObject;
                var editId = eo?["characterId"]?.GetValue<string>();
                if (!Guid.TryParse(editId, out var editGuid)) throw new InvalidOperationException("角色 id 不对。");
                // 编辑时显示角色自己的外观（插件的 Skins 按钮不管）。
                Visuals.Mapping.CharConfig.Enabled = true;
                // 预览用开始编辑时取的那份角色（不每次都问服务器）；保存、取消时按 id 重新取。
                var who = (msg.Type == "ui.character.edit" ? Visuals.Mapping.Wardrobe.Character(editId) : null)
                    ?? await dqq.GetCharacter(editGuid)
                    ?? throw new InvalidOperationException("找不到这个角色。");
                CharacterSummary shown;
                if (msg.Type == "ui.character.edit")
                {
                    var cfg = Visuals.Mapping.Wardrobe.Apply(who, eo?["change"] as JsonObject ?? new JsonObject());
                    shown = Summarize(who, cfg);
                }
                else if (msg.Type == "ui.character.save")
                {
                    if (Visuals.Mapping.Wardrobe.Pending(who) is { } json)
                    {
                        // 先保存、等它完成，再按 id 取回角色，按取回来的配置更新人物（用户 2026-11-11）。
                        await dqq.SaveCharConfig(editGuid, json);
                        Console.WriteLine($"[wardrobe] 已保存 {who.DisplayName} 的 WoWCharConfig（{json.Length} 字）：{json}");
                        Visuals.Mapping.Wardrobe.Drop(editId);
                        who = await dqq.GetCharacter(editGuid) ?? who;
                        Console.WriteLine($"[wardrobe] 取回 {who.DisplayName}：{who.WoWCharConfig ?? "（空）"}");
                        if (who.WoWCharConfig != json)
                        {
                            Console.WriteLine($"[wardrobe] 保存后取回的 WoWCharConfig 不一致：{(who.WoWCharConfig == null ? "空" : who.WoWCharConfig)}");
                            await Send(conn, "error", msg.Seq, new ErrorData(
                                "Saved, but DQQ returned the character without it (is the DQQ server updated?)."), ct);
                        }
                    }
                    shown = Summarize(who);
                }
                else
                {
                    Visuals.Mapping.Wardrobe.Drop(editId);
                    shown = Summarize(who);
                }
                await Send(conn, msg.Type + ".result", msg.Seq, new[] { shown }, ct);
                break;
            // 插件的 Skins 按钮的状态（插件每次加载时同步一次，不重新穿）。
            case "ui.skins":
                if (msg.Data is JsonObject so && so["skins"] is JsonValue sv2 && sv2.TryGetValue<bool>(out var on2))
                {
                    Visuals.Mapping.CharConfig.Enabled = on2;
                    Console.WriteLine($"[char-config] 幻化与技能皮肤：{(on2 ? "显示" : "隐藏")}（插件同步）");
                }
                break;
            // 技能皮肤窗口（PLAN-0011）：能换皮肤的技能与选项。
            case "ui.skill.skins":
                Visuals.Mapping.MappingFiles.Refresh();
                var sid = (msg.Data as JsonObject)?["characterId"]?.GetValue<string>();
                if (!Guid.TryParse(sid, out var sguid)) throw new InvalidOperationException("角色 id 不对。");
                var schar = Visuals.Mapping.Wardrobe.Character(sid) ?? await dqq.GetCharacter(sguid)
                    ?? throw new InvalidOperationException("找不到这个角色。");
                await Send(conn, "ui.skill.skins.result", msg.Seq, Visuals.Mapping.Wardrobe.SkinOptions(schar), ct);
                break;
            case "ui.wow.items":
                Visuals.Mapping.MappingFiles.Refresh();
                await Send(conn, "ui.wow.items.result", msg.Seq, Visuals.Mapping.Wardrobe.NamedItems(), ct);
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

    public static CharacterSummary Summarize(CharacterDTO c) => Summarize(c, Visuals.Mapping.CharConfig.For(c));

    /// <param name="cfg">套用的角色配置（梳妆台、幻化的预览用正在改的那份）。</param>
    public static CharacterSummary Summarize(CharacterDTO c, Visuals.Mapping.CharConfig? cfg)
    {
        // 英雄的统一外观穿上 DQQ 装备（config/mapping/equipment.json），再按角色自己的 WoWCharConfig（PLAN-0011）换长相、幻化：
        // 客户端用在选人界面、自由模式与回放里的本体上。
        var dress = Visuals.Mapping.MappingFiles.HeroDress(c.Equips, cfg);
        return new(
            c.Id?.ToString() ?? "", c.DisplayName ?? "", Convert.ToInt32(c.Level), c.CombatArcheTypeNumber.ToString(),
            (c.Equips ?? []).Where(p => p.Value != null)
                .ToDictionary(p => p.Key.ToString(), p => new EquipSummary(p.Value!.Profile?.ProfileNumber.ToString() ?? "", p.Value.Profile?.Name)),
            dress.Race, Visuals.WowLook.WowClass(c.CombatArcheTypeNumber.ToString()),
            dress.Gender, Visuals.WowLook.DisplayHumanFemale, dress);
    }

    private Task Send(BridgeConnection conn, string type, long? re, object? data, CancellationToken ct)
    {
        var env = new Envelope(ProtocolJson.Version, Interlocked.Increment(ref seq), type, re,
            data == null ? null : JsonSerializer.SerializeToNode(data, data.GetType(), ProtocolJson.Options));
        return conn.SendAsync(JsonSerializer.SerializeToUtf8Bytes(env, ProtocolJson.Options), ct);
    }
}
