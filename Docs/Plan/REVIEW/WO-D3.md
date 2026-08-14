# WO-D3 · 结算 UI 与过场 · 静态审核报告

**日期**：2026-08-14  
**结论**：**页面主体已实现；主线仍有 HUD 模态层级和选奖后旧按钮可重复触发两项待修，截图与全量尚未验收。**

> 本轮只读代码与测试，没有连接或占用 Unity。

## 交付逐项核对

| M1-05 要求 | 静态结果 | 实现 / 测试证据 |
|---|---|---|
| 结算弹窗 3 卡 | 已实现 | `RewardScreen.BuildReward` 遍历 `CurrentOffer.Cards`，每卡创建图标、名称、描述与操作按钮（`Assets/Scripts/View/RewardScreen.cs:115-161`）。`RewardScreen_RewardAndTerminalStatesAlwaysExposeAContinuation` 确认渲染 3 卡（`Assets/Scripts/Tests/PlayMode/GameFlowTests.cs:166-185`）。 |
| 每卡选择、免费重随、广告按钮 | 已接规则 API | 每卡分别绑定 `Choose/Reroll/WatchAd(slotIndex)`（`RewardScreen.cs:147-160`）；重随重建当前 offer，广告调用 D1 接口并显示结果（`:164-189`）。次数与合法性由 `SettlementRewardSystem` 决定，View 不重算规则。 |
| 阶段切换过场动画 | 已实现，待视觉验收 | `CanvasGroup` 从 0 淡入，同时根从 0.94 缩放到 1（`RewardScreen.cs:55-64`、`:66-93`）。现有测试没有推进帧或断言动画完成；单张截图也不能证明过场。 |
| 大关通关页 | 已实现 | `MajorVictory` 显示“大关通关”，按钮调用 `CompleteMajorVictoryAndRestart`（`RewardScreen.cs:39-49`）。 |
| 失败页 | 已实现 | `Defeat` 显示“营地失守”，按钮调用 `RestartMajorStage`（同上）。 |
| 任意状态有出口 | 基本入口存在；交互生命周期待修 | Reward 初态有 3 个“选择”入口，选中后增加“继续”；通关/失败各有“重新开始”（`:147-167`、`:191-205`）。但选中后旧的 Choose/Reroll/Ad 仍保持可交互：再次点击 Choose 或 Ad 会让已封闭的 D1 系统在 `RequireOpenOffer()` 抛 `InvalidOperationException`，而这两条 UI 路径没有 catch。应在选择后禁用/移除卡片 controls 或重建确认态，并保持唯一继续按钮。 |

## 模态层级待修

`M1GameBootstrap` 在 Reward/终局阶段保留 `battleHud` active（`Assets/Scripts/View/M1GameBootstrap.cs:181-192`），而 `BattleHudCanvas` 使用 `overrideSorting` 与 `short.MaxValue`（`Assets/Scripts/View/BattleHudCanvas.cs:17`、`:96-111`）。因此 HUD 会压在 Reward 模态上方并可能拦截点击。战场实体应保留用于模态背景，但 HUD 应只在 `GameFlowPhase.Battle` 可见，或模态必须拥有更高且明确的 Canvas 排序。

## 测试覆盖判断

现有 `RewardScreen_RewardAndTerminalStatesAlwaysExposeAContinuation` 只确认 Reward 有 3 卡、按钮总数不少于 3，以及手工投影 Defeat 时 `ExitButtonCount == 1`。它没有：

- 真实点击选奖、重随、广告与继续；
- 覆盖 `MajorVictory` 页面；
- 验证选奖后的旧按钮被禁用；
- 验证 Reward/终局期间 HUD 不盖模态；
- 推进帧验证过场动画；
- 验证退出按钮真实改变 phase 且不写 Error。

## 截图 / 录屏证据位

- [ ] `queue-stage1-reward`：三卡、每卡重随/广告按钮、战场背景可见、HUD 不盖模态。
- [ ] `queue-major-victory`：大关通关页与“重新开始”出口。
- [ ] 失败页截图：现有 7 张 C3+D2 去重队列没有该画面；如审核要求交付物逐页截图，应另补 `queue-defeat`。
- [ ] 过场短录屏或逐帧测试；静态截图不能证明淡入/缩放。
- [ ] PlayMode 全量、既有基线与 `read_console` Error = 0。

