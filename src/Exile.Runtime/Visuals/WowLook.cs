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

    /// <summary>
    /// DQQ 怪物 → 客户端生物模型关键字（客户端在 CreatureDisplayInfo 里找第一个模型路径包含该关键字、
    /// 自带贴图的生物）。验证阶段的粗粒度对应，正式表后续细化。
    /// </summary>
    public static string MobModel(string? mob)
    {
        var m = mob ?? "";
        if (m.Contains("Crab")) return "Crab";
        if (m.Contains("Werewolf")) return "Worgen";
        if (m.Contains("Wolf")) return "Wolf";
        if (m.Contains("Goblin")) return "Goblin";
        if (m.Contains("Orc")) return "Orc";
        if (m.Contains("Cow") || m.Contains("UnName")) return "Cow";
        if (m.Contains("Dummy")) return "Kobold";
        return "Kobold";
    }

    /// <summary>DQQ 职业（战斗风格）→ WoW 职业 id（人类可用：1 战士 2 圣骑士 4 盗贼 5 牧师 8 法师 9 术士）。</summary>
    public static int WowClass(string archetype) => archetype switch
    {
        "Stable" => 2,      // 勇士 → 圣骑士
        "Sneaky" => 8,      // 秘术师 → 法师
        "Reckless" => 1,    // 狂战士 → 战士（怒气）
        "DraggingOut" => 4, // 侠客 → 盗贼
        "Funny" => 4,       // 格斗家 → 盗贼（能量 + 连击点）
        _ => 1,
    };
}
