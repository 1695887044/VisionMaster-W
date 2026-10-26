# 分册：AI 操作电脑 / 调用 exe 测试的踩坑台账

> 上位规则：`AGENTS.md` §41 / **R185**。
> 用途：把"AI 通过操作电脑、调用 exe/探针/断言宿主做测试"时踩过的坑收在一处。
> **开工前查「快速索引」→ 收工前按模板补**；重复问题合并更新，不新增重复条目。
> 本文件是**操作经验台账**，不是规则正文；规则判据以 `AGENTS.md` 为准。

## 状态图例

| 标记 | 含义 |
|---|---|
| ✅ | 已解决：有明确解法，按条目照做即可 |
| ⚠️ | 仍存在：有规避手段，但没有根治（环境/第三方/已知边界） |
| 🗑️ | 已失效：环境、版本或代码已变，条目不再适用（保留供追溯） |

## 记录模板（复制这段）

```markdown
#### <ID> <一句话标题>  [✅/⚠️/🗑️]  最近：YYYY-MM-DD  复发：N 次
- **现象**：
- **触发场景 / 复现**：
- **原因**：
- **解决办法**：
- **规避建议**：
- **相关命令 / 参数**：
```

## 合并规则

1. **同一现象**（同一报错文本 / 同一根因）→ 只更新原条目：追加"最近日期"、`复发 +1`、补充新解法或新命令；**不新增 ID**。
2. **不同根因** → 新开 ID；在条目里写"与 X-0N 的区别"。
3. 环境变了导致解法失效 → 标 🗑️ 并写失效原因，不删条目。
4. 条目只写"能改变下次行为"的内容；大段异常栈/取证过程放 `docs/code-changes/`，这里只留一句指针。

---

## 0. 快速索引（按症状查）

| 症状 / 关键词 | 条目 | 状态 |
|---|---|---|
| 构建报 `MSB3021`/`MSB3027`、`文件被 "VisionMaster (PID)" 锁定` | A-01 | ✅ |
| 启动第二个实例后前一个被踢 / 新实例直接退出 | A-02 | ✅ |
| 跑完真 exe 后用户布局变了 / 停靠面板位置丢了 | A-03 | ✅ |
| `taskkill` 后马上又出现新 PID，构建仍被锁 | A-04 | ⚠️ |
| 运行中的宿主突然消失，无退出日志、无崩溃事件 | A-06 | ✅ |
| 按截图坐标点按钮点到相邻按钮（"点了没反应"） | C-07 | ✅ |
| 两个断言宿主同时跑，证据 PNG 被锁、出现假红 | C-08 | ✅ |
| 宿主运行中读日志抛 IOException（文件被独占） | C-09 | ✅ |
| 红了一条之后另一条莫名崩 / 清理代码没跑 | C-10 | ✅ |
| 想加探针，但构建报的是别人正在改的错 | C-11 | ✅ |
| 构建报 `RG1000`「同一 BAML 键重复」（指向没改过的文件） | C-12 | ✅ |
| 构建冒烟宿主时锁 `Modules\Plugin.*.dll` | C-13 | ✅ |
| `UIThemeSmokeTest` 日志乱码、看不到 `[FAIL]` | C-14 | ✅ |
| 构建绿但自己加的断言一条没跑 | C-15 | ✅ |
| 插件属性 setter 自动播种默认 ROI → 探针手工加的 ROI 没参与 | C-16 | ✅ |
| 改完 `AppConfig.json` 几分钟后又变回旧内容 / 体积暴涨 | A-05、E-02 | ✅ / ⚠️ |
| `dotnet build` 报"生成失败 / 0 个错误"、退出码 1 | B-01 | ✅ |
| 改了断言但结果没变 / "全部通过"但新断言没跑 | B-02 | ✅ |
| 只想验 Core，构建却在 VisionMaster 复制阶段失败 | B-03 | ✅ |
| 改了插件源码，运行逻辑仍旧 | B-04 | ✅ |
| 日志中文乱码、统计正则永不命中、误报全绿 | C-01 | ✅ |
| 测试空转十几分钟 / `ExitCode` 恒为 `$null` | C-02、F-02 | ✅ |
| 汇总全绿，实际一个宿主都没跑 | C-03 | ✅ |
| `.ps1` 报 `MissingEqualsInHashLiteral` / 中文常量乱码 | C-04 | ✅ |
| `findstr` 搜中文明明存在却搜不到 | C-05 | ✅ |
| 根目录多出 `$null`；`audit_root` 报 `FILE  $null` | C-06 | ⚠️ |
| 相对路径找不到文件（文件确实存在） | D-01 | ✅ |
| `File.Replace` 抛 `UnauthorizedAccessException` | D-02 | ✅ |
| 保存时报"路径过长" | D-03 | ✅ |
| 配置清单每次启动翻倍 / `AppConfig.json` 涨到几十 MB | E-01 | ✅ |
| 清理后的配置被 `.bak-*` 原样覆盖 | E-02 | ⚠️ |
| `解决方案\` 根下永远有一个平铺 `.vms` | E-03 | ⚠️ |
| 19000/19100/8080 被占，服务起不来 | F-01 | ✅ |
| gate 挂死、无子进程、无日志 | F-02 | ✅ |
| HALCON `create_pose` 旋转单位/`'gba'` 顺序不对，手眼结果差几十毫米 | G-01 | ✅ |
| `HPose.RawData` 是 mixed 元组 → `#8480` / `#1304` | G-02 | ✅ |
| `find_calib_object` 没有 InitialPose / 校准前读 `calib_obj_pose` 报 `#8451` | G-03 | ✅ |

---

## 1. AI 调软件测试的标准流程（SOP）

> 目标：一次比一次熟练。每一步都对应下面的坑条目；遇到新坑先查索引，再按模板补。

### 1.1 先选层级（R175）

| 层级 | 什么时候用 | 例子 |
|---|---|---|
| 断言宿主（首选） | 逻辑/数据/静态契约；快、无副作用、不依赖桌面 | `ScadaChecks.exe`、`FlowCanvasChecks.exe`、`UIThemeSmokeTest.exe`、`HelpProbe.exe` |
| 探针 | 断言宿主证明不了的渲染/布局/像素 | `tools\probes\run-probe.ps1 -Name <登记名>`（R167） |
| 真 `VisionMaster.exe` | 真实渲染 / 启动链 / 停靠布局 / HTTP 端到端 | 只有前两层证明不了时才用 |

### 1.2 开工前检查清单

```powershell
# 1) 读台账：本文件「0. 快速索引」+「4. 最近更新」
# 2) 宿主是否在跑（A-01/A-02/A-04）
Get-Process VisionMaster -ErrorAction SilentlyContinue | Select-Object Id,StartTime,Path
# 3) 跑真 exe 前备份布局（A-03，R176）
Copy-Item "VisionMaster\bin\Debug\net9.0-windows\Layout.xml"        "artifacts\backup\<日期>-<主题>\"
Copy-Item "VisionMaster\bin\Debug\net9.0-windows\DefaultLayout.xml" "artifacts\backup\<日期>-<主题>\"
# 4) 端口（F-01，R176）
netstat -ano | findstr ":19000 :19100 :8080"
# 5) 日志目录（R166）
#    artifacts\logs\<yyyyMMdd>\ ；不要在仓库根 > xxx.log
```

判据：宿主数量 = 0 或 1（要构建时必须是 0）；端口无占用；备份文件存在。

### 1.3 构建

```powershell
dotnet build <工程>.csproj -m:1 -nodeReuse:false
```

- **判据**：`0 个错误` **且** 退出码 0（B-01）。
- 宿主运行时不要构建（A-01）；先优雅关闭并等进程真正退出。
- 只验 Core + 断言宿主时：先单独构建 Core，再 `--no-dependencies` 构建宿主（B-03）。
- 改了插件源码必须重建该插件工程（B-04 / 红线③）。

### 1.4 执行

- 断言宿主优先；按它自己的 csproj 先构建再跑（B-02）。
- 捕获输出**不要走管道**（C-01）；用 `[Diagnostics.Process]::Start` + 有界 `WaitForExit(ms)` +
  `StandardOutput.ReadToEndAsync()`，原始字节/文本落 `artifacts\logs\<yyyyMMdd>\`。
- 超时、启动失败、**零输出**一律判红（C-03 / F-02）：

```powershell
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $exe
$psi.Arguments = '<参数>'
$psi.UseShellExecute = $false
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$p = [System.Diagnostics.Process]::Start($psi)
$out = $p.StandardOutput.ReadToEndAsync()
$err = $p.StandardError.ReadToEndAsync()
if (-not $p.WaitForExit(300000)) { try { $p.Kill() } catch { }; throw 'TIMEOUT' }
$text = $out.Result + $err.Result   # 空串 = 红，不是绿
```

### 1.5 判读

- 看**条数 + 明细原文**，不看"全部通过"汇总行（R65/R183）。
- 与基线对照：通过/失败条数、关键 Check 的名称与证据字段。
- 新加的闸必须做**变异反证**：改坏 → 红 → 还原 → 绿（R65③）。
- 中文检索不要用 `findstr`（C-05）。

### 1.6 收工

1. 恢复被测试改动的布局/配置（A-03 / A-05）。
2. 关掉测试自起的进程（A-02 / F-01）。
3. **更新本台账**：新增/合并条目，更新「3. 经验迭代」与「4. 最近更新」。
4. 写 `docs/code-changes/` 或交接报告（R177），汇报里写「台账已更新：新增 N 条 / 合并 M 条」。

---

## 2. 坑记录

### A. 进程与宿主

#### A-01 宿主运行时构建在复制阶段失败（DLL 被锁）  [✅]  最近：2026-10-26  复发：5 次
- **现象**：`dotnet build VisionMaster\VisionMaster.csproj` 报 `MSB3027/MSB3021`，提示
  `The process cannot access the file '...\VM.Core.dll' because it is being used by another process. 文件被“VisionMaster (PID)”锁定`。
- **触发场景 / 复现**：`VisionMaster.exe` 正在运行（含用户刚重启的实例），执行任何会复制到
  `VisionMaster\bin\` 的构建（宿主工程本身，或引用宿主的断言宿主）。
- **原因**：进程已把 `VM.Core.dll` / `UI.dll` / `VM.FlowEngine.dll` / `VM.Communication.dll`
  映射进地址空间，Windows 不允许覆盖已映射文件；构建的复制阶段重试 10 次后失败。
- **解决办法**：先优雅关闭宿主——`taskkill /PID <pid>`（**不要 `/F`**，让它走退出链/草稿转存），
  轮询 `Get-Process -Id <pid>` 直到消失，再构建。
- **规避建议**：构建前先查 `Get-Process VisionMaster`；宿主在跑就不要开始构建链；
  不要靠"反复重试"赌它刚好退出。
- **相关命令 / 参数**：
  ```powershell
  Get-Process VisionMaster -ErrorAction SilentlyContinue | Select-Object Id,StartTime
  taskkill /PID <pid>
  dotnet build VisionMaster\VisionMaster.csproj -m:1 -nodeReuse:false
  ```
- **2026-10-26 补充（两个会话同时构建/跑测）**：锁不一定来自宿主，还可能来自**另一个会话的构建**。
  实测两种：
  ① `error MC2000: … The process cannot access the file 'Scada.Controls\obj\Debug\net9.0-windows\VM.Scada.Controls.dll'`
  ——对方正编同一个工程（XAML markup 阶段写 obj 中间 DLL）；
  ② `error MSB3027/MSB3021: … ScadaChecks.exe … 文件被"ScadaChecks (34424), ScadaChecks (17836)"锁定`
  ——对方在跑断言宿主。
  **判据**：构建前查 `Get-Process dotnet,MSBuild,ScadaChecks,VisionMaster`（`VBCSCompiler` 是常驻编译器服务，
  不算构建）；有就等它退，不要反复重试，也不要 `-F` 杀别人的跑测。

#### A-02 单实例互踢  [✅]  最近：2026-10-25  复发：1 次
- **现象**：启动第二个 `VisionMaster.exe` 后，先启动的那个被踢掉或新实例直接退出；正在跑的测试连接中断。
- **触发场景 / 复现**：调试时重复双击/重复 `Start-Process`，或测试脚本与用户各起一个。
- **原因**：启动自检里的 `SingleInstanceCheck` 只允许一个实例（R176）。
- **解决办法**：只保留一个实例；需要重启时先关旧实例，确认退出后再起。
- **规避建议**：跑真 exe 前 `Get-Process` 计数；测试脚本不要并发起两个宿主。
- **相关命令 / 参数**：`Get-Process VisionMaster -ErrorAction SilentlyContinue`

#### A-03 启动覆盖 Layout.xml / DefaultLayout.xml  [✅]  最近：2026-10-25  复发：1 次
- **现象**：跑完真 exe 后用户布局变了（停靠面板位置/宽度丢失）。
- **触发场景 / 复现**：直接启动 `VisionMaster.exe`；启动链会写布局文件。
- **原因**：停靠布局在启动/退出时落盘，覆盖用户当前布局。
- **解决办法**：跑前备份 `Layout.xml` 与 `DefaultLayout.xml` 到
  `artifacts\backup\<yyyyMMdd>-<主题>\`；需要时按备份恢复。
- **规避建议**：把"备份布局"当成跑真 exe 的默认第一步（R176），不要等丢了再找。
- **相关命令 / 参数**：
  ```powershell
  Copy-Item "VisionMaster\bin\Debug\net9.0-windows\Layout.xml"        "artifacts\backup\<日期>-<主题>\"
  Copy-Item "VisionMaster\bin\Debug\net9.0-windows\DefaultLayout.xml" "artifacts\backup\<日期>-<主题>\"
  ```

#### A-04 taskkill 后新实例立刻出现  [⚠️]  最近：2026-10-25  复发：2 次
- **现象**：关闭 PID 后几秒内又出现新 PID（例如 23:02、23:13 两次），构建仍被锁。
- **触发场景 / 复现**：用户/现场脚本自动拉起，或用户看到窗口消失后立即手动重启。
- **原因**：外部拉起，不是测试脚本能控制的；`taskkill` 只关掉当下那一个。
- **解决办法**：不要连续 kill；先确认没有实例再构建；如果反复出现，停下来与用户确认谁在拉起。
- **规避建议**：关闭前记录 `Id,StartTime`；构建前再查一次；必要时把构建放到"用户确认已关闭"之后。
- **相关命令 / 参数**：`Get-Process VisionMaster | Select-Object Id,StartTime`

#### A-05 运行中的宿主回写 AppConfig.json  [✅]  最近：2026-10-25  复发：1 次
- **现象**：清理/修改 `AppConfig.json` 后几分钟又变回旧内容，或体积暴涨。
- **触发场景 / 复现**：宿主运行中改配置文件；随后触发任意设置保存（工具箱收藏、帮助开发章节、
  画布开关、方案清单确认、系统设置保存）。
- **原因**：宿主内存里是旧配置，`AppSettingsService.Save()` 整份覆盖磁盘。
- **解决办法**：先关宿主再改配置；改完再启动。或者改完立即重启宿主，让它重新加载。
- **规避建议**：配置类修改一律走"关宿主 → 改 → 启动"；改完立刻用条数/大小复核。
- **相关命令 / 参数**：`(Select-String -Path <AppConfig.json> -Pattern '"Path":' -AllMatches).Count`

#### A-06 运行中的宿主被并发构建结束（无退出日志、无崩溃事件）  [✅]  最近：2026-10-26  复发：1 次
- **现象**：真机测试中途 `VisionMaster` 进程直接消失；日志停在半拍、**没有 `[退出链]` 行**；
  Windows 事件日志里**没有** Application Error / .NET Runtime 条目。
- **触发场景 / 复现**：另一个会话正在跑构建/`run_gates.ps1`（`dotnet` 进程存活十几分钟），
  我在同一 bin 目录跑真 exe。实测：宿主 06:20:40 起、06:27:41 消失；同时段 `RestartManager`
  事件 10000/10001 密集、`bin\` 里 exe(06:25:53)/dll(06:22:02) 被刷新。
- **原因**：构建侧为释放文件锁把运行中的宿主结束掉（A-01 的解法），属**外部结束**，不是崩溃。
- **解决办法**：判性质看三件事——有没有 `[退出链]` 日志（优雅退出必有）、有没有 WER 事件（崩溃必有）、
  同时段有没有构建在跑。三者都没有退出链但有构建 → 判外部结束，**不要按崩溃去查代码**。
- **规避建议**：跑真 exe 前先看 `Get-Process dotnet,MSBuild` 有没有在跑；有就等它结束再起，
  否则测到一半会被踢。被踢后先核对 `bin\` 里 exe/dll 的 mtime，再决定是否重跑。
- **相关命令 / 参数**：
  ```powershell
  Get-Process dotnet,MSBuild -ErrorAction SilentlyContinue | Select-Object Id,StartTime
  $lines=[IO.File]::ReadAllLines($log,[Text.Encoding]::UTF8); $lines | Select-String '退出链' | Select-Object -Last 3
  Get-WinEvent -FilterHashtable @{LogName='Application'; StartTime=(Get-Date).AddMinutes(-30)} | Where-Object Id -in 1000,1001,1026
  ```

### B. 构建与产物

#### B-01 `dotnet build` 多进程静默失败  [✅]  最近：2026-10-25  复发：多次
- **现象**：报"生成失败 / 0 个错误"、退出码 1；或看起来成功但产物没更新。
- **触发场景 / 复现**：本机默认 `dotnet build` / `dotnet restore`（多进程 MSBuild）。
- **原因**：本机多进程构建不稳定（R179）。
- **解决办法**：一律加 `-m:1 -nodeReuse:false`。
- **规避建议**：判据只看两件事：`0 个错误` **且** 退出码 0；不要只看其中一项。
- **相关命令 / 参数**：`dotnet build <工程>.csproj -m:1 -nodeReuse:false`

#### B-02 断言宿主 exe 陈旧（改了 Check 没生效）  [✅]  最近：2026-10-25  复发：1 次
- **现象**：改了断言代码，跑出来还是旧结果；或"全部通过"但新断言根本没跑。
- **触发场景 / 复现**：直接运行 `bin\...\XxxChecks.exe` 而没先构建；或只 build 了
  `VisionMaster.sln`（`UIThemeSmokeTest` / `HelpProbe` 不在 sln）。
- **原因**：exe 比源码旧（R65/R182）。
- **解决办法**：先按该宿主自己的 csproj 构建再跑；`tools\run_gates.ps1` 自带陈旧 exe 检测。
- **规避建议**：不用 `--no-build`；跑之前核 exe mtime ≥ 源码 mtime。
- **相关命令 / 参数**：`dotnet build <Host>\<Host>.csproj -m:1 -nodeReuse:false`

#### B-03 构建 ScadaChecks 连带构建宿主  [✅]  最近：2026-10-25  复发：2 次
- **现象**：只想验 Core，构建却在 VisionMaster 复制阶段被锁失败。
- **触发场景 / 复现**：宿主运行中执行 `dotnet build ScadaChecks\ScadaChecks.csproj`。
- **原因**：项目引用链把 `VisionMaster.csproj` 一起构建。
- **解决办法**：先单独构建 Core，再 `--no-dependencies` 构建 ScadaChecks：
  ```powershell
  dotnet build Core\VM.Core.csproj -m:1 -nodeReuse:false
  dotnet build ScadaChecks\ScadaChecks.csproj -m:1 -nodeReuse:false --no-dependencies
  ```
- **规避建议**：开工先明确"要验哪一层"；不需要宿主时不要触发它的构建。

#### B-04 插件源码改了没重建（Modules 仍是旧 DLL）  [✅]  最近：2026-10-25  复发：1 次
- **现象**：插件逻辑仍旧；`Modules\Plugin.X.dll` 的 mtime 比源码旧。
- **触发场景 / 复现**：只重建宿主，或只改插件源码没构建该插件工程。
- **原因**：`Modules\` 不由宿主带出（红线③）。
- **解决办法**：重建该插件工程；核 `DLL mtime ≥ 源码 mtime` 且 `Help` 内嵌资源数 = 磁盘 `.md` 数。
- **规避建议**：收尾跑 `tools\audit_artifacts.ps1`；改过 Help 文案用 `--no-incremental`。
- **相关命令 / 参数**：`tools\audit_artifacts.ps1`

### C. 命令、编码与输出

#### C-01 子进程输出走管道 → 936 解码乱码 / 正则永不命中  [✅]  最近：2026-10-25  复发：多次
- **现象**：日志中文乱码（"合法 UTF-8 的乱码"）、统计正则永不命中、每个宿主回落打印"全部通过"。
- **触发场景 / 复现**：`& $exe 2>&1 | Out-String`（R183①）。
- **原因**：.NET 宿主输出 UTF-8，PowerShell 管道按控制台代码页（本机 936）解码。
- **解决办法**：原始字节直接落盘；回读严格 UTF-8 优先、失败回落 936。
- **规避建议**：不要用 `Out-String`/管道捕获被测进程输出；用 `[Diagnostics.Process]` + 文件。
- **相关命令 / 参数**：`StandardOutput.BaseStream.CopyToAsync($fs)`

#### C-02 `Start-Process -Wait` 挂死 / `ExitCode` 恒为 `$null`  [✅]  最近：2026-10-25  复发：2 次
- **现象**：测试空转十几分钟、无子进程、无日志；或 `exit=0` 报全绿但实际失败。
- **触发场景 / 复现**：`Start-Process -Wait -PassThru` 跑被测进程（R183③）。
- **原因**：`-Wait` 等**整棵进程树**（残留后代就挂死）；去掉 `-Wait` 后 `ExitCode` 尚未填充，
  `$null -ne 0` 判真 → `[int]$null` = 0 → 印出 exit=0。
- **解决办法**：`[Diagnostics.Process]::Start(ProcessStartInfo)` + 有界 `WaitForExit(ms)`；
  退出码参数不许强类型，`$null` 一律判红。
- **规避建议**：超时判 `exit 124`、启动失败判 `exit 127`、无输出判 `NO OUTPUT`。
- **相关命令 / 参数**：见 §1.4 的代码片段。

#### C-03 子进程零输出被当成通过  [✅]  最近：2026-10-25  复发：1 次
- **现象**：gate 汇总全绿，实际六个宿主一个都没跑。
- **触发场景 / 复现**：`Start-Process -ArgumentList ''` 抛参数绑定异常，而
  `$ErrorActionPreference='Continue'` 把它变成 `Exit=0`（R183②）。
- **原因**：只看退出码，不看输出。
- **解决办法**：零输出判红；统计行必须能从日志里数出来。
- **规避建议**：汇报前先数日志行数/条数，再信"全绿"。

#### C-04 PowerShell 5.1 读无 BOM UTF-8 `.ps1` 中文乱码  [✅]  最近：2026-10-25  复发：2 次
- **现象**：`MissingEqualsInHashLiteral`；中文常量变成乱码；脚本行为与源码不一致。
- **触发场景 / 复现**：`.ps1` 里直接写中文，`powershell -File` 运行。
- **原因**：PS 5.1 按 936 代码页读无 BOM 的 UTF-8 脚本（R1/R2）。
- **解决办法**：`.ps1` 纯 ASCII（含注释）；中文放独立 UTF-8 清单文件，用
  `[IO.File]::ReadAllLines($f, [Text.Encoding]::UTF8)` 读；不在 `-Command` 里传中文。
- **规避建议**：需要中文参数时写 UTF-8 清单文件，脚本只认文件路径。

#### C-05 `findstr` 用中文 pattern 搜 UTF-8 文件不命中  [✅]  最近：2026-10-25  复发：3 次
- **现象**：明明存在的行搜不到；据此误判"没有引用"。
- **触发场景 / 复现**：`findstr /c:"方案清单" "VisionMaster\bin\...\Logs\2026-10-25.log"`。
- **原因**：`findstr` 用当前代码页（936）解释 pattern 与文件，日志是 UTF-8。
- **解决办法**：用 PowerShell `[IO.File]::ReadAllLines($f,[Text.Encoding]::UTF8)` +
  `Where-Object` / `Select-String`；或改搜 ASCII 标记（数字、`RESULT:`、`Count=`、`WROTE`）。
- **规避建议**：中文检索不走 `findstr`；不确定编码时先按 ASCII 数字定位。

#### C-06 cmd 里 `> $null` 建出名为 `$null` 的文件  [⚠️]  最近：2026-10-25  复发：2 次
- **现象**：仓库根多一个 `$null` 文件；`tools\audit_root.ps1` 报 `FILE  $null`（名字显示为空）。
- **触发场景 / 复现**：在 `cmd.exe` 里写 `command > $null`（`$null` 是 PowerShell 概念）。
- **原因**：cmd 把 `$null` 当普通文件名，创建同名文件。
- **解决办法**：删除该文件；cmd 用 `> nul`，PowerShell 用 `> $null`。
- **规避建议**：按 shell 选空设备；`audit_root` 复跑确认（瞬时条目也可能自己消失）。
- **相关命令 / 参数**：`Remove-Item -LiteralPath 'D:\C#\VM\$null' -Force`

#### C-07 按截图坐标点按钮点到相邻按钮（"点了没反应"）  [✅]  最近：2026-10-26  复发：1 次
- **现象**：点组态工具栏的 `1:1` 后倍率不变（仍 46%），看起来像"按钮没响应"或"软件卡了"。
- **触发场景 / 复现**：在 1280×860 的窗口截图上按肉眼估的坐标点 `(490,114)`；
  该行实际是 `− | 46% | + | 适应 | 1:1`，`适应` 与 `1:1` 的框只隔约 20 px，点到了 `适应`
  （重新算一次适应 = 仍是 46%，所以毫无变化）。
- **原因**：截图 raster 与窗口物理尺寸不是 1:1（本次 raster 1280×860 vs 窗口 1806×1176），
  肉眼估点误差 > 10 px，就跨到邻居按钮。
- **解决办法**：先用 `System.Drawing` 裁出工具栏区域并按 4 倍最近邻放大（落 `artifacts\render\<日期>\`），
  在放大图上量出按钮框再换算回 raster 坐标；能用无障碍元素（AX index）就别用坐标。
- **规避建议**：点完**必须回看读数**（倍率/勾选态），不要以"点了"当"生效"；
  一排相邻按钮（缩小/倍率/放大/适应/1:1）一律先量后点。
- **相关命令 / 参数**：
  ```powershell
  # 裁图 4 倍放大后再量按钮框
  $crop = $bmp.Clone((New-Object System.Drawing.Rectangle 330,90,300,50), $bmp.PixelFormat)
  $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
  ```

#### C-08 两个断言宿主同时跑 → 证据 PNG 被锁、出现假红  [✅]  最近：2026-10-26  复发：1 次
- **现象**：`ScadaChecks` 报 5 条红，其中一条是
  `System.IO.IOException: The process cannot access the file '…\artifacts\scada-editor-toolbar.png' because it is being used by another process.`
  同一构建前后两次跑却是全绿。
- **触发场景 / 复现**：另一个会话在同一时刻也跑 `ScadaChecks`（两个同名进程并存），
  两边都往同一个证据 PNG 写。
- **原因**：证据图落点是固定路径（`bin\...\artifacts\`），不按进程隔离。
- **解决办法**：跑之前 `Get-Process ScadaChecks` 确认只有一个；撞了就等对方退再跑，别把这次的红当回归。
- **规避建议**：把"有没有第二个同名宿主"写进跑测前检查；判红前先看那条失败是不是 IOException。
- **后果不止假红（2026-10-26 实测）**：那次 `IOException` 让顶栏探针在 `SavePng` 处提前抛出，
  **跳过了方法末尾的 `vm.Deactivate()`** → 静态 `ScadaEditHistory.Changed` 上留下一个死线程 VM →
  后续 `RunSoundAlarmChecks` 压栈直接崩进程（退出码 `-532462766`）。
  同一场事故的完整分析见 `docs/code-changes/2026-10-26-撤销栈跨线程（审查与修复）.md`。
- **相关命令 / 参数**：`Get-Process ScadaChecks -ErrorAction SilentlyContinue | Select-Object Id,StartTime`

#### C-09 日志文件被运行中的宿主独占，读不了  [✅]  最近：2026-10-26  复发：1 次
- **现象**：`[IO.File]::ReadAllLines($log)` 抛 `IOException: 文件正由另一进程使用`，而宿主明明还在正常写日志。
- **触发场景 / 复现**：宿主运行中直接读 `VisionMaster\bin\...\Logs\<日期>.log`。
- **原因**：日志写入端持有独占句柄，`ReadAllLines` 默认只申请 `FileShare.Read`。
- **解决办法**：显式按 `ReadWrite` 共享打开：
  ```powershell
  $fs=[IO.File]::Open($log,'Open','Read','ReadWrite'); $sr=New-Object IO.StreamReader($fs,[Text.Encoding]::UTF8)
  $text=$sr.ReadToEnd(); $sr.Close(); $fs.Close()
  ```
- **规避建议**：运行中判"日志里有没有某条错误"一律走这条；关掉宿主后再用 `ReadAllLines`。

#### C-10 清理代码写在正常路径末尾 → 中途抛异常就漏  [✅]  最近：2026-10-26  复发：1 次
- **现象**：一条检查中途抛异常后，进程"看起来只是红了一条"，但后面另一条检查莫名崩掉
  （`InvalidOperationException: 调用线程无法访问此对象`），两者之间看不出关系。
- **触发场景 / 复现**：`RunScadaEditorToolbarChecks` 末尾有 `vm.Deactivate()`；当它在更早的
  `SavePng` 处抛 `IOException` 时，退订被跳过 → 静态事件上留下一个"一次性 STA 线程上建好的 VM" →
  之后任何线程压栈都会回调到死线程的 UI 对象上。
- **原因**：清理/退订写在方法末尾，只有正常路径会走到。
- **解决办法**：把清理放进 `finally`。本次改法：核心方法通过回调把 VM 交出去，线程包装器
  `finally { editor?.Deactivate(); }`；或在方法内 `try { ... } finally { vm.Deactivate(); }`。
- **规避建议**：凡是"订阅了静态/单例事件"的临时对象，退订一律 `finally`；
  看到"红了一条之后另一条莫名崩"，先查前一条是不是漏跑了清理。
- **相关命令 / 参数**：`Select-String -Path 'ScadaChecks\*.cs' -Pattern 'Activate\(\)' -Context 0,40`

#### C-11 另一会话把工程改到编译不通过 → 临时探针改独立工程引已构建 DLL  [✅]  最近：2026-10-26  复发：1 次
- **现象**：想加一条临时探针，但 `dotnet build ScadaChecks` 报的是**别人**的错
  （本次：`CS0738 ProjectAssetsViewModel 未实现 IDialogAware.RequestClose`、`CS0246 DebugModeService`），
  `--no-dependencies` 也不行（ScadaChecks 源码已引用对方新加的类型）。
- **触发场景 / 复现**：多会话同时改同一仓库，对方处于"改了一半"的状态。
- **原因**：工程引用链把你和对方的半成品绑在一起。
- **解决办法**：把探针做成**独立 `net9.0-windows` 工程**，用 `HintPath` 直接引
  `VisionMaster\bin\Debug\net9.0-windows\` 里**上一次成功构建**的 DLL（VisionMaster / VM.Scada /
  VM.Core / VM.Scada.Controls / UI / Prism* / Newtonsoft.Json），`Private=true` 让它们进输出目录。
  探针跑完即删，仓库一行不动。
- **规避建议**：先看 `Get-Item` 那些源文件的 mtime，判断是不是对方刚改的；不要为了让自己的构建过而去改对方的文件。
- **相关命令 / 参数**：
  ```xml
  <Reference Include="VisionMaster">
    <HintPath>D:\C#\VM\VisionMaster\bin\Debug\net9.0-windows\VisionMaster.dll</HintPath>
    <Private>true</Private>
  </Reference>
  ```

#### C-12 两进程同时编同一 WPF 工程 → `RG1000`「同一 BAML 键重复」  [✅]  最近：2026-10-26  复发：1 次
- **现象**：`dotnet build VisionMaster` 报 `error RG1000: 未知的生成错误"An item with the same key has already been added. Key: views/dialogviews/globalvariableview.baml"`，指向的文件根本没改过。
- **触发场景 / 复现**：另一会话正在构建/验收同一个工程，两个 MSBuild 同时进同一个 `obj`。
- **原因**：WPF 的 `_wpftmp` 工程与主工程共用 `obj\Debug\net9.0-windows`，BAML 资源被枚举两次。
- **解决办法**：确认没有其它构建在跑，清掉该 TFM 的中间目录后重建：
  `Remove-Item -Recurse -Force VisionMaster\obj\Debug\net9.0-windows`。
- **规避建议**：构建前先看有没有别人的 `dotnet build`（`Get-CimInstance Win32_Process` 查 CommandLine）；只清该 TFM 目录，别删整个 `obj`。
- **相关命令 / 参数**：`dotnet build VisionMaster\VisionMaster.csproj -m:1 -nodeReuse:false`

#### C-13 运行中的 VisionMaster 锁 `Modules\` → 冒烟宿主构建被连带挡住  [✅]  最近：2026-10-26  复发：2 次
- **现象**：构建 `UIThemeSmokeTest` 报 `MSB3027/MSB3021`，锁的是 `Modules\Plugin.Calibration.dll`、`Modules\Plugin.ImageAcquisition.dll`。
- **触发场景 / 复现**：另一会话的 `VisionMaster.exe` 正在跑；`UIThemeSmokeTest` 引用 VisionMaster → 连带重建插件 → `Plugins\Directory.Build.targets` 投递到 `Modules\`。
- **原因**：运行中的宿主加载着插件 DLL，复制目标被锁。
- **解决办法**：等实例退出再完整构建；急着验证冒烟**本身**时用
  `-p:BuildProjectReferences=false` 只编冒烟宿主、复用已构建好的产品 DLL。
- **规避建议**：`-p:BuildProjectReferences=false` **不是 gate 口径**（没重建产品与插件），收尾仍要等实例退出后跑 `run_gates.ps1`；不要为了构建去 kill 别人的验收实例（见 A-02/A-04）。
- **相关命令 / 参数**：`dotnet build UIThemeSmokeTest\UIThemeSmokeTest.csproj -p:BuildProjectReferences=false`
- **2026-10-26 补充（硬编码 copy 不认 ModulesDir）**：`Plugin.CSharpScript.csproj` 与
  `Plugin.ImageScript.csproj` 里是 `copy ... ..\Modules\` 的 `Exec`，**不认**
  `-p:ModulesDir=<temp>`。`run_gates.ps1` 构建 `FlowCanvasChecks` 时会连带构建这两个插件，
  宿主在跑就报 `MSB3073`，锁的是 `Modules\Microsoft.CodeAnalysis*.dll` /
  `Modules\hdevenginedotnet.dll`。**解法**：宿主退出后跑完整 gate；急着验已构建宿主时用
  `run_gates.ps1 -Only FlowCanvasChecks,HelpProbe -SkipBuild`（本轮实测 3704/0），不要为了
  构建去 kill 用户实例。

#### C-14 `UIThemeSmokeTest` 日志是 GBK(936) → 按 UTF-8 回读看不到 `[FAIL]`  [✅]  最近：2026-10-26  复发：1 次
- **现象**：日志里中文全是乱码，`grep '\[FAIL\]'` / `失败` 一条都不命中，看起来像全绿。
- **触发场景 / 复现**：宿主没设 `Console.OutputEncoding`，重定向后按控制台代码页 936 落盘。
- **原因**：与 `ScadaChecks`（首行显式设 UTF-8）编码不同，两套宿主不能用同一套回读逻辑。
- **解决办法**：回读用 `[Text.Encoding]::GetEncoding(936)`；或只判 `[PASS]` 行与退出码。
- **规避建议**：读任何断言日志前先确认编码；"没有失败行"不等于"没有失败"（同 C-01 的教训）。
- **相关命令 / 参数**：`[IO.File]::ReadAllText($p, [Text.Encoding]::GetEncoding(936))`

#### C-15 并行会话改写断言宿主 → 自己加的断言调用被静默覆盖  [✅]  最近：2026-10-26  复发：1 次
- **现象**：构建 EXIT=0，但日志里一条自己的 `[DM]` 段都没有。
- **触发场景 / 复现**：另一会话整文件重写 `ScadaChecks/Program.cs`，把插入的 `DebugModeChecks();` 覆盖掉。
- **原因**：两边都在改同一个 `Main`，后写者胜。
- **解决办法**：重新插入后重跑；判据是**日志里有没有自己的段**，不是构建退出码。
- **规避建议**：加断言后立刻跑一次并留日志；发现缺失先 `rg` 自己的调用是否还在 + 看文件 mtime（与 C-11 同族，但这次构建是绿的，更容易漏）。
- **相关命令 / 参数**：`rg -n "DebugModeChecks" ScadaChecks\Program.cs`

#### C-16 插件属性 setter 自动播种默认 ROI → 探针手工加的 ROI 没参与  [✅]  最近：2026-10-26  复发：1 次
- **现象**：探针设 `MeasureKind=CircleDiameter` 后 `CaliperRegions.Add(自己的圆)`，插件却用默认圆心/半径（实测 `NormalizedParameters` 里 radius=120，自己的 100 没进去），报“第 1 把圆周卡尺未检出边缘”。
- **触发场景 / 复现**：`CaliperMeasurePlugin.MeasureKind` setter 调 `EnsureRequiredRegions()` → `AddDefaultRegions()`，默认区先进入集合；探针再 Add 自己的，`NormalizedParameters` 取前 N 个默认区。
- **原因**：属性 setter 有副作用（UI 便利：切类型自动补搜索区），不是纯赋值。
- **解决办法**：设完类型后 `CaliperRegions.Clear()` 再手工加；或走插件自己的画布/区域编辑入口。
- **规避建议**：任何“设属性后手工摆配置集合”的探针，先查 setter 是否会自动补默认项；用反射调 `NormalizedParameters()` 打印实际参数核对，不要只看自己 Add 的值。
- **相关命令 / 参数**：
  ```csharp
  p.MeasureKind = MeasureKind.CircleDiameter;
  p.CaliperRegions.Clear();
  p.CaliperRegions.Add(new CaliperRegion { ShapeType = DrawShapeType.Circle, Params = new[] { 240, 320, 100, 0, 0 } });
  ```

### D. 路径与权限
#### D-01 相对路径按进程当前目录解析  [✅]  最近：2026-10-25  复发：2 次
- **现象**：`[IO.File]::ReadAllLines('docs\x.md')` 报找不到，文件确实存在。
- **触发场景 / 复现**：脚本从 `bin` 目录运行，或 PS 的 cwd 不在仓库根（本仓曾解析到 `.Plugins\...`）。
- **原因**：相对路径按进程 cwd 解析，不按脚本所在目录。
- **解决办法**：一律用绝对路径；脚本用 `$PSScriptRoot` / `$RepoRoot` 拼绝对路径。
- **规避建议**：所有文件操作传绝对路径，尤其是断言宿主与探针。

#### D-02 `File.Replace` 在受限环境被拒  [✅]  最近：2026-10-25  复发：2 次
- **现象**：`UnauthorizedAccessException` 带崩断言宿主；替换式写文件失败。
- **触发场景 / 复现**：受限环境/目标文件被占用时调用 `File.Replace`（R180）。
- **原因**：`ReplaceFile` 要求能删除目标文件；受限环境只给写权限。
- **解决办法**：同时 catch `IOException` + `UnauthorizedAccessException`，回落
  `File.Copy(tmp, dest, overwrite: true)`。
- **规避建议**：写配置类文件用"临时文件 + Copy 覆盖"，不要只 catch `IOException`。

#### D-03 长路径（MAX_PATH）  [✅]  最近：2026-10-25  复发：1 次
- **现象**：写盘阶段抛"路径过长"，用户只看到系统异常。
- **触发场景 / 复现**：方案名/保存位置过长（`> 240` 字符，扣 `.tmp` 余量）。
- **原因**：`MAX_PATH 260` 限制。
- **解决办法**：保存前校验长度并明确拒绝（`SolutionPaths.TryValidateSaveTarget`）。
- **规避建议**：方案名短、保存位置浅；测试夹具放 `artifacts\` 而不是深层临时目录。

### E. 配置与测试数据

#### E-01 Newtonsoft `PopulateObject` 对 `List<T>` 是追加  [✅]  最近：2026-10-25  复发：1 次
- **现象**：方案清单每次启动翻倍（2048→4096→8192→16384→32768→65536），
  `AppConfig.json` 涨到 27.4 MB；工具箱收藏 2,097,152 条（只有 1 个不同 id）。
- **触发场景 / 复现**：`AppSettingsService.Load()` 用 `PopulateObject` 原地填充；
  启动链上 Load 至少两次（构造器 + `ConfigCheck`）。
- **原因**：Newtonsoft 对已存在的 `List<T>` 默认**追加**而不是替换（实测 1→3→5）。
- **解决办法**：集合属性走 `ObjectCreationHandling.Replace` 的 ContractResolver；对象属性仍
  Populate（保住 `ScadaWeb` 引用身份）。
- **规避建议**：断言必须"同一实例连续 Load 两次"；`new` 两个实例永远绿，测不出这条。
- **相关命令 / 参数**：`Core\AppSettingsService.cs`、`ScadaChecks\Program.cs` 的
  `AppSettingsLoadIdempotencyChecks`。

#### E-02 备份还原覆盖当前配置  [⚠️]  最近：2026-10-25  复发：1 次
- **现象**：清理成 1 条的 `AppConfig.json` 几分钟后又变回 16384 条；与
  `AppConfig.json.bak-smartline` 的 SHA-256 完全相同。
- **触发场景 / 复现**：有人/某脚本把宿主 bin 里的 `.bak-*` 原样拷回。
- **原因**：备份文件保留旧内容；`Copy-Item` 覆盖当前文件并保留旧 mtime，表面看不出刚被覆盖。
- **解决办法**：清理前确认没有还原脚本/手工还原；清理后立刻复核条数；必要时先关宿主。
- **规避建议**：改配置前备份到 `artifacts\backup\<日期>-<主题>\`，不要拿宿主 bin 里的
  `.bak-*` 当工作副本；用 `Get-FileHash` 判断"是不是同一份"。
- **相关命令 / 参数**：`Get-FileHash <a>,<b> -Algorithm SHA256`

#### E-03 测试夹具写进方案根（`合成颜色验证.vms`）  [⚠️]  最近：2026-10-25  复发：每次跑闸
- **现象**：`解决方案\` 根下永远有一个平铺 `.vms`，看起来像"生成器没统一"。
- **触发场景 / 复现**：每次跑 `FlowCanvasChecks`（`ColorSyntheticChecks`）。
- **原因**：该 Check 把夹具落盘到方案根，方便手工在软件里打开。
- **解决办法**：已知边界，保留；它不是生成器产物。
- **规避建议**：不要把根下这个文件当"生成器回归"；要清根需改该 Check 的落点
  （会失去"可直接打开"的便利）。

### F. 端口与网络

#### F-01 端口占用（19000 / 19100 / 8080）  [✅]  最近：2026-10-25  复发：2 次
- **现象**：宿主/探针启动失败；HTTP 服务起不来；断言 `S4 HttpImageServer.Start()` 红。
- **触发场景 / 复现**：宿主或上次残留进程仍占端口。
- **原因**：端口独占（R176）。
- **解决办法**：`netstat -ano | findstr ":19000 :19100 :8080"` 找 PID；关掉占用者。
- **规避建议**：跑真 exe 前检查三个端口；不要用"换个端口再试"掩盖残留进程。
- **相关命令 / 参数**：`netstat -ano | findstr ":19000 :19100 :8080"`

#### F-02 残留进程树导致 gate 挂死  [✅]  最近：2026-10-25  复发：1 次
- **现象**：`run_gates.ps1` 空转、无子进程、无日志（实测 11 分钟）。
- **触发场景 / 复现**：被测进程拉起后代进程，而用 `Start-Process -Wait` 等整棵树。
- **原因**：同 C-02。
- **解决办法**：不用 `Start-Process`；有界等待；超时判红（`exit 124`）。
- **规避建议**：gate 里任何"等进程结束"的地方都必须有超时。

### G. HALCON 位姿与标定对拍

#### G-01 HALCON `create_pose` 旋转单位 / `'gba'` 顺序与插件不一致  [✅]  最近：2026-10-26  复发：1 次
- **现象**：手眼对拍插件与 HALCON 差 71 mm；`find_calib_object` 估计的 B 旋转差 10~21°，但平移误差几乎为零。
- **触发场景 / 复现**：把插件 Mat4（角度为**度**）按**弧度**传给 `create_pose`；或把插件 ZYX 欧拉角直接塞给 `'gba'`。
- **原因**：`create_pose` 的 RotX/RotY/RotZ 单位是**度**；HALCON `'gba'` 实际矩阵是 `Rx·Ry·Rz`，不是插件 `EulerZyxDegToMat3` 的 `Rz·Ry·Rx`。
- **解决办法**：不要手工拆欧拉角。把插件的 3×4 矩阵（平移 mm→m）拼成 12 元 `hom_mat3d`，调 `hom_mat3d_to_pose` 得规范 pose；`PoseToHomMat3d` 可精确还原。
- **规避建议**：对拍前先做 `Mat4 → pose → Mat4` 往返自证，旋转/平移误差必须为 0；日志里同时打印 B 的 rot/trans 误差，旋转误差到 10° 量级先查单位/顺序，不要先怀疑求解器。
- **相关命令 / 参数**：
  ```csharp
  HOperatorSet.HomMat3dToPose(new HTuple(h12), out HTuple pose); // h12 平移用米
  ```

#### G-02 `HPose.RawData` 是 mixed 元组 → `#8480` / `#1304`  [✅]  最近：2026-10-26  复发：1 次
- **现象**：`set_calib_data('tool', …, 'tool_in_base_pose', pose.RawData)` 后 `calibrate_hand_eye` 报 `#8480 Not enough calibration object poses`；`set_calib_data('calib_obj_pose', [0,obs], 'pose', pose.RawData)` 报 `#1304 Wrong value of control parameter`。
- **触发场景 / 复现**：把 `HPose.RawData` 直接当 `DataValue` 传给 `set_calib_data` / `find_calib_object`。
- **原因**：`HPose.RawData` 是 `HTupleMixed`，带 `"Rp+T"`/`"gba"`/`"point"` 字符串；这些算子只接受规范 7 元组/pose。
- **解决办法**：用 `create_pose` 或 `hom_mat3d_to_pose` 产生规范 `HTuple`；`HPose` 只传给 `sim_caltab` 这类明确接受 `HPose` 的算子。
- **规避建议**：看到 `#8480` 先查工具位姿是不是用 mixed 元组写的；看到 `#1304` 先查 `DataValue` 的类型，不要先怀疑 ItemIdx。
- **相关命令 / 参数**：
  ```csharp
  HOperatorSet.CreatePose(..., out HTuple pose);   // 规范 pose
  HOperatorSet.HomMat3dToPose(hom, out HTuple pose);
  ```

#### G-03 `find_calib_object` 23.05 没有 InitialPose / 校准前读 `calib_obj_pose` 报 `#8451`  [✅]  最近：2026-10-26  复发：1 次
- **现象**：给 `find_calib_object` 传 pose 当第 5 参报 `#1205 Wrong type of control parameter 5`；校准前 `get_calib_data('calib_obj_pose', …)` 报 `#8451 Model not optimized yet`。
- **触发场景 / 复现**：按记忆以为 `find_calib_object` 有 `InitialPose`；或想在校准前读优化后的板位姿。
- **原因**：23.05 的签名是 `find_calib_object(Image, CalibDataID, CameraIdx, CalibObjIdx, CalibObjPoseIdx, GenParamName, GenParamValue)`，第 5 参是 `GenParamName`；`calib_obj_pose` 是优化后的数据，校准前不存在。
- **解决办法**：需要初值时用 `find_marks_and_pose` + `set_calib_data_observ_points`；校准前取观测位姿用 `get_calib_data_observ_pose`。
- **规避建议**：查本机 `%HALCONROOT%\doc\html\reference\operators\find_calib_object.html` 的签名再写调用，不要按记忆传参。
- **相关命令 / 参数**：
  ```csharp
  HPose observed = calib.GetCalibDataObservPose(0, 0, obs);
  ```

---

## 3. 经验迭代（跨轮次）

| 日期 | 本轮用到的旧条目 | 新发现 | 优化 / 合并 |
|---|---|---|---|
| 2026-10-25（方案清单翻倍） | C-05（中文检索）、B-01 | E-01、E-02 | 用"同一实例连续 Load 两次"替代"new 两个实例"的假绿写法 |
| 2026-10-25（生成器统一文件夹） | A-01、B-01、C-01、E-01 | C-06、A-04 | 把"先关宿主再构建"固化成 SOP 第 1.2/1.3 步 |
| 2026-10-26（建账） | 全部 | — | 把两轮经验归并为 A–F 六类；SOP 首版；建立快速索引与合并规则 |
| 2026-10-26（SCADA 发糊实测） | A-01、A-03、C-05 | A-06、C-07 | 真机跑 GUI 时"进程消失"先判外部结束；点按钮前先裁图量框；DPI 结论必须实测 `GetWindowDpiAwarenessContext`，不能只看 manifest |
| 2026-10-26（修跑马灯冻结） | A-01、B-01、B-02 | C-08、C-09（并入 A-01 的并发构建变体） | 改产品源码前先查有没有别的会话在构建/跑宿主；跑断言前先确认没有第二个同名进程；读运行中日志走 `FileShare.ReadWrite` |
| 2026-10-26（修撤销栈跨线程） | C-08、B-02 | C-10（C-08 补"假红连锁成真崩"） | 订阅静态事件的临时对象一律 `finally` 退订；断言里复现跨线程要 `GC.KeepAlive` 按住对象 |
| 2026-10-26（手眼 HALCON 对拍） | A-01、B-01、C-01 | G-01、G-02、G-03 | 对拍前先做 pose 往返自证；`HPose.RawData` 不能直接进 `set_calib_data`；`find_calib_object` 23.05 没有 `InitialPose` |
| 2026-10-26（几何/匹配 HALCON 对拍） | A-01、B-01、C-13 | — | `Plugin.CSharpScript` / `Plugin.ImageScript` 的硬编码 copy 不认 `ModulesDir`；宿主在跑时用 `-SkipBuild` 跑已构建宿主 |
| 2026-10-26（卡尺 HALCON 对拍） | C-11、C-13、C-15 | C-16 | 插件属性 setter 会自动播种默认 ROI；探针先清空再手工加，并用 `NormalizedParameters` 反射核对实际参数 |

> 迭代口径：**下一次同类测试优先复用本表"已用到"的条目**；如果某条目这次没起作用（换了环境/版本），
> 在条目里标 🗑️ 并写原因，而不是默默绕过。

## 4. 最近更新

| 日期 | 变更 | 新增 | 合并 |
|---|---|---|---|
| 2026-10-26 | 建账：SOP 首版 + A–F 六类坑记录 + 快速索引 + 合并规则 | A-01…A-05、B-01…B-04、C-01…C-06、D-01…D-03、E-01…E-03、F-01…F-02 | — |
| 2026-10-26 | SCADA 组态画面发糊真机实测：新增两条（宿主被并发构建结束、截图坐标点错邻居）；确认 DPI 判断必须实测 | A-06、C-07 | — |
| 2026-10-26 | 修报警条跑马灯冻结：A-01 补"并发构建锁 obj DLL / 锁宿主 exe"变体；新增两条（双实例跑断言、日志独占） | C-08、C-09 | A-01（补变体，不新增 ID） |
| 2026-10-26 | 修撤销栈跨线程：新增"清理代码漏跑"一条；C-08 补"假红连锁成真崩"的后果 | C-10 | C-08（补后果，不新增 ID） |
| 2026-10-26 | 验证模板库事件：新增"对方改坏工程时的探针做法" | C-11 | — |
| 2026-10-26 | 手眼 HALCON 对拍：新增 HALCON pose 单位/顺序、mixed 元组、`find_calib_object` 签名三条 | G-01、G-02、G-03 | — |
| 2026-10-26 | 几何/匹配 HALCON 对拍：C-13 补“硬编码 copy 不认 ModulesDir”变体 | — | C-13（补变体） |
| 2026-10-26 | 卡尺 HALCON 对拍：新增“属性 setter 自动播种默认 ROI”一条 | C-16 | — |

**下次更新时**：只改「0. 快速索引」「对应条目」「3. 经验迭代」「4. 最近更新」四处；
新增条目按模板补齐五段；汇报里写「台账已更新：新增 N 条 / 合并 M 条」。
