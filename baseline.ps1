<#
.SYNOPSIS
  跑一遍 ScadaChecks --stress，把两个"留档数字"块抓下来落进 docs/（S13-a 欠债 3）。

.DESCRIPTION
  为什么要有这个脚本：`[BL]`（基线数字）与 `[ST]`（长稳数字）两块，**断言是不看的**——
  断言只拦数量级退化（防"塌"），数字管趋势（防"慢"）。S13-a 原来的做法是
  "跑完人工粘贴进开发记录"，于是"回归时看数字不看感觉"全靠人记得粘；
  漏粘一次，后面就永远对不上。本脚本把"跑 → 抓 → 落档"这三步自动化，
  只留最后一步"人来看数字"给人。

  落档位置：docs/code-changes/性能基线-YYYY-MM-DD.md
  **同一天重跑会覆盖**：同日多次跑通常是改代码试数字，留最早那次没有意义；
  跨天才有对比价值。（这一条就是"谁覆盖"的答案。）

  本脚本**不产出任何结论**。数字是给人看的，不是给机器判的——它不会因为你比昨天慢
  就报错，也不会自动写"通过/不通过"。退出码只反映 ScadaChecks 自己的断言结果。

.PARAMETER Configuration
  构建配置，默认 Release。性能数字只在 Release 下有意义。

.PARAMETER NoWrite
  只跑并打印两个块，不落档（想直接粘进某份开发记录时用）。

.EXAMPLE
  .\baseline.ps1
  .\baseline.ps1 -NoWrite

.NOTES
  本文件必须存为 **UTF-8 with BOM**：Windows PowerShell 5.1 读无 BOM 的 .ps1 时
  按系统 ANSI 码页（中文机器 = GBK）解码，中文注释会变成乱码并引发一串语法错误。
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [switch]$NoWrite
)

$ErrorActionPreference = 'Stop'
$repoRoot = $PSScriptRoot
$project = Join-Path $repoRoot 'ScadaChecks\ScadaChecks.csproj'
$outFile = Join-Path $env:TEMP ("scada-stress-out-" + [Guid]::NewGuid().ToString('N') + ".log")
$errFile = Join-Path $env:TEMP ("scada-stress-err-" + [Guid]::NewGuid().ToString('N') + ".log")

Write-Host ''
Write-Host "==== 跑 ScadaChecks --stress（$Configuration） ====" -ForegroundColor Cyan

# ---------- 1/2 编译 ----------
# 用 `&` 直接调，不用 Start-Process：`Start-Process -Wait` 等的是**整个进程树**，
# 而 `dotnet build` 会留下 MSBuild 工作进程（node reuse，常驻 15 分钟），
# 于是 `-Wait` 会一直等它们——脚本卡在"编译"这一步不动，看起来像编译没跑完。
# `&` 只等被调用的那个进程本身，编译一结束就返回。
Write-Host '  1/2 编译…'
& dotnet build -c $Configuration $project --nologo
if ($LASTEXITCODE -ne 0) { throw "编译失败（退出码 $LASTEXITCODE），先修编译再看数字。" }

# ---------- 2/2 跑（重定向到文件） ----------
Write-Host '  2/2 跑 --stress：要跑满 60 秒长稳负载，请等它。'
$appDll = Join-Path $repoRoot "ScadaChecks\bin\$Configuration\net9.0-windows\ScadaChecks.dll"
if (-not (Test-Path $appDll)) { throw "找不到 $appDll —— 编译产物不在预期位置？" }

try {
    # ① 为什么用 Start-Process 重定向，而不用 PowerShell 的 `>`：`>` 会先按
    #    [Console]::OutputEncoding 解码再重编码，中文机器上默认 GBK，会把 UTF-8 输出解成乱码。
    #    重定向到文件是原始字节直通。
    # ② 为什么这里也**不用 `-Wait`**：理由同上一条——它等进程树 + 输出流 EOF，
    #    只要树里有一个不肯退的进程，脚本就永远等下去，而且**不报错**（最坏的一种失败：
    #    看日志早就"全部断言通过"了，人还在等）。所以自己轮询 + 兜底超时：
    #    正常情况跟 -Wait 一样准，异常情况最多等到 deadline，不会无限期挂着。
    $proc = Start-Process -FilePath 'dotnet' `
        -ArgumentList @($appDll, '--stress') `
        -WorkingDirectory $repoRoot -NoNewWindow -PassThru `
        -RedirectStandardOutput $outFile -RedirectStandardError $errFile

    $deadline = (Get-Date).AddMinutes(20)
    while (-not $proc.HasExited) {
        if ((Get-Date) -gt $deadline) {
            Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
            throw "ScadaChecks 跑过 20 分钟仍未结束，已强杀（输出片段在 $outFile）"
        }
        Start-Sleep -Milliseconds 500
    }

    # ③ 退出码为什么要 `WaitForExit()` 之后再读：`HasExited` 只保证"进程退了"，
    #    而 Windows PowerShell 5.1 的 `Start-Process -PassThru` 返回的对象在**没调用过
    #    WaitForExit** 时，`ExitCode` 会取回 $null（实测现象：脚本末尾打出"退出码 "后面空着，
    #    还误判成"有断言失败"）。`WaitForExit()` 会顺手把退出码缓存住，之后再读就稳了。
    $proc.WaitForExit()
    $exitCode = $proc.ExitCode

    # 等输出落定再读：文件长度连续 3 次不变才认（避免读到半截就落档）。
    $lastLen = -1; $stable = 0
    while ($stable -lt 3) {
        $len = if (Test-Path $outFile) { (Get-Item $outFile).Length } else { 0 }
        if ($len -eq $lastLen) { $stable++ } else { $stable = 0; $lastLen = $len }
        Start-Sleep -Milliseconds 300
    }
    $lines = @(Get-Content $outFile -Encoding UTF8)

    # 兜底：万一某些 PS 版本还是拿不到退出码，退回用 ScadaChecks 自己的结论行判断——
    # 它一定会打 `>>> 全部断言通过 <<<`，这一行等价于"全绿"。
    if ($null -eq $exitCode) {
        $exitCode = if (($lines -join "`n") -match '>>> 全部断言通过 <<<') { 0 } else { 1 }
    }
} catch {
    Write-Host "  [×] 起不来：$_" -ForegroundColor Red
    throw
}

if ($lines.Count -eq 0) {
    $err = if (Test-Path $errFile) { (Get-Content $errFile -Encoding UTF8 -Raw) } else { '' }
    throw "ScadaChecks 没有输出（退出码 $exitCode）。stderr：`n$err"
}

# ---------- 抽留档块 ----------
# 块的形状固定：以 "┌─" 开头、以 "└─" 结尾，中间每行以 "│" 起。抓首尾两行一起贴，
# 这样落档出来的东西和人在控制台上看到的一模一样，不需要二次加工。
$blocks = New-Object System.Collections.Generic.List[string]
$inBlock = $false
foreach ($line in $lines) {
    if (-not $inBlock) {
        if ($line -match '┌─') { $inBlock = $true; $blocks.Add($line) }
    } else {
        $blocks.Add($line)
        if ($line -match '└─') { $inBlock = $false }
    }
}
if ($blocks.Count -eq 0) {
    throw "一个留档块都没抓到（退出码 $exitCode）——ScadaChecks 是否正常跑完？输出在 $outFile"
}

$summary = ($lines | Where-Object { $_ -match '^通过: ' }) -join ' / '

Write-Host ''
Write-Host '==== 留档数字 ====' -ForegroundColor Cyan
foreach ($line in $blocks) { Write-Host $line }
Write-Host ''
Write-Host "  $summary" -ForegroundColor $(if ($exitCode -eq 0) { 'Green' } else { 'Red' })

# ---------- 落档 ----------
if (-not $NoWrite) {
    $docsDir = Join-Path $repoRoot 'docs\code-changes'
    New-Item -ItemType Directory -Path $docsDir -Force | Out-Null
    $docPath = Join-Path $docsDir ("性能基线-" + (Get-Date -Format 'yyyy-MM-dd') + ".md")

    $md = @()
    $md += '# SCADA 性能基线留档（由 `baseline.ps1` 自动生成）'
    $md += ''
    $md += "生成时间：$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
    $md += ''
    $md += '运行命令：`.\baseline.ps1`（' + $Configuration + '；内部：`dotnet build` 后直接执行 `ScadaChecks.dll --stress`）'
    $md += ''
    $md += "断言结果：$summary"
    $md += ''
    $md += '> 本文件**同一天重跑会覆盖**（同日多次跑通常是改代码试数字，跨天才有对比价值）。'
    $md += '> 数字**没有阈值**：断言只拦数量级退化，趋势必须人工和上一天这份文件对比。'
    $md += ''
    $md += '## 留档数字'
    $md += ''
    $md += '```'
    $md += @($blocks)   # @() 是必需的：List 不是数组，直接 += 会被当成"一个元素"整块塞进去
    $md += '```'
    $md += ''
    $md += '## 怎么读这两块'
    $md += ''
    $md += '- **基线数字（`[BL]`）** = 瞬时单位成本：µs/图元、µs/绑定、µs/切页、µs/报警轮。看"快不快"。'
    $md += '- **长稳数字（`[ST]`）** = 持续负载下的曲线：节拍、UI 最长停顿、内存、订阅数。看"跑得住"。'
    $md += '- 订阅数**终了必须归零**（不归零 = 泄漏，这条有断言）；其余看趋势。'
    $md += ''

    # 带 BOM 写：现场用记事本打开中文才不乱码（与 pack.ps1 的说明文件同一口径）。
    [System.IO.File]::WriteAllText($docPath, ($md -join "`r`n"), (New-Object System.Text.UTF8Encoding($true)))
    Write-Host "  已落档：$docPath" -ForegroundColor Green
}

# 全绿才清临时输出；有红条就**留着**——失败时最需要原始日志，
# 删掉等于把现场毁了（这跟"崩溃留档"是同一个道理）。
if ($exitCode -ne 0) {
    Write-Host ''
    Write-Host "  [×] ScadaChecks 有断言失败（退出码 $exitCode）——数字留档了，但先去看红条。" -ForegroundColor Red
    Write-Host "      完整输出留在：$outFile" -ForegroundColor Yellow
} else {
    Remove-Item $outFile, $errFile -Force -ErrorAction SilentlyContinue
}

exit $exitCode
