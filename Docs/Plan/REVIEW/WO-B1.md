# WO-B1 · 战斗系统内核 · 审核报告

**日期**：2026-08-12
**结论**：**通过**

## 证据

| 检查 | 结果 |
|---|---|
| `read_console`（error + warning） | 0 条 |
| `run_tests` EditMode | 80/80 通过 |
| `run_tests` PlayMode | **32/32 通过**（27 个方法经 `[TestCase]` 展开为 31 个战斗用例 + 1 个管线用例） |

## 我点名要验的三件事

**1. 伤害有没有绕过 `Formula` 另写一份 —— 没有。**
`Gameplay/` 全目录只有两个数值入口：`Formula.StatAtLevel`（8 项属性 × 等级）与 `Formula.Damage`，掉落走 `Formula.DropCoins`。
grep `armor|pierce|mitigation` 在 `Gameplay/` 下只命中 `BattleTypes.cs` 的构造函数传参，没有任何一处自己做减法或除法。红线守住了。

**2. 确定性回归是不是真的逐条比对事件流 —— 是。**
`SameSeed_ProducesIdenticalCompleteEventStreamEntryByEntry` 把每个事件规范化成 `kind|sequence|payload` 串，整表 `Is.EqualTo` 比对，并断言长度 > 10（防止空列表相等这种假绿）。
额外还有 `Events_HaveCompletePayloadContiguousSequenceAndAttackDamageDeathCoinOrder`，断言序列号 1..N 连续无空洞、且同一次攻击的 Attack→Damage→Death→Coin 顺序固定。这比我要求的更严。

**3. 能不能在无渲染环境下跑完 —— 能。**
`Gameplay/Battle/` 下没有 `Update` / `FixedUpdate` / 协程（`UpdateTarget` 是私有普通方法，`BattleUnitBody` 只是承载 Collider 的空壳）。整个推进只能由外部调 `Tick(dt)`。31 个战斗用例本身就是无 View 跑通的证明，WO-E1 的无头批跑地基已成立。

## 其余核对

- `Physics2D.simulationMode` 进场存旧值、`Dispose` 还原 ✅ 不会把 `Script` 模式泄漏给编辑器（这个细节很容易漏，漏了会让整个编辑器的物理停摆）
- `BattleSystem : IDisposable` ✅
- 战斗参数全在 `economy.json` 的 `battle` 块（`tickRateHz / retargetInterval / targetSearchRadius / colliderRadius / sameColumnTolerance / separationDistance`），`BattleSystem.cs` 里只剩 `1f / rules.TickRateHz`、一个 normalize 保护和一个 ±1 方向符号 ✅ 红线三守住
- 未触碰 `Assets/Scripts/View/`（仍为 0 个 cs 文件）✅
- `3 弓 vs 5 卒`：209 tick / 6.967s / 我方存活 3 / 敌方全灭 / 掉落 5 金币，由 `ThreeGongVersusFiveEZu_ManuallyTicksToLockedOutcomeAndDuration` 锁死 ✅

## 加分项

**1. 边界用异常显式拒绝，而不是静默跑出错误语义。**
`Backline / RushBase / Suicide`、带效果的单位、BOSS faction 一律抛 `NotSupportedException`，并有 `Spawn_RejectsTargetingModesOwnedByWoB3` 锁住。
这比"先随便跑着，等 WO-B3 再补"强得多——静默的错误语义会在 WO-B2/B3 里变成难查的幽灵 bug。

**2. 主动发现并绕开了 `OverlapCircleNonAlloc` 在 Unity 6000.3 的 `CS0618` 弃用**，改用 `ContactFilter2D + List<Collider2D>` 重载，保住了复用容器的零分配语义。工单里我写的是旧 API，这里的判断比工单对。

**3. `Duel_GongKillsEZuOnTickNinetyOne` 把死亡精确到第 91 tick。**
不是"打赢了就行"，而是钉死时刻。攻速、射程、伤害任何一处漂移都会立刻红。

**4. `rewardRank` 随出生请求传入而非从 `UnitDef.tier` 推断。**
第 19 波"精英潮"是波次语义不是兵种稀有度——这个区分做对了，否则同一个 `e_jia` 在第 5 波和第 19 波会被锁死成同一档掉落。

## 必改项

无。

## 建议项（建议在 WO-B2 顺手做，因为随后 C2/D1 会加入更多随机消费者）

**1. 按子系统拆分独立的 Rng 流。**

目前只有一个 Rng。`DECISIONS` 记录的索敌规则是「按距离排序 → 相同距离按 EntityId → 完全等距才走 `Data.Rng`」。
这里有两个可以顺手消掉的隐患：

- 既然已按 `(距离, EntityId)` 排序，就已经是全序，等距时的 Rng 抽取是多余的；
- 更麻烦的是，这让**抽取次数依赖于几何巧合**。等 WO-C2（抽卡）和 WO-D1（结算三选一）接入同一个 Rng 后，战场上某两个单位恰好等距就会平移后续所有随机流——WO-E1 批跑的两次结果将无法横向比较，玩家的 bug 复现种子也会失效。

**建议**：给战斗 / 抽卡 / 结算各自独立的 Rng 流（由主种子派生子种子），并考虑直接去掉等距时的 Rng 抽取。现在改是改一个构造函数，等三个消费者都接上就是重构。

**2. `targetSearchRadius: 24.0` 让物理广相变成了摆设。**

战场世界尺寸是 10.8 × 19.2（正交 size 9.6 @ 1080×1920），对角线约 22.0。搜索半径 24 覆盖全场，意味着每次重定向 `OverlapCircle` 都会返回**全部敌人**，再对全集排序取最近。
在 WO-B1 的 8 单位规模无所谓；到 WO-B4 的 200 单位目标下，就是每秒 5 次 × 200 单位 × 全集排序，而且还额外背着 GameObject + Collider 的开销——等于花了物理的成本却没拿到广相的收益。

`Nearest` 确实需要在远距离也能找到目标（否则单位不知道往哪走），所以大半径本身是合理的。留给 **WO-E3** 处理即可，方式可以是分级搜索（先小半径命中就返回）或换成纯 C# 空间哈希。**现在不用动，记在这里免得到 200 单位时才发现。**

## 本轮新增技术债

沿用施工方记录的 1 条（MCP 无筛选 EditMode `run_tests` 的进度订阅误报，严重度低）。我这轮用 `assembly_names` 筛选重跑，80/80 正常返回，可复现该现象，属 MCP 侧问题，不影响项目。
