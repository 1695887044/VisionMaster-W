# 2026-10-20 · 变量管理 GlobalVariableView Web 化复刻

> 范围：`VisionMaster\Views\DialogViews\GlobalVariableView.xaml` + `.xaml.cs` + `GlobalVariableViewConverters.cs`（新增）按 `docs\UI设计基线\global-variables\` v1 设计基线复刻；同时补齐 `VariableSourceNode` 显示属性、`Colors.xaml` 3 个数组类型色键、`GlobalVariableChecks` / `UIThemeSmokeTest` / `RenderProbe` 工装。
> 设计基线：`docs\UI设计基线\global-variables\README.md` v1（2026-10-20 用户确认）。
> 本批**不改插件工程**、不改 `Modules\` 产物、不改公共契约程序集。

## 一、改动清单

| # | 文件 | 变化 |
| --- | --- | --- |
| 1 | `VisionMaster\Views\DialogViews\GlobalVariableView.xaml` | 整体重写为「左栏 232 来源导航 + 右栏 60 卡片头 + DataGrid + 34 状态条」；左栏 48px 来源卡片、按 `IsNetwork` 分「本地与系统 / 通信连接」两组；DataGrid 变量名 220 / 类型 104 / 来源 120 / 扫描组 104 / 地址 88 / 当前值 * MinWidth 160 / 操作 100（2026-10-20 用户按 1200 窄档定稿）；根行 54 / 子行 40；空态与搜索无结果双覆盖层；系统只读横幅；视图零十六进制、无本地 keyed Style |
| 2 | `VisionMaster\Views\DialogViews\GlobalVariableView.xaml.cs` | 保留 `HookViewModel()` 双路调用与 `ApplyDetailColumnMode()`；新增左栏分组视图（`CollectionViewSource` + `PropertyGroupDescription(IsNetwork)`，幂等）；新增 `Loaded/Unloaded` 钩子；新增 `TypeAbbreviationConverter` 等 4 个 UI 转换器（独立文件） |
| 3 | `VisionMaster\Views\DialogViews\GlobalVariableViewConverters.cs`（新增） | `TypeAbbreviationConverter` / `DataTypeBrushConverter` / `GlobalVariableValueModeConverter` / `EmptyStateVisibilityConverter` / `StringNotEmptyToVisibilityConverter` |
| 4 | `VisionMaster\ViewModels\DialogViewModels\GlobalVariableManagerViewModel.cs` | `VariableSourceNode` 增加 `Subtitle` 计算属性 + `ProtocolText`，`IsOnline` 变化时通知副行；`SelectedSource` 忽略 null；`IsNetworkSource` 去掉切本地/网络副作用（修复「网络 → 系统」被改回本地）；`ResetVariable` 增加系统/只读守卫；`ProtocolDisplayName` 显示名映射 |
| 5 | `UI\Controls\Themes\Colors.xaml` | 追加 `DialogTypeArrayBrush` / `DialogTypeArrayTextBrush` / `DialogTypeArraySoftBrush`（仅追加，不改既有键） |
| 6 | `FlowCanvasChecks\GlobalVariableChecks.cs` | 视口常数 256→276；`MinWidth` 锚定 `prism:Dialog.WindowStyle`；本地/网络两档列宽；**逐列宽度 + 逐列 MinWidth 定稿断言（220/104/120/104/88/*160/100）**；固定列 `MinWidth` 契约；剥注释；缺失文件红着报；新增 `PropertyGroupDescription` / `TypeAbbreviationConverter` / 全量 ToolTip 显式样式检查 |
| 7 | `FlowCanvasChecks\VariableBindingCheck.cs` | 删除 `AllowedColorLiterals["GlobalVariableView.xaml"]`（视图 hex 清零后同批清理） |
| 8 | `UIThemeSmokeTest\hardcoded-colors.baseline.txt` | 删除 `GlobalVariableView.xaml|4` |
| 9 | `UIThemeSmokeTest\GlobalVariableViewSmoke.cs`（新增） | `--global-variables` 真实 VM 冒烟（分组/选中/列收放/系统只读/当前值六态/行高/空态/搜索/命令/ToolTip/Subtitle/转换器）；**可见性统一按 `EffectivelyVisible`、展开箭头按真实 Toggle 点击、网络→系统→网络按中间态断言**；`--global-variables-render` 出 8 张 PNG |
| 10 | `UIThemeSmokeTest\Program.cs` | 接入 `--global-variables` / `--global-variables-render` |
| 11 | `FlowCanvasChecks\RenderProbe.cs` | 场景 I 改用真实 `GlobalVariableManagerViewModel`（真实服务 + 假数据 + Dispose），左栏分组/三列收放/行高在 `--render` 下也走真实 code-behind |

## 二、契约保留

- `DataContext = GlobalVariableManagerViewModel`（Prism AutoWire + 宿主 RegisterDialog）不变；不新增独立 VM。
- `SourceNodes` / `SelectedSource` / `FilteredDisplayNodes` / `SearchText` 不变；数组子行跟随父级筛选不变。
- 全部命令保留：`AddCommand` / `DeleteCommand` / `ResetCommand` / `EditArrayCommand` / `CopyNameCommand` / `RenameCommand` / `ManageScanGroupsCommand` / `CloseCommand` / `ToggleNodeCommand`。
- `IsNetworkSource` 仍驱动「来源 / 扫描组 / 地址」三列显隐；`HookViewModel()` 构造器直调保留。
- 当前值 `CellValueEditBehavior` / `CurrentValueText` / `IsValueDeviated` / `IsValueReadOnly` 保留；数组元素行只写 Value、不碰 DefaultValue。
- `ScanGroupOptions` / `ScanGroupText` / `ShowScanGroup` / `ScanGroupChanged` 保留。
- 系统 / IO 变量只读：`IsSystemOwned` / `IsEditableNode` / `IsValueReadOnly` 不变；系统行保留复制，隐藏重命名/删除。
- 「添加变量」仍打开 `EasyDialog.ShowPropertyGridAsync` 属性网格弹窗，本批不改弹窗结构。

## 三、行为修复说明

1. **系统来源可达**：原 `IsNetworkSource` setter 在 `value=false` 时无条件选「本地变量」；选中「系统变量」会被改回本地。现只保留 `SetProperty`，由 `SelectedSource` 单向驱动。
2. **左栏副行实时**：`Subtitle` 为计算属性，`IsOnline` setter 发 `PropertyChanged(nameof(Subtitle))`；连接断线/重连后「Modbus TCP · 在线/离线」立即更新。
3. **当前值六态互斥**：`IsSystemOwned > HImage > IsValueReadOnly > (IsArrayRootNode && OriginalModel is LocalVariableModel) > IsChildNode > (IsEditableNode && !IsValueReadOnly)`。网络可写标量仍走可编辑 TextBox；系统/图像/只读存储区显示锁+值，不挂 `CellValueEditBehavior`；系统/网络数组不出现「强制覆盖集合...」死按钮。
4. **空态判据**：`EmptyStateVisibilityConverter` 同时看 `FilteredDisplayNodes.Count` 与 `SearchText`，空来源 / 搜索无结果 / 有结果三态互斥；`SearchText=null` 按空处理。
5. **窄档列宽**：固定列全部带 `MinWidth`，1200 窄档不再把类型/来源列压窄，而是出现横向滚动条。

## 四、验证结果（实测）

| 项 | 命令 | 结果 |
| --- | --- | --- |
| 全解决方案构建 | `dotnet build VisionMaster.sln -v q` | **0 错误**（`final_sln_build_continue4.log`；仅既有警告） |
| UIThemeSmokeTest 构建 | `dotnet build UIThemeSmokeTest\UIThemeSmokeTest.csproj -v q`（不在 sln，显式构建） | **0 错误**（`continue_ui_build10.log`） |
| 常规 gate | `dotnet run --project FlowCanvasChecks --no-build` | **2797 通过 / 0 失败**（`continue_flow_gate7.log`，含逐列宽度/MinWidth、命名元素、18/18 ToolTip 断言） |
| 变量管理冒烟 | `dotnet run --project UIThemeSmokeTest --no-build -- --global-variables` | **38 PASS / 0 FAIL**（`continue_gv_smoke10.log`；分组/双向选中/列收放/系统只读/当前值六态/行高 54/40/空态/搜索/命令/ToolTip/Subtitle/转换器/真实点击展开） |
| 默认冒烟 | `dotnet run --project UIThemeSmokeTest --no-build` | **79 PASS / 0 FAIL**；颜色令牌 **无新增硬编码颜色**（`continue_ui_default5.log`） |
| 渲染 | `dotnet run --project UIThemeSmokeTest --no-build -- --global-variables-render` | **9 PASS / 0 FAIL，8 张 PNG**：local / network / network-offline / system / empty / search / write-error / narrow（`continue_gv_render4.log`） |
| 资源探针 | `dotnet run --project _ResProbe --no-build` | `VIEW 变量管理 GlobalVariableView -> CREATED+LAID OUT`；`RESULT: ALL_OK`（`continue_res_probe3.log`） |
| 独立终审 | reviewer 只读复核 | **B PASS / D PASS**（列宽定稿、断言有效性两项阻断均关闭） |
| 变异反证 1 | 把分组属性改成 `DisplayName` | 冒烟准确红：`PropertyGroupDescription(IsNetwork)` + `Groups=4`；还原后绿 |
| 变异反证 2 | 把重命名按钮的系统隐藏触发器改成 `IsNetwork` | 冒烟准确红：`renameHidden=False`；还原后绿 |

## 五、证据

- 设计基线：`docs\UI设计基线\global-variables\design.html` + `README.md` + `evidence\design-v1-*.png`
- WPF 复刻：`docs\UI设计基线\global-variables\evidence\after-render-v1-*.png`
  - `after-render-v1-local.png` / `network.png` / `network-offline.png` / `system.png`
  - `after-render-v1-empty.png` / `search.png` / `write-error.png` / `narrow.png`
- 改前对照：`docs\UI设计基线\global-variables\evidence\before-render-v1-local.png`（取自 2026-10-15 的 `variable-manager-render-I.png`，旧两栏布局）

## 六、已知偏差（与设计稿）

1. 图标字体：Web Lucide → WPF Font Awesome 6 Pro Solid。
2. 字体度量/滚动条：WPF 滚动条约 17px，窄档可用宽度少约 17px。
3. 行高：视图内已把 `DialogDataGridRow` 的 `Margin` 覆盖为 0，根行实测 54 / 子行 40，与设计一致。
4. 类型 chip：`DialogTag` 1px 描边 + 圆角 4（设计无描边 + 圆角 6）。
5. 卡片头保留 `DialogCardTitleIcon`（设计右栏无标题图标）。
6. 1200 窄档网络来源：定稿固定列 736 + 星号 160 = 896，理论 ≤ 905 有效视口；实测 DataGrid 边框/竖向滚动条/单元格边距仍额外占用，截图中出现横向滚动条。设计允许横向滚动。
7. 空态组头：无通信连接时 WPF 分组视图不会生成空的「通信连接」组头；无连接时只显示「本地与系统」。
8. 写入失败态用 `Validation.MarkInvalid` 模拟真实 `CellValueEditBehavior` 提交失败红框（无设备可复现真实下发失败）；真实设备失败路径仍由 `GlobalVariableChecks` 的当前值契约覆盖。
9. DPI 125%/150%、高对比度、`AutomationProperties` 需真机复核；本批自动化按 96 DPI。
10. 「添加变量」属性网格弹窗外观未改（D1 延后）。
11. 卡片头 / 状态条高度：设计稿 64 / 40，实施 60 / 34（1300×700 下多留一行明细），属有意取舍。

## 七、回滚

1. `git revert` 本批的 `GlobalVariableView.xaml` / `.xaml.cs` / `GlobalVariableViewConverters.cs`。
2. 回滚 VM 的 `Subtitle` / `ProtocolText` / `SelectedSource` null 守卫 / `IsNetworkSource` 副作用 / `ResetVariable` 守卫。
3. 回滚 `Colors.xaml` 的 3 个 `DialogTypeArray*` 键（先确认无其它引用）。
4. 回滚 `GlobalVariableChecks.cs`、`VariableBindingCheck.cs`、`UIThemeSmokeTest\Program.cs`、`GlobalVariableViewSmoke.cs`、`RenderProbe.cs`、`hardcoded-colors.baseline.txt`。
5. 本批不改插件工程 / `Modules\`，无插件 DLL 回滚项。
