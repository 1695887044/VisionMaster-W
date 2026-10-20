# 变量绑定（输入端口绑定弹窗）· 设计基线

| 项 | 内容 |
| --- | --- |
| 版本 | v1 |
| 日期 | 2026-10-20 |
| 确认日期 / 人 | 2026-10-20 / 用户（指令：这个样式我很满意，协同后端一起落地） |
| 覆盖范围 | `VisionMaster\Views\DialogViews\VariableBindingView.xaml` + `VariableBindingView.xaml.cs`（已删除无引用的 `TabControl_SelectionChanged`） |
| 窗口尺寸 | 1180 × 760 默认；最小 1000 × 640；三栏 280 / 300 / 自适应（右栏 ≥ 360） |
| 设计基线文件 | `design.html`（自包含、无外部依赖、内联 SVG 图标、可点击） |
| 设计证据 | `evidence\design-v1-*.png`（默认 / 常量 / 功能预设 / 类型不兼容 / 数组索引 / 空态 / 单绑定 / 1000 窄档） |
| 复刻状态 | **已复刻（2026-10-20）**；WPF 证据 `evidence\after-render-v1-*.png`；验证 `UIThemeSmokeTest --variable-binding` 全部通过、`UIThemeSmokeTest --variable-binding-render` 8 张全部生成；`FlowCanvasChecks` 静态契约按现有基线通过（完整套件被并行任务 `ConditionEditorV2Checks.cs` / `ConditionEditorSummaryProbe.cs` 的编译错误阻断，见 `docs\code-changes\2026-10-20-变量绑定弹窗Web化复刻.md`） |
| 改造记录 | [2026-10-20-变量绑定弹窗Web化复刻.md](../../code-changes/2026-10-20-变量绑定弹窗Web化复刻.md) |

## 定位

本文件是「Web 设计基线 → 用户确认 → WPF 复刻 → 同屏截图验收」四步流程的**第 ① 步**产物，
口径沿用 `docs\UI设计基线\插件自定义视图Web化改造SOP.md`（R126-R132）。
本页是**宿主弹窗**，不是插件自定义视图，因此没有 `IPluginConfigView` / `LinkableValueEditor` / HALCON 画布；
但「先出完整 Web 设计稿、确认后再改 WPF、零功能改动、断言守门」的纪律完全一致。

Web 只作设计媒介，**不进入构建、不部署**。确认后按本基线做 WPF 复刻，第 ④ 步以同一屏、同一状态的截图对照验收。

## 一句话方向

**保留三栏工作流，把它从"三列列表"改造成"目标端口 → 数据来源 → 绑定设置"的工作区。**
顶部常驻当前步骤与绑定进度；左栏把输入端口做成可搜索、可筛选、带绑定状态的列表；
中栏把全局变量 / 运行时变量 / 上游算子分组；右栏用"连线预览英雄区 + 绑定方式二选一 + 候选端口列表"
把"到底绑了什么、还能绑什么、为什么不能绑"三件事一次说清。

## 审查清单（请重点看这几项）

- [ ] 三栏 280 / 300 / 自适应 + 顶部 44px 上下文条（当前步骤 + 类型 + 绑定进度）。
- [ ] 左栏：搜索 + 全部 / 未绑定 / 已绑定筛选；行内类型标签、绑定状态与地址；已绑定行绿色勾。
- [ ] 中栏：变量来源 / 上游算子分组；全部 / 变量 / 上游算子页签；每项输出端口数量。
- [ ] 右栏英雄区：目标端口 → 数据来源的连线预览；已绑定 / 常量 / 功能预设三态一眼可辨。
- [ ] 绑定方式显式二选一：上游端口 / 常量值；常量输入带目标类型与"会替换上游连线"警示。
- [ ] 候选端口：类型标签、已绑定标记、类型不兼容置灰 + 原因；搜索 + 显示全部 + 被隐藏数量提示。
- [ ] 功能预设：枚举端口不出常量框；卡片显示中文名 + 写入值（值与显示分离）。
- [ ] 数组索引：数组 → 标量用行内索引面板，不再弹二级对话框。
- [ ] 空态 / 单绑定：中栏空态与右栏空态分开；单绑定收敛为单目标端口 + 「绑定并返回」。
- [ ] 底部状态条：当前选择 + Esc 提示 + 取消 / 确定。

## 现状审查（改动理由）

| # | 现状 | 影响 |
| --- | --- | --- |
| 1 | 三栏等宽列表，列头只有「① 当前算子输入 / ② 选择上游算子 / ③ 绑定选项」，没有当前步骤与进度上下文 | 用户打开后先要自己拼出"我在给谁绑、绑了几个"，三栏之间没有视觉主线 |
| 2 | 左栏每行把名称、类型、枚举/变量角标、已绑定地址挤在 54px 内；无搜索、无筛选 | 插件端口一多（10+）就只能滚；想找"还没绑的"或"已经绑的"只能逐行看 |
| 3 | 中栏是 `TreeNodes` 的扁平列表：全局变量、运行时变量、上游算子混在一起；只有 `ModuleGroup` 名称能区分 | 用户不知道"全局变量"是一个包含很多变量的来源节点，还是单个变量；算子一多就分不清变量与步骤 |
| 4 | 右栏把常量输入框、上游候选列表、枚举预设三套 UI 叠在一起；「显示全部」只是标题行右侧一个 checkbox | "现在到底按常量还是按上游"不明确；勾选「显示全部」的后果也不明显 |
| 5 | 候选端口只能双击绑定；单击只是选中，且没有「绑定」按钮 | 鼠标单击用户找不到动作入口；键盘用户没有可达路径；触控板双击容易误触 |
| 6 | 没有「清除绑定」入口，尽管 VM 里已有 `UnbindCommand` | 用户想解绑只能重开流程或去画布上找连线；已绑定的端口没有显式"解除"动作 |
| 7 | 类型不兼容项默认被过滤掉；没有"还有 N 个端口被隐藏"的提示 | 用户看到某来源只有 1 个候选，会以为插件没暴露端口，而不是"类型不匹配被隐藏" |
| 8 | 数组 → 标量绑定走 `EasyDialog.ShowTextInputSync` 二级对话框 | 二级弹窗遮住上下文，用户看不到自己选的是哪个数组端口；索引输错也没有就地反馈 |
| 9 | 没有空态：来源为空、搜索无结果时就是一片白 | 看起来像加载失败；新流程里"还没有上游"是常见状态 |
| 10 | 标题栏写了一段 11.5px 的长操作说明，底部只有取消/确定 | 操作要点不在操作现场；底部没有当前选择摘要，确认前无法快速复核 |

## v1 方向

1. **三栏不变，层级重排**：左 280「输入端口」、中 300「数据来源」、右自适应「绑定设置」；
   列头带步骤号与数量徽标；顶部 44px 上下文条常驻「当前步骤 + 类型 + 输入端口 N · 已绑定 M」与进度条。
2. **左栏成为可扫描的目标端口列表**：搜索（端口名 / 类型）+ 全部 / 未绑定 / 已绑定筛选；
   行内两行式（名称 + 类型标签 / 绑定状态 + 地址）；已绑定行带绿色勾与绿色状态点。
3. **中栏成为真正的来源导航**：按「变量来源」（全局变量 / 运行时变量）与「上游算子」分组；
   搜索 + 全部 / 变量 / 上游算子页签；每项显示输出端口数量；选中态为白卡 + 主色左条。
4. **右栏先回答"当前绑了什么"**：英雄区左边目标端口、右边数据来源，中间箭头；已绑定 / 常量 / 功能预设
   三种状态用不同的底色与 chip 区分；提供「清除绑定」，走既有 `UnbindCommand`。
5. **绑定方式显式二选一**：上游端口 / 常量值。常量输入带目标类型提示；
   只要填写常量，就常驻一条「填写常量并点确定会用常量替换当前上游连线」的警示，避免"填了常量把线悄悄拆了"。
6. **候选端口列表把"能不能绑"说清**：类型标签、已绑定 chip、类型不兼容置灰 + 原因；
   支持搜索与「显示全部」；默认只列兼容项，并在列表上方提示"当前来源还有 N 个端口因类型不兼容被隐藏"。
   单击选中、双击直接绑定，同时提供行内「绑定」按钮。
7. **功能预设独立成组**：枚举端口不再出现常量框；卡片显示中文名 + 写入值（如「圆 / Circle」），
   写入的仍是成员名，值与显示分离；复用 `PresetOptions` / `SelectPresetCommand`。
8. **数组索引就地完成**：数组 → 标量时在候选列表内展开「选择元素下标」面板，写入预览显示 `分数列表[0]`；
   不再弹二级对话框（需要 VM 小幅交互编排扩展，见下）。
9. **空态 / 单绑定 / 底部状态条**：中栏空态与右栏空态分开；单绑定模式收敛为单目标端口 + 「绑定并返回」；
   底部左侧显示当前选择与绑定结果，右侧 Esc 提示与主次按钮；全令牌化、图标按钮 30×30 + `Padding=0`（R68）。

## 布局与尺寸

```text
窗口 1180 × 760（最小 1000 × 640）
┌──────────────────────────────────────────────────────────────────────┐
│ 标题栏 64：图标 36 + 标题/副标题 + 关闭 30                            │
├──────────────────────────────────────────────────────────────────────┤
│ 上下文条 44：当前步骤 + 类型 + 输入端口 7 · 已绑定 4 + 进度条          │
├──────────────┬──────────────┬────────────────────────────────────────┤
│ 输入端口 280 │ 数据来源 300 │ 绑定设置 自适应（≥ 360）              │
│ 搜索 32      │ 搜索 32      │ 英雄区（目标 → 来源）                  │
│ 筛选 24      │ 页签 24      │ 绑定方式 24                            │
│ 行 54        │ 分组行 54    │ 候选列表 / 预设卡片 / 常量表单         │
│ 底 34        │ 底 34        │ 底 34                                  │
├──────────────┴──────────────┴────────────────────────────────────────┤
│ 底部 56：当前选择 + Esc 取消 + 取消 / 确定                            │
└──────────────────────────────────────────────────────────────────────┘
```

外边距 16 × 2 + 栏间距 12 × 2 + 280 + 300 = 636；1180 下右栏 **544**。
最小 1000 下改为 264 / 280 / ≥ 360（外边距 14 × 2 + 间距 10 × 2 = 48；右栏 408）。
右栏低于 360 时宁可窗口卡住不缩，也不让英雄区/候选行挤成一条缝。

### WPF 列宽计划

| 区域 | 宽度 | 说明 |
| --- | --- | --- |
| 左栏 | 280 固定（窄档 264） | `ColumnDefinition Width="280"` + `MinWidth="264"` |
| 中栏 | 300 固定（窄档 280） | `ColumnDefinition Width="300"` + `MinWidth="280"` |
| 右栏 | `*` + `MinWidth="360"` | 星号列；英雄区两列 `*` / `1.12*`，箭头 30 |
| 窗口 | 1180 × 760 | `SizeToContent="Manual"`、`ResizeMode="CanResizeWithGrip"`、`MinWidth=1000`、`MinHeight=640` |
| 标题栏 / 上下文条 / 底部 | 64 / 44 / 56 | 固定行高 |

## 交互模型

| 动作 | 结果 |
| --- | --- |
| 单击左栏目标端口 | `SelectedInputPort` 更新；回显该端口的常量/预设；按目标类型重算候选；右栏英雄区刷新 |
| 单击中栏来源 | `SelectedNode` 更新；右栏候选列表切换到该来源的输出端口 |
| 单击候选端口 | 选中（`SelectedOutputPort`）；行内「绑定」按钮可用 |
| 双击候选端口 | 立即绑定（数组 → 标量先展开索引面板） |
| 点「绑定」按钮 | 与双击同一路径（`DoublockClickBindCommand`） |
| 切换「上游端口 / 常量值」 | 只切换右栏编辑区；不立即写模型，点「确定」才写 |
| 勾选「显示全部」 | `ShowAllPorts=true`；不兼容项以 55% 透明度显示，行内给「类型不匹配」 |
| 点「显示全部」提示条 | 同上，等价于勾选 checkbox |
| 点「清除绑定」 | `UnbindCommand`；清空 `LinkedAddress` 与常量回显 |
| 点「确定」 | 常量非空 → 写常量；否则保留最后一次上游绑定；单绑定模式回传 `BoundLink` 并自动关闭 |
| 点「取消」 / Esc | `CancelCommand`；不改模型 |

**键盘可达性**：Tab 顺序为「左栏 → 中栏 → 右栏 → 底部」；列表项可聚焦；Esc 取消；
候选行支持 Enter 绑定（WPF 复刻时用 `InputBindings` 实现，Web 基线暂未演示 Enter）。
**五态**：所有按钮 / 行 / 输入框都具备 Normal / Hover / Pressed / Focus / Disabled；
焦点环统一 `DialogFocusVisual`，ToolTip 统一 `DialogToolTipStyle`。

## 状态矩阵

| 场景 | 左栏 | 中栏 | 右栏 |
| --- | --- | --- | --- |
| 默认 | 选中「图像」，已绑定 `采集_0.输出图像` | 选中「采集_0」 | 英雄区显示已绑定；候选「输出图像」带已绑定 chip；提示 2 个端口被隐藏 |
| 常量值 | 选中「最小分数」，已有常量 0.75 | 选中「全局变量」 | 绑定方式 = 常量值；输入框 + 类型提示 + 替换上游连线警示 + 写入预览 |
| 功能预设 | 选中「查找类型」（枚举角标） | 选中「全局变量」 | 不显示常量框；预设卡片「圆 / 直线 / 椭圆」，中文名 + 写入值 |
| 类型不兼容 | 选中「图像」 | 选中「标定_0」 | 勾选「显示全部」；「标定矩阵」置灰 + 类型不匹配；选中项用警示底色而非主色 |
| 数组索引 | 选中「最小分数」 | 选中「数据记录_0」 | 选中「分数列表 Double[]」→ 行内索引面板；写入预览 `分数列表[0]` |
| 空态 | 7 个输入端口，全部未绑定 | 没有可用来源的空态 | 「先选择一个数据来源」空态，不显示候选列表 |
| 单绑定 | 仅 1 个「目标变量」端口，隐藏搜索/筛选 | 正常来源列表 | 选定候选后立即回传；主按钮「绑定并返回」 |

## 契约保留（一个都不能改）

| # | 契约 | 为什么不能动 |
| --- | --- | --- |
| C1 | `DataContext = VariableBindingViewModel`（Prism `AutoWireViewModel` + 宿主 `RegisterDialog`） | 换独立 VM = 命令、对话框生命周期、宿主注册全部要重接 |
| C2 | `DisplayDataPort` / `SelectedInputPort` / `TreeNodes` / `SelectedNode` / `DisplayPorts` / `SelectedOutputPort` / `ShowAllPorts` / `ConstantValue` / `HasFunctionalEnumPort` / `PresetOptions` | 三栏的数据源与选中语义都在 VM；视图只换外观 |
| C3 | `ConfirmCommand` / `CancelCommand` / `DoublockClickBindCommand` / `SelectPresetCommand` / `UnbindCommand` | 全部是功能入口；「清除绑定」只是把既有 `UnbindCommand` 接到界面上，不允许出现死按钮 |
| C4 | `TypeHelper.CanBindTo` 是列表过滤与双击校验的唯一尺子；`IsBindable` / `IncompatibleReason` 语义不变 | 一把尺子量两次，否则会出现"列表里放着、双击又说不匹配" |
| C5 | `ConditionStep` / `WaitStep` 的 bindKey 用端口 `Description`（变量 Guid），其余用端口 `Name`；`ResolveBindKey` 三处一致 | 条件节点写常量/连线时键写错会"填了值但读不回来"，界面看不出异常 |
| C6 | `LinkReference.Kind` / `TargetVariableId` / 数组下标写进 `TargetPortName` | 编译器按 Kind 解析；数组下标只进显示串会导致"数组取元素"编译不过或整数组静默传下去 |
| C7 | 单绑定模式不直写目标步骤的 `LinkedSources`，只通过 `BoundLink` 回传；选定后自动确认 | 目标可能是条件节点，bindKey 会落到中文描述上，在活模型里留下垃圾键 |
| C8 | 无边框自绘标题栏 + `WindowDragBehavior`；`SizeToContent=Manual`；可缩放、最小 1000 × 640 | 弹窗唯一移动/关闭入口；`SizeToContent` 不设回 Manual 会看起来"不能缩放" |

## 允许的 VM 改动（仅显示属性 / 交互编排）

| # | 改动 | 理由 | 风险 |
| --- | --- | --- | --- |
| V1 | `VariableBindingViewModel` 增加 `HiddenByTypeCount`（int，只读）并在 `RefreshDisplayPorts` 里更新 | 右栏提示"当前来源还有 N 个端口因类型不兼容被隐藏"需要知道被过滤掉的数量 | 纯显示属性，不参与绑定；`RefreshDisplayPorts` 已在那里，加一次 `RaisePropertyChanged` |
| V2 | `VariableBindingViewModel` 增加 `ArrayIndexText`（string）、`PendingArrayPort`（`PortDefinition`）、`ConfirmArrayBindCommand`、`CancelArrayBindCommand`；把 `DoublockClickBind` 的索引分支拆成"开始索引绑定 / 确认索引绑定" | 行内索引面板取代 `EasyDialog.ShowTextInputSync` 二级对话框 | 只改交互编排；`DoFinalBind(port, index)` 与 `TargetPortName` 的 `[index]` 写法不变；取消不写模型 |
| V3 | `ToolItemModel` 增加 `SourceCategory`（string，`init`，默认 `"Step"`；`FlowQueryHelper` 对 Global / Runtime 设 `"Variable"`） | 中栏按「变量来源 / 上游算子」分组；`ModuleGroup` 的值是 `"Global"` / `"Runtime"` / 步骤名，不能直接分组 | 纯显示字段，不参与流程寻址；`ToolItemModel` 是 record class，加 `init` 属性对既有构造无影响 |
| V4 | `VariableBindingViewModel` 增加 `BoundPortCount` / `TotalPortCount`（已落地） | 上下文条与列脚显示"已绑定 M / N"；VM 在 `BuildTree` 与 `InputPortUIModel.LinkedAddress` 变化时通知，视图直接使用 | 订阅/退订成对；`BuildTree` 重建端口表时先退订旧端口 |
| V5 | 清理 `VariableBindingView.xaml.cs` 中未被引用的 `TabControl_SelectionChanged` | 视图里没有 `TabControl`，是死代码 | 无行为影响；删除前确认无外部 `SelectionChanged` 订阅 |

**本批实际触及的语义边界（与 V1-V5 对应，已落地）**：
`BuildTree` 增加 ConditionStep 按 `LocalVariables` 铺端口（`Description=Guid`）、单绑定无 `TargetStep` 时先建 mock 端口、自动选第一个来源、空端口表清空选中；
`RefreshDisplayPorts` 增加 `HiddenByTypeCount`；
`DoublockClickBind` 的数组→object 走整数组、数组→标量改行内索引；
`UnbindCommand` 同时清 `LinkedSources` 与 `InputValues`；
`Confirm` 的常量回传 `DataTypeName` 改目标端口类型；
`UpdatePresetOptions` 在枚举端口已有非常量连线时不自动填首项。
**不变**：`DoFinalBind` 的 `TargetPortName` / `LinkReference` 协议、`ResolveBindKey` 的键口径（条件/等待节点 Guid、其余端口名）、单绑定 `BoundLink` 回传、`TypeHelper.CanBindTo` 唯一判据。
不新增"绑定历史""多选批量""变量当前值预览"等需要新契约的功能。

## 令牌映射

| 设计变量 | 值 | WPF 资源键 |
| --- | --- | --- |
| `--vb-surface` | `#FFFFFF` | `DialogSurfaceBrush` |
| `--vb-surface-2` | `#F8FAFC` | `DialogRowAlternateBrush` |
| `--vb-surface-3` | `#F1F5F9` | `DialogRowHoverBrush` |
| `--vb-border` | `#E2E8F0` | `DialogBorderBrush` |
| `--vb-border-soft` | `#EEF2F7` | `DialogBorderLightBrush` |
| `--vb-border-strong` | `#CBD5E1` | `DialogBorderStrongBrush` |
| `--vb-text` | `#0F172A` | `DialogTextPrimaryBrush` |
| `--vb-text-2` | `#475569` | `DialogTextSecondaryBrush` |
| `--vb-text-3` | `#94A3B8` | `DialogTextMutedBrush` |
| `--vb-text-4` | `#64748B` | `ScadaSecondaryTextBrush`（近似） |
| `--vb-primary` | `#0EA5E9` | `DialogAccentBrush` |
| `--vb-primary-strong` | `#0284C7` | `DialogAccentDarkBrush` |
| `--vb-primary-text` | `#0369A1` | `DialogAccentTextBrush` |
| `--vb-primary-soft` | `#F0F9FF` | `DialogAccentSoftBrush` |
| `--vb-primary-light` | `#E0F2FE` | `DialogAccentLightBrush` |
| `--vb-primary-border` | `#BAE6FD` | `DialogAccentBorderBrush` |
| `--vb-success*` | `#22C55E / #15803D / #F0FDF4 / #BBF7D0` | `DialogSuccessBrush` / `DialogSuccessTextBrush` / `DialogSuccessSubtleBrush` / `DialogSuccessBorderBrush` |
| `--vb-danger*` | `#EF4444 / #B91C1C / #FEF2F2 / #FECACA` | `DialogDangerBrush` / `DialogDangerTextBrush` / `DialogDangerSubtleBrush` / `DialogDangerBorderBrush` |
| `--vb-warning*` | `#F59E0B / #B45309 / #FFF4E5 / #FDE68A` | `DialogWarningBrush` / `DialogWarningTextBrush` / `DialogWarningSubtleBrush` / `DialogWarningBorderBrush` |
| `--vb-violet*` | `#8B5CF6 / #6D28D9 / #F5F3FF` | `LogicCaseBrush` / `LogicCaseTextBrush` / `LogicCaseSoftBrush` |
| `--vb-cyan*` | `#06B6D4 / #0E7490 / #ECFEFF` | `DialogTypeArrayBrush` / `DialogTypeArrayTextBrush` / `DialogTypeArraySoftBrush`（若已由变量管理批次新增；否则复用 `DialogAccent*`） |
| `--vb-slate*` | `#64748B / #475569 / #F1F5F9` | `DialogTextSecondaryBrush` / `DialogTextSecondaryBrush` / `DialogRowHoverBrush` |
| 圆角 6/8/10/12/16 | — | WPF `CornerRadius` 字面量（仓内暂无圆角令牌） |
| 间距 4/8/12/16 | — | WPF `Margin` / `Padding` 字面量 |
| 字体 | Segoe UI / Microsoft YaHei UI | 同族系统字体；等宽用 Cascadia Mono / Consolas |

**颜色结论：本批不新增颜色键。** 类型色复用既有语义族（图像=青、区域=紫、数值=主色、枚举=警告、对象=灰）。
若 `DialogTypeArray*` 尚未落地，数组类型标签复用 `DialogAccent*`，不新造第二套。

## 设计块 → 现有样式键（R141/R117 对照）

| 设计块 | 复用键 | 说明 |
| --- | --- | --- |
| 窗口 / 三栏卡片 | `DialogColumnCard` / `DialogColumnHeader` / `DialogColumnTitle` / `DialogColumnTitleIcon` | 列头高度 44 由视图内联覆盖，不改共享键 |
| 列头步骤号 | **新增** `DialogStepBadge`（Border 徽标，内含 TextBlock） | 三处重复（①②③），按 R141 下沉到 `Dialog.Text.xaml`；加键前全仓 grep 防 R146 撞名 |
| 数量徽标 | `DialogChipNeutral` + `DialogChipTextNeutral` | 用现有中性胶囊，不新建 `Badge` |
| 目标 / 来源行 | `DialogOptionItem`（ListBoxItem） | 视图内 `DataTemplate` 两行式；选中态由 `ListBoxItem` 触发器负责 |
| 类型标签 / 状态 chip | `DialogTag` / `DialogTagText`；`DialogChipSuccess` / `DialogChipWarning` / `DialogChipAccent` | 胶囊与方圆角标签分开，沿用现有语义 |
| 搜索框 | `DialogSearchChrome` / `DialogSearchIcon` / `DialogSearchInput` | 左/中/右三处搜索，宽度由布局决定 |
| 常量输入 | `DialogInput` | 高度 34；右侧类型后缀用 `DialogTag` |
| 枚举预设卡片 | `DialogOptionItem` + 视图内 `RadioButton` 模板 | 选中态 = 主色描边 + 浅主色底 + 勾 |
| 绑定方式分段 | `DialogSegmentHost` + `DialogSegmentRadio` | 两段：上游端口 / 常量值 |
| 显示全部 | `DialogCheckBox` | 既有复选框样式 |
| 清除绑定 / 显示全部文字按钮 | **新增** `DialogTextButton`（Button） | 两处重复（清除、显示全部），按 R141 下沉到 `Dialog.Chip.xaml`；键名带用途后缀，先查同名 |
| 空态 | `DialogEmptyStateIcon` / `DialogEmptyStateTitle` / `DialogEmptyStateHint` | 中栏空态 + 右栏空态 |
| 底部 | `DialogFooter` / `DialogFooterPrimaryButton` / `DialogFooterSecondaryButton` | 高度 56；主按钮最小宽 88 |
| 图标按钮 | `DialogIconButton` | 30×30 + `Padding="0"`（R68） |
| 焦点 / ToolTip | `DialogFocusVisual` / `DialogToolTipStyle` | 所有可聚焦控件显式挂焦点样式；ToolTip 显式写 `<ToolTip Style=...>` |
| 英雄区 / 过滤提示条 | 视图内联（无共享键） | 仅本弹窗使用；不新增共享键，避免过度抽象 |

**样式纪律**：`VariableBindingView.xaml` 属于 `CollectedDialogViews`，**禁止本地 `<Style x:Key=...>`**
（`VariableBindingCheck.RunCollectedViewStyleContract`）；视图只用无 `x:Key` 的内联 Style、`DataTemplate`、`ControlTemplate`。
新增共享键只有 `DialogStepBadge` 与 `DialogTextButton` 两个候选，落地前先 `grep 'x:Key="…"'` 全 `UI\Controls\Themes`。

## WPF 复刻映射（第 ③ 步计划）

| 设计项 | WPF 实现 |
| --- | --- |
| 窗口 | `prism:Dialog.WindowStyle`：`WindowStyle=None`、`WindowCenteringBehavior.IsEnabled=True`、`ResizeMode=CanResizeWithGrip`、`SizeToContent=Manual`、`Width=1180`、`Height=760`、`MinWidth=1000`、`MinHeight=640` |
| 标题栏 64 | `Border` + 图标（`DialogIconFont`）+ 标题/副标题 + 关闭按钮（`DialogIconButton`，30×30，`Padding=0`，R68） |
| 上下文条 44 | `Grid` 两列：左侧当前步骤 chip；右侧「输入端口 N · 已绑定 M」+ 5px 进度条（`Border` 双层，宽度用 `MultiBinding` 换算，或 `ProgressBar` 自定义模板） |
| 左栏目标列表 | `ListBox` + `ICollectionView`（Filter：`IsBound` + 搜索文本）；`ItemContainerStyle BasedOn=DialogOptionItem`；`DataTemplate` 两行式；`ScrollViewer.HorizontalScrollBarVisibility=Disabled` |
| 左栏搜索 / 筛选 | `DialogSearchChrome` + `DialogSearchInput`；筛选用三个 `RadioButton` 套 `DialogSegmentRadio`，`Checked` 切换 `ICollectionView.Filter` |
| 中栏来源列表 | `ListBox` + `CollectionViewSource`（`GroupDescriptions` 按 `SourceCategory`）；`GroupStyle.HeaderTemplate` 映射「变量来源 / 上游算子」；`ItemContainerStyle BasedOn=DialogOptionItem` |
| 中栏页签 | `TabControl` 太重；用 `RadioButton` + `DialogSegmentRadio` 或 `ListBox` 横向三项；`Checked` 切换 `ICollectionView.Filter` |
| 右栏英雄区 | `Grid` 三列 `* / 30 / 1.12*`；左右 `Border` 用 `DataTemplate`/绑定；中间箭头 `Path`/`TextBlock`；状态 chip 由 `DataTrigger` 按 `SelectedInputPort.LinkedAddress`、`HasFunctionalEnumPort`、`ConstantValue` 切换 |
| 绑定方式 | `DialogSegmentHost` + 两个 `DialogSegmentRadio`；`Checked` 切换 `Visibility`（上游候选 / 常量表单） |
| 常量表单 | `DialogInput` + `DialogTag` 类型后缀 + `DialogHintText` + `DialogInfoPanel`（警告底色） |
| 候选列表 | `ListBox` `ItemsSource=DisplayPorts`、`SelectedItem=SelectedOutputPort`；`ItemContainerStyle BasedOn=DialogOptionItem`；行内 `Button Command=DoublockClickBindCommand CommandParameter={Binding}`；`MouseBinding` 双击同一命令；不兼容项 `IsEnabled=False` + `Opacity=.55` + `ToolTip` 原因 |
| 显示全部 | `CheckBox Style=DialogCheckBox` `IsChecked=ShowAllPorts`；提示条上的「显示全部」按钮走同一命令/属性 |
| 枚举预设 | `ItemsControl` + `RadioButton` 卡片；`IsChecked={Binding IsSelected}`；`Command=SelectPresetCommand` `CommandParameter={Binding Option}`；写入值仍是 `Option` |
| 数组索引 | 视图内 `Border` 面板（`Visibility` 由 VM `PendingArrayPort != null` 驱动）+ `DialogInput` + 「确认绑定 / 取消」；VM V2 扩展 |
| 空态 | `Grid` 覆盖层 + `DialogEmptyStateIcon/Title/Hint`；中栏与右栏各一套，按 `TreeNodes.Count` / `SelectedNode == null` / 搜索文本切换 |
| 底部 | `DialogFooter` + 左侧 `TextBlock`（当前选择摘要，`MultiBinding`/转换器）+ 右侧 `DialogFooterSecondaryButton` / `DialogFooterPrimaryButton` |
| 焦点 / ToolTip | 所有可聚焦控件 `FocusVisualStyle={StaticResource DialogFocusVisual}`；ToolTip 显式 `<ToolTip Style="{StaticResource DialogToolTipStyle}">` |

## 验证计划（WPF 复刻时执行）

| 层 | 判据 |
| --- | --- |
| 构建 | `dotnet build VisionMaster.sln` 0 错误；**再显式** `dotnet build UIThemeSmokeTest\UIThemeSmokeTest.csproj`（它不在 sln 里，R65） |
| 静态 | `FlowCanvasChecks\VariableBindingCheck` 扩展：视图 XAML `hex=0`（剥注释）；关键绑定齐全（`DisplayDataPort` / `SelectedInputPort` / `TreeNodes` / `SelectedNode` / `DisplayPorts` / `SelectedOutputPort` / `ShowAllPorts` / `ConstantValue` / `PresetOptions`）；`SelectedOutputPort` 已绑定；`UnbindCommand` 已接线；无 keyed Style；`ResolveBindKey` 三处一致；`TypeHelper.CanBindTo` 仍为唯一判据；V1/V2/V3 新属性存在且仅用于显示 |
| 运行时 | `UIThemeSmokeTest --variable-binding`（新增段）：真实 `VariableBindingViewModel` + 真实视图；断言三栏、目标筛选、来源分组/页签、候选类型过滤、显示全部、双击绑定、清除绑定、枚举预设、数组行内索引、空态、单绑定；至少覆盖 7 个状态 |
| 渲染 | `UIThemeSmokeTest --variable-binding-render` 或 `FlowCanvasChecks --render` 场景 J：默认 / 常量 / 功能预设 / 类型不兼容 / 数组索引 / 空态 / 单绑定 / 1000 窄档，至少 8 张 PNG，存 `evidence/after-render-v1-*.png`；改前截图存 `before-render-v1-default.png` |
| 回归 | `FlowCanvasChecks` 0 失败；默认 `UIThemeSmokeTest`（含 `RunHardcodedColorChecks`）全 PASS；资源断链契约 + `_ResProbe` `RESULT: ALL_OK`；`hardcoded-colors.baseline.txt` 不新增条目 |
| 变异反证 | 至少 2 条：① 摘掉候选类型过滤 → "不兼容项默认不出现"断言红；② 摘掉「清除绑定」按钮 → `UnbindCommand` 接线断言红；还原后复绿 |
| 真机 | 打开变量绑定 → 切目标端口（图像/ROI/常量/枚举）→ 切来源（全局变量/运行时/上游算子）→ 搜索/筛选 → 显示全部 → 双击绑定 → 清除绑定 → 数组索引 → 单绑定（从插件视图发起）→ 取消/确定；确认后重开验证回显 |

## 已知偏差（WPF 复刻时按 R130 记录）

1. **图标**：Web 用 Lucide 描边 SVG；WPF 用 Font Awesome 6 Pro Solid（`DialogIconFont`），形状不同、语义一致。
2. **字体度量**：同字号下 Segoe UI / 微软雅黑与 Web 的字宽、行高略有差异；不按 px 硬套。
3. **滚动条**：WPF 约占 17px；1000 窄档右栏可用宽度比 Web 少约 17px，候选行仍应保持 360 下限。
4. **窗口圆角 / 阴影**：Web 是 16px 圆角 + 多层阴影；WPF 无边框窗口的圆角由宿主决定，内容卡片保留 12px 圆角，不强行做透明窗口圆角。
5. **行高**：Web 目标行 54px；WPF `DialogOptionItem` 自带 Padding/Margin，实际视觉可能 55-56px；以不裁切两行文字为准。
6. **类型 chip 描边**：`DialogTag` 有 1px 描边 + 圆角 4，设计稿是无描边圆角 6；统一走共享 Tag 键，不另造一套。
7. **焦点态**：Web 用 2px 主色 outline；WPF 走 `DialogFocusVisual`，视觉略有差异。
8. **进度条**：Web 5px 圆角条；WPF 用自定义 `Border` 或 `ProgressBar` 模板，高度与圆角可能有 1px 差异。
9. **被隐藏数量提示**：Web 显示"还有 N 个端口被隐藏"；WPF 需要 V1 的 `HiddenByTypeCount`，若暂不加则省略该提示（功能不受影响）。
10. **数组索引面板**：Web 行内面板；WPF 需要 V2 的交互编排扩展。若本批不做 V2，则保留 `EasyDialog.ShowTextInputSync` 二级对话框，并记为偏差。
11. **枚举预设卡片**：Web 2 列网格；WPF `ItemsControl` 用 `UniformGrid Columns=2`，窄档可能改为 1 列；以文字不裁切为准。
12. **来源分组**：Web 固定两组；WPF `CollectionViewSource` 在没有变量/没有上游时不会生成空组头，属正常分组行为。
13. **单绑定模式**：Web 保留三栏、左栏只 1 个目标；WPF 如空间不足可把左栏收窄到 220，但不要隐藏目标端口上下文。
14. **Enter 键**：Web 基线只演示 Esc；WPF 复刻时再补 Enter 绑定/确认的 `InputBindings`，并确保输入框内不误触发。
15. **DPI / 高对比度 / 无障碍**：本批按 96 DPI 出图；125%/150%、高对比度与 `AutomationProperties` 需真机复核。
16. **类型标签文案**：Web 用短类型名（`HImage` / `Double`）；WPF 统一走 `TypeNameToFriendlyNameConverter`，显示「图像 (HImage)」「小数 (Double)」，与设计稿的短标签不同但信息更完整。
17. **文字按钮 hover 色**：`DialogTextButton` 的 hover 底色统一为 `DialogRowHoverBrush`、前景 `DialogTextSecondaryBrush`；设计稿「清除绑定」hover 偏红，本批不新增 Danger 变体，保持共享样式一致。

## v1 明确延后（需新增功能或契约）

| # | 项 | 需要什么 |
| --- | --- | --- |
| D1 | 多选批量绑定 | VM 新增选中集合与批量命令；本批保持单端口逐个绑定 |
| D2 | 来源树内联展开 | 当前保持「选来源 → 选输出端口」两级；把端口展开进来源树需要新的树形模板与 VM 结构 |
| D3 | 变量当前值预览 | 候选行显示变量当前值需要运行时读值契约；本批只显示类型与描述 |
| D4 | 最近使用 / 绑定历史 | 需要持久化与排序策略；本批不新增状态 |
| D5 | 从画布拖拽连线到弹窗 | 跨窗口拖放需要新的交互契约；本批保留弹窗内选择 |
| D6 | 完整键盘导航（方向键 / Enter 绑定） | 需要焦点管理与 InputBindings；本批只保证 Tab 顺序与 Esc |
| D7 | 搜索高亮 / 模糊匹配 | 需要高亮转换器与匹配算法；本批只做包含匹配 |

## 证据清单

| 文件 | 状态 |
| --- | --- |
| `evidence\design-v1-default.png` | 默认（图像 ← 采集_0.输出图像；显示全部提示） |
| `evidence\design-v1-constant.png` | 常量值（最小分数 = 0.75；替换上游连线警示） |
| `evidence\design-v1-enum.png` | 功能预设（查找类型：圆 / 直线 / 椭圆；中文名 + 写入值） |
| `evidence\design-v1-incompatible.png` | 类型不兼容（标定_0：像素当量 / 标定矩阵置灰 + 原因） |
| `evidence\design-v1-array.png` | 数组索引（分数列表 Double[] → 行内下标面板） |
| `evidence\design-v1-empty.png` | 空态（没有可用来源 + 先选择数据来源） |
| `evidence\design-v1-single.png` | 单绑定（目标变量 + 绑定并返回） |
| `evidence\design-v1-narrow.png` | 1000 窄档（264 / 280 / 408） |

> 截图均为 1400 × 1000 浏览器视口整页截取，包含页面标题、场景切换条与产品窗口；产品窗口本身 1180 × 760。

### WPF 复刻证据（2026-10-20）

| 文件 | 说明 |
| --- | --- |
| `evidence\after-render-v1-default.png` | 默认（图像 ← 全局变量 ImageVar；显示全部提示） |
| `evidence\after-render-v1-constant.png` | 常量值（最小分数 = 0.75；替换上游连线警示） |
| `evidence\after-render-v1-enum.png` | 功能预设（查找类型：圆 / 直线 / 椭圆；中文名 + 写入值） |
| `evidence\after-render-v1-incompatible.png` | 类型不兼容（显示全部；6 个端口置灰 + 原因） |
| `evidence\after-render-v1-array.png` | 数组索引（ScoresVar[0] 行内下标面板） |
| `evidence\after-render-v1-empty.png` | 空态（无输入端口 + 无可用来源 + 先选择数据来源） |
| `evidence\after-render-v1-single.png` | 单绑定（目标变量 + 绑定并返回） |
| `evidence\after-render-v1-narrow.png` | 1000 窄档（三栏仍可用） |

验证命令与结果：
- `dotnet build UIThemeSmokeTest\UIThemeSmokeTest.csproj` → **0 错误**；
- `dotnet run --project UIThemeSmokeTest -- --variable-binding` → **全部通过**（33 条：视图实例化、三栏数据、`SourceCategory` 分组、目标筛选、来源页签、类型过滤、显示全部、候选绑定、清除绑定、数组索引、换候选收起面板、数组→object、枚举预设、条件节点 Guid 键、单绑定无 TargetStep、单绑定 `BoundLink` 回传、空态、ToolTip、滚轮就近滚动接线）；
- `dotnet run --project UIThemeSmokeTest -- --variable-binding-render` → **8/8 张生成**；
- `dotnet build VisionMaster\VisionMaster.csproj` → **0 错误**；
- `FlowCanvasChecks` 既有二进制：11:18 版 **2782 通过 / 0 失败**；11:50 版 **2782 通过 / 1 失败**（仅并行任务 `[CE2-L]`）；12:16 版 **2789 通过 / 2 失败**（2 条均在 `[X] 数据线显隐与画布编辑入口`，非本批文件；`[V]` 变量绑定段全过）；fresh `dotnet build VisionMaster.sln` 被并行任务 `FlowCanvasChecks\ConditionEditorV2Checks.cs` / `ConditionEditorSummaryProbe.cs` 的编译错误阻断（`BranchType` / `Harness.Dispose` / `RecordingDialogService` 缺失，非本批文件），需并行任务收口后重跑全量。
- **鼠标滚轮**：视图根挂 `PreviewMouseWheel`，就近滚动最近可滚动的 `ScrollViewer`（一格 3 行），不依赖点击/键盘焦点；解决真机「悬停滚轮没反应、必须先点一下」。细节见 `docs\code-changes\2026-10-20-变量绑定弹窗Web化复刻.md` §3.9。

## 变更规则

设计基线确认后只读。需要调整时升版本（v2），并同步更新：
1. 本目录 `README.md` 的版本/日期/确认人；
2. `design.html` 的布局、令牌与状态；
3. 已复刻的 `VariableBindingView.xaml` 与令牌映射表；
4. `docs\UI设计基线\README.md` 的基线索引；
5. `docs\code-changes\` 的复刻记录。
