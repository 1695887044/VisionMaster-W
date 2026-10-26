# SCADA · 按钮图元审查与改版 · 设计基线

| 项 | 内容 |
| --- | --- |
| 版本 | **v1** |
| 日期 | 2026-10-25 |
| 确认日期 / 人 | 待用户确认 |
| 覆盖范围 | `Hmi.Button`（`Scada.Controls/Themes/ButtonElement.xaml`，72 行）的四态、焦点、危险变体、忙碌态、最小尺寸 |
| 设计文件 | `design.html`（现状按模板逐项 CSS 复刻 + 改版同尺寸对照 + 7 态矩阵，可悬停/按住） |
| 状态 | **v1 已落地（2026-10-25）**：主题四态 + Danger 变体已实现；忙碌态未做（需运行态分发器参与） |

## 落地记录（2026-10-25）

改了两个文件：

1. `Scada.Controls/Themes/ButtonElement.xaml`：模板改为六层（填充 / 悬停白叠 / 按下黑叠 / 危险红蒙版 /
   禁用暗蒙版 / 焦点环）+ 文字；触发器 `IsMouseOver`（叠加 0.10 + 边框提亮）、`IsPressed`（黑叠 0.18）、
   `IsKeyboardFocused`（2px 青环）、`Danger`（红边 + 淡红字 + 红蒙版 0.22）、`IsEnabled=False`
   （暗蒙版 0.55 + 文字降亮，不再整体 50% 透明）；样式级 `MinHeight=36 / MinWidth=72`、
   禁用时 `Cursor=Arrow`。
2. `Scada.Controls/Elements/ButtonElement.cs`：新增只读 DP `IsPressed`（鼠标按下/抬起/丢捕获三处维护，
   本控件是 ContentControl 派生、没有 `ButtonBase.IsPressed` 可用）+ `Danger` 属性（`[ScadaProperty]`）。
3. `BuiltInElements.cs`：`Hmi.Button` 描述符补 `Danger` 一行（属性面板可见）。
4. `ScadaTokens.xaml`：新增 6 个按钮令牌（悬停边框 / 危险蒙版与文字 / 禁用蒙版与文字 / 焦点环），
   `ButtonElement.xaml` 里不再有 hex（R140）；该文件自带一份令牌合并，因为断言宿主单独加载字典时
   跨册 StaticResource 会抛 `XamlParseException`。

**验证**：`ScadaChecks` **3052 通过 / 0 失败**（`artifacts\logs\20261025\checks-button.log`）；
`[DSK1]` 50/50；描述符快照断言已同步 `Danger`。

**未做**：忙碌态（执行中圆环）——需要运行态分发器在动作执行期间置位，属跨模块改动，另批。


## 审查结论（10 条，按级别）

| 级别 | 发现 |
| --- | --- |
| P1 | 没有按下反馈（模板注释写明 `IsPressed` 用不了，运行态也没有补） |
| P1 | 没有键盘焦点环（`Focusable=True` 但模板无 `IsKeyboardFocused` 触发器） |
| P1 | 没有危险动作变体（急停/复位与"启动"同款） |
| P1 | 没有最小可点尺寸（触摸屏可被拉到 20px 高） |
| P2 | 禁用态仍是手型光标、整体 50% 透明（深色底文字几乎不可读） |
| P2 | 悬停强度取决于用户配色（白色 12% 叠加在浅色填充上几乎不可见） |
| P2 | 没有忙碌/进行中态（长动作期间可被连点） |
| P2 | 文字贴边、无行高（与 R184 同族） |
| P3 | 圆角 6 与全局卡片 8 不一致 |
| P3 | 文字被裁剪时没有 ToolTip 兜底 |

**保留的优点**：悬停用"叠加白层"而不是改填充色——填充可能是用户配置或变量绑定，不该被样式覆盖。

## 改版要点

- 四态：Normal / Hover（提亮边框 + 8~10% 叠加）/ Pressed（下移 1px + 底暗一档）/ Disabled（降饱和 + 保文字对比 + `cursor:Arrow`）；
- 焦点：2px 品牌青外环，**仅键盘导航时出现**（`:focus-visible` 语义）；
- `Danger` 变体：红底红边，由属性面板勾选（是否按动作类型自动判定见 Q1）；
- 忙碌态：12px 圆环 + 吞点击（是否改文案见 Q3）；
- `MinHeight` 36（触摸 44，见 Q2）。

## 边界

- 全部落在**主题模板 + 一个布尔属性**内；不动模型、不动落盘、不动动作语义；
- 不做渐变动画/阴影扩散/圆角动画（工业触摸屏 150ms 以内，只做位移与明暗）；
- 本页是 CSS 按模板复刻，**不是 WPF 截图**；像素级验收要在实现后同屏截图（R132）。

## 待裁定

| # | 问题 | 本稿 |
| --- | --- | --- |
| Q1 | `Danger` 是否按动作类型自动判定 | 人工勾选 |
| Q2 | 最小高度 36 还是 44 | 36 |
| Q3 | 忙碌态是否改文案为"执行中…" | 只加圆环 |
| Q4 | 焦点环颜色：品牌青 or 琥珀 | 品牌青 `#38BDF8` |
