<#
.SYNOPSIS
  Run a probe project registered in tools\probes\probes.json.

.DESCRIPTION
  This is the only supported entry point for running probes. Tasks must not
  hardcode probe paths: the registry (probes.json) is the single source of
  truth, so moving a probe only requires updating the registry and README.

  The script is intentionally ASCII-only (Windows PowerShell 5.1 reads
  non-BOM scripts as ANSI; see AGENTS.md R1). Chinese descriptions live in
  probes.json, which is read explicitly as UTF-8.

.PARAMETER Name
  Registered probe name, e.g. ResProbe. Use -List to see all names.

.PARAMETER Configuration
  Build configuration, Debug (default) or Release.

.PARAMETER BuildOnly
  Build the probe project but do not run it.

.PARAMETER List
  Print all registered probes and exit.

.EXAMPLE
  powershell -NoProfile -File tools\probes\run-probe.ps1 -List

.EXAMPLE
  powershell -NoProfile -File tools\probes\run-probe.ps1 -Name ResProbe

.EXAMPLE
  powershell -NoProfile -File tools\probes\run-probe.ps1 -Name ChartProbe5 -BuildOnly
#>
[CmdletBinding()]
param(
    [string]$Name,
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug',
    [switch]$BuildOnly,
    [switch]$List,
    [Parameter(ValueFromRemainingArguments = $true)][string[]]$ProbeArgs
)

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$manifestPath = Join-Path $here 'probes.json'

if (-not (Test-Path -LiteralPath $manifestPath)) {
    throw "Probe registry not found: $manifestPath"
}

$json = [IO.File]::ReadAllText($manifestPath, [Text.Encoding]::UTF8)
$registry = $json | ConvertFrom-Json

if ($List) {
    $registry.probes | ForEach-Object { '{0,-18} {1}' -f $_.name, $_.description }
    exit 0
}

if ([string]::IsNullOrWhiteSpace($Name)) {
    throw "Usage: run-probe.ps1 -Name <probe> [-Configuration Debug|Release] [-BuildOnly] | -List"
}

$probe = $registry.probes | Where-Object { $_.name -eq $Name } | Select-Object -First 1
if (-not $probe) {
    # Tolerate the historical underscore-prefixed directory name (e.g. _ResProbe),
    # so scripts written before the 2026-10-21 move keep working.
    $legacyName = $Name.TrimStart('_')
    $probe = $registry.probes | Where-Object { $_.name -ieq $legacyName } | Select-Object -First 1
}
if (-not $probe) {
    $known = ($registry.probes | ForEach-Object { $_.name }) -join ', '
    throw "Probe '$Name' is not registered. Known probes: $known"
}

$projectPath = Join-Path (Join-Path $here $probe.dir) $probe.project
if (-not (Test-Path -LiteralPath $projectPath)) {
    throw "Registered project missing: $projectPath (registry is stale; fix probes.json before running)"
}

if ($BuildOnly) {
    & dotnet build $projectPath -c $Configuration --nologo
    exit $LASTEXITCODE
}

if ($probe.runMode -eq 'exe') {
    & dotnet build $projectPath -c $Configuration --nologo
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    $exePath = Join-Path (Join-Path $here $probe.dir) ("bin\{0}\{1}\{2}" -f $Configuration, $probe.framework, $probe.exe)
    if (-not (Test-Path -LiteralPath $exePath)) {
        throw "Built executable not found: $exePath"
    }
    & $exePath @ProbeArgs
    exit $LASTEXITCODE
}

& dotnet run --project $projectPath -c $Configuration --nologo @ProbeArgs
exit $LASTEXITCODE
