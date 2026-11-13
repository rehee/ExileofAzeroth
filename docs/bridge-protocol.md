# 桥接协议（runtime ↔ 客户端）

| 字段 | 内容 |
|------|------|
| 作者 | Claude |
| 创建日期 | 2026-11-06 |
| 状态 | 草案 v1（验证阶段，随实现更新） |
| 关联 | docs/architecture.md D9、§6；PLAN-0003 |
| 实现 | `src/Exile.Runtime/Protocol/Messages.cs`、`Bridge/` |

## 1. 传输

- runtime 在 `ws://127.0.0.1:<port>/` 提供 WebSocket（默认端口 7710，`--port` 可改），只监听本机。
- 文本帧，UTF-8 JSON，字段名 camelCase，空值省略。
- TCP_NODELAY 已开启。客户端断开后 runtime 退出（D26，调试可用 `--stay`）。

## 2. 消息外壳

```json
{ "v": 1, "seq": 12, "type": "combat.frame", "re": 3, "data": { } }
```

| 字段 | 说明 |
|------|------|
| `v` | 协议版本，当前 1 |
| `seq` | 发送方自增序号 |
| `type` | 消息类型，前缀 `ui.` / `scene.` / `combat.` / `cmd.` |
| `re` | 可选，回复的是对方哪条消息的 `seq` |
| `data` | 消息内容 |

## 3. 消息列表（v1）

| 方向 | type | data | 说明 |
|------|------|------|------|
| R→C | `hello` | `{runtime, protocol}` | 连接建立后 runtime 先发 |
| C→R | `auth.login` | `{user, password}` | 只在演示版启动器（runtime `--player-login`，客户端 `EXILE_LOGIN=dqq`）：玩家在 WoW 登录界面输入的 DQQ 账号，runtime 在线登录。密码不记日志、不落盘 |
| R→C | `auth.login.result` | `{ok, code, error}` | `code`：`credentials`（账号或密码不对，登录界面显示密码错误）或 `network`（连不上 DQQ 服务器） |
| C→R | `ui.character.list` | — | 取角色列表（ICharacterService.GetAllCharacterDTOs） |
| R→C | `ui.character.list.result` | `CharacterSummary[]` | `{id, name, level, archetype, equips{slot:{item,name}}, wowRace, wowClass, wowGender, displayId}`（D20，对应表见 `Visuals/WowLook.cs`） |
| C→R | `combat.start` | `{characterId?, map?, mode?}` | `map` 为 DQQ `EnumMapNumber` 名，默认 `Map_1_1`；`mode`：`watch`（默认，实时推帧）/ `background`（直接算完，只发 begin/end） |
| R→C | `combat.begin` | `{combatId, map, mode, tickRate, focusId}` | `focusId` 为玩家英雄的 id |
| R→C | `combat.frame` | `{combatId, tick, actors[], projects[], events[], sentAtMs}` | 每 tick 一帧，30/s |
| R→C | `combat.end` | `{combatId, result, ticks, experience, kills, totalDamage, stats}` | `result` 为 DQQ `EnumCombatResult` 名 |
| C→R | `combat.replay` | `{replayId?, file?, characterId?, mode, hold?}` | 回放一场录像（服务器录像或录像文件）；没有录像时用角色开新战斗 |
| C→R | `combat.ready` | `{combatId}` | 开场就绪：`hold: true` 时 runtime 发完第一个 tick 的帧后等它（最多 15 秒），再从下一个 tick 起按节奏推送 |
| C→R | `combat.stop` | — | 中止当前战斗 |
| C→R | `cmd.*` | — | 预留（D7），runtime 回 `cmd.ack {accepted:false, reason:"reserved"}` |
| R→C | `error` | `{message}` | `re` 指向出错的请求 |

### 3.1 ActorState（`actors[]`、`projects[]`）

`{id, kind:"player"|"enemy"|"project", x, y, radius, angle, hp(0~1), life?, maxLife?, moving, name?, mob?, model, targetX?, targetY?, oversize?}`
`radius`：DQQ 的体型半径（`TileConst.TileRadius` = 0.5 为标准：角色、普通怪；精英 1.0，守卫与首领 1.5）。客户端按"体型 = radius / 0.5"缩放模型，见 architecture §5.2。`oversize`：特殊标记的怪物（runtime `WowLook.Oversize`），不受体型占地上限。
`moving`：DQQ 的 `IsMoving`，与位移一致，可靠。客户端收到即显示，不缓冲（architecture §5.2）。
`angle`：DQQ 只给投射物设朝向，角色和怪物恒为 0。客户端据此推出朝向：移动时面向移动方向，站立时面向 `targetX/targetY`（DQQ 当前目标的战场坐标，没有目标时为空）。
`model` 为外观关键字：`@HumanFemale`（人类女性 NPC 外观）或生物模型路径关键字（如 `Crab`、`Wolf`），客户端在 CreatureDisplayInfo 中解析为模型编号；也可直接给数字编号。
坐标为 DQQ 战场坐标（double），客户端按 §5.2 换算成 WoW 坐标。

- 旋转技能的投射物（用户 2026-11-10）：照常飞；碰到敌人的区域事件 `action = projectileSpin`，`from` 是这个投射物，客户端让这个投射物施放事件的 `spell`（旋风斩）。`projects[]` 的 `spin` 标出这种投射物（客户端现在不用）。

- `actors[].rarity / nameColor / rank`（2026-11-12，PLAN-0006 B，只对怪物）：DQQ 稀有度（`EnumMobRarity` 名）、名字颜色（RRGGBB）、头像边框的 WoW 生物等级（`CreatureQuery` 的 rank）。客户端的显示在 PLAN-0004 做；稀有度的身上光环已并进 `auras`。
- `actors[].glow`（2026-11-13，PLAN-0004 C，只对怪物）：整只怪的外发光颜色（RRGGBB，`mobs.json` rarities 的 `glow`）。
- 客户端对这几项的显示（PLAN-0004 C，2026-11-13）：回放期间所有单位头顶显示姓名板（名字 + 血条，英雄本体也有，不限 20 码，不显示等级；死了就没有），回放结束恢复玩家自己的姓名板设置；`nameColor` 给姓名板上的名字着色（白色不处理）；`rank` 写进 `CreatureQuery` 的回答（同名不同 rank 的怪物各占一个生物 entry），选中时头像带银龙、金龙；`glow` 进模型着色器的边缘光。清场时这些标记一并清掉。
- 区域事件 `action = cone`（2026-11-12，顺劈）：`from` 转向区域中心（没有就转向 `target`），施放 `spell`，只播施法。`action = coneSwing`（挥砍类技能的顺劈）：`from` 施放 `spell`，只播特效、不播特效包自带的动作（客户端 Exile 开关），挥砍由伤害事件带出。
- runtime 生成的事件 `DashStart`（2026-11-12）：英雄（`from`）开始冲刺，施放 `spell`（只播施法）；冲刺期间英雄的 `auras` 带冲刺光环。`DurationStart` 带 `hit: false` 时只播施法；runtime 生成的 `ChargeUpStart`（`from` 读条 `spell`，`amount` = 读条秒数）/ `ChargeUpEnd`（打断读条）：技能的蓄力（`chargeUps`）。
- 伤害事件 `action = bothHands`（2026-11-12，迅猛攻击）：主手挥砍带一半伤害，0.2 秒后副手挥砍（HITINFO_LEFTSWING）带其余。伤害事件 `offHand: true`：双持英雄这一下用副手挥砍。
- 伤害事件 `action = special / special2H`（2026-11-12）：不带基础、核心标签的英雄挥砍；`from` 向 `target` 施放 `spell`（英勇打击 / 猛击：特殊攻击动作 + 命中闪光），数字按法术伤害显示。
- `actors[].auras`（2026-11-11）：这个单位此刻带着的常驻光环法术（去重、排序）；客户端写进光环栏（前 8 格），变了才发。`actors[].ring = {spell, radius, count}`：以它为中心的范围圈（`radius` 是 DQQ 距离）。

### 3.2 CombatEvent（`events[]`）

`{id, tick, kind, from?, target?, skill?, tags[], phase?, shape?, range, centerX?, centerY?, element, amount, critical, killed, success, duration?, action?, spell, school}`
`kind` 取 DQQ `EnumLogType`：CastSkill、TriggerSpell、SkillRangeAndTarget、DamageTaken、HealingTaken、Killed、ActorIsDown、ActiveAura（光环开启）；另有 runtime 生成的 `DurationStart`（`target` 身上出现了持续效果 `duration`）。客户端据此选动画、特效和飘字（architecture §5.4）。

事件 → 客户端表现（PLAN-0004 A，实现在客户端 `net/exile.rs` 的 `play_event`；动作与法术怎么选见 `docs/skill-presentation.md`）：

| `kind` | 客户端表现 |
|--------|------------|
| `CastSkill`、`ActiveAura` | `from` 施放 `spell`（`SMSG_SPELL_GO`：施法动作、自身特效、投射物、命中特效）；`hit: false` 只播施法；近战类 `action` 不在这里播（挥砍随伤害事件）；`channel` 类保持引导姿势 |
| `SkillRangeAndTarget` | 按 `action`：`area` 在区域中心播区域特效（按 `scale` 缩放）；`cone` / `coneSwing` 施放者前方的锥形；`ray` 两端各放一个标记画光束；`projectileSpin` 由投射物施放旋风 |
| `DamageTaken` | 近战类 `action`（`melee`、`mobAttack`、`bothHands`）是一次挥砍（`AttackerState`，带伤害数字）；其余发 `SpellDamageLog`，数字按 `school` 着色，`critical` 放大，没有 `action` 的（持续伤害）按周期伤害显示；`special` / `special2H` 先施放特殊攻击 |
| `HealingTaken` | `SpellHealLog`（绿色数字） |
| `Killed`、`ActorIsDown` | `target` 血量置 0，客户端播死亡动作与叫声；怪物从帧里消失后尸体留 3 秒再移除 |
| `DurationStart` | `target` 身上播 `spell`（战吼之类）；常驻的部分走 `actors[].auras` |
| `ChargeUpStart` / `ChargeUpEnd`、`DashStart` | 读条（`SpellStart`，`amount` 秒）/ 打断；冲刺的施法 |

`success: false` 的事件不播。投射物不走事件：`projects[]` 里出现就从发射点向它飞（借 `spell` 的飞行模型，跟着 DQQ 的位置走），从 `projects[]` 消失就在最后的位置爆开，命中时刻以 DQQ 为准。

PLAN-0006 D 起，runtime 按 `config/mapping/skills.json` 替客户端判定好演法：`action`（角色动作，如 melee、shoot、projectile、cast、channel、spin、nova）、`spell`（借用的 WoW 法术 id，客户端当作 `SMSG_SPELL_GO` 播放；0 = 不播）、`school`（伤害学派，飘字颜色）。`duration`：持续伤害/治疗来自的持续效果，或 `DurationStart` 的持续效果。另有 `scale`（区域特效的缩放）与 `hit`（false = 只播施法）。`projects[]` 的每项多一个 `spell`：这个投射物借用的 WoW 飞行法术（0 = 不画）。

## 4. 实测（2026-11-06，Windows，runtime 自检，同进程回环）

| 项 | 结果 |
|----|------|
| 帧大小 | 平均约 10.8 KB，最大约 13.5 KB |
| 推送节奏 | 平均 33.32 ms，p99 33.43 ms |
| 发出→收到延迟 | 平均 0.67 ms，p99 5 ms，最大 11 ms |
| 到达间隔 | 平均 33.3 ms，p99 37.7 ms，最大 43.6 ms |

结论：WebSocket 传输满足要求，客户端先不加缓冲（D9）。

## 5. 插件消息（Lua 插件 ↔ 桥接）

界面插件不直接连 runtime：它用 WoW 1.12 自带的插件消息和客户端里的桥接对话，桥接再转成上面的 runtime 消息。

- 插件 → 桥接：`SendAddonMessage("EXILE", "<verb>;<arg>;…", "PARTY")`。桥接从 `ClientCommand::AddonMessage` 收到。
- 桥接 → 插件：桥接合成一条 `LANG_ADDON` 的聊天（发送者是玩家自己），客户端触发 `CHAT_MSG_ADDON`：`arg1` = `EXILE`，`arg2` = `<verb>;<arg>;…`。
- 字段用 `;` 分隔（`|` 是聊天转义字符，不能用）；文本里的 `;`、`|` 换成空格。
- 实现：客户端 `net/exile.rs`（`on_addon`、`addon_say`），插件 `addons/ExileUI/ExileUI.lua`。

| 方向 | 消息 | 说明 |
|------|------|------|
| 插件 → 桥接 | `ping;<x>` | 往返测试，桥接直接回 `pong;<x>` |
| 插件 → 桥接 | `replay.list` | 桥接发 runtime `ui.replay.list`（当前角色） |
| 插件 → 桥接 | `replay.play;<id>` | 回放这场录像；`id` 为空表示最近一场（没有录像就开新战斗）。桥接走读条传送，加载完发 `combat.replay` |
| 桥接 → 插件 | `replay.begin;<总数>` | 列表开始 |
| 桥接 → 插件 | `replay;<id>;<说明>` | 一条录像（最多 10 条，新的在前）；说明如 `11-06 19:50  TimeOut  30s  XP 40  dmg 52` |
| 桥接 → 插件 | `replay.end` | 列表结束 |
| 插件 → 桥接 | `replay.export;<id>` | 把服务器上的这场录像导出成文件（runtime `ui.replay.export`），回 `exported;<文件名>` |
| 插件 → 桥接 | `replay.files` | 列出录像文件（runtime `ui.replay.files`） |
| 插件 → 桥接 | `replay.file;<文件名>` | 回放这个录像文件（`combat.replay` 带 `file`） |
| 桥接 → 插件 | `file.begin;<总数>` / `file;<文件名>;<说明>` / `file.end` | 文件列表，格式同录像列表 |
| 桥接 → 插件 | `exported;<文件名>` | 导出完成 |
| 桥接 → 插件 | `error;<文字>` | 出错（runtime 的 `error`、回放进行中等） |

说明文字以**创建战斗的角色名**和录像自己的名字（`Name`，有才显示）开头，例如 `1a  10-30 04:11  Clear  29 ticks  XP 160  dmg 11692`。角色名取自录像 `ReplayDetail` 的 `FriendDTOs` 中 Id 等于 `CreateActorId` 的 `DisplayName`。中文名在英文版客户端字体里显示为空白。

- `character.refresh`（插件 → 桥接，用户 2026-11-09）：桥接重新发 `ui.character.list`，用结果里当前角色的 `dress` 给本体换装备（自由模式立即换，回放中等回家），回 `character;<文字>`。

### 5.1 录像文件

- 格式与 DQQ 网页版的导出、导入相同：`CombatReplayEntity` 的 JSON 再 Base64，一行文本（DQQ `ExportReplay` / `PlayReplay`）。网页版导出的文件可以直接放进来回放，反之亦然。
- 目录：仓库根的 `replays/`（`exile-play` 传 `--replays`；不进 git）。文件名 `dqq-replay-<录像 id 前 8 位>.txt`，同一场重复导出会覆盖。
- 回放文件按种子重新模拟，不需要是当前账号的角色。runtime 只认目录里的文件名，不接受路径。
- runtime 消息：`ui.replay.export {replayId}` → `ui.replay.export.result {file, path}`；`ui.replay.files` → `ui.replay.files.result [{file, id, playTime, ticks, result, experience, totalDamage, name, character}]`；`combat.replay` 新增可选 `file`。`ui.replay.list.result` 同样新增 `name`、`character`。
- 实现：`src/Exile.Runtime/Replays/ReplayFiles.cs`。

### 5.2 对应表审阅
- 聊天命令 `.lineup mobs` → runtime `ui.mapping.mobs` → `ui.mapping.mobs.result [{mob, displayId, boss}]`，桥接把它们排成一排（`.lineup`，dev-workflow §4.0.1）。
- 桥接给单位起名字：先发 `CreatureName {entry, name}`（相当于 `SMSG_CREATURE_QUERY_RESPONSE`），再建带 `OBJECT_FIELD_ENTRY` 的单位；客户端发来的 `CreatureQuery` 也按同一张表回答。
- `combat.begin` 新增 `heroes {DQQ 角色 id: dress}`：种子里每个英雄穿着录像当时装备的外观（`equipment.json`）。客户端给扮演创建者的玩家本体换上（每个装备槽都设，种子里没有的槽清空），其他英雄建成穿装备的玩家式单位；回到自由模式时本体换回角色列表里的装备。`ui.character.list.result` 的每项新增 `dress`（现在的装备）。
- `combat.prepare {replayId, file, characterId}` → `combat.prepare.result {map, scene, wowMap, center:[x,y,z], note, clear}`（或 null；`clear` 为清小摆件的正方形边长，码，null = 200）：开打前问录像的战场（`maps.json`），客户端传送过去（`wowMap` 为任意 Map.dbc 地图，可跨大陆；加载画面至少 0.5 秒）、读那里的地形，加载完再发 `combat.replay`。5 秒没有回答就用上一次的战场。
- 怪物的 `model` 现在是数值 display id（runtime `MobLooks`），客户端直接使用。
- 穿装备的人形：ActorState（及 `ui.mapping.mobs.result` 的项）可带 `dress {race, gender, skin, face, hair, hairColor, items: [{slot, displayId, inventoryType, itemClass, itemSubclass}]}`。有它时客户端建玩家式单位（guid 高位 0，种族/性别的本体 display，`PLAYER_BYTES` 长相，`PLAYER_VISIBLE_ITEM_n_0` 装备），装备的物品模板（entry = 800000 + 外观 id）和名字（`PlayerName`）先于单位下发，客户端的 `ItemQuery`、`NameQuery` 也按同一份回答。体型按 DQQ 半径缩放，不做占地上限（人物模型的包围盒含整个动画范围，会被缩得过小）。

以后的界面（装备、技能等）沿用同一方式：插件发 `<verb>;…`，桥接对应到 runtime 的 `ui.*`。

## 变更记录
| 日期 | 修改人 | 说明 |
|------|--------|------|
| 2026-11-06 | Claude | v1 草案，含首次实测 |
| 2026-11-06 | Claude | 新增 §5 插件消息；ActorState 增 targetX/targetY |
| 2026-11-07 | Claude | §5.2 对应表审阅（`ui.mapping.mobs`、生物名字）；怪物 `model` 改为数值 display id |
| 2026-11-12 | Claude | ActorState 增 `rarity`、`nameColor`、`rank`；区域事件新增 `action = cone`（PLAN-0006） |
| 2026-11-13 | Claude | §3.2 加"事件 → 客户端表现"表；ActorState 增 `glow`；姓名板、名字颜色、rank 的客户端显示（PLAN-0004） |
