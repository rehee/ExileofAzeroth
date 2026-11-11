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
| Rust（版本由 `client/rust-toolchain.toml` 决定） | 装在本项目内 `.toolchain/rust/`（已忽略）：首次运行 `tools/run-setup-rust.cmd`。构建脚本只用这一份，不读系统 PATH 或其他项目的 cargo |
| VS 2022 C++ 生成工具 | Rust MSVC 链接需要 |
| WoW 1.12.1 enUS 数据 | 自备（受版权保护，不分发）。目录写在 `config/dev.local.json` 的 `paths.wowData` |
| DQQ 源码 | 私有项目，只读取不修改。目录写在 `config/dev.local.json` 的 `paths.dqqRoot` |

本机目录集中在 `config/dev.local.json`（模板见 `dev.example.json`）：

```json
"paths": {
  "dqqRoot": "C:\\code\\dqq\\DQQ",
  "wowData": "C:\\wow\\Data"
}
```

环境变量 `DQQ_ROOT` / `WOW_DATA` 可临时覆盖。没有配置或目录不存在时，脚本直接报错说明该填哪一项，不再回退到写死的路径。

`tools/setup-dev.ps1` 可用 winget 安装 .NET SDK 与 VS 生成工具；Rust 由 `tools/run-setup-rust.cmd` 安装到项目内（rustup 下载自 static.rust-lang.org）。

## 3. 脚本

每个 `tools/X.ps1` 都有双击启动器 `tools/run-X.cmd`。脚本输出写 `tools/logs/X.log`，状态写 `tools/logs/X.status`（running / ok / fail），结束后窗口自动关闭。

| 脚本 | 作用 | 备注 |
|------|------|------|
| `launcher-build` | 生成演示版启动器 `dist/EoALauncher.exe`（单个 exe，见 §3.1） | 包含客户端、runtime（自带 .NET）、对应表、插件；不含暴雪文件。中间文件在 `build/launcher/` |
| `setup-rust` | 把 rustup 与客户端要求的 Rust 装到 `.toolchain/rust/` | 只需一次；`client-build` / `wow-dbc` 找不到 cargo 时会提示先运行它 |
| `update-dqq` | 编译 DQQ 三个项目并收集全部依赖到 `source/dqq/` | 跳过 DQQ 本地化同步，避免改写 DQQ 目录 |
| `runtime-selftest` | 编译 runtime 并自检：在线登录、角色列表、后台/实时战斗、WebSocket 回环实测 | 报告 `tools/logs/selftest.json` |
| `client-build` | 编译客户端（profile `play`） | 首次约 14 分钟，增量 1–2 分钟；运行中的游戏会锁住 exe，先退出游戏 |
| `client-run` | 以普通方式运行客户端（不进 Exile 模式） | |
| `exile-play` | 一键运行：编译并后台启动 runtime → Exile 模式启动客户端 | 见 §4 |
| `mapping-report` | 编译 runtime 并输出 DQQ ↔ WoW 对应表的现状与缺失项（PLAN-0006） | 结果 `tools/logs/mapping-report.md`；不登录 DQQ |
| `event-stats` | 编译 runtime，后台重放 `replays/` 的录像，统计技能事件，并列出 DQQ 全部技能的定义（PLAN-0006 D） | 结果 `tools/logs/event-stats.md`；会登录 DQQ |
| `wow-dbc` | 编译 `benilla-extract`，从本机 WoW 数据导出 DBC 原文件（生物外观、法术、动画、物品外观……） | 结果 `tools/logs/wow-dbc/`（只供本地分析，不入库、不分发） |
| `wow-blp` | 把 `tools/logs/wow-blp.txt` 里列出的贴图转成 PNG 到 `tools/logs/wow-blp/` | 选肤色、看贴图用；先跑过 `wow-dbc`（它编译 `benilla-extract`） |
| `commit-push` | 提交并推送；有 `tools/commit-message-client.txt` 时先提交推送 `client` 子模块 | 提交信息文件用后删除（已忽略） |
| `init-repo` / `setup-dev` | 一次性脚本 | |

AI 的执行方式：AI 的命令行是用户电脑上的 Linux 虚拟机（无网络、无 .NET、非 Windows），只能读写文件；编译和运行由 AI 在资源管理器里**双击 `tools/run-*.cmd`**（资源管理器只授权左键），再读 `tools/logs/`。新加的文件在资源管理器里不会自动出现，回到上一级再进入即可刷新。


### 3.1 演示版启动器 EoALauncher（用户 2026-11-08）

`tools/run-launcher-build.cmd` 生成 `dist/EoALauncher.exe`，玩家把它放进 WoW 1.12.1 目录（和 `WoW.exe`、`Data` 在一起）代替 `WoW.exe` 运行：

1. 首次运行（或包变了）把接在 exe 末尾的包解压到 `WoW目录/Exile/`（`client/`、`runtime/`、`benilla-config/AddOns/`）；玩家设置 `benilla-config/`、录像 `replays/` 保留。
2. 后台启动 runtime：在线模式，服务器 `https://pogapi2.idlepog.com/`，`--player-login`（不读开发配置）。
3. 启动客户端（`EXILE_LOGIN=dqq`）：停在 WoW 登录界面，输入 DQQ 用户名、密码 → 桥接 `auth.login` → runtime 在线登录 → 角色列表 → 进入游戏，聊天框 `.replay` 等读取在线录像。账号名用登录界面自带的“记住账号名”勾选保存，密码不保存。
4. 客户端退出后关掉 runtime。日志在 `WoW目录/Exile/logs/`（launcher、runtime、client）。

这是独立的演示命令，正式的启动器（自动更新、CI）见 PLAN-0008。开发时仍用 `run-exile-play`（测试账号自动登录）。

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

### 4.0 界面插件
`addons/` 下的插件（目前只有 `ExileUI`）由 `exile-play` 在每次启动前复制到 `benilla-config\AddOns\`（开发版客户端的插件目录），改了插件只需重新运行 `exile-play`，不用重编客户端。进入世界后点右上角小地图左边的 **Replays** 按钮打开录像列表（或输入 `/exile`），点一条即回放；Recent 栏每条的 Export 把录像导出到仓库根 `replays\`，Files 栏列出 `replays\` 里的录像文件（包括从 DQQ 网页版下载的 `.txt`）并可回放；`/exile ping` 测试与桥接的往返。

### 4.0.1 对应表审阅（`.lineup`）
对应表是配置文件 `config/mapping/*.json`，改完保存即可，格式与找 id 的方法见 `docs/mapping-config.md`。自由模式下聊天框输入：`.lineup mobs [怪物名…]` 把 `config/mapping/mobs.json` 里的每个怪物（写了名字就只摆这几个，如 `.lineup mobs UnName CowKing`）排在面前，鼠标指上去显示 DQQ 怪物名，首领按首领体型显示；`.lineup [size=N] <display id> …` 把任意生物外观排成一排（名字 `#id`，`size` 为 DQQ 体型），用来比较候选；`.lineup` 清掉。

### 4.0.2 选战场（`.goto`）与在指定战场回放（`.replay 场景名`）
`.replay forest|coast|den|plains|rift|graveyard`：播放最近一场录像，战场换成该场景（`maps.json` 的 scenes）。
自由模式聊天框 `.goto x y [半径] [map=地图id]`：传送到该地图（默认当前地图；`map=1` 卡利姆多）的 (x, y)（带半径时找半径内最平的点），日志记录周围 ±10 码的高差，用来给 `config/mapping/maps.json` 选战场中心（`docs/mapping-config.md` §2.5）。

### 4.0.3 看某个技能的录像（`.play`）
`.play 名字 [场景]`：播放 `replays/replay-名字.txt`（也可以写完整文件名），可选战场场景。用来逐个检查技能表现（PLAN-0006 D），例如 `.play FireBall plains`。录像文件名按技能命名（用户整理）。DQQ 改过代码后先跑 `run-update-dqq`，否则重放会和录像对不上。

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
- 镜头跟随（用户 2026-11-09）：Exile 模式下 `cameraSmoothStyle` 默认 0（Never），自由模式走动时镜头不会自动转回角色背后。只在配置里还没有这一项时设置，之后玩家在界面设置里改成别的会保留。
- 模式切换时镜头重置（用户 2026-11-09）：自由模式 ↔ 战斗回放都是一次传送，Exile 模式下每次传送后建一个新镜头（同 `/reload`）：放到人物背后（到达时的朝向）、默认俯角（约 29°）和默认距离（15 码），停掉所有过渡。
- 拖视角时偶尔变成从头顶往下看（用户 2026-11-09）：日志显示那一帧的鼠标移动量是 (32972, 24150) 这样的屏幕绝对坐标（Windows 原始输入的绝对坐标报告），被当成移动量，镜头一帧转了约 70 弧度。客户端现在丢掉单帧超过 4000 的鼠标移动量（日志记 `camera: dropped a mouse delta`）；俯角单帧跳变超过 0.3 弧度也会记日志，方便以后排查。
- 表情动作（用户 2026-11-09）：自由模式可以用 `/wave`、`/dance` 等。没有服务器回应，由桥接代答（同 vmangos）：聊天行与声音（`SMSG_TEXT_EMOTE`），一次性动作（挥手）发 `SMSG_EMOTE`，循环动作（跳舞）设 `UNIT_NPC_EMOTESTATE`、走动就停；坐下等姿势本来就由客户端自己处理。回放中不响应（身体属于回放）。
- 登出（用户 2026-11-10）：游戏菜单（Esc）的 Logout 立即回到角色列表（没有 20 秒等待），角色列表重新向 DQQ 取（装备等是最新的）。回放中登出会先停掉回放。Exit Game 仍是退出游戏。

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
| 2026-11-07 | Claude | 新增 `mapping-report`、`wow-dbc` 脚本与 `.lineup` 审阅命令（PLAN-0006） |
| 2026-11-07 | Claude | 新增 `event-stats` 脚本（PLAN-0006 D） |
| 2026-11-06 | Claude | 补充：加载画面最短 0.5 秒、诊断探针、按键绑定问题与修复 |
| 2026-11-06 | Claude | 初稿 |
