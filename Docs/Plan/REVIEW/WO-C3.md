# WO-C3 · 部署 UI · 静态审核报告

**日期**：2026-08-14  
**结论**：**静态主体已完成，待最终 PlayMode、鼠标全流程与截图验收；“合成带动效”尚无实现证据。**

> 本轮按要求只读代码与测试，没有连接或占用 Unity。下文“用例存在”不等于本轮已经执行通过。

## 目标与交付逐项核对

| M1-05 要求 | 静态结果 | 实现 / 测试证据 |
|---|---|---|
| 全屏 `DeployRoot`；暗态网格、手牌、金币、刷新费用、Mock 广告刷新、扩建、指挥官头像、小关进度 | 已实现 | `DeployScreen.BuildHeader/BuildGrid/BuildHand/BuildFooter`（`Assets/Scripts/View/DeployScreen.cs:278`、`:302`、`:337`、`:358`）；未解锁格暗态在 `:423-453`；金币、关卡和当前刷新价在 `:412-420`。 |
| 拖起、合法/非法高亮、落位 | 已实现 | 预览只调用 `economy.Grid.Evaluate`，再按返回的 `IsValid/Action/Message` 着色与显示（`:102-111`、`:140-157`）；提交只调用 `economy.PlaceUnit` / `Grid.Move`（`:114-178`）；Pointer/Drop 接线在 `:645-799`。 |
| 七种 footprint，L 形按真实缺角 | 已实现且有用例 | `PaintPreview` 遍历 `UnitFootprint.OccupiedOffsets`，不是画 2×2 外接矩形（`:521-538`）。`Preview_AllSevenFootprintKinds_AreAcceptedAfterConfiguredExpansion` 覆盖 `zu/dun/mao/tie/zqi/nuc/chc`（`Assets/Scripts/Tests/PlayMode/DeployScreenTests.cs:70-89`）。 |
| 同 id 同等级拖放合成 | 规则调用与状态持久化已实现 | `Grid.Move` 返回 `DeploymentActionKind.Merge` 后刷新 `RunState`（`DeployScreen.cs:160-168`）；用例确认仅剩 1 个二级单位并写回 `RunState`（`DeployScreenTests.cs:109-133`）。 |
| 合成带动效 | **缺口** | 当前只有“合成成功”文字与 `FeedbackPresenter` 的 Merge 音效调用；`DeployScreen` 没有合成缩放、闪光或其它视觉动画。不能把音频 cue 记作“动效”完成。 |
| 战斗按钮只通知 `GameFlow` | 已实现且有用例 | `RequestBattle` 只更新提示并调用注入的 `battleRequested`（`DeployScreen.cs:231-235`）；`RequestBattle_OnlyRaisesInjectedCommand`（`DeployScreenTests.cs:135-144`）。 |
| View 不自算金币、占位、合成规则 | 符合 | 金币与刷新价只读取 `economy.State/NextRefreshCost`；刷新、扩建、落位、移动均委托 C1/C2 API。View 对 `CardCategory` / `DeploymentActionKind` 的分支只做命令分派和表现选择，没有金币比较、格位碰撞或合成等级计算。 |
| 非法放置明确反馈 | 已实现且有用例 | 失败使用规则层的 `evaluation.Message` 或异常消息，并涂红锚点（`DeployScreen.cs:124-136`、`:171-176`）；锁格失败用例在 `DeployScreenTests.cs:57-68`。 |

## 已有自动化覆盖

`DeployScreenTests` 静态枚举为 **7 个 PlayMode 用例**（6 个 `[Test]` + 1 个 `[UnityTest]`）：

- 初始 3 列、最大 7 列、21 个格、12 个暗格、3～4 张手牌；
- 锁格非法预览；
- 七种 footprint；
- 卡牌落位走 `CardEconomy`；
- 拖动已有单位完成合成并持久化；
- 战斗按钮仅发送命令；
- 经济层拒绝刷新时金币和刷新次数不变。

本轮未执行这些用例，因此最终报告仍需填写实际 runner 结果。

## 已修复的跨关生命周期问题

早先 `BuildDeployScreen()` 重建时会销毁承载根，可能导致重开大关后新页面挂到待销毁对象。当前主线已改为保留 `DeployRoot`，只销毁其下的 `Deploy Screen` 子对象，再将新页挂回同一个根（`Assets/Scripts/View/M1GameBootstrap.cs:156-174`）。这项静态修复成立；仍建议用“大关结束 → 重新开始 → 新部署页可交互”的 PlayMode 场景补证。

## 7 张队列中的 C3 截图位

现有 `QueueReviewCaptureCoordinator` 把 C3 与 D2 的重叠画面合并，C3 使用其中 3 张：

| 证据 | 预期标签 / 文件名片段 | 状态 |
|---|---|---|
| 初始 3×3 | `queue-stage1-deploy-initial` | **待生成、待人工看图** |
| 多形状单位 | `queue-stage1-deploy-mixed-shapes` | **待生成、待人工看图** |
| 扩建后 ≥5 列 | `queue-stage3-deploy-expanded` | **待生成、待人工看图** |

队列代码会调用 `ScreenshotTool.CaptureForQueue`，Play 时先写入 `Library/HanziDefendScreenshotStaging`，退出 Play 后再导入 `Assets/Screenshots/`（`Assets/Scripts/Editor/ScreenshotTool.cs:35-43`、`:129-179`）。旧的无标签截图不能替代以上证据。

## 最终验收证据位

- [ ] 鼠标完成“抽卡 → 拖放 → 合成 → 刷新 → 扩建 → 战斗”。
- [ ] 合成视觉动效实现并录屏/逐帧确认。
- [ ] 上述 3 张 1080×1920 截图生成且人工检查文字、暗格、footprint 与裁切。
- [ ] 本轮 PlayMode 全量实际结果。
- [ ] 既有 EditMode / PlayMode / 性能基线实际结果，不直接沿用历史 `270 + 126 + 3` 数字。
- [ ] `read_console` Error = 0。

