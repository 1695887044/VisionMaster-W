# OCR 文本识别配置窗口 · 设计基线

| 项 | 内容 |
| --- | --- |
| 版本 | v1 |
| 日期 | 2026-10-20 |
| 状态 | 设计基线已产出；WPF 复刻已按本基线落地（待用户真机确认） |
| 覆盖范围 | `Plugins\Plugin.Ocr\OcrView.xaml`（插件内容区） |
| 宿主外壳 | `VisionMaster\Views\DialogViews\PluginConfigShellView.xaml`（960×680，最小 640×560，本轮不改） |
| 设计文件 | `design.html`（自包含、无外部依赖、可点击；含默认 / 已试算 / 空态 / 未框选 / 试算失败五态） |
| 设计模板 | 沿用 `docs/UI设计基线/image-acquisition/design.html` 的 CSS 布局与令牌体系（v2 视觉语言） |
| 复刻证据 | `docs/UI设计基线/_batch-evidence/Plugin.Ocr-OcrPlugin.png`（批量冒烟初始态） |

## 1. 定位与范围

本文件是 SOP §5.2 的产物：先给出 OCR 插件自定义配置视图的 Web 设计基线，再按 SOP §5.4/§5.5 复刻 WPF。

**只做纯 UI 重构**：

- 不新增功能、不改端口名/命令名/持久化字段、不改 `OnConfirm`/`Initialize` 语义；
- 不引入独立 ViewModel，`DataContext` 仍是 `OcrPlugin` 实例；
- 不改共享控件与共享字典：`LinkableValueEditor`、`ImageEdit`、`HalconBase`、`PluginConfigStyles.xaml`、`PluginConfigShellView.xaml`。

## 2. 现状审查（改动理由）

| # | 现状 | 影响 |
| --- | --- | --- |
| 1 | 根视图固定 `Width="1000" Height="620"`（`OcrView.xaml:9-10`） | 宿主 960×680 下内容溢出/被裁；640×560 最小档不可用 |
| 2 | 左列固定 `330px`，所有参数卡片平铺，只有「高级参数」折叠 | 常用项与高级项层级已有，但表单节奏、控件高度、帮助文字不统一 |
| 3 | 右侧只有 `ImageEdit` + 一张卡片边框，没有画布头/状态行 | 看不出「画布是什么、当前有没有图、识别区有没有框」；无图时整块黑，缺少下一步指引 |
| 4 | `ParamLabel`/`ParamBox` 局部样式，TextBox 高 24、标签与控件间距不统一 | 与 ImageAcquisition V2 的 36px 控件 / 12px 标签 / 11.5px 帮助文字不一致 |
| 5 | 试算结果用 `Consolas 11` 的 `TextBlock`，无滚动、无空态容器 | 多行结果会把左栏撑长，长明细被截断 |
| 6 | 清空/试算按钮走 `Click` 事件 | 功能正常；为满足批量插件视图冒烟的 `Button.Command` 契约，改为视图级 `ICommand` 转发，行为不变 |
| 7 | 无图试算提示仍写「先点『载入』」（`OcrPlugin.cs:380`） | 新界面没有「载入」按钮，文案指向不存在的入口；复刻时只改文案为「绑定上游图像 / 点执行试运行」，不改逻辑 |

## 3. 设计方向

> 识别区优先，一屏完成配置；画布常驻、参数分层、试算结果可读。

1. **两栏固定分工**：左 356px 配置轨（图像输入 / 识别区 / 常用参数 / 高级参数 / 试算），右侧识别区工作区自适应。
2. **画布是工作区**：画布头（图标 + 标题 + `ROI · 可编辑` + `HALCON ImageEdit`）、无图提示条、`ImageEdit` 常驻画布、底部状态行（`Hint` + 识别区状态）。
3. **参数分层不变**：常用 5 项（极性 + 四个字符尺寸）在前；阈值/形态学/面积/模型/列优先收进「高级参数」`Expander`。
4. **试算结果可读**：`ResultSummary` 放进等宽结果块（带滚动上限），空态显示「尚未试算」；失败态用危险色。
5. **识别区摘要前置**：`RoiSummary` 以等宽信息块显示中心/角度/半长/半宽；「清空识别区」是现有 `ClearRoi()` 的唯一入口。
6. **图像输入只包裹**：`LinkableValueEditor` 设 `Label=""` 外置标签，保留 `Port`/`StepData`；链接/解除链接仍走控件自带按钮。
7. **全令牌化**：颜色只引用 `Dialog*` / `PluginPreview*` / `ScadaSecondaryTextBrush`；视图 XAML 内 0 个十六进制颜色。
8. **五态与可达性**：hover / pressed / focus / disabled 由公共样式提供；图标按钮 30×30 且 `Padding="0"`（R68）；焦点环走 `DialogFocusVisual`。

## 4. design.html 交互说明

| 控件 | 行为 |
| --- | --- |
| 窗口宽度 960 / 640 | 切换 `is-narrow`，左栏 356 → 300，右侧自适应 |
| 界面状态：默认 / 已试算 / 空态 / 未框选 / 试算失败 | 切换画布、识别区、结果块、状态行、提示条 |
| 极性：暗字亮底 / 亮字暗底 | 只切换示意画布文字颜色与选中态，对应 WPF `TextIsDark` |
| 清空识别区 | 隐藏 ROI 矩形与手柄，切到「未框选」 |
| 试算一下 | 有图有 ROI → 「已试算」；无图 → 警告提示；无 ROI → 「试算失败」 |
| 高级参数 | 原生 `<details>` 展开/收起 |
| 复选框 | 点击切换勾选态（固定阈值 / 按列优先） |

## 5. 令牌映射

| 设计变量 | 值 | WPF 资源键 |
| --- | --- | --- |
| `--ocr-surface` | `#FFFFFF` | `DialogSurfaceBrush` |
| `--ocr-surface-2` | `#F8FAFC` | `DialogRowAlternateBrush` |
| `--ocr-surface-3` | `#F1F5F9` | `DialogRowHoverBrush` |
| `--ocr-border` | `#E2E8F0` | `DialogBorderBrush` |
| `--ocr-border-soft` | `#EEF2F7` | `DialogBorderLightBrush` |
| `--ocr-border-strong` | `#CBD5E1` | `DialogBorderStrongBrush` |
| `--ocr-text` | `#0F172A` | `DialogTextPrimaryBrush` |
| `--ocr-text-2` | `#475569` | `DialogTextSecondaryBrush` |
| `--ocr-text-3` | `#94A3B8` | `DialogTextMutedBrush` |
| `--ocr-text-4` | `#64748B` | `ScadaSecondaryTextBrush` |
| `--ocr-primary` | `#0EA5E9` | `DialogAccentBrush` |
| `--ocr-primary-strong` | `#0284C7` | `DialogAccentDarkBrush` |
| `--ocr-primary-text` | `#0369A1` | `DialogAccentTextBrush` |
| `--ocr-primary-soft` | `#F0F9FF` | `DialogAccentSoftBrush` |
| `--ocr-primary-light` | `#E0F2FE` | `DialogAccentLightBrush` |
| `--ocr-primary-border` | `#BAE6FD` | `DialogAccentBorderBrush` |
| `--ocr-success` | `#22C55E` | `DialogSuccessBrush` |
| `--ocr-success-text` | `#15803D` | `DialogSuccessTextBrush` |
| `--ocr-success-soft` | `#F0FDF4` | `DialogSuccessSubtleBrush` |
| `--ocr-success-border` | `#BBF7D0` | `DialogSuccessBorderBrush` |
| `--ocr-danger` | `#EF4444` | `DialogDangerBrush` |
| `--ocr-danger-text` | `#B91C1C` | `DialogDangerTextBrush` |
| `--ocr-danger-soft` | `#FEF2F2` | `DialogDangerSubtleBrush` |
| `--ocr-danger-border` | `#FECACA` | `DialogDangerBorderBrush` |
| `--ocr-warning` | `#F59E0B` | `DialogWarningBrush` |
| `--ocr-warning-text` | `#B45309` | `DialogWarningTextBrush` |
| `--ocr-warning-soft` | `#FFF4E5` | `DialogWarningSubtleBrush` |
| `--ocr-warning-border` | `#FDE68A` | `DialogWarningBorderBrush` |
| `--ocr-canvas` | `#0B1220` | `PluginPreviewCanvasBrush` |
| 圆角 6/8/10/12/16 | — | WPF `CornerRadius` 字面量（仓内暂无圆角令牌） |
| 间距 4/8/12/16/20/24 | — | WPF `Margin`/`Padding` 字面量 |
| 字体 | Segoe UI / Microsoft YaHei UI | 同族系统字体；等宽 `Consolas, Cascadia Mono, Courier New` |

**缺失令牌：无。** 本轮没有新增公共令牌，也没有修改 `Colors.xaml` / `PluginConfigColors.xaml`。

## 6. 契约保留（C1-C5）

| # | 契约 | 复刻要求 |
| --- | --- | --- |
| C1 | `DataContext = OcrPlugin` + `IPluginConfigView` | 构造签名不变；`GetConfigView` 仍是 `new OcrView { DataContext = this }`；不引入独立 VM |
| C2 | `LinkableValueEditor`（`Image` 端口） | 保留 `Port="{Binding Image}"`、`StepData="{Binding StepData}"`；只设 `Label=""` 外置标签 |
| C3 | `ImageEdit`（识别区画布） | 保留 `x:Name="ImageEditor"`、`ActiveRoi`（TwoWay）、`DrawObjectList`、`HImage`（OneWay）；**控件常驻，不用 Visibility 折叠** |
| C4 | 参数/结果属性 | `TextIsDark`、`MinCharHeight`、`MaxCharHeight`、`MinCharWidth`、`MaxCharWidth`、`UseFixedThreshold`、`FixedThreshold`、`MinArea`、`MaxArea`、`ClosingRadius`、`ErosionRadius`、`DilationRadius`、`ModelPath`、`ColumnFirst`、`RoiSummary`、`Hint`、`ResultSummary` 全部保留 |
| C5 | 两个功能入口 | 「清空识别区」→ `OcrPlugin.ClearRoi()`；「试算一下」→ `OcrPlugin.TryPreviewRecognize()`；视图只做命令转发 |

> `OcrPlugin` 未暴露 `StatusMessage` / `StatusLevel`，因此本基线**不新增**三档状态枚举；状态行消费现有 `Hint`，结果块消费 `ResultSummary`。这是与 SOP 通用清单的差异，不是遗漏。

## 7. 复刻实现映射

| 设计项 | WPF 实现 |
| --- | --- |
| 两栏 356px + 自适应 | `Grid`：左列 `Width="1*" MinWidth="280" MaxWidth="356"`，右列 `Width="1.45*" MinWidth="200"` |
| 卡片 | 局部 `CardStyle`（`DialogSurfaceBrush` + `DialogBorderLightBrush` + `CornerRadius=10` + `Padding=12`） |
| 表单三件套 | `DialogFormLabel`（12/13px）、`DialogInput`（高 36）、`DialogHintText`/局部 `HelpTextStyle`（11.5px） |
| 极性 | `DialogCheckBox` 绑定 `TextIsDark`（WPF 不做双 Radio 反向转换，避免 bool 双向转换竞态） |
| 字符尺寸 2×2 | 四个 `TextBox`（`DialogInput`，高 36）绑定四个 int 属性 |
| 高级参数 | `Expander` + 卡片；所有原绑定与 ToolTip 保留 |
| 试算结果 | `ScrollViewer` + `TextBlock`（`DialogMonoText`，`TextWrapping=Wrap`，`MaxHeight=110`）绑定 `ResultSummary` |
| 图像输入 | `LinkableValueEditor Label=""`；外置标签「输入图像」；`Port`/`StepData` 原绑定 |
| 识别区画布 | `h:ImageEdit` 常驻；外层 `Border` 背景 `PluginPreviewCanvasBrush`；无图提示条在画布上方（HALCON `HwndHost` 有 airspace，不做覆盖层） |
| 画布头 | 图标 chip + 「识别区画布」+ `DialogChipAccent`「ROI · 可编辑」+ `DialogChipNeutral`「ImageEdit」（固定文案；**无窄档隐藏触发器**，不随宽度隐藏——当前文案已是简写，640 档不会溢出） |
| 状态行 | 中性点 + `Hint`（`TextTrimming` + ToolTip）+ 右侧「识别区：已框选/未框选」（`CanvasRois.Count` 触发器，随 ObservableCollection 更新） |
| 按钮 | 清空：`DialogSecondaryButton`；试算：`DialogPrimaryButton`；均由视图级 `ICommand` 转发 |
| 令牌 | 视图 XAML `hex=0`；无新增公共键 |

## 8. 已知偏差（复刻时按 R130 记录）

1. **画布无图态**：Web 稿在深色画布中央显示空态；WPF 的 `ImageEdit` 是 HALCON `HwndHost`，WPF 覆盖层会被其 HWND 盖住（airspace）。因此 WPF 把「还没有图像」提示放在**画布上方的提示条**，画布本身保持常驻，不做 `Visibility=Collapsed`（遵守 SOP §5.4）。
2. **极性控件**：Web 稿用两段式分段控件；WPF 用 `DialogCheckBox` 绑定 `TextIsDark`。原因：`TextIsDark` 是 bool，双 Radio 需要反向转换器，双向绑定在取消选中时会回写反向值，存在竞态；CheckBox 语义与现有文案「不勾 = 亮字暗底」一致。
3. **图标**：Web 用内联 SVG；WPF 用 Font Awesome 6 Pro Solid（`DialogIconFont`），形状不同、语义一致。
4. **状态三档**：Web 稿演示了 ok/warn/err 三色状态行；WPF 只有 `Hint` 文本，没有 `StatusLevel`，因此状态点使用中性色，不伪造三档。详见 C5 备注。
5. **结果块滚动**：Web 稿结果块自然撑开；WPF 限制 `MaxHeight=110` 并加滚动，避免左栏被长明细撑长。
6. **宿主页脚**：Web 稿展示宿主「执行 / 取消 / 确认」；WPF 插件视图不实现页脚，页脚仍由 `PluginConfigShellView` 提供。
7. **窄档**：Web 稿 640 档左栏 300；WPF 用 `MinWidth=280` + `MaxWidth=356` 的弹性列，640 下实际约 280~300。
8. **渲染证据边界**：`ImageEdit` 是 HALCON `HwndHost`，`RenderTargetBitmap` 不捕获其内容；批量冒烟 PNG 只证明 WPF 布局，真实 ROI 画布需真机窗口。
9. **画布头标签**：Web 稿写「HALCON ImageEdit」，WPF 写「ImageEdit」并在 ToolTip 里保留说明，避免 640 窄档头部溢出。
10. **高级参数默认折叠**：Web 稿可展开查看全部字段；WPF 复刻沿用原视图的 `Expander IsExpanded=False`，首次打开只看到常用参数。

## 9. 未纳入本版（记录，不改）

- 加载骨架 / 蓝色加载态（需插件新增 `IsPreviewLoading`/`PreviewState`）。
- 尺寸 / 文件大小（需插件新增只读属性）。
- 放大 / 缩小按钮（`ImageEdit` 继承 `HalconBase`，本版不放工具条按钮；右键菜单保留）。
- 打开所在文件夹 / 系统导航（OCR 配置无文件路径入口，不适用）。
- `StatusLevel` 三档状态（插件未暴露，不新增）。
- 拖拽入图、相机实时预览、ROI 叠加样式扩展（不属于本插件配置视图）。

## 10. 变更规则

设计基线确认后只读。需要调整设计时升版本（v2），并同步更新 `OcrView.xaml` 与本映射表；不复用 ImageAcquisition 的页面内容，OCR 页按本插件字段独立维护。

## 11. 2026-10-20 遗留项修复（OCR1 / OCR2）

- **OCR1（Hint 跟随操作）**：`ClearRoi()` 清空后写 `Hint = "识别区已清空：在右侧画布右键 → 新建矩形重新框住整行字符"`，并把 `ResultSummary` 复位为「尚未试算」；`TryPreviewRecognize()` 的无图 / 失败 / 成功三分支分别写对应 Hint（成功按字符数与平均置信度写一句，含拒识数）。只改提示文案，识别与分割逻辑未动。
- **OCR2（README 口径）**：画布头第二枚 chip 的实际文案是固定的「ImageEdit」，视图**没有窄档隐藏触发器**；原「窄档省略 HALCON 前缀」的表述已改为实际口径（见 §7）。
- **验证**：`dotnet build Plugins\Plugin.Ocr\Plugin.Ocr.csproj -v m` → 0 错误 / 0 警告；`UIThemeSmokeTest --plugin-views Plugin.Ocr` → 11 条 PASS / 0 条 FAIL。
