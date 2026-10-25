# SCADA 文本列表 + ComboBox 接入（P1-1）

日期：2026-10-24
设计基线：`docs/UI设计基线/scada-state-list/`（v1，范围已收窄为“只做 ComboBox”）
范围：方案级共享文本列表 + `Hmi.ComboBox` 可选 `StateListId`；不新增 `Hmi.StateView`，不接 Button/SelectorSwitch/BitButton/Lamp。

## 一、当前状态

- **已完成**：列表模型/序列化、ComboBox 三态（只读文字 / 读写下拉 / 旧 Options 回退）、列表缺失与无匹配回退、属性面板选择器 + 管理入口、文本列表管理器、运行态上下文/画布/编辑器/运行窗口的解析器注入、Web 快照与 ComboBox 文本态渲染、`ScadaChecks` 断言与真窗口渲染。
- **验证**：`ScadaChecks` **2710 通过 / 0 失败**；`ScadaWebChecks` 147/0（改动后静态/HTTP 段）；全量 gate 13 步中 11 步绿，`audit_root` 红（另一条会话在根目录新建了 `scripts\`，非本批改动），`FlowCanvasChecks` 首轮 2 项红系并发测试进程争用、隔离复跑 **3657/0**。
- **未做**：`StateView`、图形开关/图形 IO 域/字指示灯、SelectorSwitch/BitButton/Lamp 状态文字、从 Options 导入、列表导入导出、多语言列——全部 P1-2，等第二个真实消费方。

## 二、已做

### 领域层（VM.Scada）

| 文件 | 改动 |
| --- | --- |
| `Scada\Models\ScadaStateList.cs` | 新增 `ScadaStateList`（稳定 Id / Name / DefaultText / Entries[Value:int, Text]）+ `ScadaStateEntry`；`Find`/`DisplayText`；条目增删改推高 `Version`；`Clone`/`CopyFrom`/`MarkDeleted` |
| `Scada\Models\ScadaDocument.cs` | 新增 `StateLists` 集合（整体替换 + 变更记录，照 `Alarms` 范式） |

### 控件层（VM.Scada.Controls）

| 文件 | 改动 |
| --- | --- |
| `Elements\ComboBoxElement.cs` | 新增 `StateListId`（Kind=`StateList`）；`StateListId` 非空时候选项/显示文字来自列表，为空回退 `Options`；`StateListMissing` 只读 DP；无匹配用 `DefaultText`；订阅列表 `Version` 实时重解析；`OnStateListResolverChanged` 处理解析器晚注入 |
| `Elements\ScadaElementBase.cs` | 新增 `StateListResolver`（Id → 列表）与 `OnStateListResolverChanged` 钩子 |
| `Canvas\ScadaCanvas.cs` | 新增 `StateListResolver` DP，建容器/换解析器时注入每个图元 |
| `Runtime\ScadaRuntimeContext.cs` | 新增 `StateLists`（运行态回落；设计期用画布解析器） |
| `Descriptors\ElementPropertyDescriptor.cs` | `ElementPropertyKind` 末尾追加 `StateList` |
| `Descriptors\ScadaPropertyCatalog`（生成器产物） | `[ScadaProperty]` 自动生成 `StateListId` 的 DP + 描述符 |

### 宿主（VisionMaster）

| 文件 | 改动 |
| --- | --- |
| `Services\ScadaMonitoringHost.cs` | 上下文带入 `document.StateLists` |
| `Services\ScadaRuntimeHost.cs` | 旧报警路径的上下文也带 `StateLists` |
| `ViewModels\ScadaEditorViewModel.cs` | `ResolveStateList(Guid)` |
| `Views\ScadaEditorView.xaml.cs` | Loaded/DataContextChanged 显式注入画布 `StateListResolver` |
| `Views\ScadaRuntimeWindow.xaml.cs` | 运行/弹窗画布注入 `ResolveStateList` |
| `ViewModels\ScadaPropertyViewModel.cs` | `ScadaStateListRow`（选择器 + 管理…）+ 工厂分支 + `OnManageStateLists` |
| `Views\ScadaPropertyView.xaml` | `EditStateList` 模板 + `StateList` 触发 |
| `ViewModels\DialogViewModels\StateListManagerViewModel.cs` | 副本编辑、引用数、AddList/AddEntry/RemoveEntry、`Commit` 原地写回 |
| `Views\DialogViews\StateListManagerView.xaml(.cs)` | 列表 + 条目表 + 新建/删除/添加/删除条目；删除前显示引用数并二次确认 |

### Web

| 文件 | 改动 |
| --- | --- |
| `Scada\Models\ScadaPageSnapshot.cs` | 新增 `StateLists` + `ScadaStateListSnapshot` / `ScadaStateEntrySnapshot` |
| `Scada\Models\ScadaPageSnapshotBuilder.cs` | 每页快照带文档的文本列表 |
| `Web\...\wwwroot\app.js` | `normalizePage` 归一化 `StateLists`（Pascal/camel 双写） |
| `Web\...\wwwroot\elements-interactive.js` | ComboBox 优先按 `StateListId` 取列表；缺失/空列表/无匹配文案；选项签名变化重建弹层 |

## 三、验证

| 手段 | 结果 |
| --- | --- |
| `ScadaChecks` `[BL1]~[BL19]` | 模型/版本/序列化、列表优先、只读、回退、缺失、旧 Options 回退、读写下拉写回 `Entry.Value`、Output 不写、管理器副本/引用数/原地写回/删除/新增、属性行候选/摘要、页面快照 |
| 全量 `ScadaChecks` | **2710 通过 / 0 失败**（日志 `artifacts/logs/20261024/scadachecks-p11b.log`） |
| 真窗口渲染 | `artifacts/render/20261024/scada-state-list/01-combobox.png`（只读文本“运行”/读写下拉“停止”/无匹配“未知”）、`02-manager.png`（管理器真实布局） |
| Web | `ScadaWebChecks` 147/0；`node --check` 两处 JS 通过；快照断言 `[BL19]` 证明列表进快照 |
| 收尾 gate（全量） | `tools\run_gates.ps1` 共 13 步：`docs_index`/`audit_artifacts`/`audit_docs`/`sync_constraints`/`CommChecks` 336/0/`DataPipelineChecks` 97/0/`DatabaseChecks` 134/0/`ScadaChecks` 2710/0/`RecipeChecks` 138/0/`UIThemeSmokeTest`/`HelpProbe` 全绿；`audit_root` 红（根目录多出 `scripts\`，见下）；`FlowCanvasChecks` 首轮 3655/2，隔离复跑 **3657/0** |
| 并发干扰复现 | 首轮全量 gate 期间另一条会话同时运行 `FlowCanvasChecks.exe`，本机两次构建报 `MSB3027/MSB3021`（DLL 被该进程锁定）；其退出后 `-Only FlowCanvasChecks` 重建并复跑 3657/0。两项红均落在运动假设备 `[M]` 段，与本批 SCADA 改动无调用关系 |
| 根目录闸门阻塞 | `gate-audit_root.log`：`DIR scripts`（39 个 `tmp_*.ps1`，mtime 21:03~21:40，另一条会话的临时脚本）。按 R9/R166 未搬动、未登记；本批交付物不在其中 |

## 四、未做 / 已知偏差

- **只接 ComboBox**：`Button` 标题继续手填；`SelectorSwitch`/`BitButton`/`Lamp`/`StateView` 全部 P1-2。
- **没有“从 Options 导入”**：老工程继续用 `Options`，新列表手动建；导入留 P1-2。
- **没有只读下拉**：`Output + Field` 当前整体禁用（`IsEditable=false`），只读请用 `Output + Text`；要“可展开不可改”需另开交互，P1-2。
- **列表只有离散值 → 文本**：不存图片/范围/位号；这些随 P1-2 的 StateView 加字段。
- **管理器 v1**：无导入导出、无多语言列、无拖拽排序；删除二次确认在视图层，VM 只做纯动作。
- **Web 文本态**：已接渲染器与快照；图片态/其它消费方不在本批；未做浏览器端到端（需 Web 运行态宿主）。
- **并发编辑/并发测试**：本轮施工期间另一条会话在改 `ScadaTemplateParameterizer` / `FlowCanvasView` / `ScadaEditorViewModel`，导致两次 `dotnet build` 瞬时失败；其同时运行 `FlowCanvasChecks.exe` 又导致一次 DLL 锁定构建失败与首轮 gate 两项运动断言红。等其退出后重建/复跑通过，未改对方文件。
- **根目录 `scripts\` 未处理**：属于另一条会话的临时脚本，不在本批改动内；`audit_root` 因此红，需该会话自行迁到 `tools\oneoff\` 或删除后才能全量全绿。

## 五、下一步

1. 全量 gate 剩余唯一红项 `audit_root` 由另一条会话的根目录 `scripts\` 引起；该会话清理后复跑 `tools\run_gates.ps1` 应 13 步全绿。
2. P1-2（等真实需求）：`Hmi.StateView` + 图形开关/图形 IO/字指示灯；给列表加图片/位号/范围字段。
3. 需要时补“从 Options 导入”和只读下拉交互。
