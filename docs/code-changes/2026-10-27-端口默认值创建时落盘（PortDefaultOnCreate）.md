# 端口默认值创建时落盘（[PortDefaultOnCreate]，2026-10-27）

## 一、当前状态

- 已实现并验证：拖「延时」进流程栏，**不用打开参数窗点确定**，500ms 默认值直接进 `InputValues`，
  编译通过；运动类（轴运动 / 运动 IO / 运动使能）不标特性，仍然必须人工确认。
- 代码四处：
  - 新特性 `Shard\Core.Interfaces\PortDefaultOnCreateAttribute.cs`；
  - `Shard\Core.Interfaces\VisionPluginBase.cs`：端口发现时收集标记端口名，暴露 `AutoDefaultInputNames`；
  - `VisionMaster\Services\StepFactory.cs`：`CreateFromTool` 造完步骤立刻 `ApplyDeclaredDefaults`；
  - `Plugins\Plugin.Utility\DelayPlugin.cs`：`DelayTimePort` 标 `[PortDefaultOnCreate]`。
- 断言：`FlowCanvasChecks\StepDefaultOnCreateChecks.cs` 的 `[PD1]~[PD7]`（4 正 3 反），
  已注册进 `FlowCanvasChecks\Program.cs` 常规 gate。
- 运行结果：`3958 通过 / 0 失败`（上一轮 gate 3951 条 + 新增 7 条），见
  `artifacts\logs\20261027\pd-checks2.log`。
- 产物已重建（红线③，判据看内容不看退出码）：
  - `Modules\Plugin.Util.dll`（08:17）二进制内含 `PortDefaultOnCreate` 字样；
  - `VisionMaster\bin\Debug\net9.0-windows\VisionMaster.dll` 内含 `ApplyDeclaredDefaults`；
  - 同目录 `Core.Interfaces.dll` 内含 `PortDefaultOnCreateAttribute`。
- gate：`run_gates.ps1 -Only docs_index,audit_root,audit_artifacts,audit_docs,sync_constraints`
  五步全绿（含 `audit_artifacts` 的产物新鲜度判据）；
  其余 7 个断言宿主单独跑也全绿：CommChecks 336 / DataPipelineChecks 97 / DatabaseChecks 134 /
  ScadaChecks 3138 / RecipeChecks 138 / UIThemeSmokeTest / HelpProbe（0 失败）。

### 为什么不是改编译器判据

诊断见 `2026-10-27-延时算子默认值被判参数缺失（诊断）.md`。改判据（"看生效值而不是看 InputValues 有没有键"）
会连带放行轴运动的 Target=0 / 运动 IO 的 Value=true —— 那是真会动的默认值。特性方案把
"这个默认值可以直接跑" 变成**每个端口自己声明**的事实，编译器、画布角标、必填检查一行都不用动。

## 二、已做

1. `[PortDefaultOnCreate]`（`AttributeTargets.Property`）：只作用于 `InputPort<T>` 属性；
   `[StepConfig]` 配置属性不需要（它们不参与编译期必填检查）。
2. `VisionPluginBase.EnsurePortsDiscovered`：`TryAdd` 成功且属性带特性 → 记进 `AutoDefaultInputNames`
   （与端口名同源，属性名 ≠ 端口名时也不会错位）。
3. `StepFactory.CreateFromTool`：`CreateCore` 造模型 → `ApplyDeclaredDefaults` 反射造原型实例 →
   逐个把标记端口的当前值 `SetInputValue` 写进模型。**失败一律静默**（假类型名桩 / 构造抛异常
   只是"没有默认值可落"，拖入绝不能因此失败）；`null` 值不落盘（仍交给人确认）。
4. 延时插件标记；运动类保持原样。
5. 断言 `[PD1]~[PD7]`：
   - PD1/PD2 正向：延时落盘 500 + 直接编译通过；
   - PD3/PD4/PD5 反向：同一桩插件里标了特性的落盘、没标的不落盘，且没标的仍报 `[参数缺失]`
     （钉住"特性不是全局放开"）；
   - PD6/PD7 反向：轴运动 Target/Velocity/Mode 不落盘，编译仍报缺失（钉住"运动必须人工确认"）。
6. 文档：`docs\工程纪律\新插件端口速查.md` §2 增补用法与"什么时候不要标"。

## 三、未做

- **全量 `run_gates.ps1` 没跑成**：`FlowCanvasChecks\PreProcessingChecks.cs:213`（08:15 由另一会话改动）
  引用 `SobelFilterType.SumSqrtAbs`，而 `Plugins\Plugin.PreProcessing\Models\OperatorEnums.cs`（同 08:15）
  里只有 `SumSqrt` → `error CS0117`，该宿主编不出来。**与本轮改动无关，也没有去改别人的文件**（R9）。
  其余 7 个宿主已单独跑（见 `artifacts\logs\20261027\pd-gates-hosts.log`）。
- 其它带可用默认值的算子（数值比较 / 数学计算 / 逻辑运算 / 类型转换 / 子流程超时）**没有**标记，
  行为不变：仍需人工确认。要不要一起标是产品决定。
- 没有在真机 `VisionMaster.exe` 上点一遍（应用在构建期间被关闭，重启验证留给下一轮）。

## 四、下一步

1. 等 `PreProcessingChecks.cs` 的 `SobelFilterType.SumSqrtAbs` 在插件侧补齐（改 `OperatorEnums.cs`
   或回退断言），再跑一次完整 `tools\run_gates.ps1`。
2. 真机复验：新建流程 → 拖「延时」→ 直接编译 → 应通过且参数面板显示 500ms；
   再拖「轴运动」→ 直接编译 → 应仍报 `[参数缺失]`。
3. 若要给其它算子也开这个口，只在端口上加 `[PortDefaultOnCreate]`，无需改框架。

## 五、踩过的坑

- **`%VAR%` 在同一条 cmd 行里是解析期展开**：`set PD=... & dotnet build -p:OutputPath=%PD%\` 拿到的是空串，
  命令直接跑飞（两次"无输出"就是这么来的）。拆成两条命令或用字面路径。
- 影子构建会把 `ModulesDir` 指到临时目录，产物拷回 `FlowCanvasChecks\bin` 时会多出一个 `Modules\` 子目录；
  `PluginCacheChecks` 的 `ResolveModulesDir` 会命中它 → `PC6` 判红（"删清单后重扫仍读不出缓存"）。
  删掉那个多余目录后 `PC6` 恢复绿色 —— **这是布局假红，不是代码回归**。
- `dotnet build` 输出重定向到文件后，`findstr` 匹配中文会受 936 码页影响；判读错误请用
  `[IO.File]::ReadAllLines($p,[Text.Encoding]::UTF8)`，或只匹配 `error CS` / `error MSB` 这类 ASCII 片段。
- 运行中的 `VisionMaster.exe` 会锁 `Modules\*.dll`：**先确认应用已关闭再构建**，
  否则看到的是几十条 MSB3027/MSB3021，容易误判成"改坏了"。
