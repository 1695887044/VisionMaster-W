# C#脚本 · 快速上手

> 分类：逻辑控制 ｜ 来源：插件 Plugin.CSharpScript

用**完整 C# 语法**写一个流程步骤：取上游输入、算逻辑、写输出、读写变量、记日志、推图像、主动判失败。
脚本正文由 Roslyn 脚本引擎（`Microsoft.CodeAnalysis.CSharp.Scripting`）在运行时编译执行。

## 为什么需要它 / 原理

流程里的算子是"一格一职能"：拼接十几个算子才能表达的判定逻辑（多条件组合、统计聚合、按类型分派、
读配置、拼报文），在 **C#脚本** 里是一段几十行的正经代码。反过来，只调几个 Halcon 算子出个效果图，
用**图像脚本**更省事（它的绘图与算子提示是为 Halcon 流程优化的）。

五条机制决定了它的用法：

1. **变量表 = 端口 = 脚本里的键**。左列每加一行输入/输出变量，插件就生成一个同名动态端口；
   脚本里用 `Context.GetInput<T>("名字")` / `Context.SetOutput("名字", 值)` 读写它。
   名字同时是三样东西，所以**改输入变量名会自动把本步骤的连线与手填值迁到新名字**
   （改输出变量名后下游要重选端口——下游存的是端口名）。
2. **输入/输出 = 数据流边，共享变量 = 命名状态**。全局/系统变量不要绑在输入上：
   在左列「共享变量」表按变量 **Id** 声明（改名不断链），脚本里用
   `Context.GetGlobal<T>("别名")` 读；方向=读写时再可 `Context.SetGlobal("别名", 值)`。
   系统变量恒只读。声明表同时是编译期的写集合（并行写冲突检查靠它）。
3. **`Context` 是唯一门面**。模块参数、变量池、日志、显示、主动失败都从它走（成员清单见第 2、3 页）。
   自引用属性 `Context.Context` 也在，写成 `Context.Context.Info(...)` 同样能用。
4. **脚本是"顶层语句"**。可以在正文里定义 class、方法、泛型、LINQ、`using`，但**真正干活的语句要放在顶层**——
   手写的 `Main` / 自定义入口会被忽略。
5. **失败契约只有两条**：脚本抛异常 = 失败；`Context.Fail("原因")` = 显式失败。
   **返回值不参与判定**，脚本正常返回即视为成功。

## 典型用法 / 接线

```
图像采集.Image ──▶ C#脚本.Image        （HImage，必须连线）
阈值设置.Value ──▶ C#脚本.阈值          （Double，可连线可手填）
C#脚本.Count   ──▶ 下游步骤.缺陷数
C#脚本(共享变量 imageIndex, 读写) ⇄ 全局变量 ImageIndex   （按 Id 声明，脚本内直接读写）
```

> 只把上游结果落全局、脚本不需要读它时，也可以走纯数据流：`C#脚本.Result ──▶ 变量赋值(全局).值`。

新建步骤的默认正文（可直接照抄结构）：

```csharp
// C# 脚本（顶层语句）—— 可用完整 C#：class、方法、LINQ、using、HalconDotNet 等
// 自定义函数统一走 Context：
//   var img = Context.GetInput<HImage>("Image");   // 取上游输入端口
//   Context.SetOutput("Count", 12);                // 写输出端口（供下游连线）
//   int g = Context.GetGlobal<int>("imageIndex");  // 读共享变量（校验/确认时自动补进左侧表）
//   Context.SetGlobal("imageIndex", g + 1);        // 写共享变量（自补的行方向=读写；系统变量只读）
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

**Q：脚本里能直接改全局变量吗？**
能——先在左侧「共享变量」表声明它（方向 = 读写），脚本里 `Context.SetGlobal("别名", 值)` 即可；
读用 `Context.GetGlobal<T>("别名")`。系统变量只读，不能写。声明按变量 Id 寻址，变量改名不断链。
不想在脚本里读值、只把上游结果落全局时，也可以走输出行 + 下游「变量赋值」。

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
