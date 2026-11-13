# PLAN-0005：界面插件与 DQQ service

| 字段 | 内容 |
|------|------|
| 编号 | PLAN-0005 |
| 状态 | 待开始 |
| 负责人 | （待认领） |
| 创建日期 | 2026-11-07 |
| 最后更新 | 2026-11-13 |
| 完成度 | 0% |
| 关联设计 | docs/architecture.md §4、D5、D18；docs/bridge-protocol.md §5 |
| 依赖计划 | 无（可与 PLAN-0004、0006 并行） |

## 目标
用 Lua 插件做出 DQQ 的主要界面，所有操作通过 DQQ 现有 service 完成（runtime 只转接，不写业务逻辑）。界面支持多语言（用户 2026-11-13）：先做中文和英文，**默认中文**；语言以后在启动器里选（PLAN-0008），在那之前固定用默认值。

## 现状（2026-11-07）
- 插件 `addons/ExileUI`：Replays 窗口（服务器录像、录像文件、导出、回放），英文界面。
- 通道：插件 ↔ 桥接用 WoW 插件消息（`EXILE` 前缀，`verb;参数`），桥接 ↔ runtime 用 `ui.*`（bridge-protocol §5）。
- 客户端只有英文字体，中文显示为空白。
- 现在全是英文：插件里的文字直接写在 Lua 里（`ExileUI.lua`、`ExileWardrobe.lua`），桥接发给插件的提示（`error;…`、`character;…`、录像说明）也是写死的英文；runtime 没有设置语言。

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
5. **多语言**（用户 2026-11-13）。一个语言设置贯穿三处，取值 `zhCN`（默认）/ `enUS`，以后加语言只加文件、不改代码：
   - **语言从哪来**：runtime 启动参数 `--lang`（`exile-play` 读 `config/dev.local.json` 的可选 `lang`，不写就是 `zhCN`；以后由启动器的语言选择传入）。runtime 在 `hello` 里把语言告诉桥接，桥接在插件加载后发 `lang;<语言>` 给插件。
   - **DQQ 的内容**（技能、物品、词缀、怪物、地图等的名称与描述）：不自己翻译，用 DQQ 自带的中英文资源。runtime 按语言设置 `CultureInfo`（`zhCN` → zh-Hans，`enUS` → en），发给插件的名称和描述已经是该语言的文字。
   - **我们自己的界面文字**（窗口标题、按钮、提示、报错）：全部做成 key → value 的语言表，代码里只出现 key。
     - 插件：`addons/ExileUI/Locales/zhCN.lua`、`enUS.lua`，各自填一张表 `ExileUI_Locales["zhCN"] = { REPLAYS_TITLE = "战斗回放", … }`；取字用 `L["REPLAYS_TITLE"]`，带参数的用 `string.format(L["KEY"], …)`。当前语言缺某个 key 时回退 `enUS`，再没有就显示 key 本身（一眼能看出漏了）。
     - 桥接和 runtime 发给插件的提示不再发成句的文字，改发 key 加参数（`error;#12;ERR_REPLAY_BUSY`、`msg;LOOK_SAVED`），由插件查表显示。DQQ 自己返回的错误文字（已按语言本地化）原样透传。
     - key 用大写加下划线，按界面加前缀（`REPLAYS_`、`BAG_`、`SKILL_`、`ERR_`…）。两个语言文件的 key 必须一致，加一个检查（`mapping-report` 里顺带，或单独的脚本）列出缺的和多的。
   - **字体**：中文需要第 4 条的缺字回退字体；`enUS` 不受影响。

## 任务清单
### A. 基础
- [ ] 中文字体：选字体并确认许可；客户端加载缺字回退字体；测试中文名、中文界面显示
- [ ] 语言设置：runtime `--lang`（默认 `zhCN`），`exile-play` 读 `dev.local.json` 的 `lang`（同步 `dev.example.json`）；`hello` 带语言，桥接发 `lang;<语言>` 给插件
- [ ] 确认 DQQ 的中英文资源都在 `source/dqq/` 里（`update-dqq` 目前跳过了 DQQ 的本地化同步，缺就补上收集），runtime 按语言设置 `CultureInfo`；两种语言下各核对技能名、物品名、词缀描述
- [ ] 插件语言表：`Locales/zhCN.lua`、`enUS.lua` 与取字函数 `L`（缺 key 回退英文）；把 `ExileUI.lua`、`ExileWardrobe.lua` 里现有的英文全部搬成 key
- [ ] 桥接与 runtime 发给插件的提示改为 key 加参数（现有的 `error;…`、`character;…`、录像说明等）
- [ ] 语言表检查：两个语言文件的 key 一致，代码里用到的 key 都有
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
- [ ] 回放窗口完善：分页、按地图/结果筛选（文字随语言表）

### C. 检查
- [ ] 每个界面操作后，用 DQQ 网页版确认数据一致
- [ ] 错误（网络、DQQ 拒绝）在界面上有提示

## 验收标准
- 不打开 DQQ 网页版，只在游戏里就能完成：查看角色、整理装备和背包、调整技能、选择地图与难度、看回放。
- 默认（`zhCN`）下界面文字与 DQQ 返回的名称、描述都显示为中文；把语言改成 `enUS` 重启后全部变成英文，没有混杂。
- 插件、桥接、runtime 的代码里没有写死的界面文字，全部来自语言表或 DQQ 资源。
- 所有操作走 DQQ service，结果与网页版一致。

## 待确认
1. ~~中文字体的选择~~ ✅ 用户要求选免费字体，免费的里面选清晰的（2026-11-07）。两者都是 SIL OFL 免费字体，选**思源黑体（Source Han Sans / Noto Sans SC）**：无衬线，小字号更清晰。
2. ~~界面风格~~ ✅ 仿 WoW 原生窗口（2026-11-07）。
3. ~~第一批界面~~ ✅ 装备与背包、技能、地图选择（2026-11-07）。
4. ~~语言~~ ✅ 多语言，先中文和英文，默认中文；语言选择以后放在启动器里；DQQ 内容用 DQQ 自带的中英文名称和描述，其余界面文字做成 key → value 的语言表（2026-11-13）。
5. 待核实：DQQ 的英文资源是否齐全（若某些名称只有中文，`enUS` 下先显示中文并在检查里列出）。

## 进度日志
<!-- 追加，不要删除旧记录 -->
- 2026-11-07（Claude）：由 PLAN-0003 的验证结论拆出创建；同日细化为可执行计划（可用 service 盘点、消息规范、中文字体方案）。
- 2026-11-07（Claude）：记录用户对待确认问题的答复。
- 2026-11-07（Claude）：用户确认：界面风格仿 WoW 原生窗口。
- 2026-11-13（Claude）：按用户要求改计划：由"界面默认中文"改为多语言（`zhCN` 默认、`enUS`），语言以后由启动器选择（已在 PLAN-0008 加任务）；DQQ 内容用 DQQ 资源，其余界面文字做成 key → value 语言表。改了目标、现状、设计第 5 条、任务 A、验收标准。尚未开工。

## 讨论 / 交接
