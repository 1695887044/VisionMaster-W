# 2026-09-15 日志/刷新封送 Dispatcher 关闭竞态修复

## 一、需求与决策

### 现象
运行流程（For+延时循环）后关闭软件，抛出未处理异常：
`System.Threading.Tasks.TaskCanceledException: A task was canceled.`
抛出点：`LogViewModel.cs L18` 的 `Dispatcher.Invoke`，调用链为
`流程线程 → DelayPlugin.RunAlgorithm → LogService.Info → OnLogReceived → Dispatcher.Invoke`。

### 根因时序
```
流程线程（ThreadPool）                 UI 线程（Dispatcher）
──────────────────────                ─────────────────────
DelayPlugin 循环打印 Info(...)         用户点关闭 → Dispatcher 开始关机
Invoke 投递操作并【同步等待】UI 执行      关机 = 取消所有挂起的 DispatcherOperation
Invoke 抛出 TaskCanceledException ✗     ← 等待中的操作被"拦腰取消"
异常沿调用栈抛回插件/引擎层 → 崩溃
```
本质：**同步 `Dispatcher.Invoke` 把 UI 线程的生死绑到了流程线程的调用栈上**。软件关闭时 UI 先死，
后台线程后停，中间窗口期内的每一次同步封送都是一颗地雷。

### 决策
1. 病灶点 `LogViewModel` 改**异步投递**（`BeginInvoke`，fire-and-forget）；
2. 全库排雷：同类"后台线程路径上的同步 Invoke"共 5 处，统一换成新增的
   `UI.Core.SafeDispatch.BeginInvoke`（关机守卫 + 竞态窗口兜底吞取消）；
3. 不阻塞、不引返回值语义改动；`ElementExtention` 的 2 处同步 Invoke **保留**——
   它们需要返回值（DependencyProperty 跨线程读写），且调用方均在 UI 侧，属正当用法。

## 二、修改文件清单

| 文件 | 修改点 |
|---|---|
| `UI/Controls/Core/SafeDispatch.cs` | **新增**：统一守卫——`Application.Current` 判空、`HasShutdownStarted/Finished` 拦截、`BeginInvoke` 不阻塞、竞态窗口 catch 吞掉取消异常 |
| `VisionMaster/ViewModels/LogViewModel.cs` | L18 事故点：`Dispatcher.Invoke` → `SafeDispatch.BeginInvoke`；日志不再阻塞产出线程，关闭期静默丢弃（文件通道独立，日志不丢） |
| `VisionMaster/ViewModels/DialogViewModels/GlobalVariableViewModelBase.cs` | 变量集合变化 → RefreshTree 改异步投递（运行中增删变量由流程线程触发，同款地雷） |
| `VisionMaster/ViewModels/DialogViewModels/GlobalVariableManagerViewModel.cs` | 本地变量值变化 → 全树重建改异步投递（还顺带治了"流程线程干等 UI 重建整棵树"的性能坑） |
| `UI/Controls/CustomControl/PropertyGrid/FlatPropertyGrid.cs` | 防抖 50ms 后的重绘改异步投递（pending 标志在回调 finally 中复位，语义不变） |
| `UI/Controls/CustomControl/PropertyGrid/CardPropertyGrid.cs` | 同上（修复中曾误删 `await Task.Delay(50)` 防抖行，已当场补回并复核） |

## 三、验证结果

- `dotnet build UI.csproj -c Debug`：**0 错误**
- `dotnet build VisionMaster.csproj -c Debug`：**0 错误**
- 运行期验证：重启后"运行流程 → 直接关窗口"应无 TaskCanceledException 弹窗（待用户实测）

## 四、已知边界

1. **日志窗口在关闭瞬间可能丢最后几条**：关机器后 UI 不再追加，属预期（文件日志完整）。
2. **流程线程与关机的更大竞态仍在**：本修复治的是"UI 封送层"；理想终态是退出流程先
   `Cancel + 等待会话结束` 再关窗（App.OnExit 编排），属关闭时序工程，另立课题。
3. **`SystemLogs` 无上限裁剪**：长时间循环运行日志会无界增长拖慢 UI，未在本次范围。
4. `ElementExtention` 跨线程读写 DependencyProperty 仍用同步 Invoke（需返回值），
   若未来从后台线程大量调用，同样有理论竞态窗口，暂不动。
