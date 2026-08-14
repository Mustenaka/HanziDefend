# WO-E1 BalanceRunner report

Generated: 2026-08-14 08:38:48 UTC  
Seed: `0xE1002026`  
Execution: parallel (4 workers), no-render, physics step: `False`  
Games: **100** (25 per cohort)  
Wall clock: **11.567s**  
100-game projection: **11.567s**  
Timeouts: **5**

## Cohorts

| Lineup | Stage | Games | Win rate | Mean duration | P95 duration | Mean wall/game | Mean coins |
|---|---:|---:|---:|---:|---:|---:|---:|
| A · 含器械（3弓+1盾+1矛+1弩兵） | 1 | 25 | 48.0% | 181.5s | 600.0s | 133.07ms | 75.7 |
| A · 含器械（3弓+1盾+1矛+1弩兵） | 5 | 25 | 40.0% | 130.5s | 301.2s | 123.09ms | 75.3 |
| B · 无器械（3弓+2盾+1矛） | 1 | 25 | 0.0% | 345.7s | 600.0s | 563.00ms | 57.4 |
| B · 无器械（3弓+2盾+1矛） | 5 | 25 | 0.0% | 106.9s | 261.1s | 243.99ms | 55.0 |

## Acceptance checks

- [x] 100 games < 60s: 11.567s measured/projected for 100 games
- [ ] No timed-out battles: 5 timeout(s)
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

- `summary.csv`: cohort win rate, duration and wall-clock summary
- `battles.csv`: every seed and battle result
- `unit_metrics.csv`: battle-window DPS, alive-window DPS, survival time and survivor rate per unit id
- `coin_curve.csv`: raw deterministic coin curve points for every battle
- `armor_distribution.csv`: configured wave armor counts and ratios
