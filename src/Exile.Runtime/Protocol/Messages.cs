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
    double? TargetX = null, double? TargetY = null, bool Oversize = false, Dress? Dress = null, int Spell = 0, bool Spin = false,
    int[]? Auras = null, AuraRing? Ring = null);
// Auras（PLAN-0006 D，2026-11-11）：这个单位身上的持续效果借用的 WoW 光环法术（skills.json 的 auras）：客户端写进它的光环栏，
// 光环的常驻特效（护盾、燃烧、冰冻……）就一直挂着，没了就消失。Ring：以它为中心的一圈特效，显示范围（正义之火）。

/// <summary>范围圈：沿半径 Radius（DQQ 距离）一圈放 Count 个挂着光环 Spell 的隐形单位，跟着中心走。</summary>
/// <remarks>Area > 0：改画地面上的一个圆（借用地面法术 Area 的区域模型，按 Radius 从它原本的半径 Native 码缩放），不放那圈单位。</remarks>
public sealed record AuraRing(int Spell, double Radius, int Count, int Area = 0, double Native = 0);
// Spell（只对 kind = project）：这个 DQQ 投射物借用的 WoW 飞行法术（0 = 不画，例如弓的箭由施放时的自动射击画），PLAN-0006 D。
// Spin（只对 kind = project，用户 2026-11-10）：带 Spin 标签的技能的投射物。照常飞；碰到敌人时由它自己施放旋风斩
// （事件 action = projectileSpin）。

/// <summary>
/// 穿装备的人形外观（PLAN-0006）：客户端把它建成玩家式单位，种族、性别、长相加身上的装备（<c>ItemDisplayInfo</c> id）。
/// 有它时 <c>model</c> 不用。
/// </summary>
/// <remarks>FacialHair：胡子 / 耳环等（PLAYER_BYTES_2 字节 0），角色配置（PLAN-0011）的 look.facialHair。</remarks>
public sealed record Dress(int Race, int Gender, int Skin, int Face, int Hair, int HairColor, DressItem[] Items, int FacialHair = 0);

/// <summary>
/// 一件装备：装备槽（0 头 … 4 胸 … 15 主手 16 副手）、外观 id、InventoryType、物品大类与子类，
/// 收起时挂在哪（Sheath：0 按种类，1 斜背在背上，2 法杖竖背，3 身侧，4 盾背在背上）。
/// </summary>
public sealed record DressItem(int Slot, int DisplayId, int InventoryType, int ItemClass, int ItemSubclass, int Sheath = 0);
public sealed record CombatEvent(string Id, int Tick, string Kind, string? From, string? Target,
    string? Skill, string[] Tags, string? Phase, string? Shape, double Range, double? CenterX, double? CenterY,
    string Element, double Amount, bool Critical, bool Killed, bool Success,
    string? Duration = null, string? Action = null, int Spell = 0, int School = 0, double Scale = 1, bool Hit = true,
    double? EndX = null, double? EndY = null, double[]? Color = null, bool Channel = false);
// 射线（action = ray）：CenterX/CenterY 为出发点（DQQ 日志的 From，例如爆炸的火球投射物），EndX/EndY 为结束处（日志的 Target）。
// Duration：持续效果（持续伤害/治疗来自哪个持续效果，或 DurationStart 的那个）。Action / Spell / School：runtime 按
// config/mapping/skills.json 判定的角色动作、借用的 WoW 法术（0 = 不播）、伤害学派（PLAN-0006 D）。
// Kind 除 DQQ 日志种类外还有 runtime 生成的 DurationStart（某角色身上出现了一个持续效果，Target = 该角色）。
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

/// <summary>auth.login：玩家在登录界面输入的 DQQ 账号（EoALauncher 演示版）。</summary>
public sealed record AuthLoginRequest(string? User, string? Password);
/// <summary>auth.login.result：Ok；失败时 Code = credentials（账号或密码不对）或 network（连不上服务器）。</summary>
public sealed record AuthLoginResult(bool Ok, string? Code, string? Error);
