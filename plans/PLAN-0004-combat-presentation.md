# PLAN-0004：战斗表现

| 字段 | 内容 |
|------|------|
| 编号 | PLAN-0004 |
| 状态 | 待开始 |
| 负责人 | （待认领） |
| 创建日期 | 2026-11-07 |
| 最后更新 | 2026-11-13 |
| 完成度 | 0% |
| 关联设计 | docs/architecture.md §5.4；docs/bridge-protocol.md §3；docs/spike-results.md |
| 依赖计划 | PLAN-0006（技能、怪物、元素的对应项）。阶段 A、B 可以先做 |

## 目标
让战斗回放在 WoW 里看起来像一场完整的战斗：谁在放什么技能、投射物飞向哪里、打中谁、伤害多少、谁死了，都能一眼看出来。

## 现状（2026-11-07）
已有：单位生成、贴地、移动（跑步动画，起步至少 5 帧，不缓冲）、朝向、体型、开场等待、战斗姿势、近战挥砍（`AttackerState`，只有物理飘字）、血量更新、结束清场。
没有：施法与投射物、技能形状、按元素着色的飘字、死亡表现、名字与血条、光环、装备外观；自由模式物件的隐藏（目前没有物件）。

## 做法要点
- **借用客户端现成的法术系统**：benilla 已实现 1.12 的法术视觉链（`creature_anim/spell_visual.rs`：`Spell.dbc` → `SpellVisual` → `SpellVisualKit`，含施法动画、投射物、命中特效、音效）。桥接只需发 `SpellStart` / `SpellGo`（带 WoW 法术 id、施法者、目标），客户端就会播放整套表现。法术 id 来自对应表（PLAN-0006）。
- **伤害数字**：法术伤害发 `SpellDamageLog`（带学派），客户端按学派着色；近战仍用 `AttackerState`。治疗发 `SpellHealLog`。
- **死亡**：血量为 0 时客户端播放死亡动画（`unit_reads_dead`），桥接延迟数秒再移除单位。
- **名字**：单位 guid 里带生物 entry，桥接应答客户端的 `CreatureQuery`（名字、类型），名字就能显示；中文名依赖 PLAN-0005 的中文字体。
- **血条**：1.12 没有姓名板血条，用插件在头顶画血条（读 `UnitHealth`），或用目标框架；具体方式在阶段 C 决定。

## 任务清单
### A. 日志事件梳理（runtime）
- [ ] 盘点 `TickLog` 的 `LogType` 与字段（CastSkill、TriggerSpell、SkillRangeAndTarget、DamageTaken、HealingTaken、Killed、ActorIsDown 以及持续效果），整理"DQQ 事件 → 客户端表现"表，写入 `docs/bridge-protocol.md` §3.2
- [ ] CombatEvent 补字段：施法者与目标 id、技能对应的 WoW 法术 id、学派、`RenderPhase`（Launch/Impact）、形状中心与范围（部分已有）
- [ ] 投射物 `projects[]`：确认 DQQ 投射物的生命周期（出现、移动、命中、消失）

### B. 死亡与伤害（客户端桥接）
- [ ] 死亡：`Killed`/`ActorIsDown` 时血量置 0，单位播放死亡动画；`CLEAR` 前保留尸体若干秒（DQQ 已移除的单位不要立即 `ObjectDestroyed`）
- [ ] 伤害飘字按学派着色：法术伤害改发 `SpellDamageLog`；暴击放大
- [ ] 治疗飘字：`SpellHealLog`

### C. 名字与血条
- [ ] guid 带生物 entry；桥接应答 `CreatureQuery`（名字取 DQQ `DisplayName`，类型、等级）
- [ ] 回放期间打开名字显示（`UnitNameNPC` 等 CVar，或桥接指定）
- [ ] 血条：所有单位头顶显示（用户 2026-11-07 确认），跟随单位、按血量更新
- [ ] 稀有度：名字颜色与头像边框——数据已由 PLAN-0006 下发（`actors[].nameColor`（RRGGBB）、`rank`（`CreatureQuery` 的生物等级：1 金龙、4 银龙），`mobs.json` 的 `rarities`）；光环已生效。客户端要做：按 `nameColor` 着色名字，`CreatureQuery` 回答带 `rank`
- [ ] 精英、首领的外发光（用户 2026-11-13 决定做）：像 D3 那样整只怪向外发蓝、发金。WoW 原版的光环染色（SpellVisualKit 的 TINT）是乘到模型颜色上的，只能偏色、不会发光，要在客户端着色器里加一个按单位的发光（边缘光 / 加色），颜色取稀有度（精英蓝、冠军与守卫金），强度要克制（用户 2026-11-12：看得出是精英、首领就行）。现在先用 28134 的淡蓝偏色和 7069 脚下法阵顶替（`mobs.json` 的 `rarities`）

### D. 施法与投射物
- [ ] `CastSkill` → `SpellStart`（有施法时间的技能）/ `SpellGo`（瞬发），施法动画、施法特效
- [ ] 投射物：`SpellGo` 带目标，客户端按 `Spell.dbc` 速度飞行；DQQ 的投射物位置与客户端飞行时间的对齐方式（以 DQQ 命中时刻为准）
- [ ] 命中特效：`RenderPhase` Impact 时播放

### E. 技能形状与光环
- [ ] 形状：All（新星）、Cleave（扇形）、Chain（闪电链，`SpellChainTargets`）、Ray（光束）、Scatter（散射）——用对应表的默认法术，或地面特效
- [ ] 持续效果：Buff/Debuff → 光环（客户端 `aura_visual`），出现与消失跟随 DQQ

### F. 装备外观与场景
- [ ] 英雄装备外观：武器、护甲 display id（对应表）写入玩家可见装备字段
- [ ] 进入战场时隐藏自由模式的 NPC 与可交互物品（D28；PLAN-0007 有物件后再测）
- [ ] 战场位置按地图（对应表 E），`BATTLE_CENTER` 由 `combat.begin` 下发

### G. 检查
- [ ] 体型 2、3（精英、首领）的实际效果；`WowLook.Oversize` 名单
- [ ] 怪物多（> 40）时的帧率与可读性；必要时限制同屏数量或简化特效
- [ ] 用 DQQ 网页版回放同一场战斗对照，列出遗漏

## 验收标准
- 一场含法术、投射物、范围技能、精英怪的回放：每个施法都有动画和特效，投射物飞向目标并在命中时出现伤害数字（按元素着色），死去的单位倒地，头顶有名字，能看出各单位血量。
- 与 DQQ 网页版回放对照，没有遗漏关键信息。
- 一场 900 tick 的回放全程流畅（无明显卡顿）。

## 待确认
1. ~~血条的显示方式~~ ✅ 所有单位头顶都显示血条（2026-11-07）。
2. ~~尸体保留多久~~ ✅ 3 秒（2026-11-07）。
3. ~~是否限制同屏数量~~ ✅ 先不限制，实测卡了再说（2026-11-07）。

## 进度日志
<!-- 追加，不要删除旧记录 -->
- 2026-11-07（Claude）：由 PLAN-0003 的验证结论拆出创建；同日细化为可执行计划（借用客户端法术系统的做法、分阶段任务）。
- 2026-11-07（Claude）：记录用户对待确认问题的答复。
- 2026-11-07（Claude）：用户确认：尸体保留 3 秒；同屏数量先不限制。

## 讨论 / 交接
