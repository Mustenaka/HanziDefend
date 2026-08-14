# WO-D2 · 流程状态机 · 静态审核报告

**日期**：2026-08-14  
**结论**：**规则状态机与跨关状态静态完成；主线仍有 Reward/终局 HUD 层级待修，且真实五关、根切换、全量与六张截图尚未验收。**

> 本轮只做静态审计，没有连接或占用 Unity。截图队列是演示夹具，不能替代“真实连打五关”的验收。

## 目标与交付逐项核对

| M1-05 要求 | 静态结果 | 实现 / 测试证据 |
|---|---|---|
| `Deploy → Battle → Reward → Deploy`，五小关一大关 | 已实现 | `GameFlowPhase` 与命令入口在 `Assets/Scripts/Gameplay/GameFlow.cs:12-19`、`:74-129`；战斗结算在 `:197-219`；五胜循环用例在 `Assets/Scripts/Tests/PlayMode/GameFlowTests.cs:121-147`。 |
| 单场景、三个根，不切 Scene | 已实现 | `M1GameBootstrap.Initialize` 在同一场景创建 `DeployRoot/BattleRoot/RewardRoot`（`Assets/Scripts/View/M1GameBootstrap.cs:66-94`），`HandlePhaseChanged` 只切 active（`:181-205`）。代码中没有阶段切换 Scene API。 |
| Reward 模态覆盖保留的战场实体 | 主体已实现；HUD 层级待修 | Reward/终局阶段令 `BattleRoot` 与 `RewardRoot` 同时 active，因此实体未销毁（`M1GameBootstrap.cs:181-192`）。但当前 `BattleHudCanvas` 在这些阶段也保持 active，且其独立 Canvas 为 `sortingOrder = short.MaxValue`（`Assets/Scripts/View/BattleHudCanvas.cs:17`、`:96-111`），会盖在 Reward 模态之上并可能截获 HUD 区域点击。主线应只在 `phase == Battle` 时启用 HUD，或给模态更高且明确的排序。 |
| `RunState` 跨关继承金币、部署、效果、刷新次数、网格、三 RNG | 实现链路存在；测试部分覆盖 | `GameFlow.BeginNextStage` 在同一 `CardEconomy/RunState` 上推进关卡（`GameFlow.cs:106-129`）；专项用例确认金币、部署、效果、网格和 Battle RNG 保留（`GameFlowTests.cs:95-119`）。`RefreshCount` 实现上不会在 `AdvanceToMinorStage` 清零，但该用例没有先制造非零刷新次数；三条 RNG 也只断言 Battle 一条，建议补强。 |
| 第 5 关 → 大关结束页 → 重置 | 已实现且有用例 | `BeginNextStage` 在最后一关选择奖励后转 `MajorVictory`；`CompleteMajorVictoryAndRestart` 调 `ResetRun`（`GameFlow.cs:106-143`）。五胜测试确认回到第 1 关并清空金币初值以外的部署、效果、刷新与扩建（`GameFlowTests.cs:121-147`）。 |
| 失败 → 失败页 → 重开 | 已实现且有用例 | 失败结算转 `Defeat`（`GameFlow.cs:197-215`），`RestartMajorStage` 用新 seed 重置（`:131-135`）；失败分支测试在 `GameFlowTests.cs:149-164`。 |
| 部署期不推进战斗 | 已实现且有用例 | `GameFlow.Tick` 只在 Battle 阶段转发（`GameFlow.cs:97-104`）；`DeployPhase_DoesNotAdvanceBattleTick`（`GameFlowTests.cs:67-76`）。 |
| 结算后立即冻结 | 已实现且有用例 | `Win_ChangesToRewardAndFreezesBattleTickImmediately` 记录结算 tick 后再次调用 `Tick`，tick index 不变（`GameFlowTests.cs:78-93`）。 |

## 已有自动化覆盖

`GameFlowTests` 目前静态枚举为 **8 个 PlayMode 用例**，覆盖初始状态、部署映射、部署期冻结、胜利转 Reward 与 tick 冻结、跨关继承、五胜与重置、失败与重开、Reward/终局出口的基本投影。

本轮未执行。特别是这些用例直接创建 `GameFlow`，没有实例化真实 Battle 场景里的 `M1GameBootstrap`，因此不能证明三个根、Canvas 排序、鼠标入口或五关真实配置在 Play 中无异常。

## 已修复的跨关生命周期问题

1. **部署根被重建误销毁**：当前 `BuildDeployScreen` 只替换 `DeployRoot` 下的 `Deploy Screen` 子对象，保留根本身（`M1GameBootstrap.cs:156-174`）。
2. **跨关复用旧战斗表现**：`M1GameBootstrap.StartBattle` 在 `Flow.StartBattle()` 发布新 spawn 事件前调用 `BattleView.PrepareForEncounter()`（`:123-129`）。后者回收旧实体、战斗数字与 FX，清空复用 entity id、HUD 部署列表、截图队列和结算状态（`Assets/Scripts/View/BattleView.cs:252-291`）。`PrepareForEncounter_RecyclesStaleEntityIdsAndHudDeploymentState` 已覆盖 entity id 从 1 重新开始的场景（`Assets/Scripts/Tests/PlayMode/BattleViewTests.cs:68-83`）。

以上是代码层已修证据；仍需最终 PlayMode 全量确认。

## 7 张截图队列与证据边界

`HanziDefend/Capture Queue Review` 计划一次生成 7 张去重后的 C3 + D2 证据：

1. `queue-stage1-deploy-initial`（同时满足 C3 初始 3×3）；
2. `queue-stage1-deploy-mixed-shapes`（C3 多形状）；
3. `queue-stage1-battle`；
4. `queue-stage1-reward`；
5. `queue-stage3-deploy-expanded`（同时满足 C3 ≥5 列）；
6. `queue-stage5-boss`；
7. `queue-major-victory`。

队列的最小实现使用 `EditorApplication.EnterPlaymode/ExitPlaymode`、`SessionState` 跨域保存 step、`EditorApplication.update` 等待页面稳定，再调用既有 `ScreenshotTool.CaptureForQueue`（`Assets/Scripts/Editor/QueueReviewCaptureCoordinator.cs:18-57`、`:59-152`、`:364-396`）。Play 中 PNG 暂存到 `Library`，退出后统一导入，避免 Play 期间 `AssetDatabase.ImportAsset` 触发刷新（`ScreenshotTool.cs:55-64`、`:129-179`）。

证据边界：队列为快速构图，阶段胜利和第 5 关 BOSS 表现由 Editor 夹具注入事件（`QueueReviewCaptureCoordinator.cs:224-317`），并没有真实打完五场。它可证明 UI/根/构图，不能证明战斗规则、真实结算或稳定性。

## 最终验收证据位

- [ ] 修复 Reward/MajorVictory/Defeat 阶段 HUD 盖住模态的问题，并补根与 HUD active 真值表测试。
- [ ] 用真实配置从 Play 连打 5 小关；记录金币、效果、部署、刷新次数、网格与三 RNG 的继承/重置。
- [ ] 六张 D2 必需截图生成并人工看图（上列除“mixed-shapes”外的 6 张）。
- [ ] 全流程 `read_console` Error = 0。
- [ ] EditMode、PlayMode、性能全量无回归。

