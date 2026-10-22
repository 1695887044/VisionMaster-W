# Artifact freshness gate (2026-10-22) - R42 / red-line (3): judge the artifact, not the build exit code.
#
# Why this exists:
#   `dotnet build VisionMaster.sln` can report 0 errors while `Modules\Plugin.X.dll` is stale:
#     * incremental builds do not treat a newly added EmbeddedResource as dirty (R42/R62);
#     * Plugin.Camera.Network and Plugin.ImageAssign are not in the solution at all (R62);
#     * editing a plugin source without rebuilding it leaves the old DLL in Modules (red line 3).
#   The build exit code cannot see any of that, so the two rules are checked here on the files.
#
# Two checks per plugin project (both must hold):
#   1) Modules\<AssemblyName>.dll mtime >= newest mtime of that project's sources/resources
#      (bin/obj excluded; Help\*.md, csproj, .cs, .xaml, Assets all count)
#   2) number of embedded resource names containing ".Help." inside that DLL
#      == number of .md files under <project>\Help  (R42 recipe: tr '\0' '\n' | grep -a "\.Help\.")
#
# Exit codes: 0 = all plugin artifacts fresh and complete, 1 = at least one mismatch (listed).
#
# ASCII only on purpose (Windows PowerShell 5.1 reads BOM-less scripts as ANSI, R1/R2).

[CmdletBinding()]
param(
    [string]$RepoRoot = ''
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = Split-Path -Parent $PSScriptRoot
}
$RepoRoot = [IO.Path]::GetFullPath($RepoRoot)

$modulesDir = Join-Path $RepoRoot 'Modules'
$pluginsDir = Join-Path $RepoRoot 'Plugins'

Write-Host ('[audit_artifacts] RepoRoot  : ' + $RepoRoot)
Write-Host ('[audit_artifacts] Modules   : ' + $modulesDir)

if (-not (Test-Path $modulesDir)) {
    Write-Host ('[FAIL] Modules directory not found: ' + $modulesDir)
    exit 1
}
if (-not (Test-Path $pluginsDir)) {
    Write-Host ('[FAIL] Plugins directory not found: ' + $pluginsDir)
    exit 1
}

# ---------------------------------------------------------------------------
# helpers
# ---------------------------------------------------------------------------

# .md count under <project>\Help (recursive; 0 when the folder is absent)
function Get-HelpMdCount([string]$projectDir) {
    $helpDir = Join-Path $projectDir 'Help'
    if (-not (Test-Path $helpDir)) { return 0 }
    $files = Get-ChildItem -LiteralPath $helpDir -Recurse -File -ErrorAction SilentlyContinue |
             Where-Object { $_.Extension -ieq '.md' }
    return @($files).Count
}

# Distinct embedded resource names that contain ".Help." and end with ".md".
# The CLI metadata stores resource names as length-prefixed UTF-8 blobs, so a byte-level
# scan is enough (this is the R42 recipe `tr '\0' '\n' | grep -a "\.Help\."` done in-process).
# NOTE: page file names are allowed to contain non-ASCII characters (e.g. "02-<cjk>.md"),
# so the tail class must accept bytes 0x80-0xFF - splitting on "printable ASCII only" would
# truncate those names and silently under-count (measured: RunFlow 4 vs 5).
# Identical names carry identical length prefixes, so Sort-Object -Unique dedupes exactly.
function Get-EmbeddedHelpNames([string]$dllPath) {
    $bytes = [IO.File]::ReadAllBytes($dllPath)
    $latin = [Text.Encoding]::GetEncoding(28591)   # 1 byte == 1 char, offsets preserved
    $text = $latin.GetString($bytes)
    $pattern = '[A-Za-z0-9_.]+\.Help\.[A-Za-z0-9_.\-\x80-\xFF]+\.md'
    $names = [regex]::Matches($text, $pattern) | ForEach-Object { $_.Value } |
             Sort-Object -Unique
    return @($names)
}

function Format-Time($t) { return $t.ToString('yyyy-MM-dd HH:mm:ss') }

# ---------------------------------------------------------------------------
# scan
# ---------------------------------------------------------------------------

$problems = New-Object 'System.Collections.Generic.List[string]'
$checked = 0

$projects = Get-ChildItem -LiteralPath $pluginsDir -Directory -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -like 'Plugin.*' } | Sort-Object Name

foreach ($proj in $projects) {
    $csproj = Get-ChildItem -LiteralPath $proj.FullName -File -Filter '*.csproj' -ErrorAction SilentlyContinue |
              Select-Object -First 1
    if ($null -eq $csproj) { continue }
    $checked++

    # MSBuild default AssemblyName = csproj file name (NOT the folder name:
    # Plugins\Plugin.Utility\ holds Plugin.Util.csproj -> Modules\Plugin.Util.dll)
    $assemblyName = [IO.Path]::GetFileNameWithoutExtension($csproj.Name)
    $csprojText = [IO.File]::ReadAllText($csproj.FullName)
    $m = [regex]::Match($csprojText, '<AssemblyName>\s*([^<\s]+)\s*</AssemblyName>')
    if ($m.Success) { $assemblyName = $m.Groups[1].Value }

    $dll = Join-Path $modulesDir ($assemblyName + '.dll')
    if (-not (Test-Path $dll)) {
        $problems.Add(('MISSING_DLL  {0}  (project {1})  expected {2}' -f $assemblyName, $proj.Name, $dll))
        continue
    }

    # newest source/resource file of the project (bin/obj excluded)
    $newest = Get-ChildItem -LiteralPath $proj.FullName -Recurse -File -ErrorAction SilentlyContinue |
              Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
              Sort-Object LastWriteTime -Descending | Select-Object -First 1

    $dllTime = (Get-Item -LiteralPath $dll).LastWriteTime
    if ($null -ne $newest -and $dllTime -lt $newest.LastWriteTime) {
        $problems.Add(('STALE_DLL    {0}  dll={1}  src={2}  newest={3}' -f `
            $assemblyName, (Format-Time $dllTime), (Format-Time $newest.LastWriteTime), $newest.Name))
    }

    $mdCount = Get-HelpMdCount $proj.FullName
    $helpNames = Get-EmbeddedHelpNames $dll
    if ($helpNames.Count -ne $mdCount) {
        $problems.Add(('HELP_RES     {0}  embedded={1}  md={2}  dll={3}' -f `
            $assemblyName, $helpNames.Count, $mdCount, (Format-Time $dllTime)))
    }
}

Write-Host ('[audit_artifacts] plugin projects checked: ' + $checked)
if ($checked -eq 0) {
    Write-Host '[FAIL] No Plugin.* project found under Plugins\ - refusing to report OK'
    exit 1
}

if ($problems.Count -eq 0) {
    Write-Host '[audit_artifacts] RESULT: OK (every Modules\Plugin.*.dll is newer than its sources and carries all Help pages)'
    exit 0
}

Write-Host ''
Write-Host ('=== PROBLEMS (' + $problems.Count + ') ===')
foreach ($p in $problems) { Write-Host ('  ' + $p) }
Write-Host ''
Write-Host '[audit_artifacts] RESULT: FAIL - rebuild the listed projects (use --no-incremental when Help pages changed, R42)'
exit 1
