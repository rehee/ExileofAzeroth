namespace Exile.Runtime.Visuals;

/// <summary>
/// DQQ → WoW 外观对应表（D17：写在代码里，编译时打包）。验证阶段的最小版本。
/// </summary>
public static class WowLook
{
    public const int RaceHuman = 1;
    public const int GenderFemale = 1;
    /// <summary>人类女性的 CreatureDisplayInfo（D20 默认外观）。</summary>
    public const int DisplayHumanFemale = 50;
    /// <summary>敌人暂用的模型（兽人男性），正式的怪物对照表后续补。</summary>
    public const int DisplayEnemyPlaceholder = 51;

    /// <summary>英雄在战场上的外观关键字：客户端取一个人类女性 NPC 外观（自带服装）。</summary>
    public const string HeroModel = "@HumanFemale";

    /// <summary>DQQ 怪物 → 客户端生物外观 id（数字字符串，客户端 <c>ModelBook.resolve</c> 直接用）。表见 <see cref="Mapping.MobLooks"/>。</summary>
    public static string MobModel(string? mob) => Mapping.MobLooks.Get(mob).DisplayId.ToString();

    /// <summary>
    /// 特殊标记的怪物：渲染时不受"体型 N 最多占 N×N 单位见方"的限制（客户端 `unit_scale`）。
    /// 目前没有，按需在这里列出 DQQ 怪物编号（<c>EnumMob</c> 名）。
    /// </summary>
    private static readonly HashSet<string> OversizeMobs = [];

    public static bool Oversize(string? mob) => mob != null && OversizeMobs.Contains(mob);

    /// <summary>DQQ 战斗风格 → WoW 职业 id。表见 <see cref="Mapping.ClassLooks"/>。</summary>
    public static int WowClass(string archetype) => Mapping.ClassLooks.Get(archetype);
}
