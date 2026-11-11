using System.Text.Json;

namespace Exile.Runtime;

/// <summary>命令行与本地配置（config/dev.local.json，凭据不进仓库）。</summary>
public sealed class RuntimeOptions
{
    public int Port { get; set; } = 7710;
    public bool SelfTest { get; set; }
    /// <summary>D26：客户端断开后 runtime 退出；--stay 时不退出（调试用）。</summary>
    public bool Stay { get; set; }
    /// <summary>--mapping-report：写出 DQQ ↔ WoW 对应表的现状（tools/logs/mapping-report.md）后退出。</summary>
    public bool MappingReport { get; set; }
    /// <summary>--event-stats：后台重放录像目录里的每个录像，统计技能事件（PLAN-0006 D），写 tools/logs/event-stats.md。</summary>
    public bool EventStats { get; set; }
    /// <summary>--mapping：对应表配置目录（默认仓库的 config/mapping，找不到时用 runtime 旁的 mapping/）。</summary>
    public string? MappingDir { get; set; }
    public string? ConfigPath { get; set; }
    /// <summary>录像文件目录（导出、导入）；--replays 指定，默认 runtime 旁的 replays/。</summary>
    public string ReplaysDir { get; set; } = Path.Combine(AppContext.BaseDirectory, "replays");
    public string ApiBaseUrl { get; set; } = "https://pogapi2.idlepog.com/";
    /// <summary>
    /// --player-login：玩家在 WoW 登录界面输入 DQQ 账号（EoALauncher 演示版）。不读配置文件里的测试账号，
    /// 等客户端发 auth.login 再在线登录。
    /// </summary>
    public bool PlayerLogin { get; set; }
    public string? TestUser { get; set; }
    public string? TestPassword { get; set; }

    public static RuntimeOptions Parse(string[] args)
    {
        var o = new RuntimeOptions();
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--port": o.Port = int.Parse(args[++i]); break;
                case "--selftest": o.SelfTest = true; break;
                case "--stay": o.Stay = true; break;
                case "--mapping-report": o.MappingReport = true; break;
                case "--event-stats": o.EventStats = true; break;
                case "--mapping": o.MappingDir = args[++i]; break;
                case "--config": o.ConfigPath = args[++i]; break;
                case "--replays": o.ReplaysDir = args[++i]; break;
                case "--player-login": o.PlayerLogin = true; break;
                case "--api": o.ApiBaseUrl = args[++i]; break;
            }
        }
        if (o.PlayerLogin) return o; // 演示版：不读开发配置（测试账号、接口地址）
        o.ConfigPath ??= FindConfig();
        if (o.ConfigPath != null && File.Exists(o.ConfigPath))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(o.ConfigPath));
            if (doc.RootElement.TryGetProperty("dqq", out var dqq))
            {
                if (dqq.TryGetProperty("apiBaseUrl", out var url)) o.ApiBaseUrl = url.GetString() ?? o.ApiBaseUrl;
                if (dqq.TryGetProperty("testUser", out var u)) o.TestUser = u.GetString();
                if (dqq.TryGetProperty("testPassword", out var p)) o.TestPassword = p.GetString();
            }
        }
        return o;
    }

    private static string? FindConfig()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "config", "dev.local.json");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }
}
