# 类型转换 · 端口与参数

> 分类：数据处理 ｜ 来源：插件 Plugin.Utility

本页是字典：三个输入、一个输出，逐个写清**端口名、声明类型、默认值、必填性、
界面上这一行长什么样、不配置的后果**。内容逐字取自 `DataConversionPlugin.cs`。

## 端口声明对照

| C# 成员 | 端口名 | 声明类型 | 默认值 | 端口描述 | 必填 |
| --- | --- | --- | --- | --- | --- |
| `InputValue` | `Input` | `InputPort` 装 `object` | `null` | 输入值 | 是（基类默认） |
| `Target` | `Target` | `InputPort` 装 `TargetDataType` | `Double` | 目标类型 | 是（基类默认） |
| `Format` | `Format` | `InputPort` 装 `string` | `yyyy-MM-dd HH:mm:ss` | 格式字符串 | 是（基类默认） |
| `Result` | `Result` | `OutputPort` 装 `object` | — | 转换结果 | 输出端口无此项 |

三行输入的必填性都**来自基类默认**（`InputPort` 的 `IsRequired` 初值是 `true`，
源码里没有写 `{ IsRequired = false }`；只有「延时」显式重复了一次）。
`Input` 的默认值是 `null`，这一点让它比另外两个端口更容易撞上必填检查，见下文。

## 本插件没有 `[StepConfig]` 配置属性

四个参数全部以输入端口形态存在（源码里没有任何 `[StepConfig]` 标注）。
后果：三个参数都能连线、共用同一个存盘字典 `InputValues`、界面回退到宿主自动生成的参数面板。

## 参数面板上这三行

宿主自动生成的参数面板只列输入端口，本插件正好三个：

- **行序按端口名排序**（忽略大小写）：界面上是 `Format`、`Input`、`Target` 这个顺序
  （不是源码的声明顺序），行标签取端口描述，所以看到的是「格式字符串」「输入值」「目标类型」。
- `Target` 那一行是**下拉**（枚举端口被识别为"只能选"）：
  没有链接按钮、没有清除按钮，候选按端口类型现场取、顺序即声明顺序
  （`Int32` / `Double` / `Boolean` / `String` / `DateTime`）。
  **显示的是英文成员名**——`TargetDataType` 未配中文 `[Display]`（已知待办）。
- `Input` 与 `Format` 是普通文本框 + 🔗 链接 + ✕ 清除。

## `Format` 只在一种情况下生效

`Format` 只被 `DateTime` 那一路用到：`DateTime.ParseExact(input.ToString(), format, null)`。
选 `Int32`/`Double`/`Boolean`/`String` 时这一行**根本不参与运算**（填什么都一样）。
运行期如果端口里是 `null`，代码会回落成默认值 `yyyy-MM-dd HH:mm:ss`
（源码写了 `?? "yyyy-MM-dd HH:mm:ss"`），所以清空 `Format` 不等于"不格式化"，
而是等于"用默认格式"。

反过来，`Format` 端口上的文字对**日期解析是逐字符严格的**：`H` 与 `HH`、`M` 与 `MM` 的差别
直接决定 `2026-10-14 9:30:00` 能不能解析。

## 逐个端口的"不配置会怎样"

**`Input`（输入值，`object`，默认 `null`）**
没连线也没在面板里填过 → 编译期报
`[参数缺失] '步骤名' 的必填参数 'Input' 未配置！`
判据是"存盘字典里这个键的值不是 null"——`Input` 的默认值恰好就是 `null`，
所以**光看着端口上有值是不够的**（对象端口压根没有初值可看）。
要么连线、要么在文本框里打点东西（**空格串、空串都算"填过"**，能过编译，
但随后按目标类型解析时会失败）。

**`Target`（目标类型，默认 `Double`）**
没连线、面板里也从没确认过 → 同样报必填缺失（端口名换成 `Target`）。
确认后它就恒有值，实际不会为空。

**`Format`（格式字符串，默认 `yyyy-MM-dd HH:mm:ss`）**
同上，端口名换成 `Format`。默认值非 null，所以只要确认过一次面板就永久满足。

**`Result`（转换结果，`object`）**
输出端口。类型是 `object`，两个方向都"装得下"，
所以任何下游输入端口都能接受它的连线（编译期判据对 `object` 一律放行）。
代价是**类型错误要到运行期才暴露**——"连得上"不等于"转得对"。

## 连线兼容性速查（按编译期判据 `FlowCompiler.IsLinkable`）

| 上游输出类型 | 连进 `Input`（期望 `object`） | 连进 `Target`（期望枚举） | 连进 `Format`（期望 `string`） |
| --- | --- | --- | --- |
| 任意类型 | **可**（object 目标一律放行） | 否（除 `string`/`object` 来源） | **可**（string 目标一律放行，字符串化是有意放开的） |
| `string` | 可 | **可**（运行期 `Enum.Parse` 按成员名解析） | 可 |
| `int` / `double` 等数值 | 可 | **否**（数值 → 枚举编译期不放行） | 可 |
| `bool` | 可 | 否 | 可 |
| `HImage` / `HRegion` | 可 | 否 | 可（会走字符串化，结果基本没用） |

两把尺子的分工要知道：**编译期**用 `FlowCompiler.IsLinkable`（会报 `[连线类型不匹配]`），
**变量绑定弹窗**用 `TypeHelper.CanBindTo`（决定候选列不列出来）——
后者对 `object` 双向放行，是实测出来的（全局变量的默认类型就是 `object`，
静态这层拦会把合法用法全禁掉）。

## 改端口名 / 改成员顺序的代价

- 改端口名（`Input` / `Target` / `Format` / `Result`）= 改存盘键，
  老方案里的键查不到 → 端口静默回落默认值，症状是"升级之后参数全没了"。
- `TargetDataType` 进 JSON 落的是**序号**（`Int32=0` … `DateTime=4`），
  所以**调成员顺序会静默改变历史方案选的目标类型**。新增类型只能在末尾追加。
- 给成员补中文 `[Display(Name="…")]` **不会**动存盘值（存的永远是成员名），是安全的。
