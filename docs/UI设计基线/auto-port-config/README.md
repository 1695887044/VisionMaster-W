# 无自定义视图插件 · 自动参数面板 · 设计基线

| 项 | 内容 |
| --- | --- |
| 版本 | v1 |
| 日期 | 2026-10-20 |
| 确认日期 / 人 | 待用户确认 |
| 覆盖范围 | `VisionMaster\Views\DialogViews\AutoPortConfigView.xaml` / `.xaml.cs` + `PluginConfigShellView` 共用外壳（仅内容区） |
| 窗口尺寸 | 560 × 内容自适应高度（`SizeToContent.Height`，`MinHeight=180`，`MaxHeight=760`）；设计稿另附 640 宽对照 |
| 设计文件 | `design.html`（自包含、内联 SVG、无外部依赖） |
| 证据 | `evidence/design-v1-*.png`（默认 / 已链接 / 空态 / 执行中 / 成功 / 失败 / 640 宽 / 总览） |
| 复刻状态 | **已复刻（2026-10-20）**；WPF 证据 `evidence/wpf-after-render-v1-*.png`；待用户确认 |

## 定位

按 `AGENTS.md` §30.1（R126-R132）与 `docs/UI设计基线/插件自定义视图Web化改造SOP.md` 的视觉语言，
本目录是无自定义视图插件兜底面板的**设计媒介**：

- 不进入工程构建、不部署、不作为另一个客户端维护；
- 确认后只读；需要调整时升 v2，并同步 WPF 复刻与令牌映射；
- 产品交付物仍是 WPF 代码、令牌映射表、截图证据、差异清单。

## 现状审查（为什么需要美化）

| # | 现状 | 影响 |
| --- | --- | --- |
| 1 | `AutoPortConfigView.xaml` 只有一个 `ScrollViewer` + `StackPanel`（`:18-24`），行由 code-behind 动态创建 | 没有步骤上下文，用户只看到一串输入框，不知道“这是哪个步骤、哪些是必填、哪些已链接” |
| 2 | 每行是一个 `LinkableValueEditor`，固定 `Width=520`、`Margin=0,0,0,6`（`AutoPortConfigView.xaml.cs:85-104`） | 端口多时行距紧、标签与输入框贴在一起；链接态与常量态缺少层次 |
| 3 | 提示条是浅主色圆角条 + `ℹ`（`BuildHintBar`，`:136-167`） | 信息只有“怎么用”，没有“当前步骤有多少参数、多少已链接”的全局视图 |
| 4 | 空态只有一行 `TextBlock`「该步骤没有输入参数。」（`:69-79`） | 看起来像界面坏了；没有说明为什么空、还能不能确认/执行 |
| 5 | `ScrollViewer.MaxHeight=420`（`:19`） | 10 个端口的真实兜底场景（几何查找）会出滚动条；窗口本身可以更高，420 是历史遗留的保守值 |
| 6 | 枚举端口由 `LinkableValueEditor` 自己渲染下拉，但外部没有任何“封闭候选”的提示 | 用户会疑惑为什么这一行没有 🔗 / ✕（枚举只能选、不能链接） |
| 7 | 底部状态只存在于宿主 `PluginConfigShellView`，内容区不呼应执行中 / 成功 / 失败 | 执行时用户的目光在参数区，状态变化不够显眼 |

## v1 方向

> 只做**信息层级与视觉**重构；不新增端口、不新增命令、不改绑定与生命周期。
> 让“步骤 → 输入参数 → 每个端口的状态”在一屏内可读。

1. **Step 信息前置**：标题下增加只读步骤卡，展示步骤名、面板来源、输入 / 必填 / 已链接三个统计。
2. **输入清单化**：一行一个端口，左侧「中文描述 + 端口键 + 类型徽章」，右侧固定宽度控件，链接 / 清除固定在行尾。
3. **三种端口形态**：可填常量 = 输入框；不可文本输入 = 「需链接变量」占位 + 链接按钮；枚举 = 下拉 + 锁图标（封闭候选）。
4. **链接态是地址**：链接后显示只读地址条（如 `图像采集_0.Image`），解除后恢复固定值；不把“值”当成“地址”。
5. **空态给下一步**：图标 + 原因 + 提示（确认 / 取消 / 执行仍可用），并说明 `[StepConfig]` 不在本面板。
6. **状态复用宿主三档**：执行中 / 成功 / 失败全部由 `PluginConfigShellViewModel.StatusText` 驱动，不新增 `StatusLevel`。

## 布局结构

```text
560 × 自适应高度
├─ 标题栏 72
│  ├─ 40×40 图标徽章（主色浅底 + 步骤图标）
│  ├─ 标题：StepName
│  ├─ 副标题：StepDescription
│  └─ 「自动生成」chip + 关闭 30×30
├─ 内容区（padding 16 / 20）
│  ├─ 步骤卡 56：步骤图标 + STEP / 名称 / 通用参数面板 + 输入 / 必填 / 已链接
│  ├─ 区块头 22：输入参数 + 计数 + 使用提示
│  ├─ 参数列表：10 行 × 48，1px 分隔
│  │  └─ 行：描述 + 端口键 + 类型徽章 | 190px 控件 | 64px 链接/清除
│  └─ 空态：图标 + 标题 + 说明 + 提示 chip
└─ 页脚 56（宿主）：状态胶囊 + 耗时 + 执行 / 取消 / 确认
```

### 尺寸刻度

| 项 | 值 |
| --- | --- |
| 窗口宽 | 560（产品）；640（设计对照） |
| 标题栏 | 72 |
| 内容左右内边距 | 20（上下 8） |
| 步骤卡 | 56 高 / 12 圆角 |
| 区块头 | 22 高 |
| 参数行 | 48 高（4px 上下内边距） |
| 输入控件 | 36 高 / 8 圆角 |
| 图标按钮 | 28×28（行内）/ 30×30（标题栏） |
| 页脚 | 56 |
| 字号 | 标题 16 / 步骤名 14 / 行标签 12 / 端口键 10 / 类型徽章 9.5 / 页脚 10.5~11.5 |
| 圆角 | 6 / 8 / 10 / 12 / 16 |
| 动效 | 150ms（hover / focus）/ 200ms（窗口宽度） |

## 状态覆盖

| 状态 | 触发 | 设计表现 |
| --- | --- | --- |
| 默认 | 打开面板 | `SrcImage` 已链接；`MaskRegion` 未链接（需链接变量）；枚举为下拉；其余为固定值 |
| 已链接 | 演示切换 | 图像 / 区域 / 数值三个代表端口显示地址条；已链接统计变为 3 |
| 空态 | 插件没有输入端口 | 步骤卡统计归零；列表替换为图标 + 原因 + 提示；确认 / 取消 / 执行仍可用 |
| 执行中 | 点「执行」 | 状态胶囊变主色 + 脉冲点；「执行」禁用；耗时归零 |
| 成功 | 试运行成功 | 绿色状态胶囊 + `✅ 成功 - …`；耗时显示实际毫秒 |
| 失败 | 试运行失败 | 红色状态胶囊 + `❌ 失败 - …`；耗时显示实际毫秒 |
| 640 宽 | 设计对照 | 行标签获得更多空间，控件与行尾按钮位置不变；产品仍固定 560 |

## 契约保留（一个都不能改）

| # | 契约 | 为什么不能动 |
| --- | --- | --- |
| C1 | `AutoPortConfigView` 实现 `IPluginConfigView`；`Initialize(stepData)` 幂等（端口不变复用、变化时重建）；`OnConfirm` 委托 `_plugin.OnConfirm` | 宿主靠控件自身取 `OnConfirm`；改成独立 VM 或换接口 = 确认不保存 |
| C2 | 端口来源仍是 `_plugin.Inputs.Values`，按 `Name` 排序 | 编辑器要读写端口实时值并连上游；`PortDefinition` 快照是旧数据 |
| C3 | 每行仍由 `LinkableValueEditor` 承载；`Port` / `StepData` / 链接与清除行为原样 | 端口持久化、类型转换、`ValueChanged` 订阅、`LinkPathEvent` 都在控件里 |
| C4 | 窗口策略仍由视图自己负责：`MinWidth=560`、`Width=560`、`SizeToContent.Height`、`MinHeight=180`、`MaxHeight=760` | 外壳不猜内容类型；`MinWidth=560` 抵消外壳的 640 保底；插件自带视图不受影响 |
| C5 | 宿主头部 / 页脚 / 执行 / 取消 / 确认 / 状态文案全部复用 | `PluginConfigShellView` 是所有插件共用的外壳，本设计不改它 |

> 本版**不**把 `[StepConfig]` 属性加入面板：`AutoPortConfigView` 当前只渲染 `InputPort`，这是刻意边界（见代码注释）。
> 需要显示 `[StepConfig]` 的插件（如「轴回零」「轴停止」）要另做宿主契约扩展，见 D1。

## 令牌映射（Web → WPF）

| 设计变量 | 值 | WPF 资源键 |
| --- | --- | --- |
| `--ap-surface` | `#FFFFFF` | `DialogSurfaceBrush` |
| `--ap-surface-2` | `#F8FAFC` | `DialogRowAlternateBrush` |
| `--ap-surface-3` | `#F1F5F9` | `DialogRowHoverBrush` |
| `--ap-border` | `#E2E8F0` | `DialogBorderBrush` |
| `--ap-border-soft` | `#EEF2F7` | `DialogBorderLightBrush` |
| `--ap-border-strong` | `#CBD5E1` | `DialogBorderStrongBrush` |
| `--ap-text` | `#0F172A` | `DialogTextPrimaryBrush` |
| `--ap-text-2` | `#475569` | `DialogTextSecondaryBrush` |
| `--ap-text-3` | `#94A3B8` | `DialogTextMutedBrush` |
| `--ap-text-4` | `#64748B` | `ScadaSecondaryTextBrush`（近似） |
| `--ap-primary` | `#0EA5E9` | `DialogAccentBrush` |
| `--ap-primary-strong` | `#0284C7` | `DialogAccentDarkBrush` |
| `--ap-primary-text` | `#0369A1` | `DialogAccentTextBrush` |
| `--ap-primary-soft` | `#F0F9FF` | `DialogAccentSoftBrush` |
| `--ap-primary-light` | `#E0F2FE` | `DialogAccentLightBrush` |
| `--ap-primary-border` | `#BAE6FD` | `DialogAccentBorderBrush` |
| `--ap-success` | `#22C55E` | `DialogSuccessBrush` |
| `--ap-success-text` | `#15803D` | `DialogSuccessTextBrush` |
| `--ap-success-soft` | `#F0FDF4` | `DialogSuccessSubtleBrush` |
| `--ap-success-border` | `#BBF7D0` | `DialogSuccessBorderBrush` |
| `--ap-danger` | `#EF4444` | `DialogDangerBrush` |
| `--ap-danger-text` | `#B91C1C` | `DialogDangerTextBrush` |
| `--ap-danger-soft` | `#FEF2F2` | `DialogDangerSubtleBrush` |
| `--ap-danger-border` | `#FECACA` | `DialogDangerBorderBrush` |
| `--ap-warning` | `#F59E0B` | `DialogWarningBrush` |
| `--ap-warning-text` | `#B45309` | `DialogWarningTextBrush` |
| `--ap-warning-soft` | `#FFF4E5` | `DialogWarningSubtleBrush` |
| `--ap-warning-border` | `#FDE68A` | `DialogWarningBorderBrush` |
| 窗口阴影 | — | 宿主已有 `DropShadowEffect` + `DialogShadowColor`（`PluginConfigShellView.xaml:130-137`） |
| 圆角 6/8/10/12/16 | — | WPF `CornerRadius` 字面量（仓内暂无圆角令牌字典） |
| 间距 4/8/12/16/20/24 | — | WPF `Margin` / `Padding` 字面量（同上） |
| 字体 | Segoe UI / Microsoft YaHei UI | 同族系统字体；等宽用 Cascadia Mono / Consolas |

> 本设计没有新增颜色语义；上表键均为既有语义令牌，实施时只引用、不改既有键值。

## 复刻实现（第 ③ 步，已完成）

| 设计项 | WPF 实现 |
| --- | --- |
| Step 概览卡 | `AutoPortConfigView.xaml` 的 `StepCardStyle` + `StepIconText` / `StepNameText` / 三个统计；数据来自 `IStepConfigData` 与端口集合 |
| 输入参数区块头 | `SectionTitleStyle` + `CountBadgeStyle` + `SectionHintTextStyle`（「常量直接填 · 需要上游供值点链接」） |
| 单行端口 | 新增 `AutoPortRow.xaml` / `.xaml.cs`：外置描述 / 端口键 / 类型徽章 + 240px `LinkableValueEditor` 编辑列（共享控件内置链接/清除，输入区实际约 190）+ 28px 枚举锁 |
| 行状态 | `EditorShell` 的 DataTrigger：已链接 → `DialogAccentSoftBrush` / `DialogAccentBorderBrush`；不可文本未链接 → `DialogRowAlternateBrush` / `DialogBorderStrongBrush`；行本身只做 hover |
| 类型徽章 | `AutoPortRow.GetTypeLabel`：HImage→图像 / HRegion→区域 / enum→枚举 / bool→布尔 / int→整数 / double→数值 / string→文本 / 其它→参数 |
| 空态 | `EmptyStateStyle` + `EmptyTitleText` / `EmptyDescriptionText`；`_plugin == null` 时改为「无法读取插件参数」 |
| 滚动上限 | `AutoPortConfigView.xaml` 的 `ScrollViewer.MaxHeight=600`；窗口策略为 `MinWidth=560` / `Width=560` / `SizeToContent.Height` / `MinHeight=180` / `MaxHeight=760`（`MinWidth` 用于抵消外壳的 640 保底） |
| 宿主页脚 | 未改；状态胶囊 / 耗时 / 执行 / 取消 / 确认仍由 `PluginConfigShellView` 提供 |

### 与共享控件的边界

- `LinkableValueEditor` 仍是唯一编辑入口；`Port` / `StepData` / `LinkPathEvent` / `RemoveLink` 行为不变。
- 设计稿中的「链接地址胶囊」与「需链接变量虚线按钮」已用 `EditorShell` 的浅主色底 / 描边与灰底 / 实线描边近似；控件内部的地址仍是蓝色下划线只读框、不可文本输入仍是「需链接变量」文本 + 行尾链接按钮（见「已知偏差」#3/#4）。
- 未改 `PluginConfigShellView.xaml` 的头部 / 页脚 / 窗口样式；未改 `AutoPortConfigView` 的 `DataContext` / `OnConfirm` 委托。

## 证据

| 文件 | 说明 |
| --- | --- |
| `evidence/design-v1-overview.png` | 设计稿整页总览（1280 宽） |
| `evidence/design-v1-default-560.png` | 默认态（560 宽，`SrcImage` 已链接） |
| `evidence/design-v1-linked-560.png` | 已链接态（图像 / 区域 / 数值三个代表端口） |
| `evidence/design-v1-empty-560.png` | 空态（没有输入端口） |
| `evidence/design-v1-running-560.png` | 执行中（执行按钮禁用） |
| `evidence/design-v1-success-560.png` | 成功态（绿色状态胶囊） |
| `evidence/design-v1-failure-560.png` | 失败态（红色状态胶囊） |
| `evidence/design-v1-default-640.png` | 640 宽对照 |
| `evidence/wpf-after-render-v1-default.png` | WPF 复刻默认态（8 端口探针，含链接 / 枚举锁 / 类型徽章） |
| `evidence/wpf-after-render-v1-empty.png` | WPF 复刻空态 |

## 已知偏差（设计稿 vs 现状 / 复刻预判）

1. **字体度量**：Web 用 Segoe UI / 微软雅黑；WPF 同族字体，行高与字宽略有差异，不按 px 硬套。
2. **窗口/客户区**：560 是窗口外宽；WPF 客户区约 548，扣宿主内容边距后卡片约 508。Web 设计以 560 卡片为基准，因此 WPF 内容区会略窄；滚动条约再占 17px（端口多时出现）。
3. **链接态**：Web 用浅主色胶囊；WPF 用 `EditorShell` 的浅主色底 + 浅主色描边，内部仍是 `LinkableValueEditor` 自带的蓝色下划线只读地址（未改共享控件）。形态接近，非像素一致。
4. **需链接变量**：Web 用虚线按钮；WPF 用 `EditorShell` 的浅灰底 + `DialogBorderStrongBrush` 实线描边，内部仍是 `PART_Placeholder` 文本 + 行尾链接按钮（WPF `Border` 无虚线）。
5. **枚举锁图标**：Web 用行尾锁图标；WPF 可在行容器追加一个只读图标，不改 `LinkableValueEditor` 内部。
6. **阴影 / 动效**：Web 多层 CSS 阴影；WPF 用宿主现有 `DropShadowEffect`；动效统一 150~200ms。
7. **状态图标**：Web 用 `✅ / ❌` 字符；WPF 状态文案仍由 `StatusText` 原样输出，是否保留 emoji 由宿主现有文案决定。
8. **640 宽档**：仅设计对照，产品仍固定 560；不改变 `AutoPortConfigView` 的窗口策略。
9. **图标字体**：Web 用内联 SVG；WPF 使用 `IStepConfigData.Icon`（Font Awesome 字符），形状不同、语义一致。
10. **高度边界**：Web 窗口约 725 高；WPF 受宿主 `ContentPresenter Margin="20,16"` 与 `MaxHeight=760` 影响，实际可用内容高度约 600。10 个端口在 48px 行高下接近上限，更多端口由滚动条接管。
11. **行布局**：设计稿是「190px 控件 + 64px 链接/清除」；WPF 的 `LinkableValueEditor` 把链接/清除内置在控件里，无法在不改共享控件的前提下拆开，因此采用「240px 编辑列（输入区约 190）+ 28px 枚举锁」。总宽 268 vs 设计 254，标签列相应少约 14px。
12. **端口集合变化时的订阅**：`LinkableValueEditor` 没有卸载退订入口；`Initialize` 在端口集合/顺序不变时复用现有行（避免外壳与视图各调一次造成订阅累积），只有端口集合变化才重建。当前无自定义视图插件的端口是静态的，动态端口插件均有自定义视图，不走本面板。

## 明确延后（需要新增功能或契约）

| # | 项 | 需要什么 |
| --- | --- | --- |
| D1 | `[StepConfig]` 编辑入口 | 宿主暴露反射属性表、类型转换与统一写路径；当前兜底面板只渲染 `InputPort` |
| D2 | 布尔开关 / 枚举卡片 | 扩展 `LinkableValueEditor` 或新增行内控件；当前布尔仍是文本框 |
| D3 | 搜索 / 分组 / 行内校验 | 端口分组元数据、筛选状态、校验契约；本版单列直出 |
| D4 | 输出端口只读区 | 输出属于流程连线 / 运行结果，不在配置面板展示 |
| D5 | 参数说明 Tooltip | 端口 `Description` 已是人话标签；更长帮助文本需要插件侧元数据 |
| D6 | 宽窄双档 | 产品固定 560；640 只用于设计对照，不改变窗口策略 |

## 验证（设计 + 复刻）

| 层 | 结果 |
| --- | --- |
| 设计稿渲染 | 自包含、无外部依赖；浏览器渲染通过；8 张设计证据 PNG 已生成 |
| 宿主构建 | `dotnet build VisionMaster\VisionMaster.csproj -v m`：**0 错误**；本次命令仅 1 个既有 MSB3245 警告（HslCommunication） |
| 冒烟 | `dotnet run --project UIThemeSmokeTest --no-build -- --auto-port-config`：**33/33 PASS**（行数/排序、统计、链接态、不可输入态、枚举锁、类型徽章、240px 编辑列、外壳 MinWidth 钳制、幂等复用、空态、插件未注入） |
| 渲染证据 | `evidence/wpf-after-render-v1-default.png` / `wpf-after-render-v1-empty.png` |
| 静态门禁 | `FlowCanvasChecks`：**2817 通过 / 0 失败**；自动参数面板断言已更新到 v1（240px 编辑列 / 48px 行高 / 使用提示行） |

> 说明：`UIThemeSmokeTest` 的**默认全量冒烟**退出码恒为 1（既有 GridView 红灯，R46）；`--auto-port-config` 模式自身以退出码 0 + 日志 `PASS/FAIL` 为准。本次未改插件工程，无需重建 `Modules`。设计基线确认后只读；需要调整时升 v2，并同步 `docs/UI设计基线/README.md` 索引与复刻页面。
>
> **最终验证（2026-10-20）**：宿主构建 **0 错误**、`--auto-port-config` 冒烟 **33/33**、`FlowCanvasChecks` **2817/0**。另修复了打开「等待_0」时的 `ConditionEditor.Branches` 空引用（见 `docs/code-changes/2026-10-20-等待节点参数弹窗空引用（ConditionEditor Branches未初始化）.md`），该修复让原本未接入 `Run()` 的 Case/Wait/For 三组条件编辑器断言重新生效。
