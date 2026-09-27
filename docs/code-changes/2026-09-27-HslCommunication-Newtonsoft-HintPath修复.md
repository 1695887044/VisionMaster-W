# 2026-09-27 HslCommunication Newtonsoft.Json HintPath 路径修复

## 一、需求与决策

### 现象
新设备 clone 仓库后编译 `VisionMaster.sln`，`DLL\HslCommunication\HslCommunication.csproj`（.NET Framework 4.8 / packages.config 老式项目）报约 57 个错误，其余项目因它失败而不编译：
- CS0234：命名空间 `Newtonsoft.Json` 中不存在 `Linq`；
- CS0246：找不到 `JObject / JArray / JToken / Formatting / JsonIgnore`。

### 根因
该 csproj 中 Newtonsoft.Json 的 `HintPath` 少了一级目录。项目位于 `DLL\HslCommunication\`，距解决方案根的 `packages\` 有两级：
- 错误：`..\packages\...` → 解析为 `DLL\packages\...`（不存在）；
- 正确：`..\..\packages\...` → 解析为解决方案根 `packages\...`。

### 为什么本机以前能编过（掩盖因素）
本机磁盘上除根 `packages\` 外，还存在一份**异常多余的** `DLL\packages\Newtonsoft.Json.13.0.4`（推测曾在 DLL 目录下单独还原过），恰好让错误 HintPath 也能找到文件。两个 packages 目录均被 .gitignore 忽略（`**/[Pp]ackages/*`），新设备 clone 后均不存在，必然报错。GAC 中无 Newtonsoft.Json。

### 决策（已经用户确认）
- 只改 Newtonsoft.Json 这一行 HintPath，不动其他任何引用；
- `WPF-Halcon-流程拖拉` 中失效 HintPath 不在主解决方案内，本次不处理；
- 本机多余的 `DLL\packages` 用户选择保留（忽略目录，不影响）。

## 二、修改文件清单

| 文件 | 改动 |
| --- | --- |
| `DLL/HslCommunication/HslCommunication.csproj` | 第 40 行 HintPath：`..\packages\` → `..\..\packages\`（仅此一行） |

未改动任何 .cs 代码。

## 三、验证结果

| 项 | 结果 |
| --- | --- |
| `dotnet build VisionMaster.sln` | **0 个错误**（1185 个警告为仓库既有，非本次引入） |
| 程序集解析来源 | 强制 Rebuild 该项目，csc 命令行实际引用 `仓库根\packages\Newtonsoft.Json.13.0.4\lib\net45\Newtonsoft.Json.dll`，证明走的是正确路径 |
| 主程序产物 | `VisionMaster\bin\Debug\net9.0-windows\VisionMaster.exe` 已生成 |

## 四、新设备还原注意

1. `DLL\HslCommunication` 是 packages.config 老式项目，**`dotnet restore` 不会还原它**；需用 `nuget restore VisionMaster.sln`，或在 Visual Studio 中右键解决方案 → 还原 NuGet 包。
2. 还原目标是解决方案根 `packages\`（被 .gitignore 忽略，不入库）。
3. 还原后再编译，期望 0 错误。

## 五、已知边界

1. 本机 `DLL\packages` 多余副本按用户意愿保留；它被忽略、不入库，也不影响修复后的正确解析。
2. `WPF-Halcon-流程拖拉` 旧参考项目中的失效 HintPath 仍未修，不影响主解决方案编译。
3. 若目标机器缺少 .NET Framework 4.8 Developer Pack（目标框架定位包），该项目仍可能无法编译，需另行安装。
