# WO-B3 · 索敌策略与效果系统 · 审核报告

**日期**：2026-08-13
**结论**：**通过**

## 证据

| 检查 | 结果 |
|---|---|
| `read_console` | 干净（仅 Test Framework 自身的 2 条日志） |
| `run_tests` PlayMode | **88/88 通过**（MCP 回执，25s） |
| EditMode | **121/121 通过**（MCP 回执又挂，改读原生 `TestResults.xml` 确认） |

> EditMode 实际 121 而非汇报的 116：多出的 5 个是仓库里已有的 `ArtPipelineEditorTests`(4) 等，
> 与本单无关，全部通过。数字差异不是问题。

## 我点名要验的三件事

**1. 四种索敌是否各有「选中目标正确」的断言 —— 是，全部到位。**

| 策略 | 用例 | 断言的是 |
|---|---|---|
| Nearest | `Targeting_Nearest_SelectsTheExactClosestLivingEnemy` | 精确最近的**存活**敌人 |
| Backline | `Targeting_Backline_SelectsTheFarthestEnemyRowInsteadOfTheClosestUnit` | 选最远排**而非**最近单位 |
| RushBase | `Targeting_RushBase_SelectsEnemyBaseDespiteACloserBlockingUnit` | 有更近的拦路单位时仍锁基地 |
| Suicide | `Targeting_SuicideSelectsNearestThenExplodesOnceAndKillsItself` | 选中 + 只炸一次 + 自身死亡 |

没有一个是「跑了没崩」式的空断言。额外还有
`EnemyInRadius_IncludesExactBoundaryButExcludesOutsideAndFriendlyUnits` —— 半径边界**内外各一**，且排除友军。边界用例是最容易偷懒省掉的，这里做了。

**2. 效果解释器有没有滑向过度设计 —— 没有，边界卡得准。**
严格 6 个 op × 6 个 target，没有做通用脚本化 DSL。
`EffectCatalog_ContainsEveryM1Operation` / `ContainsEveryM1Target` 按枚举参数化——**以后谁加了一个 op 却没在数据里覆盖，测试直接红**。这是结构性守卫，比事后 review 可靠。
`trigger` / `stacking` 进 JSON，来源与施法语境放运行时 `EffectExecutionContext` 而不扩 target 集，这个切分是对的。

**3. 第一份诚实平衡读数 —— 产出了。**
`HonestBalance_RealUnmodifiedTwentyWaveConfig_CompletesExactlyOnceAndPrintsReading`，真实未改数据，seed `0xB3002026`，1 级 3弓+2盾+1矛 + 刘备：71.5s / 2145 tick / 唯一结果 **Lose**，我方全灭、敌方剩 225 个。按要求只锁「唯一结算」不锁胜负。

**怎么读这份数据**：6 个 1 级单位对 ~240 个敌人，输是正常且正确的结果。它证明的是**整条链路在未修改数据下能端到端跑通**，而不是平衡如何。真正有用的信号是量级差——参考阵容大约差一个数量级。不过 WO-03 会把整张表推翻，这份读数的数值意义到此为止，方法学意义（固定 seed + 固定阵容 + 只锁唯一结算）保留给 WO-E1。

## 加分项

**1. 自己发现并修掉了 B2 的一个红线违规。**
自爆击杀 BOSS 时会在 Tick 中途提前结算，导致 `BattleSettled` 之后仍有状态事件——这违反 B2 建立的「结算是该 Tick 最后一条状态事件」不变量。
现在改成 Tick 内效果造成的胜负一律等行动、锚点攻击、同步全部结束后再由 `TrySettle()` 落地，并加了 `SuicideKillsBoss_SettlementIsLastAndFurtherTicksFreeze` 回归。
**在自己的既有产物里找出红线违规并补回归，这是审核方最希望看到的行为。** 这个 bug 如果留到 WO-B4 表现层接事件时才炸，会非常难查。

**2. 技术债偿还得实。** `e_lang` 的临时 `Nearest` 降级删除，且把原来的兼容路径用例改写成真实行为回归 `Scheduler_WolfRushBaseBypassesAUnitStandingOnItsSpawnPoint`（狼骑绕过站在出生点上的单位直冲基地）。账本已标 ✅ 已偿还。

**3. 属性重算公式选对了。** `(base + ΣAdd) × (1 + ΣMul)` —— 同属性的多个 Mul 先相加再一次性乘，避免连续乘法带来的叠加顺序依赖。`Refresh` 按 `(effectId, sourceEntityId, targetEntityId, opIndex)` 定位，只刷期限不建实例。这两处都是效果系统最容易埋不确定性的地方。

## 必改项

无。

## 运维发现（对后续所有工单有用）

MCP `get_test_job` 的**挂起根因找到了：`wait_timeout` 参数**。
带 `wait_timeout` 会静默挂到 1800s 超时；**不带参数直接轮询秒回**（本轮 PlayMode 88/88 就是这么拿到的）。

固定用法更新为：
```
run_tests(mode=..., assembly_names=[...])   →  拿 job_id
get_test_job(job_id=...)                     →  不带 wait_timeout，隔十几秒轮一次
仍失败 → 读 C:/Users/Mumte/AppData/LocalLow/DefaultCompany/HanziDefend/TestResults.xml
```
EditMode 本轮仍报 `failed to initialize`，但原生 XML 显示 121/121 实际跑完并通过——**MCP 的失败回执不等于测试失败**，务必交叉确认再下结论。

## 本轮新增技术债

无新增。`e_lang` 那条已偿还。
