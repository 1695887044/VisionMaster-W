# 已退役探针（2026-10-21）

本目录存放 **2026-10-21 根目录整理**时退役的 6 个探针工程。
它们**不参与任何构建，也不在任何 `.sln` 里**，只作留痕与随时恢复之用。

## 退役清单

| 目录 | 退役原因 | 被谁取代 |
| --- | --- | --- |
| `_ChartProbe` | 旧版曲线探针 | `tools/probes/_ChartProbe5`（AGENTS R77 指定的视觉验证件） |
| `_WebProbe1` | SCADA Web M0 初版探针 | `tools/probes/_WebProbe5` / `_WebProbe6` |
| `_WebProbe2` | 同上 | 同上 |
| `_WebProbe3` | 同上 | 同上 |
| `_ApiDump` | 一次性 API 导出工具 | 无（需要时重新写） |
| `_MvvmDemo` | MVVM 演示件，无生产消费 | 无 |

## 如何恢复

1. 把目录整体移回仓库根目录（**不要**移进 `tools/probes/`——这些工程的 csproj 仍使用 `..\` 相对路径，根目录才是它们的原始位置）。
2. 如果确实要移到别处，必须把所有 `ProjectReference` / `HintPath` 的 `..\` 改成对应的层级。
3. 恢复后 `dotnet build` 该工程确认；要长期使用就登记进 `tools/probes/probes.json` 与 `README.md`。

## 彻底删除

确认不再需要留痕时，直接删掉对应目录即可（本目录不参与编译）。
