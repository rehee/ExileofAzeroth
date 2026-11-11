# PLAN-0008：启动器与发布

| 字段 | 内容 |
|------|------|
| 编号 | PLAN-0008 |
| 状态 | 待开始 |
| 负责人 | （待认领） |
| 创建日期 | 2026-11-07 |
| 最后更新 | 2026-11-07 |
| 完成度 | 0% |
| 关联设计 | docs/architecture.md §8、§9、D10–D13、D21、D26 |
| 依赖计划 | PLAN-0005 的中文字体（随包发布）；其余可先做 |

## 目标
玩家把启动器放进 WoW 1.12.1 目录运行，就能自动安装或更新到最新版本并进入游戏；我们推一个 tag，就能在 GitHub Releases 发布新版本。

## 现状（2026-11-07）
- 开发版通过 `tools/exile-play.ps1` 运行：现场编译 runtime，设置环境变量（`WOW_DATA`、`EXILE_RUNTIME`、`WOW_USER/PASS/CHAR`），复制插件到 `benilla-config/AddOns/`，启动客户端。
- 客户端可执行文件约 134 MB（`--profile play`）。runtime 依赖 .NET 8。
- 没有启动器，也没有 CI。

## 发布包内容
| 组件 | 来源 | 说明 |
|------|------|------|
| 客户端 `Exile/client/benilla.exe` | `client/`，`cargo build --profile play` | 附带 benilla 的 MIT / Apache-2.0 许可文件 |
| runtime `Exile/runtime/` | `dotnet publish -r win-x64 --self-contained` | 自带 .NET，玩家不用另装；含 `source/dqq/` 的 DLL |
| 插件 | `addons/*` | 安装到客户端状态目录的 `AddOns/` |
| 中文字体 | PLAN-0005 选定 | 附带字体许可 |
| 发布清单 `manifest.json` | CI 生成 | 版本号、各文件大小与 SHA-256、最低启动器版本 |

**目录与状态位置**：发布版客户端的状态目录是可执行文件旁的 `benilla-config/`（客户端 `local_state.rs` 的第 4 步），WoW 数据用 `WOW_DATA` 指向 WoW 目录下的 `Data`。启动器负责设置这些环境变量（相当于现在的 `exile-play.ps1`）。

## 设计
1. **CI（GitHub Actions，windows-latest）**：tag `v*` 触发 → 拉取子模块 → 装 Rust（按 `client/rust-toolchain.toml`）与 .NET 8 → 编译客户端与 runtime → 组装发布包 → 生成清单 → 上传 Release。按组件分别打 zip（客户端、runtime、插件与字体），更新时只下载变化的组件。
2. **启动器 `ExileLauncher.exe`**：C#、Avalonia、单文件发布。流程（architecture §8）：校验 WoW 目录（`WoW.exe`、`Data/` 与 build 5875）→ 读 Releases 的 `manifest.json` → 下载变化的组件、校验哈希 → 备份旧版本 → 安装到 `Exile/` → 设置环境变量启动 runtime，就绪（端口可连）后启动客户端 → 客户端退出时 runtime 退出（D26）→ 启动器自更新。
3. **账号**：在线模式需要 DQQ 账号。启动器提供登录界面，凭据保存在本机（Windows 凭据管理器或加密文件），以 runtime 的配置传入；不写入仓库，不明文落盘。
4. **合规**：不包含任何暴雪文件；启动器与 README 写明"非官方粉丝项目，需要自备 WoW 1.12.1 enUS"；附带 benilla 与字体的许可。

## 任务清单
### A. 打包
- [ ] runtime 改为可配置的发布形态：配置文件位置、日志位置、录像目录（现在由 `exile-play.ps1` 传参）
- [ ] 客户端发布形态验证：在一个干净目录里只放 `benilla.exe` 与 `benilla-config/`，用环境变量能启动、能加载插件
- [ ] `tools/package.ps1`：本地组装发布包与清单（CI 也用它）

### B. CI 与发布
- [ ] `.github/workflows/release.yml`：tag 触发，编译、打包、上传 Release
- [ ] 编译缓存（Rust target、NuGet），控制 CI 时长
- [ ] 版本号规则（runtime、客户端、插件同一版本号，清单里分别记录）

### C. 启动器
- [ ] Avalonia 工程 `src/Exile.Launcher/`：单窗口（状态、进度、开始游戏、设置）
- [ ] WoW 目录校验（选择目录、记住路径）
- [ ] 更新：读清单、增量下载、哈希校验、备份与回滚
- [ ] 启动：设置环境变量、启动 runtime、等待就绪、启动客户端、监视退出
- [ ] DQQ 账号登录与保存
- [ ] 自更新

### D. 合规与说明
- [ ] README（中英）：介绍、安装方法、截图、声明
- [ ] 许可文件随包

## 验收标准
- 在一台只装了 WoW 1.12.1 enUS（没有 Rust、.NET、Git）的 Windows 电脑上：下载启动器、放进 WoW 目录、运行、登录，即可进入游戏并回放战斗。
- 推送新 tag 后 CI 自动发布；启动器检测到新版本，只下载变化的组件。
- 更新失败时能回滚到上一个版本。

## 待确认
1. ~~账号在哪登录~~ ✅ 启动器里登录，凭据加密保存在本机（2026-11-07）。
2. ~~代码签名~~ ✅ 先不签（2026-11-07）。
3. ~~安装位置~~ ✅ 先只支持放在 WoW 目录（2026-11-07）。

## 进度日志
<!-- 追加，不要删除旧记录 -->
- 2026-11-07（Claude）：由 PLAN-0003 的验证结论拆出创建；同日细化为可执行计划（发布包组成、CI、启动器流程、账号）。
- 2026-11-07（Claude）：用户确认：启动器里登录；先不签名；只支持 WoW 目录。
- 2026-11-08（Claude）：用户要一个独立的演示版启动器命令：`tools/run-launcher-build.cmd` → `dist/EoALauncher.exe`（单个 exe，包接在 exe 末尾，首次运行解压到 WoW 目录的 `Exile/`）。登录改在 WoW 登录界面输入 DQQ 账号（桥接 `auth.login`），记住用户名、不记密码。正式版的登录方式等演示后再定。

## 讨论 / 交接
