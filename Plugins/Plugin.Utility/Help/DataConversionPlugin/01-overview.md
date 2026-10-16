# 类型转换

> 分类：数据处理 ｜ 来源：插件 Plugin.Utility

把上游的一个值转成另一种 CLR 类型（Int32 / Double / Boolean / String / DateTime），
输出仍是弱类型端口。最常用的两条路是"数值 ↔ 文本"和"文本 → 时间"。
本页只讲**定位**和**三步上手**；端口字典查「端口与参数」，按现象找原因查「排障」。

## 什么时候用它

流程里的数据类型是按端口声明死的：测量输出 `double`、判定输出 `bool`、上报通道要 `string`、
变量管理里的时间戳要 `DateTime`。类型对不上时下游取值就抛转换异常。
本节点把 `Convert.ToInt32` / `Convert.ToDouble` / `Convert.ToBoolean` / `ToString()` /
`DateTime.ParseExact` 这五条最常用的路收进一个步骤：**读 `Input` → 按 `Target` 选一条 → 写 `Result`**。

**不该用它的事**：单位换算、小数位数格式化、区域设置切换、图形对象的转换
（区域→图像那类是算子的事）。它只管 CLR 类型。

## 三步上手

```
① 工具箱（分组「数据处理」）拖出「类型转换」。
② 参数面板三行：Input（连上游或填文本常量）、Target（下拉选目标类型）、Format（只有选 DateTime 才用）。
③ 下游从 Result 取值——它是 object 端口，什么类型都接得住，
   但**下游端口自己的类型声明才是最终约束**。
```

## 五条转换路（`Target` 下拉里显示的就是这个成员名）

| 成员名 | 实际执行 | 要注意 |
| --- | --- | --- |
| `Int32` | `Convert.ToInt32(input)` | 浮点入参走**银行家舍入**（`2.5 → 2`、`3.5 → 4`）；文本必须能解析成数 |
| `Double` | `Convert.ToDouble(input)` | 用**当前区域设置**解析文本：区域用 `.` 作小数点时 `12,3` 会失败 |
| `Boolean` | `Convert.ToBoolean(input)` | 文本只认 `true`/`false`（大小写不敏感）；数值 0=假、非 0=真；其它文本抛异常 |
| `String` | `input.ToString()` | 浮点按当前区域格式化，**不做位数控制** |
| `DateTime` | `DateTime.ParseExact(input.ToString(), Format, null)` | **逐字符严格匹配** `Format`，多一个字符就失败；`null` provider 表示按当前区域信息解析 |

目标类型的默认值是 `Double`。

## 口径（三条决定"这步算不算成功"的规则）

- **输入为 `null`（没连线也没填值）→ `Result=null`，并且这一步算成功。**
  这是显式写的分支（置成功然后返回），不是漏判。别指望它帮你发现漏接。
- **转换抛异常 → 记日志 `{实例名} 类型转换失败: {消息}`，`Result=null`、`Success=false`。**
  失败原因**只写在日志里**，步骤的错误信息可能是空的。排查请打开流程日志按实例名过滤。
- **认不出的枚举值**（方案被手工改坏）→ 原样透传 `input`，不报错。

## 典型接法

```
Blob 分析.Area ──▶ 类型转换.Input            （double）
                   类型转换.Target = String
                   类型转换.Result ──▶ 上报 / SCADA 的文本端口

PLC 读到的文本 ──▶ 类型转换.Input
                   类型转换.Target = Int32     （或 Double）
                   类型转换.Result ──▶ 数值比较.A

时间文本"2026-10-14 09:30:00" ──▶ 类型转换.Input
                                  类型转换.Target = DateTime
                                  类型转换.Format = yyyy-MM-dd HH:mm:ss
```

## 想看更多

- 端口名、类型、默认值、必填性、`Format` 什么时候才生效 → 「端口与参数」
- 报"类型转换失败"、`12,3` 转不动、`2.5` 变 2、换台机器就坏 → 「排障」
- 为什么它不再自己声明 `Success`、object 端口的连线为什么全放行 → 「开发：结构、契约与不变量」
