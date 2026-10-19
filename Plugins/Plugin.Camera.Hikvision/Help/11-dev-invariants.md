# 海康相机 · 开发：不变量与陷阱清单

> 分类：相机驱动 ｜ 来源：插件 Plugin.Camera.Hikvision ｜ 读者：开发

两组：A 组"违反就编译失败 / 闸门红"，B 组"违反不报错、只出诡异现象"。每条一律写成
**症状 → 机理 → 守门断言名**。断言名取自真实文件 `FlowCanvasChecks\HikvisionChecks.cs`（14 条，
段名 `[HK] 海康相机驱动：发现 → 构造 → 缺 SDK 优雅降级 → 像素格式分类`）、`FlowCanvasChecks\HelpManualCheck.cs`、
`FlowCanvasChecks\ImageAcquisitionChecks.cs`；**查不到对应断言的条目已标明"无断言，靠人工核对"，不许当已守**。

## A 组：违反就编译不过 / 闸门红

| 症状 | 机理 | 守门断言名 |
| --- | --- | --- |
| 相机设置的驱动列表里没有「海康相机」，但没有任何报错 | 类上缺 `[Display]` 或 `Name` 不等于「海康相机」——宿主扫描 `Modules\` 时**静默跳过**没有 `[Display]` 的类型 | `驱动带 [Display]（没有它 PluginService 会静默跳过，相机设置里选不到）` |
| 插件根本没被发现 | 工程不在 `Plugins\` 下（构建约定按目录把 DLL 投到 `Modules\`）或没重建插件工程 | `驱动 DLL 在 Modules 目录里`、`插件程序集可装载` |
| 宿主构造不出这台设备 | 驱动必须有一个**接受单个 `CameraDescriptor` 的公开构造函数**；签名不符时反射构造失败，界面上只看到一条装载异常 | `实现 ICameraDevice 的非抽象类型存在`、`按 (CameraDescriptor) 构造成功，身份正确透传`（异常路径另有一条同名"构造失败"的分支断言） |
| 关掉软件后残留设备状态 / 二次释放抛异常 | `Dispose` 不幂等或释放路径上把异常抛出 | `Dispose 幂等（二次释放不抛）` |
| 未连接就能"开始采流"、状态被推到 `Streaming` | 基类的闸门被绕过（子类不该自己实现 `StartStream`） | `未打开时 StartStream 被拒绝`、`初始状态 Closed` |
| 没装 MVS 的机器上插件加载就失败 / 打开时状态机脏（停在 `Connecting`） | 编译期引用了海康 DLL，或 `OpenCore` 吞异常返回 false 却没让基类收尾 | `合成序列号 → Open 失败（真机枚举不到 / 缺 SDK 时同样失败）`、`失败原因说清「去哪装、怎么对」：要么缺 SDK 指路，要么报未找到序列号` |
| 灰度被判成彩色、或彩色被判成单通道（下游整片错位） | `ClassifyPixelFormat` 的命名匹配被改坏（`Mono` / `Bayer` / `RGB` / `BGR` / `YUV` / `YCbCr`） | `像素分类：Mono 系归 Mono`、`像素分类：Bayer / RGB / YUV 系归 Color（统一转 RGB8 再进管线）` |
| 新格式静默进队列、下游 `GenImage` 炸出未知尺寸的图 | 认不出的格式不再归 `Unknown`（"宁可丢帧也不出花屏图"这条策略被改掉） | `像素分类：认不出的归 Unknown（宁可丢帧也不出花屏图）` |
| 手册被"自动生成页"顶掉 / 用户看到的不是这份 | `Help\` 整目录没挂进 `EmbeddedResource`、目录层级不对、文件名没 `NN-` 前缀 | `HM4 有 Help/*.md 的工程都带 EmbeddedResource（手册进得了 DLL）`、`HM2 多插件程序集不许放扁平手册（防串页）`、`HM6 手册文件名是 NN-xxx.md（页序确定）`、`HM5 每页第一行内容就是 # 一级标题（页名取得到）` |
| 开发章节混进用户册（显示分层失效） | 文件名没有 `dev-` 段，或被塞进 `Help\dev\` 子目录（会把同插件用户页整批挤掉） | `HM12 开发页文件名是 NN-dev-xxx.md（宿主认得出这是开发章节）`、`HM12b 手册最多一层目录（开发页另立子目录会挤掉用户页）` |
| 手册编码坏（中文乱码）/ 变成占位页 | BOM 或非法 UTF-8；正文太短；出现占位标记 | `HM7 手册都是无 BOM 的合法 UTF-8 且含中文（编码纪律）`、`HM8 每个插件的手册正文 ≥ 70 行（不是占位页）`、`HM9`（扫描手册正文里的占位词；那条断言的原文案本身含禁用词，此处不复述） |
| 采集步骤拿不到帧号 / 来源序列号，多相机溯源断链 | 输出端口被改名（驱动侧不改，但移植时最容易顺手重命名） | `输出端口含 FrameId / ReceivedCount（相机模式精确帧号，不靠语义重载）`、`输出端口含 SourceSerial（多相机身份；相机模式=配置序列号，其余模式为空）`（后两条在 `ImageAcquisitionChecks`） |

## B 组：不报错，只出诡异现象

| 症状 | 机理 | 守门断言名 |
| --- | --- | --- |
| 界面明明写了失败原因，屏幕上却是"打开失败：InvalidOperationException: …" | 基类 `Open()` 收尾**覆盖**状态说明；子类里 `SetDetail` 的文字会被顶掉——所以 `OpenCore` 的失败必须**抛异常**、不许 `SetDetail` + `return false` | 无断言，靠人工核对（`HikvisionChecks` 只断言"文案里含 MvCameraControl 或未找到序列号"，不禁止 `SetDetail` 写法） |
| 状态说明出现"启动采流失败：InvalidOperationException: 启动采流失败（错误码 0x…）"这种两层前缀 | 驱动抛的文案已带前缀，基类再包一层 | 无断言，靠人工核对（改文案时**不要**顺手删驱动侧那句，否则错误码就没了） |
| 运行中途把 `MvCameraControl.Net.dll` 拷进 `Modules\` 也报"未找到运行时" | `MvsRuntime` 是 `Lazy` 单例：一个进程只探测一次，结果与 `LoadError` 全局静态共享 | 无断言，靠人工核对（`HikvisionChecks` 刻意**不装 SDK** 也要全绿，所以不会跑到探测成功的分支） |
| 两份包装器同时在位，行为随目录顺序变化 | `CandidatePaths()` 命中即止；进程里"先加载的那份"胜出 | 无断言，靠人工核对 |
| 调用点新增重载后报"MVS SDK 缺少方法 X（N 参数）"，或挑错一个重载 | `Call` 只按**方法名 + 参数个数**匹配；返回值不是 `int` 时按 -1（"失败"）处理，文案里显示 `0xFFFFFFFF` | 无断言，靠人工核对 |
| 设备上下线时界面状态不变（网线拔了还显示"采流中"） | `RequiresHeartbeat => false` 且**未注册任何 SDK 回调**，`NotifyDeviceLost` 从未被调用 | 无断言，靠人工核对（判据是"累计收帧不涨 + 采集步骤等图超时"，见用户册《排错》） |
| 取到的图是上一件工件的，但流程全程无错 | 入队的 `byte[]` 必须是 `Marshal.Copy` 出来的独立拷贝；把钉住缓冲直接交给 `PushFrame` = 下一帧覆写 | 无断言，靠人工核对 |
| 越跑越不出图 / 偶发整段取图失败 | `MV_CC_FreeImageBuffer_NET` 必须留在 `finally`；早退（格式不认识、转换失败）也**照样要归还**——当前实现靠 `continue` 跳出 `try` 前的 `finally` 之外完成，改结构时最容易漏 | 无断言，靠人工核对 |
| 换更大分辨率相机后第一次取图就失败或内容被截 | `EnsureConvertBuffer(need)` 按需扩容并重新钉住；扩容时漏 `Free` 旧 `GCHandle` = 句柄泄漏 + 旧缓冲被 GC 回收后 SDK 仍写它 | 无断言，靠人工核对 |
| 长时间停止采流后进程内存偏高 | `StopStreamCore` **不** unpin，`_convertBuffer` 一直钉到 `CloseCore`（设计取舍：下次起流复用、免再分配） | 无断言，靠人工核对 |
| 「停止采流」点下去 100~2100 毫秒才生效 | 停止标志要等当前取图周期结束；`Join(2000)` 超时后不强杀线程、继续调 `StopGrabbing`（托管线程不能强杀） | 无断言，靠人工核对 |
| 相机侧配好硬触发后"又变回连续出图" | `OpenCore` 必须按 `ReadSettings().TriggerMode` 走 `ApplyTriggerSettingsCore`，不得写死 `TriggerMode=0`；下发后回读 `TriggerMode`，不一致必须阻断 | `CameraTriggerChecks` 映射/迁移/采集步骤断言；真机回读靠现场探针 |
| 界面选「上升沿」，相机仍连续出图 | 同上：驱动必须读 `TriggerMode` 并经 `HikvisionTriggerPlan` 映射；`TriggerSoftware` 只在软触发模式下下发 | `CameraTriggerChecks` 映射与采集步骤断言 |
| 旧方案升级后全部等图超时 | 旧配置没有 `TriggerSchemaVersion`，`Normalized()` 必须迁移成 `Continuous`；不能把旧值当软触发执行 | `CameraTriggerChecks` 迁移断言 |
| 软触发模式下采集步骤不触发/先等帧 | 采集步骤必须在 `WaitNextFrame` 之前调 `TriggerSoftware`；驱动不支持软触发时必须明确失败，不能退化成超时 | `CameraTriggerChecks` 采集步骤断言 |
| 参数面板显示改了、下次打开又变回去 | `ApplySettings` 成功后才把归一化值同步回 `Descriptor.Settings`；若子类在 `ApplySettingsCore` 里自己改了传入实例或在失败路径写配置，两份值就分叉 | 无断言，靠人工核对 |
| 非法曝光被"夹成一个能用的值"，现场看不出相机其实拒绝了 | 驱动**不做**本地夹值：原值下发、按 SDK 错误码报错。归一化只发生在 `CameraSettings.Normalized()` 的四条（容量 1~1024、`FrameTimeoutMs` <1→3000、`HeartbeatTimeoutMs` <200→3000），曝光/增益不在其中 | 无断言，靠人工核对 |
| 两台相机配同一序列号时，只有一台在出图 | `FindDeviceBySerial` 返回**第一个**命中者；宿主另有告警但不拦 | 无断言，靠人工核对 |
| 枚举 N 台却"找不到"，而 MVS 客户端明明能开 | 命中判据是 `nTLayerType` 与单个常量的**相等**比较（不是位与）：若某版本/机型把传输层存成组合位，该设备会被整条跳过（`sn` 保持 null） | 无断言，靠人工核对 |
| 序列号抄对了却报"未找到" | 只忽略首尾空格与大小写；中间的 `O`/`0`、少一位都判不同 | 无断言，靠人工核对 |

## 改这个文件时必须一起动的东西

| 改了什么 | 必须同步 |
| --- | --- |
| `[Display]` 的 `Name` / `GroupName` | 用户册三页的页眉「分类」与工具箱实名；`HikvisionChecks` 里对 `Name == "海康相机"` 的断言 |
| 构造函数签名 | 宿主反射构造与 `HikvisionChecks` 的构造断言 |
| `ClassifyPixelFormat` 的关键词 | 用户册《取流与触发口径》的格式表 + `HikvisionChecks` 三条像素分类断言（该方法是 `internal` 且靠 `InternalsVisibleTo FlowCanvasChecks` 直测，别改成 `private`） |
| 任何错误文案 | `05-troubleshooting.md` 的"现象"列逐字改（那张表承诺与源码一致） |
| `MvsRuntime` 新增绑定成员 | `10-dev-architecture.md` 第六节的码值归属表（**写明是 MVS/GenICam 的码还是本仓 C# 值**） |
| 触发模式 / 触发源线号 / 触发延时 | `CameraSettings` 迁移（`TriggerSchemaVersion`）、`HikvisionTriggerPlan`、`CameraTriggerChecks`、`04-streaming-and-trigger.md`、相机设置界面 |
| 触发相关错误文案 | `05-troubleshooting.md` 的触发 Q&A + `CameraTriggerChecks` 的文案断言 |
| 驱动 DLL 或手册 | 该工程用 `--no-incremental` 重建，并核对 DLL 里 `.Help.` 资源名数 == `Help\` 下 .md 数（增量构建会静默漏打包手册） |
