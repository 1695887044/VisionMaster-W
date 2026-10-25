# 2026-10-24-zcode-legacy-agents

## 为什么下线

本目录收纳的是 **ZCode 时代遗留的 Agent/Skill 定义**，共 19 个文件（13 个 `agents/*.md` + 6 个 `skills/*.yaml`）。

判定为"僵尸"的依据（2026-10-24 实测，非推测）：

| 判据 | 结论 |
|---|---|
| 是否被任何现行文档引用 | **无**。全仓 grep 这 19 个文件名，`AGENTS.md` / `docs/` / `tools/` 均无引用 |
| 是否被 ZCode 或 DSH 实际加载 | **否**。DSH 通过 `AGENTS.md` 注入约束，AgentTeams 通过 profile 定义成员，都不读 `.zcode/agents/` |
| 内容是否仍有价值 | 大部分已被 `AGENTS.md` 的 §30（UI 工作流 R111-R125）与 `docs/工程纪律/` 覆盖或超额覆盖 |

用户原话（2026-10-24）："我用了Zcode和你 双方的约束也不同步"——这 19 个文件正是"不同步"的实体：它们是**第二套约束源**，与 `AGENTS.md` 并行存在且内容不一致，读者无法判断哪套生效。

## 保留了哪些、为什么

`.zcode/skills/` **没有清空**，保留两个目录：

| 保留项 | 原因 |
|---|---|
| `.zcode/skills/vm-property-grid/SKILL.md` | `AGENTS.md` R106 明确指路："用法见 `.zcode/skills/vm-property-grid/SKILL.md`（Priority 表、两条枚举路、草稿语义、何时该手写视图）"。**删了就断链** |
| `.zcode/skills/wpf-ui-debug/SKILL.md` | `AGENTS.md` 附录明确指路："界面改了没生效 → §30.2+skill `wpf-ui-debug`"。**删了就断链** |

同理保留：
- `.zcode/plans/` — ZCode 会话计划留档，属历史记录，不参与构建
- `.zcode/workflow-drafts/`、`.zcode/workflow-runs/` — workflow 运行产物，各有 `.gitignore`

**注意**：这两个保留的 skill 仍是 `.zcode/` 下的第二套约束源。后续 B5（双约束源同步）应决定它们迁到 `docs/工程纪律/` 或统一改由 DSH skill 目录承载——**本次不动，因为改动会同时影响两处引用。**

### 更新（2026-10-24 复核）

用户已明确"两个客户端都继续用"，B5 因此定为**单一事实源 + 单向镜像**：`D:\C#\VM\AGENTS.md` 仍是唯一事实源，
`tools\sync_constraints.ps1` 把 7 类硬约束写进 `%USERPROFILE%\.zcode\AGENTS.md` 的标记块，`run_gates.ps1` 用 `-Check` 守门。

**这两个 skill 不迁、不删**，理由变成三条：

1. `AGENTS.md` R106 与附录**按路径指名**它们，迁走就断链（`R59` 同族：断链比重复更糟）；
2. 它们是 ZCode 侧唯一还在生效的**操作手册类**约束（怎么查属性面板、怎么调界面），而 DSH 侧同类 skill 目录不通用；
3. 它们的内容与 `AGENTS.md` **不冲突**——只讲"怎么做"，不讲"必须怎样"，不构成第二套判据源。

> 判据：**判据只许一份**（`AGENTS.md`），**操作手册允许多份**。区分看一句——它说"必须/不许"就是判据，
> 说"推荐这样做"就是手册。若日后这两个 skill 里出现与 `AGENTS.md` 矛盾的强制语句，先改 `AGENTS.md`
> 再回来改它，不要反向。


## 怎么恢复

19 个文件都是用 `git mv` 迁移的，**Git 历史完整保留**。

```bash
# 看这一批都动了什么
git log --oneline --follow _Retired/2026-10-24-zcode-legacy-agents/agents/architect.md

# 整体回滚（恢复到迁移前的状态）
git revert <this-commit>
```

若只想拿回某一个文件：

```bash
git show <commit-before-migration>:.zcode/agents/architect.md
```

回滚后需要**手工重建 `.zcode/agents/` 目录**（迁移时它被 `rmdir` 删了）：
```bash
mkdir -p .zcode/agents
```

## 何时可以真正删除

满足以下**全部**条件后，本目录可整体删除：

1. B5 双约束源治理完成，`.zcode/skills/` 下两个保留 skill 已决定去留
2. 用户在 ZCode 环境下不再需要这 13 个角色（即确认 ZCode 已停用）
3. 距迁移时间 ≥ 6 个月（2027-04-24 之后），期间无回滚需求

**在那之前不要删**——它是"当初为什么这么分"的唯一实物证据（`AGENTS.md` R177：上下文不是记忆，工作区文件才是）。

## 文件清单

```
agents/                      13 个，全部来自 .zcode/agents/
  architect.md               架构评审
  comm.md                    通讯（旧版，已被 communication.md 取代）
  communication.md           通讯
  docs.md                    文档（旧版，已被 documentation.md 取代）
  documentation.md           文档
  hmi.md                     上位机界面
  motion.md                  运动控制
  operation_control.md       操作控制
  reviewer.md                代码审查
  ui.md                      界面（7317 字节，本批最大，内容最多）
  upper_control.md           上位机
  vision.md                  视觉算法
  visual_development.md      视觉开发

skills/                      6 个，全部来自 .zcode/skills/
  architect.yaml
  communication.yaml
  documentation.yaml
  operation_control.yaml
  upper_control.yaml
  visual_development.yaml
```

> 注：`comm.md`/`docs.md`/`hmi.md`/`motion.md`/`reviewer.md`/`vision.md`/`ui.md` 这 7 个**没有对应的 `.yaml`**；
> `skills/` 里另有 6 个 yaml 对应的 `.md` 存在。两套集合本就不同步——这正是本次治理要暴露的问题之一。
