# BalanceRunner report

Generated: 2026-08-17 12:17:47 UTC  
Seed: `0xE1002026`  
Execution: parallel (4 workers), no-render, physics step: `False`  
Games: **100** (25 per cohort)  
Wall clock: **75.444s**  
100-game projection: **75.444s**  
Timeouts: **31**

## Lineups

`Cells` is the unlock mask the cohort deploys on: the level's starting rect
unless the lineup carries its own accumulated mask. `Levels` names the stages the
board is meaningful on — an accumulated stage-five board is not run against stage one.

| Lineup id | Name | Cells | Levels | Composition | Siege |
|---|---|---:|---|---|---|
| `A_real_s1` | A_real_s1 · 真实开局（弩车2+长矛+冰+重骑，3×3 满格） | 9 | level_1_1 | 1×`bing` + 1×`mao` + 1×`nuc` + 1×`zqi` | yes |
| `A_real_s5` | A_real_s5 · 真实积累 S5（11 单位 / 21 格 / 含弩车+冲车） | 21 | level_1_5 | 2×`gong` + 1×`bing` + 1×`chc` + 1×`dao` + 1×`huo` + 1×`mao` + 1×`nuc` + 1×`qqi` + 1×`zqi` + 1×`zu` | yes |
| `B_real_s1_nosiege` | B_real_s1_nosiege · 真实开局·无器械（长矛+冰+重骑+弓） | 9 | level_1_1 | 1×`bing` + 1×`gong` + 1×`mao` + 1×`zqi` | no |
| `B_real_s5_nosiege` | B_real_s5_nosiege · 真实积累 S5·无器械（12 单位 / 21 格） | 21 | level_1_5 | 3×`gong` + 3×`zu` + 2×`mao` + 1×`bing` + 1×`dao` + 1×`huo` + 1×`zqi` | no |

## Cohorts

`0-atk` is the share of enemy deaths that never landed an attack (ceiling 35%) and
`life p50` the median enemy lifetime (floor 6.0s). Those two are the front-line gate.
`Death Y p90` is retained as an observation only: it cannot tell a front line that
never formed apart from a siege engine out-ranging the enemy spawn door.

| Lineup | Stage | Cells | Games | Win rate | Timeouts | Mean duration | P95 duration | Mean wall/game | Mean drops | Peak units | 0-atk | life p50 | Death Y p90 | pre-castle |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| A_real_s1 · 真实开局（弩车2+长矛+冰+重骑，3×3 满格） | 1 | 9 | 25 | 92.0% | 1 | 84.3s | 138.4s | 270.43ms | 68.9 | 42 | 58.5% | 1.6s | 5.74 | 5.45 |
| A_real_s5 · 真实积累 S5（11 单位 / 21 格 / 含弩车+冲车） | 5 | 21 | 25 | 44.0% | 10 | 117.7s | 160.0s | 2298.30ms | 99.2 | 85 | 45.7% | 1.6s | 6.13 | 6.19 |
| B_real_s1_nosiege · 真实开局·无器械（长矛+冰+重骑+弓） | 1 | 9 | 25 | 0.0% | 8 | 120.9s | 160.0s | 1783.44ms | 22.9 | 53 | 31.5% | 2.8s | 3.15 | 3.15 |
| B_real_s5_nosiege · 真实积累 S5·无器械（12 单位 / 21 格） | 5 | 21 | 25 | 0.0% | 12 | 136.1s | 160.0s | 4013.81ms | 42.1 | 113 | 47.5% | 2.4s | 4.30 | 4.30 |

## Net difficulty

`budget / measured ally DPS`, normalised so stage one is 1.0 — the single number that
says how much harder the run actually gets. It must rise at every stage.

| Stage | Measured DPS | Difficulty scalar | Net difficulty (S1=1) |
|---:|---:|---:|---:|
| 1 | 296 | 1.05 | **1.000** |
| 2 | 297 | 1.25 | **1.190** |
| 3 | 294 | 1.45 | **1.381** |
| 4 | 351 | 1.65 | **1.571** |
| 5 | 338 | 1.85 | **1.762** |

## Acceptance checks

- [ ] 100 games < 60s: 75.444s measured/projected for 100 games
- [ ] No timed-out battles: 31 timeout(s)
- [ ] A win rate, stage 1: 92.0 %; target 55%-75%
- [x] A win rate, stage 5: 44.0 %; target 30%-50%
- [ ] Mean duration A_real_s1/stage 1: 84.320s; target 90-150s
- [x] Mean duration A_real_s5/stage 5: 117.717s; target 90-150s
- [ ] Heavy armor share main_20 (all acts): 14.9 %; floor 15%
- [ ] Enemy deaths with no attack landed: 58.5 %; ceiling 35%
- [ ] Median enemy lifetime: 1.633s; floor 6.0s
- [x] Net difficulty rises every stage: smallest step 0.190
- [ ] Heavy armor share main_20_stage_5 (all acts): 14.7 %; floor 15%

## Armor distribution

Counted per act; the castle is excluded from the non-boss denominator.

| Wave set | Act | Unarmored | Light | Heavy | Building | Heavy floor |
|---|---|---:|---:|---:|---:|---|
| main_20 | ACT1 | 70.8% | 29.2% | 0.0% | 0.0% | n/a / below |
| main_20 | ACT2 | 45.0% | 40.0% | 15.0% | 0.0% | PASS |
| main_20 | ACT3 | 34.8% | 34.8% | 30.4% | 4.2% | PASS |
| main_20 | ALL | 50.7% | 34.3% | 14.9% | 1.5% | n/a / below |
| main_20_stage_5 | ACT1 | 70.8% | 29.2% | 0.0% | 0.0% | n/a / below |
| main_20_stage_5 | ACT2 | 46.3% | 39.0% | 14.6% | 0.0% | n/a / below |
| main_20_stage_5 | ACT3 | 36.2% | 34.0% | 29.8% | 2.1% | PASS |
| main_20_stage_5 | ALL | 51.5% | 33.8% | 14.7% | 0.7% | n/a / below |

## CSV files

- `summary.csv`: cohort grid columns, win rate, duration and wall-clock summary
- `battles.csv`: every seed and battle result
- `unit_metrics.csv`: battle-window DPS, alive-window DPS, survival time and survivor rate per unit id
- `coin_curve.csv`: raw deterministic coin curve points for every battle
- `armor_distribution.csv`: configured wave armor counts and ratios
