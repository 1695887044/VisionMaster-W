# 图像对齐 · 内部结构

> 分类：图像处理 ｜ 来源：插件 Plugin.ImageAlign

开发页。讲这个插件由哪几个文件组成、一轮执行走哪些步骤、每个决策为什么这么定。
改代码前先读完这一页；改完照 12 页的不变量清单自检。

## 文件构成

```
Plugins/Plugin.ImageAlign/
├── Plugin.ImageAlign.csproj   net9.0-windows / UseWPF / WPF 插件
├── AlignEnums.cs              2 个枚举（模式 / 插值）
├── AlignPointRow.cs           画布/表格的一行点对（INPC）
├── ImageAlignConfigModel.cs   「参数…」弹窗模型（[SuperDisplay]）
├── ImageAlignService.cs       纯 HALCON 算法层（internal static，无状态）
├── ImageAlignPlugin.cs        步骤类：端口 + [StepConfig] + RunAlgorithm + 视图逻辑
├── ImageAlignView.xaml        配置视图：画布取点 + 点对表格（+ .xaml.cs）
└── Help/                      本手册（8 页，内嵌资源）
```

**有 `ImageAlignView.xaml`**（画布点选 + 点对表格）。插件实现 `IPluginCustomViewProvider`，
`GetConfigView` 先 `Initialize(stepData)` 再返回视图；视图是普通 `UserControl`，
`Initialize/OnConfirm` 仍由插件（基类契约）负责。
标量/枚举参数不重复手写控件：视图的「参数…」按钮用 `EasyDialog.ShowPropertyGridSync` +
`ImageAlignConfigModel`（`[SuperDisplay]`），`FlatPropertyGrid` 直接渲染。

## 三层职责

| 层 | 文件 | 能做什么 | 不能做什么 |
| --- | --- | --- | --- |
| 策略层 | `ImageAlignPlugin.RunAlgorithm` | 判空、取点/位姿、模式分派、填端口、可选阈值判 IsOk | 不直接调 HALCON 算子 |
| 算法层 | `ImageAlignService` | 点对拟合、位姿构造、warp、逆矩阵、有效区、残差 | 不碰端口、不记日志、不读 `[StepConfig]` |
| 数据层 | `AlignEnums` / `AlignFit` | 模式、插值、一轮拟合结果 | 无逻辑 |

## 一轮执行：`RunAlgorithm` 的调用链

```
① 取输入（ActualValue）
   Image / ReferenceImage / RefPoints / CurPoints / HomMat2D / Pose*
      ↓ Image 空 → Fail，return
② 模式分派
   TemplatePose:
      有上游 HomMat2D → 参考→当前，求逆得当前→基准
      无上游 HomMat2D → vector_angle_to_rigid(模板参考点,0 → 位姿点,角)，求逆
   点对:
      端口非空优先，否则解析 RefPointsText/CurPointsText
      校验数量一致、2~4 对、ManualFourPoint 必须 4 对
      解析 ValidationPointText（可选）
③ 拟合
   2 点 → vector_to_rigid
   3 点 → vector_to_hom_mat2d
   4 点 → vector_to_proj_hom_mat2d('normalized_dlt')
   5~12 点 → 确定性 RANSAC（枚举 4 点子集，内点最多/RMS 最小，再用内点重拟合；要求 ≥5 内点）
   6 元补成 9 元；行列式守卫；2/3 点自检残差≈0
   有验证点 → 映射验证点，残差 > ResidualLimit 直接失败
④ warp
   projective_trans_image(image, hom当前→基准, 插值, adapt=false, transformDomain=false)
   projective_trans_region(全画幅, hom当前→基准) ∩ 目标域 → ValidRegion
   ValidAreaRatio = 有效区面积 / 画幅面积；为 0 → Fail
⑤ 填端口
   AlignedImage / ValidRegion / HomMat2D / InverseHomMat2D
   Score / Residual / ValidAreaRatio / UsedPointCount
⑥ 可选阈值
   MinScore / ResidualLimit / MinValidAreaRatio → IsOk
   **不调 Fail**（质量不达标 ≠ 执行失败）
⑦ 明细
   模式/点数/残差/分数/有效区/正逆矩阵/提示
```

## 坐标约定（最容易错，单独强调）

| 数据 | 方向 | 谁用 |
| --- | --- | --- |
| 点对 | 参考点在基准帧、当前点在当前图 | 拟合输入 |
| 上游 `HomMat2D` | 参考 → 当前 | `TemplatePose` 输入 |
| 输出 `HomMat2D` | 当前 → 基准 | `projective_trans_image` 的 warp |
| 输出 `InverseHomMat2D` | 基准 → 当前 | 下游结果坐标回搬 |

`VectorToProjHomMat2d(curRows, curCols, refRows, refCols, ...)` 得到的是**当前→基准**；
`HomMat2dInvert` 得到**基准→当前**。这两行不要写反。

## 每个决策为什么这么定

### 为什么 v1 不自己跑模板匹配

`Plugin.Matching` 已经有模板库、搜索域、各向异性、配方；再写一遍是重复建设。
本插件只消费位姿/矩阵，把“找基准”和“搬图”分开：匹配负责找，本插件负责搬。
这也是评审 D2 的口径。

### 为什么 4 点必须配验证点

4 对点定 8 个自由度，方程数=8，**总是精确解**；残差恒 0。
单点错/遮挡无法从残差发现。验证点（第 5 对）是 4 点模式唯一能发现错点的机制。
没有验证点时，`Details` 会明确提示“4 点透视无冗余”。

### 为什么 >4 点用 RANSAC

5 点以上有冗余，可以自动剔除外点。但 4 点子集总能精确拟合，所以“4 个内点”没有诊断力；
实现要求**至少 5 个内点**，否则失败并提示改用 4 点 + 验证点。
RANSAC 是确定性的（枚举 4 点子集，最多 C(12,4)=495），不引入随机性，断言可复现。

### 为什么画布用 `ImageReadOnly` + `IsPickMode`

`HalconBase` 已经提供取点通道：`IsPickMode` 打开后左键单击触发 `ImagePicked(Row, Col)`，
坐标就是图像坐标（缩放/平移已由控件换算）。选 `ImageReadOnly` 而不是 `ImageEdit`：
只读画布没有“打开图片”和 ROI 菜单，配置态不会误换预览/误建区域。
取点是**一次性**的：`ApplyPickedPoint` 写完立即把 `IsPicking` 置 false，避免画布长期吃鼠标事件。

### 为什么参数弹窗用 `SuperDisplay` 模型

`FlatPropertyGrid` 只渲染带 `[SuperDisplay(Visible=true)]` 的属性，不认 `[StepConfig]`。
如果把插件本身塞进去，会连端口/输出一起显示；所以用 `ImageAlignConfigModel` 只镜像 10 个标量/枚举参数，
弹窗确认后 `ApplyTo(plugin)` 写回。点对/验证点不放进模型：点对由表格管理，验证点是视图里的文本框。

### 为什么视图不持有图像

`OnViewLoaded` 只读 `Image.ActualValue` / `ReferenceImage.ActualValue` 的引用用于显示，
不 Dispose、不复制；图像所有权仍归上游/端口。视图关闭后不留下任何 HALCON 句柄。

### 为什么手动四点少于 4 点直接失败

3 点只能定仿射（6 自由度），不能定透视（8 自由度）。
如果静默退化成仿射，用户以为在做透视，实际丢掉了倾斜/非等比——这是“看着有数其实错”。
所以 `ManualFourPoint` 强制 4 对；要 3 点请显式选 `PointPairs`。

### 为什么有效区用几何生成

对齐后画幅外填 0，但**黑色工件本身也可能是 0**；靠像素值判黑边会把工件吃掉。
所以用全画幅矩形经同一个 hom 做 `projective_trans_region`，再与目标域求交。
下游必须用它限制检测区（见 04 页的 6 个下游）。

### 为什么统一 9 元矩阵

HALCON 的仿射返回 6 元、透视返回 9 元；下游如果还要分两种长度，容易把 6 元当 9 元读。
本插件统一补成 9 元（第三行 0,0,1），`HomMat2D`/`InverseHomMat2D` 永远是 9 个数。

### 为什么 v1 没有画布点选

画布点选要自定义视图 + `h:ImageEdit` + 坐标换算，成本高、坑多（R19-R22/R30-R32/R141/R173）。
v1 先用文本/端口把核心变换跑通；画布点选 v1.1 再补，不影响算法与下游契约。

## 资源释放纪律

`ImageAlignService`：

| 对象 | 释放 | 说明 |
| --- | --- | --- |
| `destDomain` / `fullRect` / `transformed` | 是 | warp 中间区域 |
| `aligned`（HImage） | **否** | 交给 `AlignedImage` 端口，由基类轮首回收 |
| `valid`（HRegion） | **否** | 交给 `ValidRegion` 端口 |
| 输入 `image` / `reference` | **否** | 上游对象 |

`AlignFit` 里的 `HTuple` 是托管对象，不需要 Dispose。

## 构建与投递

- 工程必须在 `Plugins\` 下（红线①），构建后由 `Directory.Build.targets` 投递 `Modules\`；
- 必须登记 `VisionMaster.sln`（R62），否则改代码后 `Modules\` 仍是旧 DLL 且无报错；
- 必须登记 `HelpManualCheck.Tiers`（HM16）；
- 改过 `Help\*.md` 必须重建并核内嵌资源数 = 磁盘 `.md` 数；
- 构建统一加 `-m:1 -nodeReuse:false`（本机多进程构建会静默失败）。

## 性能

5MP（2448×2048）4 点透视、含 warp + 有效区 + 端口赋值：**Release 实测中位数 3.4ms**
（min 3.2 / max 5.0；Debug 3.9~4.9ms；2026-10-27 本机）。复跑命令 `FlowCanvasChecks.exe --align-perf`。
耗时主要在 `projective_trans_image`；点对拟合本身是微秒级。
