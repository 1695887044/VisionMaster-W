# 开发说明：结构、线程与协议

## 一、类与文件

| 文件 | 内容 |
|---|---|
| `SimMotionCard.cs` | 唯一的生产代码：`sealed class SimMotionCard : MotionDeviceBase` |
| `Plugin.Motion.Sim.csproj` | `net9.0`（不带 `-windows`）+ 只引 `Shard/Core.Interfaces` + `EmbeddedResource Include="Help\**\*"` |

驱动**只实现基类的四个抽象成员**（`MotionDeviceBase.cs:172/175/181/196`）：

```csharp
protected override bool ConnectCore();
protected override void DisconnectCore();
protected override void PollStatusCore(CancellationToken ct);
protected override MotionCommandOutcome ExecuteCommandCore(
    MotionCommand command, CancellationToken ct, out string error);
```

外加两个可选重写：`SafeStopCore()`（失联/急停收尾时停住仿真世界的轴）与
`ClassifyOutcome(...)`（把"未使能/未回零"归成可恢复，而不是设备故障）。
**不要**在这里重写门禁逻辑——状态、轴号、软限位、未使能/未回零/正在运动全部由
`MotionDeviceBase.CheckGateLocked`（`MotionDeviceBase.cs:786`）判，驱动里再写一遍就是两套判据。

## 二、线程模型（三个线程，一把锁）

```
命令线程（基类起）  ──┐
                      ├─→ Request(json)  ── 一把 _io 锁 ──→ 同一条 TCP 连接
轮询线程（基类起）  ──┘
```

- **命令线程**：逐条执行 `ExecuteCommandCore`。`Move`/`Jog` 只发 `enqueue` 就返回；
  回零/清报警这类 `WaitsForCompletion` 的命令才在这里等。
- **轮询线程**：按 `PollIntervalMs` 调 `PollStatusCore`，把 `poll` 的结果回填成 `AxisStatus` / 输入点。
- **一把 `_io` 锁**：两条线程共用一条连接，不加锁会把两条报文交错，
  表现为"偶发 `bad_json` / 应答错位"，且只在忙的时候出现（空跑永远复现不了）。

连接断开时 `Request` 返回 `null`：轮询路径据此**抛异常**（基类记一次轮询失败 → 看门狗 → 安全停机），
命令路径据此返回 `Failed`（并写清是超时还是断开）。

## 三、协议命令表（客户端 → 仿真器）

所有报文都是**一行 JSON**（`\n` 结尾），UTF-8 无 BOM。

| `op` | 关键字段 | 应答 |
|---|---|---|
| `hello` | `proto`（必须 1）、`client` | `ok/proto/sim/build/world/card/axes/din/dout/caps` |
| `enqueue` | `id/kind/axis/pos/speed/acc/port/value/dir/stopMode/homeMode` | `ok/accepted/id`，拒绝时多 `result/reason` |
| `poll` | — | `ok/tick/state/axes[]/din/dout/wp/belt` |
| `estop` / `reset.estop` | — | `ok` |
| `io.write` / `io.read` | `port` / `value` | `ok` / `ok+value` |
| `cancel` | — | `ok`（减速停全部轴） |

`enqueue.kind` 与 `MotionCommandKind` 的映射（缺一个就是"命令发不出去"）：

| `MotionCommandKind` | `kind` | 备注 |
|---|---|---|
| `Enable` / `Disable` | `Enable` / `Disable` | 立即受理 |
| `MoveAbsolute` / `MoveRelative` | 同名 | **下发即完成**，不等到位 |
| `Home` | `Home` | 带 `homeMode`；`WaitsForCompletion=true`，这里等到位 |
| `Stop` | `Stop` | 带 `stopMode`（2 = 减速停，3 = 立即停）；`axis=-1` 表示全部轴 |
| `SetOutput` | `SetOutput` | `port`/`value` |
| `ClearAlarm` | `ClearAlarm` | `axis=-1` 表示卡级 |
| `Jog` | `Jog` | 带 `dir`（+1/-1）、`speed`、`acc`；无终点 |

## 四、`poll` 应答的字段大小写（踩过的坑）

仿真器用 `JsonSerializer.Serialize` 直接序列化 `AxisSnapshot` **record struct**，
属性名**保持 PascalCase**；而匿名对象那部分（`ok/tick/state/din/dout/wp/belt`）是**小写**。
即：

```json
{"ok":true,"tick":1234,"state":"Online","din":"0","dout":"0","wp":3,"belt":12.5,
 "axes":[{"PhysicalIndex":0,"LogicalName":"ST1.X","PositionMm":120.5,"VelocityMmPerS":200,
          "InPosition":true,"Moving":false,"Enabled":true,
          "PositiveLimit":false,"NegativeLimit":false,"Origin":true,
          "EmergencyStop":false,"Alarm":false,"AlarmMessage":null}]}
```

读错大小写不会报错，只会**静默全取默认值**（位置恒 0、`Moving` 恒 false）——
这种"能连上、能发命令、就是位置不动"的现象，第一嫌疑就是这里。

## 五、能力位与回零

`hello.caps.supportsAbsoluteEncoder` 为 `false`（当前仿真器就是）时：

- `SetCapabilities(... SupportsAbsoluteEncoder = false ...)`；
- `MarkHomed(false)`：位置不可信，必须回零；
- 于是 `MoveAbsolute` 会被基类门禁以"轴未回零"拒绝（`MotionDeviceBase.cs:993` 一带）。

这是**刻意**与真机一致的行为。为了让流程好跑而改成 `true`，等于把"增量编码器必须先回零"
这条真机上最常出事的约束从仿真里抹掉。

八种回零方式照实上报（仿真器 `Axis.StartHome` 全部实现）；报少了会让「轴回零」步骤
在能力协商阶段就拒绝用户选的方式（`MotionDeviceBase.cs:850-859`）。

## 六、输入点解码

`poll.din` 是十六进制串，**每 4 位一个字符、低位在前**：
第 `i` 个字符的第 `b` 位 = 第 `i*4+b` 个输入点。驱动按 `hello.din` 截断，多出来的位丢弃。

## 七、错误映射（谁的话算数）

| 来源 | 驱动返回 | 上位机表现 |
|---|---|---|
| 基类门禁拒绝（未使能/未回零/软限位/轴号越界） | 驱动根本收不到这条命令 | 命令入队即被拒，`RejectedCommandCount++` |
| 仿真器 `accepted=false` + `result=unsupported` | `NotSupported` | 提示"不支持"，不建议重连 |
| 仿真器 `accepted=false` + 其它 `result` | `Failed`，`error` 用仿真器的 `reason` 原文 | 报故障，按 `ClassifyOutcome` 分级 |
| 无应答 / 超时 | `Failed`（等待类命令超时是 `TimedOut`） | 报通信故障，看门狗可能在下一拍判失联 |
| `ct` 取消（急停打断） | **抛 `OperationCanceledException`** | 记 `Canceled`，不报故障、不进安全停机 |

最后一行是硬约定：返回 `Failed` 会把"人工按了急停"记成"设备故障"，现场排查会被带偏。

## 八、不写在本驱动里的东西

- 位置插值/预测：见 `11-dev-invariants.md` 的 A3。
- 门禁判据：基类唯一一份。
- 仿真世界的物理：在**独立仓库** `D:\C#\LineSim\src\World\`（不在本仓），驱动只读快照。
- 新增配置类型：端点走 `MotionDescriptor.Address`，不新增 `SimulationSettings`
  （插件是 `net9.0` 只引契约层，引不到宿主的 `net9.0-windows` 配置类型；设计 §9.3 已把该方案撤销）。
