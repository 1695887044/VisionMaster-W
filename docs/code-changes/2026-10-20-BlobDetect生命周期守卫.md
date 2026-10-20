# 2026-10-20 BlobDetect 生命周期守卫（BD6）

## 1. 范围

| 项 | 内容 |
| --- | --- |
| 插件 | `Plugins\Plugin.BlobDetect\BlobDetectPlugin.cs` |
| 断言 | `FlowCanvasChecks\BlobDetectChecks.cs`（新增 `[BD6]` 段，12 条） |
| 文档 | 本文件 + `docs/UI设计基线/blob-detect/README.md` §8.1 |
| 不改 | 预览算法、端口名/类型、命令名、持久化键、`OnConfirm`/`Initialize` 语义、共享 UI/样式、其他插件 |

## 2. 背景与根因

`BlobDetectPlugin` 已有 `_disposed` 字段，`Dispose()` 也会把它置 true（2026-10-20 Web 化批次补的那一行），但两个预览入口都没有读它：

- `SchedulePreview()`：防抖 Tick / 参数 setter / 输入端口 `ValueChanged` 都会进来；
- `RefreshPreview()`：`SchedulePreview()` 在无 Dispatcher 时直接调它，后台预览完成后的"待刷新"也会再调它。

`Dispose()` 末尾调用 `base.Dispose()`，基类会对每个输入端口执行 `InputPort.Dispose()`；该实现把 `LinkedSource` 置 null，而 `LinkedSource` setter 在"有链接 → 无链接"时会触发 `ValueChanged`。此时 `OnSrcImageValueChanged` 仍然订阅着，于是释放过程中会再排一次 `SchedulePreview()`。防抖 Tick 也可能在 `Dispose()` 之后才到。没有入口守卫时，结果是在释放后新建 `DispatcherTimer` 或再启动一轮预览；已有的 `_disposed` 回填守卫只能拒绝"写 UI"，拦不住"新任务已经启动"。

## 3. 改动

| 位置 | 改动 |
| --- | --- |
| `SchedulePreview()` | 方法体首条语句 `if (_disposed) return;`（在 `_isConfigInstance` / `_suppressPreviewSchedule` 判断之前） |
| `RefreshPreview()` | 方法体首条语句 `if (_disposed) return;`（在 `_isConfigInstance` 判断之前） |
| `Dispose()` | `_disposed = true;` 之后、`base.Dispose()` 之前，退订 `SrcImage.ValueChanged` / `MaskRegion.ValueChanged` / `ExcludeRegion.ValueChanged` 上的 `OnSrcImageValueChanged`；随后保持原有"轮次号 → 清待刷新 → 清检测缓存 → 停防抖 → 清 UI 状态 → 释放渲染器 → base.Dispose"顺序 |

明确没有做（按 BD6 约束）：

- 没有加 `IsLoaded` 检查；
- 没有强制 `Dispatcher.CheckAccess`；
- 没有改预览算法、缓存策略、端口或 UI 布局。

## 4. 断言（`BlobDetectChecks.cs` 的 `[BD6]` 段）

静态扫描（4 条）：

1. `SchedulePreview` 首条语句是 `if (_disposed) return;`；
2. `RefreshPreview` 首条语句是 `if (_disposed) return;`；
3. `Dispose` 中 `_disposed = true;` 出现在 `_previewDebounce` 停止与 `DisposeDetectCache()` 之前；
4. `Dispose` 在 `base.Dispose()` 之前退订三个输入端口。

运行时探针（每个入口 4 条，共 8 条）：`OnViewLoaded()` 先产出真实预览 → `Dispose()` → 再调用入口。

- 不抛异常；
- `PreviewImage` / `StatusMessage` / `DefectRows` 保持 `Dispose` 后的快照；
- 不启动新任务：`_previewRound` 不推进、`_detectPack` 保持 null；
- 反射 `_previewDebounce == null`、`_previewInFlight == false`。

> 运行时探针的一个边界：无 `Application` 的断言宿主走同步预览分支，`_disposed` 回填守卫会让"UI 状态"保持不变，所以**单看 PreviewImage/StatusMessage/DefectRows 抓不到 RefreshPreview 入口守卫缺失**；因此额外把 `_previewRound` 与 `_detectPack` 纳入判据（入口守卫的语义正是"不启动新任务"）。变异反证已验证这两条判据能准确变红。

## 5. 变异反证

| 变异 | 结果 |
| --- | --- |
| 删掉 `SchedulePreview` 的 `if (_disposed) return;` | `[BD6] SchedulePreview 首条语句…` 红：`首条=if(!_isConfigInstance)return;`。运行时 `SchedulePreview` 探针仍绿——它同步委托给仍有守卫的 `RefreshPreview()`；这条靠静态扫描守。恢复后绿。 |
| 删掉 `RefreshPreview` 的 `if (_disposed) return;` | 静态 `[BD6] RefreshPreview 首条语句…` 红；运行时 `[BD6·运行时] Dispose 后 RefreshPreview 不启动新任务` 红：`round=2→3 pack=非null`。`PreviewImage/StatusMessage/DefectRows` 仍不变（`_disposed` 回填守卫兜住了 UI 写入），证明"状态不变"不能单独作为入口守卫的判据。恢复后 12/12 绿。 |

## 6. 验证

| 项 | 命令 | 结果 |
| --- | --- | --- |
| 插件构建 | `dotnet build Plugins\Plugin.BlobDetect\Plugin.BlobDetect.csproj -v m` | **0 错误 / 0 警告** |
| 断言宿主构建 | `dotnet build FlowCanvasChecks\FlowCanvasChecks.csproj -v m` | **0 错误**（警告均为既有依赖工程的 nullable / `MSB3245`，非本批新增） |
| 插件视图冒烟 | `dotnet run --project UIThemeSmokeTest --no-build -- --plugin-views Plugin.BlobDetect` | **11/11 PASS**（`elements=574`；3 个 Port；`InputValues=29`；XAML hex=0） |
| 常规 gate | `dotnet run --project FlowCanvasChecks --no-build` | `[BD6]` **12/12 PASS**；整轮 **2749 通过 / 0 失败**（`>>> 全部断言通过 <<<`） |
| Modules 产物 | `dir Modules\Plugin.BlobDetect.dll` | `2026-10-20 08:21:35 / 189,952 bytes` |

> 中途一次复跑曾出现 2 条 `[深度学习分类] 模型状态契约（DL1）` 失败（并行工作区当时正在改该插件视图），最终复跑已全绿；本批没有修改 `Plugin.DlClassify` 或共享 UI 文件，失败与本批无关。

## 7. 遗留

1. **入口守卫只阻止新任务，在途预览竞态仍为既有问题**：如果 `Dispose()` 发生时已经有一轮 `PreviewRunCore` 在后台跑，`DisposeDetectCache()` / `_renderer.Dispose()` 仍可能与后台线程的缓存/渲染访问重叠；`_disposed` 只在回填时拒绝写 UI，不能取消在途计算。本批按 BD6 范围没有引入取消令牌或等待任务结束。
2. `_previewInFlight` 由后台任务的 `finally` 复位，`Dispose()` 不强制置 false；无 `Application` 的同步探针路径能验到 false。若以后要求"Dispose 返回时保证无在途任务"，需要额外的任务完成/取消机制，不属于本次改动。
3. 没有加 `IsLoaded` / `Dispatcher.CheckAccess` 检查（按 BD6 明确要求），因此离屏/设计态调用入口仍走既有同步分支。

## 8. 回滚

删除两个入口的首条 `if (_disposed) return;` 与 `Dispose()` 中的三行退订，重新构建 `Plugin.BlobDetect` 与 `FlowCanvasChecks` 即可；其余行为未变。

## 9. 复跑命令

```text
dotnet build Plugins\Plugin.BlobDetect\Plugin.BlobDetect.csproj -v m
dotnet build FlowCanvasChecks\FlowCanvasChecks.csproj -v m
dotnet run --project UIThemeSmokeTest --no-build -- --plugin-views Plugin.BlobDetect
dotnet run --project FlowCanvasChecks --no-build
```
