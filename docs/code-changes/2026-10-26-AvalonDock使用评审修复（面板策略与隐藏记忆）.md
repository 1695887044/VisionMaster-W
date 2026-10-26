# AvalonDock 使用评审修复（面板策略 / 隐藏记忆 / 胶囊粒度）— 2026-10-26

> 对应评审结论：`docs/code-changes/2026-10-26-VS式胶囊实测修复（自动隐藏第三态与点击链路）.md` 之后的 AvalonDock 用法复查。
> 本文记录“按评审建议实施”的代码改动与验证结果。

## 一、当前状态

- `VisionMaster` 构建 **0 个错误**；`UIThemeSmokeTest` **140 PASS / 0 FAIL**（新增 5 条断言，见 §2.8）。
- `ScadaChecks` **3107 / 0**、`CommChecks` 336/0、`DataPipelineChecks` 97/0、`DatabaseChecks` 134/0、`RecipeChecks` 138/0、`ScadaWebChecks` 193/0；`audit_root` / `audit_artifacts` / `audit_docs` / `sync_constraints` / `docs_index` 全 clean。
- `tools\run_gates.ps1` 最近一次 **13/14 绿**：唯一红是 `FlowCanvasChecks`（3704/4），4 条全是**运动卡报警/安全停机**断言（`整卡进入 Alarm`、`只清 0 号轴`、`清 0 号轴后整卡仍 Alarm`、`最后一根轴清报警后回 Online`），与 AvalonDock/布局无关；并发会话正在改运动代码（`SolutionProjectAssetCatalog.cs` 14:56 仍在写、FlowCanvasChecks 结果在 3708/0 ↔ 3704/4 之间波动）。
- 真机 `Layout.xml` 已确认：检查器 4 个面板与流程导航都写入了
  `CanDockAsTabbedDocument="False"`、`AutoHideWidth="300"`、`CanFloat="False"`；
  两列检查器 `DockWidth` 都是 **300**（原来一列 304）。

## 二、已做

### 2.1 恢复布局后重新套用面板策略

`XmlLayoutSerializer.Deserialize` 会新建 `LayoutAnchorable`；XML 里没写的属性回落库默认：

| 属性 | 库默认 | 旧布局 XML | 后果 |
| --- | --- | --- | --- |
| `CanDockAsTabbedDocument` | true | 无 | 检查器页签又能被拖成中央文档 |
| `AutoHideWidth` | 0 | 无 | 自动隐藏浮层宽度丢失（回落 min 100） |
| `CanFloat` | true | 无 | 检查器/流程栏可被拖成浮动窗口 |

修法：

- `Shell.xaml` / `ShellCopy.xaml`：流程导航、4 个检查器面板、控制台/监控栏显式 `CanFloat="False"`（中央文档仍保留可浮动，设计基线明确允许）。
- `LayoutHelper.ApplyPanelPolicies(manager)`：在 `LoadFromString` 的 `EnsureInspectorPanelsAreAnchorables` 之后调用，按 ContentId 重新套用
  `CanClose=false`、`CanHide`、`CanDockAsTabbedDocument=false`、`CanFloat=false`、`AutoHideWidth=300`（检查器）/
  `CanDockAsTabbedDocument=false` + `CanFloat=false`（流程导航）/ `CanFloat=false`（控制台、监控栏）/ `CanClose=false`（中央文档）。

### 2.2 拒绝指向文档 pane 的隐藏记忆

真机 `Layout.xml` 里工具箱的 `PreviousContainerId` 指向中央 `LayoutDocumentPane`，索引 2 有效、容器在树上——旧判据放行，库的 `Show()` 会真的把 `LayoutAnchorable` 插进文档 pane（临时探针实测不抛异常）。

修法：

- `ShowAnchorable` 的 `memoryUnusable` 增加 `remembered is not LayoutAnchorablePane`；
- `AnchorToHomePane` 的 `rememberedGroup` 从 `ILayoutGroup` 收窄到 `LayoutAnchorablePane`，文档 pane / 自动隐藏组不再作为落点；
- 兜底新建 pane 的宽度按面板类型：检查器 300、流程导航 340、其余 260（`DefaultPaneWidthFor`）。

### 2.3 胶囊按面板收回

AvalonDock 的 `ToggleAutoHide()` 是 pane 级：收回时整组标签一起回原 pane；胶囊是 panel 级。

修法：

- 新增 `LayoutHelper.GetAutoHiddenGroupSiblingIds(manager, contentId)`；
- `ShellViewModel.ShowInspectorCapsule` 在 `ShowPanel` 前记下同组面板，收回后对非目标面板调用 `HidePanel`，只让点中的那枚胶囊对应的面板回来。

### 2.4 检查器列宽归一化

`EnsureInspectorColumnWidth` 从“只修第一个可见面板的那一列”改成**遍历所有检查器 pane**（当前 Parent + PreviousContainer），每一列：

- `DockMinWidth = 300`；
- 已知漂移值 `260 / 304 / 320` → `300`；
- 用户自己拖出的 280/340 保持不动。

### 2.5 关键面板判据

`EnsureCriticalPanelsVisible` 从 `if (anchorable.IsVisible) continue;` 改为
`if (anchorable.IsVisible && !anchorable.IsAutoHidden) continue;`，自动隐藏的流程导航也会走 `ShowAnchorable` 收回常驻。

### 2.6 `HidePanel` 注释纠偏

明确写清：`IsVisible=false` 进的是 **Hidden 区**（Parent=LayoutRoot），不是图钉的 **自动隐藏**（Parent=LayoutAnchorGroup）；两者恢复路径不同。

### 2.7 `SaveToString` 失败也恢复界面

原实现先把所有文档/日志 pane 放回、序列化、再按模式收回去；`Serialize` 抛异常时 `catch` 直接返回 null，界面会永久停在“全部文档可见 + 日志展开”的临时态。改为 `try/finally`：成功/失败都重新 `ApplyDocumentMode` + `ApplyConsoleState`。

### 2.8 新增断言（UIThemeSmokeTest）

| 断言 | 覆盖 |
| --- | --- |
| 旧布局缺 `CanDockAsTabbedDocument/AutoHideWidth/CanFloat`：恢复后补回 | 2.1 |
| 记忆指向文档 pane 时 `ShowPanel` 不塞进中央文档 | 2.2 |
| 自动隐藏组按面板收回：点工具箱只回来工具箱 | 2.3 |
| 检查器列宽 304 → 300、260 → 300 | 2.4 |

## 三、未做 / 偏差

- **没有强制把检查器合并回一列**：真机布局里属性/图层/图元工具箱一列、工具箱另一列（历史自动隐藏/拖动留下的拆分）。两列宽度都已归一到 300；是否合并回单列涉及“用户是否故意拆列”的产品判断，未在本轮强制做。
- **全量闸门未绿**：`FlowCanvasChecks` 的 4 条运动卡报警断言红，是并发会话正在改运动代码导致的；本机 15:0x 的多次复跑在 3708/0、3706/2、3704/4 之间波动，同一 exe 同一源码。与 AvalonDock 改动无关。
- 真机“多面板自动隐藏组 → 点一枚胶囊只回来一个”的完整 UI 操作未稳定跑完：CUA 对 WPF 图钉的物理点击只聚焦，需要“点击 + Space”才切换；右侧胶囊贴边，点击必须落在左半区（右 8px 是窗口 resize border）。该语义已由 Smoke 的 `GetAutoHiddenGroupSiblingIds` + `ShowPanel` + `HidePanel` 断言覆盖。
- 中央文档仍允许浮动（设计基线“可拆分/浮动”）；浮动文档在模式切换/保存恢复下的行为没有新增断言。

## 四、下一步

1. 等并发会话完成运动代码后重跑 `tools\run_gates.ps1`，确认 14/14；若 4 条运动断言仍红，按独立任务排查（与布局无关）。
2. 若要恢复“检查器单列”，先与用户确认是否允许覆盖用户手动拆列，再在 `LoadFromString` 后做一次 pane 合并（需新增断言）。
3. 若要给胶囊点击加自动化回归，优先在 `UIThemeSmokeTest` 直接调 `ShowInspectorCapsuleCommand`；真机 UIA 点击只作人工验收（图钉需 focus+Space、胶囊要点左半区）。

## 五、踩过的坑

- **`XmlLayoutSerializer` 只还原 XML 里写过的属性**：新加的 `CanDockAsTabbedDocument=False` / `AutoHideWidth=300` / `CanFloat=False` 在旧布局里没有，恢复后全部回落库默认；必须在 `FillContents` 之后按 ContentId 重新套策略。
- **`LayoutAnchorable.Show()` 会接受 `LayoutDocumentPane`**：临时探针实测 `PreviousContainer` 指向文档 pane 时，`Show()` 把 anchorable 插进文档 pane 且不抛异常；判废必须检查容器类型。
- **`CanFloat=False` 不阻止图钉**：图钉仍可用，但 CUA 的物理点击只聚焦；`click + Space` 才真正 Toggle。测试“自动隐藏”时不要以“点击后没反应”判定功能坏了。
- **胶囊贴右边必须点左半区**：右 8px 是窗口 resize border，点 (1268,120) 不生效，点 (1260,120) 生效（真机日志 `15:02:33 [胶囊] Click 到达`）。
- **自定义 `OutDir` 末尾不要带反斜杠**：`-p:OutDir=...\avalon-fix\` 会把引号转义、生成带尾随空格的目录，后续 `GetFiles` 枚举不到；用 `-p:OutDir=...\avalon-fix2`（无尾斜杠）。
- **并发会话会反复改运动/资产代码**：`FlowCanvasChecks` 的红与 `SolutionProjectAssetCatalog.cs` 的 mtime 同步变化；先看 mtime 再判断归属，别把别人的半成品算到自己头上。
