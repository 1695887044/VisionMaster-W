# [OBSOLETE 2026-10-22] StaticResource key audit -> see tools/audit_resource_scope.ps1
#
# Why this file no longer contains its own scanner:
#   Measured on 2026-10-22: it reported 8 missing keys and ALL 8 were false positives:
#     * 4 x nested-brace parsing bug  ({StaticResource {x:Type X}} inside a larger markup extension)
#     * 3 x text that lives inside XML comments
#     * 1 x a stale comment describing a key that had already been fixed
#   It also never returned a non-zero exit code, so it could not gate anything - the worst
#   combination: noisy (people learn to ignore it) and toothless (it cannot fail a build).
#   Keeping a second, wrong implementation of the same rule is worse than having none:
#   somebody will run it, see 8 problems, and spend an afternoon on 0 real ones.
#
# The authoritative gate is the [resource-link] contract pair in
# FlowCanvasChecks/VariableBindingCheck.cs (RunDictionaryScopeContract).
# tools/audit_resource_scope.ps1 is the same rule as a build-free scanner (keep the two in sync).
#
# ASCII only on purpose (Windows PowerShell 5.1 reads BOM-less scripts as ANSI).

$ErrorActionPreference = 'Stop'
Write-Host '[OBSOLETE] This script no longer scans by itself.'
Write-Host '[OBSOLETE] On 2026-10-22 it reported 8 missing keys; all 8 were false positives'
Write-Host '[OBSOLETE]   (nested braces / XML comments / stale comment) and it never set a non-zero exit code.'
Write-Host '[OBSOLETE] Corrected scanner: tools/audit_resource_scope.ps1'
Write-Host '[OBSOLETE] Gate baseline check: FlowCanvasChecks -> [resource-link] contract pair'
Write-Host ''
& (Join-Path $PSScriptRoot 'audit_resource_scope.ps1')
exit $LASTEXITCODE
