# 2026-10-04 插件配置属性源生成器（StepConfig partial 属性）：7 插件 97 属性迁移

- 日期：2026-10-04
- 范围：新增 `Shard\Core.PluginGenerators\`（生成器工程：csproj + StepConfigGenerator.cs）；新增 `Plugins\Directory.Build.props`（Analyzer 接线）；`VisionMaster.sln` 登记；7 个插件工程 97 个 `[StepConfig]` 属性迁移
- 依据：`docs\问题汇总\2026-10-04-插件配置属性样板压缩（源生成器+约定收敛）讨论.md`（同日"已拍板并落地"；本记录为其首轮实施）
- 背景：`[StepConfig]` 配置属性（后备字段 + SetProperty + 副作用 setter）样板约 7.6 行/个。拍板路线：**源生成器（partial property 写法，完整版含钩子）+ 约定三选一收敛**——零公共契约改动、可逐插件渐进迁移。本记录覆盖生成器工程与首轮 7 个工程迁移。

> 口径标注：**已构建验证** = 施工会话实际运行过对应构建命令；**静态核对** = 不运行代码的源码 / 产物检查（含本次落盘的复核）。

---

## 一、改动清单

| 文件 / 产物 | 内容 |
| --- | --- |
| `Shard\Core.PluginGenerators\Core.PluginGenerators.csproj` | netstandard2.0 + `Microsoft.CodeAnalysis.CSharp` 4.12.0（`PrivateAssets=all`）、`IsRoslynComponent`；刻意**不放** `Plugins\` 下（否则会被投递约定当插件工程处理） |
| `Shard\Core.PluginGenerators\StepConfigGenerator.cs` | `IIncrementalGenerator`：只认「partial、无实现体」的属性定义声明；发射后备字段 + `SetProperty` + `On<名>Changing/Changed` 钩子声明；用户已手写实现（另一半声明）的属性跳过；诊断 CPG0001~CPG0005（Error，不静默） |
| `Plugins\Directory.Build.props` | 目录级接线（MSBuild 为 `Plugins\` 下每个工程自动导入）：`ProjectReference` + `OutputItemType=Analyzer` + `ReferenceOutputAssembly=false` → 24+ 个插件工程一次挂接，生成器 DLL 不进插件输出 |
| `VisionMaster.sln` | `dotnet sln add` 登记生成器工程（Shard 文件夹） |

**迁移明细（97 个属性）**：

| 插件工程 | 迁移属性数 |
| --- | --- |
| Plugin.ResultUpload | 17（试点，参考样板） |
| Plugin.Matching | 6 |
| Plugin.ExcelExport | 11 |
| Plugin.DataRecord | 11 |
| Plugin.Calibration | 13 |
| Plugin.CaliperMeasure | 15 |
| Plugin.BlobDetect | 24（收尾） |
| **合计** | **97** |

行数变化：7 个插件主文件合计 **8992 → 8642**（净减 ~350 行；单插件最多 BlobDetect -76、ResultUpload -72）。——施工会话统计。

**跳过项（保持手写）与原因**：

| 跳过项 | 原因 |
| --- | --- |
| ResultUpload `Headers` | 集合 `new()` 默认值（setter 内联兜底） |
| ResultUpload `Fields` | Unhook/Hook 成对替换 setter |
| DataRecord `Columns` | Unhook/Hook 成对替换 setter |
| DataRecord `ImageSaveMode` | 结构型 setter：变更后重建动态输入端口 |
| Calibration `PointRows` | 集合 `new()` 默认值（setter 内联兜底 + 刷新副作用） |
| CaliperMeasure `CaliperRegions` | 结构型 setter：变更后重建动态端口 |
| BlobDetect `DetectTarget` | setter 需要读取旧值 |

> 跳过项仍在 `[StepConfig]` 反射面内（计数含它们）；对应速查「三选一」中的第 ③ 类。

---

## 二、生成器行为与契约要点

- 谓词：只处理「`partial` 且无实现体」的属性定义声明；已有手写实现（另一半声明）时跳过该属性、让给用户。
- 发射形态（同一 partial class 内；节选自 ResultUpload 产物）：

```csharp
private int _retryCount = 3;

partial void OnRetryCountChanging(ref int value);
partial void OnRetryCountChanged(int value);

public partial int RetryCount
{
    get => _retryCount;
    set
    {
        var __value = value;
        OnRetryCountChanging(ref __value);            // 赋值前：可归一化入参
        if (SetProperty(ref _retryCount, __value))
            OnRetryCountChanged(__value);             // 仅值真的变化时触发
    }
}
```

- 后备字段 = `_` + 属性名首字母小写；属性名 / 类型 / `[StepConfig]` 位置不变（`.vms` 键名与基类反射发现不受影响）。
- `[DefaultValue(常量表达式)]`（`System.ComponentModel`）：partial 定义声明不能带初始化器，默认值由它承载；省略则取类型默认值。
- 诊断（均为编译 Error，不静默）：

| 编号 | 含义 |
| --- | --- |
| CPG0001 | 不支持的 `[StepConfig]` partial 属性形态（非顶层 / 泛型 / record class 等） |
| CPG0002 | 不支持的属性修饰符（static / virtual / override / new …） |
| CPG0003 | 需要同时声明 `get;` 与 `set;` |
| CPG0004 | 生成的后备字段名被现有成员占用 |
| CPG0005 | `[DefaultValue]` 形态不支持（如非单参数常量） |

---

## 三、验证证据

| 项 | 结果 | 口径 |
| --- | --- | --- |
| 7 个插件工程单独构建 | 0 error | 已构建验证（施工会话） |
| `dotnet build VisionMaster.sln` | 0 error | 已构建验证（施工会话） |
| sln 外工程 `Plugin.Camera.Network`、`Plugin.ImageAssign` 单独构建 | 0 error | 已构建验证（施工会话） |
| 契约反射核对：7 个 DLL 的 `[StepConfig]` 属性计数 = 19 / 13 / 11 / 13 / 14 / 16 / 25（ResultUpload / Matching / ExcelExport / DataRecord / Calibration / CaliperMeasure / BlobDetect），与源声明一致 | 一致 | 反射核对（施工会话）；静态核对（落盘时）：7 个插件目录内非注释 `[StepConfig]` 声明行逐一吻合 19 / 13 / 11 / 13 / 14 / 16 / 25 |
| `Modules\` 投递 | 25 个 `Plugin*.dll`；未见 `Core.PluginGenerators.dll` / `Core.Interfaces.dll` / `Core.Controls.dll` | 静态核对（落盘时） |
| 生成器产物抽查 | `Plugins\Plugin.ResultUpload\obj\Debug\net9.0-windows\generated\Core.PluginGenerators\Core.PluginGenerators.StepConfigGenerator\Plugin.ResultUpload.ResultUploadPlugin.StepConfig.g.cs`（后备字段 / 钩子声明 / `SetProperty` 实现齐备） | 静态核对（落盘时） |

---

## 四、说明（既有警告与运行期影响）

- `MatchingPlugin.cs:1701`、`MatchingPlugin.cs:1821` 两处警告与 `BlobDetectPlugin.cs:769`（CS0649）为**迁移前既有**，非本次引入（施工会话以编辑快照核查）。
- `[DefaultValue]` **无运行期消费者**（生成器只在编译期读取），不改持久化行为；属性名不变 → `.vms` 灌值 / 写回键名不变。静态核对（生成器源码 + 基类反射发现逻辑）。

---

## 五、未决 / 待人工验收

- 宿主人工验收未做：GUI 打开既有 `.vms` 回显、试运行、XAML 绑定。
- CPG 负面用例未做：故意写错形态（缺 partial 类 / 不支持修饰符等），验证 CPG0001~CPG0005 确实报编译错误。
