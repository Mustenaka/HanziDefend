# WO-B5 · 战斗内核性能 · 审核报告

**日期**：2026-08-13
**结论**：**通过**

## 证据（全部由我独立复跑）

| 检查 | 结果 |
|---|---|
| `read_console`（error + warning） | 0 条 |
| EditMode | **228/228 通过** |
| PlayMode | **115/115 通过**（112 功能 + 3 性能） |
| 200 单位首次重定向 Tick | **12.616 ms**（门槛 16.667 ms） |

EditMode 分布：`DeploymentGridTests` 73 / `GameConfigTests` 42 / `DataEditorTests` 39 / `FormulaTests` 34 /
`UnitCardPoolPolicyTests` 12 / `RngTests` 10 / `RngStreamsTests` 8 / **`DamageTypeMultiplierLookupTests` 5** / `ArtPipelineEditorTests` 4 / 管线 1。

## 性能收益核实

| 指标 | 优化前 | 施工方 | 我复跑 | 门槛 |
|---|---:|---:|---:|---:|
| `FullMain20` | ~5.46 s | 0.486 s | — | < 2 s |
| 真实平衡局 | > 180 s 超时 | 9.795 s | — | < 45 s |
| 200 单位重定向 Tick | — | 15.558 ms | **12.616 ms** | 16.667 ms |

数量级收益属实。C1 的性能债已实质偿还，账本标记正确。

## 确定性核实

**实际有 4 条守卫，比汇报的 3 条多一条**：
`FullMain20_SameSeedProducesIdenticalEventStreamEntryByEntry`、
`ExpandedBattle_SameSeedProducesIdenticalStateSequenceEntryByEntry`、
`SameSeed_ProducesIdenticalCompleteEventStreamEntryByEntry`、
`Scheduler_SpreadXUsesBattleSeedDeterministically`。

把索敌从物理查询换成纯 C# 空间桶，是**最容易悄悄破坏确定性**的一类改动（查询返回顺序变了、遍历顺序变了都会漂移）。四条逐条比对事件流的守卫全绿，且基线未被改写——这条我重点看了，没有问题。

## 其余核实

- 克制矩阵改成预构建 O(1) 查表，数值仍只在 `economy.json`，并有 5 个专项用例（`DamageTypeMultiplierLookupTests`）
- `Physics2D.simulationMode = Script` 与每 Tick `Physics2D.Simulate(dt)` 均保留；`Gameplay/` 下 `Rigidbody2D` / `Collider2D` **零引用**

## 加分项

**1. 性能用例是真的在测最坏情况，不是软测。**
`TwoHundredUnits_FirstRetargetTick` 在计时前先断言**每个单位进入该 Tick 时都没有目标**，计时后断言 200 个全部拿到目标、且 `TickIndex == 1`。
三重约束保证它不可能误测到一个已经预热过的便宜 Tick。性能门禁最常见的失效方式就是"测了个假的轻负载"，这里堵死了。

**2. missing-script 洪水的根因定位，是本轮最有价值的一件事。**
连续三次长 Play 都复现，静态 GUID 清理、重进 Play、延迟截图导入都不管用，最后靠 `Editor.log` 精确定位到
`[InitializeOnLoad] ArtManifestGenerator` 的延迟任务在 **Play 运行中**导入运行时 Resources 并请求强制编译。

修法也对：不是加个 try-catch 压住，而是**结构性地约定「Play 中 Editor 资产零写入零导入」**，把重建统一挪到 `EnteredEditMode`。
这个污染也一直在干扰我前几轮的验收环境，修掉是净收益。

**3. 病理输入的债记成了「低」而不是藏起来。**
空间桶在全部单位同点时仍会退化到 O(n²)——施工方明确写出来，并说明「当前不为理论最坏情况引入更复杂结构」，把多规模斜率与 GC 门禁推给 WO-E3。判断和记账都对。

## 必改项

无。

## 建议项

**1. 重定向没有相位错开——这是那 12~15 ms 的直接来源，也是性价比最高的下一步。**

`BattleSystem.cs:612`：
```
unit.NextRetargetTime = SimulatedTimeSeconds + rules.RetargetInterval;
```
没有任何 per-unit 偏移。**同一波一起出生的单位会永远同步重定向**，之后每 0.2 秒一起尖峰一次。
性能用例的名字 `FirstRetargetTick` 和它「断言 200 个全都没有目标」的构造，测的正是这个同步尖峰。

关键是：**这不是测试构造出来的人造场景。** 真实波次成组刷怪，同组单位出生时间几乎相同，同步是结构性的。

**建议**：首次赋值时加一个由 `EntityId` 派生的确定性相位偏移，例如
`+ (EntityId % k) * (RetargetInterval / k)`，把尖峰摊平到整个间隔上。
确定性完全不受影响（EntityId 派生，不消费 Rng），改动约两行，预计最坏 Tick 能降到 1/3 ~ 1/5。

不阻塞验收——现在已经达标了——但这是下一轮最便宜的一块收益。

**2. 性能门禁余量偏窄，建议先留痕、不要急着收紧或放宽。**

施工方 15.558 ms、我复跑 12.616 ms，两次相差 24%，门槛 16.667 ms。最差那次只剩 **6.6% 余量**。
不建议现在放宽门槛（那等于取消门禁的意义），也不建议因为一次偶发红就判回归。
建议等建议 1 落地后余量自然变大；若 WO-E3 时仍窄，再把它改成「记录趋势 + 显著劣化才失败」。
**现在只要知道它会抖动即可，别把一次红当成真回归。**

**3. 文档已过时——这条是我的活，已同步修掉。**

`M1-00` §2.3 / §2.8 与 `M1-01` WO-B1 条目里都还写着「索敌用 `Physics2D.OverlapCircleNonAlloc`」。
B5 之后索敌是纯 C# 空间桶，物理只剩一个不含任何刚体的空转 `Simulate(dt)`。
保留 `simulationMode = Script` + `Simulate(dt)` 作为"将来若引入物理已就位"的挂钩是合理的，
但文档不能再声称索敌走物理——否则下一个接手的 agent 会照着过时描述做决策。已改。

## 本轮新增技术债

施工方记 2 条，我确认：
- 空间桶在病理密集输入下仍可能退化（低，指向 WO-E3 的多规模斜率与 GC 门禁）
- missing-script 洪水已标 ✅ 已偿还，我复核环境确实干净了
