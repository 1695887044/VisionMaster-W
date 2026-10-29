using System.Globalization;
using System.Text;

namespace VisionMaster.Scada
{
    /// <summary>
    /// 模板正文里的索引令牌：<c>{i}</c>、<c>{i+1}</c>、<c>{i:000}</c>。
    ///
    /// 为什么用 <c>{i}</c> 而不是裸 <c>[0]</c>：模板里"所有实例共用元素 0"是一个合法意图，
    /// 把裸 <c>[0]</c> 自动当令牌就再也表达不了它；显式令牌让"要不要按实例变"由模板作者决定。
    /// <c>{{</c> / <c>}}</c> 是字面花括号转义；不认识的 <c>{...}</c> 原样保留（不吞正文）。
    /// </summary>
    public static class ScadaTemplateTokens
    {
        /// <summary>
        /// 一份模板负载里有没有任何未转义的 <c>{i}</c>（图元名、属性文本、绑定/动画/动作的变量名、
        /// 动作文本/值、确认消息）。拖入路径据此决定"按参数化模板展开"还是"按旧模板原样物化"。
        /// </summary>
        public static bool ContainsIndexToken(ScadaClipboardPayload? payload)
        {
            if (payload == null || payload.IsEmpty)
                return false;

            foreach (var element in payload.Items)
            {
                if (element == null)
                    continue;

                if (ContainsIndexToken(element.Name))
                    return true;

                if (element.Properties != null)
                {
                    foreach (var value in element.Properties.Values)
                    {
                        if (ContainsIndexToken(value))
                            return true;
                    }
                }

                if (element.Bindings != null)
                {
                    foreach (var binding in element.Bindings)
                    {
                        if (binding != null && ContainsIndexToken(binding.VariableName))
                            return true;
                    }
                }

                if (element.Animations != null)
                {
                    foreach (var animation in element.Animations)
                    {
                        if (animation != null && ContainsIndexToken(animation.VariableName))
                            return true;
                    }
                }

                if (element.EventHooks != null)
                {
                    foreach (var hook in element.EventHooks)
                    {
                        if (hook == null)
                            continue;

                        if (ContainsIndexToken(hook.ConfirmationMessage))
                            return true;

                        if (hook.Actions == null)
                            continue;

                        foreach (var action in hook.Actions)
                        {
                            if (action == null)
                                continue;

                            if (ContainsIndexToken(action.Text)
                                || ContainsIndexToken(action.Value)
                                || ContainsIndexToken(action.VariableName))
                                return true;
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// 正文里未转义索引令牌的个数（<c>{{</c> / <c>}}</c> 不算）。
        ///
        /// 用途：一条变量引用里出现**两处以上** <c>{i}</c> 时，说明它很可能是旧版参数化器
        /// "全量替换"留下的产物（<c>Recipes[{i}].Ints[{i}]</c>），展开后会把成员下标也换成实例号。
        /// 这里只负责数数，判定与告警在展开器里做。
        /// </summary>
        public static int CountIndexTokens(string? text)
        {
            if (string.IsNullOrEmpty(text))
                return 0;

            var source = text!;
            int count = 0;

            for (int i = 0; i < source.Length; i++)
            {
                if (source[i] != '{')
                    continue;

                // {{ 是字面花括号转义，不算令牌
                if (i + 1 < source.Length && source[i + 1] == '{')
                {
                    i++;
                    continue;
                }

                int end = source.IndexOf('}', i + 1);

                if (end > i && TryFormat(source.Substring(i + 1, end - i - 1), 0, out _))
                {
                    count++;
                    i = end;
                }
            }

            return count;
        }

        /// <summary>正文里有没有未转义的索引令牌（用于"模板没参数化"的警告）</summary>
        public static bool ContainsIndexToken(string? text)
        {
            if (string.IsNullOrEmpty(text))
                return false;

            var source = text!;

            for (int i = 0; i < source.Length; i++)
            {
                if (source[i] != '{')
                    continue;

                // {{ 是字面花括号转义，不算令牌
                if (i + 1 < source.Length && source[i + 1] == '{')
                {
                    i++;
                    continue;
                }

                int end = source.IndexOf('}', i + 1);

                if (end > i && TryFormat(source.Substring(i + 1, end - i - 1), 0, out _))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 把正文里的索引令牌替换成 <paramref name="index"/> 对应的值。
        /// 数字一律用不变区域性格式化，避免同一份模板在不同区域设置下产出不同名字。
        /// </summary>
        public static string Substitute(string? text, int index)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            var source = text!;

            if (source.IndexOf('{') < 0)
                return source;

            var builder = new StringBuilder(source.Length + 8);

            for (int i = 0; i < source.Length; i++)
            {
                char c = source[i];

                if (c == '{' && i + 1 < source.Length && source[i + 1] == '{')
                {
                    builder.Append('{');
                    i++;
                    continue;
                }

                if (c == '}' && i + 1 < source.Length && source[i + 1] == '}')
                {
                    builder.Append('}');
                    i++;
                    continue;
                }

                if (c == '{')
                {
                    int end = source.IndexOf('}', i + 1);

                    if (end > i && TryFormat(source.Substring(i + 1, end - i - 1), index, out string formatted))
                    {
                        builder.Append(formatted);
                        i = end;
                        continue;
                    }
                }

                builder.Append(c);
            }

            return builder.ToString();
        }

        /// <summary>解析令牌体：<c>i</c> / <c>i+N</c> / <c>i-N</c> / <c>i:格式</c>；其余一律不认</summary>
        private static bool TryFormat(string body, int index, out string result)
        {
            result = string.Empty;

            if (body.Length == 0 || body[0] != 'i')
                return false;

            int position = 1;
            int offset = 0;
            string? format = null;

            if (position < body.Length && (body[position] == '+' || body[position] == '-'))
            {
                int sign = body[position] == '-' ? -1 : 1;
                int start = position + 1;
                position = start;

                while (position < body.Length && char.IsDigit(body[position]))
                    position++;

                if (position == start)
                    return false;

                if (!int.TryParse(body.AsSpan(start, position - start), NumberStyles.None,
                        CultureInfo.InvariantCulture, out int magnitude))
                    return false;

                offset = sign * magnitude;
            }

            if (position < body.Length && body[position] == ':')
            {
                format = body[(position + 1)..];
                position = body.Length;
            }

            if (position != body.Length)
                return false;

            int value = index + offset;

            try
            {
                result = format == null
                    ? value.ToString(CultureInfo.InvariantCulture)
                    : value.ToString(format, CultureInfo.InvariantCulture);
                return true;
            }
            catch (FormatException)
            {
                // 用户写了一个 .NET 不认识的格式串：当普通正文保留，不把整段模板炸掉
                return false;
            }
        }
    }
}
