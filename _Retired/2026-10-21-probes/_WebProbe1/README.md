# M0 探针：可选宿主模块（`Web\` 目录 + `IAppModule`）的加载语义

> 隔离实验，全部产物在 `_WebProbe1\` 下。**未修改任何生产代码 / 工程 / 文档**。
> 唯一副作用：构建时通过 `ProjectReference` 顺带编译了 `VM.Core` / `VM.Scada` / `Core.*` 到它们各自的 `bin\Debug\`（二进制，无源码改动）。

## 1. 环境

| 项 | 值 |
| --- | --- |
| OS | Windows 10.0.26200 x64 |
| .NET SDK | 9.0.318 |
| 运行时 | Microsoft.NETCore.App 9.0.20 / Microsoft.WindowsDesktop.App 9.0.20 |
| 目标框架 | `net9.0-windows` + `UseWPF=true`（与 `Core\VM.Core.csproj` 一致） |
| 契约 | `VisionMaster.Lifetime.IAppModule`（`Core\Modules\IAppModule.cs`，宿主程序集 = `VM.Core.dll`） |
| 时间 | 2026-10-21 00:57 ~ 01:05 |

## 2. 工程结构

```
_WebProbe1\
  Host\Host.csproj            # 模拟 VisionMaster.exe：net9.0-windows + UseWPF，引用 VM.Core，控制台程序
  Host\Program.cs             # 读 AppConfig.json -> 决定是否 Assembly.LoadFrom -> 打印 6 条判据
  Module\Module.csproj        # 模拟 Web\Probe.ScadaWeb.dll，AssemblyName=Probe.ScadaWeb，输出到 ..\out\Web\
  Module\ScadaWebModule.cs    # 实现 IAppModule（只打印，不碰真实容器）
  ExtraDep\ExtraDep.csproj    # 附加观察 G 用的"模块私有依赖"（宿主完全不知道）
  HostAspNet\HostAspNet.csproj# 附加观察 H 的对照宿主（同一份 Program.cs + AspNetCore FrameworkReference）
  out\                        # 部署根（等价 <publish>）
    Probe.Host.exe            # 宿主
    AppConfig.json            # ScadaWeb.Enabled=false（默认态）
    AppConfig.on.json         # ScadaWeb.Enabled=true（探针用）
    Web\Probe.ScadaWeb.dll    # 模块目录：只有模块自己的文件，没有 VM.*.dll / Core.*.dll
```

宿主与模块的关系：`Host -> VM.Core`，`Module -> VM.Core`，**Module 不引用 VisionMaster.exe**（实验证明不需要）。

## 3. 命令

```bat
:: 构建（不要用 --no-build，见 R65）
dotnet build _WebProbe1\Host\Host.csproj
dotnet build _WebProbe1\ExtraDep\ExtraDep.csproj
dotnet build _WebProbe1\Module\Module.csproj
dotnet build _WebProbe1\HostAspNet\HostAspNet.csproj

:: A / F：Enabled=false，DLL 存在
cd _WebProbe1\out && Probe.Host.exe

:: B / C / E：Enabled=true，DLL 存在
set "PROBE_CONFIG=D:\C#\VM\_WebProbe1\out\AppConfig.on.json"
Probe.Host.exe

:: D：Enabled=true，DLL 缺失
ren Web\Probe.ScadaWeb.dll Probe.ScadaWeb.dll.bak
Probe.Host.exe
ren Web\Probe.ScadaWeb.dll.bak Probe.ScadaWeb.dll

:: 附加观察 G / H
set "PROBE_TOUCH_EXTRA=1"
set "PROBE_TOUCH_ASPNET=1"
Probe.Host.exe
```

环境变量（只影响探针，不改配置结构）：`PROBE_CONFIG`（默认 `<baseDir>\AppConfig.json`）、`PROBE_WEB_DIR`（默认 `<baseDir>\Web`）、`PROBE_TOUCH_EXTRA`、`PROBE_TOUCH_ASPNET`。

### 关于"是不是跑的旧二进制"（R65）

- `Host\Program.cs` 最后修改 00:57:09，`out\Probe.Host.exe` 生成于 00:57:15 —— 之后 `Program.cs` 未再改动。
- 模块在加完 G/H 观察点后**显式重建**（`dotnet build Module.csproj --no-dependencies`，0 错误），随后重跑了 B/C/E 与 A/F。
- 中途改用 `--no-dependencies` + 直接运行 exe 的原因见 §7（仓库被另一个进程并发修改）。

## 4. 六条判据结果

| # | 判据 | 结果 | 关键证据 |
| --- | --- | --- | --- |
| A | `Enabled=false` → 不在 AppDomain、不调 `LoadFrom` | **PASS** | `AppDomain VM.*/Probe.* : Probe.Host, VM.Core`（无 `Probe.ScadaWeb`）；`LoadFrom called=False` |
| B | `Enabled=true` + DLL 存在 → 加载成功、`is IAppModule`、`Name` 可打印 | **PASS** | `module type: Probe.ScadaWeb.ScadaWebModule`；`module.Name: Probe.ScadaWeb (vm.scada=VM.Scada 1.0.0.0)` |
| C | 类型身份唯一（不出现两份 `VM.Core`） | **PASS** | 模块侧 `IAppModule -> VM.Core @ D:\C#\VM\_WebProbe1\out\VM.Core.dll`；宿主侧同路径；`same Assembly object=True` |
| D | DLL 缺失 → 不抛异常、打 Warn、继续运行 | **PASS** | `[WARN] ScadaWeb module dll is missing, skip loading; host continues`；`EXITCODE=0`；`Host reached end of Main` |
| E | 模块目录不含 `VM.Core.dll`/`VM.Scada.dll` 仍能加载 | **PASS** | `VM.*.dll inside WebDir: <none>`；`VM.Core from baseDir=True, VM.Scada from baseDir=True`；模块构造函数里 `typeof(ScadaPage)` 成功（`vm.scada=VM.Scada 1.0.0.0`） |
| F | `Enabled=false` 时 DLL 存在也不加载 | **PASS** | `ModuleDll: ... (exists)` + `Probe.ScadaWeb loaded AFTER: False` |

汇总输出：

```
:: Enabled=false
SUMMARY: A=PASS B=SKIP C=SKIP D=SKIP E=SKIP F=PASS
:: Enabled=true
SUMMARY: A=SKIP B=PASS C=PASS D=SKIP E=PASS F=SKIP
:: DLL 缺失
SUMMARY: A=SKIP B=SKIP C=SKIP D=PASS E=SKIP F=SKIP
```

补充证据（`Enabled=false` 时链路干净）：`VM.Scada loaded: False` —— 不仅模块没加载，`VM.Core` 的传递依赖也没被提前拉起来。

## 5. 附加观察（不计入 6 条，但对 M0 决策有影响）

### G. 模块私有依赖放 `Web\` 能被解析（**意外的好消息**）

`Probe.ExtraDep.dll` **只**存在于 `out\Web\`（宿主既没引用、`out\` 根目录也没有这个文件），模块构造函数里访问 `Probe.ExtraDep.Marker.Value`：

```
[MODULE] extra dep resolved: Probe.ExtraDep@1.0.0 @ D:\C#\VM\_WebProbe1\out\Web\Probe.ExtraDep.dll
```

结论：.NET 9 的 `Assembly.LoadFrom` 会为**被加载程序集自己**探测其所在目录，所以模块的私有 DLL 可以随模块放在 `Web\`，**不需要** `AssemblyDependencyResolver` / 自定义 ALC。

⚠ 解析顺序仍是"应用基目录 / deps.json 优先，模块目录兜底"（本次未单独做版本冲突实验，属已知机制推断）：**不要把宿主已有的程序集（`VM.Core.dll` / `VM.Scada.dll` / `Newtonsoft.Json.dll` …）复制进 `Web\`**，否则那份是死文件，永远用不到；想靠它"带自己的版本"也做不到。

### H. ASP.NET Core 共享框架**不能**这样解析（真会踩的坑）

模块加了 `<FrameworkReference Include="Microsoft.AspNetCore.App" />`，宿主**没有**。模块里访问 `Microsoft.AspNetCore.Builder.WebApplication`：

```
[MODULE] aspnet FAILED: System.IO.FileNotFoundException: Could not load file or assembly
'Microsoft.AspNetCore, Version=9.0.0.0, Culture=neutral, PublicKeyToken=adb9793829ddae60'.
系统找不到指定的文件。
```

原因是共享框架程序集不在 `Web\` 里（框架引用不落盘），而 `LoadFrom` 的目录探测救不了它——必须在**宿主的 runtimeconfig / deps.json** 里有这个框架。

对照宿主（`HostAspNet`，唯一差别是 `<FrameworkReference Include="Microsoft.AspNetCore.App" />`）：

```
[MODULE] aspnet resolved: Microsoft.AspNetCore@9.0.0.0
SUMMARY: A=SKIP B=PASS C=PASS D=SKIP E=PASS F=SKIP
```

## 6. 对 M0 的结论

1. **"配置关闭 = 程序集不加载"可以落地**，语义干净：`Enabled=false` 时 `AppDomain` 里既没有 `Probe.ScadaWeb`，也没有 `VM.Scada`（`VM.Core` 是宿主自己引用的，本来就在）。`Enabled=true` 时模块与宿主共用同一份 `VM.Core`（`ReferenceEquals` 为真），不存在"两份静态状态"的隐患。DLL 缺失/加载异常都必须降级成 Warn 并继续启动（与 R48/R57 的"只报不拦"口径一致）。
2. **模块依赖放哪**：模块自己的 DLL（含第三方私有依赖）放 `Web\` 即可；**宿主已有的程序集不要放进去**（`VM.Core` / `VM.Scada` / `Core.*` / `Prism` / `Newtonsoft` 等，宿主输出目录里的那份会被优先解析）。
3. **不需要 `AssemblyDependencyResolver`** —— 除非将来要做"模块携带与宿主不同版本、彼此隔离"。真要隔离才需要 `AssemblyDependencyResolver` + 自定义 `AssemblyLoadContext`（模块目录里已有的 `Probe.ScadaWeb.deps.json` 就是它的输入）。
4. **但宿主必须补一条**：Web 模块要用 ASP.NET Core（Kestrel/WebApplication）时，`VisionMaster.csproj` 必须加 `<FrameworkReference Include="Microsoft.AspNetCore.App" />`，否则模块里任何 AspNetCore 类型都是 `FileNotFoundException`（§5-H 实测）。这是**宿主侧**改动，模块侧无解。

## 7. 执行期间的环境事故（可复现性说明）

- 00:58:05 / 00:58:17，**另一个进程**正在编辑 `D:\C#\VM\Scada\Models\ScadaEventHook.cs`（`_requireConfirmation` / `_confirmationMessage` 字段被删、引用还在），导致 `dotnet build` 在 `VM.Scada` 上报 `CS0103` 两条 —— **不是本探针引入的**（本探针从未写 `Scada\`）。
- 规避方式：对探针工程用 `--no-dependencies` 构建（复用已成功构建的 `VM.Scada.dll`），直接运行已是最新的 `Probe.Host.exe`。
- 01:05 复查：该文件 mtime 停在 00:58:17，`dotnet build Scada\VM.Scada.csproj` 恢复为 **0 警告 / 0 错误**，仓库当前可正常构建。
- 教训对齐 AGENTS.md R9：改动前先看 mtime；发现并发修改时不要动它、改用隔离路径继续。

## 8. 实验脚手架说明（不是生产建议）

- `Module.csproj` 的 `Private="false"` 只阻止**直接**引用 `VM.Core.dll` 被复制；`VM.Scada` / `Core.*` 作为传递项目引用仍会被拷到 `Web\`。因此加了一个 `AfterTargets="Build"` 的 `StripHostOwnedAssembliesFromWebDir` 目标把它们删掉，用来构造"`Web\` 只有模块自己文件"的真实部署形态。
- `HostAspNet` 用 `<Compile Include="..\Host\Program.cs" Link="Program.cs" />` 复用同一份判据代码，保证 H 的对照只差一个 `FrameworkReference`。
