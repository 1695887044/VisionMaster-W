<#
.SYNOPSIS
  Verify that the repository root only contains whitelisted entries.

.DESCRIPTION
  Whitelist source of truth: tools\root-layout.json (UTF-8, read explicitly
  because Windows PowerShell 5.1 would otherwise read it as ANSI).
  The script is ASCII-only on purpose (AGENTS.md R1).

  Exit code 0 = clean, 1 = violations.

.EXAMPLE
  powershell -NoProfile -File tools\audit_root.ps1
#>
[CmdletBinding()]
param(
    [string]$Root = ''
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = Split-Path -Parent $PSScriptRoot
}
$Root = [IO.Path]::GetFullPath($Root)

$layoutPath = Join-Path $PSScriptRoot 'root-layout.json'
if (-not (Test-Path -LiteralPath $layoutPath)) {
    throw "Layout manifest not found: $layoutPath"
}

$json = [IO.File]::ReadAllText($layoutPath, [Text.Encoding]::UTF8)
$layout = $json | ConvertFrom-Json

$allowedFiles = @{}
foreach ($f in $layout.files) { $allowedFiles[$f] = $true }
$allowedDirs = @{}
foreach ($d in $layout.dirs) { $allowedDirs[$d] = $true }

$violations = New-Object System.Collections.Generic.List[string]

Get-ChildItem -LiteralPath $Root -File -Force | ForEach-Object {
    if (-not $allowedFiles.ContainsKey($_.Name)) {
        $violations.Add('FILE  ' + $_.Name)
    }
}
Get-ChildItem -LiteralPath $Root -Directory -Force | ForEach-Object {
    if (-not $allowedDirs.ContainsKey($_.Name)) {
        $violations.Add('DIR   ' + $_.Name)
    }
}

if ($violations.Count -eq 0) {
    Write-Host ("ROOT LAYOUT OK: " + $allowedFiles.Count + " files / " + $allowedDirs.Count + " dirs allowed")
    exit 0
}

Write-Host ("ROOT LAYOUT VIOLATIONS: " + $violations.Count)
$violations | ForEach-Object { Write-Host ("  " + $_) }
Write-Host 'Move outputs to artifacts\<kind>\<date>\ or retire them under _Retired\<date>-<topic>\.'
exit 1
