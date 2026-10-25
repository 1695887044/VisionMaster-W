# docs/AI协作 — AI 协作的资料与范式

> 这里放"给 AI 客户端（DSH / ZCode / 其他）读的东西"，**不绑定任何客户端**：
> 判据仍在仓库根 [`AGENTS.md`](../../AGENTS.md)，本目录放展开后的操作范式。

## 目录

| 子目录 | 内容 |
|---|---|
| [`skills/`](skills/README.md) | 可复用排查/实现范式（`vm-property-grid`、`wpf-ui-debug`），2026-10-24 从 `.zcode/skills/` 迁入 |
| [`操作计划/`](../AI操作计划/) | 跨会话的 AI 协作计划与执行记录（另见上级目录地图） |

## 为什么有这一层

2026-10-24 之前，本项目同时存在 `.zcode/agents/*.md`（13 个角色定义）、`.zcode/skills/*.yaml`（6 份）
和 `AGENTS.md`，三套内容互不一致 —— 读者无法判断哪套生效。治理结论：

1. **判据只留一份**：`AGENTS.md`。
2. **角色定义归客户端**：角色怎么分派是 DSH/AgentTeams 的机制，不进仓库文档。
3. **范式留仓库**：`skills/` 里的内容是项目事实（属性面板生成器优先级、主题资源作用域……），
   与"用哪个客户端"无关，所以放在 `docs/` 下、任何客户端都能读。
4. **镜像只做单向**：`AGENTS.md → %USERPROFILE%\.zcode\AGENTS.md` 的硬约束镜像由
   [`tools/sync_constraints.ps1`](../../tools/sync_constraints.ps1) 负责，禁止手改镜像侧。

返回：[`docs/README.md`](../README.md)
