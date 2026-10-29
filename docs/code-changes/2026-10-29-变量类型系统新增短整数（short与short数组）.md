# 2026-10-29 变量类型系统新增短整数（short / short[]）

> 需求来源：汇川变量表（12.csv，2358 行）里有 619 个 Int16 点（含 2 个 Int16[20] 数组）。
> 变量类型表原先没有 16 位类型，映射成 int 会按 4 字节读，把相邻字段的值一起读进来。
> 实现方式：主会话派工（R10/R11），智能体按清单落地，本报告为收尾落盘（R177）。

## 一、当前状态

类型系统已支持 `short` 标量与 `short[]` 数组：创建弹窗可选中、导入白名单放行、轮询/写入链路天然支持
（轮询解码与写入编码按 TypeCode 泛化，`HslHelper.MinByteCount(short)=2`、`ArrayConversion.IsNumericElement` 已含 Int16）。

## 二、已做

| 文件 | 改动 |
|---|---|
| `VisionMaster\ViewModels\DialogViewModels\GlobalVariableManagerViewModel.cs` | `AllTypes` 加 `短整数 (short)`、`短整数数组 (short[])`（不改任何既有显示名——显示名变了会让存量 xlsx 导入导出断） |
| `Core\Helpers\TypeCache.cs` | `RegisterCommonTypes` 补 `RegisterType(typeof(short[]))`（short 本体已有） |
| `VisionMaster\Services\DeviceVariableIoService.cs` | 导入数组元素白名单放行 `short`，Fail 文案同步 |
| `VisionMaster\ViewModels\DialogViewModels\GlobalVariableManagerViewModel.cs`（`AddNetworkVariable`） | **顺手修**：确认路径上还有一份与导入完全相同的数组元素白名单，只改导入会让 short[] 在弹窗可选、确认被拒。两处已同步；全仓确认无第三份 |

核查结论（智能体）：
- `ValueTextCoercion` / `VariableValueConverter` / `ArrayConversion` 都是 TypeCode 泛化，short 天然支持，无需改。
- `ToDataValueType` 早已映射 `short → DataValueType.Int16`（数组按元素类型）。
- 构建：`Core` 与 `VisionMaster` 均 0 错误退出 0（日志 `artifacts\logs\build-core-short.log` / `build-vm-short.log`）。

## 三、未做

- **未跑 run_gates**：本报告撰写时刚落地，gate 在导入转换器交付时统一跑（本文件所在轮次先落盘）。
- `FlowResultSourceDialogViewModel.TypeTextOf`（767 行）对未知类型返回 null → short 变量会从「结果变量」候选**静默消失**。
  该对话框的模式描述限定 bool/string/int/double，是否放行取决于流程统计引擎下游，属范围外决策，未动。
- 未加 `ushort`（本次数据不需要；要加照抄 short 两处 + 白名单即可）。

## 四、下一步

1. CSV→xlsx 转换器 + 真跑 `BuildPlan` 校验（另见同日 EasyNet 文档）。
2. 若流程统计要用 short 变量，再决定 `TypeTextOf` 是否放行。

## 五、踩过的坑

- **数组元素白名单有两份**（导入 `DeviceVariableIoService` 与创建确认 `AddNetworkVariable`），漏改一份的症状是
  "弹窗里能选、点确定被拒"——半静默。新增元素类型时两处必须同步。
- 类型显示名是导入/导出的**协议**（`ResolveType` 按 DisplayName 精确匹配），改名等于断存量表格，只能加不能改。
