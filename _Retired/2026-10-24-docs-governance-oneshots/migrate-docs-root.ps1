# migrate-docs-root.ps1 -- B3 step 1: move docs/ root-level files into topic dirs.
# ASCII only. Uses git mv so history follows the rename.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Set-Location $root

# target dir -> file names (in docs/ root today)
$map = [ordered]@{
  'docs/架构文档/交接报告' = @(
    '交接报告-2026-10-21-P0B数据链路与P0F通信协议.md'
    '交接报告-2026-10-22-SCADA运行态Web-R4.md'
    '交接报告-2026-10-23-AI操作计划覆盖扩展.md'
    '交接报告-2026-10-23-四项插件能力扩展.md'
    '交接报告-2026-10-23-插件缺口治理与数据库通讯.md'
    '交接报告-2026-10-23-数据库通讯v1落地.md'
    '交接报告-2026-10-24-CSharp脚本插件GStack审查.md'
    '交接报告-2026-10-24-docs结构治理B1B2.md'
    '交接报告-2026-10-24-全插件GStack审查.md'
  )
  'docs/架构文档/基线' = @(
    '2026-10-22-项目接管基线.md'
    '2026-10-22-验证体系基线.md'
  )
  'docs/架构文档' = @(
    'ArchitectureGuide.md'
    'ProjectDetailedModules.md'
    '项目知识结构大纲.md'
  )
  'docs/工程纪律' = @(
    '设计思想.md'
    '软件手册.md'
    '新插件端口速查.md'
  )
  'docs/基础设施' = @(
    '图标字体使用.md'
    'PluginReview.md'
  )
  'docs/SCADA运行态Web客户端' = @(
    'SCADA运行态Web客户端方案.md'
    'SCADA运行态Web客户端测试矩阵.md'
  )
  'docs/通信协议矩阵' = @(
    '通信协议矩阵.md'
  )
  'docs/数据记录' = @(
    '变量管理系统-设计说明与交接文档.md'
  )
}

$moved = 0
$missing = 0
foreach ($dir in $map.Keys) {
  if (-not (Test-Path $dir)) {
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    Write-Host ("MKDIR  {0}" -f $dir)
  }
  foreach ($f in $map[$dir]) {
    $src = Join-Path 'docs' $f
    $dst = Join-Path $dir $f
    if (-not (Test-Path $src)) {
      Write-Host ("MISS   {0}" -f $src)
      $missing++
      continue
    }
    if (Test-Path $dst) {
      Write-Host ("EXISTS {0}  -- refusing to overwrite" -f $dst)
      $missing++
      continue
    }
    & git mv -- $src $dst 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw ("git mv failed: {0} -> {1}" -f $src, $dst) }
    Write-Host ("MOVE   {0}  ->  {1}" -f $f, $dir)
    $moved++
  }
}

Write-Host ""
Write-Host ("DONE moved={0} problems={1}" -f $moved, $missing)
