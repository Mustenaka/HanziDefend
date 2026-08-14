# 美术资产规范与状态

> 本文是美术的**唯一**文档：命名规范、尺寸规范、目录结构、现有资产、待产出清单、入库要求。
> **不含风格圣经、prompt 模板、出图指导**——实测人工直出远好于 agent 指挥，结论归档在
> `Docs/Plan/DECISIONS.md`（搜 `WO-A0`）。单位占格/数值/克制见 `Docs/Plan/M1-04-单位设计与数值.md`。

流程：**人在 ChatGPT 出图（白底）→ 丢 `_inbox/候选/` → `Tools/art_intake.py` 抠图入库 → Unity 自动成 Sprite**。
已经人工抠好的批次放在 `_inbox/候选处理/`，由清单 `sourcePath` 显式映射并保留原 alpha。

最后更新：2026-08-14

---

## 1. 目录约定

```
Docs/Art/
  _inbox/候选/{资产目录}/{文件}.png     ← 人工出图的落地点，一个资产可有多个风格候选
  _inbox/候选处理/{分类}/{文件}.png     ← 已抠图 RGBA；允许中文来源名，但必须由 sourcePath 显式映射
  _inbox/字骨/{字}-字骨.png             ← 正楷字骨参考图（生成时作为结构输入）
  _inbox/参考/                          ← 已认可的风格母版
  _inbox/废案/                          ← 淘汰稿，不入库
  _review/                              ← 入库工具产出的质检 contact sheet
```

---

## 2. 命名规范

### 2.1 通用格式

```
{类别}_{名称全拼}_{规格段}_{风格全拼}_v{NN}.png
```

- 只用**小写 ASCII、数字、下划线**；不用汉字、连字符、不可解释的缩写
- 名称按全称逐字拼写：`chang_mao` / `tie_jia_bing` / `liu_bei`；「吕」写作 `lv`
- `vNN` 固定两位；筛掉候选时**不重排版本号**，保留生产身份
- 玩法数据继续用短 id（`nub` / `zqi` / `cmd_bei`），文件名与短 id 的映射由 `art_manifest.json` 显式维护

### 2.2 七个类别

| 类别 | 含义 | 规格段 | 示例 |
|---|---|---|---|
| `unit` | 战场单位（角色卡） | `{state}_{NxM}[_{缺角}]` | `unit_zhong_qi_bing_idle_3x1_xuan_tie_v01.png` |
| `cmd` | 指挥官 | `{side}_{variant}` | `cmd_liu_bei_ally_card_dan_feng_v01.png` |
| `bld` | 建筑（营地 / 城堡） | `{side}_{NxM}` | `bld_ying_ally_4x2_dan_feng_v01.png` |
| `icon` | 图标（BUFF / 技能 / 资源） | `{group}` | `icon_rui_yi_buff_dan_feng_v01.png` |
| `bg` | 背景 | `{kind}[_{NN}]` | `bg_zhan_chang_tile_dan_feng_v01.png` |
| `ui` | UI 底板 / 按钮 / 卡框 | *（无，名称即全部）* | `ui_card_bg_green_dan_feng_v01.png` |
| `fx` | 特效 | *（无）* | `fx_hit_dan_feng_v01.png` |

**枚举取值**

| 段 | 取值 |
|---|---|
| `state` | `idle` —— M1 **只有这一个**。攻击/死亡动画系统尚未设计，**不要出 attack / roar 态** |
| `NxM` | `N` = 横向格数，`M` = 纵向格数。故 `2x1` 是横图、`1x2` 是竖图 |
| 缺角 | `que_you_shang` / `que_zuo_xia` |
| `side` | `ally` / `enemy` |
| `variant` | `card`（竖版指挥官卡） / `avatar`（方形头像，由 card 裁切派生） |
| `group` | `buff` / `skill` / `res` |
| `kind` | `tile`（四方连续） / `decal`（散落装饰贴片） / `full`（整张背景） |

### 2.3 解析规则（给 `art_intake.py`）

从两端向中间锚定，中间剩下的就是名称：

```
第 1 段      = 类别
最后 1 段    = v\d{2}
倒数第 2 段  = 风格
其余按类别正则从右侧吃掉规格段：
  unit → [que_(you_shang|zuo_xia)]? , \d+x\d+ , idle
  cmd  → (card|avatar) , (ally|enemy)
  bld  → \d+x\d+ , (ally|enemy)
  icon → (buff|skill|res)
  bg   → [\d{2}]? , (tile|decal|full)
  ui / fx → 无规格段
剩下的连续段 = 名称全拼
```

`NxM` 是 `\d+x\d+` 的强锚点，所以多段名称（`tie_jia_bing`）不会与规格段混淆。

### 2.4 汉字构成的硬约束（适用于 unit / cmd / bld）

- 汉字必须逐字输入 `_inbox/字骨/{字}-字骨.png`；保留笔画数量、拓扑、方向、比例、交叉关系和负形，
  **不镜像、不旋转、不透视、不连写、不漏笔、不增笔**
- 汉字是角色身体与第一视觉层级；甲胄、材质和兵器只沿字骨外缘生长，**不能遮盖或替代笔画**

---

## 3. 尺寸规范

**判定规则：比例必须精确，绝对像素只看倍率** —— 短边 ≥ 入库目标短边的 **2 倍**即合格。
（不要写死「≥1024px」：`887×1774` 的竖图短边 887 < 1024，但相对 256 的目标仍有 3.5 倍余量，完全够用。）

| 类别 / 规格 | 比例 | 入库尺寸 | 生成建议 | 说明 |
|---|---|---|---|---|
| unit 1×1 | 1:1 | 256×256 | 1024² 起 | |
| unit 2×1 横 | 2:1 | 512×256 | 1024×512 起 | |
| unit 1×2 竖 | 1:2 | 256×512 | 512×1024 起 | |
| unit 2×2 | 1:1 | 512×512 | 1024² 起 | 含两个 L 形 |
| unit 3×1 横 | 3:1 | 768×256 | 1536×512 起 | 目前只有重骑兵 |
| **cmd card** | 2:3 | 512×768 | 1024×1536 | 竖版指挥官卡 |
| **cmd avatar** | 1:1 | 256×256 | 由 card 裁切派生，不单独出图 | 战斗 HUD 左上角 |
| **bld 4×2** | 2:1 | 1024×512 | 2048×1024 起 | 我方营地 / 敌方城堡 |
| **icon** | 1:1 | 128×128 | 1024² | BUFF / 技能 / 金币 |
| **bg tile** | 1:1 | 512×512 | 1024² | **必须四方连续可平铺** |
| **bg decal** | 任意 | 长边 ≤512 | 1024 级 | 带 alpha，随机撒 |
| **bg full** | 9:16 | 1080×1920 | 887×1774 后裁 | 若最终选整图方案 |
| ui / fx | 各异 | 见 §6 清单 | | |

常规出图格式统一：**RGB 纯白底，无 alpha**。透明通道由入库管线抠，不要求出图带。
已完成抠图的 RGBA 可走 `sourcePath` precut 分支：源画布比例不再作为构图依据，但仍要求短边达到目标短边 2 倍；管线直接使用 alpha、跳过 rembg / 去白边 / 白 rim，再裁切等比适配目标画布。

### ⚠️ `fx` 类是唯一的例外：黑底

刀光、火焰、爆炸、投射物这类是**加色混合（additive）**素材，用白底抠图会把浅色发光部分一起抠掉。

- **纯黑 `#000000` 底出图**
- 入库时走 **亮度转 alpha**（luminance→alpha），黑色部分自然全透明
- Unity 侧材质用 **Additive 混合**，不用 alpha blend

当初否掉黑底的理由（会吃掉墨黑描边）在 FX 上不成立——特效没有黑描边，只有发光。

**朝向约定**：竖屏战场，投射物一律画成**朝正上方（12 点方向）**，运行期由代码旋转。

### L 形单位的缺角

```
弩车 nu_che（缺右上）      冲车 chong_che（缺左下）
  ┌────┬────┐              ┌────┬────┐
  │ 主 │ 空 │              │ 主 │ 主 │
  ├────┼────┤              ├────┼────┤
  │ 主 │ 主 │              │ 空 │ 主 │
  └────┴────┘              └────┴────┘
```
入库时把 alpha 切成 2×2 四份，缺角象限覆盖率 **>8% 判不合格并标红**，≤8% 则把残留像素清零。

---

## 4. 入库目录结构

```
Assets/Art/
  Units/{id}/idle.png              例 Units/zqi/idle.png
  Units/{id}_enemy/idle.png        ← 程序化色相偏移生成，不出图
  Commanders/{id}/card.png
  Commanders/{id}/avatar.png       ← 由 card 裁切派生
  Buildings/{id}.png               例 Buildings/bld_ying.png
  Icons/{id}.png
  Background/{name}_tile.png
  Background/{name}_decal_{NN}.png
  UI/{name}.png
  FX/{name}.png
```

`art_manifest.json` 由 Editor 工具扫目录自动生成，运行期按 id 取图，**任何一处都不需要在 Inspector 拖引用**。

---

## 5. 现有资产（2026-08-13，26 张候选，比例 26/26 全对）

目录 `_inbox/候选/`，全部 `idle` 态、白底；其中肉盾源图为全不透明 RGBA，其余为 RGB，入库时统一按白底重新抠取 alpha。肉盾 v04/v05 为单一闭合盾壳包裹“肉盾”两字；重骑兵三款均为“重 / 骑 / 兵”一字一格，兵器不横贯字面。

| 资产 | 占格 | 实际画布 | 候选数 |
|---|---|---|---:|
| 卒 zu | 1×1 | 1254² | 1 |
| 弓 gong | 1×1 | 1254² | 1 |
| 火 huo | 1×1 | 1254² | 1 |
| 冰 bing | 1×1 | 1254² | 1 |
| 狼 lang | 1×1 | 1254² | 3 |
| 长矛 chang_mao | 1×2 竖 | 887×1774 | 1 |
| 流寇 liu_kou | 1×2 竖 | 887×1774 | 3 |
| 弩兵 nu_bing | 1×2 竖 | 887×1774 | 1 |
| 肉盾 rou_dun | 2×1 横 | 1024×512 ⚠️ | 3 |
| 大刀 da_dao | 2×1 横 | 1774×887 | 1 |
| 轻骑 qing_qi | 2×1 横 | 1774×887 | 1 |
| 山贼 shan_zei | 2×1 横 | 1774×887 | 1 |
| 重骑兵 zhong_qi_bing | 3×1 横 | 2172×724 | 3 |
| 铁甲兵 tie_jia_bing | 2×2 | 1254² | 1 |
| 链甲兵 lian_jia_bing | 2×2 | 1254² | 1 |
| 弩车 nu_che | 2×2 缺右上 | 1254² | 1 |
| 冲车 chong_che | 2×2 缺左下 | 1254² | 1 |
| 吕布 lv_bu | 2×2 | 1254² | 1 ⚠️ 类别需改 |

⚠️ **肉盾** 的 1024×512 是全套里余量最薄的（短边 512 相对入库 256 正好 2×，卡在门槛上）。可用，但别再小。

⚠️ **吕布** 按 2026-08-13 的决定不再是战场单位，改做指挥官卡（2:3 竖版）。
现有 `unit_lv_bu_idle_2x2_*` 作废，需重出为 `cmd_lv_bu_enemy_card_*`。

---

## 6. 待产出清单

### 6.1 角色

角色单位候选已齐；火 / 冰均为 1×1、无武器的光环单位。仍缺的“刘备”属于 §6.2 指挥官类别。

### 6.2 指挥官（2，新类别）
| 文件 | 汉字 | 说明 |
|---|---|---|
| `cmd_liu_bei_ally_card_*` | 备 | 我方指挥官刘备，2:3 竖版卡 |
| `cmd_lv_bu_enemy_card_*` | 吕 | 敌方指挥官吕布，2:3 竖版卡 |

指挥官只出现在 UI（左上角头像 + 技能按钮），**没有战场实体、没有占格、没有护甲/攻击类型**。

### 6.3 建筑（2，新类别）
| 文件 | 汉字 | 说明 |
|---|---|---|
| `bld_ying_ally_4x2_*` | 营 | 我方营地，建筑护甲，14000 对应的防守目标 |
| `bld_cheng_enemy_4x2_*` | 城 | 敌方防御城堡 = 关底 BOSS，建筑护甲，**可开火** |

两者延续「汉字即兵器」的构成方式，只是从人形换成建筑形。

### 6.4 背景（5 张）
方案已定：**tile + decal**（理由见 §7）。清单与 prompt 见 [`BATCH-02-特效背景指挥官.md`](BATCH-02-特效背景指挥官.md) §2。

### 6.5 UI（约 12 张）—— **建议程序化生成，不出图**
`ui_card_bg_green/blue/purple/gold`（卡牌底，2:3，入库 512×768，九宫格边 48）、
`ui_btn_primary/secondary`（8:3，入库 256×96，九宫格边 32）、
`ui_grid_cell_normal/locked`（1:1，128×128）、
`ui_panel_frame`（1:1，512×512，九宫格边 64）、
`ui_hpbar_fill_ally/enemy`（8:1，256×32）。

### 6.6 图标（22 张）
清单、prompt、文件名对照见 **[`ICONS-图标清单.md`](ICONS-图标清单.md)**。
走「汉字 + 象征件」，1:1，入库 128×128，不带外框、不分稀有度。

### 6.7 特效（10 张）
清单与 prompt 见 [`BATCH-02-特效背景指挥官.md`](BATCH-02-特效背景指挥官.md) §1。
按**四种攻击类型**组织：3 投射物 + 3 命中特效覆盖全部单位，另加爆炸 / 燃烧 / 冰霜 / 枪口闪光。
**唯一用黑底出图的类别**（加色发光素材，见 §3）。M1 不做序列帧。

---

## 7. 背景方案：已定为 tile + decal

### 走「可平铺 tile + 装饰贴片 decal」，不做整张背景

| | tile + decal | 整张 full |
|---|---|---|
| 包体 | 512² tile ≈ 100KB | 1080×1920 ≈ 800KB+，**差 8 倍** |
| 战场长度 | 无限延伸，竖向推进不受限 | 长度写死，超出要拉伸 |
| 换关卡 | 换 tile + decal 组合即可 | 每关重画一整张 |
| 屏幕适配 | 天然适配任意比例 | 非 9:16 要么变形要么裁切 |
| 美术控制力 | 弱一些，靠 decal 补 | 强 |

终点是微信小游戏、包体极小化是硬指标，且战场是竖向滚动的——**tile 方案在这个项目上几乎是单选**。
美术控制力的损失用 3~5 张 `decal`（残旗、石堆、焦土、车辙）随机撒来补。

若最终仍选整图，命名与尺寸规范 §2/§3 已经覆盖 `full`，不需要改规范。

---

## 8. 入库管线要求（`Tools/art_intake.py` = WO-A1）

```
1. 扫 _inbox/候选/，按 §2.3 解析 类别 / 名称 / 规格 / 风格 / 版本；`sourcePath` 可显式映射 `_inbox/候选处理/` 的来源文件
2. 候选选择：读一份清单文件指定每个资产选中哪个 style+vNN，未选中的不入库
3. 比例校验：按 §3 断言宽高比与倍率，不合格报错并跳过
4. 原始 RGB 抠图：rembg(GPU, birefnet-general) + 四角 flood-fill 双通道取优
   （本机 RTX 4070，放心用 GPU 模型）
   precut RGBA：直接使用源 alpha，不重复抠图、去白边或添加白 rim
5. alpha 阈值化 → decontaminate（半透边缘用邻近不透明像素颜色回填，消除白色渗色）
6. [可选开关] 程序化白 rim：alpha 膨胀 6~8px 填白，强化深色战场上的剪影
7. L 形缺角象限校验（见 §3）
8. 按 alpha 包围盒裁剪 → 等比缩放到入库目标 → 单位类底部居中并留 8% 底部空白
9. 输出到 §4 的目录结构
10. 派生：cmd avatar 由 card 裁切；敌方单位由我方图色相偏移生成
11. 缺图的 id 生成「阵营色底 + 汉字 + 黑描边」占位块——代码轨不被美术阻塞的保证
12. 质检报告 + contact sheet → _review/{日期}.png（原尺寸行 + 64px 缩略行）
```

Unity 侧 `Editor/ArtPostprocessor.cs` 按路径规则自动设 Sprite / PPU / pivot / 压缩，
**任何图进 `Assets/Art` 都不需要点 Inspector**。

### 8.1 选择清单与命令行

- `Tools/art_intake_manifest.json` 同时维护完整资产 id、长拼音 → 短 id 映射、汉字/阵营/规格和人工选择结果。
- 已抠图或中文命名来源用项目相对 `sourcePath` 精确映射；同目录未映射文件只记 warning，不会误入库。
- `selections` 可写 `"id": "style_vNN"`，也可写 `{"style":"...","version":"vNN"}`；未写的 id 自动选择数值最大的 `vNN`。
- 风格名允许包含下划线。解析时用候选父目录锚定 `{类别}_{名称}_{规格}`，再从文件末尾锚定 `vNN`，中间完整保留为 style。
- 缺少候选、候选不合格或选中项处理失败时，仍为清单中的 id 生成阵营占位块；真实图到位后用 `--force` 替换。

安装与执行（依赖均可由 pip 安装，模型由 rembg 首次运行时自动取得）：

```powershell
python -m pip install -r Tools/requirements-art-intake.txt
python Tools/art_intake.py --dry-run
python Tools/art_intake.py --force
python Tools/art_intake.py --only zqi --force
python Tools/art_intake.py --only zqi --force --no-rim
# WO-A1 已处理批次（角色走候选目录，其余来源由 sourcePath 精确映射）
python Tools/art_intake.py --inbox Docs/Art/_inbox/候选处理 --force
```

每次完整执行会把结构化质检报告与 contact sheet 写到
`Docs/Art/_review/{YYYY-MM-DD}.json/.png`；contact sheet 的红色单元表示处理失败，橙色表示双通道 alpha 差异超过阈值。
