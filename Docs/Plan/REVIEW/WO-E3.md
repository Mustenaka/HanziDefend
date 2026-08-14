# WO-E3 · 性能与图集 · 施工自查

**日期**：2026-08-14  
**结论**：**部分完成** —— CPU、重定向相位、稳态 GC、FX 池与图集覆盖均达标；200 单位 DrawCall 实测 **236**，未达到 `< 50`，不可报完成。

## 验收证据

| 检查 | 实测结果 | 门槛 / 结论 |
|---|---:|---|
| Performance PlayMode | **10/10 Passed** | job `e58675e770ed4cae9dcbb66043202bac` |
| 原 200 单位首次索敌 Tick | **14.446 ms** | `< 16.667 ms`，原用例、断言和阈值未改 |
| 100 / 200 / 400 首次索敌中位数 | **2.516 / 6.993 / 21.988 ms** | 5 samples / scale |
| 100→200 / 200→400 增长倍率 | **2.78x / 3.14x** | 均 `< 5.5x` |
| 200 单位相位化周期重索敌 | 峰值 **2.756 ms**，均值 **2.225 ms**，`k=6` | 峰值 `< 12 ms`；首次拿目标仍立即执行 |
| 400 单位稳态 Tick GC | **0 B / 120 ticks**（24 ticks 预热） | `0 B` |
| Sprite FX 池稳态 GC | **0 B / 256 次播放回收** | 预热容量 8，未扩容 |
| 完整 20 波真实局墙钟 | **9.991 s** | `< 45 s` |
| SpriteAtlas 覆盖 | 单位 **31**；UI / 图标 **20**；missing **0** | 覆盖通过 |
| 200 单位渲染 rig | **202 entity views，236 batches，29 set-pass** | **未达到 `< 50`** |
| 收尾 Editor 状态 | Battle scene、非 Play、非 compiling、非 running tests | Console 新增 Error **0** |

Unity 原生结果文件：
`C:/Users/Mumte/AppData/LocalLow/DefaultCompany/HanziDefend/TestResults.xml`。
DrawCall 原始日志可在
`C:/Users/Mumte/AppData/Local/Unity/Editor/Editor.log` 搜索
`[WO-E3] Render stats: batches/draw calls=236`。

## 交付内容

- `Gameplay/Performance/RetargetPhaseSchedule.cs`
  - 纯函数 API 从 `EntityId` 派生相位，不读取或推进 RNG。
  - `k` 由 `retargetInterval / fixedDeltaTime` 得到；当前配置为 6。
  - 主线已在 `BattleSystem.UpdateTarget` 接入 `GetNextDeadline(...)`，首次目标获取路径保持原语义。
- `View/Pooling/ComponentPool.cs`、`IPoolableView.cs`
  - 有界预热、可观测容量、稳定复用的通用组件池。
- `View/Pooling/PooledSpriteFx.cs`、`SpriteFxPool.cs`
  - FX 无 per-object `Update`，由池统一 `Advance(dt)`；稳态播放/回收零托管分配。
- `Editor/Performance/SpriteAtlasGenerator.cs`
  - 菜单 `HanziDefend/Performance/Rebuild Sprite Atlases` 从约定目录重建并校验覆盖，不手改 YAML。
- `Editor/Performance/SpriteAtlasSourcePostprocessor.cs`
  - 在通用美术导入规则之后保证单位、UI、图标源图可供图集无损读取。
- `Editor/Performance/RenderStatsCapture.cs`
  - 提供 200 单位瞬态 PlayMode review rig 与 `UnityStats` 捕获菜单；不修改场景资产。
- `Assets/Art/Atlases/BattleUnits.spriteatlas`
- `Assets/Art/Atlases/GameUI.spriteatlas`
- `Tests/PlayMode/Performance/BattleScalabilityPerformanceTests.cs`
- `Tests/PlayMode/Performance/PoolingPerformanceTests.cs`
- `Tests/PlayMode/Performance/RetargetPhaseScheduleTests.cs`
- performance asmdef 仅追加 `HanziDefend.View` 引用，保留其余引用；既有 `BattlePerformanceTests.cs` 未改。

## DrawCall 未达标说明

第一次 200 单位 rig 让各兵种在同一行交错，实测 **237 batches / 29 set-pass**。
第二次改为正常的同兵种编队行，仍为 **236 batches / 29 set-pass**。这说明主要瓶颈不是测试编队的纹理交错，而是当前 `BattleView` 的每实体透明 SpriteRenderer 提交 / 排序路径；仅生成 SpriteAtlas 资产不足以把提交数压到 50 以下。

按三次法则不继续用测试构型规避门槛，也不伪造 `<50` 结果。后续修复需要跨入公共 `BattleView` 渲染结构，建议用批量 Mesh / instancing 或重新设计 sorting group 与 atlas page 布局后，以同一菜单复测。

## 已绕过的 Unity 6 图集问题

显式调用 `SpriteAtlasUtility.PackAtlases` 的三种导入 / 输出格式组合均触发 Unity 6 内部
`Image invalid format` / `Unsupported Format 0`。现改为只生成、配置、保存图集资产，让 Unity 的正常 import / build 管线打包；重建菜单当前干净输出 `units=31, UI/icons=20, missing=none`。

## 残余风险

1. **阻塞验收：DrawCall 236 > 50。** 必须优化公共 `BattleView` 的渲染提交方式后复测。
2. 图集页当前为 1024、RGBA32 / Uncompressed，以避开 Unity 6 显式 pack 格式错误；WebGL 显存与包体仍需在目标平台 profile 后收紧格式。
3. 本 job 只运行独立 performance 程序集；四条全局确定性守卫与 EditMode / PlayMode 全量回归由主线收尾统一执行。
4. 未产出截图；DrawCall 是 `UnityStats` 数值验收，证据保存在 Editor.log 与本报告中。

