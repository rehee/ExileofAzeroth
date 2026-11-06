using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Exile.Runtime.Protocol;

/// <summary>
/// 桥接消息外壳（docs/bridge-protocol.md）。所有消息：{ v, seq, type, re?, data? }。
/// type 前缀：ui.* 界面；scene.* 场景；combat.* 战斗；cmd.* 玩家指令（预留）。
/// </summary>
public sealed record Envelope(
    [property: JsonPropertyName("v")] int V,
    [property: JsonPropertyName("seq")] long Seq,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("re")] long? Re,
    [property: JsonPropertyName("data")] JsonNode? Data);

public static class ProtocolJson
{
    public const int Version = 1;
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

// ---- ui.* ----
public sealed record CharacterSummary(string Id, string Name, int Level, string Archetype,
    Dictionary<string, EquipSummary> Equips, int WowRace, int WowClass, int WowGender, int DisplayId);
public sealed record EquipSummary(string Item, string? Name);

// ---- combat.* ----
public sealed record CombatStartRequest(string? CharacterId, string? Map, string? Mode);
public sealed record ReplayListRequest(string? CharacterId);
public sealed record ReplaySummary(string Id, DateTime? PlayTime, int Ticks, string Result, long Experience, string? TotalDamage);
/// <summary>combat.replay：指定 replayId，或不指定时取该角色最近一场；没有录像时用角色开一场新战斗。</summary>
public sealed record CombatReplayRequest(string? ReplayId, string? CharacterId, string? Mode);
public sealed record CombatBegin(string CombatId, string Map, string Mode, int TickRate, string? FocusId);
public sealed record CombatFrame(string CombatId, int Tick, ActorState[] Actors, ActorState[] Projects, CombatEvent[] Events, long SentAtMs);
public sealed record ActorState(string Id, string Kind, double X, double Y, double Radius, double Angle,
    double Hp, double? Life, double? MaxLife, bool Moving, string? Name, string? Mob, string Model);
public sealed record CombatEvent(string Id, int Tick, string Kind, string? From, string? Target,
    string? Skill, string[] Tags, string? Phase, string? Shape, double Range, double? CenterX, double? CenterY,
    string Element, double Amount, bool Critical, bool Killed, bool Success);
public sealed record CombatEnd(string CombatId, string Result, int Ticks, long Experience, int Kills, string TotalDamage, CombatStats Stats);
public sealed record CombatStats(int Frames, double SimulateMs, double AvgFrameBytes, int MaxFrameBytes, double GcPauseMs, int Gen0, int Gen1, int Gen2);
public sealed record ErrorData(string Message);
