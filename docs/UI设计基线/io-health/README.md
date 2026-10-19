# IO 健康 · 设计基线

| 项 | 内容 |
| --- | --- |
| 版本 | v1 |
| 确认日期 | 2026-10-19 |
| 确认人 | 待用户确认 |
| 覆盖范围 | `VisionMaster/Views/DialogViews/IoHealthView.xaml`（设备 / 总线 / 点位 三页签） |
| 设计基线文件 | `design.html`（自包含，浏览器直接打开） |

## 设计要点

1. **标题栏**：52 高、白底、底部发丝线；图标 14 + 标题 14/SemiBold + 副标题 12/Muted。
2. **摘要卡**：白底、12 圆角、1px 描边；统计文字 13/SemiBold，提示 12/Muted 单行截断；刷新按钮 26 高、12 号字。
3. **页签**：下划线式——无灰带、无凸起块；选中态 2px 主色下划线 + 主文字色 + SemiBold；悬停只提亮文字。
4. **表格**：表头 36 高、12 号 Muted、仅底部发丝线、无竖分隔线；行高 40；悬停 `#F1F5F9`，选中 `#E0F2FE`（两者必须可辨）。
5. **空态**：图标 40 + 标题 14/SemiBold + 说明 12，居中；不显示"白板表格"。
6. **状态点**：8px 圆点，成功/警告/错误三色；颜色不是唯一信息载体（旁边有文字）。

## 与桌面端的对应关系

| Web 令牌 | WPF 资源键 |
| --- | --- |
| `--primary` | `DialogAccentBrush` |
| `--primary-weak` | `DialogAccentLightBrush` |
| `--foreground` | `DialogTextPrimaryBrush` |
| `--muted` | `DialogTextSecondaryBrush` / `DialogTextMutedBrush` |
| `--border` | `DialogBorderLightBrush` |
| `--background` | `DialogBackgroundBrush` |
| `--card` | `DialogSurfaceBrush` |
| `--row-hover` | `DialogRowHoverBrush` |
| `--row-selected` | `DialogAccentLightBrush` |

| Web 组件 | WPF 对应 |
| --- | --- |
| 页签 | `DialogTabControl` / `DialogTabItem`（`UI/Controls/Themes/Dialog/Dialog.Navigation.xaml`） |
| 表头 | `GridListHeaderStyle`（`UI/Controls/Themes/Controls/GridView.xaml`） |
| 摘要卡 / 标题栏 | `DialogCardHeader` / `DialogCardBorder` |
| 按钮 | `DialogChipButton` / `DialogPrimaryButton` |
| 空态 | `DialogEmptyStateIcon/Title/Hint` |

## 已知偏差（需用户裁定）

1. **字体度量**：Web 用系统无衬线，WPF 用系统字体；同一字号下字宽与行高略有差异，不按 px 硬套。
2. **滚动条**：Web 为覆盖式细条，WPF 占位约 17px；表格列宽已按此预留（合计 1100，窗口 1180）。
3. **阴影**：Web 两层柔和阴影，WPF 为单层极浅阴影 + 1px 描边。
4. **动效**：Web 用 CSS transition，WPF 用 150ms `CubicEase`，曲线为近似。
5. **窗口圆角**：设计稿是 16px 圆角卡片；WPF 弹窗为 `WindowStyle=None` 的方角窗口，做圆角需要 `AllowsTransparency`（会波及全部弹窗的阴影/渲染），本期不引入，保持方角。
6. **阴影**：设计稿两层柔和阴影；WPF 窗口级阴影未启用，改为 1px 描边 + 无外阴影。
7. **图标按钮**：尺寸已对齐基线（30×30），但 WPF 侧复用 Fluent 按钮模板，圆角为 8（基线 6）、悬停底色为 5% 黑叠加（白底上 `#F2F2F2`，基线 `--row-hover #F1F5F9`）。差异肉眼不可辨，未改共享模板。

## 变更规则

设计基线确认后只读。需要调整设计时升版本（v2），并同步更新已复刻的桌面端页面与该映射表。
