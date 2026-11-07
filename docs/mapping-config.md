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
    "<EnumItem 名>": { "display": 1628, "type": "twoHand", "weapon": "sword2h" }   // 或带放法、武器种类
  }
}
```

物品放到哪个 WoW 槽由 DQQ 角色把它装在哪个槽决定（`slots`；没写的槽不显示），`items` 里只写外观、放法（type，不写就按槽默认）和武器种类（weapon）。表里没有的物品不显示，日志记一次缺失。

**DQQ 新增装备槽的流程**（用户 2026-11-07）：用户通知 → Claude 在 `slots` 补一行，同时更新本文档（本节与 §2.3 的槽表）。runtime 对没写的槽不报警，靠这个流程保证不漏。

装备从哪来（用户 2026-11-07）：
- **选人界面、自由模式**：DQQ 角色服务给的 `CharacterDTO` 的装备（现在穿着的）。
- **战斗（回放）**：录像种子里的角色 DTO（`CombatRequestDTO.Creators`，录像当时穿着的），所以看别人的录像文件也是别人的装备；回到自由模式后换回自己现在的装备。

### 2.5 `maps.json`

```jsonc
{
  "fallback": "forest",                       // 表里没有的地图用的场景
  "scenes": {
    "<场景名>": { "map": 0, "center": [x, y], "note": "说明" }   // map 目前只支持 0（东部王国）；高度取地面
  },
  "maps": { "<EnumMapNumber 名>": "<场景名>" }
}
```

战场是以 `center` 为中心、边长 20 码的方块（DQQ 战场 10×10 单位，每单位 2 码）。开打前客户端先问 runtime 这场录像的战场（`combat.prepare`），再传送过去；问不到时用上一次的战场。

**找平整的中心点**：自由模式聊天框 `.goto x y` 传送过去，日志（`tools/logs/exile-play.log`，搜 `exile: goto`）记录周围 ±10 码的地面高差；`.goto x y 半径` 在半径内按 10 码网格找高差最小的点传送过去（候选前 5 名记在日志里，搜 `flat spot`）。注意：高差只看地形，不看房子、树、栅栏等摆件，选好后要在游戏里看一眼。高差 1 码左右可以接受。打完一场后会回到最后一次 `.goto` 的地方。

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
