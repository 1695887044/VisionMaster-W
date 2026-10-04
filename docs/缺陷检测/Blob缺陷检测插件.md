# Blob 缺陷检测插件（Plugin.BlobDetect）

> **这是本插件的唯一文档。** 原先分散的 4 份开发/修复记录已全部并入本文件。
>
> 本文件分两层读法：
> - **零基础 / 操作者** → 读第 0 ~ 3 章 + 3.7~3.9 节（原理直觉、界面说明、逐步操作、参数速查、FAQ、排障）
> - **开发者 / 维护者** → 重点读第 4 章（异常与修复记录）与第 5 章（附录：搭配算子、HALCON 算子速查、实战案例、源码索引、回归断言）

---

## 目录

- [第 0 章 这是什么](#第-0-章-这是什么)
- [第 1 章 设计思想](#第-1-章-设计思想)
- [第 2 章 技术特点](#第-2-章-技术特点)
- [第 3 章 使用方式](#第-3-章-使用方式)
- [第 4 章 异常与修复记录](#第-4-章-异常与修复记录)
- [第 5 章 附录](#第-5-章-附录)

---

# 第 0 章 这是什么

**Blob 缺陷检测 = "把可疑的暗斑/亮点找出来，数一数有几个、多大，再判合格不合格"。**

> 生活比喻：一张白纸上洒了墨点。你要做的三件事是——① 把墨点和白纸分开（二值化）；② 数有几个墨点、每个多大（连通域 + 面积）；③ 按规矩判"超过 3 个就不合格"（判定）。Blob 做的就是这三件事。

**Blob** 是 Binary Large Object 的缩写，这里指"二值图里连成一片的区域"——一块连通的异常区域就是一个 Blob。

它在流程里的位置（归在 **「缺陷检测」** 分组）：

```
[图像采集] → [创建 ROI] → [图像预处理] → [Blob 缺陷检测] → [CSV记录] / [结果上报] / [C#脚本]
                                        ↓ 可选输入
                              MaskRegion(检测区) / ExcludeRegion(排除区)
```

**一句话**：模板匹配解决"目标在哪"，预处理解决"图干不干净"，而 **Blob 解决"有没有缺陷、缺陷多大几个、合不合格"**——它是"有无 / 脏污 / 划痕"类检测的主力算子。

---

# 第 1 章 设计思想

## 1.1 核心原理：五步流水线

Blob 检测的骨架永远是这五步（`DetectBlob` 的实现顺序与此一致）：

```
① 转灰度        → 彩色图变黑白（单通道图直接用）
② 二值化        → 按阈值把"可疑像素"切出来（固定/自动/动态三种方式）
③ 形态学        → 去毛刺（开运算）、补孔（闭运算）、合并断裂（合并半径）
④ 拆连通域      → 一片片区域拆开，每片 = 一个候选缺陷
⑤ 筛选 + 判定   → 按面积/圆度/长宽比/触边筛掉噪声，再与规格比对给 OK/NG
```

最后叠一张**标注图**（原图 + 缺陷红圈 + 编号 + 判定文字），让人一眼看到"缺陷在哪、判了什么"。

## 1.2 五种阈值方式怎么选（新手最容易卡的地方）

| 方式 | 原理 | 什么时候用 | 要调的参数 |
|---|---|---|---|
| **固定阈值** | 灰度落在 `[MinGray, MaxGray]` 区间的像素全要 | 背景干净、明暗稳定 | `MinGray` / `MaxGray` |
| **自动阈值** | HALCON `binary_threshold` 的 Otsu 法，自动找"最能分开两类"的那个灰度 | 想省事、或产品换型不想重调阈值 | 只需选亮/暗极性 |
| **动态阈值** | `var_threshold`：拿每个像素跟它**周围一小块**的平均灰度比，比背景暗/亮多少就提出来 | **明暗不均**（打光不匀、曲面、有渐变） | 局部窗口 `VarMaskWidth/Height`、标准差权重、绝对偏移 |

> 打光不匀是现场最常见问题——固定阈值在这种图上"一头判死一头漏检"，这时就该换**动态阈值**。
> 亮暗同检（`DetectBrightAndDark`）只在**自动/动态**下生效：它会用两种极性各做一次二值化再取并集，一次把"暗划痕 + 亮亮点"都检出来。

## 1.3 三条设计原则（读懂了少踩 80% 的坑）

**原则①：筛选 ≠ 判定**（最容易被混为一谈）

| 概念 | 问的问题 | 参数 | 后果 |
|---|---|---|---|
| **筛选** | "这个东西**算不算缺陷**？" | `MinArea`、`MaxBlobArea`、`MinCircularity`、`MaxAspectRatio`、`ExcludeBorderPx` | 超了 → **不计数**，等于没看见 |
| **判定** | "这个缺陷**合不合格**？" | `MaxDefectCount`、`MinDefectCount`、`MaxSingleArea`、`MaxTotalArea` | 超了 → **判 NG** |

> 早期版本把这俩合一，把筛选上限写死 `1e7`，结果大画幅（>1000 万像素）上整块背景被静默滤掉——既不计数也不 NG，**漏检还报 OK**。这是本插件最严重的一次事故（见 4.2 ①）。

**原则②：配置坏了最多结果不对，绝不炸流程**

所有越界参数在进入 HALCON 前做**静默夹取**（`NormalizedParameters`）。典型：`MinArea` 填得比上限还大时，`select_shape` 会直接抛 `#1304` 把整条流程带崩——插件改为夹取后继续跑。

**原则③：像素当量不参与判定**

`PixelSizeMm` 只影响 `MaxAreaMm2` / `TotalAreaMm2` 两个附加输出，**判定始终按像素做**。避免"改了当量就改判定结果"这种隐式耦合。

## 1.4 配置态 / 运行态严格隔离

宿主会为"打开配置界面"和"正式运行"各造一个实例。本插件用 `_isConfigInstance` 守卫把**预览、自动适配**全部限制在配置态。

为什么必须这么做：放到运行实例上会——① 产线每帧在 UI 线程多跑一遍全图算法 + 直方图 + 离屏渲染；② 运行实例按**首帧图像**静默改写阈值/面积参数，现场表现为"参数自己变了"。工业软件里这比"参数不理想"严重得多。

## 1.5 输出数组的"顺序契约"

HALCON 区域数组本身**没有顺序约定**，下游按索引取"第 N 个缺陷"拿到的其实是算子内部顺序（不可预期）。所以插件定死 `SortMode`（不排序 / 面积从大到小 / 从上到下从左到右），并保证这 8 个数组端口逐项对齐：

`CenterRows` / `CenterCols` / `DefectAreas` / `DefectWidths` / `DefectHeights` / `DefectCircularities` / `DefectAspectRatios` / `DefectPhis`

---

# 第 2 章 技术特点

## 2.1 元数据

| 特性 | 值 |
|---|---|
| `Name` | Blob 缺陷检测 |
| `GroupName` | 缺陷检测 |
| `Description` | 阈值分割 + 连通域分析，检出划痕/暗斑等缺陷并按个数与面积判定 OK/NG；支持固定/自动/动态阈值与亮暗同检，支持检测/排除区域，输出逐缺陷面积、圆度、长宽比、方向等特征 |
| `ShortName` | 放大镜图标（FontAwesome `f002`） |

> 与仓库其它插件同一写法：**流程节点 + 配置界面 ViewModel 合一**。`RunAlgorithm` 跑算法给端口赋值；属性/命令/预览服务于配置界面。

## 2.2 端口完整清单（全部固定端口，无动态端口）

### 输入（3 个）

| 端口 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `SrcImage` | HImage | ✔ | 待检测图像 |
| `MaskRegion` | HRegion | ✖ | **检测区域**：只在该区域内找缺陷（接创建 ROI 的 `MaskRegion`）。未连线 = 整图 |
| `ExcludeRegion` | HRegion | ✖ | **排除区域**：区域内候选一律丢弃 |

> 排除区域用在哪：螺丝孔、二维码、标记载体这类"位置固定、永远不该报"的误检源。触边排除只能处理图像边缘，处理不了画面中间的固定干扰。

### 输出（16 个，只增不改）

**原始 7 个（名字/类型/顺序永不变更，老方案连线不受影响）**

| 端口 | 类型 | 说明 |
|---|---|---|
| `DefectImage` | HImage | 标注图（原图 + 缺陷红圈 + 判定文字） |
| `Defects` | HRegion | 缺陷区域对象集合 |
| `DefectCount` | int | 缺陷个数 |
| `MaxArea` | double | 最大单缺陷面积（像素） |
| `IsOk` | bool | 判定结果（true = OK 合格） |
| `CenterRows` | HTuple | 各缺陷中心行坐标数组 |
| `CenterCols` | HTuple | 各缺陷中心列坐标数组 |

**第一批补全 6 个**：`DefectAreas`(HTuple)、`DefectWidths`(HTuple)、`DefectHeights`(HTuple)、`TotalArea`(double)、`MaxAreaMm2`(double)、`TotalAreaMm2`(double)

**第二批逐缺陷形状特征 3 个**：`DefectCircularities`(HTuple 圆度)、`DefectAspectRatios`(HTuple 长宽比)、`DefectPhis`(HTuple 方向角，度)

> 为什么补形状特征：分拣场景要在下游把"圆斑=气泡 / 长条=划痕"分流。光靠筛选参数把特征**筛掉**不够，得把每个缺陷的特征值作为数组输出（与 `CenterRows` 等同一顺序逐项对齐）。

## 2.3 配置项（按 5 组）

### ① 二值化

| 配置 | 类型 | 默认 | 说明 |
|---|---|---|---|
| `ThresholdMode` | 枚举 | `Fixed` | 固定 / 自动 / 动态阈值 |
| `DetectTarget` | 枚举 | `Dark` | 亮缺陷 / 暗缺陷 |
| `DetectBrightAndDark` | bool | `false` | 亮暗同检（仅自动/动态生效） |
| `MinGray` | double | `0` | 固定阈值下限灰度 |
| `MaxGray` | double | `128` | 固定阈值上限灰度 |
| `VarMaskWidth` | int | `15` | 动态阈值局部窗口宽 |
| `VarMaskHeight` | int | `15` | 动态阈值局部窗口高 |
| `VarStdDevScale` | double | `0.4` | 动态阈值标准差权重 |
| `VarAbsThreshold` | double | `2` | 动态阈值绝对灰度偏移 |

> `MaxGray` 默认 128 = 8 位灰度图的中位，是"不针对任何特定产品"的通用起点。⚠️ 这是**绝对灰度值、隐含 8 位图假设**：图像若是 uint2（12/16 位，0~4095），128 会落在极暗处，需按位深换算（12 位图约取 2048）。

### ② 特征筛选

| 配置 | 类型 | 默认 | 说明 |
|---|---|---|---|
| `MinArea` | double | `30` | 面积下限，小于它的连通域当噪点丢弃 |
| `MaxBlobArea` | double | `0` | 单缺陷面积**筛选**上限（0 = 不限，取图像像素总数） |
| `MinCircularity` | double | `0` | 圆度下限（0~1，1 = 正圆） |
| `MaxAspectRatio` | double | `0` | 长宽比上限（长边/短边） |
| `ExcludeBorderPx` | int | `0` | 排除触边缺陷的边界带宽（像素） |

> `ExcludeBorderPx` 为什么必须有：打光边缘效应是固定的误检源（自带样图上就有不少缺陷落在右缘/底缘），而上游 ROI 只能整体裁形状、没法"往里缩一圈"。

### ③ 形态学

| 配置 | 类型 | 默认 | 说明 |
|---|---|---|---|
| `OpenRadius` | double | `0` | 开运算半径（去毛刺/断细桥），0 = 不做 |
| `CloseRadius` | double | `0` | 闭运算半径（补孔/连断口），0 = 不做 |
| `FillHoles` | bool | `false` | 是否填孔 |
| `MergeRadius` | double | `0` | 相邻缺陷合并半径 |

> 合并半径用在哪：一条划痕常被噪声断成三四段，个数虚高好几倍，判定必然 NG。做法是 `union1` → `closing_circle` → `connection`。⚠️ **必须先 `union1`**：直接对区域数组做形态学，HALCON 是逐对象处理、合不到一起。

### ④ 判定规格

| 配置 | 类型 | 默认 | 说明 |
|---|---|---|---|
| `MaxDefectCount` | int | `0` | 缺陷个数上限（超即 NG，0 = 零容忍） |
| `MinDefectCount` | int | `0` | 缺陷个数下限（少于即 NG，0 = 不判定） |
| `MaxSingleArea` | double | `100` | 单个缺陷面积上限（超即 NG） |
| `MaxTotalArea` | double | `0` | 缺陷总面积上限（超即 NG） |

> `MinDefectCount` 用在哪：**存在性检测**——Blob 在产线上大量用于"这个标记有没有"。此时"一个都没检到"恰恰是坏消息，只靠上限的话 0 个会判 OK。
> `MaxSingleArea` 默认值已与其余参数对齐为"0 = 不启用"。历史版本里 0 的语义是"任何缺陷都 NG"，与本插件其余规格参数相反而易踩坑（已对齐）。

### ⑤ 输出

| 配置 | 类型 | 默认 | 说明 |
|---|---|---|---|
| `PixelSizeMm` | double | `1.0` | 像素当量 mm/px（只影响 mm² 输出，不参与判定） |
| `SortMode` | 枚举 | `None` | 不排序 / 面积从大到小 / 从上到下从左到右 |
| `DisplayViewIndex` | int | `1` | 运行显示窗口（0 = 不发布，1~9） |

> `DisplayViewIndex` 为什么必须有：检测完成后操作员要在产线屏幕上直接看到"缺陷在哪、判了什么"，而配置对话框只在调参时打开。**NG 也要发布**——恰恰是判 NG 时操作员最需要立刻看到缺陷在哪。

## 2.4 HALCON 算子与算法链

### 主链路（`DetectBlob`，10 步）

| 步 | 算子 | 作用 |
|---|---|---|
| 1 | `count_channels` → `copy_image` / `rgb1_to_gray` / `access_channel`×3 + `rgb3_to_gray` | 转灰度（1/3/4 通道分别处理） |
| 1.5 | `reduce_domain` | 有 `MaskRegion` 时把计算域缩到 ROI |
| 2 | `threshold` / `binary_threshold` / `var_threshold` + `union2` | 二值化（亮暗同检时两种极性取并集） |
| 2.5 | `difference` | 减去 `ExcludeRegion` |
| 3 | `opening_circle` / `closing_circle` | 形态学去毛刺 / 补孔 |
| 4 | `connection` | 拆连通域 |
| 5 | `union1` → `closing_circle` → `connection` | 合并相邻缺陷 |
| 6 | `fill_up` | 填孔 |
| 7 | `select_shape` | 按 `area`（+ 可选 `circularity`）筛选 |
| 8 | `area_center` + `smallest_rectangle1` | 候选区面积/中心/外接矩形 |

> ⚠️ 本插件**没有**用 `dilation_circle` / `erosion_circle` / `sort_region` / `dyn_threshold` / `boundary`。形态学用复合算子；排序是 C# 里对索引列表排序（`BuildKeepOrder`）而非 `sort_region`；触边判定是自己算外接矩形（`TouchesBorder`）而非 `boundary`+`intersection`。

### 排序 / 判定 / 渲染（`ArrangeJudgeRender`）

`count_obj` → `region_features`（`circularity` / `phi`）→ `tuple_max` / `tuple_sum` → `gen_rectangle1`（清单联动高亮黄框）→ `disp_text` + `dump_window_image`（标注图）→ `select_obj` + `concat_obj`（按索引重建区域）

### 直方图 / 底图

`gray_histo` / `gray_histo_range`（直方图）、`get_image_type` / `get_domain` / `min_max_gray`（取**实际**灰度范围）、`scale_image` + `convert_image_type`（非 byte 图线性拉伸到 0~255）、`append_channel`（丢 alpha 后拼回 3 通道）

## 2.5 性能与独特实现

| 手段 / 设计 | 做法 | 收益 |
|---|---|---|
| **预览两级缓存 + 轮次号** | 检测组参数指纹 + 源图 hash 做键；后台结果回来对不上轮次号 = 过期，只释放不回填 | ⭐⭐⭐ 大图调参不卡窗口，连点刷新不会旧图盖新图 |
| **预览后台化** | UI 只拷图 + 适配；检测/直方图/渲染全在后台线程 | ⭐⭐⭐ 根治大图界面冻结 |
| **配置态守卫** | `_isConfigInstance` 守住预览与自动适配 | ⭐⭐⭐ 产线不白烧 CPU、参数不会"自己变" |
| **静默夹取** | `NormalizedParameters` 把所有越界值夹回合法区间 | ⭐⭐⭐ 配置坏了不炸流程 |
| **筛选 vs 判定分开** | 两套参数两套语义 | ⭐⭐⭐ 杜绝"漏检还报 OK" |
| **离屏窗口缓存** | 按图像尺寸缓存 `open_window(...,"buffer",...)` | ⭐⭐⭐ 避免 HALCON 反复开关窗口 #9302 死锁 |
| **注解源对象释放** | `dump_window_image` 后立刻 `shot.Dispose()` | ⭐⭐⭐ 修掉 500 次约漏 160MB 的泄漏 |
| **阈值/面积自适应** | 按实际灰度范围取最暗（最亮）25% 作保守带；面积按画幅比例放大 | ⭐⭐ 给"可用起点"而非"正确值" |
| **直方图可拖动阈值线** | 拖标记线直接改 `MinGray`/`MaxGray` | ⭐⭐ "阈值该切在哪"从看着调变拖着调 |

---

# 第 3 章 使用方式

## 3.1 安装与启用

1. **无需单独安装**：正常编译解决方案即产出 `Modules/Plugin.BlobDetect.dll`（由 `Plugins/Directory.Build.targets` 自动投递，无注册代码）。
2. 依赖 HALCON 原生库（随软件分发）。
3. 首次使用前关掉宿主 VisionMaster 进程再生成（运行中的程序锁着插件 DLL）。
4. 重开软件，工具箱里出现「Blob 缺陷检测」（**缺陷检测** 分组）。

## 3.2 配置界面总览（两列）

```
┌─────────────────────────┬──────────────────────────────────┐
│ 左：参数（可滚动）         │ 右：预览                           │
│  图像输入                 │  页签：「图像视图」/「数据输出」      │
│   输入图像 / 检测区域 /    │                                   │
│   排除区域                │  图像视图 = 标注图                  │
│   「按当前图像重新适配」   │  数据输出 = 缺陷清单 + 灰度直方图     │
│  ① 二值化                │                                   │
│  ② 特征筛选              │  底部：状态信息栏（Info/Warn/Error） │
│  ③ 形态学                │                                   │
│  ④ 判定规格              │                                   │
│  ⑤ 输出                  │                                   │
└─────────────────────────┴──────────────────────────────────┘
```

## 3.3 第一次配检测（6 步）

**第 1 步｜接图**：把前级（采集 / ROI / 预处理）图像连到 `SrcImage`。

**第 2 步｜选阈值方式**：左栏「① 二值化」选方式。
- 图干净、明暗稳定 → **固定阈值**
- 想省事 → **自动阈值**
- **打光不匀 / 曲面 / 有渐变 → 动态阈值**

**第 3 步｜看直方图定阈值**：右侧切到「数据输出」页签，看**灰度直方图**（可拖动阈值标记线）。把线拖到"背景峰"和"缺陷峰"之间。

**第 4 步｜设筛选**：「② 特征筛选」设 `MinArea`（小于它的噪点丢掉，默认 30）。噪声多就调大。如需排除打光边缘，设 `ExcludeBorderPx`。

**第 5 步｜设判定规格**：「④ 判定规格」填允许的个数/面积上限。**默认是零容忍**（`MaxDefectCount=0`），看到默认即 NG 是正常的——它在提醒你"该填规格了"。

**第 6 步｜看标注图**：右侧「图像视图」看红圈是否圈对了。清单里点某一行，图上会给那个缺陷套**黄框**联动高亮。

## 3.4 检测区 / 排除区怎么用

| 想做什么 | 怎么做 |
|---|---|
| 只在某个区域内检测 | 把创建 ROI 的 `MaskRegion` 连到 `MaskRegion` 端口 |
| 忽略某个固定干扰（螺丝孔/二维码） | 把对应 ROI 连到 `ExcludeRegion` 端口 |
| 忽略图像边缘误检 | 用 `ExcludeBorderPx`（设边界带宽像素） |

> 检测区域语义与 `reduce_domain` 一致：候选区先与它**取交集**再做后续处理，压在区域边界上的缺陷按**裁剪后**的面积计。

## 3.5 灰度直方图：新手最该学会的工具

直方图横轴是灰度（0~255），纵轴是该灰度的像素数量。图上会画出当前 `MinGray`/`MaxGray` 两条标记线，**可直接用鼠标左右拖动**——拖到两个峰之间的谷底，就是最佳分割点。

> 自适应给的只是"可用起点"（取最暗/最亮的 25% 作保守带），**不是正确值**。现场仍需按直方图微调——这正是直方图存在的意义。

## 3.6 参数调节速查表

| 现象 | 调哪里 |
|---|---|
| 缺陷没检出来（漏检） | 阈值区间没覆盖缺陷灰度 → 看直方图重设 `MinGray`/`MaxGray`；或 `MinArea` 太大 |
| 把背景当缺陷（全是 NG） | 阈值太宽 → 收窄；或 `MinArea` 太小，调大 |
| 打光不匀、一头漏一头死 | 换**动态阈值**，调 `VarMaskWidth/Height`（一般取缺陷尺寸的 2~3 倍） |
| 缺陷个数虚高（一条划痕算成 3 个） | 调 `MergeRadius`（合并断裂），或 `CloseRadius`（连断口） |
| 边缘总有假缺陷 | 调 `ExcludeBorderPx` |
| 固定位置的螺丝孔总误报 | 接 `ExcludeRegion` |
| 某个区域外不需要检测 | 接 `MaskRegion` |
| 缺陷里有孔洞导致面积偏小 | 勾 `FillHoles` |
| 大画幅相机（>1000 万像素） | 面积规格要按面积比例放大；`MaxBlobArea` 默认取图像像素总数 |
| 想区分气泡（圆）和划痕（长条） | 看 `DefectCircularities` / `DefectAspectRatios` 数组，或在下游用 C#脚本分流 |

## 3.7 FAQ

**Q1** 为什么默认一上来就判 NG？
默认 `MaxDefectCount=0`（零容忍）。这是**有意的**——通用平台算子不该假装知道你产品的规格值。你看到 NG 就会意识到"要去填规格"。

**Q2** 判 NG 算不算"执行失败"？
**不算**。NG 是正常结果（`Success` 仍为 true），只写 `ErrorMessage` 说明原因。只有程序级异常才 `Fail`。

**Q3** 12/16 位相机（uint2）为什么效果不对？
`MaxGray` 默认 128 隐含 8 位图假设。12 位图（0~4095）要按位深换算，约取 2048。或用「按当前图像重新适配」。

**Q4** 标注图全白看不清？
16 位图早期会这样（`convert_image_type` 是**截断**不是缩放，uint2 的 1632 会变 255）。已修：现在按**实际灰度范围**线性拉伸到 0~255。

**Q5** 缺陷很多时会不会卡？
画**面**上的编号最多画 300 个（`MaxRenderMarkers`）、清单最多 200 行（`MaxDefectRows`），端口与判定是**全量**不受影响。

**Q6** 数组端口的顺序是固定的吗？
按 `SortMode` 定死，8 个数组逐项对齐。默认 `None` 是区域顺序；要"最大缺陷排第一"选 `AreaDescending`。

**Q7** 16 位图能出标注图吗？
能，底图按实际灰度范围拉伸后生成。

## 3.8 现场排障

| 现象 | 排查 |
|---|---|
| 一个缺陷都没检出 | ① 阈值区间没覆盖（看直方图）② `MinArea` 太大 ③ 极性选反（暗缺陷选成了亮缺陷）④ 输入是彩色图但转灰度失败 |
| 检到一大片（整块背景） | 阈值太宽；或 `MaxBlobArea` 筛选上限没生效（确认不是旧版本写死 1e7 的行为） |
| 缺陷个数虚高 | 一条划痕断成多段 → `MergeRadius`；或噪声点太多 → `MinArea` 调大 / `OpenRadius` 去毛刺 |
| 画面边缘总误报 | `ExcludeBorderPx`；或接 `ExcludeRegion` 排除固定干扰 |
| 参数自己变了 | 已修（配置态守卫）。如仍出现，确认运行的是新版本 |
| 预览一直不刷新 | 已修（防抖定时器绑 UI Dispatcher）。旧版本在后台线程创建的定时器 Tick 永不触发，**无日志无法归因** |
| 大画幅漏检还报 OK | 已修（`select_shape` 上限改为图像像素总数） |
| 标注图没有编号文字 | 已修（`disp_text` 必须用 `"window"` 坐标系，`"image"` 在无显示会话环境画不出来） |

---

# 第 4 章 异常与修复记录

> 本章记录**真实发生过的问题**（来自代码注释与开发文档）。每条给：现象 → 原因 → 修法。

## 4.1 缺陷修复总览

| 级别 | 问题 | 一句话 |
|---|---|---|
| P0 | 运行实例也跑配置态预览 | 每帧多跑全图算法 + 按首帧静默改参数 |
| P0 | `select_shape` 面积上限写死 1e7 | 大画幅整块背景被静默滤掉，**漏检还报 OK** |
| P0 | `MinArea > 上限` | HALCON `#1304` 炸流程 |
| P0 | 16 位图标注底图死白 | `convert_image_type` 是截断不是缩放 |
| P0 | 非 `IDisposable` 端口不清零 | 失败轮下游读到上一轮脏数据 |
| P0 | HALCON 源对象泄漏 | `dump_window_image` 后未释放，500 次约漏 160MB |
| P1 | `DispatcherTimer` 创建在后台线程 | Tick 永不触发，**预览静默失效且无日志** |
| P1 | HALCON 23.05 不认识 `'elongation'`（`#3101`） | 改自己用 `smallest_rectangle1` 算长宽比 |
| P1 | `disp_text` 用 `"image"` 坐标 | 无显示会话环境整段画不出来 |
| P1 | 检测目标切换后阈值停在暗侧 | 默认值守卫把 `DetectTarget` 也算进去了 |
| P2 | `MaxGray` 默认 140 | 拿验证样图当调参目标 → 改 128 |
| P2 | `MaxSingleArea=0` 语义相反 | 与其余"0=不启用"对齐 |
| P2 | 4 通道图直接拒绝 | 改为取前 3 通道 |
| P2 | 预览失败仍摆上一张正常图 | 补 `PreviewImage = null` |
| P3 | WPF `StaticResource` / `GridLength` / 合并字典 | XAML 三个坑 |

## 4.2 P0 级详解

### ① `select_shape` 面积上限写死 1e7 → 大画幅漏检还报 OK

**现象**：在 >1000 万像素的相机上，整块背景（面积超过 1e7）被静默滤掉——既不计数也不 NG，**漏检却报 OK**。

**原因**：`select_shape` 的筛选上限写死常量 `1e7`，且早期版本只有"判定用的 `MaxSingleArea`"、没有独立的筛选上限。

**修法**：拆成两个参数（筛选 `MaxBlobArea` vs 判定 `MaxSingleArea`）；`select_shape` 上限改为**图像像素总数**；常量 `AreaClampMax` 只用于参数夹取（放宽到 1e12）。

### ② `MinArea > 上限` → HALCON `#1304` 炸流程

**现象**：用户把 `MinArea` 填得比上限还大时，`select_shape` 抛 `#1304`（Wrong value of control parameter 4）把整条流程带崩。

**修法**：进入算子前**静默夹取**下限不超过上限。按本插件"配置坏了最多结果不对，绝不炸流程"的原则。

### ③ 16 位图标注底图整片死白

**现象**：12/16 位相机的标注底图全白，底图信息全丢。

**原因**：`convert_image_type(..., 'byte')` 是**截断**不是缩放。实测 uint2 灰度 1632 转出来就是 255。

**修法**：先按**实际灰度范围**（`min_max_gray`，不按像素类型硬编码）线性拉伸到 0~255 再转。

### ④ 非 `IDisposable` 端口不清零 → 脏数据

**现象**：本轮执行失败时，下游读到**上一轮**的 `DefectCount`/`IsOk`/`MaxArea` 等值。

**原因**：基类的 `AutoDisposeRoundOutputs` 只处理 `IDisposable`（HImage/HRegion），`int`/`bool`/`double`/`HTuple` 端口的上一轮值会原样留下。

**修法**：`RunAlgorithm` 开轮显式重置所有非 `IDisposable` 端口。这是**极易漏的镜像 bug**。

### ⑤ HALCON 源对象泄漏

**现象**：长时间运行内存只增不减。

**原因**：`dump_window_image` 产出的 `shot` 是"源对象"，`new HImage(shot)` 是独立句柄，但 HALCON .NET 是**引用计数语义**——不释放源对象会留下一个引用计数。实测 500 次不释放约泄漏 160MB。

**修法**：包装成 `HImage` 后立刻 `shot.Dispose()`。

### ⑥ 运行实例跑配置态预览

**现象**：产线每帧多跑一遍全图算法 + 直方图 + 离屏渲染，CPU 白烧；且运行实例按首帧图像静默改写参数，现场表现为"参数自己变了"。

**修法**：加 `_isConfigInstance` 守卫，预览与自动适配只在配置态执行；端口 `ValueChanged` 订阅也只在配置态挂上。

## 4.3 P1 级详解

### ⑦ `DispatcherTimer` 创建在后台线程 → 预览静默失效

**现象**：改参数后预览纹丝不动，且**不留日志、不报错、无法归因**。

**原因**：`DispatcherTimer` 归属"创建它的那个线程"的 Dispatcher。后台线程（如 HTTP 收图链路）上 new 出来的定时器寄生在一个永不泵消息的 Dispatcher 上，`Tick` 永远不触发。

**修法**：改为首次使用时显式绑定到 UI 线程 Dispatcher（不在字段初始化器里直接 new）。

### ⑧ HALCON 23.05 不认识 `'elongation'`（`#3101`）

**现象**：用 `select_shape` / `region_features` 的 `'elongation'` 特征报 `#3101 Unknown feature`。

**原因**：本仓库 HALCON 版本（23.05）不支持该特征名。**仓库脚本模板里教用户写 `'elongation'` 是错的**（`ScriptTemplates.cs:238`）。

**修法**：改用 `smallest_rectangle1` 自己算长宽比，语义与 elongation 一致且在任何版本都成立。

### ⑨ `disp_text` 用 `"image"` 坐标不渲染

**现象**：缺陷编号文字整段画不出来。实测探针 `image=0 / window=17`（像素数）。

**原因**：无显示会话的环境里，buffer 窗口的 `disp_obj` 不生效、image part 不成立，`"image"` 坐标的 `disp_text` 画不出来。

**修法**：统一用 `"window"` 坐标系（总是可靠）。

### ⑩ 检测目标切换后阈值停在暗侧

**现象**：`DetectTarget` 从"暗缺陷"改到"亮缺陷"，阈值区间应该翻到最亮那段，结果仍停在暗侧、与意图相反。

**原因**："是否出厂默认值"的守卫把 `DetectTarget` 也算进去了——一改就判成"用户调过参数"，于是永远不再自动适配。

**修法**：切换目标时记一笔 `_pendingAdaptOnTargetChange`，下次刷新时补做一次适配。

## 4.4 代码级踩坑汇编

| # | 现象 | 原因 | 正确做法 |
|---|---|---|---|
| 1 | 合并半径不生效 | 直接对区域数组做形态学，HALCON 逐对象处理 | 必须先 `union1` 再 `closing_circle` 再 `connection` |
| 2 | `MinArea > 上限` 炸流程 | `select_shape` `#1304` | 进入算子前静默夹取 |
| 3 | 16 位底图死白 | `convert_image_type` 是截断 | 先按实际灰度范围 `scale_image` 拉伸 |
| 4 | 长宽比算不出 | `'elongation'` 不被支持 | `smallest_rectangle1` 自算 |
| 5 | 编号文字不显示 | `disp_text` 用 `"image"` 坐标 | 用 `"window"` 坐标 |
| 6 | HALCON 窗口死锁 | 反复 open/close 窗口 `#9302` | 按图像尺寸缓存复用窗口 |
| 7 | 内存只增不减 | 引用计数语义，源对象未释放 | 包装后立刻 `shot.Dispose()` |
| 8 | 预览定时器不触发 | 后台线程创建 `DispatcherTimer` | 绑 UI 线程 Dispatcher |
| 9 | 失败轮读到旧值 | 非 `IDisposable` 端口不清零 | 开轮显式重置 |
| 10 | 文字没白底框 | 只给 `box_color` 不画框 | 必须同时给 `'box','true'` |

## 4.5 HALCON API 踩坑

| # | 现象 | 原因 | 正确做法 |
|---|---|---|---|
| 1 | `'elongation'` `#3101` | 本版本不支持该特征名 | 自算长宽比 |
| 2 | `#1304` Wrong value of control parameter 4 | `select_shape` 下限 > 上限 | 夹取 |
| 3 | `#9302` 死锁 | 反复 open/close 窗口 | 缓存复用 |
| 4 | uint2 转 byte 后死白 | `convert_image_type` 截断 | 先拉伸 |
| 5 | `disp_text` 无显示会话画不出 | `"image"` 坐标不成立 | `"window"` 坐标 |
| 6 | `disp_text` 无白底框 | 缺 `'box','true'` | 同时给两个参数 |
| 7 | 形态学合并不到一起 | 区域数组逐对象处理 | 先 `union1` |
| 8 | 内存泄漏 | 引用计数，源对象未释放 | `Dispose` 源对象 |

## 4.6 已知边界与待办

- **分级判定**：目前只有 OK/NG 两档，无"警告"档。
- **滞后阈值**：未实现（防临界抖动）。
- **按 mm² 判定**：判定恒按像素做，mm² 只是附加输出。
- `ScriptTemplates.cs:238` 的 `'elongation'` **仍是错的**（模板侧未同步修）。
- 其余 13 个插件尚未切换到 `Plugin*` 公共样式（Blob 是试点）。
- `Win11Card` 与 `PluginCard` 仍是两套值。
- 圆度/方向特征（`DefectCircularities`/`DefectPhis`）算不出时只丢这两列，不影响判定（try/catch 降级）。
- 缺陷编号恒开，无开关。

---

# 第 5 章 附录

## 5.1 附录 A：搭配使用的算子清单

### A.1 上下游接线

★ = 与 Blob 直接相关。

| 工程目录 | 中文名 | 分组 | 怎么接 | 一句话用途 |
|---|---|---|---|---|
| Plugin.ImageAcquisition ★ | 图像采集 | 常用工具 | 接 `SrcImage`（上游） | 取图 |
| Plugin.CreateRoi ★ | ROI | 常用工具 | `MaskRegion` / `ExcludeRegion` 接它的区域输出 | 圈检测区 / 排除区 |
| Plugin.PreProcessing ★ | 图像预处理 | 图像处理 | 接 `SrcImage` | 洗图（去噪、二值化前置） |
| Plugin.Matching | 模板匹配 | 定位 | 定位后把 ROI 送给 Blob | 先定位再检测 |
| **Plugin.BlobDetect** | **Blob 缺陷检测** | **缺陷检测** | — | 检出缺陷并判定 |
| Plugin.CaliperMeasure | 卡尺测量 | 测量 | 可与 Blob 并列做不同尺寸项 | 量尺寸 |
| Plugin.CSharpScript ★ | C#脚本 | 逻辑控制 | 接 `DefectCircularities`/`DefectAspectRatios` 分流 | 气泡 vs 划痕分类 |
| Plugin.DataRecord ★ | CSV记录 | 数据处理 | 接 `DefectCount`/`MaxArea`/`IsOk` | 存档追溯 |
| Plugin.ExcelExport ★ | Excel报表 | 数据处理 | 接结果 | 导出报表 |
| Plugin.ResultUpload ★ | 结果上报 | 数据处理 | 接 `IsOk`/`Count` | 上报 MES |

### A.2 典型接线

```
① 基本检测
图像采集.OutputImage ──▶ BlobDetect.SrcImage ──▶ IsOk ──▶ { CSV记录 / 结果上报 }

② 限定区域（推荐）
ROI.MaskRegion  ──▶ BlobDetect.MaskRegion
ROI.ExcludeRegion ──▶ BlobDetect.ExcludeRegion
预处理.Image ──▶ BlobDetect.SrcImage

③ 缺陷分类分流
BlobDetect.DefectCircularities / DefectAspectRatios ──▶ C#脚本（圆斑=气泡 / 长条=划痕）
```

> **位置原则**：Blob 是"检测判定"节点，前面要有图（采集/预处理/ROI），后面接记录/上报/分流。

## 5.2 附录 B：HALCON 算子速查

### B.1 按用途分组

**① 通道与灰度**

| 算子 | 用途 |
|---|---|
| `count_channels` | 判通道数（1/3/4） |
| `copy_image` | 拷一份（单通道） |
| `rgb1_to_gray` | 3 通道转灰度 |
| `access_channel` ×3 + `rgb3_to_gray` | 4 通道取前 3 通道转灰度（丢 alpha） |
| `reduce_domain` | 缩小计算域到 ROI |

**② 二值化**

| 算子 | 用途 |
|---|---|
| `threshold` | 固定阈值 |
| `binary_threshold`（`max_separability` = Otsu） | 自动阈值 |
| `var_threshold` | 动态阈值（局部窗口比较） |
| `union2` | 亮暗同检：两种极性候选区取并集 |
| `difference` | 减去排除区域 |

**③ 形态学**

| 算子 | 用途 |
|---|---|
| `opening_circle` | 开运算（去毛刺/断细桥） |
| `closing_circle` | 闭运算（补孔/连断口） |
| `fill_up` | 填孔 |
| `union1` + `closing_circle` + `connection` | 合并相邻缺陷（**必须先 union1**） |

**④ 连通域与筛选**

| 算子 | 用途 |
|---|---|
| `connection` | 拆连通域 |
| `select_shape` | 按 `area` / `circularity` 筛选 |
| `area_center` | 面积 + 中心行列 |
| `smallest_rectangle1` | 外接矩形（自算长宽比/触边） |
| `region_features` | 取 `circularity` / `phi` |
| `count_obj` | 个数 |
| `select_obj` + `concat_obj` | 按索引顺序重建区域 |

**⑤ 显示与标注**

| 算子 | 用途 |
|---|---|
| `gen_rectangle1` | 清单联动高亮黄框 |
| `open_window(...,"buffer",...)` | 离屏窗口（**要缓存复用**） |
| `disp_obj` / `set_color` / `set_line_width` | 画区域 |
| `disp_text`（`"window"` 坐标 + `'box','true'`） | 写文字 |
| `dump_window_image` | 抓窗口成图（**源对象要 Dispose**） |
| `gen_empty_obj` | 空结果兜底（防下游拿 null） |

**⑥ 底图与直方图**

| 算子 | 用途 |
|---|---|
| `get_image_type` / `get_domain` / `min_max_gray` | 取**实际**灰度范围 |
| `scale_image` + `convert_image_type` | 线性拉伸到 0~255 再转 byte |
| `append_channel` | 丢 alpha 后拼回 3 通道 |
| `gray_histo` / `gray_histo_range` | 直方图 |

## 5.3 附录 C：HALCON 实战案例

### 案例 1｜固定阈值检暗斑（最常用）

```hdevelop
* ① 转灰度（单通道图直接透传，不要对灰度图再 rgb1_to_gray —— 会报错）
rgb1_to_gray (Image, Gray)
* ② 固定阈值：0~128 = 暗缺陷
threshold (Gray, Region, 0, 128)
* ③ 去毛刺 + 拆连通域
opening_circle (Region, Opened, 1.5)
connection (Opened, Connected)
* ④ 按面积筛掉噪点
select_shape (Connected, Defects, 'area', 'and', 30, 100000000)
* ⑤ 统计与判定
count_obj (Defects, Number)
area_center (Defects, Areas, Rows, Cols)
```

### 案例 2｜动态阈值（打光不匀的救命稻草）

```hdevelop
rgb1_to_gray (Image, Gray)
* 局部窗口 15×15，比周围暗 StdDevScale×标准差 + AbsThreshold 就提出来
var_threshold (Gray, Region, 15, 15, 0.4, 2, 'dark')
connection (Region, Connected)
select_shape (Connected, Defects, 'area', 'and', 30, 1e12)
```

> 窗口尺寸经验：取**缺陷尺寸的 2~3 倍**。太小会把缺陷自己算进背景，太大会跟不上渐变。

### 案例 3｜合并断裂的划痕

```hdevelop
* 一条划痕被噪声断成 3 段 → 个数虚高 → 必判 NG
* 做法：先 union1 合成一个区域集，再闭运算连断口，最后重新拆连通域
union1 (Defects, Union)
closing_circle (Union, Closed, 2.5)
connection (Closed, Merged)      * ← 现在才是 1 个
```

> ⚠️ 直接对区域数组做 `closing_circle` 是**逐对象处理**、合不到一起，必须先 `union1`。

### 案例 4｜长宽比筛选（划痕 vs 气泡）

```hdevelop
* 本仓库 HALCON 23.05 不认识 'elongation'（#3101），用 smallest_rectangle1 自算
smallest_rectangle1 (Defects, R1, C1, R2, C2)
Width  := C2 - C1 + 1
Height := R2 - R1 + 1
Ratio  := max2(Width, Height) / max2(1, min2(Width, Height))   * 短边夹 ≥1 防除零
* 筛掉 Ratio > 5 的细长条（或反过来只留细长条 = 划痕）
```

### 案例 5｜排除触边缺陷

```hdevelop
* 缺陷外接矩形碰到最外 N 像素就丢弃（打光边缘效应）
get_image_size (Image, ImgW, ImgH)
smallest_rectangle1 (Defects, R1, C1, R2, C2)
* 触边判据：R1 < N | C1 < N | R2 >= ImgH-N | C2 >= ImgW-N
```

### 案例 6｜16 位图标注底图（别直接转 byte）

```hdevelop
* 错误：convert_image_type 是截断 —— uint2 的 1632 会变成 255，整片死白
* convert_image_type (Gray16, Gray8, 'byte')          ← 错

* 正确：先按实际灰度范围线性拉伸
get_domain (Gray16, Domain)
min_max_gray (Domain, Gray16, 0, Min, Max, Range)
scale_image (Gray16, Scaled, 255.0 / max2(1, Max - Min), -Min * 255.0 / max2(1, Max - Min))
convert_image_type (Scaled, Gray8, 'byte')            * ← 对
```

## 5.4 附录 D：源码索引

### D.1 文件清单（`Plugins/Plugin.BlobDetect/`）

| 文件 | 职责 |
|---|---|
| `BlobDetectPlugin.cs` | **核心**（2233 行）：端口、配置、算法、预览、判定、渲染调度 |
| `BlobDetectEnums.cs` | 枚举（`ThresholdMode` / `DetectTarget` / `DefectSortMode` / `StatusLevel`） |
| `BlobDetectView.xaml` | 配置界面（833 行） |
| `BlobDetectView.xaml.cs` | 视图桥接 |
| `AnnotationRenderer.cs` | 标注图渲染器（离屏窗口 + 编号 + 黄框） |
| `GrayHistogram.cs` | 直方图数据模型 |
| `HistogramPlot.cs` | 直方图矢量绘制控件（可拖阈值线） |
| `EnumDisplayNameConverter.cs` | 枚举→中文名 |
| `NumberValidationRule.cs` | 数值校验规则（非法值进不了 VM） |

> 本插件**没有** `Models/`、`Services/` 目录。数据模型是 `BlobDetectPlugin.cs` 内部的私有嵌套类（`BlobResult` / `BlobParams` / `DetectPack`）+ 命名空间级 `DefectRow` + 独立的 `GrayHistogram`。

### D.2 关键方法与位置

| 方法 | 位置 | 说明 |
|---|---|---|
| `RunAlgorithm()` | `:1341` | 流程执行（开轮重置 → 检测 → 端口赋值 → 发布预览） |
| `DetectBlob()` | `:1544` | **阶段一**：10 步检测链 |
| `ArrangeJudgeRender()` | `:1742` | **阶段二**：排序 + 特征 + 判定 + 渲染 |
| `TryToGrayImage()` | `:1499` | 1/3/4 通道转灰度（算法/直方图/自适应三处共用） |
| `RefreshPreview()` | `:863` | 预览入口（UI 拷图 → 后台跑 → 回 UI 回填） |
| `PreviewRunCore()` | `:964` | 后台两阶段（检测缓存复用 + 排序判定渲染） |
| `TryAdaptCore()` | `:1201` | 按当前图像自适应阈值与面积 |
| `NormalizedParameters()` | `:2004` | **静默夹取** |
| `BuildKeepOrder()` | `:2114` | 长宽比/触边筛选 + 排序（定义顺序契约） |
| `MarkAsConfigInstance()` | `:780` | 配置态守卫 |
| `SchedulePreview()` | `:794` | 200ms 防抖 |

### D.3 数据模型

| 类 | 位置 | 关键字段 |
|---|---|---|
| `BlobResult` | `:1435` | `AnnotatedImage` / `Defects` / `DefectCount` / `MaxArea` / `TotalArea` / `IsOk` / 8 个 HTuple 数组 / `NgReason` |
| `BlobParams` | `:1457` | 归一化后参数快照（含 `Polarity` = light/dark） |
| `DetectPack` | `:1900` | 阶段一缓存包（`IDisposable`） |
| `DefectRow` | `:2213` | 缺陷清单一行（只读快照，全部 `get; init;`） |
| `GrayHistogram` | 独立文件 | `Bins`（归一化到 0~1）/ `BinMin/Max` / `ImageType` |

## 5.5 附录 E：回归断言清单

**文件**：`FlowCanvasChecks/BlobDetectChecks.cs`（注册于 `Program.cs:87`）
**真值策略**：不依赖外部样图，全部 HALCON **现场合成确定性图像**（200×200、背景灰度 200、暗斑灰度 50）。

| 分组 | 关键测试点 |
|---|---|
| **核心契约** | 运行不抛；报成功（检出缺陷是正常工况不是失败）；检出 5 个（4px 噪点被面积下限滤掉）；8 个数组端口逐项对齐；`MaxArea` = 逐项最大值；`TotalArea` = 逐项和；默认零容忍判 NG 但 `Success` 仍 true |
| **排序** | `AreaDescending` 输出单调不增；排序后第一个就是 `MaxArea` |
| **形状与触边** | `ExcludeBorderPx=1` 恰好少一个；剩下的都不压最外 1 像素；`MaxAspectRatio=5` 细长条被排除；`MinCircularity=0.5` 细长条被排除 |
| **合并与填孔** | 断裂两段：不合并是 2 个；`MergeRadius=2.5` 合并成 1 个；环形缺陷填孔后面积变大 |
| **判定与单位** | 个数少于下限 → NG 且原因写明「少于下限」；单缺陷都合格但总面积超标 → NG；mm² = 像素面积 × 当量²（当量 2 → ×4） |
| **开轮重置与位深** | 失败轮把上一轮脏数据清零；失败原因写明图像为空；uint2 底图按实际灰度范围拉伸；16 位图标注图生成成功 |
| **多通道与编号** | 3 通道输入与 1 通道一致；4 通道一致；4 通道标注图照样生成；缺陷旁写有 1 基编号（红像素 ≥3） |
| **视图样式契约**（静态扫描） | 插件视图已无硬编码颜色；不再本地定义 CardBorder/CardTitle 等；改用 `Plugin*` 公共样式；主题令牌全在 UI 库；引用的每个 `Plugin*` 键都有定义（实测 23 个） |

> 最近一次全量冒烟 `644 通过 / 15 失败`，**Blob 段零失败**（失败项全在相机插件与并行任务线）。

## 5.6 附录 F：术语表

**图像基础**

| 术语 | 含义 |
|---|---|
| Blob | 二值图里连成一片的区域（Binary Large Object） |
| 二值化 | 把图变成 0/255 的黑白图，把"可疑像素"切出来 |
| 连通域 | 相互连通的一片像素 = 一个候选缺陷 |
| 形态学 | 用结构元修形状：开=去毛刺，闭=补孔 |
| 圆度 | 1 = 正圆，越扁越小 |
| 长宽比 | 长边/短边，区分圆斑（≈1）与划痕（很大） |
| 直方图 | 横轴灰度、纵轴像素数的统计图，用来定阈值 |

**插件专属**

| 术语 | 含义 |
|---|---|
| 检测区域 `MaskRegion` | 只在该区域内检测（语义 = `reduce_domain`） |
| 排除区域 `ExcludeRegion` | 区域内候选一律丢弃（处理画面中间的固定干扰） |
| 筛选 vs 判定 | "算不算缺陷" vs "合不合格"，两套参数两套语义 |
| 触边排除 `ExcludeBorderPx` | 碰到最外 N 像素的缺陷丢弃（打光边缘效应） |
| 配置态 / 运行态 | 打开配置窗的实例 vs 正式运行的实例，预览只在前者跑 |
| 零容忍默认 | 规格默认 0，看到默认即 NG，提醒用户填规格 |
| 静默夹取 | 越界参数夹回合法区间，保证"配置坏了不炸流程" |

**HALCON 对象**

| 术语 | 含义 |
|---|---|
| HImage / HRegion / HXLD | 图像 / 区域 / XLD 轮廓（非托管资源，要 Dispose） |
| `reduce_domain` | 缩小计算域 |
| 引用计数语义 | HALCON .NET 的源对象与包装对象都要释放 |
| `#1304` / `#3101` / `#9302` | 参数非法 / 特征名不认识 / 窗口死锁 |

## 5.7 附录 G：文档合并说明

本文件由以下 4 份文档合并而成（内容已全部并入，原文件已归档在 `docs/缺陷检测/`）：

| 原文件 | 并入位置 |
|---|---|
| `2026-09-24-Blob缺陷检测插件.md`（~44 KB） | 第 1 章（六项设计决策）+ 第 2 章（端口/配置/算法）+ 第 4 章（已知边界、通用性修正、泄漏修复） |
| `2026-09-25-BlobDetect修复与功能补全.md`（~19 KB） | 第 1.3/1.4 节（筛选 vs 判定、像素当量）+ 第 4.2/4.3 节（P0/P1 修复详解） |
| `2026-09-25-Blob插件三项功能补全.md`（~7 KB） | 第 2.3 节（`DisplayViewIndex`）+ 第 4.3 节（编号 `disp_text` 坐标、4 通道支持） |
| `2026-09-25-插件配置界面样式收编UI库（Blob试点）.md`（~10 KB） | 第 5.5 节（视图样式契约断言组）+ 第 4.1 节（P3 WPF 三个坑） |

**未并入、但保留为参考的跨插件文档**（只是顺带提到 Blob）：
`docs/code-changes/2026-09-25-插件配置视图样式统一.md`、`2026-09-17-端口系统编译期类型检查与开发者体验优化.md`、`2026-09-15-连线语义显式化LinkKind与ForStep改名盲区修复.md`、`docs/新插件端口速查.md`。
