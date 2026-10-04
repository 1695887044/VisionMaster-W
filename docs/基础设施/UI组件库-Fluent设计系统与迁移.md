# UI 组件库：Fluent（Windows 11）设计系统与迁移说明

> 版本：2026-10-01 ｜ 库位置：`UI/Controls`
> 新增：`UI/Controls/Themes/Fluent/` → `FluentTokens.xaml` / `FluentControls.xaml` / `FluentAliases.xaml`

---

## 1. 盘点：改造前的四类问题

### 1.1 命名规范不一致 —— 同一库里 8 套命名体系

| 体系 | 例子 | 出处 |
|---|---|---|
| `Win11*` | `Win11Card`、`Win11WindowStyle` | DefaultStyle.xaml |
| `Dialog*` | `DialogPrimaryButton`、`DialogInput` | DialogStyles.xaml（70+ 键） |
| `Plugin*` | `PluginButton`、`PluginCard` | PluginConfigStyles/Colors（100+ 键） |
| `Scada*` | `ScadaAccentBrush` | Colors.xaml |
| `FlowCanvas*` | `FlowCanvasNodeBrush` | Colors.xaml |
| `Mb*` | `MbPrimaryButton` | MotionBoardTheme.xaml |
| 无前缀 | `SidebarBg`、`TextMain`、`AccentBlue` | PropertyGrid.xaml |
| 下划线/拼写错 | `Grid_SwitchToggleStyle` | PropergridStyle.xaml（文件名也拼错） |

后缀也不统一：`Win11Card`、`PluginCard` 不带 `Style`，`DialogPrimaryButton` 又带。

### 1.2 分类与职责不清

- **同一概念四份定义**：强调色有 `AccentBlueBrush(#3182CE)`、`AccentBrush`、`AccentBlue`、`PluginAccentBrush` —— 改一个不会带动其它三个。
- **颜色与样式混装**：`PropertyGrid.xaml` 里既定义画刷又定义控件样式，与 `Colors.xaml` 职责重叠。
- **目录层级不一致**：`CustomControl/` 下 `Message/`、`PropertyGrid/` 是子目录，`AlarmBar.cs`、`LogConsole.cs` 等却平铺。

### 1.3 视觉缺乏现代感

- 圆角 2px（`App.xaml` 的展开按钮）、5px、6px、10px 混用，无统一值；Win11 是 4/8/12/16。
- 动效时长硬编码散落（`0:0:0.15`、`0.25`、`0.3`），缓动各自 `new`。
- 颜色大量写死（`#FAFAFA`、`#EEEEEE`、`#8B8B8B`），无文本层级与状态色体系。
- 无材质（Mica/Acrylic）与层级阴影令牌。

### 1.4 类型与变体不足

- **按钮三套**：Dialog 域 6 个、Plugin 域 3 个、PropertyGrid 域 2 个，互不相通，没有统一的语义×尺寸矩阵。
- **缺失**：InfoBar、Segmented、Chip/Badge 语义变体、ToggleSwitch、ProgressBar、Divider、NavItem、ToolTip、亚克力卡片。

---

## 2. 解决方案

### 2.1 令牌层 `FluentTokens.xaml`（唯一事实来源）

| 类别 | 键（节选） |
|---|---|
| 强调色 | `FluentAccentBrush` / `Hover` / `Pressed` / `Disabled` / `Subtle` / `FluentOnAccentTextBrush` |
| 表面与材质 | `FluentBackgroundBrush`、`FluentSurfaceBrush`、`FluentCardBackgroundBrush`、`FluentAcrylicBackgroundBrush`、`FluentMicaBackgroundBrush` |
| 边框 | `FluentBorderBrush`、`FluentBorderStrongBrush`、`FluentCardStrokeBrush`、`FluentDividerBrush` |
| 文字 | `FluentTextPrimary/Secondary/Tertiary/DisabledBrush` |
| 状态 | `FluentSuccess/Warning/Danger/InfoBrush` + 各自 `Subtle` |
| 圆角 | `FluentRadiusSmall(4)`、`Medium(8)`、`Large(12)`、`ExtraLarge(16)`、`Full` |
| 间距 | `FluentPaddingTight/Compact/Control/Row/Card/Dialog`、`FluentBorderThickness` |
| 阴影 | `FluentShadowCard`、`FluentShadowRaised`、`FluentShadowOverlay`（`x:Shared="False"`） |
| 动效 | `FluentDurationFast(150ms)`、`Normal(250ms)`、`Slow(350ms)`、`FluentEaseOut`、`FluentEaseInOut`、`FluentEaseBack` |
| 字体 | `FluentFontFamily`（Segoe UI Variable + 微软雅黑回退）、`FluentMonoFontFamily` |
| 字阶 | `FluentTextCaption/Body/BodyStrong/Subtitle/Title/MonoStyle` |

**亚克力说明**：真 Mica/Acrylic 需 DWM 合成，纯控件库拿不到桌面像素。这里用「半透明色 + 低不透明度」做视觉近似（`FluentAcrylicBackgroundBrush`），避免引入 `DwmSetWindowAttribute` 的进程级副作用。

### 2.2 控件库 `FluentControls.xaml`（统一命名 + 完整变体）

命名：**`Fluent` + 控件名 + 变体 + `Style`**。

| 控件 | 变体 |
|---|---|
| Button | `FluentButtonStyle`(默认) / `Accent` / `Subtle` / `Outline` / `Transparent` / `Danger` / `Success` + `Small` / `Large` |
| Card | `FluentCardStyle` / `Elevated` / `Acrylic` / `Outline` |
| 输入 | `FluentTextBoxStyle`（聚焦底部强调线动画）、`FluentComboBoxItemStyle`、`FluentToggleSwitchStyle`（40×20 + 滑块位移动画） |
| 反馈 | `FluentChipStyle` + Neutral/Accent/Success/Warning/Danger、`FluentInfoBarStyle` + 四种语义、`FluentProgressBarStyle`、`FluentStatusDotStyle` |
| 结构 | `FluentNavItemStyle`、`FluentSegmentedRadioStyle`、`FluentDividerStyle`、`FluentToolTipStyle` |

**状态叠加层设计**：按钮的悬停/按下用半透明叠加层（`#0D000000` / `#1A000000`）做透明度动画，而不是换背景色。因此变体只需改一组颜色即可获得完整状态表现 —— 这正是 Win11 的做法，也避免了「触发器覆盖变体背景色」的经典冲突。

### 2.3 旧键转发层 `FluentAliases.xaml`（同步更新所有引用页面的方式）

不改任何业务 XAML，而是利用**资源字典后合并者优先**的规则，把旧键重定向到 Fluent 样式：

```xml
<Style x:Key="DialogPrimaryButton" BasedOn="{StaticResource FluentButtonAccentStyle}" TargetType="Button" />
```

- 已转发：三套按钮体系、卡片容器、输入框、列表项、Chip/状态标签、字阶（约 50 个键）。
- **不转发**（保留原样，避免破坏）：`DialogCombo`、`DialogDataGrid*`、`DialogSearch*`、`DialogSegmentRadio`、`DialogCheckBox`（各有自定义模板）；`Scada*`、`FlowCanvas*`、`Mb*`（域专属视觉，有意为之）。

效果：上百个引用旧键的页面下一次加载即统一为 Win11 观感，**零页面改动、零运行时风险**。

### 2.4 合并顺序（`Themes/Generic.xaml`）

```
Colors → FluentTokens → PluginConfigColors → IconDictionary → DefaultStyle
→ …各域样式… → FluentControls → FluentAliases（必须最后）
```

`FluentAliases` 最后合并是转发生效的前提。

### 2.5 弹窗引擎同步 `EasyDialog.cs`

原先硬编码白底 / 8px 圆角 / 自建阴影，是全项目唯一不跟随令牌的角落。现改为 `TryResource<T>()` 取 `FluentCardBackgroundBrush`、`FluentRadiusLarge`、`FluentShadowOverlay`、`FluentDividerBrush`，取不到时回退旧值（宿主可能没合并 Fluent 主题）。

---

## 3. 使用规范（新代码必须遵守）

1. **新样式一律 `Fluent` 前缀 + `Style` 后缀**，不要用 `FluentAliases` 里的旧键（它们已标记 deprecated）。
2. **颜色只引用令牌**，禁止在 XAML 里写十六进制；域令牌（Dialog/Plugin/Scada/Mb）只表达「用在哪」。
3. **动效引用 `FluentDuration*` 与 `FluentEase*`**，禁止写字面量时长。
4. **圆角/间距引用令牌**，禁止写 `CornerRadius="5"`。
5. 新增变体时按「语义 × 尺寸」两维给全，不要让调用方为了一个红按钮自己抄模板。

## 4. 后续可选（未做，需评估）

- 把 `PropergridStyle.xaml` 文件名与键 `Grid_SwitchToggleStyle` 纠正为 `PropertyGrid.SwitchToggleStyle`（涉及引用方批量改名）。
- 将 `Scada*`/`FlowCanvas*`/`Mb*` 域令牌改为引用 Fluent 令牌（当前是独立色板，属有意为之）。
- 深色主题：替换 `FluentTokens.xaml` 一份即可，域令牌不动。