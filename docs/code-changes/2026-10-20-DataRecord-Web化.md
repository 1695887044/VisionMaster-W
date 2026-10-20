# 2026-10-20 Plugin.DataRecord 自定义视图 Web 化

## 1. 范围

- 设计基线：`docs/UI设计基线/data-record/README.md` v1 + `design.html`（自包含、可点击）。
- 覆盖：`Plugins\Plugin.DataRecord\DataRecordView.xaml` + `DataRecordView.xaml.cs`。
- 不在范围：`DataRecordPlugin.cs` 业务逻辑、`RecordColumnDef.cs`、`RecordingHub.cs`、帮助手册、公共令牌字典、宿主外壳、其它插件、`UIThemeSmokeTest` 源码。
- 参考实现：`docs/UI设计基线/image-acquisition/` V2；差异见 §4。

## 2. 耦合矩阵（SOP §5.1）

| 区域 / 元素 | 当前绑定 / 契约 | 功能依赖 | 可重构级别 |
| --- | --- | --- | --- |
| 根 `UserControl` | `DataContext = DataRecordPlugin`（`GetConfigView` 里设置）；`MinWidth=560`（适配宿主 640×560 最小档）/ `MinHeight` 取 `PluginViewMinHeight` | 宿主 `viewObj.DataContext as IPluginConfigView` 取 `OnConfirm` | 只改外观，构造/生命周期不动 |
| 账本文件 | `DirectoryTemplate` / `FileNamePattern` / `KeepDays` / `WriteHeader` / `FlushEveryRow` / `BlockOnFailure` | 全部 `[StepConfig]`，随 `.vms` 持久化 | 只能包裹/改外观，双向绑定不动 |
| 列定义 | `Columns`（`ObservableCollection<RecordColumnDef>`）；行内 `Enabled/Name/Source/VariableName/BuiltIn/Format` | 列名/来源/启用变化触发 `RebuildDynamicInputs`；动态端口是流程编译的输入 | 行模板可重排；字段绑定不能换 |
| 列端口编辑器 | `LinkableValueEditor` 的 `Port` = `InputPortConverter(Name, plugin, PortsVersion)`；`StepData` 来自插件 | 端口持久化、类型转换、`SetLink/RemoveLink`、变量绑定弹窗 | 只能包裹；`Port/StepData` 原样 |
| 列来源下拉 | `ItemsSource=ColumnSources`；`SelectedItem=Source` | `ColumnSource` 枚举，驱动三态取值单元格 | 只改外观 |
| 内置字段下拉 | `ItemsSource=BuiltInFields`；`SelectedItem=BuiltIn` | `BuiltInField` 枚举 | 只改外观 |
| 图片记录方式 | `ImageSaveMode`（枚举）；原 `ComboBox SelectedItem` | `RebuildDynamicInputs` 决定「图片」端口是否存在 | 可换控件形态，必须写回同一枚举 |
| 图片源 | `LinkableValueEditor` 的 `Port` = `InputPortConverter(ImagePortNameValue, plugin, PortsVersion)` | 非「不存图」时才有「图片」动态端口 | 只能包裹；按需创建 |
| 图片字段 | `ResultColumn` / `NgValue` / `ImageNameTemplate` / `ImageFormat` / `ImageDirName` | 全部 `[StepConfig]`；NG 判定与命名模板 | 双向绑定不动 |
| 添加列 | `AddColumn()` 方法 | 自动起不重名列名并加入 `Columns` | 可改入口形态（命令转发） |
| 删除列 | `Columns.Remove(col)` | 集合变更触发端口重建 | 可改入口形态（命令转发） |
| 测试写一行 | `TestWriteOneRow()` → `TestResult` | 真实写一行；返回 ✔/✘ 文案 | 可改入口形态与视觉，不改方法 |
| 状态 | `TestResult` 文本 | ✔ = 成功、✘ = 失败、空 = 未试写 | 只做视觉三态；不扩展 `StatusLevel` |
| HALCON | 本插件无 HALCON 控件 | — | 不适用 |

**判定**：`LinkableValueEditor`、`Columns`/`PortsVersion`、`ImageSaveMode`、`TestResult` 是功能入口，只做包裹/外观；`DataRecordPlugin` 未改一行。

## 3. 改动点

1. **整页重写为两栏**：左 300~356px 配置轨（账本文件 + 图片记录），右侧列定义工作区（列定义表 + 试写卡）；配置与表格分离，列多时只滚表格。
2. **列定义表重做**：统一 6 列（启用 / 列名 / 来源 / 取值 / 格式 / 删除）；表头固定、行区独立滚动；窄档保留 430px 最小表宽并横向滚动。
3. **取值三态按需创建**：
   - 端口行用 `ContentControl` + `DataTemplate` 创建 `LinkableValueEditor`，只有 `Source=Port && Enabled && Name != ""` 才实例化（DR1：空列名不创建 `Port=null` 编辑器）；
   - 变量行用 `TextBox`（`VariableName`）；
   - 内置行用 `DialogCombo`（`BuiltIn`）。
   这样非端口行不再产生 `Port=null` 的编辑器（批量冒烟要求每个 `LinkableValueEditor.Port != null`）。
4. **图片记录方式改为 3 张卡片**：`RadioButton` + `ui:EnumConverter` 写回 `ImageSaveMode`；非「不存图」时用 `ContentControl` + `DataTemplate` 创建图片端口编辑器，切回「不存图」立即销毁。
5. **试写结果状态化**：`TestResultStateConverter` / `TestResultDisplayConverter` / `TestResultIconConverter` 三态（未试写 / 成功 / 失败），只消费既有 ✔/✘ 前缀；颜色走 `DialogSuccess*` / `DialogDanger*`。
6. **动作入口命令化**：视图新增 `AddColumnCommand` / `DeleteColumnCommand` / `TestWriteCommand`，转发到既有 `AddColumn()` / `Columns.Remove()` / `TestWriteOneRow()`；不新增业务逻辑。
7. **全令牌化**：视图 XAML 零十六进制（批量冒烟 `hex=0`）；code-behind 删除 `Color.FromRgb(0x2E,0x7D,0x32)` / `Brushes.Red`，改由 XAML 样式引用公共令牌。
8. **可访问性**：表单控件 38px（`DialogInput` / `DialogCombo` 默认）、表格控件 32px；图标按钮 30×30 + `Padding=0`（R68）；焦点 `DialogFocusVisual`；ToolTip `DialogToolTipStyle`；数字列 `Typography.NumeralAlignment="Tabular"`。视图 `MinWidth` 由公共 620 调整为 **560**（与 ImageAcquisition V2 一致，适配宿主 640×560 最小档；两栏最小 300+16+240=556）；`MinHeight` 仍取公共 `PluginViewMinHeight`（360）。
9. **code-behind**：保留四个原有转换器（`InputPortConverter` / `CnEnumLabelConverter` / `SourceToVisibilityConverter` / `NotOffToVisibilityConverter`），新增 `ZeroToVisibilityConverter` 与三个 `TestResult*` 转换器、三个命令；不引入独立 VM。
10. **设计基线**：新增 `docs/UI设计基线/data-record/design.html`（v1，自包含、可点击：960/640、图片记录三档、试写三态、空列边界、增删列、来源三态）与 `README.md`（版本/范围/设计要点/令牌映射/契约保留/已知偏差/未纳入项）。

## 4. 与 ImageAcquisition V2 的差异

| 维度 | ImageAcquisition V2 | DataRecord v1 |
| --- | --- | --- |
| 右栏 | 预览工作区（`ImageReadOnly` 按需创建 + 空/错误态） | 列定义工作区（表格 + 试写卡），无 HALCON |
| 左栏模式 | 2×2 采集模式卡片 | 3 张图片记录方式卡片（不存图 / 仅NG / 每件） |
| 按需创建的对象 | `ImageReadOnly`（避免折叠后不重绘） | `LinkableValueEditor`（避免非端口行空 `Port`） |
| 状态 | `StatusMessage` / `StatusLevel` 三档 | 只有 `TestResult` 文本；视觉三态由前缀推导，不扩展状态枚举 |
| 表格 | 无 | 6 列行模板，端口/变量/内置三态；窄档横向滚动 |
| 表单控件 | 36px | 表单 38px（`DialogInput` 默认）/ 表格 32px |
| 图片记录方式 | 无 | 原 `ComboBox SelectedItem=ImageSaveMode` 改为 3 个 `RadioButton` + `EnumConverter`，写回同一枚举；`ImageSaveModes` 属性保留但不再被视图消费 |

## 5. 验证

| 项 | 命令 | 结果 |
| --- | --- | --- |
| 插件构建 | `dotnet build Plugins\Plugin.DataRecord\Plugin.DataRecord.csproj -v m` | **0 错误 / 0 警告**；`Modules\Plugin.DataRecord.dll` 已刷新（112,128 字节） |
| 批量视图冒烟 | `dotnet run --project UIThemeSmokeTest --no-build -- --plugin-views Plugin.DataRecord` | **11/11 通过**（见下） |
| 设计基线渲染 | 本地 HTTP 打开 `data-record/design.html`，浏览器截图 + 脚本点击验证 | 通过；960/640、图片记录展开态、试写三态、空列边界、增删列、来源切换均可交互 |
| 交互探针（临时，非仓内） | `%TEMP%\DataRecordProbe`（WPF STA，真实实例化视图） | **13/13 通过**（见下） |
| 渲染证据 | `docs/UI设计基线/_batch-evidence/Plugin.DataRecord-DataRecordPlugin.png` + `docs/UI设计基线/data-record/evidence/after-render-v1-initial.png` | 已生成（视图初始态 920×520） |
| `FlowCanvasChecks` | 未跑 | 本轮指令只要求插件构建 + 批量视图冒烟；未改公共契约。建议 reviewer/批次收尾补跑 |

## 5.1 C 组遗留修复（2026-10-20）

| ID | 修复 | 说明 |
| --- | --- | --- |
| DR1 | 列行端口 `ContentControl` 的 `Style.Triggers` 增加 `DataTrigger Binding="{Binding Name}" Value=""` → `Visibility=Collapsed`、`ContentTemplate=null`，并放在 `MultiDataTrigger` 之后以覆盖 `Visible` | 空列名时 `GetInputPort("")` 返回 null，原实现会创建 `Port=null` 的 `LinkableValueEditor`；现在直接不实例化。运行时探针实测：默认 `LinkableValueEditor=1` → 列名清空 `0` → 恢复列名 `1`。 |

**验证**：插件构建 **0 错误 / 0 警告**；`--plugin-views Plugin.DataRecord` **11/11 PASS**；临时 STA 探针的 DR1 两条均 PASS。DR2（动态状态探针入仓）/DR3（只有 1 张 PNG）属 TEST，未做。

批量冒烟 11 条：

1. 找到待检插件 DLL（count=1，filter=Plugin.DataRecord）
2. `GetConfigView` 返回 `FrameworkElement`（`DataRecordView`）
3. 视图实例化（elements=410）
4. `DataContext` / `IPluginConfigView`（`DataRecordPlugin`）
5. `LinkableValueEditor.Port`（count=1，ports=结果）
6. `Button.Command` 绑定（buttons=5，commands=`AddColumnCommand` / `DeleteColumnCommand` / `TestWriteCommand`）
7. `ComboBox` 绑定（`ColumnSources/Source`、`BuiltInFields/BuiltIn`）
8. `TextBox.Text` 绑定（`DirectoryTemplate`、`FileNamePattern`、`KeepDays`、`ResultColumn`、`NgValue`、`ImageNameTemplate`、`ImageDirName`、`Name`、`VariableName`、`Format`、`PortValue`、`LinkAddress`）
9. `OnConfirm` 可执行（InputValues=14）
10. XAML `hex=0`（xaml=1，hex=0）
11. 渲染 PNG 已生成

交互探针 13 条（真实 `Application` + 主题资源 + 920×520 宿主，未写入任何 CSV）：

1. 初始端口编辑器：count=1，ports=结果
2. 找到「仅NG存图」卡片
3. 卡片写回 `ImageSaveMode=OnlyNG`
4. 非「不存图」时创建图片端口编辑器：count=2，ports=图片,结果
5. 切回「不存图」销毁图片编辑器：mode=Off，count=1
6. 存在默认端口列（结果）
7. 变量来源不创建端口编辑器：count=0
8. 切回端口来源恢复编辑器：count=1，port=结果
9. `AddColumnCommand` 添加列：before=3，after=4
10. 新增端口列创建编辑器：count=2，ports=结果,列
11. `DeleteColumnCommand` 删除列：count=3
12. 删除列后编辑器回收：count=1
13. 三个命令均已创建

> 探针位于 `%TEMP%\DataRecordProbe`，不属于仓库；用于补齐批量冒烟覆盖不到的“图片记录切换 / 来源三态 / 增删列”回环。`UIThemeSmokeTest` 源码/工程**未修改**（硬约束），因此仓库内仍没有 `--data-record` 单插件模式。

## 6. 功能契约保留（逐项）

| # | 契约 | 状态 |
| --- | --- | --- |
| C1 | `DataContext = DataRecordPlugin` + `IPluginConfigView` | 保留；`GetConfigView` 未改 |
| C2 | `LinkableValueEditor` 的 `Port` / `StepData` | 保留；列行按 `Name + plugin + PortsVersion` 取端口，图片行按 `ImagePortNameValue + plugin + PortsVersion` 取端口 |
| C3 | 全部 `[StepConfig]` 双向绑定 | 保留（目录/文件名/保留天数/三个开关/图片方式/结果列/NG值/命名模板/格式/图片子目录） |
| C4 | `Columns` + `PortsVersion` | 保留；列名/来源/启用变化仍触发 `RebuildDynamicInputs` |
| C5 | 命令入口 | `AddColumn()` / `Columns.Remove()` / `TestWriteOneRow()` 原样调用；仅入口形态改为视图命令 |
| C6 | `TestResult` | 保留；只做视觉三态，不改 `TestWriteOneRow()` 文案与写入行为 |
| C7 | 端口名 / 持久化 | `ImagePortName = "图片"`、动态端口名=列名、`[StepConfig]` 名称均未改 |

## 7. Reviewer 重点

1. **图片记录切换回环**：`不存图 → 仅NG存图 → 每件存图 → 不存图` 时，`ImageSaveMode` 写回、`PortsVersion` 递增、「图片」端口增删、图片编辑器创建/销毁是否正确；切回「不存图」后 `LinkableValueEditor` 不应残留空 `Port`。
2. **列来源三态回环**：`端口 → 变量 → 内置 → 端口` 时，`Source` 写回、动态端口重建、端口编辑器创建/销毁、变量名/内置字段输入是否保留。
3. **列名修改**：改 `Name` 后 `PortsVersion` 重取新端口；旧端口是否从 `Inputs` 移除、`StepData` 链接是否按既有语义保留/恢复。
4. **启用/停用列**：停用后端口被移除，端口编辑器应折叠；重新启用后端口重建、编辑器恢复。
5. **增删列**：`AddColumnCommand` 新列 `列N` 是否自动获得动态端口；`DeleteColumnCommand` 删除后端口与行同步消失；`Columns.Count` 的「N 列」是否更新。
6. **试写结果**：`TestWriteOneRow()` 的 ✔/✘ 文案是否驱动三态横幅颜色/图标；空串是否显示引导语；不出现第四档状态。
7. **`ImageSaveModes` 属性**：视图不再消费，确认无其它代码依赖其绑定（仅 API 保留）。
8. **共享件未改**：`Colors.xaml`、`PluginConfigColors.xaml`、`LinkableValueEditor`、`HalconBase`、`PluginConfigShellView.xaml`、`UIThemeSmokeTest` 均未动。

## 8. 遗留问题

1. **设计基线待用户确认**：`data-record/README.md` v1 的「确认日期/人」仍为待确认；确认后按 R130 冻结，改动升 v2。
2. **无单插件冒烟模式**：受硬约束未改 `UIThemeSmokeTest`，`--plugin-views` 只覆盖初始态；建议批次收尾时由主会话统一补 `--data-record` 模式（含图片记录/来源切换/增删列/试写回环）。
3. **`FlowCanvasChecks` 未跑**：本轮未改公共契约；建议与批次 gate 一起跑。
4. **窄档 640**：右栏列定义表保留 430px 最小表宽，出现横向滚动；左栏可滚动。是否接受由真机验收决定。
5. **`ImageSaveModes` 属性**：视图改为 3 个 `RadioButton` 后该属性不再被消费；保留以兼容既有 API，后续可评估是否删除（本次不动业务类）。
6. **`LinkableValueEditor` 的共享控件行为**：端口对象重建/编辑器销毁时，控件内部对 `Port.ValueChanged` 的订阅没有显式退订；当前端口对象随重建一起失去根，未观察到泄漏，但属于共享控件既有边界，不在本插件视图内修。
7. **真机回环**：需要在 VisionMaster 里打开配置 → 改字段 → 确认 → 重开，验证 `.vms` 持久化、动态端口、变量链接、试写写盘；本记录只完成构建/批量冒烟/设计稿验证。

## 9. 复跑命令

```text
dotnet build Plugins\Plugin.DataRecord\Plugin.DataRecord.csproj -v m
dotnet run --project UIThemeSmokeTest --no-build -- --plugin-views Plugin.DataRecord
```

> 注意：`UIThemeSmokeTest` 退出码恒为 1（既有 GridView 红灯，R46）；以日志 `[PASS]` 行与“全部通过”判定。
