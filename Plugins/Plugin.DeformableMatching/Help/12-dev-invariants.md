# 可形变模板匹配 · 开发不变量

本页是可形变匹配插件的“不可回退”契约。每条都对应一次实测或一次 HALCON 文档口径；
改代码前先读，改完后逐条自检。

## I1 原点参数是相对域重心的偏移

`set_deformable_model_origin(ModelID, Row, Column)` 的 `Row/Column` 不是图像绝对坐标，
而是相对模板域重心的偏移。`(0,0)` 表示重心就是原点。

- 对称 ROI：重心 = ROI 中心 → 传 `(0,0)`；
- 非对称掩膜：`origin = roiCenter - cog`；
- `cog` 用 `GetDomain` + `AreaCenter` 求；
- 写成绝对坐标会让 Row/Column 与 HomMat2D 投影整体偏掉。

探针实测：ROI 中心 `(199.5,199.5)`，`origin(0,0)` → Row/Col `(199.5,199.5)`；
`origin(50,30)` → `(249.499,229.5)`。

## I2 创建期与查找期参数必须分开

创建期（改了就重学）：

- `Algorithm`
- `Shape`
- `TemplateRect`
- `TemplateMask`
- `NumLevels`
- `UsePolarity`
- `ScaleRMin`
- `ScaleCMin`

查找期（改了立即生效）：

- `AngleStartDeg`
- `AngleExtentDeg`
- `ScaleRMax`
- `ScaleCMax`
- `Smoothness`
- `MinScore`
- `MaxMatches`
- `MaxOverlap`
- `Greediness`

HALCON 23.05 文档明确：`create_local_deformable_model` 与
`create_planar_uncalib_deformable_model` 的 `AngleStart/AngleExtent/ScaleRMax/ScaleCMax`
**not used**。创建时一律传 `[]`。

## I3 NumLevels 两处语义不同

- 创建期：`0` 或 `1..10`，`0` 映射为 `'auto'`；
- 查找期：`0..10`，`0` 合法（默认）；
- 不要把创建期的 `'auto'` 传给查找期；
- 不要用同一个变量两处传。

## I4 模型轮廓 level 是 1 基

`get_deformable_model_contours(ModelID, Level)`：

- `Level=1` 是最细层，合法；
- `Level=0` 非法，HALCON #1302；
- 合法范围 `1..num_levels`；
- 探针实测 3 层模型：1/2/3 OK，0/4 #1302；
- 平面模式固定用 1。

## I5 ImageRectified 是每命中一张

`find_local_deformable_model` 的 `ImageRectified` 类型是 `(multichannel-)image(-array)`：

- `NumMatches=2` → `CountObj=2`；
- 端口必须是 `OutputPort<HObject>`；
- 不能用 `OutputPort<HImage>` 装多张；
- 平面模式该端口为空。

## I6 DeformedContours 是扁平 XLD 数组

- 局部模式 HALCON 直接返回；
- 平面模式由模型轮廓投影 + `ConcatObj` 生成；
- 每个命中由多段组成（探针实测 486 段/命中）；
- `CountObj` 不等于 `MatchCount`；
- 下游不要按命中数分组；
- 端口描述与帮助必须写清。

## I7 命中顺序不保证分数降序

探针实测 `NumMatches=2` 时 `rows=[349.5,149.5]`，第二个实例排在前面。
标量端口必须自己取最高分：

- 遍历 `scores` 找最大值与索引；
- 不要假设 `index 0` 是最高分；
- 数组端口保持 HALCON 返回顺序。

## I8 HomMat2D 以当前参考点为 (0,0)

- `find_planar_uncalib_deformable_model` 返回 9×N；
- 映射“当前参考点为 (0,0)”的模型坐标到搜索图；
- 设原点为 ROI 中心后，`ProjectiveTransPixel(hom,0,0)` 就是 ROI 中心；
- 不要投影绝对 ROI 中心；
- 不设原点时参考点是重心，要投影 `(roiCenter - cog)`。

探针实测：`origin(0,0)` → `project(0,0)=(249.5,229.5)`；
`origin(50,30)` → `(299.5,259.5)`。

## I9 齐次坐标要防 qw≈0

- 优先用 `ProjectiveTransPixel`（内部做齐次除法）；
- 手写 `ProjectiveTransPoint2d` 时必须判 `qw` 接近 0；
- `qw≈0` 表示点在相机后方，明确失败，不要输出 Infinity/NaN；
- `HomMat2D` 长度不足 9 时也要明确失败。

## I10 模型必须缓存

- 键：`RuntimeModelSource == Entry.Model`；
- 命中直接复用 `RuntimeModelId`；
- 未命中才写临时文件 + `read_deformable_model`；
- 绝不每帧重读模型；
- 平面模式同时缓存 `RuntimeModelContours`；
- `ModelLoadCount` 用于断言缓存命中。

## I11 模型类型必须独立校验

- 学习时写 `LearnedAlgorithm = Algorithm`；
- 运行期先比 `LearnedAlgorithm` 与当前 `Algorithm`；
- 不一致 → `Fail("模板是用「X」学的，当前算法是「Y」：请重新学习")`；
- 只存一个 `Algorithm` 字段校验不了——用户切换算法时那个字段已经变成新值。

## I12 过期指纹只含创建期参数

`LearnedSignature`：

```text
Algorithm|Shape|Rect|Mask.Length|NumLevels|UsePolarity|ScaleRMin|ScaleCMin
```

查找期参数不进指纹：

- `Smoothness`
- `MinScore`
- `MaxMatches`
- `MaxOverlap`
- `Greediness`
- `AngleStartDeg/AngleExtentDeg`
- `ScaleRMax/ScaleCMax`

改查找期参数不应提示重新学习。

## I13 运行实例只走 ApplyConfigValues

流程编译器构造运行实例时只调 `ApplyConfigValues`，不调 `Initialize`：

- `TemplateJson` 解析必须放 `ApplyConfigValues`；
- 放 `Initialize` 会得到空库（MatchingPlugin.cs:1409 实测）；
- `Initialize` 只用于配置态；
- `OnConfirm`：内嵌模式序列化完整 `Entry`；外部来源模式只写元数据（`Model` 置空），
  模型真源在资产/文件里。

## I14 模板载荷随方案落盘

- `TemplateJson` 是**内嵌模式**的唯一真源；
- 外部来源模式下真源是方案模板资产/本地模板文件，`TemplateJson` 只存元数据；
- `Entry.Model`/`TemplateMask` 都是 base64；
- `RuntimeModelId`/`RuntimeModelSource`/`RuntimeModelContours` 是 `[JsonIgnore]`；
- 保存→重开→运行结果一致；
- `.vms` 体积要记录；形变模型比 shape model 大。

## I14.1 外部来源优先级与失败

- 优先级固定：`TemplateAssetId` → `TemplateFilePath` → `TemplateJson`；
- 外部来源必须能解析、类型正确、文件存在、JSON 可读、`Model` 非空、算法一致；
- 任何一项失败都直接 `Fail`，**不回退到内嵌模板**；
- 资产缓存键含 `ContentHash + Version`；本地文件缓存键含路径/大小/mtime；
- 资产升版、回滚或本地文件覆盖后必须重新加载模型；
- 外部来源模式禁用学习；要改模板先取消外部来源。

## I15 两个输入端口必须 IsRequired=false

`InputPort.IsRequired` 默认 `true`：

- `Image` 允许配置态没有图；
- `SearchRegion` 是可选端口；
- 不写 `IsRequired=false` 会被流程编译器判成必填；
- “上游无图 → Fail”的失败路径也会永远不执行。

## I16 输入图像所有权归上游

- `Image.ActualValue` 是上游的图；
- 插件不 Dispose 输入图；
- 需要修改时 `CopyImage`/`ReduceDomain` 产生新对象；
- 输出 `MeasureImage` 用副本，不把输入图直接塞进输出端口。

## I17 输出端口所有权归端口

- `HImage`/`HObject`/`HXLD` 输出由基类 `AutoDisposeRoundOutputs` 轮首回收；
- 插件不手动 Dispose 已赋给端口的对象；
- 中间对象（`reduced`/`gray`/`rgb`/`contours`）必须自己 Dispose；
- 透传上游值由基类 `IsUpstreamLinkedValue` 保护。

## I18 临时文件必须 finally 删除

- `write_deformable_model`/`read_deformable_model` 用 `Path.GetTempFileName` 或 GUID 文件名；
- `finally` 删除；
- 删除失败不打断业务；
- 不要在每帧运行期写临时文件（缓存后只读一次）。

## I19 失败语义必须与平台一致

| 情况 | 行为 |
| --- | --- |
| 无图 | `Fail("输入图像为空或未初始化")` |
| 无模型 | `Fail("模板还没学习：...")` |
| 算法不符 | `Fail("模板是用「X」学的...")` |
| 加载失败 | `Fail("模板加载失败（载荷损坏？请重新学习）")` |
| 资产类型不符 | `Fail("资产「X」的类型是 model，这里需要 template")` |
| 资产文件缺失 | `Fail("模板资产文件不存在：...")` |
| 资产已归档 | `Fail("资产「X」已归档，不能用于运行；...")` |
| 资产 JSON 损坏 | `Fail("读取模板资产失败：...")` |
| 本地文件缺失 | `Fail("本地模板文件不存在：...")` |
| 外部模板无模型 | `Fail("模板里没有模型载荷（Model 为空）...")` |
| 外部模板算法不符 | `Fail("模板是用「X」学的，当前算法是「Y」...")` |
| 无命中 | `Fail("未找到目标：...")`，`MatchCount=0` |
| 算子异常 | `Fail($"可形变匹配失败：{ex.Message}")` |

失败原因写 `ErrorMessage`，并 `context.Logger` 记一条。

## I20 无命中仍输出标注图

- `MatchCount=0`；
- `Score/Row/Column=0`；
- `Scores/Rows/Columns` 空；
- `HomMat2D` 空；
- `DeformedContours` 空；
- `RectifiedImages` 空；
- `MeasureImage` 输出原图（原因看 `ErrorMessage`）。

## I21 搜索域口径

- `SearchRegion` 已连且面积 >0 → `ReduceDomain`；
- 为空/未连 → 全图；
- 不要静默缩到零面积（会变成“永远找不到”）；
- v1 是“连了就用”，帮助里写清。

## I22 掩膜是域差集，不是图像遮罩

- `TemplateMask` 解成区域；
- `Difference(roi, maskRegion, effectiveRegion)`；
- `ReduceDomain(image, effectiveRegion)`；
- 非对称掩膜会改变域重心；
- 掩膜 PNG 是 255=挖掉，`Threshold(1,255)` 还原。

## I23 ROI 形状映射

| 形状 | 算子 | 字段 |
| --- | --- | --- |
| 矩形 | `gen_rectangle2` | 中心行/列、角度、半长、半宽 |
| 圆 | `gen_circle` | 中心行/列、半径 |
| 椭圆 | `gen_ellipse` | 中心行/列、角度、长半轴、短半轴 |

HALCON `rectangle2` 的 `Length1` 沿 column 轴（phi=0），不是 row；填反会旋转 90°。

## I24 角度单位只换算一次

- 界面/端口：度；
- HALCON：弧度；
- 学习时 `phi` 存弧度；
- 查找时 `AngleStart/AngleExtent` 转弧度；
- 不要重复转换（度→弧度只做一次）。

## I25 缩放默认 1/1

- `ScaleRMin/ScaleCMin` 创建期；
- `ScaleRMax/ScaleCMax` 查找期；
- 默认 1/1（不搜缩放）最快；
- 缩放范围退化时明确失败或撑开最小窗口；
- 不要静默当“已启用”。

## I26 平滑度只影响局部查找

- 最小 3；
- 典型 11；
- 越大越刚；
- 只在 `find_local_deformable_model` 的 `deformation_smoothness` 传；
- 平面模式不传、界面隐藏。

## I27 ParallelSafe 先不加

加 `[ParallelSafe]` 之前必须确认：

1. 模型缓存是实例级，不是 static；
2. 运行期不调 `SetDeformableModelOrigin`；
3. 插件实例不被并行分支共享；
4. `RuntimeModelId`/`RuntimeModelContours` 访问有同步；
5. `set_deformable_model_origin` 文档要求跨线程访问 ModelID 同步。

有疑问先不加。

## I28 发布预览的窗口号直传

```csharp
this.PublishPreview(anno, DisplayViewIndex);
```

- `viewIndex` 就是 1~9；
- 不要 `+1`；
- 无订阅者时 no-op；
- 图像所有权在发布方，下一轮释放。

## I29 帮助是交付物

- `Help\` 下 8 页；
- 首行 `# 一级标题`；
- 每页 ≥70 行；
- L 档：用户页 ≥5、开发页 ≥3、最长开发页 ≥300 行；
- 禁用占位符与空章节；
- 文件名除扩展名外不许再有点；
- `HelpManualCheck` 登记 L 档；
- 改帮助必须重建插件。

## I30 sln 必须登记

- 新插件工程必须进 `VisionMaster.sln`；
- 不进 sln，`dotnet build` 不编它；
- `Modules\` 永久停旧版且无报错；
- 同时 `FlowCanvasChecks.csproj` 加 ProjectReference；
- `FlowCanvasChecks/Program.cs` 调 `DeformableMatchingChecks.Run()`。

## I31 对拍容差

| 项 | 容差 |
| --- | --- |
| `MatchCount` | 相等 |
| `Score` | ≤1e-6 |
| `Row/Column` | ≤1e-3 px |
| `DeformedContours` | 点数/坐标 ≤1e-6 |
| `HomMat2D` | 逐值 ≤1e-9 |
| 派生 Row/Column | ≤1e-3 px |

平面 11 张刹车盘图 11/11 命中数一致。

## I32 反向断言

- 模板 0°、搜索图旋转 30°；
- 查找角度范围 0/0 → 两边 0 命中；
- 查找角度范围 -180/180 → 两边 ≥1 命中；
- 局部模式创建期角度参数不生效，反向只能改 find 参数；
- 没有反向断言的对拍可能是“两边都没跑”。

## I33 断言必须做变异反证

- 新断言先改坏 → 红；
- 还原 → 绿；
- “全部通过”不等于覆盖；
- 改断言源码后必须重建再跑，不许 `--no-build`；
- 判据看条数 + 明细原文。

## I34 视图契约

- `DataContext = 插件实例`；
- 数值用 `NumericBox`；
- 预览 `ImageReadOnly` 常驻，不折叠；
- 结果表 `ListView+GridView` + `GridListViewStyle` + `GridViewRowPresenter`；
- 颜色只引 `Dialog*` / `PluginPreview*`；
- 视图零十六进制；
- 可复用控件下沉 `Dialog\` 分册。

## I35 设计基线

- UI 改动先看 `docs/UI设计基线/可形变模板匹配/README.md`；
- 视觉基准是 `design.html`，不是现有桌面视图；
- 五态齐全；
- 差异写进“已知偏差”；
- 验收附改前改后截图 + 差异清单。

## I36 标注只走图像算子

- 不在插件里 `OpenWindow`（R36/R37）；
- 优先 `PaintXld`/`GenRegionContourXld`；
- 需要离屏烘焙时走 `AnnotationBaker`；
- 标注失败不影响端口数据。

## I37 日志口径

- 成功：`context.Logger?.Info(...)` 或 `Success`；
- 失败：`Fail(...)` + `context.Logger?.Error(...)`；
- 取消：不写失败；
- 日志文案带步骤名/实例名，便于现场定位。

## I38 取消不是失败

- 基类 `Execute` 对 `OperationCanceledException` + 令牌已取消原样上抛；
- 插件不要把它包成 `Fail`；
- 运行期长循环要检查 `context.CancellationToken`；
- 取消后不写脏状态。

## I39 大模型体积

- 形变模型比 shape model 大；
- 内嵌模式会让 `.vms` 明显变大；
- 单模板单份；
- 学习后记录载荷字节数；
- 已实现 `IAssetIdConfig` + 本地模板文件：外部来源模式下 `TemplateJson` 只存元数据，
  模型放方案 `Assets\Templates\` 或本地 JSON；
- 外部来源模式仍保留「清除内嵌」按钮，用于清掉历史内嵌 base64。

## I40 已知边界

- v1 单模板，不做配方库；
- v1 不做 `PlanarCalibDeformableModel`；
- v1 不做 `VectorField` 输出；
- v1 画布只读预览，ROI 用数值；
- 局部模式不返回角度/缩放；
- 平面模式不直接返回 Row/Column；
- `DeformedContours` 不按命中分组；
- 掩膜编辑器在后续批次提供，字段与学习逻辑已就绪。

## I41 交付前自检清单

- [ ] 插件工程构建 0 错误 / 退出码 0；
- [ ] `Modules\Plugin.DeformableMatching.dll` mtime ≥ 源码 mtime；
- [ ] `Help` 内嵌资源数 = 磁盘 `.md` 数；
- [ ] `FlowCanvasChecks` 新增断言全绿，失败 0；
- [ ] 局部对拍：MatchCount/Score/Row/Column/Contours 在容差内；
- [ ] 平面对拍：11/11 命中数一致，HomMat2D 逐值 ≤1e-9；
- [ ] 反向断言：0/0 → 0 命中，±180 → ≥1 命中；
- [ ] 序列化往返：保存→重开→结果一致；
- [ ] 模型缓存：连续两帧只读一次；
- [ ] 资产/本地来源：优先级、类型/缺失/归档/坏 JSON/算法不符、升版/文件更新重载；
- [ ] 外部来源 OnConfirm 只写元数据（`Model` 为空），资产 Id 保留；
- [ ] `HelpProbe` 退出 0；
- [ ] `UIThemeSmokeTest` 无新增红；
- [ ] `audit_artifacts.ps1` OK；
- [ ] `audit_docs.ps1` 全 0；
- [ ] `docs/code-changes/` 五段式记录；
- [ ] 台账更新条数已写。

## I42 复现命令

```text
dotnet build Plugins\Plugin.DeformableMatching\Plugin.DeformableMatching.csproj -m:1 -nodeReuse:false
dotnet build FlowCanvasChecks\FlowCanvasChecks.csproj -m:1 -nodeReuse:false
FlowCanvasChecks.exe
tools\run_gates.ps1
```

判据：

- 构建 `0 个错误` 且退出码 0；
- 断言条数非 0、失败 0；
- gate 退出码 0。

## I43 参考文档

- `docs/架构文档/Plugin.DeformableMatching-设计.md`（M0 探针结论与设计定稿）；
- `docs/UI设计基线/可形变模板匹配/README.md`（U0 基线）；
- `docs/code-changes/2026-10-26-计划-可形变匹配插件A方案.md`（任务书）；
- HALCON 23.05 算子文档：`create_local_deformable_model`、
  `find_local_deformable_model`、`create_planar_uncalib_deformable_model`、
  `find_planar_uncalib_deformable_model`、`set_deformable_model_origin`、
  `get_deformable_model_contours`。

## I44 变更纪律

- 改创建期参数/原点/模型格式 → 必须重跑局部与平面两组对拍；
- 改端口 → 必须更新帮助、断言、`.vms` 流程；
- 改视图 → 必须更新设计基线/差异清单；
- 改帮助 → 必须重建插件；
- 改断言 → 必须做变异反证；
- 任何“看起来好了”的结论都要有命令与日志。
