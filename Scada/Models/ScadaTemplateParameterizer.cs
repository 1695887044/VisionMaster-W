using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace VisionMaster.Scada
{
    /// <summary>一条查找/替换规则；<see cref="Replace"/> 通常含 <c>{i}</c>。</summary>
    public readonly record struct ScadaTemplateReplaceRule(string Find, string Replace);

    /// <summary>
    /// 把"已经配好的一组图元"参数化成模板：把绑定/动画/动作里的具体下标
    /// （<c>Control[0]</c>、数组根 + ArrayIndex 0、<c>Control_0</c>）改成 <c>{i}</c>。
    ///
    /// 为什么必须有这一步：真实选择器回传的是<b>具体变量名 + 数值下标</b>，
    /// 模板展开只替换正文里的 <c>{i}</c>；不做参数化，100 个实例会全部绑到同一个变量。
    /// 这一步是"存为模板"的作者入口，不是运行态行为。
    ///
    /// 只改变量引用，<b>不猜文字里的数字</b>：文字要按实例变，请在模板里直接写 <c>设备 {i}</c>。
    /// 猜"设备 0"里的 0 会误伤颜色、格式串、编号里的其它数字。
    /// </summary>
    public static class ScadaTemplateParameterizer
    {
        /// <summary>
        /// 把负载里所有"下标等于 <paramref name="index"/> 的变量引用"改成 <c>{i}</c>。
        /// 返回被改写的引用条数；一条都没改时返回 false 并给出原因。
        /// </summary>
        public static bool TryParameterize(
            ScadaClipboardPayload? payload,
            int index,
            out int changed,
            out string? error)
            => TryParameterize(payload, index, out changed, out _, out error);

        /// <summary>
        /// 同 <see cref="TryParameterize(ScadaClipboardPayload?,int,out int,out string?)"/>，
        /// 额外返回本次改写的数组根清单（<c>Ctl</c> / <c>Sta</c> / <c>ddd</c>）。
        /// 模板保存它，插入时才能让用户选“严格替换哪些数组”。
        /// </summary>
        public static bool TryParameterize(
            ScadaClipboardPayload? payload,
            int index,
            out int changed,
            out IReadOnlyList<string> arrayRoots,
            out string? error)
        {
            changed = 0;
            arrayRoots = Array.Empty<string>();
            error = null;

            if (payload == null || payload.IsEmpty)
            {
                error = "没有可参数化的图元";
                return false;
            }

            if (index < 0)
            {
                error = "参数化下标不能为负数";
                return false;
            }

            var roots = new List<string>();

            foreach (var element in payload.Items)
            {
                if (element == null)
                    continue;

                if (element.Bindings != null)
                {
                    foreach (var binding in element.Bindings)
                    {
                        if (binding == null)
                            continue;

                        if (TryParameterizeReference(binding.VariableName, binding.ArrayIndex, index,
                                out string? name, out int arrayIndex, out string? root))
                        {
                            binding.VariableName = name;
                            binding.ArrayIndex = arrayIndex;
                            // 名字已经变成 Control[{i}]：原来的 Id 指向"存模板时那一个元素"，
                            // 留着它只会在名字解析失败时把所有实例悄悄绑回那一个（见展开器的 Id 回退）。
                            binding.VariableId = Guid.Empty;
                            AddRoot(roots, root);
                            changed++;
                        }
                    }
                }

                if (element.Animations != null)
                {
                    foreach (var animation in element.Animations)
                    {
                        if (animation == null)
                            continue;

                        if (TryParameterizeReference(animation.VariableName, animation.ArrayIndex, index,
                                out string? name, out int arrayIndex, out string? root))
                        {
                            animation.VariableName = name;
                            animation.ArrayIndex = arrayIndex;
                            animation.VariableId = Guid.Empty;
                            AddRoot(roots, root);
                            changed++;
                        }
                    }
                }

                if (element.EventHooks != null)
                {
                    foreach (var hook in element.EventHooks)
                    {
                        if (hook == null || hook.Actions == null)
                            continue;

                        foreach (var action in hook.Actions)
                        {
                            if (action == null || action.Type != ScadaActionType.WriteVariable)
                                continue;

                            if (TryParameterizeReference(action.VariableName, action.ArrayIndex, index,
                                    out string? name, out int arrayIndex, out string? root))
                            {
                                action.VariableName = name;
                                action.ArrayIndex = arrayIndex;
                                action.VariableId = Guid.Empty;
                                AddRoot(roots, root);
                                changed++;
                            }
                        }
                    }
                }
            }

            if (changed == 0)
            {
                error = $"没有找到下标 {index} 的变量引用；模板仍按原样保存，批量插入时所有实例会绑同一组变量";
                return false;
            }

            arrayRoots = roots;
            return true;
        }

        private static void AddRoot(List<string> roots, string? root)
        {
            if (string.IsNullOrWhiteSpace(root))
                return;

            if (!roots.Contains(root, StringComparer.Ordinal))
                roots.Add(root);
        }

        /// <summary>
        /// 通用"查找/替换"参数化：把名称/变量名里的 <paramref name="nameFind"/> 和
        /// 文字里的 <paramref name="textFind"/> 替换成 <paramref name="replaceWith"/>
        /// （通常就是 <c>{i}</c>）。这是普通变量模板与死文字模板的入口。
        ///
        /// 名称替换范围：图元名、绑定/动画/写变量动作的 VariableName；
        /// 文字替换范围：属性袋字符串、动作 Text/Value、确认消息。
        /// 两个 find 都可以留空（表示不替换那一类），但至少要有一个非空。
        /// </summary>
        public static bool TryReplace(
            ScadaClipboardPayload? payload,
            IReadOnlyList<ScadaTemplateReplaceRule>? nameRules,
            IReadOnlyList<ScadaTemplateReplaceRule>? textRules,
            out int changed,
            out IReadOnlyList<string> changes,
            out string? error)
        {
            changed = 0;
            changes = Array.Empty<string>();
            error = null;

            if (payload == null || payload.IsEmpty)
            {
                error = "没有可参数化的图元";
                return false;
            }

            var effectiveNameRules = NormalizeRules(nameRules, "名称", out error);
            if (error != null)
                return false;

            var effectiveTextRules = NormalizeRules(textRules, "文字", out error);
            if (error != null)
                return false;

            if (effectiveNameRules.Count == 0 && effectiveTextRules.Count == 0)
            {
                error = "名称替换规则和文字替换规则至少填一条";
                return false;
            }

            var changedItems = new List<string>();

            foreach (var element in payload.Items)
            {
                if (element == null)
                    continue;

                if (effectiveNameRules.Count > 0)
                {
                    element.Name = ReplaceText(element.Name, effectiveNameRules,
                        $"图元名「{element.Name}」", changedItems, ref changed);
                }

                if (effectiveTextRules.Count > 0 && element.Properties != null)
                {
                    foreach (string key in element.Properties.Keys.ToList())
                    {
                        string value = element.Properties[key] ?? string.Empty;
                        element.Properties[key] = ReplaceText(value, effectiveTextRules,
                            $"属性 {key}", changedItems, ref changed);
                    }
                }

                if (element.Bindings != null)
                {
                    foreach (var binding in element.Bindings)
                    {
                        if (binding == null || effectiveNameRules.Count == 0)
                            continue;

                        string? previousName = binding.VariableName;
                        binding.VariableName = ReplaceText(binding.VariableName, effectiveNameRules,
                            $"绑定 {binding.TargetProperty}", changedItems, ref changed);

                        if (!string.Equals(previousName, binding.VariableName, StringComparison.Ordinal))
                            binding.VariableId = Guid.Empty;
                    }
                }

                if (element.Animations != null)
                {
                    foreach (var animation in element.Animations)
                    {
                        if (animation == null || effectiveNameRules.Count == 0)
                            continue;

                        string? previousName = animation.VariableName;
                        animation.VariableName = ReplaceText(animation.VariableName, effectiveNameRules,
                            $"动画 {animation.Type}", changedItems, ref changed);

                        if (!string.Equals(previousName, animation.VariableName, StringComparison.Ordinal))
                            animation.VariableId = Guid.Empty;
                    }
                }

                if (element.EventHooks == null)
                    continue;

                foreach (var hook in element.EventHooks)
                {
                    if (hook == null)
                        continue;

                    if (effectiveTextRules.Count > 0 && hook.ConfirmationMessage != null)
                    {
                        hook.ConfirmationMessage = ReplaceText(
                            hook.ConfirmationMessage, effectiveTextRules,
                            "确认消息", changedItems, ref changed);
                    }

                    if (hook.Actions == null)
                        continue;

                    foreach (var action in hook.Actions)
                    {
                        if (action == null)
                            continue;

                        if (effectiveNameRules.Count > 0)
                        {
                            string? previousName = action.VariableName;
                            action.VariableName = ReplaceText(action.VariableName, effectiveNameRules,
                                $"动作变量 {action.Type}", changedItems, ref changed);

                            if (!string.Equals(previousName, action.VariableName, StringComparison.Ordinal))
                                action.VariableId = Guid.Empty;
                        }

                        if (effectiveTextRules.Count > 0)
                        {
                            action.Text = ReplaceText(action.Text, effectiveTextRules,
                                $"动作文本 {action.Type}", changedItems, ref changed);
                            action.Value = ReplaceText(action.Value, effectiveTextRules,
                                $"动作值 {action.Type}", changedItems, ref changed);
                        }
                    }
                }
            }

            if (changed == 0)
            {
                error = "没有找到要替换的文本（检查名称/文字替换规则的查找串）";
                return false;
            }

            changes = changedItems;
            return true;
        }

        private static List<ScadaTemplateReplaceRule> NormalizeRules(
            IReadOnlyList<ScadaTemplateReplaceRule>? rules,
            string kind,
            out string? error)
        {
            error = null;
            var result = new List<ScadaTemplateReplaceRule>();

            if (rules == null)
                return result;

            foreach (var rule in rules)
            {
                string find = (rule.Find ?? string.Empty).Trim();

                if (find.Length == 0)
                    continue;

                string replace = string.IsNullOrWhiteSpace(rule.Replace)
                    ? "{i}"
                    : rule.Replace.Trim();

                if (string.Equals(find, replace, StringComparison.Ordinal))
                {
                    error = $"{kind}规则「{find}」的查找与替换相同，替换不会产生变化";
                    return result;
                }

                result.Add(new ScadaTemplateReplaceRule(find, replace));
            }

            return result;
        }

        private static string? ReplaceText(
            string? source,
            IReadOnlyList<ScadaTemplateReplaceRule> rules,
            string label,
            List<string> changes,
            ref int changed)
        {
            if (string.IsNullOrEmpty(source) || rules.Count == 0)
                return source;

            string next = source!;

            for (int i = 0; i < rules.Count; i++)
            {
                var rule = rules[i];
                next = next.Replace(rule.Find, rule.Replace, StringComparison.Ordinal);
            }

            if (string.Equals(next, source, StringComparison.Ordinal))
                return source;

            changed++;

            if (changes.Count < 20)
                changes.Add($"{label}：{source} → {next}");

            return next;
        }

        /// <summary>
        /// 单规则便捷入口（兼容旧调用）：名称/文字各最多一条，空查找表示不用那一类。
        /// </summary>
        public static bool TryReplace(
            ScadaClipboardPayload? payload,
            string? nameFind,
            string? textFind,
            string? nameReplace,
            string? textReplace,
            out int changed,
            out IReadOnlyList<string> changes,
            out string? error)
        {
            var nameRules = string.IsNullOrWhiteSpace(nameFind)
                ? null
                : new[] { new ScadaTemplateReplaceRule(nameFind!, nameReplace ?? "{i}") };

            var textRules = string.IsNullOrWhiteSpace(textFind)
                ? null
                : new[] { new ScadaTemplateReplaceRule(textFind!, textReplace ?? "{i}") };

            return TryReplace(payload, nameRules, textRules, out changed, out changes, out error);
        }

        /// <summary>
        /// 尝试把一条引用参数化：
        /// - 名字里任意位置含 <c>[index]</c>（<c>Cys[0].HpTime</c> / <c>Control[0]</c>）→ 全替换成 <c>[{i}]</c>；
        /// - 数组元素（ArrayIndex == index，名字是根名）→ 根名 + <c>[{i}]</c>，下标清 -1；
        /// 其它情况原样返回 false（不动用户配置）。
        /// </summary>
        private static bool TryParameterizeReference(
            string? variableName,
            int arrayIndex,
            int index,
            out string? newName,
            out int newArrayIndex,
            out string? arrayRoot)
        {
            newName = variableName;
            newArrayIndex = arrayIndex;
            arrayRoot = null;

            if (string.IsNullOrWhiteSpace(variableName))
                return false;

            string name = variableName!;
            string bracket = $"[{index.ToString(CultureInfo.InvariantCulture)}]";

            // 威纶通模板的常见形态：地址/标签名中间带索引（Cys[0].HpTime）。
            // 这里只替换名字，ArrayIndex 清 -1——展开后的名字自己携带下标。
            if ((arrayIndex < 0 || arrayIndex == index)
                && name.Contains(bracket, StringComparison.Ordinal))
            {
                int open = name.IndexOf('[');
                arrayRoot = open > 0 ? name[..open].Trim() : null;
                newName = name.Replace(bracket, "[{i}]", StringComparison.Ordinal);
                newArrayIndex = -1;
                return true;
            }

            if (arrayIndex == index)
            {
                // 正常数据是"根名 + ArrayIndex"；旧数据/手工编辑可能把下标也写进名字
                // （Control[0] + ArrayIndex 0），上面那支已经处理过，这里只处理纯根名。
                arrayRoot = name.Trim();
                newName = $"{name}[{{i}}]";
                newArrayIndex = -1;
                return true;
            }

            return false;
        }
    }
}
