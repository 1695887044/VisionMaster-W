using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Core.Interfaces;
using HalconDotNet;
using VisionMaster.Models;
using VisionMaster.Services;
using static FlowCanvasChecks.Program;
// 本文件 using 了 System.Threading，裸写的 ExecutionContext 会和 System.Threading.ExecutionContext 撞名
using ExecutionContext = VisionMaster.Services.ExecutionContext;

namespace FlowCanvasChecks
{
    /// <summary>
    /// 「变量赋值」节点的断言：两种作用域都要真跑一遍。
    ///
    /// 为什么值得单独钉住
    /// ---------
    /// ① <b>运行时那条路是既有行为</b> —— 加作用域不能把它碰坏（老流程里到处都是这个节点）；
    /// ② <b>全局那条路是新增能力</b> —— 插件物理上够不到"变量管理"所在的程序集，
    ///    只能经 <see cref="IGlobalVariableWriter"/> 走主程序侧；这条通路不跑一遍就不知道通没通；
    /// ③ <b>失败必须说话</b> —— 变量不存在 / 类型不匹配都要给出中文原因，不许静默成功。
    ///    这一条最要紧：若写失败却报成功，操作员会以为画面上的图已经更新了。
    ///
    /// 插件是运行期装载的，本工程没有编译期引用，故用反射构造与设端口。
    /// </summary>
    internal static class VariableAssignmentCheck
    {
        public static void Run()
        {
            Section("[A2] 变量赋值：运行时 / 全局两种作用域");

            var repoRoot = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\"));
            var asm = AppDomain.CurrentDomain.GetAssemblies()
                          .FirstOrDefault(a => a.GetName().Name == "Plugin.Util")
                      ?? Assembly.LoadFrom(Path.Combine(repoRoot, @"Modules\Plugin.Util.dll"));

            var pluginType = asm.GetType("VisionMaster.Plugins.Util.VariableAssignmentPlugin", true)!;

            // 作用域是 string 不是枚举：枚举经 InputValues 往返会抛 InvalidCastException（详见插件里的注释）
            const string scopeRuntime = "Runtime";
            const string scopeGlobal = "Global";

            var workspace = new WorkspaceContext();
            var solution = new SolutionModel { SolutionName = "变量赋值断言" };
            var flow = new FlowModel { FlowName = "赋值断言流程" };
            solution.Flows.Add(flow);
            workspace.SwitchSolution(solution);
            workspace.SwitchFlow(flow);

            // 备两个全局变量：一个图像（要写进去）、一个整数（用来验类型守门）
            workspace.GlobalVariables.Add(VariableFactory.CreateLocal("Img", typeof(HImage), "断言用图像变量"));
            workspace.GlobalVariables.Add(VariableFactory.CreateLocal("Qty", typeof(int), "断言用计数变量"));
            var varCountBefore = workspace.GlobalVariables.Count;

            Check("前提：全局变量注册后按名可查（注册表订阅了集合变更）",
                workspace.VariableRegistry?.FindByName("Img") != null,
                $"FindByName(Img) = {(workspace.VariableRegistry?.FindByName("Img") == null ? "null" : "命中")}");

            // ---------- 路一：运行时变量（既有行为，不许被碰坏）----------
            var r1 = Assign(pluginType, scopeRuntime, "Out1", "hello", true, workspace);
            Check("【运行时】变量不存在 + 勾了自动创建 → 建成并赋值成功",
                r1.Success && r1.Ctx.LocalVariables.ContainsKey("Out1")
                && (r1.Ctx.LocalVariables["Out1"] as string) == "hello",
                $"Success={r1.Success} Err=[{r1.Error}] "
                + $"值=[{(r1.Ctx.LocalVariables.ContainsKey("Out1") ? r1.Ctx.LocalVariables["Out1"] : "<无>")}]");

            var r2 = Assign(pluginType, scopeRuntime, "Out1", "world", true, workspace);
            Check("【运行时】变量已存在 → 覆盖为新值（不重复创建）",
                r2.Success && (r2.Ctx.LocalVariables["Out1"] as string) == "world",
                $"Success={r2.Success} 值=[{r2.Ctx.LocalVariables["Out1"]}]");

            var r3 = Assign(pluginType, scopeRuntime, "Out2", "x", false, workspace);
            Check("【运行时】变量不存在 + 没勾自动创建 → 明确报「不存在」",
                !r3.Success && r3.Error.Contains("不存在"),
                $"Success={r3.Success} Err=[{r3.Error}]");

            // ---------- 路二：全局变量（新增能力）----------
            HOperatorSet.GenImageConst(out HObject imgObj, "byte", 8, 8);
            var image = new HImage(imgObj);
            try
            {
                var r4 = Assign(pluginType, scopeGlobal, "Img", image, false, workspace);
                var stored = workspace.VariableRegistry?.FindByName("Img")?.Value as HImage;
                Check("【全局】目标存在 + 值是 HImage → 写进全局变量",
                    r4.Success && stored != null && stored.IsInitialized(),
                    $"Success={r4.Success} Err=[{r4.Error}] 变量现值={(stored == null ? "null/不是 HImage" : "HImage ✓")}");

                var r5 = Assign(pluginType, scopeGlobal, "NoSuchVar", image, true, workspace);
                Check("【全局】目标不存在 → 明确报错、且不新建变量（勾了自动创建也不新建）",
                    !r5.Success && workspace.GlobalVariables.Count == varCountBefore,
                    $"Success={r5.Success} Err=[{r5.Error}] 变量数 {varCountBefore} → {workspace.GlobalVariables.Count}");

                var r6 = Assign(pluginType, scopeGlobal, "Qty", "这是文本不是整数", false, workspace);
                Check("【全局】类型不匹配（int 变量写文本）→ 明确报错",
                    !r6.Success && r6.Error.Length > 0,
                    $"Success={r6.Success} Err=[{r6.Error}]");

                var r7 = Assign(pluginType, "globl", "Img", image, false, workspace);
                Check("作用域取值拼错 → 明确报错，不静默当成运行时",
                    !r7.Success && r7.Error.Contains("无法识别"),
                    $"Success={r7.Success} Err=[{r7.Error}]");
            }
            finally
            {
                image.Dispose();
                imgObj?.Dispose();
            }

            // ---------- 路三：把图写进全局图像变量之后，方案还能不能存下来 ----------
            // 旧实现里 VariablePersistenceService.Capture 直接把变量的值（HImage）塞进 VariableDto，
            // 而该字段的约定是"JSON 原生编码：数字/字符串/布尔/数组"。Newtonsoft 序列化 HImage
            // 会走 Halcon 的 serialize_image，句柄为空就抛 HALCON #4056 —— 而这一抛发生在
            // "保存方案"里，于是整个方案都存不下来。
            Section("[A3] 图像变量：只存定义、不存值（存值会让方案存不下来）");

            var w2 = new WorkspaceContext();
            w2.GlobalVariables.Clear();

            HOperatorSet.GenImageConst(out HObject liveObj, "byte", 8, 8);
            var live = new HImage(liveObj);
            var empty = new HImage(); // 句柄为空 —— 就是它把"保存方案"炸掉的
            try
            {
                w2.GlobalVariables.Add(new LocalVariableModel
                { Name = "LiveImg", DataType = typeof(HImage), DefaultValue = live, Value = live });
                w2.GlobalVariables.Add(new LocalVariableModel
                { Name = "EmptyImg", DataType = typeof(HImage), DefaultValue = empty, Value = empty });
                w2.GlobalVariables.Add(new LocalVariableModel
                { Name = "Num", DataType = typeof(int), DefaultValue = 7, Value = 42 });

                var sol = new SolutionModel { SolutionName = "图像变量落盘断言" };
                var serializeError = "";
                try
                {
                    VariablePersistenceService.Capture(sol, w2);
                    SolutionService.Serialize(sol); // ← 旧实现在这一行抛 HALCON #4056
                }
                catch (Exception ex) { serializeError = ex.Message; }

                Check("含图像变量的方案能序列化（旧实现抛 HALCON #4056 object-ID is NULL）",
                    serializeError.Length == 0, serializeError);

                var dto = sol.VariableSnapshots.FirstOrDefault(d => d.Name == "LiveImg");
                Check("图像变量的定义照样落盘（名字 / 类型 / 稳定身份都在）",
                    dto != null && dto.DataTypeString.Contains("HImage") && dto.VariableId != Guid.Empty,
                    $"DataTypeString={dto?.DataTypeString} Id={dto?.VariableId}");

                Check("图像变量的当前值与初始值都不落盘（落 null，不把像素写进 .vms）",
                    dto != null && dto.Value == null && dto.DefaultValue == null,
                    $"Value={dto?.Value ?? "null"} / DefaultValue={dto?.DefaultValue ?? "null"}");

                var numDto = sol.VariableSnapshots.FirstOrDefault(d => d.Name == "Num");
                Check("普通变量的值照旧往返（这次收紧没有误伤）",
                    numDto != null && Convert.ToInt32(numDto.Value) == 42, $"Num.Value={numDto?.Value}");
            }
            finally
            {
                live.Dispose();
                liveObj?.Dispose();
                empty.Dispose();
            }
        }

        private sealed class Outcome
        {
            public bool Success;
            public string Error = "";
            public ExecutionContext Ctx = null!;
        }

        /// <summary>设好端口、跑一次，把结果端口读回来</summary>
        private static Outcome Assign(
            Type pluginType, object scope, string name, object value, bool create,
            WorkspaceContext workspace)
        {
            var plugin = (VisionPluginBase)Activator.CreateInstance(pluginType)!;
            plugin.InstanceName = "断言.变量赋值";

            Port(pluginType, plugin, "VariableName").Value = name;
            Port(pluginType, plugin, "Value").Value = value;
            Port(pluginType, plugin, "CreateIfNotExists").Value = create;
            Port(pluginType, plugin, "Scope").Value = scope;

            var session = new FlowSession { FlowName = "赋值断言" };
            var ctx = new ExecutionContext(new StubLog(), session, workspace, new CancellationTokenSource().Token);
            plugin.RunAlgorithm(ctx);

            return new Outcome
            {
                Success = (bool)plugin.Success.Value,
                Error = plugin.ErrorMessage.Value as string ?? "",
                Ctx = ctx,
            };
        }

        private static IInputPort Port(Type pluginType, object plugin, string propertyName)
            => (IInputPort)pluginType.GetProperty(propertyName)!.GetValue(plugin)!;
    }
}
