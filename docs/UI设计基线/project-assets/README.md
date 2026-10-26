# 项目资产管理 · Web 设计基线

- 版本：v0.4（Shell 内页浅色 + 多资产类型演示数据）
- 日期：2026-10-26
- 页面：项目资产管理 / Project Assets
- 状态：**WPF 演示版已落地（仅 UI + 内存假数据）**；真实接口与权限审计未接
- 设计稿：`design.html`（1920×1080，纯 HTML/CSS，无外部依赖）
- WPF 演示入口：主界面 `☰ → 系统 → 项目资产`；实现见 `docs/code-changes/2026-10-26-项目资产管理WPF演示版.md`
- 历史稿：**v0.2 深色独立壳**已归档为 `design-v0.2-dark.html` 与 `evidence/v0.2-dark/`，仅作对比，不是复刻基线。

## 1. 目标

把标定、模板、模型、配方、图像资产统一成“方案级资产”：

- 资产有稳定 Id、名称、作用域（产线 / 工位 / 相机）、版本、生命周期、冻结状态与引用关系；
- 生产流程按 Id 引用有效版本，不在每件节拍里重算；
- 标定资产支持“Calib 计算写入 / ST3 引用加载”；
- 删除、归档、冻结前能看到引用数与影响面，避免断链。

v0.4 在标定资产完整模板之外，新增 **模板 / 模型 / 配方 / 图像** 四类演示数据；所有参数取自当前 `.vms` 与插件步骤配置，未配置的模型/模板明确标“未绑定 / 待学习”，不伪造可用状态。

## 2. 四路专家评审结论

评审角色：2 位 HALCON 资深专家（标定/测量、算法/模型）+ 2 位视觉开发工程师（产线集成、WPF 落地）。

### 2.1 标定专业意见（已改）

- **坐标系错误**：HALCON / `CalibrationTransform` 使用 `(Row, Col)`，Row 向下、原点左上。原稿 `X/Y` 向上、A/B 标注 `(240,100)/(240,200)` 是反的。已改为 `A (R240, C100) px`、`B (R240, C300) px`，坐标轴标 `R ↓` / `C →`。
- **精度表达失真**：两点像素当量只有 1 个参数、自由度 0，不可能有 `RMS 0.000 px`。已改为：已知长度 `10.000 mm`、基线 `200.00 px`、当量 `0.0500 mm/px`；残差显示“**不适用（2 点 / 1 参数）**”，另给不确定度 `U = ±0.0008 mm/px (k=2)`。
- **缺标定依据/验收**：补标定板编号、已知长度、物距 WD、镜头/光圈、环境温度、复核人、有效期、判定结论；不同标定方式按 Kind 显示不同残差字段（本页为 PixelScale，故 RMS 不适用）。
- **状态混轴**：原“有效 / 待复核 / 已锁定 / 已归档”混在一起，且锁定用红色。已拆为：生命周期（有效 / 待复核 / 已归档）+ 保护（已冻结，中性蓝灰）；健康度只统计当前页签 `4 有效 / 6 = 67%`。

### 2.2 算法/模型与集成意见（已改）

- 列表增加**工位 / 相机**列，资产名、工位、相机、类型、版本、状态、引用、更新时间各归其位；引用从“3 处”改为 `读1 · 写1 · 调试1`，详情拆成“生产读取 / 标定写入 / 调试入口”，并写清按 Id 固定版本、上次加载时间。
- “资产已加载”改为“**静态图预览 · 只读**”，不再暗示运行期已加载；预览显示图像坐标、基线、当量来源。
- 新建入口改为三段式（相机与工位 / 标定依据 / 资产信息），补工位、相机序列号、标定板、已知长度、物距、温度、复核信息；资产 Id 只读；主按钮改为“**创建草稿并开始标定**”，明确草稿 → 计算 → 复核 → 有效。
- 危险/受控操作统一为“设为 ST3 默认 / 冻结版本 / 导出资产包 / 复制 Id”；删除、归档、回滚在契约补齐前不放主界面。

### 2.3 WPF 落地意见（已写进边界）

- 本稿是**页面内容概念**，不是应用外壳：WPF 复刻时不要复制左侧 76px 导航、顶栏、底栏，这些归 `Shell`；页面作为 Shell 文档区内容。
- 1920×1080 只是设计基准，**禁止 1:1 等比缩放到 1366/1280**。WPF 重排建议：≥1680 三栏 `240 / 1fr / 420`；1366–1679 两栏 + 420 详情抽屉；<1366 筛选浮层、列表单列、行高 44。
- Web 稿已加 `1500 / 1280 / 980` 三档断点：≤1500 收窄三栏并改用紧凑列宽（保留全部列），≤1280 隐藏左筛选栏，≤980 详情堆叠到列表下方；这是防止布局破碎的兜底，不等于 WPF 复刻方案。
- 不用 9.5px 正文、不用 27px 图标按钮（R68）、不用渐变/毛玻璃/conic-gradient 作为 WPF 令牌；颜色/间距/圆角全部落 `Colors.xaml` 与 `UI/Controls/Themes`。
- 详情底部的“设为默认 / 冻结 / 导出 / 复制 Id”已改为固定底栏，列表滚动不丢操作。

### 2.4 v0.3 主题与壳层修正

v0.2 是独立深色 Web 控制台（`#070D16` + `#22D3EE` + 左侧 76px rail + 顶栏/底栏），与现有桌面端不搭配：现有 Shell 是浅色 Fluent，面板白底、主色 `#005FB8` / `#3182CE`，而且左侧 rail / 顶栏 / 状态栏由 Shell 提供，不能画进资产页。

v0.3 的修正：

- 资产页只保留文档内容，移除 rail / topbar / statusbar 的视觉，页面直接嵌入 Shell 文档区。
- 全部颜色改用现有令牌：`PanelBackgroundBrush #FFFFFF`、`ScadaPanelBackgroundBrush #F7F8FA`、`AccentColor #005FB8`、`AccentBlueBrush #3182CE`、`ControlBorderBrush #D1D1D1`、`TextPrimaryBrush #202020`、`ScadaSelectedRowBrush #ECF5FF`、`ScadaHoverBrush #E3EEF9`。
- 主按钮改纯色 `#005FB8`，去掉深色稿的青色渐变/发光/毛玻璃；卡片圆角收 8px，阴影减弱到 `0 2px 8px rgba(15,23,42,.05)`。
- 现有 SCADA 运行画面仍是深色，但资产页属于工程工具，不套用 SCADA 深色 token。

## 3. 画面结构

| 区域 | 内容 |
|---|---|
| 左侧导航 | 项目总览 / 流程设计 / 视觉工具 / **项目资产** / 数据报表 / 设置 |
| 顶部 | 面包屑、全局搜索（Ctrl K）、自动保存、当前用户 |
| 页头 | 标题、说明、导入资产、新建标定资产 |
| 页签 | 资产总览 27 / 标定资产 6 / 模板资产 4 / 模型资产 5 / 配方资产 2 / 图像资产 10（演示数量） |
| 左侧筛选 | 资产类型、生命周期、保护（已冻结）、相机、标签、当前页签健康度 |
| 中央列表 | 资产名、工位 · 相机、类型、版本、状态、引用（读/写/调试）、最近更新 |
| 右侧详情 | 四页签：**概览**（只读预览 / 标定依据 / 关联对象）、**技术参数**（相机采集 / 标定结果 / 文件契约）、**引用**（读 / 写 / 调试影响面）、**版本**（版本卡 / 差异 / 回滚）；底部固定操作栏 |
| 底部状态栏 | 不属于本页；由 Shell 提供 |
| 弹窗 | 新建标定资产：01 相机与工位 / 02 标定依据 / 03 资产信息 |

### 3.1 可承载的程序信息

| 类别 | 可放什么 | 数据源 | 口径 |
|---|---|---|---|
| 静态方案数据 | 相机型号/序列号/驱动、运动卡/轴、通讯配置、流程/步骤、变量定义、配方、SCADA 页面、标定变换 | `SolutionModel.CameraConfigs` / `MotionCards` / `CommunicationConfigs` / `Flows` / `VariableSnapshots` / `Recipes` / `Scada` / `Calibrations` | 随 `.vms` 落盘，可直接展示 |
| 运行态 | 相机在线、PLC 连接、轴使能、当前配方、最近一次运行结果、节拍 | 运行时 Provider / 服务 | 不进 `.vms`；标“实时/只读”，处理断线与超时 |
| 派生数据 | 引用数、读/写/调试分类、健康度、最近使用时间 | 扫描流程/SCADA/配方 + 运行记录 | 需要索引或统计服务；当前未落地 |
| 审计/日志 | 谁在何时改了资产、冻结/回滚记录 | 审计服务 / `ILogService` | 不在 `CalibrationAsset` 里，需单独服务 |
| 外部业务数据 | MES 工单、良率、追溯记录 | MES / 数据库 | 只读，需权限与脱敏；不建议混进资产主键 |

> 结论：**能放，但不能把“资产页”做成所有数据的垃圾桶**。静态数据进概览/技术参数，引用影响进引用页，版本/审计进版本页，运行态单独标注并默认折叠。

### 3.2 v0.4 演示数据来源

| 页签 | 演示数量 | 数据依据 |
|---|---:|---|
| 标定资产 | 6 | 当前 `.vms` 实际只有 `LineCalib` 一个；另外 5 条为界面示例，字段仍按 `CalibrationAsset` / `CalibrationTransform` 口径 |
| 模板资产 | 4 | `BeadA`（`RecipeLibraryJson`）、`Board01`（`ST2_Position` 几何查找）、`OCR_Lot`（OCR 参数）、`TemplateLib`（`TemplateLibraryJson=[]`） |
| 模型资产 | 5 | `AI_Extension` 的 YOLO / 异常检测 / 分类 / 分割槽位，以及 ST1/ST6 的 OCR 槽位；当前 `ModelPath` 均为空，状态标“未绑定” |
| 配方资产 | 2 | `BeadA`（`ST5_Assembly` 胶路检测的 RecipeLibraryJson）与 `Recipe_A`（`Recipe` 流程写入的 ProductType/BatchNo） |
| 图像资产 | 10 | 当前方案 `Image\` 目录中的实际文件：分辨率、字节数、SHA-256 前 8 位均来自磁盘 |

> 当前 `.vms` 统计：Calibrations 1、CameraConfigs 3、MotionCards 1、Flows 14、VariableSnapshots 95、Scada 15 页 / 371 图元；`Recipes` 表未启用。演示中的 6 个标定资产是 UI 样例，不代表方案实际有 6 份标定。

## 4. 设计令牌（对齐现有主题）

| 令牌 | 值 | 对应现有资源 |
|---|---|---|
| 画布底色 | `#F3F6FA` | Shell 文档区浅灰底 |
| 面板表面 | `#FFFFFF` | `PanelBackgroundBrush` / `ScadaSurfaceBrush` |
| 面板浅底 | `#F7FAFC` / `#F8FAFC` | `ExpanderHeaderBrush` / `ScadaPanelBackgroundBrush` |
| 边框 | `#E2E8F0` / `#D1D5DB` | `ToolBorderBrush` / `ControlBorderBrush` |
| 主文字 | `#202020` / `#2D3748` | `TextPrimaryBrush` / `PrimaryTextBrush` |
| 次文字 | `#606266` / `#66708A` | `ScadaLabelTextBrush` / `ScadaSecondaryTextBrush` |
| 主色 | `#005FB8` | `AccentColor` |
| 次强调 | `#3182CE` / `#3B82F6` | `AccentBlueBrush` / `ScadaAccentBrush` |
| 成功 | `#16A34A` / `#DCFCE7` | 绿色状态 |
| 警告 / 待复核 | `#B45309` / `#FEF3C7` | 琥珀状态 |
| 冻结 / 保护 | `#475569` / `#E2E8F0` | 中性，不用红色 |
| 危险（仅删除等） | `#DC2626` | 红色状态 |
| 选中行 | `#ECF5FF` | `ScadaSelectedRowBrush` |
| hover | `#E3EEF9` | `ScadaHoverBrush` |
| 圆角 | 卡片 8px / 输入 6–8px / 小标签 5–6px | Fluent 轻圆角 |
| 布局 | 基准 1920×1080；左栏 240px，中间自适应，右栏 440px | Shell 文档区内容 |

## 5. 交互状态

- 列表行 hover：`#E3EEF9` 浅蓝底；选中：`#ECF5FF` 浅蓝底 + 左侧 3px `#005FB8` 主色条。
- 生命周期：有效（绿）、待复核 / 待学习（琥珀）、已归档（灰）、未绑定（浅灰）；保护：已冻结（中性蓝灰，独立徽标）。
- 详情四页签：概览 / 技术参数 / 引用 / 版本；默认概览；切换资产行时回到概览。
- 顶部页签可切换全部 / 标定 / 模板 / 模型 / 配方 / 图像；列表、详情、类型筛选和健康度同步刷新。
- “新建标定资产”打开 760px 模态；三段式表单，字段：工位、相机、序列号、标定方式、标定图、标定板 / 已知长度、物距、温度、资产名、只读 Id、说明。
- 主界面按钮：导入资产（次要）、新建标定资产（主按钮）。
- 详情操作：设为 ST3 默认、冻结版本、导出资产包、复制 Id；主操作与危险操作分区，冻结/归档前需二次确认（契约待补）。

## 6. 证据

- `evidence/project-assets-main.png`：1920×1080 概览页签（v0.4 浅色 Shell 内页）
- `evidence/project-assets-tech.png`：标定资产技术参数页签
- `evidence/project-assets-refs.png`：标定资产引用页签
- `evidence/project-assets-versions.png`：标定资产版本页签
- `evidence/project-assets-templates.png`：模板资产演示
- `evidence/project-assets-models.png`：模型资产演示
- `evidence/project-assets-recipes.png`：配方资产演示
- `evidence/project-assets-images.png`：图像资产演示
- `evidence/project-assets-1366.png`：1366×768 紧凑断点（保留全部列）
- `evidence/project-assets-create-modal.png`：新建标定资产弹窗（v0.4 浅色）
- `evidence/v0.2-dark/`：v0.2 深色独立壳历史截图（仅对比）

## 7. 契约缺口（设计已表达，后端未实现）

> 这些字段和动作在界面里有位置，但当前代码不承诺已支持；WPF 复刻前必须先补契约，否则只能做静态壳。

| 缺口 | 现状 | 设计需要 |
|---|---|---|
| 资产生命周期 | `CalibrationAsset` 只有 Id / Name / Description / IsLocked / Version / Transform | `Status`（草稿 / 有效 / 待复核 / 已归档）、`CreatedAt/UpdatedAt/UpdatedBy`、`ReviewedBy/ReviewedAt`、`ValidUntil` |
| 统一资产描述 | 只有 `CalibrationAsset`；模板/模型/图像没有顶层资产类 | `AssetDescriptor`（Id / Kind / Name / Scope / Version / Status / ContentHash / FilePath / ValidUntil） |
| 标定依据 | `CalibrationTransform` 只有结果矩阵/内参/残差 | `StandardId`、`KnownLengthMm`、`WorkingDistanceMm`、`Lens/Aperture`、`TemperatureC`、`Uncertainty` |
| 引用索引 | 无 `GetReferences` | 按资产 Id 反查流程/步骤/设备/画面，区分读 / 写 / 调试 |
| 版本快照 | `SolutionCalibrationCatalog.Save` 直接覆盖 Transform，仅 `Version++` | 每次保存留存旧 Transform / 元数据快照，支持 diff、回滚、导出 |
| 受控动作 | `ICalibrationCatalog` 只有 `All / TryGet / TryGetByName / Save` | `Delete / Archive / SetLocked / GetVersions / Rollback / GetReferences`，全部带权限与审计 |
| 默认资产 | 无“工位默认”概念 | 每工位/相机默认资产指针，切换需复核 |
| 版本比较 | `SameTransform` 未比较 `Intrinsics` 与残差字段 | 补齐比较后再谈“内容变更才升版” |

## 8. 已知边界

- 本目录的 `design.html` 是 **Web 设计基线**；WPF 演示版已按 §9 落地，但真实资产接口仍未接。
- 标定资产是完整模板；模板 / 模型 / 配方 / 图像已有演示详情，但还没有统一的后端资产契约与真实引用索引。
- 模型 / 模板 / 图像目前仍嵌在流程步骤配置或方案目录中；要做成可冻结、可回滚的资产，需要新增统一的资产描述与文件哈希/版本字段。
- 技术参数里的相机采集参数、SHA-256、HALCON 版本、引用索引与版本卡为设计示例；真实值需接 `CameraConfigs`、资产文件元数据和引用扫描服务。
- 运行态（相机在线 / 帧率 / 温度 / 节拍）未接入本页，后续应单独成块并标“实时/只读”。
- 健康度 `4 有效 / 6` 与 `67%` 是当前页签示例数据，真实口径以资产目录统计为准。
- 1920×1080 是主设计基准；1366 / 1280 的 WPF 重排规则见 §2.3，HTML 已有兜底断点，但本轮未出窄屏证据图。
- 新建弹窗的“创建草稿并开始标定”是流程提案；当前 `Save` 要求已有 Transform，无法保存空资产，需先补草稿生命周期。

## 9. WPF 实现（真实数据 Phase 1+2，2026-10-26）

- 视图：`VisionMaster/Views/ProjectAssetsView.xaml(.cs)`
- ViewModel：`VisionMaster/ViewModels/ProjectAssetsViewModel.cs`
- 目录：`VisionMaster/Services/SolutionProjectAssetCatalog.cs`；导入：`VisionMaster/Services/ProjectAssetImporter.cs`
- 宿主接线：`Shell.xaml` 系统菜单“项目资产”、`ShellViewModel.OpenProjectAssetsCommand`、`App.xaml.cs` 注册 `ProjectAssetsView`。
- 入口：主界面 `☰ → 系统 → 项目资产`（窗口级快捷键 `Ctrl+Alt+A`）。
- 窗口：1280×850 固定窗口；三栏 + 顶部类型页签 + 详情四页签 + **根层右下角固定操作卡**（四个按钮 `Height=28 Padding="6,0" FontSize=12`，外层 `Margin="0,0,20,24"`）。
- 数据：当前方案真实数据，标定 1 / 模板 0 / 模型 0 / 配方 1（BeadA 内嵌）/ 图像 14（流程引用）；不再是 28 条模拟数据。
- 导入：模板 / 模型 / 图像页签可导入文件到方案目录；SHA-256、大小、图像分辨率、重复内容拒绝；保存方案后随 `.vms` 落盘。
- 空态：模板/模型未导入时显示“当前页签暂无资产”，不再出现空白详情头或标定预览。
- 列表徽标：类型 / 状态统一 22px 高、`Padding="8,0"`、`VerticalAlignment=Center`，内层文字垂直居中。
- 明确未做：插件配置界面的资产下拉选择器、冻结/回滚、默认资产持久化、权限/审计、版本历史。
- 插件接线：Matching `TemplateAssetId`、OCR/YOLO/DL `ModelAssetId` 已支持运行期按 Id 解析；旧路径仍可回退。

