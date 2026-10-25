# ProjectOverview.md 退役说明

## 结论

`docs/ProjectOverview.md` **已退役删除**（2026-10-24，B3 阶段）。

## 为什么删

它成文于 2026-10-05，是项目最早期的一份总览式叙述文档。到 2026-10-24 时：

1. **内容已被现行文档取代**。`docs/项目全景说明.md`（118 KB）在其开头第 6 行就写明：

   > 仓库内 `docs/ProjectOverview.md`、`ArchitectureGuide.md`、`ProjectDetailedModules.md` 成文于 2026-10-05，部分内容已被后续改造推翻（例如启动自检链、Core 分层、SCADA 模块）。本文以当前代码为准；旧文可作历史参考，不应作为现状依据。

2. **它是唯一一份"谁都不指向它"的核心文档**。`audit_docs.ps1` 的 ORPHAN 检查把 `docs/` 根下所有参考型文档扫了一遍，`ProjectOverview.md` 与 `项目全景说明.md` 是仅有的两个"没有任何 README 提及"的根级文档。而 `项目全景说明.md` 是现行主文档，必须保留并登记；`ProjectOverview.md` 则是被明示为"不应作为现状依据"的旧文。

3. **它的去留没有歧义**：既不是现行参考、又没有人链接、还自己声明了已被推翻。

> 补充：同批成文的 `ArchitectureGuide.md`（5 KB）与 `ProjectDetailedModules.md`（3 KB）**保留**，已迁入 `docs/架构文档/`。理由是体量小、仍在 `docs/项目全景说明.md` 与 `docs/架构文档/README.md` 中被明确引用为"早期总览"对照物，删掉会留下悬空引用。

## 原文去哪了

原文共 8,065 字节，含"项目定位 / 体系结构（ASCII 分层图）/ 模块清单 / 技术栈 / 上手路径"。

- **Git 历史完整保留**：`git show <B3 提交哈希前一个提交>:docs/ProjectOverview.md`
- **退役前路径**：`docs/ProjectOverview.md`
- **退役提交**：见 `git log --oneline --all -- docs/ProjectOverview.md`

## 怎么恢复

如果将来需要回看这份旧文（比如要和 `项目全景说明.md` 对照"当初是怎么想的"）：

```bash
git log --oneline --all -- docs/ProjectOverview.md    # 找删除前最后一个提交
git show <那个提交>:docs/ProjectOverview.md > /tmp/ProjectOverview.md
```

**不要**把它放回 `docs/` 根目录——那会立刻被 `tools/audit_docs.ps1` 判为 ORPHAN。若要长期保留，应放到 `docs/归档/`（该目录在 `orphanPolicy.journalDirs` 豁免清单内）。

## 相关

- 现行主文档：[`docs/项目全景说明.md`](../项目全景说明.md)
- 早期总览对照：[`ArchitectureGuide.md`](ArchitectureGuide.md)、[`ProjectDetailedModules.md`](ProjectDetailedModules.md)
- 结构审计脚本：[`tools/audit_docs.ps1`](../../tools/audit_docs.ps1)、白名单 [`tools/docs-layout.json`](../../tools/docs-layout.json)
