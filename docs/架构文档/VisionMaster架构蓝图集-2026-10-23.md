# VisionMaster 工程架构蓝图集

**版本**：v1.0 · 2026-10-23 · 出图人：主会话（Qoder）· 依据：2026-10-23 当日读码 + 当日 gate 日志 + `find`/`grep` 实测计数
**覆盖范围**：宿主/界面层、契约与源生成基座、领域与运行时服务、插件层、SCADA 运行态（桌面 + Web）、HALCON 成像与标注、帮助手册管线、验证闸链、主题注入风险面
**交付形态**：本文（可复核的文字版）+ `vm-blueprint-suite.html`（同内容的蓝图视觉版，9 张同页带页签）

> **这份文档的定位**：它不是 `ArchitectureGuide.md` 的替代品。后两者是概念级概览（76 / 100 / 286 行，成文更早、无行号锚点）；本文的特点是**每个框后面都带 文件:行**，并且数字全部当场复算过。两者并存，冲突时以本文为准、并顺手更新本文的"口径修正"一节。
>
> **R126 边界**：`.html` 是沟通媒介，不是交付物。它不进构建、不部署、不作第二客户端维护（已核：无 csproj/props/targets 以 glob 引用 `docs\`，构建不会捡走它）。

---

## 0. 失效条件（先说这个，免得日后拿旧图当真话）

| 会失效的部分 | 什么时候失效 | 怎么发现 |
|---|---|---|
| 所有 `文件:行` 锚点 | 文件被增删行 | 行号只是导航辅助。判据请以**文件 + 方法名**为准；重定位命令见 §9 |
| 断言条数（7 038） | 任何一次 gate 之后 | 每次收尾以 `artifacts\logs\<yyyyMMdd>\gate-*.log` 的 `通过: N  失败: M` 为准 |
| 结构计数（39 / 53 / 311） | 新增插件工程、新增手册页 | §9 的 `find` / `grep` 一条命令就能重算 |
| 分层与红线、闸门机制、症状族 | 基本不失效 | 这些是设计约束，变了会在 AGENTS.md 里留新规则 |

**并发改动提示（R9）**：本文成文当天仓库里另有两条改动串——09:33 的 `VisionMaster\App.xaml`（Input.xaml 提到 Fluent 之后重挂，见 §7）与 13:13 的 `Plugins\Plugin.DlSegment\DlSegmentPlugin.cs`（13:15 那轮 FlowCanvasChecks 曾以它两条红收尾，13:33 官方 gate 已全绿）。所有断言数只保证 13:30–13:34 那一轮。

---

## 1. Sheet 00 · 总览

```
L1 宿主/界面层      VisionMaster\ · UI\
   Shell 主窗 · 面板与弹窗 · 启动自检链 · UI 令牌与分册 · 帮助手册管线 · 【主题注入风险面】
        ▲ 绑定 / 命令 / 资源令牌 ▼        契约单向：宿主只依赖 Shard 与领域层
L2 契约与代码生成    Shard\
   Core.Interfaces · Core.Controls · Core.PluginGenerators(Roslyn) · Core.ScadaGenerators(Roslyn)
        ▲ 实例化 / 端口读写 / 编译执行 ▼  红线②：契约程序集不进 Modules\
L3 领域与运行时      Core\ · Engine\ · Services\ · Scada\ · Communication\
   VM.Core · VM.FlowEngine · Core.Halcon · VM.Communication · Scada 运行时 · 支撑服务
        ▲ Modules\ 装载 / 契约回调 ▼      红线①：插件源码只许住在 Plugins\
L4 插件层            Plugins\  →  Modules\
   39 工程 → 39 程序集 → 53 个带 GroupName 的类 · 311 页随件手册
```

依赖方向：L1 → L3 → L2（契约）；L4 只实现 L2 契约，由 L3 的 `PluginService` 在运行期从 `Modules\` 装载。**宿主不反向依赖具体插件**。

旁路面（不是第五层）：

| 条目 | 落点 | 口径 |
|---|---|---|
| 运行态 Web 客户端 | `Web\VisionMaster.ScadaWeb` | **R132A 例外**：全仓唯一被裁定为交付物的 Web 产物 |
| 日志与渲染产物 | `artifacts\logs\<yyyyMMdd>\` · `artifacts\render\<yyyyMMdd>\` | 禁止 `> xxx.log` 落根（R166） |
| 依赖包 | `packages\` | `tools\DeviceSimulator` 用 HintPath 直引，**不许删**（R169） |
| 纪律资产 | `AGENTS.md`（53,608 字节 < 56,000 门槛）· `docs\工程纪律\分册-*.md` · `docs\code-changes\` | 注入层满了就下沉，不压缩既有判据 |

**三条红线与后果**（违反即回滚）：

| 红线 | 判据 | 后果 |
|---|---|---|
| ① 插件工程必须放 `Plugins\` 下 | 目录位置 | `Directory.Build.targets` 按目录投递，放别处加载不到 |
| ② 契约程序集不许拷进 `Modules\` | `Modules\` 内 0 个 `Core.*.dll`（实测） | 两份静态状态，图元投进去取不出来 |
| ③ 改了插件源码必须重建该插件工程 | DLL mtime ≥ 源码 mtime | `Modules\` 不由宿主带出，只重建宿主仍是旧 DLL，且**无任何报错** |

---

## 2. Sheet 01 · 插件：构建投递 → 扫描缓存 → 配置 → 编译执行

### A. 构建期

| 环节 | 位置 | 说明 |
|---|---|---|
| 39 个插件工程 | `Plugins\*.csproj` | 全部已登记 `VisionMaster.sln`（grep 命中 39） |
| 源生成器不随件 | `Plugins\Directory.Build.props` | `OutputItemType=Analyzer` + `ReferenceOutputAssembly=false`（R17） |
| 编译期展开 | `Shard\Core.PluginGenerators\StepConfigGenerator.cs:45` | 生成 `<Ns>.<Class>.StepConfig.g.cs`（`:259-261`）；违规报 CPG0001~0005（`:55-80`） |
| 投递 | `Plugins\Directory.Build.targets:41` `CopyPluginToModules` | **只拷 `$(TargetPath)`，不拷依赖** |
| 落点 | `Modules\Plugin*.dll`（实测 39 个） | 红线③ 的靶子 |

> 红线② 的实现方式是 **omission**（压根不拷依赖），不是一条过滤规则。所以任何"顺手把依赖也带上"的改动会立刻造出两份契约静态状态 —— 这一点在图上值得单独一格，因为它看起来像是"构建脚本更健壮了"。

### B. 启动扫描（一次反射，三处过滤）

`Engine\PluginService.cs`：`InitPlugins() :55` → 先注册内置控制流工具 `:151-310`（Break/Continue/Return 不进 Modules）→ `GetFiles(dir, "Plugin*.dll") :110`（R80 名字过滤）→ `Assembly.LoadFrom :322`（**无 AssemblyLoadContext**）→ `GetTypes() :327` 一次物化、三处各写过滤器：`:339` 视觉插件 / `:442` 运动驱动 / `:483` 相机驱动 → `ToolItemModel` + 端口快照 `:343-386`（`ModuleTypeName = AssemblyQualifiedName` `:335-349`；隐藏端口从定义剔除 `:357`）→ `Engine\PluginProvider.cs:65 RegisterModule` → 工具箱按 `Category` 分组 `VisionMaster\ViewModels\ToolViewModel.cs:453`。

只有 `:339` 那支会 `Activator.CreateInstance`（元数据原型）。

### C. 清单缓存：四道指纹，任一失配即全量重扫并重写

`Engine\PluginCache.cs`，清单落 `Modules\PluginCache\manifest.json`（`:146`，实测 39 条 DLL / 53 条 ModuleTypeName）：

| 判据 | 位置 | 失配动作 |
|---|---|---|
| `SchemaVersion = 2` | `:39` / 比对 `:92` | 全量重扫 |
| 每 DLL size + mtime | `:101-102`（文件缺失 `:100`） | 全量重扫 |
| 未登记的 `Plugin*.dll` | `:108-112` | 全量重扫 |
| 宿主契约指纹 | `:192-211` = `Core.Interfaces.dll|size|mtimeTicks` | 全量重扫 |

- 契约指纹**刻意不用 AssemblyVersion**（`:183-186`）。
- 不做部分缓存：任一失配 → 重扫**并重写**（`PluginService.cs:104-136`）；加载失败**删清单**而不是写半份（`:126-132`、`PluginCache.Invalidate :154`）——R81。
- **命中仍 `LoadFrom` 全部程序集**：省的是 `GetTypes()` 与构造，不是装载（`PluginCache.cs:16-18`）。下游自动连线、编译、帮助资源都要类型身份。
- 跳过实例化的前提是"构造器无副作用"，由 PC8 静态扫描钉住：`FlowCanvasChecks\PluginCacheChecks.cs:244`（扫描面收窄到构造器体，R82）。

### D. 参数配置（第三处实例化）

`VisionMaster\Services\StepParameterDialog.cs`：`Open(...) :55`（只对 ActionStep / ConditionStep / For / Wait / Parallel 开窗，其余返回 false，R25）→ `ResolvePluginInstance :218-234` 每次新建草稿实例 → 视图二选一：插件自供 `IPluginCustomViewProvider.GetConfigView :74-78`，否则通用 `AutoPortConfigView :96`；并行分组走 `FlatPropertyGrid :136-149` → Prism 外壳 `PluginConfigShell :112`（认 `view as IPluginConfigView ?? view.DataContext as ...`，`PluginConfigShellViewModel.cs:149`）→ 上下文只读透传 `SetConfigContext :190`（FlowName / HTTP 端口令牌 / 相机 / 变量 / `IDatabaseAccess`；契约 `Shard\Core.Interfaces\IPluginConfigContextProvider.cs:23,184-191`）。实现者 5 个：`ImageAcquisitionPlugin.cs:558` · `DatabasePluginBase.cs:292` · `DataRecordPlugin.cs:217` · `VariableAssignmentPlugin.cs:121` · `CSharpScriptPlugin.cs:725`。

端口语义三纪律：**值**类端口才配绑定弹窗，**名字**类（变量名/步骤名/卡地址/端口名）不许挂绑定（R33）；手填只产文本，写入侧按声明类型 `Convert.ChangeType` 宽进严出（R34）；用户意图只认真实输入事件，不许从"值变了"倒推（R35）。

### E. 编译与执行

| 环节 | 位置 |
|---|---|
| 编译入口 | `Engine\FlowCompiler.cs:244`（调用点 `ShellViewModel.cs:1461,1529`） |
| 运行期实例化 | `FlowCompiler.cs:843-852` `Type.GetType + Activator.CreateInstance`；端口声明成字段只在编译期暴露 `:857-872` |
| 编译产物 | `Core\Models\CompiledFlow.cs`；条件变量未绑数据源 = 编译失败并指名变量（此前静默取 0 → 恒走 Else） |
| 起跑 | `Engine\FlowEngineService.cs:433-444`（另径 `:616-626`）建 `ExecutionContext` → `ExecutionEngine.Run` |
| 节点序 | `CompiledFlow.cs:55-57` → `Core\Models\Compileds\CompiledNode.cs:207` → `CompiledPluginNode.cs:41-43`（先 `BindContextAwarePorts` 再 `ExternalPlugin.Execute`） |
| 插件体 | `Shard\Core.Interfaces\VisionPluginBase.cs:108 Execute` → `:209 abstract RunAlgorithm`；参数表 `GetConfigProperties :301` / `ApplyConfigValues :312` |
| 算子与结果 | 插件直调 `HOperatorSet`（例 `Plugin.BlobDetect\BlobDetectPlugin.cs:1314,1445,1542-1558`）；标注经 AnnotationRenderer → `AnnotationBaker.Bake`（§4）落输出端口 `:1377` |

**为什么三处实例化要记住**：扫描期原型只取元数据与端口表（`PluginService.cs:339`）；编译期节点实例才是运行体（`FlowCompiler.cs:847`）；配置窗每次新建草稿实例（`StepParameterDialog.cs:234`）。三者不是同一个对象 —— 任何"在构造器里副作用初始化硬件"的写法都会被打三次，PC8 就是拦这个。

契约面速查：`IVisionPlugin.cs:11`（Inputs/Outputs `:18-19`、`Execute(IExecutionContext)` `:21`、`LastError` `:26`）；`[StepConfig]`（`StepConfigAttribute.cs:11`）；`InputPort<T>`（`DataPort\InputPort.cs:15`，由公有属性反射 `VisionPluginBase.cs:421-429`）。

---

## 3. Sheet 02 · SCADA 轴命令：两条入口 · 一个执行器 · 下发 ≠ 到位

### 单一执行器（R108/R136 的结构保证）

汇合点：**`VisionMaster\Services\Motion\MotionActionExecutor.cs:124 TryExecute(ScadaAction, out string)`**

- 入口①（轴面板按钮）：`Scada.Controls\Elements\AxisPanelElement` 的 `TryExecuteCommand` → `Scada\Models\IScadaAxisCommandSink.cs:67` → 实现 `MotionActionExecutor.cs:185 → :218`。定位走 `TryMove :332`，它**合成** `ScadaActionType.MotionMoveAbsolute` 再调 `:342 TryExecute`，不另开一条路。
- 入口②（组态动作表）：`Scada\Models\ScadaRuntime.cs:289 RaiseHooks` → `VisionMaster\Services\ScadaRuntimeHost.cs:752 OnElementEventRaised` → `VisionMaster\Services\ScadaActionDispatcher.cs:196 DispatchWithResult`（循环 `:266`）→ `ExecuteMotionAction :725` → `_motions.TryExecute :736`。
- 同实例可证：`VisionMaster\Modules\MotionModule.cs:96` 与 `:102` 把 `IScadaAxisCommandSink` 与执行器注册为同一单例。动作类型 9 种：`Scada\Models\ScadaActionType.cs:48-72`。

### 权限闸门（按下前判，每拍重算）

策略契约 `Scada\Models\IScadaAccessPolicy.cs:95`；两份实现 `Scada\Models\DefaultScadaAccessPolicy.cs:52` 与宿主 `VisionMaster\Services\ScadaAccessPolicy.cs:147`，都落到 `ScadaRole.Allows`。四处求值（全在 `AxisPanelElement.cs`）：只读 DP `:289/:440`；`RefreshCanOperate :540`（调策略 `:544`）由 `OnBeat :1055` 每拍驱动（另 `:780/:843/:1029/:1083`）；`DelegateCommand.CanExecute :381-382`；行内 `CanEdit :1252`；模板 `Scada.Controls\Themes\AxisPanelElement.xaml:468` 把 `CanOperate` 绑到 `IsEnabled`。**最后一公里**再判一次：`CheckCanRun :503`（策略 `:527`）——防"按下那一刻与真正下发之间权限变了"。

### 物理判据：预检 + 设备层权威副本

| 层 | 位置 | 判据 |
|---|---|---|
| 执行器 | `MotionActionExecutor.cs:448 ExecuteMove` → `:461` | 进命令闸 |
| 命令闸 | `Shard\Core.Interfaces\Motion\MotionCommandGate.cs:52`；预检 `:148`（软限位）`:155`（未使能）`:168-173`（未回零）`:175`（忙）；点动越界**静默** `:211` | 同一份谓词 |
| 软限位定义 | `Shard\Core.Interfaces\Motion\MotionDescriptor.cs:276 AxisMapping.IsWithinSoftLimit` | 唯一实现 |
| 入队 | `MotionActionExecutor.cs:695 NewAxisCommand` → `:649 Enqueue` | 唯一入队口 |
| 设备 | `Shard\Core.Interfaces\Motion\MotionDeviceBase.cs:622 Enqueue` → `:786 CheckGateLocked` → `:946 CheckMotionPreconditionsLocked`（`:982/:991/:998`） | 锁内权威副本 |

跨层一致性由 `FlowCanvasChecks\MotionChecks.cs:906-955` 断言（比对预检与设备结论是否同一）。**为什么必须有这条**：两套谓词各写一遍时，界面放行、设备拒绝（或反之），症状是"按钮能点但不动"，编译与静态扫描都看不见。使能与安全拆分在 `MotionDeviceBase.cs:105/:86`；流程步算子共用同一谓词 `Plugins\Plugin.Motion.Steps\MotionEnablePlugin.cs:115`。

### 三种节拍，谁也不等谁

| 节拍 | 位置 | 作用 |
|---|---|---|
| 驱动线程 10 ms | `Shard\Core.Interfaces\Motion\MotionDescriptor.cs:334 PollIntervalMs = 10`（夹取 `MotionDeviceBase.cs:1314` → 1~1000） | 真机位置与状态来源 |
| 画面节拍 200 ms | `Scada.Controls\Runtime\ScadaBeatSource.cs:41 DefaultIntervalMilliseconds = 200` | 驱动 `CanOperate` 重算、点位行刷新、点动看门狗 |
| 状态发布 100 ms | `VisionMaster\Services\Motion\MotionStatePublisher.cs:48 PublishIntervalMs = 100`；`Tick :126` → `Publish :148` → `GetAxisStatus :164` | 快照带 `PositionMm/Moving/InPosition`（`:177-181`）；换表 `:200`；**只有拓扑指纹变才 `Version++`** `:214` |

**下发 ≠ 到位**（R99）：执行器成功文案原文 `已下发（未等到位）`（`MotionActionExecutor.cs:677`，语义声明 `:14-20`）。要等到位是另一个算子：`WaitStep` / `Core\Models\Compileds\CompiledWaitNode.cs:36`（50 ms 轮询）。把"下发成功"读成"到位"是最贵的一次误判——它会让人以为机械已经停在那儿了。

**R100 静默 setter**：`Core\Models\Variable\SystemVariableModel.cs:137-145` 的 `IPort.Value` setter 只 `Debug.WriteLine`、**不抛**（原因 `:129-135`：UI 线程 + App 无全局异常处理时抛就是退进程）；`Value` 对外只读 `:124`，唯一写入口 `UpdateMirrorValue :166`。

**R138 每拍重读 + 指纹短路**：`MotionStatePublisher.TryGetPoints :276` 读活表（`MotionDescriptor.GetAxisPoints :290`，配置类型是 `List<MotionPoint>`，外部改动无通知）；`AxisPanelElement.RefreshRows :568` 比对指纹 `:600`，**没变化不重建行**（短路 `:588`，只在变化时重建 `:593-597`），由每拍 `:1051` 驱动。判据口诀："先问谁还会改它、改完有没有通知"——不重读就永远显示旧点位，无脑重建就吃掉用户正在输入的那个框。

### 点动与回零（全 code-behind，无 XAML 事件）

`OnApplyTemplate :812` 取六个命名件 `PartBtnEnable / Stop / Home / JogForward / JogReverse / ClearAlarm`（`:785-791`，由 `ScadaChecks\AxisPanelChecks.cs:180-185` AX3 钉死命名；AX9 在 `:1055-1106`）→ `AttachJog :849`：`CaptureMouse :859`，Down `:875` / Up `:876` / `MouseLeave :877` / `LostMouseCapture :878` → `StartJog :895` / `EndJog :909` / `EndJogForAxis :924`（`_jogHeld :800` 记靶轴，处理"按住中途换轴"）→ 回零长按 `HomeHoldDelay = 500 :794`（`OnHomePressed :951` → `OnHomeHoldElapsed :961`，短按不开闸）→ 看门狗 `WatchdogPeriod 200ms :62`、超时 `MotionJogGuard.PulseTimeoutMs × 2 :732`，**租约只在驱动接受后才清 `:639`**（不清就自动补停，避免"界面以为停了、驱动还在跑"）。

禁止项：不许在 `Control` 子类声明名为 `IsEnabled` 的 DP（遮蔽 `UIElement.IsEnabled`，R108）；不许绕过 `Enqueue` 直调驱动；示教只写"当前轴位置"且离线明确拒绝（R136）；行 `DataTemplate` 没有 `TemplatedParent`，颜色/可见性/可编辑性一律 `RelativeSource AncestorType=图元类型`（R136）。

---

## 4. Sheet 03 · HALCON 双路径：离屏烘焙像素 · 屏内交互 ROI

### 甲 · 离屏烘焙（唯一 `OpenWindow` 落点）

入口 `Services\Core.Halcon\Helpers\AnnotationBaker.cs:93`：`Bake(HObject baseImage, BakePlan plan, Action<string>? warn = null)` —— **从不抛**，失败返回 null 并回调告警。`BakePlan :604`（Shapes / Objects / Texts 三表，`AddTextBlock :625`）、`BakeText :667` + `HAlign :645`（Left/Center/Right，默认 Left）、输出前 `NormalizeForDisplay :192`。绘制顺序固定 `Shapes → Objects → Texts`（`:148-150`）；改层序改 plan 顺序，不改 baker。底图只读、所有权留给调用方（`:87-89`）。

六条 + 两条状态纪律（R37）逐条落点：

| 纪律 | 位置 | 漏了会怎样 |
|---|---|---|
| ① mode 只能 `'buffer'` | `EnsureWindow :447` | #1305 |
| ② 窗口按尺寸常驻复用 | `:440-451` | `Dispose()` 才关；反复开关 → #9302 死锁 |
| ③ 每轮无条件 `SetPart(win,0,0,h-1,w-1)` | `:141` | 底图缩到左上角；复用会串轮污染 |
| ④ 文字坐标只用 `"window"` | `:398-420` | 换算口径打架 |
| ⑤ `DispText` 显式给 `box`，字号走窗口级 `SetFont` | `DrawTexts :367-382,417-425` | 默认就是开的；`BakeText.Box` 是必填位置参数（⚠ 大写 `Box`） |
| ⑥ `DumpWindowImage` 源对象立刻 `Dispose` | `:156-160` | 句柄泄漏 |
| 状态 | `lock :95`；`SetLineWidth` 回 1 / `SetDraw` 回 `"margin"`（`:153-154`、`:331`、`:363-364`） | 整段罩"改状态→画→回读" |

调用方（5 个 BakePlan 生产点）：`Plugin.ResultDisplay\ResultDisplayPlugin.cs:80,485` · `Plugin.CaliperMeasure\AnnotationRenderer.cs:20,47` · `Plugin.BlobDetect\AnnotationRenderer.cs:22,52` · `Plugin.BeadInspect\BeadInspectPlugin.cs:841,856` · `Plugin.Matching\MatchingPlugin.cs:2106,2130`。

永久豁免：`Plugins\Plugin.ImageScript\HDevDisplayBackend.cs`。不可迁移的原因是 HDevEngine 逐条驱动 `dev_*`，"要画什么"要到脚本跑完才知道，且窗口是交给脚本的（`HandoutWindow :239`）；可变状态全 `[ThreadStatic] :38-59`。`AnnotationBaker.cs:16-18` 标注为永久例外。**豁免清单只许变短**（R36）。

守门：`ResultDisplayCheck.cs:1271-1306`（④ OpenWindow 白名单，正则 `:1283`，"清单只许变短" `:1295`；④b 豁免清单不失效 `:1302`；④c ResultDisplay 自己开 0 窗 `:1308`）+ `AnnotationBakerChecks.cs` B1（`:61-84` SetPart 像素保真）~ B8（`:401-481` 水平对齐换算）。

**R39 文字宽度只有 baker 能量**：`GetStringExtents` 必须排在 `TrySetFont` 之后；本仓 HALCON **没有** `GetTextExtents`；垂直分量留给调用方（块高 = 行数 × 行距）。

### 乙 · 屏内交互

`Services\Core.Halcon\Base\HalconBase.cs`：`HSmart_MouseDownForRoi :975`（挂钩 `:368`）。R51：换绑 `ActiveRoi` 后 `ReplayMouseDownForFreshHandle` 补发 down，只在换绑分支，失败静默退化为旧行为——**必然行为变更**：未选中对象内部拖 = 移动，必须用断言钉住。R52：`RoiHitToleranceScreenPx = 14.0` **只作用于"是否摘除已选中对象"**，挑选其它对象保持无容差，代价是一条"遮蔽带"。R54：`rectangle2` 的 length1 沿 **column** 轴（phi=0），`ellipse` 是 radius1 沿 column；记反会得"手柄命中失败"的假结论。

**R53 与纪律① 方向相反**：画布交互断言只能用**可见窗口**（buffer 窗口上只有"点中对象内部"能命中，手柄与拖拽全失效）。范式 = 屏幕外 WPF 窗口（`Left=-4000`）+ STA + `Dispatcher.PushFrame` + **窗口级**主题字典。守门 `RoiInteractionChecks` 8 条。

**R55**：改 `Services\Core.Halcon` 必须 Debug 与 Release 都重建（`pack.ps1` 打 Release）；核 DLL 里有没有新符号，不看退出码。

---

## 5. Sheet 04 · 帮助手册：随件写 → 内嵌打包 → 目录构建 → 渲染 → 档位闸

实测：磁盘 `Plugins\*\Help\*.md` = **311** 页，39 个 Help 目录，39 个 csproj 各自挂 `<EmbeddedResource Include="Help\**\*" />`（例 `Plugin.Yolo.csproj:19`）；宿主另有 4 页（`VisionMaster\Help\`，`VisionMaster.csproj:95`）。

| 环节 | 位置与判据 |
|---|---|
| 落点 | 本工程 `Help\`；一程序集多插件时各放 `Help\{插件类名}\`，目录名**逐字等于类名**（`HelpCatalogService.cs:330-339`） |
| 命名 | `NN-` 前缀定页序；首行必须 `# 一级标题`（`ExtractTitle :487-508`）；无 BOM UTF-8；正文 ≥70 行；禁 `TODO/待补充/待完善/（略）/TBD` |
| 打包 | **无全局 props 规则**，逐个 csproj 声明（R41） |
| 资源名 | MSBuild 把 `Help\A\b.md` 变成 `{Assembly}.Help.A.b.md` —— 这是 R60 必须先剥分册目录段再判页身份的根因（`GetPageFileName :408`） |
| 目录 | `HelpCatalogService.Build() :128`；`IsDeveloperPage :419` 靠 `DeveloperPageMark = "dev-" :64`；`NarrowToPlugin :431` |
| 懒构建 | `HelpViewModel.cs:646-652`（`if (!catalog.IsBuilt) catalog.Build()`）——手册不进启动关键路径（R79）；构造注入 `:92`，字段 `:45` |
| 渲染 | `VisionMaster\Services\Help\MarkdownToFlowDocument.cs:30,106`（**自研**渲染器，不是 Markdown.Avalonia）→ FlowDocument |

厚度档位（R61，`FlowCanvasChecks\HelpManualCheck.cs:43-75`；未登记按 M 档 `:54`；档位表 `:82+`）：

| 档 | 用户页 ≥ | 开发页 ≥ | 开发页行 ≥ |
|---|---|---|---|
| L | 5 | 3 | 300 |
| M | 4 | 2 | 150 |
| S | 3 | 1 | 100 |

闸：`HelpManualCheck` HM0~HM17（`:170-428`：HM4 内嵌 `:227`、HM5 首行标题 `:236`、HM6 文件名除扩展名外不许再出现点 `:247`、HM12 开发页名 `:332`、HM16 档位表一一对应 `:401`、HM17 厚度 `:428`）+ `tools\audit_artifacts.ps1`（逐插件核 DLL mtime ≥ 最新源码含 `.md` `:11-12`；内嵌 `.Help.` 资源名数 = 磁盘 `.md` 数 `:13-14,119-124`，取数 `Get-HelpMdCount :52` / `Get-EmbeddedHelpNames :67`）+ `HelpProbe`（离屏跑真链路：PluginService 扫描 → HelpCatalogService.Build → 真 HelpView/HelpViewModel，并 PNG 化对照，`HelpProbe\Program.cs:31-35`）。

**R63 排版前提只有渲染级对照守得住**：① 渲染后残留字面 `**` 即红（额度只从代码上下文扣）；② 代码块字体族必须显示得了代码上下文每个字符（判据"与无字形的 U+E000 位图逐像素相同"）；跨行 `**…**` 是支持的，别修；测字形按 **Rune** 不按 `char`；诊断带 `U+XXXX`。

**为什么这条链全是静默失败**：增量构建可以**不**重编插件工程 → DLL 里少一页；页名不合规 → 目录里少一页或混进用户页；**构建退出码全绿也一样**。所以判据一律看产物内容（R41/R42/R62）。验证配方：`tr '\0' '\n' < Modules\Plugin.X.dll | grep -a "\.Help\."` 数出的资源名 = 该工程 `Help\` 下 `.md` 数。

---

## 6. Sheet 05 · SCADA 运行态 Web 客户端（R132A 例外）

| 面 | 事实 |
|---|---|
| 形态 | 不是独立 exe、不是 Kestrel：宿主反射加载的可选 DLL（`VisionMaster\App.xaml.cs:352 ScadaWebModuleLoader.TryLoad`）；csproj 在 sln（`VisionMaster.sln:140`），产物只作 `bin\...\Web\VisionMaster.ScadaWeb.dll` + `wwwroot`（csproj `:75-90`） |
| 服务器 | Watson 7.2.0（`DLL\Watson\WatsonWebserver.dll`，`Private=false` 免类型身份重复；loader 明确拒收宿主/契约副本，csproj `:37-52`）；`ScadaWebHost.Start() :408` 进程内起监听，帧循环与在线表循环走 `Task.Run :500-501` |
| 端口 | **8080** = ScadaWeb（`AppConfigModel.cs:292 DefaultPort = 8080`；用点 `ScadaWebHost.cs:423-428`）。19000 = HttpImageServer，19100 = NetworkCameraServer —— `AppConfigModel.cs:291` 的注释就写着"刻意错开"。跑真机前确认三者未被占（R176） |
| 单一内核 | `Scada\Models\IScadaRunHub.cs:20` ← 实现就是桌面运行时那份 `VisionMaster\Services\ScadaRuntimeHost.cs:80`（注册 `App.xaml.cs:478`）。桌面与浏览器读同一运行会话/文档/同一拍 |
| N 会话 | `Web\VisionMaster.ScadaWeb\ScadaWebSession.cs:39` 包 Watson `WebSocketSession`；传输是裸 WebSocket，**没有** SignalR / SSE；推的是平值帧 `(elementId, property, values)` + 结构快照（`ScadaWebValueResolver.cs:11-25`），失步走 `view.resync(scope=structure|frames)`（`ScadaWebSession.cs:86-89`） |
| 默认值 | `Core\Models\AppConfigModel.cs`：`AllowCommands = false :378`、`RequireLogin = true :359`、`AllowAnonymousView = false :365`、`AllowMotionCommands = false :432` —— 四项全关 |
| 闸门顺序 | `ScadaWebAccessPolicy.cs:18-33,237-262`：总闸 → 匿名拒 → 只读 → 角色/策略 |
| 逐命令鉴权 | `ScadaWebCommandRouter.cs:54,907-924` 对九种 Motion 动作逐条判（含一处已注明的可绕过路径）；票据 `ScadaWebTicketService.cs:12`（WS 握手带不上 header，所以走一次性票据）；Origin/Host `ScadaWebSecurityGate.cs`；`ScadaWebRateLimiter.cs` |
| 审计 | `ScadaWebCommandRouter.Audit :1018` → `IScadaAuditSink` → `VisionMaster\Services\ScadaAuditWriter.cs:186,359`（注册 `App.xaml.cs:439`）；**缺 sink 直接关掉整条写路径**（`Router :173-178`）；记录带操作者 / 会话 / IP |

**交付边界（R132A）**：只覆盖"已设计画面的运行态查看 / 操作"，**不含**浏览器编辑组态。它是全仓唯一被裁定为交付物的 Web 产物；其余 Web 媒介仍是设计媒介（R126）。与 §3 同源：Web 侧命令最终仍走 `MotionActionExecutor` 那一个执行器，权限与物理判据不会因入口不同而多出一份。

---

## 7. Sheet 07 · 主题注入风险面：一次合并 → 五族症状

### 根：合并顺序 = 优先级（后合并者优先）

`VisionMaster\App.xaml` 的 `MergedDictionaries`（`:65-81`）：

| 序 | 行 | 内容 |
|---|---|---|
| 1 | `:66` | `/Core.Halcon;component/Generic.xaml` |
| 2 | `:67` | `/UI;component/Themes/Generic.xaml`（UI 自绘隐式控件样式） |
| 3 | `:69` | `/VM.Scada.Controls;component/Themes/ScadaIcons.xaml` |
| 4 | **`:70`** | `PresentationFramework.Fluent;component/Themes/Fluent.xaml` ← **风险源**：它带的隐式 Button / TextBox / PasswordBox / Slider / ListView 样式，从应用级顶掉上面两份与框架自带那份 |
| 5 | `:80` | 2026-10-23 新增：`/UI;component/Themes/Controls/Input.xaml` **重挂一次**，把输入域三类控件找回自绘样式 |

第 5 步的设计取舍原文在 `App.xaml:71-79`：只重挂"输入域"这一册，**不动** Button / ListView / DataGrid（仍走 Fluent），使 R148（模板里的 ToggleButton 依赖 Fluent 的 Left/Center）与 R19（GridView 让路）的既有前提不变；整册顺序翻转属换肤级改动，需单独评审。实测运行期 PasswordBox 曾拿到 Fluent 的 `Padding 10,5,10,6`。

### 五族症状 · 各自判据 · 各自守门

| 族 | 症状（用户看到的） | 判据 | 守门 | 规则 |
|---|---|---|---|---|
| 列头消失 | ListView+GridView 表头整条不见 / 显示类型全名 | 必须挂 `GridListViewStyle`；行模板必须是 `GridViewRowPresenter`；行容器内边距取 4；**不许**改成隐式样式 | `UIThemeSmokeTest\Program.cs:2271`（调用 `:588`）+ `ScadaChecks\Program.cs:21655-21663` | R19-R22 |
| 字形被切 | 关闭按钮 ✕ 显示成 ">" 或整根不见 | 写了 `Width`/`Height` 的图标字符按钮必须挂 `Style` 或显式 `Padding`（Fluent 给 Button 的隐式 Padding 是 `11,5,11,6`；实测 30×30 → 内容区 6.0×17.0，24×24 → 0.0×11.0） | `FlowCanvasChecks\GlyphButtonClipCheck.cs:55-106`（GB0~GB5，GB2 反向钉住解析器）+ `HelpProbe\Program.cs:433,596` 四档前提 | R68/R69 |
| 线段不画 | 标题栏"关闭"显示成一条斜线 ＼ | `Path` 几何不许有零长度线段（`M x y L x y` 起=终 = 那笔不画）；改 `Shell.xaml` 必须同步 `ShellCopy.xaml` | `RunShellLayoutChecks()` 末尾两条（原件 + 副本同一判据） | R70/R44 |
| 控件落位 | 模板里的 ToggleButton 跑到左边 | 自定义模板里的 `ToggleButton` 必须自声明 `HorizontalAlignment`（外壳类还要 `VerticalAlignment`）；三条豁免：显式 `Style` / 自声明对齐 / 同时写 Width+Height | `ScadaChecks\Program.cs:21292,21309` `[AI]` 静态扫描 + 属性面板探针"合并 Fluent 再渲染一次" | R148 |
| 输入域内缩 | 小高度输入框中文上下被切 | `PART_ContentHost` 不许再吃一层由 `Padding` 换算的 `Margin`；判据 `ViewportHeight ≥ ExtentHeight` + 数墨迹行数，**别看截图** | **人工评审（无自动闸）** | R184 |

另两条同源根因：

- **分册互相看不见**（R30-R32）：跨册 `BasedOn` / 模板内容 / `Freezable` 内 → 编译期全绿、运行期 `XamlParseException`；普通 `Setter.Value` → **不抛、值静默不生效**。出路只有两条：能同文件就同文件，或把共用令牌搬进 `UI\Controls\Themes\Colors.xaml`。**禁止改 `DynamicResource` 绕过，禁止新增分册 merge 兄弟分册的边。** 守门 `VariableBindingCheck.cs:753 RunDictionaryScopeContract`（断言 `:871,:877`；PowerShell 镜像已声明作废）。
- **"同文件后置键"那一格**：元素**自身属性**的 `{StaticResource K}` 先于该元素的 `Resources` 求值 —— K 即便就定义在同一元素里也查不到。修法：把属性挪到子元素。2026-10-22 实测会在无 `Application` 的断言宿主里 XAML 解析期抛、连带崩掉 5 段断言（§9 R32 附注）。
- **DataTemplate 里的后置键**（R173）：模板延迟物化，排在模板之后的键在"模板被独立实例化"的宿主里（gong 拖拽预览 Popup、`DataTemplate.LoadContent()`）直接抛 —— 真机症状是**一拖就崩**，启动冒烟查不出。

### 量 UI 缺陷的正确姿势

| 姿势 | 判据 | 反面教训 |
|---|---|---|
| 不许看内层 `TextBlock.ActualWidth` | R69：需求宽用**不受约束的参照 TextBlock**（`Measure(infinite)`），判据走算式"内容区 = 实得尺寸 − 生效 Padding − BorderThickness"；`Application.Current.Resources[typeof(Button)]` 读不到 Padding Setter，必须取按钮自己的生效值 | 本会话第一版 harness 用 ActualWidth 判，所有形状都报"完整"——**假绿** |
| 不许用 PowerShell 5.1 | R134：验 .NET 9 WPF 必须用 `net9.0-windows` 探针工程 | PS 5.1 资源字典加载 0 条目、`LoadComponent` 报找不到 `System.Runtime 9.0.0.0` → "样式没生效"是假结论 |
| 不许只看截图 | 禁用态 `Opacity=0.5` 让抗锯齿顶行变淡，看着像被切；数字墨迹矮（9~10 px）常被误判成正常 | R184 那次误判 |
| "我手动试是好的"不构成证据 | R105：顺序型刷新缺陷必须按真实操作顺序各跑一遍 | 静态闸挡不住顺序依赖 |

---

## 8. Sheet 06 · 验证闸链

### 8.1 今日十步骤台账（`tools\run_gates.ps1` 是唯一收尾入口，R182）

| # | 步骤 | 构建方式 | 今日（2026-10-23 13:30–13:34） | 备注 |
|---|---|---|---|---|
| 1 | `audit_root` | `powershell.exe -File`（`:193-194`） | OK，8 文件 / 43 目录白名单 | 白名单唯一事实源 `tools\root-layout.json`；实测 35 个顶层目录 |
| 2 | `audit_artifacts` | 同上 | `RESULT: OK` | 逐插件双判据（红线③） |
| 3 | CommChecks | `dotnet build <csproj> -m:1 -nodeReuse:false`（`:225`） | 336 / 0 | — |
| 4 | DataPipelineChecks | `:225` | 97 / 0 | 复用 3 份 check 源码、无窗口（`Program.cs:21-23`） |
| 5 | DatabaseChecks | `:225` | 134 / 0 | 含"插件 DLL 不引用 Core.Database"引用面扫描 |
| 6 | ScadaChecks | `:225` | 2637 / 0 | 63 处注册（`Program.cs:109-184`）；2714 站点 / 20 文件 |
| 7 | RecipeChecks | `:225` | 138 / 0 | `[STAThread] Main :26`，13 个 Section 横幅 |
| 8 | FlowCanvasChecks | `:225` | 3501 / 0 | 84 处限定 `Run()` + 33 处内联族；3482 站点 / 73 文件 |
| 9 | UIThemeSmokeTest | `:225`（**不在 sln**） | 121 [PASS] / 0（无汇总行） | 见 §8.4 口径修正 |
| 10 | HelpProbe | `:225`（**不在 sln**） | 74 / 0 | 离屏真链路 + PNG 对照 |

**合计 7 038 条断言、0 失败。** `UIThemeSmokeTest` 与 `HelpProbe` 不在 `VisionMaster.sln`（`run_gates.ps1:4-6` 写明；sln 只收 6 个宿主），`dotnet build VisionMaster.sln` 根本不重建它们 —— 这就是"每宿主按自己 csproj 构建再跑"存在的原因。

### 8.2 六条反假绿机制（每条都用过一次真事故）

| 机制 | 位置 | 事故/理由 |
|---|---|---|
| 陈旧 exe 判红 | `:239-250`（取 bin/obj 外最新 `*.cs/*.xaml/*.csproj` 与 exe mtime 比） | R65：**不许拿旧 exe 当"跑过了"** |
| 子进程零输出 = 判红 | `NO OUTPUT :254-257` | 2026-10-22 实测：整个 gate 报 `8 steps green` 而六个宿主**一个都没跑**（`Start-Process -ArgumentList ''` 抛的绑定异常被 `$ErrorActionPreference='Continue'` 变成 exit=0） |
| 退出码不许强类型 | `Add-Row :163-177`，`$null` → 判 1 + `exit code unknown :167-173` | `Start-Process` 去掉 `-Wait` 后 `-PassThru` 的 `ExitCode` 恒为 `$null`，`[int]$null` 印出 0 报全绿 |
| 捕获不许走管道 | 原始字节 `:126-127`；严格 UTF-8 优先、失败回落 936 `:60-61,84` | 管道按控制台代码页（本机 936）解码 .NET 宿主的 UTF-8 → 日志成了"合法 UTF-8 的乱码"，统计正则永不命中，每个宿主回落打印"全部通过"（R183①） |
| 超时与启动失败各有码 | `exit 124 :129-137`（taskkill 整棵进程树）；`exit 127 :110,141-143`；`-StepTimeoutSec` 默认 600 | 别用 `Start-Process -Wait`：它等的是进程树，一个残留后代能把 gate 挂死（实测空转 11 分钟） |
| 聚合与前置缺失 | `MISSING :190,219`、`BUILD FAILED :227`、`EXE MISSING :234`；无行即红 `:281-284`；任一红即 exit 1 `:285-288` | "没有汇总行"与"汇总说全绿"都要能被反证 |

参数：`-RepoRoot / -SkipBuild / -Only / -StepTimeoutSec`（`:36-41`），`-Only` 逗号复分（`:47`）；`-SkipBuild` 只用于"刚构建完立刻复跑"。

### 8.3 静态扫描矩阵（不跑界面也能红）

| 族 | 位置 | 判据要点 |
|---|---|---|
| 硬编码颜色 | `UIThemeSmokeTest\Program.cs:3055-3137`（调用 `:589`） | 基线 `hardcoded-colors.baseline.txt`（路径 `:3061`）记 `路径\|数量`，**新文件必须 0、老文件不许上涨** `:3113-3124`；豁免表就在这里，别按记忆数颜色文件（R140） |
| GridView 必挂样式 | `UIThemeSmokeTest:2271`（`:588`）+ `ScadaChecks:21655-21663` | R19/R20 |
| 资源断链（分册作用域） | `FlowCanvasChecks\VariableBindingCheck.cs:753` → 断言 `:871,:877` | R30-R32；PS 镜像已作废（`audit_resource_scope.ps1:13-16` 等） |
| ToggleButton 对齐 `[AI]` | `ScadaChecks\Program.cs:21292,21309` | R148 |
| 图标钮紧格子 GB0~GB5 | `FlowCanvasChecks\GlyphButtonClipCheck.cs:55-106` | GB2 反向钉解析器；GB4 连"Padding 写大了"也过账；前提由 `HelpProbe:433,596` 钉（R68/R69） |
| OpenWindow 白名单 | `FlowCanvasChecks\ResultDisplayCheck.cs:1271-1306` | 正则 `:1283`，"清单只许变短" `:1295`（R36） |

其它成族闸（选摘，均在常规 gate 内）：`ProcessTreeTemplateChecks` U1-U5（R23）、`FlowModelVersionChecks` E14 八条（R139）、`PluginCacheChecks` PC1~PC8（R81-R83）、`LayoutVisibilityCheck` LV0~LV7（R58）、`HelpManualCheck` HM0~HM17、`IoChecks`（R107/R137）、`MotionChecks`（§3 跨层一致性）、`StartupChainTimeoutIsNotFatal`（R50，位于 `FlowEngineFixChecks.cs:34,:160`）、`AnnotationBakerChecks` B1~B8 + `RoiInteractionChecks` 8 条。

### 8.4 没有闸的部分（诚实清单）

| 项 | 现状 | 出处 |
|---|---|---|
| R184 输入框模板内缩 | **人工评审，无自动闸** | `AGENTS.md` §40 |
| R141 可复用 UI 下沉 | 人工评审 | `AGENTS.md` §30.5 |
| `ShellCopy.xaml` 漂移 | R44 手工同步，只有约定在守 | §14 |
| GB4 覆盖不到样式站点 | 挂 `DialogIconButton` 的按钮走另一条算式 | 交付记录 2026-10-22 |
| 顺序型刷新缺陷 | R105 静态闸挡不住，必须按真实操作顺序各跑一遍 | §29 |
| 分册里的无闸规则 | **无法枚举**：两份分册里没有"人工评审/无自动闸"标注 | 本图审查结论 |

### 8.5 探针（唯一入口 `tools\probes\probes.json` + `run-probe.ps1`，R167）

13 个登记名：`ResProbe · StyleProbe · ToolboxProbe · LogViewProbe · FocusProbe · BeadViewProbe · ScadaEditorProbe · ScadaUiProbe · ChartProbe5（runMode exe/net48）· WebProbe5 · WebProbe6 · MotionSimProbe · CameraSimProbe`。
用法：`-List`（`:55-58`）· `-Name` 必填（`:60-62`）· `-BuildOnly`（`:81-84`）· 未登记名直接抛（`:71-74`）· 登记表与实际目录不一致也抛（`:77-79`）。本机执行策略 `Restricted` → **必须带 `-ExecutionPolicy Bypass`**（`tools\probes\README.md:25`）。搬迁三步原子：新址 `-BuildOnly` 通过 → 更新 json 与 README → 才能删旧路径；禁止"先搬后补"。

---

## 9. 复核命令（拿这些去重算，别信本文的数）

```bash
cd "D:/C#/VM"
find Plugins -maxdepth 2 -name "*.csproj" | wc -l                       # 期望 39
ls Modules/Plugin*.dll | wc -l                                          # 期望 39
grep -rhoP "GroupName\s*=\s*\"" Plugins --include=*.cs | wc -l          # 期望 53
find Plugins -path "*/Help/*.md" | wc -l                                # 期望 311
find . -name "*.csproj" -not -path "*/bin/*" -not -path "*/obj/*" | wc -l  # 全仓 102
grep -n "ResourceDictionary Source" VisionMaster/App.xaml                # Fluent 在第几行、Input 重挂在哪
for f in artifacts/logs/20261023/gate-*.log; do echo "$f"; grep -aE "^通过:" "$f"; done
pwsh -File tools/run_gates.ps1                                          # 唯一收尾入口
```

行号重定位一律用 `grep -n "<方法名或常量名>" <文件>`，不要用本文行号直接跳。

---

## 10. 待出图（审查结论 · 按踩坑频率排，不按好看排）

**优先三张：**

| 代号 | 主题 | 为什么值得 | 取证还需读 |
|---|---|---|---|
| A | **属性网格刷新链** | `RequireRefresh = 整表重建`（R104）、多态配置不许搭窗口（R106/R107）、两条枚举路读的不是同一个特性（R84-R86）、`[RuntimeState]` 名单（R139）。症状是"两套参数同现 / 改完不刷新"，现有闸只挡得住顺序无关那部分 | `FlatPropertyGrid` · `PropertyGridRefreshCheck` · `IoChecks` |
| B | **通讯读写链** | Modbus `Address` 是原始协议地址不是 `4xxxx`（R137）、数组元素个数落盘顺序（R149）、跨度上限直接拒绝不分块（R153）、xlsx 空白默认值按列语义给（R156）、`DataGrid.RowHeight` 不许写 Auto（R159） | `Communication\` · `IoChecks` · 分册-通讯与设备 |
| C | **FlowModel 版本与撤销** | Version++ 语义（谁 raise 什么算语义变更）、撤销栈边界、配方快照与运行态分离。症状是"改一下没进撤销 / 撤销把运行态抹了" | `FlowModel` · `SolutionService` · `RecipeChecks` |

**第二批**：D 启动时序（链上 4 项 + 2 项后台 + 逐项 `TimeoutMs/TimeoutLevel`，R48-R50/R79）· E 仓库分区与产物（R166-R170）· F SCADA 图元注册（R71-R77、R147、R92）· G 画布↔流程栏双视图（R23-R27、R171-R174）。

---

## 11. 口径修正（出图当场查出，本文已按实测印；AGENTS.md 未改，改不改由用户定）

| 条目 | 旧 | 2026-10-23 实测 | 影响 |
|---|---|---|---|
| AGENTS R41 注 | 44 类 / 32 程序集 | **53** 类 / **39** 工程与 DLL | 派工按"类"配额算手册量会少 9 类 |
| AGENTS R62 | sln 已登记 32/32 | **39/39**（结论仍成立） | 数字过期，规则没错 |
| AGENTS §36 已闭环注 | ROOT LAYOUT OK 8/41 | 8 文件 / **43** 目录白名单（实际 35 个顶层目录） | 白名单在长，注记没跟上 |
| AGENTS R46 | 冒烟退出码恒为 1（既有红灯未清） | 今日冒烟 **121 [PASS] / 0 [FAIL]**；退出码由 `UIThemeSmokeTest\Program.cs:595-597` 的计数求和决定，**不是硬编码** | "待还技术债"已还；旧说法会误导下次判读 |
| 代码注释 | `PluginService.cs:77`、`PluginCache.cs:14` 写"42 plugins" | 39 工程 / 39 DLL / 53 显示类 | 纯注释陈旧，不影响行为 |
| 项目记忆 | 手册 251 页（2026-10-14 时点） | **311** 页插件 .md + 4 页宿主 | 口径应按类不按页 |

---

## 12. 本图不承诺的事

1. **它是结构图，不是索引结论**。本会话无 CodeGraph 工具，所有"谁调用谁"为 `grep`/`Read` 结论（R28）。要当影响面用请重跑索引。
2. **行号已抽查，但不是全查**。本次对 47 个锚点做了直接抽样（含 `MotionActionExecutor:124`、`PluginCache:39`、`AnnotationBaker:93/141/447`、`HelpCatalogService:128/408`、`AppConfigModel:292/378/432`、`ScadaWebHost:408`、`SystemVariableModel:137/166`、`AxisPanelElement:540/849/1055`、`VariableBindingCheck:753`、`UIThemeSmokeTest:2271/589`、`run_gates.ps1` 全部引用锚点），其余为同日读码时记录，用前请用 §9 的命令重定位。
3. **它是时点快照**（2026-10-23 13:34 的树）。当天 09:33 的 `App.xaml` 与 13:13 的 `DlSegmentPlugin.cs` 是别的改动串带进来的，本文只描述、不覆盖、不评价其归属。
