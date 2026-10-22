# 端口、地址与参数

## 一、连接地址（唯一必填项）

地址填在**运动卡**的「连接地址」里（`MotionDescriptor.Address`），格式两种都行：

| 写法 | 含义 |
|---|---|
| `127.0.0.1:19510` | 推荐：显式写端口，多实例/改端口时不靠猜 |
| `127.0.0.1` | 省略端口时用默认 19510 |
| 空 | 按 `127.0.0.1:19510` 处理（不报错，日志会写实际用的端点） |

端口与仿真器的 `--motion-port` 必须一致。默认 19510 是设计 §8.1 端口规划里给运控通道留的段位，
与宿主自己占用的 19000（收图）、19100（相机服务）、8080 不冲突。

## 二、握手与协议版本

连上之后驱动发的第一行是：

```json
{"op":"hello","proto":1,"client":"Plugin.Motion.Sim"}
```

仿真器回：

```json
{"ok":true,"proto":1,"sim":"LineSim","build":"0.1.0-p0","world":"line-p0-2st-3ax",
 "card":"SIM20","axes":3,"din":8,"dout":8,
 "caps":{"axisCount":3,"digitalInputCount":8,"digitalOutputCount":8,
         "supportsHardLimit":true,"supportsAbsoluteEncoder":false,
         "supportsLineInterpolation":false,"supportsArcInterpolation":false,"supportsJog":true}}
```

- `proto` 不等于本驱动的 `ProtoVersion`（当前 1）→ 握手被拒，卡建不出来，说明文字会写
  `仿真器协议版本不符（仿真器 X / 本驱动 1）`。
- `axes` / `din` / `dout` **就是**这张卡的能力位来源。仿真器换了规模，重新连一次即可，不必改配置。
- `caps.supportsAbsoluteEncoder` 为 `false` 时，连上后 `IsHomed=false`，运动命令一律先被门禁拒绝，
  必须先跑「轴回零」。

## 三、能力位（由仿真器决定，不要手改）

| 能力位 | 取值 | 说明 |
|---|---|---|
| 轴数 | `hello.axes` | P0 = 3；P2 扩到 44 时同一份代码自动跟随 |
| 数字输入 | `hello.din` | P0 = 8；P2 = 96 |
| 数字输出 | `hello.dout` | P0 = 8；P2 = 96 |
| 硬限位 | 支持 | 由仿真世界按轴位置点亮 `PositiveLimit` / `NegativeLimit` |
| 绝对编码器 | 不支持 | 增量式：不回零位置不可信 |
| 直线/圆弧插补 | 不支持 | 契约层未实现插补，仿真器也没有该通道 |
| 点动 | 支持 | 点动无终点，直到 `Stop`/`Disable` |
| 回零方式 | 全部八种 | `NegativeLimitIndex` … `PositiveLimit`，仿真器逐种实现 |

## 四、与本驱动相关的卡级参数（`MotionDescriptor.Params`）

| 参数 | 默认 | 在本驱动里的作用 |
|---|---|---|
| `PollIntervalMs` | 10 | 轮询线程的 `poll` 周期。调到 50ms 以上，轴到位与限位信号的滞后会明显变大 |
| `CommandTimeoutMs` | 30000 | 命令级默认超时。回零/清报警这类"等到做完"的命令按它判超时 |
| `WatchdogTimeoutMs` | 3000 | 多久没问通就判失联 → 安全停机。仿真器崩溃时这条会生效 |
| `MaxQueueDepth` | 256 | 命令队列上限（基类判，仿真器不参与） |

命令自带的 `Timeout`（`MotionCommand.Timeout`，`TimeSpan`）优先于 `CommandTimeoutMs`；
<= 0 时用卡级值。

## 五、轴与 IO 的编号

- **轴号**：`PhysicalIndex`，从 0 起，与仿真世界一致。P0 是
  `0 = ST1.X`、`1 = ST1.Z`、`2 = ST2.Y`（逻辑名见 `hello.world` 与仿真器控制台输出）。
- **输入点**：`poll` 回的 `din` 是十六进制串，**每 4 位一个字符、低位在前**：
  第 i 个字符的第 b 位 = 第 `i*4+b` 个输入点。驱动按能力位截断，多出来的位丢弃。
- **输出点**：`SetOutput` 的 `port` 直接就是输出点号；越界由基类门禁先拦，仿真器再拦一次。

## 六、端口被占用怎么办

仿真器启动时若 19510 已被占用，会打印 `[motion] 监听失败` 并退出（非 0 退出码）。
排查顺序：

1. `netstat -ano | findstr 19510` 看谁占着；
2. 上一次的 `LineSim.exe` 没退干净（`tasklist | findstr LineSim`）；
3. 真有两个实例同时在跑——`--serve` 前先确认只起一个（仿真器不做单实例互斥，端口冲突就是它的保护）。
