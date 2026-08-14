# BalanceRunner report

Generated: 2026-08-14 10:16:13 UTC  
Seed: `0xE1002026`  
Execution: parallel (4 workers), no-render, physics step: `False`  
Games: **200** (25 per cohort)  
Wall clock: **20.252s**  
100-game projection: **10.126s**  
Timeouts: **43**

## Lineups

`Grid cols` is the deployment width the cohort actually ran on: the level's own
`gridCols` unless the lineup overrides it to probe a wider shape-unlock tier.

| Lineup id | Name | Grid cols | Composition | Siege |
|---|---|---:|---|---|
| `A_siege_nub` | A · 含器械（3弓+1盾+1矛+1弩兵） | level | 3×`gong` + 1×`dun` + 1×`mao` + 1×`nub` | yes |
| `B_no_siege` | B_worst · 无器械最差组合基准（3弓+2盾+1矛） | level | 3×`gong` + 2×`dun` + 1×`mao` | no |
| `B_fair_s1` | B_fair_s1 · 无器械 3 列最优（3轻骑+1长矛+1卒） | level | 3×`qqi` + 1×`mao` + 1×`zu` | no |
| `B_fair_s5` | B_fair_s5 · 无器械 5 列最优（2链甲+2轻骑+1长矛+1卒） | 5 | 2×`lia` + 2×`qqi` + 1×`mao` + 1×`zu` | no |

## Cohorts

| Lineup | Stage | Grid cols | Games | Win rate | Timeouts | Mean duration | P95 duration | Mean wall/game | Mean coins |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| A · 含器械（3弓+1盾+1矛+1弩兵） | 1 | 3 | 25 | 48.0% | 2 | 181.5s | 600.0s | 171.88ms | 75.7 |
| A · 含器械（3弓+1盾+1矛+1弩兵） | 5 | 3 | 25 | 40.0% | 1 | 130.5s | 301.2s | 145.33ms | 75.3 |
| B_fair_s1 · 无器械 3 列最优（3轻骑+1长矛+1卒） | 1 | 3 | 25 | 0.0% | 5 | 346.6s | 600.0s | 740.85ms | 59.1 |
| B_fair_s1 · 无器械 3 列最优（3轻骑+1长矛+1卒） | 5 | 3 | 25 | 0.0% | 0 | 142.6s | 379.9s | 300.91ms | 58.2 |
| B_fair_s5 · 无器械 5 列最优（2链甲+2轻骑+1长矛+1卒） | 1 | 5 | 25 | 0.0% | 17 | 462.5s | 600.0s | 257.40ms | 68.0 |
| B_fair_s5 · 无器械 5 列最优（2链甲+2轻骑+1长矛+1卒） | 5 | 5 | 25 | 0.0% | 16 | 434.8s | 600.0s | 214.87ms | 72.9 |
| B_worst · 无器械最差组合基准（3弓+2盾+1矛） | 1 | 3 | 25 | 0.0% | 2 | 345.7s | 600.0s | 806.33ms | 57.4 |
| B_worst · 无器械最差组合基准（3弓+2盾+1矛） | 5 | 3 | 25 | 0.0% | 0 | 106.9s | 261.1s | 282.29ms | 55.0 |

## Acceptance checks

- [x] 100 games < 60s: 10.126s measured/projected for 100 games
- [ ] No timed-out battles: 43 timeout(s)
- [ ] A win rate, stage 1: 48.0 %; target 55%-75%
- [x] A win rate, stage 5: 40.0 %; target 30%-50%
- [ ] Mean duration A_siege_nub/stage 1: 181.507s; target 90-150s
- [x] Mean duration A_siege_nub/stage 5: 130.504s; target 90-150s
- [x] Heavy armor share main_20: 15.0 %; floor 15%
- [x] Heavy armor share main_20_stage_5: 18.2 %; floor 15%

## Armor distribution

Rates exclude the wave-20 building from the non-boss denominator.

| Wave set | Phase | Unarmored | Light | Heavy | Building | Heavy floor |
|---|---|---:|---:|---:|---:|---|
| main_20 | W01-W06 | 66.7% | 33.3% | 0.0% | 0.0% | n/a / below |
| main_20 | W07-W12 | 50.0% | 33.3% | 16.7% | 0.0% | PASS |
| main_20 | W13-W19 | 50.0% | 25.0% | 25.0% | 0.0% | PASS |
| main_20 | W01-W19 | 55.0% | 30.0% | 15.0% | 0.0% | PASS |
| main_20 | W20 | 0.0% | 0.0% | 0.0% | 100.0% | n/a / below |
| main_20_stage_5 | W01-W06 | 66.7% | 33.3% | 0.0% | 0.0% | n/a / below |
| main_20_stage_5 | W07-W12 | 50.0% | 33.3% | 16.7% | 0.0% | PASS |
| main_20_stage_5 | W13-W19 | 50.0% | 20.0% | 30.0% | 0.0% | PASS |
| main_20_stage_5 | W01-W19 | 54.5% | 27.3% | 18.2% | 0.0% | PASS |
| main_20_stage_5 | W20 | 0.0% | 0.0% | 0.0% | 100.0% | n/a / below |

## CSV files

- `summary.csv`: cohort grid columns, win rate, duration and wall-clock summary
- `battles.csv`: every seed and battle result
- `unit_metrics.csv`: battle-window DPS, alive-window DPS, survival time and survivor rate per unit id
- `coin_curve.csv`: raw deterministic coin curve points for every battle
- `armor_distribution.csv`: configured wave armor counts and ratios
