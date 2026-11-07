using Exile.Runtime.Protocol;

namespace Exile.Runtime.Visuals.Mapping;

/// <summary>一个怪物在 WoW 里的样子。</summary>
/// <param name="DisplayId">生物外观 id（<c>CreatureDisplayInfo.dbc</c>；人形 NPC 自带服装）。有 <paramref name="Dress"/> 时不用。</param>
/// <param name="Model">模型路径（只供审阅）。</param>
/// <param name="Note">为什么选它。</param>
/// <param name="Dress">穿装备的人形外观：客户端按它建玩家式单位。</param>
/// <param name="Boss">首领（`.lineup mobs` 按首领体型显示）。</param>
public sealed record MobLook(int DisplayId, string? Model, string Note, Dress? Dress, bool Boss);

/// <summary>
/// DQQ 怪物（<c>EnumMob</c>，按枚举名）→ WoW 外观（PLAN-0006 B）。表在 <c>config/mapping/mobs.json</c>
/// （<see cref="MappingFiles"/>），体型不在这里：由 DQQ 半径决定。
/// </summary>
public static class MobLooks
{
    public static IReadOnlyDictionary<string, MobLook> Table => MappingFiles.Mobs;

    public static MobLook Fallback => MappingFiles.MobFallback;

    public static bool IsBoss(string mob) => Table.TryGetValue(mob, out var l) && l.Boss;

    /// <summary>怪物的外观；表里没有时用回退值并记一次缺失。</summary>
    public static MobLook Get(string? mob)
    {
        if (mob != null && Table.TryGetValue(mob, out var look)) return look;
        MappingLog.Miss("EnumMob", mob);
        return Fallback;
    }
}
