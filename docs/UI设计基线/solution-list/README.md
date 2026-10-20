# 方案列表（解决方案视图）· 设计基线

| 项 | 内容 |
| --- | --- |
| 版本 | v1 |
| 日期 | 2026-10-20 |
| 确认日期 / 人 | 2026-10-20 / 用户（指令：按照这个设计来，做好测试） |
| 覆盖范围 | `VisionMaster\Views\DialogViews\SolutionListView.xaml` + `SolutionListView.xaml.cs`（仅 UI 转换器/处理器） |
| 窗口尺寸 | 960 × 640 默认；900 × 560 最小；右侧动作栏 152px |
| 设计基线文件 | `design.html`（自包含、无外部依赖、内联 SVG 图标） |
| 设计证据 | `evidence\design-v1-*.png`（正常 / 空清单 / 未选中 / 保存失败 / 900 窄档） |
| 复刻状态 | **已复刻（2026-10-20）**；WPF 证据 `evidence\after-render-v1-*.png`；待用户真机确认 |
| 记录 | `docs\code-changes\2026-10-20-解决方案视图Web化.md` |

## 定位

本文件是「Web 设计基线 → 用户确认 → WPF 复刻 → 同屏截图验收」四步流程的**第 ① 步**产物，
流程口径沿用 `docs\UI设计基线\插件自定义视图Web化改造SOP.md`（R126-R132）。

本页是**宿主弹窗**，不是插件自定义视图，因此没有 `IPluginConfigView` / `LinkableValueEditor` / HALCON 画布；
但“先出完整 Web 设计稿、确认后再改 WPF、零功能改动、断言守门”的纪律完全一致。
可对照的先例是 `docs\UI设计基线\global-variables\README.md`（同样是宿主弹窗）。

Web 只作设计媒介，**不进入构建、不部署**。确认后按本基线做 WPF 复刻，第 ④ 步以同一屏、同一状态的截图对照验收。

## 一句话方向

**保留“表格 + 右侧动作栏”的交互路径，把“主次 / 默认方案 / 空态 / 注释编辑”四件事做清楚。**

不重排信息架构，不把动作塞进行内菜单；用主色、危险色、状态徽标与安静输入框，让“打开哪份方案、默认跑哪份、注释在哪改”一眼可见。

## 审查清单（请重点看这几项）

- [ ] 右侧动作栏按「打开 / 清单 / 排序 / 危险操作」分组，主次是否清楚。
- [ ] 「确认」是主色、「取消」是次要色、「从清单移除」是危险色（不再倒置）。
- [ ] 表格新增「默认」列；默认方案在行内与底部状态条同时可见。
- [ ] 注释单元格是否明显可编辑（hover 边界 + 铅笔 + focus 主色环）。
- [ ] 空清单空态是否给出「添加当前方案 / 浏览添加…」两个现有入口。
- [ ] 900 最小宽度下是否无横向滚动（路径列 220px，实测）。
- [ ] 是否接受把「删除」文案改为「从清单移除」（该动作不删磁盘文件）。
- [ ] 是否接受把表格从 `ListView + GridView` 换成 `DataGrid`（为了路径星号列；见“WPF 列宽计划”）。

## 现状审查（改动理由）

| # | 现状 | 影响 |
| --- | --- | --- |
| 1 | 整窗深色（`#1E1E22`）+ 蓝紫强调色（`#6B6BEF`），与全仓浅色 Web 基线不是一套语言 | 与变量管理、条件编辑器、相机设置等弹窗并排时像两个产品 |
| 2 | 7 颗操作按钮全部同色同重；“删除”与“添加”外观一致 | 打开、排序、移除没有主次，危险动作不显眼 |
| 3 | “取消”是蓝紫主色、“确认”是红色 | 肯定动作被画成危险动作，最容易点错 |
| 4 | 固定 900 宽 + 列宽 50/150/220/330 = 750 > 列表视口 722 | 默认尺寸必出横向滚动条，路径列右侧 28px 被推出视野 |
| 5 | 空清单只有一块空白 + 列头 | 看起来像加载失败，没有“下一步做什么”的入口 |
| 6 | 注释列是裸 `TextBox`（透明底、无边框、无共享样式） | 用户看不出这一列能改，也没有 focus/脏标记 |
| 7 | 默认启动方案只在底部一行只读文本里出现 | 表格里看不出哪一行是默认，改默认要来回对照 |
| 8 | 列头手写深色样式、模板回落 Aero2（带竖分隔线） | 库里的 `GridListHeaderStyle` 已经解决了这个问题，这里重复造且更旧 |
| 9 | 视图零 `Dialog*` 令牌、16 处六位硬编码色 + 6 处三位简写 | 切主题/统一换肤时这一页不会跟着变 |
| 10 | 无空态、无焦点环、无 ToolTip、无 Enter/Esc | 键盘用户与长路径场景不可用 |

## v1 方向

1. **保留两区结构**：左侧方案表（自适应）+ 右侧动作栏（152px）；不改成顶部工具栏或行内“⋯”菜单。
2. **动作分组**：`打开方案`（主按钮）→ `清单`（设为默认 / 添加当前方案 / 浏览添加…）→ `排序`（上移 / 下移）→ `危险操作`（从清单移除）。
3. **表格五列**：序号 40 / 方案名称 `1.2*`（MinWidth 160）/ 注释 `1.0*`（MinWidth 100）/ 路径 `1.6*`（MinWidth 220）/ 默认 72；行高 52、表头 40。
4. **默认方案双处可见**：行内「默认启动」chip + 底部“默认启动 + 路径”状态条；判据是 `Path == StartupSolutionPath`，纯显示派生。
5. **注释是安静输入框**：hover 出现边界、focus 出现主色环与铅笔图标；仍绑 `Comment`，不改提交语义。
6. **空态独立**：空清单显示图标 + 说明 + 「添加当前方案 / 浏览添加…」；“未选中”是另一种状态（行还在、动作禁用 + 提示）。
7. **主次与危险语义归位**：确认=主色、取消=次要、从清单移除=危险色；打开方案=主色。
8. **全令牌化**：根背景 `DialogBackgroundBrush`、卡片 `DialogSurfaceBrush`、按钮 `DialogPrimaryButton` / `DialogDangerButton` / `DialogSecondaryButton`、输入 `DialogCellInputQuiet`、空态 `DialogEmptyState*`；视图零十六进制，**不新增颜色键**。
9. **键盘与可达性**：Esc 取消；Enter 在非输入框时确认、在注释框内提交本行；行/按钮可见焦点环；长路径 ToolTip。

## 布局与尺寸

```text
窗口 960 × 640（最小 900 × 560）
┌──────────────────────────────────────────────────────────────────────┐
│ 宿主标题栏 44（Prism DialogWindow，示意）                       [×]  │
├───────────────────────────────────────────────────────────┬──────────┤
│ 方案清单卡片                                              │ 动作栏   │
│ ┌───────────────────────────────────────────────────────┐ │ 152      │
│ │ 卡片头 64：图标 + 方案清单 + 计数 + 默认 chip + 副标题 │ │ 打开方案 │
│ ├───────────────────────────────────────────────────────┤ │          │
│ │ 表头 40：序号 / 方案名称 / 注释 / 路径 / 默认         │ │ 清单     │
│ │ 行 52 × N                                             │ │ 设为默认 │
│ │ ...                                                   │ │ 添加当前 │
│ │                                                       │ │ 浏览添加 │
│ │                                                       │ │ 排序     │
│ │                                                       │ │ 上移/下移│
│ │                                                       │ │ 危险     │
│ │                                                       │ │ 从清单移除│
│ ├───────────────────────────────────────────────────────┤ │          │
│ │ 底栏 56：默认启动 + 路径 chip      取消 / 确认 + 键盘 │ │ 说明     │
│ └───────────────────────────────────────────────────────┘ │          │
└───────────────────────────────────────────────────────────┴──────────┘
```

外边距 16×2 + 动作栏 152 + 间隔 12 = 196；960 下卡片 **764**，900 下卡片 **704**。

### WPF 列宽计划

| 列 | 宽度 | 说明 |
| --- | --- | --- |
| 序号 | 40 固定 | 等宽数字，`Index` 由 `RefreshIndexes()` 重排，不持久化 |
| 方案名称 | `1.2*`（MinWidth 160） | 960 下约 199；文件图标 + 名称，超长省略 + ToolTip |
| 注释 | `1.0*`（MinWidth 100） | 960 下约 162；`DataGridTemplateColumn` + 安静输入框；绑定 `Comment` |
| 路径 | `1.6*`（MinWidth 220） | 960 下约 265、900 下 220（MinWidth）；等宽字体、省略 + ToolTip |
| 默认 | 72 固定 | `Path == StartupSolutionPath` 时显示「默认启动」chip，否则「—」 |

- 960 下实测：名称约 199 / 路径约 265（`--solution-list` 的 `ActualWidth`）；900 下路径 220（MinWidth）。列宽合计已扣行内 `Padding="0,12"`、卡片边框与 WPF 滚动条（约 17px）；两档都 ≥ 220，**无横向滚动**。
- 注意：列宽合计必须扣掉行内 `Padding="0,12"`、卡片边框与竖向滚动条（WPF 约 17px，Web 稿按 9px）；这正是当前视图 750 > 722 溢出的同一类算术错误。
- **WPF 实现建议改用 `DataGrid`**：`GridView` 没有星号列宽（R130 已点名），当前 750 > 722 的溢出就是它造成的。
  用 `DataGrid` 时关闭排序/列重排/列宽拖拽（`CanUserSortColumns=False`、`CanUserReorderColumns=False`、`CanUserResizeColumns=False`），
  保持“列集合固定”的现状；样式走 `DialogDataGrid` / `DialogDataGridRow` / `DialogDataGridColumnHeader` / `DialogDataGridCell`。
- 若坚持保留 `ListView + GridView`：必须保留 R19-R22 三条（`GridListViewStyle` + `GridViewRowPresenter` + 行内边距 4），
  并把路径列固定为 220（900 档）或把卡片宽度固定为 704；否则横向滚动条会回来。

## 文案变更（只改按钮文字，不改命令）

| 现文案 | 设计文案 | 理由 |
| --- | --- | --- |
| 打开 | 打开方案 | 与“打开文件/打开方案”区分，动词更明确 |
| 设置默认启动 | 设为默认启动 | 与“默认启动”状态条同一措辞 |
| 添加 | 浏览添加… | 与“添加当前方案”区分；该命令会弹文件选择框 |
| 删除 | 从清单移除 | 该命令只移出清单，不删磁盘 `.vms`；避免用户误以为会删文件 |
| 取 消 / 确 认 | 取消 / 确认 | 去掉字间空格，与全仓 Web 基线一致 |

> 按钮文案不参与持久化（`.vms` / `AppConfig.json` 都不存），所以这是纯 UI 变更；
> 9 个命令名与绑定一个都不改。

## 契约保留（一个都不能改）

| # | 契约 | 为什么不能动 |
| --- | --- | --- |
| C1 | `DataContext = SolutionListViewModel`（Prism `RegisterDialog` + `ViewModelLocationProvider.Register`） | 换独立 VM = 命令、对话框生命周期、宿主注册全部要重接；视图不自己 `new` VM |
| C2 | `Solutions` / `SelectedSolution` / `StartupSolutionPath` | 清单工作副本、当前选中、默认启动路径的唯一状态源；筛选/排序都从这里派生 |
| C3 | 9 个命令：`OpenCommand` / `SetStartupCommand` / `AddCurrentCommand` / `AddCommand` / `DeleteCommand` / `MoveUpCommand` / `MoveDownCommand` / `ConfirmCommand` / `CancelCommand` | 全部是功能入口；不允许出现死按钮，也不允许把入口藏掉 |
| C4 | 注释 TwoWay：`Comment` + `UpdateSourceTrigger=PropertyChanged` | 直接改工作副本；`OnConfirm` 的 `SameList` 比对依赖它 |
| C5 | `Index` 是显示值 | `RefreshIndexes()` 按顺序重排；不落盘，不改成可编辑字段 |
| C6 | `OnDelete` 只移出清单 | 不删磁盘 `.vms`；若删的是默认方案，VM 会清空 `StartupSolutionPath`。文案改成“从清单移除”是为了说清这件事 |
| C7 | `OnConfirm` 只写变了的项 | 保存失败不关窗、错误提示照旧；审计 `ScadaAuditWriter` 与署名口径不变 |
| C8 | `OnDialogOpened` 拷贝工作副本 | 取消不污染 `AppConfig.json`；这是“取消”语义的全部 |
| C9 | 宿主窗口 | 标题栏/关闭按钮/窗口尺寸由 Prism `DialogWindow` 提供；视图不新增自绘标题栏 |

## 允许的 VM 改动

**无。** 本设计不新增 VM 属性、不新增命令、不改持久化。

三处“看起来像新状态”的显示，全部由现有数据派生：

| 显示 | 派生方式 | 实现位置 |
| --- | --- | --- |
| 行内「默认启动」chip | `Path == StartupSolutionPath` | 视图级 `IMultiValueConverter`（行 `Path` + 祖先 `DataGrid.DataContext.StartupSolutionPath`） |
| 卡片头「默认：<名称>」 | 在 `Solutions` 里找 `Path == StartupSolutionPath` 的项 | 同上，或一个只读 `MultiBinding` 转换器 |
| 空态 / 未选中 | `Solutions.Count == 0` / `SelectedSolution == null` | `DataTrigger`（空态）/ `NullToVisibility` 转换器（未选中），不需要 VM 属性 |

> 若评审认为“转换器里做查找”太重，可把“默认方案名称”做成 VM 的只读派生属性；
> 但那属于 VM 改动，必须单独评审（本版按“零 VM 改动”冻结）。

## 令牌映射

| 设计变量 | 值 | WPF 资源键 |
| --- | --- | --- |
| `--sl-bg` | `#EEF2F7` | `DialogBackgroundBrush` |
| `--sl-surface` | `#FFFFFF` | `DialogSurfaceBrush` |
| `--sl-surface-2` | `#F8FAFC` | `DialogRowAlternateBrush` |
| `--sl-surface-3` | `#F1F5F9` | `DialogRowHoverBrush` |
| `--sl-border` | `#E2E8F0` | `DialogBorderBrush` |
| `--sl-border-soft` | `#EEF2F7` | `DialogBorderLightBrush` |
| `--sl-border-strong` | `#CBD5E1` | `DialogBorderStrongBrush` |
| `--sl-text` | `#0F172A` | `DialogTextPrimaryBrush` |
| `--sl-text-2` | `#475569` | `DialogTextSecondaryBrush` |
| `--sl-text-3` | `#94A3B8` | `DialogTextMutedBrush` |
| `--sl-primary` | `#0EA5E9` | `DialogAccentBrush` |
| `--sl-primary-strong` | `#0284C7` | `DialogAccentDarkBrush` |
| `--sl-primary-text` | `#0369A1` | `DialogAccentTextBrush` |
| `--sl-primary-soft` | `#F0F9FF` | `DialogAccentSoftBrush` |
| `--sl-primary-light` | `#E0F2FE` | `DialogAccentLightBrush` |
| `--sl-primary-border` | `#BAE6FD` | `DialogAccentBorderBrush` |
| `--sl-danger*` | `#EF4444 / #B91C1C / #FEF2F2 / #FECACA` | `DialogDangerBrush` / `DialogDangerTextBrush` / `DialogDangerSubtleBrush` / `DialogDangerBorderBrush` |
| `--sl-success*` | `#22C55E / #15803D / #F0FDF4 / #BBF7D0` | `DialogSuccessBrush` / `DialogSuccessTextBrush` / `DialogSuccessSubtleBrush` / `DialogSuccessBorderBrush` |
| `--sl-warning*` | `#F59E0B / #B45309 / #FFF4E5 / #FDE68A` | `DialogWarningBrush` / `DialogWarningTextBrush` / `DialogWarningSubtleBrush` / `DialogWarningBorderBrush` |
| 圆角 6/8/10/12/16 | — | WPF `CornerRadius` 字面量（仓内暂无圆角令牌） |
| 间距 4/8/12/16 | — | WPF `Margin` / `Padding` 字面量 |
| 字体 | Segoe UI / Microsoft YaHei UI | 同族系统字体；等宽用 Cascadia Mono / Consolas |

**新增颜色键：0 个。** 本页只复用 `Colors.xaml` 的既有语义键；不改既有键/值，不造第二套蓝/灰。

## 设计块 → 现有样式键（R141 / R117 对照）

| 设计块 | 复用键 | 说明 |
| --- | --- | --- |
| 窗口背景 / 卡片 | `DialogBackgroundBrush` / `DialogSurfaceBrush` / `DialogCardBorder` | 卡片 12px 圆角；不改共享键 |
| 主按钮 / 次要按钮 / 危险按钮 | `DialogPrimaryButton` / `DialogSecondaryButton` / `DialogDangerButton` | 打开/确认=主色；取消/清单动作=次要；从清单移除=危险 |
| 表格 / 行 / 表头 / 单元格 | `DialogDataGrid` / `DialogDataGridRow` / `DialogDataGridColumnHeader` / `DialogDataGridCell` | 若保留 GridView：`GridListViewStyle` + `GridListHeaderStyle` + 行模板（R19-R22） |
| 注释输入 | `DialogCellInputQuiet` | 安静输入框；hover/focus 边界与主色环 |
| 默认 chip / 计数徽标 | 卡片头：内联 `Style` BasedOn `DialogChip` + `DialogAccentSoftBrush` / `DialogAccentBorderBrush`（1px、6px）；行内：BasedOn `DialogChip` + `DialogAccentLightBrush`（6px）；计数徽标：`DialogChipAccent` / `DialogChipTextAccent` | 默认 chip 不再用通用 9px 胶囊；卡片头是 soft 底 + 描边 |
| 空态 | `DialogEmptyStateIcon` / `DialogEmptyStateTitle` / `DialogEmptyStateHint` | 空清单；按钮复用主/次按钮键 |
| 焦点 / ToolTip | `DialogFocusVisual` / `DialogToolTipStyle` | 所有可聚焦控件；路径/名称长文本 |
| 路径等宽 | `DialogTextSecondaryBrush` + `Typography.NumeralAlignment="Tabular"` | 序号列也走 tabular |

**样式纪律**：复刻后 `SolutionListView.xaml` 里不再有本地 `<Style x:Key=...>`；
把该文件加入 `FlowCanvasChecks\VariableBindingCheck.cs` 的 `CollectedDialogViews`，
并删除 `UIThemeSmokeTest\hardcoded-colors.baseline.txt` 的 `SolutionListView.xaml|16` 行。

## WPF 复刻映射（第 ③ 步计划）

| 设计项 | WPF 实现 |
| --- | --- |
| 窗口内容区 | 根 `UserControl` 不写死 `Width/Height`，只设 `MinWidth="900" MinHeight="560"`；首次 `Loaded` 通过 `Window.GetWindow(this)` 把宿主窗口钉到 **960×640**（最小 900×560），之后内容随窗口伸缩 |
| 两区布局 | `Grid` 两列：`*` + `152`，`Margin="16"`，列间隔 12 |
| 卡片头 | `Border` + `Grid`：32px 图标块 + 标题 + 计数 + 默认 chip + 副标题 |
| 表格 | `DataGrid`：`AutoGenerateColumns=False`、`HeadersVisibility=Column`、`CanUserSortColumns=False`、`CanUserReorderColumns=False`、`CanUserResizeColumns=False`、`AlternationCount=1`、`GridLinesVisibility=Horizontal`、`ScrollViewer.HorizontalScrollBarVisibility=Disabled`；行高 52、表头 40（列头 `Padding="12,0,0,0"`） |
| 序号 / 名称 / 路径 / 默认 | 序号/名称/路径/默认均为 `DataGridTemplateColumn`；名称 `1.2*`（MinWidth 160）、注释 `1.0*`（MinWidth 100）、路径 `1.6*`（MinWidth 220）+ `TextTrimming` + ToolTip；选中态 2px 左条画在序号单元格内（避免 Row.BorderThickness 造成内容位移） |
| 注释 | `DataGridTemplateColumn` 内 `TextBox`，`Style="{StaticResource DialogCellInputQuiet}"`，`Text="{Binding Comment, UpdateSourceTrigger=PropertyChanged}"`；右侧铅笔图标 `IsHitTestVisible=False` |
| 默认 chip | `DataGridTemplateColumn` + `MultiBinding`（行 `Path` + 祖先 `StartupSolutionPath`）→ `Visibility` |
| 空态 | 表格上层 `Grid` 覆盖（表头保持可见），`DataTrigger` 绑 `Solutions.Count == 0`；两个按钮绑 `AddCurrentCommand` / `AddCommand` |
| 未选中 | 动作按钮绑命令（`CanExecute` 已按 `SelectedSolution` 控制）；rail 提示用 `NullToVisibility` 转换器（参数 `Invert`） |
| 底栏 | `Grid` 两列：默认启动 label + 路径 chip；右侧取消/确认 + 键盘提示 |
| 键盘 | 取消 `IsCancel="True"`；确认 `IsDefault="True"`；注释 `TextBox` 的 `PreviewKeyDown` 里处理 Enter（`UpdateSource` 后 `e.Handled=true`，提交本行、不冒泡到确认） |
| 焦点 | 按钮/行/输入统一 `FocusVisualStyle="{StaticResource DialogFocusVisual}"` |
| ToolTip | 路径/名称/危险动作：`<ToolTip Style="{StaticResource DialogToolTipStyle}" Content="…"/>` |
| 图标 | Font Awesome 6 Pro Solid（`DialogIconFont`）；Web 设计稿用 Lucide 描边，形状不同、语义一致（已知偏差） |

## 验证结果（2026-10-20）

| 层 | 结果 |
| --- | --- |
| 构建 | `VisionMaster.csproj` 0 错误（861 个既有警告）；`UIThemeSmokeTest` 0 错误（25 个既有警告）。 |
| 静态 | `--solution-list` 静态 4 条全 PASS：XAML 可读、`hex=0`、无本地 keyed Style、关键绑定/结构齐全。 |
| 断言 | `--solution-list` **32/32 PASS**（chip 样式改动前的最后一次完整运行）：DataContext、5 列、列宽 40/1.2*/1.0*/1.6*220/72、行高 52、960 名称 199/路径 265、900 路径 220、默认 chip 只在默认行、卡片头摘要/计数、底栏路径、9 命令、选中/未选中/排序边界、注释回写、焦点环、ToolTip 28/28、空态、未选中提示、宿主窗口 960×640/min 900×560。**chip 样式新增 3 条断言**（卡片头 soft 底/1px 边框/6px 圆角、行内 6px、空态 neutral），宿主 `CameraSettingsViewModel.cs` 的 `#region` 未闭合阻塞构建，待修复后重跑（预期 35/35）；临时探针已实测 `bg=#F0F9FF / border=#BAE6FD / radius=6`。 |
| 渲染 | `--solution-list-render` 5 张 + `--solution-list` 的 `after-render-v1-window.png`（真实窗口内容 960×640）+ 探针 `after-render-v1-chip.png`（chip 样式）：normal / empty / noselect / long-path / narrow / window / chip。 |
| 回归 | 默认 `UIThemeSmokeTest`：颜色令牌 `无新增硬编码颜色`，GridView / Shell / Panel / LogConsole 全部通过，退出码 0。`FlowCanvasChecks` **2858 通过 / 1 项失败**：`[S4] HttpImageServer.Start()` 端口 19000 被运行中的 `VisionMaster.exe` 占用（PID 随重启变化），属环境冲突，与本次改动无关；关闭宿主后重跑应为 0 失败。 |
| 收编 | `VariableBindingCheck.RunCollectedViewStyleContract` 扫描 **7 个弹窗**（含 `SolutionListView.xaml`），颜色与本地 Style 两项均 PASS；`hardcoded-colors.baseline.txt` 已删除 `SolutionListView.xaml|16`。 |
| 资源 | 本批不新增颜色键、不改资源合并结构；资源断链契约不受影响。 |
| 变异反证 | 静态闸已做：临时给卡片头 `Border` 注入 `BorderBrush="#FF0000"` → `XAML 无硬编码十六进制颜色` 准确红（1 项失败）；还原后复绿。运行时闸的「删默认 chip」变异未做（会触发 VisionMaster 重建，而运行中的宿主锁着 `VM.FlowEngine.dll`），列为待补。 |
| 真机 | **待用户**：先关闭运行中的 VisionMaster/VS 并重建（bin 的 exe/DLL 被锁；本次用 obj 新 DLL 喂冒烟宿主），再走：打开 → 选行 → 打开方案（成功/文件不存在）→ 设为默认 → 上移/下移 → 改注释 → 添加/浏览添加 → 从清单移除 → 确认/取消 → 重开。 |

## 已知偏差（WPF 复刻时按 R130 记录）

1. **图标**：Web 用 Lucide 描边 SVG；WPF 用 Font Awesome 6 Pro Solid（`DialogIconFont`），形状不同、语义一致。
2. **字体度量**：同字号下 Segoe UI / 微软雅黑与 Web 的字宽、行高略有差异；不按 px 硬套。
3. **滚动条**：WPF 约占 17px；900 窄档路径列预算 221px、实测 220px（`--solution-list`），仍 ≥ 220，允许纵向滚动、不允许横向滚动。
4. **DataGrid 与设计网格**：Web 稿用 CSS Grid 表达列宽；WPF `DataGrid` 的列边框/单元格边距可能带来 1–2px 差异。
5. **宿主标题栏**：设计稿里的 44px 标题栏只是示意；真实标题栏由 Prism `DialogWindow` 提供，视图不重复画。
6. **窗口圆角 / 阴影**：Web 是 16px 圆角 + 多层阴影；WPF 无边框窗口的圆角由宿主决定，卡片保留 12px 圆角。
7. **焦点态**：Web 用 `:focus-visible` 2px 主色环；WPF 走 `DialogFocusVisual`，形状可能略有差异。
8. **动效**：Web 150ms ease；WPF 用 `Storyboard` 近似，或不做动效（以稳定为先）。
9. **DPI**：本批按 96 DPI 渲染证据；125% / 150% 下 1px 线与列宽需真机复核。
10. **无障碍**：首版给注释输入补了 `AutomationProperties.Name`；路径/按钮用可见文本 + ToolTip；完整屏幕阅读器支持需单独验收。
11. **删除二次确认**：本版只做危险色与文案澄清（“从清单移除”）；确认框/撤销栈列入延后（D5）。
12. **脏标记**：本版不显示“未保存”；`OnConfirm` 仍按“只写变了的项”执行。
13. **行内默认 chip 文案取「默认」**：列宽 72 − 单元格左内边距 12 = 60；「默认启动」四字 + 星形约 67px 会溢出，故行内 chip 写「默认」，ToolTip 写「默认启动方案」；卡片头与底栏仍写全「默认启动」。
14. **右栏按钮文字居中**：Fluent 按钮模板的 `ContentPresenter` 固定 `HorizontalAlignment=Center`；设计稿是左对齐。未改共享模板（R141），按钮仍是图标 + 文字。
15. **窗口尺寸**：视图不写死 `Width/Height`；首次 `Loaded` 把宿主窗口钉到 960×640（最小 900×560），之后内容随窗口伸缩。
16. **空态主按钮**：用 `DialogPrimaryButton` 实心主色（设计稿是 soft 主色）；未新增 soft 按钮变体（R141）。
17. **保存失败 toast**：由宿主 `Notifier` 提供，不在视图内渲染；`--solution-list-render` 因此没有 save-error 图，VM 侧行为由 `ScadaChecks.SolutionListAuditChecks` 覆盖。
18. **列头「序号」**：共享列头样式是左右各 12 内边距，40px 列会被挤成 16px（实测「序号」被裁）；本视图把列头 `Padding` 覆盖为 `12,0,0,0`，不改共享键。
19. **名称/注释/路径改为比例列**：设计稿是 40/152/176/*/72 固定列；真实方案名「解决方案-20260829224154」在 152 下会被截断，WPF 改为名称 `1.2*` / 注释 `1.0*` / 路径 `1.6*`（960 下约 199/162/265，900 下路径 220），路径仍 ≥220。

## v1 明确延后（需新增功能或契约）

| # | 项 | 需要什么 |
| --- | --- | --- |
| D1 | 搜索 / 筛选 | VM 新增 `SearchText` 与筛选集合 |
| D2 | 多选批量移除 | VM 新增选中集合与批量命令 |
| D3 | 拖拽排序 | 新的 Drop 契约（现有只有上移/下移） |
| D4 | 重命名方案名称 | 新命令与落盘规则（名称来自文件名） |
| D5 | 删除二次确认 / 撤销 | 确认对话框或撤销栈 |
| D6 | 未保存脏标记 | VM 新增 `IsDirty` |
| D7 | 打开文件所在文件夹 | 新命令 + `Process.Start` 口径 |
| D8 | 清单导入 / 导出 | 新的文件契约与校验 |

## 证据清单

| 文件 | 状态 |
| --- | --- |
| `design.html` | 已产出（自包含，可直接浏览器打开） |
| `evidence\design-v1-normal.png` | 已产出（正常：选中行 2 + 默认 chip 在行 1） |
| `evidence\design-v1-empty.png` | 已产出（空清单） |
| `evidence\design-v1-noselect.png` | 已产出（有数据、未选中、动作禁用） |
| `evidence\design-v1-save-error.png` | 已产出（保存失败 toast） |
| `evidence\design-v1-narrow.png` | 已产出（900 窄档，默认 chip 不被裁切） |
| `evidence\before-render-v1.png` | 已产出（当前深色实现，900×560；临时探针渲染） |
| `evidence\after-render-v1-normal.png` | WPF 复刻：正常（选中行 2 + 默认 chip 在行 1） |
| `evidence\after-render-v1-empty.png` | WPF 复刻：空清单（标题/说明居中 + 两个入口） |
| `evidence\after-render-v1-noselect.png` | WPF 复刻：有数据未选中（动作禁用 + 提示） |
| `evidence\after-render-v1-long-path.png` | WPF 复刻：超长路径（省略 + ToolTip） |
| `evidence\after-render-v1-narrow.png` | WPF 复刻：900×560（路径 220px，无横向滚动） |
| `evidence\after-render-v1-window.png` | WPF 复刻：真实宿主窗口内容 960×640（`Window.GetWindow` 尺寸断言通过） |
| `evidence\after-render-v1-chip.png` | 探针：卡片头默认 chip 的 soft 底 / 1px 边框 / 6px 圆角（`bg=#F0F9FF`、`border=#BAE6FD`） |

> 设计稿证据来自 `design.html`；WPF 证据来自 `--solution-list-render`。两套图都只证明布局与状态，
> 真机交互（打开/确认/取消/文件不存在）仍需用户按验证结果表最后一行验收。

## 变更规则

设计基线确认后只读。需要调整时升版本（v2），并同步更新：

1. 本目录 `README.md` 的版本/日期/确认人；
2. `design.html` 的布局、令牌与状态；
3. 已复刻的 `SolutionListView.xaml` 与令牌映射表；
4. `docs\UI设计基线\README.md` 的基线索引；
5. `docs\code-changes\` 的复刻记录。
