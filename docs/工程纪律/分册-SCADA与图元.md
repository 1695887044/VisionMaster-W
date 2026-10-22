# 工程纪律分册 · SCADA 与图元

> 本册是 `AGENTS.md` §23 / §27 / §28 / §31 / §32 的**完整判据**（R71-R77、R87-R94、R95-R103、R139/R140/R141 的 B 义、R142-R147）；注入层只留指针与「最易踩」。
> 起因、异常栈、反编译取证见 `2026-10-22-工程纪律全集（原文归档）.md` 的同名章节。
> 读法：接到本域任务先读本册，改完按各条「守门」核对——**不看构建退出码**。

## 23. SCADA 图元（R71-R77）

| # | 判据 |
|---|---|
| R71 | **改 `Shard\Core.ScadaGenerators` 后必须重建全部引用它的图元工程**（`VM.Scada.Controls`、`VM.Scada.Controls.Charts`）；与 R16 同族，不看退出码。 |
| R72 | **图元私有属性走 `[ScadaProperty]`；基类外观属性走 `AppearanceProperties`/`TextProperties`，绝不许再声明一遍**（报 SPG0006）。同属性不同叫法用 `AppearanceProperties.Rename(...)`，**只换 DisplayName/Description**，Key/默认值/`TargetProperty` 仍只有基类一处（返回新数组，不就地改）。诊断 SPG0001~0006 一律 Error。守门：`[G]` 段 + `[R]` 段 18 条描述符快照。 |
| R73 | **新增图元控件类必须静态构造 `OverrideMetadata(DefaultStyleKeyProperty, ...)`**；不覆盖会继承基类模板 → `GetTemplateChild` 永远 null。**症状极静默**。 |
| R74 | **挂到新父前必须先 `ScadaElementBase.DetachFromParent`**（逻辑子元素直接抛异常）；**不要在布局过程中改正在被枚举的集合**（容器模板在 `MeasureCore` 就绪）。正解：有父的子图元直接交容器、不经过元素层。 |
| R75 | **曲线图元必须由宿主显式调 `ScadaChartElements.Register()`**（`App.cs` + `ScadaChecks` 各一处）；跨程序集静态构造不会因引用 DLL 就跑。守门：`[V]` 段。 |
| R76 | **底板容器：坐标保持画面绝对坐标，"父带动子"由 `ScadaPage.CascadeContainerMove` 在模型层做，回放时跳过**（`ScadaChangeScope.IsReplaying`）。三条：子图元 X/Y 不改相对坐标；选中簇不展开子图元；回放不联动。守门：`[C]` + `[X]` 段。 |
| R77 | **曲线视口由它自己推，不许依赖库的 `AutoScroll`**（只认定采样率系列 → 趋势图永远停在默认轴范围、看起来没画线）。**像素级验证在无头宿主做不到**（`WpfPlot` 无渲染目标）→ 只钉"数据进缓冲 + 描述符与绑定通道齐备"，**不钉像素**；**不许写无头环境永远为假的断言**。 |

## 27. 组态基座画面（R87-R94）

| # | 判据 |
|---|---|
| R87 | **基座 = 组合 + 实时引用，不做页面继承**：基座是普通 `ScadaPage`，页面只多 `BasePageId`；`BasePages` 单独存子集。**悬空不清理**（`ResolveBase` 返回 null 按无基座降级）。**集合订阅必须挂在 `[JsonConstructor]` 里那份字段实例上**（只挂 setter = 新建文档永不收通知且不报错）。注册/摘除各包一层 `BeginEdit`。 |
| R88 | **一层限制必须在两个入口都拦 + 加载期拆链**（`TryAddBasePage` + `TrySetBasePage`；`EnsureIdentity` 兜两道）。只拦一边 = 模型有链、画面只画一层。 |
| R89 | **导航口径唯一：`FindNavigablePage`/`FindNavigablePageByName`/`IsNavigable`；`FindPage` 只给编辑与持久化**。五个消费点走可导航判据。**"可寻址"≠"可导航"**；目标是基座时给**专属原因**。 |
| R90 | **基座图元必须进 `ScadaCanvas.EnumerateItems()`（基座先出、本页后出）**，叠放次序靠产出顺序不靠 ZIndex。**画面级钩子只对 `CurrentPage` 触发** → 断言一正一反（本页 1 次 / 基座 0 次）。底色始终取本页。 |
| R91 | **`SelectedItem` 的候选项类型必须与选中项一致；禁止 `CompositeCollection` + `ComboBoxItem` 占位清空强类型属性**（转换失败 = 源属性收不到通知、界面还显示占位、不报错）。正解 `BasePageOption`（`Page == null` 表示无基座），重建集合先 `Detach()` 再 `Clear()`。**连带**：选中项类型变了，画布绑定要改 `SelectedBasePage.Page`。 |
| R92 | **基座图元在引用页上只读，三个闸门缺一不可**：`SelectedElementsProperty` Coerce；`SelectedElementProperty` Coerce（**只拦集合不拦主选中会分叉**）；`CanEditElement` + 左右键命中（左键按点空白；右键要**清选中**让菜单退回画面级）。判据只有 `IsForeignBaseElement` 一处。守门：`[X]` 四条。 |
| R93 | **「新建基座」= 新建空白公共层，不是"把当前页提升为基座"**；命名「基座_N」取未占用最小号；建完**立刻选中它**。 |
| R94 | **渲染探针三条纪律**：`Activate()` 过的 VM 必须 `Deactivate()`（订阅**静态** `ScadaEditHistory.Changed`）；菜单命令断言先给 `PlacementTarget` 再泵 Dispatcher；**视图不许依赖 `App.xaml` 内联资源**（无 `Application` 的宿主取不到 = XAML 解析期整棵失败）→ 令牌进 `Colors.xaml` 并在视图就地合并。 |

## 28. SCADA 轴绑定（R95-R103）

| # | 判据 |
|---|---|
| R95 | **轴必须有稳定 `AxisId`，按 Id 寻址、名字只做展示与旧数据兜底**；历史轴由 `EnsureAxisIds()` 按 `(卡 Id, 物理轴号)` 生成确定性 Guid（`MotionAxisId.ForLegacy`），补齐必须在**算索引指纹之前**。改名/换轴号/换卡都不动 Id。守门：AX1。 |
| R96 | **"轴候选清单"是设计期配置，不是 `ScadaBinding`**（属性袋 JSON `ScadaAxisOptionsCodec`，属性行 `Kind=AxisList` 且 **`IsBindable=false`**）；写模型只走 `ScadaPage.TrySetAxisOptions`，解析只一处。 |
| R97 | **轴面板只认 `IScadaAxisSource`，绝不直接拿 `IMotionProvider`**；宿主 `MotionStatePublisher` 是唯一接触 `GetAxisStatus` 的地方（100ms 一拍、按物理轴去重、整表交换）。**`Version` 只在拓扑变化时递增**；**拉模式不是推模式**（面板在 200ms 节拍读）。守门：AX4/AX5。 |
| R98 | **"当前显示哪根轴"是控件状态，不写进 `.vms`**；单候选时下拉收起（三个可见性由控件算好、模板直绑——本环境 `ControlTemplate` 的 `DataTrigger` 不触发）。**删/停用不自动跳轴**（保留 Id、标"已移除/不可用/已停用"、量值"—"）。**离线不编 0**。 |
| R99 | **运动命令走动作表 + `MotionActionExecutor`，绝不由图元直接下发**：九条动作（`ScadaActionType` 7~15，只许末尾追加）→ `MotionCommandGate`（唯一门禁）→ `IMotionDevice.Enqueue`。① **下发 ≠ 到位**；② **有 `AxisId` 只认 Id，不落名字兜底**（错靶更危险）；③ **点动结束只在卡真收下停止后才清租约**。守门：AX7（含反向）。 |
| R100 | **系统变量 = 只读、派生、确定性身份，绝不进 `GlobalVariables`/`.vms`**：`VariableType.System`（末尾追加）、身份 `SHA1("motion-var\|{AxisId:N}\|{Field}")`、**不实现 `IWritableVariable`/`IRenameableVariable`**；`VariableRegistry` 用两张表 + 查找时用户优先。**`IPort.Value` setter 必须静默忽略而不是抛**（UI 线程 + App 无 `DispatcherUnhandledException` → 抛就是退进程）。守门：AX8。 |
| R101 | **系统变量名是派生量：轴改名时集合不增不减，必须主动重扫名字索引**（`Rebuild` 记账 `renamed` → `RefreshSystemNames()`）。守门：AX8 反向断言"新名可查、旧名查不到"。 |
| R102 | **轴改名级联三跳，注册表只管前两跳**（点位表 + 凸轮表；画面这跳由宿主 `RefreshAxisReferences` 补）；整链套 `ScadaWriteGuard.Suspend()`。**已知残余**：`AxisOptions` 名字不回填。 |
| R103 | **"设计期配置 + 运行期选择"的图元必须分两段测**：设计段（候选/单轴收起/空态/不编造量值）与运行段（订阅节拍/默认选第一根/量值落地/切轴换源/删轴不跳/摘上下文归零）。**模板必须真 `ApplyTemplate` 一次并查命名件。** |

## 31. 流程栏 v2 视觉（R139/R140/R141 的 B 义 + R142-R146）

| # | 判据 |
|---|---|
| R139 | **树行铺满整行时 `ContentPresenter` 必须 `Stretch`**，section 色条靠负边距 + 箭头 `ZIndex`：模板根 `Margin="-22,…"`、内容 19/22 左内边距拉回 30px 竖线、`Expander` 设 `Panel.ZIndex="2"`。**改 Padding/箭头列宽时 -22 与 19/22 要同步。** 守门：`RenderProbe` 场景 F + 三条点击拦截。 |
| R140 | **`ObservableCollection` 只替自己发 `Count`，不替容器发聚合值**：各分支 `Steps.Count` 之和必须自己订阅（`Children` 增删 + 每分支 `Steps` 增删）；已封装 `ContainerStepCountWatcher`。**没有通知的只读计算属性 = 界面永远停在旧值。** |
| R141 | **`RenderProbe` 出图的豆腐块不是产品缺陷**（探针把 `Icon` 设成 `Segoe MDL2`，优先于场景 F 后合并的内联资源）。探针图只判布局/底色/对齐，字形回真机核对；**不要把 tofu 当字体 bug 修**。 |
| R142 | **同类图元的多个"预设"只能一个控件类 + 多套描述符默认值**（轴控台/轴面板/旧点位表共用 `AxisPanelElement`：一个 `AxisOptions` + 一个 `SelectedAxisId`）。默认值走 `ScadaPropertyCatalog.WithDefaults(...)`；旧 TypeKey 保留但 `ShowInToolbox=false`。**切轴必须"先对旧轴收手"**：`SelectedAxisId` 变化时用 `e.OldValue` 发 `JogStop`（**不能用按当前轴的 `EndJog()`**）。守门：AX11 + AX10。 |
| R143 | **改面板默认宽度必须同时处理已保存的 `Layout.xml`**（恢复用文件里的绝对像素覆盖 XAML）。正解：反序列化后按 `ContentId` 找列并**夹紧**（`EnsureProcessColumnWidth`），只动该列，**不升版本**。守门：`RunPanelModeChecks` 第 ⑨ 条。 |
| R144 | **出厂快照 `DefaultLayout.xml` 必须每次启动重捕获，"加载布局"命令不许刷新它**（`Load(refreshDefaultSnapshot)`：启动 true、加载命令 false）。捕获前先 `UpdateLayout()`。**恢复命令必须"先确认、失败也说话"。** 守门：`RunPanelModeChecks` 第 ⑩ 条。 |
| R145 | **AvalonDock 三条硬语义**：① `DockHeight=0` 会被夹回原值，收起用 **1px**，幂等判据不能用高度；② 同 pane 多 anchorable 是标签页，切标签看 `IsSelected`（`ShowPanel` 少一句 `anchorable.IsSelected = true` 会停在最后 Show 的标签）；③ `ShowAnchorable` 会重挂 `LayoutContent`，切换后旧引用失效，断言必须重新 `FindPanel`。**组态模式收敛**（`ApplyScadaPanelMode`）：左栏收起 + 日志 1px，退出恢复；属性/图层合并属面板归属变更，**必须升版本**（3→4）。守门：`RunPanelModeChecks` ③b 三条。 |
| R146 | **新增库样式键前必须全仓查同名，键名带用途后缀**：① 加键前 `grep 'x:Key="你的键"'` 扫 `UI\Controls\Themes` 全目录；② 按钮用 `...Button`，胶囊/徽标用 `DialogTag`/`DialogChip*`，**同名不同 TargetType 一律视为冲突**；③ 改完必须真机开一次对应弹窗（`XamlParseException` 只在模板实例化时抛）。 |

## 32. SCADA 属性分组（R147）

| # | 判据 |
|---|---|
| R147 | **组名只能来自 `ScadaPropertyGroups`**：① **新增分组 = 先登记**（常量 + 进 `Order`），否则 `ElementDescriptor.Validate` 判失败、图元进不了工具箱；② 顺序由 `Order` 决定，未知组排最后并保持首现次序；③ **"不可绑"只有 `NonBindable` 一处**；④ 改名只改常量值并同步 `ScadaChecks` 显示名快照。守门：`[AH]` 十条（含两条反证）。**唯一豁免**：`ScadaPropertyGenerator.cs` 里未写 `Group` 的兜底字面量 `"外观"`。 |

