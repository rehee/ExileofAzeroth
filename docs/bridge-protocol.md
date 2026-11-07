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

### 3.2 CombatEvent（`events[]`）

`{id, tick, kind, from?, target?, skill?, tags[], phase?, shape?, range, centerX?, centerY?, element, amount, critical, killed, success}`
`kind` 取 DQQ `EnumLogType`：CastSkill、TriggerSpell、SkillRangeAndTarget、DamageTaken、HealingTaken、Killed、ActorIsDown。客户端据此选动画、特效和飘字（architecture §5.4）。

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

### 5.1 录像文件

- 格式与 DQQ 网页版的导出、导入相同：`CombatReplayEntity` 的 JSON 再 Base64，一行文本（DQQ `ExportReplay` / `PlayReplay`）。网页版导出的文件可以直接放进来回放，反之亦然。
- 目录：仓库根的 `replays/`（`exile-play` 传 `--replays`；不进 git）。文件名 `dqq-replay-<录像 id 前 8 位>.txt`，同一场重复导出会覆盖。
- 回放文件按种子重新模拟，不需要是当前账号的角色。runtime 只认目录里的文件名，不接受路径。
- runtime 消息：`ui.replay.export {replayId}` → `ui.replay.export.result {file, path}`；`ui.replay.files` → `ui.replay.files.result [{file, id, playTime, ticks, result, experience, totalDamage, name, character}]`；`combat.replay` 新增可选 `file`。`ui.replay.list.result` 同样新增 `name`、`character`。
- 实现：`src/Exile.Runtime/Replays/ReplayFiles.cs`。

以后的界面（装备、技能等）沿用同一方式：插件发 `<verb>;…`，桥接对应到 runtime 的 `ui.*`。

## 变更记录
| 日期 | 修改人 | 说明 |
|------|--------|------|
| 2026-11-06 | Claude | v1 草案，含首次实测 |
| 2026-11-06 | Claude | 新增 §5 插件消息；ActorState 增 targetX/targetY |
