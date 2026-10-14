复刻参考设计的「工具箱」到右侧停靠面板 `VisionMaster\Views\ToolView.xaml`，背景沿用项目统一浅色令牌。

## 已定范围
视觉+结构全面复刻 · 只做收藏（不做最近使用/热门节点）· 不做拼音搜索 · 建分类映射表。

## 改动清单
- `VisionMaster\Views\ToolView.xaml`：**重写**。头部(标题+总数胶囊+列表/网格切换)、搜索框(放大镜+占位)、收藏置顶分组、空态提示、分类手风琴(图标+专属色+数量+chevron)、列表/网格双模板、悬停预览(Description+Input/Output端口)、搜索高亮；背景改 `{StaticResource ScadaPanelBackgroundBrush}` 并就地合并 `Colors.xaml`。
- `VisionMaster\ViewModels\ToolViewModel.cs`：加 `ViewMode`+切换命令、`Favorites`+`ToggleFavoriteCommand`+`FavoritesVersion`、收藏分组；`FilteredToolBarSource` 元素类型换为 `ToolboxGroupViewModel`（保持 `TotalCount`/`SearchText` 语义，G9 兼容）。
- 新增 `ViewModels\ToolboxGroupViewModel.cs`（Name/Icon/AccentBrush/Children/Count）。
- 新增 `Helpers\ToolboxCategoryVisuals.cs`（13 类 → FA 图标码点 + 颜色）。
- 新增 `Helpers\IsFavoriteConverter.cs`（IMultiValueConverter：item+VM+版本号→星标态）。
- 新增 `Helpers\HighlightTextBlock.cs`（搜索高亮附加属性）。
- `Core\Models\AppConfigModel.cs`：加 `ToolboxSettings Toolbox { get; set; } = new();` + 节类 `{ List<string> Favorites }`。
- `Core\AppSettingsService.cs`：加 `SyncToolboxFavorites(IEnumerable<string>)`（set + Save）。
- `FlowCanvasChecks\Program.cs`：扩展 G9（收藏切换/置顶分组/视图模式/映射表覆盖/JSON 往返）。

## 关键决策
1. **不包裹 view model**：drop 端硬认 `ToolItemModel` 本体（FlowCanvasView.xaml.cs:274-296），条目数据保持原始 `ToolItemModel`，拖拽链路零改动；星标态用 MultiValueConverter + `FavoritesVersion` 刷新。
2. 收藏键用 `ModuleTypeName`（非 `Id`，其恒为 Guid.Empty），读取时对当前键集过滤容错。
3. 持久化走 `AppConfig.json`（随用户口径），构造函数加**可选** `AppSettingsService settings = null` 保 G9 不破。
4. 分类色由 VM 暴露 `Brush`（先例 ShellViewModel.RunStateBrush）；令牌一律 `...Brush` 结尾。
5. 网格用 `ItemsControl+UniformGrid`（不用 ListView+GridView，避开 R19-R22）。
6. `ToggleFavoriteCommand` 用 `DelegateCommand<object?>`+首行判型（R27）。

## 纪律核对
R27 命令判型；R30-R32 颜色收口/命名/作用域；R19-R22 不用 GridView；`UIThemeSmokeTest`(*Color 不接 Brush / 无 h:*.Style)；视图用 `{ui:BooleanToVisibilityConverter}` 不用 `{StaticResource ...}`；主题字典键不重复（令牌倾向放 ToolView 本地 Resources）。

## 验证
构建解决方案 → 跑 FlowCanvasChecks(含 G9)/UIThemeSmokeTest/ScadaChecks → 目视列表·网格·预览·收藏·高亮 + 拖算子到画布仍可用。

## 派工
ui 实现 → reviewer 审查(三红线+拖拽+R27+R30-R32+G9) → docs 补 code-changes 记录。

## 假设
收藏按加入顺序；网格 2 列；预览浮层在条目左侧；分类映射覆盖 13 类，未知分类回落通用图标+灰。