# 图像对齐 · 不变量清单

> 分类：图像处理 ｜ 来源：插件 Plugin.ImageAlign

开发页。每当有人改这个插件的代码，按这张清单逐条过一遍。
**每条都写了“违反后的症状”和“怎么发现”**。

## A. 算法正确性

### A1 方向必须一致：点对/上游矩阵是“参考→当前”，输出 `HomMat2D` 是“当前→基准”

**判据**：

- `VectorToProjHomMat2d(curRows, curCols, refRows, refCols, ...)` 得到**当前→基准**；
- `HomMat2dInvert` 得到**基准→当前**；
- `projective_trans_image(image, HomMat2D, ...)` 用“当前→基准”。

**违反后的症状**：对齐图整幅飞出画幅、`ValidRegion` 为空；或者图被搬到相反方向，
下游整片差异但看不出原因。

**怎么发现**：合成一个已知平移 `(dr,dc)`：当前图 = 基准图平移；插件输出 `HomMat2D` 应把
当前点映射回基准点，`MapResidual` ≈ 0。方向反了会得到 2 倍位移或飞出画幅。

### A2 点数决定变换：2 刚性 / 3 仿射 / 4 透视

**判据**：

- 2 点 → `vector_to_rigid`；
- 3 点 → `vector_to_hom_mat2d`；
- 4 点 → `vector_to_proj_hom_mat2d('normalized_dlt')`；
- `ManualFourPoint` 必须正好 4 对，少于 4 对直接失败。

**违反后的症状**：3 点被硬算透视（欠定，矩阵乱跳）；或手动四点少一点时静默退化仿射，
用户以为在做透视。

**怎么发现**：3 点输入走 `ManualFourPoint` 必须失败，文案含“必须给 4 对点”；
3 点走 `PointPairs` 必须成功且 `UsedPointCount=3`。

### A3 4 点透视无冗余：残差恒 0，必须靠验证点发现错点

**判据**：4 点拟合后 `Residual` 默认 NaN（无验证点）；有验证点时用第 5 对点算残差，
超过 `ResidualLimit` 直接失败。`Details` 必须提示“4 点透视无冗余”。

**违反后的症状**：第 4 点点错仍返回成功，下游整片差异；现场以为“对齐好了”。

**怎么发现**：4 点 + 验证点，把其中一个拟合点挪 20px → 必须失败且文案含“验证点残差”；
不填验证点时，4 点残差必须≈0（这是数学事实，不是 bug）。

### A4 有效区必须几何生成，不能靠像素值判黑边

**判据**：全画幅矩形 → `projective_trans_region(hom)` → 与目标域 `intersection`；
`ValidAreaRatio = area / (w*h)`；为 0 → 失败。

**违反后的症状**：用“像素==0”判黑边，把黑色工件吃掉；或干脆不生成有效区，
下游把黑边当缺陷。

**怎么发现**：平移 10px 后 `ValidAreaRatio` 必须 <1；恒等变换必须≈1；
有效区不能包含画幅外区域。

### A5 矩阵统一 9 元

**判据**：`ToHom9` 把 6 元补成 `[h0..h5,0,0,1]`；`HomMat2D`/`InverseHomMat2D` 输出长度恒 9。

**违反后的症状**：下游按 9 元读 6 元矩阵，越界/错位；或者把 6 元当 3×3 读，平移列错。

**怎么发现**：2/3/4 点三种模式，`plugin.HomMat2D.Value.Length == 9`。

### A6 正逆矩阵必须互逆

**判据**：`InverseHomMat2D = invert(HomMat2D)`；任取一点，正变换再逆变换往返误差 ≤0.1px。

**违反后的症状**：下游结果坐标回搬错位；机械手拿错坐标。

**怎么发现**：取画幅中心点，`MapPoint(HomMat2D)` 再 `MapPoint(InverseHomMat2D)`，
与原点的距离 ≤0.1px。

### A7 2/3 点自检残差≈0

**判据**：2/3 点精确拟合后，把拟合点映射回基准，最大残差 ≤0.5px；否则抛“点对自检失败”。

**违反后的症状**：坐标顺序/矩阵布局写错，但 4 点透视仍能“拟合出一个矩阵”，错误被掩盖。

**怎么发现**：2/3 点合成已知变换，`Residual` 必须≈0；若出现大残差，先查 A1 的方向。

### A8 >4 点 RANSAC 必须要求 ≥5 个内点

**判据**：5~12 点走确定性 RANSAC；最终 `InlierCount >= 5`，否则失败。
4 点子集总能精确拟合，所以“4 个内点”不能证明模型正确。

**违反后的症状**：3 个正确点 + 5 个错配也能“成功”，给出一个乱矩阵；下游整片差异。

**怎么发现**：8 点里 5 个错配且互不一致 → 必须失败且文案含“RANSAC 一致点不足”；
6 个正确 + 2 个错配 → `InlierCount=6`、`OutlierCount=2`。

### A9 RANSAC 子集映射失败（w≈0）不能拖垮整轮

**判据**：`TryResidualAt` 把 w≈0 的点当外点；只有最终重拟合时 w≈0 才失败。

**违反后的症状**：一个退化子集让整轮抛异常，明明有正确解也报错。

**怎么发现**：8 点含 2 个错配（错配点偏移较大）→ 必须成功且内点 6。

## B. 资源与句柄

### B1 `AlignedImage`/`ValidRegion` 交给端口后不许本轮 Dispose

**判据**：`AlignedImage.Value = warp.Aligned`、`ValidRegion.Value = warp.ValidRegion` 之后，
不再 Dispose；中间区域（`destDomain`/`fullRect`/`transformed`）在 service 的 finally 释放。

**违反后的症状**：端口挂野指针；下游一用 `#4051`/崩溃。

**怎么发现**：串一个下游 `ImageDiff` 读 `AlignedImage`，连跑两轮不崩；跑 500 轮看内存回落。

### B2 service 无 static 字段

**判据**：`ImageAlignService` 只有方法和局部变量；`AlignFit` 是实例类。

**违反后的症状**：`[ParallelSafe]` 前提破坏，并行分支偶发串图。

**怎么发现**：`grep -n "static " ImageAlignService.cs`，命中处必须是方法/类声明。

## C. 守卫与判定

### C1 点数/退化/尺寸/有效区守卫必须明确失败

**判据**：

- 点数不在 2~4 → 失败；
- `ManualFourPoint` ≠4 → 失败；
- 2 点重合 / 3 点共线 / 行列式≈0 → 失败；
- `ReferenceImage` 尺寸不同 → 失败；
- `ValidAreaRatio<=0` → 失败。

**违反后的症状**：静默给出一个乱矩阵/空图，现场“看着有数其实错”。

**怎么发现**：逐条构造坏输入，`Success=false` 且文案命中。

### C2 验证点残差超限必须硬失败

**判据**：`ResidualLimit>0 && Residual>ResidualLimit` → `Fail`，不是只写 `IsOk=false`。

**违反后的症状**：点错/遮挡被当成“质量不佳”放过去，下游整片差异。

**怎么发现**：4 点 + 验证点，挪一个拟合点 20px → `Success=false`。

### C3 `IsOk=false` 不许改 `Success`

**判据**：`MinScore`/`MinValidAreaRatio` 越界只写 `IsOk`/`Reason`，不调 `Fail`。

**违反后的症状**：质量告警中断流程；现场分不清“对齐差”和“插件坏”。

**怎么发现**：设 `MinScore=1.0` 但残差 0（Score=1）→ IsOk=true；
设 `MinValidAreaRatio=1.0` 且平移 10px → IsOk=false 但 Success=true。

### C4 默认 metrics-only

**判据**：`EnableThreshold` 默认 false；此时 `IsOk=true`、`Reason` 含 metrics-only。

**违反后的症状**：新装插件默认拦截，良品率“暴跌”。

**怎么发现**：新建步骤直接跑，`IsOk` 必须 true。

## D. 契约与工程

### D1 输入端口取值一律 `ActualValue`

**判据**：`Image.ActualValue` / `ReferenceImage.ActualValue` / `RefPoints.ActualValue` 等。

**违反后的症状**：链接上游时读到默认值；出现“接了图却报未连接”。

**怎么发现**：`grep -n "\.Value" ImageAlignPlugin.cs`，输入端口处出现 `Value` 就是违规。

### D2 不写全局变量

**判据**：无 `GlobalVariables.TryWrite`。

**违反后的症状**：绕过类型守门/权限；换方案残留。

**怎么发现**：`grep -rn "GlobalVariables" Plugins/Plugin.ImageAlign/` 零命中。

### D3 不碰窗口 API

**判据**：无 `OpenWindow`/`DispText`/`SetColor(HObject,...)`。

**违反后的症状**：违反 R36；断言宿主无窗口可开。

**怎么发现**：`grep -rn "OpenWindow\|DispText" Plugins/Plugin.ImageAlign/` 零命中。

### D4 工程必须在 `Plugins\` 下并登记 sln / Tiers

**判据**：工程在 `Plugins\Plugin.ImageAlign\`；`VisionMaster.sln` 有 Project 条目；
`HelpManualCheck.Tiers` 有 `["ImageAlignPlugin"]`。

**违反后的症状**：不投递 `Modules\` / 改代码后仍是旧 DLL / HM16 红。

**怎么发现**：`dotnet sln list` 含该工程；`grep ImageAlignPlugin HelpManualCheck.cs` 命中；
构建后 `Modules\Plugin.ImageAlign.dll` mtime ≥ 源码。

### D5 改过 `.md` 必须重建并核资源数

**判据**：`Help\` 下 `.md` 数 = DLL 内 `.Help.` 内嵌资源数；DLL mtime ≥ `.md` mtime。

**违反后的症状**：帮助弹窗旧文案/新页没打进去。

**怎么发现**：`ImageAlignChecks` 的 D7 断言直接数 `GetManifestResourceNames()`。

## E. 画布视图

### E1 点对表格是唯一真源：OnConfirm 序列化、Initialize 反序列化

**判据**：`OnConfirm` 先按 `PointRows` 写 `RefPointsText`/`CurPointsText`，再 `base.OnConfirm`；
`Initialize` 先 `base.Initialize`，再把两份文本解析成 `PointRows`。

**违反后的症状**：画布取的点确认后丢失；或打开旧方案表格空白，一确认就把旧点清掉。

**怎么发现**：`--align` 的 V1/V2：1 行 `(20,30)/(40,50)` → 序列化 `"20,30"`/`"40,50"`，
再 `Initialize` 回来仍是同一行。

### E2 取点一次性：写完立即退出 `IsPicking`

**判据**：`ApplyPickedPoint` 在 `!IsPicking` 时直接 return；写完后 `IsPicking=false`。

**违反后的症状**：画布长期吃鼠标事件，用户点别处也被当成取点；或没按按钮点图也改坐标。

**怎么发现**：V3/V4：`StartPick(true)` 后 `ApplyPickedPoint` 写 Ref 且 `IsPicking=false`；
再调一次 `ApplyPickedPoint` 不应再改值。

### E3 没有选中行时取点 → 自动新增一行

**判据**：`SelectedPointRow == null` 时 `ApplyPickedPoint` 新建 `AlignPointRow`、加入 `PointRows` 并选中。

**违反后的症状**：取点没反应；或覆盖第一行。

**怎么发现**：清空后 `StartPick` + `ApplyPickedPoint`，`PointRows.Count` 必须 +1。

### E4 画布用只读控件，且只有 `IsPicking` 时才取点

**判据**：XAML 用 `h:ImageReadOnly`（无“打开图片”菜单）；`IsPickMode="{Binding IsPicking, Mode=TwoWay}"`。

**违反后的症状**：配置时误换预览图；或画布右键能打开图片，和上游图不一致。

**怎么发现**：`UIThemeSmokeTest` 解析 XAML；人工检查右键菜单只有视图/图像（无打开图片）。

### E5 视图不持有/不释放图像

**判据**：`OnViewLoaded` 只读 `Image.ActualValue`/`ReferenceImage.ActualValue` 的引用；
不 `Dispose`、不复制；`DisplayImage` 只是引用切换。

**违反后的症状**：关闭配置窗后上游图变野指针；或重复释放。

**怎么发现**：打开/关闭配置窗后继续跑流程，下游不崩；内存不持续上涨。

### E6 参数弹窗只镜像标量/枚举，不重复点对

**判据**：`ImageAlignConfigModel` 只含 10 个标量/枚举属性；点对由 `PointRows` 管理；
`ValidationPointText` 在视图文本框里。

**违反后的症状**：参数弹窗里出现第二份点对文本，两份真源互相覆盖。

**怎么发现**：弹窗里没有 `RefPointsText`/`CurPointsText`；确认后点对不被清空。

## F. 断言映射（`FlowCanvasChecks/ImageAlignChecks.cs`）

| 断言组 | 钉住的不变量 | 反向判据 |
| --- | --- | --- |
| 平移/旋转/缩放/非等比/透视 | A1、A2、A5 | 方向写反 → 对齐图差/有效区空 |
| 4 角点重投影 ≤0.3px | A1、A7 | 矩阵布局错 → 残差大 |
| 对齐图平均绝对差 ≤2 灰度 | A1、A5 | 插值/方向错 → 差大 |
| 逆变换往返 ≤0.1px | A6 | 逆矩阵没求 → 往返错 |
| 有效区 | A4 | 不生成/全画幅 → 平移后比例=1 |
| 4 点 + 验证点挪 20px | A3、C2 | 去掉验证点 → 仍成功（错） |
| 8 点含 2 错配 | A8、A9 | 去掉 RANSAC/要求 4 内点 → 错配被当内点 |
| 3 正确 + 5 错配 | A8 | 不要求 5 内点 → 仍成功（错） |
| 画布序列化/反序列化 | E1 | 去掉 OnConfirm 序列化 → 点丢失 |
| 取点回填/一次性 | E2、E3 | 不置 IsPicking=false → 第二次点击仍改值 |
| XAML 解析 | E4、E7 | 资源未合并/写死颜色 → UIThemeSmokeTest 红 |
| 手动四点 3 对 | A2、C1 | 退化仿射 → 仍成功（错） |
| 下游 ImageDiff 面积下降 ≥80% | A1、A4 | 不对齐/不接 ValidRegion → 面积不降 |
| 端口/配置/手册 | D1、D4、D5 | 改类型/漏登记 → 断言红 |

> 通用纪律：**每一条都要能反向证伪**。改坏代码仍然绿的断言不是断言，只是装饰。
