# PLAN-0005：界面插件与 DQQ service

| 字段 | 内容 |
|------|------|
| 编号 | PLAN-0005 |
| 状态 | 待开始 |
| 负责人 | （待认领） |
| 创建日期 | 2026-11-07 |
| 最后更新 | 2026-11-07 |
| 完成度 | 0% |
| 关联设计 | docs/architecture.md §4、D5、D18；docs/bridge-protocol.md §5 |
| 依赖计划 | 无（可与 PLAN-0004、0006 并行） |

## 目标
用 Lua 插件做出 DQQ 的主要界面，所有操作通过 DQQ 现有 service 完成（runtime 只转接，不写业务逻辑），界面默认中文。

## 现状（2026-11-07）
- 插件 `addons/ExileUI`：Replays 窗口（服务器录像、录像文件、导出、回放），英文界面。
- 通道：插件 ↔ 桥接用 WoW 插件消息（`EXILE` 前缀，`verb;参数`），桥接 ↔ runtime 用 `ui.*`（bridge-protocol §5）。
- 客户端只有英文字体，中文显示为空白。

## 可用的 DQQ service（runtime 已注册，在线模式）
| service | 主要方法 | 对应界面 |
|---------|----------|----------|
| `ICharacterService` | GetCharacterDTO、SetCombatArcheType、SelectMap、ChangeDifficulty、ChangeGreaterRiftLevel、AutoGreaterRift | 角色、地图选择 |
| `IItemService` | ActorInventory、ActorStorage、EquipItem、UnEquipItem、StorageIn/Out、SellBackpackItem、LockItems、QueryFromFilter | 装备、背包、仓库 |
| `ISkillService` | GetAllSkills、PickSkill、PickSkills、ReplaceSkills | 技能 |
| `IFilterService` | GetAllFilter、CreateFilter、UpdateFilter、ActiveFilter、DeleteFilter | 物品过滤器 |
| `IReforgingService` | CheckReforgingFormular、ReforgingItem、Save/LoadExpectSlot | 重铸 |
| `IReplayService` | 录像列表与读取（已用） | 回放 |
| `IIdleService` 等 | 挂机相关（PLAN-0007） | 挂机进度 |

另有 IActorPassiveSkillService（特质）、IActionBarService、ICurrencyService、IProgressService 等，按界面需要接入。

## 设计
1. **消息格式**：保持 `verb;参数` 的文本格式（插件解析简单）。列表用 `X.begin;总数` / `X;…` / `X.end`。单条超过约 200 字节时分段发送（`X.part;序号;内容` 由插件拼接）。
2. **请求与回复对应**：插件请求带一个短序号 `verb;#12;…`，回复带同一序号，避免两个窗口同时请求时串线。
3. **runtime 侧**：每个界面操作对应一个 `ui.*` 消息和一个 DQQ service 方法，在 `MessageRouter` 加分支；返回 DTO 由桥接转成插件行。复杂对象（物品词缀）由 runtime 先格式化成文字（用 DQQ 的中文资源）。
4. **中文字体**：选一个允许再分发的中文字体（候选：思源黑体 / Noto Sans SC、霞鹜文楷，均为 OFL），作为客户端的额外字体加载。做法二选一：
   - 客户端改动：在 `ui_text/engine/faces.rs` 的字体列表后加一个"缺字回退"字体，从我们的数据目录读取；
   - 或做一个补丁 MPQ 覆盖 `Fonts\FRIZQT__.TTF` 等（不改客户端代码，但改变英文字形）。
   推荐第一种。
5. **文本语言**：插件里的固定文字用中文；DQQ 返回的名称、描述用 DQQ 的中文资源（runtime 设置 `CultureInfo` 为 zh-Hans）。

## 任务清单
### A. 基础
- [ ] 中文字体：选字体并确认许可；客户端加载缺字回退字体；测试中文名、中文界面显示
- [ ] runtime 使用 DQQ 中文资源（文化设置），确认技能名、物品名为中文
- [ ] 消息规范：序号、分段、错误回复；更新 bridge-protocol §5
- [ ] 插件框架：`ExileUI` 拆成多个文件（通用通信、窗口模板、各界面），统一的窗口样式与打开方式（按钮栏或 `/exile` 子命令）

### B. 界面
- [ ] 角色面板：名字、等级、战斗风格、主要属性（只读）
- [ ] 装备与背包：装备栏 10 格、背包列表、穿戴/卸下、出售、锁定；物品提示（词缀）
- [ ] 仓库：存入、取出
- [ ] 技能：主动与辅助技能槽，选择与替换
- [ ] 特质（天赋）
- [ ] 地图选择：地图、难度、大秘境层数（开战本身在 PLAN-0007 的地图仪）
- [ ] 物品过滤器：列表、启用、编辑（编辑可以先只做启用和切换）
- [ ] 重铸
- [ ] 回放窗口完善：中文、分页、按地图/结果筛选

### C. 检查
- [ ] 每个界面操作后，用 DQQ 网页版确认数据一致
- [ ] 错误（网络、DQQ 拒绝）在界面上有提示

## 验收标准
- 不打开 DQQ 网页版，只在游戏里就能完成：查看角色、整理装备和背包、调整技能、选择地图与难度、看回放。
- 界面文字与 DQQ 返回的名称都显示为中文。
- 所有操作走 DQQ service，结果与网页版一致。

## 待确认
1. ~~中文字体的选择~~ ✅ 用户要求选免费字体，免费的里面选清晰的（2026-11-07）。两者都是 SIL OFL 免费字体，选**思源黑体（Source Han Sans / Noto Sans SC）**：无衬线，小字号更清晰。
2. 界面风格：仿 WoW 原生窗口，还是更接近 DQQ 网页版布局。
3. ~~第一批界面~~ ✅ 装备与背包、技能、地图选择（2026-11-07）。

## 进度日志
<!-- 追加，不要删除旧记录 -->
- 2026-11-07（Claude）：由 PLAN-0003 的验证结论拆出创建；同日细化为可执行计划（可用 service 盘点、消息规范、中文字体方案）。
- 2026-11-07（Claude）：记录用户对待确认问题的答复。

## 讨论 / 交接
