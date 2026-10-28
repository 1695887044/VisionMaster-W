# 可形变模板匹配配置窗口 · 设计基线

| 项 | 内容 |
| --- | --- |
| 版本 | v0.2（草案） |
| 日期 | 2026-10-27 |
| 确认状态 | **待用户确认** |
| 覆盖范围 | `Plugins\Plugin.DeformableMatching\DeformableMatchingView.xaml`（插件内容区）；宿主外壳 `PluginConfigShellView` 不在本稿范围 |
| 窗口尺寸 | 960×680 默认；640×680 窄档；宿主最小 640×560 |
| 设计基线文件 | `design.html`（v0.1，自包含、内联 SVG、可点击演示）+ 本 README 的 v0.2 模板来源增补 |
| 复刻状态 | **已复刻**（WPF 视图已实现；待用户验收） |
| 证据 | [`evidence/README.md`](evidence/README.md)（设计稿 3 张；WPF 渲染图见 `artifacts\render\20261027\_batch-evidence\`） |
| 关联文档 | `docs\架构文档\Plugin.DeformableMatching-设计.md`；`docs\code-changes\2026-10-26-计划-可形变匹配插件A方案.md` |

## 定位

按 `AGENTS.md` §30.1 的四步流程：本文件是**第 ① 步（Web 设计基线）**的产物。
Web 只是设计媒介，**不进入构建、不部署**（R126）。确认后按本基线做第 ③ 步（WPF 复刻），
第 ④ 步以「同一屏、同一状态」的截图对照验收（R129/R132）。

**算法/端口/模型持久化属非可视代码（R109），不受本基线阻塞；`DeformableMatchingView.xaml`
必须等本基线确认后再写。**

## 设计范围（v1）

- 单模板：参考图 + ROI（v1 数值：中心行/列、半长/半宽、角度）+ 掩膜 + 学习/重学。
- 模板来源：方案资产（配置壳顶部下拉）/ 本地模板 JSON / 内嵌 `TemplateJson`；资产优先。
- 两种算法二选一：
  - `LocalDeformable`：局部非刚性；输出 `DeformedContours` / `RectifiedImages`；
  - `PlanarUncalibDeformable`：平面透视；输出 `HomMat2D`。
- 查找参数：角度/缩放范围、平滑度（仅局部）、分数下限、最大命中数、最大重叠、搜索激进度、极性。
- 结果区：`MatchCount / Score / Row / Column / Scores / Rows / Columns` + 算法专有输出。
- 输出：`DisplayViewIndex`（0=不发布，1~9=主界面视图）。

## 设计要点

1. **两栏固定分工**：左 368px 配置轨（算法 → 模板 → 查找参数 → 输出 → 怎么用），右侧参考图工作区 + 结果区。
2. **创建期/查找期分层**：模板区与算法区标「创建期，改了就重学」；查找参数区标「运行期，立即生效」。
3. **算法是卡片，不是下拉**：两张卡片同屏对比「局部非刚性 / 平面透视」，选中态 = 主色描边 + 浅底 + 对勾。
4. **ROI 形状切换**：矩形 / 圆 / 椭圆；切圆时隐藏角度与第二半轴，标签改为「半径」。
5. **状态三态前置**：`未学习 / 已学习 / 参数已改·需重新学习` 同时出现在模板区、工作区头、结果区头。
6. **结果区按算法切换详情**：
   - 局部：`DeformedContours` 段数（2 命中 × 486 段）+ `RectifiedImages` 张数；
   - 平面：`HomMat2D` 9×N + 3×3 矩阵预览 + `ProjectiveTransPixel(hom,0,0)` 说明。
7. **`DeformedContours` 口径提示**：结果区明确写「轮廓段数 ≠ 命中数」，防止下游把 `CountObj` 当 `MatchCount`。
8. **五态齐全**：按钮/输入框/状态胶囊/算法卡片都有默认、悬停、焦点、禁用、错误。
9. **窄档 640**：左栏 300px，结果区单列，隐藏工作区提示与状态胶囊（保留结果区胶囊）。
10. **只读预览**：v1 画布只做参考图/ROI/结果轮廓预览，不支持拖拽 ROI（M3 增量）。
11. **模板来源前置**：模板区先显示「模板来源」提示，再是本地模板文件行与参考图行；
    方案资产由配置壳顶部下拉选择，插件视图不重复做资产下拉。
12. **外部来源只读**：资产/本地文件模式禁用学习与本地文件输入，状态胶囊显示
    「方案资产」/「本地模板」；清除按钮变「清除内嵌」，只清 `.vms` 里的内嵌模型。

## 五态

| 状态 | 视觉 | 用在哪 |
| --- | --- | --- |
| 默认 | 白底 + 1px 边框；主操作主色实心 | 输入框、按钮、算法卡片 |
| 悬停 | 边框加深 + 浅底；按钮背景 `DialogRowHoverBrush` | 所有可点元素 |
| 焦点 | 主色边框 + 3px `DialogAccentLightBrush` 焦点环 | 输入框、按钮、卡片 |
| 禁用 | 灰底 + 55% 不透明 + `not-allowed` | 学习按钮（参考图缺失/执行中）、输入框 |
| 错误 | 红边 + 红焦点环 + 原因文案 | 参考图路径、数值越界、学习失败 |

过渡统一 150ms（R116 的 150~200ms 区间内）。

## 令牌映射

| 设计变量 | 值 | WPF 资源键 |
| --- | --- | --- |
| `--dm-surface` | `#FFFFFF` | `DialogSurfaceBrush` |
| `--dm-surface-2` | `#F8FAFC` | `DialogRowAlternateBrush` |
| `--dm-surface-3` | `#F1F5F9` | `DialogRowHoverBrush` |
| `--dm-border` | `#E2E8F0` | `DialogBorderBrush` |
| `--dm-border-soft` | `#EEF2F7` | `DialogBorderLightBrush` |
| `--dm-border-strong` | `#CBD5E1` | `DialogBorderStrongBrush` |
| `--dm-text` | `#0F172A` | `DialogTextPrimaryBrush` |
| `--dm-text-2` | `#475569` | `DialogTextSecondaryBrush` |
| `--dm-text-3` | `#94A3B8` | `DialogTextMutedBrush` |
| `--dm-primary` | `#0EA5E9` | `DialogAccentBrush` |
| `--dm-primary-strong` | `#0284C7` | `DialogAccentDarkBrush` |
| `--dm-primary-text` | `#0369A1` | `DialogAccentTextBrush` |
| `--dm-primary-soft` | `#F0F9FF` | `DialogAccentSoftBrush` |
| `--dm-primary-light` | `#E0F2FE` | `DialogAccentLightBrush` |
| `--dm-primary-border` | `#BAE6FD` | `DialogAccentBorderBrush` |
| `--dm-success` | `#22C55E` | `DialogSuccessBrush` |
| `--dm-success-text` | `#15803D` | `DialogSuccessTextBrush` |
| `--dm-success-soft` | `#F0FDF4` | `DialogSuccessSubtleBrush` |
| `--dm-success-border` | `#BBF7D0` | `DialogSuccessBorderBrush` |
| `--dm-danger` | `#EF4444` | `DialogDangerBrush` |
| `--dm-danger-text` | `#B91C1C` | `DialogDangerTextBrush` |
| `--dm-danger-soft` | `#FEF2F2` | `DialogDangerSubtleBrush` |
| `--dm-danger-border` | `#FECACA` | `DialogDangerBorderBrush` |
| `--dm-warning` | `#F59E0B` | `DialogWarningBrush` |
| `--dm-warning-text` | `#B45309` | `DialogWarningTextBrush` |
| `--dm-warning-soft` | `#FFF4E5` | `DialogWarningSubtleBrush` |
| `--dm-warning-border` | `#FDE68A` | `DialogWarningBorderBrush` |
| 预览画布 `#0B1220` | — | `PluginPreviewCanvasBrush` |
| 预览表面 `#1E293B` | — | `PluginPreviewSurfaceBrush` |
| 预览描边 `#334155` | — | `PluginPreviewBorderBrush` |
| 预览文字 `#E2E8F0` | — | `PluginPreviewTextBrush` |
| 预览弱文字 `#94A3B8` | — | `PluginPreviewMutedTextBrush` |

**缺失令牌：无。** 颜色全部落在既有 `Dialog*` / `PluginPreview*` 语义域内；未新增公共键（R140）。
圆角 6/8/10/12/16、间距 4/8/12/16/20/24 在仓内暂无令牌字典，WPF 用字面量。

## v0.2 增补：模板来源（方案资产 / 本地文件 / 内嵌）

> 2026-10-27 增补。`design.html` 仍是 v0.1，未包含本区块；WPF 已按本增补实现，
> 待补 design.html 与逐项截图对照。

布局（模板区，自上而下）：

1. 标题行「模板」+ 状态胶囊（方案资产 / 本地模板 / 未学习 / 已学习 / 需重新学习）；
2. 「模板来源」提示行：说明优先级与当前来源；
3. 「本地模板文件（可选）」行：路径输入 + 「载入」+「清除」；
4. 「导出模板 JSON」按钮；
5. 「参考图」行与后续 ROI/掩膜不变。

状态：

| 来源 | 胶囊 | 学习按钮 | 本地文件行 | 清除按钮 |
| --- | --- | --- | --- | --- |
| 方案资产 | 方案资产 | 禁用 | 禁用 | 清除内嵌 |
| 本地文件 | 本地模板 | 禁用 | 可用 | 清除内嵌 |
| 内嵌 | 未学习/已学习/需重学 | 可用 | 可用 | 清除模板 |

交互：

- 方案资产在配置壳顶部下拉选择；选「（不使用资产）」回到本地/内嵌。
- 本地文件「载入」校验 JSON 并更新状态；「清除」清掉路径后回到内嵌。
- 「导出模板 JSON」把当前 `Entry` 写成可导入项目资产页的 JSON；资产模式请用资产页导出。
- 外部来源点「确定」只写元数据，不把模型 base64 再写回 `.vms`。

已知偏差：本增补没有新增颜色/字号令牌，全部复用既有 `Dialog*` 令牌；
`design.html` 未同步（待补），WPF 已实现并通过 UI 冒烟（540 元素、hex=0）。

## 契约保留（一个都不能改）

| # | 契约 | 为什么不能动 |
| --- | --- | --- |
| C1 | `DataContext = 插件实例` + `IPluginConfigView` | 宿主靠 `viewObj.DataContext as IPluginConfigView` 取 `OnConfirm`；换独立 VM = 确认不保存 |
| C2 | `NumericBox` + `NumberValidationRule` | ROI/角度/缩放/分数等数值的解析、范围校验、提交时机都在控件里；只改外观，不换控件 |
| C3 | `ImageEdit` / `ImageReadOnly`（若使用） | HALCON 显示/缩放/右键菜单/ROI 交互依赖控件 DP；必须常驻可视树，不折叠 |
| C4 | `TemplateJson` / `TemplateAssetId` / `TemplateFilePath` / `OnConfirm` | 模型载荷、`LearnedAlgorithm`、`LearnedSignature` 随方案落盘；外部来源模式只写元数据，视图不能另存一份 |
| C5 | `StatusMessage` / `StatusLevel` | 只有 Info / Warning / Error 三档；状态胶囊与底部状态栏共用同一来源 |
| C6 | 端口契约 | `MatchCount/Score/Row/Column/Scores/Rows/Columns/DeformedContours/HomMat2D/RectifiedImages/MeasureImage` 的名字与类型不变 |

## 本稿未纳入

- 多模板 / 配方库（v2 升级路径：`List<DeformableTemplateEntry>` + `TemplateLibraryJson`）。
- `PlanarCalibDeformableModel`（需要相机内参/位姿）。
- `VectorField` 输出、平面模式 `RectifiedImages`、批量学习/批量重建。
- 画布 ROI 拖拽（M3 增量；v1 只做数值 ROI + 只读预览）。
- 学习完「一键保存为方案资产」（插件无写资产权限；v0.2 走导出 JSON → 项目资产页导入）。
- 实时性调参表、假 timeout、自动参数推荐。

## 待确认问题（请用户拍板）

1. 左栏 368px + 结果区 188px 的比例是否合适？结果表是否需要更高？
2. 算法用「两张卡片」还是「下拉」？卡片占左栏高度，但对比更直观。
3. v1 数值 ROI 是否接受？画布 ROI 放 M3 是否可接受？
4. 结果表默认按分数降序显示（端口数组保持 HALCON 返回顺序）是否可接受？
5. 局部 `RectifiedImages` 在结果区显示「张数」还是缩略图？本稿先显示张数。
6. `DeformedContours` 的「轮廓段数 ≠ 命中数」提示是否要更显眼（例如结果区顶部横幅）？
7. 平面模式是否需要在结果区常驻显示 `HomMat2D` 3×3 矩阵？本稿显示首命中矩阵预览。

## 确认记录

| 版本 | 日期 | 确认人 | 结论 | 备注 |
| --- | --- | --- | --- | --- |
| v0.1 | 2026-10-26 | 待确认 | 待确认 | 首版草案；探针结论已并入设计文档 §3 |
| v0.2 | 2026-10-27 | 待确认 | 待确认 | 增补模板来源（资产/本地/内嵌）；WPF 已复刻，待验收 |
