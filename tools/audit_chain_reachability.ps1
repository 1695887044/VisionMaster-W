# App 合并链可达性审计：
# 严格按 App.xaml 的合并顺序累积键集，检查链内每个字典的 {StaticResource} 引用
# 是否能从「本文件 + 自身合并子树 + 先前已合并的链」解析。
# 引用了「链上不可达」键（例如只存在于某个视图局部资源）→ 运行期模板实例化时 Foreground=UnsetValue 崩溃。
$ErrorActionPreference = 'Stop'
$repo = 'd:\C#\VM'

# 程序集名 → 磁盘目录（pack URI 的 component 根）
$asmMap = @{
  'Core.Halcon'              = 'Services\Core.Halcon'
  'UI'                       = 'UI\Controls'
  'VisionMaster'             = 'VisionMaster'
  'VM.Core'                  = 'Core'
  'VM.Scada'                 = 'Scada'
  'VM.Scada.Controls'        = 'Scada.Controls'
  'Core.Controls'            = 'Shard\Core.Controls'
  'VM.Communication'         = 'Communication'
  'VM.FlowEngine'            = 'Engine'
}

function Resolve-Source([string]$src, [string]$curDir) {
  if ($src -match 'pack://application:,,,/([^;]+);component/(.+)') {
    $asm = $Matches[1]; $rel = $Matches[2]
    if (-not $asmMap.ContainsKey($asm)) { return $null }
    return Join-Path $repo ($asmMap[$asm] + '\' + ($rel -replace '/', '\'))
  }
  if ($src -match '^/([^;]+);component/(.+)') {
    $asm = $Matches[1]; $rel = $Matches[2]
    if (-not $asmMap.ContainsKey($asm)) { return $null }
    return Join-Path $repo ($asmMap[$asm] + '\' + ($rel -replace '/', '\'))
  }
  return (Join-Path $curDir ($src -replace '/', '\'))
}

function Get-KeysAndRefs([string]$file) {
  $t = [IO.File]::ReadAllText($file)
  $keys = New-Object 'System.Collections.Generic.HashSet[string]'
  foreach ($m in [regex]::Matches($t, 'x:Key="([^"]+)"')) { [void]$keys.Add($m.Groups[1].Value) }
  foreach ($m in [regex]::Matches($t, '<Style[^>]*TargetType="(\{x:Type )?([A-Za-z]+)\}?"')) { [void]$keys.Add($m.Groups[2].Value) }
  $refs = @()
  $lines = $t -split "`n"
  for ($i = 0; $i -lt $lines.Count; $i++) {
    foreach ($m in [regex]::Matches($lines[$i], '\{StaticResource ([^}]+)\}')) {
      $k = $m.Groups[1].Value.Trim()
      $refs += [pscustomobject]@{ Key = $k; Line = $i + 1 }
    }
  }
  return @{ Keys = $keys; Refs = $refs }
}

# ---- 递归走 App 合并链（按顺序），累积 available 键 ----
$chainKeys = New-Object 'System.Collections.Generic.HashSet[string]'
$visited = New-Object 'System.Collections.Generic.HashSet[string]'
$problems = New-Object 'System.Collections.Generic.List[string]'

function Walk([string]$file, [bool]$isInlineFirst) {
  if (-not $file -or -not (Test-Path $file)) { return }
  $norm = $file.ToLowerInvariant()
  if ($visited.Contains($norm)) { return }
  [void]$visited.Add($norm)

  $info = Get-KeysAndRefs $file
  # 本文件自身定义先入集（模板延迟内容实例化时可查全字典）
  foreach ($k in $info.Keys) { [void]$script:chainKeys.Add($k) }

  # 检查本文件的引用（延迟内容：可查 自身+子树+先前链）
  foreach ($r in $info.Refs) {
    $k = $r.Key
    if ($k -match '^\{x:Type ([A-Za-z:]+)\}$') { $k = $Matches[1] }
    if (-not $script:chainKeys.Contains($k)) {
      $rel = $file.Replace('d:\C#\VM\', '')
      $script:problems.Add(("KEY '{0}'  <-  {1}:{2}" -f $r.Key, $rel, $r.Line))
    }
  }

  # 递归合并子树（按出现顺序）
  $t = [IO.File]::ReadAllText($file)
  $curDir = Split-Path $file
  foreach ($m in [regex]::Matches($t, '<ResourceDictionary Source="([^"]+)"')) {
    $child = Resolve-Source $m.Groups[1].Value $curDir
    Walk $child $false
  }
}

# App.xaml：内联资源先入集，再走 MergedDictionaries
$appXaml = Join-Path $repo 'VisionMaster\App.xaml'
$t = [IO.File]::ReadAllText($appXaml)
foreach ($m in [regex]::Matches($t, 'x:Key="([^"]+)"')) { [void]$chainKeys.Add($m.Groups[1].Value) }
$curDir = Split-Path $appXaml
foreach ($m in [regex]::Matches($t, '<ResourceDictionary Source="([^"]+)"')) {
  $child = Resolve-Source $m.Groups[1].Value $curDir
  Write-Host ("链: " + $child)
  Walk $child $false
}

Write-Host ''
Write-Host '=== 链上不可达的 StaticResource 引用 ==='
foreach ($p in $problems) { Write-Host $p }
Write-Host ('问题总数: ' + $problems.Count)
