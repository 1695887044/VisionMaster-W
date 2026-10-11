using System;
using System.IO;
using System.Linq;
using VisionMaster.Models;
using static FlowCanvasChecks.Program;

namespace FlowCanvasChecks
{
    /// <summary>
    /// 流程栏（ProcessView 步骤树）模板覆盖守门。
    ///
    /// 事故（2026-10-09 真机截图）：树按 DataType 隐式选中模型模板——ActionStep / ConditionStep /
    /// WhileStep / ForStep / StepCollection 都有，唯独 ParallelStep 没有。WPF 找不到模板就回退默认
    /// 模板，把对象 ToString() 原样上屏：整行显示 "VisionMaster.Models.ParallelStep"，分支一并丢失。
    /// 与 AGENTS.md R19-R22「类型全名上屏」同一类病，只是触发点从 GridView 隐式样式换成类型键模板。
    ///
    /// 守门口径（静态扫描，进程内可跑）：VM.Core 里**所有非抽象 StepModel 子类**必须在
    /// ProcessView.xaml 里有 DataType 模板；例外清单见下（UI 从不实例化它们）。
    /// </summary>
    internal static class ProcessTreeTemplateChecks
    {
        /// <summary>
        /// 例外：UI 永不实例化的遗留模型类。工具箱 BuiltIn_Break / BuiltIn_Continue / BuiltIn_Return
        /// 一律落 ActionStep（ProcessViewModel 拖入只按 IsContainer 分派：容器认 While/For/Parallel
        /// 三个 BuiltIn 名，其余容器兜 If，非容器全走 ActionStep），编译期按 PluginTypeName 特判
        /// （FlowCompiler）。反向断言见 U3：一旦 UI 开始 new 它们，本清单必须同步，否则树里又裸奔类型全名。
        /// </summary>
        private static readonly string[] UiNeverInstantiates = { "BreakStep", "ContinueStep", "ReturnStep" };

        public static void Run()
        {
            Section("[U1-U5] 流程栏树模板覆盖（ProcessView.xaml 类型键模板）");

            var repoRoot = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\"));
            var xamlPath = Path.Combine(repoRoot, @"VisionMaster\Views\ProcessView.xaml");
            if (!File.Exists(xamlPath))
            {
                Check("U1：ProcessView.xaml 可读", false, xamlPath);
                return;
            }

            var xaml = File.ReadAllText(xamlPath);
            static string Key(string typeName) => "DataType=\"{x:Type models:" + typeName + "}\"";

            // ---- U1：事故回归（并行分组必须有模板）----
            Check("U1：ParallelStep 有 DataType 模板（事故回归：缺模板 → 类型全名上屏）",
                xaml.Contains(Key("ParallelStep")),
                @"VisionMaster\Views\ProcessView.xaml");

            // ---- U2：全量覆盖（反射枚举所有具体 StepModel 子类）----
            var concrete = typeof(StepModel).Assembly.GetTypes()
                .Where(t => t.IsPublic && !t.IsAbstract && typeof(StepModel).IsAssignableFrom(t))
                .Select(t => t.Name)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();
            var missing = concrete
                .Where(n => !UiNeverInstantiates.Contains(n) && !xaml.Contains(Key(n)))
                .ToList();
            Check($"U2：所有具体 StepModel 子类都有树模板（实查 {concrete.Count} 个类型）",
                missing.Count == 0,
                missing.Count == 0 ? string.Join(", ", concrete) : "缺模板: " + string.Join(", ", missing));

            // ---- U3：例外清单不过期（UI 仍未实例化 Break/Continue/Return 模型类）----
            var vmPath = Path.Combine(repoRoot, @"VisionMaster\ViewModels\ProcessViewModel.cs");
            var vm = File.Exists(vmPath) ? File.ReadAllText(vmPath) : "";
            var revived = UiNeverInstantiates.Where(n => vm.Contains("new " + n + "(")).ToList();
            Check("U3：例外清单未过期（UI 仍未实例化 Break/Continue/Return 模型类）",
                revived.Count == 0,
                revived.Count == 0 ? "ProcessViewModel 无这些类型的构造" : "已复活，须补模板: " + string.Join(", ", revived));

            // ---- U4：并行分组的模板必须是 HierarchicalDataTemplate + Children（否则分支不显示）----
            var keyPos = xaml.IndexOf(Key("ParallelStep"), StringComparison.Ordinal);
            var tagOpen = keyPos < 0 ? -1 : xaml.LastIndexOf('<', keyPos);
            var tagClose = keyPos < 0 ? -1 : xaml.IndexOf('>', keyPos);
            var openTag = tagOpen >= 0 && tagClose > tagOpen ? xaml.Substring(tagOpen, tagClose - tagOpen + 1) : "";
            var closed = tagClose > 0 && xaml.IndexOf("</HierarchicalDataTemplate>", tagClose, StringComparison.Ordinal) > tagClose;
            Check("U4：并行分组模板是 HierarchicalDataTemplate 且 ItemsSource=Children（分支可见）",
                openTag.Contains("HierarchicalDataTemplate") && openTag.Contains("ItemsSource=\"{Binding Children}\"") && closed,
                openTag.Length == 0 ? "模板缺失" : openTag);

            // ---- U5：分支胶囊模板在座（StepCollection，缺它容器里什么都看不到）----
            Check("U5：StepCollection 分支胶囊模板在座",
                xaml.Contains(Key("StepCollection")),
                "分支卡片样式 (StepCollection)");
        }
    }
}
