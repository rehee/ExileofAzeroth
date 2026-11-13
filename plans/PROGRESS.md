# 项目总进度

> 每个计划的详细内容见各自文件 `plans/PLAN-XXXX-*.md`。
> 每次工作结束前，请同步更新本表。规则见 `agents/WORKFLOW.md`。

**下一个可用编号：PLAN-0011**

## 计划总表
| 编号 | 名称 | 状态 | 负责人 | 完成度 | 最后更新 | 文件 |
|------|------|------|--------|--------|----------|------|
| PLAN-0001 | 项目协作结构初始化 | 已完成 | Claude | 100% | 2026-11-06 | [PLAN-0001](PLAN-0001-project-setup.md) |
| PLAN-0002 | 参考项目调研与架构选型 | 已完成 | Claude | 100% | 2026-11-06 | [PLAN-0002](PLAN-0002-tech-research.md) |
| PLAN-0003 | 验证：方案 T 端到端打通 | 已完成 | Claude | 100% | 2026-11-07 | [PLAN-0003](PLAN-0003-spike-csharp-server.md) |
| PLAN-0004 | 战斗表现 | 待开始 | — | 0% | 2026-11-13 | [PLAN-0004](PLAN-0004-combat-presentation.md) |
| PLAN-0005 | 界面插件与 DQQ service | 待开始 | — | 0% | 2026-11-07 | [PLAN-0005](PLAN-0005-ui-addons.md) |
| PLAN-0006 | DQQ ↔ WoW 对应表 | 已完成 | Claude | 100% | 2026-11-13 | [PLAN-0006](PLAN-0006-mapping-table.md) |
| PLAN-0007 | 藏身处与后台挂机 | 待开始 | — | 0% | 2026-11-07 | [PLAN-0007](PLAN-0007-hideout-idle.md) |
| PLAN-0008 | 启动器与发布 | 待开始 | — | 0% | 2026-11-07 | [PLAN-0008](PLAN-0008-launcher-release.md) |
| PLAN-0009 | 离线模式 | 阻塞 | — | 0% | 2026-11-07 | [PLAN-0009](PLAN-0009-offline-mode.md) |
| PLAN-0010 | 手动战斗 | 阻塞 | — | 0% | 2026-11-07 | [PLAN-0010](PLAN-0010-manual-combat.md) |

## 建议顺序
PLAN-0006（对应表，已完成）→ PLAN-0004（战斗表现）；PLAN-0005（界面）可并行；之后 PLAN-0007、PLAN-0008；PLAN-0009、PLAN-0010 等 DQQ 侧就绪再开始。见 `docs/spike-results.md` §6。

每个计划文件里有现状、设计、分阶段任务、验收标准和待确认问题（2026-11-07 细化）。

## 下一阶段待办（用户 2026-11-11：留到下一阶段）
- 审 `config/mapping/skills.json` 的 `auras`：Claude 先挑的英雄与怪物身上的光环特效（PLAN-0006 D）。2026-11-13 修好后英雄身上的光环第一次真正显示，太抢眼的（如之前的奥术涌动光球）要换。
- 怪物稀有度的名字颜色、头像边框在客户端显示（数据已下发，用户 2026-11-12 认可光环与边框等级）；精英、首领的外发光（用户 2026-11-13 决定做，要改着色器）：PLAN-0004 C。
- 实际运行一次演示启动器 EoALauncher（`tools/run-launcher-build.cmd`，PLAN-0008）。
- 时装 cosmetics（只预留了格式与收费点设计，见 docs/char-config.md）：以后另开计划。

## 当前阻塞
- PLAN-0009 等待 DQQ 侧的离线 service（SQLite）；PLAN-0010 等待 DQQ 侧的标准指令支持。

## 规则变更
| 日期 | 修改人 | 文件 | 说明 |
|------|--------|------|------|
| 2026-11-06 | Claude | agents/* | 初始创建规则、工作流与模板 |
| 2026-11-06 | Claude | agents/RULES.md | 新增 §8 凭据与本地配置规则 |
