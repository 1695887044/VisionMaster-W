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
| 构建报 `MSB3021`/`MSB3027`/`CS2012`、`文件被 "VisionMaster (PID)" 或断言宿主锁定` | A-01 | ✅ |
| 启动第二个实例后前一个被踢 / 新实例直接退出 | A-02 | ✅ |
| 跑完真 exe 后用户布局变了 / 停靠面板位置丢了 | A-03 | ✅ |
| `taskkill` 后马上又出现新 PID，构建仍被锁 | A-04 | ⚠️ |
| 运行中的宿主突然消失，无退出日志、无崩溃事件 | A-06 | ✅ |
| 按截图坐标点按钮点到相邻按钮（"点了没反应"） | C-07 | ✅ |
| 16px 收起胶囊点中心没反应 / 只有左半区可点 | C-07 | ✅ |
| `audit_artifacts` 报 `STALE_DLL`，但插件不是本批改的 | B-04 | ✅ |
| 右侧同一面板出现两条竖排标题 / 库自动隐藏页签与自定义胶囊重叠 | C-33 | ✅ |
| 两个断言宿主同时跑，证据 PNG / gate 原始日志被锁、出现假红 | C-08 | ✅ |
| 宿主运行中读日志抛 IOException / `Get-Item` 拿到的长度与时间戳是陈旧的 | C-09 | ✅ |
| 程序"界面卡死"但 `Process.Responding` 仍是 True / 主界面点什么都没反应 | C-40 | ✅ |
| `EnumChildWindows` 读 WPF 弹窗返回 0 个子窗口、读不到按钮文本 | C-40 | ✅ |
| 构建红在自己没碰过的文件里 / 报错文件 mtime 就在几分钟内 | C-41 | ✅ |
| 构建报 MSB3027/MSB3021「文件被 FlowCanvasChecks/xxx 锁定」 | C-42 | ✅ |
| 红了一条之后另一条莫名崩 / 清理代码没跑 | C-10 | ✅ |
| 想加探针，但构建报的是别人正在改的错 | C-11 | ✅ |
| 构建报 `RG1000`「同一 BAML 键重复」（指向没改过的文件） | C-12 | ✅ |
| 宿主运行抛「找不到资源 themes/controls/gridview.xaml」（构建却是绿的、产物偏小） | C-12 | ✅ |
| 构建冒烟宿主时锁 `Modules\Plugin.*.dll` | C-13 | ✅ |
| `UIThemeSmokeTest` 日志乱码、看不到 `[FAIL]` | C-14 | ✅ |
| 构建绿但自己加的断言一条没跑 | C-15 | ✅ |
| 插件属性 setter 自动播种默认 ROI → 探针手工加的 ROI 没参与 | C-16 | ✅ |
| 手工生成 `.vms`：模型不在 `TemplateRect`、加载后多三条骨架流程、连线端口名写反 | C-17 | ✅ |
| 形状匹配在散焦/低对比图上只出一小部分 / HALCON 例程用 `[0,-1]` 才全中 | C-18 | ✅ |
| 可形变匹配 `deformation_smoothness` 必须是整数 / 对拍参数必须同源 | C-19 | ✅ |
| 资产页引用碰巧命中 / 外部来源仍写模型 / 本地路径基准错 | C-24 | ✅ |
| 改完 `AppConfig.json` 几分钟后又变回旧内容 / 体积暴涨 | A-05、E-02 | ✅ / ⚠️ |
| `dotnet build` 报"生成失败 / 0 个错误"、退出码 1 | B-01 | ✅ |
| 改了断言但结果没变 / "全部通过"但新断言没跑 | B-02 | ✅ |
| 只想验 Core，构建却在 VisionMaster 复制阶段失败 | B-03 | ✅ |
| 改了插件源码，运行逻辑仍旧 | B-04 | ✅ |
| 宿主构建报 `CS0006 未能找到元数据文件 …\obj\…\ref\` / `MC3066 LogItem`（同时另开了构建） | B-05 | ✅ |
| 要给端口做绑定按钮 / 不知道有没有现成控件（别手搓 `LinkPathEvent`） | AGENTS.md §44 / R189 | ✅ |
| 日志中文乱码、统计正则永不命中、误报全绿 | C-01 | ✅ |
| PowerShell 重定向（`>`/`*>`）捕获宿主/探针输出 → 双重乱码、数字失真 | C-01 | ✅ |
| 测试空转十几分钟 / `ExitCode` 恒为 `$null` | C-02、F-02 | ✅ |
| 汇总全绿，实际一个宿主都没跑 | C-03 | ✅ |
| `.ps1` 报 `MissingEqualsInHashLiteral` / 中文常量乱码 | C-04 | ✅ |
| `findstr` 搜中文明明存在却搜不到 | C-05 | ✅ |
| 根目录多出 `$null`；`audit_root` 报 `FILE  $null` | C-06 | ⚠️ |
| 根目录多出乱码同名目录；`audit_root` 报 `DIR <中文>` 但白名单里有同名项 | C-43 | ⚠️ |
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
| `fft_image_inv` 只输出 byte，负值被截断（半波整流） | G-04 | ✅ |
| `gen_filter_mask` 23.05 只认 Laws 纹理，做不了低通/带通 | G-05 | ✅ |
| `ignore_polarity` 不是合法 metric → `#1306/#1309/#1312` | G-06 | ✅ |
| `vm-uia.ps1` 只看到 Tooltip / invoke 报 button not found | C-20 | ✅ |
| 原生 MessageBox / 文件夹对话框按钮点不动 | C-21 | ✅ |
| `SendKeys` / `AppActivate` 送不进目标窗口 | C-22 | ✅ |
| net48 探针启动后窗口不退出 / `run-probe` 挂住 | C-23 | ✅ |
| 断言宿主日志乱码、`PASS/FAIL` 统计零命中 | C-25 | ✅ |
| 坐标点击/按键送不进目标应用（远程桌面/前台被占） | C-26 | ✅ |
| 整跑流程后宿主进程不退、锁 bin | C-27 | ✅ |
| 断言宿主计时类用例在负载下抖动（两次红不同条） | C-28 | ✅ |
| 手算 PNG 头得到错误宽高 / 灰度图被 GDI+ 报成 32bppArgb | C-29 | ✅ |
| 截图"成功"但弹出菜单 / 下拉 / 对话框不在图里 | C-30 | ✅ |
| 按 pid 找不到窗口；`Start-Process -PassThru` 的 pid 不是窗口 pid | C-31 | ✅ |
| 图标按钮在 UIA 树里全叫 `?`，几个按钮分不出来 | C-32 | ✅ |
| 给控件设了 `AutomationId`，UIA 树里却找不到它 | C-34 | ✅ |
| 用 `SetValue` 写输入框后界面"没反应"（业务没收到编辑事件） | C-35 | ✅ |
| 报"启动成功"但窗口只有 520×360 / 无标题 | C-36 | ✅ |
| 启动后点哪都没反应（草稿恢复框挡着） | C-37 | ✅ |
| 按名字取第一个 ref，报 `available patterns: (none)` | C-38 | ✅ |
| 脚本明明拒绝了，`echo EXIT=%ERRORLEVEL%` 却打印 0 | C-39 | ✅ |
| `powershell -File x.ps1 ; echo ...` 报「找不到接受实际参数」 | C-39 | ✅ |
| `dotnet build > log & echo EXIT=%ERRORLEVEL%` 报 0 而日志里有 error | C-39 | ✅ |
| `timeout /t N` 报「不支持输入重新定向」→ 等待变成立即返回 | C-44 | ✅ |
| 后台轮询日志几分钟是空的 / 像卡死（其实是输出缓冲） | C-45 | ✅ |
| 清理 `artifacts\build\` 把别的会话的隔离构建产物一起删了 | C-50 | ⚠️ |
| `gray_histo` 对 uint2 少箱 → 直方图指标全 0 / Success=false | G-07 | ✅ |
| `scale_image` 在 byte 上截顶 → uint2 夹具做不出 65535 | G-08 | ✅ |
| `projective_trans_region` #1302 / `projective_trans_point_2d` 少参 | G-09 | ✅ |
| 断言夹具在 lambda 里 `using` → 执行前已释放（假“未连接”） | E-06 | ✅ |
| RANSAC 用例的错配点整体一致 → 被当成内点 | E-07 | ✅ |
| 夹具保存自动转「另存为」 | D-04 | ✅ |
| `git` 报"不是内部或外部命令"（本机无 git PATH） | D-05 | ✅ |
| 探针突然大量失败（UI 改版后） | E-04 | ⚠️ |
| 探针截图往仓库根旧目录写（迁移遗漏） | E-05 | ✅ |
| 探针夹具在 lambda 里 `using` → Execute 前已释放（假"未连接"） | E-06 | ✅ |
| RANSAC 测试"错配点"整体一致 → 被当成内点（假红） | E-07 | ✅ |
| 想造"慢查询"验证加载骨架：WAL 下写锁挡不住读 | E-08 | ✅ |
| 源码已改但断言仍红 / `STALE_DLL` → 测的是 Modules 旧 DLL | E-09 | ✅ |
| MotionSim 端口 19510 被旧 LineSim 占 | F-03 | ⚠️ |
| `file://` 路径含 `#`（`D:\C#\VM`）或中文 → 截图 `NO FILE` / 打开错页 | H-01 | ✅ |
| 无头 Edge 批量截图全部 756×488 / 布局图比预期窄一圈 | H-02 | ⚠️ |
| PowerShell 变量名 `$l`/`$L` 大小写冲突，列表莫名只剩 1 个元素 | C-42 | ✅ |
| 弹出层开着时 UIA Invoke 主窗关闭按钮无效 / 进程不退 | C-46 | ✅ |
| 浮窗/列表行在 UIA 树里找不到（虚拟化，屏幕外未实例化） | C-47 | ✅ |
| `[string]` 参数与 `$map` 同名 → JSON 解析结果变字符串，只读出 `Length` | C-48 | ✅ |
| 只复制 `.vms` 到别处 → 图像/资产读不到（方案内相对路径） | C-49 | ✅ |
| 弹窗断言全绿但进程 `-532462766` 退出（Prism 关窗回调被伪造） | C-51 | ✅ |
| XAML 注释里写 `---- 标题 ----` → `MC3000` 构建失败 | C-52 | ✅ |
| 右键菜单在 UIA 顶层窗口里看不到 / 跨调用点不到菜单项 | C-53 | ✅ |

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

- **`vm-tools` MCP 已屏蔽（AGENTS.md R186）**：本项目不注册、不调用。工作区 `.zcode\config.json` 已把 `mcp.servers["vm-tools"]` 置 `enabled:false`，`tools\oneoff\register-vm-mcp.mjs` 默认拒绝（需 `--force`）。需要 GUI/UIA 能力时按 §1.1 走探针/断言宿主，或交回人工。

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

#### A-01 宿主运行时构建在复制阶段失败（DLL 被锁）  [✅]  最近：2026-10-28  复发：11 次
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
  ③ `MSB3026`（重试 10 次）`The process cannot access the file
  'FlowCanvasChecks\bin\Debug\net9.0-windows\Plugin.*.dll' … 文件被"FlowCanvasChecks (5304)"锁定`
  ——残留的断言宿主锁着自己的 bin 副本；实例退出后 `run_gates.ps1 -Only FlowCanvasChecks`
  复跑 **3724/0** 即绿（不是代码错）。
  **判据**：构建前查 `Get-Process dotnet,MSBuild,ScadaChecks,VisionMaster,FlowCanvasChecks`
  （`VBCSCompiler` 是常驻编译器服务，不算构建）；有就等它退，不要反复重试，也不要 `-F` 杀别人的跑测。
- **2026-10-27 补充（FlowCanvasChecks 锁 `Modules\Plugin.Camera.Sim.dll`）**：另一会话的
  `FlowCanvasChecks (PID 34388)` 在跑时，`CameraSimProbe` 构建报 `MSB3027/MSB3021`；
  等该进程退出后复跑成功（19/1，唯一失败是产品曝光问题）。判据同 A-01：先查宿主进程、等它退、不杀。
- **2026-10-27 补充（CS2012 抢 obj 输出）**：并发构建窗口里 `RecipeChecks` / `FlowCanvasChecks` 两个宿主
  **同时** BUILD FAILED：`CSC : error CS2012: 无法打开"…\obj\Debug\net9.0-windows\…dll"进行写入 … 文件可能被
  '.NET Host' (PID) 锁定`。根因同①（对方在编同一批工程）。**判据**：两个不相干宿主一起挂 → 先查
  `Get-CimInstance Win32_Process … CommandLine -like '*build*'`；**处理**：等它退（本次归零后复跑
  RecipeChecks 138/0、UIThemeSmokeTest / HelpProbe 全绿），不杀对方进程。
- **2026-10-27 补充（`MSB3061` 删不掉 obj 中间 DLL）**：并发构建窗口里 `ScadaChecks` 构建报
  `MSB3061: 无法删除文件"obj\Debug\net9.0-windows\VM.Scada.Controls.dll" … 正由另一进程使用`。
  与 CS2012 同源（对方在编同一批工程），只是发生在"清理旧产物"而不是"写新产物"这一步。
  **处理**：等对方构建退出后原样复跑即绿（本次第二次尝试 0 错误）；不要删 obj、不要 `-t:Rebuild`。
- **2026-10-27 补充（`VBCSCompiler` 不是构建）**：`Get-Process dotnet` 里常驻的
  `dotnet exec ...\Roslyn\bincore\VBCSCompiler.dll` 是 Roslyn 编译器服务，**首次构建后就一直在**，
  把它当"有构建在跑"会永远等下去。**判据**：读命令行（`NtQueryInformationProcess` class 60 /
  `Get-CimInstance Win32_Process CommandLine`）过滤 `VBCSCompiler`；`vm-mcp` 的 `vm_status`
  已把它单列成 `roslyn-server` 行。真正的构建是 `MSBuild.exe` 或跑 `MSBuild.dll` 的 `dotnet`。
- **2026-10-28 补充（编译成功、只有"投递到 `Modules\`"失败 → `audit_artifacts` 报 STALE_DLL）**：
  用户开着 `VisionMaster (PID 26876)` 且已**加载过判定表插件**，此时重建 `Plugin.JudgeTable`：
  `CSC` 正常、`bin\Debug\Plugin.JudgeTable.dll` 已刷新，但 `Directory.Build.targets` 的投递步骤报
  `MSB3027/MSB3021 … Modules\Plugin.JudgeTable.dll 文件被"VisionMaster (26876)"锁定`，
  紧接着 `audit_artifacts` 报 `STALE_DLL Plugin.JudgeTable dll=22:41:58 src=22:45:53`。
  **判据**：`STALE_DLL` 的 `src` 若晚于"投递失败的那次构建"→ 先查 `Get-Process VisionMaster`，
  不要怀疑产物闸本身。**处理**：关宿主后
  `dotnet build <插件 csproj> -m:1 -nodeReuse:false --no-incremental`（Help 页改过必须
  `--no-incremental`，R42：增量可能漏打包），再跑 `run_gates.ps1 -Only audit_artifacts`。
  同一轮 `DatabaseChecks` / `ScadaWebChecks` 的 BUILD FAILED 是同一把锁的连带（分别锁
  `Modules\Plugin.Database.dll` 与 `VisionMaster\bin\...\Web\VisionMaster.ScadaWeb.dll`），与改动无关。
  **同日复发一次（`Plugin.ResultUpload`）**：编译 0 个 CS 错误、`bin\Debug` 已刷新，只有
  `Modules\Plugin.ResultUpload.dll` 投递被 `VisionMaster (45544)` 挡住（2 个 MSB3021/MSB3027）。
  **不要杀用户正在用的实例**：先做完文档/约束类改动，等宿主关闭后原样复跑
  `dotnet build <插件> --no-incremental -m:1 -nodeReuse:false`，DLL 投递成功再跑 gate。
  **同日第二次（gate 跑到一半用户启动宿主，PID 31336）**：`run_gates.ps1` 已跑完审计与多数宿主，
  三个构建步（DatabaseChecks / FlowCanvasChecks / ScadaWebChecks）同时 `BUILD FAILED`。
  **判据**：多个不相干构建步同时红 + 日志含「文件被 VisionMaster (PID) 锁定」= 环境，不是代码；
  **处理**：`Get-Process VisionMaster` 确认已退，原样整轮复跑（本次即 16/16，FlowCanvasChecks 4343/0）。

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

#### B-00 断言宿主进程也会锁 `Modules\*.dll`（不只是 VisionMaster）  [✅]  最近：2026-10-28  复发：1 次
- **现象**：`VisionMaster` 已关闭，插件工程构建仍报 `MSB3027/MSB3021`，错误文本里锁文件的进程是
  `FlowCanvasChecks (33644)`；`audit_artifacts` 随即报 `STALE_DLL`。
- **触发场景 / 复现**：另一个会话/上一轮遗留的 `FlowCanvasChecks.exe`（全量跑要 1~2 分钟）还在跑，
  它把 `Modules\Plugin.JudgeTable.dll` 映射进内存。
- **原因**：任何加载过插件 DLL 的宿主进程都会持有文件锁，**不限** `VisionMaster`。
- **解决办法**：等它自己退出（全量跑完会退）；判据 `Get-Process -Id <pid>` 消失后再构建。
  **不要杀**——杀掉等于毁掉别人正在跑的一整轮断言。
- **规避建议**：构建前先看一眼 `Get-Process VisionMaster,FlowCanvasChecks,ScadaChecks,HelpProbe`；
  错误文本里"文件被 X 锁定"的 X 才是真正的锁持有者，别只盯着 VisionMaster。
- **相关命令 / 参数**：
  ```powershell
  Get-Process -Name VisionMaster,FlowCanvasChecks,ScadaChecks,HelpProbe -ErrorAction SilentlyContinue |
    Select-Object Id,ProcessName,StartTime,CPU
  ```

#### B-00b 隔离构建（`-p:BaseOutputPath`）之后，普通构建报 `MC3074 标记不存在`  [✅]  最近：2026-10-28  复发：1 次
- **现象**：为绕开 `Modules\` 锁做过一次 `-p:BaseOutputPath=artifacts\build\<批次>\` 的隔离构建，
  之后正常构建插件报 `MC3074: XML 命名空间"clr-namespace:Core.Controls;assembly=Core.Controls"中
  不存在标记"LinkableValueEditor"`；重试一次就绿了。
- **原因**：`BaseOutputPath` 把契约工程（`Core.Controls` 等）的 `obj\...\ref\*.dll` 也搬到了隔离目录，
  默认 `obj` 下的 ref 程序集缺失/过期；XAML 编译在依赖工程重建**之前**读的是旧的 ref。
- **解决办法**：先单独重建被搬走的契约工程（`dotnet build Shard\Core.Controls -m:1 -nodeReuse:false --no-incremental`），
  再重试插件构建。
- **规避建议**：隔离构建只用来"验证能编过"，不要与后续正常构建混用；混用后第一件事是重建契约工程。
  `-p:ModulesDir` 的相对路径还会按各工程目录解析（实测落到 `Plugins\<插件>\artifacts\...`），要传绝对路径。
- **相关命令 / 参数**：`dotnet build Plugins\Plugin.X -m:1 -nodeReuse:false --no-incremental`

#### B-01 `dotnet build` 多进程静默失败  [✅]  最近：2026-10-28  复发：多次
- **现象**：报"生成失败 / 0 个错误"、退出码 1；或看起来成功但产物没更新。
- **触发场景 / 复现**：本机默认 `dotnet build` / `dotnet restore`（多进程 MSBuild）。
- **原因**：本机多进程构建不稳定（R179）。
- **解决办法**：一律加 `-m:1 -nodeReuse:false`。
- **规避建议**：判据只看两件事：`0 个错误` **且** 退出码 0；不要只看其中一项。
- **相关命令 / 参数**：`dotnet build <工程>.csproj -m:1 -nodeReuse:false`
- **2026-10-28 补充（`run_gates.ps1` 的构建步也会瞬时红）**：全量跑 gate 时
  `DataPipelineChecks` 报 `BUILD FAILED`，但 `build-DataPipelineChecks.log` 末尾是
  「已成功生成 / 0 个错误」，全文没有任何 `error` 行——gate 只是看到构建进程退出码 1。
  **判据**：日志末尾 `0 个错误` + 无 `error` 行 = 不是代码问题。**处理**：`run_gates.ps1 -Only <Host>`
  单步复跑（本轮 97 pass / 0 fail 转绿）；**不要**为此改代码、清 `obj` 或去掉 `-m:1`。

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

#### B-04 插件源码改了没重建（Modules 仍是旧 DLL）  [✅]  最近：2026-10-27  复发：2 次
- **现象**：插件逻辑仍旧；`Modules\Plugin.X.dll` 的 mtime 比源码旧。
- **触发场景 / 复现**：只重建宿主，或只改插件源码没构建该插件工程。
- **原因**：`Modules\` 不由宿主带出（红线③）。
- **解决办法**：重建该插件工程；核 `DLL mtime ≥ 源码 mtime` 且 `Help` 内嵌资源数 = 磁盘 `.md` 数。
- **规避建议**：收尾跑 `tools\audit_artifacts.ps1`；改过 Help 文案用 `--no-incremental`。
- **相关命令 / 参数**：`tools\audit_artifacts.ps1`
- **2026-10-27 补充（并发会话改 Help → 本侧 gate 的 audit_artifacts 红）**：另一会话在 16:54
  改了 `Plugins\Plugin.ImageAlign\Help\*.md` 但尚未重建，本侧 17:01 的 `run_gates.ps1` 报
  `STALE_DLL Plugin.ImageAlign dll=16:47:49 src=16:54:41`，其余 13 步全绿。**判据**：先看
  `audit_artifacts` 报的插件是不是本批改的；不是就查该插件源码 mtime 与 `Modules\` DLL mtime，
  并看有没有别的会话在跑（`Get-Process FlowCanvasChecks,dotnet` / `run_gates.ps1` 命令行）。
  **处理**：等对方重建（本轮对方把 DLL 刷到 16:55:53）后**单独复跑 `audit_artifacts.ps1`** 得
  `RESULT: OK`；随后 `run_gates.ps1 -SkipBuild` 复跑 **14/14 绿**。不要为了消红去改/重建别人正在改的插件。
  **不要**把这条红算到本批代码头上。
- **2026-10-28 复现（改 Help 文案 → 增量构建漏打包）**：`Plugin.ResultUpload` 改了 4 页 `Help\*.md` 后
  只做增量构建，`audit_artifacts` 报 `STALE_DLL Plugin.ResultUpload dll=22:59:44 src=23:01:41
  newest=03-ports-and-parameters.md`。**判据**：`newest=<文件名>` 是 `.md` 就说明是内嵌资源项，
  增量构建把它判成不脏（R42）。**处理**：`dotnet build <插件>.csproj --no-incremental -m:1 -nodeReuse:false`，
  再核 `Help` 内嵌资源数 = 磁盘 `.md` 数（本轮 6/6）。**注意**：这次全量重建会连带重建所有依赖工程，
  别和 `run_gates.ps1` 同时跑（见 B-05）。

#### B-05 与 `run_gates.ps1` 并发跑 `dotnet build --no-incremental` → 宿主构建报 CS0006 / MC3066  [✅]  最近：2026-10-28  复发：1 次
- **现象**：gate 里某宿主 `BUILD FAILED`，`build-<Host>.log` 里是
  `CSC : error CS0006: 未能找到元数据文件 "…\obj\Debug\net9.0\ref\VM.Scada.dll"` /
  `…\ref\Core.Interfaces.dll`，以及 `UI.csproj` 报
  `MC3066: 类型引用无法找到名为"LogItem"的公共类型`。
- **触发场景 / 复现**：`run_gates.ps1` 正在跑宿主构建的同时，在另一个终端
  `dotnet build Plugins\Plugin.X\Plugin.X.csproj --no-incremental`（改过 Help 后的标准动作）。
- **原因**：`--no-incremental` 会清空并重建**所有依赖工程**的 `obj\…\ref\*.dll`，
  与 gate 里正在编译的宿主抢同一批中间产物；`ref` 目录被清空的瞬间对方就找不到元数据。
- **解决办法**：一次只跑一条构建链。要重建插件就先等 gate 结束（或先重建、再开 gate）。
- **规避建议**：判据是"日志里出现 `未能找到元数据文件 …\obj\…\ref\`"——这是并发撞车，
  不是代码问题。**复跑即可**；不要清 `obj`、不要改代码、不要去掉 `--no-incremental`。
- **相关命令 / 参数**：`dotnet build <工程>.csproj --no-incremental -m:1 -nodeReuse:false`

### C. 命令、编码与输出

#### C-01 子进程输出走管道 → 936 解码乱码 / 正则永不命中  [✅]  最近：2026-10-27  复发：多次
- **现象**：日志中文乱码（"合法 UTF-8 的乱码"）、统计正则永不命中、每个宿主回落打印"全部通过"。
- **触发场景 / 复现**：`& $exe 2>&1 | Out-String`（R183①）。
- **原因**：.NET 宿主输出 UTF-8，PowerShell 管道按控制台代码页（本机 936）解码。
- **解决办法**：原始字节直接落盘；回读严格 UTF-8 优先、失败回落 936。
- **规避建议**：不要用 `Out-String`/管道捕获被测进程输出；用 `[Diagnostics.Process]` + 文件。
- **相关命令 / 参数**：`StandardOutput.BaseStream.CopyToAsync($fs)`
- **2026-10-27 补充（PowerShell 重定向 `>`/`*>` 也是同一条路）**：`& run-probe.ps1 *> $log` 捕获探针输出后，
  日志是"UTF-8 字节被按 936 解码"的双重乱码（如 `鎿嶄綔鑰呬笅鎷夋潵鑷`），**PowerShell 自己回读也救不回**
  （已不是原始字节）；ASCII 标记（`[PASS]`/`RESULT:`）照样命中、数字看似对得上，中文 pattern 全不命中——
  比管道那一版更隐蔽。**解法**：验真一律 `cmd > 文件`（原样字节）或 `[Diagnostics.Process]`（SOP §1.4）。
  **判据**：日志落盘后先用 `rg` 搜一个中文关键词，搜得到才算这份日志可用。

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
- **2026-10-27 补充（`-Command` 里拼 JSON / 变量被转义吃掉）**：`powershell -Command "... \$p.Id ..."`
  经 cmd 一层后变成字面量 `\$p.Id`，报 `方法调用中缺少")"`；here-string 里再套 JSON 更是一堆终止符错误。
  **判据**：报错指向的字符在命令里根本不存在（多了反斜杠）。**解法**：一次性脚本一律
  `[IO.File]::WriteAllText` 落盘 → `powershell -File`（与 R2 同一条），JSON 用编辑工具写文件，
  不在 `-Command` 里拼。

#### C-05 `findstr` 用中文 pattern 搜 UTF-8 文件不命中  [✅]  最近：2026-10-25  复发：3 次
- **现象**：明明存在的行搜不到；据此误判"没有引用"。
- **触发场景 / 复现**：`findstr /c:"方案清单" "VisionMaster\bin\...\Logs\2026-10-25.log"`。
- **原因**：`findstr` 用当前代码页（936）解释 pattern 与文件，日志是 UTF-8。
- **解决办法**：用 PowerShell `[IO.File]::ReadAllLines($f,[Text.Encoding]::UTF8)` +
  `Where-Object` / `Select-String`；或改搜 ASCII 标记（数字、`RESULT:`、`Count=`、`WROTE`）。
- **规避建议**：中文检索不走 `findstr`；不确定编码时先按 ASCII 数字定位。

#### C-06 cmd 里 `> $null` 建出名为 `$null` 的文件  [⚠️]  最近：2026-10-28  复发：4 次
- **现象**：仓库根多一个 `$null` 文件；`tools\audit_root.ps1` 报 `FILE  $null`（有时名字显示为空）。
- **触发场景 / 复现**：在 `cmd.exe` 里写 `command > $null`（`$null` 是 PowerShell 概念）。
- **原因**：cmd 把 `$null` 当普通文件名，创建同名文件。
- **解决办法**：删除该文件；cmd 用 `> nul`，PowerShell 用 `> $null`。
- **规避建议**：按 shell 选空设备；`audit_root` 复跑确认（瞬时条目也可能自己消失）。
  **2026-10-28 复发**：文件 383 字节、内容是一段被重定向进去的 PowerShell 报错
  （`= Get-ChildItem ... CommandNotFoundException`），CreationTime 08:02、mtime 08:05 —— **不是当前会话产生的**。
  按 R166 移到 `artifacts\logs\20261028\stray-null-redirect-error.txt`（不删，留证据）。
  **新判据：审计报红先看 CreationTime + 内容再判断归属**，别默认是自己弄脏的，也别直接删别人的在途产物。
  **2026-10-28 本会话复发（第 4 次）**：`powershell -NoProfile -Command "... } 2>$null"` 的外层仍是 cmd——
  cmd 在把整串交给 PowerShell 之前就解析了 `2>$null`，在仓库根建出 **0 字节**的 `$null`；
  CreationTime 08:36 与本会话命令同秒。**0 字节 + 时间吻合 = 自己产生，直接删**；
  非空 + 时间不符 = 别人的在途产物，按上一段移到 `artifacts\logs\` 留证据。
  **根本规避：`2>$null` 只写在 `.ps1` 文件里或纯 PowerShell 会话内，不要放在 cmd 包装的 `-Command` 串里。**
- **相关命令 / 参数**：`Remove-Item -LiteralPath 'D:\C#\VM\$null' -Force`；
  留证据用 `[IO.File]::Move('D:\C#\VM\$null', 'artifacts\logs\<yyyyMMdd>\stray-null-redirect-error.txt')`

#### C-07 按截图坐标点按钮点到相邻按钮（"点了没反应"）  [✅]  最近：2026-10-27  复发：2 次
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
- **2026-10-27 补充（16px 收起胶囊只有左半区可点）**：`Shell.xaml` 的胶囊轨道 18px、胶囊 16px，
  但为避开窗口 resize border 整体右移 6px，右侧约 6px 落在窗口边框外/不可命中；按胶囊**中心**点
  会"点了没反应"。**判据**：点左半区（本轮真机 `(1267,120)` 命中），日志出现
  `[胶囊] Click 到达 …` + `点击后 visible=True` 三条才算成功；不要只按设计稿中心坐标点。
  **若后续觉得难点**：回退到 18px 胶囊/20px 轨道，或把轨道加宽到 20px，不要加透明命中层绕过。

#### C-08 两个断言宿主同时跑 → 证据 PNG / gate 原始日志被锁、出现假红  [✅]  最近：2026-10-27  复发：3 次
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
- **2026-10-26 补充（两个 gate 抢同一份 raw 日志）**：两个会话同时跑 `run_gates.ps1` 时，
  `artifacts\logs\<日期>\raw-<Host>.out/.err` 是固定路径。后启动的一方
  `[IO.File]::Create($err)` 抛 `IOException`（被对方的宿主/`CopyToAsync` 持有），
  catch 里再 `WriteAllText` 到同一文件也抛，最终 gate 行报
  **`NO OUTPUT (host produced nothing)`**，而 `gate-<Host>.log` 可能是对方写的完整输出。
  **判据**：先查
  `Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" | ? { $_.CommandLine -like '*run_gates.ps1*' }`
  与 `Get-Process <Host>`；有就等，别把 `NO OUTPUT` 当代码红，也别 kill 对方的跑测。
  本轮实测：`gate-FlowCanvasChecks.log` 里宿主自身是 **3724/0**，gate 行红是日志文件被抢。

#### C-09 日志文件被运行中的宿主独占，读不了  [✅]  最近：2026-10-27  复发：2 次
- **现象**：`[IO.File]::ReadAllLines($log)` 抛 `IOException: 文件正由另一进程使用`，而宿主明明还在正常写日志。
  **2026-10-27 补变体**：同一次排查里 `Get-Item`/`Get-ChildItem` 报的 `Length`/`LastWriteTime` 也是**陈旧的**
  （报 22:23、文件实际已写到 22:58、体积少一半）——按它判"宿主没在写日志"会得出反向结论。
- **触发场景 / 复现**：宿主运行中直接读 `VisionMaster\bin\...\Logs\<日期>.log`，并顺手用 `Get-Item` 看增长。
- **原因**：日志写入端持有独占句柄，`ReadAllLines` 默认只申请 `FileShare.Read`；
  目录项的元数据由写端句柄缓存，未 flush 前 `Get-Item` 拿到的是旧值。
- **解决办法**：显式按 `ReadWrite` 共享打开，**长度也从同一个句柄取**：
  ```powershell
  $fs=[IO.File]::Open($log,'Open','Read','ReadWrite'); $sr=New-Object IO.StreamReader($fs,[Text.Encoding]::UTF8)
  $text=$sr.ReadToEnd(); $sr.Close(); $fs.Close()
  # 判增长：$s1=(New-Object IO.FileInfo($log)).Length 同样不可靠 → 用 $fs.Length 前后各取一次
  ```
- **规避建议**：运行中判"日志里有没有某条错误"一律走这条；关掉宿主后再用 `ReadAllLines`。
  判"宿主还在不在写日志"**只看内容里的最新时间戳**，不看 `Get-Item` 的 `Length`/`LastWriteTime`。

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

#### C-11 另一会话把工程改到编译不通过 → 临时探针改独立工程引已构建 DLL  [✅]  最近：2026-10-27  复发：2 次
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

#### C-12 两进程同时编同一 WPF 工程 → `RG1000`「同一 BAML 键重复」  [✅]  最近：2026-10-27  复发：2 次
- **现象**：`dotnet build VisionMaster` 报 `error RG1000: 未知的生成错误"An item with the same key has already been added. Key: views/dialogviews/globalvariableview.baml"`，指向的文件根本没改过。
- **触发场景 / 复现**：另一会话正在构建/验收同一个工程，两个 MSBuild 同时进同一个 `obj`。
- **原因**：WPF 的 `_wpftmp` 工程与主工程共用 `obj\Debug\net9.0-windows`，BAML 资源被枚举两次。
- **解决办法**：确认没有其它构建在跑，清掉该 TFM 的中间目录后重建：
  `Remove-Item -Recurse -Force VisionMaster\obj\Debug\net9.0-windows`。
- **规避建议**：构建前先看有没有别人的 `dotnet build`（`Get-CimInstance Win32_Process` 查 CommandLine）；只清该 TFM 目录，别删整个 `obj`。
- **相关命令 / 参数**：`dotnet build VisionMaster\VisionMaster.csproj -m:1 -nodeReuse:false`
- **2026-10-27 补充（单构建也会中，缓存陈旧）**：本轮只跑了一次 `ResProbe` 构建，
  仍报 `themes/ackbuttonelement.baml` 重复键；`dotnet build Scada.Controls\VM.Scada.Controls.csproj -m:1 -nodeReuse:false -t:Rebuild`
  后 **0 错误**。说明除了"两进程同编"，**增量缓存陈旧**也会触发；先 Rebuild 该工程，再怀疑源码。
- **2026-10-27 变体（产物"能加载但缺资源"）**：并发抢写还能产出**残缺但仍可加载**的产物：某 bin 的
  `UI.dll` 比正常版小 1,536 字节（少一个 BAML），**构建退出码是 0**；宿主运行到合并 App 资源时抛
  `XamlParseException … 找不到资源"themes/controls/gridview.xaml"`（`UI;component/Themes/Generic.xaml` 第 28 行）。
  **判据**：比较各 bin 里同一 DLL 的**大小 + mtime**（本次 11,622,400 vs 11,623,936）；**解法**：对产物工程
  `-t:Rebuild` 后复跑宿主（本次 UIThemeSmokeTest / HelpProbe 复跑 exit 0）。

#### C-13 运行中的 VisionMaster 锁 `Modules\` → 冒烟宿主构建被连带挡住  [✅]  最近：2026-10-28  复发：5 次
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
- **2026-10-27 补充（锁宿主 bin）**：`_VmsCompileProbe` 引用 `VisionMaster.csproj` 时，构建会把
  `UI.dll` / `VM.Core.dll` 复制到 `VisionMaster\bin\Debug\net9.0-windows\`，宿主在跑就报
  `MSB3027/MSB3021`（锁的是宿主 bin，不是 Modules）。**解法**：一次性探针改成对已构建 DLL 的
  `<Reference Private=true>` + `AssemblyResolve` 探测宿主 bin；或 `-p:BuildProjectReferences=false`。
  本轮据此完成 `.vms` 样例流程校验（LoadAsync→Compile→Execute，139/0）。
- **2026-10-27 复现（另一会话的 FlowCanvasChecks 在跑）**：本侧 `FlowCanvasChecks` 构建在
  `Plugin.CSharpScript` / `Plugin.ImageScript` 的 `copy → Modules\` 报 `MSB3073`
  （`Microsoft.CodeAnalysis*.dll` / `hdevenginedotnet.dll` "另一程序正在使用此文件"）；
  该会话退出（`Get-Process FlowCanvasChecks` 归零）后复跑 **0 错误**。**处理**：等对方跑测退出再构建，
  不要为了构建去 kill 对方宿主（与 10-26 补充同判据）。
- **2026-10-28 复现（用户实例在跑）**：`run_gates` 的 `FlowCanvasChecks` 构建在
  `Plugin.ImageScript` / `Plugin.CSharpScript` 的 `copy → Modules\` 报 `MSB3073`；
  `Get-Process VisionMaster` 显示实例仍在且 `Responding=True`（用户可能在验收）。
  **处理**：不 kill；用 `dotnet build FlowCanvasChecks\FlowCanvasChecks.csproj -p:BuildProjectReferences=false`
  复用已构建插件产物，跑出 **4258/0** 证明当前代码没问题；gate 记 **15/16**，
  关闭应用后必须重跑 `tools\run_gates.ps1` 才是最终口径（不要用 `-SkipBuild` 冒充）。

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

#### C-17 手工生成 `.vms`：模型不在 `TemplateRect`、加载后多三条骨架流程、连线端口名写反  [✅]  最近：2026-10-26  复发：2 次
- **现象**：手写 `.vms` 能打开，但 Matching 步骤加载不到模型；`SolutionService.LoadAsync` 后流程列表多出 `Home/Main/End` 三条；`FlowCanvasChecks` 构建报 exe 被上一次运行锁住。第二种形态：`FlowCompiler` 报 `[连线断开] 上游 '…读取标签图像' 不存在输出 'SrcImage'`，并连带报 `[参数缺失] '…' 的必填参数 'SrcImage' 未配置`。
- **触发场景 / 复现**：用 JSON 手写 `.vms` 时只写 `TemplateRect`（旧字段，实际为空），没写 `TemplateLibraryJson`；加载方案后直接按索引找流程；测试宿主退出不干净；或把 `LinkedSources` 里的 `TargetPortName` 写成下游输入端口名（如 `SrcImage`），而不是上游输出端口名（如 `Image`）。
- **原因**：Matching 的模型真源是 `TemplateLibraryJson`（含 `Model` base64）；`TemplateRect` 是旧字段；`MandatoryFlows.Ensure` 会补三条骨架流程；测试宿主可能残留。`LinkedSources` 的**键**才是下游输入端口名，`TargetPortName` 指向**上游步骤的输出端口名**——两者同名（如 Matching 的 `Image→Image`）时写错也看不出来，不同名（`Image→SrcImage`）才暴露。
- **解决办法**：生成 `.vms` 时用 `MatchingPlugin.OnConfirm` 捕获完整 `InputValues`，原样写入 `TemplateLibraryJson`；找流程按 `FlowName` 而不是索引；构建前 `Get-Process FlowCanvasChecks`，残留就等/停自己的测试宿主（不要动 `VisionMaster`）。连线时先查上游插件的输出端口名（`Inputs`/`Outputs` 声明或 `OnConfirm` 快照），`TargetPortName` 填它。
- **规避建议**：手写 `.vms` 后必须走“`SolutionService.LoadAsync` → `FlowCompiler.Compile` → 真插件跑一遍”三步验证，不要只信 JSON 能解析；`Compile` 的连线错误要逐条看，别只看第一条。
- **相关命令 / 参数**：
  ```powershell
  Get-Process FlowCanvasChecks -ErrorAction SilentlyContinue | Select-Object Id,StartTime
  # 验证：SolutionService.LoadAsync -> FlowCompiler.Compile -> MatchingPlugin.Initialize(step)
  ```

#### C-18 `find_shape_model` 的 `NumLevels` 第二值（跟踪层）被漏掉 → 散焦图只匹配 25/121  [✅]  最近：2026-10-26  复发：1 次
- **现象**：同一模型参数下，HALCON 例程 `matching_defocused_pcb.hdev` 在 121 张散焦 PCB 图上 121/121 命中；VM 形状匹配只命中 25 张，位置/角度本身是对的。
- **触发场景 / 复现**：`create_shape_model(..., 6, ...)` + `find_shape_model(..., 6, ...)` 只传单个层数；图像为散焦/低对比（`pcb_focus_telecentric_001..121.png`）。
- **原因**：HALCON `find_shape_model` 的 `NumLevels` 接受 **`[起始层, 跟踪层]` 两元组**，第二值 `-1` 表示“不跟踪到最低层”。散焦图最细层边缘太弱，单值会把最细层当跟踪终点 → 分数不过阈值。实测单值 0..6 与 `[6,1]` 全部只出 25 张，`[0,-1]`/`[6,-1]` 出 121 张。
- **解决办法**：VM `MatchingTemplateEntry` 增加 `TrackingLevel`（默认 1），查找侧统一传 `new HTuple(new[] { NumLevels, TrackingLevel })`；散焦样例条目设 `-1`。
- **规避建议**：对拍 HALCON 例程前先读例程源码/`get_shape_model_params`，确认 `NumLevels` 是单值还是两元组；**单值复刻 ≠ 算法一致**。判据用“命中数 + Row/Col/Angle/Score 差”，不能只看能跑。
- **相关命令 / 参数**：
  ```text
  HALCON: find_shape_model(Image, ModelID, ..., [0,-1], 0.5, 1, 0.5, 'least_squares', 0, 0.9, Row, Column, Angle, Score)
  VM:     NumLevels=6、TrackingLevel=-1
  ```
  详见 `docs/code-changes/2026-10-26-HALCON样例流程_散焦PCB形状匹配.md`。

#### C-19 可形变匹配：`deformation_smoothness` 必须是整数 / 对拍参数必须同源  [✅]  最近：2026-10-26  复发：1 次
- **现象**：`find_local_deformable_model` 报 `HALCON error #9465: Invalid 'deformation_smoothness'`；或插件与同进程 HALCON 参考实现的 `Score` 差约 1e-3（Row/Column/轮廓段数却一致）。
- **触发场景 / 复现**：`new HTuple(11.0, 0, 1)` 把 `deformation_smoothness` 传成 double；或插件 `AngleStartDeg=-30/AngleExtentDeg=60`、参考实现用 `0/0` 对拍。
- **原因**：`deformation_smoothness` 的 GenParamValue 要整数；局部形变的 `find_*` 角度范围会影响估计结果（同一模型下 0/0 与 -30/60 的 Score 不同）。另外 `set_deformable_model_origin` 是相对域重心偏移，`get_deformable_model_contours` 的 level 是 1 基。
- **解决办法**：`new HTuple((int)Math.Round(smoothness), 0, 1)`；对拍时插件与参考实现用同一组查找参数（角度/缩放/分数/重叠/激进度/平滑度）；原点用 `roiCenter - cog`；轮廓固定 `level=1`。
- **规避建议**：新增形变/匹配类插件先跑 `FlowCanvasChecks.exe --deformable` 专项；对拍失败先比“参数是否同源”，再比数值；`RectifiedImages` 用 `HObject`，`DeformedContours` 是扁平 XLD 数组，不要拿 `CountObj` 当命中数。
- **相关命令 / 参数**：
  ```text
  FlowCanvasChecks.exe --deformable
  HALCON: find_local_deformable_model(..., GenParamName=['deformation_smoothness','expand_border','subpixel'], GenParamValue=[11,0,1], ...)
  VM:     new HTuple((int)Math.Round(smoothness), 0, 1)
  ```

#### C-20 `vm-uia.ps1` 取"首个顶层窗口"→ Tooltip Popup 抢占，invoke/texts 只看到提示  [✅]  最近：2026-10-27  复发：2 次
- **现象**：`-Action texts` 只返回一行 Tooltip；`-Action invoke -Name "归档 / 恢复"` 报 `button not found`。
- **触发场景 / 复现**：鼠标停在按钮上产生 Tooltip Popup；进程有两个顶层窗口（Popup + 主窗），Popup 排在第一个。
- **原因**：`vm-uia.ps1` 的 `Get-Root` 取进程第一个顶层窗口，不保证是主窗。
- **解决办法**：用"取最大顶层窗口"的 dumper（本轮 `tools/oneoff/uia-dump.ps1`）；invoke 用
  `RootElement.FindAll(Descendants, ProcessId)` 全进程搜索（`uia-asset.ps1` / `uia-invoke-containing.ps1`）。
- **规避建议**：UIA 脚本不要假设"第一个顶层窗口 = 主窗口"；有 Tooltip/弹窗时先枚举顶层窗口。
- **相关命令 / 参数**：`tools/oneoff/uia-dump.ps1 -ProcId <pid>`

#### C-21 原生 MessageBox/文件夹对话框按钮是 Pane，无 InvokePattern  [✅]  最近：2026-10-27  复发：2 次
- **现象**：删除确认框的「取消」、另存为的「选择文件夹」点不动；`InvokePattern` 抛 `no supported pattern`；坐标点击也不生效。
- **触发场景 / 复现**：资产页删除确认框（`#32770`）、导出/导入/另存为的原生文件对话框。
- **原因**：Win32 对话框按钮在 UIA 里是 `ControlType.Pane`（ClassName=Button），不实现 InvokePattern；
  前台锁使坐标点击落到别的窗口。
- **解决办法**：用 `WindowPattern.Close()` 关闭对话框（等价取消）；需要"确认"时用 Win32
  `WM_COMMAND`/`BM_CLICK`，或交主会话 computer-use。
- **规避建议**：不要把原生对话框按钮当 WPF Button；计划里给"取消"留 `WindowPattern.Close` 兜底。
- **相关命令 / 参数**：
  ```powershell
  $p=$dlg.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern); $p.Close()
  ```

#### C-22 `AppActivate`/`SendKeys` 送不进目标窗口  [✅]  最近：2026-10-27  复发：1 次
- **现象**：`Ctrl+Alt+A` 没打开资产页；`[Microsoft.VisualBasic.Interaction]::AppActivate(pid)` 返回空。
- **触发场景 / 复现**：从终端 PowerShell 对 WPF 主窗发 SendKeys。
- **原因**：前台激活被系统限制；目标窗口不是真正的前台窗口。
- **解决办法**：改用 UIA `ExpandCollapsePattern`/`InvokePattern` 走菜单（`tools/oneoff/uia-menu.ps1`）。
- **规避建议**：WPF 自动化优先 UIA 模式，键盘注入只作兜底。

#### C-23 net48 探针启动后窗口常驻 → `run-probe.ps1` 等不到退出  [✅]  最近：2026-10-27  复发：1 次
- **现象**：`ChartProbe5` 出图 11 张后进程一直不退出，`run-probes.ps1` 挂住；日志文件为空。
- **触发场景 / 复现**：`run-probe.ps1` 对 `runMode=exe` 的探针直接启动并等待退出。
- **原因**：net48 WPF 探针显示窗口且不自动关闭。
- **解决办法**：给该类探针设超时/人工关窗；判据看它是否已产出 PNG，不要等退出码。
- **规避建议**：跑 net48 探针前先看 `runMode`；批量跑时给每项加超时，别让一个窗口卡住整批。

#### C-24 方案资产引用“碰巧命中” / 外部来源仍把模型写回 `.vms` / 本地路径想当然  [✅]  最近：2026-10-27  复发：1 次
- **现象**：给 `Plugin.DeformableMatching` 加 `TemplateAssetId` 后，资产页“流程引用”有时显示、
  有时不显示；选了方案资产但 `.vms` 体积没降；本地模板文件用方案相对路径运行时找不到。
- **触发场景 / 复现**：`SolutionProjectAssetCatalog.MatchesStoredAsset` 的模板分支用
  `step.Plugin.Contains("MatchingPlugin")`；`DeformableMatchingPlugin` 恰好包含这个子串；
  `OnConfirm` 无条件序列化完整 `Entry`；`TemplateFilePath` 按方案根目录想当然。
- **原因**：引用识别靠子串是巧合；插件拿不到方案根路径；外部来源模式下 `Entry.Model` 仍是 base64，
  不区分来源就会把整份模型再写回 `TemplateJson`。
- **解决办法**：资产页显式识别 `DeformableMatchingPlugin + TemplateAssetId`；外部模式 `OnConfirm`
  只写元数据（`Model` 置空）；本地文件用绝对路径或宿主程序目录相对路径，要可移植就用方案资产。
- **规避建议**：新插件接资产后必须断言“资产页显示流程引用 + `.vms` 增量不含模型 base64 +
  资产升版后重新加载”；本地路径在帮助里写清解析基准。
- **相关命令 / 参数**：
  ```text
  FlowCanvasChecks.exe --deformable
  rg "TemplateAssetId|MatchesStoredAsset" D:\C#\VM
  ```

#### C-25 `HelpProbe`/断言宿主日志按控制台代码页写 → UTF-8 回读成乱码，统计正则永不命中  [✅]  最近：2026-10-27  复发：1 次
- **现象**：`HelpProbe.exe > log 2>&1` 后用 `ReadAllLines(log, UTF8)` 读出来全是 `????`/乱码，
  `Select-String 'PASS|FAIL'` 零命中，只剩一句 `=== HelpProbe: ?????? ===`；差点把"没读到"当成"没失败"。
- **触发场景 / 复现**：`cmd /c "...\HelpProbe.exe > artifacts\logs\...\helpprobe.log 2>&1"`，再用严格 UTF-8 回读。
- **原因**：WPF/控制台宿主的 `Console.OutputEncoding` 跟随本机控制台代码页（936），
  重定向到文件时写的是 GBK 字节，不是 UTF-8。R183 已记录同一族问题（`& $exe 2>&1 | Out-String` 按 936 解码 UTF-8）。
- **解决办法**：回读按「严格 UTF-8 优先、失败回落 936」：先 `new UTF8Encoding(false, true)` 试解码，
  抛 `DecoderFallbackException` 就换 `[Text.Encoding]::GetEncoding(936)`。本轮改用 936 回读后看到
  `[PASS]` 全量与 `=== HelpProbe: 全部通过 ===`。
- **规避建议**：日志统计必须先确认"解出了非空行数"，行数异常或零命中一律判红，不许回落到"全部通过"；
  不要把 `cmd > file` 的原始字节直接当 UTF-8 文本读。
- **相关命令 / 参数**：
  ```text
  cmd /c "HelpProbe.exe > artifacts\logs\<yyyyMMdd>\helpprobe.log 2>&1"
  powershell: $enc=[Text.Encoding]::GetEncoding(936); [IO.File]::ReadAllLines($p,$enc)
  ```

#### C-26 远程桌面/前台被占时，合成鼠标键盘送不进目标应用（UIA 仍报 foreground）  [✅]  最近：2026-10-27  复发：1 次
- **现象**：`SetCursorPos` + `mouse_event` 右键流程胶囊，菜单不弹；`keybd_event` 的 Menu 键也不弹；
  UIA 却报告 VisionMaster 是前台窗口、元素 `IsOffscreen=False`、坐标正常（392,104 起）。
- **触发场景 / 复现**：用户在同一个远程桌面上用 ZCode/GameViewer（前台句柄是 GameViewer）；
  脚本先 `SetWindowVisualState(Normal)` 再点坐标。
- **原因**：远程桌面/前台锁让合成输入落到前台客户端窗口，而不是目标 WPF 窗口；
  `SetForegroundWindow`/TOPMOST 也压不住正在被使用的窗口。
- **解决办法**：只用 `InvokePattern`（无焦点也能点按钮）；需要"逐流程运行"时改用进程内生产运行时复跑
  （`FlowCanvasChecks --sample-flows`：SolutionService → FlowCompiler → CompiledFlow.Run）。
- **规避建议**：无人值守 GUI 测试不要依赖坐标点击；先看 `GetForegroundWindow()` 是不是目标进程，
  不是就换 UIA 模式或交回人工。汇报里必须写清"哪几条是 GUI 跑的、哪几条是进程内复跑"。
- **相关命令 / 参数**：
  ```text
  FlowCanvasChecks.exe --sample-flows
  powershell: [WinCap]::GetForegroundWindow()  # 确认前台不是 GameViewer/ZCode
  ```

#### C-27 `CompiledFlow.Run` 整跑完进程不退（非后台线程）→ 下次构建 MSB3027 锁 bin  [✅]  最近：2026-10-27  复发：3 次
- **现象**：`--sample-flows` 已打印汇总、`cmd /c` 已返回 0，但 `FlowCanvasChecks.exe` 进程仍在，
  下一次 `dotnet build` 报 `MSB3027 文件被 FlowCanvasChecks (pid) 锁定`。
- **触发场景 / 复现**：进程内整跑含「结果显示」的流程后，`Main` 返回但进程不退出。
- **原因**：结果显示/图集的异步发布留下了非后台线程，CLR 等所有前台线程结束才退进程。
- **解决办法**：所有模式统一走 `FinishAndExit()`（打印汇总后 `Environment.Exit`）；判据是进程消失、文件可写。
  2026-10-27 复现：默认套件跑完也留了一个 Running 线程（96 线程、CPU 已停），所以不是 `--sample-flows` 独有。
- **规避建议**：新增"整跑流程"类模式一律显式退出；构建前先 `Get-Process FlowCanvasChecks`，
  有残留就停掉（只停自己的测试宿主，不动 VisionMaster）。
  2026-10-27 又复现：`--quality` / `--help-check` 的输出经 `| findstr` 管道时进程不退，
  下一次构建锁 exe；改为**重定向到文件**，跑完 `taskkill /IM FlowCanvasChecks.exe /F` 再构建。

#### C-28 断言宿主的计时类断言在机器被占用时会抖（两次红不同条目）  [✅]  最近：2026-10-27  复发：1 次
- **现象**：同一份代码连跑两次 `FlowCanvasChecks`：第一次红「第 2 圈同一断点再次断开住」（5s 等待超时），
  第二次红「托管计时器 300ms 内跳拍密度合理」（ticks=7，期望 ≈30）；功能类断言全绿。
- **触发场景 / 复现**：用户 `VisionMaster.exe` 正在运行 + 另一会话在构建/跑插件扫描时连跑断言宿主。
- **原因**：断点状态迁移、`System.Threading.Timer` 节拍都依赖线程池调度；CPU 被占时 5s 窗口和
  300ms 密度都会失真。两次红的条目互不相同，说明不是代码路径坏了。
- **解决办法**：判定回归前先看负载（`Get-Process`/CPU）与前后两次红的是否同一条；空闲时复跑。
  需要稳定门禁时把这类用例挪出常规 gate，或把等待窗口按"负载下限"放宽（放宽等待不是放宽判据）。
- **规避建议**：汇报必须写清"功能断言全绿、计时类因负载抖动"，不许把抖动写成"全部通过"，
  也不许把一次计时红写成产品回归。
- **2026-10-28 反例（别把竞态当抖动）**：`通信类失联记为可恢复级` 偶发红（`级别= 说明= 建议=` 全空），
  单跑又绿；看起来像负载抖动，实际是 `MotionDeviceBase.HandleLost` **先置 `SafeStopped` 再写
  `LastFault`** 的落值顺序竞态——断言"看到停机"后立刻读原因，撞上中间窗口。
  **判据**：失败信息里"相关字段全空"而不是"数值偏一点"，先怀疑**写入顺序/可见性**，不要直接归为抖动；
  修复是把两件事放进同一把锁（先落故障、再置状态），不是放宽等待时间。

#### C-29 PowerShell 手算 PNG IHDR 得到错误宽高（`-shl`/`-bor` 优先级与字节符号）  [✅]  最近：2026-10-27  复发：1 次
- **现象**：读 `pcb_focus_telecentric_001.png` 的 IHDR，得到 `240x224`，实际 `752x480`；图片本身能正常打开。
- **触发场景 / 复现**：`powershell -Command` 里照抄 C 的位拼接：`($b[16]-shl 24)-bor($b[17]-shl 16)-bor(...)`。
- **原因**：PowerShell 的 `-shl`/`-bor` 优先级与参数绑定和 C 系不同，`-bor$b[19]` 会被当成参数；byte 参与运算还受类型提升影响。**不能照抄 C 的位拼接写法**。
- **解决办法**：显式整数乘法：`([int]$b[16]*16777216)+([int]$b[17]*65536)+([int]$b[18]*256)+[int]$b[19]`。宽高也可用 `System.Drawing.Image.FromFile`，但 **PNG 的 colorType 只能读 IHDR 第 25 字节**：GDI+ 对 8-bit 灰度 PNG 会报 `Format32bppArgb`，不能用来判断灰度/彩色。
- **规避建议**：任何"从文件头手算数值"的探针，先用一张已知尺寸的图自证（本仓可用 `checkerboard_400.png` = 400×400），再批量跑；尺寸/通道结论必须交叉核对两个来源。
- **相关命令 / 参数**：`[IO.File]::ReadAllBytes($f)`；IHDR `colorType = $b[25]`（0=灰度、2=RGB、3=调色板、4=灰度+Alpha、6=RGBA）。

#### C-30 截图"成功"但弹出菜单 / 下拉 / 原生对话框不在图里  [✅]  最近：2026-10-27  复发：1 次
- **现象**：`PrintWindow` 截 VisionMaster 主窗返回一张完整、非黑的 PNG，但刚展开的菜单/下拉/对话框**一个都不在图里**；
  截图本身没有报错，看起来像"菜单没弹出来"。
- **触发场景 / 复现**：`vm_ui_act` 展开 WPF `MenuItem` 后立刻 `vm_shot mode=window`；截图里菜单区域是原内容。
- **原因**：WPF 的 `Popup`（菜单、下拉、Tooltip）与原生对话框都是**独立顶层 HWND**，
  `PrintWindow(hwnd, …)` 只渲染该窗口自己的表面，子窗口/弹出窗口不在其中。
- **解决办法**：要连弹窗一起看用**屏幕合成截图**（`vm_shot mode=screen`，GDI `BitBlt` 该窗口矩形）；
  找元素用 `vm_ui_tree scope=process`（枚举进程全部顶层窗口，见 C-20）。
- **规避建议**：判据是"图里有没有那个弹窗"，不是"截图有没有报错"。`mode=screen` 的代价是受前台影响
  （目标被别的窗口盖住就截到盖住它的那个）。
- **相关命令 / 参数**：`vm_shot {"mode":"screen"}`；`vm_ui_tree {"scope":"process"}`

#### C-31 按 pid 找不到窗口：launcher pid ≠ 窗口 pid；最小化时 UIA 矩形为空  [✅]  最近：2026-10-27  复发：1 次
- **现象**：`vm_ui_tree {"pid":44304}` 报 `no top-level window for pid 44304`，但屏幕上明明有记事本窗口。
- **触发场景 / 复现**：`Start-Process notepad -PassThru` 拿到 44304；实际持有窗口的进程是另一个 pid。
  另一形态：窗口最小化时 UIA `BoundingRectangle` 返回空矩形，按"有面积才算窗口"的过滤会把它整条丢掉。
- **原因**：Win11 记事本（及部分打包应用）是 launcher + 宿主两段式，`-PassThru` 返回的是 launcher；
  UIA 对最小化窗口不给矩形。
- **解决办法**：按**进程名重新取 pid**（`Get-Process notepad | ? MainWindowHandle -ne 0`）；
  窗口最小化时先 `ShowWindow(SW_RESTORE)` 再枚举（`vm-mcp` 的 `MainWindow` 已内置这一步 + 一次重试）。
- **规避建议**：报"没有窗口"时先分三件事：进程还在不在、窗口是不是最小化、pid 是不是 launcher。
  三者判据不同，别一律当成"窗口还没起来"。
- **相关命令 / 参数**：`vm_ui_tree` / `vm_status`；`Native.FindMainWindow` + `SW_RESTORE`

#### C-32 图标按钮在 UIA 树里全叫 `?`，分不出是哪一个  [✅]  最近：2026-10-27  复发：1 次
- **现象**：VisionMaster 顶栏十几个按钮 dump 出来全是 `Button '?' 32x28`，只能靠坐标猜；
  截图上它们是不同的图标。
- **触发场景 / 复现**：`vm_ui_tree`（或 `vm-uia.ps1 -Action list`）输出直接打到控制台/日志。
- **原因**：这些按钮的 `Name` 是图标字体的**私有区码点**（U+E000–U+F8FF，本仓见 R68）。
  控制台没有对应字形 → 显示成 `?`；写进日志后原码点也丢了。
- **解决办法**：输出前把控制字符与私有区码点转义成 `\uXXXX`（`vm-mcp` 已做），
  按码点或旁边的文字子节点定位（如 `Text '添加一行'` 的父 Button）。
- **规避建议**：看到一排同名 `'?'` 不要以为是 UIA 坏了；先按码点确认是不是不同图标，
  再决定用 `AutomationId`、文字子节点还是坐标。
- **相关命令 / 参数**：`vm_ui_tree {"includePatterns":true}`；转义函数 `Tools.Escape`

#### C-33 右侧同一面板出现两条竖排标题（库自动隐藏页签 + 自定义胶囊重叠）  [✅]  最近：2026-10-27  复发：1 次
- **现象**：工具箱图钉收起后，右边缘出现两条竖排“工具箱”；左边那条被右边胶囊遮住一半，
  看起来像“文字被裁”或“多画了一层”。
- **触发场景 / 复现**：面板已有自定义胶囊轨道（`Shell.xaml` 的 `InspectorCapsuleRail`），
  再点 AvalonDock 的图钉 `PART_AutoHidePin`。
- **原因**：库的自动隐藏页签是独立的 `AvalonDock.Controls.LayoutAnchorControl`（实测 33px 宽），
  自定义胶囊是叠在 `DockingManager` 上的 18px 覆盖层；两套 UI 同时存在。
- **解决办法**：`LayoutHelper.SyncAutoHideTabs(manager, capsuleIds)` 按 `LayoutAnchorControl.Model`
  的 `ContentId` 精确匹配：有自定义胶囊 → `Visibility=Collapsed`；不在集合里的面板 → 保持 `Visible`。
  只改可视层，**不要**动 `IsAutoHidden` / `CanAutoHide`（会破坏 `ToggleAutoHide()` 收回路径）。
- **规避建议**：先用 UIA 枚举同一区域的元素，确认是两套 UI 而不是文字裁剪；
  不要隐藏整个 `LayoutAnchorSide`（会把流程导航等无胶囊面板的恢复入口一起藏掉）。
  无窗口的断言宿主不会生成 `LayoutAnchorControl`，要 `window.Show()` 到屏幕外再测（R53）。
- **相关命令 / 参数**：
  ```powershell
  # 同一区域枚举：出现 LayoutAnchorControl + InspectorCapsuleItem 即重叠
  $all=$win.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition)
  $all | ? { $_.Current.BoundingRectangle.X -gt 1200 } |
    % { "$($_.Current.ControlType.ProgrammaticName) $($_.Current.Name) $($_.Current.BoundingRectangle)" }
  ```
  验证：`run_gates.ps1 -Only UIThemeSmokeTest -SkipBuild`（143/0，含“有胶囊时 Collapsed / 空集合恢复 Visible”两条 + 变异反证）。

#### C-34 给控件设了 `AutomationId`，UIA 树里却找不到它  [✅]  最近：2026-10-27  复发：1 次
- **现象**：给 `Border` 设了 `AutomationProperties.AutomationId="PlainBorder"` + `Name`，`vm_ui_tree` 按名字查 `NODES total=0`；
  但同一个 `Border` 里的 `TextBlock` 能查到（`Text 'BorderChild'`）。元素在屏幕上看得很清楚。
- **触发场景 / 复现**：自测靶子窗口里加一个可见的 `Border`（同样适用于 `StackPanel` / `Grid` / `Canvas`），
  然后按名字/`AutomationId` 找它。
- **原因**：WPF 只有**有 automation peer 的元素**才进 UIA 树。`Control`（`GroupBox`/`Button`/`TextBox`…）
  自带 peer；`Border`/`StackPanel`/`Grid`/`Canvas` 这类布局元素**默认没有**，属性设了也不出现。
- **解决办法**：需要被自动化命中的区域用 `Control`（自测靶子的拖拽面板因此用 `GroupBox`），
  或给该元素显式挂一个 `AutomationPeer`；也可以退一步去命中它的子 `TextBlock`。
- **规避建议**：报"元素不存在"时先确认它是不是布局容器，别反复调 `maxDepth` / `scope`。
  判据：同一位置的子 `TextBlock` 能查到、父容器查不到 → 就是这个原因，不是查询写错。
- **相关命令 / 参数**：`vm_ui_tree {"nameContains":"PlainBorder"}` → `NODES total=0`；
  `vm_ui_tree {"nameContains":"BorderChild"}` → 命中。守门：`uismoke.mjs` 两条（正反各一）。

#### C-35 `ValuePattern.SetValue` 写完界面"没反应"（业务没收到编辑事件）  [✅]  最近：2026-10-27  复发：1 次
- **现象**：UIA `SetValue` 写输入框，读回值是对的，但界面上的联动/校验/候选刷新全没触发，
  看起来像"输入框坏了"或"业务逻辑没生效"。
- **触发场景 / 复现**：本仓按 R35 用 `PreviewTextInput` / `PreviewKeyDown` / "用户改过"标记判意图的控件
  （变量名手填、端口值、轴参数等），用 `SetValue` 或程序灌值。
- **原因**：`ValuePattern.SetValue` 只改值，**不产生任何键盘/文本输入事件**，所以"用户意图"判据收不到。
- **解决办法**：测试这类控件必须走真实按键（`vm_ui_type mode=keys`：SetFocus → Ctrl+A → Delete → 逐字符 unicode 输入）；
  `mode=value` 只用于"只关心最终值"的字段。
- **规避建议**：判据是"**联动**有没有发生"，不是"值对不对"。写完先读回值，再看联动结果；
  两者不一致就换 `mode=keys` 重试，不要先怀疑产品。
- **相关命令 / 参数**：`vm_ui_type {"ref":N,"mode":"keys","text":"..."}`；工具描述里已写明这条。

#### C-36 启动画面是无标题窗口，被当成"主窗口"→ 启动判据提前成立  [✅]  最近：2026-10-27  复发：1 次
- **现象**：`vm_start` 1.4 秒就报"启动成功"，返回的窗口是 `'' 520x360`（无标题），
  而真正的主窗 `VisionMaster 1280x860` 还要 2 秒才出来；后续 `vm_shot` / `vm_ui_tree` 全部指向这个 520×360 的窗口。
- **触发场景 / 复现**：任何按"第一个出现的顶层窗口"或"面积最大的窗口"选主窗的实现，在 VisionMaster 启动阶段都会中招。
- **原因**：启动画面是**有真实矩形、但没有标题**的顶层窗口，在它存在的 1.2s~2.8s 区间里它是唯一/最大的窗口。
- **解决办法**：主窗判据改成"**有标题 + 尺寸下限**"（默认 ≥640×480）；超时不要报"没窗口"，
  而是把当前所有顶层窗口列出来（`vm_windows`），这样能一眼看出是启动画面还是模态框。
- **规避建议**：判"软件起来了"永远不要用"有窗口"；要"有标题且像外壳"。实测时间线：
  t=1.23s 无标题 520×360（启动画面）→ t=2.83s 无窗口（切换间隙）→ t=3.26s `VisionMaster` 1280×860。
- **相关命令 / 参数**：`vm_windows {"pid":N}`；`vm_start {"minWidth":640,"minHeight":480}`；
  守门：`uismoke.mjs`（外壳窗口判据）+ 真机 `watch-boot.mjs` 时间线。

#### C-37 带未保存改动退出 → 下次启动弹「上次运行的现场」草稿框并阻塞界面  [✅]  最近：2026-10-27  复发：2 次
- **现象**：启动后界面看着已经好了，但点哪都没反应；UIA 树里能看到一段
  `上次运行的现场 / 检测到 1 份未保存的方案草稿 / 现在打开所在目录吗？` + `稍后再说` / `打开目录` 两个按钮。
- **触发场景 / 复现**：上一次是**带着未保存改动退出**的（正常 `vm_stop` 也会走"退出链 → 未保存草稿转存"），
  草稿落到 `bin\...\Autosave\`；下次启动就弹恢复框。
- **原因**：产品的自动转存/恢复设计，不是缺陷；但它是个覆盖层，挡住整个主界面。
- **解决办法**：自动化流程的固定第一步——启动后先查/关它：
  `vm_ui_act {"nameContains":"稍后再说","controlType":"Button","action":"invoke"}`，
  再 `vm_assert {"nameContains":"稍后再说","expect":"absent"}` 确认真的关掉了。
- **规避建议**：`vm_start` 成功 ≠ 界面可用。判据是"目标元素可操作"，不是"窗口存在"。
  需要干净启动时，测试前先把方案保存/关闭，别让退出链产生草稿。
- **相关命令 / 参数**：日志 `[退出链] 方案有未保存改动，已转存草稿：...Autosave\<方案>-<时间>.vms`；
  守门：人工评审 + 本轮真机实测。

#### C-38 按名字过滤 UIA 树，Button 旁边的 `Text` 先命中 → 拿错 ref  [✅]  最近：2026-10-27  复发：1 次
- **现象**：`vm_ui_tree {"nameContains":"稍后再说"}` 返回 3 行，第 1 个是提示文字
  `Text 'Enter 打开目录 · Esc 稍后再说'`；按 ref=1 调 `vm_ui_act invoke` 报
  "does not support InvokePattern. Available patterns: (none)"，看着像按钮不可点。
- **触发场景 / 复现**：按名字过滤后直接取第一个 ref；父/子同名（Button 与其 Text 子节点、提示行里包含同样的词）。
- **原因**：过滤只按名字匹配，**不保证第一个命中是可操作控件**；`Text` 元素没有任何模式。
- **解决办法**：`vm_ui_act` 支持不带 ref、直接按名字定位，并**自动挑支持该模式的元素**；
  或先看树里的 `ControlType`（`controlType:"Button"` 过滤）再取 ref。
- **规避建议**：判据是"**报错里 available patterns 为 (none)**"→ 先怀疑拿错元素，不要怀疑按钮不可用。
  名字过滤 + `includePatterns:true` 能一次看清每个元素能干什么。
- **相关命令 / 参数**：`vm_ui_act {"nameContains":"稍后再说","controlType":"Button","action":"invoke"}`；
  守门：`uismoke.mjs` 两条（按名字定位 + 计数确实变了）。

#### C-39 `cmd` 里 `& echo EXIT=%ERRORLEVEL%` 读到的是上一条命令的退出码  [✅]  最近：2026-10-28  复发：5 次
- **现象**：`node tools\oneoff\register-vm-mcp.mjs & echo EXIT=%ERRORLEVEL%` 打印 `EXIT=0`，
  但脚本走的是 `process.exit(1)` 的拒绝分支——"闸门没拦住"的假结论。
- **触发场景 / 复现**：cmd 单行里用 `&` 串联后紧跟 `%ERRORLEVEL%`，判"闸门到底拦没拦住"时。
- **原因**：`%ERRORLEVEL%` 在**整行解析期**展开，取的是这条命令行执行**之前**的值。
- **解决办法**：用 `if errorlevel 1 (...)`，或 PowerShell 里取 `$LASTEXITCODE`（同一进程内即时可取）。
- **规避建议**：**凡"看退出码判闸门"的验证都不许内联 `%ERRORLEVEL%`**；`$LASTEXITCODE` 复测得 1 才算拦住。
  **2026-10-28 同族复发一（`;` 不是 cmd 的分隔符）**：`powershell -NoProfile -File "tools\sync_constraints.ps1" ; echo "EXIT=$LASTEXITCODE"`
  在 cmd 里执行 → `;` 与后面整段被当成**脚本的位置参数**，报
  `找不到接受实际参数"EXIT=$LASTEXITCODE"的位置形式参数`。**`-File` 后面不许再跟 `;`**：
  要么分两次命令，要么整段交给 `powershell -NoProfile -Command "..."`。
  **同族复发二（`|` 是 cmd 的管道）**：`powershell -File x.ps1 2>&1 | Select-Object -First 30` 在 cmd 里
  把 `Select-Object` 当**下一条外部命令**去找，报"不是内部或外部命令"。cmd 里要截断输出就别接
  PowerShell 管道动词；要过滤输出就整段交给 `-Command`。
  **同族复发三（2026-10-28，构建版）**：`dotnet build X.csproj > log 2>&1 & echo EXIT=%ERRORLEVEL%`
  打印 `EXIT=0`，日志里其实是 `CS7036`——"构建通过"的假结论，白等一轮。**构建判绿的唯一写法**是
  `dotnet build ... ` 单独一行、下一行 `echo EXITCODE=%ERRORLEVEL%`（写成 `.cmd` 文件，每行独立解析），
  或 PowerShell 里 `$LASTEXITCODE`。
  **同族复发四（`;` 把整条 gate 命令吞成参数）**：`powershell -File tools\run_gates.ps1 -Only X > log 2>&1; powershell -File ...`
  → `run_gates.ps1` 报 `无法将值"powershell"转换为类型"System.Int32"`（第二段被当成 `-StepTimeoutSec` 的值）。
- **相关命令 / 参数**：`powershell -NoProfile -Command "node <脚本>; 'exitcode=' + $LASTEXITCODE"`。

#### C-42 PowerShell 变量名不区分大小写：`$l` 覆盖 `$L`，列表只剩 1 个元素  [✅]  最近：2026-10-28  复发：1 次
- **现象**：批处理脚本里 `List[string]` 存进 `$L`（1779 行），循环里顺手写了 `$l=$L[$i]`——一轮之后
  `$L.Count` 变 **1**、`$L[259]` 为 null，报「不能对 Null 值表达式调用方法」；全程无任何覆盖警告。
- **触发场景 / 复现**：PowerShell（任何宿主）里同时使用仅大小写不同的两个变量名（`$l`/`$L`、`$t`/`$T`）。
- **原因**：PowerShell 变量名**不区分大小写**，`$l=…` 就是对 `$L` 的重新赋值——第一轮把列表换成了
  标量字符串，后续 `.Count` 走标量包装恒为 1，循环 `$L[$i]` 取字符越界返回 null。
- **解决办法**：循环变量换名（`$ln`）；被写坏的脚本从上一版重读后重跑。
- **规避建议**：一次性脚本**禁止使用仅大小写不同的变量名**；写文件前先打印关键计数与锚点行自检；
  能先干跑（只统计不落盘）就先干跑。
- **相关命令 / 参数**：实例见 `docs/code-changes/2026-10-28-SCADA属性面板Web设计稿v2重构.md` 追加节
  （脚本 `tools/oneoff/fix-design-v21.ps1`，用完即删）。

#### C-43 代码页把路径写坏，在仓库根造出一个乱码"同名"目录  [⚠️]  最近：2026-10-28  复发：1 次
- **现象**：仓库根多出一个与 `解决方案` 同义、但名字是 6 个乱码字符的目录；`tools\audit_root.ps1`
  报 `DIR 解决方案`（乱码形态），而白名单里明明有正常的 `解决方案`。里面只有一条 16 字节账本 CSV。
- **触发场景 / 复现**：把含中文的绝对路径从一个按 UTF-8 处理的环境传给按 ANSI(936) 解释的进程
  （或反之），该路径被当作输出目录（`DirectoryTemplate`）或建目录参数。
- **原因**：`解决方案` 的 UTF-8 字节（12 字节）被按 GBK 解码成 6 个字符，于是"同名目录"其实**另起了一个名字**；
  `audit_root` 按字面比较，自然判违规。子目录名可能又是正常的（损坏发生在某一跳，不是整棵）。
- **解决办法**：整目录移到 `_Retired\<日期>-<主题>\` 并写 README（R168），**不要直接删**（留着查是谁写的）。
  本次：`_Retired\2026-10-28-mojibake-solutions\`。
- **规避建议**：① 中文路径只在 `-File` 脚本里用码点构造，别跨进程用 `-Command` 传；
  ② 工具遇到**未知参数必须报错退出**，别落到"默认全量生成"分支——本次已给 `tools\vms-generator`
  加守卫（`--bogus-switch` → exit 2），否则一个被损坏的开关等于一次全量重写；
  ③ `audit_root` 报 `DIR <中文>` 时**先比对码点**，别急着往白名单加条目。
- **相关命令 / 参数**：`(Get-ChildItem D:\C#\VM -Directory).Name.ToCharArray() | % { 'U+{0:X4}' -f [int]$_ }`

#### C-44 `timeout /t N` 在被重定向 stdin 的 shell 里立即失败 → "等待"根本没发生  [✅]  最近：2026-10-28  复发：1 次
- **现象**：连发三次 `timeout /t 180 >nul` 之后 `Get-Date` 只走了 5 秒；每次输出一行
  `错误: 不支持输入重新定向，立即退出此进程。`。误以为"等了三分钟"，实际是三次立即返回。
- **触发场景 / 复现**：在 AI 工具的非交互 shell（stdin 被重定向/管道）里用 `timeout /t N >nul` 当 sleep，
  例如"等并发会话改完文件再重建"。
- **原因**：`timeout.exe` 需要控制台 stdin；被重定向时它直接报错退出（退出码非 0），不睡眠。
  整行里后面的命令照常执行，于是"等待 + 重试"变成"立刻重试"，看起来像"并发改动一直没收尾"。
- **解决办法**：用 `powershell -NoProfile -Command "Start-Sleep -Seconds 180"`；轮询类等待写进
  PowerShell 循环（`Start-Sleep` + 条件判断），别用 cmd 的 `timeout`。
- **规避建议**：**任何"等一会儿再看"的动作都不许用 `timeout /t`**；脚本里若必须兼容交互/非交互，
  判 `[Console]::IsInputRedirected` 后走 `Start-Sleep`。等待型任务的输出要带时间戳，否则分不清
  "没等到"与"根本没等"。
- **相关命令 / 参数**：`powershell -NoProfile -Command "Start-Sleep -Seconds 180; Get-Date -Format HH:mm:ss"`

#### C-45 后台 PowerShell 循环输出被缓冲 → 日志几分钟是空的，误判"没跑"  [✅]  最近：2026-10-28  复发：1 次
- **现象**：后台跑 `powershell -Command "for(...){ dotnet build ...; Write-Output ... }" > poll.log`，
  5 分钟后 `poll.log` 里只有第一次的 `try 0`，看起来像循环卡死；实际上循环在正常跑（`dotnet` 进程
  一个接一个起落），只是 `Write-Output` 到重定向文件被缓冲，没落盘。
- **触发场景 / 复现**：用后台命令做"轮询构建直到通过"这类长循环，中途 `type`/`Get-Content` 读日志判进度。
- **原因**：PowerShell 对重定向的 stdout 走缓冲（4KB 级），进程未退出前不刷盘；`Write-Output`
  不会逐条 flush。
- **解决办法**：判进度别读它的 stdout——直接看**外部信号**（被等文件的 `LastWriteTime`、`dotnet` 进程是否存在）；
  或让循环把每步写进独立文件（`Add-Content` 到单独文件），或等进程结束再读日志。
- **规避建议**：**后台轮询的"进度"只认可观测的外部状态，不认被缓冲的日志**；要即时可见就
  一次一个前台命令，别套长循环。
- **相关命令 / 参数**：`Get-Item <被等文件> | Select LastWriteTime`；`Get-Process dotnet`

#### C-46 弹出层开着时，UIA Invoke 主窗关闭按钮被吞  [✅]  最近：2026-10-28  复发：1 次
- **现象**：`vm_ui_all.ps1 -Action invoke -Name CloseWindow` 返回 `INVOKED targets=1`，8 秒后
  `Get-Process VisionMaster` 仍在、窗口还在；dump 里"轮次详情"浮窗仍开着。先 invoke 浮窗的 `✕`
  再 invoke `CloseWindow`，进程立刻退出。
- **触发场景 / 复现**：底栏工位浮窗 / 轮次下钻（WPF `Popup`，`StaysOpen=False`）开着时关主窗。
- **原因**：弹层持有鼠标捕获/焦点，窗口 chrome 的关闭在弹层未收起前不生效；UIA 的 `Invoke`
  只发 Click，不经过 `PreviewMouseLeftButtonDown` 的"先关弹层"守卫。
- **解决办法**：先关弹层（`✕` / `关闭`），确认 dump 里没有"在跑流程/轮次详情"标题，再关主窗；
  或直接 `PostMessage(hwnd, WM_CLOSE)`。
- **规避建议**：关窗前先 dump 一次；"INVOKED"只证明模式调用成功，不证明业务已执行——必须回读进程/窗口状态。
- **相关命令 / 参数**：`vm_ui_all.ps1 -Action invoke -Name '✕'` → `-Name CloseWindow` → `Get-Process VisionMaster`

#### C-47 浮窗/列表行在 UIA 树里只出现视口内的几行（虚拟化）  [✅]  最近：2026-10-28  复发：1 次
- **现象**：按工位名找行，只匹配到左侧流程列表的 `ListItem`（不可 Invoke）；弹窗里排在下方的工位
  （如"模板匹配_NCC散焦图"）完全不在树里，报 `ROW INVOKE FAILED: no invokable ancestor`。
- **触发场景 / 复现**：工位浮窗 10 行 × 78px，视口 600px；窗口变小后最后 2~3 行未实例化。
- **原因**：`ItemsControl`/`List` 虚拟化，屏幕外的行没有容器，UIA 自然找不到。
- **解决办法**：选视口内的行（本次改用 `Blob缺陷检测_标签字符`）；或先 `ScrollPattern`/点 `PageDown`
  让目标行进入视口再找。
- **规避建议**：按名字找不到时先看 rect 是否落在弹层视口内；选中后必须回读底栏范围胶囊
  （本次 `Blob缺陷检测_标签字符 · 子程序` + 节拍/OK 变化）确认真的生效。
- **相关命令 / 参数**：`vm_ui_all.ps1 -Action list`（按 rect 过滤 y 区间）

#### C-48 `[string]` 参数与同名局部变量冲突（PowerShell 大小写不敏感）→ JSON 对象被强转成字符串  [✅]  最近：2026-10-28  复发：1 次
- **现象**：脚本 `param([string]$Map)` 之后写 `$map = [IO.File]::ReadAllText($Map,...) | ConvertFrom-Json`，
  随后 `foreach ($p in $map.PSObject.Properties)` 只输出一行 `MISS Length`，8 条映射全部丢失。
- **触发场景 / 复现**：用 `ConvertFrom-Json` 读映射表，参数名与解析结果同名（仅大小写不同）。
- **原因**：`$map` 与 `$Map` 是**同一个变量**；把它赋给带 `[string]` 约束的参数变量时，
  PSCustomObject 被 `ToString()` 成 `@{...}` 字符串；字符串的 `PSObject.Properties` 只有 `Length`。
- **解决办法**：局部变量换名（`$mapping`），或参数不要写类型约束。
- **规避建议**：参数名与局部变量不要只差大小写（C-42 的 `$l`/`$L` 同类）；解析结果变量统一加 `ing`/`Data` 后缀。
- **相关命令 / 参数**：`$mapping = ... | ConvertFrom-Json; $mapping.PSObject.Properties`

#### C-49 只复制 `.vms` 到别处 → 图像/资产全读不到（方案内相对路径）  [✅]  最近：2026-10-28  复发：1 次
- **现象**：把测试方案单独放到临时目录，加载后 `Image\` / `Assets\` 引用全部失效。
- **触发场景 / 复现**：为了不污染用户方案的 `History\` / `Logs\`，把 `.vms` 拷到 `artifacts\realtest\` 下运行。
- **原因**：`SourcePath` / `SourceImagePath` / `*Assets[].RelativePath` 都按 **`.vms` 所在目录**解析（R187）。
- **解决办法**：整目录 `robocopy /E`（样例 181 文件 / 120MB）；或把测试 `.vms` 放在原方案目录（会共用 History/Logs）。
- **规避建议**：跑前备份 `AppConfig.json` / `Layout.xml` / `DefaultLayout.xml`（A-03/A-05），跑后还原；
  还原判据是"原方案路径出现 2 次、测试路径 0 次"，不靠肉眼看配置。
- **相关命令 / 参数**：`robocopy <src> <dst> /E /NFL /NDL /NJH /NJS`；`patch_appconfig.ps1`

#### C-50 清理自己建的隔离构建目录，连别人的一起删了（`artifacts\build\` 是多会话共用）  [⚠️]  最近：2026-10-28  复发：1 次
- **现象**：用 `-p:BaseOutputPath=artifacts\build\` 隔离构建验证完后 `rmdir /s /q artifacts\build`，
  把**别的会话**留在里面的 `avalon-fix2`、`batch1-verify` 一起删了（只剩一个空的 `avalon-fix`）。
- **触发场景 / 复现**：宿主 exe 正在运行、锁住 `bin\` 与 `Modules\`，于是用隔离输出目录构建；
  验证完顺手把那个目录当成"我刚建的"删掉。
- **原因**：`artifacts\build\<批次>\` 是本仓**多会话共用**的约定（`docs/code-changes/2026-10-27-多流程并发帧归属…`
  就写着 `artifacts\build\batch1-verify\`）。我建的是 `artifacts\build\Debug\` 这一层，
  删父目录等于连别人的批次一起删；`Remove-Item -Recurse` 还只在最后报一句"找不到文件"，不点名。
- **解决办法**：只能重跑对应构建再生（都是隔离构建产物，不是源码）。删之前先 `dir /b` 看有没有
  别人命名的子目录。
- **规避建议**：**隔离构建目录一律带自己的批次名**（`artifacts\build\<日期>-<主题>\`），
  清理只删自己那一层；**任何 `rmdir /s /q` / `Remove-Item -Recurse` 之前，先列目录确认里面全是自己的东西**。
- **相关命令 / 参数**：`dir /b artifacts\build`；`dotnet build <proj> -m:1 -nodeReuse:false -p:BaseOutputPath=artifacts\build\<批次>\ -p:ModulesDir=<临时目录>\`
  （路径**不加引号**：结尾的 `\` 会吃掉收尾引号，两个 `-p:` 会被拼成一个非法路径）

#### C-51 Prism `DialogCloseListener` 在断言宿主里伪造 → 异步抛异常把宿主带崩  [✅]  最近：2026-10-28  复发：1 次
- **现象**：弹窗 VM 的 DLG 断言全部打印"通过"，随后进程以 `-532462766` 退出；日志里
  `Unhandled exception. System.InvalidOperationException: The DialogCloseCallback has not been properly
  initialized. This must be initialized by the DialogService, and should not be set by user code.`
  堆栈落在 `Prism.Dialogs.DialogCloseListener.Invoke` + `Task.ThrowAsync`（异步续体）。
- **触发场景 / 复现**：断言宿主里用反射 internal 构造器造 `DialogCloseListener`，调
  `vm.SaveCommand.Execute()` → VM 内 `RequestClose.Invoke(parameters, ButtonResult.OK)`。
- **原因**：`Invoke(IDialogResult)` 还会走由 `DialogService` 注入的 `DialogCloseCallback`；
  反射只补了 `_onClosed`，另一条路为空 → 在 Task 续体里抛。**断言"通过"不代表进程没崩**。
- **解决办法**：把保存核心抽成不关窗的 `Apply()`（写模型 + 返回是否成功），断言只测 `Apply()`；
  关窗回调属 Prism 管道，留给真机/集成测试。
- **规避建议**：弹窗 VM 的"业务结果"与"关窗"分离；不要在断言宿主里伪造框架回调对象。
- **相关命令 / 参数**：`FlowResultSourceDialogViewModel.Apply()`；`FlowCanvasChecks` DLG-1~17

#### C-52 XAML/XML 注释里出现 `--` → MC3000，构建直接失败  [✅]  最近：2026-10-28  复发：1 次
- **现象**：新建的 `FlowResultSourceDialogView.xaml` 构建报
  `MC3000: An XML comment cannot contain '--'`，指向 `<!--  ---- 自动 ----  -->`。
- **触发场景 / 复现**：用 `---- 标题 ----` 当分区注释（C# 里常见，XML 里非法）。
- **原因**：XML 规范禁止注释体出现连续两个连字符；`-` 也不能是注释最后一个字符。
- **解决办法**：分区注释改成 `<!--  [自动]  -->` 或 `==== 标题 ====`（等号合法）。
- **规避建议**：XAML 里画分隔线只用 `=` 或 `·`；写完先跑一次宿主构建，别等 gate。

#### C-53 右键菜单在 UIA 里不是独立窗口；菜单必须在**同一次调用内**点掉  [✅]  最近：2026-10-28  复发：1 次
- **现象**：`vm_ui_all.ps1`（按 PID 枚举顶层窗口）在右键流程胶囊后返回 `WINDOWS_AFTER=0/1`，
  看不到任何 `MenuItem`；改用 Computer Use 的 AX 树，菜单项（`menuitem 运行统计…`）**直接平铺在主窗树里**。
  另外：一次调用里"右键 → 观察 → 点菜单项"可以成功；拆成两次调用，菜单在两次之间就关了，索引全部失效。
- **触发场景 / 复现**：WPF `ContextMenu`（Popup 独立 HWND）由 DataTemplate 里的 `Grid.ContextMenu` 打开；
  验证「运行统计…」入口。
- **原因**：① UIA 的 `RootElement` 子窗口枚举在菜单打开瞬间会返回空/不全（菜单是 capture 窗口）；
  ② Computer Use 的 `getAXState` 会把 Popup 内容并进主窗树，所以能看到 `menuitem`；
  ③ 菜单是 `StaysOpen=false`，跨调用期间焦点/捕获变化会把它关掉。
- **解决办法**：右键与点菜单项放在**同一个 Computer Use 调用**里；点流程行要点"行内文本子元素"的索引
  （`row FlowModel` 后紧跟的 `text 流程名`），不要点全树里最后一个同名 Text（那是画布页签）。
- **规避建议**：菜单类交互一律"打开 + 选择"原子化；`INVOKED`/`AXPress` 只证明动作被接受，
  必须回读目标窗口标题（本次 `window 运行统计 · 结果源`）才算打开。

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

#### D-04 夹具目录名 ≠ 方案名 → 保存自动转另存为  [✅]  最近：2026-10-27  复发：1 次
- **现象**：点保存弹出「选择方案保存位置（将在其中创建以方案命名的文件夹）」文件夹框，不直接写回。
- **触发场景 / 复现**：夹具放在 `D:\VMSolutions\VMTest_Assets\智能装配检测线.vms`（父目录名不是方案名）。
- **原因**：方案保存约定 `<父目录>\<方案名>\<方案名>.vms`；不符合就当"散装老方案"转另存为（K-04）。
- **解决办法**：夹具按规范放 `D:\VMSolutions\VMTest_Assets\智能装配检测线\智能装配检测线.vms`；
  或本轮直接测另存为。
- **规避建议**：创建夜间测试夹具时先按方案名建目录，不要用主题名当父目录。
- **2026-10-27 验证**：规范目录 `D:\VMSolutions\VMTest_Assets\智能装配检测线\智能装配检测线.vms` 下保存
  **直接写回**（`.vms` mtime 02:52、大小 1,013,529→1,059,675，无另存为框）；`.vms` 内
  `AssetDefaults` / `AssetLifecycle` / `AssetVersionLog` 均可回读。

#### D-05 本机 shell 里没有 `git` 命令（提交/status 报"不是内部或外部命令"）  [✅]  最近：2026-10-27  复发：1 次
- **现象**：`git status` 报 `'git' 不是内部或外部命令`；`Get-Command git` / `where git` 都为空，像仓库坏了。
- **触发场景 / 复现**：在 ZCode/DSH 的 cmd / PowerShell 会话里直接调 `git`（PATH 与注册表 PATH 都没有 git）。
- **原因**：本机未装标准 Git for Windows；git 以**便携形态**随其它工具附带，不在 PATH。
- **解决办法**：用完整路径调用（实测两处可用）：
  `D:\Program Files\WorkBuddy\resources\vendor\PortableGit\cmd\git.exe`；
  `D:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\CommonExtensions\Microsoft\TeamFoundation\Team Explorer\Git\cmd\git.exe`。
  一律带 `-C "D:\C#\VM"`，不依赖 cwd；中文提交信息走 `-F <utf8 文件>`，不塞进命令行。
- **规避建议**：提交前先 `status --short` 看清单，**只 `add` 本工作流的文件**——工作区常驻多个会话的未提交改动，
  整目录 `add` 会扫走别人的东西；`status` 里中文路径显示为 `\346\223\215\344\275\234` 八进制转义是显示行为，不是损坏。
- **相关命令 / 参数**：
  ```text
  git -C "D:\C#\VM" status --short
  git -C "D:\C#\VM" add -- <仅本工作流文件>
  git -C "D:\C#\VM" commit -F "artifacts\logs\<yyyyMMdd>\commit-msg-xxx.txt"
  ```

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

#### E-04 探针断言随 UI 改版过期（假红）  [⚠️]  最近：2026-10-27  复发：3 次
- **现象**：`ToolboxProbe` 期望 `UseDefaultEffectDataTemplate=True`，`ToolView.xaml:618` 已按
  2026-10-23 设计改成 `False`；`BeadViewProbe` 191 条布局失败（预览卡位置/输入控件定位器过期）；
  `WebProbe5` 编译失败（`IScadaUserAuthenticator.TryValidate` 签名变了）。
- **触发场景 / 复现**：UI 改版/接口扩展后直接跑旧探针。
- **原因**：探针把当时的实现细节写死（属性值、控件位置、接口签名），产品按新设计改了。
- **解决办法**：先查源码/XAML 确认产品意图，再判"探针过期"还是"产品缺陷"；
  假红不要当产品缺陷去改产品。
- **规避建议**：探针失败先看证据（截图/源码）；每轮 UI 改版后同步更新探针。

#### E-05 探针截图仍往仓库根旧目录写（R167 迁移遗漏）  [✅]  最近：2026-10-27  复发：1 次
- **现象**：`BeadViewProbe` 31 条失败全是 `DirectoryNotFoundException:
  D:\C#\VM\_BeadViewProbe\shot_*.png`；探针本身渲染正常。
- **触发场景 / 复现**：探针源码里 `OutDir = @"D:\C#\VM\_BeadViewProbe"`，R167 迁到
  `tools\probes\` 时没改输出路径；根目录 `_BeadViewProbe` 已不存在（R168 不允许根下划线目录）。
- **原因**：迁移只改了工程位置，没改硬编码输出路径。
- **解决办法**：输出改 `Path.Combine(@"D:\C#\VM","artifacts","render",<yyyyMMdd>,"bead-view")`
  并在写前 `Directory.CreateDirectory`。
- **规避建议**：R167 迁移清单里加一项"探针输出路径"；判据看 PNG 是否落在 `artifacts\render\`。

#### E-06 断言夹具在 lambda 里 `using` → 执行前已释放（假“未连接”）  [✅]  最近：2026-10-27  复发：1 次
- **现象**：`RunExpectFail("…", p => { using HImage img = Make(); p.Image.Value = img; ... })` 全部报
  `输入图像未连接（Image 端口为空）`，看起来像插件守卫失效。
- **触发场景 / 复现**：把建图放在 `Action<Plugin>` 里，`configure(plugin)` 返回后才 `plugin.Execute(...)`。
- **原因**：`using` 在 lambda 返回时释放了 `HImage`，`Execute` 时 `IsInitialized()` 为 false。
- **解决办法**：把 `using` 放到 `RunExpectFail` 调用**外层**，让图活到 `Execute` 之后；或让夹具返回 `IDisposable` 由调用方释放。
- **规避建议**：断言里“建图 → 配插件 → 执行”三步必须在同一个生命周期作用域内；看到“未连接”先查夹具释放时机，别先怀疑插件守卫。
- **相关命令 / 参数**：`using (HImage img = Make()) { RunExpectFail(..., p => p.Image.Value = img, ...); }`

#### E-07 RANSAC 测试的“错配点”整体一致 → 被正确当成内点  [✅]  最近：2026-10-27  复发：1 次
- **现象**：8 点里 5 个“错配点”都加了同一个平移，RANSAC 仍成功；断言期望失败却红了。
- **触发场景 / 复现**：RANSAC 用例给错误点统一 `row += 40; col += 40`。
- **原因**：这 5 个点互相一致，确实能用同一个变换解释；RANSAC 把它们当内点是对的，不是插件 bug。
- **解决办法**：错配点要互不一致（每点不同偏移），并收紧 `InlierThreshold`；或加验证点。
- **规避建议**：RANSAC 用例必须区分“真内点组”和“另一组一致点”；不要用同一平移造错配。

#### E-08 想造"慢查询"验证加载骨架：WAL 下写锁挡不住读  [✅]  最近：2026-10-27  复发：1 次
- **现象**：给探针造慢查询（验证"查询 ≥300ms 才显示骨架"）时，夹具库上开了写事务 / `BEGIN IMMEDIATE`，
  查询照样毫秒级返回，`IsLoading` 永远不亮——看起来像门限逻辑没生效。
- **触发场景 / 复现**：用 SQLite 夹具库做加载态断言（本仓 `SqliteSoftwareOperationLog` 初始化即 WAL）。
- **原因**：WAL 模式**读写互不阻塞**；普通写事务只挡其它写，不挡 SELECT。
- **解决办法**：先把夹具库切回 rollback journal（`PRAGMA journal_mode=DELETE;`，持久化在库头），
  再由外部连接 `BEGIN EXCLUSIVE;` 持排他锁 → SELECT 按 `DefaultTimeout` 等待；`ROLLBACK` 后查询完成。
  切 journal 前先 `SqliteConnection.ClearAllPools()`（池里的空闲连接会挡住模式切换）。
- **规避建议**：造"慢查询"别靠写锁；判据看锁期间 `IsLoading == true`、释放后 `false` 且数据到位
  （本轮 `_ProjectAssetsUiProbe` ⑤f 即此写法：`SkeletonDelay=20ms` + `RefreshCommand.Execute()` 重发查询）。
- **相关命令 / 参数**：
  ```text
  PRAGMA journal_mode=DELETE;   -- 切回 rollback journal（WAL 下读不挡写）
  BEGIN EXCLUSIVE;              -- 持排他锁 → 读等待
  ROLLBACK;                     -- 释放
  ```

#### E-09 断言宿主优先加载 `Modules\` 旧 DLL → 源码已修但检查仍红  [✅]  最近：2026-10-27  复发：1 次
- **现象**：JudgeTable 源码已补动态端口实现，`--judge` 仍报 `输入=[] 输出=[]`、
  `输入端口不存在：实际_外径`；`audit_artifacts` 同时报 `STALE_DLL`。
- **触发场景 / 复现**：`JudgeTableChecks` 用 `Assembly.LoadFrom` 先取 `Modules\Plugin.JudgeTable.dll`，
  取不到才回落插件 bin；Modules 里是旧构建。
- **原因**：插件源码改了但没重建/没刷新 Modules；断言宿主测的是旧 DLL。
- **解决办法**：重建插件并把新 DLL 复制到 `Modules\`（宿主在跑时先关，或按 A-01 改投临时目录），
  再跑 `--judge`；判据看 Modules DLL mtime ≥ 源码、`HELP_RES` 数 = 磁盘 `.md` 数。
- **规避建议**：看到“源码已改但断言仍红”，先核 `Modules\` 产物的 mtime/内嵌资源数，再看源码；
  不要把旧 DLL 的红当成实现没修。

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

#### F-03 旧 LineSim 占 19510，MotionSimProbe 起不来  [⚠️]  最近：2026-10-27  复发：2 次
- **现象**：`LineSim` 报 `AddressAlreadyInUse`；MotionSimProbe 轴使能/回零/定位全部失败（5 ok / 7 fail）。
  **2026-10-27 补变体**：占着端口的旧实例会让**断言宿主**里的运动轴断言也红，且现象看着像产品缺陷——
  `FlowCanvasChecks` 报 `前置：第一条 Move 已让轴动起来 → Moving=False 位置=0.00`，紧接着
  `★运动中第二条 Move 被拒 → 返回=Accepted 建议=读不到轴「X」的状态…请确认运动卡已连接`。
- **触发场景 / 复现**：机器上已有 2026-10-25 启动的 `LineSim.exe --serve --web --demo`（PID 14588）占 19510；
  探针又起一个仿真器，绑定失败；或宿主里的运动断言连到这台旧模拟器（轴名/状态对不上）。
- **原因**：仿真器端口固定；旧实例未退出。
- **解决办法**：确认旧实例归属后停止，再跑探针/宿主；不确定就记 `BLOCKED`，**不要杀别人的进程**。
- **规避建议**：跑 MotionSim/CameraSim 前 `netstat -ano | findstr ":19510"` + 看进程启动时间/命令行。
  **判据**：运动轴断言整段红、且回执里出现"读不到轴…状态轮询"→ 先查端口，别先怀疑代码。

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

#### G-04 `fft_image_inv` 只输出 byte，负值被截断（半波整流）  [✅]  最近：2026-10-27  复发：1 次
- **现象**：高通/带通/陷波的结果本应有一半负值，用 `fft_image_inv` 反变换后全变成 0~正值；
  带通测试里"高频参照物"没被滤掉（A/B 仍是 1.99），看起来像滤波器没生效。
- **触发场景 / 复现**：`fft_image → convol_fft → fft_image_inv`，输入含负值（-100~100）。
- **原因**：23.05 的 `fft_image_inv` 输出类型是 **byte**，负值被截到 0；半波整流产生的二次谐波
  正好落在高频参照 bin 上，误导断言。
- **解决办法**：反向改用 `fft_generic(..., 'from_freq', 1, 'sqrt', 'dc_center', 'real')`，输出 real、保留负值；
  正向用 `fft_generic(..., 'to_freq', -1, 'sqrt', 'dc_center', 'complex')`（与 `fft_image` 逐像素相同）。
- **规避建议**：判据加"反变换后 `get_image_type` 必须是 real，且最小值可以为负"；
  任何"滤波后看起来没变化"先查输出类型，不要先怀疑掩膜。
- **相关命令 / 参数**：
  ```text
  fft_generic(Image, FFT, 'to_freq', -1, 'sqrt', 'dc_center', 'complex')
  fft_generic(FFT, Result, 'from_freq', 1, 'sqrt', 'dc_center', 'real')
  ```

#### G-05 `gen_filter_mask` 23.05 只认 Laws 纹理，做不了低通/带通  [✅]  最近：2026-10-27  复发：1 次
- **现象**：按记忆用 `gen_filter_mask` 生成低通/带通掩膜，结果参数语义对不上或直接报错。
- **触发场景 / 复现**：`gen_filter_mask(ImageFilter, FilterMask, Scale, W, H)`。
- **原因**：23.05 的 `FilterMask` 只接受 `'laws_ee'/'laws_ss'/'laws_rr'` 这类 Laws 纹理掩膜；
  `gen_lowpass/gen_highpass/gen_bandpass` 只有理想型，且 `CutoffFrequency` 以 Nyquist=1.0 计
  （与本仓 0..0.5 口径差一倍）；高斯/巴特沃斯没有现成算子。
- **解决办法**：按"到直流的距离"逐像素解析生成掩膜（`float[]` + `gen_image1('real', W, H, buf)`），
  再用 `gen_lowpass` / `gen_gauss_filter` 做对拍确认口径（实测逐像素差 0）。
- **规避建议**：频域掩膜先写一个最小探针对拍 `gen_lowpass`；别把"记忆里的算子"直接写进产品代码。
- **相关命令 / 参数**：
  ```text
  gen_image1('real', W, H, float[])
  gen_lowpass / gen_gauss_filter   # 仅用于对拍
  ```

#### G-06 `ignore_polarity` 不是合法 metric → `#1306/#1309/#1312`  [✅]  最近：2026-10-27  复发：1 次
- **现象**：`UsePolarity=false` 的模板一学习就失败，报 "Wrong value of control parameter"。
- **触发场景 / 复现**：`create_shape_model / create_scaled_shape_model / create_aniso_shape_model`
  的 `Metric` 参数传 `"ignore_polarity"`。
- **原因**：HALCON 23.05 合法值只有 `use_polarity / ignore_global_polarity / ignore_local_polarity / ignore_color_polarity`；
  `ignore_polarity` 是伪值。"忽略极性"的语义对应 `ignore_global_polarity`（整体对比度反相也能找到）。
- **解决办法**：统一走 `MetricName()`；三个 create 分支共用，新增分支必须复用它。
- **规避建议**：枚举/字符串型 HALCON 参数先查 `%HALCONROOT%\doc\html\reference\operators\*.html` 的合法值表；
  断言要覆盖 `UsePolarity=false` 这一档（否则伪值会一直藏着）。
- **相关命令 / 参数**：
  ```text
  Metric = UsePolarity ? "use_polarity" : "ignore_global_polarity"
  ```

#### G-07 `gray_histo` 对 1 通道 uint2 的箱数与预期不符（直方图指标直接失败）  [✅]  最近：2026-10-27  复发：1 次
- **现象**：uint2 图的质量指标全 0、`Success=false`；byte 图同图案正常。
- **触发场景 / 复现**：`gray_histo(domain, uint2Image, ...)` 后按 65536 箱检查 `absHist.Length`，1 通道 uint2 图抛"箱数少于预期"。
- **原因**：23.05 的 `gray_histo` 对 1 通道 uint2 返回的箱数与"每灰度值一箱"的预期不一致（实测少箱）；byte 图 256 箱正常。
- **解决办法**：改用 `gray_histo_range(domain, image, 0, 满量程, 精确箱数)`：byte 256 箱、uint2 65536 箱，截断/分位都精确。
- **规避建议**：凡是"按位深决定箱数"的直方图代码，别依赖 `gray_histo` 的长度；用 `gray_histo_range` 显式给 Min/Max/NumBins，并断言 `Length == NumBins`。
- **相关命令 / 参数**：
  ```text
  gray_histo_range(Domain, Image, 0, 65535, 65536, Hist, BinSize)
  ```

#### G-08 `scale_image` 在 byte 上会截顶（输出仍是 byte）  [✅]  最近：2026-10-27  复发：1 次
- **现象**：想造 0/65535 的 uint2 夹具，`scale_image(byteImage, 257)` 后转 uint2，得到的是 0/255，归一化用例比值 0.004。
- **触发场景 / 复现**：byte 0/255 图 → `scale_image(..., 257.0, 0.0)` → `convert_image_type(..., "uint2")`。
- **原因**：`scale_image` 的输出类型与输入相同；byte 输入乘 257 后先截到 255，再转 uint2 只能得到 0/255。
- **解决办法**：先 `convert_image_type(byte, "uint2")`（0/255），再在 **uint2** 上 `scale_image(..., 257.0, 0.0)`（0/65535，不截顶）。
- **规避建议**：造跨位深夹具时先确认"每一步的输出类型"；用 `min_max_gray` 断言夹具真的到 65535，别只看 `get_image_type`。
- **相关命令 / 参数**：
  ```text
  convert_image_type(ByteImage, U2Low, "uint2")
  scale_image(U2Low, U2Full, 257.0, 0.0)
  min_max_gray(U2Full, U2Full, 0, Min, Max, Range)
  ```

#### G-09 HALCON 投影算子签名/参数口径（`projective_trans_region` #1302、`projective_trans_point_2d` 7 参）  [✅]  最近：2026-10-27  复发：1 次
- **现象**：`projective_trans_region(rect, out region, hom, "false")` 报 `#1302 Wrong value of control parameter 2`；
  `projective_trans_point_2d(hom, row, col, out qr, out qc)` 编译报缺少 `qy` 参数。
- **触发场景 / 复现**：ImageAlign 用投影变换生成有效区、映射点对。
- **原因**：23.05 的 `projective_trans_region` 第 2 个控制参数（`AdaptRegion`）取值口径与预期不符；
  `projective_trans_point_2d` 是 **7 参齐次重载** `(HomMat2D, Px, Py, Pw, out Qx, out Qy, out Qw)`，不是 6 参。
- **解决办法**：
  - 有效区改用“输入图 4 角点 → `gen_region_polygon_filled` → 与目标域求交”，对仿射/透视都成立；
  - 点映射用 7 参重载，`Pw=1.0`，输出除以 `Qw`。
- **规避建议**：HALCON 投影族算子先看托管重载的参数个数/是否齐次；有效区别依赖 `AdaptRegion` 的字符串/整数口径。
- **相关命令 / 参数**：
  ```text
  projective_trans_point_2d(HomMat2D, Px, Py, 1.0, Qx, Qy, Qw)  # 再除 Qw
  gen_region_polygon_filled(Rows, Cols, Region)
  intersection(Region, DestDomain, Valid)
  ```

#### C-40 界面"卡死"但 `Process.Responding=True`：其实是模态弹窗把主窗置灰  [✅]  最近：2026-10-27  复发：1 次
- **现象**：用户报"界面卡死"，点主界面任何地方都没有反馈；但 `(Get-Process).Responding` 返回 **True**，
  CPU 采样显示 8~9 个核在满转（像是死循环），日志还在实时增长（像是没死锁）。三种常见判据互相矛盾。
- **触发场景 / 复现**：`VisionMaster.exe` 里对 7 条样例流程发起「循环运行」，随后双击某步骤打开了步骤参数弹窗
  （`PluginConfigShell`，标题就是步骤名，如 `读取PCB散焦图像`）。此时主界面完全不可点。
- **原因**：`ShowDialog` 期间 WPF 把**属主窗口**置 `IsWindowEnabled = False`。
  置灰窗口的点击根本不进消息处理 → 用户感知"卡死"；而 UI 线程仍在泵消息 → `Responding` 为 True。
  真正把 UI 线程压到 73% 的是并行的"日志 1000 条/秒各投一个 Normal 优先级 UI 动作"与"图像集按来帧速率提交"，
  它让弹窗反应迟钝，但不是"点不动"的原因。
- **解决办法**：
  1. 枚举进程的**可见顶层窗口**，逐个看 `IsWindowEnabled`：主窗 `False` + 另有一个可见子窗（`GetWindow(h, GW_OWNER)` 指回主窗）＝ 有模态弹窗；
  2. 该弹窗是 WPF 窗（类名 `HwndWrapper[...]`），**`EnumChildWindows` 返回 0 个**，必须用 UI Automation 读内容：
     ```powershell
     Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
     $el=[System.Windows.Automation.AutomationElement]::FromHandle([IntPtr]$hwnd)
     # 再用 TreeWalker.ControlViewWalker 递归，才能看到「执行 / 取消 / 确认」这些按钮文本
     ```
  3. 想确认"是不是真卡住"：`SendMessageTimeout(hwnd, WM_NULL, ..., 300ms)` 往返毫秒数（本例 11~15ms）——
     能往返就说明 UI 线程在泵消息，问题不是死锁。
- **规避建议**：**别用 `Process.Responding` 判"界面能不能用"**。判据顺序：
  ① 主窗 `IsWindowEnabled` → ② 枚举可见顶层窗找模态子窗 → ③ `WM_NULL` 往返测泵是否活 → ④ 线程 CPU 时间判是否空转。
  CPU 满转 + 日志在涨 + 主窗 Enabled=False 三者同时出现，就是"模态弹窗 + 后台重负载"，不是死锁。
- **相关命令 / 参数**：本例实测值——UI 线程 5s/3641ms（73%）、16 核占 8.7~9.1 核、工作集 831→834MB（稳态）、
  日志 141~154 KB/s（≈1000 行/秒）。取证过程见 `docs/code-changes/2026-10-27-界面卡死诊断（循环运行日志洪水模态弹窗）.md`。

#### C-41 构建红在"别人正在改的文件"里：先看 mtime，别顺手修  [✅]  最近：2026-10-27  复发：1 次
- **现象**：自己只改了 4 个文件（`LogViewModel` / `ImageCollectionService` / `EditGate` / `ShellViewModel`），
  `dotnet build FlowCanvasChecks.csproj` 却红在 `Plugins\Plugin.ResultDisplay\Services\DisplayDraw.cs(224,298)`：
  `error CS0266: 无法将类型"double"隐式转换为"int"`。代码是 `int width = a.LineWidth > 0 ? a.LineWidth : 2;`。
- **触发场景 / 复现**：同一台机器上**另一个会话正在改同一个仓库**（本轮：SCADA 属性页 + 结果呈现插件），
  自己的构建把它半成品的文件一起编了。
- **原因**：对方把 `LineWidth` 从 `int` 改成 `double`，这一行的 `?:` 结果类型跟着变成 `double`，赋给 `int` 就报 CS0266；
  文件 mtime 就在 1~2 分钟内（23:15:14 → 23:17:36 又改了一次）。这是"在飞改动"，不是既有缺陷。
- **解决办法**：
  1. `Get-Item <报错文件> | Select LastWriteTime` —— 最近几分钟被改过 = 别人的在飞改动；
  2. **不要顺手修**（会撞掉对方意图，或下一轮被对方覆盖）；也不要删 obj / `-t:Rebuild` 硬刷；
  3. 换一条验证路径：先构建/运行**不依赖该工程**的宿主（本轮改用 `ScadaChecks`，3177/0 全绿），
     等对方收尾后再补跑被挡住的宿主。
- **规避建议**：开工前与收工前各扫一遍最近 45 分钟被改过的源文件，先给红点定归属：
  ```powershell
  $cut=(Get-Date).AddMinutes(-45)
  Get-ChildItem -Path 'D:\C#\VM\VisionMaster','D:\C#\VM\UI','D:\C#\VM\Core','D:\C#\VM\Plugins' -Recurse -Include '*.cs','*.xaml' |
    Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' -and $_.LastWriteTime -gt $cut } |
    Sort-Object LastWriteTime -Descending | Select-Object LastWriteTime, FullName
  ```
  汇报里必须把"哪条红是别人的在飞改动"写清楚，**不要混进自己的交付结论**（也不要把别人的红说成"已验证通过"）。
- **相关命令 / 参数**：本轮实证——对方半成品红 1 条（`A1b OverlayContours`，同属 `Plugin.ResultDisplay`），
  与自己的改动无交集（自己的 4 个文件不在标注生成路径上）。等对方收尾后需复跑一次确认。

#### C-42 残留断言宿主进程锁住 exe/DLL：构建报 MSB3027/MSB3021，先结束进程  [✅]  最近：2026-10-27  复发：2 次
- **现象**：`dotnet build FlowCanvasChecks.csproj` 报
  `MSB3027: 无法将 apphost.exe 复制到 ...FlowCanvasChecks.exe。文件被"FlowCanvasChecks (14248)"锁定`，
  或 `MSB3021: ... Plugin.ResultDisplay.dll ... 被"FlowCanvasChecks (20320)"锁定`。退出码 1、0 个错误。
- **触发场景 / 复现**：上一轮 `run_gates.ps1` 或手工跑完断言宿主后，进程**没有立刻退出**
  （宿主里有后台线程/STA 泵时常见）；下一次构建或另一个会话的构建就撞锁。
- **原因**：Windows 文件锁由进程持有；构建要覆盖 `bin\...\*.exe` / `*.dll` 时重试 10 次后放弃。
  这不是代码问题，也不是"构建工具坏了"。
- **解决办法**：
  1. 看错误里的 PID，确认它就是断言宿主：`tasklist /FI "PID eq <pid>"`；
  2. `taskkill /PID <pid> /F`；
  3. 重新构建（**不要**删 bin/obj 或 `-t:Rebuild` 硬刷，那会丢掉增量证据）。
- **规避建议**：跑完宿主后等 2~3 秒再构建；批量构建前先
  `tasklist /FI "IMAGENAME eq FlowCanvasChecks.exe"`，有残留先结束。
  汇报里要写清"红是文件锁，不是断言失败"——两者都在构建日志里，但只有后者要改代码。
- **相关命令 / 参数**：`tasklist /FI "PID eq <pid>"`、`taskkill /PID <pid> /F`。

### H. 浏览器 / Web 设计稿截图

#### H-01 `file://` 路径含 `#` 或中文 → 截图 `NO FILE` / 打开错页  [✅]  最近：2026-10-27  复发：1 次
- **现象**：`msedge --headless=new --screenshot=... "file:///D:/C#/VM/docs/.../design.html"` 退出后没有产物；
  或页面看似打开但停在片段位置（内容不完整）。
- **触发场景 / 复现**：仓库根是 `D:\C#\VM`（含 `#`），设计稿在中文目录 `docs\UI设计基线\...` 下，
  直接字符串拼 `file:///` + `Replace('\','/')`。
- **原因**：URL 里 `#` 是 fragment 分隔符，`C#` 之后全被当成片段；中文/空格未 percent-encode 时
  浏览器也可能找不到文件。`--screenshot` 不报错，只是不写文件（或写了错误页）。
- **解决办法**：用 `[System.Uri]::new($path).AbsoluteUri` 生成 URL（自动得到 `C%23` 与中文编码）：
  ```powershell
  $url = [System.Uri]::new('D:\C#\VM\docs\UI设计基线\scada-property-panel\design.html').AbsoluteUri
  # file:///D:/C%23/VM/docs/UI%E8%AE%BE%E8%AE%A1%E5%9F%BA%E7%BA%BF/scada-property-panel/design.html
  ```
- **规避建议**：所有 `file://` 截图命令先 `Write-Output $url` 并确认含 `%23`；**不要手拼**。
  截图后立刻用 `[System.Drawing.Image]::FromFile($out)` 校验宽高，文件不存在直接判失败，不要继续看图。
- **相关命令 / 参数**：`design.html?shot=s2&only=page` 这类查询串在 URL 里原样保留；
  PowerShell 里用单引号包 URL，避免 `&` 被当成命令分隔符。

#### H-02 无头 Edge 批量截图尺寸被忽略 / 默认 DPI 不是 1  [⚠️]  最近：2026-10-28  复发：3 次
- **现象**：① 一个 PowerShell 循环连续截 8 张，全部输出 **756×488**（默认视口），
  `--window-size=300,1050` 完全无效；② 单张布局核对图设 `--window-size=1500,1200`，
  实际只有 1200 CSS 宽，1280 宽的主稿右侧被裁。
- **触发场景 / 复现**：循环里连续 `& msedge --headless=new ... --window-size=... --screenshot=...`；
  或未显式给 `--force-device-scale-factor`（本机默认 DPI 缩放 ≠ 1）。
- **原因**：① 前一个 Edge 进程未完全退出 / 复用了浏览器进程，新实例的窗口参数被忽略，
  截图按默认视口出；② `--screenshot` 输出的是物理像素，默认 DPI 下 1500 物理像素 ≠ 1500 CSS 像素。
- **解决办法**：
  1. **一次只跑一张**；每张用唯一 `--user-data-dir="$env:TEMP\edge-shot-<guid>"`；
  2. 加 `--virtual-time-budget=3000` 等布局/JS 稳定；
  3. 布局核对加 `--force-device-scale-factor=1`；要清晰证据再改 `2`（输出 2×）；
  4. **批量出图的正解（2026-10-28 实证 8/8）**：`& msedge ...` 返回的是启动器、进程没等——
     改用 `[System.Diagnostics.Process]::Start($psi)` + `WaitForExit(25000)`（超时 Kill）逐张等待；
     `$psi.Arguments` 里路径一律用引号包住（中文/空格），`UseShellExecute=$false`。
- **规避建议**：截图后立刻校验 `Image.Width/Height` 等于预期；不等就重跑，**不要拿错图当证据**。
- **相关命令 / 参数**：完整命令见 `docs/UI设计基线/scada-property-panel/README.md` §9；
  v2 的 8 张证据全部按「Process.Start + WaitForExit」生成。

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
| 2026-10-26（HALCON 样例流程） | C-13、C-15 | C-17 | 手写 `.vms` 的模型真源是 `TemplateLibraryJson`；加载后补 Home/Main/End；用 Load→Compile→真插件三步验证 |
| 2026-10-26（散焦 PCB 形状匹配） | C-13、C-15、C-17 | C-18 | 对拍前先确认 `NumLevels` 是单值还是 `[起始层, 跟踪层]`；散焦/低对比场景跟踪层设 -1 |
| 2026-10-26（标签 Blob 动态阈值） | C-13、C-15、C-17 | —（C-17 补连线端口名变体） | `LinkedSources` 键 = 下游输入端口，`TargetPortName` = 上游输出端口；不同名时才会暴露 |
| 2026-10-27（资产管理夜间测试） | A-01、B-01、C-01、E-01 | C-20、C-21、C-22、C-23、D-04、E-04、E-05、F-03 | UIA 优先走模式（ExpandCollapse/Invoke），原生对话框用 `WindowPattern.Close`；探针假红先查源码/XAML 再判产品缺陷；夹具按方案名建目录 |
| 2026-10-27（FFT/各向异性） | C-25、C-26、C-27、C-28 | G-04、G-05、G-06 | 频域滤波先确认反变换输出类型（real vs byte）；各向异性与等比模型类型不匹配是静默的，必须留学习快照 + 运行期 Warn；`ignore_polarity` 是伪值 |
| 2026-10-27（两份插件提案评审） | C-05、C-25 | C-29 | 真图数据集先做"数据体检"（尺寸/通道/清晰度走向）再写验收判据：`pcb_focus` 是过焦点扫描不是单调散焦，三套真图全是 8-bit 灰度 |
| 2026-10-27（ImageQuality v1） | C-05、C-12、C-13、C-27、C-29 | G-07、G-08 | 直方图按位深用 `gray_histo_range`；跨位深夹具先换类型再 `scale`；断言宿主输出走文件重定向并清理残留进程 |
| 2026-10-27（ImageAlign v1） | C-12、C-13、C-27、G-07、G-08 | G-09、E-06、E-07 | 4 点透视残差恒 0，错点检测必须加验证点；有效区用四角点多边形；投影点映射是 7 参齐次；夹具生命周期要包住 Execute；RANSAC 错配点不能整体一致 |
| 2026-10-27（JudgeTable 收尾） | C-12、C-13、C-27、E-06 | E-09 | 字段初始化器创建的集合不经过 setter，必须显式订阅 CollectionChanged；断言宿主优先加载 Modules，源码修了也要刷新产物 |
| 2026-10-27（操作记录弹窗三项补全） | B-02、C-01 | — | C-01 补「PS 重定向 `>`/`*>` 同样解码乱码」变体；A-01/C-12/C-13 各补并发构建变体（CS2012 抢 obj / 产物缺 BAML→运行期找不到资源 / 锁 Modules 的 MSB3073）；新增 D-05（本机无 git PATH → PortableGit 全路径 + 只 add 本工作流文件） |
| 2026-10-27（操作记录弹窗最后四项） | B-02、C-12、C-13 | E-08 | 异步查询必须带版本号丢弃过期结果；"慢查询"要用 rollback journal + `BEGIN EXCLUSIVE` 才造得出来（WAL 下写锁挡不住读）；骨架门限要有正反两条断言（慢→亮、快→不闪） |
| 2026-10-27（插件工具箱 v2 复刻） | C-07、B-04、C-08 | —（C-07/B-04 各补变体） | 16px 胶囊右移 6px 后只有左半区可点，点中心会假"没反应"；`audit_artifacts` 的 `STALE_DLL` 先看是不是并发会话改了别的插件 Help，等对方重建后单独复跑，不要替别人重建 |
| 2026-10-27（胶囊与库页签去重） | C-33、C-07、B-04 | —（C-33 新建） | 自定义覆盖层与库页签重叠，先用 UIA 枚举同区域确认是两套 UI；只按 `ContentId` 隐藏 `Visibility`，不动 `IsAutoHidden`/`CanAutoHide`；无窗口宿主不生成 `LayoutAnchorControl`，要屏幕外 `Show()` |
| 2026-10-27（vm-mcp 上机测试服务） | A-01、C-01、C-07、C-09、C-20、C-21、C-22、C-25、C-26 | C-30、C-31、C-32 | 把台账做成可执行工具：13 个 MCP 工具各自固化一条坑；新增"弹出窗口是独立 HWND，PrintWindow 拍不到""launcher pid ≠ 窗口 pid / 最小化时 UIA 矩形为空""私有区字形要转义成 \\uXXXX 才可定位" |
| 2026-10-27（vm-mcp 第二批） | C-20、C-21、C-26、R35、G-07、G-08、C-29 | C-34、C-35 | 补齐输入/拖拽/滚轮/自断言与图像体检（22 个工具）；新增"布局容器没有 automation peer，不进 UIA 树""SetValue 不产生输入事件，R35 类控件必须真实按键"；图像侧用拉普拉斯方差识别对焦扫描集，判据取峰/中位比而非单调性 |
| 2026-10-27（vm-mcp 真机试跑） | C-20、C-21、C-26、C-32 | C-36、C-37、C-38 | **自测绿 ≠ 真机可用**：真机第一次跑就暴露"启动画面当主窗""草稿框阻塞""按名字拿错 ref"三个缺陷；判据从"窗口存在"改成"目标元素可操作"，并新增 `vm_windows` 专看全部窗口 |
| 2026-10-27（SCADA 属性面板 Web 设计稿） | C-05、C-07 | H-01、H-02 | 截图前先打印 URL（`#`→`%23`）；批量截图改一次一张 + 唯一 profile + `--virtual-time-budget`，并用图片宽高自检 |

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
| 2026-10-26 | HALCON 样例流程：新增“手写 .vms 的模型/骨架流程/残留宿主”一条 | C-17 | — |
| 2026-10-26 | 散焦 PCB 形状匹配：新增“`find_shape_model` 跟踪层被漏掉”一条 | C-18 | — |
| 2026-10-26 | 标签 Blob 样例：C-17 补“`LinkedSources` 键 vs 上游输出端口名”变体 | — | C-17（补变体，复发 +1） |
| 2026-10-26 | 可形变模板匹配：新增“`deformation_smoothness` 整数 + 对拍参数同源”一条 | C-19 | — |
| 2026-10-27 | 可形变样例流程：C-13 补“锁宿主 bin”变体；两条 `.vms` 通过 Load→Compile→Execute | — | C-13（补变体，复发 +1） |
| 2026-10-27 | 可形变模板资产：新增“资产页引用碰巧命中/外部来源仍写模型/本地路径基准错”一条 | C-24 | — |
| 2026-10-27 | 资产管理夜间测试：gate 14/14、资产探针 238/0、资产页 UI 冒烟 14 项、17 探针结果；新增 UIA/原生对话框/探针假红/夹具命名/端口冲突 8 条 | C-20、C-21、C-22、C-23、D-04、E-04、E-05、F-03 | A-01（FlowCanvasChecks 锁 Modules 变体）、C-12（单构建缓存陈旧变体） |
| 2026-10-27 | 算子对齐 HALCON 收尾：FlowCanvasChecks 3962/0、HelpProbe 全通过、audit_artifacts OK；新增“日志按 936 写、UTF-8 回读零命中”一条 | C-25 | — |
| 2026-10-27 | 真机复跑：GUI 跑通 NCC/PCB/Code128/QR，Blob 用 `--sample-flows` 进程内复跑（37/0）；新增“合成输入送不进前台被占的应用”和“整跑后进程不退”两条 | C-26、C-27 | — |
| 2026-10-27 | 加完 `--sample-flows` 连跑两次默认套件：功能全绿、计时类各红一条（条目不同）；新增“计时断言负载抖动”一条 | C-28 | — |
| 2026-10-27 | FFT 频域滤波 + 各向异性缩放匹配落地：FlowCanvasChecks 4014/0（FFT 21 条、各向异性 16 条）、run_gates 14/14；新增 HALCON 频域/极性三条 | G-04、G-05、G-06 | — |
| 2026-10-27 | Ocr 结构化输出落地：10 条断言、FlowCanvasChecks 4024/0、run_gates 14/14；帮助里写清 `Text` 含 U+001A、`CharBoxes` stride 4 | — | — |
| 2026-10-27 | 两份新插件提案评审：数据体检发现 `pcb_focus` 过焦点扫描、真图全灰度；新增"手算 PNG 头尺寸错误"一条 | C-29 | — |
| 2026-10-27 | 图像质量前置检查插件 v1：51 条断言全绿、8 页手册、sln/Tiers 登记；新增 uint2 直方图与 `scale_image` 截顶两条 HALCON 坑 | G-07、G-08 | C-27（补管道变体，复发 +1） |
| 2026-10-27 | 图像对齐插件 v1：75 条断言全绿、8 页手册、sln/Tiers 登记；新增投影算子签名与夹具生命周期两条坑 | G-09、E-06 | — |
| 2026-10-27 | 图像对齐 v1.1：>4 点 RANSAC + 5MP 实测（Debug 4.9ms）；80 条断言全绿；新增 RANSAC 错配点一致性坑 | E-07 | — |
| 2026-10-27 | 判定表收尾：Rules 构造订阅 + 输出重名后缀位置 + Tiers/Modules 刷新；`--judge` 45/45、全量 4217/0；新增 Modules 旧 DLL 假红一条 | E-09 | — |
| 2026-10-27 | 操作记录弹窗三项补全：探针 353/0（新增 9 条）；门禁复跑至 12/14（余 2 红=Plugin.JudgeTable）；补 C-01/A-01/C-12/C-13 并发构建变体；新增「本机无 git PATH」一条 | D-05 | C-01、A-01、C-12、C-13（补变体） |
| 2026-10-27 | 操作记录弹窗最后四项（防抖 250/300、异步查询 + 加载骨架 ≥300ms、焦点第一行）：探针 364/0、四态出图；新增"WAL 下写锁挡不住读"一条 | E-08 | — |
| 2026-10-27 | 插件工具箱 v2 WPF 复刻：`_ToolboxProbe` 33/0、`UIThemeSmokeTest` 141/0、完整 gate 首次 13/14（唯一红=`Plugin.ImageAlign` 并发旧 DLL，对方重建后 `-SkipBuild` 复跑 14/14）；补 16px 胶囊左半区点击与并发 `STALE_DLL` 两条变体 | — | C-07、B-04（补变体，复发 +1） |
| 2026-10-27 | vm-mcp 上机测试服务落地：13 个 MCP 工具、`smoke.mjs` 13/13、`uismoke.mjs` 14/14、真机树/截图/日志/构建全通；新增三条 GUI 侧坑并给 A-01、C-04 各补一条变体 | C-30、C-31、C-32 | A-01（VBCSCompiler 不是构建）、C-04（`-Command` 拼 JSON 被转义） |
| 2026-10-27 | 工具箱胶囊与库自动隐藏页签去重：UIA 定位 33px `LayoutAnchorControl` 与 18px 胶囊重叠；按 `ContentId` 隐藏库页签（只改 `Visibility`）+ 2 条运行时断言（含反证），冒烟 143/0、完整 gate 14/14 | C-33 | — |
| 2026-10-27 | vm-mcp 第二批：补 `vm_ui_type`/`vm_ui_drag`/`vm_ui_scroll`/`vm_assert` 与 5 个图像体检工具（共 22 个）；三套冒烟 15/9/27 全绿；真图验证 `pcb_focus` 121 张被识别为对焦扫描集（锐度峰 137.9 vs 中位 4.6）；新增两条 UIA 侧坑 | C-34、C-35 | — |
| 2026-10-27 | 真机试跑暴露三个缺陷并修复：`vm_start` 把无标题启动画面当主窗（改"有标题+尺寸下限"，新增 `vm_windows`）；草稿恢复框阻塞界面（固化为启动第一步）；按名字取 ref 拿到 Text（`vm_ui_act` 支持按名字定位并自动挑支持模式的元素）。冒烟 16/9/29 全绿 | C-36、C-37、C-38 | — |
| 2026-10-27 | **屏蔽 `vm-tools` MCP**：AGENTS.md 新增 R186 + 工作区 `.zcode\config.json` 置 `enabled:false` + `register-vm-mcp.mjs` 默认拒绝（需 `--force`）；测试回到 run_gates / 断言宿主 / 探针三层 | C-39 | — |
| 2026-10-27 | 真机"界面卡死"排查：确认非死锁（8.7~9.1 核空转、内存稳态、消息泵 11~15ms），定位为模态步骤弹窗置灰主窗 + 日志 1000 条/秒各投一个 Normal 优先级 UI 动作（UI 线程 73%）；新增"Responding 不可信"一条，C-09 补"元数据陈旧"变体 | C-40 | C-09（补变体，复发 +1） |
| 2026-10-27 | 卡死三条改完验证：日志 10Hz 批量投递、图像集 50ms 提交限速、EditGate 入树补种运行态；ScadaChecks 3177/0（新增 5 条批量追加断言）、FlowCanvasChecks 4240/2（与本次相关的 15 条全绿；2 红为 F-03 端口被 LineSim 占，另 `A1b` 首轮红系并行会话在改 `Plugin.ResultDisplay`，末轮已绿）；新增"构建红在别人文件里"一条，A-01 补 `MSB3061` 变体 | C-41 | A-01、F-03（各补变体，复发 +1） |
| 2026-10-27 | 结果与呈现分离 P1–P5：结果显示补 HRegion 覆盖层/逐对象编号/BakeOverlay；五个算法插件补几何口（MatchContours / CaliperGeometry+MeasureLine / ReferenceContour）与 `KeepAnnotatedImage`；样例方案 7 条流程迁移；FlowCanvasChecks 4242/0、run_gates 14/14；新增"残留宿主进程锁 DLL"一条 | C-42 | — |
| 2026-10-27 | SCADA 属性面板 Web 设计稿 v1：新增浏览器截图两条（`file://` 的 `#`/中文编码、无头 Edge 尺寸与 DPI 竞态）；设计稿 8 张证据、`audit_docs` / `audit_root` 全绿 | H-01、H-02 | — |
| 2026-10-28 | SCADA 属性面板 Web 设计稿 v2 重构：H-02 补「Process.Start + WaitForExit(25000) 逐张等待」正解（8/8 尺寸正确）；新增「flex 列容器子项被压缩」一条页面级坑（见 code-changes v2 记录） | — | H-02（补解法，复发 +1） |
| 2026-10-28 | SCADA 属性面板 v2.1 修订（移除类型行、曲线卡绑定改轻量标注）：新增 PowerShell 大小写变量冲突一条 | C-42 | — |
| 2026-10-28 | AGENTS.md 注入层下沉（§16/§25 → 分册）+ 新增 §42「AI 生成 `.vms`」：`audit_root` 因**他进程**遗留的 `$null` 报红，按 R166 移入 `artifacts\logs\20261028\`（未删）；gate 用 `-Only docs_index,audit_root,audit_artifacts,audit_docs,sync_constraints` 跑通 5/5 | — | C-06（复发 +1，补「先看 CreationTime + 内容判归属」）、C-39（补 cmd `;` 变体，复发 +1） |
| 2026-10-28 | 组态运行态页面菜单白条修复：`PageMenuContextStyle` 补深色 `ControlTemplate`（Aero2 默认模板的 28 DIP 浅色图标栏）、新增 ScadaChecks P0-3 浅色底块断言（真宿主 `light=0` 通过）；模板级探针做变异反证（删 Template setter → 复现三条浅色矩形）。ScadaChecks 2942/3（3 红全在并发重构中的属性面板段落）、`UIThemeSmokeTest` 因对方 `ScadaPropertyView.xaml(649)` MC3088 构建不过。新增 `timeout /t` 失效与后台输出缓冲两条，C-39 补「构建版」变体 | C-44、C-45 | C-39（补构建版变体，复发 +1） |
| 2026-10-28 | 停靠布局"工具箱与流程栏共容器"：`LayoutHelper` 补列归属判据（加载即纠正 + 显示路径纠正 + 兜底落点排除左列），`UIThemeSmokeTest` 新增 ③a 并做变异反证；冒烟 exit 0、`run_gates` 16 步全绿（`DataPipelineChecks` 首轮构建瞬时红，`-Only` 单步复跑转绿）。B-01 补「gate 内构建也会瞬时红」变体 | — | B-01（补变体，复发 +1） |
| 2026-10-28 | 多流程底栏 KPI / 工位浮窗 / 轮次下钻真机验证：测试方案副本 + AppConfig 切换（备份/还原），真机跑通主流程 1 轮（Main 45,153ms / OK 1 / 工位 7 条各 1 轮），浮窗与下钻正常；实机发现"子程序一刀切排除导致工位行置灰、N 条在跑只算主流程"与"停止后节拍变 —"两个缺陷并修复。新增关窗/虚拟化/参数同名/方案副本四条坑 | C-46、C-47、C-48、C-49 | — |
| 2026-10-28 | 整线启动落地（工位与主流程平级）：`FlowInvokeType` 加 `LineStart` 位、`FlowBatchSelector` 改选勾选流程（无勾选回落旧口径）、工具栏改「启动整线 / 整线循环」、放开运行中启动别的流程、流程管理加第五个勾选框；`FlowCanvasChecks` 4286/0（E20 重写 11 条）。宿主运行中锁 `bin\`+`Modules\`，改用隔离输出目录构建；清理该目录时误删他人批次产物 | C-50 | — |
| 2026-10-28 | 结果源配置弹窗 WPF 复刻：右键入口 + 只读探针 + 五模式候选；DLG-1~17 全绿（4304/0），结构闸 3 条。新增 Prism 关窗回调伪造致宿主崩溃、XAML 注释 `--` 两条坑；期间另一会话改 `FlowManagerView.xaml` 报 `MC3024`，按 C-41 未修、用 `-p:BuildProjectReferences=false` 先验宿主 | C-51、C-52 | C-41（补"另一会话改 XAML"变体） |
| 2026-10-28 | 结果源弹窗真机视觉验收：Computer Use 打开弹窗并截图（自动 / 结果变量两态），发现「预选模式未发通知 → 左栏不高亮」并修复；新增右键菜单跨调用点不到一条 | C-53 | — |
| 2026-10-28 | 结果上报字段端口接线：构造器补 `RebuildDynamicInputs`（扫描期快照缺默认 `sn`/`result`）、字段行加「绑定…」/解绑走 `LinkPathEvent` 单绑定、确认时迁移改名连线；Debug 0 错、内嵌 Help 6/6、`run_gates` 复跑 **16/16 绿**；新增「与 gate 并发跑 `--no-incremental` 撞掉 ref 目录」一条 | B-05 | B-04（补「改 Help 后必须 `--no-incremental`」变体，复发 +1） |
| 2026-10-28 | 用户指出「有成熟绑定控件 `LinkableValueEditor`」→ 第一轮手写 `LinkPathEvent` 属重复造轮子，改为 `ContentControl` + `FieldPortEditorTemplate`（`MultiBinding` + `PortsVersion` 锚点，范式 `Plugin.DataRecord`）；AGENTS.md 新增 §44/**R189**（`audit_rules` 190 行 / 50,277B）；结果上报自身断言全绿，gate 2 红均为并发会话（28 条 `*.deps.json` STALE + JT49/JT51） | — | A-01（同日再复发一次，补「别杀用户实例」）、C-41（补「JT 断言被对方改名」变体） |
| 2026-10-28 | 全仓审计「绑定是否都走控件」：只剩 `LinkableValueEditor` 自己在发 `LinkPathEvent`（JudgeTable 已由并发会话迁移并加 `[JT48]` 扫描断言）；8 个动态端口插件分两种合法接法（行对象自带 Port / `MultiBinding`+`PortsVersion`）；可写的手输框只有 DataRecord、ResultUpload 两处「运行时变量名」。据此新增 §45/**R190**（取值只许两种形态、手输只给运行时变量），并修正 R189 原文「必须 MultiBinding」的误伤；`audit_rules` 191 行 / 52,770B | — | R189（补「两种接法」变体） |

**下次更新时**：只改「0. 快速索引」「对应条目」「3. 经验迭代」「4. 最近更新」四处；
新增条目按模板补齐五段；汇报里写「台账已更新：新增 N 条 / 合并 M 条」。
