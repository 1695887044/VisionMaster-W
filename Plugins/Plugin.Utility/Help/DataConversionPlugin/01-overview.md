# 类型转换

> 分类：数据处理 ｜ 来源：插件 Plugin.Utility（工程 `Plugin.Util.csproj`，C# 类 `DataConversionPlugin`）

把一个上游值转成另一种 CLR 类型（Int32 / Double / Boolean / String / DateTime），输出仍是弱类型端口。
最常用的是"数值 ↔ 文本"和"文本 → 时间"。

## 为什么需要它 / 原理

流程里的数据类型是按端口声明死的：测量输出 `double`、判定输出 `bool`、上报通道要 `string`、
变量管理里的时间戳要 `DateTime`。类型对不上时下游端口取值会抛转换异常。本节点把
`Convert.ToInt32` / `Convert.ToDouble` / `Convert.ToBoolean` / `ToString()` / `DateTime.ParseExact`
这五条最常用的路收进一个步骤：**读 `Input` → 按 `Target` 选一条 → 写 `Result`**。

转换动作走 .NET `Convert` 家族，`Input` 端口是 `object`，所以上游什么类型都能连进来。
纯计算、无外部依赖，标了 `[ParallelSafe]`；**没有动态端口**，端口固定四个。

## 输入端口

| 端口 | 必填 | 说明 |
| --- | --- | --- |
| `Input` | 否 | 输入值（`object`，默认 `null`）。连线给上游结果，或在面板里填常量文本 |
| `Target` | 否 | 目标类型（枚举 `TargetDataType`，默认 `Double`） |
| `Format` | 否 | 格式字符串（`string`，默认 `yyyy-MM-dd HH:mm:ss`），**仅 `Target=DateTime` 时使用** |

本插件没有自定义配置视图，走宿主自动生成的参数面板（四行按端口名排序：
`Format` / `Input` / `Result`… 输入端口三行 + 输出供下游连线），每行填常量或点链接图标连线。

## 输出端口

| 端口 | 说明 |
| --- | --- |
| `Result` | 转换结果（`object`，"转换结果"）。失败时为 `null` |

## 关键参数

> 完整参数与取值范围以流程中该步骤的「模块参数」界面为准，这里只解释含义。

| 参数（端口名） | 默认 | 说明 |
| --- | --- | --- |
| `Target` | `Double` | 目标类型。当前显示英文成员名（该枚举未配中文 `[Display]`），含义见下表 |
| `Input` | `null` | 待转换的值 |
| `Format` | `yyyy-MM-dd HH:mm:ss` | `DateTime` 解析用的**精确格式**；留空时自动回落到同一个默认值 |

| `Target` 成员名 | 实际执行 | 注意 |
| --- | --- | --- |
| `Int32` | `Convert.ToInt32(input)` | 浮点入参走**银行家舍入**（`2.5 → 2`、`3.5 → 4`）；文本必须能解析成数；带千分位/多余空格以外的字符会失败 |
| `Double` | `Convert.ToDouble(input)` | 用**当前区域设置**解析文本：机器区域用 `.` 作小数点时，`12,3` 会失败 |
| `Boolean` | `Convert.ToBoolean(input)` | 文本只认 `true`/`false`（大小写不敏感）；数值 0=假、非 0=真；其它文本抛 `FormatException` |
| `String` | `input.ToString()` | 浮点按当前区域格式化（一般 `12.3`）；**不做位数控制**，要格式化请用脚本节点 |
| `DateTime` | `DateTime.ParseExact(input.ToString(), Format, null)` | **严格匹配** `Format`：分隔符、位数、顺序都要对，多一个字符就失败；`null` provider 意味着按当前区域信息解析 |

## 口径与失败策略

- **输入为 `null`（没连线也没填值）→ `Result=null`，并且这一步算成功。**
  这是显式写的分支（`Success=true; return;`），不是漏判。别指望它帮你发现漏接。
- **转换抛异常 → 记日志 `{实例名} 类型转换失败: {消息}`，`Result=null`、`Success=false`。**
  代码注释里有一条历史教训：本插件**不再**自己声明 `Success`/`ErrorMessage`（早期自声明同名属性
  会"影子隐藏"基类端口，插件写派生端口、引擎读基类端口，导致永远报失败）；
  因此**失败原因只写在日志里，步骤的错误信息可能为空**。排查请打开流程日志按实例名过滤。
- **`Target` 落默认分支**（枚举值不在上面五种内，例如方案被手工改坏）时原样透传 `input`，不报错。
- 不做单位换算、不做四舍五入位数、不做文化区域切换——它只管 CLR 类型。

## 典型用法 / 接线

```
Blob 分析.Area ──▶ 类型转换.Input            （double）
                    类型转换.Target = String
                    类型转换.Result ──▶ 上报/SCADA 的文本端口

PLC 读到的文本 ──▶ 类型转换.Input
                   类型转换.Target = Int32     （或 Double）
                   类型转换.Result ──▶ 数值比较.A

时间文本"2026-10-14 09:30:00" ──▶ 类型转换.Input
                                  类型转换.Target = DateTime
                                  类型转换.Format = yyyy-MM-dd HH:mm:ss
```

## 常见问题

**Q：输入连了上游、值也有，为什么 `Result` 是 `null` 而且步骤是红的？**
A：转换失败。真实原因只在日志里（`{实例名} 类型转换失败: …`），
步骤上的错误信息可能是空的。先看日志，别在界面上猜。

**Q：`Target=DateTime` 一直失败？**
A：`ParseExact` 是**逐字符严格匹配**。`2026/10/14` 对 `yyyy-MM-dd` 就是失败，
`2026-10-14 9:30:00`（个位小时）对 `HH`（要求两位）也是失败。
先确认上游文本长什么样，再照着它写 `Format`（`H` 与 `HH`、`M` 与 `MM` 的区别就在这里）。

**Q：`12,3` 转 Double 失败？**
A：区域设置问题。`Convert.ToDouble` 用当前区域的小数点符号；
本机的区域设置决定 `.` 还是 `,`。跨机器部署时这是最常见的一类"换了电脑就坏"。
（注意这与「变量赋值」写全局变量时的口径不同：那边显式锁不变区域性并**拒绝千分位**。）

**Q：把 `2.5` 转成 Int32 怎么得 2？**
A：`Convert.ToInt32` 用银行家舍入。要"半进位"就先加 0.5 再 `Floor`（用「数学计算」），或直接 `Round` 语义自己定。

**Q：`Input` 空着为什么不报错？**
A：设计口径是"空值透传为空、判成功"。如果你希望缺数据就停流程，
请在下游用条件判定（例如接一个「数值比较」或脚本节点）而不是指望这里变红。

**Q：能转成图像 / 区域吗？**
A：不能。目标只有五种 CLR 类型。图形对象的"转换"（如区域→图像）是算子的事，
不是类型转换节点的事。

**Q：`Target` 下拉是英文？**
A：这四个工具插件的枚举还没配中文 `[Display]`（已知待办），显示的就是成员名
（`Int32`/`Double`/`Boolean`/`String`/`DateTime`），含义见上表。
