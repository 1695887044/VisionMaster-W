# 深度学习异常检测 · 不变量清单

> 分类：图像处理 ｜ 来源：插件 Plugin.DlAnomaly ｜ 读者：改这个插件的人

本页列出"改代码时最容易踩坏、且坏了不会报错"的不变量。每条都写清**违反后的症状**与**怎么发现**。

## A. 契约嗅探（AnomalyContract.cs）

### A1 按秩识别是唯一入口，不许改成"按顺序取输出"

- **判据**：分数 = 秩 2 且 `dims[1] == 1`；热力图 = 秩 4 且 `dims[0] == 1 && dims[1] == 1`（H=`dims[2]`、W=`dims[3]`），或秩 3 且 `dims[0] == 1`（H=`dims[1]`、W=`dims[2]`）。其余一律拒绝。
- **违反症状**：把 `[1,84,8400]` 这类检测输出当成异常分数硬算，界面上会出一个"看着像分数"的数，永远无法判定对错。
- **怎么发现**：`TryResolveContract` 返回 false 且错误串里带真实形状。

### A2 拒绝时必须报出读到的真实形状

- **判据**：错误串包含每个输出的名字、秩、形状（`读到的输出：{name}（秩 n，形状 [...]）`），并明确"本插件只认两种……不会替你猜"。
- **违反症状**：用户看到"模型不支持"但不知道自己模型的输出是什么形状，无法自助修正。
- **怎么发现**：断言里检查错误串同时含 `秩` 与 `形状`。

### A3 输出名只能来自 `OutputMetadata` 的 Key

- **判据**：`TryResolveContract` 的入参是 `IReadOnlyList<KeyValuePair<string, NodeMetadata>>`；名字取 `Key`，维度取 `Value.Dimensions`。
- **违反症状**：`NodeMetadata` **没有 `Name` 属性**（实测 OnnxRuntime 1.x）。若改成只传 `OutputMetadata.Values`，就永远拿不到名字，只能自己编一个，加载期认出的契约名与 `Run()` 返回的名字对不上，**静默取不到值**。
- **怎么发现**：模型加载成功但运行时报"输出里找不到 xxx"。

### A4 热力图长度必须与契约声明的 H×W 一致

- **判据**：`flat.Length == HeatmapHeight * HeatmapWidth`，不等直接报错。
- **违反症状**：索引越界或图像错位，热力图上屏的位置和实际缺陷对不上。
- **怎么发现**：断言里给一个形状对、元素数不对的假张量。

## B. 预处理（AnomalyEngine.cs）

### B1 归一化必须 `/255`

- **判据**：`ToNchwFloat` 里 `bytes[i] / 255f`。
- **违反症状**：模型照样跑、照样出分数，只是精度静默劣化。所有"精度不如训练时"的问题先查这条。
- **怎么发现**：拿同一张图与训练侧推理结果比对（数值层面对拍）。

### B2 补边灰必须是 114

- **判据**：`ResizePlan.PadGrayValue == 114`。
- **违反症状**：填 0 会在目标周围造出训练时没见过的强边缘，异常模型把它当异常，**分数整体抬高、误报激增**，且不报错。
- **怎么发现**：同一张正常图，改 `PadGrayValue` 前后分数应基本一致；改 0 后明显升高就说明这条被破坏了。

### B3 Letterbox 的缩放比取 `min`，不是 `max`

- **判据**：`scale = Math.Min((float)dstW / srcW, (float)dstH / srcH)`；`scaledW = Math.Max(1, (int)Math.Round(srcW * scale))`。
- **违反症状**：取 `max` 会裁掉内容（等比"填满"而不是"装下"），热力图坐标换算全错。
- **怎么发现**：用非等比源图（如 200×100 → 224×224）检查 `ResizePlan` 的 `ScaledWidth/Height/PadX/PadY`。

### B4 画布必须无条件 3 通道

- **判据**：单通道图先 `Compose3(img, img, img)` 再参与 `OverpaintGray`。
- **违反症状**：`overpaint_gray` 要求两张图通道数一致，不一致直接抛 `HALCON error #3122`。
- **怎么发现**：灰度输入跑一次即可复现。

### B5 非 byte 图先转 byte

- **判据**：`GetImageType` 不是 `byte` 时先 `ConvertImageType(..., "byte")`。
- **违反症状**：real 图直接取字节会得到截断/无意义的值，分数静默失真。
- **怎么发现**：喂一张 real 图，与先手动转 byte 的结果比对。

## C. 会话生命周期（AnomalySession.cs）

### C1 引用计数只在锁内改

- **判据**：`Cache` 的增删与 `RefCount` 的增减都在 `CacheLock` 内。
- **违反症状**：并行分组里两条分支同时首次加载同一模型，可能创建两份会话或提前释放。
- **怎么发现**：并行压测后看 `CachedSessionCount` 是否为 1。

### C2 路径变了必须换会话

- **判据**：`EnsureSession()` 里 `_sessionModelPath != resolved` 就先 `ReleaseSession()` 再 `TryAcquire`。
- **违反症状**：用户换了模型，跑的仍是旧模型，而界面显示新路径——几乎无法从现象反推。
- **怎么发现**：断言里换路径后检查 `_sessionModelPath`。

### C3 建会话成功但元数据解析失败必须就地释放

- **判据**：`catch` 分支里 `onnxSession.Dispose()`。
- **违反症状**：原生句柄泄漏，反复换模型会耗尽内存。
- **怎么发现**：循环加载一个坏模型 100 次，看进程内存是否持续上涨。

### C4 三条前置守卫不能省

- **判据**：空路径 → "尚未选择模型……"；`!File.Exists` → "模型文件不存在：{path}……"；`!Environment.Is64BitProcess` → 32 位提示。
- **违反症状**：省掉后用户看到的是 `DllNotFoundException` 或 `BadImageFormatException`，报的路径让人以为托管 DLL 有问题。
- **怎么发现**：断言里各走一遍，检查错误串。

## D. 插件壳（DlAnomalyPlugin.cs）

### D1 `RunAlgorithm` 必须是 `public override`

- **判据**：基类声明是 `public`，写成 `protected override` 报 `CS0507`（降低访问性）。
- **违反症状**：编译失败。
- **怎么发现**：编译。

### D2 轮首复位全部输出端口

- **判据**：`Score = NaN`、`HasScore = false`、`HeatmapMaxValue = NaN`、`HeatmapMaxRow/Col = -1`、`HeatmapImage = null!`、`Summary = ""`。
- **违反症状**：上一轮的分数残留，本轮失败时下游拿到"看起来有效"的旧值。
- **怎么发现**：先跑一次有热力图的模型，再跑一次失败调用，检查端口是否为复位值。

### D3 输出图不许自己提前 Dispose

- **判据**：`HeatmapImage.Value` 交给基类的 `DisposePreviousRoundOutputs()` 下一轮释放。
- **违反症状**：提前释放会让端口上的图变野指针，下游偶发崩溃且堆栈指向别处。
- **怎么发现**：连续跑多轮，若第二轮开始出现随机崩溃。

### D4 `EnsureSession()` 里不许发 `ModelSummary` 通知

- **判据**：模型态唯一写入口是 `RefreshModelState()`，由 `OnModelPathChanged` / `Initialize(IStepConfigData)` / `OnViewLoaded` 驱动。
- **违反症状**：`ModelSummary` 的 getter 自己会调 `EnsureSession()`，在它里面发通知就是 getter 自我重入，无限递归或界面卡死。
- **怎么发现**：打开配置视图即卡住。

### D5 `RunAlgorithm` 首行检查取消

- **判据**：`if (context.CancellationToken.IsCancellationRequested) { Fail("执行已取消"); return; }`。
- **违反症状**：用户点了停止，本轮仍跑完推理。
- **怎么发现**：断言里传一个已取消的 token。

### D6 上屏要包 try/catch 且失败只 Warn

- **判据**：`this.PublishPreview(...)` 外面 `catch (Exception ex) { context.Logger.Warn(...) }`。
- **违反症状**：显示层异常把整步判失败，用户看到"执行失败"却查不出算法问题。
- **怎么发现**：无窗口环境下跑一次（无预览宿主）。

## E. 视图（AnomalyView.xaml/.xaml.cs）

### E1 命令必须在 `InitializeComponent()` 之前创建

- **判据**：构造器里先 `BrowseModelCommand = new UiCommand(BrowseModel); TryPreviewCommand = new UiCommand(TryPreview);` 再 `InitializeComponent();`。
- **违反症状**：XAML 的 `Command` 绑定在解析期求值，晚赋值会让 `BindingExpression` 缓存 null，**按钮静默无响应**。
- **怎么发现**：点"浏览"、"试算一下"没反应。

### E2 首屏刷新放 `Loaded`，不放在构造器

- **判据**：`Loaded += (_, _) => Plugin?.OnViewLoaded();`
- **违反症状**：`DataContext` 是宿主构造后才赋的，构造器里 `Plugin` 还是 null，首屏没有任何提示。
- **怎么发现**：打开视图时模型信息卡空白。

### E3 转换器 `ConvertBack` 必须返回 `Binding.DoNothing`

- **判据**：`AnomalyResizeModeLabelConverter` / `AnomalyInterpolationLabelConverter`。
- **违反症状**：把中文显示名写回模型，持久化变成中文，改成员名后旧方案读不回来。
- **怎么发现**：选一次缩放模式，看 `.vms` 里存的是 `Letterbox` 还是 `Letterbox · 等比补边（推荐）`。

### E4 图标按钮必须给 `Padding="0"`（或挂 Style）

- **判据**：`ToolbarButtonStyle` 里 `Width="30" Height="30" Padding="0"`。
- **违反症状**：吃 Fluent 的默认 `11,5,11,6`，30×30 只剩 6×17 内容区，图标被切。
- **怎么发现**：看按钮里的图标字是否只剩一条边。

### E5 视图里零十六进制颜色

- **判据**：所有颜色走 `Dialog*Brush` 令牌。
- **违反症状**：换主题/暗色底时出现不可读的硬编码色块。
- **怎么发现**：静态扫描 `#[0-9A-Fa-f]{6}`。

## F. 工程与打包

### F1 csproj 必须显式引 `halcondotnet` 并带 HintPath

- **判据**：`<Reference Include="halcondotnet"><HintPath>..\..\DLL\Halcon\halcondotnet.dll</HintPath></Reference>`。
- **违反症状**：经 `Core.Halcon` 传递的引用不带 HintPath，本工程 `using HalconDotNet` 时解析不到 `HImage`/`HObject`，报一串 `CS0246`。
- **怎么发现**：编译。

### F2 ONNX 原生库必须投递到宿主 exe 目录

- **判据**：`onnxruntime.dll` + `onnxruntime_providers_shared.dll` 两个 `<None>` 项 + `DeployOnnxRuntimeToHost` 目标。
- **违反症状**：Windows 加载原生 DLL 不递归搜 `Modules\`，运行期 `DllNotFoundException`。
- **怎么发现**：宿主里跑一次。

### F3 手册八页必须随程序集打包

- **判据**：`<EmbeddedResource Include="Help\**\*" />`，改过任何一页都要重建。
- **违反症状**：帮助里看不到新页，且构建不报错（增量构建静默漏打包）。
- **怎么发现**：比对磁盘 `.md` 条数与程序集内 `.Help.` 资源条数。

### F4 sln 必须登记本工程

- **判据**：`VisionMaster.sln` 里 1 条 Project + 12 条 Config + 1 条 NestedProjects。
- **违反症状**：VS「生成解决方案」不编本工程，`Modules\` 里的 DLL 永久停在旧版且**无任何报错**。
- **怎么发现**：`grep -c "<新工程 GUID>" VisionMaster.sln` 应为 14。

### F5 档位必须登记在 `HelpManualCheck.Tiers`

- **判据**：`["DlAnomalyPlugin"] = ManualTier.L`。
- **违反症状**：HM16 判据"档位表与插件类一一对应"直接红。
- **怎么发现**：跑 `FlowCanvasChecks`。

## G. 断言建议（`FlowCanvasChecks/AnomalyChecks.cs`）

| 断言 | 造法 | 反向判据 |
| --- | --- | --- |
| 秩 2 `[1,1]` 认成分数 | `TryResolveContract` 传 `[(score, Rank2)]` | — |
| 秩 4 `[1,1,H,W]` 认成热力图且 H/W 对 | 同上 | 把 H 改成 0 应拒绝 |
| 秩 3 `[1,H,W]` 认成热力图 | 同上 | — |
| 秩 5 被拒且错误串含真实形状 | `[1,1,1,64,64]` | 错误串不含 `秩` 即失败 |
| 检测输出 `[1,84,8400]` 被拒 | 秩 3 但 `dims[0] != 1` | — |
| 会话守卫：空路径文案 | `TryAcquire("")` | 错误串不含"尚未选择"即失败 |
| 会话守卫：路径不存在文案 | `TryAcquire("no_such.onnx")` | — |
| 插件壳：端口默认值 | 反射读 `[Display]` 与端口表 | 默认值变了即红 |
| 轮首复位 | 先填值再调 `Execute`（无图） | 复位值不对即红 |
| 取消 | `new CancellationToken(true)` | `ErrorMessage` 不是"执行已取消"即红 |

**造模型的手法**：用 Python `onnx` 包（opset 17）生成只含确定性 reduce 算子的最小模型，五种形状各一个（`anomaly_score` / `anomaly_score_heatmap` / `anomaly_heatmap_only` / `anomaly_bad_rank` / `anomaly_rank3_heatmap`）。**注意 opset 17 的 `Squeeze` 不接受 `axes` 属性**，要用第二输入常量张量，或改用 `Reshape`。

**这类自建模型只能证明"解码逻辑对给定形状正确"，不能证明"对某个真实检测器的导出正确"。两条结论必须分开记账，不许合并成一句"已验证"。**
