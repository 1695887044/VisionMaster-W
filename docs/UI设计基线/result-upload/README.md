# 结果上报配置窗口 · 设计基线

| 项 | 内容 |
| --- | --- |
| 版本 | v1 |
| 设计日期 | 2026-10-20 |
| 确认日期 / 人 | 待用户确认 |
| 覆盖范围 | `Plugins\Plugin.ResultUpload\ResultUploadView.xaml` + `.xaml.cs`（插件内容区）；宿主外壳 `PluginConfigShellView.xaml` 仅作 960×680 / 640×560 参考，不改 |
| 宿主尺寸 | 默认 960×680；最小 640×560；插件内容区约 920×520 / 600×400 |
| 设计文件 | `design.html`（v1，自包含、无外部依赖、可点击、内联 SVG） |
| 复刻状态 | 已完成（2026-10-20）；初始态证据见 `docs/UI设计基线/_batch-evidence/Plugin.ResultUpload-ResultUploadPlugin.png`；设计仍待用户确认 |
| 参考实现 | `docs/UI设计基线/image-acquisition/` v2 + `docs/code-changes/2026-10-20-图像采集配置窗口V2复刻与FluentChipDanger重复键修复.md` |

## 定位

按 `docs/UI设计基线/插件自定义视图Web化改造SOP.md` §5.2：本文件是**第 ② 步（Web 设计基线）**的产物。
Web 只是设计媒介，**不进入构建、不部署**。确认后按本基线复刻 WPF 视图；验收以「同一屏、同一状态」的截图对照为准。

本插件**没有 HALCON 画布、没有配置态预览图**：结果上报只在运行期 POST JSON，
配置期能预览的是**报文文本**（`PayloadPreview`）。因此本基线把右侧主工作区留给
「字段表 + 模板 + 实际发出 + 测试发送」，不伪造图像预览面板。

## 现状审查（改动理由）

| # | 现状（改动前） | 影响 |
| --- | --- | --- |
| 1 | 四张卡片纵向堆叠在单个 `ScrollViewer`（报文预设 / 上传内容 / 发送策略 / 测试发送），570 行 | 窗口一矮就要上下翻；字段表、预览、测试发送三者互相挤，常用路径被拉长 |
| 2 | 字段表是 5 列窄行（24 / 92 / 96 / * / 26），整页只有 540px 设计宽 | 字段名与取值列被压得很窄；来源=变量/内置的输入框几乎不可读 |
| 3 | 报文预览是浅蓝小方块（`#F5F9FE`），与日志、结果文本混在卡片里 | 预览是"实际发出去的 JSON"，应有独立工作区与等宽字体，现在像一条提示 |
| 4 | 连接参数（URL / 超时 / 请求头 / 加签 / 证书）与发送策略混在一起 | 生产参数和调试项没有分层；请求头列表长期占屏 |
| 5 | 测试发送、结果、最近日志分散在卡片底部，`LastResult` 无成功/失败底色 | 「发没发出去、为什么失败」要读三处文字才能拼出来 |
| 6 | 视图内多处硬编码颜色：`#C62828` / `#2E7D32` / `#F5F9FE` / `#FAFAFA`，转换器里还有 `0xC62828` 等 | 违反令牌化纪律；换肤/统一调整改不动 |
| 7 | 行内删除按钮是裸 `Button` + 11 号字，无固定格子 / 无 Padding | 命中区小，且与 V2 的 30×30 图标按钮节奏不一致 |

## v1 方向

> **左配置、右报文；字段表是主角，测试发送是闭环。**
> 只做纯 UI 重构，不改端口名、命令名、持久化与业务逻辑。

1. **两栏固定分工**：左 356px 配置轨（报文预设 + 目标与连接 + 发送策略，可滚动），右侧报文工作区自适应。
2. **报文预设是卡片**：4 个 `RadioButton` 2×2 卡片（MES 字段表 / 钉钉文本 / 企业微信 / 原始 JSON），`IsChecked` 仍走 `EnumConverter` 写 `PayloadKind`。
3. **字段表独占宽度**：5 列改为「启用 22 / 字段名 1fr / 来源 74 / 取值 1fr / 删除 26」，来源=端口显示「画布上连线供值」，变量/内置显示各自输入。
4. **报文预览独立**：深色代码工作区（`PluginPreviewCanvasBrush`）+ 等宽字体；组报文失败时红色显示「无法预览：原因」。
5. **连接参数分层**：「目标 URL / 超时」常驻；请求头、钉钉加签、证书校验收进「高级连接」折叠区。
6. **测试发送闭环**：`IsTesting` 置灰 + 「正在发送…」；`LastResult` 成功绿 / 失败红；`RecentLogs` 最近 50 条与刷新按钮同卡。
7. **全令牌化**：颜色映射 `Colors.xaml` / `PluginConfigColors.xaml`；只在本视图派生样式，不改共享字典。
8. **五态与可达性**：hover / pressed / focus / disabled 全量；图标按钮 30×30 且 `Padding="0"`（R68），焦点环 `DialogFocusVisual`，ToolTip `DialogToolTipStyle`。

## 设计要点（完整清单）

1. 左侧顺序：报文预设 → 目标与连接（URL / 超时 / 高级连接）→ 发送策略（时机 / 条件块 / 队列 / 重试 / 续传）。
2. 报文预设卡片：选中 = 主色描边 + 主色浅底 + 右侧对勾；未选中 = 白底 + 浅描边；悬停加深描边。
3. URL 常驻：占位符 `https://…`；帮助文字说明 MES / 钉钉 / 企微三种地址形态。
4. 超时：数字输入 + `ms` 后缀；帮助文字写明默认 3000、≤0 会被插件拉回 3000。
5. 高级连接：`details` 折叠，内含请求头（Key / 值打码 / 删除 / 添加）、钉钉加签密钥、跳过证书校验 + 风险提示。
6. 发送时机：下拉；选「触发端口」显示 `TriggerPort` 的 `LinkableValueEditor`；选「仅 NG」显示判定字段（可编辑下拉，候选 = 启用字段名）+ NG 值。
7. 队列与失败处置：异步队列 / 失败阻断两个复选；重试次数（0~10）+ 成功关键字并排；断网续传复选。
8. 字段表头：启用 / 字段名 / 来源 / 取值 / 删除；行高 30；禁用行整行 55% 透明度但配置保留。
9. 来源=端口：取值列显示「画布上连线供值」；来源=变量：显示变量名输入；来源=内置：显示时间戳 / 步骤名下拉。
10. 空字段态：虚线卡「还没有启用字段，至少添加一个字段才有东西可上报」；添加按钮始终可见。
11. 模板区：通知模式显示「通知内容模板」；原始 JSON 模式显示「JSON 模板」；帮助文字列出占位符规则。
12. 时间格式 + UTC：始终显示，影响内置时间戳与 `{时间}` 占位符。
13. 报文预览：深色工作区，顶部一行目标 URL + 报文类型；内容等宽、自动换行；错误态红色。
14. 测试发送：主按钮「立即发送」；发送中置灰；结果区带成功/失败底色；日志行 = 时间 / 级别 / 消息，级别着色走令牌。
15. 窄档 640：左栏 300px；字段表列宽收紧；日志时间列从 104 收到 86；卡片提示文字隐藏。
16. 底部宿主栏：状态胶囊 + 耗时 + 执行 / 取消 / 确认；插件视图不改它。

## 状态清单（design.html 可点击）

| 状态 | 触发 | 界面表现 |
| --- | --- | --- |
| 默认 | 打开配置窗口 | MES 字段表；sn / result 走端口、time 走内置时间戳；URL 已填；预览为三字段 JSON；日志显示最近三条 |
| 发送中 | 点「立即发送」 | 按钮置灰显示「发送中…」；结果区「正在发送…」；日志追加 INFO |
| 成功 | 发送返回 2xx 且命中成功关键字 | 结果区绿色「✔ HTTP 200（业务成功）」；日志 INFO |
| 失败 | 发送返回 5xx / 网络错误 | 结果区红色「✘ HTTP 500 …」；日志 ERROR |
| 空 URL | 边界态「空 URL」 | URL 清空；预览仍可看；点发送返回「✘ 请先填目标 URL」 |
| 空字段 | 边界态「空字段」 | 字段表虚线空态；预览红色「无法预览：字段表没有启用的字段…」 |
| 通知 / 原始 JSON | 点对应预设卡片 | 字段表变值池；显示通知模板或 JSON 模板；预览随之切换 |

> 这些状态全部来自插件现有属性 / 行为（`PayloadKind` / `SendTiming` / `IsTesting` / `LastResult` / `PayloadPreview` / `Fields`），
> 不是设计稿自造的业务状态。本插件**没有** `StatusLevel` / `StatusMessage`，设计稿不伪造状态栏。

## 契约保留（一个都不能改）

| # | 契约 | 为什么不能动 |
| --- | --- | --- |
| C1 | `DataContext = 插件实例` + `IPluginConfigView` | 宿主靠 `viewObj.DataContext as IPluginConfigView` 取 `OnConfirm`；换独立 VM = 确认不保存 |
| C2 | `TriggerPort` 的 `LinkableValueEditor`（`Port` / `StepData`） | 端口链接、解除链接、变量绑定弹窗都在控件里；只在「触发端口」时机显示 |
| C3 | `Fields` / `Headers` 两个 `ObservableCollection` | 字段名/来源/启用变化会触发 `RebuildDynamicInputs()`（动态输入端口）；换集合 = 端口表不同步 |
| C4 | 枚举与 int 类型 | `PayloadKind` / `SendTiming` / `FieldSource` / `BuiltInField` 是枚举；`TimeoutMs` / `RetryCount` 是 int，setter 夹取规则不变 |
| C5 | `TestSend()` / `IsTesting` / `LastResult` / `RecentLogs` | 测试发送与日志回看的唯一入口；不新增命令 |
| C6 | `PasswordBoxHelper.BoundPassword` | 请求头值仍打码显示；持久化仍是明文随方案保存（既有拍板） |

## 令牌映射

| 设计变量 | 值 | WPF 资源键 |
| --- | --- | --- |
| `--ru-surface` | `#FFFFFF` | `DialogSurfaceBrush` |
| `--ru-surface-2` | `#F8FAFC` | `DialogRowAlternateBrush` |
| `--ru-surface-3` | `#F1F5F9` | `DialogRowHoverBrush` |
| `--ru-border` | `#E2E8F0` | `DialogBorderBrush` |
| `--ru-border-soft` | `#EEF2F7` | `DialogBorderLightBrush` |
| `--ru-border-strong` | `#CBD5E1` | `DialogBorderStrongBrush` |
| `--ru-text` | `#0F172A` | `DialogTextPrimaryBrush` |
| `--ru-text-2` | `#475569` | `DialogTextSecondaryBrush` |
| `--ru-text-3` | `#94A3B8` | `DialogTextMutedBrush` |
| `--ru-text-4` | `#64748B` | `ScadaSecondaryTextBrush`（`#66708A`，近似） |
| `--ru-primary` | `#0EA5E9` | `DialogAccentBrush` |
| `--ru-primary-strong` | `#0284C7` | `DialogAccentDarkBrush` |
| `--ru-primary-text` | `#0369A1` | `DialogAccentTextBrush` |
| `--ru-primary-soft` | `#F0F9FF` | `DialogAccentSoftBrush` |
| `--ru-primary-light` | `#E0F2FE` | `DialogAccentLightBrush` |
| `--ru-primary-border` | `#BAE6FD` | `DialogAccentBorderBrush` |
| `--ru-success` | `#22C55E` | `DialogSuccessBrush` |
| `--ru-success-text` | `#15803D` | `DialogSuccessTextBrush` |
| `--ru-success-soft` | `#F0FDF4` | `DialogSuccessSubtleBrush` |
| `--ru-success-border` | `#BBF7D0` | `DialogSuccessBorderBrush` |
| `--ru-danger` | `#EF4444` | `DialogDangerBrush` |
| `--ru-danger-text` | `#B91C1C` | `DialogDangerTextBrush` |
| `--ru-danger-soft` | `#FEF2F2` | `DialogDangerSubtleBrush` |
| `--ru-danger-border` | `#FECACA` | `DialogDangerBorderBrush` |
| `--ru-warning` | `#F59E0B` | `DialogWarningBrush` |
| `--ru-warning-text` | `#B45309` | `DialogWarningTextBrush` |
| `--ru-warning-soft` | `#FFF4E5` | `DialogWarningSubtleBrush` |
| `--ru-warning-border` | `#FDE68A` | `DialogWarningBorderBrush` |
| 代码预览底 / 面 / 边 / 字 / 弱字 | `#0B1220` / `#1E293B` / `#334155` / `#E2E8F0` / `#94A3B8` | `PluginPreviewCanvasBrush` / `PluginPreviewSurfaceBrush` / `PluginPreviewBorderBrush` / `PluginPreviewTextBrush` / `PluginPreviewMutedTextBrush` |
| 等宽字体 | Cascadia Mono / Consolas | `PluginMonoFontFamily` |
| 图标字体 | — | `DialogIconFont` |
| 圆角 6/8/10/12/16 | — | WPF `CornerRadius` 字面量（仓内暂无圆角令牌字典） |
| 间距 4/8/12/16/20/24 | — | WPF `Margin` / `Padding` 字面量 |

**缺失令牌：无。** 本插件所需语义（表面 / 描边 / 文字 / 主色 / 成功 / 危险 / 警示 / 深色预览）全部已存在于
`UI\Controls\Themes\Colors.xaml` 与 `UI\Controls\Themes\PluginConfigColors.xaml`；本视图不新增、不修改任何公共键。
视图 XAML 静态扫描 **hex=0**。

## 复刻实现映射（WPF）

| 设计项 | WPF 实现 |
| --- | --- |
| 2×2 报文预设卡片 | 4 个 `RadioButton` + `ModeCardStyle`；`IsChecked` 走 `ui:EnumConverter` 写 `PayloadKind` |
| 两栏布局 | 左列 `1*`（280~340）+ 右列 `1.75*`（MinWidth 280）；左栏 `ScrollViewer` |
| 字段表 | `ItemsControl` + 5 列 `Grid`；`Enabled` / `Name` / `Source` / `VariableName` / `BuiltIn` 原绑定 |
| 请求头 | `ItemsControl` + `DialogCellInput` + `PasswordBox`（`PasswordBoxHelper.BoundPassword`） |
| 高级连接 | `Expander` 包住请求头 / 加签 / 证书三项 |
| 报文预览 | `Border`（`PluginPreviewCanvasBrush`）+ `ScrollViewer` + 等宽 `TextBlock` 绑 `PayloadPreview` |
| 测试发送 | `Button`（`IsEnabled=!IsTesting`）+ `TextBlock x:Name=ResultText` 绑 `LastResult` + `ListBox x:Name=RecentLogsList` |
| 加签密钥 | `PasswordBox` + `PasswordBoxHelper.BoundPassword`（`SignSecret` 仍是 string，持久化不变） |
| ToolTip | 全部 27 处统一 `ToolTip Style="{StaticResource DialogToolTipStyle}"`，无裸 `ToolTip="..."` |
| 状态色 | 代码后置从 `Application.Current.TryFindResource` 取 `DialogSuccessTextBrush` / `DialogDangerTextBrush` / `DialogTextMutedBrush` |
| 五态 | 本地 `LocalButtonStyle` / `ModeCardStyle` / `IconButtonStyle` 模板触发器；焦点 `DialogFocusVisual`；ToolTip `DialogToolTipStyle` |

## 已知偏差（复刻时按 R130 记录）

1. **图标字体**：Web 用内联 Lucide SVG；WPF 用 Font Awesome 6 Pro Solid（`DialogIconFont`），形状不同、语义一致。
2. **字体度量**：同字号下 Segoe UI / 微软雅黑与 Web 的字宽、行高略有差异，不按 px 硬套。
3. **阴影/动效**：Web 窗口阴影为 CSS 多层阴影；WPF 用宿主现有 `DropShadowEffect`；本视图不新增动效。
4. **折叠区**：WPF 用 Fluent 默认 `Expander` 头（外加卡片边框），未复刻 Web 的自绘 `details` header；展开/收起行为一致。
5. **字段表宽度**：Web 在 960 下可显示完整 5 列；WPF 在 640 最小档把字段名列与取值列压到 `MinWidth`，必要时横向信息略紧，但无横向滚动。
6. **日志列表**：Web 用等宽行；WPF `ListBox` 默认容器有内边距，行高略大，日志区固定 74~96px 显示约 3~4 条。
7. **请求头值 / 加签密钥**：WPF 用 `PasswordBox`，Web 用 `type=password`，打码行为一致；两者持久化仍是明文随方案保存（既有拍板）。
8. **报文预览高亮**：Web 有 JSON 关键字着色；WPF 使用单色等宽文本（不新增语法高亮器，见 D3）。
9. **测试发送结果**：Web 用模拟数据；WPF 显示真实 `LastResult`（`TestSend()` 后台线程结果），颜色由代码后置按 `✔` 前缀切换。
10. **空字段态**：WPF 仍显示空 `ItemsControl` + 虚线提示卡；添加字段后自动消失（由 `Fields.Count` 触发器控制）。

## 未纳入本版（记录，不改）

| # | 项 | 需要什么 |
| --- | --- | --- |
| D1 | 加载骨架 / 发送进度条 | VM 新增 `IsPreviewLoading` / 进度属性；本版只用 `IsTesting` 置灰 |
| D2 | 字段拖拽排序 | 新的拖拽行为与集合重排逻辑 |
| D3 | 模板语法高亮 | 自绘编辑器；本版用等宽文本框 |
| D4 | 日志导出 / 清空 | 新命令 |
| D5 | 请求头导入 / 导出 | 文件 IO 与新持久化格式 |
| D6 | 连通性探测 | 新的网络行为；本版用「立即发送」真实验证 |

## 复审修复（M1-M3，2026-10-20）

| 项 | 修复 |
| --- | --- |
| M1 | 26 处裸 `ToolTip="..."` 全部改为 `<ToolTip Style="{StaticResource DialogToolTipStyle}" .../>`；当前共 27 处（含原有 URL 一处），裸属性 0 |
| M2 | `SignSecret` 由明文 `TextBox` 改为 `PasswordBox` + `PasswordBoxHelper.BoundPassword`；属性仍是 `[StepConfig] string`，持久化不变 |
| M3 | 插件在 `Fields` setter / `OnFieldDefPropertyChanged` / `OnFieldsCollectionChanged` 补 `OnPropertyChanged(nameof(PayloadPreview))`；改字段名/来源/增删字段后预览即时刷新（运行期验证 `RESULT: PASS`） |
| M4 | 插件专属断言仍为遗留；`UIThemeSmokeTest` 共享文件未改，本插件只跑通用 `--plugin-views`（11/11） |

## 证据

| 项 | 结果 |
| --- | --- |
| 设计稿 | `design.html`（960 / 640 两档；默认 / 发送中 / 成功 / 失败；标准 / 空 URL / 空字段；四种报文预设可点击） |
| 初始态渲染 | `docs/UI设计基线/_batch-evidence/Plugin.ResultUpload-ResultUploadPlugin.png`（`--plugin-views Plugin.ResultUpload` 生成） |
| 真机 | 待用户验收：打开配置 → 改每个字段 → 确认 → 重开；测试发送成功/失败；日志刷新 |

## 变更规则

设计基线确认后只读。需要调整设计时升版本（v2），并同步更新已复刻的 `ResultUploadView.xaml` 与本映射表。
