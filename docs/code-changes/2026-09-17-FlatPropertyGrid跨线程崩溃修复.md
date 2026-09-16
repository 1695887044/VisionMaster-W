# FlatPropertyGrid 跨线程访问崩溃修复（t11）

- 日期：2026-09-17
- 阶段：t11（通信重构收尾后的运行时崩溃修复）
- 状态：已完成，VS2022 MSBuild 全量编译 EXIT=0（`error (CS|MSB|NETSDK)` 零匹配）
- 用户拍板范围：**控件兜底 + Manager 同步**（两处都改）
- 验证方式：编译验证通过；**运行验证待用户在真机场景确认**

---

## 一、需求与决策

用户在运行软件时抛出运行时异常，栈顶落在属性网格控件上：

```
System.InvalidOperationException
  HResult=0x80131509
  Message=调用线程无法访问此对象，因为另一个线程拥有该对象。
  Source=WindowsBase
  StackTrace:
   在 System.Windows.Threading.Dispatcher.<VerifyAccess>g__ThrowVerifyAccess|7_0()
   在 System.Windows.DependencyObject.GetValue(DependencyProperty dp)
   在 UI.CustomControl.FlatPropertyGrid.get_BindingObject()   ... FlatPropertyGrid.cs: 第 36 行
   在 UI.CustomControl.FlatPropertyGrid.<OnBindingObjectPropertyChanged>d__19.MoveNext() ... 第 102 行
```

栈帧含义非常明确：**后台线程**跑进了 `OnBindingObjectPropertyChanged`，第一句读
`BindingObject`（依赖属性）时被 `Dispatcher.VerifyAccess` 拦下抛异常。

处置决策（AskUserQuestion 拍板）：

| 选项 | 决策 |
| --- | --- |
| 只改控件层（治果） | 否 |
| 只改 Manager 层（治因） | 否 |
| **控件兜底 + Manager 同步（两处都改）** | **采用** |

---

## 二、根因分析（三层）

### 第 1 层：依赖属性（DP）有线程亲和性

`BindingObject` 是 `DependencyProperty`。`GetValue`/`SetValue` 内部第一步就是
`Dispatcher.VerifyAccess()`——只有**创建该控件的那条线程（UI 线程）**能访问，
其它线程一碰就抛 `InvalidOperationException`。这不是 bug，是 WPF 的硬约束。

### 第 2 层：这里是「直接订阅 INPC」，不是 WPF 绑定引擎

```csharp
newNotifier.PropertyChanged += control.OnBindingObjectPropertyChanged;
```

`+=` 是**直接订阅**：事件在**属性变更源线程**上同步执行处理器。
对比之下，如果通过 WPF 绑定引擎（`{Binding Xxx}`）消费 `PropertyChanged`，
绑定引擎会**自动封送到 UI 线程**——所以"绑定到界面的属性被后台线程改"平时并不炸，
一炸就是这种"直接订阅"的控件。

### 第 3 层：谁在后台线程改 `BindingObject` 指向的对象？

| 链路 | 改的属性 | 变更源线程 | 宿主 |
| --- | --- | --- | --- |
| ① 连接状态 | `CommunicationConfig.State` / `LastConnectedTime` | 连接专属线程（ConnectionWorker 状态机回调） | 通讯设置对话框的状态徽标 |
| ② 轮询镜像 | `NetworkVariableModel.Value`（`UpdateMirrorValue`） | 轮询线程（ConnectionWorker 的 PollAction） | 变量管理列表 |
| ③ 算子参数 | 各算子参数属性 | 流程引擎 / 插件线程 | `PreProcessingView.xaml` 的 `BindingObject="{SelectedOperator}"` |

链路 ① 是本段唯一能从代码上"治因"的：`AdvancedCommunicationManager.OnWorkerStateChanged`
由 Worker 线程回调，却直接给**绑定到界面的模型**赋值 —— 它就是在源头放枪的人。

### 附带隐患（本次**未修**，记入已知边界）

`_currentNotifier.PropertyChanged -= ...` 只在 **BindingObject 被换掉**时执行，
**弹窗关闭 / 控件卸载都不退订** → 已关闭的控件仍会被后台线程回调（订阅泄漏 + 僵尸控件）；
同时 `_currentNotifier` 强引用宿主对象，阻止其被回收。

---

## 三、修改文件清单

1. `UI/Controls/CustomControl/PropertyGrid/FlatPropertyGrid.cs` — 处理器开头加线程兜底（治果）
2. `Communication/Communications/Manager/AdvancedCommunicationManager.cs` — 状态同步改为回 UI 线程赋值（治因）

---

### 1. FlatPropertyGrid：处理器开头线程兜底（治果）

```csharp
// 🌟 注意：方法签名加上了 async 关键字！
private async void OnBindingObjectPropertyChanged(object? sender, PropertyChangedEventArgs e)
{
    // 【线程兜底】这里是"直接订阅 INotifyPropertyChanged"，不是 WPF 绑定引擎——
    // 事件在**属性变更源线程**上执行。而属性变更可能来自后台线程：
    //   ① 连接专属线程改 CommunicationConfig.State（连接/断开/重连）
    //   ② 轮询线程改 NetworkVariableModel 的镜像值
    //   ③ 流程引擎/插件线程改算子参数（PreProcessingView 的 BindingObject）
    // 属性值本身可以跨线程读，但 BindingObject 是 DependencyProperty，
    // 有严格的线程亲和性：只有创建本控件的 UI 线程才能 GetValue，否则
    // Dispatcher.VerifyAccess 直接抛 InvalidOperationException。
    // 所以统一先封送回 UI 线程再重入本方法，把三条来源的隐患一次抹平。
    if (!Dispatcher.CheckAccess())
    {
        VisionMaster.Helpers.SafeDispatch.BeginInvoke(() => OnBindingObjectPropertyChanged(sender, e));
        return;
    }

    if (BindingObject == null || string.IsNullOrEmpty(e.PropertyName)) return;
    ...
}
```

要点：
- `Dispatcher.CheckAccess()` 是 `DispatcherObject` 的实例属性，无需额外 `using System.Windows.Threading;`；
- 用 `SafeDispatch.BeginInvoke`（而非裸 `Dispatcher.BeginInvoke`）→ 软件关闭时 Dispatcher 已停，
  该投递被**静默丢弃**，不会把 `TaskCanceledException` 抛回属性变更源线程；
- **重入**而非复制逻辑：封送后从方法头再走一遍，只有一条代码路径，不会两边逻辑漂移。

### 2. AdvancedCommunicationManager：状态同步回 UI 线程赋值（治因）

```csharp
private void OnWorkerStateChanged(string connectionName, ConnectionState oldState, ConnectionState newState)
{
    if (_configCache.TryGetValue(connectionName, out var config))
    {
        // 【必须回 UI 线程赋值】本方法由连接专属线程回调，而 config 是**直接绑定到界面的模型**
        // （通讯设置对话框的状态徽标等）。赋值会触发 INotifyPropertyChanged，
        // 若在后台线程发出，凡是"直接订阅 PropertyChanged"的控件（如 FlatPropertyGrid）
        // 就会在后台线程读到自己的 DependencyProperty → 抛
        // "调用线程无法访问此对象，因为另一个线程拥有该对象"。
        // 这里是整条链路的源头，改在源头赋值最彻底；控件层的线程兜底只是第二道保险。
        VisionMaster.Helpers.SafeDispatch.BeginInvoke(() =>
        {
            if (newState == ConnectionState.Connected)
                config.UpdateLastConnectedTime();

            config.State = newState;
        });
    }

    // 连上即重编译轮询计划：……（注释原文保留）
    // 纯数据结构操作（不动 UI），保持在 Worker 线程同步执行，避免改变建连时序
    if (newState == ConnectionState.Connected)
        RebuildPollPlan(connectionName);

    OnConnectionStateChanged(connectionName, oldState, newState);
}
```

要点：
- `Communication` 工程未引用 `System.Windows`，因此用全限定名 `VisionMaster.Helpers.SafeDispatch.BeginInvoke`（`VM.Core` 已引用，无需加 using 与工程引用）；
- **只搬 `config` 赋值**：`RebuildPollPlan` 是纯数据结构操作（构造 `PollBatchPlanner` 并挂 `worker.PollAction`），
  留在 Worker 线程同步执行，不改变"连上即重编译"的时序；
- **时序保证**：`SafeDispatch.BeginInvoke` 按 Dispatcher 队列 FIFO 执行，而 ViewModel 侧
  `OnConnectionStateChanged` → `SafeDispatch.BeginInvoke(HandleStateChanged)` 也是同一条队列投递，
  先投先执行 → UI 侧读到的 `config.State` 一定是已更新值，不会滞后。

---

## 四、验证结果

```
MSBuild.exe VisionMaster.sln /t:Build /m /v:m /nologo /flp:logfile=build-t11-msbuild.log
EXIT=0
```

- 日志中 `error (CS|MSB|NETSDK)` 匹配数：**0**（Grep 实证）
- 18 个工程全部产出 DLL；输出中仅有既有的可空性警告（CS8618/CS8622/CS8600 等），
  本次两处改动未引入任何新警告
- 运行验证：**待用户在实际场景复现**（连接/断开、变量轮询、算子参数刷新）

---

## 五、已知边界

1. **弹窗关闭不退订**（本段未修）：`OnBindingObjectChanged` 只在 BindingObject 被替换时退订。
   控件随弹窗关闭后仍留在委托链上，`_currentNotifier` 也强引用宿主对象。
   本次修的是"崩溃"，泄漏是另一类问题，需在 `Unloaded`/`OnDetached` 处补退订，宜单独一轮处理。
2. **链路 ② 与 ③ 只在控件层兜底**：轮询镜像值（`NetworkVariableModel.UpdateMirrorValue` 由轮询线程调用）
   与流程引擎改算子参数，仍会在后台线程发 INPC；只是不再让控件崩。
   若后续要"治因"，应把这两处也搬到 `SafeDispatch` 上赋值，或在数据模型层统一封送。
3. **`config.State` 赋值变为异步**：极短时间内（一帧内）读 `config.State` 可能读到旧值。
   现有读方全在 UI 线程（ViewModel 判断"当前是否已连接"），且状态事件同队列投递，未观察到影响。
4. **网络变量写入路径仍是同步阻塞语义**（t8 已知边界）：`ExecuteTestConnection` 内部
   `worker.Invoke(c => c.TestConnection()).GetAwaiter().GetResult()`，设备不可达时会等一个 socket 超时。
5. **不改动项**（沿 t8 决定）：B5 协议选项不一致；`IsRunning` 手工置位。
6. **`AddConnection` 里 `config.State = ConnectionState.Disconnected`（L157）仍在调用线程上赋值**：
   现调用方均为 UI 线程（添加/导入配置），暂无风险；若将来出现后台线程调用 `AddConnection`，
   按同样方式回 UI 线程赋值即可。

---

## 六、一句话总结

> DP 只能在 UI 线程读，而"直接订阅 INPC"让事件跑在源线程上——
> 于是"后台线程改模型" + "控件直接订阅" 必然撞出 `InvalidOperationException`。
> 本次**治因**（Manager 回 UI 线程赋值）+ **治果**（控件入口统一封送），一次覆盖全部三条后台链路。
