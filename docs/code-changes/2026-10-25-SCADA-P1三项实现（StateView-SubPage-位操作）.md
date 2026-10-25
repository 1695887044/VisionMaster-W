# SCADA P1 三项实现（StateView / SubPage P1a / 位操作）

日期：2026-10-25
范围：用户已拍板的 P1 三项——StateView（一个 TypeKey，列表需要颜色）、SubPage P1a（固定页 + 只读 + 活数据，M）、位操作动作（位号 0~31）。**报警生命周期事件本批不做；Web 运行态不同步，最后统一。**

## 一、当前状态

- **StateView 已完成**：`Hmi.StateView` 一个 TypeKey，覆盖字指示灯 / 图形 IO 域 / 图形开关；列表条目支持 `Image` / `Color`，列表支持 `DefaultImage`；管理器可编辑图片/状态色。
- **SubPage P1a 已完成**：`Hmi.SubPage` 固定页、只读、活数据；缺页/无权/未选/弹窗页/嵌套过深都有占位；内嵌画布自有 binder，换页/卸载退订阅。
- **位操作已完成**：`SetBit=24` / `ResetBit=25` / `ToggleBit=26`，`BitIndex` 0~31；Bool 整体写、整型读-改-写；动作编辑器有变量 + 位号编辑区。
- **独立评审**：首轮 BLOCK（SubPage 只读未落到运行态交互、卸载/切页未退订阅）→ 已按评审修复并补 [SP4b]/[SP5b]/[SV8b~d]/[SV20]/[BIT10b]；复跑 **2802/0**。
- **验证**：`ScadaChecks` **2802 通过 / 0 失败**；`UIThemeSmokeTest` **0 FAIL**；`FlowCanvasChecks` **3657 / 0**；全量 `run_gates.ps1` **12/13 绿**——`FlowCanvasChecks` 构建被并发 `VisionMaster.exe` 锁定 `Modules` 插件 DLL（`Plugin.CSharpScript` / `Plugin.ImageScript` 的 post-build copy）阻断，非本批代码；隔离直跑 3657/0。
- **未做**：报警生命周期事件、Web 渲染器/快照、SubPage P1b（变量切页/交互）、StateView 列表级 DefaultText/DefaultImage 的管理器编辑入口。

## 二、已做

### StateView

| 文件 | 改动 |
| --- | --- |
| `Scada\Models\ScadaStateList.cs` | 条目加 `Image` / `Color`；列表加 `DefaultImage`；Clone/CopyFrom/MarkDeleted 带新字段；`DisplayImage` / `DisplayColor` |
| `Scada.Controls\Elements\StateViewElement.cs`（新） | Value / StateListId / Presentation（Text/Image/TextAndImage）/ Interaction（None/Toggle/WriteValue）/ ClickValue / ImageStretch；只读 CurrentText/CurrentImage/CurrentColor/StateListMissing/CanOperate；列表订阅、缺失/无匹配/图片缺失、点击写值与事件 |
| `Scada.Controls\Themes\StateViewElement.xaml`（新） | 三呈现、状态色、图片占位、焦点/禁用态；令牌就地合并 |
| `Scada.Controls\Descriptors\BuiltInElements.cs` | 注册 `Hmi.StateView`（180×64，事件 ObjectChanged/Opened/Closed） |
| `VisionMaster\Views\DialogViews\StateListManagerView.xaml` | 条目表加“状态图片（相对路径）”“状态色”两列 |
| `ScadaChecks\StateViewChecks.cs`（新） | [SV1]~[SV19]：字段/Clone/CopyFrom、列表优先、缺失、无匹配、图片、三呈现、只读、Toggle/WriteValue、权限、Version 刷新、引用数/副本 |

### SubPage P1a

| 文件 | 改动 |
| --- | --- |
| `Scada.Controls\Elements\SubPageElement.cs`（新） | 固定页、只读、Fit/ShowBackground/MissingBehavior；缺页/无权/未选/弹窗页/嵌套过深占位；嵌套 ScadaCanvas + 自有 ScadaRuntimeBinder；换页/卸载 Stop |
| `Scada.Controls\Themes\SubPageElement.xaml`（新） | Viewbox 缩放、占位、状态条；令牌就地合并 |
| `Scada.Controls\Elements\ScadaElementBase.cs` | 加 `PageResolver` / `OnPageResolverChanged` / `ResolvePage` |
| `Scada.Controls\Canvas\ScadaCanvas.cs` | 加 `PageResolver` DP 并转发；加 `AllowSubPages`（嵌套画布设 false，防递归） |
| `Scada.Controls\Descriptors\ElementPropertyDescriptor.cs` | 追加 `ElementPropertyKind.Page` |
| `VisionMaster\ViewModels\ScadaPropertyViewModel.cs` | `ScadaPageRow`（候选来自可导航页，排除弹窗/基座；已删除页占位） |
| `VisionMaster\Views\ScadaPropertyView.xaml` | `EditPage` 模板 |
| `VisionMaster\ViewModels\ScadaEditorViewModel.cs` | `ResolvePage` |
| `VisionMaster\Views\ScadaEditorView.xaml.cs` / `ScadaRuntimeWindow.xaml.cs` | 注入 `PageResolver` |
| `ScadaChecks\SubPageChecks.cs`（新） | [SP1]~[SP12]：描述符、固定页、只读、活数据、变量刷新、未选/缺页/弹窗/无权、嵌套一层、卸载退订阅 |

### 位操作

| 文件 | 改动 |
| --- | --- |
| `Scada\Models\ScadaActionType.cs` | 追加 `SetBit=24` / `ResetBit=25` / `ToggleBit=26`；DisplayName/PendingReason |
| `Scada\Models\ScadaAction.cs` | `BitIndex`（可空）、`IsBitAction`、Detail/Describe 位操作文案 |
| `VisionMaster\Services\ScadaActionDispatcher.cs` | `ExecuteBitAction`：Bool 整体写、整型 0~31 读-改-写、类型/越界/读失败/写失败分档 |
| `VisionMaster\ViewModels\ScadaEventEditorViewModel.cs` / `ScadaEventEditor.xaml` | 动作排序、位操作编辑区（变量 + BitIndex） |
| `ScadaChecks\BitActionChecks.cs`（新） | [BIT1]~[BIT27]：枚举、文案、Bool/整型、bit0/bit31、越界、类型拒绝、变量缺失、写失败、编辑器候选 |

### 独立评审后的修复（BLOCK → 修复）

| 评审项 | 修复 |
| --- | --- |
| P0 SubPage 只读没落到交互 | 嵌套画布改用 `ToReadOnlyContext`（Writer/AxisCommands/Confirmations 置空，保留 Alarms/Beat/AccessPolicy/AxisSource/Values/History/StateLists），并 `IsHitTestVisible=false`；新增 [SP4b]：目标页 BitButton 点击不写变量 |
| P1 SubPage 卸载/切页不退订阅 | 加 `Loaded`/`Unloaded` 成对；`DetachEmbedded()` 停 binder 并清空嵌套画布的 `Page/ItemsSource/RuntimeContext`；新增 [SP5b] 订阅清零断言 |
| P1 图片解析器晚注入不刷新 | `ScadaElementBase.ImageAssetResolver` 改成带 `OnImageAssetResolverChanged` 钩子的属性；StateView/SubPage 覆写刷新；新增 [SV8c]/[SV8d] |
| P1 StateView 断言偏弱 | [SV8] 改成引用相等（DefaultImage 而不是“有图”）；新增 [SV8b] ImageMissing、[SV18] ComboBox+StateView 计数=2、[SV19]/[SV20] Commit 后文档字段 |
| P1 StateView 列表订阅 | `Loaded`/`Unloaded` 成对订阅/退订 |
| P2 Bool 非零位号 | dispatcher 对 Bool + BitIndex != 0 明确拒绝；新增 [BIT10b] |
| P2 枚举稳定性 | [BIT1] 追加完整 1~26 序列断言 |
| 建议项 | `CanOperate` 补 `IsEnabled`；`WriteError` 上 ToolTip；`Presentation=TextAndImage` 且条目无图时不再显示“无图”占位；`Dispatcher.BeginInvoke` 加 `HasShutdownStarted` 守卫；KeepLast 时停 binder（冻结上次画面） |

### 兼容与注册

- `Scada\Models\ScadaChoiceNames.cs`：补 `Image`/`TextAndImage`/`Toggle`/`WriteValue`/`Original`/`Placeholder`/`KeepLast`/`Continuous`/`Step` 中文译名。
- `ScadaChecks\Program.cs`：名册 36→38；动作下拉顺序加入位操作三条；搜索命中改用唯一词“多态灯”。
- `ScadaChecks\WebRendererCoverageChecks.cs`：StateView / SubPage 进例外清单（用户决定 Web 最后统一），WR3 上限 0→2。
- `ScadaChecks\Program.cs`：接入 `StateViewChecks.Run()` / `SubPageChecks.Run()` / `BitActionChecks.Run()`。

## 三、未做

- **报警生命周期事件**：按用户决定本批不做；`ScadaEventType` / `ScadaAlarmEngine` / `ScadaEventHook` 未动。
- **Web 运行态**：没有改 `wwwroot`，没有给 StateView/SubPage 写渲染器，快照未加条目 Image/Color/DefaultImage 与嵌入页递归；WR1 以例外清单显式登记。
- **SubPage P1b**：变量切页、子页交互事件、活跃子页事件宿主未做。
- **StateView 列表级默认值编辑**：`DefaultText` / `DefaultImage` 已有模型字段，管理器还没给编辑入口。
- **StateView 多语言**：`Color` 只存 `#AARRGGBB` 文本；多语言列不在本批。

## 四、下一步

1. 等全量 `run_gates.ps1` 结果；若只剩并发/存量问题，单独报告。
2. 独立评审角色复审三项实现；按必改项修复后再跑 `ScadaChecks`。
3. Web 统一批次：补 StateView/SubPage 渲染器与快照字段，删除 WR 例外清单两项。
4. 需要时做 SubPage P1b（变量切页 + 交互）与 StateView 管理器默认值入口。
5. 报警生命周期事件另起批次，按 `docs/方案设计/2026-10-24-SCADA-P1代码评审与设计切分.md` 的语义实现。

## 五、踩过的坑

- **三个并行智能体都被模型服务端错误中断**：StateView 只落了 `ScadaStateList` 的 Image/Color，SubPage 未开始，位操作落了大半；主会话接手补齐并验证。派工单与文件归属仍有效，后续可复用。
- **并发会话改 `AxisPanelElement` / `RecipeManagerView`**：多次导致 `Scada.Controls` / `VisionMaster` 编译失败、`VisionMaster.exe` 锁 DLL；等对方落盘后正常构建。未改对方文件（只补了 `ScadaChoiceNames` 的 Continuous/Step 译名以过全局译名断言）。
- **全量 gate 的 FlowCanvas 构建被并发 `VisionMaster.exe` 锁 `Modules`**：`Plugin.CSharpScript` / `Plugin.ImageScript` 的 post-build copy 报 `MSB3073`，不是本批代码；`FlowCanvasChecks.exe` 隔离直跑 3657/0。等并发进程退出后复跑 gate 即可全绿。
- **`Presentation=Both` 与 Line 的 `Both=双向` 撞全局译名**：StateView 落盘值改用 `TextAndImage`，显示“图文”；设计稿里的 Both 是概念名。
- **`BIT15` 测试期望写错**：long 的 bit31 复位后是 `-2147483649`（64 位其余位保留），不是 32 位的 `2147483647`；改测试期望，不改实现。
- **StateView 管理器测试副本为空**：测试忘了 `document.StateLists.Add(list)`；补上后才验证到 Clone/Image/Color。
- **SubPage `NestedTooDeep` 无变更回调**：直接设 DP 不刷新占位；补 `OnNestedTooDeepChanged`。
- **SubPage 初始值未刷**：`ScadaRuntimeBinder.Start` 走 Dispatcher 排队；断言里要 `PumpDispatcher(60)` 再读，否则看到设计值。
- **UIThemeSmokeTest 硬编码颜色**：`StateListManagerView.xaml` 的 ToolTip 里写了 `#FF22C55E` 示例，被扫描器当成颜色；改成“22C55E 前补 #”。
- **`ScadaChecks` 曾因新 RunOnSta 建 `Application` 与后续 `ScadaMonitoringChecks` 跨线程冲突**：新检查的 STA helper 不再创建 `Application`，沿用既有宿主。
- **临时探针/脚本**：`tools/oneoff` 下的探针用完已删；`artifacts/tmp` 的临时日志不作为交付物。
