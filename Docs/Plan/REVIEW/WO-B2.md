# WO-B2 · 波次 · BOSS · 胜负 · 审核报告

**日期**：2026-08-12
**结论**：**通过**

## 证据

| 检查 | 结果 |
|---|---|
| `read_console`（error + warning） | 0 条 |
| `run_tests` EditMode（`assembly_names` 筛选） | **103/103 通过**，2.68s |
| `run_tests` PlayMode | **51/51 通过**，4.33s |
| 波次时间线（读 `waves.json` 独立复算） | W1@2.0s / W18@53.0s / W19@57.0s(Elite) / W20@62.0s(Boss) —— 与汇报一致 |

## 我点名要验的三件事

**1. 胜负判定是否真的只有一个出口 —— 是。**
全仓 grep：`Result` 在 `Gameplay/` 下只有 `BattleSystem.cs:299` 一处赋值，就在 `TrySettle()` 体内（另一处命中是事件 payload 的自有属性，非模拟状态）。
`TrySettle()` 在模拟中只有一个调用点（`Tick` 末尾，line 272）。
配套 `IsSettled => Result != None` 三处守卫 + `ThrowIfSettled()` 拦截变更入口。没有任何散落的 `if (hp <= 0)` 分支。

**2. 同帧双杀是不是真能触发同帧 —— 是，而且证明方式比我要求的严。**
`Settlement_SameTickAllyBaseAndBossReachZero_WinPriorityAndOneResult`：双方 HP 各 1、攻击各 100，**同时断言我方基地 HP 与 BOSS HP 都为 0**，再断言 `Result == Win` 且 `Settlements` 计数为 1。
断言两边都归零，排除了「其实错开一帧、只是恰好也产出了一个结果」这种假绿。

支撑它的机制是对的：锚点（基地/BOSS）攻击意图被延迟到所有单位行动阶段结束后统一结算（`anchorAttackIntents` + `ResolveAnchorAttackIntents`），所以一个 tick 内双方都能出手。同帧双杀在这个设计下是**可达路径**，不是纸面条款。

**3. 结算冻结是否真的无副作用 —— 是。**
`Settlement_AfterResultFurtherTicksFreezeAllObservableState`：捕获完整状态串 → 再 `Tick` 30 次 → 断言状态串完全相同。
比对的是包含事件记录器在内的全部可观测状态，不是只看 `Result` 有没有变。
另有 `TrySettle_IsIdempotentAndNeverPublishesSecondResult` 锁住重复调用只发一次结算事件。

## 顺手做的 Rng 拆流（WO-B1 建议第 1 条）

`RngStreams` 从主种子按域标签（`BATT`/`CARD`/`SETT`）avalanche 派生三条独立流，注释明确写着派生规则属于**持久化回放契约**，并单独暴露每条流的初始种子供回放诊断，`SaveState` 覆盖三条流。
索敌里「完全等距额外抽 Rng」那一步已按建议移除——`(距离, EntityId)` 已是全序。
C2 抽卡与 D1 结算现在可以直接接入，不会互相平移随机序列。

## 加分项

**1. 向后兼容做得干净。** 保留 `new BattleSystem(config, seed, events)` 作为 B1 沙盒入口，完整局走新的 `CreateEncounter(...)`，新事件通过可选的 `IBattleEncounterEvents` 扩展。结果是 B1 的 31 个用例、91 tick 死亡时刻、209 tick 对局基线全部零漂移。加功能不动既有基线，这是对的做法。

**2. 临时降级是被测试锁住的，不是静默的。** `e_lang` 的 `RushBase` 在波次内暂按 `Nearest` 跑，但公共 `Spawn` 仍显式拒绝，且有 `Scheduler_RushBaseEnemyUsesTemporaryEncounterNearestPath` 专门锁住这条兼容路径。技术债也记了（中，指向 WO-B3）。

**3. 掉落档位与波次等级进了 `waves.json`**，没有塞进构造参数、也没有按波号在代码里推断。红线三守住，且 Data Editor 的校验同步跟上了。

**4. 两次停下来问我的时机是对的。** 同帧双杀的优先级、`e_lang` 是否越界提前实现 RushBase —— 都命中「跨工单/不可逆」这一类，正是修订后铁律一要求提问的情形。不该问的一个没问，该问的没漏。

## 必改项

无。

## 注意事项（不是缺陷，但必须记下来免得被误读）

**「62s Win」不能当成平衡基线。**

`FullMain20_CompletesUnderTwoSecondsWithTimelineAndRewardRanks` 跑的是**改过的配置**：

```
我方基地 HP = 1,000,000        （不可能输）
全部敌方单位 HP=1 / 护甲=0 / 攻速=0 / 移速=0   （不能动、不能打）
敌方基地 HP = 1
索敌半径 = 200
```

这个用例验证的是**波次调度、掉落档位分类（225 Normal / 14 Elite / 1 Boss）、事件流连续性、单一结算**——这些它验证得很好，断言也很扎实。但它对战斗平衡**零信息量**。

所以要分清楚：
- **波次时间线 2s / 53s / 57s / 62s 是真实的**，来自 `waves.json`，我独立复算过
- **「62s Win」不是**，那是敌人全部瘫痪时的必然结果

再叠加 `e_lang` 目前还按 `Nearest` 跑，任何平衡结论都得等 WO-B3 之后。**在 WO-E1 之前不要引用这些数字做数值判断。**
已在 WO-B3 的工单里加了「用真实数据跑一局」的交付物，届时才有第一份诚实的平衡读数。

## 本轮新增技术债

施工方记录 2 条，我确认：

| 内容 | 我的核实 |
|---|---|
| `e_lang` 的 `RushBase` 临时按 `Nearest`（中） | 属实，已被测试锁住，指向 WO-B3 偿还 |
| MCP EditMode `run_tests` 回执问题（低） | **本轮我也踩到，第四次复现**：PlayMode 作业实际 12 秒跑完，但 `get_test_job` 挂死到 1800s 超时；EditMode 需 `clear_stuck` 才能重启。确属 MCP 侧问题，不影响项目产物 |

**给后续所有工单的操作建议**：`run_tests` 一律带 `assembly_names` 筛选，`get_test_job` 用短 `wait_timeout`（30~60s）轮询，卡住就 `clear_stuck` 重跑。别在这上面耗时间。
