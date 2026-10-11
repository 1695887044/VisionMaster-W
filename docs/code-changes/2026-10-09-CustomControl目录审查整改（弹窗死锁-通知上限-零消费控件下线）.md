# 2026-10-09 CustomControl 目录审查整改（弹窗同步路径死锁 / 通知容量上限 / 4 个零消费控件下线）

- 日期：2026-10-09
- 项目：UI（WPF / .NET 9）+ 检查工程 UIThemeSmokeTest + 探针 _LogViewProbe（外加一处并发编辑的编译修复，见「六」）
- 范围：2 处修复（`Message\EasyDialog.cs`、`Message\Notifier.cs`）+ 4 个控件与 3 个主题下线（移到 `_Retired\2026-10-07-unused-controls\`）+ `Themes\Generic.xaml` 移除 3 行合并 + 探针新增 1 条断言
- 类型：代码审查整改（2 处"会炸/会挂"缺陷 + 死代码下线）
- 口径：**构建** = `dotnet build`（UI / VisionMaster / UIThemeSmokeTest，Debug 与 Release）；**冒烟** = `UIThemeSmokeTest.exe`；**探针** = `_LogViewProbe.exe`（离屏渲染 + 控制台断言）

---

## 一、起因

上一轮对 `UI\Controls\CustomControl` 目录做了一次全量审查（12 个控件、约 4900 行），给出"先修两处会炸的 → 做零消费控件去留决定 → 补断言 → 再谈新增控件"的顺序。本次执行前三条中的 1、2、3。

## 二、消费方普查（做决定的依据）

普查全仓 `*.cs` / `*.xaml` 引用（排除 `obj/`、`bin/`、探针工程、`_MvvmDemo`、主题字典自身）：

| 控件 | 引用数 | 控件 | 引用数 |
| --- | --- | --- | --- |
| Notifier | 148 | DialogArrayEditor | 2 |
| EasyDialog | 57 | **AlarmBar** | **0** |
| FlatPropertyGrid | 1 视图 + 代码若干 | **BindableParamBox** | **0** |
| LogConsole | 1（LogView） | **CommunicationTextBox** | **0** |
| OverlayHost | 1（Shell） | **StatusIndicator** | **0** |
| | | **Popover** | **0** |
| | | **CardPropertyGrid** | **0**（仅 `_StyleProbe`） |

## 三、修复 1：`EasyDialog` 两处会炸的（`Message\EasyDialog.cs`）

### 3.1 同步路径的 DispatcherFrame 死锁

`RunSync`（`ShowSync` / `ShowTextInputSync` / `ShowPropertyGridSync` 三个同步 API 都走它）原先：

```csharp
_ = asyncMethod().ContinueWith(t => { result = t.Result; frame.Continue = false; }, TaskScheduler.Default);
Dispatcher.PushFrame(frame);
```

**任务一旦失败，`t.Result` 在 `frame.Continue = false` 之前抛出 → 放行那行永远执行不到 → UI 线程永久卡死在 `PushFrame` 里**（连"操作失败"的提示都弹不出来）。改成 `try/catch/finally`：异常存进局部变量，`frame.Continue = false` 放 finally，`PushFrame` 之后照旧 `throw`（保持原来的 `AggregateException` 语义，只不再卡 UI）。

### 3.2 裸 `FindResource` 兜底

取消按钮图标的 `Fill = (SolidColorBrush)Application.Current.FindResource("TextRegular")` —— 同文件其它 8 处资源都走 `TryResource`（带兜底），唯独这一处直接 `FindResource`：键不在（换主题 / 宿主没合并那本字典）就是 `KeyNotFoundException`，**整个弹窗引擎当场崩**。改为 `TryResource<Brush>("TextRegular") ?? Brushes.DimGray`。

### 3.3 顺带：把漏掉的三处硬编码色归到令牌

同一文件顶部注释写着"视觉一律取 Fluent 令牌，不再自己硬编码"，但标题/正文字色与文本输入框边框仍是硬编码。改为（都带旧值兜底）：
`FluentTextPrimaryBrush`（标题）、`FluentTextSecondaryBrush`（正文）、`FluentBorderStrongBrush`（输入框边框）。

## 四、修复 2：`Notifier` 判空 + 容量上限（`Message\Notifier.cs`）

| 项 | 原状 | 改后 |
| --- | --- | --- |
| 无 WPF 上下文 | `Application.Current.Dispatcher` 直接解引用 —— 148 处调用里相当一部分就在异常处理与关闭路径上，"提示错误的调用"自己先 NRE | `var dispatcher = Application.Current?.Dispatcher; if (dispatcher == null) return;` 静默丢弃 |
| 容量 | 无上限：报警风暴（通信断线刷错误那种）会同时堆几十张卡片糊满右下角 | `MaxVisible = 5`，`Add` 之后立刻裁剪（新卡片还没渲染就被移除，不会闪），丢最旧 |
| 画刷 | 每条通知都 `new SolidColorBrush(...)` | 4 个等级色画刷改成静态只读并 `Freeze()`（冻结后可跨线程共享、WPF 不必再包装） |

## 五、修复 3：4 个零消费控件下线（决定 + 执行）

### 5.1 决定与理由

| 控件 | 决定 | 理由 |
| --- | --- | --- |
| `AlarmBar` | **下线** | SCADA 已有现役实现 `Scada.Controls\Elements\AlarmBannerElement.cs` + `AlarmBannerRow.cs`，职责重叠 |
| `BindableParamBox` | **下线** | 现役实现是 `Shard\Core.Controls\LinkableValueEditor`（`IsLinked` / 解绑按钮 / 占位提示一应俱全）；本件是未接线旧原型，且它的 `PART_UnlinkBtn` 用匿名 lambda 订阅（模板重建会重复挂命令） |
| `CommunicationTextBox` | **下线** | 半成品：`AutoRefresh` / `RefreshIntervalMs` 两个 DP 没实现（变更回调是空方法）、`Dispose()` 空实现、`float.Parse` 未指定 `InvariantCulture`、命名空间是全目录唯一的 `UI.Controls.CustomControl` |
| `Popover` | **下线** | 与 WPF 原生 `Popup` / `ToolTip` 重叠，无使用计划 |
| `StatusIndicator` | **保留** | 全仓无重复实现的通用状态灯原语。归宿：通信连接状态灯（绿/黄/红 + 悬停显示最近错误 + 点击重连） |
| `CardPropertyGrid` | **保留** | 与 `FlatPropertyGrid` 是"属性网格两形态"设计对的一半（共用 `PropertyGridBase` / `UseCardLayout`），删它会破坏刚抽好的基类抽象。待接线：插件配置壳 / SCADA 属性面板可切卡片式 |

### 5.2 执行方式：**不硬删，移到 `_Retired`**

本仓**没有版本控制**，`del` 掉的代码不可恢复。所以 7 个文件（4 个 `.cs` + 3 个主题 `.xaml`）用 `move` 原样移到
`_Retired\2026-10-07-unused-controls\`（保持原目录结构），并写了 `README.md`：普查口径、下线理由、**逐文件的恢复步骤**（放回路径 + 要补回的 `Generic.xaml` 三行原文 + 重建命令 + "只补一半会运行期抛异常"的提醒）。
该目录不参与任何构建、不在任何 `.sln` 里；要彻底删除时直接删目录即可。

> 目录名里的日期是**动议日**（2026-10-07 做的审查），实际执行在 10-09，为与审查结论对齐保留原名。

### 5.3 主题字典同步移除

`UI\Controls\Themes\Generic.xaml` 第三层删掉 3 行合并（Popover / AlarmBar / BindableParamBox），并留注释说明"留着会指向不存在的资源，运行期直接抛"。
移除后该层剩：`LogConsole.xaml` / `StatusIndicator.xaml` / `PropertyGrid.xaml` / `BtnExtension.xaml` / `BorderExtension.xaml`。

## 六、过程中撞到一次并发编辑（**不是我写的代码**）

执行期间 `VisionMaster\Services\StepParameterDialog.cs` 在 22:28 被**另一个进程改过**（我的文件是 22:29–22:31），它把并行分组出口的版本推进写成了：

```csharp
workspace?.CurrentFlow?.Version++;   // ← 条件访问表达式不是可赋值目标：CS1059
```

这一行让**整个 VisionMaster 构建失败**（连带 UIThemeSmokeTest 也起不来，冒烟一度只能跑旧产物）。按该行上方注释的原意做了一行最小修复并**在代码里标注了"2026-10-07 修"**：

```csharp
if (workspace?.CurrentFlow is { } currentFlow)
    currentFlow.Version++;
```

同一时间 `UIThemeSmokeTest\Program.cs` 也在被并发修改（分发行多出了 `flowCanvasFailures`，`RunLogConsoleChecks` 由 `:1456` 位移到 `:1459`）。按 AGENTS.md R9（"最近几分钟被改过 = 有并发任务在跑，先别动"），**该文件本次我没有修改**——原计划放进它的 Notifier 断言改放进了探针 `_LogViewProbe`（见 7.2）。

## 七、验证证据

| 项 | 结果 | 口径 |
| --- | --- | --- |
| 构建（Debug） | `Build succeeded. 0 Error(s)`；`EasyDialog` / `Notifier` / `Generic.xaml` 相关告警 **0 条** | `dotnet build UIThemeSmokeTest -c Debug` |
| 构建（Release） | `Build succeeded. 0 Error(s)`；`VisionMaster.dll` / `UI.dll`（Release）时间戳 22:36 | `dotnet build VisionMaster -c Release` |
| 冒烟 | `EXIT=ZERO`、`[FAIL]` 零条、**33 条 PASS**；四个分区全过：`=== OK: Shell.xaml 全树模板实例化无异常 (Fluent=True) ===`、多格布局、GridView、LogConsole | `UIThemeSmokeTest.exe`（输出 `%TEMP%\vm_smoke13.log`） |
| 主题字典少三项后仍可用 | 同上第一条：Shell 全树模板实例化（会合并 `Generic.xaml`）无异常 —— 证明移除 3 行合并没有留下指向空资源的引用 | 同上 |
| Notifier 容量上限 | `[PASS] Notifier 上限：塞 8 条 → 可见 5 条（期望 5），最后一条=storm 7（期望 storm 7）`（丢的是最旧的） | 探针 `_LogViewProbe.exe`（新增 `CheckNotifierCap()`） |
| 零外部引用 | 下线前普查：3 个主题文件在 `Generic.xaml` 之外 0 处引用；4 个类型在 UI 目录之外 0 处代码引用 | `findstr /s` 全仓（`*.cs` / `*.xaml`，排除 obj/bin/探针/Demo） |

## 八、未决与后续

- **`EasyDialog` 仍无自动化断言**（原计划的第 4 项）：`RunSync` 放行、`SetResult` 收口、Esc/关闭收口、锁不泄漏这几条都在"要开真窗口 + 需要点击"的路径上，得用无窗口宿主 + 定时触发的方式设计，本次没做。**这是目前最值得补的一块**（57 处调用、含锁与调度黑科技、零覆盖）。
- **两个保留控件的接线**（原第 5 项）：`StatusIndicator → 连接状态灯`、`CardPropertyGrid → 插件配置壳/SCADA 属性面板可切卡片式`。都还没动。
- **新增控件**（原第 5 项）：`NumericBox`（统一四处分散的数值解析，含 InvariantCulture）、`FilterableComboBox`（把"可搜索选择"从对话框压回行内）、`CycleTimeGauge`（MonitorView 的节拍表）。
- **`CommunicationTextBox` 下线后，"SCADA 运行时写值"这条需求没有控件承接**：它原本的 `ICommunicationReader` 抽象（`Read<T>` / `Write`）如果将来要用，从 `_Retired` 里取回并按上面 5.1 列的四个缺陷修干净再上。
- **并发编辑提醒**：本次执行期间有另一个进程在改 `VisionMaster\Services\StepParameterDialog.cs` 与 `UIThemeSmokeTest\Program.cs`（见「六」）。`StepParameterDialog.cs` 我只做了一行编译修复，**业务改动请以那边的版本为准**。
