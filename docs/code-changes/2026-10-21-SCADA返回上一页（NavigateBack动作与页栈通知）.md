# SCADA 返回上一页（NavigateBack 动作与页栈通知）

> 日期：2026-10-21 ｜ 范围：P0-1（SCADA 推荐控件第二批） ｜ 状态：已实现，断言通过，待真机确认

## 1. 背景

设计基线 `docs/UI设计基线/scada-controls-batch2/` 的 P0-1：运行态页栈（`ScadaRuntime._pageStack`）与 `GoBack()` 早已实现，但**没有动作出口**——画布上的按钮无法配“返回上一页”，只能硬编码目标页；同时 `CanGoBack` 是计算属性，没有变更通知，顶栏返回按钮即使绑上也永远停在初始态。

本项把“动作 → 导航出口 → 会话回退 → 页栈通知”整条链接通，并把 Web 镜像的页面类动作口径统一。

## 2. 改动清单

| 文件 | 改动 |
| --- | --- |
| `Scada\Models\ScadaActionType.cs` | 末尾追加 `NavigateBack = 18`；`DisplayName`「返回上一页」；`PendingReason` 为 `null` |
| `Scada\Models\IScadaNavigator.cs` | 新增 `bool GoBack(out string? reason)`；与 `Navigate` 同一契约：不抛异常、无会话安静返回 false |
| `Scada\Models\ScadaRuntime.cs` | 显式实现 `IScadaNavigator.GoBack`；`Stop`/`PushPage`/`PopPage` 后广播 `CanGoBack` / `PageStackDepth` |
| `Scada\Models\ScadaAction.cs` | `Detail` / `Describe` 增加 `NavigateBack` 分支 |
| `VisionMaster\Services\ScadaNavigator.cs` | `GoBack` 转发到会话；无会话时原因「运行已经结束」 |
| `VisionMaster\Services\ScadaActionDispatcher.cs` | `case NavigateBack` + `ExecuteNavigateBack`；失败记 Warn（栈空/已停/回退目标已删） |
| `VisionMaster\ViewModels\ScadaEventEditorViewModel.cs` | `ActionDisplayOrder` 中 `NavigateBack = 3`（紧随「切换画面」） |
| `Web\VisionMaster.ScadaWeb\ScadaWebActionFilter.cs` | 新增共用判据 `IsMirrorOnlyPageAction`（Navigate / NavigateBack）；过滤器用它剔除 |
| `Web\VisionMaster.ScadaWeb\ScadaWebCommandRouter.cs` | `BuildAllowedSet` 与审计 `why` 改用同一判据；NavigateBack 记为 `mirror-mode`，不再误报 `not-allowed` |
| `ScadaChecks\Program.cs` | 动作顺序表加 NavigateBack；`FakeNavigator` 加 `GoBack/GoBackCalls/FailBackReason`；新增分发器 2 条、页栈通知 3 条、出口 3 条断言 |
| `Web\ScadaWebChecks\M2ActionFilterChecks.cs` | 源钩子加 NavigateBack；新增 `M2-AF2b`（即使在 allowed 里也必须剔除）；同步条数断言 |

## 3. 关键决策

### 3.1 枚举只追加，旧版本安全降级

`NavigateBack = 18` 追加在 `ResumeFlow = 17` 之后，不回收旧值。旧版本读到 18 时：

- `DisplayName` → `未知动作(18)`；
- `PendingReason` → `本版本不认识该动作`；
- 分发器落 `default` 分支，记 Warn「本条未执行」，不会崩、不会误执行。

### 3.2 接口扩 `GoBack`，不改公开无参 `GoBack()`

`ScadaRuntime` 已有公开 `bool GoBack()`（既有断言与调用点按它写）。`IScadaNavigator.GoBack(out reason)` 用**显式接口实现**委托它，并把“栈空 / 运行结束”翻译成可直接写日志的原因；`ScadaNavigator` 再转发到会话的接口实现，避免两处各写一套原因文案。

全仓 `IScadaNavigator` 实现只有三处：`ScadaRuntime`、`ScadaNavigator`、`ScadaChecks.FakeNavigator`，均已补齐。

### 3.3 页栈通知覆盖三条变更路径

`CanGoBack` / `PageStackDepth` 是计算属性，必须在栈真正变化的地方广播：

- `Stop()` 清栈后；
- `PushPage()` 压栈（含超过 32 层丢栈底）后；
- `PopPage()` 弹栈后（`GoBack` 跳过已删除/基座页时每弹一层都会走这里）。

`Start()` 不改栈，不广播。顶栏返回按钮（P0-3）将直接绑定 `CanGoBack`，不再需要 code-behind 手动刷新。

### 3.4 Web 镜像：页面类动作统一口径

镜像模式下“切页 / 回退”都会改掉所有客户端看到的画面，属于现场动作。三处必须共用同一判据：

- `ScadaWebActionFilter.IsMirrorOnlyPageAction`：过滤器硬剔除（即使 allowed 里包含）；
- `ScadaWebCommandRouter.BuildAllowedSet`：白名单先摘掉，启动日志不显示“允许切页/回退”；
- `ScadaWebCommandRouter` 的审计 `why`：记 `mirror-mode`，而不是 `not-allowed`。

否则现场会把“镜像模式固有限制”误判成“白名单配置问题”。

### 3.5 失败语义

- 没选画面：不适用（NavigateBack 不需要目标页）；
- 栈空 / 会话已停 / 回退目标已被删或改成基座：`Skipped`（Warn），文案「返回上一页：{原因}，本条未执行」；
- 成功：`Ok`（Info），文案与面板 `Describe()` 同源。

## 4. 验证证据

| 项 | 结果 |
| --- | --- |
| `ScadaChecks` 构建 | 0 错误（1 条既有 HslCommunication 警告） |
| `ScadaChecks` 运行 | **2354 通过 / 0 失败**，含：返回上一页①/②、切页后广播 CanGoBack、GoBack 后广播、Stop 清栈后广播、出口无会话/转发/摘掉三条 GoBack 断言 |
| `ScadaWebChecks` 构建 | 0 错误 |
| `ScadaWebChecks` 运行 | **110 PASS / 0 FAIL**，含 `M2-AF2b`（NavigateBack 即使在 allowed 里也被剔除） |
| `VisionMaster.ScadaWeb` 构建 | 0 错误 |
| 全量 `VisionMaster.sln` | **未完成**：`VisionMaster.exe`（PID 9624）正在运行，锁定 `Modules\Plugin.ColorCheck.dll` 等产物，构建报 MSB3027/MSB3021 文件占用；无任何 `error CS`。关闭宿主后需补一次全量构建 |

## 5. 边界与未做

1. **Unloaded 钩子里配 NavigateBack 存在重入覆盖**：`SwitchTo` 先广播旧页 `Unloaded`、后写 `CurrentPage`；若 `Unloaded` 钩子里配了回退，内层回退会先完成，外层再覆盖回去，且旧页 `Unloaded` 响两次。这是 `Navigate` 已有的同形边界，不是本项引入；本项只在记录里写明，未加“切页中禁止重入”守卫。
2. **顶栏页面菜单未做**（P0-3）：本项只提供 `CanGoBack` 通知与动作出口；按钮/菜单 UI 在 P0-3。
3. **未做真机验证**：当前仓内没有绑定 `CanGoBack` 的按钮，UI 级“置灰/点亮”要等 P0-3。
4. **未做变异反证**：只读评审建议删通知/删 web 剔除条件验证断言变红；本项已用新增断言覆盖，但未实际执行变异。

## 6. 红线核对

- R12 大改动闭环：本记录 + 设计基线 + 断言证据。
- R65 改断言宿主后先 build 再 run：已执行（`ScadaChecks`、`ScadaWebChecks` 均先 build）。
- R38 跳过型断言：本项没有 skip 型断言。
- R55 Debug/Release 都要重建：本项未改 `Services\Core.Halcon`，不触发；但发布前仍需按常规 Debug/Release 重建。
- R16/R71 生成器：未改生成器。
- R42/R62 内嵌资源：未改插件手册。
