# 码读取 · 开发：内部结构与数据流

> 分类：图像处理 ｜ 来源：插件 Plugin.CodeReader ｜ 读者：开发

本页讲**插件怎么组织、两套引擎各自的生命周期、一次读码的数据流**。
用户口径（码制怎么选、参数怎么调、怎么排错）在用户册四页，这里不重复。

## 一、六个文件，两种身份挤在同一个类里

```
Plugins\Plugin.CodeReader\
  CodeReaderPlugin.cs     363 行  ← 算法入口 + 配置界面 ViewModel（一个类兼任）
  BarCodeEngine.cs        140 行  ← 一维：create_bar_code_model + find_bar_code
  DataCodeEngine.cs       167 行  ← 二维：create_data_code_2d_model + find_data_code_2d
  CodeSymbology.cs         93 行  ← 码制表（20 条）+ TryGet + DescribeValidNames
  CodeReaderView.xaml     167 行  ← 纯绑定视图
  CodeReaderView.xaml.cs   24 行  ← 只剩两行转发（Loaded 信号 + 试算按钮）
```

```
CodeReaderPlugin : VisionPluginBase              ← 运行期算子（RunAlgorithm / 端口）
                   IPluginCustomViewProvider      ← 设计期：GetConfigView 交出视图
```

`GetConfigView(stepData)`（:213-217）里先 `Initialize(stepData)` 再 `new CodeReaderView { DataContext = this }`：
**VM 就是插件本身**，所以码制/极性的下拉、`Is2D` 的可见性切换全挂在插件属性上，code-behind 只剩
`Loaded → (DataContext as CodeReaderPlugin)?.OnViewLoaded()` 与按钮转发。
代价与 ROI 类插件相同：这个对象活得和配置会话一样长，所有非托管句柄必须在它的 `Dispose()` 里收尾（见第六节）。

类身份：`[Display(Name="码读取", GroupName="图像处理", Description="读取一维条码与二维码（Data Matrix / QR / Micro QR / PDF417 / Aztec / DotCode 等）", ShortName=U+F02A)]`（:44-49，
`ShortName` 是 FontAwesome 私有区字形，换字体就是方框）+ `[ParallelSafe]`（:50）。

## 二、参数走 `[StepConfig]`，图像走端口——分得很清楚

| 成员 | 通道 | 默认 | 为什么走这条 |
| --- | --- | --- | --- |
| `Symbology`（string，:64-76） | `[StepConfig]` → `InputValues` | `CodeSymbologyTable.Default.HalconName` = `Data Matrix ECC 200` | 常量配置，不参与数据流、不可变量链接；setter 里 `SetProperty` 成功后补发 `Is2D` / `SymbologyHint` 通知（下拉换码制，界面说明与可见性要同一刻跟上） |
| `TimeoutMs`（int，:113-114） | `[StepConfig]` | 500 | 同上 |
| `Polarity`（string，:121-122） | `[StepConfig]` | `any` | 同上。存 HALCON 原始值，不存中文 |
| `Image`（`InputPort<HImage>`，:129-132） | **端口** | null，`IsRequired = false` | 它是数据流，要能被上游连线、要参与试运行桥接 |
| `Codes` / `Count` / `Text`（:135-141） | **输出端口** | — | `string[]` / `int` / `string` |

本插件**有自定义视图**，所以 `[StepConfig]` 在这里是有入口的（对照「几何查找」「几何计算」：
没有自定义视图的插件必须把参数全做成端口，否则界面上改不了）。

## 三、一次 `RunAlgorithm`（:281-320）

```
1. image = Image.GetTypedValue()；null 或未初始化 → Fail("上游没有图像：请把上级定位节点（或裁剪节点）的输出连到本节点的 Image") → return
2. TryRead(image, out codes, out error, out countNote)；false → Fail(error) → return
3. Codes.Value = codes.ToArray(); Count.Value = codes.Count; Text.Value = string.Join(",", codes)
4. codes.Count > 0 → Logger.Info("码读取：读到 N 个码（码制 X）：a | b")
   否则 → Logger.Info("码读取：本帧未读到码（当前码制 X，超时 Nms）。若图上确有码，请先确认：① 码制是否选对；② …")
5. countNote 非空 → Logger.Warn("码读取：" + countNote)
```

`TryRead`（:327-348）是**运行期与试算共用**的唯一入口（:255-275 的 `TryPreviewRecognize` 也调它）：

```
CodeSymbologyTable.TryGet(Symbology, out entry) 失败 → error = "码制「X」不在支持的码制表里。可选：{DescribeValidNames()}" → false
entry.Family == DataCode2D → _dataCodeEngine.TryDecode(img, HalconName, Polarity, TimeoutMs) 且 countNote = _dataCodeEngine.LastCountNote
否则（一维）    → _barCodeEngine.TryDecode(img, HalconName, TimeoutMs)          ← 不传极性（实测不接受）
```

**返回 false 只表示程序级失败**（码制名不认识 / 模型建不起来 / 查找阶段抛）；
"没读到码"是返回 true 且列表为空——这条语义是用户确认过的，别在重构时把它改成 Fail。

## 四、两套引擎的差异（这是本插件的全部复杂度所在）

| 维度 | `BarCodeEngine` | `DataCodeEngine` |
| --- | --- | --- |
| 码制在哪给 | `find_bar_code` 的**入参**（:79-85，`new HTuple(halconCodeType)`） | `create_data_code_2d_model` 的**构造参数**（:144-149），一个模型只能一种码制 |
| 句柄缓存 | 建一次就复用（`_handle.Length > 0` 直接返回，:121） | 按码制缓存：`_handleForSymbology` 相同才复用，不同就 `ClearDataCode2dModel` + 重建（:135-150） |
| 每次 find 前重设的参数 | 只有 `timeout`（且 `timeoutMs > 0` 才设，:113-114） | `polarity`（非空即设）+ `timeout`（>0 才设，:124-128） |
| 结果形态 | 只回 `dataStrings` 一个元组；`symbolRegions` 立刻在 `finally` 释放 | 回 `symbols`（区域，`finally` 释放）+ `resultHandles` + `dataStrings` |
| 收码规则 | 逐个 `dataStrings[i].S`，**空白文本丢弃**（:87-92） | 同样按"字符串非空"收（:95-100） |
| 诊断字段 | 无 | `_lastCountNote`：`句柄数 != 解出数` 时写"二维候选 N 个（句柄 M 个），其中成功解出 K 个"（:104-107） |
| 异常口径 | `catch (Exception)` → `error = "一维条码识别失败（码制 'X'）：…。请确认码制是否为：…"` → false | 建模型失败与识别失败两条文案（:67-68 / :113） |

两个引擎都是 `sealed class … : IDisposable`、都**不懒初始化**：
插件字段初始化器直接 `new()`（:152-154），构造几乎零成本、模型句柄首次用到时才建（:117-125 / :131-152）。
注释写明了理由——配置的「试算」与流程运行可能并发，懒初始化要额外处理创建竞态，而两个空引擎的开销可以忽略。

两个引擎内部都留了**没暴露的旋钮**：一维实测被接受的参数名清单写在 `BarCodeEngine` 的 remarks
（`element_size_min` / `num_scanlines` / `quiet_zone` / `orientation` 等），当前只用 `timeout`——
"多一个旋钮就多一处要现场理解的东西"。要加就照这张实测清单加，不要猜参数名。

`_lastCountNote` 是**粘滞字段**：只在二维成功路径里被赋值，失败路径不清空。
调用方（`TryRead`）必须在同一次 `TryDecode` 之后**立刻**读，别隔轮读、别把它当"本轮状态"缓存到插件字段上。

## 五、为什么配置界面没有框选

`CodeReaderView.xaml` 右侧用的是 `ImageReadOnly`（`HImage` 单向绑定 `DisplayImage`），不是 `ImageEdit`。
视图注释写得明确：与 OCR 的配置界面相比少了一整块——没有图像编辑器、没有框选，
因为本节点只吃上级裁好的小图，识别区参数一概不存在；给个能画的控件会让人以为能在这里框码。

图进入配置的**唯一来源**是端口：构造函数里订阅 `Image.PropertyChanged`（:162），
`OnImagePortChanged` 只认 `nameof(IInputPort.Value)` 这一项变化（:167），然后 `ShowUpstreamImage()`。
这条订阅不是装饰：试运行时上游图是 `PluginTestRunner.BridgeInputs` **通过给端口赋手动值**桥接进配置实例的
（`Engine\PluginTestRunner.cs:232-241`：先把 `configPort.LinkedSource = null`，再 `configPort.Value = 实际值`），
没有它，配置界面就只剩"自己找图"一条路，用户会觉得"图像绑不上上游"。

## 六、内存所有权

| 对象 | 谁创建 | 谁释放 |
| --- | --- | --- |
| `_barCodeEngine` / `_dataCodeEngine` 内的 HALCON 模型句柄 | 引擎 `GetHandle()`（首次用到时） | 引擎自身 `Dispose()`（`clear_bar_code_model` / `clear_data_code_2d_model`，包在 `try/catch` 里，释放失败不打断整体回收）；由插件 `Dispose()` 调 |
| `symbols` / `symbolRegions`（查找输出的区域） | 每次 find | 引擎 `finally` 立刻释放，**从不外传** |
| `DisplayImage`（配置界面预览） | `ShowUpstreamImage` 里 `new HImage(upstream)`（**自己的副本**） | setter 覆盖前 `Dispose` + 插件 `Dispose()` 收尾 |
| `Codes` / `Text` / `Count` 的值 | `RunAlgorithm` | `string[]` / `string` 由 GC 管；基类轮首回收只看 `IDisposable`，这里没有非托管值 |
| `Image` 端口承载的上游图 | 上游 | **上游**；本插件从不释放它，只用它的引用查询 |

`Dispose()`（:354-361）是**四步都做对**的样板：两个引擎 → 预览图 → `base.Dispose()`。
注释说明了为什么必须重写：基类只回收输出端口承载的非托管值，管不到引擎内部的句柄。
