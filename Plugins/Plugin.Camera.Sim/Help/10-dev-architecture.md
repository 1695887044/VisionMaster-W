# 开发者视角：结构与不变量

本页给改这个驱动的人看。目标不是复述代码，而是把"为什么这么写"和"改了会坏什么"钉住。

## 1. 类型与依赖边界

```
Plugin.Camera.Sim  (net9.0, 无 WPF)
  └── Core.Interfaces (net9.0)
        ├── CameraDeviceBase   ← 状态机/环形队列/心跳看门狗/帧号回填 全在这里
        ├── CameraFrame        ← 像素载体（行优先无填充）
        ├── CameraSettings     ← 曝光/增益/触发模式/超时
        └── CameraDescriptor   ← 身份（Id/SerialNo/DriverTypeKey）
```

**刻意不引**：`VM.Core`、`VisionMaster`（宿主）、`halcondotnet`。
理由与 `Plugin.Camera.Network` 相同——驱动层不碰 Windows 专有 API 与图像库，
将来搬到 Linux 工控机只需重编 csproj。**加任何 ProjectReference 前先问"这是不是又要绑死平台"**。

## 2. 类图与职责

| 类型 | 职责 | 不该做的事 |
|---|---|---|
| `SimCameraDevice` | 实现四个差异点 + 软触发 | 不自己管状态机、不自己排队列 |
| `CameraDeviceBase`（契约） | `Open/Close/StartStream/StopStream` 状态机、环形队列、溢出计数、心跳看门狗、帧号与身份回填 | —— |

驱动只实现：`OpenCore` / `CloseCore` / `ApplySettingsCore` / `StartStreamCore` /
`StopStreamCore`，外加覆写 `RequiresHeartbeat`、`StateAfterOpen`、`SupportsSoftwareTrigger`、
`TriggerSoftware`。

## 3. 生命周期时序

```
Open()
  ├─ OpenCore()            GET /health → 校验 cameras 含 SerialNo → 起心跳循环
  └─ SetState(StateAfterOpen = Online)

ApplySettings(settings)
  └─ ApplySettingsCore()   POST /camera/{serial}/config  { exposureUs, gain }

StartStream()
  ├─ _acceptFrames = true
  └─ StartStreamCore()     起收流任务：GET /camera/{serial}/stream（ResponseHeadersRead）

TriggerSoftware()
  └─ POST /camera/{serial}/trigger  → 仿真器锁内快照世界 → 渲染 → 入队

收流任务循环
  ├─ ReadFrameAsync：int32 头长 | 头 JSON | int32 像素长 | 像素
  └─ PushFrame(frame)      ← 基类回填 CameraId/SerialNo/FrameId，更新心跳证据

StopStream()
  └─ _acceptFrames = false（收流任务继续跑，帧被拒）

Close()
  └─ CloseCore()           Cancel + Dispose HttpClient（**不 Join 收流任务**）
```

## 4. 五条不变量

**I1 软触发必须为 true。**
`SupportsSoftwareTrigger => true` 是整个方案存在的理由。若改成 false，采集步骤会走
`WaitNextFrame` 死等，而仿真器不触发就不出图 → 必然超时。

**I2 帧头不许携带真值。**
`SourceName` 只放 `linesim-{serial}-{seq:D6}.png` 这种文件名，**绝不放 tick/位置/真值**。
设计 §5.4 曾打算把 tick 编进 `SourceName`，因与契约语义（"仅用于日志与界面"）冲突而作废。
断言 S15 会做旁路扫描。

**I3 Close 不许等收流任务。**
`Close()` 可能由 UI 线程调用。收流任务正卡在 `ReadAsync` 上时，Join 会把界面冻住；
而取消令牌只在下一帧到达或连接断开时才被观察到。所以 `CloseCore` 只做 Cancel + Dispose，
任务自己退出。

**I4 未连接时下发参数不算失败。**
`ApplySettingsCore` 在 `_http is null` 时只更新字段并返回 true。
否则"先配参数、后连接"的操作顺序会被一个莫名其妙的失败挡住。

**I5 一切异常都要落到 SetDetail，不许往上抛。**
`OpenCore`/`ApplySettingsCore`/`TriggerSoftware` 都是同步方法，异常逃出去会变成
"点一下按钮进程没了"。所有网络异常都必须 catch 成 `SetDetail` + 返回 false。

## 5. 为什么用轮询而不是 WebSocket

仿真器侧是手写的最小 HTTP（`TcpListener`，不用 `HttpListener`——后者在 Windows 上绑
`http://127.0.0.1:port/` 需要 urlacl，非管理员直接失败）。既然已经手写了 HTTP，
长连接 + 长度前缀帧是最省的：不需要 WebSocket 握手、不需要掩码、不需要分片。

**代价**：没有服务端主动推送的语义（仿真器不能"自己"出图）。这恰好与"触发才渲染"一致，
所以不是缺陷。

## 6. 端点解析

`ResolveEndpoint()`：环境变量 `VM_LINESIM_CAM`（`host:port`）优先，否则常量 `127.0.0.1:19500`。
**刻意不读宿主配置**：插件拿不到 `AppConfigModel`（那是 `net9.0-windows` 的类型），
插件配置通道按设计只服务配置视图、不传运行期连接信息。详见设计 §17.1 D6。

## 7. 改这里要连带改什么

| 改动 | 必须同步 |
|---|---|
| 端口常量 | 设计 §8.1 端口表、**独立仓库 `LineSim`**（`D:\C#\LineSim`，不在本仓）的 `--camera-port` 默认值、断言 S29 |
| 帧头字段 | 仿真器 `CameraServer.cs`、断言 S15 白名单、本页 §4 的 I2 |
| 队列容量 | `CameraSettings.BufferCapacity` 默认值、仿真器 `CameraChannel` 容量（两处同值） |
| 新增相机 | `LineSim` 的 `CameraServer` 构造里加一路、`/health` 自动带出、本驱动无需改 |
| 触发语义 | 采集步骤 `ImageAcquisitionPlugin` 的软触发分支、断言 S7/S8 |

## 8. 断言在哪

| 断言 | 位置 | 覆盖什么 |
|---|---|---|
| 渲染确定性 | `LineSim --selftest` | 同 tick 同状态逐字节一致、换 tick 必变、曝光参与 |
| 相机流通道 | `LineSim --selftest` | `/health`、触发受理、`seq` 递增、404、帧头白名单、像素长度 |
| 驱动端到端 | `tools/probes/_CameraSimProbe` | 真驱动 Open → 软触发 → 取帧 → 像素与真值对得上 |
| 帧头无真值 | 设计 S15 | 字段白名单 + `SourceName`/`Timestamp` 旁路扫描 |

改完必须跑：`dotnet build`（插件工程本身，红线③）+ `LineSim --selftest` + 探针。
只重建宿主不算验证——`Modules\` 不会由宿主带出。

## 9. 已知取舍

- **不做连续采集**：仿真器是拉模型，连续模式下必然超时。要连续采集得给仿真器加一个
  "自动按固定频率渲染"的模式，那会让"触发时刻"这个语义变模糊，所以不做。
- **不做硬件触发**：契约里 `TriggerSourceLine` 在仿真器侧没有对应物。
- **不做彩色**：P1 只有灰度（`ch=1`）。彩色要改 `Renderer` 与像素布局，且 HALCON 侧要另走
  `GenImageInterleaved`，属于独立批次。
- **不做掉电保持**：仿真器进程退出即世界归零，不落盘。回放能力走 `.lsrec`（P3）。
