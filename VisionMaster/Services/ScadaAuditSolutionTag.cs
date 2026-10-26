using System;
using System.Text;

namespace VisionMaster.Services
{
    /// <summary>
    /// 审计说明列的方案标签。格式只有三种：
    /// <c>[sol=&lt;id 前 12 位&gt; &lt;方案名&gt;]</c> / <c>[sol=unsaved &lt;方案名&gt;]</c> / <c>[sol=none]</c>。
    ///
    /// <b>只在 <see cref="ScadaAuditWriter.BuildRow"/> 里加一次</b>：Web 那一路的说明已经自带
    /// <c>[web …]</c> 前缀，若在 Append 再加就会两条路径各加一次/各漏一次。
    /// 标签解析只剥行首一次，说明里自带的 <c>]</c> 不影响。
    /// </summary>
    public static class ScadaAuditSolutionTag
    {
        public const int IdLength = 12;

        public static string Build(Guid? id, bool identityPersisted, string? name)
        {
            if (id == null) return "[sol=none]";

            var cleanName = Clean(name);
            if (!identityPersisted)
                return string.IsNullOrEmpty(cleanName) ? "[sol=unsaved]" : "[sol=unsaved " + cleanName + "]";

            var idText = id.Value.ToString("N").Substring(0, IdLength);
            return string.IsNullOrEmpty(cleanName)
                ? "[sol=" + idText + "]"
                : "[sol=" + idText + " " + cleanName + "]";
        }

        /// <summary>名字里的方括号与换行会打断解析，统一换成下划线。</summary>
        public static string Clean(string? name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            var sb = new StringBuilder(name.Length);
            foreach (var c in name)
                sb.Append(c is '[' or ']' or '\r' or '\n' ? '_' : c);
            return sb.ToString();
        }

        public static bool TryParse(string? detail, out Guid? id, out bool unsaved,
                                    out string? name, out string remainder)
        {
            id = null;
            unsaved = false;
            name = null;
            remainder = detail ?? string.Empty;

            if (string.IsNullOrEmpty(detail) || !detail.StartsWith("[sol=", StringComparison.Ordinal))
                return false;

            var end = detail.IndexOf(']', 5);
            if (end < 0) return false;

            var payload = detail.Substring(5, end - 5);
            remainder = end + 1 < detail.Length && detail[end + 1] == ' '
                ? detail.Substring(end + 2)
                : detail.Substring(end + 1);

            if (string.Equals(payload, "none", StringComparison.Ordinal))
                return true;

            if (payload.StartsWith("unsaved", StringComparison.Ordinal))
            {
                unsaved = true;
                var rest = payload.Length > 7 ? payload.Substring(7).Trim() : string.Empty;
                name = rest.Length == 0 ? null : rest;
                return true;
            }

            var space = payload.IndexOf(' ');
            var idPart = space < 0 ? payload : payload.Substring(0, space);
            name = space < 0 ? null : payload.Substring(space + 1);
            if (name != null && name.Length == 0) name = null;

            if (idPart.Length != IdLength || !IsHex(idPart)) return false;

            // 审计文件只存 12 位前缀；后 20 位补 0 还原成一个可比较的 Guid。
            // 过滤时只比前 12 位（见 ScadaAuditRow.SolutionId 的说明）。
            var padded = idPart + new string('0', 32 - IdLength);
            if (!Guid.TryParseExact(padded, "N", out var parsed)) return false;
            id = parsed;
            return true;
        }

        private static bool IsHex(string text)
        {
            foreach (var c in text)
            {
                if (!Uri.IsHexDigit(c)) return false;
            }
            return true;
        }
    }
}
