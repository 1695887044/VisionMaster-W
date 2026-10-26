# tools/perf — 性能测量工具

本目录只放**测量**工具，不放产品代码、不参与构建。基准结论写在
`docs/performance/`，本目录的脚本只负责产出可复核的数字。

| 脚本 | 测什么 | 入口 |
| --- | --- | --- |
| `run_switch_perf.ps1` | 流程切换：UI 线程上的 `vm` / `layout` / `deferred` 三段 + GC 暂停 | `FlowCanvasChecks.exe --switch-perf`（见下） |
| `vm_perf_tests.ps1` | HTTP 端到端：单流程 / 带并发 / 长跑，含 2 GB 守卫 | `VisionMaster.exe`（真机） |
| `vm_perf_extra.ps1` | HTTP 端到端补充段 | 同上 |

## run_switch_perf.ps1

```powershell
# 构建（--no-build 会跑到旧 exe）
dotnet build FlowCanvasChecks\FlowCanvasChecks.csproj -m:1 -nodeReuse:false

# 每个相位 1 个独立进程，跑 3 轮
powershell -NoProfile -ExecutionPolicy Bypass -File tools\perf\run_switch_perf.ps1 `
    -Runs 3 -Phases canvas,tree,none,floor
# 归因实验（摘 DataContext，看成本掉在哪）
powershell -NoProfile -ExecutionPolicy Bypass -File tools\perf\run_switch_perf.ps1 -Runs 1 -Phases diag
```

**相位必须在独立进程里跑**。同进程连跑会累积污染：2026-10-23 实测五个相位连跑时，
`canvas` 238 ms → `tree` 1601 ms，而中间只有开关差异。

相位：`canvas`（默认，含冷切换）· `noorder` · `minimap` · `collapse` · `tree` · `none`（两栏收起，对照）·
`floor`（不切流程，归零地板）· `diag`（归因）。

输出判据（缺任一条就不算跑过）：

- `[switchperf] phase_state phase=... tree=Visible/52 canvas=Collapsed/102`
  —— 相位开关是否真的生效。**没有这一行，相位对照全部作废**。
- `[switchperf] summary phase=... heavy_layout=...`
- `[switchperf] render_tier=N ps2=... processors=...`
  —— 渲染档位。Tier 0（软件渲染）下的绝对毫秒数与 Tier 2 不可比。

日志：`artifacts\logs\<yyyyMMdd>\gate-switchperf-<phase>-r<N>.log`（UTF-8 无 BOM）。
退出码：0 = 每个相位都出了汇总行；1 = 有相位没出。

### 被测方案从哪来

不带路径参数时，探针取 `解决方案\` 下（含一级子目录）**最大的** `.vms`。
想指定就直接传路径：

```powershell
FlowCanvasChecks\bin\Debug\net9.0-windows\FlowCanvasChecks.exe `
    --switch-perf "D:\C#\VM\解决方案\压测方案-3000节点\压测方案-3000节点.vms" 1 canvas
```

压测方案由生成器造，见下节。

## 压测方案（`tools\vms-generator --stress`）

```powershell
dotnet build tools\vms-generator -m:1 -nodeReuse:false
tools\vms-generator\bin\Debug\net9.0\GenSolution.exe --stress          # 3000 节点 / 10 流程
tools\vms-generator\bin\Debug\net9.0\GenSolution.exe --stress --giant  # 3000 节点 / 单流程
```

其它参数：`--nodes N`、`--flows N`、`--seed N`、`--layout none`（不预填坐标，专测 `AutoLayout`）。

产物落在「一个方案一个文件夹」布局（文件夹名 = `SolutionName`）：
`解决方案\压测方案-3000节点\压测方案-3000节点.vms` /
`解决方案\压测方案-3000节点-单流程\压测方案-3000节点-单流程.vms`。

**全部步骤 `IsDisEnable=true`**（编译期跳过，见 `Engine\FlowCompiler.cs:303`），
所以不依赖图片/设备/通讯就能打开——它只压画布，不压算法。
同层相邻叶子之间有真实数据连线，`BuildLinks` 那条路径才会被量到。

## 与 run_gates 的关系

这两个脚本都**不在** `tools\run_gates.ps1` 里：性能数字会抖，混进常规 gate 会让"红了"
说不清是回归还是机器忙。它们是**手动选择性运行**的基准工具，改动前后各跑一次做对照。
