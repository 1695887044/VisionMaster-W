# 为引用 Colors 令牌的样式字典就地自合并 Colors.xaml（依赖就地合并，对父级失效免疫）
# 幂等：已含 MergedDictionaries 的文件跳过
$files = @(
  "d:\C#\VM\UI\Controls\Themes\Controls\Window.xaml",
  "d:\C#\VM\UI\Controls\Themes\Controls\Button.xaml",
  "d:\C#\VM\UI\Controls\Themes\Controls\Input.xaml",
  "d:\C#\VM\UI\Controls\Themes\Controls\Selection.xaml",
  "d:\C#\VM\UI\Controls\Themes\Controls\List.xaml",
  "d:\C#\VM\UI\Controls\Themes\Controls\DataGrid.xaml",
  "d:\C#\VM\UI\Controls\Themes\Controls\Navigation.xaml",
  "d:\C#\VM\UI\Controls\Themes\Controls\Layout.xaml",
  "d:\C#\VM\UI\Controls\Themes\Controls\Misc.xaml",
  "d:\C#\VM\UI\Controls\Themes\Dialog\Dialog.Text.xaml",
  "d:\C#\VM\UI\Controls\Themes\Dialog\Dialog.Container.xaml",
  "d:\C#\VM\UI\Controls\Themes\Dialog\Dialog.Input.xaml",
  "d:\C#\VM\UI\Controls\Themes\Dialog\Dialog.Selection.xaml",
  "d:\C#\VM\UI\Controls\Themes\Dialog\Dialog.Chip.xaml",
  "d:\C#\VM\UI\Controls\Themes\Dialog\Dialog.List.xaml",
  "d:\C#\VM\UI\Controls\Themes\Dialog\Dialog.DataGrid.xaml",
  "d:\C#\VM\UI\Controls\Themes\PluginConfigStyles.xaml"
)

$merge =
"  <ResourceDictionary.MergedDictionaries>`r`n" +
"    <!--  依赖就地合并：本文件的样式 Setter 内含跨字典 StaticResource 引用，`r`n" +
"         若只依赖父级 Generic 的合并顺序，父级在加载完本字典后又被失效（如后合并`r`n" +
"         Fluent 主题包、App 资源写入），跨兄弟字典的引用会重新解析失败并被烘焙成`r`n`r`n" +
"         UnsetValue，模板应用时抛 ""{DependencyProperty.UnsetValue} 不是 Foreground 的有效值""。`r`n" +
"         自合并后引用只走自身链，与父级无关。同 FluentTokens/FluentAliases 的既有模式。  -->`r`n" +
"    <ResourceDictionary Source=""/UI;component/Themes/Colors.xaml"" />`r`n" +
"  </ResourceDictionary.MergedDictionaries>"

$fixed = 0
foreach ($f in $files) {
  if (-not (Test-Path $f)) { Write-Host "MISS: $f"; continue }
  $t = [IO.File]::ReadAllText($f)
  if ($t -match '<ResourceDictionary\.MergedDictionaries>') { Write-Host "SKIP(已有): $(Split-Path $f -Leaf)"; continue }
  # 在根元素开标签结束后插入
  $m = [regex]::Match($t, '<ResourceDictionary\b[^>]*>')
  if (-not $m.Success) { Write-Host "NO-ROOT: $f"; continue }
  $new = $t.Insert($m.Index + $m.Length, "`r`n" + $merge)
  [IO.File]::WriteAllText($f, $new, (New-Object System.Text.UTF8Encoding($false)))
  $fixed++
  Write-Host ("FIXED: " + (Split-Path $f -Leaf))
}
Write-Host ("完成，共修改 {0} 个文件" -f $fixed)
