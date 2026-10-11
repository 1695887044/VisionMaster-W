# Narrow-scope StaticResource audit for global ResourceDictionary files (build-free).
#
# Model (proven by the 2026-09-23 and 2026-10-10 incidents, see docs/code-changes):
#   A {StaticResource K} written INSIDE a ResourceDictionary file D resolves against
#     D's own keys + D's own MergedDictionaries subtree (recursive).
#   Sibling dictionaries merged at the same parent are NOT visible, no matter how the
#   keys look globally. Landing spots and consequences:
#     * BasedOn / template content / Freezable -> throws XamlParseException at runtime
#       (dialog will not open at all)
#     * plain Setter.Value                     -> silently skipped (value not applied,
#       typical symptom: icon font falls back -> tofu blocks)
#
# The authoritative gate for this rule is the [resource-link] contract pair in
# FlowCanvasChecks/VariableBindingCheck.cs (RunDictionaryScopeContract, runs in the
# normal gate baseline). This script is the same rule as a standalone scanner so it can
# be run without building; keep the two in sync when the rule changes.
#
# Known blind spots of this rule (documented, not implemented - zero live instances):
#   * same-file forward references: BasedOn to a key defined LATER in the same file does
#     throw in WPF, while the "own keys" set used here is order independent;
#   * a reference sharing its line with the opening tag of a template / Setter.Value gets
#     classified SILENT instead of CRASH (both buckets must be empty anyway).
#
# ASCII only on purpose (Windows PowerShell 5.1 reads BOM-less scripts as ANSI).
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent

$asmMap = @{
  'Core.Halcon'        = 'Services\Core.Halcon'
  'UI'                 = 'UI\Controls'
  'VisionMaster'       = 'VisionMaster'
  'VM.Core'            = 'Core'
  'VM.Scada'           = 'Scada'
  'VM.Scada.Controls'  = 'Scada.Controls'
  'Core.Controls'      = 'Shard\Core.Controls'
  'VM.Communication'   = 'Communication'
  'VM.FlowEngine'      = 'Engine'
}

# ------------------------------------------------------------------ file / key plumbing

function Resolve-Src([string]$src, [string]$curDir) {
  if ($src -match 'pack://application:,,,/([^;]+);component/(.+)') {
    $a = $Matches[1]; $r = $Matches[2]
  } elseif ($src -match '^/([^;]+);component/(.+)') {
    $a = $Matches[1]; $r = $Matches[2]
  } else {
    return (Join-Path $curDir ($src -replace '/', '\'))
  }
  if (-not $asmMap.ContainsKey($a)) { return $null }
  return (Join-Path $repo ($asmMap[$a] + '\' + ($r -replace '/', '\')))
}

# Strip XML comments but keep the line count (offender line numbers must match the file).
function Get-CodeLines([string]$file) {
  $out = New-Object 'System.Collections.Generic.List[string]'
  if (-not (Test-Path $file)) { return $out }
  $inComment = $false
  foreach ($line in [IO.File]::ReadAllLines($file)) {
    $sb = New-Object System.Text.StringBuilder
    $i = 0
    while ($i -lt $line.Length) {
      if (-not $inComment) {
        $open = $line.IndexOf('<!--', $i)
        if ($open -lt 0) { [void]$sb.Append($line.Substring($i)); break }
        [void]$sb.Append($line.Substring($i, $open - $i))
        $i = $open + 4
        $inComment = $true
      } else {
        $close = $line.IndexOf('-->', $i)
        if ($close -lt 0) { $i = $line.Length; break }
        $i = $close + 3
        $inComment = $false
      }
    }
    $out.Add($sb.ToString())
  }
  return $out
}

$ownKeyCache = @{}
function Get-OwnKeys([string]$file) {
  if ($ownKeyCache.ContainsKey($file)) { return $ownKeyCache[$file] }
  $set = New-Object 'System.Collections.Generic.HashSet[string]'
  foreach ($line in (Get-CodeLines $file)) {
    foreach ($m in [regex]::Matches($line, 'x:Key="([^"]+)"')) { [void]$set.Add($m.Groups[1].Value.Trim()) }
    # implicit style keys: TargetType is the key (xml namespace prefix optional)
    foreach ($m in [regex]::Matches($line, '<Style[^>]*TargetType="(\{x:Type )?([A-Za-z0-9_.]+:)?([A-Za-z0-9_.]+)\}?"')) {
      [void]$set.Add($m.Groups[3].Value)
    }
  }
  $ownKeyCache[$file] = $set
  return $set
}

function Get-ScopeKeys([string]$file, [System.Collections.Generic.HashSet[string]]$visiting) {
  $keys = New-Object 'System.Collections.Generic.HashSet[string]'
  foreach ($k in (Get-OwnKeys $file)) { [void]$keys.Add($k) }
  $norm = $file.ToLowerInvariant()
  if ($visiting.Contains($norm)) { return $keys }
  [void]$visiting.Add($norm)
  foreach ($line in (Get-CodeLines $file)) {
    foreach ($m in [regex]::Matches($line, '<ResourceDictionary[^>]*?Source="([^"]+)"')) {
      $child = Resolve-Src $m.Groups[1].Value (Split-Path $file)
      if (-not $child) { continue }   # external assembly (PresentationFramework.Fluent): unreadable
      foreach ($k in (Get-ScopeKeys $child $visiting)) { [void]$keys.Add($k) }
    }
  }
  return $keys
}

# ------------------------------------------------------------------ scan helpers

# All {StaticResource ...} keys on one line, brace aware ({StaticResource {x:Type Button}} -> "{x:Type Button").
function Get-Refs([string]$line) {
  $token = '{StaticResource '
  $refs = New-Object 'System.Collections.Generic.List[string]'
  $i = $line.IndexOf($token)
  while ($i -ge 0) {
    $start = $i + $token.Length
    $depth = 0; $j = $start
    while ($j -lt $line.Length) {
      $ch = $line[$j]
      if ($ch -eq '{') { $depth++ }
      elseif ($ch -eq '}') { if ($depth -eq 0) { break }; $depth-- }
      $j++
    }
    $refs.Add($line.Substring($start, ([Math]::Min($j, $line.Length) - $start)).Trim())
    if ($j -ge $line.Length) { break }
    $i = $line.IndexOf($token, $j + 1)
  }
  return $refs
}

# Non-self-closing opening tags of the given alternation (for nesting depth tracking).
function Count-TagOpens([string]$line, [string]$alt) {
  $opens = ([regex]::Matches($line, "<(?:$alt)(?=[\s>])")).Count
  $selfClosed = ([regex]::Matches($line, "<(?:$alt)\b[^>]*/>")).Count
  return $opens - $selfClosed
}

# ------------------------------------------------------------------ walk App.xaml chain

$chain = New-Object 'System.Collections.Generic.List[string]'
$visited = New-Object 'System.Collections.Generic.HashSet[string]'
function Walk([string]$file) {
  if (-not $file -or -not (Test-Path $file)) { return }
  $norm = $file.ToLowerInvariant()
  if ($visited.Contains($norm)) { return }
  [void]$visited.Add($norm)
  [void]$chain.Add($file)
  foreach ($line in (Get-CodeLines $file)) {
    foreach ($m in [regex]::Matches($line, '<ResourceDictionary[^>]*?Source="([^"]+)"')) {
      Walk (Resolve-Src $m.Groups[1].Value (Split-Path $file))
    }
  }
}

$appXaml = Join-Path $repo 'VisionMaster\App.xaml'
Walk $appXaml

# ------------------------------------------------------------------ audit

$problems = New-Object 'System.Collections.Generic.List[object]'
$refCount = 0
$exemptType = 0

foreach ($file in $chain) {
  $scope = Get-ScopeKeys $file (New-Object 'System.Collections.Generic.HashSet[string]')
  $lines = Get-CodeLines $file
  $templateDepth = 0; $setterValueDepth = 0; $freezableDepth = 0
  for ($i = 0; $i -lt $lines.Count; $i++) {
    $line = $lines[$i]
    foreach ($key in (Get-Refs $line)) {
      $refCount++
      if ($key.Length -eq 0) { continue }
      if ($key[0] -eq '{') { $exemptType++; continue }   # {x:Type X}: framework theme fallback, not a broken link
      if ($scope.Contains($key)) { continue }
      $kind = 'SILENT(setter)'
      if ($line.Contains('BasedOn') -or $templateDepth -gt 0 -or $setterValueDepth -gt 0 -or $freezableDepth -gt 0) {
        $kind = 'CRASH'
      }
      $problems.Add([pscustomobject]@{
        Key = $key; File = $file.Replace($repo + '\', ''); Line = $i + 1; Kind = $kind
      })
    }
    $templateDepth += Count-TagOpens $line 'ControlTemplate|DataTemplate|ItemsPanelTemplate|HierarchicalDataTemplate'
    $templateDepth -= ([regex]::Matches($line, '</(?:ControlTemplate|DataTemplate|ItemsPanelTemplate|HierarchicalDataTemplate)>')).Count
    $setterValueDepth += Count-TagOpens $line 'Setter\.Value'
    $setterValueDepth -= ([regex]::Matches($line, '</Setter\.Value>')).Count
    $freezableDepth += Count-TagOpens $line 'DropShadowEffect|SolidColorBrush|LinearGradientBrush|RadialGradientBrush|BlurEffect'
    $freezableDepth -= ([regex]::Matches($line, '</(?:DropShadowEffect|SolidColorBrush|LinearGradientBrush|RadialGradientBrush|BlurEffect)>')).Count
  }
}

Write-Host ('chain dictionaries: ' + $chain.Count + '   refs scanned: ' + $refCount + '   type-key exempted: ' + $exemptType)
Write-Host ''
Write-Host '=== StaticResource refs unreachable inside the declaring file scope ==='
foreach ($p in $problems) {
  Write-Host ('[{0}] {1}:{2}  KEY={3}' -f $p.Kind, $p.File, $p.Line, $p.Key)
}
Write-Host ''
$crash = @($problems | Where-Object { $_.Kind -eq 'CRASH' })
$silent = @($problems | Where-Object { $_.Kind -ne 'CRASH' })
Write-Host ('CRASH-class: ' + $crash.Count + '   SILENT-class: ' + $silent.Count)
if ($problems.Count -eq 0) { Write-Host 'RESULT: CLEAN (no cross-book reference)' }
