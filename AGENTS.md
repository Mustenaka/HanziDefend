# HanziDefend · Agent 常驻规则

汉字·兵器三国。Unity 6000.3.11f1 + URP 2D，竖屏 1080×1920，终点是微信小游戏。
当前里程碑：M1 战斗 Demo。范围见 `Docs/Plan/M1-00-总体方案.md`。

## 四条红线（违反即工单不通过）
1. 玩法规则不许写进 View 层；View 不做任何数值或胜负判断。
2. 胜负判定只有一个出口 `TrySettle()`，判定后立即冻结 tick。
3. 数值只写在 `Assets/GameData/*.json`，代码里不硬编码数值。
4. 随机一律走 `Data.Rng`（带 seed），禁止 `UnityEngine.Random`。

## 两条架构要求
- `BattleSystem.Tick(float dt)` 必须能被测试代码连续调用 N 次，玩法逻辑不埋在 `Update()` 里。
  物理用 `Physics2D.simulationMode = SimulationMode2D.Script` + `Physics2D.Simulate(dt)` 手动步进。
- `Assets/Scripts/Data/` 保持纯 C#（它只有数据结构和公式）。
  `Gameplay/` 层可以自由使用 Physics2D、Transform、协程、对象池等一切引擎能力。

## 工作方式
- 工单没写到的局部选择，自己决定并记 `Docs/Plan/DECISIONS.md`，**不要停下来问**。
- 只有这四类才停下来问，且提问要带 A/B 方案和推荐项：
  与已确认文档冲突 / 要推翻已验收模块 / 跨工单或不可逆的变更 / 两案成本差一个量级且没把握。
  **提问期间继续做不依赖该决定的部分 —— 问 ≠ 停。**
- 同一个坑试 3 次不通就换方案（哪怕更笨），失败原因记 `Docs/Plan/TECH_DEBT.md`。
- 任何时刻仓库必须能进 Play 且控制台无 Error。宁可 TODO stub，不可编译不过。
- 收工前跑 `Docs/Plan/M1-03-Agent协作协议.md` §4 的 5 项自检。

## 约定（不作为打回依据，但照着做省事）
- 场景与 Prefab 由代码 / Editor 命令生成，不手工编辑 `.unity` / `.prefab` 的 YAML。
- 美术引用走约定路径 + `art_manifest.json`，不在 Inspector 里手拖引用。
- 技能与 BUFF 做成「效果指令」数据（见 `M1-00` §2.7），由 C# 解释器执行。

## 面向微信小游戏的长期约束（现在免费，以后很贵）
- 不用反射、不用动态代码生成、不在运行期依赖 Editor-only API
- 不开实时光 / 阴影 / 后处理 / HDR
- 单张贴图 ≤ 1024，走 SpriteAtlas
- 资源加载走抽象接口，不散落 `Resources.Load`

## 常用 MCP 动作
- 改完代码：`read_console` 看编译结果；轮询 `editor_state.isCompiling` 等域重载结束再调用新类型
- 验收：`run_tests`（EditMode / PlayMode）
- 看画面：`manage_editor play` + 菜单 `HanziDefend/Screenshot` → `Assets/Screenshots/`

## 文档地图
| 文件 | 内容 |
|---|---|
| `Docs/Plan/M1-00-总体方案.md` | 范围、架构、数据契约、三阶段规则 |
| `Docs/Plan/M1-01-工单总表.md` | 工单清单与验收标准、状态看板 |
| `Docs/Plan/M1-02-美术管线规范.md` | 风格圣经、prompt 模板、抠图管线 |
| `Docs/Plan/M1-03-Agent协作协议.md` | 铁律、自检清单、验收流程 |
| `Docs/Plan/M1-04-单位设计与美术规范.md` | **克制系统、单位总表与数值、占格与卡池解锁、美术生成规范**（单位数值的唯一真源） |
| `Docs/Note/拆解文档.md` | 上游需求（含参考图） |
