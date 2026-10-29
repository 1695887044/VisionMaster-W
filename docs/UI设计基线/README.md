# UI 设计基线（Web 形式）

本目录存放**以 Web 形式表达的设计基线**，用于界面设计与确认。

## 定位（必读）

- 这里的 HTML/截图/原型是**设计媒介**，**不是交付物**：
  - 不进入任何工程构建，不参与打包，不部署；
  - 不作为"另一个客户端"维护；
  - 产品交付物只有四类：WPF 代码、令牌映射表、截图证据、差异清单（见 `AGENTS.md` §30.1）。
- 设计基线是**只读**的：确认后冻结版本；需要改设计时升版本（`v1` → `v2`），并同步更新已复刻的桌面端页面。

## 流程

```text
设计基线（Web 形式） → 令牌映射 → 桌面端复刻 → 截图验收
```

没有设计基线，不得直接改桌面端样式。

## 目录约定

```text
docs/UI设计基线/
  README.md                 本文件（总约定）
  <页面名>/
    README.md               版本号、确认日期、确认人、覆盖范围、已知偏差
    design.html             可点击设计基线（自包含，无外部依赖）
    evidence/               桌面端复刻后的对照截图（改前 / 改后 / 差异标注）
```

## 现有基线

| 页面 | 版本 | 覆盖范围 | 状态 |
| --- | --- | --- | --- |
| `process-panel/` | v2 | `ProcessView.xaml`（步骤树 + 底部统计） | 已复刻，证据 `process-panel/evidence/after-render-F.png` |
| `global-variables/` | v3.2 | `VisionMaster\Views\DialogViews\GlobalVariableView.xaml`（变量管理：来源导航 + 变量明细）+ `ArrayEditorView.xaml`（数组编辑弹窗，v3 专项优化）+ `DeviceVariableExportView.xaml` / `DeviceVariableImportView.xaml`（单设备变量表批量导入 / 导出，v3.2 增量）；左栏二级分组树 / 「分组」列 / 管理分组覆盖层（v3.1 增量） | 已复刻（v1 2026-10-20 / v2 2026-10-21 / v3 2026-10-21 / v3.1 2026-10-21 / v3.2 2026-10-21）；设计证据 `global-variables/evidence/design-v1-*.png`、`design-v2-*.png`、`design-v3-*.png`；WPF 证据 `after-render-v1-*.png`、`after-render-v2-*.png`、`after-render-v3-*.png`；v3.1 / v3.2 增量见下方两行 |
| `global-variables/`（v3.1 显示分组） | v3.1 | 左栏二级分组树 + 表格「分组」列 + 管理分组覆盖层；只做归类与筛选，不影响采集 / 轮询 / 写入；扫描组编辑入口收敛到通讯设置 | 已复刻（2026-10-21，实现驱动）；详见 `global-variables/README.md` §「v3.1 显示分组」；WPF 证据 `global-variables/evidence/after-render-v1-group-editor.png` |
| `global-variables/`（v3.2 批量导入 / 导出） | v3.2 | `DeviceVariableExportView.xaml` / `DeviceVariableImportView.xaml`：单设备网络变量表 xlsx 批量导出（全部 / 当前显示分组）与导入（按变量名匹配、差异预览、事务应用）；入口仅网络来源 | 已复刻（2026-10-21，先出 Web 设计稿 `variable-batch-io-design.html`）；详见 `global-variables/README.md` §「v3.2 批量导入 / 导出」；证据 `global-variables/evidence/after-render-v1-export.png`、`after-render-v1-import.png` |
| `variable-binding/` | v1 | `VisionMaster\Views\DialogViews\VariableBindingView.xaml`（变量绑定：输入端口 / 数据来源 / 绑定设置三栏） | 已复刻（2026-10-20）；设计证据 `variable-binding/evidence/design-v1-*.png`；WPF 证据 `variable-binding/evidence/after-render-v1-*.png`；验证 `UIThemeSmokeTest --variable-binding` 全部通过 |
| `condition-editor/` | v2 | `VisionMaster\Views\DialogViews\ConditionEditorView.xaml`（双击逻辑算子弹出的条件窗口：If/While 条件、分支匹配、等待条件、计次循环四模式；1120×760 + 960 最小档） | 已复刻（2026-10-20），设计证据 `condition-editor/evidence/design-v2-*.png`；待用户真机确认 |
| `solution-list/` | v1 | `VisionMaster\Views\DialogViews\SolutionListView.xaml`（方案列表：清单表格 + 右侧动作栏 + 默认启动状态条；960×640 / 900×560） | 已复刻（2026-10-20）；设计证据 `solution-list/evidence/design-v1-*.png`；WPF 证据 `after-render-v1-*.png`；待用户真机确认 |
| `io-health/` | v1 | ⚠ **已失效**：原覆盖 `VisionMaster\Views\DialogViews\IoHealthView.xaml`，该视图**已不存在**（IO 域随 IO 接入层下线；`IoHealth` / `SystemHealth` 枚举成员与旧转发分支已删除，系统健康只经硬件管理打开 `SystemHealthView`） | 待归档，不参与任何复刻 |
| `app-shell/` | v2 | `Shell.xaml` 及三场景工作区（调试 / 运行 / 组态 + 自由布局）、单列检查器、可收起控制台、运行条、运行驾驶舱、命令面板；v1 归档在 `design-v1.html` | 待用户确认 |
| `app-shell-narrow/` | v0 草案 | 非全屏（1310×829 DIP）图像区布局：现状 604×464 / A 控制台 32 + 右栏 300（+41%）/ B 专注图像（+109%）三帧对照与面积账；VS 式胶囊交互稿 | **A 已实施（2026-10-26）**：控制台默认 32、右栏 300、`ConsoleState` 持久化；VS 胶囊已实施并修复（2026-10-26：自动隐藏第三态 + 点击链路，真 exe 通过；`UIThemeSmokeTest` 135/0）；浮层未做；B 未做 |
| `toolbox-plugin/` | v2（胶囊 v2.1 子修订） | `VisionMaster/Views/ToolView.xaml`（右侧插件工具箱：工具条 / 搜索 / 收藏提示 / 分组手风琴 / 列表 / 网格 / 空态 + 收起胶囊 16×48~72）；只做样式与密度，不改拖拽/预览/收藏/分组/视图切换行为 | **已确认并复刻（2026-10-27）**：设计证据 `toolbox-plugin/evidence/design-01-overview.png`、`design-02-specs.png`；真机证据 `after-toolbox-300.png`、`after-capsule-16.png`、`before-capsule-overlap.png`；`_ToolboxProbe` 33/0、`UIThemeSmokeTest` 143/0、`run_gates` 14/14；实现记录 `docs/code-changes/2026-10-27-插件工具箱v2 WPF复刻（紧凑工具条与胶囊）.md`、`docs/code-changes/2026-10-27-工具箱胶囊与库自动隐藏页签重叠修复.md`；待用户按 R132 最终验收 |
| `image-acquisition/` | v2 | `Plugin.ImageAcquisition\ImageAcquisitionView.xaml`（配置窗口内容区 + 960×680 宿主外壳；v1 归档在 `design-v1.html`） | 已复刻，证据 `image-acquisition/evidence/after-render-v2-*.png`；待用户确认 |
| `auto-port-config/` | v1 | `VisionMaster\Views\DialogViews\AutoPortConfigView.xaml`（无自定义视图插件的框架兜底参数面板：步骤卡 + 输入端口清单 + 空态/执行态） | 已复刻（2026-10-20）；设计证据 `auto-port-config/evidence/design-v1-*.png`；WPF 证据 `wpf-after-render-v1-*.png`；待用户确认 |
| `yolo/` | v1 | `Plugin.Yolo\YoloView.xaml`（YOLO 目标检测配置：图像输入 / 模型 / 检测参数 / 高级参数 / 试算 + 只读预览） | 已复刻，证据 `_batch-evidence/Plugin.Yolo-YoloPlugin.png`；待用户确认 |
| `dl-classify/` | v1 | `Plugin.DlClassify\ClassifyView.xaml`（深度学习分类配置：图像输入 / ONNX 模型 / 缩放模式与 TopK / 试算 + 只读预览） | 已复刻，证据 `dl-classify/evidence/initial-render-920x520.png`；待用户确认 |
| `hand-eye/` | v2 | `Plugin.HandEye\HandEyeView.xaml`（**拟建**，插件当前无视图）：顶部工具条（装法 / 两个残差上限 / 组数门 / 导入导出 CSV / 求解）+ **全宽 12 列成对数据表（本页主体，可手填）** + 只读结果区（4×4 矩阵 + 三枚残差 KPI + 红黄两档失败态） | **仅设计基线，尚未复刻**；四态可点（默认 / 空态 / 失败 / 窄档 640）；Q1 已由用户拍板改为"表格为主、CSV 为扩展"，其余五项待确认 |
| `variable-assignment/` | v1 | `Plugin.Utility\VariableAssignmentView.xaml`（变量赋值配置） | 已复刻，证据 `variable-assignment/evidence/`；待用户确认 |
| `code-reader/` | v1 | `Plugin.CodeReader\CodeReaderView.xaml`（读码配置：图像输入 / 码制 / 参数 / 试算） | 已复刻，证据 `code-reader/evidence/`；待用户确认 |
| `excel-export/` | v1 | `Plugin.ExcelExport\ExcelExportView.xaml`（报表导出配置） | 已复刻，证据 `excel-export/evidence/`；待用户确认 |
| `image-script/` | v1 | `Plugin.ImageScript\ImageScriptView.xaml`（图像脚本配置） | 已复刻，证据 `image-script/evidence/`；待用户确认 |
| `csharp-script/` | v1 | `Plugin.CSharpScript\CSharpScriptView.xaml`（C# 脚本配置） | 已复刻，证据 `csharp-script/evidence/`；待用户确认 |
| `result-display/` | v1 | `Plugin.ResultDisplay\ResultDisplayView.xaml`（结果显示/烘焙配置） | 已复刻，证据 `result-display/evidence/`；待用户确认 |
| `data-record/` | v1 | `Plugin.DataRecord\DataRecordView.xaml`（数据记录配置） | 已复刻，证据 `data-record/evidence/`；待用户确认 |
| `result-upload/` | v1 | `Plugin.ResultUpload\ResultUploadView.xaml`（结果上传配置） | 已复刻，证据 `result-upload/evidence/`；待用户确认 |
| `color-region/` | v1 | `Plugin.ColorRegion\RegionColorView.xaml`（颜色区域配置） | 已复刻，证据 `color-region/evidence/`；待用户确认 |
| `ocr/` | v1 | `Plugin.Ocr\OcrView.xaml`（OCR 配置） | 已复刻，证据 `ocr/evidence/`；待用户确认 |
| `create-roi/` | v1 | `Plugin.CreateRoi\CreateRoiView.xaml`（ROI 创建配置） | 已复刻，证据 `create-roi/evidence/`；待用户确认 |
| `color-check/` | v1 | `Plugin.ColorCheck\ColorCheckView.xaml`（颜色序列检查配置） | 已复刻，证据 `color-check/evidence/`；待用户确认 |
| `pose-transform/` | v1 | `Plugin.PoseTransform\PoseTransformView.xaml`（位姿变换配置） | 已复刻，证据 `pose-transform/evidence/`；待用户确认 |
| `pre-processing/` | v1 | `Plugin.PreProcessing\PreProcessingView.xaml`（图像预处理配置） | 已复刻，证据 `pre-processing/evidence/`；待用户确认 |
| `caliper-measure/` | v1 | `Plugin.CaliperMeasure\CaliperMeasureView.xaml`（卡尺测量配置） | 已复刻，证据 `caliper-measure/evidence/`；待用户确认 |
| `matching/` | v1 | `Plugin.Matching\MatchingView.xaml`（模板匹配配置） | 已复刻，证据 `matching/evidence/`；待用户确认 |
| `calibration/` | v1 | `Plugin.Calibration\CalibrationView.xaml`（标定配置） | 已复刻，证据 `calibration/evidence/`；待用户确认 |
| `blob-detect/` | v1 | `Plugin.BlobDetect\BlobDetectView.xaml`（斑点检测配置） | 已复刻，证据 `blob-detect/evidence/`；待用户确认 |
| `bead-inspect/` | v1 | `Plugin.BeadInspect\BeadInspectView.xaml`（胶路检测配置） | 已复刻，证据 `bead-inspect/evidence/`；待用户确认 |
| `log-console/` | v1 | `VisionMaster\Views\LogView.xaml`（过滤工具条 + 日志区）+ `UI\Controls\Themes\LogConsole.xaml`（控件皮肤）；等级筹码、错误行整行底、无结果/空态、行选中 | **已复刻（2026-10-22）**；两文件 `hex=0`；日志冒烟全绿；渲染证据 `docs/UI样式参考/logview.png`、`logview_narrow.png` |
| `watch-panel/` | v1 | `VisionMaster\Views\MonitorView.xaml`（添加监视工具条 + 分组监视列表 + 底部计数）；联想下拉分组/空态、方向徽标、失效行 | **已复刻（2026-10-22）**；`hex=0`；新增 `MissingCount` / `Name` / `KindLabel` |
| `flow-list/` | v2.1 | `VisionMaster\Views\FlowListView.xaml`（流程胶囊条 + 底部统计 + 右键菜单）；v1 因 380×260 与宿主 340×112 冲突作废，v2 改单行紧凑式 | **已复刻（2026-10-22）**；按 340×112 单行 22px 胶囊；`hex=0`；**右键菜单部分已由 `flow-context-menu/` v1 取代（2026-10-27）** |
| `flow-context-menu/` | **v1（已复刻）** | `VisionMaster\Views\FlowListView.xaml` 的流程胶囊右键菜单：取消「流程 ▸」二级菜单，改「流程 / 配置 / 运行 / 方案」四组平铺；320px / 32px 行 / 图标底片 / 语气色 / 状态徽标 / 页脚；四种状态（默认 / 流程禁用 / 骨架流程 / 运行中）+ 双行变体对照 + 五态与令牌映射 | **用户已裁定 A → 已复刻（2026-10-27）**：`StepContextMenu.xaml` 三个新样式键 + `FlowListView.xaml` 12 项重写 + `FlowListViewModel` 13 个只读展示属性；`UIThemeSmokeTest` 两条新断言（15 处契约 + 真实套模板 300×34）绿；落地记录 `docs/code-changes/2026-10-27-流程右键菜单复刻（A方案）.md`；待真机验收 |
| `run-cockpit/` | v1 | `VisionMaster\Views\RunCockpitView.xaml`（运行场景深色驾驶舱：顶条 56 + 图像区 + KPI 条 96）；运行中 / 待接入 / 故障三场景 | **部分复刻（2026-10-22）**：顶条指标位 + KPI 语义色条已落地；故障态/结果章/帧信息缺数据源，见该页 §6 |
| `剩余视图设计稿-2026-10-22/` | v1 | **33 个剩余视图的汇总设计稿**（单文件 `design.html`，左侧目录分 4 组）：SCADA 编辑工作台 5 / SCADA 数据与弹窗 8 / SCADA 选择器与运动板卡 10 / 设置弹窗与零散 10 | 设计稿已出（2026-10-22），**待用户逐页确认**；未复刻。分片源在 `parts/`，写作规范 `_SPEC.md` |
| `logic-operators/` | v1 | 通用逻辑门参考库：41 个算子 / 6 族、真值表、门形与 WPF 映射；**安全回路备用，不是本期流程画布基线** | 参考库，待未来安全回路使用 |
| `flow-logic/` | v1 | 项目真实算子版流程栏 + 画布：逻辑运算 6 种操作、数值比较 6 种操作、If/分支匹配/While/For/并行分组/等待/Break/Continue/Return；42 节点复杂项目、逻辑容器重点改造、检查器、状态矩阵、WPF 复刻映射 | 待用户确认 |
| `property-grid/` | v1 | `UI\Controls\CustomControl\PropertyGrid\`（框架级公共控件：`PropertyGridBase` + `FlatPropertyGrid` + `CardPropertyGrid` + 生成器/处理器管线）与 `UI\Controls\Themes\PropertyGrid.xaml`；含表格式 / 卡片式两种形态与默认 / 校验失败 / 权限受限 / 无可见属性四态 | **未复刻**；待用户确认 |
| `message-dialog/` | v1 | UI 库公共组件 `MessageDialog`（消息 / 确认弹窗）：信息 / 成功 / 警示 / 危险四种语义、单 / 双按钮（三按钮组合见设计稿，API 延后）、结构化详情区（标签 + 数量 chip + 等宽路径 + 复制）、键盘与焦点契约；覆盖现场恢复 / 信息 / 成功 / 警示 / 危险五场景与 480 / 560 两档宽度 | **已复刻（2026-10-20）**；设计证据 `message-dialog/evidence/design-v1-*.png`；WPF 证据 `message-dialog/evidence/after-render-v1-*.png`；待用户真机确认 |
| `scada-confirm-pause/` | v1 | SCADA P0 交互：事件钩子「执行前确认」、`PauseFlow/ResumeFlow`、条件动作「仅在上一条成功时执行」、自动页暂停/恢复模板与动作编辑器 | **WPF 复刻完成（2026-10-21）**；设计证据 `scada-confirm-pause/evidence/design-v1-*.png`；WPF 证据 `after-01-editor.png`、`after-02-dialog-warning.png`、`after-03-dialog-danger.png`、`after-04-editor-danger.png`；ScadaChecks 2286 / FlowCanvasChecks 3055 / UIThemeSmokeTest 86 全绿；待用户确认 |
| `scada-controls/` | v1 | `Scada.Controls`（27 个内置 TypeKey）+ `Scada.Controls.Charts`（趋势曲线）图元控件；深色运行画面 + 浅色编辑上下文、五态 / 状态矩阵 / 令牌映射；含 9 个分类页签与 16 项原候选 + 7 项补充候选（6 项已落地） | **Phase 1+2 已复刻**（Tank/KpiBoard + SelectorSwitch/TextField/ComboBox/AckButton + 现有控件修复）；证据 `scada-controls/evidence/after-phase1-*.png`、`after-phase2-*.png`；其余待确认 |
| `scada-controls-batch2/` | **v2（复核版）** | SCADA 推荐控件第二批：ComboBox 只读显示（替代 StateText）、运行窗口顶栏页面菜单（替代 PageMenu）、软键盘、弹窗 / 子画面、滑块 `Hmi.Slider`（**唯一新增 TypeKey**）、AlarmBanner 行内逐条确认（替代 RecordList）、DataGrid 历史源模式（替代 HistoryTable）、旋转 / 缩放动画；沿用 `scada-controls` v1 双上下文与 `Hmi*` 令牌，含 v1→v2 复核对照与 10 项待裁定 | **P0-1 / P0-2 / P0-3 / P1 弹窗 + 每画布多基座 + 软键盘 / P2 模拟量滑块已实施**（2026-10-22）；设计文件 `scada-controls-batch2/design.html`；实施记录 `docs/code-changes/2026-10-21-SCADA返回上一页（NavigateBack动作与页栈通知）.md`、`docs/code-changes/2026-10-21-SCADA下拉框只读显示（ComboBox Presentation-Text）.md`、`docs/code-changes/2026-10-21-SCADA顶栏页面菜单（运行窗口导航与页面级角色）.md`、`docs/code-changes/2026-10-21-SCADA多基座与弹窗（特殊画面）.md`、`docs/code-changes/2026-10-22-SCADA软键盘（数字字符共享设施）.md`、`docs/code-changes/2026-10-22-SCADA模拟量滑块（Hmi.Slider）.md`；设计证据 `scada-controls-batch2/evidence/design-v2-*.png`（6 张）；评审记录 `scada-controls-batch2/评审记录-2026-10-21.md`（6 角色 pass with changes）；v1 截图归档在 `evidence/v1/` |
| `industry-templates/` | v1 | 三套 1440×900 深色运行画面：SCADA 监控 / 自动化设备运控 / 视觉行业；公共壳 A~E、信息层级、图元映射与复用约束 | 设计稿待用户确认；证据 `industry-templates/evidence/template-0*.png` |
| `scada-controls-batch3/` | **v1** | SCADA 控件第三批（P0 三项 + P1 除配方外六项，共 10 项）：本地图片 `Hmi.Picture`、线/折线/箭头 `Hmi.Line`、增量减量动作、历史数据 `IScadaHistorySource` + DataGrid `Source=History`、二维码/条码 `Hmi.QRCode`/`Hmi.Barcode`、启用对象动画、对象更改/开关事件、多曲线趋势 `Hmi.MultiTrend`、声音报警、运行计时/计数器 `Hmi.RunMeter`；新增 6 个 TypeKey + 4 项扩展，沿用 20 个 `Hmi*` 令牌并新增 4 个 | **设计稿 v1 已出（2026-10-23），待用户确认**；`design.html` 可交互（十项演示 + 深浅预览）；证据 `scada-controls-batch3/evidence/design-v3-*.png`（10 张）；上游缺口清单 `docs/code-changes/2026-10-23-SCADA控件对标手册缺口清单.md`；未改任何 WPF/Web 代码 |
| `scada-state-list/` | **v1（P1-1 已复刻）** | P1-1：方案级共享文本列表 + `Hmi.ComboBox` 可选 `StateListId`（只读文字 / 读写下拉 / 旧 Options 回退）；`StateView`、图形开关、图形 IO 域、字指示灯、SelectorSwitch/BitButton/Lamp 状态文字全部 P1-2 | **P1-1 已复刻（2026-10-24）**：`ScadaChecks` 2710/0；`UIThemeSmokeTest` / `FlowCanvasChecks` 3657/0；设计证据 `scada-state-list/evidence/01-runtime.png`、`02-manager.png`；WPF 渲染 `artifacts/render/20261024/scada-state-list/01-combobox.png`、`02-manager.png`；实现记录 `docs/code-changes/2026-10-24-SCADA文本列表与ComboBox接入（P1-1）.md` |
| `scada-p1/` | **v1.1（三项已复刻；报警不做、Web 未同步）** | P1 剩余四项：`Hmi.StateView`（图形开关 / 图形 IO 域 / 字指示灯，一个 TypeKey + 三预设）、`Hmi.SubPage`（画中画；P1a 固定页 + 只读 + 活数据）、报警生命周期事件（页面级激活 / 取消 / 确认 + 来源过滤）、位操作动作（`SetBit` / `ResetBit` / `ToggleBit` + `BitIndex`）；含代码评审结论、契约改动与 Q1~Q6 | **StateView / SubPage P1a / 位操作已复刻（2026-10-25）**；`ScadaChecks` **2802/0**；`UIThemeSmokeTest` 0 FAIL；`FlowCanvasChecks` 3657/0；全量 gate 12/13（FlowCanvas 构建被并发 VisionMaster 锁 Modules，非本批）；报警按用户决定本批不做，Web 渲染器/快照最后统一（WR 例外清单显式登记）；`design.html` 可交互（28/28 交互探针 + 6/6 结构复检）；实现记录 `docs/code-changes/2026-10-25-SCADA-P1三项实现（StateView-SubPage-位操作）.md`；代码评审 `docs/方案设计/2026-10-24-SCADA-P1代码评审与设计切分.md` |
| `scada-schedule/` | **v2（已复刻）** | 计划表管理对话框：**时间计划**（每天 / 每周 / 指定日期 + 时刻 + 「下次触发」预览）与**事件计划**（变量 + 条件 + 持续时长）+ 动作表（五列：序号 / 动作 / 对象或内容 / 参数 / 操作）+ 七态（默认·每天 / 每周 / 指定日期 / 事件计划 / 空列表 / 校验失败 / 操作员只读）；明确不含安全联锁。**v2 从深色 SCADA 运行态令牌改为浅色 `Dialog*` 域**，并逐条回答 2026-10-27 走查列出的 **17 条 UI 问题** | **已复刻（2026-10-27）**：`ScadaScheduleManagerView.xaml` / `.xaml.cs` 重写；新增 `IDialogValidator` 契约并把校验接进 EasyDialog 的「确定」；宿主卡片 `MaxWidth` 700→1040（原先把 980 内容压到 652，动作列被裁成窄条）；`ScadaChecks` **3177/0**（含 `[SV1]~[SV20]` 25 条）；WPF 渲染证据 `artifacts/render/20261027/scada-schedule/schedule-v2-*.png`（5 张）；设计证据 `scada-schedule/evidence/design-v2-01~07-*.png`（7 张）；已知偏差 12 条见该页 README；待用户按 R132 验收 |
| `industry-templates/scada-suite/` | v1 | SCADA 完整套件：7 个界面（总览 / 监控 / 手动 / 自动 / 报警条 / 报警日志 / 操作记录）+ 跳转关系、信息层级、图元映射 | 设计稿待用户确认；证据 `scada-suite/evidence/0*.png` |
| `recipe-manager/` | v1 | `VisionMaster\Views\DialogViews\RecipeManagerView.xaml`（配方管理弹窗：左栏配方列表 + 右栏参数对比表；空 / 未选中 / 只读 / 运行中 / 有差异 / 应用确认六态） | 设计稿 v1 已出（2026-10-23），**待用户裁定**；未复刻；证据 `recipe-manager/evidence/design-*.png` |
| `app-shell-chrome/` | **v2** | `Shell.xaml` 的 **chrome 专项**。v2 按用户真机截图（1280×853，调试模式，`Test_Motion` 已选中 37 步、admin 已登录、日志 108 条含 71 警告）出稿，现状 / 改后同内容对照：顶栏 36→40（文件操作**保留图标但归组**、模式改分段带 `^1..^4`、中央方案胶囊含目录尾段）、命令条 44 不变（保留绿=运行/红=停止，仅补不可用对比度与命中区）、底栏 26→28（1px 分隔块、方案名取代截断路径、24px 可点区域）。**语义不改**：底栏「告警」仍是产线告警 KPI，不与日志 71 条警告合并。v1（抽象画布版）保留在 `design.html` | 设计稿 v2 已出（2026-10-24），**待用户裁定**；未复刻；未改任何 WPF 代码；证据 `app-shell-chrome/evidence/design-v2-{before,after}.png` |
| `flow-nav-shell/` | **v2（已复刻）** | 只做两块：① `Shell.xaml` Row0 顶栏样式（白底 36px + 底部 1px、模式分段单一强调、文件三键/全局命令分隔归组、16px 图标、居中方案名、窗口按钮 46×36）；② `FlowListView.xaml` 流程胶囊（保留 2 列，22→24px，**流程区 112~224 自适应（13 条=224）**，角色/调用徽标合一，选中左条+浅底，运行/失败点，禁用删除线，底栏「全部」）。**不做**：步骤树样式、计数条位置、身份胶囊、顶栏 40、深色 | **v2 已复刻（2026-10-25，用户指令）**：构建 0 错、`UIThemeSmokeTest` 125/0、`ScadaChecks` 3066/0；FlowCanvas 2~4 条为已知运动/断点抖动（U/V 模板检查全过）；证据 `evidence/after-flow-list-224/112.png`；`run_gates` 4 步红为退出码采集缺陷（底层日志 OK/0 错），待单独修复 |
| `scada-alarm-config/` | **v2.1** | `VisionMaster\Views\DialogViews\ScadaAlarmConfigDialogView.xaml`（报警配置弹窗：扁平表格 + 按种类展开的详情块；默认 / 断线 / 筛选 / **搜索无结果** / 校验失败 / 删除回执 / **新增行（未绑变量）** / 空态 / 运行中九态；每屏带右侧功能说明） | 设计稿 v2.1 已出（2026-10-24），**待用户裁定**；未复刻；证据 `scada-alarm-config/evidence/design-*.png`（9 张，含批注），v1 七张归档在 `evidence/v1/` |
| `hmi-console/` | **v6** | 四套方案（A BGA 焊球全检 / B 包装线全检追溯 / C 精密尺寸测量站 / D 精密运动控制站）的 **1920×1080 工业触摸屏完整界面**：公共壳 A~E + 左导航 + 右信息栏三级；A/B/C 各 6 页 + D 7 页（多「点动与回零」「轴参数」）+ **V6-01 轴控件点动模式设计专页** 共 **26 屏**；每区块标真实图元 TypeKey 与候选；含图像采集 Folder 模式与 `Hmi.ImageView` 实时图口径。**v3 按用户裁定把 D 改为「轴控件搭配」**：`Hmi.AxisConsole` / `Hmi.AxisPanel` / `Hmi.AxisPointTable` 三个真实预设成为运动界面主体，删掉自绘四轴卡、3×3 点动盘、独立点位表。**v6 按用户三条裁定定稿轴控件点动模式**：只加一个属性 `JogMode`（`ScadaJogMode { Continuous, Step }`）+ 一个数值 `JogStepMm`（0~50 mm，步进 0.1，缺键兜底 `Continuous + 1.0`）；档位是**可输入数值框**不是按钮组；单步目标 = **当前位置 ± 档位**（一次 `TryMove`）；**运动指令执行中只锁两个点动键**（`CanJog = CanOperate && !IsMoving`），停止 / 清报警 / 失能 / 回零照常可点；四形态对照 + 属性表 + 行为口径表 + 落点约束 + 改动边界 + 5 条断言，**仅设计未改源码** | 设计稿 v6 已出（2026-10-24，26 屏），**待用户裁定**；未复刻进 `.vms`；`design.html` 可交互（双维页签 + `?shot=1` 单屏截图）；`evidence/` 26 张 1920×1080 截图齐（`27-axis-jogmode-v6.png` 1620×654 专页 + 同名隔离渲染 `.html`） |
| `user-management/` | **v4（已确认并复刻）** | `VisionMaster\Views\DialogViews\UserManagementView.xaml`（1100×820 单窗口壳：账号与用户组 / 在线会话 / 审计记录三页签；账号页两卡 + 搜索 + 确认密码 + 策略摘要；审计筛选行 + 7 列 + 结果五桶；权限目录五列 + 行内新建 + 32/32；用户组编辑模板/自定义两段 + 删除守卫） | **v4 已确认并复刻（2026-10-25）**：四视图 + 四 VM；`run_gates` **14 步绿**（UIThemeSmokeTest 125/0、ScadaChecks 3052/0、FlowCanvasChecks 3672/0、ScadaWebChecks 190/0）；设计证据 `evidence/v4/design-v4-*.png`（**7 张**）+ WPF 证据 `evidence/v4-wpf/after-*.png`（**7 张**）；差异清单见该页 README §10.7；v3.1 与 B5 旧实现保留为历史 |

| `scada-report-dynamicline/` | **v1** | 两个**说明型**基线：① `Hmi.Report`（报表图元）——它绑的是「列」（表头 + 变量名 + 宽度 + 对齐 + 格式），行来自 CSV 账本按时间范围查询，支持明细/汇总 + 分页 + 导出 xlsx/csv + 打印；澄清「死数据 / 不能绑定 / 想不到用途」三个误区，含运行态模拟器（可切明细/汇总、翻页、模拟导出回执）；② `Hmi.DynamicLine`（动态画线）——长度与角度是两个独立可绑变量，段数 >1 画锯齿；含可拖滑杆的实时演示与用途清单；末尾 5 条待裁定项 | 设计稿 v1 已出（2026-10-25），**待用户裁定**；未复刻；`design.html` 自包含可交互；未改任何 WPF/Web 代码 |

| `scada-chart-alarmtable/` | **v1** | 三个新图元：`Hmi.BarChart`（柱状图：标签+变量列表、横/竖、目标线+超限加粗、排序、缺失柱灰纹）、`Hmi.PieChart`（饼图：实心/环形、百分比/数值、图例右/底、小片并入「其他」）、`Hmi.AlarmTable`（报警汇总表：级别/时间/名称/条件/当前值/状态/确认，只读态、静音横幅、空态「系统正常」）；含三图元选型速查与 7 条待裁定 | **内部评审通过（2026-10-25）**：9 条发现 → 3 处修正已回写（负值轴范围 / 超目标三重编码 / 图例不用绝对坐标），4 条实现前置（**报警引擎活动列表+确认 API 为阻塞项**、两个 series 编解码、WR 例外 +3、手册 3 页）；`design.html` 自包含可交互；**待用户裁定 Q1~Q7**；未开始实现 |

| `scada-button-review/` | **v1** | `Hmi.Button` 主题审查：现状（按 `ButtonElement.xaml` 逐项 CSS 复刻）vs 改版对照 + 7 态矩阵；10 条发现（4×P1：无按下反馈 / 无键盘焦点环 / 无危险变体 / 无可点最小尺寸）+ 改版方案（四态、focus-visible、Danger、Busy、MinHeight） | 审查稿 v1 已出（2026-10-25），**待用户裁定 Q1~Q4**；未改任何 XAML；`design.html` 可悬停/按住对照 |

| `scada-iofield-slider-review/` | **v1** | 抽查两个操作控件：`Hmi.IOField`（四态已有、缺触摸可写提示/禁用态/越限图标/单位独立列/值变化高亮）与 `Hmi.Slider`（拇指尺寸无下限、无禁用态、无写失败反馈、无数值跟随与量程标注）；含现状复刻 vs 改版对照、17 条发现、两条共性根因（按鼠标设计而非触摸 / 状态只画看得见的） | 评审稿 v1 已出（2026-10-25），**待裁定 Q1~Q5**；未改任何 XAML |

| `scada-third-round-review/` | **v1（现状包，非设计稿）** | 第三轮会审前提材料：`Hmi.DataGrid`（R19/R20/R21/R159 模板要点 + 可配属性 + 9 处断言引用）、`Hmi.AxisConsole`/`Hmi.AxisPanel`（R108/R138 操作纪律 + 属性 + 24 处断言）、`Hmi.AlarmBanner`（三重编码 + 6 个属性 + 16 处断言）；每节分"已钉住 / 未钉住"，并给出四态截图的探针扩展规格 | 现状包已出（2026-10-25），**截图待补**（需扩展 `_ScadaUiProbe` 支持按 TypeKey 渲染）；配合 `docs/方案设计/2026-10-25-第三轮控件联合评审计划（数据表格-轴控台-报警条）.md` |

| `scada-full-review/` | **v1** | 剩余 **42 个**工具箱图元的全量审查（按 8 族：文本与显示 / 数值与仪表 / 状态与操作 / 容器与导航 / 图形与装饰 / 工艺与设备 / 数据与配方 / 媒体与码），SCADA 应用工程师 × 前端工程师双视角；**3 条 P1**（`Indicator` 缺断线态、`StateView` 互斥字段、`PipeRun` 可改却无效）+ 20 余条 P2/P3 + 4 条共性结论（触摸尺寸无令牌 / 缺第三态 / 同语义两套画法 / 编辑期可改性无规则） | 审查稿已出（2026-10-26），**待用户裁定**；`review.html` 页签式可读；未改任何代码 |

| `project-assets/` | **v0.4 + WPF 演示** | 项目资产管理：标定 / 模板 / 模型 / 配方 / 图像资产统一列表；v0.2 按四路专家评审修正标定坐标、残差/不确定度、生命周期、工位·相机、读/写/调试引用与三段式草稿；v0.3 改为 Shell 内页浅色主题并拆四页签（概览 / 技术参数 / 引用 / 版本）；**v0.4 新增模板 4 / 模型 5 / 配方 2 / 图像 10 的演示数据**，参数取自当前 `.vms` 与插件步骤配置（未绑定模型明确标未绑定），保留 1680/1366/1280 重排与后端契约缺口 | **WPF 演示版已落地（2026-10-26，仅 UI + 内存假数据）**：入口 `☰ → 系统 → 项目资产`；视图 `VisionMaster/Views/ProjectAssetsView.xaml`，VM `ProjectAssetsViewModel.cs`；真实接口/引用索引/版本回滚/权限审计未接；证据 `project-assets/evidence/` 下 main / tech / refs / versions / templates / models / recipes / images / 1366 / create-modal；v0.2 深色稿归档在 `project-assets/evidence/v0.2-dark/` |

| `scada-style-review/` | **v1** | **样式层**专项（区别于功能审查）：状态色不统一（4 个运行绿/蓝）、缺断线态视觉、字号七档 / 行高四套 / 圆角三套 / 线宽四套、细线不可见且点不中、选中框与运行蓝撞色；给出 6 类令牌建议（`HmiState*` / 字号四档 / 行高三档 / 圆角三档 / 线宽三档 / `HmiTouchTarget`）与落地顺序 | 审查稿已出（2026-10-26），**待用户裁定**；未改任何代码 |
| `scada-property-panel/` | **v2.2（已复刻，待 R132 验收）** | `VisionMaster\Views\ScadaPropertyView.xaml`（右侧 300px 停靠面板）+ `ScadaEventEditor.xaml` + `ScadaPropertyViewModel.cs`。v2 彻底重构：卡片化分组（三层深度）、混合标签布局（左标签72/上标签/成对网格）、filled 输入、绑定 chip 替换输入、事件/动画折叠卡（勾选与展开解耦）、曲线卡定稿（外观组 8→4、数据组 11→7）、折叠全部、搜索强制展开、组头错误点；v2.1 移除「类型」行、曲线卡绑定改轻量小字标注；v2.2 颜色行不显示十六进制（只留色块 + 取色）；配色说明只用颜色名称 | **已复刻（2026-10-28）**：`ScadaChecks` 3177/0、`FlowCanvasChecks` 4258/0；`run_gates` 15/16（唯一红是 FlowCanvasChecks 构建被运行中 VisionMaster 锁 `Modules`，非代码失败）；WPF 证据 `evidence/wpf-v2/after-*.png`（5 张）；实现记录见该页 README §13；待用户按 R132 验收 |

| `可形变模板匹配/` | **v0.2（草案）** | `Plugin.DeformableMatching\DeformableMatchingView.xaml`（可形变模板匹配配置：模板来源/参考图 + 数值 ROI + 掩膜 + 局部非刚性/平面透视 + 查找参数 + 结果区；v1 单模板，画布只读预览） | **待用户确认**（2026-10-27 v0.2 增补模板来源）；WPF 已复刻待验收；`design.html` 仍为 v0.1，v0.2 增补以 README 为准 |

| `判定表/` | **v1（草案）** | `Plugin.JudgeTable` 规则表配置弹窗：全宽表格 + 可展开行（数据源/模式/期望/Actual/判定算式）+ 每行结果生成运行时变量/动态输出端口（bool，可关；全局映射交给下游「变量赋值」）+ 表级固定端口；令牌复用 `app-shell-chrome` v2，无新增颜色 | 设计稿 v1 已出（2026-10-27），**待用户确认**；未复刻；`design.html` 自包含可交互（展开行 / 结果输出开关 / 端口名 / 结果计数） |

| `flow-runtime-kpi/` | **v2（底栏 KPI 已下线）** | 多流程 / 多工位运行时的统计口径：底栏五项（节拍/OK/NG/未判定/告警）**已于 2026-10-29 按用户决策删除**；保留 `N 条在跑` 浮窗（逐流程节拍/判定/判定源/最近一轮）、轮次下钻（`FlowRoundResult` + 步骤时间线）、`System.Flow.*` 系统变量、L2 引擎轮次记录；Home/End/调试轮排除、子程序按工位计入 | **底栏 KPI 下线（2026-10-29）**：`Shell.xaml` 五组控件 + `ShellViewModel` 五个属性/计算/告警读取全部移除，`ScadaMonitoringHost` 依赖摘除；`UIThemeSmokeTest` 改为"五个绑定必须消失"的回归闸；浮窗/下钻/系统变量不动；`FlowCanvasChecks 4349/0`、`UIThemeSmokeTest exit 0`；线级拓扑未做 |
| `flow-result-source/` | **v1.3（已复刻 + 三处修订）** | 流程级「结果源」配置弹窗：自动 / 结果输出步骤 / 结果变量 / 步骤端口 / 不参与；变量与步骤端口选择、当前生效摘要、校验与只读态；后端 `FlowModel.ResultSource` 解析优先级 | **WPF 已复刻**：流程右键「运行统计…」→ `FlowResultSourceDialogView`（600×520，Dialog 令牌，无硬编码颜色）；候选按 Q2 四类、分支步骤可选；v1.3 修自动标签随状态、只读单行、变量搜索；`FlowCanvasChecks 4349/0`（DLG-1~22）、`UIThemeSmokeTest exit 0`（5 条结构闸）；真机截图 `artifacts\render\20261028\result-source-*.png`；待用户按 R132 验收 |

| `flow-manager/` | **v1.1（已复刻）** | `VisionMaster\Views\DialogViews\FlowManagerView.xaml`（流程管理 1180×620 弹窗）：**调用方式 5 个复选框 → 5 个可点 chip（单排约 280px，绑定仍是 5 个 bool）**、删除与 chips 重复的「调用关系」列（信息并入 chip 颜色 + 名称旁角色徽标 + 「仅子程序/手动」派生小字）、状态列四态、操作列按状态收敛（锁定/解锁合并成图标键，**运行中置灰 = Q1 补的行为**）、说明面板折叠 160→62、搜索空态、选中态改左侧强调条、**「未配齐」提示 + A 方案齿轮配置入口**（`EasyDialog` + `InvokeParamEditModel`）；四帧（默认 7 行 / 说明展开 / 搜索无结果 / 1060 窄档）；**令牌 0 新增**，新增 4 个样式键 | **已复刻（2026-10-28）**：`FlowCanvasChecks` **4337/0**（含新增 `[FM1]~[FM6]` 25 条）；真渲染 `app-shell/evidence/flow-manager-v11.png`、`flow-manager-v11-empty.png`；实现记录 `docs/code-changes/2026-10-28-流程管理v1.1复刻（调用胶囊与未配齐提示）.md`；`run_gates.ps1 -SkipBuild` **16/16 全绿**（全量重建那轮 15/16：唯一红是 FlowCanvasChecks 的构建在 `copy → Modules` 上失败，用户正开着 `VisionMaster.exe` 锁了 `Modules\`，非代码失败）；待用户按 R132 真机验收 |

> 本期流程栏 + 画布以 `flow-logic/` 为准；`logic-operators/` 是通用门电路参考库，留给未来的安全回路使用，不进入本期复刻。

## 插件改造 SOP 与清单

- [**审阅清单-第1批-2026-10-21.md**](审阅清单-第1批-2026-10-21.md)：第 1 批 4 份设计稿（控制台 / 监视 / 流程胶囊条 / 运行驾驶舱）的**待用户逐页确认清单**，含 6 项需用户拍板的事项与复刻时必须新增的接口。
- [未出设计稿视图盘点-2026-10-21.md](未出设计稿视图盘点-2026-10-21.md)：全仓视图覆盖度盘点（哪些已出稿、哪些待出稿、按批分组）。
- [插件自定义视图Web化改造SOP.md](插件自定义视图Web化改造SOP.md)：适用范围、前置条件、逐步操作、涉及文件、验证、回滚、注意事项、单插件记录模板、DoD。
- [插件自定义视图改造清单与实施计划.md](插件自定义视图改造清单与实施计划.md)：22 个自定义视图插件盘点（截至 2026-10-20 全部完成 Web 化，含 ImageAcquisition 参考实现）、分批计划、9 个不改造项目清单。
- [插件自定义视图Web化改造总览.md](插件自定义视图Web化改造总览.md)：22 个插件的变更清单、审核状态、验证结果与统一遗留问题。
- [插件遗留问题核查与修复结果.md](插件遗留问题核查与修复结果.md)：91 项遗留的逐条核查结论、已修复清单与未修复原因（FUNC/TEST/真机/发布）。
- [插件遗留问题核查清单.md](插件遗留问题核查清单.md)：上面那份修复结果的**前置清单**——91 项逐条待核查条目，按插件分组，供按批执行时打勾与回填。

## 对应关系

- 颜色：CSS 变量 ↔ `UI/Controls/Themes/Colors.xaml` 语义令牌；
- 间距/圆角/字号/阴影：按 `AGENTS.md` §30 的刻度一一对应；
- 组件：HTML 控件 ↔ UI 库里的 `Dialog*` / Fluent 样式（映射表随每个页面记录）。

## 允许偏差

字体度量、滚动条宽度、阴影层数、动效曲线允许与 Web 基线存在细微差异，但必须逐条写进该页面的 `README.md`，并由用户裁定是否接受。
