# PLAN-0003：验证（Spike）—— 方案 T 端到端打通

| 字段 | 内容 |
|------|------|
| 编号 | PLAN-0003 |
| 状态 | 待开始（等用户说开始） |
| 负责人 | |
| 创建日期 | 2026-11-06 |
| 最后更新 | 2026-11-06 |
| 完成度 | 0% |
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
- [ ] 首次提交并推送本仓库；把 `rehee/benilla` 加为子模块 `client/`：脚本 `tools/init-repo.ps1` 已写好，待用户在 Windows 上右键运行
- [ ] 开发机（Windows）安装 Rust 工具链、VS 2022 C++ 生成工具（Rust 在 Windows 上编译需要）、.NET 8 SDK（用户已授权 AI 操作）
- [x] `config/dev.local.json` 已存在（DQQ 服务器地址与测试账号，见 D16）

## 任务清单

### A. 仓库与 DQQ 接入
- [ ] 建 `source/`：从 `C:\code\dqq\DQQ` 编译 DQQ、DQQ.Core、DQQ.Web.Component，连同**全部依赖**和中文附属资源 DLL 复制进去（D23）
- [ ] 写更新脚本（如 `tools/update-dqq.ps1`），并在 `source/` 记录 DQQ 版本/提交与编译时间
- [ ] 建 `src/Exile.Runtime`（.NET 8 控制台），只引用 `source/` 里的 DLL

### B. Runtime 最小版本（风险 1）
- [ ] 在非浏览器进程中初始化 DQQ.Web.Component 的 service（在线模式），读取 `config/dev.local.json` 登录
- [ ] 调用 CharacterService 取角色列表并打印
- [ ] 用 DQQ 跑一场 `Map_1_1`（黑暗的海滩 1）战斗，开启每 tick 回调，产出渲染帧（参考 DQQ.Ember 的 `EmberFrameAdapter`），可先存成 JSON 文件
- [ ] 后台挂机模式：整场直接算完，只输出结果与进度（D19）
- [ ] 记录结论：哪些 service 能直接用、哪些依赖浏览器/JS、需要什么替代或绕开（不改 DQQ 源码）

### C. 桥接协议最小版本
- [ ] Runtime 开 127.0.0.1 WebSocket；定义最小消息：`ui.character.list`、`combat.start`、`combat.frame`、`combat.end`、预留 `cmd.*`
- [ ] 写 `docs/bridge-protocol.md` 初稿（字段、版本号、序号）

### D. benilla 分支（风险 2）
- [ ] 把 `rehee/benilla` 作为子模块加入（D22、D25）；本项目代码集中在自己的 crate（参考艾泽拉斯之潮 `client/crates/hoa`）
- [ ] 研究 Azeroth Survivors 的无服务器做法（`crates/benilla-app/src/survivors/`），写结论
- [ ] 不连服务器启动，进入一块平地场景，第三人称镜头（D8）
- [ ] 跳过登录，选人画面显示 runtime 发来的角色列表；默认人类女性，按职业/武器显示外观（D20，验证阶段武器可先固定）
- [ ] 桥接模块连 WebSocket，按 `combat.frame` 渲染：生成英雄与螃蟹、位置插值、施法/攻击动画、伤害飘字、死亡
- [ ] DQQ 坐标 ↔ WoW 坐标换算（§5.2，暂定 1 单位 ≈ 3 码），地面高度取自地形

### E. Lua 插件最小版本
- [ ] 插件收到 runtime 的消息（如战斗结算）并显示
- [ ] 插件发一条请求给 runtime（如刷新角色列表）并收到回复

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

## 讨论 / 交接
- 2026-11-06（Claude）：发现 `C:\wow_survival` 已有一套接近方案 T 的原型（详见下）。fork 与工具安装暂停，等用户决定是否以它为基础。`tools/setup-dev.ps1` 已写好但**未运行**，原型目录显示 Windows 上已有 Rust 构建产物，可能不需要。
  - `csp/`：C# .NET 8，经 hostfxr **在 Rust 显示进程内**运行；直接项目引用 `C:\code\dqq\DQQ`；`CombatRenderService` 逐 tick 快照、`CombatVisualAdapter` 表现事件、`NativeBridge` 原生接口（JSON）；`Csp.Browser` 网页版用 DQQ.Web.Component + IndexedDB，在线连 pogapi2；还有 `Csp.Launcher`。
  - `wenilla/`（Arnesen/wenilla 克隆）：`crates/dqq-world-demo` 显示端，2.5 码/单位，人类女性圣骑士 + 狗头人模型，北郡修道院/闪金镇场景，无需服务器。
  - `RESEARCH.md`：Azeroth Survivors 与 DQQ 渲染接入调研。
