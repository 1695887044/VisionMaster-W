# Excel 报表插件（Plugin.ExcelExport）

> **这是本插件的唯一文档。** 原先分散的 Excel 报表开发记录与样式/构建记录已全部并入本文件。
>
> 本文件分两层读法：
> - **零基础 / 操作者** → 读第 0 ~ 3 章 + 3.4~3.6 节（原理直觉、界面说明、逐步操作、参数速查、FAQ、排障）
> - **开发者 / 维护者** → 重点读第 4 章（异常与修复记录）与第 5 章（附录：搭配算子、实战案例、源码索引、回归断言）

---

## 目录

- [第 0 章 这是什么](#第-0-章-这是什么)
- [第 1 章 设计思想](#第-1-章-设计思想)
- [第 2 章 技术特点](#第-2-章-技术特点)
- [第 3 章 使用方式](#第-3-章-使用方式)
- [第 4 章 异常与修复记录](#第-4-章-异常与修复记录)
- [第 5 章 附录](#第-5-章-附录)

---

# 第 0 章 这是什么

**Excel 报表 = 把机器看的 CSV 账本，加工成人看的带图 Excel 报表。**

> 生活比喻：CSV 账本是流水账（密密麻麻的路径和数值，机器读得懂、人看着累）。Excel 报表是给它做的"**可读版**"——表头冻结、能筛选排序，最关键的是：**图片那一列不再是一长串路径，而是蓝色下划线的短文件名，点一下就用系统看图软件打开磁盘上的高清原图**。

它在流程里的位置（归在 **「数据处理」** 分组）：

```
[CSV记录] 账本（D:\Records\…\2026-09-16.csv）
        ↓ 读
[Excel报表] ──▶ D:\Records\Excel报表_0\2026-09\2026-09-16_报表.xlsx
```

**一句话**：数据记录负责"记"，**Excel 报表负责"给人看"**——它是事后导出，不是边测边写。

## 0.1 为什么是"事后导出"而不是"边测边写 xlsx"

> - 产线上写 xlsx 要**整份重写**、还得防 Excel 占用，代价高且没意义（**机器不看报表**）；
> - 人要看的时候再导出，报表就是"当时那份账"的**快照**——文件小、能筛选排序、点一下看高清原图。

## 0.2 报表长什么样

```
第 1 行 = 表头（冻结 + 自动筛选）
第 2 行起 = 每件一行的数据
    其中「图片路径」列 → 蓝色下划线的短文件名（如 0001_OK.png）
    点击 → 用系统看图软件打开磁盘上的原图
```

> ⚠️ **报表里没有图片二进制**，只有指向磁盘原图的 `file:///` 链接。原图被删/挪走，链接就断。

---

# 第 1 章 设计思想

## 1.1 核心原理：三段式流水线

```
CSV 账本（机器看）
   ↓ MiniExcel 读（必须显式 excelType: CSV）
纯数据 xlsx（MiniExcel 写数据 + 冻结首行 + 自动筛选）
   ↓ 自写 zip 注入 OOXML 外部超链接
带图链接的报表（人看）✅
```

> 需要改 zip 里三处（**缺一处 Excel 就报"需要修复"**）：
> 1. `xl/worksheets/sheet1.xml` —— 目标列单元格挂链接样式、值改成短显示名，末尾追加 `<hyperlinks>` 段
> 2. `xl/worksheets/_rels/sheet1.xml.rels` —— 追加 External 关系（Id → `file:///` 目标）
> 3. `xl/styles.xml` —— 追加"蓝色下划线"字体 + 对应的 cellXfs 样式项

## 1.2 为什么是"改写 zip"而不是"换个库"

> MiniExcel 写数据 / 冻结首行 / 自动筛选都很利落，**唯独链接能力缺失**；而 xlsx 本质就是一个 zip + 若干 XML，缺的那点东西自己补最省事——**不必为了超链接引入一整个 OpenXML SDK**。

## 1.3 失败契约：报表不阻断生产

> 报表是给"人"看的附属产物，默认**不**阻断生产（`BlockOnFailure=false`），导出失败只写 WARN 日志。需要"报表导出不了就停线"的场合可勾选阻断。

## 1.4 不导出时静默通过

`RunAlgorithm` 里若本次不该导出，**直接 return，不写日志**——注释原文："高频流程里刷日志会淹没真正的问题"。

---

# 第 2 章 技术特点

## 2.1 元数据

| 特性 | 值 |
|---|---|
| `Name` | Excel报表 |
| `GroupName` | 数据处理 |
| `Description` | 把 CSV 账本导出成带图片超链接的 Excel 报表：表头冻结可筛选，点文件名看高清原图 |
| `ShortName` | Excel 图标（FontAwesome `f1c3`） |

实现 `IPluginCustomViewProvider`（自带配置视图）。

## 2.2 端口完整清单（**全部固定端口，无动态端口**）

| 端口名 | 属性名 | 方向 | 类型 | 必填 | 说明 |
|---|---|---|---|---|---|
| `导出触发` | `TriggerPort` | 入 | bool | ✖（`IsRequired=false`） | 为 true 时导出；**仅"触发端口"方式生效** |
| `报表路径` | `ReportPath` | 出 | string | — | 本次导出的 xlsx 完整路径（失败时为空串） |
| `导出行数` | `ExportedRows` | 出 | int | — | 本次写入报表的数据行数（不含表头） |
| `Success` | 基类 | 出 | bool | — | 基类契约（默认预置 true） |
| `ErrorMessage` | 基类 | 出 | string | — | 失败原因 |

> 本插件是**读文件**而非接端口数据流，所以没有动态端口（这点与 DataRecord 不同）。

## 2.3 配置项（11 个）

| # | 属性 | 类型 | 默认 | 说明 |
|---|---|---|---|---|
| 1 | `SourcePathTemplate` | string | `D:\Records\{源步骤}\{yyyy-MM}\{yyyy-MM-dd}.csv` | 源账本路径模板 |
| 2 | `SourceStepName` | string | `""` | 源账本所属的步骤名（供 `{源步骤}` 展开） |
| 3 | `OutputDirectoryTemplate` | string | `D:\Records\{StepName}\{yyyy-MM}\报表\` | 报表输出目录模板 |
| 4 | `ReportFileNamePattern` | string | `{yyyy-MM-dd}_报表.xlsx` | 文件名模板（含 .xlsx；不加会自动补） |
| 5 | `LinkColumn` | string | `图片路径` | 要做成超链接的列名 |
| 6 | `FreezeHeader` | bool | `true` | 冻结首行（滚动时表头不跑） |
| 7 | `AutoFilter` | bool | `true` | 首行自动筛选（可按结果/日期筛） |
| 8 | `OnlyRowsWithImage` | bool | `false` | 只导出图片列有有效路径的行（抽检看 NG 图时用） |
| 9 | `ExportPolicy` | 枚举 | `Manual` | 导出方式：仅手动 / 触发端口 / 每次执行 |
| 10 | `OpenAfterExport` | bool | `false` | 导出成功后用系统默认程序打开报表 |
| 11 | `BlockOnFailure` | bool | `false` | 导出失败是否阻断流程（默认否） |

### 占位符（关键，别搞混）

| 占位符 | 含义 |
|---|---|
| **`{源步骤}`** | **上游 CSV 记录步骤的名字** |
| **`{StepName}`** | **本 Excel 报表步骤自己的名字** |
| `{yyyy}` `{yyyy-MM}` `{yyyy-MM-dd}` `{MM}` `{dd}` | 日期 |

> 默认目录模板与上游 `Plugin.DataRecord` 严格对齐，所以**开箱就能命中账本**。报表输出默认单独放 `报表\` 子目录，避免污染账本目录。

### 导出方式三种

| 方式 | 何时导出 | 适用 |
|---|---|---|
| **仅手动**（默认） | 流程执行时不导出，只在配置面板点"立即导出" | 最常见：收工后手动出报表 |
| **触发端口** | 输入端口「导出触发」为 true 的那一次 | 放在流程末尾，由收工信号触发 |
| **每次执行** | 每次执行都导出 | ⚠️ 适合"一个班次只跑一次"的报表流程；**高频循环流程慎用**（每次都要重写整本报表） |

### ⚠️ 明确"没有"的配置

| 你可能会找 | 实际情况 |
|---|---|
| "是否带图"开关 | **没有**。靠 `LinkColumn` 有无该列隐式决定——找不到列就降级为纯表格 |
| 时间范围筛选 | **没有**。只支持"当前时刻"日期占位符，即"今天那份账" |
| 行数上限 | **没有**。只有 `OnlyRowsWithImage` 过滤 |
| 模板 xlsx | **没有**。报表是从 CSV 现场生成的 |
| 编码配置 | **没有**。本插件不写 CSV；编码在上游数据记录插件（固定 UTF-8 带 BOM） |

## 2.4 用的库：MiniExcel 1.46.0（不是 EPPlus）

| MiniExcel 能力 | 实测结论 |
|---|---|
| 读 CSV | ✅ 但**必须显式给 `excelType: ExcelType.CSV`**，否则按 xlsx 解析**直接抛** |
| 写 xlsx | ✅ `SaveAs(path, List<IDictionary<string,object>>, overwriteFile:true, configuration: OpenXmlConfiguration)`；**列序 = 字典键插入序 = CSV 表头序**，不需要列映射 |
| 冻结 / 筛选 | ✅ `OpenXmlConfiguration{ FreezeRowCount=1, AutoFilter=true }` |
| **超链接** | ❌ **完全不支持**（自写注入的唯一原因） |
| 公式 | ❌ 不支持（落成 `t="str"` 文本） |
| `EnableAutoWidth` | ❌ 必抛 |

> 默认样式索引：表头 `s="1"`、数据格 `s="2"`；`fonts count=2`、`cellXfs count=6`（**末位 cellXfs 是时间格式 numFmtId=21**）。

## 2.5 性能与独特实现

| 手段 / 设计 | 做法 | 收益 |
|---|---|---|
| **事后导出** | 不边测边写 xlsx | ⭐⭐⭐ 不占产线节拍 |
| **外链不嵌图** | 报表只放 `file:///` 链接 | ⭐⭐⭐ 文件小、能筛选排序、点开看高清原图 |
| **手动导出放后台线程** | `Task.Run` + `IsExporting` 重入保护 | ⭐⭐⭐ 几万行也不卡界面 |
| **原子替换** | 先写 `.tmp` 再替换 | ⭐⭐⭐ 任何一步失败都不会留下半个损坏的报表 |
| **即时存在性校验** | 面板上实时显示"✔ 账本已找到 / ✘ 账本不存在" | ⭐⭐⭐ 路径配错当场暴露 |
| **缺列静默降级** | 找不到链接列 → 纯表格，不报错 | ⭐⭐ 报表仍然可用 |
| **长 token 优先替换** | `{yyyy-MM-dd}` 不会被 `{yyyy}` 截胡 | ⭐⭐ 模板不写错 |
| **整包读内存** | xlsx 通常几 MB，内存换简单 | ⭐ 改写场景最划算 |

---

# 第 3 章 使用方式

## 3.1 安装与启用

1. **无需单独安装**：编译后由 PostBuild 把 `MiniExcel.dll` 投递到 `Modules\`。
   > MiniExcel.dll **不会**被误注册成插件（宿主 `PluginService.LoadPlugin` 只认 `Name.StartsWith("Plugin") && EndsWith(".dll")`），但能被默认 ALC 按 LoadFrom 连续性解析到。
2. 重开软件，工具箱里出现「Excel报表」（**数据处理** 分组）。

## 3.2 配置界面总览（4 张卡片）

```
┌─────────────────────────────────────────┐
│ ① 报表来源（CSV 账本）                    │
│    源步骤名 / 账本路径                    │
│    实际读取 →  <预览路径>  ✔ 账本已找到    │
│ ② 报表输出                               │
│    输出目录 / 报表文件名 / 图片走这里      │
│    实际写出 →  <预览路径>                 │
│ ③ 报表选项                               │
│    导出方式（下拉）                       │
│    触发信号（仅"触发端口"时出现，可🔗连线）│
│    冻结首行 / 自动筛选 / 只导有图行        │
│    导出后打开报表 / 导出失败阻断流程       │
│ ④ 立即导出                               │
│    「立即导出」按钮 + 结果文本             │
└─────────────────────────────────────────┘
```

## 3.3 第一次导出（5 步）

**第 1 步｜确认上游有账本**：先确保流程里有一个「CSV记录」步骤，并且它已经写出过账本。

**第 2 步｜填源步骤名**：在「源步骤名」填 CSV 记录步骤的实例名（如 `CSV记录_0`）。留空则用本步骤名。
> 面板上会**实时显示"实际读取 → 路径"和"✔ 账本已找到 / ✘ 账本不存在"**，配错当场发现。

**第 3 步｜设输出**：默认 `D:\Records\{StepName}\{yyyy-MM}\报表\` + `{yyyy-MM-dd}_报表.xlsx`，一般不用改。

**第 4 步｜设链接列**：「图片走这里」默认 `图片路径`——与数据记录插件的内置字段列同名，开箱即用。

**第 5 步｜导出**：点「立即导出」（后台线程跑，几万行不卡界面）。绿字显示 `✔ …`；成功后可选自动打开报表。

> 若要挂进流程：把「导出方式」改成 **触发端口**，并把收工信号连到「导出触发」端口。

## 3.4 参数速查表

| 你想做 | 调哪里 |
|---|---|
| 手动出一次报表 | 导出方式 = 仅手动 + 点「立即导出」 |
| 收工时自动出报表 | 导出方式 = 触发端口 + 连「导出触发」信号 |
| 每班一次固定出报表 | 导出方式 = 每次执行（⚠️ 高频循环慎用） |
| 只看有图的行 | 勾 `OnlyRowsWithImage` |
| 图片列不叫"图片路径" | 改 `LinkColumn` |
| 想按结果/日期筛选 | 保持 `AutoFilter=true`（默认开） |
| 导出后自动打开 | 勾 `OpenAfterExport`（仅手动导出时建议） |
| 报表出不了要停线 | 勾 `BlockOnFailure` |
| 账本找不到 | 检查源步骤名 + 账本路径模板（面板会实时提示） |

## 3.5 FAQ

**Q1** 报表里真的有图片吗？
**没有**。只有指向磁盘原图的 `file:///` **超链接**。报表里显示的是短文件名（如 `0001_OK.png`），点击才打开磁盘上的高清原图。好处：文件小、能筛选排序。代价：**原图被删/挪走链接就断**。

**Q2** 为什么点链接打不开？
① 原图被删除或挪走了；② 账本里记的是**相对路径**（相对路径不能作为外部链接目标，会被跳过）。数据记录插件存的是绝对路径，正常没问题。

**Q3** 能嵌图片进 Excel 吗？
**不能**（当前只做外链）。这是刻意取舍：嵌图会让报表体积暴涨且无法筛选排序。

**Q4** 为什么默认不阻断流程？
报表是给人看的附属产物。**宁漏报表不停线**（默认 `BlockOnFailure=false`）。要停线可勾。

**Q5** 每次执行都导出会怎样？
每次都要**重写整本报表**。适合"一个班次只跑一次"的报表流程；**高频循环流程慎用**。

**Q6** 能导出指定时间范围吗？
**不能**。只支持"今天那份账"（按当前日期展开占位符）。

**Q7** 图片列不存在会怎样？
**不报错**，降级为纯表格，并提示"未找到列『XX』"。报表仍然可用。

## 3.6 现场排障

| 报错 / 现象 | 排查 |
|---|---|
| 面板显示"✘ 账本不存在（检查『源步骤名』是否与 CSV 记录步骤一致）" | 源步骤名填错了，或那天还没产生账本 |
| `源账本不存在：{路径}（请检查『源账本路径』模板与『源步骤名』）` | 同上 |
| `没有启用的字段…`（本插件是"字段表没有启用的字段"） | — |
| `写入报表失败：…（报表可能正被 Excel 打开，请先关闭）` | **关闭 Excel 中打开的报表文件**再导 |
| `没有写入权限：…` | 输出目录无写权限 |
| Excel 打开报表提示"需要修复" | 极可能是 zip 改写三处有缺失——检查是否改过注入器 |
| 链接列没变蓝色 | 该列路径为空/相对路径 → 被跳过（空路径行保持原样） |
| 显示名分不出 png/jpg | 已改为 `GetFileName`（带扩展名）；旧版曾去扩展名导致 `0001_OK` |
| 导出一直没反应 | `IsExporting` 重入保护；检查是否有上一次未结束 |

---

# 第 4 章 异常与修复记录

> 本章记录**真实发生过的问题**（来自代码注释与开发文档）。每条给：现象 → 原因 → 修法。

## 4.1 缺陷修复总览

| 级别 | 问题 | 一句话 |
|---|---|---|
| P0 | MiniExcel 读 CSV 不传 `excelType` 必抛 | 默认按 xlsx 解析 → `InvalidDataException` |
| P0 | 漏写回 sheet → "关系有了、单元格没变" | 改过的 sheet 必须写回条目表 |
| P0 | rels 的 `Type` 写成 XName → Clark 记法 | Excel 直接报"需要修复" |
| P1 | CT_Font 子元素顺序错 | `u` 必须在 `sz/color/name` 之前，否则 Excel 报错 |
| P1 | 克隆末位 cellXfs → 时间格式 | 末位是 `numFmtId=21`，应克隆"数据格样式" |
| P1 | `<hyperlinks>` 位置不合 schema | 必须在 `autoFilter/mergeCells` 之后、`printOptions/pageMargins/drawing` 之前 |
| P1 | 显示名去扩展名 → 分不出 png/jpg | 改用 `GetFileName`（带扩展名） |
| P2 | 占位符截胡 | `{结果2}` 被 `{结果}` 抢先替换 → 长 token 优先 |
| P2 | `{StepName}` 配置期拿不到 | `InstanceName` 由引擎注入，构造时还没有 |
| P2 | `EnableAutoWidth` 必抛 | MiniExcel 不支持 |

## 4.2 P0/P1 级详解

### ① MiniExcel 读 CSV 必须显式给 `excelType`

**现象**：`InvalidDataException`（它默认按 **xlsx** 解析 CSV）。

**修法**：
```csharp
// —— 读账本：MiniExcel 读 CSV 必须显式给 excelType，否则它按 xlsx 解析直接抛 ——
var query = MiniExcel.Query(req.SourceCsvPath, useHeaderRow: true, excelType: ExcelType.CSV);
```

### ② 漏写回 sheet（"关系有了、单元格没变"）

**现象**：rels 里关系都建好了，但单元格内容没变、链接不生效。

**修法**（注释原文）：
> ```
> // ★ 必须把改过的 sheet 写回条目表：漏这步会退化成"关系有了、单元格没变"
> doc[sheetName] = sheet.ToString(SaveOptions.DisableFormatting);
> ```

### ③ rels 的 `Type` 必须是纯 URI

**现象**：Excel 报"需要修复"。

**原因**：写成 `XName` 会被序列化成 **Clark 记法** `{http://...}hyperlink`。

**修法**（注释原文）：
> ```
> // Type 必须是纯 URI 字符串：写成 XName 会被序列化成 Clark 记法 {http://...}hyperlink，Excel 直接报"需要修复"
> ```

### ④ CT_Font 子元素顺序

**现象**：Excel 报错。

**原因**：`CT_Font` 子元素顺序是 **schema 固定**的。

**修法**（注释原文）：
> ```
> // CT_Font 子元素顺序是 schema 固定的：u 必须排在 sz/color/name 之前，否则 Excel 报错
> ```
> 构造顺序：`u(single)` → `vertAlign(baseline)` → `sz(11)` → `color(rgb FF0563C1)` → `name(Calibri)` → `family(2)`

### ⑤ 克隆"数据格样式"而非末位

**现象**：链接格变成时间格式。

**原因**：MiniExcel 默认 `cellXfs` **末位是时间格式**（`numFmtId=21`）。

**修法**（注释原文）：
> ```
> // 克隆"数据格样式"而非末位样式：MiniExcel 默认 cellXfs 末位是时间格式（numFmtId=21）
> ```

### ⑥ `<hyperlinks>` 位置有 schema 约束

**现象**：Excel 报错。

**修法**（注释原文）：
> ```
> // sheet 子元素顺序固定：hyperlinks 在 autoFilter/mergeCells 之后，
> // 在 printOptions/pageMargins/pageSetup/headerFooter/rowBreaks/colBreaks/drawing 之前
> ```

### ⑦ 显示名去扩展名导致分不出 png/jpg

**现象**：显示 `0001_OK`，看不出是 png 还是 jpg。

**修法**：改用 `Path.GetFileName(target)`（**带扩展名**）。

### ⑧ `{StepName}` 配置期拿不到（与 DataRecord/ResultUpload 同一类）

**现象**：预览路径里出现"未赋值"。

**原因**：`InstanceName` 由引擎注入，构造视图时还没有。

**修法**：`EffectiveStepName`——运行期用 `InstanceName`，**配置期退回 `StepData.StepName`**；并在 `Loaded` 时刷新一次预览。

## 4.3 代码级踩坑汇编

| # | 现象 | 原因 | 正确做法 |
|---|---|---|---|
| 1 | 读 CSV 抛 `InvalidDataException` | 没传 `excelType` | `excelType: ExcelType.CSV` |
| 2 | Excel 报"需要修复" | rels 的 Type 写成 XName | 用纯 URI 字符串 |
| 3 | Excel 报错 | CT_Font 子元素顺序 | `u` 在 `sz/color/name` 之前 |
| 4 | 链接格变成时间格式 | 克隆了末位 cellXfs | 克隆数据格样式 |
| 5 | Excel 报错 | `<hyperlinks>` 位置不对 | 按 schema 顺序插入 |
| 6 | 关系有了单元格没变 | 漏写回 sheet 条目 | `doc[sheetName] = sheet.ToString(...)` |
| 7 | 分不出 png/jpg | 显示名去了扩展名 | `GetFileName`（带扩展名） |
| 8 | 预览出现"未赋值" | `InstanceName` 注入时机 | `EffectiveStepName` 回退 + Loaded 刷新 |
| 9 | 占位符截胡 | 短 token 先替换 | 长 token 优先 |
| 10 | 半个损坏的报表 | 写失败留下残包 | 先写 `.tmp` 再原子替换 |
| 11 | 相对路径链接失败 | 不能作 External 目标 | 用 `new Uri(path, UriKind.Absolute)` 判，失败跳过 |
| 12 | `EnableAutoWidth` 抛 | MiniExcel 不支持 | 不要用 |
| 13 | 中文被转义成 `\u4EF6` | 默认编码器 | `UnsafeRelaxedJsonEscaping` |

## 4.4 已知边界与待办

- ⚠️ **`FlowCanvasChecks/` 没有任何 ExcelExport 断言**（全目录检索 `Excel|报表` 只命中 Blob 的一条无关注释）。历史验证来自已删除的临时冒烟工程（37/37）。
- **只支持外链，不支持嵌图**。
- **只处理 sheet1**（多 sheet 报表不支持）。
- `OnlyRowsWithImage` 依赖链接列存在；列不存在时该过滤不生效。
- **图片路径必须是绝对路径**（相对路径会被跳过）。
- **`EachRun`（每次执行）高频慎用**——每次重写整本报表。
- **导出路径被占用不旁路改名**（与数据记录插件不同：那边会写 `_conflictN.csv`）。
- 超链接样式**字号固定 11、字体 Calibri**（不可配）。
- 五家视图（Excel/ResultUpload/DataRecord/CSharpScript/ImageScript）卡片内边距是 **12**，公共 `PluginCard` 是 **14**——待归一后删本地派生样式。

---

# 第 5 章 附录

## 5.1 附录 A：搭配使用的算子清单

★ = 与 Excel 报表直接相关。

| 工程目录 | 中文名 | 分组 | 怎么接 | 一句话用途 |
|---|---|---|---|---|
| **Plugin.DataRecord ★** | **CSV记录** | 数据处理 | **唯一数据源**：读它的账本 | 记流水账 |
| Plugin.ImageAcquisition ★ | 图像采集 | 常用工具 | （间接）原图来源 | 取图 |
| Plugin.BlobDetect ★ | Blob 缺陷检测 | 缺陷检测 | （间接）产出行与 NG 图 | 缺陷检测 |
| **Plugin.ExcelExport** | **Excel报表** | **数据处理** | — | 把账本加工成给人看的报表 |
| Plugin.ResultUpload | 结果上报 | 数据处理 | 并列：一个存档一个上报 | MES |
| Plugin.Motion.* | 运动控制 | — | （间接）报表里可含运动相关字段 | — |

### 典型接线

```
① 标准链路（事后导出，推荐）
采集 ──▶ 检测 ──▶ CSV记录（写账本，含 ImagePath 列）
                       ↓ 事后
                   Excel报表（读账本 → 带图链接 xlsx）

② 收工自动出报表
... ──▶ CSV记录
    ──▶ Excel报表（导出方式=触发端口，触发信号连收工信号）

③ 与结果上报并列
CSV记录 ──▶ Excel报表（人看）
        └─▶ 结果上报（机器/MES 看）
```

> **位置原则**：Excel 报表**不接端口数据流**，它读文件。所以它的上游是「CSV记录」的**产物**，不是连线。

## 5.2 附录 B：实战案例（6 个）

### 案例 1｜最小可用（默认配置直接导出）

```
源步骤名     = CSV记录_0
账本路径     = D:\Records\{源步骤}\{yyyy-MM}\{yyyy-MM-dd}.csv
输出目录     = D:\Records\{StepName}\{yyyy-MM}\报表\
报表文件名   = {yyyy-MM-dd}_报表.xlsx
图片走这里   = 图片路径

→ D:\Records\Excel报表_0\2026-09\2026-09-16_报表.xlsx
```

### 案例 2｜只导有图行（抽检 NG 图）

```
OnlyRowsWithImage = true
→ 4 行数据 → 只保留 3 行（有有效图片路径的）
```

### 案例 3｜列名写错 → 优雅降级

```
LinkColumn = "不存在的列"
→ 不报错，降级为纯表格，提示"未找到列『不存在的列』"
→ 不写 hyperlinks 节点，报表仍是合法可用 xlsx
```

### 案例 4｜特殊路径（逗号 + 空格）也能链

```
图片路径 = D:\Records\smoke, with comma\0003_OK.png
→ 正确百分号编码成 file:/// 链接（RFC4180 引号解析也过）
```

### 案例 5｜OOXML 三处改写（给二次开发者）

```
1) xl/worksheets/sheet1.xml
   目标列单元格挂链接样式、值改成短显示名，末尾追加 <hyperlinks> 段
2) xl/worksheets/_rels/sheet1.xml.rels
   追加 External 关系（Id → file:/// 目标），Type 必须是纯 URI
3) xl/styles.xml
   追加"蓝色下划线"字体（FF0563C1）+ 对应的 cellXfs 样式项
```

### 案例 6｜原子替换（防半个损坏报表）

```
写 path + ".tmp" → 成功 → File.Delete(path); File.Move(tmp, path)
失败 → 删 tmp → 抛出（不留残包）
```

## 5.3 附录 C：源码索引

### C.1 文件清单（`Plugins/Plugin.ExcelExport/`）

| 文件 | 职责 |
|---|---|
| `ExcelExportPlugin.cs` | 主插件类（333 行）：元数据、端口、配置、执行、预览 |
| `ExcelReportExporter.cs` | 导出编排（163 行）：占位符展开、读 CSV、写 xlsx、**模型定义** |
| `XlsxHyperlinkInjector.cs` | **OOXML zip 改写**（242 行，最硬核） |
| `ExcelExportView.xaml` | 配置界面（266 行，四卡片） |
| `ExcelExportView.xaml.cs` | 3 个转换器 + 加载/退订/校验（127 行） |

> 本插件**没有** `Models/` / `Services/` 目录（模型就地定义在 `ExcelReportExporter.cs`）。

### C.2 关键方法与位置

| 方法 | 位置 | 说明 |
|---|---|---|
| `GetConfigView` | `ExcelExportPlugin.cs:162` | `Initialize(stepData)` + 返回视图 |
| `EffectiveStepName` | `:173` | **名字回退**（运行期 InstanceName / 配置期 StepName） |
| `SourcePathPreview` / `OutputPathPreview` | `:187` / `:191` | 实时预览（含自动补 `.xlsx`） |
| `RunAlgorithm` | `:236` | 判定是否导出 → 导出 → 失败按 `BlockOnFailure` 处置 |
| `ExportNow` | `:266` | **手动导出（后台线程 + 重入保护）** |
| `RunExport` | `:290` | 组参 → 调用导出器 |
| `ExcelReportExporter.ExpandTemplate` | `Exporter:52` | **占位符展开（长 token 优先）** |
| `ExcelReportExporter.Export` | `Exporter:68` | 编排（**不抛异常**，失败收敛成 Result） |
| `XlsxHyperlinkInjector.Inject` | `Injector:39` | 注入主流程（三处改写） |
| `WriteZipEntries` | `Injector:216` | **原子替换** |

### C.3 数据模型

| 类 | 位置 | 字段 |
|---|---|---|
| `ExportPolicy` | `ExcelExportPlugin.cs:11` | `Manual` / `OnTrigger` / `EachRun` |
| `ExcelExportRequest` | `Exporter:12` | `SourceCsvPath` / `OutputXlsxPath` / `LinkColumn` / `FreezeHeader` / `AutoFilter` / `OnlyRowsWithImage` |
| `ExcelExportResult` | `Exporter:29` | `Success` / `OutputPath` / `Rows` / `Links` / `Message` |

## 5.4 附录 D：回归断言清单

⚠️ **`FlowCanvasChecks/` 没有任何 ExcelExport 断言文件**（30 个检查文件中无 `ExcelExportChecks.cs`；全目录检索 `Excel|报表` 仅命中 Blob 的一条无关注释）。

历史验证来自 **MVP 期临时冒烟工程（跑完已删）37/37 全过**，可整理成等价清单：

| 用例 | 测试点 | 结果 |
|---|---|---|
| **场景1 全量导出** | 导出 4 行 / 3 条链接；zip 里无残留 `.tmp`；`<x:hyperlink>` count=3 且 `ref=F2/F3/F4`；`r:id` 正确挂在 hyperlink 上；`hyperlinks` 位置在 `autoFilter(1991)` 之后、`drawing(2162)` 之前 | ✔ |
| **显示短名** | `0001_OK.png`（带扩展名）、单元格不再含完整路径；冻结 `pane ySplit=1`；筛选覆盖 `A1:F5` | ✔ |
| **rels** | 3 条 `External`、`Type` 是纯 URI（非 Clark 记法）、路径已编码成 `file:///` | ✔ |
| **特殊路径** | 含逗号+空格的路径正确百分号编码（RFC4180 引号解析也过） | ✔ |
| **styles** | 蓝色字体 `FF0563C1`，`fonts count=3 real=3`、`cellXfs count=7 real=7`（计数自洽），`F2` 挂 `s=6` | ✔ |
| **回读** | MiniExcel 回读 4 行、表头含「图片路径」、图片列已是短名（产物仍是合法 xlsx） | ✔ |
| **场景2 只导有图行** | 4 行 → 3 行，链接 3 | ✔ |
| **场景3 列名写错** | 不报错，降级为纯表格，不写 hyperlinks 节点 | ✔ |
| **场景4 源账本不存在** | 不抛异常，原因讲到"源账本不存在 + 请检查模板与源步骤名" | ✔ |
| **占位符展开** | `D:\Records\流程1.CSV记录\2026-09\2026-09-17.csv`；长日期 token 没被短 token 截胡 | ✔ |

**编译验证**：`Plugin.ExcelExport.csproj`（Debug + Release）**0 错误**。

> **维护建议**：后续应补 `FlowCanvasChecks/ExcelExportChecks.cs` 固化上表。

## 5.5 附录 E：术语表

| 术语 | 含义 |
|---|---|
| 账本 | 上游 `Plugin.DataRecord` 写出的 CSV 文件 |
| 事后导出 | 人要看时再导，而不是边测边写（报表是当时那份账的快照） |
| 外链 | 报表里放 `file:///` 超链接指向磁盘原图（不是嵌入图片） |
| 短文件名 | 单元格只显示 `0001_OK.png`，不显示完整路径 |
| MiniExcel | 轻量 Excel 读写库（1.46.0），写数据利落但**不支持超链接** |
| OOXML | Office Open XML——xlsx 本质是一个 zip + 若干 XML |
| `hyperlinks` | sheet1.xml 末尾的超链接定义段（位置有 schema 约束） |
| `rels` | `_rels/sheet1.xml.rels`，定义 External 关系 |
| `cellXfs` | styles.xml 里的单元格样式索引表（**末位是时间格式**） |
| CT_Font | OOXML 的字体定义（**子元素顺序固定**） |
| Clark 记法 | `{命名空间}名称` 的 XML 写法——rels 的 Type 不能用它 |
| 占位符截胡 | `{结果2}` 被 `{结果}` 抢先替换 → 需**长 token 优先** |
| 原子替换 | 先写 `.tmp` 再替换，防半个损坏文件 |
| `{源步骤}` vs `{StepName}` | 前者=上游 CSV 记录步骤名；后者=本 Excel 报表步骤名 |

## 5.6 附录 F：文档合并说明

本文件由以下文档合并而成（内容已全部并入，原文件已归档在 `docs/Excel报表/`）：

| 原文件 | 并入位置 |
|---|---|
| `2026-09-17-Excel带图报表导出插件.md`（101 行） | 第 1 章（三条共识 + 三段式图）+ 第 2 章（MiniExcel 实测事实 + 11 项配置）+ 第 3 章（四卡片 + 操作）+ 第 4.2 节（6 条落地踩坑）+ 第 4.4 节（已知边界 8 条）+ 第 5.4 节（37/37 冒烟） |
| `2026-09-25-插件配置视图样式统一.md`（Excel 部分） | 第 3.2 节（样式收编）+ 第 4.4 节（卡片 12 vs 14 待归一） |
| `2026-09-20-插件构建投递收口Directory.Build.targets.md`（1 行） | 第 3.1 节（MiniExcel.dll 投递） |
| `2026-09-27-Git仓库产物清理与NuGet镜像源.md`（1 行） | 第 3.1 节（MiniExcel.dll 属产物不入库） |

**未并入、但保留为参考的跨插件文档**：
`docs/数据记录/数据记录插件.md`（上游：UTF-8 带 BOM、内置 `ImagePath` 列、KeepDays 联动）、`docs/数据记录/README.md`、`docs/code-changes/2026-09-19-结果上报插件.md`（兄弟插件：明确"零第三方依赖，不学 ExcelExport 往 Modules 塞私有包"）。
