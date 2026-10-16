# YOLO 目标检测 · 开发章节：宿主契约与配置模型

> 分类：图像处理 ｜ 来源：插件 Plugin.Yolo ｜ 读者：开发

## 一 类声明与工具箱身份

```
[Display(Name = "YOLO 目标检测", GroupName = "图像处理",
         Description = "用 ONNX 格式的 YOLO 检测模型找出目标（类别 + 置信度 + 位置）", ShortName = "\uf03d")]
[ParallelSafe] // 二期真并行：YoloSession.Cache 带 CacheLock
partial class YoloPlugin : VisionPluginBase, IPluginCustomViewProvider
```

`ShortName` 是工具箱图标用的私有区字形码，不是给中文界面看的文字。
`[ParallelSafe]` 的判据是基类按 **FullName 字符串匹配**这个特性，改命名空间或类名会让它**静默失效**（编译照过、并行照关）。

## 二 白拿的基类行为

| 基类给的 | 本插件用法 |
| --- | --- |
| 反射收集端口属性 | 8 个静态端口（下表），没有动态端口 |
| `Execute` 预置 `Success=true` | `RunAlgorithm` 只写失败分支 |
| `Fail(msg)` | 上游没图时 `Fail("上游没有图像：请把图像节点的输出连到本节点的 Image")` |
| `ApplyConfigValues` | 用默认实现；换模型路径后由 `Hint`、`ModelSummary` 呈现 |
| `PublishPreview` | 试算时把带框示意图交宿主（R36：不许自己开窗口） |
| `Logger` | 零检测时写 Info 自查清单，裁剪夹取时写 Warn |

`Dispose` 里做三件事：释放会话引用、释放 `DisplayImage`、`base.Dispose()`。少第一步会话计数不归零，缓存永远留着。

## 三 端口契约

| 端口 | CLR 类型 | 口径 |
| --- | --- | --- |
| `Image` | `InputPort<HImage>` | `IsRequired = false`：让节点能先摆上去配模型再接线；真正的空判定在 `RunAlgorithm` 里做 |
| `Count` | `OutputPort<int>` | 0 也是成功 |
| `ClassIds` | `OutputPort<int[]>` | 与 `Count` 同长 |
| `ClassNames` | `OutputPort<string[]>` | 元数据没有类别名时是 `class_N` |
| `Confidences` | `OutputPort<double[]>` | 降序 |
| `Boxes` | `OutputPort<double[]>` | **扁平数组**，每 4 个一组 `x1,y1,x2,y2`，原图像素 |
| `BoxesText` | `OutputPort<string>` | 单条 `{类名}:{置信度}@[x1,y1,x2,y2]`，条目间用分号接，给记录上传这类标量下游 |
| `BestCrop` | `OutputPort<HImage>` | 置信度最高目标的裁剪图 |

数组端口给下游判定时**别只看长度**；`Boxes` 的下标除以 4 才是目标序号，写反是常见的静默错位。

## 四 `[StepConfig]` 与源生成器

| 键 | 默认值 | 备注 |
| --- | --- | --- |
| `ModelPath` | 空串 | `partial string` + `[BrowsePath]` 的文件筛选串是「ONNX 模型」加通配扩展名，再跟「所有文件」两档；相对路径按 `AppDomain.CurrentDomain.BaseDirectory` 解析 |
| `InputSize` | 0 | 0 = 用模型自带的静态输入尺寸 |
| `ConfidenceThreshold` | 0.25 | 与 Ultralytics 默认一致 |
| `NmsIoU` | 0.45 | 同类框重叠阈值 |
| `MaxDetections` | 100 | 0 = 不限 |
| `ClassFilterText` | 空串 | 逗号分隔的**类别索引**，不是类别名 |
| `CropMargin` | 0 | 没有 `DefaultValue` 标注，靠类型默认值 |

属性体由 `Core.PluginGenerators` 生成（写 `partial` 声明，别手写背垫，R16/R18）。
`BrowsePath` 的文件筛选串里出现竖线，写手册时要转义或改成文字描述——表格单元里的裸竖线会把表打坏。

## 五 配置界面与测试器桥接

实现 `IPluginCustomViewProvider.GetConfigView`，`Initialize(stepData)` 后 `new YoloView{DataContext=this}`；
`OnViewLoaded` 里刷新一次摘要（换图后不用重开界面也能刷新）。
构造函数订阅 `Image.PropertyChanged`，是为了 `PluginTestRunner` 桥接输入（`BridgeInputs`）时能立刻感知到值变化。
界面态属性：`Hint`、`ResultSummary`（未试算时显示"尚未试算"）、`ModelSummary`（getter 里调 `EnsureSession`）。

## 六 落盘与打包

`.vms` 里只有上面 7 个 `[StepConfig]` 键，没有额外自定义键；会话与推理结果都不落盘。
手册五页用户册加三页开发章节，全部扁平在 `Help\` 下、由 `.csproj` 里那条把 `Help` 目录全部 Markdown 收进 DLL 的 `EmbeddedResource` 通配项 收进 `Plugin.Yolo.dll`。
onnxruntime 的原生库同样要落在**宿主 exe 目录**（`x64`），只投插件 dll 会报"加载 ONNX 模型失败"。
