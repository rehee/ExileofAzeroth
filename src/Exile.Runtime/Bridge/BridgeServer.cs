using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;

namespace Exile.Runtime.Bridge;

/// <summary>
/// 最小 WebSocket 服务端（只监听 127.0.0.1，D9）。不依赖 ASP.NET，避免与 DQQ 预编译依赖的版本冲突。
/// 开启 TCP_NODELAY，降低逐帧推送的延迟。
/// </summary>
public sealed class BridgeServer
{
    private readonly TcpListener listener;
    private readonly Func<BridgeConnection, Task> onConnection;

    public BridgeServer(int port, Func<BridgeConnection, Task> onConnection)
    {
        listener = new TcpListener(IPAddress.Loopback, port);
        this.onConnection = onConnection;
    }

    private bool started;
    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

    /// <summary>开始监听（port 为 0 时由系统分配，Start 后可读 Port）。</summary>
    public void Start()
    {
        if (started) return;
        listener.Start();
        started = true;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        Start();
        Console.WriteLine($"[bridge] 监听 ws://127.0.0.1:{Port}/");
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(ct);
                client.NoDelay = true;
                _ = Task.Run(() => HandleAsync(client, ct), ct);
            }
        }
        catch (OperationCanceledException) { }
        finally { listener.Stop(); }
    }

    private async Task HandleAsync(TcpClient client, CancellationToken ct)
    {
        using var _ = client;
        var stream = client.GetStream();
        try
        {
            var key = await ReadHandshake(stream, ct);
            if (key == null) return;
            var accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
            var response = "HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n" +
                           $"Sec-WebSocket-Accept: {accept}\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(response), ct);
            using var ws = WebSocket.CreateFromStream(stream, new WebSocketCreationOptions
            {
                IsServer = true,
                KeepAliveInterval = TimeSpan.FromSeconds(30),
            });
            await onConnection(new BridgeConnection(ws));
        }
        catch (Exception e) when (e is IOException or WebSocketException or OperationCanceledException)
        {
            Console.WriteLine($"[bridge] 连接结束：{e.Message}");
        }
    }

    private static async Task<string?> ReadHandshake(NetworkStream stream, CancellationToken ct)
    {
        var buffer = new byte[8192];
        var total = 0;
        while (total < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(total), ct);
            if (n == 0) return null;
            total += n;
            var text = Encoding.ASCII.GetString(buffer, 0, total);
            var end = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if (end < 0) continue;
            foreach (var line in text[..end].Split("\r\n"))
            {
                var i = line.IndexOf(':');
                if (i > 0 && line[..i].Trim().Equals("Sec-WebSocket-Key", StringComparison.OrdinalIgnoreCase))
                    return line[(i + 1)..].Trim();
            }
            return null;
        }
        return null;
    }
}

/// <summary>一条客户端连接：串行发送，逐条接收文本消息。</summary>
public sealed class BridgeConnection
{
    private readonly WebSocket ws;
    private readonly SemaphoreSlim sendLock = new(1, 1);
    public BridgeConnection(WebSocket ws) => this.ws = ws;
    public bool IsOpen => ws.State == WebSocketState.Open;

    public async Task SendAsync(byte[] utf8, CancellationToken ct)
    {
        await sendLock.WaitAsync(ct);
        try
        {
            if (ws.State == WebSocketState.Open)
                await ws.SendAsync(utf8, WebSocketMessageType.Text, true, ct);
        }
        finally { sendLock.Release(); }
    }

    public async Task<string?> ReceiveAsync(CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        using var ms = new MemoryStream();
        while (true)
        {
            var r = await ws.ReceiveAsync(buffer, ct);
            if (r.MessageType == WebSocketMessageType.Close)
            {
                if (ws.State == WebSocketState.CloseReceived)
                    await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", ct);
                return null;
            }
            ms.Write(buffer, 0, r.Count);
            if (r.EndOfMessage) return Encoding.UTF8.GetString(ms.ToArray());
        }
    }
}
