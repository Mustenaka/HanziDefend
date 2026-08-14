# BalanceRunner report

Generated: 2026-08-14 10:16:23 UTC  
Seed: `0xE4001800`  
Execution: parallel (4 workers), no-render, physics step: `False`  
Games: **80** (10 per cohort)  
Wall clock: **10.517s**  
100-game projection: **13.146s**  
Timeouts: **10**

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
| A · 含器械（3弓+1盾+1矛+1弩兵） | 1 | 3 | 10 | 60.0% | 0 | 182.6s | 497.9s | 240.14ms | 77.8 |
| A · 含器械（3弓+1盾+1矛+1弩兵） | 5 | 3 | 10 | 30.0% | 0 | 116.9s | 141.3s | 129.95ms | 73.6 |
| B_fair_s1 · 无器械 3 列最优（3轻骑+1长矛+1卒） | 1 | 3 | 10 | 0.0% | 1 | 416.0s | 1800.0s | 536.54ms | 60.5 |
| B_fair_s1 · 无器械 3 列最优（3轻骑+1长矛+1卒） | 5 | 3 | 10 | 0.0% | 0 | 121.2s | 268.7s | 244.01ms | 57.7 |
| B_fair_s5 · 无器械 5 列最优（2链甲+2轻骑+1长矛+1卒） | 1 | 5 | 10 | 0.0% | 7 | 1310.9s | 1800.0s | 354.40ms | 68.1 |
| B_fair_s5 · 无器械 5 列最优（2链甲+2轻骑+1长矛+1卒） | 5 | 5 | 10 | 10.0% | 2 | 462.3s | 1800.0s | 317.88ms | 72.8 |
| B_worst · 无器械最差组合基准（3弓+2盾+1矛） | 1 | 3 | 10 | 0.0% | 0 | 412.2s | 858.4s | 809.78ms | 57.5 |
| B_worst · 无器械最差组合基准（3弓+2盾+1矛） | 5 | 3 | 10 | 0.0% | 0 | 158.4s | 443.0s | 383.64ms | 54.8 |

## Acceptance checks

- [x] 100 games < 60s: 13.146s measured/projected for 100 games
- [ ] No timed-out battles: 10 timeout(s)
- [x] A win rate, stage 1: 60.0 %; target 55%-75%
- [x] A win rate, stage 5: 30.0 %; target 30%-50%
- [ ] Mean duration A_siege_nub/stage 1: 182.640s; target 90-150s
- [x] Mean duration A_siege_nub/stage 5: 116.857s; target 90-150s
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
