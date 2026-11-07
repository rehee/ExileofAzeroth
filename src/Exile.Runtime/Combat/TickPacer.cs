using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Exile.Runtime.Combat;

/// <summary>
/// 观看模式的节拍器：按「开始时间 + tick × 1000/30 ms」精确推送。
/// DQQ 自带的节流用 Task.Delay，Windows 默认计时精度约 15.6 ms，实测每 tick 约 45 ms（慢 1.4 倍），
/// 所以让 DQQ 不等待（SkipDelay），由这里控制节奏（DQQ 零改动）。
/// </summary>
public sealed class TickPacer
{
    private readonly double msPerTick;
    private readonly Stopwatch clock = new();
    private int firstTick = -1;

    public TickPacer(int ticksPerSecond) => msPerTick = 1000.0 / ticksPerSecond;

    public async Task WaitForTick(int tick, CancellationToken ct)
    {
        if (firstTick < 0) { firstTick = tick; clock.Start(); return; }
        var target = (tick - firstTick) * msPerTick;
        var remaining = target - clock.Elapsed.TotalMilliseconds;
        if (remaining > 2) await Task.Delay(TimeSpan.FromMilliseconds(remaining - 1.5), ct);
        while (clock.Elapsed.TotalMilliseconds < target) Thread.SpinWait(50);
    }

    /// <summary>从下一个 tick 重新计时（开场等待客户端就绪之后用，免得之后的 tick 一口气追赶）。</summary>
    public void Rebase()
    {
        firstTick = -1;
        clock.Reset();
    }

    [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint ms);

    /// <summary>把 Windows 计时器精度调到 1 ms（进程退出时系统自动恢复）。</summary>
    public static void RaiseTimerResolution()
    {
        if (OperatingSystem.IsWindows()) _ = timeBeginPeriod(1);
    }
}
