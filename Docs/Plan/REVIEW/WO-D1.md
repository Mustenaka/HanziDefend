# WO-D1 · 结算系统 · 静态审核报告

**日期**：2026-08-14  
**结论**：**交付项与专项测试静态齐全，待 Unity EditMode 全量实跑后定稿。**

> 本轮按要求没有连接或占用 Unity；下列是代码与测试定义审计，不宣称当前工作树已经编译或通过 runner。

## 目标与交付逐项核对

| M1-05 要求 | 静态结果 | 实现证据 |
|---|---|---|
| 三选一纯逻辑 | 已实现 | `SettlementRewardSystem` 位于 `Gameplay/Reward`，不引用 `UnityEngine`；`CreateOffer` 按配置的 `SlotCount` 构造不可变 offer（`Assets/Scripts/Gameplay/Reward/SettlementRewardSystem.cs:61-105`）。 |
| BUFF 85% / 主动技能 15%，权重走 JSON | 已实现 | `Assets/GameData/economy.json:63-80` 定义 `slotCount: 3`、`buffWeight: 85`、`activeSkillWeight: 15` 与两个池；抽取使用 `rules.BuffWeight/ActiveSkillWeight`（`SettlementRewardSystem.cs:197-228`），没有把 85/15 写进规则代码。 |
| 每张免费重随 1 次 | 已实现 | `freeRerollsUsed` 按 slot 独立记录；第二次在没有广告 credit 时拒绝（`:107-129`）。 |
| `IAdService` + 成功 Mock，再给 1 次 | 已实现 | 接口与 `MockAdService` 在 `:8-16`；`WatchAd` 只有免费次数用完后才能给一次 credit，并防重复领（`:131-150`）。 |
| 选中进入 `RunState.ownedEffects` | 已实现 | `Select` 校验 effect 后更新 `OwnedEffects`、保存 RNG 状态并封闭本次选择（`:152-168`）。 |
| 不重复出已持有的唯一效果 | 已实现 | 每次抽取先把 `OwnedEffects` 中配置为 unique 的 id 加入排除集（`:170-181`、`:231-242`）；可叠加 BUFF 不会被错误排除。 |
| 只消费 Settlement 子流 | 已实现 | 类别和池内选择都只调用 `streams.Settlement`（`:218-228`）；发布 offer/选择时把三流状态整体快照回 `RunState`，不推进另外两流。 |
| 边界：不做 UI | 符合 | 本单规则类无 View/UI 依赖；UI 消费者独立在 WO-D3 的 `RewardScreen`。 |

## 专项测试逐项核对

`Assets/Scripts/Tests/EditMode/SettlementRewardTests.cs` 静态枚举为 **19 个展开用例**（17 个 `[Test]` + `SlotCommands_RejectIndexesOutsideOffer` 的 2 个 `[TestCase]`），超过“≥12”门槛：

- 三张、池合法性：`:20-35`；
- 同 seed 同卡同类别：`:38-46`；
- 只推进 Settlement，Battle/CardDraw 消费不扰动：`:48-74`；
- 85/15 分布：`:76-93`；
- 指定 slot 重随、每槽独立免费 1 次、失败不耗 RNG：`:95-134`；
- 广告后恰好多 1 次、提前观看拒绝、广告失败不授权：`:136-176`；
- 越界、选中写回、选后封闭：`:177-216`；
- 跨小关累积、已持有唯一效果排除、可叠加 BUFF 仍可累积：`:219-267`；
- 重复 `CreateOffer` 拒绝且不耗 RNG：`:269-277`。

## 最终验收证据位

- [ ] `SettlementRewardTests` 实跑结果（应展开 ≥19 个用例）。
- [ ] EditMode 全量结果与 XML / runner 截图。
- [ ] PlayMode 与性能基线无回归。
- [ ] `read_console` Error = 0。

本单没有截图交付要求；不要用 D3 的 UI 截图替代 D1 的确定性与随机流测试。

