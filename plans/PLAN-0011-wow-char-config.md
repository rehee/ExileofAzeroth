# PLAN-0011：角色外观配置（WoWCharConfig：外观、幻化、技能皮肤）

| 字段 | 内容 |
|------|------|
| 编号 | PLAN-0011 |
| 状态 | 完成 |
| 负责人 | Claude |
| 创建日期 | 2026-11-11 |
| 最后更新 | 2026-11-11 |
| 完成度 | 100% |
| 关联设计 | docs/char-config.md（用法与格式）；docs/mapping-config.md §2.4、§2.6；docs/skill-presentation.md |
| 依赖计划 | PLAN-0006（装备与技能的对应表：本计划的默认值） |

## 目标
每个角色可以有自己的 WoW 外观：种族、性别、长相；每个装备槽显示什么物品（幻化）；某个技能用什么特效（技能皮肤）。
配置存在 DQQ 角色 DTO 的字符串字段 `WoWCharConfig` 里（JSON），跟着角色走，所以：
- 自由模式、选人界面显示自己的配置；
- 回放用录像种子里的角色 DTO，看别人的录像就是别人的外观和技能皮肤。

没写的部分一律用现在的全局对应表（`equipment.json`、`skills.json`），所以空配置 = 现在的样子。

## 1. 原则
1. **覆盖，不重写**：配置只写“和默认不一样”的地方，其余走全局对应表。
2. **写法和对应表一致**：物品外观的写法同 `equipment.json` 的 `items`，技能的写法同 `skills.json`。会写对应表就会写角色配置，runtime 用同一套解析。
3. **幻化同时决定动作**：物品的武器种类（`weapon`）和放法（`type`）参与技能动作判定。主手幻化成弓，攻击就是射击；幻化成剑，就是挥砍。这条和现在的规则（`docs/skill-presentation.md` §2.1）相同，只是“武器种类”改为取幻化后的。
4. **技能皮肤只换外观，不换逻辑**：DQQ 的范围、目标、伤害都不变。皮肤换的是借用的 WoW 法术，按原来的方式播。例如正义之火的范围圈换成奉献：大小仍是 DQQ 的判定范围，仍然跟着人走，也同样是“铺满的那几秒循环”。
5. **坏配置不影响游戏**：写错的部分忽略并记日志，其余照用（同对应表的 `Safe()`）。

## 2. JSON 格式（版本 1）

```jsonc
{
  "v": 1,

  // 外观：不写的项用 equipment.json 的 hero
  "look": {
    "race": "nightelf",          // human orc dwarf nightelf undead tauren gnome troll，或数字
    "gender": "female",          // male / female
    "skin": 2, "face": 1, "hair": 4, "hairColor": 3, "facialHair": 0
  },

  // 幻化：键是槽位，值是物品外观
  "transmog": {
    "MainHand": { "item": "Thunderfury" },                              // ① 物品库里的名字
    "OffHand":  { "display": 2324, "type": "shield" },                  // ② 直接写外观，写法同 equipment.json
    "Head":     { "hide": true },                                       // ③ 不显示
    "Shoulder": { "display": 29961 },                                   // ④ DQQ 没有的 WoW 槽位也能加
    "Back":     { "display": 23000 }
  },

  // 技能皮肤：键是 DQQ 技能（EnumSkillNumber），值的写法同 skills.json
  "skills": {
    "RighteousFireProfile": { "ring": "Consecration" },       // 范围圈换成奉献（地面模型库里的名字）
    "FireBall":             { "spell": 25306 },               // 换借用的法术（这里是高级火球术）
    "Lightning":            { "ray": { "spell": 421, "color": [1.0, 0.4, 1.0] } }  // 换光束并染色
  },

  // 持续效果皮肤：键是 DQQ 持续效果（EnumDurationNumber），值的写法同 skills.json 的 auras
  "durations": {
    "PowerWordShield": { "hero": 11426 }                      // 护盾罩换成寒冰护体的冰罩
  }
}
```

### 2.1 `look`
| 键 | 说明 |
|----|------|
| `race` | 种族名或 WoW 种族 id |
| `gender` | `male` / `female` |
| `skin` `face` `hair` `hairColor` `facialHair` | `CharSections` / `CharHairGeosets` 的序号；超出该种族的范围时 runtime 夹到范围内 |

职业不写：WoW 职业只影响少量动作（DQQ 决定一切），沿用 `classes.json`。

### 2.2 `transmog`
- **键**：DQQ 装备槽（`MainHand` `OffHand` `Head` `Body` `Gloves` `Boots` `Belt`），或 DQQ 没有的 WoW 槽（`Shoulder` `Back` `Legs` `Wrist` `Shirt` `Tabard`）。
- **值**，三选一：
  - `{ "item": "名字" }`：物品库 `config/mapping/wow-items.json` 里的条目（新增，见 §3.1）；
  - `{ "display": id, "type": …, "weapon": …, "slot": …, "sheath": … }`：同 `equipment.json` 的物品写法；
  - `{ "hide": true }`：这个槽不显示。
- **生效顺序**：幻化 > DQQ 装备（`equipment.json`）> 不显示。
- **弓、魔杖等现有规则照常**：幻化成弓自动进远程槽、左手拿、背在背上；幻化成魔杖按匕首站姿（`docs/mapping-config.md` §2.4）。
- **不改变 DQQ 装备本身**：只是显示。主手 DQQ 是剑、幻化成弓，回放里攻击按弓演（射击）。这正是“用风剑的外观和战斗动画逻辑代替”。

### 2.3 `skills` 与 `durations`
值的写法和全局 `skills.json` 一致，只写要换的键：

| 键 | 作用 | 对应全局 |
|----|------|---------|
| `action` | 换角色动作（melee shoot cast channel spin …） | `skills.*.action` |
| `spell` | 换借用的法术（数字，或按元素 `{ "Fire": id, "*": id }`） | `skills.*.spell` |
| `ray` | 换射线 / 散射 / 连锁的光束 `{ spell, color, channel }` | `rays.*` |
| `area` | 换区域爆发的法术 | `areas.*` |
| `ring` | 换范围圈的地面模型（地面模型库的名字或法术 id） | `auras.*.ring.area` |
| `hero` / `enemy` | 换持续效果的常驻光环（只用于 `durations`） | `auras.*` |

**技能 → 持续效果**：正义之火的范围圈其实挂在持续效果 `RighteousFire` 上。为了让用户按技能写（`skills.RighteousFireProfile.ring`），runtime 把技能的 `ring` 套用到这个技能产生的持续效果上（见 §6 问题 3）。

## 3. 配套的全局表
### 3.1 物品库 `config/mapping/wow-items.json`（新增）
WoW 1.12 客户端没有物品表（物品数据在服务器上），所以“风剑长什么样、是什么武器”要我们自己列：

```jsonc
{
  "Thunderfury": { "display": 30606, "type": "oneHand", "weapon": "sword", "name": "雷霆之怒，逐风者的祝福之剑" },
  "Ashbringer":  { "display": 22691, "type": "twoHand", "weapon": "sword2h" }
}
```

外观 id 逐条在游戏里确认（棋盘格问题见 `docs/mapping-config.md`）。先收几十件常见的传奇、套装武器，之后按需补。

### 3.2 地面模型库（`skills.json` 新增 `groundModels`）
范围圈的大小和循环段属于模型本身，不该让每个角色配置去写：

```jsonc
"groundModels": {
  "SearingGround": { "spell": 22191, "native": 20,   "loop": [0.3, 0.6] },
  "Consecration":  { "spell": 20116, "native": 8,    "loop": [0.3, 0.6] },
  "Retribution":   { "spell": 7294,  "native": 1.45, "hold": 0.8 }
}
```

`auras.*.ring` 与角色配置的 `ring` 都只写名字（或法术 id），大小、循环段从这里取。现在写死在客户端的 30%～60% 与 0.8 秒挪到这里，由 runtime 随事件发给客户端。

## 4. 实现
### A. 格式与文档
- [ ] 本文档定稿（§6 问题确认后）
- [ ] `docs/char-config.md`：给写配置的人看的说明 + 完整示例
- [ ] `config/char-config.example.json`

### B. runtime：读取与套用
- [ ] 读 `CharacterDTO.WoWCharConfig`（DQQ 加字段前，先从本地 `config/char-config/<角色 id>.json` 读，方便测试）
- [ ] `look`、`transmog` → `HeroDress`（选人界面、自由模式、回放种子英雄都走这里）
- [ ] 幻化后的武器种类 → `HeroWeapon` → 技能动作判定
- [ ] `skills` / `durations` → 按施放者套用。`SkillLooks.Play` 加一个“施放者的覆盖表”参数，先查覆盖，再查全局
- [ ] 物品库 `wow-items.json`、地面模型库 `groundModels` 的读取与校验
- [ ] 日志：每个英雄套用了哪些覆盖、哪些无效

### C. 客户端
- [ ] 范围圈的模型参数（`native`、`loop` / `hold`）改由事件带来，不再写死
- [ ] 新增 WoW 槽位（肩、背、腿、护腕、衬衣、战袍）的显示核对

### D. 验证
- [ ] 本地配置测试：夜精灵女性、主手幻化风剑、正义之火换奉献，自由模式与回放各看一遍
- [ ] 回放别人的录像（种子里的配置）显示对方的外观

### E. 编辑界面
只在 WoW 客户端里（插件的 Barber / Transmog / Skills 窗口），不做网页编辑（用户 2026-11-11）。

## 5. DQQ 那边要做的
- `CharacterDTO`（或它的实体）加字符串字段 `WoWCharConfig`，存 JSON 原文，DQQ 不解析。
- 录像种子（`CombatRequestDTO.Creators`）里的 DTO 带上这个字段。
- 保存接口（可以以后再加，编辑界面用）。

## 6. 待确认
1. **物品按名字还是按 WoW 物品 id**：`{ "item": "Thunderfury" }`（我们的物品库）还是 `{ "item": 19019 }`（WoW 物品编号，需要物品库按编号收录）？建议两种都支持，物品库同时记编号。
2. **幻化是否影响 DQQ 没有的槽位**：例如肩甲、披风，DQQ 角色没有这些装备，配置里写了就显示。建议允许。
3. **技能皮肤的键**：按技能（`RighteousFireProfile`）写，runtime 自动套到它产生的持续效果上；还是让配置直接按持续效果（`RighteousFire`）写？建议按技能写，`durations` 留给没有对应技能的持续效果（如护盾）。需要 DQQ 的持续效果日志带上来源技能，或者在 `skills.json` 里写一张“技能 → 持续效果”的表。
4. **种族、性别的限制**：WoW 里某些种族的体型和动作不同（牛头人、侏儒），DQQ 的体型由 DQQ 决定。是否允许所有种族？建议允许，体型仍按 DQQ。
5. **大小上限**：配置字符串建议不超过 16 KB。

## 进度日志
<!-- 追加，不要删除旧记录 -->
- 2026-11-11（Claude）：按用户要求创建：外观、幻化、技能皮肤存在角色 DTO 的 `WoWCharConfig`；格式与对应表一致；技能皮肤只换外观（例：正义之火换奉献，仍按判定范围、仍循环铺满段）。

- 2026-11-11（Claude）：§6 用户确认：名字和编号都支持；DQQ 没有的槽允许；技能皮肤按技能写（skillDurations 表套到持续效果）；种族全开；大小不限。
- 2026-11-11（Claude）：实现 runtime 读取与套用（CharConfig、物品库 wow-items.json、地面模型库 groundModels、skillDurations）；风剑幻化、正义之火换奉献验证通过。
- 2026-11-11（Claude）：DQQ 加了 WoWCharConfig 与 ICharConfigService（用户）。做了游戏内编辑：Barber（替身预览、保存后原地重进世界）、Transmog（ItemDisplayInfo 整理的外观列表）、Skills（范围圈）；保存写回 DQQ 后按 id 重新取角色（列表接口不带配置）。去掉本地测试文件；空配置 = 人类女 + 装备；回放只按种子 DTO；战场人物在 combat.begin 后按种子建。Skins 开关只管幻化和技能皮肤，种族等不受影响（用户确认）。
- 2026-11-11（Claude）：梳妆台预览改为原地换装（长相细节，不拆模型）/ 原地重建（种族、性别），保存后自己的人物同样原地更新，不再重进世界（用户确认细节不闪、视角正常）。
- 2026-11-11（Claude）：完成。外观只在 WoW 客户端里编辑，不做网页编辑（用户）。物品库按需补名字。
- 2026-11-11（Claude）：预留时装 cosmetics 的格式与时装库写法（docs/char-config.md「时装」），未实现，以后另开计划。

## 讨论 / 交接
