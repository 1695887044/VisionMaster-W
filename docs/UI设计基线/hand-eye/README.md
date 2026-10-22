# 手眼标定（`Plugin.HandEye`）配置窗口 · 设计基线 v2

> 版本：**v2**　｜　确认日期：待确认　｜　确认人：待确认　｜　状态：**设计稿已出图，WPF 复刻尚未开始**
> 设计文件：`design.html`（自包含可点击，无外部依赖）　｜　证据：`evidence/design-v2-*.png`（四态）
> 覆盖范围：`HandEyePlugin` 的插件配置视图（`IPluginCustomViewProvider.GetConfigView` 返回的 `IPluginConfigView`）。
> 不覆盖：流程栏步骤卡片、结果比较节点、`PoseTransform` 的运行期换算界面。

## 1. 定位与范围

手眼标定解决的问题是：**由若干组成对的「机器人位姿 + 相机位姿」，解出一个 6-DOF 刚体变换 X**，让"相机看到的东西"能换算到"机器人能去的位置"。

这个页面的价值不在填参数——参数只有两个残差上限和一个装法。价值在**让人能核对解出来的 4×4 矩阵**，以及在矩阵不合理时，能当场发现是哪几组数据坏了。

因此本页设计的核心主张是：**数据是主体，矩阵是结论，参数是配角。**

## 2. 现状审查：为什么数据入口是表格而不是文件

v1 把数据入口定成"CSV 文件路径 + 端口绑定"，理由写的是"现场数据来自机器人控制器导出，手打 12 列 × N 行不现实"。**这个理由站不住**：

- 它回答的是「**数据从哪来**」（采集阶段），而定的是「**数据放哪、怎么改**」（使用阶段）。
- 现场真正要改的是数据本身——改一个错数字、删一组明显离群的、补一组刚采的。
- 只给文件路径意味着改一个数要**回去开 Excel**，而 Excel 打开会锁文件、可能改编码、会把 `45.600` 显示成 `45.6` 再存回去。
- 逐组加行、当场填数，本身就是台账；采完自然有记录，不需要额外一步"另存成一个文件"。

v2 因此改为：**表格是主入口，CSV 降级为成批进出的扩展功能**。CSV 仍然保留（一次采 20 组时逐行敲不现实），但它不再承担编辑。

> 这条是本版**唯一的推倒重来**，由用户在评审 v1 时提出（原话：「不能做成列表吗 也可以手填写，CSV 作为扩展功能 可以导入导出」）。

## 3. 设计方向

1. **顶部一条工具条，下面是全宽表格，再下面是结果。** 不做左右分栏——12 个数值列 + 结果矩阵，右栏会挤掉表格宽度，而表格是这个页面最重要的东西。
2. **表格是主体。** 13 列（序号 + 6 机器人 + 6 相机 + 删除），两级表头把两段分组标出来（`机器人位姿` / `相机位姿`），避免"12 个数字哪个是哪个"。
3. **参数贴着它的判据放。** 两个残差上限在工具条上，但结果区每枚 KPI 下面都写着对应的上限（`上限 0.50 °`），不用回头找。
4. **失败分两色，红黄不同义。** 红 = 解不出来（必须改数据）；黄 = 解出来了但超上限（可能只是门槛设紧了）。只有黄态允许"放宽门槛"这个合法动作。
5. **表内就地编辑，不进弹窗。** 双击格改数不需要开对话框；行尾 ✕ 直接删行。

## 4. 页面结构与状态

### 4.1 默认态（有 ≥ 3 组有效数据且求解通过）

```
┌ 手眼标定  [完成] ────────────────────────────────────────────┐
│ 装法 [眼在手上|眼在手外]  平移上限 1.00mm  旋转上限 0.50°     │
│ ☑要求≥6组                          [导入 CSV][导出 CSV][求解] │
├──────────────────────────────────────────────────────────────┤
│  # │ 机器人位姿 X Y Z RX RY RZ │ 相机位姿 X Y Z RX RY RZ │    │
│  1 │ 512.300 120.800 ...      │ 12.440 ...              │ ✕ │
│  … │                          │                          │ ✕ │
├──────────────────────────────────────────────────────────────┤
│ [＋ 加一行]              已录入 7 组 · 建议 ≥ 6 组 · 第 8 行还没填 │
├──────────────────────────────────────────────────────────────┤
│ 旋转残差 RMS │ 平移残差 RMS │ 参与组数 │ 变换矩阵 X（4×4）    │
│ 0.021°      │ 0.147 mm    │ 7 组    │ X 0.998 -0.052 …    │
│ 上限 0.50°   │ 上限 1.00mm  │ 首组基准 │ X = T_gripper←camera│
├──────────────────────────────────────────────────────────────┤
│ → 下一步：把 Transform 接到 PoseTransform 做运行期换算…        │
└──────────────────────────────────────────────────────────────┘
```

要点：

- 三枚 KPI 与矩阵卡**同一行**，矩阵卡占约 1.35 份宽。数字用等宽字体、按列对齐（见 4.3 的说明）。
- 矩阵卡副标题写出**语义**：`X = T_gripper←camera`（眼在手上）/ `X = T_base←camera`（眼在手外），附单位与求解耗时。光给 16 个数，用户无法判断该左乘还是右乘。
- 「下一步」提示条**只在解出矩阵时出现**——没解出东西时让人"把 Transform 接到 PoseTransform"是没有对象的。
- 空白行（加了但没填）整行浅黄 + `—` 占位，表尾计数直接点名「第 8 行还没填」。

### 4.2 空态（表格 0 行）

- 表格与表尾整行隐藏，换成虚线空态卡：标题「还没有成对数据」+ 两种录入途径（手采 / 导入 CSV）+ **两个按钮（`＋ 加一行`、`导入 CSV`）** + 三步指引胶囊。
- 按钮必须在这里再出现一次：表尾那行已经隐藏，只留文字让人"点 ＋ 加一行"会指向一个不存在的按钮。
- 结果区与提示条整块不出。头部 chip 为灰「未求解」。

### 4.3 失败态（求解返回失败）

- 头部 chip 转红「失败」。
- 表格**保留**——失败时最需要看数据。
- 结果区换成两块：
  1. 错误块（红）：等宽字体显示 `LastError` 原文 + 「怎么办」一句话可执行建议。
  2. 说明块（中性）：`失败时 Transform 被置空、UsedPairs 归零 —— 下游不会读到上一轮残留的矩阵。`
- 「下一步」提示条不出现（没有矩阵可接）。
- 表尾计数照常显示「第 8 行还没填」——数据状态与求解结果分开，不因失败而混淆。

### 4.4 边界态：窄档 640

- 表格 `min-width: 760px`，容器横向滚动 + 首列（序号）冻结。**不能靠压缩列宽适配**：12 个数值列挤到 600px 会显示成 `512.3C`（末位被裁），看着像数据损坏。
- 工具条换行成两行：第一行 4 个参数，第二行三个按钮（`导入 CSV` / `导出 CSV` / `求解`）。
- KPI 三列降两列，第三枚「参与组数」与矩阵卡各占整行。

## 5. 设计要点

1. **网格不用裸 `ContentPresenter`，用 `GridView` + `GridViewRowPresenter`**（见 §7 C1）。
2. **单元格是 `TextBox`（`UpdateSourceTrigger=LostFocus`）**，不是双击弹窗。失焦提交，避免每敲一个字符就重算。
3. **数值右对齐 + 等宽数字**（`FontFamily` 用等宽，或 `Typography.NumeralAlignment`）。左对齐的数字列无法逐位比较。
4. **行级 ✕ 只在悬停时显红**，平时是灰色，避免整列红色噪点。
5. **空白行判据 = 12 个值全为 0**（`HandEyeSample.IsBlank`）。这比"允许留空字符串"简单，且能把"忘填"与"数据退化"分开报错——不拦的话用户看到的是「旋转残差 15° 超过上限」然后跑去怀疑欧拉约定。
6. **矩阵用 4 行 4 列的表格，不用空格对齐**。等宽字体下 16 个不同长度的数字用空格也排不齐，而这一页就是要逐格核对。
7. **KPI 内容垂直居中**：三枚 KPI 与矩阵卡同行，高度被最高的那块拉伸，不居中会留一大片空白。
8. **状态切换用语义类**（`.only-ok` / `.only-failed` / `.only-empty` / `.has-rows`），不用 inline `style`、不用 `only-default` 组合类。v1 的两个渲染 bug 就是这两类写法造成的。
9. **列宽不钉死**：`#` 34px、操作列 32px，其余按内容均分。

## 6. 令牌映射（全部落在既有 `Dialog*` 域，本页不新增公共令牌）

| 用途 | 令牌 | 值 |
|---|---|---|
| 页面底色 | `DialogSurfaceBrush` | `#FFFFFF` |
| 工具条 / 表头 / 页脚底 | `DialogRowAlternateBrush` | `#F8FAFC` |
| 行悬停 / 表头悬停 | `DialogRowHoverBrush` | `#F1F5F9` |
| 输入框底 | `DialogInputBackgroundBrush` | `#F8FAFC` |
| 分隔线（轻） | `DialogBorderLightBrush` | `#EEF2F7` |
| 表头下边线 / 输入框边 | `DialogBorderBrush` / `DialogBorderStrongBrush` | `#E2E8F0` / `#CBD5E1` |
| 卡边框 | `DialogCardBorderBrush` | `#E3E9F2` |
| 正文 / 次要 / 三级 / 占位 | `DialogTextPrimary/Secondary/Tertiary/MutedBrush` | `#0F172A` / `#475569` / `#64748B` / `#94A3B8` |
| 主按钮 / 悬停 | `DialogAccentBrush` / `DialogAccentDarkBrush` | `#0EA5E9` / `#0284C7` |
| 主色文字 / 浅底 / 边 | `DialogAccentTextBrush` / `DialogAccentSoftBrush` / `DialogAccentBorderBrush` | `#0369A1` / `#F0F9FF` / `#BAE6FD` |
| 完成 chip | `DialogSuccessSubtleBrush` / `DialogSuccessBorderBrush` / `DialogSuccessTextBrush` | `#F0FDF4` / `#BBF7D0` / `#15803D` |
| 空白行底（提示"还没填"） | `DialogWarningSubtleBrush` | `#FFF4E5` |
| 失败 chip / 错误块 | `DialogDangerSubtleBrush` / `DialogDangerBorderBrush` / `DialogDangerTextBrush` | `#FEF2F2` / `#FECACA` / `#B91C1C` |

约束（R117 / R140）：**视图 XAML 里 `hex = 0`**。凡上表没有的组合（例如"相机位姿"表头的紫色分组标题）在复刻时**必须改用 `DialogTextSecondaryBrush` 或 `DialogAccentTextBrush`**，不许新增令牌颜色——分组标题的区分靠位置与字号，不靠色相。

## 7. 新增视图必须遵守的契约

| # | 契约 |
|---|---|
| C1 | 表格走 `GridView` + `Style="{StaticResource GridListViewStyle}"` + `GridViewRowPresenter`（R19/R20）；引用该样式必须**就地合并** `GridView.xaml`（R22，断言宿主无 `Application`）。 |
| C2 | 装法用**分段控件**（两个 `RadioButton` 挂 `DialogSegmentStyle` 或两个 `ToggleButton` 互斥），不用裸 `RadioButton`：Fluent 的圆点与卡片视觉冲突，且宽度不等。 |
| C3 | 枚举显示名走 `StepConfigOption.ForEnum<HandEyePoseKind>()` + `DisplayMemberPath`（R40/R85）——**不许** `Enum.GetNames()`，也不许手写中文。 |
| C4 | 数值格用 `TextBox` + `UpdateSourceTrigger=LostFocus`，`StringFormat` 显示 3 位小数但**绑定值是 double**，不许把显示格式写进数据模型。 |
| C5 | **界面不许再算一遍数学。** 残差、组数、矩阵全部只读绑端口值（`RotationRmsDeg` / `TranslationRmsMm` / `UsedPairs` / `Transform`）。界面重算就成了第二个计算源，两边不一致时无法判定谁错。 |
| C6 | 结果区不维护独立状态，只读端口 + `LastError`。红/黄/绿由"端口是否有值 + 是否失败"推导。 |
| C7 | 失败文案只取 `LastError`，界面不改写、不截断。`LastError` 已经是给人看的中文（见 `HandEyePlugin` 的 `Fail(...)` 文案）。 |

## 8. 复刻实现映射（WPF）

| 设计元素 | 实现 |
|---|---|
| 表格数据源 | `HandEyePlugin.Samples`（`ObservableCollection<HandEyeSample>`，`[StepConfig]`） |
| 单元格绑定 | `HandEyeSample.RobotX` … `CameraRz`（12 个 `double`，`ObservableObject` + `SetProperty`） |
| 数值格式 | 每个 `Binding` 带 `StringFormat="{}{0:F3}"`（**必须带引号**，裸写 `StringFormat=F3` 报 MC3000） |
| 单元格样式 | `NumCellStyle`（等宽右对齐，`Margin=0`）+ `NumCellHostStyle`（`BasedOn` `DialogDataGridCell`，`Padding="6,0,4,0"`）——`DialogDataGridCell` 自带 `Padding="12,0,0,0"`，不减掉会把负号裁没 |
| 列宽 | `Width="*"` + `MinWidth="72"`（8 字符 `-511.280` 的最小宽度；窄档靠横向滚动，不压列宽） |
| 行序号 | `DataGrid.LoadingRow` 里 `e.Row.Header = (e.Row.GetIndex() + 1)`；`HeadersVisibility="All"`（**不能写 `Row`**，那会连列头一起关掉），序号不进模型 |
| 行尾 ✕ | `RemoveSampleCommand`（`Core.Commands.RelayCommand`，`CommandParameter` 是行对象），字形 `×`（U+00D7）**不用 Font Awesome 私有区字形**——删除这种歧义代价高的操作不该赌字体命中 |
| `＋ 加一行` | `AddSampleCommand` → `Samples.Add(new HandEyeSample())`（新行 12 值全 0 = `IsBlank`，求解时被点名） |
| `导入 CSV` / `导出 CSV` | `HandEyeCsv.TryParse` / `HandEyeCsv.Format` + `OpenFileDialog`/`SaveFileDialog`（**文件 IO 只在视图层**）；导出写 BOM，Excel 双击中文表头才不乱码 |
| 计数文案 | `SampleCountHint`（`[JsonIgnore]` 计算属性） |
| 装法 | `PoseKind` + 两个布尔投影 `IsEyeInHand`/`IsEyeToHand`（RadioButton 绑布尔，不绑枚举） |
| 两个上限 | `MaxTranslationRmsMm` / `MaxRotationRmsDeg`（`[StepConfig]`，`[DefaultValue]` 1.0 / 0.5，`ui:NumericBox`） |
| 矩阵 16 格 | `string[] PreviewMatrixCells` 索引绑定（`PreviewMatrixCells[0]` … `[15]`）。**不用 `IMultiValueConverter`/`ConverterParameter`**：那要求 XAML 里写 `ConverterParameter="7"` 再在 `Convert` 里转 int，写错只会静默不显示 |
| 语义副标题 | `PreviewMatrixCaption`（由 `PoseKind` 决定两句话，不新建字段） |
| 三态切换 | `IsPreviewOk` / `IsPreviewFailed` 两个**布尔投影**驱动 `DataTrigger Value="True"`（枚举直接做 `DataTrigger` 要靠字符串→枚举转换，猜不中时 XAML 不报错、只是永不触发） |
| 求解结论 | `SolveForPreview()` 只写 `Preview*` 只读属性，**不动输出端口**；端口只在 `RunAlgorithm` 里写（界面看结果 ≠ 流程拿结果） |
| 窄档 | `UpdateNarrowLayout()`：`ActualWidth < 720` 时把表尾计数换成「窄窗口：表格可左右滑动，右侧还有 4 列」 |

> `HandEyePlugin` **没有输入端口**（数据住在 `Samples` 列表里），所以视图不需要"端口绑定"控件。CSV 只是搬运。

## 9. 复刻证据

**WPF 已复刻（2026-10-23），四态由探针实渲染截图。**

| 状态 | WPF 实渲染 | 设计稿 |
|---|---|---|
| 默认（8 组求解通过） | `evidence/wpf-v2-ok.png` | `evidence/design-v2-default.png` |
| 空态 | `evidence/wpf-v2-empty.png` | `evidence/design-v2-empty.png` |
| 空表按过求解（红块） | `evidence/wpf-v2-empty-solved.png` | —（设计稿未单列此态） |
| 失败（旋转激励不足） | `evidence/wpf-v2-failed.png` | `evidence/design-v2-failed.png` |
| 窄档 640 | `evidence/wpf-v2-narrow-640.png` | `evidence/design-v2-narrow-640.png` |

产出方式：`artifacts/tmp-viewprobe/`（临时探针，用完即删）离屏 `Window`（`Left=-4000`）+ `Dispatcher.PushFrame` + `RenderTargetBitmap`，24 条断言落在"看得见"（`TextBlock.IsVisible` 为真）而不是"控件存在"。断言覆盖：XAML 能解析 / 13 列 / 空态可见且自带入口 / 空表求解的文案是人话 / 未填行报「还没填」而不是拿 0 去算 / 8 组成功出 16 格矩阵与三枚 KPI / 姿态不变必失败 / 失败时矩阵清空 / 数字列不被裁（负号不能消失）/ 窄档也不被裁 / 没有绑定到 `HandEye*` 的错误。

## 10. 已知偏差

| # | 偏差 | 处理 |
|---|---|---|
| 1 | HTML 用 `:hover` 表达悬停，WPF 用 `IsMouseOver` 触发器 | 允许（平台差异） |
| 2 | HTML 表格滚动条被截图为 `--hide-scrollbars` 隐藏 | 允许；WPF 用 `ScrollViewer` 默认滚动条 |
| 3 | HTML 的 `input` 无 IME/输入法行为 | 允许；WPF 复刻时数值格需挡住非数字输入 |
| 4 | 矩阵卡在 960 档只占 1.35 份宽，超长数字（如 `-1234.567`）会挤 | 复刻时按实际列宽微调或缩短小数位（3 位→2 位） |
| 5 | 13 列在窗口默认 960 宽下约 900px，**行尾 ✕ 要横向滚一点才完全露出**（12×72 + 行头 ≈ 900 > 内容区 940 减去左侧无、实际可见） | 接受：默认窗口能直接看到前 12 列与行号，删除走滚动或键盘；下一步选项是把 ✕ 挪进行头（需改 `RowHeaderTemplate`） |
| 6 | **窄档「表格可左右滑动」提示是复刻时新加的**，设计稿只有滚动条 | 接受并回写进 §4.4：浅色主题下 12px 滚动条几乎看不见，不给一句话用户会以为后面没列了 |
| 7 | 设计稿里"求解中禁用按钮"未做 | 求解是同步的且只算 SVD，毫秒级；等出现真卡顿再加（不先造无用的禁用逻辑） |
| 8 | **设计稿的两级表头（`机器人位姿` / `相机位姿` 分组行）未复刻** | `DataGrid` 没有原生双层表头，做要叠 `Grid` 假表头并与真实列头对齐（窗口缩放必然错位）。改为**列名自带前缀**：`机器人 X` … `相机 Rz`，分组语义不丢 |

## 11. 未纳入本版

| # | 项 | 原因 |
|---|---|---|
| D1 | 表格内批量选择 / 多行删除 | 组数通常 < 20，逐行 ✕ 够用 |
| D2 | 逐对残差表（哪一组偏差大） | 需要扩 `SolveAXEqualsXB` 返回逐对残差，是契约变更，独立批次 |
| D3 | 眼在手外（`AX = ZB`）求解器 | 当前只有契约字段 `HandEyePoseKind.EyeToHand`，**求解器未实现**；本页只把装法作为选项呈现，选了眼在手外求解会明确失败 |
| D4 | 实时采集进度（边采边看） | 需要与运动控制/相机驱动联动的运行期能力，先做离线 |
| D5 | 结果落盘（一次标定存成一个标定文件） | 现在只走 `Transform` 端口；落盘涉及标定库与版本语义 |
| D6 | 3D 可视化（相机/机器人坐标系画出来） | 独立批次 |
| D7 | 装法自动判定 | 需要物理先验，判定错比不判更糟 |
| D8 | 加权 / 剔除离群点 | 先让人手动删行（这也是"表格是主体"的理由之一） |

### v1 渲染修订（保留记录）

v1 设计稿写完后四态逐个渲染，发现并修掉 3 个**静态读 HTML 看不出来**的缺陷：

| # | 缺陷 | 根因 | 修法 |
|---|---|---|---|
| R1 | 默认态与失败态同时显示 | 失败块带 inline `style="display:flex"`，压过状态选择器 | 改用 `.failed-stack` 类 |
| R2 | 路径输入框整行消失 | 类写成 `only-default only-failed`，被默认态的 `.only-failed{display:none}` 命中 | 引入语义类 `hide-on-empty` |
| R3 | 求解按钮被挤出首屏 | 左栏 4 张卡 ≈694px > 内容区 600px | v2 起结构改为工具条 + 全宽表格，左栏不存在了 |

### v2 渲染修订

| # | 缺陷 | 根因 | 修法 |
|---|---|---|---|
| R4 | 640 档数字被裁成 `512.3C` | 12 列挤进 600px，列宽被压 | `table{min-width:760px}` + 容器横向滚动 + 首列冻结 |
| R5 | 640 档装法控件文字竖排成「眼/在/上」 | 工具条丢掉 `flex-wrap`，flex 项被压到最小内容宽 | 640 档加回 `flex-wrap:wrap` + `.field/.seg/.cb{flex:0 0 auto}` |
| R6 | 640 档三个按钮被拆到两行 | `.grow` 从 `display:none` 改成占位换行后未同时保留换行能力 | `.grow{flex:1 1 100%;height:0}` 强制换行 |
| R7 | 空态让人"点 ＋ 加一行"但按钮不存在 | 表尾整行在空态隐藏（`.has-rows`） | 空态卡内补 `＋ 加一行` / `导入 CSV` 两个按钮 |
| R8 | 失败态出现「删掉第 8 行」按钮，与错误内容无关 | 按钮被绑成"失败时出现"，但失败原因可能是激励不足 | 删掉该按钮，行删除只走行尾 ✕ |
| R9 | 矩阵 16 个数用空格对齐，列不齐 | 数字长度不同，等宽字体也排不齐 | 改 4×4 表格 |
| R10 | KPI 卡下方一大片空白 | 与矩阵卡同行被拉伸，内容顶对齐 | `.kpi{display:flex;flex-direction:column;justify-content:center}` |
| R11 | 失败/空态仍显示「下一步把 Transform 接到 PoseTransform」 | 提示条无条件显示 | 提示条加 `.only-ok` |

## 12. 待确认 / 已确认决策

| # | 决策 | 状态 |
|---|---|---|
| Q1 | 数据入口 = **表格为主 + CSV 导入导出（扩展）** | **已拍板（用户，2026-10-22）**，v1 的"只认文件路径"作废 |
| Q2 | 采集进度：只显示「已录入 N 组 · 建议 ≥ 6 组」，不做实时进度条 | 待确认（按推荐落笔） |
| Q3 | 残差超限：**拦住**但门槛可改，红黄两色区分"解不出"与"不达标" | 待确认（按推荐落笔） |
| Q4 | 结果去向：只走 `Transform` 端口，不落盘 | 待确认（按推荐落笔） |
| Q5 | 换装法时字段不变，只换结果语义文案 ⇒ 不触发 R106/R107 的互斥字段集 | 待确认（按推荐落笔） |
| Q6 | 表格是否需要"粘贴 Excel 一片区域"导入（比 CSV 更贴现场） | 待确认（本版未做） |

## 13. 变更规则

- 本目录**只读**：确认后冻结。变更必须**升版本**（v2 → v3），同步更新本 README 的版本表、令牌映射、以及已复刻页面的差异表（R128）。
- 设计稿不需要参与构建；`docs/` 下的 HTML 与 PNG 是**设计媒介，不是交付物**（R126）。
- 复刻阶段发现设计不可实现时，走 R125：记录差异点 + 替代方案 → 评审 → 用户确认 → 本文件登记「已知偏差」，**不得静默降级**。
