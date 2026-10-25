using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using VisionMaster.Models;
using VisionMaster.Services;
using Core;

namespace VmsCompileProbe;

/// <summary>
/// .vms 方案导入校验探针：走真机同一条路（SolutionService.LoadAsync →
/// WorkspaceContext.SwitchSolution → VariablePersistenceService.Restore →
/// 逐流程 FlowCompiler.Compile），把"能不能导入运行"从"文件长得像"变成"编译通过"。
///
/// 为什么必须 Restore：只 SwitchSolution 不灌全局变量池，编译器会把每个
/// "全局变量 (Global).xxx" 链接都判成 [连线断开] —— 那是假红。
///
/// 为什么要把 Modules\ 挂进 AssemblyResolve：FlowCompiler 用裸 Type.GetType(PluginTypeName)
/// 造插件，插件 DLL 不在探针的引用集里，不挂解析器就会全报 [加载失败] 找不到插件。
///
/// 用法：VmsCompileProbe.exe --solution "D:\...\xxx.vms"
/// 退出码：0 = 全部流程编译通过；1 = 有错误；2 = 加载失败
/// </summary>
internal static class Program
{
    private static readonly List<string> ProbeDirs = new();

    [STAThread]
    private static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        string path = null;
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--solution", StringComparison.OrdinalIgnoreCase))
                path = args[i + 1];
        }

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            Console.WriteLine($"找不到方案文件: {path}");
            return 2;
        }

        return RunAsync(Path.GetFullPath(path)).GetAwaiter().GetResult();
    }

    /// <summary>仓库根下的 Modules\（插件产物）与 bin\（宿主依赖）都挂上。</summary>
    private static void HookAssemblyResolve()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "VisionMaster.sln")))
            dir = dir.Parent;

        if (dir != null)
        {
            foreach (var sub in new[] { "Modules", "bin", Path.Combine("VisionMaster", "bin", "Debug", "net9.0-windows") })
            {
                var full = Path.Combine(dir.FullName, sub);
                if (Directory.Exists(full)) ProbeDirs.Add(full);
            }
        }

        AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
        {
            var name = new AssemblyName(e.Name).Name;
            foreach (var d in ProbeDirs)
            {
                var candidate = Path.Combine(d, name + ".dll");
                if (File.Exists(candidate))
                    return Assembly.LoadFrom(candidate);
            }
            return null;
        };
    }

    private static async Task<int> RunAsync(string path)
    {
        HookAssemblyResolve();
        Console.WriteLine($"方案: {path}");

        var service = new SolutionService();
        var loaded = await service.LoadAsync(path);
        if (!loaded.Success || loaded.Data == null)
        {
            Console.WriteLine($"RESULT: FAIL  加载失败: {loaded.Message}");
            return 2;
        }

        var solution = loaded.Data;
        Console.WriteLine($"  流程 {solution.Flows.Count} 个 / 变量快照 {solution.VariableSnapshots.Count} 条");

        var workspace = new WorkspaceContext();
        workspace.SwitchSolution(solution);

        // 这一步不能省，否则全局变量链接全是假红。
        VariablePersistenceService.Restore(solution, workspace);
        Console.WriteLine($"  全局变量池 {workspace.GlobalVariables.Count} 条");

        if (solution.Scada?.Pages != null)
        {
            Console.WriteLine(
                $"  组态页面 {solution.Scada.Pages.Count} 个 / 图元 {solution.Scada.Pages.Sum(p => p.Elements?.Count ?? 0)} 个" +
                $" / 报警 {solution.Scada.Alarms?.Count ?? 0} 条 / 起始页 {solution.Scada.StartupPageId}");
        }

        var compiler = new FlowCompiler(workspace);
        int failed = 0;

        foreach (var flow in solution.Flows)
        {
            if (flow == null) continue;

            workspace.SwitchFlow(flow);
            var result = compiler.Compile(flow.Steps, flow.FlowName);

            if (result.Success)
            {
                Console.WriteLine($"  [OK]   {flow.FlowName,-14} 步骤 {Count(flow.Steps)}");
            }
            else
            {
                failed++;
                Console.WriteLine($"  [FAIL] {flow.FlowName,-14} 步骤 {Count(flow.Steps)}  错误 {result.Errors.Count} 条");
                foreach (var err in result.Errors.Take(15))
                    Console.WriteLine($"           · {err.StepName}：{err.Message}");
                if (result.Errors.Count > 15)
                    Console.WriteLine($"           · ……另有 {result.Errors.Count - 15} 条");
            }
        }

        Console.WriteLine(failed == 0 ? "RESULT: OK" : "RESULT: FAIL");
        return failed == 0 ? 0 : 1;
    }

    private static int Count(System.Collections.IEnumerable steps)
    {
        if (steps == null) return 0;
        int n = 0;
        foreach (var _ in steps) n++;
        return n;
    }
}
