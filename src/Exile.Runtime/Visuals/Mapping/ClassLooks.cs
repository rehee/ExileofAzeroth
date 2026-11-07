namespace Exile.Runtime.Visuals.Mapping;

/// <summary>
/// DQQ 战斗风格（<c>EnumCombatArcheType</c>）→ WoW 职业 id（PLAN-0006 C）。表在 <c>config/mapping/classes.json</c>（<see cref="MappingFiles"/>）。
/// </summary>
public static class ClassLooks
{
    public static IReadOnlyDictionary<string, (int ClassId, string Note)> Table => MappingFiles.Classes;

    public static int Get(string? archetype)
    {
        if (archetype != null && Table.TryGetValue(archetype, out var c)) return c.ClassId;
        MappingLog.Miss("EnumCombatArcheType", archetype);
        return MappingFiles.ClassFallback;
    }
}
