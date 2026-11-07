using System.Collections.Concurrent;

namespace Exile.Runtime.Visuals.Mapping;

/// <summary>对应表的缺失记录：每个（类别, 键）只打一次日志，<c>--mapping-report</c> 也读它。</summary>
public static class MappingLog
{
    private static readonly ConcurrentDictionary<(string Kind, string Key), byte> Missing = new();

    /// <summary>记一次缺失（类别如 <c>EnumMob</c>，键为枚举名）；第一次时打日志。</summary>
    public static void Miss(string kind, string? key)
    {
        var k = key ?? "(null)";
        if (Missing.TryAdd((kind, k), 0)) Console.WriteLine($"[mapping] {kind}.{k} 没有对应项，使用回退值");
    }

    public static IEnumerable<(string Kind, string Key)> Seen => Missing.Keys;
}
