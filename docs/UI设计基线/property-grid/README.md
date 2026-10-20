# 反射属性面板 · 设计基线

| 项 | 内容 |
| --- | --- |
| 版本 | v1 |
| 确认日期 | 待用户确认 |
| 确认人 | 待用户确认 |
| 覆盖范围 | `UI\Controls\CustomControl\PropertyGrid\`（`PropertyGridBase` + `FlatPropertyGrid` + `CardPropertyGrid` + 生成器/处理器管线）与 `UI\Controls\Themes\PropertyGrid.xaml` |
| 设计基线文件 | `design.html`（v1，自包含，内联 SVG 图标） |
| 复刻状态 | **已复刻（2026-10-20）**；`_StyleProbe` 26 条断言全绿，出图 `pg-flat-ok.png` / `pg-card-ok.png` / `pg-flat-error.png` / `pg-flat-empty.png` / `pg-card-empty.png` / `pg-flat-denied.png`；设计仍待用户确认 |

## 定位

按 `AGENTS.md` §30.1 的四步流程，本文件是**第 ① 步（Web 设计基线）**的产物。
Web 只是设计媒介，**不进入构建、不部署**（R126）。确认后按本基线做第 ③ 步（WPF 复刻），
第 ④ 步以「同一屏、同一状态」的截图对照验收（R129/R132）。

## 覆盖对象

这不是一个插件视图，而是**框架级公共控件**——被三处直接使用：

| 使用方 | 形态 | 说明 |
| --- | --- | --- |
| `EasyDialog.ShowPropertyGridSync/Async` | 两者皆可 | 弹窗入口，`ParallelGroupEditModel` 等走这里 |
| `VisionMaster\Views\DialogViews\IoConfigView.xaml:419` | `FlatPropertyGrid` | IO 链路与驱动配置 |
| `Plugins\Plugin.PreProcessing\PreProcessingView.xaml:1044` | `CardPropertyGrid` | 预处理算子参数 |

另有两处间接使用：`NestedPropertyGridGenerator` 会为嵌套对象**再 new 一个同形态的子网格**；
`_StyleProbe` 是现有的渲染探针工程（`dotnet run --project _StyleProbe`，产出 `pg_render.png`）。

## 现状审查（改动理由）

以下结论全部来自实读源码与 `_StyleProbe` 的像素树输出，不是目测。

| # | 现状 | 影响 |
| --- | --- | --- |
| 1 | 同一张表里控件高度不一致：开关 24px、文本框 32px、数值框 **44px** | `NumericBox` 的 `MinHeight=32`（`Themes\NumericBox.xaml:15`）又叠了 `Padding="10,6"`（`:16`），三类控件并排像三套体系 |
| 2 | 颜色体系分裂 | `PropertyGrid.xaml` 头部注释明确"不再自带色值、全部指向 FluentTokens"，但生成器层没跟上：`StructValueGenerator` 写死 `#DCDFE6/#606266/#F5F7FA`，`BoolStateGenerator` 兜底写死 `#409EFF`（正是那段注释抱怨的第四份强调色），`FlatPropertyGrid` 外框写死 `229,229,229` |
| 3 | 校验只覆盖文本框 | `ValidationProcessor.cs:26` 是 `if (context.Control is not TextBox tb) return;`——下拉、开关、数值框一律不校验；且只在 `LostFocus` 触发；错误文案 `FontSize = 10` + `Brushes.Red` 而非危险色令牌 |
| 4 | 嵌套对象长出第二层页签 | `NestedPropertyGridGenerator.cs:46` 直接 new 完整 `CardPropertyGrid`/`FlatPropertyGrid`，子网格自带 `TabControl`，层级关系读不出来 |
| 5 | 扁平式标签列留白不均 | 标签侧 `Padding 16,12,32,12`、控件侧 `16,8,16,8`（`PropertyGridLayoutProcessor.cs:102/108`），上下差 4px，行节奏不稳 |
| 6 | 缺状态表达 | 没有"待应用"提示、没有分组内错误汇总；只读与权限不足都只是变灰 + 一个红色 emoji 锁（`PermissionProcessor.cs` 的 `🔒`） |
| 7 | 卡片式左栏页签条偏窄 | 左栏 128px 灰底，选中项只有 3px 竖线；组名超过 4 字就贴边 |

## v1 方向

> **只做纯 UI 重构**：保留 `BindingObject`、`[SuperDisplay]` 反射契约、生成器/处理器管线、
> `RequireRefresh` 整表重建语义；不新增字段、不改绑定类型、不改分组规则。

1. **行高统一 36px**：输入框 / 数值框 / 下拉 / 开关四类控件同高、8px 圆角，单行 48px 节奏。
2. **颜色全令牌化**：生成器与主题里的写死色值换成 `Dialog*Brush` / `Fluent*Brush`，改主题只改令牌。
3. **校验行级化**：错误态 = 行底浅红 + 控件红描边 + 行内错误文案，数值框 / 下拉 / 开关同样可承载。
4. **嵌套对象内联化**：左侧强调条 + 浅底内联块，块头标注实际类型名（如 `ModbusTcpConfig`），不再生成子页签。
5. **只读与权限分家**：只读 = 虚线边框 + 浅底；权限不足 = 禁用 + 锁图标 + 一行原因文字。
6. **扁平式节奏对齐**：标签侧与控件侧同竖向内边距（10px），去掉 32px 右内边距。
7. **底部状态条**：左侧胶囊显示分组数 / 校验结论，右侧取消 + 确定。
8. **卡片式左栏导航化**：176px 浅底导航，图标 + 组名 + 条目计数，选中项白底卡片 + 左侧强调条。

## 设计要点（完整清单）

1. 窗口 880×720；扁平式顶部页签 44px 高、选中项 2px 主色下划线；卡片式左栏 176px。
2. 扁平式行：标签列 150px + 值列自适应，行间 1px `DialogBorderLightBrush` 分隔，悬停整行浅底。
3. 数值框单位（`ms`）与范围（`1–65535`）作为框内右侧后缀，不另占一列。
4. 开关 38×22，轨道 `DialogBorderBrush` → 选中 `DialogAccentBrush`，滑块 18px 带 1px 投影。
5. 必填星号用 `DialogDangerBrush`；错误文案 11.5px、`DialogDangerTextBrush`，带 `role="alert"`。
6. 只读字段用 `pg-readonly`：虚线边框 + `DialogRowHoverBrush` 底 + 弱化文字。
7. 权限不足字段：禁用输入框 + 锁图标 + 「当前角色 X 无此权限，需 Y」。
8. 嵌套块：左 3px 主色强调条 + `DialogRowAlternateBrush` 底 + 块头显示 `[PropertyItem]` 的类型名。
9. 卡片式 12 栅格：`ColSpan` 直接映射为 `span-N`，未标 `ColSpan` 的默认满宽 12。
10. 命令按钮（`[Command]`）渲染成 32px 次要按钮，带播放图标，不再用"执 行"全角空格凑字。
11. 空态：居中图标 + 标题 + 说明，明确"只渲染标了 `[SuperDisplay]` 且 `Visible=true` 的属性"。
12. 底部胶囊三态互斥：默认「13 个可见属性 · 4 个分组」/ 错误「1 个字段校验未通过」/ 空态「0 个可见属性」。
13. **空态窗口按内容收高**：正常态固定 880×720；空态时 `height:auto`（实测 333px），页脚紧贴内容下沿，不留空白。
    标题栏副标题同步切换为「0 个可见属性」，不能还挂着「4 个分组 · 13 个可见属性」。

## 契约保留（一个都不能改）

| # | 契约 | 为什么不能动 |
| --- | --- | --- |
| C1 | `BindingObject` + `INotifyPropertyChanged` 订阅 | 跨线程封送、换绑时解绑旧监听（`PropertyGridBase.cs:237-256`）；破坏即内存泄漏或 `VerifyAccess` 崩溃 |
| C2 | `[SuperDisplay]` 可见性契约 | 只渲染带该特性且 `Visible == true` 的公开实例属性（`PropertyGridDefaults.GetVisibleProperties`） |
| C3 | 生成器 Priority 降序命中 | Nested(10) / Numeric(110) / ComboSource(105) / Enum·Option·Struct·Bool(100) / Type(0) |
| C4 | `RequireRefresh` = **整表重建** | 必须保持 `UpdatePropertyGrid(preserveSelectedTab: true)`；"只重建触发者所在页签"的优化会让多态字段驱动的子对象永不刷新 |
| C5 | 分组与排序 | `Group` 优先于 `GroupPath`；`GroupOrder` 按数值比较（"10" 排在 "2" 后）；组内按 `Order` |
| C6 | 页签懒加载 + 事件按实例挂 | 只建页签头、选中才构建；`SelectionChanged` 记实例而非布尔标志（`PropertyGridBase.cs:62-68`） |
| C7 | `MaxAutoHeight` 自钳制 | 无限高度容器里钳到 640，防止把宿主窗口撑成几屏高（`:143-152`） |
| C8 | 外部 `Generators` / `Processors` 可追加 | 插件可扩展管线；样式改造不得封闭这两个集合 |

## 令牌映射

| 设计变量 | 值 | WPF 资源键 | 状态 |
| --- | --- | --- | --- |
| `--pg-surface` | `#FFFFFF` | `DialogSurfaceBrush` | 已有 |
| `--pg-surface-2` | `#F8FAFC` | `DialogRowAlternateBrush` | 已有 |
| `--pg-surface-3` | `#F1F5F9` | `DialogRowHoverBrush` | 已有 |
| `--pg-border` | `#E2E8F0` | `DialogBorderBrush` | 已有 |
| `--pg-border-soft` | `#EEF2F7` | `DialogBorderLightBrush` | 已有 |
| `--pg-border-strong` | `#CBD5E1` | `DialogBorderStrongBrush` | 已有 |
| `--pg-text` | `#0F172A` | `DialogTextPrimaryBrush` | 已有 |
| `--pg-text-2` | `#475569` | `DialogTextSecondaryBrush` | 已有 |
| `--pg-text-3` | `#94A3B8` | `DialogTextMutedBrush` | 已有 |
| `--pg-primary` | `#0EA5E9` | `DialogAccentBrush` | 已有 |
| `--pg-primary-strong` | `#0284C7` | `DialogAccentDarkBrush` | 已有 |
| `--pg-primary-text` | `#0369A1` | `DialogAccentTextBrush` | 已有 |
| `--pg-primary-soft` | `#F0F9FF` | `DialogAccentSoftBrush` | 已有 |
| `--pg-primary-light` | `#E0F2FE` | `DialogAccentLightBrush` | 已有 |
| `--pg-danger` | `#EF4444` | `DialogDangerBrush` | 已有 |
| `--pg-danger-text` | `#B91C1C` | `DialogDangerTextBrush` | 已有 |
| `--pg-danger-soft` | `#FEF2F2` | `DialogDangerSubtleBrush` | 已有 |
| `--pg-danger-border` | `#FECACA` | `DialogDangerBorderBrush` | 已有 |
| `--pg-warning` / `--pg-warning-text` | `#F59E0B` / `#B45309` | `DialogWarningBrush` / `DialogWarningTextBrush` | 已有 |
| 圆角 6 / 8 / 10 / 12 / 16 | — | `CornerRadius` 字面量 | 仓内暂无圆角令牌 |
| 间距 4 / 8 / 12 / 16 / 20 | — | `Margin` / `Padding` 字面量 | 同上 |
| 字体 | Segoe UI / 微软雅黑 | 同族系统字体 | 等宽用 Cascadia Mono |

**不新增颜色语义**：全部落在既有 `Dialog*` 语义域内。`PropertyGrid.xaml` 现有的
`SidebarBg` / `ContentBg` / `AccentBlue` 等桥接键（第 22-36 行）保持不变——`LayoutProcessor` 与
`EasyDialog` 是按名字取这些资源的（含 `FindResource`），改名会让取不到的一方直接抛异常。

## 复刻约束（第 ③ 步必须遵守）

1. **只改外观层**：`PropertyGridLayoutProcessor` 的内边距与对齐、`PropertyGrid.xaml` 的两套模板、
   各生成器的控件构造参数。**不动** `PropertyGridBase` 的管线顺序、事件挂载、刷新防抖。
2. **生成器里的写死色值全部换令牌**，但保留 `TryFindResource` 的回退路径——属性网格可能被
   没有合并本库主题的宿主直接 `new` 出来（`PropertyGridLayoutProcessor.cs:142` 的注释已写明），
   抛异常等于控件不可用。
3. **`NestedPropertyGridGenerator` 改为内联块**：不再 `new CardPropertyGrid()`，而是产出
   「强调条 + 浅底 + 递归子面板」的组合。**这一步会动递归结构，建议单独一批做**，
   并补一条断言证明嵌套层级的字段仍然全部可见（用 `GetVisibleProperties` 逐层核对）。
4. **`ValidationProcessor` 扩展非 TextBox 控件**时，`LostFocus` 语义对下拉/开关不适用——
   改用 `SelectionChanged` / `Checked` / `ValueChanged`，且必须保留现有的 `RegisterCleanup` 解绑，
   否则重绘时旧控件被事件链拽住不释放（`ValidationProcessor.cs:45-46` 已有这条注释）。
5. **`PropertyGrid.xaml` 的键名不动**：`SidebarBg` / `ContentBg` / `TextMain` / `TextRegular` /
   `BorderLight` / `AccentBlue` / `AppBg` / `CardBg` 都有按名取用的消费方。
6. **`CardPropertyGrid` 左栏宽度**：现状 128px 是有理由的（注释写明 180 太宽，会让插件配置壳
   540px 参数列只剩 248px）。v1 设计稿是 176px，**复刻前必须实测** `PluginConfigShellView`
   在 640 最小档下的可用宽度；若不足，退到 152px。
7. **空态高度**：WPF 的 `PropertyGridBase` 有 `MaxAutoHeight` 钳制（默认 640），但**没有"内容少就收高"的机制**——
   `MeasureOverride` 只在高度无限时往下钳，不会因为属性为 0 而收缩。空态要在 WPF 侧真正不留空白，
   需要宿主配合（`EasyDialog` 的窗口 `SizeToContent`）或在 `UpdatePropertyGrid` 里发现
   `properties.Count == 0` 时把控件高度设为 `Auto`。**这是设计稿与现状的真实差异，复刻时需单独决策**。
8. **验证**：`_StyleProbe` 出图对照 + `FlowCanvasChecks` 的 `PropertyGridRefreshCheck` 不回归。

## 已知偏差（Web 与 WPF 的预期差异，复刻时按 R130 记录）

1. **图标**：Web 用内联 Lucide SVG；WPF 用 Font Awesome 6 Pro Solid（`DialogIconFont`）。形状不同，语义一致。
2. **字体度量**：同字号下 Segoe UI / 微软雅黑与浏览器渲染的字宽、行高略有差异，不按 px 硬套。
3. **滚动条**：WPF 滚动条约占 17px，窄档可用宽度比 Web 少约 17px。
4. **阴影**：Web 窗口阴影是多层 CSS；WPF 复用宿主 `DropShadowEffect` + `DialogShadowColor`。
5. **动效**：Web 150ms 过渡；WPF 统一 150~200ms，或按 `SystemParameters.ClientAreaAnimation` 直接关掉。
6. **焦点环**：Web 用 `:focus-visible`；WPF 走 `DialogFocusVisual`（R135），鼠标点击不亮环。
7. **`pg-type` 类型标注**：设计稿早期版本在标签右侧显示 `string`/`int` 等类型名，**已移除**——
   那是开发者信息，现场用户不需要。若后续要做"开发者模式"，属新增契约。
8. **卡片式 `pg-nav-count` 计数**：设计稿显示每组的属性条数。WPF 侧可算（`group.Count()`），
   但属新增显示元素，复刻时若不做需在差异清单里注明。

## 未纳入本版（需新增契约，记录不改）

| # | 项 | 需要什么 |
| --- | --- | --- |
| D1 | 分组内错误汇总（页签头显示"该组有 N 个错误"） | 处理器管线是逐属性执行，没有跨属性汇总状态 |
| D2 | 脏值与回滚（"待应用"计数、撤销） | 属性面板即时写属性，点取消不回滚；需在 `BindingObject` 之外引入草稿层 |
| D3 | 字段级搜索与重置 | 需按 `Name`/`Description` 建索引，以及"重置为默认值"的元数据来源 |
| D4 | 跨页签联动高亮 | `RequireRefresh` 能整表重建，但"定位到被驱动的字段"需要新的聚焦契约 |
| D5 | 字段级 ToolTip 富文本 | 现状 `Description` 只做纯文本 ToolTip（`PropertyGridLayoutProcessor.cs:183-184`） |

## 变更规则

设计基线确认后只读。需要调整设计时升版本（`v1` → `v2`），并同步更新本映射表与已复刻的
`PropertyGrid.xaml` / 生成器代码。

## 复刻实现（第 ③ 步，已完成 2026-10-20）

| 设计项 | WPF 实现 |
| --- | --- |
| 行高统一 48 / 控件 36 | `PropertyGridMetrics.ControlHeight = 36` + 布局处理器 `RowMinHeight = 48`；四类控件（TextBox / ComboBox / NumericBox / ToggleButton）都过 `ApplyInputLook` |
| 标签列 150px | `PropertyGridLayoutProcessor.FlatLabelWidth = 150`，去掉原 32px 右内边距 |
| 扁平式外框 12px 圆角 | `FlatPropertyGrid.CreateFlatContent`：`CornerRadius(12)` + 1px `DialogBorderBrush` |
| 页签 44px + 2px 下划线 | `Themes\PropertyGrid.xaml` 的 `LightFlatTabItemStyle`（`MinHeight=44` + `BorderThickness="0,0,0,2"`） |
| 卡片式左栏 176px + 计数 | 模板 `ColumnDefinition Width="176"`；计数走新增附加属性 `PropertyGridAttach.GroupCount` |
| 校验行级化 | `ValidationProcessor`：行容器浅红底 + 控件红描边 + 行内文案；覆盖 TextBox / ComboBox / ToggleButton |
| 嵌套内联块 | `NestedPropertyGridGenerator.BuildInlineBlock`：3px 强调条 + 浅底 + 块头（属性名 + 类型名）；子网格 `IsNested = true` 不画外框 |
| 只读 / 权限分家 | `ApplyReadOnlyLook`（浅底 + 弱化字）；`PermissionProcessor` 锁图标 + 「当前角色 X 无此权限，需 Y」 |
| 空态 | `PropertyGridBase.IsEmptyState`（只读 DP）+ `BuildEmptyState()`；卡片式模板按它收起左栏；`MeasureOverride` 空态返回内容高 |

### 复刻中抓到的四个真问题（都是断言抓的，不是目测）

1. **行高 48/49 混排**：`BorderThickness` 占布局，值列 Padding 写 6 会让整行 49px。改成 5.5。
2. **嵌套块双层卡片**：子网格自带外框 + 父级内联块又一层 → 屏幕上像"卡片套卡片"。加 `IsNested`。
3. **卡片式块内第二条导航**：嵌套子网格继承卡片形态后，块里又长出 176px 导航栏。改为嵌套一律用扁平式。
4. **命令按钮被拉成满宽主色横杠**：`HorizontalAlignment` 原先只对 ToggleButton 特殊处理，Button 被 Stretch。改为 Button 也贴左。

### 有意偏离设计稿的一处（需用户裁定）

**开关轨道保持黑色（`FluentToggleTrackOnColor = #FF1B1B1F`），未改成设计稿的蓝色。**

理由：该令牌是**全应用级**的，`FluentTokens.xaml` 的注释明确写着"定成单色黑轨道 + 白滑块而不是系统蓝：
蓝色在浅灰界面上太抢，和真正的操作按钮（也是蓝）摆在一起，状态和动作就分不清了"。
属性面板单独改蓝，会与全应用其它所有开关（相机设置、插件配置、系统设置）不一致 ——
那是比"设计稿不一致"更严重的一致性问题。若裁定要蓝，应改 `FluentTokens.xaml` 一处、全局生效，
而不是在属性网格里单点覆盖。

### 验证证据

| 项 | 结果 |
| --- | --- |
| `dotnet build UI\Controls\UI.csproj` | **0 错误 / 0 警告** |
| `_StyleProbe` 断言 | **26/26 通过**（行高 / 控件高 / 标签列 / 最后一行无底线 / 左栏 176 / 分组计数 / 校验三态 / 空态四条 / 权限三条 / 空态收高两条） |
| `FlowCanvasChecks` | 2789 通过 / 2 失败；**PG 段 9 条全绿**（多态刷新契约未破）。2 条失败在 `AutoPortConfigView` 相关断言上，该文件由并发会话于 11:55 改动、不引用 PropertyGrid/NumericBox，**非本次改动引入** |
| 资源作用域 | `tools\audit_resource_scope.ps1`：**RESULT: CLEAN**（0 处跨分册引用） |
| 硬编码颜色 | `PropertyGrid.xaml`：**0 处**（基线原为 1，只减不增达标） |
| 渲染出图 | 6 张 PNG 在 `_StyleProbe\`：扁平/卡片 × 默认/空态，加校验失败、权限受限 |
