# vm-property-grid（属性面板技能）

> 本目录只有 `SKILL.md` 一个文件，所以不放 README —— 入口就是它本身。
> 归属：`docs/AI协作/skills/`；上层说明见 [`../../README.md`](../../README.md)。
> 被引用处：`AGENTS.md` R106（多态配置不许搭窗口）。

## 什么时候读它

要做**属性面板**（`FlatPropertyGrid`）相关的改动时：

- 配置类上有字段需要"切换后整表重建"（`RequireRefresh`）
- 同一个属性在不同模式下要显示不同控件（多态配置）
- 不确定枚举该读 `[Display]` 还是 `[Description]`
- 不确定什么时候该手写视图、什么时候用 PropertyGrid 自动生成

## 内容

[`SKILL.md`](SKILL.md) —— Priority 表、两条枚举上屏路、草稿语义、"何时该手写视图"。
