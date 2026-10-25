# docs/AI协作/skills — 可复用排查/实现范式

> 这两份 SKILL 原在 `.zcode/skills/`（ZCode 时代），2026-10-24 迁到这里：
> **它们讲的是本项目的事实与判据，不是某个客户端的配置**，放在任何客户端都会读的
> `docs/` 下才不会再变成"第二套源"。

## 文件

| Skill | 什么时候读 | 对应纪律 |
|---|---|---|
| [`vm-property-grid/SKILL.md`](vm-property-grid/SKILL.md) | 用属性面板承载"多态配置"（切协议/切模式后字段整组换掉）时；判断该走属性面板还是手写视图时 | `AGENTS.md` R106、R107、R84 |
| [`wpf-ui-debug/SKILL.md`](wpf-ui-debug/SKILL.md) | 改主题/模板后"没生效"、"样式没应用"、渲染结果与预期不符时；**别凭截图猜** | `AGENTS.md` §30.2、R63、R65 |

## 使用方式

任何客户端（DSH / ZCode / 别的）都可以直接读这两份 Markdown 全文，**不依赖任何专属工具**：
先读判据，再按里面的"第 0 步证明新代码 → 静态排查 → 探针 → 按症状定位"顺序走。

## 与工程纪律的关系

`AGENTS.md` 是**判据的唯一事实源**，本目录是**怎么做**的展开（含实测踩坑记录）。
两者冲突时以 `AGENTS.md` 为准；本目录里的实测细节可以反过来提议补进 `AGENTS.md`。

返回：[`docs/AI协作/README.md`](../README.md) · [`docs/README.md`](../../README.md)
