# 2026-10-24-zcode-desktop-state

ZCode 桌面版退役后留下的工作状态（2026-10-24 归档）。

## 为什么下线

仓库同时被 DSH 与 ZCode 两个客户端操作时，`.zcode\` 下攒了 ZCode 自己的会话计划与工作流草稿。
ZCode 退役后 DSH 从不读这些目录；它们既不是文档也不参与构建。有阅读价值的那部分（实施计划、审核清单、交接说明）
已经整理进 `docs\`，本目录只剩工具私有状态，归档是为了"删了还能拿回来"。

## 移入清单

| 原路径 | 说明 |
|---|---|
| `.zcode/plans/plan-sess_1ecd39f5-cd9c-425f-935b-2b7aa7985de7.md` | ZCode 会话计划（待办大纲，已执行或已废弃） |
| `.zcode/plans/plan-sess_3ec804f6-b891-44d5-9aea-5555a6322a0f.md` | ZCode 会话计划（待办大纲，已执行或已废弃） |
| `.zcode/plans/plan-sess_7a762cea-80bf-493a-a569-307145ca8b9b.md` | ZCode 会话计划（待办大纲，已执行或已废弃） |
| `.zcode/plans/plan-sess_bf9a5cc3-13d6-4de6-82ed-59814927e49d.md` | ZCode 会话计划（待办大纲，已执行或已废弃） |
| `.zcode/workflow-drafts/二期真并行执行方案设计与交叉质证.dwf.ts` | 工作流草稿（未运行的编排定义） |

## 直接删除（未归档）

下列文件文件头自带 `safe to delete once the run has settled`，是工作流运行器每次启动前生成的一次性脚本，已删除：

- `.zcode/workflow-runs/dwfrun-0aa03fc1-1996-4ef3-a744-ed027d7a0835.mjs`
- `.zcode/workflow-runs/dwfrun-143ce225-504f-4acf-9afd-f3f6c0eb7285.mjs`
- `.zcode/workflow-runs/dwfrun-28c03326-71c3-4c75-828e-bd044eae3847.mjs`
- `.zcode/workflow-runs/dwfrun-bff4a72b-d099-41b1-8036-50d70fb0b312.mjs`
- `.zcode/workflow-runs/dwfrun-f7ec8e24-af10-4f83-80f2-18a40447fb0f.mjs`

## 原地保留（不在此目录）

- `.zcode\skills\vm-property-grid\SKILL.md` — `AGENTS.md` R106 与 skill 附录直接点名引用，移走会断链。
- `.zcode\skills\wpf-ui-debug\SKILL.md` — 同上。
- `.zcode\workflow-runs\.gitignore`、`.zcode\workflow-drafts\.gitignore` — 工具生成，留着以免工具复活时工作区变脏。

## 怎么恢复

按上表把文件搬回 `.zcode\plans\` 或 `.zcode\workflow-drafts\` 即可。本目录不参与构建、不进 `VisionMaster.sln`、
不会被任何脚本读取。

## 何时可彻底删除

确认不再启动 ZCode 客户端、`.zcode\` 也不再被任何工具写入之后，可整目录删除。
