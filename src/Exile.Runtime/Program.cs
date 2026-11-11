using DQQ.Enums;
using Exile.Runtime;
using Exile.Runtime.Bridge;
using Exile.Runtime.Combat;
using Exile.Runtime.Dqq;
using Exile.Runtime.Protocol;
using System.Runtime;
using System.Text;
using System.Text.Json;

Console.OutputEncoding = Encoding.UTF8;
GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency; // D9
TickPacer.RaiseTimerResolution();
var options = RuntimeOptions.Parse(args);
Console.WriteLine($"[runtime] .NET {Environment.Version}  api={options.ApiBaseUrl}  config={options.ConfigPath ?? "(无)"}");

Exile.Runtime.Visuals.Mapping.MappingFiles.Init(options.MappingDir);
if (options.MappingReport)
{
    // 只读枚举与表，不登录 DQQ。仓库里写到 tools/logs/，否则写在 runtime 旁。
    var root = options.ConfigPath != null ? Path.GetDirectoryName(Path.GetDirectoryName(options.ConfigPath)) : null;
    var dir = root != null ? Path.Combine(root, "tools", "logs") : AppContext.BaseDirectory;
    Directory.CreateDirectory(dir);
    var path = Path.Combine(dir, "mapping-report.md");
    File.WriteAllText(path, Exile.Runtime.Visuals.Mapping.MappingReport.Build());
    Console.WriteLine($"[runtime] 对应表现状已写到 {path}");
    return 0;
}

await using var dqq = DqqHost.Create(options.ApiBaseUrl);
Console.WriteLine("[runtime] DQQ service 已注册");

if (options.PlayerLogin) Console.WriteLine("[runtime] 等玩家在登录界面输入 DQQ 账号（auth.login）");
else if (options.TestUser != null && options.TestPassword != null)
{
    await dqq.LoginOnline(options.TestUser, options.TestPassword);
    Console.WriteLine($"[runtime] 在线登录成功：{options.TestUser}");
}
else Console.WriteLine("[runtime] 未配置测试账号，跳过登录");

if (options.SelfTest) return await SelfTest.RunAsync(dqq);
if (options.EventStats)
{
    var root = options.ConfigPath != null ? Path.GetDirectoryName(Path.GetDirectoryName(options.ConfigPath)) : null;
    var dir = root != null ? Path.Combine(root, "tools", "logs") : AppContext.BaseDirectory;
    Directory.CreateDirectory(dir);
    var path = Path.Combine(dir, "event-stats.md");
    File.WriteAllText(path, await EventStats.Build(new Exile.Runtime.Replays.ReplayFiles(options.ReplaysDir)));
    Console.WriteLine($"[runtime] 技能事件统计已写到 {path}");
    return 0;
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
var router = new MessageRouter(dqq, new Exile.Runtime.Replays.ReplayFiles(options.ReplaysDir));
Console.WriteLine($"[runtime] 录像文件目录 {Path.GetFullPath(options.ReplaysDir)}");
var active = 0; var everConnected = false;
var server = new BridgeServer(options.Port, async conn =>
{
    Interlocked.Increment(ref active); everConnected = true;
    Console.WriteLine("[bridge] 客户端已连接");
    try { await router.ServeAsync(conn, cts.Token); }
    finally
    {
        Console.WriteLine("[bridge] 客户端已断开");
        // D26：客户端退出时 runtime 也退出。
        if (Interlocked.Decrement(ref active) == 0 && everConnected && !options.Stay) cts.Cancel();
    }
});
await server.RunAsync(cts.Token);
Console.WriteLine("[runtime] 退出");
return 0;

/// <summary>不开客户端的自检：登录 → 角色列表 → 后台跑一场 + 实时跑一场，输出统计（PLAN-0003 风险 1）。</summary>
static class SelfTest
{
    public static async Task<int> RunAsync(DqqHost dqq)
    {
        var logDir = Path.Combine(AppContext.BaseDirectory, "logs");
        Directory.CreateDirectory(logDir);
        var report = new Dictionary<string, object?>();
        try
        {
            var chars = await dqq.GetCharacters();
            Console.WriteLine($"[selftest] 角色 {chars.Length} 个");
            var summaries = chars.Select(MessageRouter.Summarize).ToArray();
            foreach (var c in summaries) Console.WriteLine($"  - {c.Name} Lv{c.Level} {c.Archetype} 装备 {c.Equips.Count} 件");
            report["characters"] = summaries;
            if (chars.Length == 0) throw new InvalidOperationException("账号下没有角色，无法测试战斗。");

            var background = await CombatRunner.RunAsync(chars[0], EnumMapNumber.Map_1_1, watch: false,
                b => Task.CompletedTask, null, CancellationToken.None);
            Console.WriteLine($"[selftest] 后台战斗：{JsonSerializer.Serialize(background, ProtocolJson.Options)}");
            report["background"] = background;

            // 实时（watch）模式：记录前 600 帧及到达间隔，检查 30 tick/s 节奏是否平稳。
            var path = Path.Combine(logDir, "selftest-frames.jsonl");
            await using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
            var intervals = new List<double>(); long last = 0;
            using var watchCts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            CombatEnd? watch = null;
            try
            {
                watch = await CombatRunner.RunAsync(chars[0], EnumMapNumber.Map_1_1, watch: true, b => Task.CompletedTask,
                    async f =>
                    {
                        var now = System.Diagnostics.Stopwatch.GetTimestamp();
                        if (last != 0) intervals.Add((now - last) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
                        last = now;
                        if (intervals.Count < 600) await writer.WriteLineAsync(JsonSerializer.Serialize(f, ProtocolJson.Options));
                    }, watchCts.Token);
            }
            catch (OperationCanceledException) { Console.WriteLine("[selftest] 实时战斗 25 秒后截止"); }
            if (intervals.Count > 0)
            {
                var sorted = intervals.OrderBy(x => x).ToArray();
                var pacing = new
                {
                    frames = intervals.Count + 1,
                    avgMs = intervals.Average(),
                    p50Ms = sorted[sorted.Length / 2],
                    p99Ms = sorted[(int)(sorted.Length * 0.99)],
                    maxMs = sorted[^1],
                };
                Console.WriteLine($"[selftest] 实时节奏：{JsonSerializer.Serialize(pacing)}");
                report["watchPacing"] = pacing;
            }
            report["watch"] = watch;
            report["bridge"] = await BridgeLoopback(dqq);
            report["ok"] = true;
            Console.WriteLine($"[selftest] 帧样本：{path}");
        }
        catch (Exception e)
        {
            Console.WriteLine($"[selftest] 失败：{e}");
            report["ok"] = false; report["error"] = e.ToString();
        }
        var reportPath = Path.Combine(logDir, "selftest.json");
        Console.WriteLine($"[selftest] 结论：{(report["ok"] is true ? "通过" : "失败")}");
        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions(ProtocolJson.Options) { WriteIndented = true }));
        Console.WriteLine($"[selftest] 报告：{reportPath}");
        return report["ok"] is true ? 0 : 1;
    }

    /// <summary>同进程 WebSocket 回环：实测桥接协议的端到端延迟与到达间隔（D9 验收）。</summary>
    private static async Task<object> BridgeLoopback(DqqHost dqq)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var router = new MessageRouter(dqq, new Exile.Runtime.Replays.ReplayFiles(Path.Combine(Path.GetTempPath(), "exile-selftest-replays")));
        var server = new BridgeServer(0, conn => router.ServeAsync(conn, cts.Token));
        server.Start();
        _ = server.RunAsync(cts.Token);

        using var ws = new System.Net.WebSockets.ClientWebSocket();
        await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{server.Port}/"), cts.Token);
        var buffer = new byte[256 * 1024];
        async Task<JsonElement> Receive()
        {
            using var ms = new MemoryStream();
            while (true)
            {
                var r = await ws.ReceiveAsync(buffer, cts.Token);
                ms.Write(buffer, 0, r.Count);
                if (r.EndOfMessage) return JsonDocument.Parse(ms.ToArray()).RootElement.Clone();
            }
        }
        Task Send(string type, object? data, long seq) => ws.SendAsync(
            JsonSerializer.SerializeToUtf8Bytes(new { v = 1, seq, type, data }, ProtocolJson.Options),
            System.Net.WebSockets.WebSocketMessageType.Text, true, cts.Token);

        var hello = await Receive();
        Console.WriteLine($"[bridge-test] 收到 {hello.GetProperty("type").GetString()}");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await Send("ui.character.list", null, 1);
        var list = await Receive();
        var listMs = sw.Elapsed.TotalMilliseconds;
        Console.WriteLine($"[bridge-test] {list.GetProperty("type").GetString()} 往返 {listMs:F1} ms");

        await Send("combat.start", new { map = "Map_1_1", mode = "watch" }, 2);
        var latencies = new List<double>(); var gaps = new List<double>(); long last = 0; var begin = false;
        while (latencies.Count < 300)
        {
            var m = await Receive();
            var type = m.GetProperty("type").GetString();
            if (type == "combat.begin") begin = true;
            if (type == "error") throw new InvalidOperationException(m.GetRawText());
            if (type != "combat.frame") continue;
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            latencies.Add(now - m.GetProperty("data").GetProperty("sentAtMs").GetInt64());
            var ts = System.Diagnostics.Stopwatch.GetTimestamp();
            if (last != 0) gaps.Add((ts - last) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
            last = ts;
        }
        await Send("combat.stop", null, 3);
        await ws.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
        cts.Cancel();
        var g = gaps.OrderBy(x => x).ToArray(); var l = latencies.OrderBy(x => x).ToArray();
        var result = new
        {
            characterListRoundTripMs = listMs,
            combatBegin = begin,
            frames = latencies.Count,
            latencyAvgMs = l.Average(), latencyP99Ms = l[(int)(l.Length * 0.99)], latencyMaxMs = l[^1],
            gapAvgMs = g.Average(), gapP50Ms = g[g.Length / 2], gapP99Ms = g[(int)(g.Length * 0.99)], gapMaxMs = g[^1],
        };
        Console.WriteLine($"[bridge-test] {JsonSerializer.Serialize(result)}");
        return result;
    }
}
