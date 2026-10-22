# Excel报表：架构、宿主契约与数据流

> 分类：数据处理 ｜ 来源：插件 Plugin.ExcelExport ｜ 读者：开发

用户页讲的"怎么填"这里不重复，只讲**这套行为由谁提供、为什么这么分文件、改哪个文件要连带做什么**。

## 一、文件分工

| 文件 | 职责 | 不该出现的东西 |
| --- | --- | --- |
| `ExcelExportPlugin.cs` | 元数据、端口、11 个 `[StepConfig]` 配置属性、执行时机判定、结果回写与日志决策、预览属性 | IO、xlsx 细节 |
| `ExcelReportExporter.cs` | 一次导出任务的编排：校验 → 读 CSV → 过滤 → 写 xlsx → 注入链接 → 汇总消息 | 界面、日志、引擎上下文 |
| `XlsxHyperlinkInjector.cs` | 改写 xlsx 内部 zip 的三处 XML，把指定列变成外部超链接 | CSV 解析、业务口径 |
| `ExcelExportView.xaml(.cs)` | 四张卡片的配置界面、即时存在性校验、结果文本着色 | 业务逻辑（只在按钮事件里调 `ExportNow()`） |

这条分界的实际价值：`RunExport()` 之内**不做任何界面/日志决策**（只组 `ExcelExportRequest` 交出去、
拿回 `ExcelExportResult`），所以"流程里跑"和"面板上点"是同一段代码，行为不可能分叉。

## 二、执行时间线（`RunAlgorithm`）

| # | 代码动作 | 为什么必须在这一刻做 |
| --- | --- | --- |
| 1 | `ExportPolicy == EachRun` 或 `OnTrigger && TriggerPort.GetActualValue() is bool b && b` | 时机判定只看配置与端口，**读的这一刻**定值；`is bool` 意味着非布尔值当作"不导" |
| 2 | 不满足条件 → `return`（不写日志） | 高频流程里"每次执行都刷一条没做事的日志"会淹没真问题；代价是"静默通过"在界面上无痕迹，排障页已明说 |
| 3 | `RunExport(context)` 组请求并执行 | 请求里传的是**预览属性**（`SourcePathPreview`/`OutputPathPreview`）——即展开完的绝对路径，展开逻辑只有一份 |
| 4 | 成功：写 `ReportPath` / `ExportedRows`，`Logger.Info` 一句带路径 | 下游与日志都靠这两个口看最近一次报表 |
| 5 | 成功且 `OpenAfterExport` → `TryOpen` | `Process.Start` 包 `UseShellExecute = true`；**打开失败只 WARN，不改导出结果** |
| 6 | 失败且 `BlockOnFailure` → `Success.Value = false` + `ErrorMessage` | 默认不阻断（报表不拦产线）；勾上才让本步骤失败 |
| 7 | 失败且不阻断 → `Logger.Warn` | 现场检索词统一是 `Excel报表`，两种分支文案同源 |

`RunExport()` 内部还有一次"成功就回写两个输出端口"——那是**手动导出**（`context` 为 null）唯一的回写路径；
流程路径会被 `RunAlgorithm` 再写一次。改动时不要把它当成重复代码删掉。

## 三、手动导出的那条支路

```
ExportNow()
  ├─ if (IsExporting) return;            // 重入保护：几万行确实在跑，第二次点击直接吞掉
  ├─ IsExporting = true; LastResult = "正在导出…";
  ├─ try {                               // ← 外层 try：罩住 Task.Run 本身
  │    Task.Run(() →
  │      try   → RunExport(null)
  │              LastResult = "✔/✘ " + Message (+ "\n→ " + OutputPath)
  │      catch → LastResult = "✘ 导出异常：" + 类型名 + "：" + 说明
  │      finally IsExporting = false;
  │    )
  │  }
  └─ catch → LastResult = "✘ 导出异常：…"；IsExporting = false   // 调度失败也要落终态
```

**为什么必须两层**（2026-10-20 补的外层，别删）：内层 `catch` 只在 lambda **已经开始执行**时才有效。
线程池拒绝调度、或 lambda 还没进 try 就被拆掉时，异常会直接抛给按钮事件（UI 线程），
`LastResult` 永远停在「正在导出…」、`IsExporting` 一直是 `true` → 按钮从此永久置灰，
且下次点击被 `if (IsExporting) return` 静默吞掉，界面上看不出任何错误。
守门：`FlowEngineFixChecks` 的 `[EE4]` 五条静态断言（Task.Run 在外层 try 内 / 外层 catch 写 LastResult /
外层 catch 复位 IsExporting / 文案带类型名 / 内层 finally 仍在，`IsExporting = false` 恰好两处）。
变异反证：删掉外层 catch → ②③④ 准确变红。

两条纪律：

- **只发标量属性通知，不发集合通知**：绑定引擎会把非 UI 线程的标量属性变更调度回 UI 线程，
  集合变更不会——这里没有集合属性，改动时别加。
- `RunExport(null)`：手动导出**不写日志**（`context == null`），失败只体现在面板红字。
  这是设计，不是漏了日志。

## 四、导出器内部的五步（`ExcelReportExporter.Export`）

| 段 | 动作 | 关键细节 |
| --- | --- | --- |
| 0 等落盘 | `DataRecordFlushBridge.TryFlush(3000)`（`Core.Interfaces` 静态桥，未注册直接放行） | 账本是「CSV记录」后台线程异步追加的，不等就可能读到半截；排不空返回可读消息、**不读文件** |
| ① 校验 | 路径/输出非空、`File.Exists(源)` | 校验顺序决定"看到哪条消息"，改动会影响现场排障表的对应关系 |
| ② 读账本 | `MiniExcel.Query(path, useHeaderRow: true, excelType: ExcelType.CSV)` 收成 `List<IDictionary<string, object>>` | **`excelType` 必须显式给**，不给就按 xlsx 解析、当场抛 |
| ③ 可选过滤 | `hasLinkColumn = 列名非空 && rows[0] 含该键`；仅当过滤开关与列都存在才筛行 | 判定只看**第一条数据行**；筛完为空则失败退出 |
| ④ 写出 | `Directory.CreateDirectory` → `MiniExcel.SaveAs(overwriteFile: true, FreezeRowCount = FreezeHeader?1:0, AutoFilter)` → `XlsxHyperlinkInjector.Inject()` | 链接注入失败/为 0 不影响成功状态（降级为纯表格） |

所有异常都收敛成带中文说明的结果对象（`IOException` / `UnauthorizedAccessException` / 其它各一支），
导出器**不向上抛**——界面与日志只消费 `Message`。

## 五、超链接注入：改 zip 的三处（缺一处 Excel 报"需要修复"）

| # | 位置 | 做什么 | 为什么这么写 |
| --- | --- | --- | --- |
| 1 | `xl/worksheets/sheet1.xml` | 目标列数据格：换样式索引、`<v>` 改成 `Path.GetFileName`、末尾追加 `<hyperlinks>` 段 | 显示名带扩展名（`12_NG.png`）才分得出 png/jpg；`<hyperlinks>` 有 schema 位置约束——在 `autoFilter`/`mergeCells` 之后、`printOptions`/`pageMargins`/`pageSetup`/`headerFooter`/`rowBreaks`/`colBreaks`/`drawing` 之前 |
| 2 | `xl/worksheets/_rels/sheet1.xml.rels` | 每行追加一条 `External` 关系，`Id` 形如 `rIdHL{行号}`（首个数据行是 `rIdHL2`，因为表头占第 1 行） | `Type` 必须是**纯 URI 字符串**；写成 `XName` 会被序列化成 Clark 记法 `{http://...}hyperlink` |
| 3 | `xl/styles.xml` | 追加一支蓝色下划线字体 + 克隆"数据格样式"的 `cellXfs` 项 | CT_Font 子元素顺序固定：`u` 必须排在 `sz`/`color`/`name` 之前；克隆对象是**数据格样式**（默认 `s="2"`）而不是 `cellXfs` 末位——末位是时间格式（`numFmtId=21`），克隆它就得到"链接格显示成时间" |

另有两条同类纪律：

- **改过的 sheet 必须写回条目表**（`doc[sheetName] = …`）——漏了就退化成"关系有了、单元格没变"。
- **整包读进内存再重写**（`ReadZipEntries` / `WriteZipEntries`），写临时包 `.tmp` 后
  `File.Delete(原)` + `File.Move(临时, 原)`；任何一步失败删临时件后**继续上抛**（由导出器收成消息）。
  条目一律以 UTF-8 读写、写出时**不带 BOM**。
- **没有可用链接就早退**（`rels.Count == 0` 直接 `return 0`，不写包）——
  此时前面追加的样式改动随内存字典一起丢弃，磁盘上的包保持刚写出的样子。

## 六、白拿的宿主/基类行为（作者不用写、也别重写）

| 行为 | 由谁提供 | 本插件的表现 |
| --- | --- | --- |
| 元数据与工具箱条目 | `[Display(Name, GroupName, Description, ShortName)]` | 条目名「Excel报表」、分组「数据处理」、图标取 `ShortName` 的图标码点 |
| 端口发现与注册 | `VisionPluginBase.EnsurePortsDiscovered`（反射 public 属性） | 端口名由构造参数显式给定（`导出触发` / `报表路径` / `导出行数`），与属性名不同，存档键随之用**端口名** |
| `Success` / `ErrorMessage` | 基类声明的两个输出端口 | 本插件**不自声明同名端口**（写了会影子隐藏基类成员，插件写派生口、引擎读基类口，永远失败且日志为空） |
| 默认成功契约 | `Execute()` 进 `RunAlgorithm` 前预置 `Success=true`、清空 `ErrorMessage` | 所以"不导出"分支什么都不写也是成功；失败时才显式置 `Success=false` |
| 配置属性持久化 | `[StepConfig]` + 源生成器（`Core.PluginGenerators`）实现 partial 属性；`ApplyConfigValues` / `OnConfirm` 走 `InputValues`，键名 = 属性名 | 11 个配置属性无需手写后备字段与通知；`On<属性>Changing(ref …)` 做入参归一（null → 空串/默认模板），`On<属性>Changed` 里推预览属性通知 |
| 灌值时机 | `Initialize(stepData)` → `ApplyConfigValues`；`GetConfigView` 里先 `Initialize` 再建视图 | 面板打开时显示的是**已保存值**而不是默认值 |
| 自定义视图装载 | `IPluginCustomViewProvider.GetConfigView` | 返回 `ExcelExportView { DataContext = this }`，视图不写业务逻辑 |
| 输出端口非托管值轮首回收 | `AutoDisposeRoundOutputs`（默认 true） | 本插件输出只有 `string`/`int`，无副作用 |
| 插件 DLL 投递 | `Plugins\Directory.Build.targets` 的 `CopyPluginToModules` | DLL 自动进仓库根 `Modules\`，宿主只扫描 `Modules\Plugin*.dll` |
| 私有依赖投递 | 本工程 csproj 的 `CopyLocalLockFileAssemblies=true` + `PostBuild` 拷 `MiniExcel.dll` 到 `$(ModulesDir)` | 宿主默认 ALC 靠 `LoadFrom` 连续性从 `Modules` 解析插件私有依赖（同 `CSharpScript` 的 Roslyn 交付方式） |

## 七、存盘键（`.vms` 里 `InputValues` 的实际键）

| 键 | 来源 |
| --- | --- |
| `导出触发` | 输入端口名（`TriggerPort`） |
| `SourcePathTemplate` / `SourceStepName` / `OutputDirectoryTemplate` / `ReportFileNamePattern` / `LinkColumn` / `FreezeHeader` / `AutoFilter` / `OnlyRowsWithImage` / `ExportPolicy` / `OpenAfterExport` / `BlockOnFailure` | `[StepConfig]` 属性名 |

三条口径：

- **改了属性名或端口名 = 键名变 = 老方案该项静默失效**（落回 `[DefaultValue]`），用户只看到"配置怎么自己变了"。
- 有连线的输入端口在 `OnConfirm` 时**移除固定值键**（运行时以连线为准），不会留下脏数据。
- `ExportPolicy` 存的是枚举成员名口径（值本身是枚举，视图 `SelectedItem` 绑的就是枚举值）；
  中文标签只活在界面上（转换器写死），**不参与存盘**——所以改显示文案不会打断老方案，改成员名会。

## 八、内存与文件所有权

- 本插件实例**没有非托管资源**：不建 HALCON 窗口、不做图像句柄（R36/R37 的烘焙件与本工程无关）。
- 报表文件的所有权：`MiniExcel.SaveAs` 建包 → `XlsxHyperlinkInjector` 读进内存字典、改写、临时件替换。
  中途任何异常都由注入器删掉 `.tmp` 后上抛，导出器收成中文消息。
- `ExcelExportRequest` / `ExcelExportResult` 都是 `internal`：调用链只在本程序集内，不对外暴露契约。
