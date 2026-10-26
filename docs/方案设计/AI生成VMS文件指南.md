# AI 生成 `.vms` 文件指南（流程 / 变量 / 硬件 / 组态）

> 目的：让 AI 或脚本直接生成 `VisionMaster.exe` 能打开、能编译、能运行的 `.vms` 解决方案。
> 依据：本仓库 `Core\Models\SolutionModel.cs`、`Core\Models\FlowModel.cs`、`Scada\Models\*`，以及 `tools\vms-generator` 的可用生成器。
> 示例：`解决方案\CCG-BGA焊球全检\CCG-BGA焊球全检.vms`、`解决方案\CCG-包装线全检追溯\`、
> `解决方案\CCG-精密尺寸测量站\`（**文件夹化布局 + 方案内相对路径**，见 §1.1），
> 以及 `解决方案\自动化产线\自动化产线.vms`（14 流程 / 315 步 / 8 页组态）和
> `解决方案\综合测试台\综合测试台.vms`（14 流程 / 边界测试）。

---

## 1.1 方案不是一个文件，是一个文件夹（生成前先定这一点）

生成器最容易犯的错：只写一个 `.vms`，图片写绝对路径或 `..\Image\images\xxx.png`。
那样方案一旦搬走、或用户另存为，图就全断——**"资源在方案文件夹里面"是硬要求，不是风格偏好**
（用户裁定见 `docs\UI设计基线\scada-controls\README.md:150`、`scada-controls-batch3\README.md:163`）。

正确的落点形状（`Core\SolutionPaths.cs:12-19` 的注释就是这份示例）：

```
<保存位置>\
└── <方案名>\
    ├── <方案名>.vms          配置文件，文件夹名与文件名必须同名
    ├── Image\                组态图元 / 采集步骤用到的图片（平铺一层）
    ├── Assets\               方案内容资产
    │   ├── Templates\        模板文件（Matching 模板库 JSON 等）
    │   └── Models\           模型文件（.onnx 等）
    ├── Alarms\               报警历史 CSV
    ├── History\              生产历史 CSV（Hmi.DataGrid 的"历史源"读这里）
    └── Logs\                 运行日志
```

五条生成期纪律：

1. **`.vms` 里只写方案内相对路径**：`Image\bga.png`。绝不写 `D:\...`，绝不写 `..\Image\...`。
2. **图片必须平铺在 `Image\` 一层**，不要再套子目录。宿主 `VisionMaster\Services\SolutionAssetService.cs:198`
   的 `TryResolve` 要求 `Path.GetFullPath(相对路径拼到方案根)` 落在 `<方案根>\Image\` 之内；`Image\sub\a.png`
   虽然能过前缀检查，但构建方案时只会把文件平铺复制进 `Image\`，带子目录的引用就成了空引用。
3. **生成时把源图从样图库复制进方案自己的 `Image\`**，`.vms` 只记文件名。同名同内容按 SHA-256 复用，
   同名不同内容自动改名（`name_<hash8>.ext`），绝不覆盖用户已有资产。
4. **相对路径的解析责任在宿主**：`Scada.Controls` 不碰方案目录，它只吃
   `ScadaElementBase.ResolveImageAsset` → 宿主注入的 `ImageAssetResolver`
   （运行时 `VisionMaster\Views\ScadaRuntimeWindow.xaml.cs:191-194/268-269`，编辑器
   `ScadaEditorView.xaml.cs:73-78`，探针 `tools\probes\_ScadaUiProbe\Program.cs:324`）。
   探针与宿主都按「方案文件所在目录 + 相对路径」解析，所以整包搬走就能用。
5. **模板 / 模型资产写 `Assets\Templates\` / `Assets\Models\`**，图片仍写 `Image\`；
   `.vms` 只存 `RelativePath` + `ContentHash`，不把模型二进制写进 JSON。
   `AssetId` 必须是稳定 GUID；插件按 Id 解析文件，不再依赖绝对路径。

流程里 CSV/Excel 的落点同理，写相对目录（`DirectoryTemplate = "History\{StepName}\{yyyy-MM}\"`），
不要写 `D:\Records\...`。**已知边界**：`RecordingHub.ExpandTemplate` 之后走 `Path.GetFullPath`，
相对目录按宿主进程当前目录解析，没有方案根锚点；要让账本严格落进方案自己的 `History\`，
需把宿主工作目录设到方案根，或用绝对路径覆盖模板。生成器保证的是"配置文件里不含绝对路径、可整包搬迁"。

自检判据（生成器落盘前必须过）：

- 全文正则 `"[^"]*[A-Za-z]:\\` 命中数 = 0；
- 全文正则 `\.\.[\\/]` 命中数 = 0；
- 每个 `SourcePath` / `SourceImagePath` 值都匹配 `^Image\\[^\\]+$`，且该文件确实存在于方案 `Image\`；
- 每个 `TemplateAssets[].RelativePath` 都匹配 `^Assets\\Templates\\[^\\]+$`，`ModelAssets[].RelativePath` 都匹配 `^Assets\\Models\\[^\\]+$`，且文件存在；
- 每个 `TemplateAssets[].AssetId` / `ModelAssets[].AssetId` / `ImageAssets[].AssetId` 都是稳定 Guid；插件 `TemplateAssetId` / `ModelAssetId` 能反查到对应集合；
- 把整个方案文件夹复制到另一个目录后，**两个探针仍然全绿**（这是唯一的搬迁证据）。

`tools\vms-generator\Program.CcgSolutionFolder.cs` 是这套纪律的可复制实现：
`WriteFolderSolution`（建目录 + 收图 + 复制 + 自检）、`SourceImageMap`（样图库子目录 → 方案内平铺文件名）、
`AssertNoExternalPath`、`CcgCsvRecord`/`CcgExcel`（相对输出目录）。

2026-10-25 起，生成器的**所有**模式（默认六方案、`--stress`、`--smart-line`、`--ccg-*`）都走
`WriteFolderSolution`；旧的 `WriteSolution`（平铺 + 绝对路径）已删除。压测产物在
`解决方案\压测方案-3000节点\` 与 `解决方案\压测方案-3000节点-单流程\`。
唯一例外是 `FlowCanvasChecks` 的断言夹具 `解决方案\合成颜色验证.vms`（不是生成器产物）。


---

## 1. 文件本质

`.vms` 就是 UTF-8（无 BOM）JSON，反序列化为：

```csharp
SolutionModel
```

序列化配置是 Newtonsoft `TypeNameHandling.Auto` + 白名单 binder。  
**步骤节点必须自己写 `$type`**，否则会落到基类/无法构造。

最小骨架：

```json
{
  "SolutionName": "示例方案",
  "Version": 1.0,
  "Config": { "ImageViewMode": 0 },
  "Flows": [],
  "SolutionId": "11111111-1111-1111-1111-111111111111",
  "CommunicationConfigs": [],
  "IoBuses": [],
  "IoDevices": [],
  "CameraConfigs": [],
  "MotionCards": [],
  "CamTables": [],
  "Calibrations": [],
  "TemplateAssets": [],
  "ModelAssets": [],
  "ImageAssets": [],
  "AssetDefaults": [],
  "AssetLifecycle": [],
  "AssetVersionLog": [],
  "Recipes": [],
  "VariableSnapshots": [],
  "VariableGroups": [],
  "WatchItems": [],
  "Scada": {
    "SchemaVersion": 1,
    "Pages": [],
    "Alarms": [],
    "VariableEvents": [],
    "StartupPageId": "00000000-0000-0000-0000-000000000000",
    "BasePages": []
  }
}
```

顶层字段一个都不能少；缺数组时加载器可能补，但生成时建议全部写全，避免版本差异。

---

## 2. ID 与引用规则（最容易错）

### 2.1 稳定 ID 生成

生成器统一用“种子字符串 → SHA1 前 16 字节 → Guid”：

```csharp
static Guid G(string seed)
{
    using var sha1 = SHA1.Create();
    var hash = sha1.ComputeHash(Encoding.UTF8.GetBytes(seed));
    return new Guid(hash.Take(16).ToArray());
}
```

约定：

| 对象 | 种子 |
| --- | --- |
| 步骤 | `G("步骤种子")` |
| 全局变量 | `G("var:" + 变量名)` |
| 页面 | `G("page:" + 页面名)` |
| 图层 | `G("layer:" + 页面名)` |
| 图元 | `G("el:" + 页面名 + ":" + 图元名)` |
| 运动轴 | `G("axis:" + 轴名)` |
| 流程 | `G("flow:" + 流程名).ToString("N")` |

**不要用随机 Guid**：SCADA 绑定、连线、系统变量都按 ID 寻址；两次生成不一致就会断链。

### 2.2 连线 `LinkedSources`

```json
"LinkedSources": {
  "Image": {
    "Kind": 1,
    "TargetStepId": "上游步骤的 StepID",
    "TargetPortName": "Image",
    "TargetVariableId": "00000000-0000-0000-0000-000000000000",
    "DisplayAddress": "采集图.Image"
  }
}
```

| Kind | 含义 | 必填 |
| --- | --- | --- |
| 1 | 步骤输出端口 | `TargetStepId` + `TargetPortName` |
| 2 | 全局变量 | `TargetVariableId = G("var:名字")`，`TargetPortName = 名字` |
| 3 | 运行期变量 | `TargetStepId = D5A2E1B0-1111-4F8C-9B3F-2A6E0B6A9F01`，`TargetPortName = 变量名` |
| 4 | 常量 | 由步骤自己的 `InputValues` 表达，通常不用 Kind 4 |

`DisplayAddress` 只用于界面显示，但建议写成 `上游步骤名.端口名`，否则画布连线显示会错。

**If / Wait / Case 的局部变量**：

```json
"LocalVariables": [
  { "Id": "局部变量ID", "Name": "Ok", "DataTypeName": "System.Boolean" }
],
"LinkedSources": {
  "局部变量ID": { "Kind": 1, "TargetStepId": "上游步骤ID", "TargetPortName": "Success", ... }
}
```

`LinkedSources` 的 **key 必须是 LocalVariables 里的 Id**，不是变量名。

---

## 3. 流程 `Flows`

```json
{
  "FlowID": "流程ID（N 格式，无连字符）",
  "FlowName": "Main",
  "Description": "主任务",
  "Version": 0,
  "IsEnabled": true,
  "Role": 2,
  "InvokeType": 96,
  "TimerIntervalMs": 0,
  "StepsEncrypted": false,
  "TriggerMode": 0,
  "Layout": { "Nodes": {}, "Count": 0 },
  "Steps": []
}
```

### 3.1 Role

| 值 | 含义 |
| --- | --- |
| 0 | None：普通/工位/子程序 |
| 1 | Home：回零 |
| 2 | Main：主任务；工具栏「运行主流程 / 循环主流程」只选它 |
| 3 | End：收尾 |

`Role` 不是 Flags；一个方案只应有一条启用的 Main。

### 3.2 InvokeType（位集）

| 位 | 值 | 含义 |
| --- | --- | --- |
| Manual | 0 | 仅手动 |
| Timer | 8 | 定时触发 |
| Variable | 16 | 变量上升沿 |
| Subroutine | 32 | 允许被「调用流程」拉起 |
| Http | 64 | 允许 `/flow/{名字}` |

- 工位流程：`Role=0`、`InvokeType=32`、`IsEnabled=true`；
- 需要 HTTP 的流程：再加 64，例如 `96`；
- **工位流程必须启用**，否则 `FlowInvoker` 会拒绝；但 `Role=0` 让它不参与工具栏批量。

---

## 4. 步骤

### 4.1 动作步骤 `ActionStep`

```json
{
  "$type": "VisionMaster.Models.ActionStep, VM.Core",
  "StepID": "步骤ID",
  "Icon": "?",
  "PluginName": "二维码读取",
  "Category": "图像处理",
  "PluginTypeName": "Plugin.CodeReader.CodeReaderPlugin, Plugin.CodeReader, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null",
  "StepName": "读载具码",
  "Description": "二维码读取",
  "SortId": 0,
  "IsDisEnable": false,
  "State": 0,
  "InputValues": {
    "Symbology": "QR Code",
    "TimeoutMs": 500,
    "Polarity": "any"
  },
  "LinkedSources": {},
  "OutputPortDefinitions": []
}
```

关键：

- `PluginTypeName` 必须是 `Modules\Plugin*.dll` 里类型的 **AssemblyQualifiedName**，不是显示名；
- `InputValues` 的 key 是插件端口/配置属性名；
- 枚举值通常写成员名（`Circle`、`Absolute`、`HoldingRegisters`、`Int16`），不要写中文显示名；
- `OutputPortDefinitions` 一般留空，由插件/编译器生成。

### 4.2 控制流节点

| 节点 | `$type` | `PluginTypeName` |
| --- | --- | --- |
| If | `VisionMaster.Models.ConditionStep, VM.Core` | `BuiltIn_If` |
| Case | `VisionMaster.Models.CaseStep, VM.Core` | `BuiltIn_Case` |
| For | `VisionMaster.Models.ForStep, VM.Core` | `BuiltIn_For` |
| While | `VisionMaster.Models.WhileStep, VM.Core` | `BuiltIn_While` |
| Parallel | `VisionMaster.Models.ParallelStep, VM.Core` | `BuiltIn_Parallel` |
| Wait | `VisionMaster.Models.WaitStep, VM.Core` | `BuiltIn_Wait` |
| Break | `VisionMaster.Models.BreakStep, VM.Core` | `BuiltIn_Break` |
| Continue | `VisionMaster.Models.ContinueStep, VM.Core` | `BuiltIn_Continue` |
| Return | `VisionMaster.Models.ReturnStep, VM.Core` | `BuiltIn_Return` |

容器节点的 `Children` 是分支数组：

```json
"Children": [
  { "Steps": [], "BranchType": 1, "StepName": "If", "Expression": "Ok" },
  { "Steps": [], "BranchType": 2, "StepName": "Else", "Expression": "" }
]
```

| BranchType | 含义 |
| --- | --- |
| 0 | 默认/并行/循环体 |
| 1 | If |
| 2 | Else |
| 3 | ElseIf |
| 4 | Case |
| 5 | WhileLoop |

- `ForStep` 额外有 `DefaultLoopCount`；
- `CaseStep` 有 `JudgeExpression` 和 `RuntimeVariableRefs`；
- `ParallelStep` 有 `ExecutionMode`、`FailFastMode`、`JoinTimeoutMs`；
- `WaitStep` 有 `ConditionExpression`、`PollIntervalMs`、`TimeoutMs`、`TimeoutAction`、`TimeoutMessage`、`AlarmVariableName`。

### 4.3 控制流约束

1. **If/Wait 的局部变量 key 用 ID**，不是名字；
2. **跨分支取数会编译失败**：`ResultDisplay`/`Compare` 不能引用另一分支里的步骤输出，改用全局变量；
3. **Parallel 分支只能放 `[ParallelSafe]` 插件**：图像采集、`RunFlow` 都不行；采集要放在 Parallel 之前；
4. **For/While 的循环体不要写无限循环**；连续运行由 Main 的外层循环负责；
5. **Return/Break/Continue 只在容器内有效**。

---

## 5. 变量 `VariableSnapshots`

### 5.1 本地/全局变量

```json
{
  "VarType": "Local",
  "Name": "OKCount",
  "VariableId": "G(\"var:OKCount\")",
  "DataTypeString": "System.Int32, System.Private.CoreLib, Version=9.0.0.0, Culture=neutral, PublicKeyToken=7cec85d7bea7798e",
  "Description": "累计良品数",
  "DefaultValue": 0,
  "Value": 0,
  "Offset": "0",
  "BitOffset": -1,
  "Length": 0,
  "AddressOffset": 0,
  "ElementByteLength": 0,
  "UseLegacyAddress": false,
  "DbNumber": 1,
  "ScanGroup": "",
  "GroupId": "00000000-0000-0000-0000-000000000000"
}
```

`VarType="Local"` 在方案里就是“全局变量池”（不是局部变量）。  
`VariableId` 必须用 `G("var:" + Name)`，否则 SCADA 绑定/变量赋值按名字兜底，按 ID 会断。

常用 `DataTypeString`：

| 类型 | 值 |
| --- | --- |
| Int32 | `System.Int32, System.Private.CoreLib, Version=9.0.0.0, Culture=neutral, PublicKeyToken=7cec85d7bea7798e` |
| Int16 | `System.Int16, ...` |
| Double | `System.Double, ...` |
| Single | `System.Single, ...` |
| Boolean | `System.Boolean, ...` |
| String | `System.String, ...` |

### 5.2 网络变量

在 Local 字段基础上增加：

```json
"VarType": "Network",
"ConnectionName": "PLC_Modbus",
"Protocol": "ModbusTcp",
"Area": "HoldingRegisters",
"Offset": "0",
"BitOffset": -1,
"Length": 1,
"DbNumber": 0,
"DataValueType": "Int16",
"MergeAdjacentRead": true
```

- `DataValueType` 必须和 `DataTypeString` 对齐（`Int16`/`Int32`/`Float`/`Boolean`）；
- Modbus `Offset` 是**原始协议地址**，不是 `4xxxx`：写 `"0"`，不要写 `"40001"`；
- `ByteoRDER`（拼写就是这五个字母）在通讯配置里：0=ABCD，3=CDAB；
- 只读区：`DiscreteInputs` / `InputRegisters` / S7 `I` 不能写。

### 5.3 方案级标定资产 `Calibrations`

商业软件的“标定一次、生产只引用”在 `.vms` 里对应顶层 `Calibrations` 集合，
而不是把标定步骤塞进每件产品的流程：

```json
"Calibrations": [
  {
    "CalibrationId": "稳定 GUID",
    "Name": "LineCalib",
    "Description": "产线像素当量标定",
    "IsLocked": false,
    "Version": 1,
    "Transform": {
      "Kind": 0,
      "MmPerPixel": 1.0,
      "Matrix": [1.0, 0, 0, 1.0, 0, 0],
      "ProjectiveMatrix": null,
      "MeshNodes": null,
      "MeshSize": 0,
      "Intrinsics": null,
      "SourceImageWidth": 640,
      "SourceImageHeight": 480,
      "SourceTag": "LineCalib",
      "CameraSerial": "NET-STATION-02",
      "CreatedAtUtc": "2026-10-25T00:00:00Z",
      "ResidualRmsPx": 0,
      "MaxResidualPx": 0,
      "ResidualByRadiusBands": [0, 0, 0],
      "ResidualBandCounts": [0, 0, 0],
      "HandEyeMatrix": null,
      "HandEyePose": 0,
      "HandEyeRotationRmsRad": 0,
      "HandEyeTranslationRmsMm": 0,
      "HandEyeSampleCount": 0
    }
  }
]
```

「标定」步骤的两种用法（同一个 `Plugin.Calibration` 类型）：

- **计算并写入资产**：`UseSavedCalibration=false`、`SaveToCatalog=true`、`CatalogAssetName` / `CatalogAssetId`；
  放在独立的 `Calib` 流程或换型流程里，不在每件节拍中执行。
- **引用资产**：`UseSavedCalibration=true`、`SavedCalibrationName` 或 `SavedCalibrationId`；
  不读图、不重算，输出资产里的 `Transform` / `MmPerPixel`。
- 资产失配（Id/名字不存在、相机序列号不符）会明确失败，绝不静默退回当前表格计算。

### 5.4 方案内容资产（模板 / 模型 / 图像）

这三类是**方案级内容资产**，和 `Calibrations` 一样随 `.vms` 落盘，但文件本体放在方案目录：

| 集合 | 文件落点 | 典型用途 |
|---|---|---|
| `TemplateAssets` | `Assets\Templates\` | Matching 模板库 JSON、几何模板 |
| `ModelAssets` | `Assets\Models\` | OCR / YOLO / DL 的 `.onnx` 模型 |
| `ImageAssets` | `Image\` | 标准图 / 参考图 / 采集图 |

最小模板资产：

```json
"TemplateAssets": [
  {
    "AssetId": "稳定 GUID",
    "Name": "Board01",
    "Description": "ST2 定位模板库",
    "Scope": "ST2 · 视觉定位",
    "Camera": "NET-STATION-02",
    "Version": 1,
    "Status": "有效",
    "IsFrozen": false,
    "RelativePath": "Assets\\Templates\\Board01.json",
    "ContentHash": "SHA256",
    "FileSize": 12345,
    "CreatedAtUtc": "2026-10-26T00:00:00Z",
    "UpdatedAtUtc": "2026-10-26T00:00:00Z",
    "CreatedBy": "AI",
    "UpdatedBy": "AI",
    "TemplateKind": "模板库",
    "PreviewRelativePath": ""
  }
]
```

最小模型资产：

```json
"ModelAssets": [
  {
    "AssetId": "稳定 GUID",
    "Name": "Yolo_ProductA",
    "Description": "产品 A 目标检测",
    "Scope": "ST6 · 识别追溯",
    "Camera": "NET-STATION-02",
    "Version": 1,
    "Status": "有效",
    "IsFrozen": false,
    "RelativePath": "Assets\\Models\\productA.onnx",
    "ContentHash": "SHA256",
    "FileSize": 12345678,
    "CreatedAtUtc": "2026-10-26T00:00:00Z",
    "UpdatedAtUtc": "2026-10-26T00:00:00Z",
    "CreatedBy": "AI",
    "UpdatedBy": "AI",
    "ModelKind": "ONNX",
    "InputWidth": 640,
    "InputHeight": 640,
    "Labels": "OK,NG"
  }
]
```

最小图像资产：

```json
"ImageAssets": [
  {
    "AssetId": "稳定 GUID",
    "Name": "Board01_Ref",
    "Description": "板卡定位参考图",
    "Scope": "ST2 · 视觉定位",
    "Camera": "NET-STATION-02",
    "Version": 1,
    "Status": "有效",
    "IsFrozen": false,
    "RelativePath": "Image\\board_01.png",
    "ContentHash": "SHA256",
    "FileSize": 619775,
    "CreatedAtUtc": "2026-10-26T00:00:00Z",
    "UpdatedAtUtc": "2026-10-26T00:00:00Z",
    "CreatedBy": "AI",
    "UpdatedBy": "AI",
    "Width": 1280,
    "Height": 960,
    "ImageKind": "标准图",
    "SourceCamera": "NET-STATION-02"
  }
]
```

生成规则：

1. `AssetId` 用稳定种子生成，**不要每次随机**；同一资产跨次生成必须同 Id。
2. `RelativePath` 只用方案内相对路径；模板/模型不能写 `D:\...`，图片必须落 `Image\`。
3. `ContentHash` 写文件 SHA-256（大写/小写保持一致即可）；`FileSize` 写字节数。
4. `Version` 从 1 开始；只有内容真正变化才递增，不能每次生成都 +1。
5. 文件本体必须真实存在；`TemplateAssets` / `ModelAssets` / `ImageAssets` 里有记录但文件缺失，资产页会标“缺失”，插件解析会明确失败。
6. 模板资产若是 Matching 模板库，文件内容必须是 `MatchingTemplateEntry[]` JSON；不是任意 JSON 都能当模板库。

**插件按 AssetId 引用（优先于路径）：**

| 插件 | `InputValues` 字段 | 资产类型 |
|---|---|---|
| `Plugin.Matching.MatchingPlugin` | `TemplateAssetId` | template |
| `Plugin.Ocr.OcrPlugin` | `ModelAssetId` | model |
| `Plugin.Yolo.YoloPlugin` | `ModelAssetId` | model |
| `Plugin.DlAnomaly.DlAnomalyPlugin` | `ModelAssetId` | model |
| `Plugin.DlClassify.DlClassifyPlugin` | `ModelAssetId` | model |
| `Plugin.DlSegment.DlSegmentPlugin` | `ModelAssetId` | model |
| `Plugin.ImageAcquisition.ImageAcquisitionPlugin` | `ImageAssetId` | image（仅单文件模式） |

示例：

```json
"InputValues": {
  "TemplateAssetId": "稳定 GUID",
  "ModelAssetId": "",
  "ModelPath": ""
}
```

- `AssetId` 非空时优先解析资产文件；为空才回退旧 `ModelPath` / `TemplateLibraryJson`。
- 插件配置窗口顶部有“资产”下拉（插件实现 `IAssetIdConfig` 时显示），可直接选当前方案的模板/模型/图像资产；选“（不使用资产）”会清空 Id。
- 图像资产 Id 只适用于 `ImageAcquisitionPlugin` 的“单文件”模式；文件夹/相机模式仍走原端口。
- `AssetId` 必须是合法 Guid；类型不符（例如把模板 Id 填到模型槽）会明确失败。
- 模板资产必须是模板库 JSON；模型资产必须是可读文件。
- 生成器可以只写 `AssetId`，不要再同时复制一份路径；两者都写时以 Id 为准。

### 5.5 资产生命周期字段（默认 / 版本 / 冻结）

资产页的「设为默认 / 冻结 / 回滚」都会写两个顶层集合，生成器要写就一起写全：

```json
"AssetDefaults": [
  { "Kind": "calibration", "Scope": "ST3_Measure", "AssetId": "稳定 GUID" }
],
"AssetLifecycle": [
  {
    "AssetId": "稳定 GUID",
    "Kind": "template",
    "State": "已归档",
    "ChangedAtUtc": "2026-10-26T00:00:00Z",
    "ChangedBy": "admin",
    "Note": "归档（文件保留）"
  }
],
"AssetVersionLog": [
  {
    "RecordId": "稳定 GUID",
    "AssetId": "稳定 GUID",
    "Kind": "calibration",
    "Name": "LineCalib",
    "Version": 1,
    "ContentHash": "",
    "SnapshotJson": "{\"Kind\":0,\"MmPerPixel\":0.05,...}",
    "Action": "设为默认",
    "Actor": "admin",
    "CreatedAtUtc": "2026-10-26T00:00:00Z",
    "Note": "作用域：ST3_Measure"
  }
]
```

规则：

1. `AssetDefaults` 按 `Kind + Scope` 唯一；同一作用域只能有一个默认。
2. `AssetLifecycle` 只有"已归档"才需要写；没有记录 = 有效。归档不改版本号、不删文件。
   **归档资产在运行期会被拒绝解析**（插件拿不到文件/内容），不是只影响界面。
3. `AssetVersionLog` 只存元数据与快照：标定快照是 `CalibrationTransform` JSON；文件资产快照是路径/哈希，**不含二进制**；配方快照是 `RecipeDefinition.CreateAssetSnapshot()` 的形状。
4. 冻结状态写在资产自身：`CalibrationAsset.IsLocked`、`TemplateAsset/ModelAsset/ImageAsset.IsFrozen`、`RecipeDefinition.IsFrozen`。
5. 冻结的资产拒绝内容写入；回滚也要求先解冻。

### 5.6 配方两种形态（参数配方 / 文本配方）

```json
"Recipes": [
  {
    "RecipeId": "稳定 GUID",
    "Name": "颜色配方",
    "Code": "COLOR-A",
    "Scope": "Solution",
    "ContentVersion": 1,
    "IsFrozen": false,
    "TextContent": "红白线 = 红,白\n红黑线 = 红,黑",
    "Entries": [],
    "Points": []
  }
]
```

- `TextContent` 非空 = **文本配方**（如颜色配方表，插件按原文解析）；否则是参数配方（`Entries`/`Points`）。
- 两者互斥，不要同时写内容。
- 文本配方由项目资产页「配方 → 导入资产」从 `.txt/.csv/.tbl` 生成：内容进方案、**不落文件**。
- 插件按 AssetId 取配方时：`ProjectAssetRef.ContentKind` 为 `text`（原文）或 `json`（参数快照）；
  `ColorCheck.RecipeAssetId` 只接受 `text`。
5. 生成器可以不写这两段（加载时会补空集合），但**不要写指向不存在 Id 的默认指针或版本记录**。

### 5.6 系统运动变量

SCADA 绑定 `System.Motion.X.Position` 等，ID 不是 `G("var:...")`，而是：

```csharp
var axisId = G("axis:" + axisName);
var id = new Guid(SHA1($"motion-var|{axisId:N}|{field}").Take(16).ToArray());
```

字段：`Position`、`Velocity`、`Enabled`、`InPosition`、`Alarm`、`StatusText`。  
轴名必须与 `MotionCards[].Axes[].LogicalName` 一致。

---

## 6. 硬件

### 6.1 通讯连接

```json
{
  "ConnectionName": "PLC_Modbus",
  "Protocol": 0,
  "Config": {
    "$type": "VisionMaster.Communications.ModbusTcpConfig, VM.Core",
    "ByteoRDER": 0,
    "IpAddress": "127.0.0.1",
    "Port": 503,
    "EnableKeepAlive": true,
    "KeepAliveIntervalMs": 30000,
    "Type": 0,
    "TimeoutMs": 3000,
    "RetryCount": 3,
    "RetryIntervalMs": 1000,
    "ReadTimeoutMs": 5000
  },
  "ReadCycleMs": 200,
  "ScanGroups": [],
  "AutoStart": true,
  "IsolateFaultedSegments": false,
  "AutoReconnect": true,
  "IsEnabled": true,
  "Description": "主 PLC"
}
```

S7：

```json
"Protocol": 2,
"Config": {
  "$type": "VisionMaster.Communications.SiemensS7Config, VM.Core",
  "S7CpuType": "S1200",
  "Rack": 0,
  "Slot": 0,
  "IpAddress": "127.0.0.1",
  "Port": 102,
  "Type": 2,
  ...
}
```

### 6.2 运动卡

```json
{
  "Id": "G(\"motion:card\")",
  "DriverTypeKey": "Plugin.Motion.ZMotion.ZMotionCard, Plugin.Motion.ZMotion, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null",
  "DisplayName": "ZMotion_Virtual",
  "Address": "127.0.0.1",
  "CardModel": "VPLC532R",
  "AutoConnect": true,
  "Remarks": "",
  "Axes": [
    {
      "AxisId": "G(\"axis:X\")",
      "LogicalName": "X",
      "PhysicalIndex": 0,
      "Enabled": true,
      "UnitsPerMm": 100.0,
      "SoftLimitMinMm": -500.0,
      "SoftLimitMaxMm": 500.0,
      "AxisRemark": "X 轴",
      "EncoderType": "",
      "EncoderPpu": 0.0,
      "MotorDriver": "",
      "MotorRatedCurrentA": 0.0,
      "FollowErrorUpperPulse": 0.0,
      "FollowErrorLowerPulse": 0.0
    }
  ],
  "Points": [],
  "Params": {
    "DefaultVelocityMmPerS": 10.0,
    "DefaultAccelMmPerS2": 100.0,
    "PollIntervalMs": 10,
    "CommandTimeoutMs": 30000,
    "WatchdogTimeoutMs": 3000,
    "AutoReconnect": true,
    "MaxQueueDepth": 256
  }
}
```

`DriverTypeKey` 必须是驱动程序集的 AQN；`CardModel` 会决定回零方式（VPLC532R 被标成总线型，Step Encoder 轴回零可能失败）。

---

## 7. 组态 `Scada`

### 7.1 文档

```json
"Scada": {
  "SchemaVersion": 1,
  "Pages": [],
  "Alarms": [],
  "VariableEvents": [],
  "StartupPageId": "G(\"page:总览\")",
  "BasePages": []
}
```

`StartupPageId` 必须是某个 `Pages[].PageId`。

### 7.2 页面

```json
{
  "PageId": "G(\"page:总览\")",
  "Name": "总览",
  "Description": "",
  "Width": 1920,
  "Height": 1080,
  "Background": "#FF0B1220",
  "ShowGrid": true,
  "GridSize": 10.0,
  "SnapToGrid": true,
  "BasePageIds": [],
  "IsPopup": false,
  "PopupModal": true,
  "PopupAnchor": 0,
  "PopupOffsetX": 0,
  "PopupOffsetY": 0,
  "Elements": [],
  "Layers": [
    {
      "LayerId": "G(\"layer:总览\")",
      "Name": "图层_1",
      "IsVisible": true,
      "IsLocked": false
    }
  ],
  "EventHooks": []
}
```

- 图元的 `LayerId` 必须等于页面 `Layers[].LayerId`；
- 弹窗页 `IsPopup=true`，用 `OpenPopup`/`ClosePopup` 动作；
- 页面尺寸 1920×1080 时，`X+Width` 不要超过 1920；小屏会裁切（页头时钟建议 `X=1400,W=300`）。

### 7.3 图元

```json
{
  "ElementId": "G(\"el:总览:KPI\")",
  "Name": "KPI",
  "LayerId": "G(\"layer:总览\")",
  "GroupId": "00000000-0000-0000-0000-000000000000",
  "ParentId": "00000000-0000-0000-0000-000000000000",
  "TemplateId": "00000000-0000-0000-0000-000000000000",
  "TemplateIndex": -1,
  "TemplateRevision": 0,
  "TypeKey": "Hmi.KpiBoard",
  "X": 60, "Y": 180, "Width": 900, "Height": 200,
  "Rotation": 0.0,
  "ZIndex": 10,
  "IsLocked": false,
  "Properties": {
    "Columns": "4",
    "Label1": "节拍", "Value1": "0", "Unit1": "次"
  },
  "Bindings": [
    {
      "TargetProperty": "Value1",
      "VariableId": "G(\"var:CycleCount\")",
      "VariableName": "CycleCount",
      "ArrayIndex": -1,
      "IsEnabled": true,
      "DisplayFormat": null
    }
  ],
  "EventHooks": [],
  "Animations": []
}
```

`Properties` 是 **string bag**：数字/布尔也写成字符串（`"4"`、`"True"`、`"False"`）。  
`TypeKey` 与属性 key 来自 `Scada.Controls\Descriptors\BuiltInElements.cs` 和 `ScadaPropertyCatalog`；不要凭猜写。

常用 `TypeKey`：

| TypeKey | 主要属性 |
| --- | --- |
| `Hmi.Text` | `Text`、`Foreground`、`FontSize`、`FontWeight`、`TextAlignment` |
| `Hmi.Rectangle` | `Fill`、`Stroke`、`StrokeThickness`、`CornerRadius` |
| `Hmi.Button` | `Text`、`Fill`、`Stroke`、`CornerRadius`、`Foreground`、`Danger`（`"True"` = 危险动作：红边 + 淡红字） |
| `Hmi.Indicator` | `IsOn`、`OnColor`、`OffColor`、`Shape`、`Text` |
| `Hmi.Lamp` | `State`、`OnColor`、`WarningColor`、`AlarmColor`、`Text` |
| `Hmi.IOField` | `Value`、`ValueFormat`、`Unit`、`Text`（`ValueOnlyText` 是只读派生，**不要写**） |
| `Hmi.TextField` | `Value`、`Placeholder`、`MaxLength`、`Mode`、`Text` |
| `Hmi.KpiBoard` | `Columns`、`Label1..4`、`Value1..4`、`Unit1..4` |
| `Hmi.Gauge` | `Value`、`Minimum`、`Maximum`、`ValueFormat`、`Unit` |
| `Hmi.ChartTrend` | `Value`、`TimeWindow`、`Minimum`、`Maximum`、`Title` |
| `Hmi.AxisConsole` | `AxisOptions`（JSON 字符串）、`ShowCommands`、`ShowPoints`、`PointsRows` |
| `Hmi.AlarmBanner` | `MaxRows`、`EmptyText`、`NormalColor`、`WarningColor`、`CriticalColor` |
| `Hmi.AckButton` | `Scope`、`Text`、`Fill` |
| `Hmi.Report` | `Mode`（`Detail`/`Summary`）、`From`、`To`、`Grouping`、`ShiftStarts`、`Keyword`、`ReportColumns`（JSON 字符串）、`AutoQuery`、`MaxRows`、`PageSize`、`AllowExport`、`AllowPrint`、`ShowToolbar`、`PageOrientation`、`Zebra` |
| `Hmi.Symbol` | `SymbolKind`（30 种，见 `ProcessSymbolCatalog`）、`State`、`AccentColor`、`StoppedColor`、`FaultColor`、`BlinkOnFault`、`DetailCount`、`TagPlacement`、`ShowValue`、`Value`、`ValueFormat`、`Unit`、`Interactive` |
| `Hmi.PipeRun` | `FromId` / `ToId`（**图元 Id，不是名字**）、`FromPort` / `ToPort`（`Top`/`Right`/`Bottom`/`Left`）、`RouteMode`（`Orthogonal`/`Straight`）、`Detour`、`ShowLabel`、`LabelAt`（0~1）、`LabelOffset`、`Text`（管线号标签） |
| `Hmi.BarChart` | `BarSeries`（JSON 字符串）、`Horizontal`、`MaxValue`、`TargetValue`、`ShowValues`、`SortMode`（`None`/`Desc`/`Asc`）、`ValueFormat` |
| `Hmi.PieChart` | `PieSlices`（JSON 字符串）、`Donut`、`ShowPercent`、`MergeBelow`（0~1）、`ValueFormat` |

`Hmi.AxisConsole` 的 `AxisOptions`：

```json
"[{\"AxisId\":\"G(axis:X)\",\"Name\":\"X\"},{\"AxisId\":\"G(axis:Y)\",\"Name\":\"Y\"}]"
```

注意它是 **JSON 字符串**，不是数组。

#### 7.3.1 三个"JSON 字符串"属性怎么写

`ReportColumns` / `BarSeries` / `PieSlices` 在 `Properties` 里都是**字符串**（要再转义一层），
内容是一个数组。**能用属性面板的「配置列… / 配置数据项…」就用面板生成**，下面是手写时的格式。

柱状图 `BarSeries`（`Label` 显示名、`Variable` 工程变量名、`Color` 可空）：

```json
"[{\"Label\":\"A 工位\",\"Variable\":\"Qty_A\",\"Color\":\"\"},{\"Label\":\"B 工位\",\"Variable\":\"Qty_B\",\"Color\":\"#38BDF8\"}]"
```

饼图 `PieSlices` 同形，只是键名换成 `PieSlices`。

报表 `ReportColumns`（字段：来源列名 → 显示名 → 格式 → 聚合 → 宽度）：

```json
"[{\"Source\":\"时间\",\"Title\":\"时间\",\"Format\":\"HH:mm:ss\",\"Aggregate\":0,\"Width\":140},{\"Source\":\"产量\",\"Title\":\"产量\",\"Format\":\"F0\",\"Aggregate\":1,\"Width\":0}]"
```

`Aggregate` 序号：`0` 不聚合 / `1` 求和 / `2` 平均 / `3` 最小 / `4` 最大 / `5` 计数 / `6` 末值。
`Width=0` 表示占满剩余宽度。**来源列必须与账本 CSV 表头一致**（报表按列名取值，不是变量名）。

管线两端引用的是**图元 Id**（`ElementId`），不是 `Name`：

```json
"FromId": "G(\"el:总览:罐1\")", "ToId": "G(\"el:总览:泵1\")", "FromPort": "Right", "ToPort": "Left"
```

生成顺序上，管线要放在两端图元**之后**，否则画布重排时找不到端点（会按空管线处理）。


### 7.4 绑定

- `TargetProperty` 必须是图元描述符里的属性 key；
- `VariableId` 用 `G("var:"+Name)`，系统运动变量用第 5.3 节算法；
- `VariableName` 用于显示/兜底；
- `ArrayIndex=-1` 表示标量；数组元素用 0..n-1。

### 7.5 事件与动作

```json
"EventHooks": [
  {
    "Event": 2,
    "RequireConfirmation": false,
    "ConfirmationMessage": null,
    "Actions": [
      {
        "Type": 6,
        "Text": null,
        "VariableId": "00000000-0000-0000-0000-000000000000",
        "VariableName": null,
        "ArrayIndex": -1,
        "Value": null,
        "TargetPageId": "00000000-0000-0000-0000-000000000000",
        "TargetPageName": null,
        "FlowId": "流程ID",
        "FlowName": "Main",
        "AxisId": "00000000-0000-0000-0000-000000000000",
        "AxisName": null,
        "TargetMm": null,
        "VelocityMmPerS": null,
        "OnlyIfPreviousSucceeded": false
      }
    ]
  }
]
```

`Event`：

| 值 | 事件 |
| --- | --- |
| 0 | Loaded |
| 1 | Unloaded |
| 2 | Pressed |
| 3 | Released |
| 4 | ValueChanged |
| 5 | ValueBecameTrue |
| 6 | ValueBecameFalse |
| 7 | ValueOverUpperLimit |
| 8 | ValueUnderLowerLimit |
| 9 | InputCompleted |

`Action.Type`：

| 值 | 动作 |
| --- | --- |
| 1 | Log |
| 2 | WriteVariable |
| 3 | Navigate |
| 4 | StartFlow |
| 5 | StopFlow |
| 6 | RunFlowOnce |
| 7 | MotionEnable |
| 8 | MotionDisable |
| 9 | MotionMoveAbsolute |
| 10 | MotionMoveRelative |
| 11 | MotionHome |
| 12 | MotionStop |
| 13 | MotionClearAlarm |
| 14 | MotionJogStart |
| 15 | MotionJogStop |
| 16 | PauseFlow |
| 17 | ResumeFlow |
| 18 | NavigateBack |
| 19 | OpenPopup |
| 20 | ClosePopup |

动作字段按类型填：

- WriteVariable：`VariableId/Name` + `Value`；
- Navigate/OpenPopup：`TargetPageId/Name`；
- StartFlow/StopFlow/RunFlowOnce：`FlowId/Name`；
- Motion*：`AxisId/Name` + `TargetMm/VelocityMmPerS`；
- Log：`Text`。

`FlowId` 是 `FlowID` 的 N 格式（无连字符），不是 Guid 带连字符。

### 7.6 报警

```json
{
  "AlarmId": "G(\"alarm:通讯断线\")",
  "Name": "通讯断线",
  "VariableId": "G(\"var:PLC_Trigger\")",
  "VariableName": "PLC_Trigger",
  "ArrayIndex": -1,
  "Kind": 6,
  "Threshold": 0.0,
  "Severity": 2,
  "Deadband": 0.0,
  "DelaySeconds": 0.0,
  "StaleSeconds": 5.0,
  "IsEnabled": true,
  "Message": "PLC 触发变量超过 5 秒未刷新",
  "AlarmGroup": "通讯",
  "Cause": "连接中断",
  "Remedy": "检查 PLC_Modbus",
  "ExtraInfo": null
}
```

`Kind`：0 High / 1 HighHigh / 2 Low / 3 LowLow / 4 BoolOn / 5 BoolOff / 6 Stale。  
`Severity`：0 Info / 1 Warning / 2 Critical。

### 7.7 变量事件

```json
{
  "VariableId": "G(\"var:CurrentResult\")",
  "VariableName": "CurrentResult",
  "UpperLimit": null,
  "LowerLimit": null,
  "IsEnabled": true,
  "EventHooks": [
    { "Event": 5, "RequireConfirmation": false, "ConfirmationMessage": null, "Actions": [] }
  ]
}
```

变量事件只支持事件 4~8；`UpperLimit/LowerLimit` 用于 7/8。

---

## 8. 生成代码模板

完整参考实现：`tools\vms-generator\`（可直接 `dotnet run`，输出到 `解决方案\*.vms`）。

最小骨架：

```csharp
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

static Guid G(string seed)
{
    using var sha1 = SHA1.Create();
    var hash = sha1.ComputeHash(Encoding.UTF8.GetBytes(seed));
    return new Guid(hash.Take(16).ToArray());
}

static JObject Action(string seed, string name, string pluginType, JObject inputs, JObject links = null)
    => new()
    {
        ["$type"] = "VisionMaster.Models.ActionStep, VM.Core",
        ["StepID"] = G(seed).ToString(),
        ["Icon"] = "?",
        ["PluginName"] = name,
        ["Category"] = "",
        ["PluginTypeName"] = pluginType,
        ["StepName"] = name,
        ["Description"] = name,
        ["SortId"] = 0,
        ["IsDisEnable"] = false,
        ["State"] = 0,
        ["InputValues"] = inputs ?? new JObject(),
        ["LinkedSources"] = links ?? new JObject(),
        ["OutputPortDefinitions"] = new JArray(),
    };

static JObject LinkStep(string seed, string port, string display)
    => new()
    {
        ["Kind"] = 1,
        ["TargetStepId"] = G(seed).ToString(),
        ["TargetPortName"] = port,
        ["TargetVariableId"] = Guid.Empty.ToString(),
        ["DisplayAddress"] = display,
    };

static JObject LinkGlobal(string name)
    => new()
    {
        ["Kind"] = 2,
        ["TargetStepId"] = Guid.Empty.ToString(),
        ["TargetPortName"] = name,
        ["TargetVariableId"] = G("var:" + name).ToString(),
        ["DisplayAddress"] = "全局变量 (Global)." + name,
    };

static void Write(string path, JObject solution)
{
    File.WriteAllText(path,
        JsonConvert.SerializeObject(solution, Formatting.Indented),
        new UTF8Encoding(false));
}
```

生成流程：

```csharp
var flow = new JObject
{
    ["FlowID"] = G("flow:Main").ToString("N"),
    ["FlowName"] = "Main",
    ["Description"] = "主任务",
    ["Version"] = 0,
    ["IsEnabled"] = true,
    ["Role"] = 2,
    ["InvokeType"] = 96,
    ["TimerIntervalMs"] = 0,
    ["StepsEncrypted"] = false,
    ["TriggerMode"] = 0,
    ["Layout"] = new JObject { ["Nodes"] = new JObject(), ["Count"] = 0 },
    ["Steps"] = new JArray
    {
        Action("main:read", "读PLC触发", P_CREAD, new JObject { ["VariableName"] = "PLC_Trigger" }),
        // ...
    }
};
```

---

## 9. 校验（必须做）

### 9.1 加载 + 编译

```csharp
var svc = new SolutionService();
var r = await svc.LoadAsync(path);
if (!r.Success) throw new Exception(r.Message);

var workspace = new WorkspaceContext();
workspace.SwitchSolution(r.Data);
VariablePersistenceService.Restore(r.Data, workspace); // 关键：把 VariableSnapshots 还原成 GlobalVariables

foreach (var flow in r.Data.Flows)
{
    workspace.SwitchFlow(flow);
    var cr = new FlowCompiler(workspace).Compile(flow.Steps, flow.FlowName);
    if (!cr.Success)
        foreach (var e in cr.Errors) Console.WriteLine($"[{flow.FlowName}] {e}");
}
```

> 只 `SwitchSolution` 不会把 `VariableSnapshots` 灌进 `GlobalVariables`；验证器里必须 `VariablePersistenceService.Restore`，否则会误报“找不到全局变量”。

### 9.2 组态渲染

```powershell
dotnet run --project tools\probes\_ScadaUiProbe\_ScadaUiProbe.csproj -- --solution "D:\C#\VM\解决方案\自动化产线.vms"
```

输出 `RESULT: OK` 且每页 PNG 生成，说明 XAML/资源/图元属性可解析。

### 9.3 真机打开

1. 启动 `VisionMaster.exe`；
2. 文件 → 打开 `.vms`；
3. 确认流程编译无红字；
4. 打开组态运行页，确认启动页正确；
5. 有设备时写触发变量、点运行。

---

## 10. 常见坑（生成前逐条对照）

| 坑 | 现象 | 正确做法 |
| --- | --- | --- |
| 步骤 ID 随机 | 连线断、SCADA 取不到值 | 全部用 `G(seed)` |
| `LinkedSources` key 写变量名 | If/Wait 条件恒假/编译报错 | key 用 `LocalVariables[].Id` |
| 跨分支引用步骤输出 | `[跨分支取数]` 编译失败 | 改用全局变量 |
| `PluginTypeName` 写显示名 | 加载后步骤无法构造 | 用 AQN |
| 枚举写中文 | 插件解析失败/回落默认 | 写成员名 |
| 工位流程 `IsEnabled=false` | Main 调用失败“已禁用” | 启用 + `InvokeType=32` + `Role=0` |
| 工位流程也设 `Role=Main` | 工具栏批量启动多条 | 只有一条 Main；工位 None |
| 图像采集放进 Parallel | 编译报 `[ParallelSafe]` | 采集放 Parallel 之前 |
| `RunFlow` 放进 Parallel | 编译报 `[ParallelSafe]` | 工位级串行 |
| SCADA 绑定 ID 随机 | 运行时值为空 | 用 `G("var:"+名字)` |
| 系统运动变量用 `G("var:...")` | 绑定不上 | 用 `motion-var|axisId|field` 算法 |
| 页面 `StartupPageId` 为空 | 运行无启动页 | 指向真实 PageId |
| 图元 LayerId 与页面图层不一致 | 图元不显示 | 用同一个 `G("layer:"+页面名)` |
| 属性值写数字/布尔 | 控件解析失败 | `Properties` 全部字符串 |
| Modbus 地址写 40001 | 读写错位 | 写原始 `"0"` |
| `ByteoRDER` 拼成 `ByteOrder` | 字序配置不生效 | 保持 `ByteoRDER` |
| `FlowId` 带连字符 | SCADA 启动流程找不到 | 用 `FlowID` 的 N 格式 |
| 页面 X+Width > 1920 | 小屏裁切 | 控制右边界，时钟放 1400+300 |
| 用 `Get-Content/Set-Content` 改 `.vms` | 中文/编码损坏 | 用 `[IO.File]::ReadAllText/WriteAllText(UTF8 no BOM)` |
| 只构建插件不重建 | `Modules\` 仍旧 DLL | 改插件源码必须重建该插件工程 |
| 把只读派生属性写进 `Properties` | 加载后被立刻覆盖（写不进） | `ValueOnlyText` / `DisplayBrush` / `DisplayStroke` / `IsEditing` 这类是运行时算的，**不要写** |
| `ReportColumns` / `BarSeries` / `PieSlices` 写成 JSON 数组 | 反序列化失败，面板显示"未配置" | 它们在 `Properties` 里是**字符串**（再转义一层），见 §7.3.1 |
| 管线 `FromId`/`ToId` 写成图元名字 | 运行时找不到端点，管线不显示 | 必须写 `ElementId`；且管线要排在两端图元之后 |
| 报表 `Source` 写成工程变量名 | 报表查不到列 | `Source` 是**账本 CSV 列名**（不是变量名）；用「从账本导入列」生成最稳 |
| 柱/饼的数据项写成 `label=var` 文本 | 也能读（兼容），但面板不认 | 规范形态是 §7.3.1 的 JSON；手写文本只作为兜底 |

---

## 11. 生成顺序建议

1. 定 `SolutionId`、流程名/角色、变量名；
2. 生成变量快照（先变量，后步骤，方便连线）；
3. 生成通讯/运动卡（硬件 ID 先定）；
4. 生成每个流程的步骤（Action → 控制流 → 连线）；
5. 生成 SCADA 页面/图元/绑定/事件/报警；
6. 写文件（UTF8 无 BOM）；
7. `SolutionService` 加载 + 逐流程 `FlowCompiler`；
8. `_ScadaUiProbe` 渲染；
9. 真机打开/HTTP 触发；
10. 记录到 `docs/code-changes/`。

---

## 12. 参考文件

| 内容 | 路径 |
| --- | --- |
| 生成器源码 | `tools\vms-generator\` |
| 生产版示例 | `解决方案\综合检测线.vms` |
| 边界测试示例 | `解决方案\综合测试台.vms` |
| 9 工位示例 | `解决方案\自动化产线.vms` |
| 组态渲染探针 | `tools\probes\_ScadaUiProbe\` |
| 验证器示例 | 本机临时 `ValidateSolution`（核心逻辑见 §9.1） |

> 生成器里的绝对路径（`D:\C#\VM\...`）和插件 AQN 是按本机 `Modules\` 写的；换机器时先反射重新 dump 插件类型和配置键，不要照抄 AQN。
