# WO-E2 · 手感与音频占位 · 施工自查

结论：待 Unity 导入与专项 PlayMode 验证（静态实现完成）

## 交付内容

- `FeedbackPresenter`：战斗命中/基地受击触发确定性震屏与纯表现 hit-stop；出兵、命中、胜败、结算音效调用点。
- `FeedbackEventRouter`：先转发既有 `BattleView`，再转发反馈，不修改 Gameplay 事件或胜负逻辑。
- `IFeedbackSink.NotifyDeployment(DeploymentActionKind)`：部署/移动与合成调用点直接消费规则层已经决定的结果。
- `AudioSourcePool`：固定容量、确定性复用/抢占、运行时总音量入口；clip 缺失时 no-op。
- `feedback.json`：停顿/震动/音源池/总音量/cue/占位合成参数唯一真源。
- `FeedbackPlaceholderAudioGenerator`：从 JSON 生成 7 个 WAV 与单一运行时 `FeedbackAudioCatalog`。

## 架构红线自查

- [x] View 不计算伤害、部署合法性、合成条件或胜负；仅响应 `DamageDealtEvent`、`BattleSettledEvent` 与 `DeploymentActionKind`。
- [x] 未触碰 `TrySettle()`；hit-stop 不改 `Time.timeScale`，不暂停/补偿/跳过 `BattleSystem.Tick`。
- [x] 所有可调表现数值只在 `Assets/GameData/feedback.json`。
- [x] 新增代码无 `UnityEngine.Random`；震动相位由事件 `Sequence` 确定性派生。
- [x] `Resources.Load` 只集中在 `FeedbackRuntimeAssets`，其余代码依赖抽象的 `IFeedbackAudioClipSource`。

## 主线接线点

`M1GameBootstrap` 初始化 `battleView` 后：

```csharp
feedback = FeedbackPresenter.Attach(gameObject, battleRoot.transform, battleView);
var presentationEvents = new FeedbackEventRouter(battleView, feedback);
Flow = new GameFlow(Config, seed, presentationEvents, new MockAdService());
feedback.BindFlow(Flow);
```

部署/移动成功取得 `DeploymentApplyResult result` 后调用：

```csharp
feedback.NotifyDeployment(result.Action);
```

`BindFlow` 会在进入 `Reward` 时触发结算 cue；战斗胜/败与出兵/命中由 router 自动接入。

## 验证证据

- Unity 已导入新增程序集并重生项目文件；`dotnet build HanziDefend.sln --no-restore` 编译 Data / View / E2 Editor / PlayMode tests，0 Error（仅既有 BCL 引用版本 warning）。
- PlayMode `FeedbackTests`：待编辑器空闲后运行。
- 全量 EditMode / PlayMode：待编辑器空闲后运行。
- 控制台 / Play 30 秒：待编辑器空闲后运行。
- 二进制占位音：待运行 `HanziDefend/Feedback/Regenerate Placeholder Audio` 生成。

## 截图 / 录屏建议

- 静态截图无法证明 45ms hit-stop 或音频；建议用 120fps 录屏对比开启/关闭反馈的同 seed 命中瞬间。
- 可补一张命中帧截图，确认 BattleRoot 位移时 HUD 未跟随震动；建议路径：
  `Assets/Screenshots/WO-E2-hit-feedback.png`。

## 残余与非阻塞项

- 当前占位音为单音正弦提示，正式音色属于 M1 明确延期范围。
- 总音量入口只在当前运行期生效，M1 尚无持久化设置系统。
- 本文件列出的三处主线接线由协调代理落到公共 `M1GameBootstrap` / `DeployScreen`；E2 独占实现未直接改这两个并发热点文件。

本轮新增技术债：无。
