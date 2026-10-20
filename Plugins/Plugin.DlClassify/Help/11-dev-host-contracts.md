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
`partial void OnModelPathChanged(string value) => RefreshModelState();` 是换模型后刷新模型态与摘要的入口。

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
`OnViewLoaded` 里补一次 `RefreshModelState()`（模型态 + 摘要一起刷新）。
`ModelState`（`DlModelState`：`NotLoaded / Ready / Failed`）是 `private set` 的只读运行态，**不带 `[StepConfig]`**，
`OnConfirm` 不会把它写进 `InputValues`。

## 五 落盘与打包

`.vms` 里只有 `ModelPath`、`InputSize`、`TopK`、`ResizeMode` 四个键；会话、推理结果、模型元数据、
`ModelState` 都不落盘（每次加载重读）。
`onnxruntime.dll` 等原生库随投递进**宿主 exe 目录**，插件目录里的副本不会被加载器找到。
手册四页用户册加三页开发章节，扁平放在 `Help\` 下（M 档，开发章节合计下限 150 行），
由工程里已有的 `.csproj` 里那条把 `Help` 目录全部 Markdown 收进 DLL 的 `EmbeddedResource` 通配项 收进 `Plugin.DlClassify.dll`；不建子目录（HM12b 判红）。

## 六 配置界面契约（视图只读状态）

视图（`ClassifyView`）与插件的分工是固定的：

- `DataContext` 必须是插件实例；宿主靠 `DataContext as IPluginConfigView` 取 `OnConfirm` / `OnCancel`。
- 视图只负责布局、文件对话框、两个缩放转发；**不写业务逻辑**，也不自己判断模型是否加载成功。
- 模型失败态的唯一判据是 `ModelState == DlModelState.Failed`：
  模型信息卡（背景/边框）、摘要文字色、空态互斥、错误覆盖层四处都绑它，**不绑 `ModelSummary` 文案**。
- `ModelSummary` 只做文本展示（等宽多行），不参与任何 `DataTrigger` 的 `Value`。
- `Hint` / `ResultSummary` 原样消费；插件没有 `StatusLevel`，视图不伪造档位。
- 图像预览走 `ContentControl` + `DataTemplate` 内的 `ImageReadOnly`，有图才创建控件；
  不要把已设图的控件用 `Visibility=Collapsed` 折叠（折叠后收到新图可能不重绘）。

## 七 状态刷新的时序

`RefreshModelState()` 会在三个时刻执行：`ModelPath` 的源生成器 setter 回调、`Initialize(stepData)`、`OnViewLoaded`。
每次执行都会真实调一次 `EnsureSession()`（同路径命中会话缓存，不会重复加载模型），
所以"绑定 `ModelSummary`"本身不会额外加载模型——加载发生在这些明确入口。
宿主若复用同一个插件实例打开第二次配置窗口，`Initialize` 会重新应用 `InputValues` 并刷新状态；
`ModelState` / `PreviewState` 不是配置项，不需要（也不会）从 `.vms` 恢复。
