# 开发与运行流程（Windows）

| 字段 | 内容 |
|------|------|
| 作者 | Claude |
| 创建日期 | 2026-11-06 |
| 状态 | 现行（验证阶段） |
| 关联 | docs/architecture.md、docs/bridge-protocol.md、PLAN-0003 |

本文给接手的人和 AI：项目怎么编译、怎么运行、结果看哪里、有哪些坑。

## 1. 目录与仓库

| 路径 | 内容 |
|------|------|
| `src/Exile.Runtime/` | C# runtime（.NET 8）：DQQ 承载、战斗渲染帧、WebSocket 桥接服务 |
| `source/dqq/` | 预编译的 DQQ 及全部依赖（D23），由 `tools/update-dqq.ps1` 生成 |
| `client/` | 子模块 `rehee/benilla`（fork 自 samwhosung/benilla）。本项目代码在 `crates/benilla-app/src/net/exile.rs` |
| `tools/` | 编译、运行、提交脚本；`tools/logs/` 为运行日志（已忽略） |
| `config/dev.local.json` | 本地配置与测试账号（已忽略，模板见 `dev.example.json`） |
| `benilla-config/` | 客户端本地状态（CVar 等，已忽略），见 §5 |

## 2. 环境

| 依赖 | 说明 |
|------|------|
| .NET SDK 8.0.416 | 仓库根 `global.json` 固定版本（与 DQQ 一致；用 SDK 10 嵌套编译 DQQ 会失败） |
| Rust 1.98.1 MSVC | 系统 PATH 里没有时，`client-build` 自动使用免安装版 `C:\wow_survival\tools\{cargo,rustup}`（可用 `EXILE_RUST_HOME` 改） |
| VS 2022 C++ 生成工具 | Rust MSVC 链接需要 |
| WoW 1.12.1 enUS 数据 | 默认 `C:\wow_survival\wow-data\Data`（可用 `WOW_DATA` 改） |
| DQQ 源码 | `C:\code\dqq\DQQ`（只读取，不修改；可用 `DQQ_ROOT` 改） |

`tools/setup-dev.ps1` 可用 winget 安装上述工具，目前机器上已具备，未运行过。

## 3. 脚本

每个 `tools/X.ps1` 都有双击启动器 `tools/run-X.cmd`。脚本输出写 `tools/logs/X.log`，状态写 `tools/logs/X.status`（running / ok / fail），结束后窗口自动关闭。

| 脚本 | 作用 | 备注 |
|------|------|------|
| `update-dqq` | 编译 DQQ 三个项目并收集全部依赖到 `source/dqq/` | 跳过 DQQ 本地化同步，避免改写 DQQ 目录 |
| `runtime-selftest` | 编译 runtime 并自检：在线登录、角色列表、后台/实时战斗、WebSocket 回环实测 | 报告 `tools/logs/selftest.json` |
| `client-build` | 编译客户端（profile `play`） | 首次约 14 分钟，增量 1–2 分钟；运行中的游戏会锁住 exe，先退出游戏 |
| `client-run` | 以普通方式运行客户端（不进 Exile 模式） | |
| `exile-play` | 一键运行：编译并后台启动 runtime → Exile 模式启动客户端 | 见 §4 |
| `commit-push` | 提交并推送；有 `tools/commit-message-client.txt` 时先提交推送 `client` 子模块 | 提交信息文件用后删除（已忽略） |
| `init-repo` / `setup-dev` | 一次性脚本 | |

AI 的执行方式：AI 的命令行是用户电脑上的 Linux 虚拟机（无网络、无 .NET、非 Windows），只能读写文件；编译和运行由 AI 在资源管理器里**双击 `tools/run-*.cmd`**（资源管理器只授权左键），再读 `tools/logs/`。新加的文件在资源管理器里不会自动出现，回到上一级再进入即可刷新。

## 4. Exile 模式运行（`exile-play`）

环境变量（脚本自动设置）：

| 变量 | 值 | 作用 |
|------|----|------|
| `EXILE_RUNTIME` | `ws://127.0.0.1:7710/` | 客户端改由 runtime 驱动，不连 WoW 服务器 |
| `WOW_USER` / `WOW_PASS` | `exile` / `exile` | benilla 的环境变量自动登录（不校验） |
| `WOW_CHAR` | `config/dev.local.json` 的 `dqq.testCharacter` | 自动进入该角色 |
| `EXILE_AUTOCOMBAT` | `dev.local.json` 的 `dqq.autoReplay` 为 true 时设为 `1` | 无人值守测试：进入世界 10 秒后自动走一遍回放流程。默认关闭，进入世界为自由模式（D27） |

流程：自动登录 → 领域 "Exile of Azeroth" → 选人（runtime 的 DQQ 角色）→ 进入北郡（地图 0，`(-8949.95, -132.49, 83.6)`），自由模式。
聊天框输入 `.replay`（临时入口，以后换插件界面）→ 锁住操作、传送到战场的加载画面 → 加载完成（客户端发 WorldportAck）后 runtime 回放该角色最近一场录像，玩家本体扮演战斗创建者 → 结束 3 秒后清场、传回原处、加载完成后交还操作。退出游戏（主菜单 → 退出游戏）时 runtime 随之退出（D26）。

日志：客户端 `tools/logs/exile-play.log`（搜 `exile:`），runtime `tools/logs/runtime.log`。

自由模式与战斗模式切换时，加载画面至少显示 0.5 秒（仅 Exile 模式，`loading_screen.rs`）。

### 4.1 诊断探针（开发版客户端自带）

`config/dev.local.json` 的 `probe.key` / `probe.lua` 由 `exile-play` 转成 `WOW_PROBE_KEY` / `WOW_PROBE_LUA`：
- `WOW_PROBE_KEY="W@14:3"`：进入世界 14 秒时在客户端内部按住 W 3 秒（不经过操作系统和输入法）。
- `WOW_PROBE_LUA="<Lua>"`：进入世界 10 秒时在界面 VM 里执行，可调用 `ProbeLog(text)` 写日志（搜 `probe-log:`）。
- 打开探针时客户端会缩成右上角的置顶小窗；**用完清空 `probe`**，否则影响正常游玩。

`config/dev.local.json` 的可选 `rustLog` 由 `exile-play` 转成 `RUST_LOG`（客户端日志过滤），例如 `"info,benilla_app::blob_shadow=debug"`。可选 `env` 对象里的每一项会设成环境变量，例如 `"env": {"WOW_MOVE_TRACE": "C:\\code\\ExileofAzeroth\\tools\\logs\\move-trace.log"}` 会把本体的动画选择（`anim mode=… gait=…`）写进该文件：gait 0 站立、4 走、5 跑。回放期间桥接每秒记一行单位坐标（搜 `exile: frame`），战斗结束记移动/转向次数。

## 5. 首次运行与客户端状态

- benilla 把状态写在 `benilla-config/`。因为客户端是子模块（`client/.git` 是文件），benilla 把它当成 git 工作树，状态目录落在**仓库根**的 `benilla-config/`（已忽略）。
- 首次运行会弹出暴雪 EULA 和 TOS（用户 2026-11-06 授权 AI 接受），接受后写入 `readEULA`、`readTOS`。
- 预设 `realmName = "Exile of Azeroth"`，否则会出现领域选择向导。
- 在全新机器上需要重复以上两步（以后由启动器处理）。

## 6. 已知问题

| 问题 | 说明 |
|------|------|
| 战场未居中 | DQQ 坐标 0~10 直接加在原点上，战场偏在玩家西北方；应以原点为中心 |
| 玩家本体未隐藏 | §5.1 要求战斗时隐藏非战斗角色，尚未做 |
| 怪物多且挤 | `Map_1_1` 场上约 30+ 只螃蟹，3 码/单位偏小，比例待调 |
| 表现缺失 | 施法特效、死亡表现、名字/血条、装备外观未做；1.12 默认界面不显示他人伤害数字 |
| 键盘没反应 | Windows 中文输入法会截走游戏里的按键（WASD、回车、Esc 都会变成拼音候选）。玩游戏时切换到英文输入法 |
| 按键/鼠标视角/Esc 全部无效（已解决） | 按键绑定属于"账号数据"，客户端要等服务器发来各段摘要（`SMSG_ACCOUNT_DATA_MD5`）才加载。桥接在进入世界时补发全空摘要（`exile.rs`），之后 W=MOVEFORWARD、Esc=TOGGLEGAMEMENU、右键=TURNORACTION 均生效 |
| 节奏 | DQQ 自带节流在 Windows 上偏慢，runtime 用 `TickPacer` 自行控制（已解决） |

## 变更记录
| 日期 | 修改人 | 说明 |
|------|--------|------|
| 2026-11-06 | Claude | 补充：加载画面最短 0.5 秒、诊断探针、按键绑定问题与修复 |
| 2026-11-06 | Claude | 初稿 |
