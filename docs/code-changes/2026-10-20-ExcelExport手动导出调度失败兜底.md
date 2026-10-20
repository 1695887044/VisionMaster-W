# 2026-10-20 Excel报表：手动导出的调度失败兜底（EE4）

## 1. 症状与根因

`ExcelExportPlugin.ExportNow()` 原先的兜底**只在 `Task.Run` 的 lambda 内部**：

```csharp
IsExporting = true;
LastResult = "正在导出…";
Task.Run(() => {
    try { ... }          // ← 只有这层
    catch (Exception ex) { LastResult = "✘ 导出异常：" + ex.Message; }
    finally { IsExporting = false; }
});
```

lambda 里的 `catch` 只在**任务已经开始执行**时才有机会跑。两类异常落在它外面：

1. **线程池拒绝调度**（进程退出中、线程池被掐、`OutOfMemoryException` 一类）；
2. lambda 还没进入内层 `try` 就被拆掉。

后果链（比"导出失败"更难查，因为界面**看不出任何错误**）：

- 异常直接抛给按钮事件 → UI 线程 → 可能带崩宿主；
- `LastResult` 永远停在「正在导出…」；
- `IsExporting` 一直是 `true` → 按钮永久置灰，且后续点击被 `if (IsExporting) return` 静默吞掉。

## 2. 修法

在 `Task.Run` **外层**加一层 `try/catch`（lambda 内原 `try/catch/finally` **逐字保留**）：

```csharp
try
{
    Task.Run(() => { try { ... } catch (...) { ... } finally { IsExporting = false; } });
}
catch (Exception ex)
{
    LastResult = "✘ 导出异常：" + ex.GetType().Name + "：" + ex.Message;
    IsExporting = false;
    Debug.WriteLine("[Excel报表] 导出调度失败：" + ex);
}
```

四条口径：

| # | 口径 | 理由 |
| --- | --- | --- |
| 1 | 外层 catch **不 rethrow** | 这是按钮事件（UI 线程），抛出去就是未捕获异常 → 进程退出 |
| 2 | 外层 catch **必须复位 `IsExporting`** | 不复位 = 按钮永久置灰 + 重入保护把后续点击全吞掉 |
| 3 | 文案带 **`ex.GetType().Name`** | 现场能分清是哪种异常；两层文案同形（都以「✘ 导出异常：」开头），结果面板的失败判色不用改 |
| 4 | 内层 `finally` **不许删** | 正常路径与业务异常的复位仍由它负责；外层只兜调度失败 |

调试输出用两条不同前缀区分层次：`[Excel报表] 后台导出异常`（内层）/ `[Excel报表] 导出调度失败`（外层）。

顺带把内层 catch 的文案也补上类型名（原为 `"✘ 导出异常：" + ex.Message`），两层口径一致。

## 3. 断言（`FlowCanvasChecks\FlowEngineFixChecks.cs` 新增 `[EE4]` 段，6 条）

判据落在**结构**上（`MethodBody` 取方法体 + 花括号配对定位外层 catch），不写行号与空白，改缩进/加注释不误伤（与 R46 同思路）：

| ID | 判据 | 对应的故障形状 |
| --- | --- | --- |
| ① | `Task.Run(` 出现在外层 `try` **之后** | Task.Run 裸露在外 = 调度异常直接抛给 UI 线程 |
| ② | 外层 catch 体含 `LastResult` 且含「导出异常」 | 界面停在「正在导出…」，用户以为卡死 |
| ③ | 外层 catch 体含 `IsExporting = false` | 按钮永久置灰，且后续点击被重入保护静默吞掉 |
| ④ | 外层 catch 体含 `ex.GetType().Name` | 现场分不清异常种类 |
| ⑤ | 方法体内 `finally` 仍在，且 `IsExporting = false` **恰好两处** | 只留外层（正常路径多走一次多余的 setter）或只留内层（调度失败不复位） |

### 变异反证（已做）

删掉外层 `catch` 块后重跑：

```
[√通过] ExportNow 方法体可取（判据前提）  →  体长=1179
[√通过] ★① Task.Run 落在外层 try 内（调度异常不许抛给按钮事件）  →  try@123 Task.Run@159
[×失败] ★② 外层 catch 写 LastResult（界面不会停在「正在导出…」）  →  外层 catch 未找到
[×失败] ★③ 外层 catch 复位 IsExporting（按钮不会永久置灰）  →  外层 catch 未找到
[×失败] 异常文案带类型名（现场能分清是哪种异常）  →  外层 catch 未找到
[×失败] ★④ 内层 finally 仍在（复位点必须两处：内层 finally + 外层 catch）  →  IsExporting = false 出现 1 次（应为 2）
```

恢复后 5 条全绿（体长回到 1457）。

## 4. 文档同步

| 文件 | 改动 |
| --- | --- |
| `Plugins\Plugin.ExcelExport\Help\10-dev-architecture.md` | 「手动导出的那条支路」流程图重画为两层 try/catch，补"为什么必须两层"+ 守门断言 + 变异反证 |
| `Plugins\Plugin.ExcelExport\Help\11-dev-invariants.md` | "界面卡在正在导出…"一行：补"若永远停在这句"的排查口径与守门断言（原为"无断言"） |
| `Plugins\Plugin.ExcelExport\Help\05-troubleshooting.md` | ①`导出异常：{类型名} {说明}` 一行补两层兜底的区分办法；②"结果文本一直停在正在导出…"补调试输出检索词 |
| `docs\UI设计基线\excel-export\README.md` | 新增「EE4 修复」段（症状/修法/边界/断言/变异反证） |

## 5. 验证

| 项 | 命令 | 结果 |
| --- | --- | --- |
| 插件构建 | `dotnet build Plugins\Plugin.ExcelExport\Plugin.ExcelExport.csproj --no-incremental`（改过 Help，按 R42/R62 全量重建） | **0 错误** |
| 内嵌手册对账 | `Modules\Plugin.ExcelExport.dll` 里的 `*.Help.*.md` 资源名数 vs `Help\` 下 .md 数 | **6 = 6**（含中文文件名的 `04-列映射与写入口径.md`），无漏打包 |
| 断言宿主构建 | `dotnet build FlowCanvasChecks\FlowCanvasChecks.csproj` | 0 错误（1 条既有 `HslCommunication` 警告） |
| 常规 gate | `dotnet run --project FlowCanvasChecks --no-build` | **2749 通过 / 0 失败**（EE4 6 条全绿） |
| 插件视图冒烟 | `dotnet run --project UIThemeSmokeTest --no-build -- --plugin-views Plugin.ExcelExport` | **11/11 PASS** |

> 说明：最后一次 `dotnet build FlowCanvasChecks` 被**其它并发任务**在 `VisionMaster\Views\DialogViews\GlobalVariableView.xaml`
> 引入的 `MC3024`（同一属性设两次）挡住，与本次改动无关；gate 用上一次成功构建的二进制跑（本组断言源码此后未再改）。

## 6. 未验证 / 遗留

1. **调度失败无法在真机复现**：线程池拒单不可控，所以这条只能靠静态结构断言守；运行时行为（复位 + 文案）按代码路径复核。
2. **视图侧判色未改**：结果面板仍按 `✔` / `✘` 前缀判成功/失败，两层 catch 的文案都以「✘ 导出异常：」开头，所以不需要改。
3. **未做**：把异常写进 `ILogService`（`ExportNow` 目前拿不到 `IExecutionContext`；手动导出按设计不写流程日志，
   只用 `Debug.WriteLine` 留痕）。若将来需要现场取证，应给插件注入日志服务而不是在这里 new 一个。
