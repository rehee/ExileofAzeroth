# PLAN-0006：DQQ ↔ WoW 对应表

| 字段 | 内容 |
|------|------|
| 编号 | PLAN-0006 |
| 状态 | 进行中 |
| 负责人 | （待认领） |
| 创建日期 | 2026-11-07 |
| 最后更新 | 2026-11-07 |
| 完成度 | 50% |
| 关联设计 | docs/architecture.md D17、§5.4；docs/skill-presentation.md；docs/spike-results.md |
| 依赖计划 | 无（PLAN-0004 依赖本计划） |

## 目标
把 DQQ 的枚举系统地对应到 WoW 1.12.1 的资源，作为**数据**维护（D17：写在代码里、编译时打包），让战斗表现（PLAN-0004）、界面（PLAN-0005）、藏身处（PLAN-0007）都从同一份表取值。没有对应项时有统一的回退，并能列出缺失清单。

## 现状（2026-11-07）
- runtime `Visuals/WowLook.cs`：怪物按名字关键字猜模型（Crab→"Crab"、Wolf→"Wolf"……，其余一律 Kobold）；职业按战斗风格写死；英雄固定 `@HumanFemale`。
- 客户端 `net/exile.rs` 的 `ModelBook`：把关键字在 `CreatureDisplayInfo` 里搜出第一个匹配的 display id；体型由半径缩放（PLAN-0003）。
- 技能、元素、地图都还没有对应。

## 对应范围（DQQ 当前的枚举）
| DQQ 枚举 | 数量 | 对应到 WoW | 用在 |
|----------|------|------------|------|
| `EnumMob` | 39 | 生物 display id（模型 + 皮肤），可选缩放修正、名字 | 0004 |
| `EnumMobRarity` | 5（Normal/Magic/Elite/Champion/Guardian） | 颜色或光环、名字前缀（体型已由半径决定） | 0004 |
| `EnumCombatArcheType` | 5 | WoW 职业 id（决定能量条、待机姿势） | 0004、0005 |
| 装备（`EnumEquipSlot` 10 槽 × 物品 profile） | — | 装备外观 display id（`ItemDisplayInfo`） | 0004 |
| `EnumSkillNumber` | 87 | WoW 法术 id（`Spell.dbc`，借用它的动画、投射物、命中特效）或纯动画 | 0004 |
| `EnumSkillTag` | 29（Attack/Spell/Melee/Range/Nova/Chaining…） | 没有单独对应项时按标签选动画与默认特效 | 0004 |
| `EnumSkillShape` | 6（Chain/Cleave/Scatter/All/Random/Ray） | 形状特效的默认法术 | 0004 |
| `EnumDamageType` | 5（Physics/Fire/Cold/Lightning/Chaos） | 法术学派（物理、火焰、冰霜、自然、暗影）→ 飘字颜色、命中特效 | 0004 |
| `EnumDurationNumber` | 54 | Buff/Debuff 光环的法术 id（状态特效） | 0004 |
| `EnumMapNumber` | 24 | 战场位置（地图 id + 中心坐标 + 朝向），以及战斗前加载画面 | 0004、0007 |
| `EnumCharacterTrait` | 16 | 界面图标（`Interface\Icons\…`） | 0005 |

## 设计
1. **表的形式**（2026-11-07 用户改为配置文件，D17 修订）：`config/mapping/*.json`，按 DQQ 枚举名为键（枚举名而不是数值，DQQ 插入新值时不错位），每类一个文件（`mobs.json`、`classes.json`，以后 `skills.json` 等）；runtime `Visuals/Mapping/MappingFiles.cs` 读取，文件改过就重读。格式见 `docs/mapping-config.md`。
2. **谁来解析**：runtime 把"外观描述"放进协议（ActorState `model` 已有；新增 `skill`、`element` 等对应结果），客户端只执行，不认识 DQQ 枚举。数值型 display id / spell id 直接下发；关键字型（如 `@HumanFemale`）由客户端 `ModelBook` 解析。
3. **回退**：每类有默认值（怪物→狗头人、技能→按标签的普通攻击动画、元素→物理）。缺失时记录一次日志。
4. **缺失清单工具**：runtime 加命令行开关 `--mapping-report`，列出每个枚举值的对应结果与缺失项，输出到 `tools/logs/mapping-report.md`。
5. **挑选资源的工具**：benilla 自带 `benilla-extract`（如 `spellvis <spell id>` 查一个法术的视觉链），用来确认某个 WoW 法术的动画和特效是否合适。

## 任务清单
### A. 框架
- [x] 建 `Visuals/Mapping/`：按枚举名查表、回退与一次性日志（`MappingLog`）；怪物、战斗风格两张表已建，技能/元素/地图的类型随 D、E 加
- [x] 把 `WowLook.MobModel` / `WowClass` 迁到新表（`MobLooks`、`ClassLooks`）；怪物改为下发数值 display id
- [x] `--mapping-report` 缺失清单（`tools/run-mapping-report.cmd` → `tools/logs/mapping-report.md`）
- [ ] 协议：ActorState 增 `displayId`（数值）与 `nameKey`；CombatEvent 增 `spellId`、`school`（PLAN-0004 使用）；更新 `docs/bridge-protocol.md`

### B. 怪物（39）
- [x] 每个 `EnumMob` 选一个生物 display id 或穿装备的人形（`config/mapping/mobs.json`，附模型路径与理由）——用户 2026-11-07 已接受（奶牛按用户指定）
- [ ] 稀有度（用户 2026-11-07 确认，三者都要）：名字颜色（仿 PoE：普通白、魔法蓝、精英黄、冠军/守卫橙）、头像精英边框（WoW 金龙/银龙，选中时可见）、精英以上身上光环特效
- [x] 首领占地（用户 2026-11-07 确认）：首领（CrabKing、GoblinChief、CowKing 等）**也不能超过**体型换算的正方形（体型 N → N×2 码见方），`Oversize` 名单保持为空

### C. 角色与装备
- [ ] 战斗风格 → WoW 职业（现有 5 项复核）
- [x] 装备外观：DQQ 物品 → `ItemDisplayInfo`（`config/mapping/equipment.json`，56 件：武器 43 件、护甲 13 件；箭袋、标枪暂不显示）；英雄（玩家本体）在自由模式、回放、选人界面都穿着；战斗中武器拔出（sheath 1）——第一版待用户审阅
- [x] 角色外观（用户 2026-11-07 确认）：所有 DQQ 角色统一一套人类女性外观（肤色、脸、发型、发色固定），以后再说

### D. 技能与元素
- [ ] 技能按 `docs/skill-presentation.md` 的规则分四层建表：①角色动作（按动作类型）、②自身特效、③技能形状、④受击特效（按元素 × 层），加投射物外观；个别技能覆盖（旋风斩旋转等）
- [ ] 技能标签的默认表现（Attack+Melee、Spell、Range、Nova、Chaining、Warcry、Minion、Totem……）
- [ ] 技能形状的默认特效（Chain、Cleave、Scatter、All、Ray）
- [ ] 元素 → 学派与颜色
- [ ] 54 个持续效果 → 光环法术 id（只做能看出来的，其余不显示）

### E. 地图
- [ ] 24 张地图 → 战场位置（用户 2026-11-07 确认）：按主题选 3~5 个场景（海岸、森林、山洞、平原等），每张地图对应其一；中心点需平整（参考现在的北郡战场）
- [ ] 客户端 `exile.rs` 的 `BATTLE_CENTER` 改为由 `combat.begin` 下发

## 验收标准
- `--mapping-report` 显示 DQQ 现有全部怪物、技能、元素、地图、战斗风格都有对应项（持续效果可以有"不显示"项）。
- 在游戏里看一场包含多种怪物的回放，没有出现回退模型（狗头人）。
- 新增 DQQ 枚举值时，只需在表里加一行；没加时游戏用回退值正常运行，并出现在缺失清单里。

## 待确认
1. ~~首领是否允许超过体型占地~~ ✅ 不允许，同样受体型正方形限制（2026-11-07）。
2. ~~角色外观~~ ✅ 统一一套外观（2026-11-07）。
3. ~~地图场景~~ ✅ 按主题选 3~5 个场景（2026-11-07）。
4. 稀有度表现 ✅ 名字颜色 + 头像精英边框 + 光环特效（2026-11-07）。
5. ~~技能的表现方式~~ ✅ 规则见 `docs/skill-presentation.md`（2026-11-07 已确认）。
6. ~~怪物模型由谁挑~~ ✅ Claude 按"螃蟹→海岸蟹、地精→地精、狼→野狼、狼人→狼人、兽人→兽人、牛→牛"的思路先挑一版（附模型名），用户审（2026-11-07）。

## 进度日志
<!-- 追加，不要删除旧记录 -->
- 2026-11-07（Claude）：由 PLAN-0003 的验证结论拆出创建；同日细化为可执行计划（枚举盘点、表的设计、分阶段任务）。
- 2026-11-07（Claude）：记录用户对待确认问题的答复。
- 2026-11-07（Claude）：记录用户答复：角色外观统一、地图 3~5 个主题场景、稀有度三种表现都要；技能对应方式待讨论。
- 2026-11-07（Claude）：用户确认：怪物模型由 Claude 先挑、用户审。
- 2026-11-07（Claude）：开工。A 框架完成（`Visuals/Mapping/`、`--mapping-report`）；B 第一版怪物表（38 个，DQQ.Core 当前版本的全部）完成，客户端加 `.lineup` 审阅命令与生物名字（`CreatureName`）；导出 DBC 的工具 `run-wow-dbc`。协议的 `displayId`/`nameKey`/`spellId`/`school` 留到 D（技能）一并加。
- 2026-11-07（Claude）：用户指定"不可名状"（D2 地狱奶牛）：女性牛头人、奶牛肤色（肤色 1，白底黑斑）、穿流沙外套（物品 21499，外观 33895）、拿双手大砍刀（牛头人双手剑，外观 21774）。为此加了"穿装备的人形"（协议 `dress`，客户端建玩家式单位并下发物品模板）——这也是 C（装备外观、英雄换装）要用的通道。用户随后定：武器改为双手长柄偃月刀（`Polearm_2H_Bladed_B_01`，外观 3702）；不可名状之王同为奶牛女牛头人，穿战士 T2 愤怒套（外观 33981–33990）。碰撞、占地以 DQQ 为准，WoW 侧只管画面。新增 `run-wow-blp` 看贴图。
- 2026-11-07（Claude）：按用户要求把对应表从 C# 代码移到配置文件 `config/mapping/`（mobs.json、classes.json），写了 `docs/mapping-config.md`；改完保存后 `.lineup mobs` 或下一场回放即生效，问题写进日志和 `mapping-report`。已用 `run-mapping-report` 验证读取（怪物 38/38、战斗风格 5/5，无问题）。
- 2026-11-07（Claude）：C 装备外观：`equipment.json`（英雄统一外观 + DQQ 装备槽 → WoW 槽 + 56 件物品外观，护甲用勇气/暗影/野兽追猎者三套 T0，外观 id 对 Wowhead 核对，武器按模型家族挑）；runtime 角色列表下发英雄 `dress`，客户端给玩家本体加外观字段与可见装备，选人界面的模型也穿上；战斗中拔出武器。测试角色（只有主手青铜短剑）在游戏里看到腰间的剑。已知：DQQ 的弓在主手，WoW 里会拿在右手。
- 2026-11-07（Claude）：奶牛武器改为黑手末日之锯（物品 12583 → 外观 22404），只改配置、游戏里 `.lineup mobs UnName CowKing` 即生效（验证了热重读）。用户接受怪物表，B 的外观部分完成；稀有度表现（名字颜色、精英边框、光环）留到 PLAN-0004 实现时一起做。

## 审阅工具
- `tools/run-wow-dbc.cmd`：从本机 WoW 数据导出 DBC 原文件到 `tools/logs/wow-dbc/`（不入库），供挑选模型、法术。
- 游戏里（自由模式）聊天框：
  - `.lineup mobs`：按对应表把所有怪物排在面前，鼠标指上去显示 DQQ 怪物名；首领按首领体型显示。
  - `.lineup [size=N] <display id> …`：任意生物外观排成一排（名字 `#id`），用来比较候选。
  - `.lineup`：清掉。

## 讨论 / 交接
