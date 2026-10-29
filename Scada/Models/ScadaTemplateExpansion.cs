using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace VisionMaster.Scada
{
    /// <summary>
    /// 批量实例化的参数：生成几个、从几号开始、按几列排、间距与起始落点。
    /// </summary>
    public sealed class ScadaTemplateExpansionOptions
    {
        /// <summary>生成实例数（至少 1）</summary>
        public int Count { get; init; } = 1;

        /// <summary>第一个实例的索引（<c>{i}</c> 的起始值）</summary>
        public int StartIndex { get; init; }

        /// <summary>每行几个（至少 1）</summary>
        public int Columns { get; init; } = 1;

        /// <summary>同一行相邻实例的水平间距（设计像素）</summary>
        public double GapX { get; init; } = 20;

        /// <summary>相邻两行的垂直间距（设计像素）</summary>
        public double GapY { get; init; } = 20;

        /// <summary>整批内容的起始左上角 X</summary>
        public double OriginX { get; init; }

        /// <summary>整批内容的起始左上角 Y</summary>
        public double OriginY { get; init; }

        /// <summary>
        /// 严格替换：只把 <see cref="SelectedArrayRoots"/> 里的数组根换成实例序号，
        /// 其余数组根保留 <see cref="OriginalIndex"/>。
        /// </summary>
        public bool StrictArrayReplacement { get; init; }

        /// <summary>模板保存时记录的数组根清单；不在清单里的 <c>{i}</c> 按名称规则处理，不参与严格选择。</summary>
        public IReadOnlySet<string>? ArrayRoots { get; init; }

        /// <summary>严格模式下勾选要替换的数组根；null = 全部替换（等同非严格）。</summary>
        public IReadOnlySet<string>? SelectedArrayRoots { get; init; }

        /// <summary>模板保存时的原始序号；严格模式下未勾选的根回退到它。</summary>
        public int OriginalIndex { get; init; }
    }

    /// <summary>
    /// 模板里一条变量引用解析后的结果：根变量身份 + 元素下标 + 元素类型 + 可写性。
    /// </summary>
    public readonly record struct ScadaTemplateVariable(
        Guid VariableId,
        string VariableName,
        int ArrayIndex,
        Type DataType,
        bool IsWritable);

    /// <summary>
    /// 模板展开时的变量解析入口。
    ///
    /// 为什么放在 Scada 领域层、实现放在 VisionMaster：领域层负责"模板正文怎么变成实例"，
    /// 但它够不到变量注册表（依赖方向 VM.Core → VM.Scada）。把解析抽成接口，
    /// 领域层就能在断言里用假解析器逐条钉住替换规则，宿主再用真注册表接上。
    /// </summary>
    public interface IScadaTemplateVariableResolver
    {
        /// <summary>
        /// 解析一段引用文本：先按精确变量名找（支持"100 个独立变量，名字就叫 Control[0]"），
        /// 找不到再按 <c>Base[index]</c> 语法拆成"数组根 + 下标"。
        /// </summary>
        bool TryResolve(string referenceText, out ScadaTemplateVariable variable, out string? error);

        /// <summary>
        /// 按"根 Id/名字 + 下标"解析（模板里只有 Id、没有名字时的兜底）。
        /// </summary>
        bool TryResolve(Guid variableId, string? variableName, int arrayIndex,
                        out ScadaTemplateVariable variable, out string? error);
    }

    /// <summary>一条展开错误：哪个实例、哪个图元、哪段引用、为什么。</summary>
    public readonly record struct ScadaTemplateExpansionError(
        int InstanceIndex,
        string ElementName,
        string Reference,
        string Reason);

    /// <summary>展开计划：每个实例一份<b>已经改写并重新解析过</b>的负载。</summary>
    public sealed class ScadaTemplateExpansionPlan
    {
        public ScadaTemplateExpansionPlan(
            IReadOnlyList<ScadaClipboardPayload> instances,
            IReadOnlyList<string> warnings)
        {
            Instances = instances ?? Array.Empty<ScadaClipboardPayload>();
            Warnings = warnings ?? Array.Empty<string>();
        }

        public IReadOnlyList<ScadaClipboardPayload> Instances { get; }

        public IReadOnlyList<string> Warnings { get; }

        public int Count => Instances.Count;
    }

    /// <summary>
    /// 模板参数化展开：把一份模板负载复制成 N 份，按实例序号替换 <c>{i}</c> 令牌，
    /// 并在<b>当前工程</b>重新解析每一条变量引用（模板可能来自另一个方案，旧 Id 不可信）。
    ///
    /// 三条纪律：
    /// 1. <b>绝不改模板原文</b>：每份实例都从 JSON 深拷贝开始（模板库返回的是库内实例）。
    /// 2. <b>原子</b>：任意一条引用解析失败，整批不产出计划；错误按实例/图元/引用聚合。
    /// 3. <b>只替换"每个实例该不同"的内容</b>：图元名、属性袋文本、变量名、动作文本/值；
    ///    不碰 TypeKey、属性键、图层名、目标画面/流程/轴名——那些替换只会制造新歧义。
    /// </summary>
    public static class ScadaTemplateExpansion
    {
        public static bool TryPlan(
            ScadaClipboardPayload? template,
            ScadaTemplateExpansionOptions? options,
            IScadaTemplateVariableResolver? resolver,
            out ScadaTemplateExpansionPlan? plan,
            out IReadOnlyList<ScadaTemplateExpansionError> errors,
            out IReadOnlyList<string> warnings)
        {
            plan = null;
            errors = Array.Empty<ScadaTemplateExpansionError>();
            warnings = Array.Empty<string>();

            if (template == null || template.IsEmpty)
            {
                errors = new[] { new ScadaTemplateExpansionError(-1, string.Empty, string.Empty, "模板为空，没有可插入的图元") };
                return false;
            }

            if (resolver == null)
            {
                errors = new[] { new ScadaTemplateExpansionError(-1, string.Empty, string.Empty, "没有变量解析器，无法展开模板") };
                return false;
            }

            var settings = options ?? new ScadaTemplateExpansionOptions();

            if (settings.Count <= 0)
            {
                errors = new[] { new ScadaTemplateExpansionError(-1, string.Empty, string.Empty, "实例数量必须大于 0") };
                return false;
            }

            if (settings.Columns <= 0)
            {
                errors = new[] { new ScadaTemplateExpansionError(-1, string.Empty, string.Empty, "每行实例数必须大于 0") };
                return false;
            }

            if (settings.StartIndex < 0)
            {
                errors = new[] { new ScadaTemplateExpansionError(-1, string.Empty, string.Empty, "起始索引不能为负数") };
                return false;
            }

            if (settings.StartIndex > int.MaxValue - settings.Count + 1)
            {
                errors = new[] { new ScadaTemplateExpansionError(-1, string.Empty, string.Empty, "起始索引 + 实例数量超出整数范围") };
                return false;
            }

            var instances = new List<ScadaClipboardPayload>(settings.Count);
            var collectedErrors = new List<ScadaTemplateExpansionError>();
            bool tokenSeen = false;

            // 按引用逐条记账：只要有一条绑定/动画/写变量动作没有 {i}，就说明"这个引用不会随实例变"。
            // 只做全局 tokenSeen 不够——文字里写了 {i} 会把"变量仍绑同一个"的警告掩盖掉。
            var unparameterizedRefs = new HashSet<string>(StringComparer.Ordinal);

            // 一条引用里出现两处以上 {i}：多半是旧版参数化器"全量替换"留下的产物
            // （Recipes[{i}].Ints[{i}]）。展开后成员下标也会跟着实例号走，静默绑错成员——
            // 这里不改模板，只把它列出来让用户看见（改模板是作者的决定）。
            var multiTokenRefs = new HashSet<string>(StringComparer.Ordinal);

            void NoteReference(string? reference)
            {
                if (reference != null && ScadaTemplateTokens.CountIndexTokens(reference) > 1)
                    multiTokenRefs.Add(reference);
            }

            for (int offset = 0; offset < settings.Count; offset++)
            {
                int number = settings.StartIndex + offset;

                ScadaClipboardPayload instance;

                try
                {
                    instance = Clone(template);
                    instance.Items ??= new();
                }
                catch (Exception ex)
                {
                    collectedErrors.Add(new ScadaTemplateExpansionError(
                        number, string.Empty, string.Empty, $"模板深拷贝失败：{ex.Message}"));
                    continue;
                }

                foreach (var element in instance.Items)
                {
                    if (element == null)
                        continue;

                    // 模板文件是可按文档手工编辑的：显式 null 子集合反序列化后就是 null。
                    // 这里补齐而不是抛 NRE——批量对话框跑在 UI 线程，未捕获异常会直接退进程。
                    element.Properties ??= new();
                    element.Bindings ??= new();
                    element.Animations ??= new();
                    element.EventHooks ??= new();

                    if (ScadaTemplateTokens.ContainsIndexToken(element.Name))
                        tokenSeen = true;

                    element.Name = ScadaTemplateTokens.Substitute(element.Name, number);

                    foreach (string key in element.Properties.Keys.ToList())
                    {
                        string value = element.Properties[key] ?? string.Empty;

                        if (ScadaTemplateTokens.ContainsIndexToken(value))
                            tokenSeen = true;

                        element.Properties[key] = ScadaTemplateTokens.Substitute(value, number);
                    }

                    foreach (var binding in element.Bindings)
                    {
                        if (binding == null)
                            continue;

                        if (ScadaTemplateTokens.ContainsIndexToken(binding.VariableName))
                            tokenSeen = true;
                        else if (binding.IsEnabled && settings.Count > 1)
                            unparameterizedRefs.Add(
                                new ScadaVariableRef(binding.VariableId, binding.VariableName, binding.ArrayIndex).DisplayText);

                        NoteReference(binding.VariableName);

                        int referenceNumber = ReferenceNumber(
                            settings, binding.VariableName, binding.ArrayIndex, number);

                        // 停用绑定不阻断整批，但仍尽量把引用落成"以后启用就能用"的形态：
                        // 能解析就按 A（独立变量）/ B（数组根+下标）落好；解析不到才只替换文本。
                        if (!binding.IsEnabled)
                        {
                            if (TryResolveReference(
                                    binding.VariableId, binding.VariableName, binding.ArrayIndex,
                                    referenceNumber, resolver,
                                    out var disabledResolved, out _))
                            {
                                binding.VariableId = disabledResolved.VariableId;
                                binding.VariableName = disabledResolved.VariableName;
                                binding.ArrayIndex = disabledResolved.ArrayIndex;
                            }
                            else if (binding.VariableName != null)
                            {
                                binding.VariableName = ScadaTemplateTokens.Substitute(binding.VariableName, referenceNumber);
                            }

                            continue;
                        }

                        if (TryResolveReference(
                                binding.VariableId, binding.VariableName, binding.ArrayIndex,
                                referenceNumber, resolver,
                                out var resolved, out string? error))
                        {
                            binding.VariableId = resolved.VariableId;
                            binding.VariableName = resolved.VariableName;
                            binding.ArrayIndex = resolved.ArrayIndex;
                        }
                        else
                        {
                            collectedErrors.Add(new ScadaTemplateExpansionError(
                                number, element.Name,
                                new ScadaVariableRef(binding.VariableId, binding.VariableName, binding.ArrayIndex).DisplayText,
                                error ?? "变量解析失败"));
                        }
                    }

                    foreach (var animation in element.Animations)
                    {
                        if (animation == null || !HasVariable(animation.VariableId, animation.VariableName))
                            continue;

                        animation.States ??= new();

                        if (ScadaTemplateTokens.ContainsIndexToken(animation.VariableName))
                            tokenSeen = true;
                        else if (animation.IsEnabled && settings.Count > 1)
                            unparameterizedRefs.Add(
                                new ScadaVariableRef(animation.VariableId, animation.VariableName, animation.ArrayIndex).DisplayText);

                        NoteReference(animation.VariableName);

                        int referenceNumber = ReferenceNumber(
                            settings, animation.VariableName, animation.ArrayIndex, number);

                        // 停用动画同样"能解析就落好、解析不到只替换文本"（理由同停用绑定）
                        if (!animation.IsEnabled)
                        {
                            if (TryResolveReference(
                                    animation.VariableId, animation.VariableName, animation.ArrayIndex,
                                    referenceNumber, resolver,
                                    out var disabledResolved, out _))
                            {
                                animation.VariableId = disabledResolved.VariableId;
                                animation.VariableName = disabledResolved.VariableName;
                                animation.ArrayIndex = disabledResolved.ArrayIndex;
                            }
                            else if (animation.VariableName != null)
                            {
                                animation.VariableName = ScadaTemplateTokens.Substitute(animation.VariableName, referenceNumber);
                            }

                            continue;
                        }

                        if (TryResolveReference(
                                animation.VariableId, animation.VariableName, animation.ArrayIndex,
                                referenceNumber, resolver,
                                out var resolved, out string? error))
                        {
                            animation.VariableId = resolved.VariableId;
                            animation.VariableName = resolved.VariableName;
                            animation.ArrayIndex = resolved.ArrayIndex;
                        }
                        else
                        {
                            collectedErrors.Add(new ScadaTemplateExpansionError(
                                number, element.Name,
                                new ScadaVariableRef(animation.VariableId, animation.VariableName, animation.ArrayIndex).DisplayText,
                                error ?? "动画变量解析失败"));
                        }
                    }

                    foreach (var hook in element.EventHooks)
                    {
                        if (hook == null)
                            continue;

                        hook.Actions ??= new();

                        if (ScadaTemplateTokens.ContainsIndexToken(hook.ConfirmationMessage))
                            tokenSeen = true;

                        if (hook.ConfirmationMessage != null)
                            hook.ConfirmationMessage = ScadaTemplateTokens.Substitute(hook.ConfirmationMessage, number);

                        foreach (var action in hook.Actions)
                        {
                            if (action == null)
                                continue;

                            if (ScadaTemplateTokens.ContainsIndexToken(action.Text)
                                || ScadaTemplateTokens.ContainsIndexToken(action.Value)
                                || ScadaTemplateTokens.ContainsIndexToken(action.VariableName))
                                tokenSeen = true;

                            if (action.Type == ScadaActionType.WriteVariable
                                && settings.Count > 1
                                && !ScadaTemplateTokens.ContainsIndexToken(action.VariableName))
                            {
                                unparameterizedRefs.Add(
                                    new ScadaVariableRef(action.VariableId, action.VariableName, action.ArrayIndex).DisplayText);
                            }

                            if (action.Text != null)
                                action.Text = ScadaTemplateTokens.Substitute(action.Text, number);

                            if (action.Value != null)
                                action.Value = ScadaTemplateTokens.Substitute(action.Value, number);

                            // 只有写变量动作需要解析目标变量；其它动作的 VariableName 没有语义。
                            if (action.Type != ScadaActionType.WriteVariable)
                                continue;

                            NoteReference(action.VariableName);

                            int referenceNumber = ReferenceNumber(
                                settings, action.VariableName, action.ArrayIndex, number);

                            if (!TryResolveReference(
                                    action.VariableId, action.VariableName, action.ArrayIndex,
                                    referenceNumber, resolver,
                                    out var resolved, out string? error))
                            {
                                collectedErrors.Add(new ScadaTemplateExpansionError(
                                    number, element.Name,
                                    new ScadaVariableRef(action.VariableId, action.VariableName, action.ArrayIndex).DisplayText,
                                    error ?? "写变量动作的目标变量解析失败"));
                                continue;
                            }

                            if (!resolved.IsWritable)
                            {
                                collectedErrors.Add(new ScadaTemplateExpansionError(
                                    number, element.Name,
                                    new ScadaVariableRef(resolved.VariableId, resolved.VariableName, resolved.ArrayIndex).DisplayText,
                                    "目标变量不可写，写变量动作无法展开"));
                                continue;
                            }

                            action.VariableId = resolved.VariableId;
                            action.VariableName = resolved.VariableName;
                            action.ArrayIndex = resolved.ArrayIndex;
                        }
                    }
                }

                instances.Add(instance);
            }

            if (collectedErrors.Count > 0)
            {
                errors = collectedErrors;
                return false;
            }

            var warningList = new List<string>();

            if (settings.Count > 1 && unparameterizedRefs.Count > 0)
            {
                string refs = string.Join("、", unparameterizedRefs.Take(8));
                string more = unparameterizedRefs.Count > 8
                    ? $" 等 {unparameterizedRefs.Count} 条"
                    : string.Empty;

                warningList.Add(
                    $"以下变量引用没有 {{i}}，所有实例会绑同一组变量：{refs}{more}。"
                    + "如需按实例切换，请把下标写成 {i}。");
            }
            else if (settings.Count > 1 && !tokenSeen)
            {
                warningList.Add(
                    "模板正文里没有 {i} 令牌：所有实例会显示同一段文字；"
                    + "如果这是有意的（例如多个实例共用一份数据），可以忽略。");
            }

            if (multiTokenRefs.Count > 0)
            {
                string refs = string.Join("、", multiTokenRefs.Take(8));
                string more = multiTokenRefs.Count > 8 ? $" 等 {multiTokenRefs.Count} 条" : string.Empty;

                warningList.Add(
                    $"以下引用里有不止一处 {{i}}：{refs}{more}。"
                    + "其中若有一处其实是结构体成员下标（例如 Recipes[{i}].Ints[{i}] 里的 Ints），"
                    + "展开后它会跟着实例号一起变，可能绑到错成员；请把成员下标写回固定值。");
            }

            plan = new ScadaTemplateExpansionPlan(instances, warningList);
            warnings = warningList;
            return true;
        }

        /// <summary>
        /// 解析一条引用：优先用模板里写的名字（已做令牌替换）；名字为空时退回 Id+下标。
        /// </summary>
        /// <summary>
        /// 严格模式下这条引用该用哪个序号：勾选的根用实例序号，未勾选的根回退到模板原始序号。
        /// 名字里没有数组根（名称替换规则产生的 <c>Cylinder{i}</c>）一律用实例序号。
        /// </summary>
        private static int ReferenceNumber(
            ScadaTemplateExpansionOptions settings,
            string? variableName,
            int arrayIndex,
            int number)
        {
            if (!settings.StrictArrayReplacement)
                return number;

            if (settings.ArrayRoots is not { Count: > 0 } roots)
                return number;

            string? root = ExtractArrayRoot(variableName, arrayIndex);

            if (root == null || !roots.Contains(root))
                return number;

            if (settings.SelectedArrayRoots is { } selected && !selected.Contains(root))
                return settings.OriginalIndex;

            return number;
        }

        private static string? ExtractArrayRoot(string? variableName, int arrayIndex)
        {
            if (string.IsNullOrWhiteSpace(variableName))
                return null;

            int open = variableName.IndexOf('[');

            if (open > 0)
                return variableName[..open].Trim();

            return arrayIndex >= 0 ? variableName.Trim() : null;
        }

        private static bool TryResolveReference(
            Guid variableId,
            string? variableName,
            int arrayIndex,
            int number,
            IScadaTemplateVariableResolver resolver,
            out ScadaTemplateVariable resolved,
            out string? error)
        {
            resolved = default;
            error = null;

            if (!string.IsNullOrWhiteSpace(variableName))
            {
                // 模板里若已经记了元素下标（例如绑定是 Control + ArrayIndex 3），
                // 先拼成 Control[3] 再替换令牌；这样 Control[{i}] 与 Control[0] 两种写法都成立。
                string reference = arrayIndex >= 0
                    ? $"{variableName}[{arrayIndex}]"
                    : variableName!;

                reference = ScadaTemplateTokens.Substitute(reference, number);

                if (resolver.TryResolve(reference, out resolved, out error))
                    return true;

                // 未参数化的模板可能是在变量改名之前存的：名字对不上、但 Id 仍然有效时，
                // 按 Id 再解析一次。带 {i} 的模板不做这个回退——快照里的 Id 是"存模板时那一个
                // 具体变量"，拿它回退会把所有实例都绑回那一个变量（比报错更糟）。
                if (!ScadaTemplateTokens.ContainsIndexToken(variableName)
                    && variableId != Guid.Empty
                    && resolver.TryResolve(variableId, variableName, arrayIndex, out resolved, out _))
                    return true;

                return false;
            }

            if (variableId == Guid.Empty)
            {
                error = "模板里的变量引用为空";
                return false;
            }

            return resolver.TryResolve(variableId, variableName, arrayIndex, out resolved, out error);
        }

        private static bool HasVariable(Guid id, string? name)
            => id != Guid.Empty || !string.IsNullOrWhiteSpace(name);

        /// <summary>
        /// 深拷贝：负载是纯数据快照（无 $type），JSON 往返能自动覆盖将来新增的字段，
        /// 不必手写一份容易漏字段的拷贝器。模板库返回的是库内实例，绝不能就地改。
        /// </summary>
        private static ScadaClipboardPayload Clone(ScadaClipboardPayload template)
        {
            string json = JsonConvert.SerializeObject(template);

            return JsonConvert.DeserializeObject<ScadaClipboardPayload>(json)
                   ?? throw new InvalidOperationException("模板负载反序列化后为空");
        }
    }
}
