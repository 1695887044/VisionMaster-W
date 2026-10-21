# 探针工程登记表（tools/probes）

> **2026-10-21 迁移**：原先散在仓库根目录的 16 个 `_XxxProbe` 已收编。
> 10 个仍在使用的搬到这里，6 个被取代的退役到 `_Retired/2026-10-21-probes/`。
> **目录名沿用历史名（保留 `_` 前缀）**：只搬位置、不改工程名，避免 AssemblyName 变化打断正在运行的任务。

---

## 1. 给"正在运行的任务"的硬约定

**不许再按记忆拼 `_ResProbe` 这类根目录路径**——它们已经不存在了。两种正确用法：

```powershell
# 列出全部已登记探针
powershell -NoProfile -ExecutionPolicy Bypass -File tools\probes\run-probe.ps1 -List

# 按登记名运行（推荐）
powershell -NoProfile -ExecutionPolicy Bypass -File tools\probes\run-probe.ps1 -Name ResProbe

# 只构建、不运行（改完代码先验编译）
powershell -NoProfile -ExecutionPolicy Bypass -File tools\probes\run-probe.ps1 -Name ChartProbe5 -BuildOnly
```

> **本机执行策略是 `Restricted`**（直接 `-File` 会报"禁止运行脚本"），所以命令必须带 `-ExecutionPolicy Bypass`。

如果某任务需要探针路径本身（例如要读它的输出文件），从 `probes.json` 解析，不要写死：

```powershell
$reg = [IO.File]::ReadAllText('tools\probes\probes.json', [Text.Encoding]::UTF8) | ConvertFrom-Json
$p = $reg.probes | Where-Object { $_.name -eq 'ResProbe' }
$project = Join-Path (Join-Path 'tools\probes' $p.dir) $p.project
```

**判据**：登记表里没有的探针 = 未登记探针，先登记再跑；`run-probe.ps1` 遇到未登记的名字会直接报错并列出全部已知名字。

---

## 2. 已登记探针（10 个）

| 登记名 | 目录 | 工程文件 | 运行方式 | 用途 |
| --- | --- | --- | --- | --- |
| `ResProbe` | `tools/probes/_ResProbe` | `_ResProbe.csproj` | `dotnet run` | 资源字典合并链 + 模板落地 + 弹窗尺寸（R32 运行期对照，期望 `RESULT: ALL_OK`） |
| `StyleProbe` | `tools/probes/_StyleProbe` | `_StyleProbe.csproj` | `dotnet run` | 控件样式与主题渲染对照 |
| `ToolboxProbe` | `tools/probes/_ToolboxProbe` | `_ToolboxProbe.csproj` | `dotnet run` | 工具箱面板（收藏 / 视图 / 分类映射） |
| `LogViewProbe` | `tools/probes/_LogViewProbe` | `_LogViewProbe.csproj` | `dotnet run` | 日志控制台（过滤 / 分组 / 粘底 / 容量） |
| `FocusProbe` | `tools/probes/_FocusProbe` | `_FocusProbe.csproj` | `dotnet run` | 焦点视觉与输入态 |
| `BeadViewProbe` | `tools/probes/_BeadViewProbe` | `_BeadViewProbe.csproj` | `dotnet run` | 胶路检测配置视图 |
| `ScadaEditorProbe` | `tools/probes/_ScadaEditorProbe` | `_ScadaEditorProbe.csproj` | `dotnet run` | 组态编辑器框选 / 拖拽 / 版式 |
| `ChartProbe5` | `tools/probes/_ChartProbe5` | `_ChartProbe5.csproj` | 构建后启 exe（net48） | 趋势曲线（ScottPlot）渲染 |
| `WebProbe5` | `tools/probes/_WebProbe5` | `WebProbe5.csproj` | `dotnet run` | SCADA 运行态 Web 客户端（M0 初版） |
| `WebProbe6` | `tools/probes/_WebProbe6` | `WebProbe6.csproj` | `dotnet run` | SCADA 运行态 Web 客户端（当前版） |

### 旧路径 → 新路径对照

| 旧路径（已不存在） | 新路径 |
| --- | --- |
| `_ResProbe\` | `tools\probes\_ResProbe\` |
| `_StyleProbe\` | `tools\probes\_StyleProbe\` |
| `_ToolboxProbe\` | `tools\probes\_ToolboxProbe\` |
| `_LogViewProbe\` | `tools\probes\_LogViewProbe\` |
| `_FocusProbe\` | `tools\probes\_FocusProbe\` |
| `_BeadViewProbe\` | `tools\probes\_BeadViewProbe\` |
| `_ScadaEditorProbe\` | `tools\probes\_ScadaEditorProbe\` |
| `_ChartProbe5\` | `tools\probes\_ChartProbe5\` |
| `_WebProbe5\` | `tools\probes\_WebProbe5\` |
| `_WebProbe6\` | `tools\probes\_WebProbe6\` |

> 历史 `docs/code-changes/*.md` 里出现的旧路径**保留原文不追改**——它们是带日期的历史记录。
> 当前要跑探针，一律以本表和 `probes.json` 为准。

---

## 3. 已退役（6 个）

| 目录 | 退役原因 | 新位置 |
| --- | --- | --- |
| `_ChartProbe` | 被 `_ChartProbe5` 取代 | `_Retired/2026-10-21-probes/_ChartProbe` |
| `_WebProbe1` | 被 `_WebProbe5/6` 取代 | `_Retired/2026-10-21-probes/_WebProbe1` |
| `_WebProbe2` | 被 `_WebProbe5/6` 取代 | `_Retired/2026-10-21-probes/_WebProbe2` |
| `_WebProbe3` | 被 `_WebProbe5/6` 取代 | `_Retired/2026-10-21-probes/_WebProbe3` |
| `_ApiDump` | 一次性 API 导出工具 | `_Retired/2026-10-21-probes/_ApiDump` |
| `_MvvmDemo` | MVVM 演示件，无生产消费 | `_Retired/2026-10-21-probes/_MvvmDemo` |

退役目录**不参与构建、不在任何 `.sln` 里**；恢复方式见 `_Retired/2026-10-21-probes/README.md`。

---

## 4. 迁移时做过的路径修正

搬动后仓库根从"上一级"变成"上三级"，10 个工程里有 9 个改过 csproj：

| 工程 | 改动 |
| --- | --- |
| `_BeadViewProbe` | `ProjectReference` / `HintPath`：`..\` → `..\..\..\` |
| `_FocusProbe` | 同上（UI / VisionMaster） |
| `_LogViewProbe` | 同上（VisionMaster + 5 个插件 + Halcon） |
| `_ResProbe` | 同上（VisionMaster + Halcon） |
| `_ScadaEditorProbe` | 同上（VisionMaster） |
| `_StyleProbe` | 同上（UI） |
| `_ToolboxProbe` | 同上（VisionMaster） |
| `_WebProbe5` | 同上（ScadaWeb + 5 个 Watson DLL） |
| `_WebProbe6` | 同上（ScadaWeb + 5 个 Watson DLL + `wwwroot` Content） |
| `_ChartProbe5` | 无相对引用，未改 |

**验收**：10 个工程全部 `-BuildOnly` 通过；`ResProbe` 输出 `RESULT: ALL_OK`；`ChartProbe5` 能启动出图。

---

## 5. 约束（已写入 AGENTS.md §36）

- **C9**：探针只许登记在 `probes.json`，运行只许走 `run-probe.ps1`；根目录不再接受任何 `_XxxProbe`。
- **C10**：搬迁必须"新址构建通过 → 更新登记表 → README 旧→新对照"三步原子完成，禁止先搬后补；正在运行的任务必须先停。
- **C11**：新建探针放 `tools/probes/<Name>/`，并在 `probes.json` + 本表同时登记；未登记 = 不存在。
