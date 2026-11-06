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
| C→R | `combat.stop` | — | 中止当前战斗 |
| C→R | `cmd.*` | — | 预留（D7），runtime 回 `cmd.ack {accepted:false, reason:"reserved"}` |
| R→C | `error` | `{message}` | `re` 指向出错的请求 |

### 3.1 ActorState（`actors[]`、`projects[]`）

`{id, kind:"player"|"enemy"|"project", x, y, radius, angle, hp(0~1), life?, maxLife?, moving, name?, mob?, model, targetX?, targetY?}`
`moving`：DQQ 的 `IsMoving`，与位移一致，可靠。客户端按 5 帧一段演出移动，整体延后 5 帧显示（architecture §5.2）。
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

## 变更记录
| 日期 | 修改人 | 说明 |
|------|--------|------|
| 2026-11-06 | Claude | v1 草案，含首次实测 |
