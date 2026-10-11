# 2026-10-10 连接状态灯：StatusIndicator 重写并接线到通信设置（新控件第 2 个）

- 日期：2026-10-10（动议与实现均在当日；上一轮 NumericBox 的记录是 2026-10-09）
- 项目：UI（WPF / .NET 9）+ 探针 _LogViewProbe + 样式参考 `docs/UI样式参考/`
- 范围：1 控件重写（`UI\Controls\CustomControl\StatusIndicator.cs`）+ 1 主题重写（`Themes\StatusIndicator.xaml`）+ 1 宿主模板重定向（`Themes\Dialog\Dialog.Chip.xaml` 的状态胶囊）+ 1 处既有隐患修复（同文件的字体键引用）
- 类型：新增控件（**带真实消费方**：通信设置"状态"列）+ 死代码复活（该控件此前"保留但零消费"）
- 口径：**构建** = `dotnet build`（Release；Debug 被 VS 与运行中的宿主占用，见「五」）；**冒烟** = `UIThemeSmokeTest.exe -c Release`；**探针** = `_LogViewProbe.exe -c Release`（断言 + 渲染）

---

## 一、落点确认（动手前）

| 问题 | 结论 |
| --- | --- |
| 谁在用状态指示？ | 通信设置视图的"状态"列 —— 用的 `DialogStatusPillTemplate`，**全仓仅此一处引用**（改它安全） |
| 那个胶囊是怎么实现的？ | 纯 XAML：四套 `DataTrigger` × 圆点填充 / 文字颜色 / 文案，再加 `HasError` 角标与 ToolTip —— **100 多行、全都手搓在 DataTemplate 里** |
| 有检查盯着它吗？ | 没有（`FlowCanvasChecks`/`ScadaChecks`/`CommChecks`/`UIThemeSmokeTest` 都不引用该模板） |
| 状态枚举在哪？ | `VisionMaster.ConnectionState { Disconnected, Connecting, Connected, Error, Reconnecting }`（`Core\Enums.cs:457`）——**不进 UI 库**：控件不认识业务枚举，映射留在宿主模板里 |

## 二、重写 StatusIndicator

旧版是 VSM 驱动的"开关灯"（`IsActive` / `ActiveBrush` / `InactiveBrush` / `IsPulsing` + Content），**全仓零消费**。新形状：

| 依赖属性 | 作用 |
| --- | --- |
| `Level`（枚举 `StatusLevel { Off, Ok, Busy, Error }`） | 四档：颜色 + 是否脉动都由它决定。枚举**刻意不用 `ConnectionState`** —— 控件认识业务枚举 = 每加一种业务都要改控件 |
| `StatusText` | 圆点右侧文案（"在线"/"重连中"…）；**空串时整段收起**（空 TextBlock 仍占 6px 左边距） |
| `Detail` | 悬停详情（一般是最近一次错误）。**在代码里赋值而不是模板绑定**：`ToolTip` 只有为 null 才不弹，空字符串照样弹一个空框（老模板为这个专门写过 DataTrigger） |
| `IsPulsing` | Busy 档是否脉动（静态截图/打印可关） |
| `DotSize` | 圆点直径（默认 8） |

- 旧的 `IsActive` / `ActiveBrush` / `InactiveBrush` / `CornerRadius` **弃用**（零消费，无迁移成本）；`Content` 保留（基类仍是 `ContentControl`）。
- 颜色口径与旧胶囊**逐字对齐**：文字 `#15803D` / `#B45309` / `#B91C1C`（这三个就是原模板里的值），圆点同色系实色 `#16A34A` / `#D97706` / `#DC2626`，Off 灰 `#98A2B3`（原值）。
- Busy 脉动用 **MultiTrigger**（`Level=Busy` 且 `IsPulsing=True`）作用在整枚灯上，**带 ExitActions 归位** —— 不写 ExitActions 的话动画会把 Opacity 钉在中间值上。

## 三、接线：胶囊模板从"手搓 100 行"变成"一张映射表"

`Dialog.Chip.xaml` 的 `DialogStatusPillTemplate` 现在只剩两件事：

1. **状态 → 档位/文案**：5 条 `DataTrigger`（`Disconnected→Off/离线`、`Connecting→Busy/连接中`、`Connected→Ok/在线`、`Reconnecting→Busy/重连中`、`Error→Error/错误`）；
2. 胶囊底色与"出过错"角标（这两件是**行**的外观，不属于灯本身）。

悬停详情改由 `HasError=True → Lamp.Detail = LastError` 一条触发器喂给控件（空框问题在控件里解决了）。

## 四、顺带修掉一个既有隐患（探针逼出来的）

离屏渲染时模板实例化直接抛：

```
XamlParseException → 无法找到名为"DialogIconFont"的资源
```

根因：模板里那个"出过错"⚠ 字形用 `{StaticResource DialogIconFont}`，而该键定义在**兄弟字典** `Themes/Dialog/Dialog.Text.xaml` 里；`Dialog.Chip.xaml` 自己只合并了 `Colors.xaml`。
**这正是该文件顶部注释写着的那类坑**（"跨兄弟字典的引用会重新解析失败"）—— 它当年修了 Colors，漏了这个字体键。失败点在**模板实例化**时，所以整条模板一起打不开。

修法：按它自己立的规矩，把 `Dialog.Text.xaml` 也自合并进 `Dialog.Chip.xaml`（引用只走自身链）。修完探针里 ⚠ 正常渲染（见参考图最后一行）。

> 〔2026-10-10 订正：`DialogIconFont` 随后按「分册作用域」整改上移到第一层令牌字典 `Themes\Colors.xaml`（`Dialog.Text.xaml` 现只留指针注释、不再定义该键）；`Dialog.Chip.xaml` 已自合并 `Colors.xaml`（:10），模板里的引用照常可达。见 `docs\code-changes\2026-10-10-变量管理弹窗打不开（Dialog分册跨册BasedOn断链）.md`〕

## 五、验证证据

| 项 | 结果 | 口径 |
| --- | --- | --- |
| 断言（5 条，全 PASS） | ① 四档颜色落到圆点：`#FF98A2B3 / #FF16A34A / #FFD97706 / #FFDC2626`；② 空文案 → `Label.Visibility=Collapsed`；③ `Detail` 非空挂 ToolTip、全空白为 `null`；④ **真模板 × 五种 `ConnectionState`**：`Disconnected→Off/离线`、`Connecting→Busy/连接中`、`Connected→Ok/在线`、`Reconnecting→Busy/重连中`、`Error→Error/错误` 全对；⑤ `HasError` 时 `LastError` 落到灯的 `Detail` 与 `ToolTip` | 探针 `_LogViewProbe -c Release` |
| 渲染 | `docs/UI样式参考/connection_status.png`（五行：离线/连接中/在线/重连中/错误+详情，落盘时回看过：胶囊底色、圆点色、文案色、⚠ 都对） | 同上 |
| 冒烟回归 | `EXIT=ZERO`、四分区全过（Shell 全树模板实例化 / 多格布局 / GridView / LogConsole） | `UIThemeSmokeTest.exe -c Release` |
| 构建 | `-c Release` 全链 `0 Error(s)`；**Debug 本轮没能构建** —— `UI.dll` 被 *Microsoft Visual Studio 2022* 与正在运行的 *VisionMaster (PID 29416)* 占用（`MSB3027/MSB3021`），未去杀进程，改用 Release 完成验证 | `dotnet build` |

## 六、未决与观察

- **真机观感未验**：只有离屏渲染。Busy 档的脉动（0.7s 往返、Opacity 1→0.35）在真机上是否合适、以及胶囊换成控件后与表格其它列的对齐，都要看一眼真机。
- **Debug 产物滞后**：用户的 VS 与运行中的实例锁着 `VisionMaster\bin\Debug\...\UI.dll`，所以本轮 Debug 侧仍是旧控件。**关掉 VS 与实例后需要补一次 `dotnet build -c Debug`。**
- **还有谁该用这枚灯**：SCADA 的设备状态、通信设置表头（"3/5 在线"的汇总灯）、运动轴的使能/报警状态都适合；本轮只接了通信设置这一处（唯一现成的落点）。
- **旧的 `IsActive`/`ActiveBrush`/`InactiveBrush` 已删**：如果将来需要"与业务无关的朴素开关灯"，用 `Level=Ok/Off` 即可，不必再把那套 API 加回来。
