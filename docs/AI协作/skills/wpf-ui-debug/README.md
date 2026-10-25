# wpf-ui-debug（WPF 界面调试技能）

> 本目录只有 `SKILL.md` 一个文件，所以不放 README —— 入口就是它本身。
> 归属：`docs/AI协作/skills/`；上层说明见 [`../../README.md`](../../README.md)。
> 被引用处：`AGENTS.md` 附录"常见症状 → 章节"（界面改了没生效）。

## 什么时候读它

改了 XAML / 样式 / 资源字典，**界面上看不到变化**，或者看到的是"假结论"时：

- 改了样式没生效，怀疑资源字典没加载
- 用 PowerShell 5.1 验 .NET 9 WPF，得到的"样式没生效"结论
- 想截图对比，但不确定该看哪个数（`ActualWidth` / `ViewportHeight` / 墨迹行数）
- 需要写一次性探针工程去验一件事

## 内容

[`SKILL.md`](SKILL.md) —— 探针工程怎么建、判据看哪里、哪些"看起来坏了"其实是假象。
配套硬约束见 `AGENTS.md` R134（必须 `net9.0-windows` 探针工程，不许 PowerShell 5.1）与 R184（输入框文字被切的判据是 `ViewportHeight ≥ ExtentHeight`，别看截图）。
