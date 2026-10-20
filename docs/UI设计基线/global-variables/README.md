# 变量管理（全局变量）· 设计基线

| 项 | 内容 |
| --- | --- |
| 版本 | v1 |
| 日期 | 2026-10-20 |
| 确认日期 / 人 | 2026-10-20 / 用户（指令：完整落地到桌面端） |
| 覆盖范围 | `VisionMaster\Views\DialogViews\GlobalVariableView.xaml` + `GlobalVariableView.xaml.cs`（仅列显隐/UI 处理器） |
| 窗口尺寸 | 1300 × 700 默认；最小 1200 × 580；左栏 232px |
| 设计基线文件 | `design.html`（自包含、无外部依赖、内联 SVG 图标） |
| 设计证据 | `evidence\design-v1-*.png`（本地 / 网络在线 / 网络离线 / 系统 / 空 / 搜索无结果 / 写入失败 / 1200 窄档） |
| 复刻状态 | **已完成（2026-10-20）**；WPF 证据 `evidence\after-render-v1-*.png`；改前对照 `evidence\before-render-v1-local.png`；记录 `docs\code-changes\2026-10-20-变量管理GlobalVariableView Web化复刻.md` |

## 定位

本文件是「Web 设计基线 → 用户确认 → WPF 复刻 → 同屏截图验收」四步流程的**第 ① 步**产物，
流程口径沿用 `docs\UI设计基线\插件自定义视图Web化改造SOP.md`（R126-R132）。
本页是**宿主弹窗**，不是插件自定义视图，因此没有 `IPluginConfigView` / `LinkableValueEditor` / HALCON 画布；
但「先出完整 Web 设计稿、确认后再改 WPF、零功能改动、断言守门」的纪律完全一致。

Web 只作设计媒介，**不进入构建、不部署**。确认后按本基线做 WPF 复刻，第 ④ 步以同一屏、同一状态的截图对照验收。

## 一句话方向

**保留两栏主从结构，把「来源导航 / 明细行 / 当前值编辑 / 空态」四件事做清楚。**
左栏从「一列图标文字」升级为真正的来源导航；右栏从「一张 30px 高的普通表格」升级为
可扫描的数据工作区：变量名两行、类型有色点、当前值是编辑主入口、系统变量只读语义前置。

## 审查清单（请重点看这几项）

- [ ] 左栏 232px + 「本地与系统 / 通信连接」两组 + 48px 来源卡片（状态副行 + 计数）。
- [ ] 明细行两行式（变量名 + 描述）、根行 54 / 子行 40、类型 chip + 色点。
- [ ] 当前值单元格：质量点 + 提交式输入 + 偏离时的 ↺；数组根「强制覆盖集合...」。
- [ ] 系统来源只读横幅 + 操作列「只读」chip；「添加变量」保留（点击会切到本地）。
- [ ] 空态与搜索无结果分开；底部状态条 + 键盘提示。
- [ ] 「添加变量」属性网格弹窗是否要在本批一起改（当前列在 D1 延后）。

## 现状审查（改动理由）

| # | 现状 | 影响 |
| --- | --- | --- |
| 1 | 左栏来源节点 38px 单行：图标 + 名称 + 计数；在线状态只有一个 7px 圆点，系统变量与本地变量外观几乎一样 | 用户分不清「本地 / 系统 / 网络」，也看不出系统变量为什么不能删；连接离线只能靠一个红点猜 |
| 2 | 右栏卡片头 52px：标题 + 计数 + 220px 搜索 + 管理扫描组 + 添加变量全挤在一行 | 最小宽度下搜索框与按钮互相挤；没有「当前来源」上下文，切换来源后只能从左栏反推 |
| 3 | 明细表行高约 30-32px，变量名 / 类型 / 来源 / 地址 / 当前值同字重、同灰度 | 变量一多就是一片灰字；描述（`VariableNode.Description`）已有数据但完全没上屏 |
| 4 | 当前值单元格是 110px 裸输入框；质量点只有 5px 且无图例；↺ 只在偏离时出现 | 写值/复位是最高频动作，却最不显眼；黄点/灰点含义只能靠猜 |
| 5 | 表格无空态、无搜索无结果态 | 空来源或搜不到时是一张白板，看起来像加载失败 |
| 6 | 底部是一段 11px 长说明（写值/复位/Runtime.*） | 操作要点不在操作现场；长段落占掉表格高度 |
| 7 | 系统变量行仍显示复制/重命名/删除三颗按钮，点删除才弹「不能删除」 | 给了可点的假象；用户要先点一次才知道这是只读数据 |
| 8 | 来源列表没有分组：本地、系统、每条连接平铺 | 连接一多就分不清「本机变量」与「设备变量」两类信息 |

## v1 方向

1. **两栏不变，左栏 232px**：左栏来源导航，右栏明细工作区；主从关系不重新发明。
2. **来源分组**：「本地与系统」+「通信连接」两组；节点是 48px 卡片：28px 图标块 + 名称 + 状态副行 + 计数。
   选中态 = 白卡 + 主色左条；系统变量用 CPU 图标 + 锁副行；连接用插头图标 + 在线/离线副行。
3. **右栏头部建立上下文**：`变量明细` + 数量徽标 + 当前来源 chip + 一句副标题；搜索 238px、
   管理扫描组（仅网络来源）、`＋ 添加变量` 固定右对齐。
4. **明细行两行式**：根行 54px（变量名 12.5px + 描述 10.5px），子行 40px（缩进 24px）；
   类型是中性 chip + 类型色点；数组根显示「N 项」；地址用等宽字体。
5. **当前值单元格是编辑主入口**：质量点（正常/读取失败/断线）+ 提交式输入框 + 偏离时的 ↺；
   数组根保留「强制覆盖集合...」；图像与只读存储区显示锁 + 值，不给可编辑假象。
6. **空态与搜索无结果分开**：空来源给「还没有变量 + 添加变量」；搜索无结果给「没有匹配 + 清空搜索提示」。
7. **系统变量只读语义前置**：系统来源顶部常驻只读横幅；行内操作列换成「只读」chip，
   不再显示点了只会弹提示的删除/重命名按钮。
8. **底部状态条**：左侧 `N 个变量 · 来源 · 内存/轮询`；右侧 `Enter 提交 / Esc 取消 / ↺ 恢复初始值`。
9. **全令牌化**：视图零十六进制；类型色复用既有语义族（只追加 3 个数组色键）；图标按钮 30×30 + `Padding=0`（R68）。

## 布局与尺寸

```text
窗口 1300 × 700（最小 1200 × 580）
┌──────────────────────────────────────────────────────────────────────┐
│ 标题栏 64：图标 36 + 标题/副标题 + 关闭 30                            │
├──────────────┬───────────────────────────────────────────────────────┤
│ 左栏 232     │ 右栏自适应（1300 下约 1024）                          │
│              │ ┌───────────────────────────────────────────────────┐ │
│ 变量来源     │ │ 卡片头 64：标题/数量/来源 chip + 搜索 + 按钮      │ │
│ 本地与系统   │ ├───────────────────────────────────────────────────┤ │
│  本地变量 6  │ │ 表头 34                                           │ │
│  系统变量 8  │ │ 根行 54 / 子行 40                                 │ │
│ 通信连接     │ │ ...                                               │ │
│  PLC_Modbus  │ ├───────────────────────────────────────────────────┤ │
│  PLC_S7_1    │ │ 状态条 40                                         │ │
│ 轮询镜像说明 │ └───────────────────────────────────────────────────┘ │
│ 采集质量图例 │                                                       │
└──────────────┴───────────────────────────────────────────────────────┘
```

外边距 16×2 + 左栏 232 + 间隔 12 = 276；最小窗口 1200 下右栏视口 **924**。

### WPF 列宽计划

| 列 | 宽度 | 说明 |
| --- | --- | --- |
| 变量名 | **220 固定（MinWidth 220）** | 2026-10-20 按 1200 窄档定稿（早期 280 会让操作列被裁）；两行：名称 + 描述；子行缩进 22 |
| 类型 | **104（MinWidth 104）** | 中性 chip + 类型色点；内容宽 92，类型名截断 |
| 来源 | **120（MinWidth 120）** | 仅网络来源；本地/系统整列收起；badge 最大 108 |
| 扫描组 | 104（MinWidth 104） | 仅网络来源；根节点下拉（单元格 Padding `6,0,0,0`，内容 98） |
| 地址 | 88（MinWidth 88） | 仅网络来源；等宽 |
| 当前值 | `*` MinWidth 160 | 星号列；单元格 Padding `8,0,0,0`（仅左内边距）；内容 152 = 质量点 6 + 间距 6 + 输入 104 + 间距 6 + ↺ 30 |
| 操作 | 100（MinWidth 100） | 单元格 `DialogDataGridCellFlush`（Padding 0）；三颗 30×30 + 间距 2×2 = 94 |

> **实施定稿（2026-10-20，用户确认）**：早期计划的 280/88/100 已废弃；按 1200 最小窗口实测，变量名 280→220、来源 100→120、类型 88→104、地址 84→88。以下合计按定稿值计算。

本地来源固定列 = 220 + 104 + 100 = **424**；网络来源固定列 = 220 + 104 + 120 + 104 + 88 + 100 = **736**；
424 + 160 = 584 ≤ 924，736 + 160 = 896 ≤ 924。满足 `GlobalVariableChecks` 的列宽契约，
视口常数为 276（左栏 232 + 间距 12 + 外边距 16×2），本地/网络两档都要断言。

## 契约保留（一个都不能改）

| # | 契约 | 为什么不能动 |
| --- | --- | --- |
| C1 | `DataContext = GlobalVariableManagerViewModel`（Prism `AutoWireViewModel` + 宿主 `RegisterDialog`） | 换独立 VM = 命令、对话框生命周期、宿主注册全部要重接 |
| C2 | `SourceNodes` / `SelectedSource` / `FilteredDisplayNodes` / `SearchText` | 来源筛选与数组子行跟随父级的语义在 VM 里，视图只换外观 |
| C3 | `AddCommand` / `DeleteCommand` / `ResetCommand` / `EditArrayCommand` / `CopyNameCommand` / `RenameCommand` / `ManageScanGroupsCommand` / `CloseCommand` / `ToggleNodeCommand` | 全部是功能入口；不允许出现死按钮，也不允许把入口藏掉 |
| C4 | `IsNetworkSource` 驱动「来源 / 扫描组 / 地址」三列显隐；`GlobalVariableView.xaml.cs` 构造器直调 `HookViewModel()` | 2026-10-11「变量明细显示不全」事故的修法；只靠 `DataContextChanged` 会一次都不执行 |
| C5 | `CellValueEditBehavior` / `CurrentValueText` / `IsValueDeviated` / `IsValueReadOnly` / 数组元素行写回 | 当前值就地编辑的提交、Esc 放弃、编辑中暂停轮询、失败红框全部在这里 |
| C6 | `ScanGroupOptions` / `ScanGroupText` / `ShowScanGroup` / `ScanGroupChanged` | 扫描组是轮询节拍的唯一入口；改名/删组后要重注册 |
| C7 | DataGrid 横向滚动 `Auto`；当前值列 `Width="*"` 且 `MinWidth ≥ 80`（本版 160） | 固定列吃光视口的守门；宁可横向滚动也不许星号列变 0 |
| C8 | 系统 / IO 变量只读：`IsSystemOwned` / `IsEditableNode` / `IsValueReadOnly` | 系统变量由宿主维护，删了会回来且会断引用；VM 侧拦截必须保留。操作列只隐藏「重命名 / 删除」，「复制变量名」仍保留（HMI 组态入口） |
| C9 | `AddCommand` 仍打开 `EasyDialog.ShowPropertyGridAsync`（本地 / 网络草稿） | 「添加变量」框架弹窗不在本批；主窗口只换入口外观 |
| C10 | `CloseCommand` + 自绘标题栏拖动 | 无边框窗口的唯一关闭/拖动入口 |

## 允许的 VM 改动（仅显示属性）

| 改动 | 理由 | 风险 |
| --- | --- | --- |
| `VariableSourceNode` 增加 `Subtitle`（计算属性）+ `ProtocolText`（显示名） | 三态公式固定：网络 = `{ProtocolText} · 在线/离线`；本地 = `内存变量 · 可编辑`；系统 = `宿主维护 · 只读`。`IsOnline` 变化时副行必须跟着变 | 纯显示属性，不参与筛选、持久化、命令；`IsOnline` setter 补 `RaisePropertyChanged(nameof(Subtitle))` |
| `SelectedSource` setter 忽略 null | 分组视图刷新/集合重建时 ListBox 可能把选中项写回 null，导致右栏错位 | 只拦 null，不改变合法选中项语义 |
| `IsNetworkSource` setter 去掉「切本地/网络」副作用 | 2026-10-15 分段胶囊撤掉后，它只由 `SelectedSource` 驱动；保留 else 分支会把「系统变量」选择强制改回「本地变量」 | 这是**行为修复**：网络 → 系统 → 网络必须可达；补运行时断言 |

除此之外**不改 VM**：不加 `SelectedNode`、不加 `IsLoading`、不加排序/列状态、不改 `AddVariableCore`、不改筛选/持久化/创建逻辑。

## 令牌映射

| 设计变量 | 值 | WPF 资源键 |
| --- | --- | --- |
| `--gv-surface` | `#FFFFFF` | `DialogSurfaceBrush` |
| `--gv-surface-2` | `#F8FAFC` | `DialogRowAlternateBrush` |
| `--gv-surface-3` | `#F1F5F9` | `DialogRowHoverBrush` |
| `--gv-border` | `#E2E8F0` | `DialogBorderBrush` |
| `--gv-border-soft` | `#EEF2F7` | `DialogBorderLightBrush` |
| `--gv-border-strong` | `#CBD5E1` | `DialogBorderStrongBrush` |
| `--gv-text` | `#0F172A` | `DialogTextPrimaryBrush` |
| `--gv-text-2` | `#475569` | `DialogTextSecondaryBrush` |
| `--gv-text-3` | `#94A3B8` | `DialogTextMutedBrush` |
| `--gv-text-4` | `#64748B` | `ScadaSecondaryTextBrush`（近似） |
| `--gv-primary` | `#0EA5E9` | `DialogAccentBrush` |
| `--gv-primary-strong` | `#0284C7` | `DialogAccentDarkBrush` |
| `--gv-primary-text` | `#0369A1` | `DialogAccentTextBrush` |
| `--gv-primary-soft` | `#F0F9FF` | `DialogAccentSoftBrush` |
| `--gv-primary-light` | `#E0F2FE` | `DialogAccentLightBrush` |
| `--gv-primary-border` | `#BAE6FD` | `DialogAccentBorderBrush` |
| `--gv-success*` | `#22C55E / #15803D / #F0FDF4 / #BBF7D0` | `DialogSuccessBrush` / `DialogSuccessTextBrush` / `DialogSuccessSubtleBrush` / `DialogSuccessBorderBrush` |
| `--gv-danger*` | `#EF4444 / #B91C1C / #FEF2F2 / #FECACA` | `DialogDangerBrush` / `DialogDangerTextBrush` / `DialogDangerSubtleBrush` / `DialogDangerBorderBrush` |
| `--gv-warning*` | `#F59E0B / #B45309 / #FFF4E5 / #FDE68A` | `DialogWarningBrush` / `DialogWarningTextBrush` / `DialogWarningSubtleBrush` / `DialogWarningBorderBrush` |
| `--gv-violet*` | `#8B5CF6 / #6D28D9 / #F5F3FF` | `LogicCaseBrush` / `LogicCaseTextBrush` / `LogicCaseSoftBrush` |
| `--gv-cyan*` | `#06B6D4 / #0E7490 / #ECFEFF` | **新增** `DialogTypeArrayBrush` / `DialogTypeArrayTextBrush` / `DialogTypeArraySoftBrush` |
| `--gv-slate*` | `#64748B / #475569 / #F1F5F9` | `DialogTextSecondaryBrush` / `DialogTextSecondaryBrush` / `DialogRowHoverBrush` |
| 圆角 6/8/10/12/16 | — | WPF `CornerRadius` 字面量（仓内暂无圆角令牌） |
| 间距 4/8/12/16 | — | WPF `Margin` / `Padding` 字面量 |
| 字体 | Segoe UI / Microsoft YaHei UI | 同族系统字体；等宽用 Cascadia Mono / Consolas |

**只新增 3 个颜色键**（`Colors.xaml`，追加不改既有键）：`DialogTypeArrayBrush` / `DialogTypeArrayTextBrush` / `DialogTypeArraySoftBrush`。
类型色其余五档全部复用既有语义族，不新造第二套蓝/灰。

## 设计块 → 现有样式键（R141/R117 对照）

| 设计块 | 复用键 | 说明 |
| --- | --- | --- |
| 窗口 / 卡片 | `DialogCardShadow` / `DialogCardBorder` / `DialogCardHeader` | 卡片头局部覆盖 `Height=60`（设计 64，实施按一屏行数取舍），不改共享键 |
| 标题 / 图标 | `DialogCardTitle` / `DialogCardTitleIcon` | 右栏卡片头保留标题图标（与设计稿的差异记入已知偏差） |
| 搜索 | `DialogSearchChrome` / `DialogSearchInput` | 宽度 238，高度 32 |
| 主按钮 / 图标按钮 | `DialogPrimaryButton` / `DialogIconButton` | 图标按钮 30×30 + `Padding=0`（R68） |
| 表格 / 行 / 表头 / 单元格 | `DialogDataGrid` / `DialogDataGridRow` / `DialogDataGridColumnHeader` / `DialogDataGridCell` / `DialogDataGridCellFlush` | 视图内仅用无 `x:Key` 的内联 Style 覆盖尺寸；不改共享键 |
| 胶囊 / 标签 | `DialogChip*` / `DialogTag` / `DialogTagText` | 类型 chip、来源 badge、只读 chip、数量徽标 |
| 输入 / 下拉 | `DialogCellInputQuiet` / `DialogCombo` | 当前值输入、扫描组下拉 |
| 焦点 / ToolTip | `DialogFocusVisual` / `DialogToolTipStyle` | 共享按钮/输入样式已带焦点；所有 ToolTip **显式**写 `<ToolTip Style="{StaticResource DialogToolTipStyle}" Content="…"/>`（不依赖视图内隐式 Style） |
| 空态 / 状态条 | 视图内联（无共享键） | 仅本弹窗使用；不新增共享键 |

**样式纪律**：`GlobalVariableView.xaml` 属于 `CollectedDialogViews`，**禁止本地 `<Style x:Key=...>`**（`VariableBindingCheck.RunCollectedViewStyleContract`）；本视图只用无 `x:Key` 的内联 Style、`DataTemplate`、`ControlTemplate`。共享字典只追加 `Colors.xaml` 的 3 个 `DialogTypeArray*` 键。

## WPF 复刻映射（第 ③ 步计划）

| 设计项 | WPF 实现 |
| --- | --- |
| 左栏来源导航 | `ListBox` + 视图内 `DataTemplate`（不改 `Dialog.Misc.xaml` 的共享 `DialogSourceNodeTemplate`）；`SelectedItem="{Binding SelectedSource, Mode=TwoWay}"`、`IsSynchronizedWithCurrentItem=False`；`ItemsSource` 由 code-behind 设为分组视图（幂等，不重复加 `GroupDescriptions`）；`ItemContainerStyle` 内联模板 + `DialogFocusVisual`，DataTemplate 触发器覆盖 Hover/Selected/KeyboardFocus/Disabled 五态 |
| 来源分组标题 | `CollectionViewSource` 按既有 `IsNetwork` 分组（false = 本地与系统，true = 通信连接），`GroupStyle.HeaderTemplate` 用 `DataTrigger` 出中文组标题；不新增分组字段 |
| 右栏卡片头 | `Grid` 两行：标题/徽标/来源 chip + 副标题；搜索、管理扫描组、添加变量右对齐 |
| 两行式变量名 | `DataGridTemplateColumn` + `Grid`（名称 / 描述）；描述为空时第二行 `Collapsed`；数组项数绑 `Children.Count` |
| 类型 chip + 色点 | `DialogTag` + `Ellipse`；内联 `Style.Triggers` 按 `TypeName` 切换 `DialogAccent*` / `LogicCase*` / `DialogWarning*` / `DialogSuccess*` / `DialogTypeArray*` / `DialogTextMuted*`；类型缩写走 `TypeAbbreviationConverter` |
| 数组展开 | 保留 `ToggleNodeCommand` + `IsExpanded` + `LevelToIndentConverter`；`ToggleButton.IsChecked="{Binding IsExpanded, Mode=OneWay}"` + `Command="{Binding DataContext.ToggleNodeCommand, RelativeSource={RelativeSource AncestorType=DataGrid}}"` + `CommandParameter="{Binding}"`（避免双翻转；DataContext 是 VariableNode，必须走 DataGrid 的 DataContext），`Padding=0`、16×16；`DataGrid.RowHeight="{x:Static sys:Double.NaN}"` + `MinRowHeight="40"`；内联 `DataGrid.RowStyle BasedOn=DialogDataGridRow`，默认 `Height=54`，`IsChildNode=True` → `Height=40`；`AlternationCount=1` |
| 表头 | 内联 `DataGrid.ColumnHeaderStyle BasedOn=DialogDataGridColumnHeader`：`Height=34`、`FontSize=11`；不改共享字典 |
| 当前值 | 一个 `Grid` + `GlobalVariableValueModeConverter` 的 `MultiBinding`，按优先级 `IsSystemOwned > HImage > IsValueReadOnly > (IsArrayRootNode && OriginalModel is LocalVariableModel) > IsChildNode > (IsEditableNode && !IsValueReadOnly)` 分六态；**网络可写标量也走可编辑态**；可编辑根保留 `CellValueEditBehavior` + `DialogCellInputQuiet` + 质量点 + ↺；只读根显示锁 + `CurrentValueText`；图像显示「图像由流程写入」；数组根仅本地变量显示「强制覆盖集合...」 |
| 系统只读 | `IsSystemOwned` 触发：操作列保留复制 + 「只读」chip（Border），重命名/删除单独 Collapsed；当前值显示锁 + 值；顶部横幅按 `SelectedSource.Key == System` |
| 空态 / 搜索无结果 | DataGrid 上层两个 `Grid` 覆盖（`Margin="0,34,0,0"` 让表头保持可见）；`MultiBinding` + `EmptyStateVisibilityConverter` 输入 `FilteredDisplayNodes.Count` 与 `SearchText`，分别返回空来源/搜索无结果两态 |
| 状态条 | 左侧 `SelectedSource.Count` + `DisplayName`；右侧静态键盘提示 |
| 列收放 | 保留 `ApplyDetailColumnMode()` / `HookViewModel()`；`SourceColumn` / `ScanGroupColumn` / `AddressColumn` 三列名不变 |
| 标题栏 | 保留 `WindowDragBehavior` + `CloseCommand`；高度 52 → 64 |

## 验证计划（WPF 复刻时执行）

| 层 | 判据 |
| --- | --- |
| 构建 | `dotnet build VisionMaster.sln` 0 错误；**再显式** `dotnet build UIThemeSmokeTest\UIThemeSmokeTest.csproj`（它不在 sln 里，R65） |
| 静态 | `FlowCanvasChecks\GlobalVariableChecks` 扩展：视图 XAML `hex=0`（剥注释）；关键绑定齐全；列宽契约（定稿 220/104/120/104/88/*160/100，本地 424+160、网络 736+160、视口 924、逐列 `MinWidth` 锚定）；`HookViewModel()` 双路调用；`TypeAbbreviationConverter`；空态/系统横幅/来源模板；`VariableBindingCheck` 无 keyed Style |
| 运行时 | `UIThemeSmokeTest --global-variables`：真实 `GlobalVariableManagerViewModel` + 真实视图（构造后 `view.DataContext = vm`，确保 `DataContextChanged → HookViewModel`）；断言 DataContext、分组数、双向选中、列显隐、系统横幅、系统行复制/只读、当前值六态、行高 54/40、空态/搜索无结果、命令绑定；渲染前显式设置网络节点 `IsOnline=true`，断言在线/离线副行不同 |
| 渲染 | `UIThemeSmokeTest --global-variables-render` 或 `FlowCanvasChecks --render` 场景 I：本地 / 网络在线 / 网络离线 / 系统 / 空 / 搜索无结果 / 写入失败 / 1200 窄档，至少 8 张 PNG，存 `evidence/after-render-v1-*.png`；写入失败态用 `Validation.MarkInvalid` 模拟真实提交失败红框；改前截图存 `before-render-v1-local.png` |
| 回归 | `FlowCanvasChecks` 0 失败；默认 `UIThemeSmokeTest`（含 `RunHardcodedColorChecks`）与新 `--global-variables` 段全 PASS；以日志明细为准（R46/R65） |
| 资源 | `FlowCanvasChecks` 资源断链契约 + `ResProbe` `RESULT: ALL_OK`（新增 3 个颜色键后必须跑）；`hardcoded-colors.baseline.txt` 删除 `GlobalVariableView.xaml|4`；视图 hex 清零同一批次删除 `VariableBindingCheck.cs` 的 `AllowedColorLiterals["GlobalVariableView.xaml"]` 白名单条目 |
| 变异反证 | 至少 2 条：① 删 `PropertyGroupDescription` → 分组断言红；② 摘掉系统行复制按钮 → 系统行断言红；还原后复绿 |
| 真机 | 打开 → 切本地/系统/连接 → 搜索 → 改当前值（成功/失败）→ ↺ → 数组展开/覆盖 → 添加 → 重命名 → 删除 → 管理扫描组 → 关闭 |

## 已知偏差（WPF 复刻时按 R130 记录）

1. **图标**：Web 用 Lucide 描边 SVG；WPF 用 Font Awesome 6 Pro Solid（`DialogIconFont`），形状不同、语义一致。
2. **字体度量**：同字号下 Segoe UI / 微软雅黑与 Web 的字宽、行高略有差异；不按 px 硬套。
3. **滚动条**：WPF 约占 17px，窄档可用宽度比 Web 少约 17px；网络来源允许横向滚动。
4. **DataGrid 选择态**：Web 用浅蓝行底 + 2px 左条；WPF 走 `DialogDataGridRow` 的选中态，不另造一套。
5. **质量点颜色**：现有视图硬编码 `#FF67C23A / #FFE6A23C / #FFC0C4CC`，复刻改为 `DialogSuccessBrush` / `DialogWarningBrush` / `DialogTextMutedBrush`（同语义、略有色差）。
6. **类型色**：Web 的紫色/青色分别映射到 `LogicCase*` / 新增 `DialogTypeArray*`，不是新造第二套数据色。
7. **行高**：Web 根行 54 / 子行 40；WPF 受 DataGrid 单元格 Padding 影响可能有 1-2px 差异。
8. **空态覆盖层**：WPF 用 DataGrid 上层 Grid 覆盖，表头仍可见（与设计稿一致）；不是替换整个 DataGrid。
9. **「添加变量」弹窗**：仍为框架属性网格，外观不在本批；主窗口只保留入口。
10. **状态条计数**：搜索时 Web 显示 `0 / 6`，WPF 第一版可只显示来源总数（`SelectedSource.Count`）；若要多 Binding 再补转换器。
11. **控件高度**：数据密集型弹窗使用 32px 头部按钮 / 30px 行内输入，不套用插件表单的 36px；这是为了一屏多看几行，属于有意取舍。
12. **窗口圆角 / 阴影**：Web 是 16px 圆角 + 多层阴影；WPF 无边框窗口的圆角由宿主决定，内容卡片保留 12px 圆角，不强行做透明窗口圆角。
13. **1200 窄档网络来源**：定稿固定列 736 + 星号 160 = 896，理论上 ≤ 905 有效视口；实测 DataGrid 的边框/竖向滚动条/单元格边距仍会额外占用，截图中出现横向滚动条。星号列仍保 160 下限，不会变 0；设计稿允许横向滚动，真机需确认可接受。
14. **行高**：`DialogDataGridRow` 的 `Margin` 已在视图内覆盖为 0，根行实测 54、子行 40，与设计一致。
15. **类型 chip 描边**：`DialogTag` 有 1px 描边 + 圆角 4，设计稿是无描边圆角 6；统一走共享 Tag 键，不另造一套。
16. **卡片头标题图标**：WPF 保留 `DialogCardTitleIcon`（设计稿右栏卡片头没有图标），属信息密度差异。
17. **空态组头**：WPF 分组视图在没有通信连接时不会生成空的「通信连接」组头；设计稿固定显示两组。无连接时只显示「本地与系统」组，属正常分组行为。
18. **DPI / 焦点 / 无障碍**：本批按 96 DPI 渲染证据；125%/150% 与高对比度、`AutomationProperties` 需真机复核，不在本批自动化范围。
19. **卡片头 / 状态条高度**：设计稿为 64 / 40，实施为 60 / 34（为 1300×700 下多留一行明细）；视觉差 4 / 6px，属有意取舍，已在渲染证据中体现。

## v1 明确延后（需新增功能或契约）

| # | 项 | 需要什么 |
| --- | --- | --- |
| D1 | 「添加变量」弹窗改版 | 新的弹窗视图 + 草稿渲染契约；当前是 `EasyDialog.ShowPropertyGridAsync` |
| D2 | 列选择 / 密度切换 / 排序 | VM 新增列状态与排序命令 |
| D3 | 多选批量删除 | VM 新增选中集合与批量命令 |
| D4 | 变量详情检查器 | VM 新增 `SelectedNode` 与详情字段 |
| D5 | 导入 / 导出变量表 | 新的文件契约与校验 |
| D6 | 历史趋势 / 迷你曲线 | 按变量采样与环形缓冲 |
| D7 | 来源节点右键菜单（刷新连接 / 查看连接） | 新的宿主导航契约 |

## 证据清单

| 文件 | 状态 |
| --- | --- |
| `evidence\design-v1-overview.png` | 整页总览（产品窗口 + 关键改动 + 契约保留 + 延后清单） |
| `evidence\design-v1-local.png` | 本地来源（默认态，含数组展开、图像只读、偏离 ↺） |
| `evidence\design-v1-network.png` | 网络来源（在线连接，含扫描组、地址、质量三态） |
| `evidence\design-v1-network-offline.png` | 网络来源（离线连接：红徽标 + 灰质量点 + 上次镜像值） |
| `evidence\design-v1-system.png` | 系统来源（只读横幅、只读 chip） |
| `evidence\design-v1-empty.png` | 空来源（0 个变量 + 添加变量） |
| `evidence\design-v1-search-empty.png` | 搜索无结果（`XYZ`） |
| `evidence\design-v1-write-error.png` | 网络写值失败（红框） |
| `evidence\design-v1-narrow.png` | 1200 窄档（网络来源） |

### WPF 复刻证据（2026-10-20）

| 文件 | 说明 |
| --- | --- |
| `evidence\before-render-v1-local.png` | 改前旧两栏布局（2026-10-15 渲染探针） |
| `evidence\after-render-v1-local.png` | 本地来源（真实 VM；6 变量 + 数组展开 + 图像只读） |
| `evidence\after-render-v1-network.png` | 网络在线（5 变量，质量三态/扫描组/地址） |
| `evidence\after-render-v1-network-offline.png` | 网络离线（3 变量，红徽标 + 灰质量点） |
| `evidence\after-render-v1-system.png` | 系统来源（只读横幅 + 只读 chip + 锁值） |
| `evidence\after-render-v1-empty.png` | 空来源（0 个变量 + 添加变量） |
| `evidence\after-render-v1-search.png` | 搜索无结果 |
| `evidence\after-render-v1-write-error.png` | 写值失败红框（Validation.MarkInvalid 模拟） |
| `evidence\after-render-v1-narrow.png` | 1200 窄档（横向滚动） |

验证结论：`FlowCanvasChecks` **2754 通过 / 0 失败**；`UIThemeSmokeTest --global-variables` **全部通过**；默认 `UIThemeSmokeTest` 颜色令牌 **无新增硬编码颜色**；`_ResProbe` **ALL_OK**。详见 `docs\code-changes\2026-10-20-变量管理GlobalVariableView Web化复刻.md`。

## 变更规则

设计基线确认后只读。需要调整时升版本（v2），并同步更新：
1. 本目录 `README.md` 的版本/日期/确认人；
2. `design.html` 的布局、令牌与状态；
3. 已复刻的 `GlobalVariableView.xaml` 与令牌映射表；
4. `docs\UI设计基线\README.md` 的基线索引；
5. `docs\code-changes\` 的复刻记录。
