# 码读取 · 开发：宿主契约与配置界面移植清单

> 分类：图像处理 ｜ 来源：插件 Plugin.CodeReader ｜ 读者：开发

这页列**本插件依赖的宿主约定**：配置生命周期怎么走、值怎么落盘、试运行为什么能把图带进配置界面。
要照它写一个"带自定义配置界面的识别/读码类插件"，看最后那张清单。

## 一、配置会话的时间线（谁在什么时候调什么）

```
双击步骤 → 宿主调 GetConfigView(stepData)
  ├─ Initialize(stepData)（插件覆写，:206-211）
  │    ├─ base.Initialize(stepData)：记 StepData + ApplyConfigValues 把 InputValues 灌进
  │    │    端口（Image）与 [StepConfig] 属性（Symbology / TimeoutMs / Polarity）
  │    └─ 插件补发 OnPropertyChanged(Is2D / SymbologyHint)
  │        ← 这一步**必须**：属性值是从 JSON 直接 Set 上去的，不走 setter，
  │          不补通知的话界面第一次打开时"二维码参数"卡片可能整块不显示
  ├─ new CodeReaderView { DataContext = this }：绑定在这一刻建立
  └─ 视图 Loaded → OnViewLoaded() → ShowUpstreamImage()：把端口当前值搬到预览

用户改码制 / 极性 / 超时 …（SetProperty → InputValues 由基类 OnConfirm 写回）

点确定 → OnConfirm(stepData)（基类默认实现，插件不覆写）
点取消 → OnCancel()（默认空实现）

关闭 → 宿主 Dispose 插件实例 → 插件 Dispose()：两个引擎 + DisplayImage + base.Dispose()
```

`GetConfigView` 里先调 `Initialize(stepData)` 再建视图，是**顺序契约**：视图一绑定就得看到正确的值。

## 二、值的双通道（同一份 `InputValues`，两种形态）

`IStepConfigData.InputValues` 是 `Dictionary<string, object>`（键 = 端口名或配置属性名）。
基类 `ApplyConfigValues` / `OnConfirm` 对两类成员一视同仁，差别只在**能不能被连线**：

| 通道 | 本插件成员 | 语义 | 界面上的形态 |
| --- | --- | --- | --- |
| `InputPort<T>` | `Image` | 数据流：可连上游、可被变量替换、可被试运行桥接值覆盖 | `LinkableValueEditor`（🔗 / ✕） |
| `[StepConfig]` | `Symbology` / `TimeoutMs` / `Polarity` | **纯常量配置**：不暴露为端口、不进数据流、不可链接（`Shard\Core.Interfaces\StepConfigAttribute.cs` 的口径） | 普通 ComboBox / TextBox |

所以"码制要不要做成端口"是有答案的：**不要**——它是配置不是数据，做成端口反而会让人去连一条莫名其妙的线。
反过来，`Image` 必须是端口：要连上游、要参与编译期连线校验。

**没有自定义视图的插件不能用这一套**（`AutoPortConfigView` 只渲染输入端口，`[StepConfig]` 在那里没有入口）。
本插件能这么分，前提是它实现了 `IPluginCustomViewProvider`。

## 三、下拉的两套显示机制（本插件用的是自己那份，不是枚举标注）

码制与极性都不是枚举，而是 `string` + 一张候选表，界面靠 WPF 绑定直连：

```
ComboBox ItemsSource = {Binding SymbologyOptions}   ← CodeSymbologyTable.All（20 条）
         DisplayMemberPath = DisplayName            ← 中文/通用名
         SelectedValuePath = HalconName             ← 存 HALCON 原始名
         SelectedValue     = {Binding Symbology}    ← 绑的是原始名，不是显示名
```

极性同理：`PolarityOptions` 是 `record PolarityOption(string DisplayName, string Value)`（:100-107），
显示 `自动（两种都试，推荐）`、存 `any`。

**为什么要这么绕一层**（`CodeSymbology.cs:27-35` 注释）：`HalconName` 是**稳定身份**，
改中文标签不会让存量图纸失效；如果存盘存显示名，改一个字就废掉全部旧方案。

这与平台里另外两条枚举路径不同，移植时别混：
`LinkableValueEditor` / 变量绑定界面读成员的 `[Display(Name)]`；属性网格 `EnumGenerator` 读成员的 `[Description]`。
**本插件三个参数都不是枚举端口，所以两条都不走**——存盘值是字符串，`.vms` 里直接看得见（`"Symbology": "Code 128"`）。

## 四、试运行的图从哪来（`BridgeInputs` 契约）

`Engine\PluginTestRunner.cs` 的 `BridgeInputs(compiledNode, configPlugin, context)`（:223-242）：
对编译实例每个输入端口取 `GetActualValue()`，然后在**配置实例**上
`configPort.LinkedSource = null; configPort.Value = actual;`（值快照，注释说明 `HImage` 是引用复制、
上游编译实例在窗口关闭前一直存活）。

本插件对这条契约的反应：

1. 构造函数订阅 `Image.PropertyChanged`（:162），`OnImagePortChanged` 只认 `e.PropertyName == nameof(IInputPort.Value)`（:167）
   ——**正好是桥接写手动值时发的那个通知**。
2. `ShowUpstreamImage()` 用 `Image.GetTypedValue()` 取（未连线时 = 手动值 = 桥接进来的那份），
   然后 `DisplayImage = new HImage(upstream)` **拷一份自己的**（:234-239）：
   上游句柄归框架管，配置界面绝不能拿着它的引用显示——窗口关掉后那张图可能被回收，预览就成了野句柄。

移植"配置界面要显示上游图"的插件时，这三条（订阅 `Value` 通知、走 `GetTypedValue`、**必须自己拷一份并自管释放**）
一起抄，`RegionColor` / `ColorCheck` 是同一模式。

## 五、执行与失败契约

- 基类 `Execute()` 预置 `Success=true` + 清 `ErrorMessage` → 轮首回收 `IDisposable` 输出 → `RunAlgorithm` →
  未捕获异常统一转 `Fail("{Type}: {Message}")`。本插件两个策略属性都不覆写。
- 业务失败只走 `Fail(...)` + `return`，共 2 条（无图 / `TryRead` 返回 false）；"没读到码"**不是**失败。
- `context.Logger` 在 `RunAlgorithm` 里是**直接用**的（:302 / :311 / :319，没有 null 条件判断）——
  断言走 `plugin.Execute(MakeContext(log))` 会传真 `StubLog`，所以能跑到；
  但要有人手工调 `RunAlgorithm(null)`，这里就是 `NullReferenceException`（基类会把它转成失败，但现场会看到
  `NullReferenceException: Object reference not set...` 这种难读的文案）。移植时按 `Logger?.Info(...)` 写更稳。
- 输出端口 `Codes` 是 `string[]`：数组端口能整体连线（画布上下标写法未必给得出），
  所以才必须有 `Text` 这条标量出口给既有下游。
- `[ParallelSafe]`（`Shard\Core.Interfaces\ParallelSafeAttribute.cs` 的口径：无锁 static 可变字段、
  不碰进程级独占资源、不写全局共享状态、对取消令牌协作）。
  **移植时注意两个引擎实例是每插件实例一份**（不是 static），所以并行分支各自有句柄，不互抢；
  真并行下如果哪天改成进程级共享模型池，这个声明立刻不成立。

## 六、资源投递：本插件"没有要投递的文件"

`Plugin.CodeReader.csproj` 里只有 `<EmbeddedResource Include="Help\**\*" />`，**没有 Assets、没有 PostBuild**：
模型是运行期按码制名现场构造的，不依赖任何外部模型文件（对照 `Plugin.Ocr`：`.omc` 是训练产物，必须随插件带）。
少一条会出错的链路，这是刻意的。

手册页序 = 资源名 Ordinal 序（`01-`、`02-` … 前缀），页标题取正文第一个一级标题，
`NN-dev-` 前缀的页只在开发视图出现。本工程只有一个插件类，所以手册**扁平**放在 `Help\` 根下——
建子目录会让宿主按目录树分册，反而把用户页挤掉。

## 七、照抄一个"带配置界面的识别类插件"的清单

| 项 | 本插件的做法 | 抄的时候别漏 |
| --- | --- | --- |
| 插件即 VM | `GetConfigView` 返回 `new XxxView { DataContext = this }`，code-behind 只留转发 | 别忘了 `Dispose()` 收尾所有私有非托管对象 |
| 参数分通道 | 常量走 `[StepConfig]`，数据走端口 | 没有自定义视图就不要用 `[StepConfig]` |
| 候选表 | `DisplayName` + 稳定 `HalconName`，`SelectedValuePath` 指稳定值 | 存显示名 = 改文案废掉存量方案 |
| 家族分派 | `Is2D` 控制卡片可见性 + `TryRead` 分派两套引擎 | 界面与算法要用**同一个**判据（都问 `CodeSymbologyTable`） |
| 试算 | 与运行共用 `TryRead` | 两套代码必然出现"界面看着对、跑起来不对" |
| 预览图 | 订阅端口 `Value` 通知 + 自己拷一份 | 拿上游引用显示 = 关窗后野句柄 |
| 未命中语义 | 成功 + 计数 0 + Info 带当前配置 | 把正常工况记成失败会污染良率 |
| 失败文案 | 明确失败并把可选值列进文案 | 静默退回默认值是"无从排查"的头号来源 |
| 手册 | `Help\NN-*.md` 扁平 + 开发页 `NN-dev-*.md` | `.csproj` 要有 `EmbeddedResource Include="Help\**\*"`；改完重建插件工程 |
