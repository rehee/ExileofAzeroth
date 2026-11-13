# PLAN-0004：战斗表现

| 字段 | 内容 |
|------|------|
| 编号 | PLAN-0004 |
| 状态 | 已完成 |
| 负责人 | Claude |
| 创建日期 | 2026-11-07 |
| 最后更新 | 2026-11-13 |
| 完成度 | 100% |
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
- [x] 盘点 `TickLog` 的 `LogType` 与字段（CastSkill、TriggerSpell、SkillRangeAndTarget、DamageTaken、HealingTaken、Killed、ActorIsDown 以及持续效果），整理"DQQ 事件 → 客户端表现"表，写入 `docs/bridge-protocol.md` §3.2　✅ 2026-11-13：表在 bridge-protocol §3.2，细则在 skill-presentation.md
- [x] CombatEvent 补字段：施法者与目标 id、技能对应的 WoW 法术 id、学派、`RenderPhase`（Launch/Impact）、形状中心与范围（部分已有）　✅ PLAN-0006 D 已做（`action`、`spell`、`school`、`scale`、`hit` 等，由 runtime 判定好）
- [x] 投射物 `projects[]`：确认 DQQ 投射物的生命周期（出现、移动、命中、消失）　✅ PLAN-0006 D 已做（出现即飞、跟 DQQ 位置、消失处爆开）

### B. 死亡与伤害（客户端桥接）
- [x] 死亡：`Killed`/`ActorIsDown` 时血量置 0，单位播放死亡动画；`CLEAR` 前保留尸体若干秒（DQQ 已移除的单位不要立即 `ObjectDestroyed`）　✅ 怪物 PLAN-0006 时已做；2026-11-13 英雄一方同样倒地留 3 秒（本体不移除）
- [x] 伤害飘字按学派着色：法术伤害改发 `SpellDamageLog`；暴击放大　✅ PLAN-0006 D 已做
- [x] 治疗飘字：`SpellHealLog`　✅ PLAN-0006 D 已做

### C. 名字与血条
- [x] guid 带生物 entry；桥接应答 `CreatureQuery`（名字取 DQQ `DisplayName`，类型、等级）　✅ 名字 PLAN-0006 已做（每个名字一个 entry）；等级统一为 1，姓名板上不显示
- [x] 回放期间打开名字显示（`UnitNameNPC` 等 CVar，或桥接指定）　✅ 2026-11-13：回放时所有单位都有姓名板，名字在姓名板上（实机确认）
- [x] 血条：所有单位头顶显示（用户 2026-11-07 确认），跟随单位、按血量更新　✅ 2026-11-13：用客户端自带的姓名板（V 键那套，原计划写的"1.12 没有"不对），回放期间强制敌我全开、含英雄本体、不限距离（实机确认）
- [x] 稀有度：名字颜色与头像边框——数据已由 PLAN-0006 下发（`actors[].nameColor`（RRGGBB）、`rank`（`CreatureQuery` 的生物等级：1 金龙、4 银龙），`mobs.json` 的 `rarities`）；光环已生效。客户端要做：按 `nameColor` 着色名字，`CreatureQuery` 回答带 `rank`　✅ 2026-11-13：名字颜色在姓名板上，rank 进 `CreatureQuery`（实机确认）
- [x] 精英、首领的外发光（用户 2026-11-13 决定做）：像 D3 那样整只怪向外发蓝、发金。WoW 原版的光环染色（SpellVisualKit 的 TINT）是乘到模型颜色上的，只能偏色、不会发光，要在客户端着色器里加一个按单位的发光（边缘光 / 加色），颜色取稀有度（精英蓝、冠军与守卫金），强度要克制（用户 2026-11-12：看得出是精英、首领就行）。现在先用 28134 的淡蓝偏色和 7069 脚下法阵顶替（`mobs.json` 的 `rarities`）　✅ 2026-11-13：`mobs.json` rarities 的 `glow` → 模型着色器边缘光；28134、7069 已从表里去掉（实机确认；强度见进度日志）

### D. 施法与投射物
- [x] `CastSkill` → `SpellStart`（有施法时间的技能）/ `SpellGo`（瞬发），施法动画、施法特效　✅ PLAN-0006 D 已做
- [x] 投射物：`SpellGo` 带目标，客户端按 `Spell.dbc` 速度飞行；DQQ 的投射物位置与客户端飞行时间的对齐方式（以 DQQ 命中时刻为准）　✅ PLAN-0006 D 已做（标记单位沿 DQQ 路径飞，命中以 DQQ 为准）
- [x] 命中特效：`RenderPhase` Impact 时播放　✅ PLAN-0006 D 已做

### E. 技能形状与光环
- [x] 形状：All（新星）、Cleave（扇形）、Chain（闪电链，`SpellChainTargets`）、Ray（光束）、Scatter（散射）——用对应表的默认法术，或地面特效　✅ PLAN-0006 D 已做
- [x] 持续效果：Buff/Debuff → 光环（客户端 `aura_visual`），出现与消失跟随 DQQ　✅ PLAN-0006 D 已做

### F. 装备外观与场景
- [x] 英雄装备外观：武器、护甲 display id（对应表）写入玩家可见装备字段　✅ PLAN-0006 / PLAN-0011 已做
- [-] 进入战场时隐藏自由模式的 NPC 与可交互物品（D28；PLAN-0007 有物件后再测）　➡ 2026-11-13 移交 PLAN-0007（那边已有同一条任务；现在自由模式没有物件，无从测试）
- [x] 战场位置按地图（对应表 E），`BATTLE_CENTER` 由 `combat.begin` 下发　✅ PLAN-0006 E 已做（`combat.prepare`）

### G. 检查
- [x] 体型 2、3（精英、首领）的实际效果；`WowLook.Oversize` 名单　✅ 2026-11-13：`.lineup mobs` 看了 CrabKing、WerewolfCaptain、GoblinChief 的首领体型，战斗里看了守卫螃蟹，大小与占地正常；`WowLook.Oversize` 名单保持为空
- [x] 怪物多（> 40）时的帧率与可读性；必要时限制同屏数量或简化特效　✅ 2026-11-13：45～53 只怪的 900 tick 回放两场，战斗中没有卡顿告警；姓名板多时互相避让、会离开怪物头顶（见交接）
- [-] 用 DQQ 网页版回放同一场战斗对照，列出遗漏　➡ 2026-11-13 移交用户验收：网页版要用户自己的账号登录，AI 不代登；对照时发现的遗漏另开计划或记到 PROGRESS 待办

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
- 2026-11-13（Claude）：认领并开工。先对照代码核对：A、B、D、E 和 F 的大部分已在 PLAN-0006 D / PLAN-0011 里顺带做完（上面逐项标注），真正剩下的是 C 和 G。本次做了 C：① 血条用客户端自带的姓名板（`vplates.rs`），回放期间强制敌我全开、英雄本体也有、不限 20 码、不显示等级，回放结束恢复玩家设置；② 怪物名字按稀有度着色（姓名板名字前加颜色转义）；③ `CreatureQuery` 带 rank（同名不同 rank 各占一个 entry）；④ 外发光：`rarities.glow` → `actors[].glow` → 借体色表（`rig_tint`）每个单位那个字的最高字节（原版恒为 0xFF，按 3-3-2 存颜色）→ `wow_model.wgsl` 加边缘光，不改缓冲布局；⑤ B 补漏：英雄一方死亡同样倒地留尸体，本体的血量上限改用 DQQ 的。文档：bridge-protocol §3.2 事件表、mapping-config `glow`。改动文件：runtime `MappingFiles/MobLooks/CombatRunner/Messages/MappingReport`，客户端 `net.rs`、`net/exile.rs`、`vplates.rs`、`vplates/gate.rs`、`aura_visual.rs`、`instance_tint.rs`、`wow_model.wgsl`。
- 2026-11-13（Claude）：编译通过（`client-build`），`exile-play` 实机检查：① 回放时所有单位头顶有血条（敌红、英雄蓝），跟着单位走，死了就消失，回到自由模式恢复原设置；② 把 Normal 临时配成橙名 + 蓝光 + rank 1 检查（查完已改回）：姓名板名字变橙、整只怪蓝色边缘光、鼠标提示显示 `Level 1 (Elite)`，三项都生效；③ 边缘光强度看过后从 0.75 调到 0.6（`wow_model.wgsl` 的 `EXILE_GLOW_RIM`，改它要重编客户端）；④ `replay-NormalAttack_bow`（900 tick、53 只怪）连放两场到结束，战斗中没有 `frame hitch`。遇到一次 `exile: runtime link failed: WebSocket protocol error: Encountered invalid opcode: 3`（回放到第 361 帧时客户端断线、runtime 随之退出），当时我正在回放中途改写 `mobs.json`；之后两场完整回放没有再出现，原因没查清。没做：真正的守卫 / 精英在画面里的金光、蓝光没有单独截到（几场录像里守卫都在镜头外或开场就死了）；G 的"体型 2、3 实际效果""与 DQQ 网页版对照"；F 的"隐藏自由模式物件"（等 PLAN-0007）。未提交 git。
- 2026-11-13（Claude）：收尾（用户要求尽快结束进入下一阶段）。补做 G 的体型检查；F 的"隐藏自由模式物件"移交 PLAN-0007，"与 DQQ 网页版对照"移交用户验收，两项标为 `[-]`（未做、已移交）。状态改为已完成。验收结果：施法、投射物、范围技能、按元素着色的伤害数字、死亡倒地、头顶名字与血条、精英标识都已在实机回放中看到；900 tick 回放两场无卡顿告警；"与网页版对照无遗漏"这一条未验。产出位置见上一条日志的改动文件列表。

## 讨论 / 交接
- 2026-11-13（Claude）给下一位：
  - 姓名板在怪多时很挤：客户端自带的避让会把姓名板推离怪物头顶（开场 45 只怪挤在一起时最明显）。要不要只给受伤的 / 精英以上的显示，或缩小姓名板，等用户看过再定。
  - 英雄的姓名板没有名字、头像框名字也是空的：中文名在英文客户端字体里是空白，等 PLAN-0005 的中文字体。
  - `invalid opcode: 3` 断线只见过一次（见进度日志）。客户端 `ws_loop` 用 2 ms 读超时轮询 tungstenite，可疑但没证实；再出现时记下是不是也在改配置文件。
  - 外发光借用了体色表 `rig_tint` 每个字的最高字节（`instance_tint::with_glow`），颜色只有 3-3-2 位。以后要更准的颜色或可调强度，再给 `wow_light` 缓冲加一段单独的表。
