# 深度学习异常检测 · 内部结构

> 分类：图像处理 ｜ 来源：插件 Plugin.DlAnomaly ｜ 读者：改这个插件的人

## 一、文件构成与职责

| 文件 | 行数量级 | 职责 | 是否依赖 HALCON |
| --- | --- | --- | --- |
| `AnomalyContract.cs` | ~160 | 输出契约类型 `AnomalyOutputContract` + 按秩嗅探 `TryResolveContract` + `Flatten` | **否**（纯 OnnxRuntime） |
| `AnomalyPostProcess.cs` | ~120 | `AnomalyResizeMode` / `AnomalyInterpolation` 枚举，`ResizePlan`（含 `PadGrayValue = 114`） | 否 |
| `AnomalySession.cs` | ~220 | 进程级会话缓存 + 引用计数 + `TryAcquire` 三条守卫 + 模型元数据解析 `AnomalyModelInfo` | 否 |
| `AnomalyEngine.cs` | ~420 | 预处理（缩放进灰画布）、NCHW 张量构造、推理、热力图还原成 HImage | **是** |
| `DlModelState.cs` | ~15 | 三态枚举 `NotLoaded / Ready / Failed`（与 Plugin.DlClassify 同名同义，各自一份） | 否 |
| `DlAnomalyPlugin.cs` | ~460 | 插件壳：参数、端口、运行期、会话生命周期、视图态 | 是（端口类型） |
| `AnomalyView.xaml` + `.xaml.cs` | ~830 + ~140 | 专属配置视图 + 双命令 + 两个显示名转换器 | 是（预览控件） |

这个分层是**刻意的重复**：Plugin.DlClassify 里有一份结构几乎一样的实现。方案说明书 §5.1 明确"每个 DL 插件各自持有实现"，理由是——不改已验证的分类插件、避免跨插件引用导致的版本联动。**不要为了"去重"把这两份抽成公共工程**，除非要落 `Services/Core.Onnx`（那是公共契约级改动，按 R12 需架构评审）。

## 二、一次检测的九步调用链

```
DlAnomalyPlugin.RunAlgorithm(context)
 ①  协作取消检查（context.CancellationToken）
 ②  轮首复位全部输出端口（NaN / false / -1 / null / ""）
 ③  Image.GetTypedValue() 取图；空 → Fail
 ④  TryDetect → EnsureSession()：路径没变复用、变了 ReleaseSession + TryAcquire
 ⑤  new AnomalyEngine(session).TryDetect(image, params, out result, out error)
       a. ResolveInputSize：参数 InputSize > 0 优先，否则模型静态 H/W
       b. GetImageSize src → ResizePlan.Compute（Letterbox 等比 + 居中，或 Stretch）
       c. TryBuildCanvas：转 byte → 单通道 Compose3 → ZoomImageSize → 补 114 灰画布 → ChangeDomain + OverpaintGray
       d. ToNchwFloat：逐通道 AccessChannel + GetImagePointer1 + Marshal.Copy + /255
       e. DenseTensor → NamedOnnxValue.CreateFromTensor → session.Run
       f. 按契约名从输出字典取值（分数 flat[0]；热力图校验长度）
       g. 热力图 min/max/mean + 峰值索引 → (idx / W, idx % W)
       h. BuildHeatmapImage：按上限归一化 0~255 → GenImage1
 ⑥  填端口（Score / HasScore / HeatmapMaxValue / Row / Col / HeatmapImage / Summary）
 ⑦  context.Logger.Info（含耗时与模型文件名）
 ⑧  DisplayViewIndex > 0 且有热力图 → PublishPreview(heat, viewIndex)，异常只 Warn
 ⑨  返回；成功与否由 Success / ErrorMessage 表达
```

## 三、资源释放四层

| 层 | 谁释放 | 关键点 |
| --- | --- | --- |
| 输入图 | **上游/框架** | 本节点只读，绝不 `Dispose` 上游句柄 |
| 中间态 `HObject`（缩放结果、画布、overpaint 结果） | 本节点的 `finally` | 每个 `HObject` 与新 `HImage` 都要有归宿，漏一个就是长期泄漏 |
| 输出端口上的 `HImage` | `VisionPluginBase.DisposePreviousRoundOutputs()`（下一轮开头） | **提前 Dispose 会让端口上的图变野指针**，表现是下游偶发崩 |
| ONNX 会话 | `AnomalySession` 引用计数归零时 | `Dispose()` 里 `_entry.RefCount--`，归零才 `Session.Dispose()` + `Cache.Remove` |

顶部 `TryBuildCanvas` 里另有一条纪律：**`overpaint_gray` 要求两张图通道数一致**，所以画布无条件构造成 3 通道（否则报 `#3122`）。非 byte 图要先 `ConvertImageType(...,"byte")`——real 图转出来会被截断，是静默错误。

## 四、每个决策为什么这么定

### 1. 为什么独立的插件，而不是给 DlClassify 加个"异常模式"？

分类的产物是"类别概率分布"，异常的产物是"分数 + 空间热力图"，输出端口、判据、阈值方法三者都不同。塞进同一个类，端口会变成一半永远为空的形状，用户永远搞不清哪个该接。方案说明书也是这么排的：分类、异常、分割三个独立插件。

### 2. 为什么"按秩识别"而不是让用户选输出布局？

让用户选，等于让用户去理解 ONNX 输出语义——那不是他的工作；而自动猜又会让错误结果**看起来正常**。按秩识别是折中：**能唯一确定的就认，不能确定的就拒绝并显示真实形状**。"不猜"这条是方案说明书 §3.2 的硬要求。

### 3. 为什么补边用 114 而不是 0？

训练侧（Ultralytics 系）默认填充值就是 114。填 0 会在目标周围造出训练时没见过的强边缘，异常检测模型会把这些边缘当成异常，分数整体抬高。这个错误**不报错**，是最典型的静默失效。

### 4. 为什么归一化必须 /255？

模型训练时输入是 0~1 的 float。少除或多除，模型照样跑、输出照样是"分数"，只是精度静默劣化。所有"精度莫名其妙不如训练时"的问题，先查这一条。

### 5. 为什么会话要进程级共享 + 引用计数？

- 每次调用都 `Acquire`：引用计数只增不减，进程内存里堆模型，永不释放。
- 只在初始化 `Acquire` 一次：用户换了模型之后**跑的仍是旧模型**，而界面显示新路径——这类问题几乎无法从现象反推。

所以 `EnsureSession()` 判"路径变了没"：没变复用，变了先 `ReleaseSession()` 再取新的。

### 6. 为什么 `EnsureSession()` 里不发 `ModelSummary` 通知？

`ModelSummary` 的 getter 自己会调 `EnsureSession()`。在 `EnsureSession()` 里发 `OnPropertyChanged(nameof(ModelSummary))` 就是 getter 的自我重入，会无限递归。模型态的唯一写入口是 `RefreshModelState()`，由 `OnModelPathChanged` / `Initialize(IStepConfigData)` / `OnViewLoaded` 三处驱动。

### 7. 为什么热力图要自己还原成 HImage 而不是把张量直接给下游？

HALCON 侧的一切下游（结果显示、Blob、卡尺）只认 HImage。把 `float[]` 交给下游，等于要求每个下游都懂"这是热力图"。还原成 `byte` 0~255 的图，下游当普通灰度图用就行。

### 8. 为什么 `HeatmapMax = 0` 表示"自适应"而不是"关闭热力图"？

"关闭"由模型有没有热力图输出决定，不需要参数。"自适应"是常用档（看在哪），固定上限是高级档（看趋势）。默认给常用档，且 0 这个值不会与"上限为 0"这种无意义设定冲突。

## 五、端口发现与配置持久化

- 插件基类通过反射发现 `InputPort<>` / `OutputPort<>` 字段，**顺序即声明顺序**；这就是端口面板里的排列。
- `[StepConfig]` 属性同样按 `MetadataToken`（≈声明顺序）发现，读属性上的 `[Display]` 取中文名与说明。
- 持久化：`SaveParams/LoadParams` 走反射取 `[SuperDisplay]` 或 `[StepConfig]` 声明的可读写属性；枚举**存成员名**。
- 因此：**改成员名 = 旧方案读不回来（静默回落默认值）**；改显示名（`[Display(Name)]`）不影响。

## 六、构建与投递

```
dotnet build Plugins/Plugin.DlAnomaly/Plugin.DlAnomaly.csproj -m:1 -nodeReuse:false \
    -p:ModulesDir='D:\C#\VM-artifacts\modstage\'
```

- `-m:1 -nodeReuse:false`：本机多进程 MSBuild 会静默失败（报"生成失败/0 个错误"、退出码 1），一律单进程。
- `-p:ModulesDir=...`：当 `Modules\Plugin.DlAnomaly.dll` 被一个开着的宿主进程锁住时，把投递改到暂存目录；**不要为了能编译去杀用户的 VisionMaster 进程**。
- 原生库投递：`DeployOnnxRuntimeToHost` 目标把 `DLL\OnnxRuntime\` 整个 `xcopy` 到宿主 exe 目录。Windows 加载原生 DLL 不递归搜索 `Modules\`，所以这一步不能省。

## 七、手册打包

`<EmbeddedResource Include="Help\**\*" />` 把八页 .md 打进程序集。**改过任何一页内容，产物就算脏**，必须重新构建；增量构建会静默漏打包。核验：

```
ls Plugins/Plugin.DlAnomaly/Help/*.md | wc -l
tr '\0' '\n' < Plugins/Plugin.DlAnomaly/bin/Debug/net9.0-windows/Plugin.DlAnomaly.dll | grep -a "\.Help\." | sort -u
```

两条命令的**条数**必须相等（中文文件名会让 ASCII 正则截断，只比条数，不逐行匹配）。
