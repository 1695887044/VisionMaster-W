# 手眼标定配置窗口 · 设计基线

| 项 | 内容 |
| --- | --- |
| 版本 | v1 |
| 日期 | 2026-10-22 |
| 确认状态 | **待用户确认**（本页为 Web 设计基线；WPF 尚未复刻，验收后冻结） |
| 覆盖范围 | 拟建 `Plugins\Plugin.HandEye\HandEyeView.xaml` + `HandEyeView.xaml.cs`（插件内容区）；不含宿主 `PluginConfigShellView` 的「执行 / 取消 / 确认」页脚 |
| 窗口尺寸 | 960×680 默认；640×680 窄档；宿主内容区最小 640×560 |
| 设计文件 | `design.html`（v1，自包含、内联 SVG、可点击切换 默认 / 空态 / 失败 / 窄档 四态）；四态 PNG 见 `evidence/design-v1-*.png` |
| 插件复杂度 | S（1 个 `LinkableValueEditor` + 2 个 `NumericBox` + 1 个 `ComboBox` 分段 + 1 个复选框 + 只读结果区） |
| 参考实现 | `docs/UI设计基线/dl-classify/README.md` v1 + `design.html`；`docs/UI设计基线/image-acquisition/` v2 的视觉语言 |
| 前置事实 | 插件本体已落地并全绿：提交 `3a1d41ed`（插件）、`c153132b`（闸门修复）；`FlowCanvasChecks` 3327/0，其中 `HandEyeChecks` 覆盖数学/欧拉/插件三层 |

## 1. 定位与范围

本文件是 SOP §5.2 的产物：**Web 只是设计媒介，不进入构建、不部署**（R126）。视觉语言沿用 ImageAcquisition V2 / DlClassify v1：
两栏、卡片、36px 控件、12px 标签、11.5px 帮助文字、8px 圆角、公共令牌、五态（hover / pressed / focus / disabled / selected）。

**本轮只做配置界面**，不改：

- 数学内核 `HandEyeMath.cs`（求解、SVD、欧拉正反变换、四条退化判据）；
- 插件执行路径 `HandEyePlugin.RunAlgorithm`（清零 → 取路径 → 校验 → 组数门 → 求解 → 残差门 → 组装 `CalibrationTransform`）；
- 端口名与类型（`SamplesPath` / `Transform` / `RotationRmsDeg` / `TranslationRmsMm` / `UsedPairs`）；
- 五个 `[StepConfig]` 属性（`PoseKind` / `SamplesFilePath` / `MaxTranslationRmsMm` / `MaxRotationRmsDeg` / `RequireRecommendedPairs`）与持久化；
- 契约字段 `CalibrationTransform.HandEye*`（16 元 4×4 行主序 + 装法 + 两个残差 + 组数）；
- `LinkableValueEditor` / `Dialog*` 等共享控件。

> ⚠ 本基线**不含**"在界面里编辑成对数据表格"。数据入口只有两条：文件路径、输入端口绑定。理由见 §11 D1。

## 2. 现状审查（改动理由）

插件当前**没有任何视图**（未实现 `IPluginCustomViewProvider`），宿主回落到 `AutoPortConfigView` 框架兜底面板 —— 用户看到的是"五个端口 + 五个配置项"的平铺属性表。具体问题：

| # | 现状 | 影响 |
| --- | --- | --- |
| 1 | 兜底面板把 5 个端口与 5 个配置项平铺，无分区 | 「装法」与「质量门槛」是两类完全不同的决策（前者决定 X 的物理含义，后者决定放过还是拦住），视觉同权 |
| 2 | `PoseKind` 以枚举下拉出现，两个成员的中文名之外没有一句解释 | 选错装法的后果是**抓取点整体镜像**，而界面不给任何提示 |
| 3 | `SamplesFilePath` 是裸文本框 | 12 列 CSV 靠手打路径，写错列/少列的错误只在执行后才出现 |
| 4 | 求解结果（4×4 矩阵）只出现在端口值里 | 4×4 十六个数没有容器，用户无法核对；而**核对矩阵是现场唯一能发现"装法选错"的手段** |
| 5 | 三个指标端口（旋转 RMS / 平移 RMS / 组数）没有与门槛值并排 | 用户看到 `0.83°` 却看不到"上限是 0.5°"，只能自己心算 |
| 6 | 失败原因只在 `LastError` 里 | 本插件的失败文案带行号+列号+原文，是全仓最有信息量的错误文本之一，却没有展示容器 |
| 7 | 没有视图 = 无法在 `UIThemeSmokeTest --plugin-views` 里被渲染断言覆盖 | 布局回归无闸 |

## 3. 设计方向

> **配置在左、证据在右；矩阵是这一页的主角。**

手眼标定的界面价值不在"填参数"，而在"**让人能核对解出来的 4×4 是不是他想要的那个**"。所以右栏不是预览图，而是**结果证据区**：4×4 矩阵 + 三枚指标 + 门槛对照。

1. **两栏固定分工**：左 360px 配置轨（装法 → 成对数据 → 求解），右侧结果工作区自适应（**质量门槛 + 结果 + 状态**）。
2. **装法用分段控件而非下拉**：两个选项常驻可见，各带一行说明；这是全页唯一"选错就全错"的字段，不该藏在展开后。
3. **数据源双入口同排**：端口绑定行（`LinkableValueEditor`）在上、文件路径行在下，二者互斥且后者优先 —— 与插件既有语义一致（`RunAlgorithm` 里端口优先于配置）。
4. **样本概况条**：`已读取 8 组 · 建议 ≥ 6 组`，把"够不够解"这件事在点求解之前就说清。
5. **门槛与指标同处右栏**：上限与它判断的实测在同一栏内上下相邻、同一数字格式，肉眼可对照；左栏只留"会让结果完全不同"的三件事（装法 / 数据 / 求解）。
6. **矩阵等宽 + 行列表头**：4 行 × 4 列，行标 `X / Y / Z / t`，列标 `X / Y / Z / 平移`；右下角注明单位 `mm`，左上角注明 `R`（旋转块）。
7. **失败态不折叠错误文本**：`LastError` 全文等宽展示、可滚动、可复制。截断它等于把最有用的信息丢掉。
8. **全令牌化**：颜色全部落在既有 `Dialog*` 语义域内，**本页不新增公共令牌**；视图 XAML 零十六进制（R140）。

## 4. 页面结构与状态

### 4.1 默认态（已选数据、求解完成）

- 左栏「装法」：`眼在手上` 选中（深蓝底白字），`眼在手外` 未选；下方帮助文字「相机装在机器人末端，随手一起动；解出的 X = 夹爪←相机」。
- 左栏「成对数据」：端口行显示 `SamplesPath`（未绑定，🔗 可见）；路径行 `D:\data\handeye-20261022.csv` + 「浏览…」；概况条绿点 `已读取 8 组 · 建议 ≥ 6 组 · 12 列校验通过`。
- 左栏「求解」：主按钮「求解手眼变换」全宽 36px；提示条「求解只读数据，不改机器人」。
- 右栏顶部「质量门槛」：平移 `1.00` mm、旋转 `0.50` ° 并排，第三行「组数少于 6 组时判失败」勾选（附注「关掉后 3 组也能解，但解可能不稳」）。
- 右栏结果卡：状态 chip `完成`（绿）；三枚 KPI `旋转残差 RMS 0.021°` / `平移残差 RMS 0.147 mm` / `参与组数 8`，每枚下方一行 `上限 0.50` / `上限 1.00` / `建议 ≥ 6`；矩阵 4×4 等宽；副标题 `眼在手上 · X = T_gripper←camera · 单位 mm`。
- 右栏底部：`求解耗时 3 ms · 数据源 handeye-20261022.csv`。
- 结果卡下方「下一步」虚线提示条：把 `Transform` 接到 `PoseTransform` 做运行期换算；并给出排错提示「矩阵核对不了时，先怀疑装法选错（症状是抓取点整体镜像）」。

### 4.2 空态（未选数据 / 未求解）

- 左栏「成对数据」：路径行空，占位提示「选择机器人导出的成对位姿 CSV，或把上游路径绑到 SamplesPath」。
- 右栏：虚线空态卡「还没有求解结果」+ 三行指引：
  1. `选装法`：眼在手上 / 眼在手外，选错会让抓取点整体镜像；
  2. `给数据`：12 列 CSV，至少 3 组、建议 ≥ 6 组；
  3. `点求解`：结果与残差会出现在这里。
- 右栏底部状态：中性圆点 + `未求解`。

### 4.3 失败态（三类，同一容器不同措辞）

| 触发 | 右栏表现 | 原文示例（插件 `Fail` 文案，界面原样展示） |
| --- | --- | --- |
| 文件问题 | 红卡 + 等宽错误块 | `第 1 行有 11 列，应为 12 列` / `第 1 行第 4 列不是数字：「abc」` |
| 数据退化 | 红卡 + 等宽错误块 + 下方「怎么办」三条 | `旋转激励不足：相机侧最大旋转只有 0°（要求 ≥ 5°）。请让机器人以不同姿态取点（只平移或每次都回到同一姿态无法解出旋转）` |
| 残差超限 | 黄卡（不是红卡）+ 指标行标红 + 「放宽上限」入口 | `点位数据可能含错点：平移残差 RMS 1.84 mm 超过上限 1.00 mm` |

> **黄 vs 红的判据**：红 = 解不出来（数据/数学问题，必须改数据）；黄 = 解出来了但不达标（可能只是门槛设紧了，用户有权放宽）。两者都能拦住结果，但**放宽门槛是合法动作，改数据不是**。

### 4.4 边界态

| 边界 | 设计处理 |
| --- | --- |
| 窗口 640×680 | 左栏 300px，三张卡仍不需内部滚动；右栏最小 260px；KPI 由三列降为两列（第三枚「参与组数」占整行）；路径框独占一行、「浏览…」换到下一行；矩阵字号 11px、列间距 8px；副标题换行 |
| 路径超过 40 字符 | TextBox 内水平滚动，右侧「浏览…」按钮不缩 |
| 错误文本超过 5 行 | 错误块 `MaxHeight=132` + 内部 `ScrollViewer` + 右上角「复制」图标按钮 |
| 矩阵数值为负零 / 极小数 | 统一 `0.000`（不做科学计数法）；`-0.000` 归一为 `0.000` |
| 组数 3~5 且未勾选「少于 6 组判失败」 | 概况条转橙：`已读取 4 组 · 低于建议 6 组，结果可能不稳` |
| `PoseKind = 眼在手外` | 求解必失败：`眼在手外（AX=ZB）求解器尚未实现` —— **选项保留可选，不置灰**（见 §11 D3） |
| 无视图权限 / 只读模式 | 不涉及：本插件无权限分级 |

## 5. 设计要点（完整清单）

1. 配置轨背景 `DialogRowAlternateBrush`，1px `DialogBorderLightBrush`，圆角 8；内部 14px 内边距。
2. 区块标题 12px SemiBold `DialogTextSecondaryBrush`；右侧 10.5px 提示 `DialogTextMutedBrush`。
3. 字段标签 12px `DialogTextSecondaryBrush`；控件高 36px；帮助 11.5px `DialogTextMutedBrush`，带 12px 信息图标。
4. 装法分段控件：两枚等宽按钮（各 50%），高 36；选中 `DialogAccentBrush` 底 + 白字；未选 `DialogSurfaceBrush` 底 + `DialogBorderBrush` 1px 边 + `DialogTextSecondaryBrush` 字。**不用 `RadioButton`**（WPF 原生圆点在 Fluent 下与卡片视觉冲突，且无法等宽铺满）。
5. 装法说明行：11.5px，随选中项切换文案（`DataTrigger` 绑 `PoseKind`），不新增 VM 属性。
6. 端口行：`LinkableValueEditor`，`Label=""`（外置标签「绑定上游路径（可选）」），`Port=SamplesPath`。
7. 路径行：`TextBox`（`Style=DialogInput`，高 36，`UpdateSourceTrigger=PropertyChanged`）+ 「浏览…」按钮（图标+文字，高 36，`Width=88`）。
8. 概况条：7px 圆点 + 11.5px 文本；绿 = 达标、橙 = 组数不足、红 = 解析失败。圆点颜色由文本前缀判定（`DataTrigger`），不伪造第三套状态。
9. 门槛数值框：`ui:NumericBox` 高 36；平移 `0.01~100.0`（2 位小数）、旋转 `0.01~90.0`（2 位小数）；帮助文字写明单位。
10. 复选框：「组数少于 6 组时判失败」+ 帮助「关掉后 3 组也能解，但解可能不稳」。
11. 主按钮：「求解手眼变换」全宽 36px，`DialogAccentBrush` 底；`Command` 走视图级 `SolveCommand`（委托插件既有求解入口）。
12. 结果卡：`DialogSurfaceBrush` 底 + `DialogBorderBrush` 1px + 圆角 8；头部 12px SemiBold + 右上状态 chip（完成绿 / 未求解中性 / 失败红）。
13. KPI 三枚：等宽三等分，数值 15px SemiBold tabular，标签 11px `DialogTextMutedBrush`；超限时数值转 `DialogDangerTextBrush` 并在右侧加「上限 1.00」小字。
14. 矩阵：等宽字体 11.5px，`Typography.NumeralAlignment=Tabular`；行标/列标 10.5px `DialogTextMutedBrush`；单元格右对齐；整块 `DialogRowAlternateBrush` 底 + 8px 圆角 + 12px 内边距。
15. 矩阵副标题：11px `DialogTextMutedBrush`，文案随 `PoseKind` 切换（`X = T_gripper←camera` / `X = T_base←camera`）。
16. 错误块：`DialogDangerSubtleBrush` 底 + `DialogDangerBorderBrush` 边 + 圆角 8；正文等宽 11px `DialogDangerTextBrush`；右上「复制」30×30 图标按钮。
17. 图标按钮 30×30、`Padding=0`、`FocusVisualStyle=DialogFocusVisual`；ToolTip 走 `DialogToolTipStyle`（R68 / R135）。
18. 所有数字（矩阵、残差、组数、耗时）使用 tabular 数字，避免跳动。

## 6. 令牌映射

| 设计变量 | 值 | WPF 资源键 |
| --- | --- | --- |
| `--he-surface` | `#FFFFFF` | `DialogSurfaceBrush` |
| `--he-surface-2` | `#F8FAFC` | `DialogRowAlternateBrush` |
| `--he-surface-3` | `#F1F5F9` | `DialogRowHoverBrush` |
| `--he-input` | `#F8FAFC` | `DialogInputBackgroundBrush` |
| `--he-border` | `#E2E8F0` | `DialogBorderBrush` |
| `--he-border-soft` | `#EEF2F7` | `DialogBorderLightBrush` |
| `--he-border-strong` | `#CBD5E1` | `DialogBorderStrongBrush` |
| `--he-card-border` | `#E3E9F2` | `DialogCardBorderBrush` |
| `--he-text` | `#0F172A` | `DialogTextPrimaryBrush` |
| `--he-text-2` | `#475569` | `DialogTextSecondaryBrush` |
| `--he-text-3` | `#64748B` | `DialogTextTertiaryBrush` |
| `--he-text-4` | `#94A3B8` | `DialogTextMutedBrush` |
| `--he-primary` | `#0EA5E9` | `DialogAccentBrush` |
| `--he-primary-dark` | `#0284C7` | `DialogAccentDarkBrush` |
| `--he-primary-text` | `#0369A1` | `DialogAccentTextBrush` |
| `--he-primary-soft` | `#F0F9FF` | `DialogAccentSoftBrush` |
| `--he-primary-light` | `#E0F2FE` | `DialogAccentLightBrush` |
| `--he-primary-border` | `#BAE6FD` | `DialogAccentBorderBrush` |
| `--he-success` | `#22C55E` | `DialogSuccessBrush` |
| `--he-success-text` | `#15803D` | `DialogSuccessTextBrush` |
| `--he-success-soft` | `#F0FDF4` | `DialogSuccessSubtleBrush` |
| `--he-success-border` | `#BBF7D0` | `DialogSuccessBorderBrush` |
| `--he-warning` | `#F59E0B` | `DialogWarningBrush` |
| `--he-warning-text` | `#B45309` | `DialogWarningTextBrush` |
| `--he-warning-soft` | `#FFF4E5` | `DialogWarningSubtleBrush` |
| `--he-warning-border` | `#FDE68A` | `DialogWarningBorderBrush` |
| `--he-danger` | `#EF4444` | `DialogDangerBrush` |
| `--he-danger-text` | `#B91C1C` | `DialogDangerTextBrush` |
| `--he-danger-soft` | `#FEF2F2` | `DialogDangerSubtleBrush` |
| `--he-danger-border` | `#FECACA` | `DialogDangerBorderBrush` |
| 图标字体 | — | `DialogIconFont`（Font Awesome 6 Pro Solid） |
| 圆角 8 | — | `PluginCardCornerRadius` |
| 间距 8/12/14/16 | — | `Margin` / `Padding` 字面量（仓内暂无间距令牌） |
| 等宽字体 | — | `Consolas, Cascadia Mono, monospace`（与 `ResultSummary` 一致） |

**结论：本页不需要新增公共令牌。** 设计用到的 30 个颜色全部落在既有 `Dialog*` 语义域内，视图 XAML 静态扫描 `hex=0` 可证明。

## 7. 新增视图必须遵守的契约

| # | 契约 | 为什么不能动 |
| --- | --- | --- |
| C1 | `DataContext = 插件实例` + 视图实现 `IPluginConfigView` | 宿主靠 `viewObj.DataContext as IPluginConfigView` 取 `OnConfirm`；独立 VM = 确认不保存 |
| C2 | `GetConfigView(IStepConfigData)` 返回 `object`（WPF `FrameworkElement`） | `IPluginCustomViewProvider` 的唯一方法（`Shard\Core.Interfaces\IPluginCustomViewProvider.cs:113-123`） |
| C3 | `LinkableValueEditor`：`Port=SamplesPath` / `StepData` | 端口持久化、类型转换、变量链接、解除链接都在控件里 |
| C4 | 五个 `[StepConfig]` 属性名与类型 | 换了 = 持久化与已配流程不匹配 |
| C5 | 「浏览…」→ `SamplesFilePath`；「求解」→ 走插件既有求解入口（不复制数学） | 界面**不许**再算一遍：否则界面成了第二个计算源，与执行结果可能不一致 |
| C6 | 结果显示只读绑定 `Transform` / `RotationRmsDeg` / `TranslationRmsMm` / `UsedPairs` 端口值 | 界面不维护独立结果状态；失败时插件会把 `Transform` 置空、`UsedPairs` 归零 |
| C7 | 失败文本只取 `LastError` | 不自己拼错误文案；插件文案带行号/列号/原文与下一步，是全仓最具体的错误描述 |

## 8. 复刻实现映射（WPF）

| 设计项 | WPF 实现 |
| --- | --- |
| 两栏 360px + 自适应 | `Grid` 两列：`360`（MinWidth 300）+ `*`（MinWidth 260） |
| 左栏卡片 | `Border` 背景 `DialogRowAlternateBrush`、1px `DialogBorderLightBrush`、`PluginCardCornerRadius` |
| 装法分段 | 两枚 `RadioButton` 套自定义 `Style`（`GroupName=PoseKind`，`IsChecked` 绑 `PoseKind` 需转换器）或 `ToggleButton` + `Converters`；**推荐 `ListBox` + `ItemsPanel=UniformGrid Columns=2`**，`SelectedValue` 绑 `PoseKind`、`ItemsSource` 走 `StepConfigOption.ForEnum(typeof(HandEyePoseKind), …)`（R40：值仍是成员名、显示中文） |
| 装法说明 | `TextBlock` + `Style` 内两个 `DataTrigger`（`PoseKind=EyeInHand/EyeToHand`）切文案 |
| 端口行 | `cv:LinkableValueEditor Label="" Port="{Binding SamplesPath}" StepData="{Binding StepData}"` |
| 路径行 | `TextBox Style=DialogInput Height=36 Text="{Binding SamplesFilePath, UpdateSourceTrigger=PropertyChanged}"` + 「浏览…」按钮 |
| 概况条 | `Ellipse` 7px + `TextBlock`；圆点 `Fill` 由 `DataTrigger` 按文本前缀（`已读取` / `低于建议` / `解析失败`）切换 |
| 门槛 | `ui:NumericBox` 保持 `Value` 绑定与范围；单位写在右侧 `TextBlock` |
| 复选框 | `CheckBox Style=DialogCheckBox IsChecked="{Binding RequireRecommendedPairs}"` |
| 求解按钮 | 主按钮 `Command` 走视图级 `SolveCommand`（委托插件既有求解入口）；`CanExecute` 判"路径非空或端口已绑定" |
| 结果卡 | `Border` + 头部 `Grid`（标题 + 状态 chip）；chip 用 `DataTrigger` 按 `LastError` 空/非空 + `Transform` 空/非空切三态 |
| KPI | `UniformGrid Columns=3`；数值 `TextBlock` 绑端口值 + `StringFormat`；超限时 `Foreground` 由 `DataTrigger` 切红 |
| 矩阵 | `UniformGrid Columns=4 Rows=4` 或 `Grid` 4×4；单元格 `TextBlock` 绑 `Transform.HandEyeMatrix[i]`（视图侧索引器转换器） |
| 错误块 | `Border` + `ScrollViewer MaxHeight=132` + `TextBlock`（等宽、`TextWrapping=Wrap`）；「复制」按钮走 `Clipboard.SetText(LastError)` |
| 颜色 | 全部 `StaticResource`；视图 XAML `hex=0` |

> **矩阵取值的落地方式**：`HandEyeMatrix` 是 `double[]?`（16 元行主序）。视图侧建议写一个 `MatrixCellConverter`（`ConverterParameter` 传 0~15 索引），避免在视图里写 16 个具名属性。转换器放 `Plugin.HandEye` 工程内，不进 `UI`（只此一处用）。

## 9. 复刻证据（设计稿已出图，WPF 复刻尚未开始）

| 项 | 结果 |
| --- | --- |
| 设计稿渲染 | 已出（headless Edge，`--window-size` 1060×1150 / 窄档 760×1150），四态见下 |
| 设计稿截图 | `evidence/design-v1-default.png` / `design-v1-empty.png` / `design-v1-failed.png` / `design-v1-narrow-640.png`（2026-10-22 22:28 渲染，v1） |
| 设计稿渲染中发现并修掉的缺陷 | 见 §11 末尾「v1 渲染修订」三条 |
| 插件构建 | 待复刻后填：`dotnet build Plugins\Plugin.HandEye\Plugin.HandEye.csproj -m:1 -nodeReuse:false` |
| 插件视图冒烟 | 待填：`UIThemeSmokeTest --plugin-views Plugin.HandEye` |
| 渲染对照 | 待填：`evidence/initial-render-960x680.png`（WPF 真渲染，与 design-v1-default 同尺寸对照） |
| 常规 gate | 待填：`tools\run_gates.ps1` 八步 |

> 截图是设计媒介不是交付物（R126）。`evidence/` 里只放设计稿，不代表已复刻。

## 10. 已知偏差（复刻时按 R130 记录）

1. **图标字体**：Web 用内联 SVG 线性图标，WPF 用 Font Awesome 6 Pro Solid（`DialogIconFont`）；形状不同、语义一致。
2. **分段控件的键盘行为**：Web 用按钮 + `aria-pressed`，WPF 用 `ListBox` 选中态；方向键行为由 `ListBox` 提供，与 Web 的 Tab 切换不完全一致。
3. **矩阵字体**：Web 用 `ui-monospace`，WPF 用 `Consolas`；列宽按字符数固定，不做自动对齐算法。
4. **动效**：Web 状态切换有 150ms 过渡；WPF 不做过渡（避免在 `HwndHost` 同级引入合成层）。
5. **滚动条宽度**：WPF 窄档左栏滚动条约占 17px，可用宽度比 Web 少约 17px。
6. **`NumericBox` 模板颜色**：共享控件模板自带一套颜色（未令牌化），本轮不改共享控件，视图只设 `Height=36`、范围与 `Value` 绑定。
7. **`LinkableValueEditor` 内部样式**：链接/解除链接按钮与输入区的内部颜色/间距由共享控件决定，视图只包裹不改内部。
8. **状态 chip 的三态判据**：WPF 用"`LastError` 是否为空 + `Transform` 是否为 null"两字段组合判定，不新增 VM 属性；若插件未来改这两个字段的语义，需同步本视图与冒烟断言。
9. **宿主页脚**：设计稿底部的「执行 / 取消 / 确认」属于 `PluginConfigShellView`，插件视图不实现。
10. **空态指引的编号**：Web 稿用 ①②③，WPF 用等宽数字 + 圆角方块（不引入新图标资源）。

## 11. 未纳入本版（需新增功能/契约，延后）

| # | 项 | 需要什么 |
| --- | --- | --- |
| D1 | **界面内编辑成对数据表格** | 需要新增"增删行 / 单元格编辑 / 导入 / 逐行校验"一整套契约与 VM；现场数据来自机器人控制器导出，手打 12 列 × N 行不现实。**本轮明确不做** |
| D2 | 逐对残差表 | `HandEyeMath.SolveAXEqualsXB` 现在只回聚合 `Quality`（RMS/Max），**不回逐对残差**；要做需要扩 API |
| D3 | 眼在手外（AX=ZB）求解器 | 契约与枚举已有（`HandEyePoseKind.EyeToHand`），求解分支未实现。**界面保留该选项可选**，求解时明确失败——置灰会让"契约支持但功能没做"变成看不见的谎 |
| D4 | 实时采集进度（已采几组 / 还差几组） | 插件是**离线求解**，不做在线采集会话；实时采集属机器人侧。界面的"已读取 N 组"是解析结果，不是采集进度 |
| D5 | 结果落盘（导出矩阵到文件 / 剪贴板） | 落盘由 `DataRecord` / `ExcelExport` 插件负责；本轮只给「复制错误文本」，不给「复制矩阵」（后者需先定文本格式契约） |
| D6 | 3D 可视化（相机/机器人坐标系绘制） | 需要 HALCON 3D 或自绘投影，且 `HObjectModel3D` 全仓零命中；超出本插件范围 |
| D7 | 装法自动判定 | 需要额外的"已知靶标在基座/在末端"先验，当前契约没有该信息 |
| D8 | 多点加权 / 剔除离群点 | 数学层现在是等权最小二乘；加权与 RANSAC 需扩 `SolveAXEqualsXB` 签名 |

### v1 渲染修订（设计稿出图时发现，已修）

设计稿不是"画完就算"——**四态逐个渲染成 PNG 再肉眼核对**，下面三条是真渲染才暴露的缺陷（静态读 HTML 看不出来）：

| # | 缺陷 | 根因 | 修法 |
| --- | --- | --- | --- |
| R1 | 默认态与失败态的内容同时出现 | 失败块带 inline `style="display:flex"`，内联样式优先级高于 `[data-state="default"] .only-failed{display:none}` | 删 inline，改 `.failed-stack` 类 |
| R2 | 路径输入框整行消失 | 元素类写成 `only-default only-failed`，被默认态的 `.only-failed{display:none}` 命中 | 引入语义类 `hide-on-empty`（只在空态隐藏） |
| R3 | 左栏「求解」按钮被挤出首屏 | 左栏 4 张卡实测 ≈694px > 600px 内容区，静态按卡片数估不出来 | **结构变更**：质量门槛整卡从 left 移到右栏顶部，与它判断的实测值同栏（见 §3 第 5 条） |

另有两处一致性修正：① 失败态左栏原来写「解析失败 · 第 1 行有 11 列」而右栏写「旋转激励不足」，**两个不同的错并列** ⇒ 左栏只表达数据读取状态、右栏只表达求解结果；② 默认态矩阵与页脚之间的大片空白用「下一步」虚线提示条填充（写清 `Transform` 接 `PoseTransform` 的用法与排错提示）。

窄档 640 另修：路径框独占一行、「浏览…」换行、KPI 三列降两列、宿主页脚按钮不压扁。

## 12. 待用户拍板（五项交互决策）

下面五项已在本基线里按**推荐答案**落笔（§3/§4 的图与文案按此绘制）。若不同意任一项，改这里再升版本。

| # | 问题 | 本基线采用 | 理由 |
| --- | --- | --- | --- |
| Q1 | 成对数据怎么进？ | **文件路径 + 输入端口绑定**，不做内置表格编辑 | 现场数据来自机器人控制器导出；内置表格需整套编辑契约（见 D1） |
| Q2 | 采集进度怎么显示？ | **只显示"已读取 N 组 · 建议 ≥6"**，不做实时进度条 | 插件是离线求解，不做在线采集会话（见 D4） |
| Q3 | 残差超限拦不拦？ | **拦住**（不给 `Transform`），但门槛可调；失败分**红（解不出）/ 黄（不达标）** 两色 | 手眼错 1 rad 的后果是抓空或撞机；宁可在标定这一步就红 |
| Q4 | 结果只走端口还是也落盘？ | **只走端口** | 落盘由 `DataRecord`/`ExcelExport` 负责，不重复造轮子（见 D5） |
| Q5 | 换装法时哪些字段变？ | **字段一个不变**，只换结果语义文案（`X` 的定义） | 两种装法的输入都是成对数据，只有解方程形式不同 ⇒ **不触发 R106/R107 的互斥字段集**（不需要"切换字段 + 嵌套子对象"） |

## 13. 变更规则

设计基线确认后只读。需要调整设计时升版本（v1 → v2），同步更新：

- 本 README 的版本、映射表、已知偏差、待拍板表；
- `design.html`；
- `HandEyeView.xaml` / `HandEyeView.xaml.cs`；
- `docs/code-changes/` 对应记录。
