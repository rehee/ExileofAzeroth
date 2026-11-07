# DQQ ↔ WoW 对应表：配置文件

| 字段 | 内容 |
|------|------|
| 状态 | 现行 |
| 作者 | Claude |
| 创建日期 | 2026-11-07 |
| 最后更新 | 2026-11-07 |
| 关联 | PLAN-0006；architecture.md D17（2026-11-07 修订）；bridge-protocol.md §5.2 |

## 1. 放在哪、怎么生效

对应表是**配置文件**，不写在代码里（用户 2026-11-07：以后会频繁调整）。

- 位置：仓库的 `config/mapping/`，进 git。目前有两份：
  - `mobs.json`：DQQ 怪物（`EnumMob`）→ WoW 外观
  - `classes.json`：DQQ 战斗风格（`EnumCombatArcheType`）→ WoW 职业
  - `equipment.json`：英雄的统一外观，DQQ 装备槽与物品（`EnumEquipSlot`、`EnumItem`）→ WoW 装备外观
  - `maps.json`：DQQ 地图（`EnumMapNumber`）→ WoW 战场场景
  - 以后的技能、元素、地图等各加一份，格式同样写进本文档。
- 谁读：Exile Runtime（C#，`src/Exile.Runtime/Visuals/Mapping/MappingFiles.cs`）。客户端（Rust）不认识 DQQ，只执行 runtime 发来的外观（display id、`dress`）。
- 查找顺序：`--mapping <目录>` → 仓库里的 `config/mapping/`（从 runtime 所在目录往上找）→ runtime 旁的 `mapping/`（编译时自动复制过去，供以后的发布包使用）。
- **改完保存即可，不用重新编译**：runtime 在下面这些时候检查文件有没有改过，改过就重读：
  - 游戏里输入 `.lineup mobs`（审阅怪物外观）
  - 开始一场回放或战斗（`combat.replay`、`combat.start`）
  - 取角色列表（选人界面；`equipment.json` 的改动在下次进入世界时穿到英雄身上）
- 出错时：保留上一次读好的表继续运行，问题写进 runtime 日志（`tools/logs/runtime.log`，搜 `[mapping]`）和 `run-mapping-report` 生成的报告。表里没有的项用回退值，并在日志里记一次 `没有对应项`。

## 2. 文件格式

JSON，允许 `//` 注释和末尾多余的逗号。键名区分大小写；DQQ 名字必须和 DQQ 枚举名完全一致（拼错会在报告里列出）。

### 2.1 `mobs.json`

```jsonc
{
  "fallback": { "display": 26, "note": "狗头人" },   // 表里没有的怪物用它
  "dresses": { "<外观名>": { /* 见 2.3 */ } },        // 可复用的穿装备外观
  "mobs": {
    "<EnumMob 名>": {
      "display": 342,              // 生物外观 id（CreatureDisplayInfo.dbc），和 dress 二选一
      "dress": "cow",              // 或者：dresses 里的名字 / 直接写一个 2.3 的对象
      "model": "Creature\\Crab",   // 可选，只供审阅（报告里显示）
      "note": "铜色",              // 可选，选择理由
      "boss": true                 // 可选，只影响 `.lineup mobs` 的显示大小
    }
  }
}
```

体型不在表里：画面大小由 DQQ 的半径决定（客户端缩放），碰撞和占地完全按 DQQ 计算，WoW 只负责画出来。

### 2.2 `classes.json`

```jsonc
{
  "fallback": 1,
  "classes": {
    "<EnumCombatArcheType 名>": { "class": 2, "note": "勇士 → 圣骑士" }
  }
}
```
职业 id：1 战士、2 圣骑士、3 猎人、4 盗贼、5 牧师、7 萨满、8 法师、9 术士、11 德鲁伊（人类可用 1/2/4/5/8/9）。

### 2.3 穿装备的人形（`dress`）

客户端把它建成"玩家式"单位：种族的本体模型 + 长相 + 身上的装备（每件只要外观 id）。

```jsonc
{
  "race": "tauren",      // human orc dwarf nightelf undead tauren gnome troll，或数字 1–8
  "gender": "female",    // male / female
  "skin": 1,             // 肤色编号（CharSections.dbc 的颜色序号；0 起）
  "face": 0, "hair": 0, "hairColor": 0,   // 可选，默认 0
  "items": {
    "chest": 33895,                                   // 简写：只写外观 id，放法按槽默认
    "mainHand": { "display": 3702, "type": "twoHand", "weapon": "polearm" }
  }
}
```

**装备槽**（`items` 的键）与默认放法：

| 槽 | 默认 type | 说明 |
|----|-----------|------|
| `head` | head | 头盔 |
| `shoulder` | shoulder | 肩 |
| `shirt` | shirt | 衬衣 |
| `chest` | chest | 胸甲；长袍写 `"type": "robe"`（下摆盖住腿） |
| `waist` | waist | 腰带 |
| `legs` | legs | 腿 |
| `feet` | feet | 鞋 |
| `wrist` | wrist | 护腕 |
| `hands` | hands | 手套 |
| `back` | back | 披风 |
| `mainHand` | oneHand | 主手；双手武器写 `"type": "twoHand"` |
| `offHand` | offHand | 副手；盾写 `"type": "shield"`，副手物品写 `"type": "holdable"` |
| `ranged` | ranged | 远程 |
| `tabard` | tabard | 战袍 |

**type** 可选值：head shoulder shirt chest robe waist legs feet wrist hands back oneHand twoHand mainHand offHand shield holdable ranged thrown rangedRight tabard。

**weapon**（武器种类，可选，影响收刀位置等）：axe axe2h mace mace2h sword sword2h polearm staff dagger fist bow gun crossbow thrown wand。

### 2.4 `equipment.json`

```jsonc
{
  "hero": { "race": "human", "gender": "female", "skin": 0, "face": 0, "hair": 0, "hairColor": 0 },  // 写法同 2.3，不写 items
  "slots": { "<EnumEquipSlot 名>": "<WoW 装备槽>" },   // 如 "Body": "chest"；项链、戒指不写 = 不显示
  "items": {
    "<EnumItem 名>": 29958,                                            // 只写外观
    "<EnumItem 名>": { "display": 1628, "type": "twoHand", "weapon": "sword2h" },  // 或带放法、武器种类
    "<EnumItem 名>": { "display": 8102, "slot": "ranged", "type": "ranged", "weapon": "bow" }  // slot：物品自己定 WoW 槽
  },
  "uniqueItems": { "<EnumUniqueItem 名>": 35050 }   // 独特物品的外观，写法同 items
}
```

物品放到哪个 WoW 槽由 DQQ 角色把它装在哪个槽决定（`slots`；没写的槽不显示），`items` 里只写外观、放法（type，不写就按槽默认）和武器种类（weapon）。表里没有的物品不显示，日志记一次缺失。

**EnumItem 的每一项是一件具体物品**，各自对应一个 WoW 外观（没有“套装风格”这种整体选项；挑外观时参考了哪套只是来源说明）。**独特物品**（物品带 `EnumUniqueItem`，用户 2026-11-07）有自己的模型：先找 `uniqueItems`，没填时用底子物品（EnumItem）的外观，日志记一次缺失。独特物品的外观由 Claude 先挑，用户看到不对再改。

`"slot"`（可选）让物品自己决定 WoW 槽，盖过 `slots` 的映射。现在只有弓用：DQQ 把弓装在主手，WoW 的弓要放在远程槽（`ranged`），战斗时才会**左手持弓**、射箭（用户 2026-11-07）。只有远程武器的单位在战斗中拿出远程武器，攻击显示为"自动射击"（法术 75，带飞行的箭），不是近战挥砍。

**挑外观要确认模型和贴图都在**：`ItemDisplayInfo` 里有些行指向客户端数据里不存在的模型或贴图（例如 2461、8099 的短弓），游戏里会显示成**蓝白棋盘格**。挑好后在游戏里看一眼；`tools/logs/wow-dbc/files.txt` 是本机数据的文件列表，可以先查。

**DQQ 新增装备槽的流程**（用户 2026-11-07）：用户通知 → Claude 在 `slots` 补一行，同时更新本文档（本节与 §2.3 的槽表）。runtime 对没写的槽不报警，靠这个流程保证不漏。

装备从哪来（用户 2026-11-07）：
- **选人界面、自由模式**：DQQ 角色服务给的 `CharacterDTO` 的装备（现在穿着的）。
- **战斗（回放）**：录像种子里的角色 DTO（`CombatRequestDTO.Creators`，录像当时穿着的），所以看别人的录像文件也是别人的装备；回到自由模式后换回自己现在的装备。

### 2.5 `maps.json`

```jsonc
{
  "fallback": "forest",                       // 表里没有的地图用的场景
  "scenes": {
    "<场景名>": { "map": 0, "center": [x, y], "clear": 200, "note": "说明" }   // map = WoW 地图 id（Map.dbc：0 东部王国、1 卡利姆多，副本也可以）；高度取地面；clear 见下
  },
  "maps": { "<EnumMapNumber 名>": "<场景名>" }
}
```

战场是以 `center` 为中心、边长 20 码的方块（DQQ 战场 10×10 单位，每单位 2 码）。开打前客户端先问 runtime 这场录像的战场（`combat.prepare`），再传送过去（可以跨大陆）；问不到时用上一次的战场。去战场和回来时都显示该地图的加载画面，至少 0.5 秒（用户 2026-11-07）。

**清场（`clear`，用户 2026-11-07）**：战斗期间，以战场中心为中心、边长 `clear` 码的正方形里的**小摆件**（石头、树、碎墙、栅栏、马车、灯柱……）不放置；**建筑（WMO）和草保留**，所以战场要选在空旷处（小起伏可以）。不写 = 200 码（DQQ 战场最大 100×100 单位，每单位 2 码），0 = 不清。只在战斗时生效：传送去战场前设置、传送回来前取消，两次都在加载画面下重新读地形，所以自由模式里一切照旧。

**在某个场景里打一场看看**：自由模式聊天框 `.replay 场景名`（如 `.replay rift`）——播放最近一场录像，但战场换成指定的场景（测试用；不写场景名就按录像自己的地图）。

**选点标准**（用户 2026-11-07，以渡鸦岭墓地为样板）：空旷、主题鲜明、附近没有建筑（建筑不清，小摆件战斗时会清掉）；小起伏可以。

**找平整的中心点**：自由模式聊天框 `.goto x y [半径] [map=地图id]` 传送过去（`map=1` 去卡利姆多；不写就是当前地图），日志（`tools/logs/exile-play.log`，搜 `exile: goto`）记录周围 ±10 码的地面高差；`.goto x y 半径` 在半径内按 10 码网格找高差最小的点传送过去（只找和起点高度差不多的点：不低于起点 2 码、不高于 4 码，免得找到水底或山上）（候选前 5 名记在日志里，搜 `flat spot`）。注意：高差只看地形，不看房子、树、栅栏等摆件，选好后要在游戏里看一眼。高差 1 码左右可以接受。打完一场后会回到最后一次 `.goto` 的地方。

### 2.6 `skills.json`（技能表现，PLAN-0006 D）

```jsonc
{
  "elements": { "<EnumDamageType 名>": { "school": 2, "note": "说明" } },   // WoW 伤害学派：0 物理 1 神圣 2 火焰 3 自然 4 冰霜 5 暗影 6 奥术
  "actions":  { "<动作>": 133 },                                             // 或按元素 { "Fire": 133, "Cold": 116, "*": 7268 }
  "skills":   { "<EnumSkillNumber 名>": { "action": "spin", "spell": 1680 } }, // 个别技能覆盖，两项都可选；spell 也可按元素
  "areas":    { "<EnumSkillShape 名>": { "Fire": 11969, "*": 6343, "radius": 10 } }, // 技能区域中心的特效
  "rays":     { "<EnumSkillShape 名>": { "Fire": 8211, "*": 13003 } },      // 画成射线的形状（散射）
  "durations":{ "<EnumDurationNumber 名>": 6673 }                           // 持续效果出现时在那个角色身上播的法术
}
```

数字都是 **WoW 1.12 的法术 id**（`Spell.dbc`）：客户端播这个法术的施法动作、自身特效、飞行物和命中特效（就像服务器发来 `SMSG_SPELL_GO`）。这样角色动作 ①、自身特效 ②、投射物和受击特效 ④ 都随法术一起来；同一种动作按元素换法术就换了颜色。

**动作**由 runtime 按 `docs/skill-presentation.md` §2.1 的规则判定（技能标签、施放者主手武器、是不是怪物），`skills` 里可以为个别技能改：

| 动作 | 什么时候 | 默认法术 |
|---|---|---|
| `melee` | 攻击 + 肉搏；拿近战武器的非肉搏攻击 | 不播法术，伤害带出挥砍（白色近战数字） |
| `mobAttack` | 怪物的攻击、怪物专用技能 | 同上，怪物自己的攻击动画 |
| `shoot` / `throw` | 主手弓（弩、枪）/ 标枪，攻击且无肉搏 | 75 自动射击 / 2764 投掷 |
| `projectile` | 投射物技能 | 按元素：火球、寒冰箭、闪电箭、暗影箭、奥术飞弹 |
| `cast` | 法术 | 按元素；治疗、回复、盾在 skills 覆盖 |
| `channel` | 引导 | 精神鞭笞、吸取法力（冰霜）、火焰之雨 |
| `spin` | 旋风斩（skills 覆盖） | 1680 旋风斩 |
| `nova` | 新星 | 冰霜新星、火焰新星、魔爆术 |
| `shout` / `aura` / `kick` / `charge` / `judgement` | 战吼 / 光环开启 / 闪空脚 / 冲锋 / 尸爆、自毁命令、召唤 | 战斗怒吼 / 圣印 / 脚踢 / 冲锋 / 审判 |

非近战动作的伤害显示为法术伤害数字（颜色按 `elements` 的学派，元素取实际伤害最多的那种）；持续伤害（光环、流血）显示为周期伤害数字。

**区域（`areas`，用户 2026-11-07）**：DQQ 每次技能生效会写技能区域日志（形状、半径、中心）。runtime 按 `areas` 里该形状 × 元素的法术，在区域中心播放（例如火球命中处的爆炸），按 DQQ 半径 ÷ 特效半径（`radius`，码）缩放。客户端在中心放一个看不见的单位（Invisible Stalker）施放这个法术，4 秒后移除。新星、旋风斩、引导自己已有范围表现，不重复；没写的形状不播。

```jsonc
"areas": {
  "All": [
    { "upTo": 3, "Fire": 2136, "Melee": 6343, "*": 6343, "radius": 4 },   // DQQ 半径 ≤ 3：小爆炸
    { "Fire": 11969, "Cold": 122, "*": 6343, "radius": 10 }               // 其余：新星圈
  ]
}
```

一档可以直接写对象，也可以按大小分几档写成数组（`upTo`：DQQ 半径不超过它时用这一档）。WoW 的特效模型不随单位缩放，所以小范围用法术的命中爆炸、大范围用新星圈。键先按元素（技能的元素标签），再按其他技能标签（如 `Melee` → 冲击波），最后 `"*"`（用户 2026-11-07：圆形区域按技能标签选冲击波、冰霜新星一类的动画）。

**射线（`rays`，用户 2026-11-07）**：形状日志里类型是散射（`Scatter`）时，从散射的出发点（区域中心，例如火球爆炸的中心）向结束处（打到的敌人；没有就是范围边缘）发出射线，形态和颜色按技能标签选（先元素，再其他标签，最后 `"*"`）。火球 + 散射：爆炸中心发出火焰射线灼伤敌人。两端各放一个隐形单位，出发端向结束端施放射线法术。

```jsonc
"rays": { "Scatter": {
  "Fire":  { "spell": 15407, "color": [2.2, 0.9, 0.2] },   // 精神鞭笞的光束，染成橙红
  "Cold":  5138,                                         // 吸取法力的蓝色光束
  "Lightning": 421                                       // 闪电链
} }
```

射线用 WoW 的引导光束（用户 2026-11-07）。光束的颜色来自贴图，按贴图挑法术（`SpellChainEffects.dbc`，`run-wow-dbc` 导出）：

| 贴图 | 颜色 | 引导法术（可用） |
|---|---|---|
| Beam_Red | 红 | 29172 Ribbon Pole Channel |
| Beam_RedDrops | 血红带滴 | 24322 Blood Siphon |
| Beam_Purple | 紫 | 1120 吸取灵魂 |
| ManaBeam | 蓝 | 5138 吸取法力 |
| SoulBeam | 绿白 | 689 吸取生命、755 生命通道 |
| ShockLightning | 电弧 | 8345 Control Machine |
| Lightning / HealBeam | 闪电 / 金绿（一次性弧线，`"channel": false`） | 421 闪电链 / 1064 治疗链 |

`color` 是乘到光束贴图上的 RGB（加色混合，大于 1 更亮），用来给光束换颜色（原版客户端不给光束染色，这是 Exile 的改动）。光束持续约 0.65 秒。

**投射物**：DQQ 的投射物（火球、奥术球、充能弹、苍穹之矛、音速爆弹、刀光剑影……）每帧都有位置。客户端在发射处放一个隐形单位施放该技能的 `projectile` 法术，飞向另一个沿 DQQ 路径移动的隐形单位，所以 WoW 的飞行物跟着 DQQ 的轨迹走；投射物消失后 0.8 秒移除。施放者本身只播施法动作。弓和标枪的箭、矛由施放时的自动射击、投掷画，不再另画。

**只播施法**：`skills` 里写 `"hit": false`，只播法术的施法部分、不播命中特效（烈焰新星：自身的爆炸没有伤害，不播新星圈，用户 2026-11-07）。

**持续效果**：DQQ 有些技能不写施放日志（战吼、超级霸王电影弹），runtime 比较前后帧角色身上的持续效果，出现时按 `durations` 播法术。

**挑法术**：`tools/logs/wow-dbc/Spell.dbc`（`run-wow-dbc` 导出）；在游戏里用 `.play 录像名 [场景]` 看（`docs/dev-workflow.md` §4.0.3）。`run-event-stats` 的"技能"表最后一列列出每个技能实际用了哪个动作、哪个法术。

## 3. 怎么找 id

1. **从游戏数据里查**：双击 `tools/run-wow-dbc.cmd`，把本机 WoW 的 DBC 导出到 `tools/logs/wow-dbc/`（不入库）：
   - 生物外观 → `CreatureDisplayInfo.dbc` + `CreatureModelData.dbc`（模型路径、皮肤贴图名）
   - 装备外观 → `ItemDisplayInfo.dbc`（模型名、贴图名、图标名）
   - 肤色 → `CharSections.dbc`；贴图可用 `run-wow-blp`（把 `tools/logs/wow-blp.txt` 里列的贴图转成 PNG）看
2. **从物品查外观**：Wowhead 经典版 `https://www.wowhead.com/classic/item=<物品 id>&xml` 里的 `displayId`，例如流沙外套 21499 → 33895、愤怒胸甲 16966 → 33983。
3. **在游戏里比较**（自由模式聊天框）：
   - `.lineup mobs [怪物名…]`：按当前 `mobs.json` 把怪物排在面前（写了名字就只摆这几个，如 `.lineup mobs UnName CowKing`），鼠标指上去显示 DQQ 名；
   - `.lineup [size=N] <display id> …`：任意生物外观排成一排比较候选；
   - `.lineup`：清掉。
4. **检查**：双击 `tools/run-mapping-report.cmd` → `tools/logs/mapping-report.md`，列出每个 DQQ 枚举值的对应、缺失项和配置文件的问题。

## 4. 例子

把"螃蟹"换成朱红色的大号螃蟹：`"Crab": { "display": 9571, "note": "换成大号" }` → 保存 → 游戏里 `.lineup mobs`。

给一个怪物穿一身现成外观：先在 `dresses` 里定义，再在 `mobs` 里写 `"dress": "名字"`；几个怪物可以共用同一身。

## 变更记录
| 日期 | 修改人 | 说明 |
|------|--------|------|
| 2026-11-07 | Claude | 初稿：对应表改为配置文件（mobs、classes），含穿装备的人形 |
| 2026-11-07 | Claude | 新增 equipment.json（英雄外观与装备，§2.4） |
| 2026-11-07 | Claude | 新增 maps.json（战场场景，§2.5）与 `.goto` 选点 |
| 2026-11-07 | Claude | 新增 skills.json（技能表现，§2.6）；equipment.json 加标枪（长柄矛顶替，投掷） |
