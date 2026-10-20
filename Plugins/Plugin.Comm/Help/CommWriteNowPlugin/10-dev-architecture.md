# 立刻写 · 开发：结构、契约与不变量

> 分类：通讯 ｜ 来源：插件 Plugin.Comm ｜ 读者：开发

S 档条目：架构、宿主契约、不变量合在本页。源码 `CommWriteNowPlugin.cs`，
程序集由 `Plugin.Comm.csproj` 产出（同程序集两个插件类，手册按类名分目录，本页只管「立刻写」）。

## 一、结构：一个类、三个输入、五个输出、零私有状态

```
CommWriteNowPlugin : VisionPluginBase
                     没有 IPluginCustomViewProvider / IPluginConfigContextProvider
                     没有 [StepConfig] 属性
                     没有私有字段、没有 static 可变状态
```

`RunAlgorithm` 的完整流程：

```
1. 清空五个输出（WrittenValue / Completion / TypeName / ConnectionName / ElapsedMs）
2. name = VariableName.GetTypedValue()?.Trim()
   空 → Fail("通讯变量名不能为空")
3. comms = context.Comms；null → Fail("没有接入通讯层")
4. comms.TryDescribe(name, out info, out error)
   失败 → Fail(error)，Logger.Error
5. ConnectionName = info.ConnectionName；TypeName = info.TypeName
6. info.IsReadOnly → Fail("位于只读存储区…")
7. info.IsConnected == false → Fail("连接 [X] 离线，立刻写未执行")
8. context.CancellationToken 已取消 → Fail("流程已取消，立刻写未执行")
9. value = Value.GetTypedValue()；null → Fail("要写入的值不能为空")
10. wait = WaitForCompletion.GetTypedValue()
11. Stopwatch 起
    wait=true  → comms.TryWriteNow(name, value, out written, out error)
    wait=false → comms.TryQueueWriteNow(name, value, out written, out error)
    Stopwatch 停；ElapsedMs = 实测毫秒
    失败 → Fail(error)，Logger.Error
12. WrittenValue = written；Completion = wait ? "Completed" : "Queued"
    Success = true；Logger.Info（等待模式写"立刻写"，入队模式写"立刻写已入队"）
```

第 1 步是硬要求：输出端口跨轮保留，失败时不清空会让下游读到上一轮的旧值。
第 6/7/8 步是"发起前短路"：只读区、离线、已取消都不占用插队队列。

## 二、宿主契约（本插件用到的每一条）

| 契约 | 提供方 | 本插件哪里靠它 |
| --- | --- | --- |
| `IExecutionContext.Comms` | `Core.Interfaces` 默认成员 / `ExecutionContext` 注入 | 唯一的通讯访问入口 |
| `ICommVariableAccess.TryDescribe` | 宿主 `CommVariableAccess` | 变量存在性、类型、只读、连接状态快照 |
| `ICommVariableAccess.TryWriteNow` | 宿主 `CommVariableAccess` → `NetworkVariableModel.TryWriteNowToValue` | 真正的插队写 |
| `VisionPluginBase.Fail` | 基类 | 置 `Success=false` + 写 `ErrorMessage` |
| 端口发现 | 基类反射公开实例属性 | 六个端口初始化即注册 |
| 统一灌值 | `ApplyConfigValues`：InputValues → 端口 | 变量名与值的常量从这里进来 |
| 默认确认 | `OnConfirm`：未连线端口写回 InputValues | 变量名/常量落盘；已连线端口移除固定值 |
| 日志器 | `context.Logger` | Info/Error 两条 |

**没有 `[ParallelSafe]`**：本插件触碰共享设备与写租约，并行分支里应被编译期拒绝
（`FlowCompiler` 的 `[并行不安全]` 门禁）。租约能保证"不同 owner 不会同时写同一个点"，
但那是运行期拒绝，不是并行安全的证明；三期设备级锁落地后再评估。

`WaitForCompletion` 是 `InputPort<bool>` + `IsFunctionalEnum = true` +
`OptionKind = BooleanChoice`：`LinkableValueEditor` 因此渲染成 True/False 下拉，
并隐藏 🔗/✕。布尔二选一的候选由 `StepConfigOptionSource` **契约层自带**
（`BooleanChoice` 直接返回 `True`/`False`，不依赖宿主模块注册），
避免测试夹具 `Register(...)` 覆盖后退回文本框。

## 三、调用链

```
CommWriteNowPlugin.RunAlgorithm
  → context.Comms.TryDescribe(name)                  // 纯本地查表，无 I/O

  等待模式（WaitForCompletion=true）：
  → context.Comms.TryWriteNow(name, value)
    → CommVariableAccess.TryWriteNow
      → NetworkVariableModel.TryWriteNowToValue
        → TryWriteCore(urgent: true, waitForCompletion: true)
           ① 地址/管理器/连接名/连接在线校验
           ② IsReadOnlyArea 拦截
           ③ 文本按声明类型解析（ValueTextCoercion）
           ④ 写租约（WriteLeaseRegistry，owner = WriteScope.CurrentOwner）
           ⑤ AddressConfig.ConvertToRaw（工程值 → 原始值）
        → AdvancedCommunicationManager.WriteVariableNow
          → ConnectionWorker.InvokeUrgentWrite       // L0 插队写车道，等设备返回
            → ICommunicationConnection.Write
        → UpdateMirrorValue(工程值)

  不等待模式（WaitForCompletion=false）：
  → context.Comms.TryQueueWriteNow(name, value)
    → CommVariableAccess.TryQueueWriteNow
      → NetworkVariableModel.TryQueueWriteNowToValue
        → TryWriteCore(urgent: true, waitForCompletion: false)   // ①②③④⑤ 完全相同
        → AdvancedCommunicationManager.TryTriggerWriteNow
          → ConnectionWorker.TryEnqueueUrgentWrite   // 只入队，立即返回
            → Worker 之后执行 ICommunicationConnection.Write
              失败 → 日志 + OnCommunicationError（本步骤已返回，无法再观测）
        → **不更新镜像**（命令还没执行）
```

关键点：

- `TryWriteCore` 是普通写与插队写的**共同核心**：两条路径共用校验、文本解析、租约、换算，
  只有最后一步的传输不同（`WriteVariable` vs `WriteVariableNow`）。
  分开写两套必然出现"普通写能过、插队写过不了"的口径漂移。
- Manager 只负责传输：`rawValue` 已是设备原始值；它不重复做工程值换算，也不更新镜像。
- 镜像由 Model 在写成功后更新（与普通写一致），因此变量管理/组态画面看到的就是刚写入的值。

## 四、车道与延迟边界

| 车道 | 内容 | 优先级 |
| --- | --- | --- |
| L0 | 插队写（本插件） | 最高 |
| L1 | 插队读（「立刻读」） | 高 |
| L2 | 常规写 | 中 |
| L3 | 常规读 | 低 |
| L4 | 轮询 | 最低，且段间让路 |

- 同一车道 FIFO；跨车道严格优先级；一次只执行一条命令。
- **不能抢占正在执行的命令/段**：插队只改变排队位置，不打断 `ReadBytes`/`Write`。
- 轮询在**段与段之间**检查插队队列；`Yielded` 时不 `MarkSuccess`，下一拍重跑整组。
- 典型等待：空闲时一个设备往返；轮询中一段 + 一个往返；常规命令执行中 = 当前命令完成。
- 最坏等待 = 连接配置的写超时（设备无响应）。插队不能取消已发出的 I/O。

## 五、写租约

`WriteLeaseRegistry` 的键是 `{ConnectionName}.{Name}`，owner 取 `WriteScope.CurrentOwner`
（流程/并行分支各一个），流程外的手动写用 `manual`。

- 同一 owner 重复写：允许（同一流程里连续写同一个点很正常）；
- 不同 owner：拒绝，错误消息带当前持有者；
- `AllowSharedWrite=true`：不互斥，最后写入者赢。

插队写**同样取租约**：不能因为"急"就绕过互斥，否则并行分支的冲突会以"设备偶尔动作错"
的形式出现，比直接拒绝难查得多。需要"永远写赢"的公共点，显式勾「允许共享写」。

## 六、只读区与类型

- 只读区：Modbus 离散输入 / S7 输入映像 `I`。插件先拦一次，Model 再拦一次（双保险）。
  这样即使将来有别的入口绕过插件，写链路也不会把只读区命令发到设备。
- 文本解析：`ValueTextCoercion` 与「变量赋值」的全局变量写入口**共用同一份实现**
  （不变区域性、数值拒绝千分位、只放宽文本）。
- 非文本值（上游链接）不在这里强制转换；`ConvertToRaw` 按现有写链路处理。
  如果上游类型与变量声明类型不兼容，错误会在写入前或设备返回时明确出现。

## 七、失败语义

- 变量不存在/不是通讯变量/缺连接或地址/只读区/离线：插件在发起前 `Fail`，不占队列。
- 插队队列满：Manager 抛 `InvalidOperationException`，Model 转成 `error`，插件 `Fail`；
  **不回退常规队列**。
- 租约冲突：Model 在写之前拒绝，错误带 owner。
- 设备写失败：Manager 记 `LogError` + `OnConnectionError` 后抛出；插件 `Fail`。
  **不调用 `OnConnectionBroken`**：一次写失败不把好连接打进重连循环。
- **不等待模式**：`TryTriggerWriteNow` 返回 false 只覆盖"入队失败"（连接不存在/未连接、
  变量未注册、插队队列满、Worker 已释放）；返回 true 后设备晚到的失败只走日志 +
  `OnCommunicationError`，**不改变步骤结果**，也**不更新镜像**。
- 取消：只在发起前检查；已发出的 I/O 等它返回或超时。

## 八、边界与已知限制

1. **一个步骤一个变量**：需要多个点就放多个步骤。
2. **不是事务**：写成功后设备业务逻辑是否执行、是否需要触发位，由设备决定。
3. **不自动重试**：重试可能重复下发命令；需要重试请在流程层加条件/循环。
4. **同值也写**：工业写是命令，不做"值没变就跳过"的优化。
5. **不等待模式是 at-most-once**：只保证入队，不保证执行；进程退出/连接停止时队列里的
   命令会被放弃（`ReleasePendingItems` 触发 `OnAbandon`，晚到失败进日志）。
5. **名字寻址**：变量改名后步骤报"找不到变量"，需要重选。
6. **高频调用**：插队队列容量 256、入队等待 200ms；周期性同步请用常规写/扫描组。
7. **试运行**：`PluginTestRunner` 透传 `ICommVariableAccess`，面板上点「执行」走同一套插队车道。

## 九、守门与验证

- 通讯层：`CommChecks` C29（插队顺序、队列满、段间让路、单变量读取）。
- 插件层：`FlowCanvasChecks` `[CN]` 组（元数据、成功/失败路径、只读区、租约、取消、
  文本 `"21"` → Int32 `21`）。
- 手册：`HelpManualCheck` HM 组（打包、分册、页序、档位厚度）。
- 变异反证：去掉 `DrainQueues` 的插队写一行 → C29 顺序断言必红；
  去掉 `TryWriteCore` 的文本解析 → `[CN5]` 必红；去掉只读区检查 → `[CN6]` 必红。

## 十、相关源码

- 插件：`Plugins\Plugin.Comm\CommWriteNowPlugin.cs`
- 契约：`Shard\Core.Interfaces\ICommVariableAccess.cs`
- 宿主实现：`Core\Binding\CommVariableAccess.cs`、`Core\Models\Variable\NetworkVariableModel.cs`
- 文本解析：`Core\Helpers\ValueTextCoercion.cs`
- 通讯层：`Communication\Communications\Manager\ConnectionWorker.cs`、
  `AdvancedCommunicationManager.cs`
- 候选来源：`Communication\CommunicationModule.cs` 的 `RegisterStepConfigOptions`
