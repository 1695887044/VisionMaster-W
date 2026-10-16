# 图像采集 · 开发：宿主契约与移植清单

> 分类：常用工具 ｜ 来源：插件 Plugin.ImageAcquisition ｜ 读者：开发

本页列**这个插件依赖宿主的哪些约定**（哪些行为不是它自己实现的，是基类/契约层/宿主服务给的），
以及照它写一个新的"入口/采集类"插件时要带走的东西。

## 一、继承自 `VisionPluginBase` 白拿的行为（不要重复实现）

| 契约 | 宿主给的行为 | 本插件哪里靠它 |
| --- | --- | --- |
| 端口发现 | 反射本类型公开实例属性，`IInputPort` / `IOutputPort` 自动进 inputs/outputs；端口名为空时自动用属性名；用 `TryAdd`（静态属性优先，动态注册不覆盖） | 本插件每个口都显式给了名字（`FilePath`、`Image`、`FrameId`…），属性名与端口名故意不同（`FilePathPort` → `FilePath`），**改名会打断既有接线与落盘键** |
| 默认成功 | `Execute` 进 `RunAlgorithm` 前预置 `Success=true`、清空 `ErrorMessage` | 本插件在 `RunAlgorithm` 开头**自己写回 false**（采集类语义：没产出就是没成功），成功路径再显式置 true——这正是"预置默认值"允许被覆盖的那类插件 |
| 异常转失败 | 未捕获异常 → `Fail("{Type}: {Message}")` + 日志；`RethrowOnException`（默认 false）决定是否继续上抛中断当轮 | 本插件自己 catch 成 `ErrorMessage = ex.Message`，所以走不到基类那句；两条路都不留"半张图" |
| 轮首回收输出 | `AutoDisposeRoundOutputs`（默认 true）在 `RunAlgorithm` **之前**释放所有输出端口的 `IDisposable` 旧值并置空；透传防御：与某条输入端口上游链接当前值同引用的输出跳过不杀 | `OutputImage` 的上一轮就靠它回收。**注意它只回收 IDisposable**，`CurrentIndex`/`TotalFiles`/`FrameId`/`ReceivedCount`/`SourceSerial` 必须自己在开轮清零 |
| 统一灌值 | `ApplyConfigValues`：`InputValues` → 输入端口（**已链接的端口跳过**）+ `[StepConfig]` 属性（走 `ValueConverter.Convert`，类型不匹配宽容跳过） | 本插件**重写**了它：`_loadingConfig = true; try { base… } finally { _loadingConfig = false; }`。两条灌值路径都经过这里（配置对话框的 `Initialize` 与 `FlowCompiler` 编译运行实例），不压住 `OnModeChanged` 钩子就会在同一次灌值里刷两遍目录/预览，而运行实例根本没有界面 |
| 配置初始化 | `Initialize(IStepConfigData)` 默认实现 = `ApplyConfigValues(stepData)` | 本插件重写：先 `_disposed = false`（否则上一次 Dispose 的标记会把新会话的预览全误杀），`base.Initialize` 之后按模式恢复预览（Folder → `RefreshFolderFiles`；SingleFile 且文件存在 → `LoadPreview`；其余 → `ValidatePathInputs` 保证信息栏不空白） |
| 默认确认 | `OnConfirm`：未链接端口值写回 `InputValues`，**已链接端口 RemoveInputValue**（避免残留脏数据）；`[StepConfig]` 属性逐个 `SnapshotConfigValue` 后写入 | 本插件**不重写** `OnConfirm`。但「推送测试」在真发 HTTP 之前**主动调一次** `OnConfirm(StepData)`——否则测的是上一次确认的旧配置，测通了也不算数 |
| 配置快照 | `SnapshotConfigValue`：基元/string/枚举/decimal 直存，其余 JSON 序列化再反序列化拷贝 | 本插件的配置项全是基元，靠的就是"直存"这条；集合类快照语义见「ROI」插件 |
| 默认取消 | `OnCancel` 空实现（试运行读的是已保存的 `InputValues`、不落盘） | 未重写 |
| 动态端口 | `AddDynamicOutput` / `ClearDynamicOutputs` | **本插件不用**：入口算子的端口面固定（`IDynamicOutputProvider` 未实现） |
| 基类 `Dispose` | 释放所有输出端口承载的 `IDisposable` 值，单个失败不影响整体 | 本插件 `Dispose()` 只做 `_disposed=true; _previewLoadId++; PreviewImage=null; base.Dispose();`——**末尾必须调 base** |

## 二、实现的两个接口各自意味着什么

| 接口 | 方法 | 谁调、什么时候 |
| --- | --- | --- |
| `IPluginCustomViewProvider` | `object GetConfigView(IStepConfigData stepData)` | 宿主打开该步骤的配置窗口时调；返回 `ImageAcquisitionView`，它同时实现 `IPluginConfigView`（`Initialize`/`OnConfirm`/`OnCancel` 转交插件实例） |
| `IPluginConfigContextProvider` | `void SetConfigContext(PluginConfigContext context)` | 宿主在配置窗口打开、且已把视图挂进内容区**之后**调一次；不实现此接口的插件不受影响（宿主做一次 `is` 判定，属于非破坏性扩展） |

`PluginConfigContext` 的字段与本插件的用法：

| 字段 | 类型 | 本插件用来做什么 |
| --- | --- | --- |
| `FlowName` | string | 预填「网络推送」页的流程名，并作为 Hub 取图的分槽名（与 `IExecutionContext.CurrentFlowName` 同口径） |
| `HttpEnabled` / `HttpListening` | bool | 服务状态提示。两者**可能不一致**：配置为启用但端口被占用/启动抛异常时就是"启用但没在听"，界面要分开说 |
| `HttpHost` | string | 已由宿主把 `0.0.0.0` / `[::]` 映射成可连接的本地回环地址，插件直接拼 URL |
| `HttpPort` | int | 端口输入框预填值 |
| `HttpToken` | string | 令牌输入框预填值（`Authorization: Bearer {HttpToken}`） |
| `RequestTimeoutMs` | int | 服务端等待流程上限；推送测试把**客户端**超时设为 `RequestTimeoutMs + 5000` |
| `Cameras` | `List<CameraOption>` | 「相机采集」页下拉候选（`ApplyCameraOptions` 灌进 `CameraOptions`，并按已落盘的 `CameraSerial` 回显选中项，`OrdinalIgnoreCase`） |
| `Variables` | `List<VariableOption>` | 本插件当前未用它做候选（路径/索引走行内 🔗 链接），契约与 `Cameras` 同一范式 |

> **为什么必须是快照而不是活对象**：插件视图只用于"照着当前配置拼一次请求"，不需要感知宿主配置的热变更；
> 传值类型可以彻底避免插件反向持有宿主对象、在窗口关闭后仍引用已释放资源。
> 直接给 `CameraDescriptor` 等于让插件持有宿主方案里的活对象，插件即使在窗口关闭后仍能改它（顺手改序列号），
> 而宿主完全察觉不到——这类问题只在下次保存方案时以"配置怎么变了"的形式暴露。
> 同理 `CameraOption.State` 给的是**枚举**而不是中文串：插件侧要按状态决定"这是正常还是会失败"，
> 拿文案比较等于把界面文字变成逻辑契约，改一次文案就断。

## 三、`IExecutionContext` 用到的四项

| 成员 | 用途 | 口径 |
| --- | --- | --- |
| `Logger` | `Info`（成功一句话结果）/ `Warn`（预览投递失败）/ `Error`（业务失败） | 一律带 `{InstanceName}` 前缀；`InstanceName` 是编译期注入的 `流程名.步骤名` |
| `Cameras`（`ICameraProvider`） | `TryGetDeviceBySerial(serial, out device)` 拿运行态设备 | 序列号两端 Trim、`OrdinalIgnoreCase`；未装配时是 `NullCameraProvider`（查不到、不抛），所以插件不必判空只判返回值 |
| `CurrentFlowName` | `ImageHub` 的分槽键 | 与 HTTP 投递方（URL 里的流程名）同口径；两侧名字来源不同但必须相等，否则图永远取不到 |
| `CancellationToken` | 传给 `ImageHub.WaitPop` / `ICameraDevice.WaitNextFrame` | 取消后本步骤以"未成功"收尾并写取消文案，**插件不决定流程去向**（引擎的序列执行器检测取消令牌后自会结束当轮） |

`ImageHub`（静态）：`Push` / `TryPop` / `WaitPop` / `Count` / `Clear` / `ClearAll` / `TryRemove`。
按流程名分槽的理由：多条流程可同时被 HTTP 触发，共用一个队列会出现"A 流程的图被 B 流程取走"，
只在并发时偶发、最难查。每槽上限 8 帧、满则丢**最旧**：实时检测里旧帧已无价值，丢新帧会让延迟越积越大。

## 四、显示侧契约（`PublishPreview`）

```csharp
if (DisplayViewIndex > 0 && result.Image != null)
    TryPublishPreview(result.Image, DisplayViewIndex, context);   // 内部 try/catch → 只记 Warn
```

- **窗口号直传，不要 `+1`**：消费端按 `ViewIndex == 窗口号` 等值筛选（九宫格）。历史上示例代码写过 `+1`
  被部分插件照抄，症状是"选窗口 1 图进第 2 格、选窗口 9 哪格都不显示"。
- **所有权不转移**：传的仍是端口持有的那张图，发布方下一轮就会释放它。需要留存帧的宿主必须通过
  `ImagePreviewCaptureHub.Current`（`IImagePreviewCapture`）在**发布线程上同步拷走**——
  `PublishPreview` 在派发事件前先调它。异步到 UI 线程再 `CopyImage` 就已经晚了：图被完全释放时
  `IsInitialized()` 为 false 会静默丢弃（画布空白、无日志），释放进行到一半则 HALCON 报
  `#4060 access to deleted region in copy_image`。现场表现是"单次运行正常、循环一开图不会被收纳"。
- **推图失败绝不判采集失败**：事件总线在无 UI 环境会就地同步执行订阅者，所以 `TryPublishPreview`
  把异常吞成一条 Warn。显示是"锦上添花"，不参与成败判定。

## 五、`[StepConfig]` 写法与源生成器（R16 / R18）

本插件的三个配置项走"partial property + 钩子"写法（类必须声明 `partial`，钩子**只写实现、不写声明**）：

| 属性 | 钩子 | 为什么必须是这个时机 |
| --- | --- | --- |
| `Mode` | `partial void OnModeChanged(AcquisitionMode value)` | 换模式要清预览 + 重新校验；Folder 且非灌值期才刷目录 |
| `DisplayViewIndex` | `partial void OnDisplayViewIndexChanging(ref int value) => value = Math.Clamp(value, 0, 9);` | **改值前夹取**：手改方案文件塞进 10 或负数不会报错，但表现为"配了显示哪格都不显示"，所以必须在写入时夹死 |
| `CameraSerial` | 无钩子（由影子属性 `SelectedCameraOption` 写入） | 下拉绑定的是 `CameraOption` 对象，选中时把 `SerialNo` 落进 `CameraSerial` |

不满足约定报编译错误 `CPG0001~CPG0005`；改动 `Shard\Core.PluginGenerators` 后必须**重建全部插件工程**（R16），
生成物在插件 DLL 里编译期展开，不重建则运行的是旧 DLL。`[ParallelSafe]` 由 `FlowCompiler` 按类型
**FullName 字符串**匹配（不是 `typeof` 等值），为的是绕开"两份 `Core.Interfaces.dll`"导致程序集身份不一致——
本插件是进程级资源的消费方（阻塞等图），**没有**标 `[ParallelSafe]`。

## 六、试运行与"同一份代码"

试运行**不在任何接口里**：由主程序 `PluginTestRunner` 统一执行插件的 `RunAlgorithm`，
所以插件不需要、也不许为"试运行"写第二套代码。配置窗口底部的「执行」跑的正是正式运行同一条路径——
这也是"信息栏那句结果就是最可信自检"的根据。

## 七、移植清单（照它写新的入口类插件）

| 要带走 | 必须保持 |
| --- | --- |
| `VisionPluginBase` + 两个可选接口的三身份结构；视图 code-behind 只挂 `DataContext` 与 `Initialize` | 插件不持有宿主活对象，只吃 `PluginConfigContext` 快照 |
| 开轮清零的写法（含"基类只清 IDisposable，数值口自己归零"这条） | 失败轮不得留任何上一轮残值，包括身份口 `SourceSerial` |
| 像素 → HImage 的六道关口与 `GCHandle(Pinned)` + `try/finally Free` | 全仓只有一处转换核心；新链路必须复用它，不许另写一份 |
| 取消与失败的分离（`Cancelled` 分支只写 `ErrorMessage`） | 插件不决定流程去向 |
| 显示是锦上添花（`>0` 才发、异常吞成 Warn） | 窗口号直传、不转移所有权 |
| 目录列举/扩展名白名单的单一真相源 | 排序稳定（Ordinal 自然序），不做文化敏感比较 |
