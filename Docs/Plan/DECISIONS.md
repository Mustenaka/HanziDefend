# 决策日志

工单没写到的选择，自行决定后追加一行。**不要停下来问**——记在这里就是交代。
格式：一行一个决定，理由写一句话就够。

| 日期 | 工单 | 决定 | 理由 |
|---|---|---|---|
| 2026-08-12 | 计划 | M1 全 C#，不引入 xLua / HybridCLR | 玩法逻辑用 Lua 会失去编译期检查与 UTF 单测，打断「Codex 写 / Claude 审」的验证闭环；改用「效果指令数据」覆盖热更需求，M2 末再评估 |
| 2026-08-12 | 计划 | 撤回「逻辑层禁用 UnityEngine」 | 禁用等于放弃 Physics2D 查询、Transform、对象池，成本高于收益；改为「Tick 可手动驱动 + 带 seed 的 Rng」两条低成本要求 |
| 2026-08-12 | 计划 | 出图背景定为纯白 `#FFFFFF` | 绿底残留品红脏边；黑底与墨黑描边同值会吃掉轮廓；白底与闭合黑描边对比最大，且白色溢出肉眼不可见 |
| 2026-08-12 | 计划 | 白色 rim 由后处理程序生成，不让 AI 画 | 粗细均匀、可一键开关、不用重出图，且顺带提升深色战场上的剪影可读性 |
| 2026-08-12 | 计划 | 基地网格 3×3 起，最大 7×3 横向扩建 | 需求文档「7x3」的直读理解；网格仍按 (cols, rows) 配置驱动 |
| 2026-08-12 | WO-00 | 新建独立 `HanziDefend_2DRenderer` / `HanziDefend_RPAsset`，保留模板 PC/Mobile 渲染资产 | 避免覆盖模板资产上的既有未提交改动，并让游戏场景明确使用 2D Renderer |
| 2026-08-12 | WO-00 | `HanziDefend/Screenshot` 用指定尺寸 RenderTexture 同步渲染，临时把 Overlay Canvas 切为 Screen Space Camera | 编辑态和播放态均可用，输出尺寸稳定为 1080×1920，且能包含 uGUI Overlay 内容 |
| 2026-08-12 | WO-00 | `Battle.unity` 设为唯一启用的 Build Settings 场景，保留未改动的 `SampleScene.unity` 文件 | 后续运行入口固定为战斗骨架，同时不覆盖模板场景中已有的未提交对象 |
| 2026-08-12 | WO-01 | 显式安装 `com.unity.nuget.newtonsoft-json@3.2.2`，仅由 `JsonCodec` 封装使用，并禁用类型元数据与整数枚举 | `Data` 的 `noEngineReferences` 排除 `JsonUtility`；Unity 官方 Newtonsoft 包提供 AOT DLL，显式依赖也避免未来移除 MCP 时丢失间接包。Json.NET 内部契约反射视为 M1-00 §2.5 明确授权的数据加载例外，不扩散到业务代码 |
| 2026-08-12 | WO-01 | `units.json` 用 `units[10] + bosses[1] + commanderIds[1]` 表达 12 个锁定 id | 保持 `UnitDef`、`BossDef`、`CommanderDef` 的契约边界，同时让可部署我方/敌方/BOSS/指挥官计数稳定为 6/4/1/1 |
| 2026-08-12 | WO-01 | 一个大关的五个小关各落一条 `LevelDef`，共用一套 `main_20` 波次 | `LevelDef.stageIndex` 对应小关索引，既满足“1 个大关 5 个小关”，又不擅自扩展大关容器契约 |
| 2026-08-12 | WO-01 | 伤害常数、刷新常数与掉落值全部由 `economy.json` 传入公式；`round` 锁为非负数中点远离零 | 避免代码和 JSON 双重真源，并用 `2.5 → 3` 单测消除跨运行库的舍入歧义 |
| 2026-08-12 | WO-01 | xorshift32 的 seed=0 映射到固定非零值 `0x6D2B79F5` | xorshift32 的零状态是吸收态；固定映射保持所有输入 seed 可用且完全可复现 |
| 2026-08-12 | WO-01 | 保留 Mobile / PC 两个质量等级，并让二者都引用 `HanziDefend_RPAsset` | 最小化 ProjectSettings 结构变化，同时确保 Android、iOS、WebGL 的真实 Mobile 默认等级不再使用模板 3D RP Asset |
| 2026-08-12 | WO-02 | 数据编辑器继续使用 WO-01 已引入的 Newtonsoft，并在编辑态维护 JSON 源文本、按路径定点替换单个值 | 复用同一套契约与解析规则，同时避免完整序列化导致无关字段换行或排序，保证单格调数的 diff 只触及对应行 |
| 2026-08-12 | WO-02 | `level_1_2`～`level_1_5` 的 `gridCols/startCoins` 统一为 `3/45` | 正常流程会从 RunState 恢复这些值；统一初值可避免直接从小关调试时误把上一关进度当成关卡配置 |
| 2026-08-12 | WO-B1 | 在 `economy.json` 增加 `battle` 块，集中配置 30Hz、重定向间隔、搜索/碰撞半径与同列排斥参数 | 这些是战斗规则数值，必须遵守 JSON 唯一真源；复用既有六文件聚合入口可避免新增独立加载路径，并同步纳入 GameConfig 与 WO-02 schema 校验 |
| 2026-08-12 | WO-B1 | `Tick(dt)` 表示一个手动模拟切片，正式调用方按 `economy.battle.tickRateHz` 传入 `FixedDeltaTime`；第一发进入射程即发，后续周期为 `1 / atkSpeed` | 保持测试与无头批跑可直接驱动，同时让攻击节奏只由 UnitDef 与 battle 配置决定，不引入 Update 或隐藏累加器 |
| 2026-08-12 | WO-B1 | 战场坐标按参考图定为我方在下向 `Vector2.up` 推进、敌方在上向 `Vector2.down` 推进；“同列”按 X 容差判定并沿 Y 保持最小间距 | 与竖屏战斗参考布局一致，并让事件携带的二维朝向可直接供后续表现层使用 |
| 2026-08-12 | WO-B1 | 用 `Physics2D.OverlapCircle(..., ContactFilter2D, List<Collider2D>)` 代替已弃用的 `OverlapCircleNonAlloc`，候选按距离与稳定 EntityId 排序，完全等距时才走 `Data.Rng` | Unity 6000.3 中旧 API 会产生 CS0618；现代 List 重载保持复用容器的零分配语义，且显式排序消除物理查询顺序的不确定性 |
| 2026-08-12 | WO-B1 | B1 只暴露存活计数，不实现 `TrySettle()`；仅接受普通 UnitDef 的 `Nearest` 且无效果单位，其他策略、效果、BOSS 明确抛 `NotSupportedException` | 波次、BOSS、胜负属于 WO-B2，特殊索敌与效果属于 WO-B3；显式拒绝可避免静默运行出错误语义 |
| 2026-08-12 | WO-B1 | 敌军掉落档位由 `UnitSpawnRequest.rewardRank` 随出生语境传入，默认 `Normal`；不按 UnitDef.tier 自动推断精英 | 第 19 波“精英潮”是波次语义而非兵种稀有度，交给 WO-B2 的调度方指定可避免同一兵种在不同波次被错误锁死为同一掉落档 |
| 2026-08-12 | WO-B2 | 主 seed 用固定域标签与 32 位混合函数派生 `Battle / CardDraw / Settlement` 三条独立 `Rng` 流；索敌按 `(距离, EntityId)` 全序后不再为等距额外抽取随机 | 几何巧合不应平移抽卡或结算随机序列；固定派生规则和聚合状态快照让 C2、D1 可直接接入并保持回放兼容 |
| 2026-08-12 | WO-B2 | 保留 `new BattleSystem(config, seed, events)` 为 WO-B1 Sandbox；完整局改走 `CreateEncounter(config, levelId, seed, events)`，新增事件通过可选的 `IBattleEncounterEvents` 扩展 | 避免自动基地/波次改变 B1 的 EntityId、91/209 tick 与既有事件接收器，同时给无头完整局一个显式入口 |
| 2026-08-12 | WO-B2 | `waves.json` 显式保存每波 `rewardRank` 与每个刷怪组 `level`，`economy.battle` 保存双方基地和敌军出生中心坐标 | 掉落档位、等级和布局都是玩法数值，放进构造参数或按波号推断都会破坏 JSON 唯一真源；Data Editor 同步校验这些字段 |
| 2026-08-12 | WO-B2 | `delaySec` 定义为相对上一波开始时刻，组间并行、组内按 `intervalSec` 发出；`spreadX` 是出生中心左右半宽并使用 Battle 子流采样 | 该语义让现有 `main_20` 的 BOSS 在 62 秒登场且允许相邻波发兵重叠，时间线按 `(时刻, 波号, 类型, 组号, 序号)` 稳定排序 |
| 2026-08-12 | WO-B2 | 我方基地 HP 取所选 `LevelDef.baseHp`、护甲取 `bases.ally`；敌方基地取 `bases.enemy`；BOSS 使用 `BossDef` 的战斗属性并保持静止，effects 留给 B3 | 让五个小关的基地成长值实际生效；BossDef 没有移速字段，静止 BOSS 不伪造 UnitDef 或新增未确认的契约语义 |
| 2026-08-12 | WO-B2 | 同 tick 我方基地与 BOSS 同时归零时 `Win` 优先；只延迟以基地/BOSS为目标的攻击意图到单位行动阶段末，再唯一调用 `TrySettle()` | 这是用户确认的同时归零规则；锚点攻击延迟保证双方本 tick 都能出手，又不改变 B1 普通单位即时伤害与 209 tick 基线 |
| 2026-08-12 | WO-B2 | 波次内部把 `e_lang` 的 `RushBase` 暂按 `Nearest` 运行，公共 `Spawn` 仍显式拒绝特殊索敌 | 用户选择保持 B3 边界且不改数据真源；完整 20 波可运行，真实 RushBase 语义继续由 WO-B3 统一实现 |
| 2026-08-12 | WO-B3 | 为 `EffectDef` 增加必填的 `trigger/stacking` 生命周期字段；来源、施法位置与显式相邻单位列表放在运行时 `EffectExecutionContext`，不扩展 target 集 | JSON 继续只表达 §2.7 的六种 op/六种 target；触发时机与叠加规则必须数据化，而来源和部署邻接是每次执行的运行态语境 |
| 2026-08-12 | WO-B3 | `Nearest` 按距离/EntityId 全序，`Backline` 按敌方推进方向选最远排且仅在无单位时回退基地，`RushBase` 只锁敌方基地，`Suicide` 只锁战斗单位并在 UnitDef.range 内触发自爆 | 四种策略都保持确定性；RushBase 明确绕过拦路单位，自爆不误炸基地且接触阈值继续来自 JSON 数值 |
| 2026-08-12 | WO-B3 | `Stack` 建独立效果实例；同一属性的多个 `Mul` 加成相加后一次乘到基础值，`Refresh` 按 `(effectId, sourceEntityId, targetEntityId, opIndex)` 只刷新期限；属性重算为 `(base + ΣAdd) × (1 + ΣMul)` | 避免连续乘法造成叠加顺序差异，锁定可复现的来源追踪、到期回滚与刷新语义；所有 Damage 仍唯一走 `Formula.Damage`，护盾先吸收结算值 |
| 2026-08-12 | WO-B3 | 效果生命周期事件放进可选 `IBattleEffectEvents` 并与既有事件共用全局序列；`ConfigureCommander("cmd_bei")` 立即挂被动且覆盖后续出生友军，主动首次可用、之后按 JSON 冷却 | 不破坏旧事件订阅者，同时让 View/无头批跑能仅凭事件重建效果、治疗、护盾和金币变化；指挥官所有效果 id 与冷却仍由 GameData 决定 |
| 2026-08-13 | WO-B3 | Tick 内效果造成的胜负只在该 Tick 行动、锚点攻击与同步全部结束后由 `TrySettle()` 落结果；Tick 外手动施效仍可立即结算 | 保证 `BattleSettled` 永远是结算 Tick 的最后一个状态事件，修复自爆击杀 BOSS 后仍继续改状态的冻结红线，同时保留工具/技能入口的同步反馈 |
| 2026-08-12 | WO-B3 | 第一份平衡基线固定为 level_1_1、seed `0xB3002026`、1 级 `3 gong + 2 dun + 1 mao` 并配置 `cmd_bei`，只锁“20 波后唯一结算”而不锁胜负 | 阵容与 seed 固定便于后续 WO-E1 纵向对比；当前未调平衡，测试若锁 Win/Lose 会把临时数值误当产品规则 |
| 2026-08-12 | WO-A0 返工研究 | 正式改写风格圣经前，先走「弓构形母版 → 同构形风格覆写 → 弓/卒/吕迁移验证」；单位线宽与字芯比例仅作为候选参数试验 | 同时更改字形、构图与风格无法定位失败原因；原 8px 描边缩至 64px 只有 0.5px，须先以实图验证新下限 |
| 2026-08-12 | WO-A0 美术生成 | 废弃此前由文字风格词主导的「漆绘国潮 / 木版门神 / 焦墨矿彩 / 皮影武将」试验；后续以用户选定的多张效果参考图作为风格主导，文字只补充汉字、兵器、占格、动作和入库约束 | 实测参考图驱动的生成效果显著优于仅靠长 prompt 定义风格，且更符合后续由用户在 ChatGPT 中生成、下载到 _inbox 的工作流 |
| 2026-08-13 | WO-A0 二次返工 | 角色生产改为「标准楷体字骨锁定 → 围绕字骨生长外观 → 每角色 3 个材质/配色候选 → 选定后再做攻击态」；字骨统一存 `Docs/Art/_inbox/字骨/{字}-字骨.png` | 首批直接从效果图迁移导致复合字变形、文字识别退居装饰且全员青玉审美疲劳；先锁字骨可把字形正确性设为最高优先级，多候选再解决材质与武器朝向同质化 |
