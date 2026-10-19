# 图像采集配置窗口 · 设计基线

| 项 | 内容 |
| --- | --- |
| 版本 | v1 |
| 确认日期 | 待用户确认 |
| 确认人 | 待用户确认 |
| 覆盖范围 | `Plugins\Plugin.ImageAcquisition\ImageAcquisitionView.xaml`（插件内容区）+ `VisionMaster\Views\DialogViews\PluginConfigShellView.xaml`（960×680 宿主外壳） |
| 窗口尺寸 | 960×680 默认；640×680 窄档；宿主最小 640×420 不在本版覆盖范围（见「复刻前置」第 6 条） |
| 设计基线文件 | `design.html`（自包含，内联 SVG 图标，浏览器直接打开） |

## 定位

按 `AGENTS.md` §30.1 的四步流程：本文件是**第 ① 步（Web 设计基线）**的产物。
Web 只是设计媒介，**不进入构建、不部署**（R126）。确认后按本基线做第 ③ 步（WPF 复刻），
第 ④ 步以「同一屏、同一状态」的截图对照验收（R129/R132）。

## 现状审查（改动理由）

| # | 现状 | 影响 |
| --- | --- | --- |
| 1 | `TabControl` 两级页签：「基本参数 / 图像参数」，后者只有一个「显示窗体」下拉（`ImageAcquisitionView.xaml:63/383-421`） | 信息架构失衡，为一个下拉单开一页 |
| 2 | 采集模式用 4 个 `RadioButton` + `GroupBox`，上方固定 `Height="140"` 行（`:70-83`） | 默认窗口下大量留白；窄窗口换行后高度不可控；形态陈旧 |
| 3 | 预览区只有 `ImageReadOnly` + 一行状态文字（`:424-478`） | 无文件信息、无缩放工具条、无空/加载/错误三态；空白时整块白板 |
| 4 | 网络推送把生产参数、服务状态、推送测试、接口文档堆在一个滚动区（`:148-306`） | 生产配置与调试工具混在一起，主机/端口/令牌这些宿主级参数长期占屏 |
| 5 | 相机模式是裸下拉 + 一行状态（`:308-379`） | 「能不能跑」不够前置；未连接时要等运行失败才知道 |
| 6 | 视图内大量内联灰阶：`#666 / #333 / #999 / #FAFAFA / #DDDDDD / #E0E0E0`（`:138-143/261-269/428`） | 违反 R117；换肤/统一调整时改不动 |
| 7 | 左列 `MinWidth=300 MaxWidth=430`（`:59`） | 表单宽度被硬截断，宽窗口下右侧预览吃掉全部增量 |

## v1 方向

> 预览优先，一屏完成配置。

1. **两栏固定分工**：左 356px 配置轨（模式 + 模式专属参数 + 输出），右侧预览工作区自适应。
2. **模式是卡片，不是单选**：2×2 卡片，图标 + 名称 + 用途 + 选中态同屏。
3. **取消二级页签**：「显示窗口」并入左栏底部「输出」区，配置一屏看完。
4. **预览是工作区**：文件信息头 + 缩放工具条 + 深色画布 + 状态栏；空/加载/错误三态齐备。
5. **生产与调试分层**：网络推送把服务状态置顶；主机/端口/令牌、推送测试、接口说明收进「调试与接口」。
6. **相机状态前置**：显示名、序列号、连接状态同卡；未连接时直接说明「运行时会失败」。
7. **全令牌化**：颜色映射 `Colors.xaml` 的 `Dialog*` / `Scada*`，去掉视图内硬编码灰阶。
8. **五态与可达性**：hover / pressed / focus / disabled 全量定义；图标按钮 30×30 且 `Padding="0"`（R68），焦点环可见（R135）。

## 设计要点（完整清单）

1. 模式卡片：选中态 = 主色描边 + 主色浅底 + 右侧对勾；未选中 = 白底 + 浅描边；悬停加深描边。
2. 表单三件套：标签 12px / 控件 36px / 帮助 11.5px；字段间距 12px；输入区随栏宽伸缩。
3. 路径行：输入框 + 「浏览」+ 变量链接按钮（`link-2`）；链接按钮只表达「可绑定变量」，不改变字段语义（R33）。
4. 文件目录：路径 + 索引步进（上一张 / 索引 / 总数 / 下一张 / 刷新）+ 当前文件名 + 自然排序说明。
5. 网络推送：绿色服务横幅置顶；流程名常驻；「调试与接口」折叠区承载连接参数、推送测试、接口示例。
6. 相机：下拉只负责选设备；状态卡负责「显示名 + 序列号 + 连接状态」；超时 0 = 用相机自身超时。
7. 输出：显示窗口下拉固定在左栏底部，`0 = 不显示；1~9 = 主界面视图窗口`。
8. 预览头：文件图标 + 文件名 + 尺寸；右侧工具条：适应窗口 / 1:1 / 放大 / 缩小 / 打开所在文件夹 / 刷新。
9. 画布：深色底 + 网格；左上「RGB · 8-bit」，右上「适应窗口 42%」；空态用虚线卡 + 选择图片按钮。
10. 状态栏：状态点 + 消息（左）+ 路径/大小（右）；窄档隐藏路径，把宽度让给消息。
11. 底部宿主栏：状态胶囊 + 耗时 + 执行 / 取消 / 确认；与宿主 shell 现有结构一致。
12. 数字等宽：尺寸、索引、总数、耗时使用 tabular-nums。

## 令牌映射

| 设计变量 | 值 | WPF 资源键 |
| --- | --- | --- |
| `--ia-surface` | `#FFFFFF` | `DialogSurfaceBrush` |
| `--ia-surface-2` | `#F8FAFC` | `DialogRowAlternateBrush` |
| `--ia-surface-3` | `#F1F5F9` | `DialogRowHoverBrush` |
| `--ia-border` | `#E2E8F0` | `DialogBorderBrush` |
| `--ia-border-soft` | `#EEF2F7` | `DialogBorderLightBrush` |
| `--ia-border-strong` | `#CBD5E1` | **待补**：新增 `DialogBorderStrongBrush`，或复用 `ControlBorderBrush`（#D1D1D1） |
| `--ia-text` | `#0F172A` | `DialogTextPrimaryBrush` |
| `--ia-text-2` | `#475569` | `DialogTextSecondaryBrush` |
| `--ia-text-3` | `#94A3B8` | `DialogTextMutedBrush` |
| `--ia-text-4` | `#64748B` | `ScadaSecondaryTextBrush`（#66708A，近似） |
| `--ia-primary` | `#0EA5E9` | `DialogAccentBrush` |
| `--ia-primary-strong` | `#0284C7` | `DialogAccentDarkBrush` |
| `--ia-primary-text` | `#0369A1` | `DialogAccentTextBrush` |
| `--ia-primary-soft` | `#F0F9FF` | `DialogAccentSoftBrush` |
| `--ia-primary-light` | `#E0F2FE` | `DialogAccentLightBrush` |
| `--ia-primary-border` | `#BAE6FD` | `DialogAccentBorderBrush` |
| `--ia-success` | `#22C55E` | `DialogSuccessBrush` |
| `--ia-success-text` | `#15803D` | `DialogSuccessTextBrush` |
| `--ia-success-soft` | `#F0FDF4` | `DialogSuccessSubtleBrush` |
| `--ia-success-border` | `#BBF7D0` | **待补**：新增 `DialogSuccessBorderBrush`，或派生自 `DialogSuccessBrush` 低透明度 |
| `--ia-danger` | `#EF4444` | `DialogDangerBrush` |
| `--ia-danger-text` | `#B91C1C` | **待补**：新增 `DialogDangerTextBrush`（与 `DialogWarningTextBrush` 对称） |
| `--ia-danger-soft` | `#FEF2F2` | `DialogDangerSubtleBrush` |
| `--ia-danger-border` | `#FECACA` | **待补**：新增 `DialogDangerBorderBrush`，或派生自 `DialogDangerBrush` 低透明度 |
| `--ia-warning` | `#F59E0B` | `DialogWarningBrush` |
| `--ia-warning-text` | `#B45309` | `DialogWarningTextBrush` |
| `--ia-warning-soft` | `#FFF4E5` | `DialogWarningSubtleBrush` |
| `--ia-warning-border` | `#FDE68A` | **待补**：新增 `DialogWarningBorderBrush`，或派生自 `DialogWarningBrush` 低透明度 |
| 窗口阴影 | — | 宿主已有 `DropShadowEffect` + `DialogShadowColor`（`PluginConfigShellView.xaml:130-137`） |
| 圆角 6/8/10/12/16 | — | WPF `CornerRadius` 字面量（仓内暂无圆角令牌字典；后续建立令牌再替换） |
| 间距 4/8/12/16/20/24 | — | WPF `Margin` / `Padding` 字面量（同上） |
| 字体 | Segoe UI / Microsoft YaHei UI | 同族系统字体；等宽用 Cascadia Mono / Consolas |

**不新增颜色语义**：所有颜色仍落在 `Dialog*` / `Scada*` 既有语义域内；上表 4 个「待补」是**同一语义的描边/文字档**，不是新语义。

## 复刻前置（第 ③ 步动手前必须确认）

1. **预览三态需要显式状态**：当前 VM 只有 `PreviewImage` / `PreviewImagePath` / `StatusMessage`，没有 Loading / Empty / Error 状态。建议新增 `PreviewState`（None / Loading / Ready / Error）或 `IsPreviewLoading`，视图按状态切换空态、骨架、错误。
2. **加载态颜色**：`StatusLevel` 目前只有 Info（绿）/ Warning / Error；设计中的「加载中」是蓝色。不要复用 Info 的绿（加载中看起来像成功）——新增 `StatusLevel.Progress`，或视图侧用 `IsLoading` 单独驱动蓝色。
3. **预览头信息**：宽度 / 高度 / 文件大小当前 VM 未暴露。需在 `LoadPreview` 成功后记录 `HImage.GetImageSize` 的宽高与 `FileInfo.Length`。
4. **预览工具条**：`ImageReadOnly` / `HalconBase` 需要暴露「适应窗口 / 1:1 / 放大 / 缩小」命令。现有滚轮缩放可用，但按钮需要方法；若本轮不做，工具条只保留「刷新预览 / 打开所在文件夹」。
5. **相机「前往相机设置」**：插件当前没有宿主导航契约（`IPluginConfigContextProvider` 只给快照）。若不加契约，WPF 复刻时保留文字提示「请到系统 → 相机设置」，不要放一个点不动的按钮。
6. **宿主最小高度**：宿主 `MinHeight=420`，内容区只有约 260px；本设计的预览工作区需要约 520px。建议把本插件视图 `MinHeight` 提到 520（或宿主 `MinHeight` 提到 560），否则 640×420 下预览只剩一条。**这是需要用户裁定的设计前提。**
7. **模式卡片**：用 4 个 `RadioButton` 套卡片模板（`IsChecked` 走现有 `EnumConverter`），不引入新的命令参数类型（R27）。
8. **图标按钮**：30×30 且 `Padding="0"`（R68）；焦点环走 `DialogFocusVisual`（R135）；ToolTip 走 `DialogToolTipStyle`。
9. **网络推送**：主机 / 端口 / 令牌移入 `Expander`（现有视图已用 `Expander`），VM 属性不变；推送测试仍在 `Expander` 内。
10. **颜色令牌**：复刻前先把上表 4 个「待补」令牌补进 `Colors.xaml`，避免视图内硬编码（R117）。

## 已知偏差（复刻时按 R130 记录）

1. **图标字体**：Web 用 Lucide，WPF 用 Font Awesome 6 Pro Solid（`DialogIconFont`）；形状不同，语义一致。
2. **阴影/动效**：Web 窗口阴影为 CSS 多层阴影；WPF 用宿主现有 `DropShadowEffect`；动效统一 150~200ms。
3. **预览图**：Web 用内联 SVG 工业零件示意图；WPF 用真实 `HImage`。
4. **深色画布**：`#0B1220` 是设计选择。若 Halcon 窗口背景色不可改，允许用宿主 `Border` 深色底 + 图像 letterbox，需在验收时说明。
5. **字体度量**：同字号下 Segoe UI / 微软雅黑与 Web 的字宽、行高略有差异，不按 px 硬套。
6. **滚动条**：WPF 左栏滚动条约占 17px，窄档可用宽度比 Web 少约 17px。
7. **工具提示**：Web 的 `data-tooltip` 由预览宿主提供；WPF 用 `DialogToolTipStyle`。

## 未纳入本版（记录，不改）

- **拖拽入图**：当前清单 B5 未实现；本版空态文案只承诺「选择图片」，不承诺拖拽。
- **相机实时预览**：仍在「系统 → 相机设置」，本步骤只从帧队列消费一帧。
- **多相机同时预览**：不属于本步骤配置界面。
- **ROI / 测量叠加**：属于算子视图，不属于采集配置。
- **640×420 最小高度下的完整布局**：见「复刻前置」第 6 条。

## 变更规则

设计基线确认后只读。需要调整设计时升版本（v2），并同步更新已复刻的 `ImageAcquisitionView.xaml` 与本映射表。
