# PLAN-0003：验证（Spike）—— 方案 T 端到端打通

| 字段 | 内容 |
|------|------|
| 编号 | PLAN-0003 |
| 状态 | 进行中 |
| 负责人 | Claude |
| 创建日期 | 2026-11-06 |
| 最后更新 | 2026-11-06 |
| 完成度 | 75% |
| 关联设计 | docs/architecture.md（已定稿） |
| 依赖计划 | PLAN-0002 |

> 文件名沿用早期的 `spike-csharp-server`，内容已按方案 T 重写（不再有 WoW 协议服务器）。

## 目标
用最小范围验证方案 T 的两个最大风险，并打通一条完整链路：
1. **DQQ.Web.Component 能否在普通 .NET 进程里以在线模式工作**（D14、D15）。
2. **benilla 能否不连服务器运行，并由外部消息驱动画面**（§7，参考 Azeroth Survivors）。

打通的链路：选人画面列出 DQQ 角色 → 进入一个平地场景 → 后台跑一场 DQQ 战斗 → 客户端按渲染帧演出（英雄打死螃蟹）。

## 范围
- 包含：`source/` 与 DQQ DLL；Exile Runtime 最小版本；WebSocket + JSON 最小协议；benilla 分支最小改动（无服务器启动、桥接模块、单位生成/移动/动画/飘字/死亡）；最小 Lua 插件双向消息。
- 不包含：启动器、藏身处设施、完整界面、读条传送、对应表的完整内容、手动指令、中文字体（验证阶段界面可先用英文或拼音）、性能优化、发布流程。

## 前置条件（需要用户提供或确认）
- [x] WoW 1.12.1（build 5875）enUS 客户端：已解压在 `C:\wow_survival\wow-data\Data`（压缩包 `C:\wow_survival\downloads\1.12.1-5875_enUS.zip` 无需再解压）
- [x] fork 原版 benilla → `rehee/benilla`（2026-11-06）；本仓库 GitHub 公开仓库 `rehee/ExileofAzeroth` 已存在（空），本地已配置 origin，尚无提交
- [x] 首次提交并推送本仓库；`rehee/benilla` 已加为子模块 `client/`（2026-11-06，提交 3479cc1、6da176a）
- [ ] 开发机（Windows）安装 Rust 工具链、VS 2022 C++ 生成工具（Rust 在 Windows 上编译需要）、.NET 8 SDK（用户已授权 AI 操作）
- [x] `config/dev.local.json` 已存在（DQQ 服务器地址与测试账号，见 D16）

## 任务清单

### A. 仓库与 DQQ 接入
- [x] 建 `source/`：从 `C:\code\dqq\DQQ` 编译 DQQ、DQQ.Core、DQQ.Web.Component，连同**全部依赖**复制进去（D23）。50 个文件；DQQ 的中文资源内嵌在主程序集里，没有单独的附属资源 DLL
- [x] 更新脚本 `tools/update-dqq.ps1`（固定 SDK 8.0.416，跳过 DQQ 本地化同步以免改写 DQQ 目录），`source/dqq/VERSION.json` 记录版本
- [x] 建 `src/Exile.Runtime`（.NET 8 控制台），只引用 `source/dqq/` 里的 DLL

### B. Runtime 最小版本（风险 1）
- [x] 在非浏览器进程中初始化 DQQ.Web.Component 的 service（在线模式），读取 `config/dev.local.json` 登录
- [x] 调用 CharacterService 取角色列表
- [x] 跑一场 `Map_1_1` 战斗，每 tick 产出渲染帧（样本存 `logs/selftest-frames.jsonl`）
- [x] 后台模式：整场直接算完（900 tick 约 2.8 s）
- [x] 结论：DQQ 原版 `AddServices` 可直接用；只需替换三处浏览器依赖——`IClientRepository`（改用内存实现 `MemoryClientRepository`）、`NavigationManager`、`IJSRuntime`。DQQ 零改动

### C. 桥接协议最小版本
- [x] Runtime 开 127.0.0.1 WebSocket（自写握手，无 ASP.NET 依赖）；消息 `ui.character.list`、`combat.start/begin/frame/end/stop`、预留 `cmd.*`
- [x] `docs/bridge-protocol.md` 初稿（含实测数据）

### D. benilla 分支（风险 2）
- [x] 把 `rehee/benilla` 作为子模块加入 `client/`（D22、D25）；本项目代码集中在自己的 crate（参考艾泽拉斯之潮 `client/crates/hoa`）
- [x] 研究客户端结构，结论（2026-11-06）：
  - `benilla-world` 是不带游戏的世界渲染库（地形、模型、光照）；`worldview` 是它的无服务器查看器。但角色/怪物外观、换装、名字板、FrameXML/Lua 插件、GlueXML 选人都在 `benilla-app` 里。原型（C:\wow_survival）基于 benilla-world，为显示单位自写了约 5600 行。
  - `benilla-app` 的网络层：读线程把服务器包转成 `benilla_protocol::SessionEvent`，经通道交给 `apply_net_updates` 生成实体；玩家操作以 `ClientCommand` 发出（`net.rs`、`net/io.rs`）。
  - **采用的做法**：沿用完整客户端 `benilla-app`，只替换会话来源——新增本项目 crate（桥接模块），从 runtime 的 WebSocket 收消息，**直接合成 `SessionEvent`**（登录成功、角色列表、进入世界、创建单位、移动、施法、伤害、插件消息），并把 `ClientCommand`（移动、插件消息等）转回 runtime。不实现 WoW 线路协议；客户端现有的模型、动画、名字板、Lua 插件、选人界面都能直接用，与艾泽拉斯之潮"完整客户端 + 插件"的形态一致。对 benilla 的改动只在会话启动处加一个可切换的钩子，代码集中在本项目自己的 crate。
- [x] 不连服务器启动，进入北郡修道院门前，第三人称镜头（D8）
- [x] 环境变量自动登录 → 领域 "Exile of Azeroth" → 角色列表来自 runtime（DQQ 角色，人类女性，职业按 `WowLook`）→ 自动进入世界（`WOW_CHAR`）。装备外观未做
- [x] 桥接模块连 WebSocket，按 `combat.frame` 渲染：生成英雄与螃蟹（模型按关键字从 CreatureDisplayInfo 解析）、`MonsterMove` 平滑移动、`AttackerState` 挥砍、血量更新、战斗结束清场。施法特效、死亡表现未细做
- [x] DQQ 坐标 → WoW 坐标（3 码/单位，原点为北郡出生点，高度交给客户端贴地）。待改：战场应以原点为中心（DQQ 坐标减 5）

### E. Lua 插件最小版本
- [x] 插件收到 runtime 的消息并显示（录像列表，2026-11-06）
- [x] 插件发请求给 runtime 并收到回复（`replay.list` → `ui.replay.list` → 列表；`replay.play` → 回放）。用户手动验收通过

### F. 收尾
- [ ] 写验证结论：两个风险的结果、遇到的问题、对架构文档需要的修订
- [ ] 根据结论拆出后续计划（藏身处、完整 UI、对应表、启动器、发布）

## 验收标准
1. Runtime 在普通 .NET 进程中以在线模式登录 DQQ 服务器，取到角色列表。
2. benilla 分支不连任何 WoW 服务器即可启动并显示场景。
3. 选人画面显示 DQQ 角色（人类女性模型）。
4. 一场 DQQ 战斗在客户端里被完整演出：英雄攻击螃蟹、有动画与伤害数字、螃蟹死亡；结果与 DQQ 计算一致。
5. Lua 插件与 runtime 能双向通信。
6. 产出验证结论文档。

## 进度日志
- 2026-11-06（Claude）：按方案 S 创建（已作废）。
- 2026-11-06（Claude）：架构定稿为方案 T，本计划按方案 T 重写。等待用户说开始，并补齐前置条件。
- 2026-11-06（Claude）：检查环境：AI 能用的命令行是用户电脑上的 Linux 虚拟机（无网络、不是 Windows），云端环境的 GitHub 登录失效，因此无法直接安装 Windows 工具链或 fork。待用户选择：AI 通过屏幕操作完成，或用户手动执行。

- 2026-11-06（Claude）：完成 A、B、C。风险 1 通过：DQQ.Web.Component 在普通 .NET 进程以在线模式工作（登录、角色列表、战斗），DQQ 零改动。发现 DQQ 自带节流在 Windows 上每 tick 约 45 ms（计时精度 15.6 ms），改由 runtime 的 TickPacer 控制节奏 + timeBeginPeriod(1)，实测 33.32 ms。WebSocket 回环延迟平均 0.67 ms、p99 5 ms。执行方式：AI 在资源管理器双击 `tools/run-*.cmd`，结果看 `tools/logs/`。下一步：D（benilla 客户端）。

- 2026-11-06（Claude）：完成 D 的主体。客户端 `net/exile.rs`（rehee/benilla）把 runtime 消息合成为 `SessionEvent`，对上游只改 3 个文件 12 行。实测：自动登录→选人→进世界→10 秒后开战→英雄与螃蟹在北郡演出→战斗结束清场；客户端退出时 runtime 随之退出（D26）。首次运行需接受 EULA/TOS（用户授权 AI 点击），客户端状态写在仓库根 `benilla-config/`（已忽略），其中预设 `realmName`。剩余：E（Lua 插件双向消息）、F（结论）、若干表现细节（隐藏玩家本体、战场居中、施法特效、死亡、名字/血条、怪物数量与缩放）。

- 2026-11-06（Claude）：工作记录归档：新增 `docs/dev-workflow.md`（编译、运行、脚本、环境变量、首次运行、已知问题）。代码已推送（ExileofAzeroth 8e0471f、rehee/benilla dc2e4283）。**下一步（接手者从这里开始）**：E——最小 Lua 插件（客户端 `ClientCommand::AddonMessage` ↔ runtime `ui.*`，runtime → 插件用 `SessionEvent::Chat` 的插件消息）；F——写验证结论（建议 `docs/spike-results.md`）并拆出后续计划（藏身处、战斗表现、完整 UI、启动器、发布）。

- 2026-11-06（Claude）：用户补充 D27–D29（自由模式 / 战斗回放模式 / 回放期间操作与镜头），已写入 architecture.md。实现：runtime 新增 `ui.replay.list`、`combat.replay`（IReplayService，按录像重放，无录像时开新战斗）；客户端进入世界为自由模式，聊天 `.replay`（临时入口）→ 锁操作 + 传送到战场加载画面 → 客户端 WorldportAck（加载完成）后请求回放，玩家本体扮演战斗创建者 → 结束清场、传回、加载完成后交还操作。用户澄清"读条"指加载画面的进度条而非炉石施法，相应改动已在工作区（16:56 另一会话所写，含 Windows 套接字错误 997/10035/10060 视为读超时的修复）。18:32 无人值守实测全流程通过、无断线。问题：战场与自由模式都在北郡，加载 0.0 s，进度条一闪而过——待用户决定战场位置。另：Windows 中文输入法会截走游戏按键（已记入 dev-workflow）。

- 2026-11-06（Claude）：用户反馈三点并已修复验证：① 切换时加载画面至少 0.5 秒（`loading_screen.rs`，仅 Exile 模式；实测 0.5 s，加载结束后才开战）；② 自由模式不能移动/转视角、③ Esc 无菜单——根因是按键绑定属于账号数据，客户端等服务器的 `SMSG_ACCOUNT_DATA_MD5` 才加载，桥接未发；现进入世界时补发全空摘要，探针实测 W/ESC/BUTTON2 已绑定、按 W 角色前进。新增诊断探针配置（见 dev-workflow §4.1）。

- 2026-11-06（Claude）：用户反馈战斗演出四点并已修复：① 镜头——回放期间（`exile_spectating`）不再自动转回身后，右键拖动只转镜头、不转身体；② 单位悬空——桥接读取战场附近 ADT 地形，按地面高度放置；③ 没有移动——根因是 `MonsterMove` 的路径必须以起点开头，只给终点时客户端不装样条，单位不动；现发 `[起点, 终点]`。朝向：DQQ 角色/怪物的 `Angle` 恒为 0，改为移动时朝移动方向、站立时朝目标（协议 ActorState 新增 `targetX/targetY`）；④ 阴影——原版淡色阴影其实已投影但几乎看不见，Exile 模式改画半透明黑色圆盘。实测：一场 900 tick 回放发出约 5600 次移动、110 次转向；左键拖动镜头后保持不动。右键拖动 AI 无法模拟，待用户验证。dev.local.json 新增可选 `rustLog`（→ RUST_LOG）。

- 2026-11-06（Claude）：按用户要求撤掉黑色圆盘阴影（太出戏），恢复 WoW 原版阴影，客户端 `blob_shadow.rs` 回到上游原样。

- 2026-11-06（Claude）：按用户规则改移动演出：5 帧一段（帧延后 5 帧显示，每段一条多点路径、按平均速度走完），段与段首尾相接，移动动画不重播；停下后才回到站立。实测一场回放发出的移动指令从约 5600 条降到约 1200 条。检查了日志：DQQ 的 `moving` 标志可靠（见 architecture §5.2）。

- 2026-11-06（Claude）：缩放由 1 单位 3 码改为 2 码（用户试用）；移动改用跑步动画（战斗中行走速度 0.05，本体实测 gait=5 Run，播放速率约 0.12 倍——DQQ 单位每秒只走约 0.8 码，跑步动作会显得很慢）。

- 2026-11-06（Claude）：回放期间走/跑动作改为标准速度（`select.rs` 的 `scaled_rate` 在 `exile_spectating` 时返回 1），实测跑步 rate=1.00；自由模式不变。

- 2026-11-06（Claude）：完成 E。插件 `addons/ExileUI`（英文界面，enUS 字体没有中文字形）：右上角 Replays 按钮 → 录像列表 → 点一条回放；`/exile` 开关，`/exile ping` 测往返。消息走 WoW 插件消息（`SendAddonMessage` / `CHAT_MSG_ADDON`，前缀 `EXILE`，字段用 `;` 分隔），桥接翻译成 runtime 的 `ui.*` / `combat.replay`（协议见 bridge-protocol §5）。`exile-play` 启动前把 `addons/*` 复制到 `benilla-config/AddOns/`。踩坑：① 客户端在会话开始时锁住聊天显示，要等进入世界的初始化放开，桥接流程下一直没放开，所有聊天和插件消息都被压住——Exile 模式下不再上锁（`ui_chat/net.rs`，上游改动 +1 处）；② 插件消息要等发送者名字解析，桥接现在应答 `NameQuery`（自己的名字）。另：AI 的模拟鼠标点击点不中插件按钮，用户手点正常，原因未查。用户手动验收通过。

- 2026-11-06（Claude）：用户追加需求：录像文件导出与导入回放（DQQ 网页版同格式，Base64 的 `CombatReplayEntity` JSON）。插件窗口分 Recent / Files 两栏：Recent 每条可 Export 到 `replays/dqq-replay-<id8>.txt`；Files 列出目录里的文件，点一条按文件回放。列表说明以角色名和录像名开头（取自录像里的 `FriendDTOs`）。实测：导出 88b608ed 成功并能解回；用户给的 c6bc687b（角色 "1a"，别人的角色）能列出并回放。注意：c6bc687b 原记录 29 tick 通关（Clear），重新模拟在约 30 tick 以 TimeOut 结束——这份录像是 10-30 的版本，很可能 DQQ 逻辑之后有改动导致重放不一致（文件里 `GameVersion` 为空，无法判断版本）；10-31 的 88b608ed 重放结果一致。部分代码由另一会话在 21:45 写好未提交，本次接手补上名字并测试。

- 2026-11-07（Claude）：用户追加体型要求：体型 = DQQ 半径 / 0.5（人类女为 1）；英雄缩放 = 体型；怪物缩放 = 模型自身比例 × 体型，非特殊标记的怪物占地不超过"体型 × 2 码"见方（模型包围盒取长宽较大值）。特殊标记在 runtime `WowLook.Oversize`（目前为空），经 ActorState `oversize` 传给客户端。实测螃蟹（display 342，包围盒 3.38 码）在体型 1 下缩到 2 码以内。精英（体型 2）、首领（体型 3）在这张图里没出现，未实测。

- 2026-11-07（Claude）：开场等待：`combat.replay` 带 `hold`，runtime 发完第一个 tick 的帧后等客户端 `combat.ready`（上限 15 秒），之后节拍器重新计时；客户端立即显示开场帧（不走 5 帧延后），每帧统计还没建好模型的单位，全部建好再等 0.8 秒（至少 2.5 秒、最多 10 秒）后发 `combat.ready`。战场里所有单位保持战斗姿势（`AttackStart`，见 architecture §5.2）。实测：开场等待 2.5 秒（模型约在 1.5 秒内全部建好），之后才开战；英雄站立时动画为 Ready（25），回到自由模式恢复 Stand（0）。

## 讨论 / 交接
- 2026-11-06（Claude）：发现 `C:\wow_survival` 已有一套接近方案 T 的原型（详见下）。fork 与工具安装暂停，等用户决定是否以它为基础。`tools/setup-dev.ps1` 已写好但**未运行**，原型目录显示 Windows 上已有 Rust 构建产物，可能不需要。
  - `csp/`：C# .NET 8，经 hostfxr **在 Rust 显示进程内**运行；直接项目引用 `C:\code\dqq\DQQ`；`CombatRenderService` 逐 tick 快照、`CombatVisualAdapter` 表现事件、`NativeBridge` 原生接口（JSON）；`Csp.Browser` 网页版用 DQQ.Web.Component + IndexedDB，在线连 pogapi2；还有 `Csp.Launcher`。
  - `wenilla/`（Arnesen/wenilla 克隆）：`crates/dqq-world-demo` 显示端，2.5 码/单位，人类女性圣骑士 + 狗头人模型，北郡修道院/闪金镇场景，无需服务器。
  - `RESEARCH.md`：Azeroth Survivors 与 DQQ 渲染接入调研。
