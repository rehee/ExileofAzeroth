using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

namespace Exile.Launcher;

/// <summary>
/// EoALauncher（演示版，用户 2026-11-08）：放在 WoW 1.12.1 目录里代替 WoW.exe。
/// 1. 检查旁边有 WoW.exe 与 Data\；
/// 2. 把接在 exe 末尾的包（客户端、runtime、对应表、插件）解压到 Exile\（包变了才重新解压）；
/// 3. 启动 runtime（在线模式，玩家登录），再启动客户端：在 WoW 登录界面输入 DQQ 账号密码；
/// 4. 客户端退出后关掉 runtime。
/// 不包含、不修改任何暴雪文件。
/// </summary>
internal static class Program
{
    private const string ApiBaseUrl = "https://pogapi2.idlepog.com/";
    /// <summary>包尾：[zip][32 字节包标识][8 字节 zip 长度][8 字节魔数]。</summary>
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("EOAPAYL1");
    private const int IdLength = 32;

    [STAThread]
    private static int Main()
    {
        string? exile = null;
        try
        {
            var exe = Environment.ProcessPath ?? throw new InvalidOperationException("找不到启动器自己的路径。");
            var wow = Path.GetDirectoryName(exe)!;
            var data = Path.Combine(wow, "Data");
            if (!File.Exists(Path.Combine(wow, "WoW.exe")) || !Directory.Exists(data))
            {
                Error("请把 EoALauncher.exe 放进魔兽世界 1.12.1 的目录（和 WoW.exe、Data 文件夹放在一起）再运行。");
                return 1;
            }
            exile = Path.Combine(wow, "Exile");
            var logs = Path.Combine(exile, "logs");
            Directory.CreateDirectory(logs);
            Log(logs, $"启动器 {exe}");
            Install(exe, exile, logs);

            var port = FreePort();
            var runtimeExe = Path.Combine(exile, "runtime", "Exile.Runtime.exe");
            var clientExe = Path.Combine(exile, "client", "benilla.exe");
            var replays = Path.Combine(exile, "replays");
            Directory.CreateDirectory(replays);

            using var runtime = Start(runtimeExe, Path.Combine(exile, "runtime"),
                ["--port", port.ToString(), "--player-login", "--api", ApiBaseUrl,
                 "--mapping", Path.Combine(exile, "runtime", "mapping"), "--replays", replays],
                null, Path.Combine(logs, "runtime.log"));
            Log(logs, $"runtime 已启动 pid={runtime.Id} 端口 {port}");

            var env = new Dictionary<string, string>
            {
                ["WOW_DATA"] = data,
                ["EXILE_RUNTIME"] = $"ws://127.0.0.1:{port}/",
                ["EXILE_LOGIN"] = "dqq",
                ["BENILLA_HOME"] = Path.Combine(exile, "benilla-config"),
            };
            using var client = Start(clientExe, Path.Combine(exile, "client"), [], env, Path.Combine(logs, "client.log"));
            Log(logs, $"客户端已启动 pid={client.Id}");
            client.WaitForExit();
            Log(logs, $"客户端已退出（{client.ExitCode}）");

            // D26：客户端退出后 runtime 自己会退；稍等一下，没退就结束它。
            if (!runtime.WaitForExit(3000)) { try { runtime.Kill(true); } catch { } }
            Log(logs, "runtime 已结束");
            return 0;
        }
        catch (Exception e)
        {
            if (exile != null) { try { Log(Path.Combine(exile, "logs"), $"错误：{e}"); } catch { } }
            Error($"启动失败：{e.Message}" + (exile != null ? $"\n\n日志在 {Path.Combine(exile, "logs")}" : ""));
            return 1;
        }
    }

    /// <summary>读 exe 末尾的包；和 Exile\payload.id 不同就解压（玩家的设置 benilla-config、录像 replays、日志保留）。</summary>
    private static void Install(string exe, string exile, string logs)
    {
        using var file = File.OpenRead(exe);
        var tail = new byte[IdLength + 16];
        if (file.Length < tail.Length) throw new InvalidOperationException("启动器不完整：没有打包内容。");
        file.Seek(-tail.Length, SeekOrigin.End);
        file.ReadExactly(tail);
        if (!tail.AsSpan(IdLength + 8).SequenceEqual(Magic))
            throw new InvalidOperationException("启动器不完整：没有打包内容（请用 tools\\run-launcher-build.cmd 生成）。");
        var id = Encoding.ASCII.GetString(tail, 0, IdLength);
        var length = BitConverter.ToInt64(tail, IdLength);
        var idFile = Path.Combine(exile, "payload.id");
        var client = Path.Combine(exile, "client", "benilla.exe");
        var runtime = Path.Combine(exile, "runtime", "Exile.Runtime.exe");
        if (File.Exists(idFile) && File.ReadAllText(idFile).Trim() == id && File.Exists(client) && File.Exists(runtime))
        {
            Log(logs, $"包 {id} 已安装");
            return;
        }
        Log(logs, $"安装包 {id}（{length / 1024 / 1024} MB）");
        foreach (var dir in new[] { "client", "runtime" })
        {
            var path = Path.Combine(exile, dir);
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
        file.Seek(file.Length - tail.Length - length, SeekOrigin.Begin);
        using (var zip = new ZipArchive(new SubStream(file, length), ZipArchiveMode.Read))
        {
            var root = Path.GetFullPath(exile) + Path.DirectorySeparatorChar;
            foreach (var entry in zip.Entries)
            {
                var target = Path.GetFullPath(Path.Combine(exile, entry.FullName));
                if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;
                if (string.IsNullOrEmpty(entry.Name)) { Directory.CreateDirectory(target); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, overwrite: true);
            }
        }
        File.WriteAllText(idFile, id);
        Log(logs, "安装完成");
    }

    private static Process Start(string exe, string workDir, string[] args, Dictionary<string, string>? env, string log)
    {
        if (!File.Exists(exe)) throw new FileNotFoundException($"找不到 {exe}");
        var info = new ProcessStartInfo(exe)
        {
            WorkingDirectory = workDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args) info.ArgumentList.Add(a);
        if (env != null) foreach (var (k, v) in env) info.Environment[k] = v;
        // 开发用的变量不带进演示版。
        foreach (var k in new[] { "WOW_USER", "WOW_PASS", "WOW_CHAR", "EXILE_AUTOCOMBAT" }) info.Environment.Remove(k);
        var p = Process.Start(info) ?? throw new InvalidOperationException($"无法启动 {exe}");
        var writer = new StreamWriter(log, append: false, Encoding.UTF8) { AutoFlush = true };
        var gate = new object();
        void Write(string? line) { if (line != null) lock (gate) writer.WriteLine(line); }
        p.OutputDataReceived += (_, e) => Write(e.Data);
        p.ErrorDataReceived += (_, e) => Write(e.Data);
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        return p;
    }

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    private static void Log(string dir, string line) =>
        File.AppendAllText(Path.Combine(dir, "launcher.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {line}{Environment.NewLine}");

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    private static void Error(string text) => MessageBoxW(IntPtr.Zero, text, "Exile of Azeroth", 0x10);
}

/// <summary>exe 里 zip 的那一段，给 ZipArchive 读（可定位、只读）。</summary>
internal sealed class SubStream(Stream inner, long length) : Stream
{
    private readonly long start = inner.Position;
    private long position;
    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => length;
    public override long Position { get => position; set => position = value; }
    public override int Read(byte[] buffer, int offset, int count)
    {
        var n = (int)Math.Min(count, length - position);
        if (n <= 0) return 0;
        inner.Seek(start + position, SeekOrigin.Begin);
        n = inner.Read(buffer, offset, n);
        position += n;
        return n;
    }
    public override long Seek(long offset, SeekOrigin origin) => position = origin switch
    {
        SeekOrigin.Begin => offset,
        SeekOrigin.Current => position + offset,
        _ => length + offset,
    };
    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
