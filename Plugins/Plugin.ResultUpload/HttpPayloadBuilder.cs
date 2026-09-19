using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Plugin.ResultUpload
{
    /// <summary>组报文结果：Error 非 null = 组报文失败（值池空、模板空、占位符炸了）。</summary>
    internal sealed class PayloadResult
    {
        public string Json;
        public string Error;
        /// <summary>通知文本展开结果（诊断用：钉钉/企微 content 实际发出的内容）。</summary>
        public string Content;
    }

    /// <summary>
    /// 报文组拼器：四种预设 → JSON 文本。纯内存、无 IO，预览与真实发送共用同一套逻辑，
    /// 保证"配置面板看到的报文"和"服务器收到的报文"永远一致。
    ///
    /// 类型保真纪律：端口值是 double 就写 12.3 而不是 "12.3"——MES 数值型字段校验靠它。
    /// </summary>
    internal static class HttpPayloadBuilder
    {
        /// <summary>{字段名} 文本占位（展开成字符串，找不到原样保留）。</summary>
        private static readonly Regex TextPlaceholder = new Regex(@"\{([^{}#]+)\}", RegexOptions.Compiled);
        /// <summary>{#字段名} 原样占位（数值/布尔/JSON片段不带引号直接进 JSON——用户明示才用）。</summary>
        private static readonly Regex RawPlaceholder = new Regex(@"\{#([^{}]+)\}", RegexOptions.Compiled);

        /// <summary>
        /// 中文不转义：默认序列化把中文打成 \u4EF6，钉钉/企微看不懂，很多 MES 还按原文匹配。
        /// UnsafeRelaxed 只是"不转义非 ASCII 与 HTML 敏感字符"，字符串内容照样合法转义。
        /// </summary>
        private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        /// <summary>按预设组报文。values 的键 = 字段名（大小写不敏感）。</summary>
        public static PayloadResult Build(PayloadKind kind, IReadOnlyList<UploadFieldDef> fields,
            IReadOnlyDictionary<string, object> values, string messageTemplate, string rawJsonTemplate,
            DateTime now, string stepName)
        {
            try
            {
                switch (kind)
                {
                    case PayloadKind.FieldTable:
                        return BuildFieldTable(fields, values, now);

                    case PayloadKind.DingTalkText:
                    case PayloadKind.WeComText:
                    {
                        // 钉钉与企业微信的群机器人文本报文同构：{"msgtype":"text","text":{"content":"…"}}
                        var text = ExpandText(messageTemplate ?? "", values, now, stepName);
                        var obj = new JsonObject
                        {
                            ["msgtype"] = "text",
                            ["text"] = new JsonObject { ["content"] = text }
                        };
                        return new PayloadResult { Json = obj.ToJsonString(JsonOpts), Content = text };
                    }

                    case PayloadKind.RawJson:
                    {
                        if (string.IsNullOrWhiteSpace(rawJsonTemplate))
                            return new PayloadResult { Error = "原始 JSON 模板为空" };
                        return new PayloadResult { Json = ExpandRawJson(rawJsonTemplate, values, now, stepName) };
                    }

                    default:
                        return new PayloadResult { Error = "未知报文预设：" + kind };
                }
            }
            catch (Exception ex)
            {
                return new PayloadResult { Error = "组报文异常：" + ex.Message };
            }
        }

        #region MES 字段表

        private static PayloadResult BuildFieldTable(IReadOnlyList<UploadFieldDef> fields,
            IReadOnlyDictionary<string, object> values, DateTime now)
        {
            var obj = new JsonObject();
            int count = 0;
            foreach (var f in fields ?? Array.Empty<UploadFieldDef>())
            {
                if (f == null || !f.Enabled || string.IsNullOrWhiteSpace(f.Name)) continue;
                object v = values != null && values.TryGetValue(f.Name.Trim(), out var got) ? got : null;
                obj[f.Name.Trim()] = ToJsonNode(v, now);
                count++;
            }
            if (count == 0)
                return new PayloadResult { Error = "字段表没有启用的字段，至少要有一个字段才有东西可上报" };
            return new PayloadResult { Json = obj.ToJsonString(JsonOpts) };
        }

        /// <summary>值 → JSON 节点（类型保真；认不出的类型（如 HImage）字符串占位，好过炸）。</summary>
        private static JsonNode ToJsonNode(object v, DateTime now)
        {
            switch (v)
            {
                case null: return null;
                case bool b: return b;
                case string s: return s;
                case DateTime dt: return dt.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
                case double d: return d;
                case float f: return f;
                case decimal m: return m;
                case long l: return l;
                case int i: return i;
                case sbyte or byte or short or ushort or uint or ulong:
                    return Convert.ToDouble(v, CultureInfo.InvariantCulture);
                default: return v.ToString();
            }
        }

        #endregion

        #region 占位符展开

        /// <summary>通知文本展开：{字段名} / {时间} / {日期} / {步骤名} → 字符串；找不到的字段原样保留（用户一眼看出写错名）。</summary>
        public static string ExpandText(string template, IReadOnlyDictionary<string, object> values,
            DateTime now, string stepName)
        {
            return TextPlaceholder.Replace(template ?? "", m =>
            {
                var resolved = Lookup(m.Groups[1].Value.Trim(), values, now, stepName);
                return resolved ?? m.Value;
            });
        }

        /// <summary>
        /// 原始 JSON 模板展开：
        /// {#名} = 原样注入（数字/布尔/已序列化的 JSON 片段；字符串自己带引号），
        /// {名}  = 按 JSON 字符串转义后注入（等价于写值本身）。
        /// </summary>
        private static string ExpandRawJson(string template, IReadOnlyDictionary<string, object> values,
            DateTime now, string stepName)
        {
            // 先处理 {#名}（原样注入）——它不会被 {名} 的正则误伤（第二个字符是 #）
            var s = RawPlaceholder.Replace(template, m =>
                Lookup(m.Groups[1].Value.Trim(), values, now, stepName) ?? m.Value);
            // 再处理 {名}（JSON 字符串转义注入）
            return TextPlaceholder.Replace(s, m =>
            {
                var resolved = Lookup(m.Groups[1].Value.Trim(), values, now, stepName);
                return resolved == null ? m.Value : System.Text.Json.JsonEncodedText.Encode(resolved).Value;
            });
        }

        /// <summary>占位符统一取值：内置优先，再查值池；没找到返回 null（调用方决定保留原样还是报错）。</summary>
        private static string Lookup(string key, IReadOnlyDictionary<string, object> values,
            DateTime now, string stepName)
        {
            switch (key)
            {
                case "时间": return now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
                case "日期": return now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                case "时间戳": return now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                case "步骤名":
                case "StepName": return stepName ?? "";
            }
            if (values != null && values.TryGetValue(key, out var v))
                return ToText(v);
            return null;
        }

        /// <summary>值 → 文本（数值一律 InvariantCulture，防德语区逗号当小数点）。</summary>
        private static string ToText(object v)
        {
            switch (v)
            {
                case null: return "";
                case string s: return s;
                case DateTime dt: return dt.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
                case IFormattable f: return f.ToString(null, CultureInfo.InvariantCulture);
                default: return v.ToString();
            }
        }

        #endregion
    }
}
