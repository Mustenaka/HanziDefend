# WO-E1 · 数值批跑与调优验收

结论：**⚠️ 部分完成（保留真实失败证据，不建议按完整验收关闭）**。

100 局双阵容批跑、确定性 CSV/Markdown 报告、无渲染/无物理步进执行路径与 `<60s` 性能门禁均已落地；Stage 5 胜率/时长和两关重甲占比达标。但最终固定种子样本仍有 **5 局 timeout**，且阵容 A 的 Stage 1 为 **48.0% / 181.507s**，分别未达到 55%–75% / 90–150s。依无人值守队列的“三次失败换方案 + 最后一轮停止”约定，不再继续试探参数，不修改战斗断言，也不放宽任何阈值。

## 最终 100 局结果

- 生成时间：2026-08-14 06:38:58 UTC
- 固定根 seed：`0xE1002026`
- 执行模式：no-render、`simulatePhysics:false`、4 worker、固定索引回填后排序写盘
- 总局数：100（2 阵容 × 2 关卡 × 每 cohort 25 局）
- 实测墙钟：**11.800s**，通过 `<60s`
- timeout：**5**，未通过 `0 timeout`

| 阵容 | 关卡 | 局数 | 胜/负/超时 | 胜率 | 平均时长 | P95 时长 | 平均终局金币 |
|---|---:|---:|---:|---:|---:|---:|---:|
| A · 含器械（3弓+1盾+1矛+1弩兵） | Stage 1 | 25 | 12/11/2 | **48.0%** | **181.507s** | 600.000s | 75.68 |
| A · 含器械（3弓+1盾+1矛+1弩兵） | Stage 5 | 25 | 10/14/1 | **40.0%** | **130.504s** | 301.167s | 75.32 |
| B · 无器械（3弓+2盾+1矛） | Stage 1 | 25 | 0/23/2 | 0.0% | 345.668s | 600.000s | 57.44 |
| B · 无器械（3弓+2盾+1矛） | Stage 5 | 25 | 0/25/0 | 0.0% | 106.904s | 261.133s | 55.04 |

## Acceptance checks

| 检查项 | 锁定阈值 | 实测 | 结果 |
|---|---:|---:|---|
| 100 局墙钟 | `<60s` | 11.800s | ✅ |
| 超时局 | `0` | **5** | ❌ |
| A / Stage 1 胜率 | 55%–75% | **48.0%** | ❌ |
| A / Stage 5 胜率 | 30%–50% | 40.0% | ✅ |
| A / Stage 1 平均时长 | 90–150s | **181.507s** | ❌ |
| A / Stage 5 平均时长 | 90–150s | 130.504s | ✅ |
| Stage 1 长期重甲占比 | `>=15%` | 15.0%（3/20） | ✅ |
| Stage 5 长期重甲占比 | `>=15%` | 18.2%（4/22） | ✅ |

部分完成摘要中的三个 Stage 1 缺口为：**胜率 48.0% < 55%、平均时长 181.507s > 150s、该 cohort 2 局 timeout**；全样本另有 A/Stage 5 1 局与 B/Stage 1 2 局 timeout，合计 5 局。

## 报告内容

真实批跑产物固定写入 `Docs/Plan/REVIEW/WO-E1-Results/`：

- `summary.csv`：cohort 胜率、时长、P95、墙钟与终局金币
- `battles.csv`：100 局的 seed、结果、结算/超时状态、基地血量与逐局摘要
- `unit_metrics.csv`：单位出场/存活数、战斗窗 DPS、存活窗 DPS、存活时长与存活率
- `coin_curve.csv`：每局完整的确定性金币事件曲线
- `armor_distribution.csv`：逐阶段及长期无甲/轻甲/重甲/建筑数量与比例
- `report.md`：人读汇总与八条锁定 acceptance checks（含明确的 `0 timeout` 失败项）
- `acceptance-failure.xml`：实际手工门禁 job 的 evaluator 失败快照，保留 8 条检查与原始失败消息

阵容 A 的关键单位指标如下；`battle DPS` 以 cohort 战斗总时长为分母，`active DPS` 以该单位存活窗为分母。

| 关卡 | 单位 | battle DPS | active DPS | 平均存活 | 存活率 |
|---|---|---:|---:|---:|---:|
| Stage 1 | 盾 `dun` | 1.977 | 3.300 | 108.721s | 48.0% |
| Stage 1 | 弓 `gong` | 8.716 | 6.970 | 75.661s | 17.3% |
| Stage 1 | 矛 `mao` | 7.856 | 13.525 | 105.433s | 44.0% |
| Stage 1 | 弩兵 `nub` | 46.696 | 87.208 | 97.189s | 48.0% |
| Stage 5 | 盾 `dun` | 2.718 | 3.762 | 94.292s | 44.0% |
| Stage 5 | 弓 `gong` | 10.921 | 9.217 | 51.542s | 9.3% |
| Stage 5 | 矛 `mao` | 10.508 | 18.648 | 73.540s | 40.0% |
| Stage 5 | 弩兵 `nub` | 69.200 | 138.522 | 65.195s | 40.0% |

金币曲线均从 45 起步；四个 cohort 的平均终局金币依次为 A/S1 75.68、A/S5 75.32、B/S1 57.44、B/S5 55.04。逐事件原始点保留在 `coin_curve.csv`，没有用采样均值替代原始曲线。

## 实现与数据变更

- `BalanceRunner` 直接串行驱动真实 `BattleSystem.Tick(1/30f)`；`simulatePhysics:false` 时才允许 cohort/game 并行，物理路径保持串行。
- false-physics 路径不读写 `Physics2D` 全局状态；每局独立系统、collector 与预生成 seed，结果按固定索引回填，写盘前固定排序。
- worker 数封顶 `min(4, Environment.ProcessorCount)`；32 worker 在本机形成严重超订阅，已通过等价测试守住并行/串行结果一致性。
- 五个小关分别绑定 `main_20`、`main_20_stage_2` … `main_20_stage_5`，形成明确的 1–5 难度梯度；更新的 `GameConfigTests` 只同步内容形态契约（逐关 waveSet ID 与 BaseHp），不是为战斗结果放宽断言。
- 最终仅调 JSON：波次数量/间隔/Stage 5 晚段收口，以及 `e_liu` ATK 24、`zqi` ATK 45、Boss HP 6000 / ATK 60；未改伤害公式、索敌、结算与既有战斗门禁。
- acceptance evaluator 是唯一阈值入口；合成 pass/fail fixture 锁住八条阈值。真实 100 局为 `[Explicit]` 手工门禁，运行时仍严格断言 evaluator 全通过，因此本次真实失败 XML 被保留，同时默认 EditMode 不会被临时调参探针永久染红。

## 验证证据

- 实际 100 局手工门禁 job `b09e0c3c931b4cd2887710c6cb351e52`：按预期 **Fail**，失败消息精确列出 5 timeout、A/S1 胜率 48.0% 与平均时长 181.507s；CSV、Markdown 与 `acceptance-failure.xml` 为本次运行证据。
- E1 runner/evaluator + 必要 GameConfig/Battle 确定性专项 job `17f39da68ad84befb178fda2d2322d43`：**13/13 Passed，0.962s**。
- `dotnet build HanziDefend.Tests.EditMode.csproj --no-restore`：**0 Error**；仅 4 条既有程序集版本 warning。
- Battle 场景持续 Play 超过 30 秒：Play 中及退出后控制台均 **0 Error**；结束时 Editor ready、非 Play，并已释放给后续工单。

## 残余问题与交接

1. A/S1 胜率仍低 7 个百分点，且平均时长高 31.507s；主要长尾来自器械阵亡后低输出清场/双方基地磨损。
2. 5 个 timeout 违反硬门禁，即使总墙钟已达标也不能视为完整通过。
3. 按队列停止规则保留当前证据最佳配置。后续若重开调优，应先对固定的这 100 个 seed 做败局分型，再只调整 Stage 1 晚段节奏/单位面板；不得提高 `maxSimulatedSeconds`、修改战斗断言或放宽 evaluator 阈值来掩盖 timeout。
