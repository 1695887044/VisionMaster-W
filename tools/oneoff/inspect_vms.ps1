param(
    [Parameter(Mandatory = $true)][string]$Path
)

$j = Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json

function Walk-Steps($steps, [int]$depth) {
    if ($null -eq $steps) { return }
    foreach ($s in $steps) {
        $t = $s.'$type'
        if (-not $t) { $t = $s.GetType().Name }
        $t = ($t -split ',')[0]
        $t = $t -replace '^VisionMaster\.Models\.', ''
        $pad = '  ' * $depth
        $nm = $s.StepName
        if (-not $nm) { $nm = $s.DisplayName }
        Write-Output ($pad + $t + ' | ' + $nm)
        if ($s.Children) { Walk-Steps $s.Children ($depth + 1) }
        if ($s.Steps) { Walk-Steps $s.Steps ($depth + 1) }
    }
}

foreach ($f in $j.Flows) {
    Write-Output ('=== ' + $f.FlowName + ' | Role=' + $f.Role + ' | Invoke=' + $f.InvokeType + ' | Steps=' + $f.Steps.Count)
    Walk-Steps $f.Steps 1
}

Write-Output '--- Config keys ---'
if ($j.Config) { Write-Output ($j.Config.PSObject.Properties.Name -join ', ') }
