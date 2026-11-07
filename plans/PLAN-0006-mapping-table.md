# PLAN-0006：DQQ ↔ WoW 对应表

| 字段 | 内容 |
|------|------|
| 编号 | PLAN-0006 |
| 状态 | 待开始 |
| 负责人 | （待认领） |
| 创建日期 | 2026-11-07 |
| 最后更新 | 2026-11-07 |
| 完成度 | 0% |
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
1. **表的形式**：C# 静态数据，放在 runtime `src/Exile.Runtime/Visuals/Mapping/`，按 DQQ 枚举名为键（枚举名而不是数值，DQQ 插入新值时不错位）。每类一个文件，例如 `MobLooks.cs`、`SkillLooks.cs`。
2. **谁来解析**：runtime 把"外观描述"放进协议（ActorState `model` 已有；新增 `skill`、`element` 等对应结果），客户端只执行，不认识 DQQ 枚举。数值型 display id / spell id 直接下发；关键字型（如 `@HumanFemale`）由客户端 `ModelBook` 解析。
3. **回退**：每类有默认值（怪物→狗头人、技能→按标签的普通攻击动画、元素→物理）。缺失时记录一次日志。
4. **缺失清单工具**：runtime 加命令行开关 `--mapping-report`，列出每个枚举值的对应结果与缺失项，输出到 `tools/logs/mapping-report.md`。
5. **挑选资源的工具**：benilla 自带 `benilla-extract`（如 `spellvis <spell id>` 查一个法术的视觉链），用来确认某个 WoW 法术的动画和特效是否合适。

## 任务清单
### A. 框架
- [ ] 建 `Visuals/Mapping/`：对应项的类型（模型、法术、学派、场景）、按枚举名查表、回退与日志
- [ ] 把 `WowLook.MobModel` / `WowClass` 迁到新表，行为不变
- [ ] `--mapping-report` 缺失清单
- [ ] 协议：ActorState 增 `displayId`（数值）与 `nameKey`；CombatEvent 增 `spellId`、`school`（PLAN-0004 使用）；更新 `docs/bridge-protocol.md`

### B. 怪物（39）
- [ ] 每个 `EnumMob` 选一个生物 display id（海岸蟹、地精、狼人、兽人、野狼……；思路见 architecture §5.4），记录模型路径与选择理由
- [ ] 稀有度（用户 2026-11-07 确认，三者都要）：名字颜色（仿 PoE：普通白、魔法蓝、精英黄、冠军/守卫橙）、头像精英边框（WoW 金龙/银龙，选中时可见）、精英以上身上光环特效
- [x] 首领占地（用户 2026-11-07 确认）：首领（CrabKing、GoblinChief、CowKing 等）**也不能超过**体型换算的正方形（体型 N → N×2 码见方），`Oversize` 名单保持为空

### C. 角色与装备
- [ ] 战斗风格 → WoW 职业（现有 5 项复核）
- [ ] 装备外观：DQQ 物品 profile → `ItemDisplayInfo`（先做武器：单手、双手、副手；再做护甲）
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

## 讨论 / 交接
