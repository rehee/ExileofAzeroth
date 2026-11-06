using DQQ.Entities;
using DQQ.Helper;

namespace Exile.Runtime.Replays;

/// <summary>
/// 战斗录像文件：与 DQQ 网页版的导出/导入同一格式——<c>CombatReplayEntity</c> 的 JSON 再 Base64，一行文本
/// （DQQ <c>ExportReplay</c>：<c>Replay.ToJson().ToBase64()</c>；<c>PlayReplay</c>：<c>FromBase64().FromJson&lt;CombatReplayEntity&gt;()</c>）。
/// 文件名 <c>dqq-replay-&lt;录像 id 前 8 位&gt;.txt</c>。导入的文件按种子重新模拟，无需联网，也不要求是当前账号的角色。
/// </summary>
public sealed class ReplayFiles
{
    public string Directory { get; }

    public ReplayFiles(string directory) => Directory = Path.GetFullPath(directory);

    public static string FileName(CombatReplayEntity replay) => $"dqq-replay-{replay.Id.ToString()[..8]}.txt";

    /// <summary>导出一场录像，返回完整路径；同名文件覆盖。</summary>
    public async Task<string> Export(CombatReplayEntity replay)
    {
        System.IO.Directory.CreateDirectory(Directory);
        var text = replay.ToJson().ToBase64() ?? throw new InvalidOperationException("录像为空，无法导出。");
        var path = Path.Combine(Directory, FileName(replay));
        await File.WriteAllTextAsync(path, text + Environment.NewLine);
        return path;
    }

    /// <summary>目录里的录像文件，新的在前；读不出的文件跳过。</summary>
    public async Task<(string File, CombatReplayEntity Replay)[]> List()
    {
        if (!System.IO.Directory.Exists(Directory)) return [];
        var list = new List<(string, CombatReplayEntity, DateTime)>();
        foreach (var path in System.IO.Directory.GetFiles(Directory, "*.txt"))
        {
            var replay = await TryRead(path);
            if (replay != null) list.Add((Path.GetFileName(path), replay, File.GetLastWriteTimeUtc(path)));
        }
        return list.OrderByDescending(x => x.Item3).Select(x => (x.Item1, x.Item2)).ToArray();
    }

    /// <summary>按文件名读取（只认目录内的文件名，不接受路径）。</summary>
    public async Task<CombatReplayEntity> Load(string file)
    {
        var name = Path.GetFileName(file);
        var path = Path.Combine(Directory, name);
        return await TryRead(path) ?? throw new InvalidOperationException($"读不出录像文件 {name}");
    }

    /// <summary>
    /// 录像里创建这场战斗的角色名：<c>ReplayDetail</c>（战斗请求的 JSON）里 <c>FriendDTOs</c> 中 Id 等于
    /// <c>CreateActorId</c> 的那个的 <c>DisplayName</c>，找不到时取第一个。导入别人的录像时用它显示是谁的战斗。
    /// </summary>
    public static string? CreatorName(CombatReplayEntity replay)
    {
        try
        {
            if (string.IsNullOrEmpty(replay.ReplayDetail)) return null;
            using var doc = System.Text.Json.JsonDocument.Parse(replay.ReplayDetail);
            if (!doc.RootElement.TryGetProperty("FriendDTOs", out var friends)
                || friends.ValueKind != System.Text.Json.JsonValueKind.Array) return null;
            string? first = null;
            foreach (var f in friends.EnumerateArray())
            {
                var name = f.TryGetProperty("DisplayName", out var n) ? n.GetString() : null;
                first ??= name;
                if (f.TryGetProperty("Id", out var id) && id.GetString() == replay.CreateActorId?.ToString()) return name;
            }
            return first;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static async Task<CombatReplayEntity?> TryRead(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var text = (await File.ReadAllTextAsync(path)).Trim();
            return text.FromBase64()?.FromJson<CombatReplayEntity>();
        }
        catch (Exception e)
        {
            Console.WriteLine($"[replays] 跳过 {Path.GetFileName(path)}：{e.Message}");
            return null;
        }
    }
}
