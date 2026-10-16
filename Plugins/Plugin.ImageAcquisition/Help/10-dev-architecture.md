# 图像采集 · 开发：内部结构与数据流

> 分类：常用工具 ｜ 来源：插件 Plugin.ImageAcquisition ｜ 读者：开发

本页讲**这个插件是怎么组织的、四条链路各自的数据流、一张图的生与死归谁管**。
改这个插件之前先读这页；要照它写新的"入口/采集类"插件，再读「宿主契约」与「不变量与陷阱清单」。

## 一、一个类兼任三个身份

```
ImageAcquisitionPlugin : VisionPluginBase              ← 运行期算子（RunAlgorithm / 端口 / 配置生命周期）
                       IPluginCustomViewProvider       ← 设计期：交出配置视图（ImageAcquisitionView）
                       IPluginConfigContextProvider    ← 设计期：接收宿主下发的上下文快照
                       ObservableObject                ← 它同时就是那个配置界面的 ViewModel
```

`ImageAcquisitionView.xaml.cs` 里只做两件事：`DataContext = plugin` 与 `plugin.Initialize(stepData)`
（注释口径与「ROI」插件相同：视图是纯绑定层，不需要额外 ViewModel、不需要 DI 容器）。
代价是"插件实例"活得和配置会话一样长，所以**所有非托管资源必须在它自己的 `Dispose()` 里收尾**。

`GetConfigView` 返回 `new ImageAcquisitionView(stepData, this)`；宿主在窗口挂好内容区之后调
`SetConfigContext(PluginConfigContext)` 把快照推进来（见「宿主契约」第二节）。

## 二、三份状态，各自只有一个真相源

| 状态 | 载体 | 角色 | 谁写它 | 谁读它 |
| --- | --- | --- | --- | --- |
| `FilePathPort` / `FolderPathPort` / `FileIndexPort` / `FrameTimeoutPort` | `InputPort<T>` | **数据流**，可被变量链接 | 引擎灌值（`ApplyConfigValues`，已链接端口跳过）、界面手填 | `ExecuteCore` 的四个分支 |
| `Mode` / `DisplayViewIndex` / `CameraSerial` | `[StepConfig]` 属性 | **纯配置**，随步骤落盘，永不连线 | 界面绑定、`ApplyConfigValues` | `ExecuteCore`、预览恢复、发布窗口号 |
| `PreviewImage` / `StatusMessage` / `FileCount` / `Push*` 表单 | 普通可绑定属性 | **纯界面**，只在配置窗口存活 | 载图任务、`SetStatus`、推送测试 | XAML |

输出口 `OutputImage`（`Image`）、`CurrentFilePath`（`CurrentPath`）、`CurrentFileIndex`（`CurrentIndex`）、
`TotalFiles`、`FrameId`、`ReceivedCount`、`SourceSerial` 是第四类：**每轮产出**，开轮清零、成功才写。

`StatusMessage` 只允许经 `SetStatus(message, level)` 写入——消息与级别（`StatusLevel.Info/Warning/Error`，
internal 枚举、不外露）必须成对更新，避免"红字配中性文案"这类界面自相矛盾。

## 三、执行主干

```
RunAlgorithm(context)
 ├─ 开轮清零：Success=false、ErrorMessage=""、OutputImage=null、CurrentPath=""、
 │            CurrentIndex=0、TotalFiles=0、FrameId=0、ReceivedCount=0、SourceSerial=""
 │   （基类的轮首自动回收只处理 IDisposable 的端口值，double/int/string 一律不管，
 │     所以这几个数值口必须自己归零，否则失败轮会把上一轮的索引/总数继续喂下游）
 ├─ ExecuteCore(Mode, FilePath, FolderPath, FileIndex, context) → CoreResult
 │    switch Mode:
 │      SingleFile → AcquireSingleFile(path)   : File.Exists 把关 → HImage.ReadImage
 │      Folder     → AcquireFromFolder(folder, index) : ListFolderImages → 夹取 → ReadImage
 │      Hub        → AcquireFromHub(context)   : ImageHub.TryPop → 没有则 WaitPop（阻塞）
 │      Camera     → AcquireFromCamera(context): context.Cameras.TryGetDeviceBySerial → WaitNextFrame
 │    default → Error = "未知的采集模式: {mode}"
 ├─ result.Cancelled → 只写 ErrorMessage（"已停止等待网络图像" / "已停止等待相机图像"），Success 保持 false，直接 return
 ├─ 成功 → 逐口写值 + Logger.Info + （DisplayViewIndex>0 时）TryPublishPreview
 └─ catch(Exception) → ErrorMessage = ex.Message + Logger.Error（不再上抛）
```

四个分支都只做"取到字节/取到文件路径"，**像素 → HImage 一律汇入同一个 `ToHImage`**（第五节）。

## 四、两条取图链路的语义差别（这是本插件最容易写错的地方）

| | 网络推送（`AcquireFromHub`） | 相机采集（`AcquireFromCamera`） |
| --- | --- | --- |
| 数据来源 | `ImageHub`（静态类，按 `context.CurrentFlowName` 分槽） | `context.Cameras` → 那台 `ICameraDevice` 的环形队列 |
| 快/慢路径 | `ImageHub.TryPop(flowName, out item)` 命中即返回；否则 `ImageHub.WaitPop(flowName, token, out item)` **阻塞等** | 直接 `device.WaitNextFrame(out frame, timeoutMs, ct)`，`timeoutMs<=0` 时取相机自身配置 |
| 一帧会被取几次 | 一次（槽是队列语义，但每槽只留最新一帧、上限 8 帧满丢最旧） | **一次**（消费语义；预览用 `TryGetLatest` 才是只读，本步骤绝不用它） |
| 取消 | `WaitPop` 随 `CancellationToken` 退出 → `Cancelled=true`，写"已停止等待网络图像" | `WaitNextFrame` 取消返回 false → 写"已停止等待相机图像" |
| 为什么这么设计 | 槽里没有图是**正常工作状态**（外力还没推）。若直接判失败，本步失败后下游每个吃图的步骤会各报一次"输入图像为空"，真正的根因被噪音盖住 | 相机是推模式，队列里可能有积压；**不做"重试等下一帧"**：这一帧没等到就是没等到，继续等只会让流程越来越滞后于生产 |

超时永远有限：`FrameTimeoutMs` 端口填 0 时取相机 `CameraSettings.FrameTimeoutMs`（默认 3000，非法值被基类归一）。

## 五、像素 → HImage：全仓唯一转换核心

`ToHImage(byte[] pixelData, int width, int height, int channels)`（private static，两个重载
`ToHImage(HubImageItem)` / `ToHImage(CameraFrame)` 只是拆字段）。守门断言直接按签名找它。

按顺序六道关口：

| 序 | 判据 | 结果 |
| --- | --- | --- |
| 1 | `width<=0 \|\| height<=0` | `ArgumentException("图像尺寸非法: WxH")` |
| 2 | `channels<1` | `ArgumentException("图像通道数非法: n")` |
| 3 | `channels==2` | **明确拒绝**："不支持的图像通道数: 2（仅支持 1=灰度 / 3=BGR / 4=BGRA；2 通道没有约定好的解释方式，直接按灰度读会把图读错半张）" |
| 4 | `expected = (long)width*height*channels > int.MaxValue` | "图像过大: …超过单幅图像上限"——必须用 long 算：int 乘法溢出成负数时，下一道"长度不足"校验反而会放行 |
| 5 | `pixelData.Length < expected` | "像素数据不足: N 字节，WxHxC 应为 M 字节（图像的宽高与像素字节数不自洽）"。**必须在进非托管世界之前拦下**：不足时 HALCON 会越界读那块缓冲，轻则图像错乱、重则进程崩掉 |
| 6 | `channels>3` | 逐像素抽掉 alpha，压成紧凑 BGR（`GenImageInterleaved` 按 3 字节/像素读 4 字节/像素的缓冲会**不报错但整幅错位花屏**） |

建图两步：

```csharp
var handle = GCHandle.Alloc(pixelData, GCHandleType.Pinned);
try {
    var pointer = handle.AddrOfPinnedObject();          // 不是 GCHandle.ToIntPtr()：那是"句柄令牌"，不是数据地址
    if (channels >= 3) image.GenImageInterleaved(pointer, "bgr", width, height, -1,
                                                   "byte", width, height, 0, 0, -1, 0);
    else               image.GenImage1("byte", width, height, pointer);
} finally { handle.Free(); }                            // HALCON 抛异常也必须解钉（钉住会阻止 GC 压缩堆）
```

- **为什么可以只钉到调用返回**：`gen_image1` / `gen_image_interleaved` 是**复制**语义
  （与 `gen_image1_extern` 的"引用 + 归还回调"相反），返回前像素已进 HALCON 自己的内存。
  于是省掉一次 `AllocHGlobal + 全帧 Copy`，而队列语义、溢出丢弃、基类轮首回收都不受影响。
- `alignment` 传 `-1` = 行间无填充，正好匹配宿主/客户端输出的紧凑 stride；`"byte"` = 8 bit。
- 外层 `catch { image.Dispose(); throw; }`：进过非托管世界的对象失败要自己收拾干净再上抛，
  否则连续失败会持续吃 HALCON 非托管内存（`AcquireSingleFile`/`AcquireFromFolder` 同一约定）。

## 六、图像所有权链：谁 new、谁 Dispose

```
字节来源（HTTP 客户端 / 相机 SDK 回调线程）
   → byte[]（宿主解码或驱动拷贝的独立数组，与 HALCON 无关）
   → ToHImage() 新建 HImage                        ← **唯一 new 点**
   → 写入 OutputImage（Image 端口）                ← 端口成为持有者
   → this.PublishPreview(image, viewIndex)          ← 只传引用，不转移所有权
   → 下一轮 RunAlgorithm 之前，基类 AutoDisposeRoundOutputs 释放上一轮的端口值
     （透传防御：若某输出与某输入的上游链接同引用，则跳过不杀）
   → 本实例 Dispose()：PreviewImage=null（setter 释放旧图）+ base.Dispose()（回收输出端口值）
```

三条纪律：

1. **插件不 `Dispose` 别人给的图**。文件模式读的图是自己 new 的，可回收；相机/Hub 模式建出的图也是自己 new 的。
2. **预览图只由 `PreviewImage` 的 setter 释放**（换图即弃旧，`ReferenceEquals` 同值直接返回）。
   `Dispose()` 里一律走 `PreviewImage = null`，不手动 `Dispose`，避免重复释放。
3. **输出口不做"手动挑着释放"**：本插件的 `Dispose()` 只有 `PreviewImage = null; base.Dispose();`。
   手动挑释放的话，以后新增的图像类输出口一定会漏掉那一份。

在途读图的两个守卫：`_previewLoadId`（每次发起载图 +1，后台回来时对不上号就是过期结果，直接丢弃；
否则用户连点刷新/快速翻页会"后完成的旧图覆盖新图"）与 `_disposed`（`volatile`，关闭对话框后在途任务
不得往已释放的绑定上写图，只释放不赋值）。两者都只在 UI 线程读写。

## 七、目录列举与自然序

`ListFolderImages(folderPath, extensions)`（private static）+ `NaturalOrderComparer`（private sealed）。

- 扩展名白名单常量 `ImageExtensions = ".bmp,.jpg,.jpeg,.png,.tif,.tiff"` 是**单一真相源**：
  执行核心（每轮重新枚举）、预览（配置态列目录）、浏览框过滤都从它派生，三处必须一致。
- 排序用自然序（`img1 < img2 < IMG3 < img10`），`StringComparer.Ordinal` 逐字符比较、数字段按数值比较，
  **刻意不做区域/文化敏感比较**：同一台设备上排序必须永远稳定，否则"第几张"会随系统区域设置漂移。
- **每轮重新枚举、不缓存目录**：支持"拍图落盘"式工作目录。
- 索引越界 → 夹取 + 文案留痕（"（索引 {req} 超出 0~{max}，已夹取）"），不失败。

## 八、装载与投递：为什么 `Modules\` 里只能有插件 DLL

本插件由 `PluginService` 用 `Assembly.LoadFrom` 装进**默认 AssemblyLoadContext**，
它引用的 `Core.Interfaces` 命中宿主已加载的那一份（同标识 → 同一程序集实例），
于是插件与宿主共用同一份静态状态——`ImageHub` 敢用静态类正是靠这一点（投递方在宿主、取走方在插件）。

**红线②就断在这里**：一旦有人把 `Core.Interfaces.dll` / `Core.Controls.dll` 拷进 `Modules\`，
加载时会得到**两份** Core.Interfaces 类型（宿主那份 + `Modules\` 那份），
两边的 `ImageHub` 是两份互不相干的静态字典，症状就是本仓真出过的**"图投进去取不出来"**
（宿主 `HttpImageServer.Push` 进 A 份，插件 `TryPop` 从 B 份取，永远为空 → 流程一直卡在等待）。
同类症状还有事件总线/预览捕获钩子失效。`Modules\` 只放插件 DLL 与厂商 SDK 的托管包装器。

**红线①③**：插件工程必须留在 `Plugins\` 下（`Directory.Build.targets` 按目录把 DLL 投递到 `Modules\`），
改了源码必须重建插件工程；`Modules\` 不由宿主工程带出，只重建宿主跑的还是旧 DLL。
手册改动还要核 DLL 真的带了资源（增量构建会静默漏打包）：

```powershell
tr '\0' '\n' < Modules\Plugin.ImageAcquisition.dll | grep -a "\.Help\."
```

资源名数量必须等于 `Help\` 下的 .md 数量（本册为 8 页）。

## 九、配置态校验的三条入口

| 方法 | 触发 | 口径 |
| --- | --- | --- |
| `ValidatePathInputs()` | `FilePathPort`/`FolderPathPort` 的 `ValueChanged`（手填、链接、解链接都会触发） | **只校验、不顺手载入预览**——载图是显式动作（浏览/刷新/翻页），混进事件里会和它们的显式调用重复读盘 |
| `CheckPath(port, emptyHint, exists)` | 上面那条 | 端口**已被链接时静默通过**（值运行期才有），空 → 橙字，非空但不存在 → 红字 |
| `ValidateCameraSelection()` | `ApplyCameraOptions` / 相机页刷新 | 按 `CameraOption.State` 分级给文案：`Streaming` 无提示、`Online`/`Connecting` 橙、`Closed` 红；下拉里没有这台相机时给"本步骤引用的相机「{serial}」不在当前方案的相机列表里…" |

`OnModeChanged` 钩子里 `ClearPreview()` + `ValidatePathInputs()`，且仅当
`value == AcquisitionMode.Folder && !_loadingConfig` 才 `RefreshFolderFiles()`——
`_loadingConfig` 压住灌值期的钩子副作用（见「宿主契约」第一节）。
