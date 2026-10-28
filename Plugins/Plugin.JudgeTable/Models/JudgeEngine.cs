using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Plugin.JudgeTable.Models
{
    /// <summary>
    /// 一次判定的结果（纯值对象，便于断言逐字段核对）。
    /// </summary>
    public readonly record struct JudgeOutcome(bool IsOk, string ActualText, string Reason, string Formula);

    /// <summary>
    /// 判定内核：七种模式各一条纯函数，**不碰端口、不碰上下文、不写任何变量**。
    ///
    /// 这样切的理由：判定口径是本插件唯一"算错了也没人发现"的地方
    /// （判错只会表现成"结果不对"，流程照常往下跑），所以它必须能脱离流程被逐条对拍；
    /// 端口读写与运行时变量写入留在插件类里，内核只回答"这条规则过没过、为什么"。
    ///
    /// 失败一律**说清原因**，绝不静默返回 true：
    /// 期望没填、实际值没接、类型解析不了、正则写错，全部按"本规则不通过"处理。
    /// </summary>
    public static class JudgeEngine
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>集合项分隔符：英文逗号 / 中文逗号 / 顿号 / 分号</summary>
        private static readonly char[] SetSeparators = { ',', '，', '、', ';', '；' };

        /// <summary>正则匹配超时（防灾难性回溯把流程卡死）</summary>
        private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

        /// <summary>
        /// 判定一条规则。
        /// </summary>
        /// <param name="rule">规则（期望 / 选项 / 模式）</param>
        /// <param name="actual">实际值（输入端口取到的对象；null 表示没有值）</param>
        public static JudgeOutcome Evaluate(JudgeRule rule, object? actual)
        {
            if (rule == null) return new JudgeOutcome(false, string.Empty, "规则为空", string.Empty);

            try
            {
                return rule.Mode switch
                {
                    JudgeMode.Numeric => Numeric(rule, actual),
                    JudgeMode.Text => Text(rule, actual),
                    JudgeMode.Set => Set(rule, actual),
                    JudgeMode.Regex => RegexMatch(rule, actual),
                    JudgeMode.Boolean => Boolean(rule, actual),
                    JudgeMode.Count => Count(rule, actual),
                    JudgeMode.NotEmpty => NotEmpty(actual),
                    _ => new JudgeOutcome(false, FormatActual(actual), $"未知判定模式：{rule.Mode}", string.Empty),
                };
            }
            catch (Exception ex)
            {
                // 单条规则异常只判该条失败，不中断整表（调用方也不会被异常打断）
                return new JudgeOutcome(false, FormatActual(actual),
                    $"判定异常：{ex.GetType().Name}: {ex.Message}", string.Empty);
            }
        }

        // ==================================================================
        //  数值
        // ==================================================================

        private static JudgeOutcome Numeric(JudgeRule rule, object? actual)
        {
            string actualText = FormatActual(actual);
            if (actual == null)
                return new JudgeOutcome(false, actualText, "没有实际值", string.Empty);

            if (!TryToDouble(actual, out double value))
                return new JudgeOutcome(false, actualText, $"无法解析为数字：{actualText}", string.Empty);

            if (double.IsNaN(value) || double.IsInfinity(value))
                return new JudgeOutcome(false, actualText, "实际值不是有限数字（NaN / Inf）", string.Empty);

            if (!TryParseNumericSpec(rule, out NumericSpec spec, out string specError))
                return new JudgeOutcome(false, actualText, specError, string.Empty);

            return spec.Kind switch
            {
                NumericKind.Center => CompareCenter(value, spec, actualText),
                NumericKind.Range => CompareRange(value, spec, actualText),
                _ => CompareBound(value, spec, actualText),
            };
        }

        private static JudgeOutcome CompareCenter(double value, NumericSpec spec, string actualText)
        {
            double center = spec.Center;
            double tolerance = spec.Tolerance;
            double distance = Math.Abs(value - center);
            bool ok = distance <= tolerance + 1e-12;
            string formula = $"|{Num(value)} − {Num(center)}| = {Num(distance)} {(ok ? "≤" : ">")} {Num(tolerance)} → {(ok ? "OK" : "NG")}";
            string reason = ok
                ? $"在 [{Num(center - tolerance)}, {Num(center + tolerance)}] 内"
                : distance > tolerance
                    ? $"超出{(value > center ? "上限" : "下限")} {Num(distance - tolerance)}"
                    : "不在容差内";
            return new JudgeOutcome(ok, actualText, reason, formula);
        }

        private static JudgeOutcome CompareRange(double value, NumericSpec spec, string actualText)
        {
            bool ok = value >= spec.Min - 1e-12 && value <= spec.Max + 1e-12;
            string formula = $"{Num(spec.Min)} ≤ {Num(value)} ≤ {Num(spec.Max)} → {(ok ? "OK" : "NG")}";
            string reason = ok
                ? $"在 [{Num(spec.Min)}, {Num(spec.Max)}] 内"
                : value < spec.Min
                    ? $"小于下限 {Num(spec.Min - value)}"
                    : $"大于上限 {Num(value - spec.Max)}";
            return new JudgeOutcome(ok, actualText, reason, formula);
        }

        private static JudgeOutcome CompareBound(double value, NumericSpec spec, string actualText)
        {
            double bound = spec.Bound;
            bool ok = spec.Kind switch
            {
                NumericKind.AtLeast => value >= bound - 1e-12,
                NumericKind.AtMost => value <= bound + 1e-12,
                NumericKind.GreaterThan => value > bound,
                _ => value < bound,
            };
            string op = spec.Kind switch
            {
                NumericKind.AtLeast => "≥",
                NumericKind.AtMost => "≤",
                NumericKind.GreaterThan => ">",
                _ => "<",
            };
            string formula = $"{Num(value)} {op} {Num(bound)} → {(ok ? "OK" : "NG")}";
            string reason = ok ? $"满足 {op} {Num(bound)}" : $"不满足 {op} {Num(bound)}";
            return new JudgeOutcome(ok, actualText, reason, formula);
        }

        private enum NumericKind { Center, Range, AtLeast, AtMost, GreaterThan, LessThan }

        private sealed class NumericSpec
        {
            public NumericKind Kind;
            public double Center;
            public double Tolerance;
            public double Min;
            public double Max;
            public double Bound;
        }

        /// <summary>
        /// 解析期望值。支持 <c>a</c> / <c>a ± b</c> / <c>a ~ b</c> / <c>≥ a</c> / <c>&gt; a</c> / <c>≤ a</c> / <c>&lt; a</c>
        /// （也接受 <c>&gt;=</c> / <c>&lt;=</c> / <c>+-</c> / 全角 <c>～</c>）。
        /// </summary>
        private static bool TryParseNumericSpec(JudgeRule rule, out NumericSpec spec, out string error)
        {
            spec = new NumericSpec();
            error = string.Empty;

            string expected = (rule.Expected ?? string.Empty).Trim();
            if (expected.Length == 0)
            {
                error = "没有填写期望值";
                return false;
            }

            // a ± b（也接受 "+-"）
            int plusMinus = expected.IndexOf('±');
            if (plusMinus < 0) plusMinus = expected.IndexOf("+-", StringComparison.Ordinal);
            if (plusMinus >= 0)
            {
                string centerText = expected.Substring(0, plusMinus).Trim();
                int tolStart = plusMinus + (expected[plusMinus] == '±' ? 1 : 2);
                string toleranceText = expected.Substring(tolStart).Trim();
                if (!TryParseNumber(centerText, out double center))
                {
                    error = $"期望值里的中心值无法解析为数字：{centerText}";
                    return false;
                }
                if (!TryParseNumber(toleranceText, out double tolerance) || tolerance < 0)
                {
                    error = $"期望值里的容差无法解析为非负数字：{toleranceText}";
                    return false;
                }
                spec.Kind = NumericKind.Center;
                spec.Center = center;
                spec.Tolerance = tolerance;
                return true;
            }

            // a ~ b（含全角 ～）
            int tilde = expected.IndexOf('~');
            if (tilde < 0) tilde = expected.IndexOf('～');
            if (tilde >= 0)
            {
                string minText = expected.Substring(0, tilde).Trim();
                string maxText = expected.Substring(tilde + 1).Trim();
                if (!TryParseNumber(minText, out double min))
                {
                    error = $"期望值里的下限无法解析为数字：{minText}";
                    return false;
                }
                if (!TryParseNumber(maxText, out double max))
                {
                    error = $"期望值里的上限无法解析为数字：{maxText}";
                    return false;
                }
                spec.Kind = NumericKind.Range;
                spec.Min = Math.Min(min, max); // 写反了也按区间理解，不静默判错
                spec.Max = Math.Max(min, max);
                return true;
            }

            // 比较符
            var bound = MatchBound(expected);
            if (bound.HasValue)
            {
                spec.Kind = bound.Value.Kind;
                spec.Bound = bound.Value.Bound;
                return true;
            }

            // 纯中心值 + Tolerance 字段
            if (!TryParseNumber(expected, out double exact))
            {
                error = $"期望值无法解析为数字：{expected}";
                return false;
            }
            spec.Kind = NumericKind.Center;
            spec.Center = exact;
            spec.Tolerance = rule.Tolerance < 0 ? 0 : rule.Tolerance;
            return true;
        }

        private static (NumericKind Kind, double Bound)? MatchBound(string text)
        {
            string rest;
            NumericKind kind;

            if (text.StartsWith(">=", StringComparison.Ordinal)) { kind = NumericKind.AtLeast; rest = text.Substring(2); }
            else if (text.StartsWith("≥", StringComparison.Ordinal)) { kind = NumericKind.AtLeast; rest = text.Substring(1); }
            else if (text.StartsWith("<=", StringComparison.Ordinal)) { kind = NumericKind.AtMost; rest = text.Substring(2); }
            else if (text.StartsWith("≤", StringComparison.Ordinal)) { kind = NumericKind.AtMost; rest = text.Substring(1); }
            else if (text.StartsWith(">", StringComparison.Ordinal)) { kind = NumericKind.GreaterThan; rest = text.Substring(1); }
            else if (text.StartsWith("<", StringComparison.Ordinal)) { kind = NumericKind.LessThan; rest = text.Substring(1); }
            else return null;

            return TryParseNumber(rest.Trim(), out double bound) ? (kind, bound) : null;
        }

        // ==================================================================
        //  文本
        // ==================================================================

        private static JudgeOutcome Text(JudgeRule rule, object? actual)
        {
            string actualText = FormatActual(actual);
            if (actual == null) return new JudgeOutcome(false, actualText, "没有实际值", string.Empty);

            string expected = rule.Expected ?? string.Empty;
            if (expected.Length == 0) return new JudgeOutcome(false, actualText, "没有填写期望值", string.Empty);

            string left = actualText;
            string right = expected;

            var confusables = ParseConfusables(rule.Confusables);
            if (confusables != null)
            {
                left = NormalizeConfusables(left, confusables);
                right = NormalizeConfusables(right, confusables);
            }

            if (rule.IgnoreWhitespace)
            {
                left = RemoveWhitespace(left);
                right = RemoveWhitespace(right);
            }

            if (rule.IgnoreCase)
            {
                left = left.ToUpperInvariant();
                right = right.ToUpperInvariant();
            }

            int allowed = Math.Max(0, rule.MaxEditDistance);
            int distance = EditDistance(left, right);
            bool ok = distance <= allowed;

            string reason = ok
                ? (distance == 0 ? "文本一致" : $"编辑距离 {distance} ≤ 允许 {allowed}")
                : $"编辑距离 {distance} > 允许 {allowed}（期望「{expected}」，实际「{actualText}」）";
            string formula = allowed == 0
                ? $"{(ok ? "实际 = 期望" : "实际 ≠ 期望")} → {(ok ? "OK" : "NG")}"
                : $"编辑距离 {distance} {(ok ? "≤" : ">")} 允许 {allowed} → {(ok ? "OK" : "NG")}";
            return new JudgeOutcome(ok, actualText, reason, formula);
        }

        /// <summary>解析易混字符组（<c>0O,1I,5S</c>）：每组字符映射到组内第一个字符</summary>
        private static Dictionary<char, char>? ParseConfusables(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var map = new Dictionary<char, char>();
            foreach (var group in text.Split(SetSeparators, StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = group.Trim();
                if (trimmed.Length < 2) continue;
                char canonical = trimmed[0];
                foreach (char ch in trimmed)
                    map[ch] = canonical;
            }
            return map.Count == 0 ? null : map;
        }

        private static string NormalizeConfusables(string text, Dictionary<char, char> map)
        {
            var sb = new StringBuilder(text.Length);
            foreach (char ch in text)
                sb.Append(map.TryGetValue(ch, out char canonical) ? canonical : ch);
            return sb.ToString();
        }

        private static string RemoveWhitespace(string text)
        {
            var sb = new StringBuilder(text.Length);
            foreach (char ch in text)
                if (!char.IsWhiteSpace(ch)) sb.Append(ch);
            return sb.ToString();
        }

        /// <summary>
        /// 编辑距离（Levenshtein）。**按 Unicode 码点迭代**，不按 char：
        /// 按 char 切会把代理对（如部分表情/生僻字）算成两个字符，中文虽在 BMP 内不受影响，
        /// 但"距离比预期大 1"这类偏差极难在现场解释。码点口径与"一个字形算一个字符"一致。
        /// </summary>
        public static int EditDistance(string left, string right)
        {
            left ??= string.Empty;
            right ??= string.Empty;
            if (left.Length == 0) return CountRunes(right);
            if (right.Length == 0) return CountRunes(left);

            var a = left.EnumerateRunes().ToArray();
            var b = right.EnumerateRunes().ToArray();
            if (a.Length == 0) return b.Length;
            if (b.Length == 0) return a.Length;

            var previous = new int[b.Length + 1];
            var current = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++) previous[j] = j;

            for (int i = 1; i <= a.Length; i++)
            {
                current[0] = i;
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    current[j] = Math.Min(
                        Math.Min(current[j - 1] + 1, previous[j] + 1),
                        previous[j - 1] + cost);
                }
                (previous, current) = (current, previous);
            }
            return previous[b.Length];
        }

        private static int CountRunes(string text) => text.EnumerateRunes().Count();

        // ==================================================================
        //  集合
        // ==================================================================

        private static JudgeOutcome Set(JudgeRule rule, object? actual)
        {
            string actualText = FormatActual(actual);
            if (actual == null) return new JudgeOutcome(false, actualText, "没有实际值", string.Empty);

            var expected = SplitItems(rule.Expected);
            if (expected.Count == 0) return new JudgeOutcome(false, actualText, "没有填写期望值", string.Empty);

            var actualItems = ToItems(actual);
            var comparer = rule.IgnoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

            var expectedSet = new HashSet<string>(expected, comparer);
            var actualSet = new HashSet<string>(actualItems, comparer);

            var missing = expectedSet.Where(x => !actualSet.Contains(x)).ToList();
            var extra = actualSet.Where(x => !expectedSet.Contains(x)).ToList();

            bool ok;
            string reason;
            switch (rule.SetSemantics)
            {
                case SetSemantics.Exact:
                    ok = missing.Count == 0 && extra.Count == 0 && actualSet.Count == expectedSet.Count;
                    reason = ok
                        ? "完全一致"
                        : missing.Count > 0
                            ? $"缺少 {string.Join("、", missing)}"
                            : $"多出 {string.Join("、", extra)}";
                    break;

                case SetSemantics.NoExtra:
                    ok = extra.Count == 0;
                    reason = ok ? "没有多余项" : $"多出 {string.Join("、", extra)}";
                    break;

                default: // AllExpected
                    ok = missing.Count == 0;
                    reason = ok ? "全部期望项都在" : $"缺少 {string.Join("、", missing)}";
                    break;
            }

            string formula = $"期望 {expectedSet.Count} 项 / 实测 {actualSet.Count} 项 / "
                             + $"命中 {expectedSet.Count - missing.Count} 项 → {(ok ? "OK" : "NG")}";
            return new JudgeOutcome(ok, actualText, reason, formula);
        }

        /// <summary>按分隔符切项（去空白、去空项）</summary>
        private static List<string> SplitItems(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return new List<string>();
            return text
                .Split(SetSeparators, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => x.Length > 0)
                .ToList();
        }

        /// <summary>实际值转集合项：字符串按分隔符切；集合逐项 ToString；其它当单元素</summary>
        private static List<string> ToItems(object actual)
        {
            if (actual is string text) return SplitItems(text);
            if (actual is IEnumerable enumerable)
            {
                var items = new List<string>();
                foreach (var item in enumerable)
                    items.Add(FormatActual(item));
                return items;
            }
            return new List<string> { FormatActual(actual) };
        }

        // ==================================================================
        //  正则
        // ==================================================================

        private static JudgeOutcome RegexMatch(JudgeRule rule, object? actual)
        {
            string actualText = FormatActual(actual);
            if (actual == null) return new JudgeOutcome(false, actualText, "没有实际值", string.Empty);

            string pattern = rule.Expected ?? string.Empty;
            if (pattern.Length == 0) return new JudgeOutcome(false, actualText, "没有填写正则", string.Empty);

            Regex regex;
            try
            {
                regex = new Regex(pattern, RegexOptions.None, RegexTimeout);
            }
            catch (ArgumentException ex)
            {
                return new JudgeOutcome(false, actualText, $"正则非法：{ex.Message}", string.Empty);
            }

            try
            {
                bool ok = regex.IsMatch(actualText);
                return new JudgeOutcome(ok, actualText,
                    ok ? "正则匹配" : $"不匹配正则 {pattern}",
                    $"IsMatch(「{actualText}」, {pattern}) = {ok} → {(ok ? "OK" : "NG")}");
            }
            catch (RegexMatchTimeoutException)
            {
                return new JudgeOutcome(false, actualText,
                    $"正则匹配超时（模式可能有灾难性回溯）：{pattern}", string.Empty);
            }
        }

        // ==================================================================
        //  布尔
        // ==================================================================

        private static JudgeOutcome Boolean(JudgeRule rule, object? actual)
        {
            string actualText = FormatActual(actual);
            if (actual == null) return new JudgeOutcome(false, actualText, "没有实际值", string.Empty);

            bool? expected = ToBool(rule.Expected);
            if (expected == null)
                return new JudgeOutcome(false, actualText,
                    $"期望值无法解析为布尔（应填 true / false）：{rule.Expected}", string.Empty);

            bool? value = ToBool(actual);
            if (value == null)
                return new JudgeOutcome(false, actualText, $"实际值无法解析为布尔：{actualText}", string.Empty);

            bool ok = expected.Value == value.Value;
            return new JudgeOutcome(ok, actualText,
                ok ? $"布尔一致（{FormatBool(value.Value)}）" : $"期望 {FormatBool(expected.Value)}，实际 {FormatBool(value.Value)}",
                $"{FormatBool(value.Value)} = {FormatBool(expected.Value)} → {(ok ? "OK" : "NG")}");
        }

        private static string FormatBool(bool value) => value ? "true" : "false";

        private static bool? ToBool(object? value)
        {
            switch (value)
            {
                case null: return null;
                case bool b: return b;
                case string s:
                    var text = s.Trim();
                    if (text.Equals("true", StringComparison.OrdinalIgnoreCase)
                        || text.Equals("1", StringComparison.Ordinal)) return true;
                    if (text.Equals("false", StringComparison.OrdinalIgnoreCase)
                        || text.Equals("0", StringComparison.Ordinal)) return false;
                    return null;
                default:
                    if (IsNumeric(value))
                        return Math.Abs(Convert.ToDouble(value, Inv)) > double.Epsilon;
                    return null;
            }
        }

        // ==================================================================
        //  计数
        // ==================================================================

        private static JudgeOutcome Count(JudgeRule rule, object? actual)
        {
            string actualText = FormatActual(actual);
            if (actual == null) return new JudgeOutcome(false, actualText, "没有实际值", string.Empty);

            double count = CountOf(actual);
            if (!TryParseNumericSpec(rule, out NumericSpec spec, out string specError))
                return new JudgeOutcome(false, actualText, specError, string.Empty);

            switch (spec.Kind)
            {
                case NumericKind.Center:
                    {
                        bool ok = Math.Abs(count - spec.Center) <= spec.Tolerance + 1e-12;
                        return new JudgeOutcome(ok, actualText,
                            ok ? $"等于 {Num(spec.Center)}" : $"期望 {Num(spec.Center)}，实际 {Num(count)}",
                            $"{Num(count)} {(ok ? "=" : "≠")} {Num(spec.Center)} → {(ok ? "OK" : "NG")}");
                    }
                case NumericKind.Range:
                    {
                        bool ok = count >= spec.Min - 1e-12 && count <= spec.Max + 1e-12;
                        return new JudgeOutcome(ok, actualText,
                            ok ? "数量在范围内" : count < spec.Min ? "数量少于下限" : "数量多于上限",
                            $"{Num(count)} ∈ [{Num(spec.Min)}, {Num(spec.Max)}] → {(ok ? "OK" : "NG")}");
                    }
                default:
                    {
                        bool ok = spec.Kind switch
                        {
                            NumericKind.AtLeast => count >= spec.Bound - 1e-12,
                            NumericKind.AtMost => count <= spec.Bound + 1e-12,
                            NumericKind.GreaterThan => count > spec.Bound,
                            _ => count < spec.Bound,
                        };
                        string op = spec.Kind switch
                        {
                            NumericKind.AtLeast => "≥",
                            NumericKind.AtMost => "≤",
                            NumericKind.GreaterThan => ">",
                            _ => "<",
                        };
                        return new JudgeOutcome(ok, actualText,
                            ok ? $"满足 {op} {Num(spec.Bound)}" : $"不满足 {op} {Num(spec.Bound)}",
                            $"{Num(count)} {op} {Num(spec.Bound)} → {(ok ? "OK" : "NG")}");
                    }
            }
        }

        /// <summary>
        /// 实际值转"个数"：数值取自身；字符串按分隔符切项数（无分隔符算 1 项，空串算 0）；
        /// 集合取元素个数；其它算 1 项。
        /// </summary>
        private static double CountOf(object actual)
        {
            if (IsNumeric(actual)) return Convert.ToDouble(actual, Inv);
            if (actual is string text) return SplitItems(text).Count;
            if (actual is IEnumerable enumerable)
            {
                int n = 0;
                foreach (var _ in enumerable) n++;
                return n;
            }
            return 1;
        }

        // ==================================================================
        //  非空
        // ==================================================================

        private static JudgeOutcome NotEmpty(object? actual)
        {
            string actualText = FormatActual(actual);
            if (actual == null)
                return new JudgeOutcome(false, actualText, "实际值为空（null）", "null → NG");

            if (actual is string text)
            {
                bool ok = !string.IsNullOrWhiteSpace(text);
                return new JudgeOutcome(ok, actualText,
                    ok ? "文本非空白" : "实际值为空白文本",
                    ok ? "非空白 → OK" : "空白 → NG");
            }

            if (actual is IEnumerable enumerable)
            {
                bool any = false;
                foreach (var _ in enumerable) { any = true; break; }
                return new JudgeOutcome(any, actualText,
                    any ? "集合非空" : "实际值为空集合",
                    any ? "Count > 0 → OK" : "Count = 0 → NG");
            }

            return new JudgeOutcome(true, actualText, "非 null", "非 null → OK");
        }

        // ==================================================================
        //  公共小工具
        // ==================================================================

        /// <summary>实际值转显示文本（null 返回空串；集合按 <c>a, b, c</c>）</summary>
        public static string FormatActual(object? value)
        {
            switch (value)
            {
                case null: return string.Empty;
                case string s: return s;
                case bool b: return FormatBool(b);
                case double d: return Num(d);
                case float f: return Num(f);
                case decimal m: return m.ToString("0.####", Inv);
                case IEnumerable enumerable:
                    {
                        var items = new List<string>();
                        foreach (var item in enumerable) items.Add(FormatActual(item));
                        return string.Join(", ", items);
                    }
                default:
                    return IsNumeric(value)
                        ? Convert.ToDouble(value, Inv).ToString("0.####", Inv)
                        : value.ToString() ?? string.Empty;
            }
        }

        private static bool IsNumeric(object value) =>
            value is sbyte or byte or short or ushort or int or uint or long or ulong
                  or float or double or decimal;

        private static bool TryToDouble(object actual, out double value)
        {
            value = 0;
            if (actual is string text)
                return TryParseNumber(text, out value);
            if (!IsNumeric(actual)) return false;
            try
            {
                value = Convert.ToDouble(actual, Inv);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 数值文本解析：**不变区域性 + 拒绝千分位**。
        /// 用 <see cref="NumberStyles.Float"/> 而不是 <c>NumberStyles.Any</c>：
        /// 后者会把 "2,5" 读成 25（用户想写 2.5），静默改值比报错难查得多。
        /// </summary>
        private static bool TryParseNumber(string? text, out double value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            return double.TryParse(text.Trim(), NumberStyles.Float, Inv, out value);
        }

        /// <summary>数字显示：最多 4 位小数，整数不带小数点</summary>
        private static string Num(double value) => value.ToString("0.####", Inv);
    }
}
