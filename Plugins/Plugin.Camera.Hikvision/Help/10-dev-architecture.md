# 海康相机 · 开发：架构、句柄生命周期与 SDK 码值归属

> 分类：相机驱动 ｜ 来源：插件 Plugin.Camera.Hikvision ｜ 读者：开发

S/M 档的开发页只讲"用户页不讲的那一层"：状态存在哪、句柄归谁、回调与缓冲什么时候归还、
哪个数值是谁家的码。怎么配、怎么排错见用户册，这里不重复。

## 一、分层位置

| 层 | 位置 | 职责 |
| --- | --- | --- |
| 契约层 | `Shard\Core.Interfaces\Camera\`（`ICameraDevice` / `CameraDeviceBase` / `CameraDescriptor` / `CameraSettings` / `CameraFrame` / `CameraTypes`） | 环形队列 + 溢出计数、状态机、心跳看门狗、取图口（消费 / 只读）全部固化在基类 |
| 驱动层 | 本工程 `HikvisionCameraDevice.cs` | 只写海康的差异点：枚举、按序列号命中、开/关设备、起停采流、下发曝光增益、把 SDK 帧解成像素 |
| 绑定层 | 本工程 `MvsRuntime.cs` | 对 `MvCameraControl.Net.dll` 的**反射**包装，全仓唯一知道 SDK 成员名的地方 |
| 宿主层 | `VisionMaster\Modules\CameraModule.cs`、`Services\Camera\`、相机设置视图模型 | 扫描驱动、按序列号寻址、注入 `Log`、落盘 `CameraDescriptor`、消费帧建 `HImage`（在「图像采集」里） |

驱动编译期**不引用**海康 DLL：`Plugin.Camera.Hikvision.csproj` 只 `ProjectReference` 契约层。
因此"没装 MVS 的机器"上插件照常加载、状态机与队列照常可验证，失败点被推迟到 `Open`。

## 二、`MvsRuntime`：绑定面与它的三个隐性约定

```
Lazy<MvsRuntime?>(_instance, ExecutionAndPublication)
  → TryLoadCore：先看进程里是否已有 MvCameraControl.Net（宿主可能先装进默认 ALC）
                 → 再按 CandidatePaths() 逐个 Assembly.LoadFrom，第一个 Bind 成功者胜出
  → Bind：asm.GetType("MvCamCtrl.NET.MyCamera") 为 null 就算这份不对（记进 errors）
```

| # | 约定 | 后果 |
| --- | --- | --- |
| 1 | **一次进程一次探测**（`Lazy` 且线程安全模式带异常发布） | 探测结果与失败原因（静态 `LoadError`）全局共享。运行中途把 DLL 拷进 `Modules\` 也**不会**被重新发现——必须重启软件 |
| 2 | `CandidatePaths()` 顺序固定：程序目录 → 程序目录的 `Modules` → 逐级向上最多 **6 层**的 `Modules` → 环境变量 `MVCAM_SDK_PATH` 的 `Development\DotNet\` | "向上六层"是为了让 `bin\Debug\net9.0-windows` 之类的深输出目录也能命中仓库根的 `Modules\`。改这个顺序会改变"两份 DLL 同时存在时哪份生效" |
| 3 | `Call(name, args)` 用 **方法名 + 参数个数** 定位重载（`GetMethods().FirstOrDefault`），结果按 `is int` 取错误码，非 int 一律当 **-1** | 海康若新增同名同参数个数的重载，这里可能挑错一个；返回值不是 `int` 的封装（返回 `void` 或枚举）会被静默当成 -1 失败。**新增绑定成员时优先挑参数个数唯一的签名** |
| 4 | 成员缺失抛 `MissingFieldException` / `MissingMemberException` / `MissingMethodException`，文案统一带「MvCameraControl.Net.dll 版本不匹配」 | 现场据此一眼分清"没装 SDK"与"装的版本不对" |
| 5 | 结构体一律以**装箱 object** 传递，`GetField` / `SetField` 走反射读写；`pDeviceInfo` 兼容 `IntPtr[]` 与"基址 + 步长 `IntPtr.Size`"两种形态；文本字段兼容 `byte[]`（按 ASCII 截到首个 0）与 `string` | 版本间结构体包装形态的差异都在这里兜住；`FrameOut` 这个 box 每线程建一次、逐帧复用，不是每帧 new |

## 三、句柄与生命周期时间线（`_camera` 就是一个 `MyCamera` 实例，句柄收在它内部）

| 阶段 | SDK 调用 | 失败时的处置 |
| --- | --- | --- |
| 打开 | `MV_CC_CreateDevice_NET` → `MV_CC_EnumDevices_NET`（GigE 与 USB3 两类一次枚举） → 按序列号命中 → `MV_CC_CreateDevice_NET(deviceInfo)` → `MV_CC_OpenDevice_NET` | 枚举失败 / 未命中 / CreateDevice 非 0 都**直接抛异常**；`OpenDevice` 非 0 时**先 `MV_CC_DestroyDevice_NET` 再抛**（不留半开的设备） |
| 打开成功后 | `ApplyTriggerSettingsCore(ReadSettings())` → `ApplyExposureGainCore(...)` | 触发下发失败**阻断打开**并 `CloseCore()` 关句柄（不能静默连续采图）；曝光/增益失败只记 Warn，不阻断打开 |
| 采流 | `MV_CC_StartGrabbing_NET` → 拉起后台线程 `HikGrab-序列号`（`IsBackground = true`） | `StartGrabbing` 非 0 → 抛 → 基类把状态退回 `Online` 并写"启动采流失败：{异常类型}: {消息}"（于是界面出现**两层前缀**，属预期文案形状） |
| 取图循环 | `MV_CC_GetImageBuffer_NET(frameOut, 100)` → 读字段 → `MV_CC_ConvertPixelType_NET` → `Marshal.Copy` → `PushFrame` → **finally** `MV_CC_FreeImageBuffer_NET(frameOut)` | `ret != 0`（含超时）直接 `continue`，回头先判停止标志；单帧异常只记 Warn，**绝不带垮线程**；归还在 `finally`，异常路径也必须归还 |
| 停流 | `_grabbing = false` → `Join(2000)` → `MV_CC_StopGrabbing_NET` | 线程不响应 Join 超时也照走停流调用（托管线程不能强杀）；此时线程若还活着，会对着已停流的设备取图，`ret != 0` 被 `continue` 吃掉 |
| 关闭 | `_grabbing = false` → `Join(2000)` → `UnpinConvertBuffer()` → 锁内 `StopGrabbing` → `CloseDevice` → `DestroyDevice` → `_camera = null` | 每一步都**不判返回值、每步都可能失败**（关闭路径要吞，否则基类把一次正常断开渲染成"关闭设备时异常"） |
| 释放 | 基类 `Dispose` → 置 `_disposed`、停看门狗、关收帧闸门、状态写"已释放" → 调 `CloseCore`（异常吞掉）→ 清队列 → `_signal.Dispose()` | `Dispose` 幂等；释放路径的异常不再上抛 |

**为什么失败一律抛异常、不许用 `SetDetail`**：基类 `Open()` 的收尾会用 `$"打开失败：{异常类型}: {消息}"`
**覆盖**状态说明，`SetDetail` 写进去的文字会被顶掉。这是本驱动与基类之间最容易踩的一条约定。

## 四、锁与线程模型

| 资源 | 保护方式 | 备注 |
| --- | --- | --- |
| `_camera` + `_sdk` 的创建与销毁 | `_sdkGate`（`lock`） | 罩 `OpenCore` / `CloseCore` / `TriggerSoftware`；`StartStreamCore` / `StopStreamCore` / `ApplySettingsCore` 不直接加这把锁（`ApplySettingsCore` 在采流中会先停流再改触发） |
| 基类状态机 | 基类自己的 `_gate`；`OpenCore` / `StartStreamCore` **在基类锁外**被调用 | 刻意为之：真机打开设备可能几百毫秒，持锁调用会把 `State` 之类只读查询全堵死 |
| 取图线程 | `volatile bool _grabbing` + `Thread.Join(2000)` | 停止标志最多滞后一个 100 ms 取图周期 |
| `_convertBuffer` / `_convertPin` | **无锁**，前提是"只有取图线程会碰它"（源码注释原话） | 若将来允许多线程共用一台设备或并行取图，这片缓冲就是竞态源 |

## 五、图像缓冲的三块内存与归属

| 内存 | 谁分配 | 谁释放 | 硬约束 |
| --- | --- | --- | --- |
| SDK 内部帧缓冲（`pBufData`） | MVS 驱动 | 驱动调 `MV_CC_FreeImageBuffer_NET`，且在 `finally` 里 | 早退/异常路径漏归还 = SDK 收图缓冲被吃干，表现为"越跑越不出图" |
| `_convertBuffer`（转换目标） | 取图线程按需扩容，`GCHandle.Alloc(..., Pinned)` | `UnpinConvertBuffer()`，只在 `CloseCore` 里 | **停止采流不 unpin**（下次起流复用）；`EnsureConvertBuffer` 扩容时先 `Free` 旧句柄再 `Alloc`，漏一步就是句柄泄漏 + SDK 写进已回收内存 |
| 入队的 `byte[]` | `new byte[need]` + `Marshal.Copy` | 消费者取走、队列清空时随帧对象一起回收 | **必须是独立拷贝**：直接把钉住缓冲的引用交给 `PushFrame`，下一帧会覆写已入队内容——表现为"取到的图是上一件工件的"，且不报错 |

`PushFrame`（基类）负责入队前的 `IsValid` 校验、回填 `CameraId` / `SerialNo` / `FrameId`（自增）、
`Timestamp`（默认值补 `DateTime.Now`）、溢出裁剪与唤醒等待者；驱动侧只负责"造一个合法的帧"。

## 六、码值归属：谁的枚举、谁的节点、谁的数值

**MVS SDK / GenICam 的码值 ≠ 本仓 C# 枚举值。** 本驱动优先**按名字**向 SDK 取值；
`TriggerMode` 的 0/1 是 GenICam 标准节点值，由 `HikvisionTriggerPlan` 显式映射，业务代码里不散落厂商数值。

| 名字 | 属于谁 | 本仓怎么拿 | 注意 |
| --- | --- | --- | --- |
| `MV_GIGE_DEVICE` / `MV_USB_DEVICE` | MVS SDK 常量（`MyCamera` 上的 `public` 字段，传输层接口类型） | `Const("…")` 反射现取，返回 `uint` | 枚举入参是两个常量**按位或**；命中判据是 `nTLayerType == Const(…)`（**相等比较，不做位与**） |
| `chSerialNumber` | 设备信息子结构里的文本字段 | `SpecialInfo` → `stGigEInfo` / `stUsb3VInfo` 的 `byte[]` 再 `Marshal.PtrToStructure` 解一遍 | 包装器把子结构存成 `byte[]`，不解这层就永远读不到序列号（表现为"枚举 N 台但找不到"） |
| `MvGvspPixelType` 的 `PixelType_Gvsp_Mono8` / `PixelType_Gvsp_RGB8_Packed` | SDK 嵌套枚举成员 | `PixelType("…")` 走 `Enum.Parse`，反查名字走 `Enum.GetName` | 像素格式**分类只看名字**（`Contains("Mono")` 等），与枚举数值无关；新机型只要命名守 GenICam 约定就自动可用 |
| `ExposureTime` / `Gain` | **GenICam 节点名**（相机侧） | `MV_CC_SetFloatValue_NET(节点名, float)` | 节点名逐字与 MVS 客户端一致；机型不同节点名可能不同（如增益分 `Gain`/`GainRaw`/`GainAnalog`），本驱动只认 `Gain` |
| `TriggerMode` 的 `0` / `1` | **GenICam/MVS 侧**节点值 = 触发关闭 / 触发开启 | `MV_CC_SetEnumValue_NET("TriggerMode", 0u/1u)`，由 `HikvisionTriggerPlan` 决定 | ⚠ 与本仓 `CameraTriggerMode.Software = 0` **数值相同、语义无关**；禁止按数值直接透传 |
| `TriggerSource` / `TriggerActivation` | **GenICam/MVS 侧**枚举名（`Software` / `Line0~Line3`、`RisingEdge` / `FallingEdge`） | 优先 `MV_CC_SetEnumValueByString_NET`；老版本 SDK 用 `EnumValue(...)` 取数值兜底 | 按名字下发，代码里不写厂商数值 |
| `CameraTriggerMode`（`Software = 0` / `RisingEdge = 1` / `FallingEdge = 2` / `Continuous = 3`） | **本仓 C# 枚举**（`CameraTypes.cs`） | `HikvisionTriggerPlan.From(settings)` 映射成节点值 | 驱动**读取它**；成员只许追加，旧值不变 |
| `MV_CC_DEVICE_INFO_LIST.nDeviceNum` / `pDeviceInfo` | SDK 结构体字段 | `DeviceCount` / `FindDeviceBySerial` | 列表形态两种（`IntPtr[]` / 基址+步长）都兜，第三种形态直接 `break` → 上层报"未找到" |
| 各 `MV_CC_*_NET` 的返回值 | MVS 错误码（`int`） | `$"0x{ret:X8}"` 打进文案 | 非 0 即失败；`Call` 拿不到 int 返回值时按 -1 处理（文案会显示 `0xFFFFFFFF`，看到它就该怀疑封装返回值类型变了） |

**未使用的 SDK 能力（当前实现的边界，别当缺陷查）**：没有注册任何 SDK 回调（设备上下线 / 异常），
所以 `NotifyDeviceLost` 这条基类通道在本驱动里**从未被调用**；没有下发包长 / GigE 心跳时间；
软触发命令已实现（`MV_CC_SetCommandValue_NET("TriggerSoftware")`）；
`HeartbeatTimeoutMs` 因 `RequiresHeartbeat => false` 根本不参与（看门狗不会被创建）。

## 七、差异点清单（本驱动相对基类改了哪些行为钩子）

| 钩子 | 本驱动的取值/实现 | 为什么 |
| --- | --- | --- |
| `OpenCore` / `StartStreamCore` / `StopStreamCore` / `CloseCore` / `ApplySettingsCore` | 见第三节 | SDK 侧的全部动作都在这里 |
| `RequiresHeartbeat` | `false` | 真机不需要靠"多久没消息"推断——但注意本驱动也没订阅掉线回调，所以掉线既不看门狗报也不回调报 |
| `StateAfterOpen` | 默认（`Online`） | 打开成功就是"已连接、未采流"，与网络相机必须落 `Connecting` 相反 |
| `OpenSuccessDetail` | 默认（"已连接，等待开始采流"） | 同上 |
