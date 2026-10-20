# 立刻读 · 开发：结构、契约与不变量

> 分类：通讯 ｜ 来源：插件 Plugin.Comm ｜ 读者：开发

S 档条目：架构、宿主契约、不变量合在本页。源码 `CommReadNowPlugin.cs`，
程序集由 `Plugin.Comm.csproj` 产出（同程序集两个插件类，手册按类名分目录，本页只管「立刻读」）。

## 一、结构：一个类、一个输入、五个输出、零私有状态

```
CommReadNowPlugin : VisionPluginBase
                    没有 IPluginCustomViewProvider / IPluginConfigContextProvider
                    没有 [StepConfig] 属性
                    没有私有字段、没有 static 可变状态
```

`RunAlgorithm` 的完整流程：

```
1. 清空五个输出（Value / ValueText / TypeName / ConnectionName / ElapsedMs）
2. name = VariableName.GetTypedValue()?.Trim()
   空 → Fail("通讯变量名不能为空")
3. comms = context.Comms；null → Fail("没有接入通讯层")
4. comms.TryDescribe(name, out info, out error)
   失败 → Fail(error)，Logger.Error
5. ConnectionName = info.ConnectionName；TypeName = info.TypeName
6. info.IsConnected == false → Fail("连接 [X] 离线，立刻读未执行")
7. context.CancellationToken 已取消 → Fail("流程已取消，立刻读未执行")
8. Stopwatch 起；comms.TryReadNow(name, out value, out error)；Stopwatch 停
   ElapsedMs = 实测毫秒
   失败 → Fail(error)，Logger.Error
9. Value = value；ValueText = FormatValue(value)
   TypeName 为空且 value != null → TypeName = value.GetType().Name
10. Success = true；Logger.Info
```

第 1 步是硬要求：输出端口跨轮保留，失败时不清空会让下游读到上一轮的旧值。
第 6/7 步是"发起前短路"：离线或已取消时**不占用插队队列**，也不让设备白跑一次。

## 二、宿主契约（本插件用到的每一条）

| 契约 | 提供方 | 本插件哪里靠它 |
| --- | --- | --- |
| `IExecutionContext.Comms` | `Core.Interfaces` 默认成员 / `ExecutionContext` 注入 | 唯一的通讯访问入口 |
| `ICommVariableAccess.TryDescribe` | 宿主 `CommVariableAccess` | 变量存在性、类型、只读、连接状态快照 |
| `ICommVariableAccess.TryReadNow` | 宿主 `CommVariableAccess` → `NetworkVariableModel.TryReadNow` | 真正的插队读 |
| `VisionPluginBase.Fail` | 基类 | 置 `Success=false` + 写 `ErrorMessage` |
| 端口发现 | 基类反射公开实例属性 | 六个端口初始化即注册 |
| 统一灌值 | `ApplyConfigValues`：InputValues → 端口 | `VariableName` 的值从这里进来 |
| 默认确认 | `OnConfirm`：未连线端口写回 InputValues | 选中变量名后落盘 |
| 日志器 | `context.Logger` | Info/Error 两条 |

**没有 `[ParallelSafe]`**：本插件触碰共享设备（连接/插队队列），且读会占用一次设备事务。
并行分支里使用它应在编译期被拒绝（`FlowCompiler` 的 `[并行不安全]` 门禁），
而不是"看起来能跑、现场偶发排队"。三期设备级锁落地后再评估。

## 三、调用链

```
CommReadNowPlugin.RunAlgorithm
  → context.Comms.TryDescribe(name)                  // 纯本地查表，无 I/O
  → context.Comms.TryReadNow(name)
    → CommVariableAccess.TryReadNow
      → NetworkVariableModel.TryReadNow
        → AdvancedCommunicationManager.ReadVariableNow
          → ConnectionWorker.InvokeUrgentRead        // L1 插队读车道
            → PollBatchPlanner.TryReadOne            // 单变量计划，只解码不发布
              → ICommunicationConnection.ReadBytes / Read<T>
          → CommunicationVariable.UpdateValue(工程值) // 镜像 + 质量戳 Good
```

关键点：

- `TryReadOne` 与常规轮询共用 `DecodeItem` / `DecodeBitItem`，字节序、类型解码、
  长度校验只有一份实现。
- `ReadVariableNow` 构造单变量计划时固定 `isolateFaultedSegments: false`：
  即使该变量所在段在常规轮询里被坏段隔离，立刻读仍要真试一次。
- 读成功由 Manager 调 `UpdateValue` 推回镜像；插件不再重复写模型，
  所以变量管理/组态画面看到的就是这次读到的值。

## 四、车道与延迟边界

| 车道 | 内容 | 优先级 |
| --- | --- | --- |
| L0 | 插队写（「立刻写」） | 最高 |
| L1 | 插队读（本插件） | 高 |
| L2 | 常规写 | 中 |
| L3 | 常规读 | 低 |
| L4 | 轮询 | 最低，且段间让路 |

- 同一车道 FIFO；跨车道严格优先级；一次只执行一条命令。
- **不能抢占正在执行的命令/段**：插队只改变排队位置，不打断 `ReadBytes`/`Write`。
- 轮询在**段与段之间**检查插队队列；`Yielded` 时不 `MarkSuccess`，下一拍重跑整组
  （已读过的段会重读一次，这是让路的代价）。
- 典型等待：空闲时一个设备往返；轮询中一段 + 一个往返；常规命令执行中 = 当前命令完成。
- 最坏等待 = 连接配置的读超时（设备无响应）。插队不能取消已发出的 I/O。

## 五、失败语义

- 变量不存在/不是通讯变量/缺连接或地址/离线：插件在发起前 `Fail`，不占队列。
- 插队队列满：Manager 抛 `InvalidOperationException`，Model 转成 `error`，插件 `Fail`；
  **不回退常规队列**（回退会静默违反"立刻"语义）。
- 设备读失败：Manager 记 `LogError` + `OnConnectionError` 后抛出；插件 `Fail`。
  **不调用 `OnConnectionBroken`**：一个坏地址/一次超时不把好连接打进重连循环；
  链路死活交给下一拍常规轮询。
- 读失败**不改质量戳**：旁路探测失败不代表轮询坏了；质量归常规轮询。
  读成功会通过 `UpdateValue` 置 `Good`（读到新鲜值本身就是证明）。
- 取消：只在发起前检查；已发出的 I/O 等它返回或超时，然后按失败返回。

## 六、结果与格式口径

- `Value` 是**工程值**：`CommunicationVariable.ToEngineering` 已按地址配置做 Scale/Offset。
- `ValueText`：`byte[]` → 十六进制空格分隔；`bool` → `True`/`False`；
  数值 → `InvariantCulture`；`null` → 空串。
- `ElapsedMs` 包含插队排队 + 当前事务等待 + 本次往返，不包含步骤调度开销。
- `TypeName` 优先取 `TryDescribe` 的声明类型；描述失败但读成功时回退 `value.GetType().Name`。

## 七、边界与已知限制

1. **一个步骤一个变量**：需要多个点就放多个步骤；批量读是后续扩展。
2. **读-改-写不是事务**：读和写是两条独立命令，中间设备可能变化。
3. **名字寻址**：步骤存变量名；变量改名后步骤报"找不到变量"，需要重选。
4. **高频调用**：插队队列容量 256、入队等待 200ms；高频读应改用扫描组，而不是插队读。
5. **试运行**：`PluginTestRunner` 会透传 `ICommVariableAccess`；面板上点「执行」走的是
   同一套插队车道，不是"模拟值"。

## 八、守门与验证

- 通讯层：`CommChecks` 的 C29 组（插队顺序、队列满、段间让路、单变量读取）。
- 插件层：`FlowCanvasChecks` 的 `[CN]` 组（元数据、成功/失败路径、只读区、租约、取消）。
- 手册：`HelpManualCheck` 的 HM 组（打包、分册、页序、档位厚度）。
- 变异反证：把 `DrainQueues` 的插队两行去掉 → C29 顺序断言必红；
  把 `TryReadOne` 改回 `Poll` 的 bool 判定 → 坏地址会被误报成链路故障。

## 九、相关源码

- 插件：`Plugins\Plugin.Comm\CommReadNowPlugin.cs`
- 契约：`Shard\Core.Interfaces\ICommVariableAccess.cs`
- 宿主实现：`Core\Binding\CommVariableAccess.cs`、`Core\Models\Variable\NetworkVariableModel.cs`
- 通讯层：`Communication\Communications\Manager\ConnectionWorker.cs`、
  `AdvancedCommunicationManager.cs`、`PollBatchPlanner.cs`、`PollScheduler.cs`
- 候选来源：`Communication\CommunicationModule.cs` 的 `RegisterStepConfigOptions`
