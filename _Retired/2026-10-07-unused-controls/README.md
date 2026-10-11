# 已下线控件（2026-10-07）

本目录存放**从 UI 库下线、且全仓零消费**的控件与其主题文件。
它们已从编译与主题字典里移除（不再参与编译、不再进 `Themes/Generic.xaml`），仅作留痕与随时恢复之用。
**本目录不参与任何构建，也不在任何 `.sln` 里。**

## 为什么下线（普查口径）

普查过全仓 `*.cs` / `*.xaml` 的引用（排除 `obj/`、`bin/`、`_StyleProbe` / `_LogViewProbe` / `_BeadViewProbe` 等探针工程、`_MvvmDemo`、主题字典自身）：

| 控件 | 引用数 | 下线理由 |
| --- | --- | --- |
| `AlarmBar`（滚动报警条） | 0 | SCADA 已有现役实现 `Scada.Controls\Elements\AlarmBannerElement.cs` + `AlarmBannerRow.cs`，职责重叠。（`_MvvmDemo` 里那处 `ExtrasSource="{Binding AlarmBars}"` 是它自己 VM 的属性名，不是本控件） |
| `BindableParamBox`（可绑定参数框） | 0 | 现役实现是 `Shard\Core.Controls\LinkableValueEditor`（`IsLinked` / 解绑按钮 / 占位提示一应俱全）；本件是它的未接线旧原型。另：它的 `PART_UnlinkBtn` 用匿名 lambda 订阅，模板重建会重复挂命令，本就该修 |
| `CommunicationTextBox`（通信读写文本框） | 0 | 半成品：`AutoRefresh` / `RefreshIntervalMs` 两个依赖属性没实现（变更回调是空方法）、`Dispose()` 空实现、`float.Parse` 未指定 `InvariantCulture`（小数点分隔符不为 `.` 的区域设置下解析必失败）、命名空间还写成全目录唯一的 `UI.Controls.CustomControl` |
| `Popover`（气泡容器） | 0 | 功能与 WPF 原生 `Popup` / `ToolTip` 重叠，且无使用计划 |

## 保留但需要"接线"的两个（**不在**本目录，仍在 UI 库内）

| 控件 | 状态 | 归宿 |
| --- | --- | --- |
| `StatusIndicator` | 保留 | 通用状态灯原语，全仓无重复实现。建议归宿：通信连接状态灯（绿/黄/红 + 悬停显示最近错误 + 点击重连） |
| `CardPropertyGrid` | 保留 | 与 `FlatPropertyGrid` 是"属性网格两形态"设计对的一半（共用 `PropertyGridBase` 与 `UseCardLayout`）；删它会破坏刚抽好的基类抽象。待接线：插件配置壳 / SCADA 属性面板可切卡片式 |

## 如何恢复

1. 把文件放回原位（本目录结构即原路径）：

| 本目录文件 | 放回 |
| --- | --- |
| `CustomControl\AlarmBar.cs` | `UI\Controls\CustomControl\` |
| `CustomControl\BindableParamBox.cs` | `UI\Controls\CustomControl\` |
| `CustomControl\CommunicationTextBox.cs` | `UI\Controls\CustomControl\` |
| `CustomControl\Message\Popover.cs` | `UI\Controls\CustomControl\Message\` |
| `Themes\AlarmBar.xaml` | `UI\Controls\Themes\` |
| `Themes\BindableParamBox.xaml` | `UI\Controls\Themes\` |
| `Themes\Popover.xaml` | `UI\Controls\Themes\` |

2. 在 `UI\Controls\Themes\Generic.xaml` 的第三层（自定义控件）按原位补回合并行（位置见同文件里 `StatusIndicator.xaml` / `PropertyGrid.xaml` 那几行）：

```xml
<ResourceDictionary Source="pack://application:,,,/UI;component/Themes/Popover.xaml" />
<ResourceDictionary Source="/UI;component/Themes/AlarmBar.xaml" />
<ResourceDictionary Source="/UI;component/Themes/BindableParamBox.xaml" />
```

3. 重建 UI 工程（`dotnet build UI\Controls\UI.csproj`，或直接重建宿主）。

> 注意：这三行**不能只恢复文件而不恢复合并**，也不能只恢复合并而不恢复文件 ——
> `ResourceDictionary.Source` 指向不存在的资源会在运行期抛异常。

## 彻底删除

确认不再需要留痕时，直接删掉本目录即可（它不参与编译、不在任何 `.sln` 里，
`Generic.xaml` 里的对应合并行已在 2026-10-07 的改动中移除）。
