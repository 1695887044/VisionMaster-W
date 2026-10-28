# 变量赋值：不变量与移植清单

> 分类：变量操作 ｜ 来源：插件 Plugin.Util（工程 `Plugin.Util.csproj`，C# 类 `VariableAssignmentPlugin`）｜ 读者：开发

两组：**A 组**违反就编译红/闸门红，**B 组**违反不报错、只出诡异现象。每条一律写成
`症状 → 机理 → 守门断言名`。v3 的断言密度仍然在工具类插件里最高：
`FlowCanvasChecks\VariableAssignmentCheck.cs`（静态扫描 + 反射构造插件/设端口）、
`UIThemeSmokeTest\PluginViewSmoke.cs`（两行注入 + 全局候选）、
`FlowCanvasChecks\GlobalVariableChecks.cs`（变量管理侧）。

## 一、A 组：违反就编译红 / 闸门红

| 症状 | 机理 | 守门断言名 |
| --- | --- | --- |
| 老流程一编译就报"必填参数未配置" | 旧四端口与每行动态端口都必须显式 `IsRequired = false`；`InputPort.IsRequired` 默认 `true`，而编译期必填判据只看有没有连线来源 | `VariableAssignmentCheck` 的 `[A2]`/`[A4]` 段集体反证；动态端口由 `RebuildDynamicInputs` 统一设置 |
| 名字行挂上通用「变量绑定」弹窗，"选了全局变量却写不进去/写去别的变量"回归 | 名字类端口的语义是**名字**，绑定弹窗只会落"值由对方提供"的连线 | `【视图】名字行不是通用绑定控件（静态扫描）`、`【视图】名字行是「名字编辑器」…`、`【全局】名字行挂「选中的全局变量」连线 → 写不到 OKCount…` |
| 全局名字被改成可手填 | v3 契约 C6：全局只能选；失效项只读保留 | `【视图】全局行只能选`（`GlobalNameToggle` + `SelectGlobalCandidateCommand` + `FilteredCandidates`） |
| 运行时名字行不是可编辑绑定 | 运行时允许手填，且必须 `UpdateSourceTrigger=PropertyChanged` | `【视图】运行时名字行是可编辑绑定`（`Text="{Binding Row.VariableName, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"`） |
| 用户敲了名字却不生效（旧连线没解除） | 只提示不解除，运行期以连线为准 | `【插件】用户在这一行选了/输了名字 → 旧连线自动解除` |
| 程序灌值误解除旧连线 | 用户意图从"值变了"倒推 | `【插件】程序灌值不解除旧连线（只有用户输入才算编辑）`、`【插件】打开窗口时能认出这一行的旧连线` |
| 动态端口重复/遗漏 | 每行必须恰好一个 `Value_<RowId>`；迁移行复用 `Value` | `PluginViewSmoke` 的"每行 `Port != null`"；`RebuildDynamicInputs` 先 `Dispose` 再移除再建 |
| 方案存得下、打不开 | `AssignmentRow` 的命名空间必须是 `Plugin.Util.Models`（`$type` 白名单） | 反序列化 binder；真机打开含 `Rows` 的 `.vms` |
| 行列表不落盘 | `Rows` 必须带 `[StepConfig]` | 重新打开配置窗口应看到同样的行（真机已验证 2 行往返） |
| 并行写冲突检查漏掉 v3 行 | `FlowCompiler.CollectAssignmentWrite` 必须先读 `Rows` | 编译含 Rows 的流程；同节点多行不误报、跨节点并行写要拦 |
| 手册被自动生成页顶掉 / 同程序集串页 | `Help\{插件类名}\` 分册目录名必须逐字等于类名 | `HM3`、`HM2`、`HM4`、`HM1` |
| 开发页混进用户册 / 页序乱 / 编码坏 / 占位标记 | 手册四条打包约定（R41）+ 读者分层判据 | `HM12`、`HM12b`、`HM6`、`HM5`、`HM7`、`HM9`、`HM8`、`HM13` |
| 视图 XAML 出现硬编码颜色 / 死按钮 | R117/R123；R68 图标按钮 Padding | `UIThemeSmokeTest --plugin-views Plugin.Util` 的 `xaml hex=0` 与 `ownButtons` 检查 |

## 二、B 组：不报错，只出诡异现象

| 症状 | 机理 | 守门断言名 |
| --- | --- | --- |
| 一条「全局」行打开后静默变成「运行时」行 | 作用域 `ComboBox` 在容器生成/回收时把 `SelectedValue` 推成 `null`；`AssignmentRow.Scope` setter 若回落 `Runtime` 就会改真值 | **无专门断言**（2026-10-27 真机命中并修复）；`PluginViewSmoke` 的「全局行候选与选中项」间接要求 `Row.Scope` 仍为 `Global`（否则 `globalVm` 为空、检查红）；回归判据：全局行打开后作用域仍显示"全局"、名字列仍是全局模板 |
| 作用域已切，名字列/右栏徽标还是旧模板 | 只通知 `Row.Scope`，没通知 `IsGlobal`/`IsRuntime`/`ScopeText`/`CanCreate`/`ShowLock` | **无专门断言**（同日真机命中并修复）；`RefreshHint` 里逐个 `OnPropertyChanged`；全局行候选检查间接覆盖"模板与作用域一致" |
| 切作用域后名字被清空 | 可编辑 ComboBox 清空 `ItemsSource` 会清 `Text` 并 TwoWay 推空串 | `【插件】换作用域重算候选后名字还在`；`RefreshCandidates` 先记后顶 |
| 配置打开后每行值全没了 | 重建动态端口后没按 `ValuePortName` 回填常量 | 真机打开/确认/重开验证；`RefillRowConstants` |
| 行列表订阅翻倍 / 提示与候选打架 | `Rows` 集合变化重建 VM 时没 `Detach` 旧 VM；`Initialize` 多次调用 | `RebuildRowVms` 先 `Detach`；`_configEventsHooked` 幂等 |
| 旧配置把新行覆盖掉 | `MigrateLegacy` 没判 `Rows.Count > 0` | `MigrateLegacy` 首行守卫；重开窗口不应重复迁移 |
| 确认后留下孤儿链接/脏常量 | `OnConfirm` 顺序错：必须先 `RebuildDynamicInputs` → base 写回 → `DropOrphanLinks` → `StripNonConstantValues` → `RemoveLegacyNameLink` | 真机确认/重开；`VariableAssignmentCheck` 的链接/常量口径 |
| 旧「名字」连线在只看一眼时被解除 | 程序灌值/候选重填触发了解除 | `NotifyRowNameEditedByUser` 只由真实输入事件调用；`RemoveLegacyNameLink` 只在确认时 |
| 全局失效项被自动替换/清空 | 违反 v3 策略 C6 | 真机验证：失效项 `IsEnabled=False`、名字保留、红色提示；搜索过滤不改变名字 |
| 删除有链接的行没提示 | 删行会一并 `RemoveLink`；空行/纯常量行可直接删 | `RequestDelete` 只在 `StepData.IsLinked(ValuePortName)` 时进 `PendingDelete` |
| 行顺序不是执行顺序 | `Rows` 与 `RowVms` 顺序不一致 | `MoveRow` 用 `Rows.Move`，重建时按 `Rows` 顺序建 VM |
| 加载期集合变化触发重建风暴 | `_loading` 没罩住 `ApplyConfigValues` | `ApplyConfigValues` 的 `try/finally` |
| 复制行写同一个变量 | 复制必须清空目标名（v3 设计） | XAML tooltip「复制行（清空目标名）」；真机复制后名字为空 |
| 窄档把输入框压扁/操作按钮溢出 | 宽档单行模板在 640 下放不下 | `IsNarrow = ActualWidth < 700`；真机 640 截图两行式 |
| 全局行还显示「自动创建」勾选框 | 切全局没把 `CreateIfNotExists` 置 false | `OnRowPropertyChanged` 的 Scope 分支；真机切全局后变锁图标 |
| 删除行后右栏显示空/错行 | `SelectedRow` 没就近重选 | `RemoveRow` 按原索引夹取；真机删除后看右栏 |
| 失败原因在引擎日志里消失 | 某条失败分支没 `return`，下一行把 `ErrorMessage` 洗成空 | 无逐条断言；`RunRows` 每行独立 try/catch + 汇总；真机看 `N/M 行失败` |
| 步骤永远红、错误日志永远为空 | 自声明 `Success`/`ErrorMessage` 同名属性影子隐藏基类成员 | 无断言；靠人工核对（插件里注释是唯一说明） |
| 名字被"输入即补全"改写成另一个变量 | 可编辑 ComboBox 默认 `IsTextSearchEnabled=true` | 静态扫描只查"不是通用绑定控件"；`IsTextSearchEnabled=False` |
| 网络变量的设备侧原因被吞掉 | 写入器把 `IWritableVariable.TryWrite` 的 `writeError` 原样透出是刻意的 | `【当前值】网络变量写入失败如实报错`（变量管理侧同一口径） |
| 手动写值把"基准"冲掉 | 写当前值不动初始值（变量管理侧纪律） | `★【当前值】提交写入当前值、初始值分毫不动` |
| 并行分支写同一个全局变量，结果时对时错 | `[ParallelSafe]` 只声明本实例无共享状态，不等于同变量并发写安全 | 无断言，靠流程设计（用户页已明写） |

## 三、改这个文件必须连带的动作

| 改了什么 | 必须同步 |
| --- | --- |
| 行字段 / `RowId` 生成规则 | `.vms` 的 `Rows` 与 `Value_<RowId>` 端口名、旧方案兼容、`FlowCompiler` 冲突检查、《端口与参数》行字段表 |
| `ValuePortName` / 迁移行判定 | 旧「值」连线保留口径、`RebuildDynamicInputs`、`RefillRowConstants`、帮助页 |
| 提示文案 | 《端口与参数》第六节与《排障》原文表必须逐字一致；`VariableAssignmentCheck` 的提示断言 |
| 端口名 / `IsRequired` | `.vms` 的 `InputValues` 键、旧方案回退、编译期必填判据、帮助页 |
| 视图 XAML | `VariableAssignmentCheck` 静态扫描（名字行/全局只能选/运行时绑定/用户输入上报）、`PluginViewSmoke`、`UIThemeSmokeTest` |
| 插件源码或手册 | **重建 `Plugin.Util` 工程**（`Modules\Plugin.Util.dll` 不由宿主带出，红线③）；加完手册核 DLL 里 `.Help.` 资源数（R42） |
| `FlowCompiler.CollectAssignmentWrite` | 重建 Engine 与相关断言宿主；并行写冲突用例（同节点多行 / 跨节点并行） |
| 设计基线（v3） | `docs/UI设计基线/variable-assignment/README.md`、`design-v3.html`、WPF 视图、断言；不得直接改已确认文件，需升 v4 |

## 四、移植清单（把这个节点搬到别的项目时）

1. **行模型要有稳定身份**：`RowId` 决定动态端口名，改名不断线；删行才失效。
2. **每行一个值端口**：`InputPort<object>`、`IsRequired=false`；重建后必须回填常量。
3. **旧配置要能迁移**：旧四键 → 第 1 行；旧「值」连线保留；旧「名字」连线只提示、确认时解除。
4. **名字与值分开**：全局名字只能选（失效保留）、运行时可选可填（四档提示）；名字行不挂通用绑定弹窗；
   值行才允许链接。
5. **用户意图接真实输入事件**（键入 / 删除 / 剪切 / 粘贴 / 拖拽 / 下拉选中新增项），
   不要从"值变了"倒推；程序灌值不解除旧连线。
6. **作用域 setter 忽略空值**，并在切换时通知所有派生投影（模板/徽标/自动创建列）。
7. **`OnConfirm` 顺序固定**：重建端口 → base 写回 → 清孤儿链接 → 去非连线常量 → 解旧名字连线。
8. **执行聚合**：行顺序即执行顺序，失败行不阻断后续行，整节点汇总 `N/M 行失败：…`。
9. **手册与断言一起交**：静态扫描 + 冒烟 + 帮助页；改源码必须重建插件工程。
