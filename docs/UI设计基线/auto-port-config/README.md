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

---

## v2 基线（2026-10-24，待用户确认）

**文件**：`design-v2.html`（自包含，无外部依赖）。v1 继续作为**已实现基线**保留，v2 通过前不覆盖 v1 的任何契约。

### 本版解决什么

| 编号 | 问题 | v1 现状 | v2 改法 |
| --- | --- | --- | --- |
| Q4 | 无自定义视图的运控步骤，`[StepConfig]` 只在本插件自带视图里可见 | 面板只渲染 `InputPort`，8 个运控步骤类的 `[StepConfig]`（回零方式、超时、轮询间隔…）在通用面板里**完全看不到** | 新增「步骤参数」区块，把 `[StepConfig]` 渲染进同一面板；与「输入参数」分成两个区块（语义不同：前者可被上游供值，后者只属于本步骤） |
| Q5 | 手填数值没有分级提示 | 手填只产文本，写进去才知道转不了 | 能转**静默**；转不了当场标红 + 说清被拒原值（R34 宽进严出）；空值灰提示默认值；不弹窗、不阻断输入 |

### 与 v1 的差异（全部为增量）

1. **两个区块**：「输入参数」（`InputPort`，可链接上游）/「步骤参数」（`[StepConfig]`）/「输出端口」（只读）三分，区块头带计数，可折叠。
2. **控件由值类型决定，不由插件决定**：枚举 → 下拉（中文显示、值仍是成员名，R40 `StepConfigOption.ForEnum`）；布尔 → 开关；整数/小数 → 数字框 + 单位；文本 → 输入框。避免每个插件自造一套。
3. **名字类参数不接链接孔**（R33）：轴号等只给「手填 + 候选下拉」，不挂 🔗，否则等于允许上游把一个值当名字送进来。
4. **页脚去掉「执行」**：本面板改的是运控动作参数（回零方式、超时），面板内一键执行会直接动轴。执行仍由流程栏发起；页脚只剩「取消 / 确认」，右侧显示「状态 + 改动 N 处」。
5. **标题右侧保留「⚡自动生成」**：面板外壳仍是自动生成语义，本版只扩展它渲染的内容，不改外壳契约。

### 契约保留（一个都不能改）

C1–C5 与 v1 完全一致（`AutoPortConfigView : IPluginConfigView`、端口来源仍是 `_plugin.Inputs.Values`、每行仍由 `LinkableValueEditor` 承载、窗口策略仍由视图自己负责、宿主头部/页脚/状态文案全部复用）。**新增一条**：

| # | 契约 | 为什么不能动 |
| --- | --- | --- |
| C6 | `[StepConfig]` 的取值仍写回插件实例的 `StepData`，确认时才 `ApplyToModel()`；枚举写回的是**成员名**不是显示名 | R40：拿「圆」当值 = 改显示名打断已配流程 |

### 令牌映射

与 v1 的表完全一致（`DialogSurfaceBrush` / `DialogBorderBrush` / `DialogAccentBrush` / `DialogSuccessBrush` / `DialogDangerBrush` / `DialogWarningBrush` 等既有语义令牌）。**本版没有新增任何颜色语义**；圆角与间距仍是字面量（仓内暂无圆角/间距令牌字典）。

### 状态覆盖

| 状态 | 触发 | 设计表现 |
| --- | --- | --- |
| 默认 | 打开面板 | 输入 1 / 必填 1 / 步骤参数 2；状态「参数完整」、改动 2 处 |
| 已链接 | 代表端口已接上游 | 数值行显示链接地址胶囊 + 取消链接图标 |
| 空态 | 插件没有任何端口与 `[StepConfig]` | 统计归零 + 图标 + 原因 + 指向帮助页；确认仍可用 |
| 执行中 → 有未保存改动 | 改了步骤参数未确认 | 状态胶囊主色 + 脉动点，「改动 2 处」 |
| 成功 | 点确认保存成功 | 绿色胶囊「参数已保存 · 改动 0 处」 |
| 失败 | 保存失败（只读） | 红色胶囊「保存失败 - 步骤参数区只读」 |
| 校验失败 | 超时输入 `12a` 后点确认 | 输入框标红 + 状态「有 1 处参数非法，无法确认」+ 焦点跳回该输入框、面板不关闭 |
| 折叠 | 收起步骤参数区块 | 计数与类型徽章保留在头部，折叠后仍看得出规模 |
| 轴等待 | 切换到 `MotionWaitPlugin` | 3 输入端口（其中 2 个可链接）/ 2 `[StepConfig]` / 2 输出端口 |
| 640 宽 | 设计对照 | 标签列获得更多空间，控件与行尾图标位置不变；产品仍固定 560 |

### 已知偏差

1. **数字框对齐**：v2 行网格为 `minmax(0,1fr) 232px 34px`（v1 是 190px / 40px），数字右对齐、单位独立占位，避免「120000ms」与锁定图标互挤。
2. **链接态与「需链接变量」**：同 v1 #3/#4，WPF 用 `EditorShell` 的浅主色底/描边近似 Web 的胶囊与虚线按钮。
3. **枚举锁图标**：Web 行尾锁图标；WPF 在行容器追加只读图标，不改 `LinkableValueEditor` 内部。
4. **字体度量 / 滚动条 / 阴影 / 动效**：同 v1 的 #1/#2/#6/#7，不按 px 硬套。
5. **空态文案**：v2 把「确认 / 取消 / 执行仍然可用」改为「确认 / 取消仍然可用」（页脚去掉执行后的必然结果）。

### 截图证据（v2）

生成于 `artifacts/render/20261024/design-v2/`（Edge `--headless=new` + `--window-size=700,1150`）：

`v2-default.png` / `v2-linked.png` / `v2-empty.png` / `v2-running.png` / `v2-success.png` / `v2-failure.png` / `v2-invalid.png` / `v2-collapsed.png` / `v2-wait.png`（1500 高）/ `v2-w640.png`

### 需要用户拍板 / 待实现时核对

1. `[StepConfig]` 写入是否需要「改动 N 处」的**撤销**？本版按 R136「运行期改配置不进撤销栈」处理，只做确认式保存。
2. 运控 7 个步骤类里 `[StepConfig]` 属性的具体集合需在实现时从源码逐个核对（本设计稿按「轴回零」「轴等待」两个真实类采样）。
3. 无自定义视图的**非运控**插件是否也要显示 `[StepConfig]`？（如采集类插件）本版按 Q4 结论先只覆盖运控域。

---

## v2.1 基线（2026-10-24，待用户确认）

**版本**：v2.1（增量）。**日期**：2026-10-24。**确认人**：待用户确认（R132 验收由用户裁定）。
**文件**：`design-v2.html`（**原地升版**，自包含、内联 SVG、无外部依赖、可点击；场景可用 `?s=` 深链直接打开）。
v1 仍是**已实现基线**，v2 / v2.1 未通过前不覆盖 v1 的任何契约。v2 小节（上文）保留原样，本小节只记 v2 → v2.1 的增量。

### 覆盖范围

| 编号 | 覆盖内容 | v2 状态 | v2.1 增量 |
| --- | --- | --- | --- |
| Q4 | `[StepConfig]` 配置项在兜底面板里可编辑（代码已实现），设计上补**「配置项 vs 输入端口」语义分层** | 两段结构已有，但两段的**视觉层级只靠标题**，行内无区分 | 三重区分：①「输入参数」段顶边 2px 主色轨 + 段徽章 `🔗 可连线`；②「插件参数」段左侧 4px 中性竖条 + 段徽章 `🏷 仅本步骤`；③ 行尾图标（可连线 `🔗` / 纯常量 `🏷` / 名字类 `🔒`）。两段各配一句 inline note 说明「改完流程别处会不会变」 |
| Q5 | 封闭集合端口候选为空 → 可手填 + **分级提示** | 只画了通用「无候选，可手填」一态 | **分三级**（info / warn / error）+ **两级空候选分开说**，且提示**落到 `AutoPortRow` 这一路**（它只镜像 `IsTextEditable`，未消费 `IsHandFillFallback`，所以兜底面板里空候选时现在没有任何提示） |

### v2.1 新增的设计结论

**提示分级判据表**（实现时逐条对号；颜色只表达级别，**只有 error 会染框**）：

| 级别 | 触发条件 | 出现位置 | 文案 |
| --- | --- | --- | --- |
| info | `IsTextEditable=false` 且 `IsOptionPort=false` | 输入参数 · 图像/区域类 | 该类型不能手填，请点 🔗 由上游提供 |
| warn-A | `IsHandFillFallback` 且**候选来源未注册** | 输入参数 · 轴名 | 未检测到运动卡，可先手填轴名，接上卡后再回来选 |
| warn-B | `IsHandFillFallback` 且**来源已注册但返回空** | 输入参数 · 轴名 | 已检测到 {n} 张运动卡，但轴上未配置，请先在运动卡设置里配轴 |
| —（无提示） | `IsOptionPort=true`（候选非空） | 输入参数 · 轴名/枚举 | 无；回退成下拉 |
| error | 写回失败 / 数值解析失败 / 超范围 | 插件参数 · 数值 | 须为不小于 {min} 的整数，被拒原值：「{raw}」（+ 已恢复上一次有效值 {last}） |
| muted | `ConfigEditorKind.ReadOnly` | 插件参数 · 集合/自定义类 | 该类型暂不支持在此编辑 |

两级空候选要在 UI 上分开，前提是 `StepConfigOptionSource` 能区分「**未注册**」与「**注册了但返回空**」——现 `GetOptionItems` 两种情况都返回空列表（`Shard/Core.Interfaces/StepConfigOptionSource.cs:84` 附近），这是 Q5 提示分级的**唯一代码侧前置条件**，本基线只出设计，不改代码。

**「同一个控件两态」**（场景 2 / 3 / 4）：空候选是「可手填输入框 + 提示」，候选恢复后**同一位置**变回下拉、提示消失。设计上明确**不新增控件类**：`IsHandFillFallback` 与 `IsOptionPort` 互斥即可表达，WPF 复刻时不要造第二个编辑器。

**只读项不假装能编辑**（场景 6）：虚线只读框显示当前值摘要（`List<AxisMapEntry> · 3 项`），下方 10.5px 灰字「该类型暂不支持在此编辑」，行尾只读锁；**不画灰掉的正常输入框**（灰框会让用户反复点）。

### 状态覆盖表（v2.1）

| 场景 | `?s=` | 内容 | 验证 |
| --- | --- | --- | --- |
| 1 默认（有卡有轴） | `default` | 「轴回零」真实参数：输入参数 3 项（轴号 Axis 必填名字类 / 目标位置 Target=NaN mm / 到位容差 Tolerance=0.05 mm）+ 插件参数 3 项（回零方式 Mode=枚举 8 项「负限位 + Index」/ 超时 TimeoutMs=120000 / 回零速度 SpeedPercent=30.0 %）；两段层级可辨 | DOM 断言 + 截图 `default.png` |
| 2 空候选-无来源 | `noc` | 轴号 → 可手填输入框（占位「手填轴名」）+ 橙色 ⚠「未检测到运动卡，可先手填轴名，接上卡后再回来选」；页脚「有 1 处需注意（空候选）」 | 同上 `noc.png` |
| 3 空候选-有来源但空 | `nocfg` | 同位置 → 可手填 + 橙色 ⚠「已检测到 2 张运动卡，但轴上未配置，请先在运动卡设置里配轴」 | 同上 `nocfg.png` |
| 4 候选恢复 | `restore` | **同一行**变回下拉（X/Y/Z/4），提示消失；页脚绿色「候选已恢复（下拉）」 | 同上 `restore.png` |
| 5 校验错误 | `invalid` | 超时行染**外圈**红（内边框透明，未染底色）+ 行内红 ⛔「须为不小于 1000 的整数，被拒原值：「12a」」+ 灰字「已恢复上一次有效值 120000 ms」；页脚红胶囊「有 1 处参数非法，无法确认」，确认禁用 | 同上 `invalid.png` |
| 6 只读态 | `readonly` | 插件参数区多出「轴映射表 AxisMap / 集合」虚线只读框 + 灰字「该类型暂不支持在此编辑」+ 只读锁 | 同上 `readonly.png` |
| 7 640 宽对照 | `w640` | 窗口变 640，行内说明文字回到单行；产品仍固定 560 | 同上 `w640.png` |
| 附：加载态 | `loading` | 主色浅底「正在读取插件参数…（`_plugin.Inputs` / `[StepConfig]` 反射）」+ 两条骨架行（shimmer 1.1s，属「持续进行」不占 150~200ms 额度）；状态胶囊「加载中」+ 确认/取消禁用 | 同上 `loading.png` |
| 附：空态 | `empty` | 虚线卡片 + 图标 + 「该步骤没有可编辑参数」+ 下一步说明；确认/取消仍可用 | DOM 断言 |

**五态**（R115）：输入框 / 下拉 / 主按钮 / 图标按钮 / 开关均为 默认 · 悬停 · 按下 · 聚焦 · 禁用；聚焦态 = 主色边 + 3px 主色光环（WPF 为 `DialogFocusVisual` 2px `DialogAccentBrush` 圆角描边）。设计稿内置「五态对照表」，点 `显示五态对照 →` 展开。
**动效**（R116）：`--ap-t-fast:150ms` / `--ap-t-slow:200ms`，无第三个时长；骨架 shimmer 1.1s 仅表达持续进行。
**垂直节奏**：间距刻度 `--ap-s1..s6 = 4/8/12/16/20/24`，全部 4 的倍数；行高 48、控件 36（布尔 36 / 枚举下拉 36）、`EditorShell` 38。

### 令牌映射（v2.1 增量）

v2.1 未新增任何颜色令牌：仍用 v1/v2 同一组 `--ap-*`，映射表见 design-v2.html 末尾「令牌映射」（CSS 变量 → WPF 资源键 → 用在哪）。本版**新增的语义**（不新增键）：

| 用途 | 引用令牌 | WPF 资源键 |
| --- | --- | --- |
| 输入参数段顶边轨 | `--ap-primary-border` | `DialogAccentBorderBrush` |
| 插件参数段左竖条 | `--ap-border-strong` | `DialogBorderStrongBrush` |
| 可连线段徽章文字/底 | `--ap-primary-text` / `--ap-primary-soft` | `DialogAccentTextBrush` / `DialogAccentSoftBrush` |
| warn 级提示（两级空候选） | `--ap-warning-text` / `--ap-warning` | `DialogWarningTextBrush` / `DialogWarningBrush` |
| error 级提示 + 错误外圈 | `--ap-danger-text` / `--ap-danger` | `DialogDangerTextBrush` / `DialogDangerBrush`（与 `ShowConfigRowError` 同义） |
| info 级提示 | `--ap-text-2` / `--ap-primary` | `DialogTextSecondaryBrush` / `DialogAccentBrush` |
| 只读值文字 | `--ap-text-4` | 仓内无独立键，实施用 `ScadaSecondaryTextBrush` 近似（与 v1 同口径） |

**R140 自查**：`design-v2.html` 全文 `#RRGGBB` 字面量只出现在 `#ap-root{}` 块内（29 个颜色变量，行 100–131），块外正则 `#[0-9A-Fa-f]{3,8}` 命中 **0** 处；令牌映射表里的色块用 `style="background:var(--ap-*)"` 引用，不写 hex。

### 已覆盖状态（与代码的对应关系）

| 设计点 | 代码现状 | 是否需改代码 |
| --- | --- | --- |
| 两段结构、「输入参数」/「插件参数」标题与 hint | `AutoPortConfigView.xaml` 已实现 | 否（hint 文案按本版微调即可） |
| 校验错误只染外圈 + 行内中文原因 | `ShowConfigRowError` 已实现（只改 `EditorShell.BorderBrush/Thickness`，不染底色） | 否 |
| 只读项文案与位置 | `CreateEditor` 的 `ConfigEditorKind.ReadOnly` 已实现 | 否 |
| 段徽章 / 顶轨 / 左竖条 / 行尾图标 | 未实现 | **是**（纯视觉，不改行为） |
| 空候选两级提示（warn-A / warn-B） | `LinkableValueEditor` 只有一句通用 `PART_NoOptionHint`；**`AutoPortRow.xaml.cs` 未消费 `IsHandFillFallback`** | **是**（Q5 的主缺口） |
| 「未注册来源」与「已注册但返回空」可区分 | `StepConfigOptionSource.GetOptionItems` 两种情况同为返回空 | **是**（分级文案的前置条件） |

### 已知偏差（v2.1）

1. **样本只取运控域**：同 v2 待拍板 #3，非运控插件（采集类等）无自定义视图时是否显示 `[StepConfig]` 未定，本稿不覆盖。
2. **Web 的 `outline` 与 WPF 的 `DialogFocusVisual` 形状不同**：Web 用 `outline`/`box-shadow`，WPF 是 2px 主色圆角描边（`Margin 2`，`CornerRadius 6`）。视觉意图一致，像素不硬套（R129 允许项）。
3. **骨架 shimmer 1.1s** 超出 R116 的 150~200ms。理由：它表达「持续进行」而非状态切换；WPF 复刻时若不便做 shimmer，可退化为静态三条浅灰条 + 状态胶囊，**必须在差异清单里写明**。
4. **`?s=` 深链与场景按钮是本稿的评估辅助**，不是产品功能；WPF 侧不实现（R126：Web 产物是设计媒介）。
5. **`--ap-text-4` 无独立资源键**：沿用 v1 的 `ScadaSecondaryTextBrush` 近似，未新增令牌（避免绕过 R140 豁免表）。
6. **空候选提示的实际位置**：本稿把两级提示画在编辑框**正下方**（行内扩展，行高临时 48→更高）。WPF 里 `AutoPortRow` 行高当前是固定节奏，落地时需允许该行向下长高，**不改其它行的行高**。

### 截图证据（v2.1）

生成于 `artifacts/render/20261024/design-v2.1/`（Edge `--headless=new`，`--window-size=1180,1100`，`--virtual-time-budget=1800`）：

`default.png` / `noc.png` / `nocfg.png` / `restore.png` / `invalid.png` / `readonly.png` / `w640.png` / `loading.png`

场景 2/3/4 的「同一个控件两态」已用 DOM 断言钉住：`noc`/`nocfg` 下轴号行为 `<input data-handfill>` 且带 `ap-hint is-warn`（各 1 处、下拉 0 处），`restore`/`default` 下为 `<select aria-label="轴号">` 且 `data-handfill` 0 处、`is-warn` 0 处。

### 需要用户拍板（v2.1 新增）

1. **两级空候选的区分口径**：是靠 `StepConfigOptionSource` 返回 `IReadOnlyList` + 「是否注册」查询（需扩接口），还是让 provider 自己返回「空 + 原因」（`StepConfigOptionKind` 维度的结果类型）？设计稿只定文案与位置，不定接口形态。
2. **warn-B 里的卡数量 `{n}`**：显示「已检测到 2 张运动卡」是否泄露过多设备细节？备选：「已检测到运动卡，但轴上未配置」。
3. **提示是否需要「去设置」跳转按钮**：本稿（场景 3）只给文字，未放按钮——放按钮要不要新开设置窗口/切到通讯设置面板，牵动 R47「没有面板落点的命令不许进活动栏」。默认**不放**。
4. **只读项是否给出「在插件自带视图里编辑」的入口**：目前只写灰字。若插件本身无视图，该文案会误导，需要按「有无自定义视图」分两套文案。

## v3 基线（2026-10-24，已复刻）

> **本版推翻了 v2/v2.1 的一条核心假设。** v2 把「方案里还没有候选」当成**空态**，于是允许手填（`无候选，可手填`）。用户当场否决：
>
> > 「这个框架提供的轴清单 为什么要可以手填呢」（2026-10-24）
> > 「成 bug 直接红着脸报错」（2026-10-24）
>
> 结论：轴名 / 卡名 / 通讯变量名 / 子流程名这类**由运行期方案决定的名字**，候选只有一个权威来源；
> 取不到候选不是"让用户自己敲"，而是**程序没接好**。v3 按此重画，并且已经落进 WPF 代码。

| 项 | 内容 |
| --- | --- |
| 版本 | v3 |
| 日期 | 2026-10-24 |
| 设计文件 | `design-v3.html`（自包含、内联 SVG、无外部依赖） |
| 覆盖范围 | `AutoPortConfigView` + `AutoPortRow` + `LinkableValueEditor` + `OptionSourceGenerator`（属性面板同源） |
| 复刻状态 | **已复刻并验证（2026-10-24）**；`tools/run_gates.ps1` 13 步全绿 |

### 一句话差异

| 场景 | v2.1 | v3（本版） |
| --- | --- | --- |
| 该类别没登记候选来源 | 空下拉 → 退化成可手填 TextBox + 灰字「无候选，可手填」 | **红框 + 缺陷说明 + 复制诊断**，输入框折叠，不可手填 |
| 已登记但方案里没有可用项（如没配运动卡） | 同上 | 灰底空态 + 原因 + 「去运动卡设置 →」入口，**仍不可手填** |
| 取候选时来源抛异常 | 与"没配卡"长得一模一样（异常被 `catch { }` 吞掉） | 单独一档：**红框 + 异常类型与消息** |
| 流程里存的名字当前已不存在 | 无专门提示 | 红框保留原值 +「该轴「Z4」当前不存在，请重新选择」，不自动改写 |
| 普通字符串端口（用户自己的文本） | 可手填 | **仍可手填**（`OptionKind == null` 不动） |

### 三档判据（唯一事实源：`StepConfigOptionResult.Failure`）

```text
NoProvider    → 缺陷：宿主没为该 Kind 登记来源     → 红框 + 「程序缺陷…请反馈给开发」
ProviderThrew → 缺陷：来源抛异常（异常文本已收集） → 红框 + 「程序缺陷…{ExType}: {Message}」
EmptyList     → 正常：来源已登记但当前没有可用项   → 灰底 + 「先到「运动卡设置」配好卡与轴」
Ok            → 有候选：禁用编辑的下拉            → 正常行
```

`IsDefect = (NoProvider || ProviderThrew)`。**缺陷态不阻断确认**——不改这个字段也能跑流程，
但页脚与指标胶囊要把缺陷数显出来（本版先落「红框 + 说明」，页脚计数留待后续）。

### WPF 复刻改动清单（2026-10-24 已完成）

| # | 文件 | 改动 |
| --- | --- | --- |
| 1 | `Shard/Core.Interfaces/StepConfigOptionSource.cs` | 新增 `StepConfigOptionFailureKind` / `StepConfigOptionResult`（含 `HasProvider` / `ProviderErrors` / `Failure` / `IsDefect`）与 `GetOptionsResult(kind)`；**异常不再静默吞掉**，收集为 `ExType: Message` |
| 2 | `Shard/Core.Interfaces/StepConfigPropertyDescriptor.cs` | 新增 `OptionsKind`（把 `[StepConfigOptions]` 的 Kind 带到描述符上——此前 `AutoPortConfigView` 读不到特性） |
| 3 | `Shard/Core.Interfaces/VisionPluginBase.cs` | `GetConfigProperties()` 构造描述符时传 `GetCustomAttribute<StepConfigOptionsAttribute>()?.Kind` |
| 4 | `Shard/Core.Controls/LinkableValueEditor.xaml.cs` | 新增只读 DP `IsOptionEmpty` / `IsOptionEmptyDefect` / `OptionEmptyReason`；`IsHandFillFallback` **恒为 false**；`OnPortValueChanged` 在 `IsOptionEmpty` 时不写回 |
| 5 | `Shard/Core.Controls/LinkableValueEditor.xaml` | `PART_NoOptionHint`（「无候选，可手填」）整段删除，换成两行空态块 `PART_OptionEmpty` / `PART_OptionEmptyText`；`PART_ValueBox` 增加 `IsOptionEmpty → Collapsed` 触发器 |
| 6 | `UI/Controls/CustomControl/PropertyGrid/Generators/OptionSourceGenerator.cs` | `IsEditable = false`；无候选时禁用并出空态模板；绑定由 `ComboBox.TextProperty` 改 `Selector.SelectedValueProperty` |
| 7 | `VisionMaster/Views/DialogViews/AutoPortConfigView.xaml.cs` | `string` + `OptionsKind` 走新的 `CreateOptionEditor`（不可编辑下拉 + 空态说明行）；`BuildConfigRow` 保证顺序 shell → 空态说明 → error |
| 8 | `VisionMaster/Views/DialogViews/AutoPortRow.xaml.cs` | 构造 `LinkableValueEditor` 由 `Height=36 + VerticalAlignment.Center` 改 **`MinHeight=36 + Stretch`**（否则空态两行文案被 36 高裁掉） |

### 落地时踩到的两个布局坑（已在代码里修，留给后续参考）

1. **`PART_OptionEmpty` 只有 36 高、文案自身要 64 高** → 文字下半截被裁。根因不在 XAML 内层，而是
   `AutoPortRow` 构造时 `new LinkableValueEditor { Height = 36 }` 把整个编辑器锁死在 36。
   **判据：量 `PART_OptionEmptyText` 的 `ActualHeight` 与祖先链每层的 `Height`**，不要看截图猜。
2. **锁定符与图标渲染成豆腐块** —— 那是断言探针自己的问题（pack URI 字体族名拼错），真机不受影响。

### 验证（2026-10-24）

| 层 | 证据 |
| --- | --- |
| 编译 | `VisionMaster` / `Core.Interfaces` / `FlowCanvasChecks` 各 0 错误 |
| 断言宿主 | `FlowCanvasChecks` **3628 通过 / 0 失败**；`PortBindChecks.RunEmptyOptionFallback` 6 条覆盖空候选不手填、空态必给原因（反向）、手打名字进不了端口（反向）、仍不许链接（R33）、有候选时写回成员名（R40）、候选清空后端口值不被抹（R35） |
| 全量闸门 | `tools/run_gates.ps1` → **`OK - 13 steps green`** |
| 真实算子面板 | 新增探针 `tools/probes/_AutoPanelProbe`：扫 `Modules\` 找**所有不实现 `IPluginCustomViewProvider`** 的算子（19 个），逐个 `new AutoPortConfigView` 出图。产物 `tools/probes/_AutoPanelProbe/bin/Debug/net9.0-windows/auto-panel/` |

### 19 个算子的面板抽查结论

空候选行（说明该类别 Host 侧没登记来源或没配卡）出现的 8 个：**运控 7 个（等待到位 / 读轴状态 / 轴使能 / 轴停止 / 轴卡 IO / 轴回零 / 轴运动）、通讯 1 个（立刻读；立刻写同理）、流程控制 1 个（调用流程）**。
其余（几何查找 / 几何计算 / 图像差分 / 图像赋值 / 延时 / 数学计算 / 类型转换 / 数值比较 / 逻辑运算）无空候选行，排版正常。

**这 8 个在真机上是缺陷态还是空态，取决于运行期是否注册了来源**：`MotionModule.RegisterStepConfigOptions`
只列**已启用**的轴、`CommunicationModule` 只处理通讯两类 Kind、`SubroutineFlowOptions` 只管子流程。
探针进程没有这些模块，所以一律显示 NoProvider —— 这正说明**探针能如实暴露"模块没起来"这件事**。

### 已知偏差

1. 插件参数区显示英文属性名（`Mode` / `TimeoutMs` / `Algorithm`…）——**插件类没写 `[Display(Name)]`**，不是面板缺陷。
2. `LinkableValueEditor.xaml` 内仍是内联色值（`#F8FAFC` / `#CBD5E1` / `#94A3B8` / `#C02B2B`）。该文件在 `Core.Controls`，
   引用不到 UI 库令牌（R140 存量，新增的两处沿用同文件既有写法，未扩大）。
3. 缺陷态暂不显示「复制诊断」按钮与页脚「缺陷 N」计数——文案已给出可读原因，按钮待下一批做（点开复制的是 `ProviderErrors` 全文）。
4. `[StepConfig]` 集合/自定义类仍以只读框呈现（「该类型暂不支持在此编辑」），v3 未改。
