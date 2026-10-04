# 全仓 StaticResource 键解析审计：找出引用了但没有任何 XAML 定义的键
$roots = @(
  "d:\C#\VM\UI\Controls","d:\C#\VM\VisionMaster","d:\C#\VM\Scada","d:\C#\VM\Scada.Controls",
  "d:\C#\VM\Shard","d:\C#\VM\Plugins","d:\C#\VM\Core","d:\C#\VM\Communication","d:\C#\VM\Engine",
  "d:\C#\VM\Services","d:\C#\VM\WPF-Halcon-流程拖拉"
)
$files = Get-ChildItem $roots -Recurse -Filter *.xaml -ErrorAction SilentlyContinue |
         Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }

$defined = New-Object 'System.Collections.Generic.HashSet[string]'
foreach ($f in $files) {
  $t = [IO.File]::ReadAllText($f.FullName)
  foreach ($m in [regex]::Matches($t, 'x:Key="([^"]+)"')) { [void]$defined.Add($m.Groups[1].Value) }
  # 隐式样式键（TargetType 即键名）
  foreach ($m in [regex]::Matches($t, '<Style[^>]*TargetType="(\{x:Type )?([A-Za-z]+)\}?"')) {
    [void]$defined.Add($m.Groups[2].Value)
  }
}
Write-Host ("XAML files: {0}, defined keys: {1}" -f $files.Count, $defined.Count)

$missing = @{}
foreach ($f in $files) {
  $t = [IO.File]::ReadAllText($f.FullName)
  $lines = $t -split "`n"
  for ($i = 0; $i -lt $lines.Count; $i++) {
    foreach ($m in [regex]::Matches($lines[$i], '\{StaticResource ([^}]+)\}')) {
      $k = $m.Groups[1].Value.Trim()
      if ($k -match '^\{x:Type ([A-Za-z]+)\}$') { $k = $Matches[1] }
      if (-not $defined.Contains($k)) {
        if (-not $missing.ContainsKey($k)) { $missing[$k] = New-Object 'System.Collections.Generic.List[string]' }
        $rel = $f.FullName -replace [regex]::Escape('d:\C#\VM\'), ''
        $missing[$k].Add(('{0}:{1}' -f $rel, ($i + 1)))
      }
    }
  }
}

Write-Host '=== MISSING KEYS ==='
foreach ($kv in ($missing.GetEnumerator() | Sort-Object Name)) {
  Write-Host ('KEY: ' + $kv.Key)
  foreach ($loc in $kv.Value) { Write-Host ('    ' + $loc) }
}
Write-Host ('Total missing keys: ' + $missing.Count)
