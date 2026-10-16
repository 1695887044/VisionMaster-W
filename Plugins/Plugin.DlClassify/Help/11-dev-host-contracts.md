# 深度学习分类 · 开发章节：宿主契约与配置模型

> 分类：图像处理 ｜ 来源：插件 Plugin.DlClassify ｜ 读者：开发

## 一 类声明

```
[Display(Name = "深度学习分类", GroupName = "图像处理", ... , ShortName = "\uf0c3")]
[ParallelSafe] // 会话带锁、推理无共享可变状态
partial class DlClassifyPlugin : VisionPluginBase, IPluginCustomViewProvider
```

`[ParallelSafe]` 由基类按 FullName 字符串匹配，改类名或命名空间会让它静默失效。

## 二 配置项：一份源生成，一份手写

| 键 | 写法 | 默认值 |
| --- | --- | --- |
| `ModelPath` | `[StepConfig]` 加 `[BrowsePath]` 的 `partial string`，属性体由 `Core.PluginGenerators` 生成 | 空 |
| `InputSize` | `partial int` | 0（用模型自带尺寸） |
| `TopK` | `[StepConfig, DefaultValue(3)] partial int` | 3 |
| `ResizeMode` | **手写属性**：`[StepConfig] public ClassifyResizeMode ResizeMode { get; set; } = ClassifyResizeMode.Letterbox;` | Letterbox |

`ResizeMode` 走手写而不是源生成，是本插件里唯一的例外；改它时要自己维护读写往返，
断言 `ModelPath 走源生成器 partial 属性（读写往返一致）` 只覆盖 `ModelPath`，别以为四个键都被同一条钉住。
`partial void OnModelPathChanged(string value) => OnPropertyChanged(nameof(ModelSummary));` 是换模型后刷新摘要的入口。

界面上的下拉数据源是 `public Array ResizeModeOptions { get; } = Enum.GetValues(typeof(ClassifyResizeMode));`（普通属性，不落盘）。
枚举成员在界面显示的是英文成员名 `Letterbox`、`Stretch`，这是 AGENTS.md R40 记在账上的显示欠账，尚未改成中文。

## 三 端口契约

| 端口 | CLR 类型 | 口径 |
| --- | --- | --- |
| `Image` | `InputPort<HImage>` | `IsRequired = false`，先摆节点再配模型；空图判定在 `RunAlgorithm` |
| `PredictedClassId` | `OutputPort<int>` | 最高分的索引 |
| `PredictedClassName` | `OutputPort<string>` | 元数据缺名时为 `class_N` |
| `Confidence` | `OutputPort<double>` | 0~1，最高类概率 |
| `Scores` | `OutputPort<double[]>` | 与 `ClassNames` **同序** |
| `ClassNames` | `OutputPort<string[]>` | 同上 |
| `TopKText` | `OutputPort<string>` | `{类名}:{概率}` 用分号接，给记录上传类下游 |

## 四 白拿的基类行为

`Success` 预置、`Fail(msg)`、`PublishPreview`、`Logger`、`ApplyConfigValues`、`OnConfirm` 与 `SnapshotConfigValue` 全部沿用基类，本插件不 `override` 端口重建（没有动态端口）。
配置界面走 `IPluginCustomViewProvider.GetConfigView(IStepConfigData)` → `Initialize(stepData)` → `new ClassifyView{DataContext=this}`；
`OnViewLoaded` 里补一次摘要刷新。

## 五 落盘与打包

`.vms` 里只有 `ModelPath`、`InputSize`、`TopK`、`ResizeMode` 四个键；会话、推理结果、模型元数据都不落盘（每次加载重读）。
`onnxruntime.dll` 等原生库随投递进**宿主 exe 目录**，插件目录里的副本不会被加载器找到。
手册四页用户册加三页开发章节，扁平放在 `Help\` 下（M 档，开发章节合计下限 150 行），
由工程里已有的 `.csproj` 里那条把 `Help` 目录全部 Markdown 收进 DLL 的 `EmbeddedResource` 通配项 收进 `Plugin.DlClassify.dll`；不建子目录（HM12b 判红）。
