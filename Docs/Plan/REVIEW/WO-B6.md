# WO-B6 · 单位特性机制验收

**日期**：2026-08-14  
**静态验收结论**：**通过**  
**最终结论**：**待主线最终回填**（focused/full PlayMode、控制台与最终基线数字）

五组 trait 都是 `BattleSystem` 的直接、确定性机制，没有引入通用技能 DSL；参数仍只来自 `Assets/GameData/units.json`。专项测试共 **17 条**，按五组分布为 **3 / 3 / 3 / 5 / 3**，全部断言具体伤害、位置、目标集合、持续时间或生成事件，而非“没有崩”。

## JSON 参数真源

| 单位 | trait | 当前 JSON 参数 |
|---|---|---|
| `qqi` | `Charge` | `multiplier=2.0` |
| `zqi` | `Trample` | 无额外数值字段 |
| `nub` / `nuc` | `PiercingShot` | `multiplier=2.0`、`decayRate=0.30`、`minimumMultiplier=0.50`、`excludesMainBase=true` |
| `huo` | `FireAura` | `multiplier=0.10`、`minimumMultiplier=1.8`（半径） |
| `bing` | `IceAura` | `multiplier=0.30`、`decayRate=1.0`（秒）、`minimumMultiplier=1.8`（半径） |
| `chc` | `DeathSpawn` | `unitId=zu`、`count=3` |

代码没有重拍上述数值：`BattleSystem.Traits` 读取随单位快照携带的 `UnitTraitDef`。

## 五组机制与专项覆盖

### 1. 轻骑冲锋：3 条

实现语义：`ChargeConsumed=false` 时移动速度与第一次攻击都乘 JSON `multiplier`；首次实际攻击后立刻置为 consumed，之后恢复普通攻击与普通移速。

- `Charge_FirstStrikeUsesConfiguredMultiplier`：首击伤害严格等于 `Formula.Damage(ATK × 2.0, ...)`。
- `Charge_SecondStrikeReturnsToOrdinaryAttack`：序列严格为 2 倍首击、1 倍次击。
- `Charge_AfterLethalFirstStrikeMovesAtOrdinarySpeed`：致死首击后下一 Tick 的位移按普通速度计算，避免加速状态泄漏。

### 2. 重骑践踏：3 条

实现语义：第一次 rush 直接沿线段推进到敌方最后方单位；按线段投影排序，路径宽度内每个存活敌方单位各走一次普攻伤害，并以 entity-id 集合防止重复命中。rush 完成后回到普通索敌；主城不是践踏路径目标。

- `Trample_PathDamagesEveryUnitExactlyOnce`：路径上 3 个目标按顺序各承受一次普通伤害。
- `Trample_UnitOutsideConfiguredPathWidthIsUntouched`：路径宽度外目标 HP 不变且无伤害事件。
- `Trample_CompletedRushDoesNotDamagePathTwice`：第二 Tick 不重复结算首段路径。

### 3. 穿甲箭：3 条

实现语义：以主目标方向建立射线，筛选射程与路径半径内的单位，再按前向投影/entity id 稳定排序。倍率从 2.0 开始，每穿透一个目标乘 `(1 - 0.30)`，并以 0.50 为下限；当前前三击倍率为 **2.0 / 1.4 / 0.98**。`excludesMainBase=true` 时即使主城是主目标也不会入穿透目标表。

- `PiercingShot_UsesConfiguredTwoTimesThirtyPercentDecaySequence`：逐项断言 2.0 / 1.4 / 0.98 经 `Formula.Damage` 后的具体整数伤害。
- `PiercingShot_OffAxisUnitIsNotHit`：离轴目标不进入伤害事件。
- `PiercingShot_ExcludesMainBaseWhenItIsThePrimaryTarget`：配置为冲主城时主城 HP 仍保持不变。

### 4. 火/冰光环：5 条

实现语义：攻击者附近 1.8 内的同队、存活光环载体为其攻击附加效果；光环载体本身不作为自己的 aura source。火光环每次附带目标最大 HP 的 10% 作为攻击值并统一进入伤害公式；冰光环把目标移速乘数压到 0.70，持续 1.0 秒，并发布明确的 `trait_ice_aura` 效果事件供 View 播放冰冻 FX。冰载体免疫火附伤，火载体免疫冰减速。

- `FireAura_AttackInsideRadiusAddsConfiguredPercentDamage`：断言目标最大 HP × 10% 的具体附加伤害。
- `FireAura_AttackOutsideRadiusAddsNoBurn`：1.8 半径外没有光环来源伤害事件。
- `IceAura_AttackInsideRadiusSlowsTargetByConfiguredAmount`：用单 Tick 实际位移断言 30% 减速。
- `IceBearer_IsImmuneToFireAuraDamage`：冰载体只承受原攻击，不承受火源附伤。
- `FireBearer_IsImmuneToIceAuraSlow`：火载体位移保持普通速度。

### 5. 冲车死亡掉卒：3 条

实现语义：冲车死亡时读取 `unitId/count`，在父单位位置生成 3 个同队、同等级 `zu`；`SpawnUnit(..., dropsCoin:false)` 保证子单位生成不伪造额外掉落。

- `DeathSpawn_CreatesConfiguredThreeZuChildren`：严格断言 JSON count 为 3 且生成事件恰好 3 个。
- `DeathSpawn_ChildrenPreserveParentTeamLevelAndPosition`：逐个断言队伍、等级、坐标继承。
- `DeathSpawn_EnemyChildrenPreserveTeamButDoNotDropExtraCoins`：敌方冲车也生成 3 个敌方卒，金币事件仍只有父冲车的一次。

## 红线核对

- **唯一伤害公式**：Charge、Trample、PiercingShot、FireAura 都只把攻击值交给 `DealDamage(...)`；真正扣血的唯一入口在 `BattleSystem.Effects.cs`，其中调用 `Formula.Damage(...)` 后才写 HP。trait 文件没有直接修改 HP。
- **随机流**：五组 trait 实现不消费随机数；战斗系统仍只持有由 seed 构造的 `RngStreams`，波次使用 `rngStreams.Battle`。未发现 `UnityEngine.Random` / `System.Random`。
- **确定性排序**：践踏/穿透目标先按路径投影、再按 entity id 排序，避免集合/物理查询顺序漂移。
- **同 seed 守卫存在**：`BattleSystemTests.SameSeed_ProducesIdenticalCompleteEventStreamEntryByEntry`、`BattleEncounterTests.FullMain20_SameSeedProducesIdenticalEventStreamEntryByEntry`、`BattleFeatureExpansionTests.ExpandedBattle_SameSeedProducesIdenticalStateSequenceEntryByEntry` 均仍在测试程序集；最终绿灯数字待主线回填。
- **View 不判规则**：冰冻表现消费 Gameplay 发布的显式 effect event；View 不反查 source trait、半径或减速参数来重做玩法判定。
- **范围收口**：实现仅为 `BattleSystem` partial 中的五组分支和少量单位运行态字段，没有通用 DSL、反射或动态代码生成。

## 验收矩阵

| M1-05 验收项 | 静态证据 | 结论 |
|---|---|---|
| 每项机制 ≥ 3 个具体用例 | 3 / 3 / 3 / 5 / 3，共 17 条 | ✅ |
| 冲锋首击确实 2 倍 | 具体 Formula 结果 + 次击回普通 | ✅ |
| 践踏路径全体各一次 | 路径内、路径外、防重复三向约束 | ✅ |
| 穿甲递减与主城排除 | 2.0/1.4/0.98 序列、离轴、主城 HP | ✅ |
| 光环只影响半径内友军且互免 | 内/外半径、实际位移、火冰双向免疫 | ✅ |
| 冲车确实掉 3 个卒 | 数量 + 队伍/等级/位置 + 无额外金币 | ✅ |
| 全部伤害唯一走 `Formula.Damage` | trait → `DealDamage` → `Formula.Damage` | ✅ |
| 随机只走 Battle 子流 | trait 无随机；Battle 持有 seed streams | ✅ |
| focused/full PlayMode 与既有基线 | 主线统一执行 | ⏳ 待主线最终回填 |

## 待主线最终回填

- `BattleTraitTests` focused job id 与实际 **17/17** 运行结果。
- 全量 PlayMode / EditMode / 性能程序集最终计数。
- Play 30 秒控制台 Error 数与最终仓库可进 Play 状态。

