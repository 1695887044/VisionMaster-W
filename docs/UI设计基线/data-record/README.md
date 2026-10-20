# CSV记录（Plugin.DataRecord）· 配置窗口设计基线

| 项 | 内容 |
| --- | --- |
| 版本 | v1 |
| 日期 | 2026-10-20 |
| 确认日期 / 人 | 待用户确认（按 SOP §5.2：批量改造沿用 ImageAcquisition V2 视觉语言，插件页单独产出设计稿） |
| 覆盖范围 | `Plugins\Plugin.DataRecord\DataRecordView.xaml` + `.xaml.cs`（插件内容区）；宿主外壳 `PluginConfigShellView` 仅作为既定 960×680 / 最小 640×560 容器，不在本基线内改动 |
| 设计基线文件 | `design.html`（自包含、无外部依赖、可点击：宽度 960/640、图片记录三档、试写三态、空列边界、增删列、来源三态） |
| 复刻状态 | 已完成（2026-10-20）；批量冒烟证据 `docs/UI设计基线/_batch-evidence/Plugin.DataRecord-DataRecordPlugin.png`；基线内副本 `evidence/after-render-v1-initial.png`（仅初始态，HALCON 不适用） |

## 1. 定位

按 `AGENTS.md` 与 `docs/UI设计基线/插件自定义视图Web化改造SOP.md`：本文件是 Web 设计基线（设计媒介，不进构建），
确认后按基线复刻 WPF 视图；验收以「同一屏、同一状态」的截图对照为准。

本插件是 **S1 级表单型**（无 HALCON 画布、无独立 VM、无数据网格控件），核心复杂度在：

- `Columns` 是 `ObservableCollection<RecordColumnDef>`，列名 / 来源 / 启用变化会重建动态输入端口；
- 每行「取值」单元格有 **端口 / 变量 / 内置** 三态，端口态必须承载 `LinkableValueEditor`；
- 图片记录方式决定「图片」端口是否存在，切回「不存图」时端口必须消失；
- 没有 `StatusMessage` / `StatusLevel`，只有 `TestResult` 文本（✔/✘ 前缀）。

## 2. 现状审查（改动理由）

| # | 现状 | 影响 |
| --- | --- | --- |
| 1 | 四张上下堆叠的卡片（账本文件 / 列定义 / 图片记录 / 测试）全部塞进一个 `ScrollViewer` | 列一多，测试与图片配置被推到屏幕外；配置与表格互相挤占高度 |
| 2 | 列定义表 6 列固定宽度，表头与行是两套手写 Grid | 列宽调整要改两处；窄窗口下无横向滚动，取值列被压扁 |
| 3 | 每行无条件实例化 `LinkableValueEditor`，非端口行 `Port=null` | 批量冒烟 `LinkableValueEditor.Port` 断言会失败；空端口编辑器也白占对象 |
| 4 | 图片源 `LinkableValueEditor` 常驻，`ImageSaveMode=Off` 时 `Port=null` | 同上；切回「不存图」后控件仍持有空端口 |
| 5 | 测试结果是一行裸文本，颜色由 code-behind 直接写 `Color.FromRgb(0x2E,0x7D,0x32)` / `Brushes.Red` | 违反令牌化；换肤改不动，且颜色语义与 `DialogSuccess*` / `DialogDanger*` 不统一 |
| 6 | 三个动作按钮用 `Click` 事件 | 批量冒烟的 `Button.Command` 断言缺少可检查的绑定；入口不可被统一断言 |
| 7 | 账本目录 / 文件名 / 保留天数 / 三个开关没有分组节奏 | 字段密度高，用户很难一眼分清「文件位置」「写入策略」「失败策略」 |

## 3. v1 方向

> **配置在左、列定义在右；表格可滚、配置不丢；三态来源同屏可辨；不新增功能。**

1. **两栏固定分工**：左 356px 配置轨（账本文件 + 图片记录），右侧列定义工作区（表格 + 试写）。
2. **列定义是工作区**：表格占满右栏高度，行多时只滚表格；表头 sticky（WPF 用固定表头 + 独立行滚动）。
3. **来源三态**：端口 → `LinkableValueEditor`（按需创建；列名为空时不创建，避免 `Port=null`）；变量 → 变量名输入；内置 → 字段下拉。
4. **图片记录三档卡片**：不存图 / 仅NG存图 / 每件存图；非「不存图」才显示图片源与命名字段。
5. **试写结果状态化**：未试写 / 成功 / 失败三态横幅，只消费既有 `TestResult`。
6. **全令牌化**：颜色全部走 `Dialog*` / `Plugin*` 公共令牌；代码后置的硬编码色改为资源查找。
7. **动作入口命令化**：`AddColumn` / `DeleteColumn` / `TestWrite` 改绑视图命令，转发到既有插件方法。

## 4. 设计要点（完整清单）

1. 左栏为浅灰配置轨（`DialogRowAlternateBrush` + `DialogBorderLightBrush`），右侧为白色卡片；两层表面拉开配置与工作区。
2. 表单三件套：标签 12px / 控件 36px / 帮助 11.5px；字段间距 11px；输入区随栏宽伸缩。
3. 账本文件：目录、文件名、保留天数；保留天数 0 = 永久保留，>0 每天清理一次。
4. 写入策略用三行开关：新文件写表头 / 每行立即落盘 / 写入失败阻断流程；右侧小字解释代价。
5. 图片记录：三档卡片 + 条件字段；图片源是 `LinkableValueEditor` 的端口编辑器（🔗 / ✕ 由共享控件自带）。
6. 图片字段：结果列 + NG值（一行），命名模板 + 格式（一行），图片子目录（一行）。
7. 列定义表：启用 / 列名 / 来源 / 取值 / 格式 / 删除；行高 42px，输入 32px（表格密度档）。
8. 取值单元格三态：
   - 端口：`LinkableValueEditor`，按「列名 + 插件 + PortsVersion」取动态端口；
   - 变量：变量名输入框；
   - 内置：时间戳 / 序列号 / 步骤名 / 本轮耗时ms / 图片路径 下拉。
9. 表头与行共用同一套列宽（WPF 用 `Grid.IsSharedSizeScope` 或同一组 ColumnDefinition；本版用固定列宽 + 同一模板）。
10. 试写卡片：标题 + 按钮 + 说明 + 三态结果横幅；结果只反映本次试写，不代表流程运行状态。
11. 状态颜色只有三档：未试写（中性）/ 成功（`DialogSuccess*`）/ 失败（`DialogDanger*`）；不新增第四档。
12. 数字等宽：保留天数、列数、耗时使用 `Typography.NumeralAlignment="Tabular"` / `FontFamily` 等宽。
13. 图标按钮 30×30 + `Padding="0"`（R68）；焦点 `DialogFocusVisual`；ToolTip `DialogToolTipStyle`（R135）。
14. 窄档 640：左栏可滚动，右栏列定义表横向滚动（`ScrollViewer HorizontalScrollBarVisibility=Auto`），不隐藏功能。

## 5. 契约保留（一个都不能改）

| # | 契约 | 为什么不能动 |
| --- | --- | --- |
| C1 | `DataContext = 插件实例` + `IPluginConfigView` | 宿主 `viewObj.DataContext as IPluginConfigView` 取 `OnConfirm`；独立 VM = 确认不保存 |
| C2 | `LinkableValueEditor` 的 `Port` / `StepData` | 列行内按列名动态取端口；图片行按 `ImagePortNameValue` 取端口；`StepData` 来自插件；`SetLink` / `RemoveLink` 都在控件里 |
| C3 | 全部 `[StepConfig]` 属性双向绑定 | `DirectoryTemplate` / `FileNamePattern` / `KeepDays` / `WriteHeader` / `FlushEveryRow` / `BlockOnFailure` / `ImageSaveMode` / `ResultColumn` / `NgValue` / `ImageNameTemplate` / `ImageFormat` / `ImageDirName` |
| C4 | `Columns` 集合与 `PortsVersion` | 列名 / 来源 / 启用变化重建动态端口；`PortsVersion` 是行内 MultiBinding 的重取锚点 |
| C5 | 命令入口 | `AddColumn()` / `Columns.Remove()` / `TestWriteOneRow()` 原样调用；只把 Click 改为视图命令转发 |
| C6 | `TestResult` 三态视觉 | 只消费既有 ✔/✘ 前缀；不改 `TestWriteOneRow()` 文案、写入行为与返回类型 |

## 6. 令牌映射

| 设计变量 | 值 | WPF 资源键 |
| --- | --- | --- |
| `--dr-surface` | `#FFFFFF` | `DialogSurfaceBrush` |
| `--dr-surface-2` | `#F8FAFC` | `DialogRowAlternateBrush` |
| `--dr-surface-3` | `#F1F5F9` | `DialogRowHoverBrush` |
| `--dr-border` | `#E2E8F0` | `DialogBorderBrush` |
| `--dr-border-soft` | `#EEF2F7` | `DialogBorderLightBrush` |
| `--dr-border-strong` | `#CBD5E1` | `DialogBorderStrongBrush`（ImageAcquisition V2 已追加） |
| `--dr-text` | `#0F172A` | `DialogTextPrimaryBrush` |
| `--dr-text-2` | `#475569` | `DialogTextSecondaryBrush` |
| `--dr-text-3` | `#94A3B8` | `DialogTextMutedBrush` |
| `--dr-text-4` | `#64748B` | `ScadaSecondaryTextBrush`（#66708A，近似） |
| `--dr-primary` | `#0EA5E9` | `DialogAccentBrush` |
| `--dr-primary-strong` | `#0284C7` | `DialogAccentDarkBrush` |
| `--dr-primary-text` | `#0369A1` | `DialogAccentTextBrush` |
| `--dr-primary-soft` | `#F0F9FF` | `DialogAccentSoftBrush` |
| `--dr-primary-light` | `#E0F2FE` | `DialogAccentLightBrush` |
| `--dr-primary-border` | `#BAE6FD` | `DialogAccentBorderBrush` |
| `--dr-success` / text / soft / border | `#22C55E` / `#15803D` / `#F0FDF4` / `#BBF7D0` | `DialogSuccessBrush` / `DialogSuccessTextBrush` / `DialogSuccessSubtleBrush` / `DialogSuccessBorderBrush` |
| `--dr-danger` / text / soft / border | `#EF4444` / `#B91C1C` / `#FEF2F2` / `#FECACA` | `DialogDangerBrush` / `DialogDangerTextBrush` / `DialogDangerSubtleBrush` / `DialogDangerBorderBrush` |
| `--dr-warning` / text / soft / border | `#F59E0B` / `#B45309` / `#FFF4E5` / `#FDE68A` | `DialogWarningBrush` / `DialogWarningTextBrush` / `DialogWarningSubtleBrush` / `DialogWarningBorderBrush` |
| 圆角 6/8/10/12/16 | — | `PluginCardCornerRadius`（8）；其余为 WPF `CornerRadius` 字面量（仓内暂无圆角令牌字典） |
| 间距 4/8/12/16/20/24 | — | WPF `Margin` / `Padding` 字面量 |
| 字体 | Segoe UI / Microsoft YaHei UI | 同族系统字体；等宽用 `PluginMonoFontFamily`（Consolas / Microsoft YaHei） |

**结论：本插件视图不需要新增公共令牌。** 所需语义键在 `Colors.xaml` / `PluginConfigColors.xaml` 中已齐备（含 ImageAcquisition V2 追加的 `DialogBorderStrongBrush`、`DialogSuccessBorderBrush`、`DialogDangerTextBrush`、`DialogDangerBorderBrush`、`DialogWarningBorderBrush`）。若后续发现缺键，只报告、不自行追加（并发施工纪律）。

## 7. 复刻实现（WPF 映射）

| 设计项 | WPF 实现 |
| --- | --- |
| 两栏 | 根 `Grid`：左 `1*` MinWidth=300 MaxWidth=356；右 `1.6*` MinWidth=240；gap 16 |
| 左配置轨 | `Border` + `ScrollViewer`；`DialogRowAlternateBrush` + `DialogBorderLightBrush` + 14 padding |
| 账本文件字段 | `DialogInput`（38px）；保留天数 `DialogInput` + 帮助文字 |
| 三个开关 | 本地 `LocalCheckBoxStyle`（16×16 圆角框 + `DialogAccentBrush` 选中态） |
| 图片记录三档 | 3 个 `RadioButton` + `ModeCardStyle`，`IsChecked` 走 `ui:EnumConverter` 写 `ImageSaveMode` |
| 图片源 | `ContentControl` + `DataTemplate`：仅 `ImageSaveMode != Off` 时创建 `LinkableValueEditor`（避免空 Port） |
| 列定义表 | `ScrollViewer`（横向 Auto）+ 表头/行同区滚动 + `ItemsControl` 行模板；列宽 26/92/74/*/62/28 |
| 取值三态 | `ContentControl` + DataTrigger（Port 模板） / `TextBox`（Variable） / `DialogCombo`（BuiltIn） |
| 添加 / 删除 / 试写 | 视图 `ICommand` 转发到 `AddColumn()` / `Columns.Remove()` / `TestWriteOneRow()` |
| 试写结果 | 三态横幅：`TestResultStateConverter` + `TestResultDisplayConverter`；颜色走 `DialogSuccess*` / `DialogDanger*` |
| 状态栏 | 本视图无 `StatusMessage` / `StatusLevel`；宿主底部栏显示「状态 / 耗时 / 执行 / 取消 / 确认」，不改 |

### 复刻约束（已遵守）

1. **保留视图构造与生命周期**：`DataRecordView()` 无参构造、`GetConfigView` 里 `DataContext = this`、`plugin.Initialize(stepData)` 原样保留。
2. **不引入独立 VM**：视图 code-behind 只加 `ICommand` 转发、转换器；`DataContext` 仍是 `DataRecordPlugin`。
3. **`LinkableValueEditor` 只包裹不改内部**：保留 `Port` / `StepData`；图片行不设 `BrowseCommand`（原视图也没有）。
4. **HALCON 控件**：本插件无 HALCON 控件；未引入任何 `h:` 控件，因此不涉及 `ContentControl` 预览宿主规则。
5. **不改业务逻辑、端口名、命令名、持久化**：`ImagePortName` 仍是 `"图片"`，动态端口重建逻辑未动。
6. **不修改共享文件**：`Colors.xaml`、`PluginConfigColors.xaml`、`UIThemeSmokeTest`、`PluginConfigShellView.xaml`、其他插件均未改。

## 8. 已知偏差

1. **图标字体**：Web 用内联 SVG / Unicode，WPF 用 Font Awesome 6 Pro Solid（`DialogIconFont`）；形状不同，语义一致。
2. **表格密度**：Web 表格输入 32px，表单控件 36px；WPF 同样区分（表格 32 / 表单 38，沿用 `DialogInput` 默认 38）。
3. **表头 sticky**：Web 用 `position:sticky`；WPF 当前表头与行在同一 `ScrollViewer` 内（列数多时表头随行滚动），不另做双 `ScrollViewer` 同步——属已知偏差，列数通常较少，不影响功能。
4. **窄档**：640 宽时右栏列定义表横向滚动（Web 稿同样保留 390px 最小表宽）；不隐藏任何字段。
5. **字体度量**：同字号下 Segoe UI / 微软雅黑与 Web 字宽、行高略有差异，不按 px 硬套。
6. **滚动条**：WPF 左栏滚动条约占 17px，窄档可用宽度比 Web 少约 17px。
7. **工具提示**：Web 的 `title` / `data-tooltip`；WPF 用 `DialogToolTipStyle`。
8. **试写结果**：Web 稿演示三种文案；WPF 的真实文案由 `TestWriteOneRow()` 返回（`✔ 已写入一行：…` / `✘ 写入失败：…`），本基线不改变文案。
9. **图片源未连线**：WPF `LinkableValueEditor` 对 `InputPort<object>` 显示可手填输入框 + 🔗 / ✕；Web 稿用同构的连线行示意，未复刻共享控件的内部图标细节。
10. **列定义空态**：Web 稿可切「空列」；WPF 中 `Columns` 为空时同样显示空态提示（`ItemsControl` 无行 + 空态层），添加列后恢复。

## 9. 未纳入本版（记录，不改）

- **加载 / 写入中骨架**：需要 VM 新增 `IsWriting` / `WriteState`；当前只有 `TestResult` 文本（D1）。
- **账本文件大小 / 行数**：需要 VM 新增只读属性（D2）。
- **打开记录目录**：需要新增 `Process.Start("explorer.exe", ...)`（D3）。
- **列模板导入 / 导出**：需要新的序列化与文件对话框契约（D4）。
- **列拖拽排序**：需要拖放行为与撤销（D5）。
- **账本内容预览**：需要读取 CSV 的只读服务（D6）。
- **图片缩略图 / 相册预览**：属于「结果显示」与图像查看器，不属于本插件。
- **记录目录创建 / 权限检测**：属于运行时行为，本版不做预检。

## 10. 变更规则

设计基线确认后只读。需要调整设计时升版本（v2），并同步更新 `design.html`、本 README 的映射表与已复刻的 `DataRecordView.xaml`。
