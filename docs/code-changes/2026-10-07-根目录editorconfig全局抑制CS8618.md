# 根目录 .editorconfig 全局抑制 CS8618

- 日期：2026-10-07
- 改动：新增根目录 `.editorconfig`（单文件，未动任何 csproj / 源码）

## 起因

错误列表被可空引用警告刷屏。命令行全解决方案重建 + VisionMaster 单独构建、按消息去重（WPF 的 `*_wpftmp.csproj` 会把源码再编一遍，同一条警告在命令行输出里出现两次）后实测：

- 全仓库 **590 条**不同警告，其中 **94%（553 条）是 CS86xx 可空引用族**；
- 最大单头是 **CS8618**（"不可为 null 的字段在退出构造函数时必须包含非 null 值"）**164 条**；
- 非可空类的零散项仅 37 条（CS0168×11、CS1998×5、CS0414/CS0169/CS0649/CS0067、CS0108/CS0109、CS8981、MSB3245 等）。

定性：这些字段绝大多数是 WPF 依赖属性、HALCON 句柄这类"构造后再赋值"或"设计上可为空"的写法——例如 `Services\Core.Halcon\Base\HalconBase.cs` 的 `hSmart`/`hWindow` 在 `HalconRuntime.IsAvailable == false` 时保持 null（占位提示分支），全部使用路径已有 null 守卫。所以 CS8618 在本仓库属于"注解写得比事实严格"的噪声，不是漏赋值缺陷。

背景：56 个 csproj 中 44 个开了 `<Nullable>enable</Nullable>`（12 个 disable），主体代码成文于 nullable 普及之前，开注解等于把历史欠账一次性照出来。

## 改动内容

`.editorconfig`（全仓库此前没有任何 .editorconfig）：

```ini
root = true

[*.cs]
dotnet_diagnostic.CS8618.severity = none
```

- **有意保持开启**：CS8602 / CS8603 / CS8604 / CS8625 等可能真实挡住空引用崩溃的检查。
- 文件刻意**全 ASCII（含注释）**：本机代码页是 GBK，.editorconfig 若被按 ANSI 误读，非 ASCII 注释的字节对可能吞掉换行、连坐下一行规则（与 AGENTS.md §1 记录的 `ImageGallery.cs` 损坏同类机理）。
- 生效机制：编译器按源文件所在目录**向上查找** .editorconfig（`root = true` 止于仓库根），全部工程源码都在仓库根之下，一次覆盖 56 个工程，无需改任何 csproj。

## 验证证据

| 构建 | 改动前 | 改动后 |
| --- | --- | --- |
| `dotnet build Services\Core.Halcon\Core.Halcon.csproj -t:Rebuild` | 102 条不同警告（CS8618 40 条） | **31 条，CS8618 = 0** |
| `dotnet build Core\VM.Core.csproj -t:Rebuild` | CS8618 大头（VariableNode/ToolItemModel/FlowSession/三个 Toolkit 等） | **CS8618 = 0** |

两次重建均 0 错误；CS8602/8603/8604/8625/8622、CS0168、CS9264 等其余警告代码原样保留，证明规则精确命中 CS8618 一条。

## 回滚 / 调节

- 想在 IDE 里保留灰色提示级线索：`severity` 改为 `suggestion`；
- 彻底恢复警告：删除该行或整个 `.editorconfig`。

## 未决问题

- 剩余约 390 条 CS8602/8603/8625 保留，作为后续按模块治理的 backlog（这批是真能防空引用崩溃的，不建议整体静音）。
- 与本改动无关的线索：命令行构建 VisionMaster 时其 XAML 临时工程（`*_wpftmp.csproj`）报 2 条 `error CS0246: 找不到 ILogService`（`HelpViewModel.cs:44,58`、`HelpCatalogCheck.cs:24,26`），主工程 C# 编译本身零错误。仓库存在两个同名 `public interface ILogService`（`Shard\Core.Interfaces` 与 `Services\Services.Logger`），怀疑与临时工程的引用集合有关；因 VS 常驻并锁定 `UI.dll`，未能在干净环境下定论。
