# 图像采集 · 开发：不变量与陷阱清单

> 分类：常用工具 ｜ 来源：插件 Plugin.ImageAcquisition ｜ 读者：开发

**改这个插件前逐条对照**：每条都是"必须成立的事实 + 不成立时的症状 + 现在由谁守"。
分两类：**A 类违反后立刻报错**（好查），**B 类违反后静默出错**（难查，历史上真踩过）。

守门断言在 `FlowCanvasChecks\ImageAcquisitionChecks.cs`（22 条，分节：装载与构造 / 端口面 /
`DisplayViewIndex` 夹取 / `ToHImage` 建图与护栏 / 目录列举与自然序）；
端到端的 Hub 链路由 `FlowCanvasChecks\HttpImageSmoke.cs`（S1~S14）守。
下面每条给的断言名都是这两个文件里的**原话**，没有断言的条目已注明"无断言，靠人工核对"。

---

## A 组：违反会当场红或抛

**A1 端口名是契约，不是实现细节**
输入侧 `FilePath` / `FolderPath` / `FileIndex` / `FrameTimeoutMs`，输出侧 `Image` / `CurrentPath` /
`CurrentIndex` / `TotalFiles` / `FrameId` / `ReceivedCount` / `SourceSerial`。属性名与端口名故意不同
（`OutputImage` → `Image`）。
症状：改名后老方案里的连线与 `InputValues` 键全部失配，表现为"接线凭空消失、步骤读不到上游值"，编译期一声不响。
守门：`输入端口未被改名（FilePath / FolderPath / FileIndex / FrameTimeoutMs）`、
`旧输出端口未被改名（Image / CurrentPath / CurrentIndex / TotalFiles）`、
`输出端口含 FrameId / ReceivedCount（相机模式精确帧号，不靠语义重载）`、
`输出端口含 SourceSerial（多相机身份；相机模式=配置序列号，其余模式为空）`。

**A2 构造期绝不碰 HALCON 原生库**
`PreviewImage` 初值**故意是 null** 而不是 `new HImage()`；构造函数只建命令与订阅端口 `ValueChanged`。
症状：`PluginService` 在启动扫描端口时就要实例化本类型，无 HALCON 的机器上一个从不使用的空预览图
会把整个插件 DLL 判成"加载失败"，与"无 HALCON 时流程编排照常可用、只降级视觉功能"的既定口径冲突。
守门：`插件可实例化（构造期不碰 HALCON 原生库）`、`构造后 PreviewImage 为 null（未在构造期创建 HImage）`、
`插件程序集可装载`、`插件类型存在且实现 IVisionPlugin`。

**A3 `ToHImage` 是全仓唯一的像素入口**
网络推送与相机采集两条链路都只能走它（两个私有重载只拆字段）。
症状（分两类，都是静默的）：绕过它另写一份 → 彩色图**红蓝互换**或整幅错位花屏，而灰度图完全看不出问题，
只表现为颜色判别类算子莫名不对；签名被改 → 断言找不到它。
守门：`ToHImage(byte[],width,height,channels) 存在`、
`灰度 4x3x1 → 单通道、尺寸与行列不转置`、
`BGR 2x1x3 → 三通道；纯红/纯蓝落在 R/B 通道（不反色、不串像素）`、
`BGRA 2x1x4 → 抽出 alpha 压成三通道 BGR（不是按 3 字节/像素错位读）`。

**A4 三道进非托管世界前的护栏**
尺寸/通道非法、`expected` 用 long 计算、`pixelData.Length < expected`——全部 `throw ArgumentException`，
外层 `catch { image.Dispose(); throw; }`。
症状：长度不足没拦住 → HALCON 越界读那块缓冲，轻则图像错乱、重则进程崩掉；
用 int 算乘积 → 溢出成负数时"长度不足"这道校验**反而放行**，最后以"分配负数长度"的怪错抛出；
`channels==2` 被当灰度读 → 静默读出半张错图，没有任何报错。
守门：`像素数据不足 → ArgumentException（绝不进非托管世界越界读）`、
`2 通道 → ArgumentException（没有约定好的解释方式，不许静默按灰度读）`、
`超大尺寸 → ArgumentException（long 计算，int 溢出不会反放行）`。

**A5 `GCHandle` 必须 `AddrOfPinnedObject()` 且 `finally Free()`**
`GCHandle.ToIntPtr()` 返回的是"句柄令牌"（供 `GCHandle.FromIntPtr` 还原用），**不是**被钉住对象的数据地址；
`GCHandle` 是结构体、不实现 `IDisposable`，所以用 `try/finally` 而不是 `using`。
症状：用 `ToIntPtr` → HALCON 拿到一个令牌当像素指针，图像内容是垃圾（或立刻崩）；漏 `Free` → 钉住的数组阻止 GC 压缩堆。
守门：`BGR 2x1x3 → 三通道…` 与 `BGRA 2x1x4 → …`（拿错地址时这两条必红）。**`finally` 分支本身无断言，靠人工核对。**

**A6 开轮清零必须连数值口与身份口一起清**
`OutputImage` / `CurrentPath` / `CurrentIndex` / `TotalFiles` / `FrameId` / `ReceivedCount` / `SourceSerial` 全部先归零。
症状：基类 `AutoDisposeRoundOutputs` **只回收 IDisposable 的端口值**，int/long/string 一概不管——
漏一条就是失败轮把上一轮的索引/总数/序列号继续喂给下游，"时对时错"最难查；
漏 `SourceSerial` 则失败轮下游仍读到上一台的序列号，身份核对形同虚设。
守门：`输出端口含 SourceSerial（多相机身份；相机模式=配置序列号，其余模式为空）`（写值路径被覆盖）；
**"失败轮清零"这条无独立断言，靠人工核对**（改 `RunAlgorithm` 开头时逐口点数）。

**A7 取消与业务失败必须分开收尾**
`result.Cancelled` → 只写 `ErrorMessage`（"已停止等待网络图像" / "已停止等待相机图像"）、`Success` 保持 false、直接 return。
症状：把取消判成成功 → 运行记录里出现"这一件检测通过"的假记录；把取消当失败上抛 → 插件越权决定流程去向。
守门：`S13b 停止打断等待后本步按「未成功」收尾`、`S13b 插件打了一条「已停止等待网络图像」的 Warn`、
`S13b 会话正常收尾（Stopped，不是 Faulted）`、`S13b 停止后槽仍空（没被凭空塞进一帧）`。

**A8 `DisplayViewIndex` 必须"写入即夹取"，不是"用时再夹"**
`partial void OnDisplayViewIndexChanging(ref int value) => value = Math.Clamp(value, 0, 9);`
症状：只在发布时夹 → 方案文件里存的还是 10/负数，界面回显与运行行为分叉；写成 `On…Changed` → 越界值已落进字段。
守门：`DisplayViewIndex 属性存在`、`DisplayViewIndex 写入即夹取到 0~9`。

**A9 目录列举三处必须同源**
`ImageExtensions` 常量（`.bmp,.jpg,.jpeg,.png,.tif,.tiff`）同时供执行核心、预览列表、浏览框 `Filter`；
`ListFolderImages` + `NaturalOrderComparer` 是 private static / private sealed。
症状：白名单各写一份 → 预览里能看到、运行期取不到（或反之）；换成 `StringComparer.CurrentCulture`
或纯 ordinal 字符串序 → `img10` 排到 `img2` 前面，"第几张"随系统区域设置漂移，**排序错 = 工件顺序错**。
守门：`ListFolderImages 存在`、`扩展名过滤：只收图像、忽略 .txt`、
`自然序：img1 < img2 < IMG3 < img10（纯字符串序会把 10 排到 2 前面）`。

---

## B 组：违反后**静默**出错（历史上真的发生过）

**B1 `Modules\` 里混进公共契约程序集 = "图投进去取不出来"**
机理：插件由 `PluginService` 以 `Assembly.LoadFrom` 装进**默认 AssemblyLoadContext**，它引用的
`Core.Interfaces` 命中宿主已加载的那一份，于是两边的 `ImageHub` 是**同一个静态类型**——这是投递方能被取走方看到的前提。
一旦 `Modules\` 里有一份 `Core.Interfaces.dll`，就是两份静态状态：宿主 `Push` 进 A 份、插件 `TryPop` 从 B 份取，
永远为空，流程静静卡在"槽内暂无图像，开始等待网络推送…"。同类症状还有预览事件收不到、捕获钩子失效。
守门：`插件 DLL 在 Modules 目录里`（正向：产物必须在位）；`S2 宿主与插件指向同一份 Core.Interfaces`（端到端反证）。
**"不许往 `Modules\` 拷契约 DLL"这条本身无断言，靠人工核对**（红线②）。

**B2 改了插件源码不重建插件工程 = 跑的还是旧 DLL**
`Modules\` 不由宿主工程带出。症状：源码改了、界面无变化、日志里仍是旧文案（红线③、R42）。
手册改动同理：`Help\*.md` 是新挂上的内嵌资源，**增量构建会静默漏打包**，只看构建退出码不算数。
守门：判据是 `tr '\0' '\n' < Modules\Plugin.ImageAcquisition.dll | grep -a "\.Help\."` 的**资源名数 == `Help\` 下 .md 数**。
手册本身的静态红线在 `HelpManualCheck`：HM12（开发页必须叫 `NN-dev-xxx.md`）、HM12b（手册最多一层目录，
另立 `Help\dev\` 会把用户页整批静默挤掉）、HM13（L 档开发章节合计 ≥300 行）、HM9（不许占位标记）、HM7（编码纪律）。

**B3 `_loadingConfig` 这个刹车不许去掉**
`ApplyConfigValues` 置位期间 `OnModeChanged` 不刷目录/预览。
症状：① 配置态同一次灌值刷两遍（`Initialize` 末尾还要恢复一次）；
② 运行实例（`FlowCompiler` 编译出来的那份，**没有界面**）在灌值时枚举目录、解码首图、并持有一份预览 `HImage`——
全是白做，还多一份非托管内存跟着实例走。
守门：`S13a 采集步骤进入等待（插件打出等待 Info）`、`S13a 推入一帧后流程继续并成功` 覆盖了运行实例的取图路径；
**灌值期副作用本身无断言，靠人工核对**。

**B4 `_previewLoadId` / `_disposed` 两个守卫管的是"在途读图"**
载图是异步的：每次发起 `++_previewLoadId`，回来时对不上号就丢弃（只释放不赋值）；`Dispose()` 里
先 `_disposed = true; _previewLoadId++;` 再 `PreviewImage = null; base.Dispose();`；
`Initialize` 开头必须 `_disposed = false`。
症状：去掉轮次号 → 用户连点刷新/快速翻页时"后完成的旧图覆盖新图"，预览与"当前文件"文字不一致；
去掉 `_disposed` → 对话框已关，任务回来往已释放的绑定上写图（`HImage.Dispose` 之后再访问）；
忘了在 `Initialize` 复位 → 第二次打开配置窗口时**所有预览都被"已释放"守卫误杀**，界面永远空白。
守门：`构造后 PreviewImage 为 null…`（构造期那一半）；**在途读图三条全部无断言，靠人工核对**。

**B5 `PublishPreview` 传的是引用，且必须"先捕获后派发"**
本插件只负责发；需要留存帧的是宿主（`ImagePreviewCaptureHub.Current`，在发布线程同步拷走）。
症状：改成异步到 UI 线程再拷 → 图已被释放：`IsInitialized()` 为 false 时静默丢弃（画布空白、无日志），
释放到一半则 HALCON `#4060 access to deleted region in copy_image`。现场表现"单次运行正常、循环一开图不会被收纳"。
守门：`S12 流程结束后 Hub 槽已排空`（跑完即回收的前提）；显示链路本身由 HTTP 冒烟覆盖，
**"捕获时机"无断言，靠人工核对**（改 `Core.Controls\Events\PluginPreviewExtensions.cs` 必须连带核 `ImageCollectionService`）。

**B6 推图失败绝不能翻成采集失败**
`DisplayViewIndex > 0` 才发、`TryPublishPreview` 把异常吞成一条 Warn。
症状：去掉 try/catch → 在无 UI 的宿主/测试进程里一次成功的采集被判异常，产线因为"显示问题"停线；
去掉 `>0` 守卫 → "不显示"照样抢占窗口。
守门：**无断言，靠人工核对**（`S8 插件自报 Success=true` 只覆盖 HTTP 主路径的成功判定）。

**B7 Hub 慢路径"槽空即等待"是刻意的，不许退回"槽空即失败"**
症状（历史 bug 的反向）：槽空时判失败 → 本步失败，下游每个吃图的步骤再各报一次"输入图像为空"，
真正的根因（客户端根本没推）被噪音盖住；
回退旧图 → 产出"长得像成功"的错结果，比失败危险得多。
守门：`S13a 起始槽为空（保证走到等待分支）`、`S13a 槽空期间流程确实被卡在采集步骤（未完成）`、
`S13a 推入一帧后流程继续并成功`、`S10 无来源名 → CurrentPath = hub://流程名（证明走的是网络槽通路）`、
`S8 CurrentPath 回传 X-Image-Name`。

**B8 相机模式消费帧，不许改用 `TryGetLatest`**
`WaitNextFrame` = 出队（每帧只被取一次）；`TryGetLatest` = Peek（预览专用）。
症状：改用 Peek → 同一帧被连续两轮检成"两件工件"，且队列越积越满、溢出计数虚高；
在两个采集步骤里选同一台相机 → 帧被**分抢**（一帧只会被消费它的那一个取走），表现为随机漏检。
守门：**无断言，靠人工核对**（帧语义由 `CameraDeviceBase` 的契约文档与 `HikvisionChecks` 的状态机断言守住，
本插件侧只核"调用的是 `WaitNextFrame`"）。

**B9 等图超时必须是有限值**
`FrameTimeoutPort` 填 0 时取相机配置的超时；两侧都会被归一（`CameraSettings` 归一化保证 `FrameTimeoutMs ≥ 1`）。
症状：无超时的等图会让流程线程永久卡死，现场只能杀进程。
守门：**无断言，靠人工核对**（本插件只保证"端口 0 → 取相机配置"，归一在 `CameraSettings.Normalized()`）。

**B10 `SourceSerial` 与 `CameraSerial` 是两条不同来源的值**
成功时 `SourceSerial = Mode==Camera ? CameraSerial.Trim() : ""`；失败轮为空。
症状：把 `SourceSerial` 写成"上游给的序列号" → 与「标定」的身份核对变成"自己跟自己对账"，
换相机不再失败（静默用错标定）；漏掉 Trim → 方案里带空格的序列号与上游对不上。
守门：`输出端口含 SourceSerial（多相机身份；相机模式=配置序列号，其余模式为空）`（值口径写进断言名）。

**B11 索引越界的"夹取 + 留痕"不要改成"失败"或"静默夹取"**
症状：改成失败 → 批处理跑到最后一个就红；改回静默 → 表现为"同一张产品被反复检测"，现场极难归因。
守门：**无断言，靠人工核对**（`自然序：…` 只保证顺序，不保证夹取留痕；改文案会连带影响排错的"照抄搜索"）。

**B12 信息栏对"已链接端口"必须中性放行**
`CheckPath(port, emptyHint, exists)` 在端口已链接时不做存在性校验。
症状：照原逻辑报"请选择…" → 用户以为链接失效；配置态强行预览 → 拿运行期的值去读盘，必然文不对题。
守门：**无断言，靠人工核对**。

**B13 链接端口时不能"顺手把值写进 `InputValues`"**
基类 `OnConfirm` 对已链接端口执行 `RemoveInputValue`。
症状：手抄一份覆盖写回 → 解链接后残留脏值，运行期用的是残留而不是上游输出。
守门：**无断言，靠人工核对**（本插件不重写 `OnConfirm`，所以这条只在"想加自定义确认逻辑"时才可能违反）。

---

## 改这个插件之后的必做动作

| 动作 | 判据 |
| --- | --- |
| 重建插件工程（改了基类/生成器则全量） | 只重建宿主跑的是旧 DLL（红线③、R16） |
| 核 DLL 真的带了 `Help` 资源 | 资源名数 == `Help\` 下 .md 数（本册 8 页；R42，增量构建会静默漏打包） |
| 跑 gate | `ImageAcquisitionChecks`（22）+ `HttpImageSmoke`（S1~S14）+ `HelpManualCheck`（HM0~HM16，红线在 HM12/HM12b/HM13） |
| 端口名反查 | 手册里每个端口名都要能在源码里找到同名字符串；改名要同时改手册与方案兼容口径 |
| 改显示链路时连带核宿主 | `Core.Controls\Events\*` 与 `ImageCollectionService` 一起看（B5） |
