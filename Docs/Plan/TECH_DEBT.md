# 技术债账本

触发「三次法则」换了笨方案、或为了先跑通而绕过去的东西，都记在这里。
记了就不算错，不记才算。

| 日期 | 工单 | 绕过了什么 | 现在的做法 | 理想做法 | 严重度 |
|---|---|---|---|---|---|
| 2026-08-12 | WO-00 | 首次长时间 Play 验收时，MCP `editor_state` 连续返回过期的 `playmode_transition` 快照并触发命令超时警告 | 改用 Editor 主线程查询 `EditorApplication.isPlaying`、控制台和截图结果交叉确认；退出 Play 后状态上报恢复 | MCP bridge 在域重载 / Play 切换后及时刷新 heartbeat，不产生超时警告 | 低 |
| 2026-08-12 | WO-B1 | MCP 无筛选的 EditMode `run_tests` 已实际生成 80/80 Passed 的 XML，但 TestJobManager 未接到启动进度并在 120 秒后误报初始化失败 | 使用明确的 `HanziDefend.Tests.EditMode` 程序集筛选重跑，结构化结果 80/80 Passed；PlayMode 全量不受影响 | 修复 MCP TestJobManager 对 EditMode 全量运行的进度订阅/结果回收，避免把已完成测试误判为未启动 | 低 |
| 2026-08-12 | WO-B2 | 同一 MCP EditMode 回执问题累计第三次出现：程序集筛选运行已写出 103/103 Passed，但域重载后作业仍停在 `running`，并短暂丢失实例路由 | 换用 Unity 原生 `TestResults.xml` 交叉确认结果，随后 `clear_stuck` 清理孤儿作业；PlayMode 结构化回执 51/51 正常 | 修复 MCP TestJobManager 在 EditMode 域重载后的作业恢复与结果回收 | 低 |
| 2026-08-12 | WO-B2 → WO-B3（已偿还） | `main_20` 第 8 波起包含 `RushBase` 的 `e_lang`，但真实特殊索敌按工单属于 WO-B3 | WO-B3 已删除 `BattleSystem` 波次出生路径的临时 `Nearest` 降级与公共 Spawn 拒绝，`e_lang/qi` 现只锁敌方基地并绕过拦路单位 | 2026-08-12 已补调度器狼骑、公共 Spawn 与精确目标选择回归 | ✅ 已偿还 |

严重度：`低`（不影响功能，有空再说） / `中`（影响扩展或性能，M1 内应还） / `高`（影响正确性，下个工单必须还）
