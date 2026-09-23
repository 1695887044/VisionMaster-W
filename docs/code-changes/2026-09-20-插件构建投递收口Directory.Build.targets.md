# 插件构建投递收口：Plugins\Directory.Build.targets

- 日期：2026-09-20
- 范围：`Plugins\` 下 8 个插件工程的"产物投递"路径统一收口；消除裸用 `$(SolutionDir)` 导致的单工程构建失败
- 前置：[插件隐性约定框架吸收](2026-09-17-插件隐性约定框架吸收.md)、[CSharpScript 编辑器智能提示与高亮](2026-09-19-CSharpScript编辑器智能提示与高亮.md)

---

## 一、需求与决策

### 1. 需求

各插件 csproj 的 PostBuild 里裸写：

```xml
<Exec Command="copy &quot;$(TargetDir)Plugin.X.dll&quot; &quot;$(SolutionDir)Modules\&quot; /Y" />
```

`$(SolutionDir)` 只有在**构建入口是 .sln** 时才有值。一旦入口是单个 csproj：

```
dotnet build Plugins\Plugin.Utility\Plugin.Util.csproj
```

`$(SolutionDir)` 就是**字面量 `*Undefined*`**，而 `*` 是非法路径字符 → 拷贝命令硬失败 → `MSB3073`。

历史上没暴露，是因为这 9 个插件恰好都登记在 `VisionMaster.sln` 里，日常都是解决方案粒度构建。

### 2. 危害面（为什么必须治）

| # | 后果 | 说明 |
|---|---|---|
| 1 | **假失败** | 编译 0 error，却报 `MSB3073`，报错点落在 PostBuild 而非代码，排查方向被误导 |
| 2 | **静默旧部署（最危险）** | 有人为了"让构建变绿"加 `ContinueOnError="true"`，构建从此永远成功，但 `Modules\` 里的插件 DLL 是**上一版**——改了代码、跑了流程、发现没生效，怀疑人生 |
| 3 | **单工程工作流全断** | `dotnet build <csproj>`、`dotnet run` 指定工程、VS Code C# Dev Kit 直接打开工程、Rider 工程粒度构建、CI 做单插件流水线——全部不可用 |
| 4 | **CI 红而实无问题** | CI 若按工程粒度切分任务，红的是环境而非代码 |
| 5 | **模板扩散** | 8/9 个插件都是裸写法，新建插件抄模板会把问题复制下去 |

### 3. 决策清单

| # | 决策点 | 选择 | 依据 |
|---|---|---|---|
| 1 | 回退方式 | **统一收口到 `Plugins\Directory.Build.targets`**，而非每个 csproj 各写一段回退 | 8 个工程各写一遍 = 8 个漏改机会；新插件默认继承，不必记得抄。仓库内 `Plugins\` 下**只有这 9 个插件工程**，放这里天然不波及第三方 `WPF-Halcon-流程拖拉\` |
| 2 | 文件形态 | **`.targets` 而非 `.props`** | 见下节——`$(Configuration)` 的导入时序陷阱 |
| 3 | 投递动作归属 | 插件自身 DLL 的投递（`CopyPluginToModules`）由 targets 统一提供；**插件私有依赖**（Roslyn / MiniExcel / hdevenginedotnet / ScriptAssets）仍留在各自 csproj | 私有依赖是"这个插件特有的东西"，不可能统一枚举；统一的是"所有插件共同的那一步" |
| 4 | 是否加 `ContinueOnError` | **不加** | 加了就是危害 #2 的静默旧部署。宁可红，不可静默 |

### 4. 关键决策 2 的推演：为什么必须是 `.targets`

第一版写成了 `Plugins\Directory.Build.props`，结果 `HostBinDir` 展开成了：

```
...\VisionMaster\bin\\net9.0-windows\ScriptAssets\      ← 注意 bin\\ 双反斜杠
```

`$(Configuration)` 是**空串**。原因：

- `Directory.Build.props` 由 `Microsoft.Common.props` 导入，而 `<Configuration Condition="'$(Configuration)'==''">Debug</Configuration>` 这个**默认值赋值在同一个文件的稍后位置**。
- 所以 `.props` 里读 `$(Configuration)` = 空。
- `Directory.Build.targets` 导入时机**足够晚**，此时 `Configuration` 已就位。

原 csproj 把 `$(ConfigurationName)` 直接写在 `<Exec Command="...">` 里之所以正确，是因为那是**目标执行时**展开——与 `.targets` 同一时机；挪进属性就变成**导入时**展开，于是踩空。

**教训**：MSBuild 属性在 `.props` / `.targets` / `Target` 里的展开时机不同。凡是依赖"构建期间才确定的属性"（`Configuration`、`TargetPath`、`TargetDir`）的**计算属性**，一律放 `.targets`。

---

## 二、修改文件清单

### 新增

- `Plugins\Directory.Build.targets`

```xml
<PropertyGroup>
  <ModulesDir Condition="'$(SolutionDir)' == '' or '$(SolutionDir)' == '*Undefined*'">$(MSBuildThisFileDirectory)..\Modules\</ModulesDir>
  <ModulesDir Condition="'$(ModulesDir)' == ''">$(SolutionDir)Modules\</ModulesDir>

  <HostBinDir Condition="'$(SolutionDir)' == '' or '$(SolutionDir)' == '*Undefined*'">$(MSBuildThisFileDirectory)..\VisionMaster\bin\$(Configuration)\net9.0-windows\</HostBinDir>
  <HostBinDir Condition="'$(HostBinDir)' == ''">$(SolutionDir)VisionMaster\bin\$(Configuration)\net9.0-windows\</HostBinDir>
</PropertyGroup>

<Target Name="CopyPluginToModules" AfterTargets="Build">
  <Copy SourceFiles="$(TargetPath)" DestinationFolder="$(ModulesDir)" SkipUnchangedFiles="true" />
</Target>
```

两个属性都采用**双条件赋值**：先判"无解决方案上下文"给回退值，再判"还没赋值"给正常值。这样即使未来有人从外部传入 `ModulesDir`，也不会被覆盖。

### 改造（8 个插件 csproj）

**A. 纯拷贝型——PostBuild 目标整体删除**（自身 DLL 投递已由 targets 提供，原本只做这一件事）：

- `Plugin.Utility\Plugin.Util.csproj`（23 行 → 15 行）
- `Plugin.CreateRoi\Plugin.CreateRoi.csproj`
- `Plugin.ResultUpload\Plugin.ResultUpload.csproj`
- `Plugin.ImageAcquisition\Plugin.ImageAcquisition.csproj`
- `Plugin.DataRecord\Plugin.DataRecord.csproj`
- `Plugin.PreProcessing\Plugin.PreProcessing.csproj`（同时删掉它**自己那份** `ModulesDir` 属性组与 `CopyPluginToModules` 目标——与 targets 重复）

**B. 保留私有依赖投递型**——只把 `$(SolutionDir)Modules\` 换成 `$(ModulesDir)`：

| 文件 | 投递内容 |
|---|---|
| `Plugin.CSharpScript\Plugin.CSharpScript.csproj` | `Microsoft.CodeAnalysis*.dll` → `$(ModulesDir)` |
| `Plugin.ExcelExport\Plugin.ExcelExport.csproj` | `MiniExcel.dll` → `$(ModulesDir)` |
| `Plugin.ImageScript\Plugin.ImageScript.csproj` | `hdevenginedotnet.dll` → `$(ModulesDir)`；`ScriptAssets\` → `$(HostBinDir)` |

### 删除

- `Plugins\Directory.Build.props`（初版，因时序缺陷废弃）
- `VisionMaster\bin\net9.0-windows\ScriptAssets\**`（`.props` 时序缺陷误建的垃圾目录）

---

## 三、验证结果

| 验证项 | 命令 | 结果 |
|---|---|---|
| 单工程构建（纯拷贝型） | `dotnet build Plugins\Plugin.Utility\Plugin.Util.csproj` | ✅ 0 错误 0 警告；`Modules\Plugin.Util.dll` 刷新 → **`*Undefined*` 回退生效** |
| 单工程构建（含资源投递） | `dotnet build Plugins\Plugin.ImageScript\Plugin.ImageScript.csproj -t:Rebuild -v:n` | ✅ 0 错误；回显 `xcopy "...\ScriptAssets" "...\Plugins\..\VisionMaster\bin\Debug\net9.0-windows\ScriptAssets\"` ——**`$(Configuration)` 已就位，不再出现 `bin\\`** |
| 资源还原 | `Glob VisionMaster/bin/**/publish_preview.hdvp` | ✅ `bin\Debug\net9.0-windows\ScriptAssets\procedures\publish_preview.hdvp` 已投递 |
| 单工程构建（Roslyn / MiniExcel） | 三个 csproj 分别 build | ✅ 全 0 错误，私有依赖正确落到 `Modules\` |
| 解决方案回归 | `dotnet build VisionMaster.sln -c Debug` | ✅ **已成功生成**，0 错误 / 377 警告（均为既有 nullable 警告） |
| 投递内容正确性 | 4 组 `Get-FileHash` MD5 比对（插件 bin → `Modules\`） | ✅ 全部 MATCH |
| 裸 `$(SolutionDir)` 残留 | `Grep SolutionDir Plugins\` | ✅ 仅 `Directory.Build.targets` 内部有，无 csproj 残留 |
| 第三方工程不受影响 | `Grep WPF-Halcon VisionMaster.sln` | ✅ 无命中（不在解决方案内，也不在 `Plugins\` 下，天然不被 `Directory.Build.targets` 波及） |
| 垃圾目录清理 | `Test-Path VisionMaster\bin\net9.0-windows` | ✅ False（已删除） |

---

## 四、已知边界

1. **增量构建下 PostBuild 钩子不保证执行**
   `AfterTargets="PostBuildEvent"` 挂在 `PostBuildEvent` 目标之后，而该目标默认带 `RunPostBuildEvent=OnOutputUpdated` 条件——**输出未更新时整个钩子不跑**。
   实测：删掉 `ScriptAssets\procedures\publish_preview.hdvp` 后跑增量 `dotnet build`，文件**不会被还原**；只有 `-t:Rebuild` 才重新投递。
   → **验证投递必须用 `-t:Rebuild -v:n` 并检查回显命令**，不能只看"build 成功"。这是既有行为，非本次引入。

2. **`copy` 保留源文件时间戳**
   `copy /Y` 不改目标文件的 `LastWriteTime`，所以 `Modules\` 里私有依赖的时间戳看起来"很旧"，不代表没拷贝。判断是否最新应比对 **Hash**，不要看时间戳。

3. **`ModulesDir` 属性名是跨工程共享的**
   它由 `Directory.Build.targets` 定义，`Plugins\` 下所有工程可见。若某插件将来需要投递到别处，应新增自己的属性名，不要覆写 `ModulesDir`（会连带影响同一构建里其它插件的投递目标——虽然属性作用域是工程级，但同名易误读）。

4. **`HostBinDir` 硬编码了 `net9.0-windows`**
   与宿主 `VisionMaster.csproj` 的 TFM 绑定。宿主升 TFM（如 net10.0-windows）时必须同步改 `Directory.Build.targets`，否则资源投递落空。目前无处校验，属于隐性契约。

5. **`.targets` 只覆盖 `Plugins\` 子树**
   若将来把插件工程挪到 `Plugins\` 之外，收口失效，会退回裸 `$(SolutionDir)` 的旧问题。

---

## 五、给后续开发者的两条纪律

1. **新建插件**：不要写 `$(SolutionDir)`，直接用 `$(ModulesDir)`；自身 DLL 的投递**什么都不用写**，`CopyPluginToModules` 已自动生效。
2. **验证构建链路**：单工程改动后用 `dotnet build <csproj>` 而非只跑 sln，这是本次收口要保住的**核心场景**。
