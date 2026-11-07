# PLAN-0009：离线模式

| 字段 | 内容 |
|------|------|
| 编号 | PLAN-0009 |
| 状态 | 阻塞（等 DQQ 侧） |
| 负责人 | （待认领） |
| 创建日期 | 2026-11-07 |
| 最后更新 | 2026-11-07 |
| 完成度 | 0% |
| 关联设计 | docs/architecture.md D15 |
| 依赖计划 | DQQ 侧：把离线 service 从浏览器 IndexedDB 抽成本地 SQLite（用户负责） |

## 目标
不连 DQQ 服务器也能玩：runtime 使用 DQQ 的离线 service，存档在本地 SQLite；玩家可以选择在线或离线。

## 现状（2026-11-07）
- runtime 只用在线模式：`ComponentInit.AddServices(url, false)`，`IClientRepository` 用内存实现 `MemoryClientRepository`（只保存登录状态等少量数据），登录后 `SetAuth(token, EnumPlayMode.Online)`。
- DQQ 的离线实现目前依赖浏览器 IndexedDB，桌面进程里无法使用。

## 设计
1. DQQ 侧提供离线 service 的 SQLite 实现（例如一个注册函数 `AddOfflineServices(dbPath)`），runtime 只负责选择和调用，不改 DQQ。
2. runtime 配置项 `dqq.mode`（`online` / `offline`，`config/dev.example.json` 已预留）与存档路径（发布版在启动器的 `Exile/data/`）。
3. 离线模式下的挂机由本地计算（PLAN-0007 的离线分支），runtime 的节奏、观看流程不变。

## 任务清单
- [ ] 与用户确认 DQQ 离线 service 的接口形态（注册方式、存档路径、初始化）
- [ ] runtime：按 `dqq.mode` 注册在线或离线 service；离线时跳过登录
- [ ] `IClientRepository`：离线时换成 DQQ 提供的本地实现（或保留内存实现，视 DQQ 设计）
- [ ] 存档位置与备份（启动器 `Exile/data/`，PLAN-0008）
- [ ] 角色列表、装备、技能、回放、挂机在离线下全部走一遍
- [ ] 在线与离线之间的存档迁移：是否需要，与用户确认

## 验收标准
- 断网时启动游戏，选人、整理装备、调整技能、挂机、回放全部可用，数据保存在本地，重启后还在。
- 切换回在线模式，在线数据不受影响。

## 待确认
1. DQQ 离线实现的交付时间与接口。
2. 是否需要在线与离线存档互相迁移。

## 进度日志
<!-- 追加，不要删除旧记录 -->
- 2026-11-07（Claude）：由 PLAN-0003 的验证结论拆出创建；同日细化（现状与 runtime 侧的改动点）。阻塞于 DQQ 侧。

## 讨论 / 交接
