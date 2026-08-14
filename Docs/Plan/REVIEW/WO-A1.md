# WO-A1 · 美术入库验收

**日期**：2026-08-14  
**静态验收结论**：**通过**  
**最终结论**：**待主线最终回填**（Unity Import/全量测试/Play 截图与控制台数字）

本报告只核对仓库中的入库请求、实际落盘文件、运行时 manifest、审查 JSON、导入 `.meta` 与调用点；没有占用 Unity。两项占位均有明确原因并保留稳定运行时 key，没有把不合格候选伪装成真图。

## 入库结果

`Tools/art_intake_manifest.json` 共 **68 个逻辑请求**：17 unit、2 commander、2 building、20 icon、27 FX。`Docs/Art/_review/2026-08-14.json` 记录 **75 个源候选**，其中 67 个被选择；最终 66 个选择通过质量门，1 个被严格质量门拒绝，另 1 个请求没有候选。审查汇总为 **0 error / 1 warning / 2 logical placeholders**。

| 类别 | 请求 | 运行时 manifest 项 | 真实项 | 占位项 | 核对结果 |
|---|---:|---:|---:|---:|---|
| unit | 17 | 31 | 29 | 2 | 14 个共享单位各产 ally/enemy 两份，3 个敌军专属单位各一份；`nuc` 两份为占位 |
| commander | 2 | 4 | 2 | 2 | `cmd_lv` card/avatar 真实，`cmd_bei` card/avatar 占位 |
| building | 2 | 2 | 2 | 0 | `bld_ying` / `bld_cheng` 均为真实源图 |
| icon | 20 | 20 | 20 | 0 | 全部落到 `Assets/Art/Icons/` |
| FX | 27 | 27 | 27 | 0 | 全部落到 `Assets/Art/FX/` |
| **合计** | **68** | **84** | **80** | **4 physical PNG entries** | 逻辑占位请求为 2 个；两者各生成两个运行时变体 |

运行时 `Assets/GameData/art_manifest.json` 共 **84 项**，分类计数为 building 2 / commander 4 / fx 27 / icon 20 / unit 31。实际 `Assets/Art/` 下也正好有 84 张 PNG，且每张都有 `.meta`。

## 候选选择与基地映射

- 多候选单位的清单固定选择为：`dun=cheng_men_v05`、`e_lang=dan_feng_v03`、`e_liu=xuan_qi_v03`、`zqi=bai_gang_v03`。审查 JSON 中对应 selector 与源文件一致。
- 第 18 个角色目录是吕布图；`cmd_lv` 通过 `candidatePrefix=unit_lv_bu_idle_2x2` 选中 `dan_feng_v01`，输出 commander card/avatar，没有把指挥官重新当战场单位。
- `基地5.png -> bld_ying`，`基地-反派.png -> bld_cheng`；两项审查状态均为 `ok`。该选择的 DECISIONS 留痕由主线维护。

## 缺角质量门（固定 8%，未放宽）

| id | 约定缺角 | 实测 | 结果 |
|---|---|---:|---|
| `chc` | 缺左下 `que_zuo_xia` | **4.2815%** 前景覆盖 | ≤ 8%，通过；输出阶段再强制清空声明象限 |
| `nuc` | 缺右上 `que_you_shang` | **8.18%** 前景覆盖 | **> 8%，严格拒绝**；保留 `nuc` / `nuc_enemy` 占位 |

`nuc` 的 warning 原文为 `L-corner que_you_shang foreground coverage is 8.18%; maximum is 8%.`。这是质量门正常生效的证据，不是 Import error；没有改阈值、改断言或把 8.18% 四舍五入成通过。

第二个逻辑占位是 `cmd_bei`：候选目录中没有可选刘备源图，审查记录明确为 `No selected candidate; placeholder emitted.`。两项缺口都保留稳定 key，后续替换真图不需要改调用代码。

## 图标、FX 与运行时接线

- 结算三选一的 5 个配置 id（`buff_atk_up`、`buff_front_shield`、`skill_reinforce`、`skill_breakthrough`、`skill_breach_base`）全部有同 id `icon/<effectId>`，`RewardScreen` 直接按该 key 查找。
- `effects.json` 中唯一没有同 id 图标的是 `unit_huo_blast`，但它不在 settlement reward 池；已入库但尚无 effect 定义的 12 个图标为 `buff_armor_up`、`buff_aspd_up`、`buff_burn_on_hit`、`buff_coin_gain`、`buff_hp_up`、`buff_last_stand`、`buff_move_up`、`buff_pierce_up`、`buff_slow_on_hit`、`currency_coin`、`expand_column`、`skill_meteor`。这不阻塞当前三选一。
- `BattleView` 的表现调用点已绑定 `fx/ci_bao`（命中）、`fx/bao_zha_03`（死亡/爆炸）、`fx/shuang_dong_03`（冰冻）；三者均存在于运行时 manifest，并由预热池复用。
- 战斗单位、基地、指挥官与结算图标均通过 manifest key 查找，不依赖 Inspector 手拖引用。

## 导入契约与占位文字

对当前 84 份 `.png.meta` 的静态逐项检查：

- **84/84** 为 Sprite、PPU 100、关闭 mipmap、开启 alpha transparency；WebGL 均 override 且最大 1024。
- 31 份 unit 均为 bottom-center pivot `(0.5, 0)` / custom alignment 9。
- 其余 53 份 building/commander/icon/FX 均为 center pivot `(0.5, 0.5)`。
- `BattleUnits.spriteatlas` 与 `GameUI.spriteatlas` 均已落盘。

占位生成器按占格矩形拆分多字文本框并逐字缩放；专项测试覆盖 1×1、2×1、1×2、2×2、3×1、多字不越界和 L 形声明角清空。

## 测试与审查证据

- 隔离环境命令：`uv run --python 3.14 --with pytest --with pillow --with numpy --with scipy pytest Tools/tests/test_art_intake.py -q`
- 结果：**29/29 passed，2.98s**。
- 首次直接 `uv run pytest ...` 命中系统 Anaconda（NumPy 2.2.6 + 旧 SciPy 二进制），表现为 **26 passed / 3 environment import errors**；切换隔离解释器后同一代码全绿，因此不记为产品断言失败。
- 机器审查：`Docs/Art/_review/2026-08-14.json`。
- 视觉接触表：`Docs/Art/_review/2026-08-14.png`。
- `ArtPipelineEditorTests`、全量 EditMode/PlayMode、Unity Import error 数与 Play 实拍路径：**待主线最终回填**。

## 验收项结论

| M1-05 验收项 | 结论 |
|---|---|
| 四类资源全部入库 | ✅ 68 个逻辑请求均有运行时输出；2 个明确占位如上 |
| 弩车/冲车缺角象限校验 | ✅ 固定 8% 门生效，`chc` 通过、`nuc` 8.18% 被拒 |
| 角色与基地真图 | ✅ 静态产物中 29 个 unit 运行时项与 2 个 building 项为真图；Play 实拍待回填 |
| 图标接结算三选一 | ✅ 当前 reward 池 5/5 同 id 命中 |
| FX 接命中/爆炸/冰冻 | ✅ 三个调用点与 manifest key 均存在 |
| manifest 重建、缺图保留占位 | ✅ 84 项；`nuc`、`cmd_bei` 稳定占位 |
| 多字占位不溢出 | ✅ Python 专项覆盖并全绿 |
| Unity 无 Import error、既有基线不回归 | ⏳ 待主线最终回填 |

