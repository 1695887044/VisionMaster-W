# Validate vm-plan/1 task files against ui-map.json.
# ASCII only (repository rule R1). Usage:
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\validate-plans.ps1
param(
    [string]$Root = "",
    [switch]$WarnOrphans
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = Split-Path -Parent $PSScriptRoot
}

$uiPath = Join-Path $Root 'ui-map.json'
$plansDir = Join-Path $Root 'plans'
$builtinVars = @('run_started_at', 'run_id', 'step_index')

$script:errors = New-Object System.Collections.ArrayList
$script:warnings = New-Object System.Collections.ArrayList

function Add-Error([string]$Message) {
    [void]$script:errors.Add($Message)
}

function Add-Warning([string]$Message) {
    [void]$script:warnings.Add($Message)
}

function Read-Json([string]$Path) {
    $text = [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8)
    return $text | ConvertFrom-Json
}

function Get-StringValues($Node) {
    $result = New-Object System.Collections.ArrayList
    if ($null -eq $Node) { return $result }
    if ($Node -is [string]) {
        [void]$result.Add($Node)
        return $result
    }
    if ($Node -is [System.Collections.IEnumerable] -and -not ($Node -is [string])) {
        foreach ($item in $Node) {
            foreach ($s in (Get-StringValues $item)) { [void]$result.Add($s) }
        }
        return $result
    }
    if ($Node -is [System.Management.Automation.PSCustomObject]) {
        foreach ($p in $Node.PSObject.Properties) {
            foreach ($s in (Get-StringValues $p.Value)) { [void]$result.Add($s) }
        }
        return $result
    }
    return $result
}

function Get-ReferencedVars($Node) {
    $names = New-Object System.Collections.ArrayList
    foreach ($s in (Get-StringValues $Node)) {
        foreach ($m in [regex]::Matches($s, '\$\{([A-Za-z0-9_]+)\}')) {
            [void]$names.Add($m.Groups[1].Value)
        }
    }
    return $names
}

if (-not (Test-Path -LiteralPath $uiPath)) {
    Write-Host "ERROR: ui-map.json not found: $uiPath"
    exit 1
}
if (-not (Test-Path -LiteralPath $plansDir)) {
    Write-Host "ERROR: plans directory not found: $plansDir"
    exit 1
}

try {
    $ui = Read-Json $uiPath
} catch {
    Write-Host "ERROR: ui-map.json is not valid JSON: $($_.Exception.Message)"
    exit 1
}

if ($ui.schema -ne 'vm-ui-map/1') {
    Add-Error "ui-map.json: schema must be 'vm-ui-map/1'"
}

$windowIds = @{}
if ($null -ne $ui.windows) {
    foreach ($p in $ui.windows.PSObject.Properties) { $windowIds[$p.Name] = $true }
}

$elementIds = @{}
if ($null -ne $ui.elements) {
    foreach ($p in $ui.elements.PSObject.Properties) {
        $elementIds[$p.Name] = $true
        $w = $p.Value.window
        if ($w -and -not $windowIds.ContainsKey($w)) {
            Add-Error "ui-map.json: element '$($p.Name)' references unknown window '$w'"
        }
    }
}

$planFiles = @(Get-ChildItem -LiteralPath $plansDir -Filter '*.json' -File | Sort-Object Name)
if ($planFiles.Count -eq 0) {
    Add-Error "plans directory contains no .json files"
}

$planIds = @{}
$planById = @{}
$plans = @()
foreach ($file in $planFiles) {
    try {
        $plan = Read-Json $file.FullName
    } catch {
        Add-Error "$($file.Name): invalid JSON: $($_.Exception.Message)"
        continue
    }
    $plans += [pscustomobject]@{ File = $file; Plan = $plan }
    $expectedId = [System.IO.Path]::GetFileNameWithoutExtension($file.Name)
    if ($plan.id -ne $expectedId) {
        Add-Error "$($file.Name): id '$($plan.id)' must equal file name '$expectedId'"
    }
    if ($planIds.ContainsKey([string]$plan.id)) {
        Add-Error "$($file.Name): duplicate plan id '$($plan.id)'"
    } else {
        $planIds[[string]$plan.id] = $true
        $planById[[string]$plan.id] = $plan
    }
}

$actionWithTarget = @('click', 'double_click', 'right_click', 'type', 'fill', 'select', 'check', 'uncheck')
$allDo = @('menu', 'click', 'double_click', 'right_click', 'type', 'type_focused', 'fill', 'key', 'select', 'check', 'uncheck', 'drag', 'wait', 'assert', 'screenshot', 'call', 'note')
$allAssert = @('process_running', 'window', 'window_closed', 'element_exists', 'element_absent', 'element_enabled', 'element_value', 'text_exists', 'text_absent', 'toast_contains', 'file_exists', 'file_mtime_after', 'param_equals')

function Check-TargetRef([string]$PlanName, [string]$Where, [string]$Id, [bool]$AllowWindow) {
    if ([string]::IsNullOrWhiteSpace($Id)) {
        Add-Error "$PlanName : $Where has an empty target"
        return
    }
    if ($elementIds.ContainsKey($Id)) { return }
    if ($AllowWindow -and $windowIds.ContainsKey($Id)) { return }
    Add-Error "$PlanName : $Where references unknown target '$Id'"
}

function Check-Expectation([string]$PlanName, [string]$Where, $Expect) {
    if ($null -eq $Expect) { return }
    if ($Expect.window -and -not $windowIds.ContainsKey([string]$Expect.window)) {
        Add-Error "$PlanName : $Where expects unknown window '$($Expect.window)'"
    }
    if ($Expect.element -and -not $elementIds.ContainsKey([string]$Expect.element)) {
        Add-Error "$PlanName : $Where expects unknown element '$($Expect.element)'"
    }
    if ($Expect.assert -and ($allAssert -notcontains [string]$Expect.assert)) {
        Add-Error "$PlanName : $Where uses unknown assertion '$($Expect.assert)'"
    }
}

foreach ($item in $plans) {
    $file = $item.File
    $plan = $item.Plan
    $name = $file.Name

    if ($plan.schema -ne 'vm-plan/1') {
        Add-Error "$name : schema must be 'vm-plan/1'"
    }
    if ([string]::IsNullOrWhiteSpace([string]$plan.title)) {
        Add-Error "$name : title is required"
    }
    if ([string]$plan.version -notmatch '^\d+\.\d+\.\d+$') {
        Add-Error "$name : version must be x.y.z"
    }
    if ($plan.on_fail -and (@('stop_and_report', 'retry_step', 'continue') -notcontains [string]$plan.on_fail)) {
        Add-Error "$name : on_fail has unknown value '$($plan.on_fail)'"
    }

    $paramNames = @{}
    if ($null -ne $plan.params) {
        foreach ($p in $plan.params.PSObject.Properties) { $paramNames[$p.Name] = $true }
    }
    foreach ($v in (Get-ReferencedVars $plan)) {
        if (-not $paramNames.ContainsKey($v) -and ($builtinVars -notcontains $v)) {
            Add-Error "$name : variable '`${$v}' is not declared in params and is not a built-in"
        }
    }

    $steps = @($plan.steps)
    if ($steps.Count -eq 0) {
        Add-Error "$name : steps must not be empty"
    }
    for ($i = 0; $i -lt $steps.Count; $i++) {
        $step = $steps[$i]
        $where = "step $($i + 1)"
        if ([int]$step.n -ne ($i + 1)) {
            Add-Error "$name : $where has n=$($step.n); steps must be numbered 1..N in order"
        }
        if ($allDo -notcontains [string]$step.do) {
            Add-Error "$name : $where has unknown do '$($step.do)'"
            continue
        }

        if ($actionWithTarget -contains [string]$step.do) {
            Check-TargetRef $name "$where ($($step.do))" ([string]$step.target) ($step.do -eq 'fill')
        }
        if ($step.do -eq 'menu') {
            if ($null -eq $step.path -or @($step.path).Count -eq 0) {
                Add-Error "$name : $where (menu) needs a non-empty path"
            } else {
                foreach ($p in @($step.path)) {
                    Check-TargetRef $name "$where (menu path)" ([string]$p) $false
                }
            }
        }
        if ($step.do -eq 'drag') {
            Check-TargetRef $name "$where (drag from)" ([string]$step.from) $false
            Check-TargetRef $name "$where (drag to)" ([string]$step.to) ($step.to -eq 'dialog.scada.canvas')
        }
        if ($step.do -eq 'call') {
            if ([string]::IsNullOrWhiteSpace([string]$step.plan)) {
                Add-Error "$name : $where (call) needs plan"
            } elseif (-not $planIds.ContainsKey([string]$step.plan)) {
                Add-Error "$name : $where (call) references unknown plan '$($step.plan)'"
            } else {
                $child = $planById[[string]$step.plan]
                if ($null -ne $child.params) {
                    foreach ($cp in $child.params.PSObject.Properties) {
                        if ($cp.Value.required -eq $true) {
                            $hasArg = $false
                            if ($null -ne $step.args) {
                                $hasArg = @($step.args.PSObject.Properties.Name) -contains $cp.Name
                            }
                            if (-not $hasArg) {
                                Add-Error "$name : $where (call $($step.plan)) misses required arg '$($cp.Name)'"
                            }
                        }
                    }
                }
            }
        }
        if ($step.do -eq 'assert') {
            if ($allAssert -notcontains [string]$step.assert) {
                Add-Error "$name : $where has unknown assert '$($step.assert)'"
            }
            if ($step.target) {
                Check-TargetRef $name "$where (assert)" ([string]$step.target) $true
            }
            if ($step.window -and -not $windowIds.ContainsKey([string]$step.window)) {
                Add-Error "$name : $where references unknown window '$($step.window)'"
            }
        }
        Check-Expectation $name $where $step.expect
        Check-Expectation $name "$where skip_if" $step.skip_if
    }

    foreach ($pre in @($plan.preconditions)) {
        if ($null -eq $pre) { continue }
        if ($pre.assert -and ($allAssert -notcontains [string]$pre.assert)) {
            Add-Error "$name : precondition uses unknown assert '$($pre.assert)'"
        }
        if ($pre.target) { Check-TargetRef $name 'precondition' ([string]$pre.target) $true }
        if ($pre.window -and -not $windowIds.ContainsKey([string]$pre.window)) {
            Add-Error "$name : precondition references unknown window '$($pre.window)'"
        }
    }
    foreach ($post in @($plan.postconditions)) {
        if ($null -eq $post) { continue }
        if ($post.assert -and ($allAssert -notcontains [string]$post.assert)) {
            Add-Error "$name : postcondition uses unknown assert '$($post.assert)'"
        }
        if ($post.target) { Check-TargetRef $name 'postcondition' ([string]$post.target) $true }
        if ($post.window -and -not $windowIds.ContainsKey([string]$post.window)) {
            Add-Error "$name : postcondition references unknown window '$($post.window)'"
        }
    }
}

# Orphan check is opt-in: ui-map is a superset and may contain entries not yet used by a plan.
if ($WarnOrphans) {
    $referenced = @{}
    foreach ($item in $plans) {
        foreach ($s in (Get-StringValues $item.Plan)) {
            foreach ($id in $elementIds.Keys) {
                if ($s -eq $id) { $referenced[$id] = $true }
            }
        }
    }
    foreach ($id in $elementIds.Keys) {
        if (-not $referenced.ContainsKey($id)) {
            Add-Warning "ui-map.json: element '$id' is not referenced by any plan"
        }
    }
}

Write-Host ("plans: {0}  windows: {1}  elements: {2}" -f $planFiles.Count, $windowIds.Count, $elementIds.Count)
if ($script:warnings.Count -gt 0) {
    Write-Host ("warnings: {0}" -f $script:warnings.Count)
    foreach ($w in $script:warnings) { Write-Host ("  WARN  {0}" -f $w) }
}
if ($script:errors.Count -gt 0) {
    Write-Host ("errors: {0}" -f $script:errors.Count)
    foreach ($e in $script:errors) { Write-Host ("  FAIL  {0}" -f $e) }
    exit 1
}

Write-Host "OK: all plans are structurally valid"
exit 0
