using Exile.Runtime.Protocol;

namespace Exile.Runtime.Visuals.Mapping;

/// <summary>一个怪物在 WoW 里的样子。</summary>
/// <param name="DisplayId">生物外观 id（<c>CreatureDisplayInfo.dbc</c>；人形 NPC 自带服装）。有 <paramref name="Dress"/> 时不用。</param>
/// <param name="Model">模型路径（只供审阅）。</param>
/// <param name="Note">为什么选它。</param>
/// <param name="Dress">穿装备的人形外观：客户端按它建玩家式单位。</param>
/// <param name="Boss">首领（`.lineup mobs` 按首领体型显示）。</param>
public sealed record MobLook(int DisplayId, string? Model, string Note, Dress? Dress, bool Boss);

/// <summary>一个怪物稀有度（<c>EnumMobRarity</c>）在 WoW 里的表现（PLAN-0006 B；客户端的显示在 PLAN-0004 做）。</summary>
/// <param name="Color">名字颜色（RRGGBB，仿 PoE：普通白、魔法蓝、精英黄、冠军/守卫橙）。</param>
/// <param name="Rank">头像边框：WoW 生物等级（0 普通，1 精英金龙，2 稀有精英，3 首领，4 稀有银龙）。</param>
/// <param name="Auras">身上常驻的光环法术（并进 ActorState 的 auras，客户端照常写进光环栏）。</param>
/// <param name="Glow">整只怪的外发光颜色（RRGGBB，客户端着色器的边缘光，PLAN-0004 C）；null = 不发光。</param>
public sealed record RarityLook(string Color, int Rank, int[] Auras, string Note, string? Glow = null);

/// <summary>
/// DQQ 怪物（<c>EnumMob</c>，按枚举名）→ WoW 外观（PLAN-0006 B）。表在 <c>config/mapping/mobs.json</c>
/// （<see cref="MappingFiles"/>），体型不在这里：由 DQQ 半径决定。
/// </summary>
public static class MobLooks
{
    public static IReadOnlyDictionary<string, MobLook> Table => MappingFiles.Mobs;

    public static MobLook Fallback => MappingFiles.MobFallback;

    public static bool IsBoss(string mob) => Table.TryGetValue(mob, out var l) && l.Boss;

    public static IReadOnlyDictionary<string, RarityLook> Rarities => MappingFiles.Rarities;

    /// <summary>稀有度的表现；没有稀有度或表里没有为 null（表里没有时记一次缺失）。</summary>
    public static RarityLook? Rarity(string? rarity)
    {
        if (rarity == null) return null;
        if (Rarities.TryGetValue(rarity, out var r)) return r;
        MappingLog.Miss("EnumMobRarity", rarity);
        return null;
    }

    private static readonly Dictionary<Type, System.Reflection.PropertyInfo?> rarityProps = new();

    /// <summary>
    /// 对象上类型为 <c>EnumMobRarity</c> 的属性（DQQ 的帧快照 <c>TickLogActor</c> 用哪个名字由 DQQ 定，按类型找，
    /// DQQ 改名也不用改这里）；没有为 null。
    /// </summary>
    public static System.Reflection.PropertyInfo? RarityProperty(Type t)
    {
        lock (rarityProps)
        {
            if (rarityProps.TryGetValue(t, out var p)) return p;
            p = t.GetProperties().FirstOrDefault(x => (Nullable.GetUnderlyingType(x.PropertyType) ?? x.PropertyType) == typeof(DQQ.Enums.EnumMobRarity));
            if (p != null) Console.WriteLine($"[mapping] 怪物稀有度取自 {t.Name}.{p.Name}");
            return rarityProps[t] = p;
        }
    }

    /// <summary>
    /// DQQ 战斗地图上活着的单位（<c>Map.AllActors</c>，没有就 <c>Actors</c>、<c>Enemies</c>；字典取值）。
    /// 帧快照 <c>TickLogActor</c> 不带稀有度，稀有度要从活单位上读（PLAN-0006 B，2026-11-12）。
    /// </summary>
    private static bool probed;
    public static List<(string? Id, object Actor)> LiveActors(object map)
    {
        var list = new List<(string? Id, object Actor)>();
        try
        {
            // 单位在 Map.AM（ActorManagement）的非公开属性 actors（Dictionary<Guid, IActor>）里；也看 Map 本身，DQQ 挪了位置也找得到。
            var holders = new[] { map.GetType().GetProperty("AM")?.GetValue(map), map }.Where(h => h != null).Select(h => h!);
            foreach (var (holder, name) in holders.SelectMany(h => new[] { "AllActors", "Actors", "Enemies" }.Select(n => (h, n))))
            {
                const System.Reflection.BindingFlags any = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase;
                if (holder.GetType().GetProperty(name, any)?.GetValue(holder) is not System.Collections.IEnumerable e) continue;
                foreach (var o in e)
                {
                    if (o == null) continue;
                    var t = o.GetType();
                    // 字典（actors：Guid → IActor）的键就是单位 id；单位自己的 Id 属性有重名（new 隐藏），按名字取会歧义。
                    if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
                        list.Add((t.GetProperty("Key")!.GetValue(o)?.ToString(), t.GetProperty("Value")!.GetValue(o)!));
                    else list.Add((IdOf(o), o));
                }
                break;
            }
        }
        catch (Exception ex) { Console.WriteLine($"[mapping] 读 DQQ 地图上的单位出错：{ex.Message}"); }
        if (!probed && list.Count == 0) { probed = true; Console.WriteLine("[mapping] DQQ 地图上没找到单位（Map.AM.actors），怪物稀有度不显示"); }
        return list;
    }

    /// <summary>DQQ 单位的 id（<c>Id</c> 属性）；没有为 null。</summary>
    public static string? IdOf(object actor)
    {
        try { return actor.GetType().GetProperties().FirstOrDefault(p => p.Name == "Id" && p.GetIndexParameters().Length == 0)?.GetValue(actor)?.ToString(); }
        catch { return null; }
    }

    /// <summary>一个 DQQ 单位快照的稀有度名（EnumMobRarity）；读不到为 null。</summary>
    public static string? RarityOf(object actor)
    {
        try { return RarityProperty(actor.GetType())?.GetValue(actor)?.ToString(); }
        catch { return null; }
    }

    /// <summary>怪物的外观；表里没有时用回退值并记一次缺失。</summary>
    public static MobLook Get(string? mob)
    {
        if (mob != null && Table.TryGetValue(mob, out var look)) return look;
        MappingLog.Miss("EnumMob", mob);
        return Fallback;
    }
}
