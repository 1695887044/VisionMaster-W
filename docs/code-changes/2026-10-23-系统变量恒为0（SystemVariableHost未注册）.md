# 系统变量恒为 0（SystemVariableHost 从未注册进容器）

日期：2026-10-23
状态：已修复并验证

## 1. 现象

方案 `综合测试台.vms` 里有一张运动卡 `ZMotion_Virtual`（X/Y/Z 三根轴 `Enabled: true`），
按设计应有 3 × 12 = 36 条系统变量（`System.Motion.{轴名}.{字段}`）。
实际「变量管理 → 系统变量」恒为 **0**；左栏「通信连接」下的 PLC_Modbus / Meter_Modbus / PLC_S7
分别显示 4 / 1 / 3，那是网络变量（`NetworkVariableModel`），本来就不进系统变量分区。

## 2. 根因

`VisionMaster/Modules/MotionModule.cs:82` 直接 `c.Resolve<SystemVariableHost>()`，
而全仓**没有任何 `RegisterSingleton<SystemVariableHost>`**。

Unity 对未注册的具体类型会自行构造，连构造参数 `ObservableCollection<IVariable>` 也新造一份空的：

- `SystemVariableCatalog.AttachToHost(host)` 据此进入"托管模式"；
- `SystemVariableCatalog.Rebuild` 从此只 `RaiseChanged()`，不再写 `workspace.SystemVariables`
  （`Core/Binding/SystemVariableCatalog.cs:263-272`）；
- 36 条变量全部写进宿主私有那份**孤儿集合**，界面读的是 `workspace.SystemVariables` → 恒为 0。

这正是 `SystemVariableCatalog` 构造函数注释里点名过的失败模式（"投影全部写进一个没人读的孤儿集合，
而且不报任何错"），这次从 DI 一侧漏了进来。启动日志里没有 `[模块] 运动卡模块 初始化失败`，
说明 Resolve 没抛异常，是静默走通的那条路。

## 3. 修法

`VisionMaster/App.xaml.cs` 在 `IWorkspaceManager` 注册之后补一条：

```csharp
containerRegistry.RegisterSingleton<SystemVariableHost>(c =>
    new SystemVariableHost(c.Resolve<IWorkspaceManager>().SystemVariables));
```

集合必须取 `workspace.SystemVariables` 本体（`IWorkspaceManager.SystemVariables` 是只读属性、
实例在 `WorkspaceContext` 构造期固定），不能新造。

## 4. 验证

1. **探针（前后对照，临时工程，已删）**：同一份 `VM.Core` + `Unity.Container`，
   两个 `WorkspaceContext` 各接 2 根假轴：

   ```
   A_OLD_WIRING  workspace.SystemVariables=0      ← 未注册（复现缺陷）
   B_NEW_WIRING  workspace.SystemVariables=24     ← 注册后（2 轴 × 12 字段）
   B_SAMPLE      System.Motion.X.Position
   ```

2. **构建**：`dotnet build VisionMaster\VisionMaster.csproj -c Debug -m:1 -nodeReuse:false`
   → 0 错误；`VisionMaster.dll`（01:01:57）晚于 `App.xaml.cs`（01:01:34）。
   日志：`artifacts/logs/20261023/build-sysvar-fix.log`。

3. **真机**：启动 `VisionMaster.exe`，加载 `综合测试台.vms`，打开「系统 → 全局变量」：
   左栏「系统变量」显示 **36**，明细为 `System.Motion.X.Position` / `.Velocity` / `.Moving` …
   全部「只读」；`AlarmMessage` 一列显示「运动卡「ZMotion_Virtual」当前为 Closed」
   （卡未连上时按 `ScadaSample.Invalid` 口径写原因，不是编 0）。
   截图：`artifacts/render/20261023/sysvar-36.png`。

## 5. 未做的守门（已知缺口）

`ScadaChecks/AxisPanelChecks.cs:607` 是直接 `new SystemVariableCatalog(workspace, registry)`，
**不接宿主**，走的是 `ApplyToCollection` 分支，所以现有断言全绿也覆盖不到生产容器这条接线。

要防回归，二选一（尚未落地）：

- 断言侧：用同一套 Prism/Unity 容器解析一次 `SystemVariableHost`，断言它的目标集合
  与 `workspace.SystemVariables` 是同一实例；
- 运行期：启动自检里核"方案有启用轴 → 系统变量数 > 0"（`Warning` 级，不进关键路径，见 R57）。

另一个同族事实：IO 运行层（`IoModule` / `IoProvider` / `IoVariableCatalog` / `IoConfigView`）
在当前工作树里已不存在，只剩 `Shard/Core.Interfaces/Io` 的描述符与迁移器；
`VariableType.Io` 全仓没有任何生产者。因此"加 IO/通讯硬件就出系统变量"这条路当前没有实现，
加 PLC 不会改变系统变量数量。
