using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using VisionMaster.Binding;
using VisionMaster.Models;
using VisionMaster.Scada;
using VisionMaster.Services;

namespace StructBindingProbe;

/// <summary>
/// 探针：PLC 导入出来的"结构体"（<c>Recipes[0].Title</c> / <c>Recipes[0].Ints[5]</c>）
/// 在 SCADA 模板体系里到底能走到哪一步。
///
/// 只回答"现状是什么"，不改产品代码。每个结论都对应一条可复现的调用：
///   P1/P2  单层成员路径能否参数化 + 展开成不同实例
///   P3/P4  嵌套下标：只换最外层，成员下标必须原样保留（2026-10-29 修复后的口径）
///   P5     结构体根（Recipes / Recipes[0]）本身不是变量
///   P6     模板实例的 TemplateStrictRoots 不拦绑定期绑定
///   P7     变量选择器没有作用域参数（结构事实，反射取证）
///
/// 退出码：0 = 全部结论与代码一致；1 = 有结论被推翻（说明读错了或修复回退了）。
/// </summary>
internal static class Program
{
    private static int _failed;

    private static int Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        SingleLevelMemberPath();
        NestedIndexIsSilentlyRewritten();
        StructRootIsNotAVariable();
        StrictRootsDoNotGuardBinding();
        PickerHasNoScope();

        Console.WriteLine();
        Console.WriteLine(_failed == 0 ? "RESULT: ALL_OK" : $"RESULT: {_failed} MISMATCH");
        return _failed == 0 ? 0 : 1;
    }

    // ------------------------------------------------------------------
    //  P1/P2：单层成员路径
    // ------------------------------------------------------------------

    private static void SingleLevelMemberPath()
    {
        Console.WriteLine("== P1/P2 单层成员路径 Recipes[0].Title ==");

        var payload = PayloadOf("Recipes[0].Title", -1);

        bool ok = ScadaTemplateParameterizer.TryParameterize(
            payload, 0, out int changed, out var roots, out string? error);

        Report("P1 参数化结果 = Recipes[{i}].Title",
            ok && payload.Items[0].Bindings[0].VariableName == "Recipes[{i}].Title",
            ok ? payload.Items[0].Bindings[0].VariableName : error ?? "参数化失败");

        Report("P1 记录的数组根 = Recipes（外层名，不含下标）",
            ok && roots.SequenceEqual(new[] { "Recipes" }),
            ok ? string.Join("/", roots) : "-");

        var resolver = new ScadaTemplateVariableResolver(RegistryOf(
            Local("Recipes[0].Title", typeof(string), "A"),
            Local("Recipes[1].Title", typeof(string), "B")));

        bool expanded = ScadaTemplateExpansion.TryPlan(
            payload,
            new ScadaTemplateExpansionOptions { Count = 2, StartIndex = 0 },
            resolver, out var plan, out var errors, out _);

        var binding = expanded ? plan!.Instances[1].Items[0].Bindings[0] : null;

        Report("P2 实例 1 绑到 Recipes[1].Title（独立变量名，ArrayIndex = -1）",
            expanded && binding!.VariableName == "Recipes[1].Title" && binding.ArrayIndex == -1,
            expanded ? $"{binding!.VariableName} / idx={binding.ArrayIndex}" : Describe(errors));

        Console.WriteLine();
    }

    // ------------------------------------------------------------------
    //  P3/P4：嵌套下标
    // ------------------------------------------------------------------

    private static void NestedIndexIsSilentlyRewritten()
    {
        Console.WriteLine("== P3/P4 嵌套下标 Recipes[0].Ints[0] ==");

        var payload = PayloadOf("Recipes[0].Ints[0]", -1);

        bool ok = ScadaTemplateParameterizer.TryParameterize(
            payload, 0, out _, out _, out string? error);

        string actual = ok ? payload.Items[0].Bindings[0].VariableName ?? "" : error ?? "参数化失败";

        Report("P3 ★只换最外层：Recipes[0].Ints[0] → Recipes[{i}].Ints[0]（成员下标不动）",
            ok && actual == "Recipes[{i}].Ints[0]",
            actual);

        var resolver = new ScadaTemplateVariableResolver(RegistryOf(
            Local("Recipes[0].Ints[0]", typeof(short), (short)0),
            Local("Recipes[1].Ints[0]", typeof(short), (short)10),
            Local("Recipes[1].Ints[1]", typeof(short), (short)11)));

        bool expanded = ScadaTemplateExpansion.TryPlan(
            payload,
            new ScadaTemplateExpansionOptions { Count = 2, StartIndex = 0 },
            resolver, out var plan, out var errors, out _);

        var name = expanded ? plan!.Instances[1].Items[0].Bindings[0].VariableName : null;

        Report("P4 ★后果：实例 1 绑到 Recipes[1].Ints[0]（不是 Ints[1]）",
            expanded && name == "Recipes[1].Ints[0]",
            expanded ? name! : Describe(errors));

        Console.WriteLine();
    }

    // ------------------------------------------------------------------
    //  P5：结构体根不是变量
    // ------------------------------------------------------------------

    private static void StructRootIsNotAVariable()
    {
        Console.WriteLine("== P5 结构体根能否作为绑定目标 ==");

        var registry = RegistryOf(
            Local("Recipes[0].Title", typeof(string), "A"),
            Local("Recipes[1].Title", typeof(string), "B"));

        bool byIndexName = registry.Resolve(Guid.Empty, "Recipes[0]") == null;
        bool byRootName = registry.Resolve(Guid.Empty, "Recipes") == null;

        Report("P5 ★Recipes[0] 解析不到（不是变量）", byIndexName, byIndexName ? "null" : "命中");
        Report("P5 ★Recipes 解析不到（不是变量）", byRootName, byRootName ? "null" : "命中");

        Console.WriteLine();
    }

    // ------------------------------------------------------------------
    //  P6：TemplateStrictRoots 不拦绑定
    // ------------------------------------------------------------------

    private static void StrictRootsDoNotGuardBinding()
    {
        Console.WriteLine("== P6 模板实例的严格根是否拦截绑定 ==");

        var document = new ScadaDocument();
        var page = new ScadaPage { Name = "探针页" };
        document.Pages.Add(page);
        page.AddLayer();
        ScadaEditHistory.Clear();

        var instance = ScadaClipboard.Materialize(
            page, PayloadOf("Recipes[0].Title", -1), 0, 0, "结构体实例", groupAll: true,
            templateId: Guid.NewGuid(), templateIndex: 0, templateRevision: 1,
            templateStrictRoots: new[] { "Recipes" });

        var element = instance[0];
        var outside = Local("Unrelated", typeof(int), 0);

        bool bound = page.TrySetBinding(
            element, "Value", outside.VariableId, outside.Name, -1, out string? error);

        Report("P6 ★严格根 = [Recipes] 的模板实例，仍能绑到根外变量 Unrelated",
            bound && element.Bindings.Any(b => b.VariableName == "Unrelated"),
            bound ? element.Bindings[^1].VariableName! : error ?? "被拒绝（与预期相反）");

        ScadaEditHistory.Clear();
        Console.WriteLine();
    }

    // ------------------------------------------------------------------
    //  P7：变量选择器没有作用域参数
    // ------------------------------------------------------------------

    private static void PickerHasNoScope()
    {
        Console.WriteLine("== P7 变量选择器 API 是否有作用域参数 ==");

        var pickerType = typeof(IScadaVariablePicker);
        var pick = pickerType.GetMethods().FirstOrDefault(m => m.Name == "Pick");

        if (pick == null)
        {
            Report("P7 找到 IScadaVariablePicker.Pick", false, "未找到 Pick 方法");
            Console.WriteLine();
            return;
        }

        var parameters = pick.GetParameters();
        bool hasScope = parameters.Any(p =>
            p.ParameterType == typeof(Func<IVariable, bool>)
            || p.ParameterType == typeof(Predicate<IVariable>)
            || p.Name!.Contains("scope", StringComparison.OrdinalIgnoreCase)
            || p.Name.Contains("filter", StringComparison.OrdinalIgnoreCase)
            || p.Name.Contains("predicate", StringComparison.OrdinalIgnoreCase));

        Report("P7 ★Pick 的参数里没有任何作用域 / 过滤参数",
            !hasScope,
            string.Join(", ", parameters.Select(p => $"{p.ParameterType.Name} {p.Name}")));

        Console.WriteLine();
    }

    // ------------------------------------------------------------------
    //  夹具
    // ------------------------------------------------------------------

    private static ScadaClipboardPayload PayloadOf(string variableName, int arrayIndex)
    {
        var payload = new ScadaClipboardPayload();
        var element = new ScadaElementSnapshot
        {
            TypeKey = "Hmi.Text",
            Name = "探针图元",
            Width = 100,
            Height = 30,
        };
        element.Bindings.Add(new ScadaBindingSnapshot
        {
            TargetProperty = "Value",
            VariableName = variableName,
            ArrayIndex = arrayIndex,
        });
        payload.Items.Add(element);
        return payload;
    }

    private static IVariable Local(string name, Type type, object? value)
        => VariableFactory.CreateLocal(name, type, string.Empty, value);

    private static VariableRegistry RegistryOf(params IVariable[] variables)
        => new(new ObservableCollection<IVariable>(variables));

    private static string Describe(IReadOnlyList<ScadaTemplateExpansionError> errors)
        => errors.Count == 0
            ? "(无错误)"
            : string.Join(" | ", errors.Take(3).Select(e => $"{e.ElementName}/{e.Reference}: {e.Reason}"));

    private static void Report(string title, bool ok, string detail)
    {
        if (!ok) _failed++;
        Console.WriteLine($"  [{(ok ? "OK" : "MISMATCH")}] {title}  ->  {detail}");
    }
}
