<#
.SYNOPSIS
  VisionMaster 目录式交付打包（S13-f 打包与交付）。

.DESCRIPTION
  把"能直接拷到现场机器上跑起来"的完整目录做出来，并且做完自己验一遍。
  为什么是目录式而不是 MSI/安装器：本工程没有任何安装器工程，也没有需要写注册表、
  注册服务、装驱动的动作——交付物就是"一个目录 + 双击 exe"。目录式的另一个好处是
  升级 = 覆盖同名文件，而用户数据（见下）不在其中，所以"升级不丢配置"是结构上成立的，
  不靠安装器脚本记得跳过某些文件。

  `dotnet publish` 的输出并不完整，本脚本补齐三处（都是实测确认过的缺口）：
    1. Halcon 原生库（halcon.dll / hcanvas.dll / hdevenginedotnet.dll）
       —— publish 只带 halcondotnet.dll（托管封装），原生库不随行，缺了表现为
          "打开图像就崩"或启动即报找不到 DLL。
    2. Modules\（插件）
       —— 插件是独立工程、构建时投递到仓库根 Modules\，不属于主程序发布项。
          整目录拷过去（含 Microsoft.CodeAnalysis.* 等插件私有依赖），
          比逐个列文件名可靠：加一个插件不用回来改本脚本。
    3. ScriptAssets\（插件运行期资源）
       —— Plugin.ImageScript 用 xcopy 投递到 bin\，同样不是发布项。
          漏了表现为"插入示例代码报 unresolved procedure call"。

  受保护用户数据（升级时**不要删**，脚本会写一份说明放进交付目录）：
    AppConfig.json        软件级配置（空闲超时、保留期、运行窗口落屏…）
    *.vms                 方案文件（用户自选路径，通常不在程序目录）
    Layout.xml            界面布局
    ScadaUsers.json       账号
    communications.json   通讯配置
    Logs\ Audit\ Alarms\  日志 / 操作审计 / 报警历史
    Autosave\             未保存草稿（异常退出后的现场恢复用）
    crash_reports\        崩溃现场留档

.PARAMETER Configuration
  构建配置，默认 Release。

.PARAMETER OutputDir
  交付目录，默认 dist\VisionMaster-<Configuration>。

.PARAMETER SkipPublish
  跳过 dotnet publish，直接对已有 bin\<Configuration>\net9.0-windows 做补齐
  （用于"只改了补拷逻辑"的快速迭代）。

.PARAMETER Zip
  打包完成后额外压一个 <OutputDir>.zip。

.PARAMETER Smoke
  打包完成后从交付目录启动一次 VisionMaster.exe，看它 15 秒后是否还活着。
  这是唯一能验出"Halcon 原生库没随行"的手段——那种情况下自检全绿，但一启动就崩。

.EXAMPLE
  .\pack.ps1
  .\pack.ps1 -Zip
  .\pack.ps1 -Smoke -Zip
  .\pack.ps1 -OutputDir D:\交付\VisionMaster-v1.0 -Zip

.NOTES
  退出码：0 = 全部必需文件就位；1 = 有缺口（缺口清单会打印出来）。
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$OutputDir = '',
    [switch]$SkipPublish,
    [switch]$Zip,
    [switch]$Smoke
)

$ErrorActionPreference = 'Stop'
$repoRoot = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $repoRoot "dist\VisionMaster-$Configuration"
}
$OutputDir = [System.IO.Path]::GetFullPath($OutputDir)

function Write-Step([string]$text) {
    Write-Host ''
    Write-Host "==== $text ====" -ForegroundColor Cyan
}
function Write-Ok([string]$text)   { Write-Host "  [√] $text" -ForegroundColor Green }
function Write-Bad([string]$text)  { Write-Host "  [×] $text" -ForegroundColor Red }

$problems = New-Object System.Collections.Generic.List[string]

# ---------- 0. 打包前守卫 ----------
# 程序文件被占用时覆盖会失败，先给一句人话，别让人看到一串 IOException。
$running = @(Get-Process -Name 'VisionMaster' -ErrorAction SilentlyContinue)
if ($running.Count -gt 0) {
    throw "VisionMaster 正在运行（PID $($running.Id -join ',')）。请先退出再打包——程序文件被占用时无法覆盖。"
}

# 清空交付目录里的"程序文件"，但**保留受保护用户数据**。
# 开头承诺的"升级 = 覆盖同名文件，用户数据不在其中，所以升级不丢配置是结构上成立的"，
# 必须在这里也成立：直接 Remove-Item 整个目录，在 -OutputDir 指向现场目录时会把配置、
# 方案、草稿、崩溃留档一起删掉——那是不可恢复的。
$protected = @('AppConfig.json', 'Layout.xml', 'ScadaUsers.json', 'communications.json',
               'Logs', 'Audit', 'Alarms', 'Autosave', 'crash_reports', '用户数据说明.txt')
if (Test-Path $OutputDir) {
    $stale = @(Get-ChildItem $OutputDir -Force |
               Where-Object { $protected -notcontains $_.Name -and $_.Extension -ne '.vms' })
    if ($stale.Count -gt 0) {
        $stale | Remove-Item -Recurse -Force
        Write-Host "  已清理交付目录里的 $($stale.Count) 个旧程序文件/目录（用户数据已保留）"
    }
} else {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

# ---------- 1. 发布主程序 ----------
Write-Step "1/6 发布主程序（$Configuration）"

if ($SkipPublish) {
    $publishSource = Join-Path $repoRoot "VisionMaster\bin\$Configuration\net9.0-windows"
    if (-not (Test-Path $publishSource)) { throw "找不到 $publishSource，去掉 -SkipPublish 重跑" }
    Write-Host "  跳过 publish，改用 $publishSource 作为文件源"
    Copy-Item "$publishSource\*" $OutputDir -Recurse -Force
} else {
    $project = Join-Path $repoRoot 'VisionMaster\VisionMaster.csproj'
    & dotnet publish $project -c $Configuration -o $OutputDir --nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败（退出码 $LASTEXITCODE）" }
}
Write-Ok "主程序已就位：$OutputDir"

# ---------- 2. 补 Halcon 原生库 ----------
Write-Step "2/6 补 Halcon 原生库"

$halconDir = Join-Path $repoRoot 'DLL\Halcon'
$halconNative = @('halcon.dll', 'hcanvas.dll', 'hdevenginedotnet.dll')
foreach ($name in $halconNative) {
    $src = Join-Path $halconDir $name
    if (-not (Test-Path $src)) { $problems.Add("Halcon 源文件缺失：$src"); continue }
    Copy-Item $src (Join-Path $OutputDir $name) -Force
    $mb = [math]::Round((Get-Item $src).Length / 1MB, 1)
    Write-Ok "$name（$mb MB）"
}

# ---------- 3. 补插件目录 ----------
Write-Step "3/6 补插件目录 Modules\"

$modulesSrc = Join-Path $repoRoot 'Modules'
if (-not (Test-Path $modulesSrc)) {
    $problems.Add("插件目录不存在：$modulesSrc（先构建 Plugins\ 下的插件工程）")
} else {
    $modulesDst = Join-Path $OutputDir 'Modules'
    New-Item -ItemType Directory -Path $modulesDst -Force | Out-Null
    Copy-Item "$modulesSrc\*" $modulesDst -Recurse -Force
    $pluginCount = @(Get-ChildItem $modulesDst -Filter 'Plugin.*.dll' -File).Count
    Write-Ok "已拷入 $(@(Get-ChildItem $modulesDst -Recurse -File).Count) 个文件，其中插件 $pluginCount 个"
    if ($pluginCount -eq 0) { $problems.Add("Modules\ 里没有任何 Plugin.*.dll，主程序启动会一个算子都没有") }
}

# ---------- 4. 补 ScriptAssets ----------
Write-Step "4/6 补 ScriptAssets（插件运行期资源）"

$scriptAssetsSrc = Join-Path $repoRoot "VisionMaster\bin\$Configuration\net9.0-windows\ScriptAssets"
if (Test-Path $scriptAssetsSrc) {
    # 注意拷的是 "$src\*" 而不是 $src：目标是已存在的目录时，Copy-Item 传目录会
    # 在目标里再套一层同名目录（dist\ScriptAssets\ScriptAssets），文件数翻倍。
    $scriptAssetsDst = Join-Path $OutputDir 'ScriptAssets'
    New-Item -ItemType Directory -Path $scriptAssetsDst -Force | Out-Null
    Copy-Item "$scriptAssetsSrc\*" $scriptAssetsDst -Recurse -Force
    Write-Ok "已拷入 ScriptAssets（$(@(Get-ChildItem $scriptAssetsDst -Recurse -File).Count) 个文件）"
} else {
    # 不算缺口：没构建过 ImageScript 插件（或它换了投递方式）时本就可能没有。
    Write-Host "  [-] 未找到 $scriptAssetsSrc，跳过（若现场要用「插入示例代码」，先构建 Plugin.ImageScript）" -ForegroundColor Yellow
}

# ---------- 5. 写用户数据说明 ----------
Write-Step "5/6 写用户数据说明"

$noticePath = Join-Path $OutputDir '用户数据说明.txt'
$notice = @"
VisionMaster 交付目录说明
生成时间：$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')
构建配置：$Configuration

零、怎么用
  整个目录拷到现场机器上，双击 VisionMaster.exe 即可，不需要安装、不需要写注册表、
  不需要装驱动。首次运行会在本目录自动生成 AppConfig.json 与 Logs\，属正常现象。

一、升级怎么做
  1) 先退出 VisionMaster；
  2) 备份下面「二」里列出的用户数据（至少备份 *.vms 方案文件与 AppConfig.json）；
  3) 用新版本目录里的文件覆盖旧目录的同名文件；
  4) 不要删除下面「二」里的任何文件/目录。

二、受保护用户数据（升级不得删除、不得覆盖）
  AppConfig.json        软件级配置：空闲超时、审计/报警保留天数、运行窗口形态与落屏
  *.vms                 方案文件（用户自选路径保存，通常在程序目录之外）
  Layout.xml            界面布局
  ScadaUsers.json       账号与角色
  communications.json   通讯配置
  Logs\                 运行日志（按天滚动，保留 30 天）
  Audit\                操作审计（按天滚动，保留天数取 AppConfig.json）
  Alarms\               报警历史（按天滚动，保留天数取 AppConfig.json）
  Autosave\             未保存草稿；recovered\ 是"已提示过"的归档
  crash_reports\        崩溃现场留档（保留最近 20 份）

三、版本升级的兼容性承诺
  新增配置项一律「默认值 = 历史行为」：老机器的 AppConfig.json 里没有新字段时，
  读出来就是老版本的行为，不会因为升级而改变现场已经在跑的动作。

四、出问题先看哪里
  Logs\ 当天日期的 .log      启动自检、通讯、流程、审计的完整流水
  crash_reports\*.txt        上一次严重异常退出的现场（时间/来源/当前方案/异常全文）
  Autosave\recovered\*.vms   异常退出时未保存的方案内容，可直接用「打开方案」载入
"@
[System.IO.File]::WriteAllText($noticePath, $notice, (New-Object System.Text.UTF8Encoding($true)))
Write-Ok "用户数据说明.txt"

# ---------- 6. 自检 ----------
Write-Step "6/6 交付目录自检"

$required = @(
    'VisionMaster.exe',
    'VisionMaster.dll',
    'VisionMaster.runtimeconfig.json',
    'VM.Core.dll',
    'VM.Scada.dll',
    'VM.Scada.Controls.dll',
    'VM.Communication.dll',
    'VM.FlowEngine.dll',
    'Core.Halcon.dll',
    'Core.Controls.dll',
    'Core.Interfaces.dll',
    'UI.dll',
    'halcondotnet.dll',
    'halcon.dll',
    'hcanvas.dll',
    'hdevenginedotnet.dll',
    'Modules'
)
foreach ($item in $required) {
    $path = Join-Path $OutputDir $item
    if (Test-Path $path) {
        Write-Ok $item
    } else {
        Write-Bad $item
        $problems.Add("交付目录缺少：$item")
    }
}

$totalFiles = @(Get-ChildItem $OutputDir -Recurse -File).Count
$totalMB = [math]::Round((Get-ChildItem $OutputDir -Recurse -File | Measure-Object -Property Length -Sum).Sum / 1MB, 1)

# ---------- 可选：压缩 ----------
if ($Zip) {
    Write-Step "压缩交付包"
    $zipPath = "$OutputDir.zip"
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Compress-Archive -Path "$OutputDir\*" -DestinationPath $zipPath -CompressionLevel Optimal
    $zipMB = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)
    Write-Ok "$zipPath（$zipMB MB）"
}

# ---------- 可选：冒烟启动 ----------
# 自检只能证明"文件在"，证明不了"跑得起来"。缺 Halcon 原生库时自检全绿而启动即崩，
# 所以这一步是目录式交付最后一道真门槛。
if ($Smoke) {
    Write-Step "冒烟启动（-Smoke）"
    $exe = Join-Path $OutputDir 'VisionMaster.exe'
    $proc = Start-Process -FilePath $exe -WorkingDirectory $OutputDir -PassThru
    Start-Sleep -Seconds 15
    if ($proc.HasExited) {
        # `HasExited` 只保证"进程退了"；Windows PowerShell 5.1 的 `Start-Process -PassThru`
        # 返回的对象在**没调用过 WaitForExit** 时 `ExitCode` 会取回 $null，
        # 那样下面的 switch 会落到 default 分支，打出"退出码 （0x00000000）"这种误导人的话。
        $proc.WaitForExit()
        $code = $proc.ExitCode
        $hint = switch ($code) {
            -1073741515 { '缺 DLL（0xC0000135）——多半是 Halcon 原生库没随行' }
            -1073741819 { '访问冲突（0xC0000005）' }
            -532462766  { '.NET 未处理异常（0xE0434352），去看 Logs\ 当天日志与 crash_reports\' }
            default     { "退出码 $code（0x$('{0:X8}' -f $code)）" }
        }
        Write-Bad "启动后即退出：$hint"
        $problems.Add("冒烟启动失败：VisionMaster.exe 启动后即退出，$hint")
    } else {
        Write-Ok "启动 15 秒后仍在运行（PID $($proc.Id)），关闭它"
        Stop-Process -Id $proc.Id -Force
        Start-Sleep -Milliseconds 500
    }
}

# ---------- 结果 ----------
Write-Host ''
Write-Host '========== 打包结果 ==========' -ForegroundColor Cyan
Write-Host "交付目录：$OutputDir"
Write-Host "文件总数：$totalFiles    占用：$totalMB MB"
if ($problems.Count -eq 0) {
    Write-Host '>>> 交付目录完整 <<<' -ForegroundColor Green
    exit 0
} else {
    Write-Host ">>> 存在 $($problems.Count) 项缺口 <<<" -ForegroundColor Red
    foreach ($p in $problems) { Write-Host "  - $p" -ForegroundColor Red }
    exit 1
}
