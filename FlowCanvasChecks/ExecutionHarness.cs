using System.Threading;
using Core.Interfaces;
using VisionMaster.Models;
using VisionMaster.Services;
// 本文件 using 了 System.Threading，裸写的 ExecutionContext 会和 System.Threading.ExecutionContext 撞名
using ExecutionContext = VisionMaster.Services.ExecutionContext;

namespace FlowCanvasChecks
{
    /// <summary>
    /// 执行层断言宿主（流程引擎优化 A1/A2/A3/B1/B2 的验证桩）。
    ///
    /// 为什么不能复用 Harness.cs 里的 StubPluginProvider：
    /// 那里的 "VM.CanvasStub.Leaf/If/For" 只是画布端口表建模用的**字符串假名**，
    /// 仓库里根本没有对应的 CLR 类型。画布断言只关心图纸拓扑，走到 FlowCompiler
    /// 会一律落到 "[加载失败] 找不到插件" 分支 —— 对画布断言无所谓，对执行断言致命。
    /// 所以本文件的桩插件是**真类型**，PluginTypeName 用 AssemblyQualifiedName，
    /// 让 FlowCompiler 的 Type.GetType + Activator.CreateInstance 能真的把它实例化出来。
    ///
    /// 桩插件的端口纪律（照抄产品算子的约束，否则断言会被编译期错误干扰）：
    /// 1. 端口必须声明成 public **属性**，写成字段会被编译期逮住报 [端口声明错误]；
    /// 2. InputPort&lt;T&gt;.IsRequired 默认是 true，未连线就报 [参数缺失]。
    ///    本文件的桩不需要输入口，一个都不声明，从根上绕开这两条。
    /// </summary>
    internal static class ExecHarness
    {
        /// <summary>流程名（编译期会拼进插件 InstanceName）</summary>
        public const string FlowName = "执行层断言流程";

        /// <summary>
        /// 把图纸编译成可执行引擎，并组装好会话 + 执行上下文。
        ///
        /// A2 的硬前提在这里满足：CompiledNode.UpdateStepRuntimeState 要求
        /// context 是 VisionMaster.Services.ExecutionContext 且 CurrentSession != null，
        /// 所以必须用带 session 的那个构造函数，不能用只收 ILogService 的简化版。
        /// </summary>
        public static ExecRun Prepare(StepModel[] blueprints)
        {
            var workspace = new WorkspaceContext();
            var result = new FlowCompiler(workspace).Compile(blueprints, FlowName);

            var run = new ExecRun { Workspace = workspace, Result = result };
            if (!result.Success)
                return run;

            var session = new FlowSession { FlowName = FlowName, ExecutionEngine = result.Data };
            foreach (var step in blueprints)
                session.Blueprints.Add(step);

            run.Session = session;
            return run;
        }

        /// <summary>编译错误的可读形式（断言失败时把原因原样吐给用户，不让人猜）</summary>
        public static string ErrorsOf(CompilationResult result)
            => string.Join(" | ", result.Errors.Select(e => e.Message));
    }

    /// <summary>一次执行断言的装配产物</summary>
    internal sealed class ExecRun
    {
        public WorkspaceContext? Workspace { get; init; }
        public CompilationResult? Result { get; init; }
        // 会话要等编译成功才装配，所以只能是 set 不能是 init
        public FlowSession? Session { get; set; }

        public bool Compiled => Result?.Success == true;
        public string Errors => Result == null ? "(未编译)" : ExecHarness.ErrorsOf(Result);
        public CompiledFlow? Engine => Result?.Data;

        /// <summary>建一个执行上下文（A2 状态上报依赖它携带 CurrentSession）</summary>
        public ExecutionContext NewContext(StubLog log)
            => new(log, Session!, Workspace!, new CancellationTokenSource().Token);
    }

    /// <summary>
    /// 日志桩：把 Warn/Error 文案收进内存，供断言"该吼的有没有吼"。
    /// A3 的判据（"忽略重复的启动请求"）正是靠这里的 Warns 列表断言的。
    /// </summary>
    internal sealed class StubLog : ILogService
    {
        public List<string> Infos { get; } = new();
        public List<string> Warns { get; } = new();
        public List<string> Errors { get; } = new();

        public void Success(params string[] messages) => Add(Infos, messages, "Success");
        public void Info(params string[] messages) => Add(Infos, messages, "Info");
        public void Warn(params string[] messages) => Add(Warns, messages, "Warn");
        public void Error(params string[] messages) => Add(Errors, messages, "Error");
        public void Error(params Exception[] messages) => Errors.Add("Error:" + string.Join(";", messages.Select(e => e.Message)));

        /// <summary>是否出现过包含指定片段的 Warn（大小写按原文匹配，断言文案漂移就有意义）</summary>
        public bool HasWarn(string fragment) => Warns.Any(w => w.Contains(fragment));

        private static void Add(List<string> target, string[] messages, string level)
            => target.Add(level + ":" + string.Join(";", messages));
    }

    /// <summary>
    /// 计数桩算子：跑一次记一笔。
    /// </summary>
    internal class CountingPlugin : VisionPluginBase
    {
        /// <summary>累计执行次数（用例开始前调 Reset 清零）</summary>
        public static int Runs;

        public static void Reset() => Runs = 0;

        public override void RunAlgorithm(IExecutionContext context)
        {
            Interlocked.Increment(ref Runs);
        }
    }

    /// <summary>
    /// 带累加的计数桩算子：顺手把运行时变量 Counter 加一。
    ///
    /// 为什么要这么个开关型子类：While 的条件参数里，运行时变量是每次迭代
    /// 现从 context.LocalVariables 取的（CompiledWhileNode.RunLoop），
    /// 而一个 ExecutionContext 的 LocalVariables 全程只有一份 ——
    /// 让循环体末端的这个桩累加 Counter，"Counter &lt; 2" 才能精确跑两圈后退出，
    /// 否则要么死循环（撞 MaxIterations），要么一圈都不进。
    ///
    /// 不靠 InstanceName 里塞标记来判断：PluginTypeName 会被拼进 AssemblyQualifiedName，
    /// 多写一个字符 FlowCompiler 的 Type.GetType 就找不到类型了。
    /// </summary>
    internal sealed class CounterStepPlugin : CountingPlugin
    {
        public override void RunAlgorithm(IExecutionContext context)
        {
            base.RunAlgorithm(context);
            double current = context.LocalVariables.TryGetValue("Counter", out var v) && v != null
                ? Convert.ToDouble(v)
                : 0d;
            context.LocalVariables["Counter"] = current + 1;
        }
    }

    /// <summary>
    /// 「变量定义」桩算子：把 loopN 写进运行期 LocalVariables，充当 For.LoopCount 连线的数据源。
    ///
    /// 为什么不继承 CountingPlugin：静态 Runs 是 CountingPlugin 上的**共享**计数器，
    /// 继承了它，"循环跑了几圈"就得从总数里减去这一笔，断言读起来费劲还容易算错。
    /// 独立类型让 [E7] 里 Runs 恰好等于圈数。
    ///
    /// 写什么值由静态字段决定：脏值（字符串）与负数是同一套图纸的两种取值，
    /// 不必为每种脏值再开一个 CLR 类型。
    /// </summary>
    internal sealed class LoopVarPlugin : VisionPluginBase
    {
        /// <summary>本步骤要写进 LocalVariables["loopN"] 的值（用例开始前改它）</summary>
        public static object Value = 3;

        public override void RunAlgorithm(IExecutionContext context)
        {
            context.LocalVariables["loopN"] = Value;
        }
    }

    /// <summary>
    /// 闸门桩算子：进来了举旗，卡在门口等放行。
    /// A3 断言用它把执行线程"按住"，从而构造出"同一会话已在运行中"这个真实窗口，
    /// 而不是靠 Thread.Sleep 赌时序。
    /// </summary>
    internal sealed class GatePlugin : VisionPluginBase
    {
        /// <summary>执行线程已抵达闸门</summary>
        public static readonly ManualResetEventSlim Reached = new(false);

        /// <summary>放行执行线程</summary>
        public static readonly ManualResetEventSlim Proceed = new(false);

        public static int Runs;

        public static void Reset()
        {
            Reached.Reset();
            Proceed.Reset();
            Runs = 0;
        }

        public override void RunAlgorithm(IExecutionContext context)
        {
            Interlocked.Increment(ref Runs);
            Reached.Set();
            // 兜 5 秒：万一断言逻辑漏了 Set，也不至于让整个断言程序永久挂死
            Proceed.Wait(TimeSpan.FromSeconds(5));
        }
    }
}
