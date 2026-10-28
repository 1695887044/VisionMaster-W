# 可形变模板匹配 · 宿主契约

## 1. 基类

`DeformableMatchingPlugin : VisionPluginBase, IPluginCustomViewProvider, IAssetIdConfig`

`VisionPluginBase` 提供：

- 端口反射发现（`InputPort<T>` / `OutputPort<T>` 属性）；
- `[StepConfig]` 配置属性发现与 `InputValues` 往返；
- `Execute(IExecutionContext)`：预置 `Success=true`、清 `ErrorMessage`、轮首回收输出、异常转失败；
- `Fail(string)`：`Success=false` + `ErrorMessage`；
- `Dispose()`：释放输出端口值与输入订阅。

插件必须实现 `RunAlgorithm(IExecutionContext)`。

## 2. 端口契约

- 输入：`Image`（`HImage`）、`SearchRegion`（`HRegion`），都 `IsRequired=false`；
- 输出：`MatchCount/Score/Row/Column/Scores/Rows/Columns/DeformedContours/HomMat2D/RectifiedImages/MeasureImage`；
- 端口名与类型是下游 API，本插件 v1 不改名/不改类型；
- `RectifiedImages` 必须是 `HObject`（image array）；
- `DeformedContours` 是 `HXLD`（扁平数组）。

## 3. [StepConfig]

| 属性 | 类型 | 默认 | 说明 |
| --- | --- | --- | --- |
| `TemplateJson` | string | `"{}"` | 内嵌单模板 JSON（含模型/掩膜 base64） |
| `TemplateAssetId` | string | 空 | 方案模板资产 Id（`template` 类型） |
| `TemplateFilePath` | string | 空 | 本地模板 JSON 路径 |
| `MinScore` | double | 0.5 | 查找期 |
| `MaxMatches` | int | 1 | 查找期 |
| `MaxOverlap` | double | 0.5 | 查找期 |
| `Greediness` | double | 0.9 | 查找期 |
| `DisplayViewIndex` | int | 1 | 0=不发布 |

- `TemplateAssetId`/`TemplateFilePath` 是 `partial` 属性（源生成器给后备字段与变更钩子）；
  其余是自动属性；
- `OnConfirm`：内嵌模式序列化完整 `Entry`；外部模式只写元数据（`Model` 置空），
  模型真源留在资产/文件里；
- 运行期只走 `ApplyConfigValues`，不走 `Initialize`；
- `ApplyConfigValues` 解析 `TemplateJson` 并替换 `Entry`；
- 解析失败回落默认条目，不抛异常。

## 4. 配置视图

- `GetConfigView(IStepConfigData)`：标记配置态 → `Initialize(stepData)` → 返回 `DeformableMatchingView { DataContext = this }`；
- 视图 `Loaded` 调 `OnViewLoaded()` 载入参考图预览；
- 视图不持有业务状态，只绑定插件属性/命令转发；
- 数值用 `ui:NumericBox`；参考图路径用 `DialogInput`；
- 结果表用 `ListView+GridView` + `GridListViewStyle` + `GridViewRowPresenter`；
- 颜色只用 `Dialog*` / `PluginPreview*` 令牌。

## 5. 配置态 vs 运行态

| 能力 | 配置态 | 运行态 |
| --- | --- | --- |
| 读参考图 | 是 | 否 |
| 生成预览 | 是 | 否 |
| 学习 | 是 | 否（但可读模型） |
| 查找 | 否 | 是 |
| 更新结果表 | 否 | 是（有 Application 时派发 UI 线程） |

`_isConfigInstance` 由 `GetConfigView` 置位；`OnViewLoaded`/`RefreshPreview` 只在配置态执行。

## 6. IExecutionContext

- `Logger`：`Info/Warn/Error/Success`；
- `CancellationToken`：基类把取消当 Skipped，不当失败；
- `Assets`：方案内容资产只读解析入口；本插件用 `TryResolve(Guid)` 取模板资产路径/哈希/版本；
- `Assets` 返回的 `ProjectAssetRef.Kind` 必须是 `template`；文件资产读 `AbsolutePath`；
- 资产已归档/缺失/越出方案目录时解析器直接失败，插件原样上报；
- 本地模板文件不经过 `Assets`，用 `TemplateFilePath` + `AppContext.BaseDirectory` 解析；
- 其它能力面（全局变量/相机/运动）本插件不用；
- 插件不直接访问宿主服务。

## 7. 发布预览

```csharp
if (DisplayViewIndex > 0 && MeasureImage.Value is HImage anno)
    this.PublishPreview(anno, DisplayViewIndex);
```

- `viewIndex` 就是窗口号 1~9，直传，不 +1；
- 无订阅者时是 no-op；
- 图像所有权在发布方，下一轮会被释放。

## 8. 并行

- v1 不加 `[ParallelSafe]`；
- 加之前必须确认：
  1. 模型缓存是实例级，不是 static；
  2. 运行期不调 `SetDeformableModelOrigin`；
  3. 插件实例不被并行分支共享；
  4. `RuntimeModelId`/`RuntimeModelContours` 访问有同步。
- `set_deformable_model_origin` 文档要求跨线程访问 ModelID 同步；
  本插件只在学习期调用，运行期只读。

## 9. 失败契约

- 业务失败：`Fail("中文原因")`，`Success=false`，`ErrorMessage` 有值；
- 异常：基类 `Execute` 捕获并 `Fail($"{类型}: {消息}")`，`Logger.Error(ex)`；
- 取消：`OperationCanceledException` + 令牌已取消 → 原样上抛，不写失败；
- 无命中：`Fail` + `MatchCount=0` + 标量 0 + 数组空 + `MeasureImage` 原图。

## 10. 资源所有权

- 输入 `Image`/`SearchRegion` 归上游，插件不 Dispose；
- 输出 `HImage`/`HObject`/`HXLD` 归端口，基类轮首回收；
- 局部临时 `HObject` 由插件 `Dispose`；
- 模型句柄 `ClearDeformableModel` 由 `ReleaseEntryModel`/`Dispose` 负责；
- `RuntimeModelContours` 随模型句柄释放；
- `PreviewImage` setter 释放旧图。

## 11. 帮助与产物

- 帮助放 `Help\`，csproj `<EmbeddedResource Include="Help\**\*" />`；
- 文件首行 `# 一级标题`，每页 ≥70 行；
- L 档：用户页 ≥5、开发页 ≥3、最长开发页 ≥300 行；
- `HelpManualCheck` 登记 `["DeformableMatchingPlugin"] = ManualTier.L`；
- `audit_artifacts.ps1` 核 DLL 内嵌资源数；
- 改帮助必须重建插件。

## 12. 测试契约

- `FlowCanvasChecks` 直接引用插件工程（不走 `Modules\` 反射）；
- `DeformableMatchingChecks` 覆盖端口、学习、查找、缓存、失败、序列化、对拍、反向、样例流程、
  方案资产/本地文件（类型/缺失/归档/坏 JSON/算法不符/优先级/升版重载）；
- `MakeContext` 用 `VisionMaster.Services.ExecutionContext`；
- `FakeStepData` 实现 `IStepConfigData`；
- 断言宿主无 `Application` 时，UI 更新直接执行。

## 13. 禁止事项

- 不把契约程序集拷进 `Modules\`（红线②）；
- 不在插件里引用 `Plugin.Matching` 程序集；
- 不直接 `OpenWindow`（R36/R37；标注只用图像算子）；
- 不在视图里硬编码颜色（R140）；
- 不改端口名/类型来“适配”视图；
- 不用 `Application.Current.Dispatcher.Invoke` 同步等待，避免死锁。
