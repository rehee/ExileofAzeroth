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
    Dictionary<string, EquipSummary> Equips, int WowRace, int WowClass, int WowGender, int DisplayId, Dress? Dress = null);
public sealed record EquipSummary(string Item, string? Name);

// ---- combat.* ----
public sealed record CombatStartRequest(string? CharacterId, string? Map, string? Mode);
public sealed record ReplayListRequest(string? CharacterId);
public sealed record ReplaySummary(string Id, DateTime? PlayTime, int Ticks, string Result, long Experience, string? TotalDamage,
    string? Name = null, string? Character = null);
/// <summary>combat.replay：指定 replayId，或不指定时取该角色最近一场；没有录像时用角色开一场新战斗。</summary>
/// <summary>回放一场录像：<c>ReplayId</c>（服务器录像），或 <c>File</c>（录像文件，见 Replays/ReplayFiles）。</summary>
/// <summary><c>Hold</c>：发完开场帧后等客户端的 <c>combat.ready</c> 再继续（最多 <c>MessageRouter.ReadyTimeout</c>）。</summary>
public sealed record CombatReplayRequest(string? ReplayId, string? CharacterId, string? Mode, string? File = null, bool Hold = false);
public sealed record ReplayExportRequest(string? ReplayId);
public sealed record ReplayExportResult(string File, string Path);
public sealed record ReplayFileSummary(string File, string Id, DateTime? PlayTime, int Ticks, string Result, long Experience, string? TotalDamage,
    string? Name = null, string? Character = null);
/// <param name="Heroes">种子里每个英雄（DQQ 角色 id → 穿上种子里装备的外观）：回放里英雄穿的是录像当时的装备，不是现在的。</param>
public sealed record CombatBegin(string CombatId, string Map, string Mode, int TickRate, string? FocusId,
    Dictionary<string, Dress>? Heroes = null);
public sealed record CombatFrame(string CombatId, int Tick, ActorState[] Actors, ActorState[] Projects, CombatEvent[] Events, long SentAtMs);
public sealed record ActorState(string Id, string Kind, double X, double Y, double Radius, double Angle,
    double Hp, double? Life, double? MaxLife, bool Moving, string? Name, string? Mob, string Model,
    double? TargetX = null, double? TargetY = null, bool Oversize = false, Dress? Dress = null);

/// <summary>
/// 穿装备的人形外观（PLAN-0006）：客户端把它建成玩家式单位，种族、性别、长相加身上的装备（<c>ItemDisplayInfo</c> id）。
/// 有它时 <c>model</c> 不用。
/// </summary>
public sealed record Dress(int Race, int Gender, int Skin, int Face, int Hair, int HairColor, DressItem[] Items);

/// <summary>一件装备：装备槽（0 头 … 4 胸 … 15 主手 16 副手）、外观 id、InventoryType、物品大类与子类。</summary>
public sealed record DressItem(int Slot, int DisplayId, int InventoryType, int ItemClass, int ItemSubclass);
public sealed record CombatEvent(string Id, int Tick, string Kind, string? From, string? Target,
    string? Skill, string[] Tags, string? Phase, string? Shape, double Range, double? CenterX, double? CenterY,
    string Element, double Amount, bool Critical, bool Killed, bool Success);
public sealed record CombatEnd(string CombatId, string Result, int Ticks, long Experience, int Kills, string TotalDamage, CombatStats Stats);
public sealed record CombatStats(int Frames, double SimulateMs, double AvgFrameBytes, int MaxFrameBytes, double GcPauseMs, int Gen0, int Gen1, int Gen2);
public sealed record ErrorData(string Message);

/// <summary><c>ui.mapping.mobs.result</c> 的一项：DQQ 怪物名、生物外观 id、是否首领（审阅时画大一些）。</summary>
/// <summary>
/// 一场战斗的战场（<c>combat.prepare.result</c>，config/mapping/maps.json）：DQQ 地图、WoW 场景名、WoW 地图 id、
/// 中心 [x, y, z]（z 由客户端按地面高度取）、说明。
/// </summary>
/// <param name="Clear">战斗期间清掉小摆件（石头、树、碎墙……）的正方形边长，码；null = 客户端默认 200（DQQ 战场最大 100×100 单位）；0 = 不清。</param>
public sealed record Battlefield(string Map, string Scene, int WowMap, double[] Center, string Note, double? Clear = null);

/// <summary><c>combat.prepare</c>：和 <c>combat.replay</c> 同样指定录像，只问它的战场；<c>Scene</c> 指定时改用该场景（测试用，聊天命令 <c>.replay 场景名</c>）。</summary>
public sealed record CombatPrepareRequest(string? ReplayId, string? CharacterId, string? File = null, string? Scene = null);

public sealed record MobLookEntry(string Mob, int DisplayId, bool Boss, Dress? Dress = null);
