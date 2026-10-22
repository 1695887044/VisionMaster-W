# 断言宿主证据图改落 `artifacts/render/`

> 日期：2026-10-23 ｜ 范围：`ScadaChecks`、`UIThemeSmokeTest` 两个断言宿主的证据图输出目录 ｜ 状态：已落地，两宿主实跑复绿（ScadaChecks 2491/0、UIThemeSmokeTest exit 0）

## 1. 背景

`ScadaChecks` 与 `UIThemeSmokeTest` 在做渲染类断言时会把截图存下来当证据，历史上直接写进 **受 git 跟踪的** `docs/UI设计基线/**/evidence/`：

```
docs/UI设计基线/scada-controls/evidence/after-phase1-core.png
docs/UI设计基线/solution-list/evidence/after-render-v1-window.png
docs/UI设计基线/_batch-evidence/<asm>-<view>.png
...
```

后果是每跑一次 gate，这些文件都被重写、字节必变（同一屏两次渲染不可能逐像素相同，实测 6,051 → 5,225 字节）。于是 `git status` 周期性变脏，每轮收尾都得 `git checkout --` 还原一遍。更糟的是**还原本身有风险**：如果某次有人真的想更新那些图，`checkout` 会把它一起抹掉。

证据是"这次跑出来的产物"，不是文档。文档里已经提交的旧图是**当时那一次**的记录，保留不动。

## 2. 改动清单

| 文件 | 改动 |
|---|---|
| `ScadaChecks/EvidencePaths.cs`（新增） | `EvidencePaths.Dir(repoRoot, page)` → `artifacts/render/<yyyyMMdd>/<page>/`，顺手 `Directory.CreateDirectory` |
| `UIThemeSmokeTest/EvidencePaths.cs`（新增） | 同上（两个宿主各自一份，不跨工程共享，避免为一个 5 行方法拉 ProjectReference） |
| `ScadaChecks/Phase1UiChecks.cs` | `evidenceDir` 改走 `EvidencePaths.Dir`（1 处） |
| `ScadaChecks/Phase2UiChecks.cs` | 同上（1 处） |
| `UIThemeSmokeTest/GlobalVariableViewSmoke.cs` / `MessageDialogSmoke.cs` / `PluginViewSmoke.cs` / `SolutionListViewSmoke.cs`（2 处）/ `VariableBindingViewSmoke.cs` / `Program.cs`（2 处） | 各自 `outDir`/`evidenceDir`/`windowShot` 改走 `EvidencePaths.Dir`（共 8 处） |

合计 **10 个调用点**，替换后 `grep -rn 'docs", "UI设计基线' ScadaChecks UIThemeSmokeTest` = **0 命中**。

## 3. 关键决策

1. **只改落点，不改名**。文件名（`after-phase1-core.png` 等）保持不变，这样看日志/看记录的人不需要重新认图。
2. **文档里的旧图不删**。它们是历史记录；删掉等于改写过去。新的跑出来落 `artifacts/`（已 gitignore），两者并存不冲突。
3. **`Dir()` 内建 `CreateDirectory`**。原来每个调用点自己 `Directory.CreateDirectory(evidenceDir)`，有一处（`SolutionListViewSmoke.cs:288` 的 `windowShot`）是直接用 `Path.Combine(..., "evidence", "文件名.png")` 拼完整路径，依赖 20 行后面那次创建——顺序一变就抛。收进 helper 后这种隐式依赖消失。
4. **两个宿主各写一份 helper，不抽公共库**。5 行方法，为一个它给断言宿主加 ProjectReference，反而让"这两个宿主不依赖彼此的构建"这条性质变模糊。

## 4. 验证证据

- `dotnet build UIThemeSmokeTest/UIThemeSmokeTest.csproj -m:1 -nodeReuse:false` → **0 个错误**
- `dotnet build ScadaChecks/ScadaChecks.csproj -m:1 -nodeReuse:false` → **0 个错误**
- 两个宿主各实跑一次：
  - `ScadaChecks` → **`通过: 2491  失败: 0`**，`exit 0`
  - `UIThemeSmokeTest` → `exit 0`
- 落点核对：`artifacts/render/20261023/scada-controls/` 下有 **7 张 PNG**（`after-phase1-core.png` / `-tank-kpi` / `-chart`、`after-phase2-selector` / `-textfield` / `-combobox` / `-ack`）。
- **关键证据（这就是本轮要的效果）**：跑完之后 `git status --porcelain -- docs` 里**再没有** `evidence/*.png` 的改动，只剩别的会话在改的 `database/*` 与几篇 `code-changes`。

## 5. 边界与未做

- **只覆盖这两个宿主**。`FlowCanvasChecks` / `HelpProbe` 等其余宿主本来就不写 `docs/`，未动。
- **`ScadaChecks` 还有一批图写到自己的 `bin\Debug\net9.0-windows\artifacts\`**（顶栏、轴控台、上下文菜单）。那些在 `bin` 下、本来就不进版本库，本轮没改——挪它们只会让路径更长、没有收益。
- **文档里指向旧图的链接没有改**。`docs/UI设计基线/*/README.md` 里引用的是已提交的那份，仍然有效。
- **本次没做变异反证**：这是"产物落点"不是"判据"，改坏的表现是"文档又被写脏"，直接看 `git status` 就能发现，比构造反证更直接。

## 6. 红线核对

| 红线/规则 | 状态 |
|---|---|
| R166 产物落点 `artifacts\render\<yyyyMMdd>\` | 是，正是本轮改成的目标 |
| R140 视图零硬编码颜色 | 不涉及（只改断言宿主的输出路径） |
| R65 改断言宿主后必须 build 再 run | 是，两宿主都重建并实跑 |
| R42 产物判据 | 不涉及（未改任何插件） |
| R177 落盘物 | 本文件 |
