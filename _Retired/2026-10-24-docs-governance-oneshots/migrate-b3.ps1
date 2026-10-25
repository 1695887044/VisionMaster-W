$ErrorActionPreference = 'Continue'
Set-Location (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
$plan = Get-Content -Raw -Encoding UTF8 'tools/oneoff/migrate-b3-plan.json' | ConvertFrom-Json

# Build a set of tracked paths (forward slashes, as git reports them)
$tracked = @{}
& git -c core.quotepath=false ls-files | ForEach-Object { $tracked[$_] = $true }

$moved = 0; $skipped = 0; $problems = 0
foreach ($e in $plan.moves) {
  $dirNative = $e.dir -replace '/', '\'
  if (-not (Test-Path $dirNative)) { New-Item -ItemType Directory -Force -Path $dirNative | Out-Null }
  foreach ($f in $e.files) {
    $srcNative = Join-Path 'docs' $f
    $dstNative = Join-Path $dirNative $f
    if (Test-Path $dstNative) {
      if (Test-Path $srcNative) { Write-Host "BOTH   $f"; $problems++ } else { $skipped++ }
      continue
    }
    if (-not (Test-Path $srcNative)) { Write-Host "MISS   $f"; $problems++; continue }

    $srcGit = 'docs/' + $f
    if ($tracked.ContainsKey($srcGit)) {
      & git -c core.quotepath=false mv -- $srcNative $dstNative 2>&1 | Out-Null
      if ($LASTEXITCODE -ne 0) { Write-Host "FAIL   $f"; $problems++; continue }
    } else {
      Move-Item -LiteralPath $srcNative -Destination $dstNative
    }
    $moved++
  }
}
Write-Host "DONE moved=$moved already_moved=$skipped problems=$problems"
Write-Host ""
Write-Host "--- docs root remaining .md ---"
Get-ChildItem -Path 'docs' -Filter '*.md' -File | ForEach-Object { Write-Host $_.Name }
