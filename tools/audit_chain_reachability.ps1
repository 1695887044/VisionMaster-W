# [SUPERSEDED 2026-10-10] Chain-reachability audit -> see tools/audit_resource_scope.ps1
#
# Why this file no longer contains its own scanner:
#   The old model treated "everything merged earlier in the chain" as reachable, and it
#   checked a file's references BEFORE accumulating that file's own merged subtree.
#   Measured on 2026-10-10 (the "GlobalVariableView cannot find DialogCombo" crash):
#     * it reported 52 problems, ALL of them false (FluentAliases -> FluentChip* etc.,
#       whose definitions live in its own merged subtree, FluentControls.xaml);
#     * it stayed silent on the real cross-book breakage
#       (Dialog.Misc.xaml -> DialogCombo / DialogInput / DialogIconFont).
#   The correct model, proven by the 2026-09-23 and 2026-10-10 incidents: a
#   {StaticResource K} inside a ResourceDictionary resolves against that file's own keys
#   plus its own MergedDictionaries subtree only - sibling dictionaries are invisible.
#
# The authoritative gate is the [resource-link] contract pair in
# FlowCanvasChecks/VariableBindingCheck.cs (RunDictionaryScopeContract).
# tools/audit_resource_scope.ps1 is the same rule as a build-free scanner.
#
# ASCII only on purpose (Windows PowerShell 5.1 reads BOM-less scripts as ANSI).

$ErrorActionPreference = 'Stop'
Write-Host '[superseded] This script no longer scans by itself.'
Write-Host '[superseded] Corrected scanner: tools/audit_resource_scope.ps1'
Write-Host '[superseded] Gate baseline check: FlowCanvasChecks -> [resource-link] contract pair'
Write-Host ''
& (Join-Path $PSScriptRoot 'audit_resource_scope.ps1')
exit $LASTEXITCODE
