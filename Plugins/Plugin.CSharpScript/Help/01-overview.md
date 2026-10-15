# C#脚本

> 分类：逻辑控制 ｜ 来源：插件 Plugin.CSharpScript

用**完整 C# 语法**写一个流程步骤：取上游输入、算逻辑、写输出、读写变量、记日志、推图像、主动判失败。
脚本正文由 Roslyn 脚本引擎（`Microsoft.CodeAnalysis.CSharp.Scripting`）在运行时编译执行。

## 为什么需要它 / 原理

流程里的算子是"一格一职能"：拼接十几个算子才能表达的判定逻辑（多条件组合、统计聚合、按类型分派、
读配置、拼报文），在 **C#脚本** 里是一段几十行的正经代码。反过来，只调几个 Halcon 算子出个效果图，
用**图像脚本**更省事（它的绘图与算子提示是为 Halcon 流程优化的）。

四条机制决定了它的用法：

1. **变量表 = 端口 = 脚本里的键**。左列每加一行输入/输出变量，插件就生成一个同名动态端口；
   脚本里用 `Context.GetInput<T>("名字")` / `Context.SetOutput("名字", 值)` 读写它。
   名字同时是三样东西，所以改名字等于换端口，连线会断。
2. **`Context` 是唯一门面**。模块参数、变量池、日志、显示、主动失败都从它走（成员清单见第 2、3 页）。
   自引用属性 `Context.Context` 也在，写成 `Context.Context.Info(...)` 同样能用。
3. **脚本是"顶层语句"**。可以在正文里定义 class、方法、泛型、LINQ、`using`，但**真正干活的语句要放在顶层**——
   手写的 `Main` / 自定义入口会被忽略。
4. **失败契约只有两条**：脚本抛异常 = 失败；`Context.Fail("原因")` = 显式失败。
   **返回值不参与判定**，脚本正常返回即视为成功。

## 输入端口

输入端口由「输入变量」表逐行驱动（初始为空表，点「+ 新增」加行，默认名 `in` / `in2` / `in3` …）。

| 端口 | 必填 | 说明 |
| --- | --- | --- |
| 变量表每行一个同名端口 | 否（行本身不强制连线） | 连线时取上游值；未连线时按类型在行内编辑框手填 |

行内「类型」下拉决定端口承载的 CLR 类型（当前显示英文成员名，含义如下）：

| 声明类型（界面英文成员名） | 端口 CLR 类型 | 用途 |
| --- | --- | --- |
| `Object` | `object` | 不确定类型、原样转发（默认值） |
| `Int` | `int` | 整数 |
| `Double` | `double` | 浮点数 |
| `Bool` | `bool` | 真假 |
| `String` | `string` | 文本 |
| `HTuple` | `HTuple` | HALCON 元组（多值数组） |
| `HImage` | `HImage` | 图像 |
| `HRegion` | `HRegion` | 区域 |
| `HXld` | `HXLDCont` | 轮廓 / XLD（注意枚举名与 CLR 类型名不一致） |
| `HObject` | `HObject` | HALCON 图形对象基类 |

图形类（`HImage`/`HRegion`/`HXld`/`HObject`）必须连线拿到真实对象，不能靠手填文本凑出来。

## 输出端口

| 端口 | 说明 |
| --- | --- |
| 变量表每行一个同名端口 | 脚本里 `Context.SetOutput("名字", 值)` 写入；名字不存在于表中会被**静默忽略** |
| （同名端口的默认值） | 类型由「类型」下拉决定；写不进该类型时抛错并被包成 `写输出端口[X]失败：…` |

**改全局变量的正门就在这里**：把本步骤的输出端口连到已绑定全局变量的目标上，
引擎在连线时落回 `Workspace.GlobalVariables`。运行期的临时变量走 `Context.SetVar` / `Context.GetVar`
（本次流程运行内共享，不跨运行持久）。

图形输出（`HImage` 等）在该行把「显示窗口」选成 窗口1~9，执行完会自动推预览到对应视图；选「不显示」则不推。

## 关键参数

> 完整参数与取值范围以流程中该步骤的「模块参数」界面为准，这里只解释含义。

| 参数 | 默认 | 说明 |
| --- | --- | --- |
| `ScriptText` | 内置模板（见下方"典型用法"） | 脚本正文（顶层语句）。随配置内嵌进 `.vms`；正文为空时按空串处理 |
| `InputVars` | 空集合 | 输入变量表；每项一个动态输入端口 |
| `OutputVars` | 空集合 | 输出变量表；每项一个动态输出端口，并写进步骤的输出端口快照供下游选择 |
| 行内 `Name` | `in` / `out`（重名自动加序号） | 变量名 = 端口名 = 脚本里的取存键；建议字母开头、不含空格 |
| 行内 `Type` | `Object` | 声明类型；**改类型会重建端口**，原连线可能需要重连 |
| 行内 `Remark` | 空 | 备注；输出行会用它作为下游端口说明与推图标题来源 |
| 行内 `DisplayWindow` | `0`（不显示） | 仅图形类输出可见：`0`=不显示，`1`~`9`=主界面第几号视图窗口 |

## 口径与失败策略

- **编译失败**：步骤直接 `Success=false`，错误信息形如 `第 N 行 第 M 列: CS…: …`。
  Roslyn 的行号就是编辑器真实行号，不用换算。
- **运行期异常**：剥掉 `AggregateException` / `TargetInvocationException` 外壳后写成
  `脚本运行异常: <消息>`，判失败。
- **`Context.Fail("原因")`**：脚本正常跑完后宿主置失败，错误信息就是那句原因。
  `Fail` **不会中断脚本**，务必紧跟 `return`。
- **输出端口不存在**：`SetOutput` 忽略，不报错（想确认就看下游有没有值）。
- **输出类型不符**：抛错并包成 `写输出端口[名字]失败：<原始消息>`，判失败。
- **无超时、无取消配置项**：只把流程的 `CancellationToken` 透传给脚本；死循环会卡住整条流程，
  需要自己在脚本里判断/加限时。
- **无沙箱**：白名单只约束"能引用哪些扩展 dll"，管不住脚本正文（仍可读写文件、起进程）。
- **编译缓存**：按脚本正文 SHA256 指纹缓存已编译委托，内容不变零成本复用；超过 64 条整体清空。
  引用集（能 `using` 的程序集清单）是**进程级一次性构建**，改白名单或补 dll 必须重启宿主。

## 典型用法 / 接线

```
图像采集.Image ──▶ C#脚本.Image        （HImage，必须连线）
阈值设置.Value ──▶ C#脚本.阈值          （Double，可连线可手填）
C#脚本.Count   ──▶ 下游步骤.缺陷数
C#脚本.Result  ──▶ 全局变量.检测结果     （绑定全局变量的端口 = 写全局）
```

新建步骤的默认正文（可直接照抄结构）：

```csharp
// C# 脚本（顶层语句）—— 可用完整 C#：class、方法、LINQ、using、HalconDotNet 等
// 自定义函数统一走 Context：
//   var img = Context.GetInput<HImage>("Image");   // 取上游输入端口
//   Context.SetOutput("Count", 12);                // 写输出端口（供下游连线）
//   Context.SetVar("temp", 3.14);                  // 写运行期变量（下游可读）
//   double v = Context.GetVar<double>("temp");     // 读运行期变量
//   Context.Info("完成"); Context.ShowImage(img, 1);
//   Context.ShowImage(img, 1, "缺陷检测结果", ("缺陷数", 51), ("判定", "NG"));  // 带说明推图
//   Context.Fail("参数越界");                        // 主动让本步骤失败

Context.Info("C# 脚本开始执行");
Context.SetOutput("Result", "ok");
```

判定 NG 的标准写法（`Fail` 后必须 `return`）：

```csharp
var count = Context.GetInput<int>("DefectCount");
if (count > 0)
{
    Context.Fail($"检出 {count} 个缺陷");
    return;
}
Context.SetOutput("Judge", "OK");
```

## 常见问题

**Q：脚本里能用什么？**
完整 C#（class、方法、泛型、LINQ、`using`、try/catch）+ 默认 imports
（`System`、`System.Collections.Generic`、`System.Linq`、`System.Text`、`System.Math`、`HalconDotNet`）
+ `Context` 门面 + 宿主运行目录里的所有托管 dll（HalconDotNet、Newtonsoft、HslCommunication 等）。
细节见第 2 页。

**Q：能不能引 NuGet 上的新包？**
不能直接引。第三方 dll 必须放宿主目录 `ExternalLibs\` 并登记进 `whitelist.json`，
且**重启宿主**才生效（引用集进程级只建一次）。

**Q：为什么 `Context.GetInput<double>("阈值")` 取到 0？**
两种情况：端口不存在（名字和输入表不一致，返回 `default`）；或上游给的是 `int`/`string`，
`GetInput<T>` 走 `Convert.ChangeType` 转换，转不动时抛 `InvalidCastException` 变成"脚本运行异常"。
先核对名字，再看类型。

**Q：`Context.GetVar<T>` 和 `GetInput<T>` 的区别？**
`GetInput` 读**本步骤端口**（连线值或手填值）；`GetVar` 读**本次流程运行的变量池**
（上游 `变量赋值`、其它脚本 `SetVar` 写的）。变量池里不存在时返回 `default`，不报错。

**Q：写完 `Context.Fail("…")` 怎么脚本还在往下跑？**
`Fail` 只记录失败意图（`Failed` 标志与消息），执行流不受影响。必须自己 `return` 短路。

**Q：图像怎么显示到主界面？**
`Context.ShowImage(img, 1)` 推第 1 号视图；还有带标注
（`Context.ShowImage(img, 1, marks)`，配 `NewMarks()`/`MarkLine`/`MarkText`）
和带标题+键值信息（`Context.ShowImage(img, 1, "标题", ("缺陷数", 51))`）的重载。
另外输出行类型为 `HImage` 且选了「窗口N」时，执行完会自动推一张。

**Q：脚本死循环把流程卡住了怎么办？**
没有超时兜底。只能中止/重启流程；写脚本时对循环加步数上限，或用 try/catch + `Context.Fail` 收口。

**Q：改了白名单、往宿主目录丢了新 dll，脚本还是不认这个类型？**
引用集与编译缓存都是进程级终身缓存，**重启宿主**。

**Q：输出变量改了类型，下游端口怎么变了？**
类型决定端口承载的 CLR 类型，改类型会重建端口（`PortsVersion` 通知行内编辑器重取），
下游按类型选择器需要重选一次。
