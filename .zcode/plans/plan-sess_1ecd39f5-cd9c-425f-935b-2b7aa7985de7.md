把左侧**流程栏**（`VisionMaster\Views\ProcessView.xaml`）换皮成设计稿的大纲观感。**画布不动**。颜色全部走现有令牌。

## 改动清单

**① 给模型补一个分类字段（用于"分类色图标"）**
- `Core\Models\ProcessStep\StepModel.cs` — 加 `public string Category { get; set; }`（可空）。
- `VisionMaster\Services\StepFactory.cs:28 CreateFromTool` — 加一行 `Category = tool.Category`。
- 只加一个字段 + 一处带入，**不改任何行为**。

**② `VisionMaster\Views\ProcessView.xaml` 换皮**
- **删掉搜索行**（`Grid.Row=0` 那块 + 对应 `RowDefinition`）：搜索框 +「上一个」+「下一个」+「失败跳转」。
- **算子行**（`ActionStep` 模板 :300）：改成「折叠箭头 + 状态点 + **分类色图标** + 两行文字（`StepName` / **`PluginName`**）+ 右侧耗时胶囊（已有，保留）+ 失败红 ×」。
  - 第二行由现在的 `Description` 改绑 **`PluginName`**（友好类型名，如「延时等待」）——设计稿要的"类型名小字"。
  - 图标前景走 `ToolboxCategoryVisuals.BrushFor(Category)`（复用现成的 `CategoryAccentBrushConverter`）。
- **容器头**（If/While/For/并行/分支匹配 5 个模板）：把「状态 glyph + 身份条 + 图标 + 中文标签 + `-[ 名称 ]`」收敛成设计稿那种「**类型标签 chip + 实例名**」，保留按容器类型的身份色。
- **分支胶囊**（`StepCollection` :776）：外观收成「圆点 + `DisplayName` + `N 算子` pill」，**文案仍只有 `DisplayName` 一个来源**（R24）。
- **颜色收口**：把 `ProcessView.xaml` 里的硬编码色（`#F5F7FA` / `#E8F1FC` / `#F0F2F5` / `#B8C4CE` / `#F0F9EB` / `#B3E19D`）换成现有令牌。
- **必须保留**：`x:Name="StepTree"`、**全部 `DataType` 模板**（R23 守门人 U1-U5）、分支胶囊右键菜单、缩进 + 层级引导线机制、TreeView 的拖拽四件套与输入绑定。

## 红线
- **R23**：不新增/改名 `StepModel` 子类 → 不新增模板；现有 `DataType` 模板一个不少。
- **R24**：分支胶囊文案只有 `StepCollection.DisplayName` 一个来源（不再挂独立 `Expression` 文本）。
- **R25**：分支卡片不进命令链（本次不动）。
- `ProcessTreeTemplateChecks` U1–U5 必须继续绿。

## 你要知道的后果（已按你的选择执行）
1. **「失败跳转」永久开启**：删掉 checkbox 后 `AutoJumpToFailure`（默认 `true`）**再无 UI 可关**。VM 保留（G4 断言仍在测它，删了会红）。
2. **搜索定位 UI 不可达**：`SearchText` / `SearchPreviousCommand` / `SearchNextCommand` 保留在 VM（G3 仍绿），界面上没有入口。
3. **`StepModel.Category` 是序列化契约的新增字段**：老 `.vms` 没有它 → 读出来为 null → 流程栏回落中性色（不崩）。只有"从工具箱拖入"的步骤有分类；粘贴 / 程序创建 / 老方案的步骤可能为空。

## 明确不做（设计稿里有、本次不抄）
- **完成状态点 / 耗时胶囊**：不加新数据（耗时胶囊现有，保留）。
- **容器卡左上角序号角标**：需要"顶层序号"，现有 `SortId` 语义不符（是布局排序号，不是执行序），不新增字段。
- **画布**：完全不碰（web 是"拓扑竖链自动布局"，桌面是 Nodify 自由拖拽 + 顺序链 + 泳道 + 断点，两种范式；`flowLayout.ts` 不移植）。

## 验证
`dotnet build VisionMaster.sln` → `FlowCanvasChecks`（U1-U5 / V1-V9 / G3 / G4）/ `ScadaChecks` / `UIThemeSmokeTest` 全绿 → 渲染探针场景 F（`--render` → `process-tree-render-F.png`）目视 → **真机打开流程栏目视**（该视图无渲染断言覆盖，模板错只有真机才炸）。

## 派工
ui 实现 → reviewer 审查（U1-U5 模板齐全 / 红线 R23-R25 / `Category` 序列化往返 / 硬编码色清零 / 搜索行确实只删 UI 未动 VM）→ docs 补 `docs/code-changes/`。