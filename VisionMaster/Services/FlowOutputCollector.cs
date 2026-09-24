using Core.Interfaces;
using HalconDotNet;
using System;
using System.Collections.Generic;
using System.Globalization;
using VisionMaster.Models;

namespace VisionMaster.Services
{
    /// <summary>
    /// 流程输出采集器：把"步骤名.端口名"翻译成编译会话里那个插件实例的端口值，
    /// 并整理成可直接交给 JSON 序列化器的字典。
    ///
    /// 为什么按「步骤名 → StepID → PluginLookup」绕一圈，而不是按 InstanceName 直接找插件
    /// ---------
    /// 插件实例的 InstanceName 有两种口径：单流程编译时是 <c>步骤名</c>，
    /// 带流程名编译时是 <c>流程名.步骤名</c>（见 FlowCompiler）。HTTP 侧补编译时传了流程名，
    /// 于是客户端只能写"流程名.步骤名.端口名"才能命中——而客户端只知道流程名（它就在 URL 里），
    /// 让它在输出选择器里再写一遍流程名纯属多余。
    /// StepID 是唯一且与命名口径无关的身份：先在 Blueprints（编译时铺平的步骤清单，
    /// 含 If/For 容器内的嵌套步骤）里按 StepName 找到 StepID，再用它去 PluginLookup 取插件，
    /// 这样客户端只需要写"步骤名.端口名"。
    ///
    /// 为什么 HImage 不直接回传像素
    /// ---------
    /// 一张 500 万像素的彩图转 base64 是十几 MB 的文本，回包体积和序列化耗时都不划算，
    /// 而 HTTP 这一侧本来就没有"把图回给客户端"的需求（图是客户端推过来的）。
    /// 所以图像端口只回一个描述（类型 + 宽高），需要看图请用流程里的其它数值输出或界面预览。
    /// </summary>
    public sealed class FlowOutputCollector
    {
        /// <summary>
        /// 采集输出值。
        /// </summary>
        /// <param name="session">已执行完的会话（调用方须保证此刻会话未被并发替换）</param>
        /// <param name="outputsSpec">
        /// 输出选择串，形如 <c>定位.中心X,测量.宽度</c>（逗号分隔，多个可混用）。
        /// 为空时采集全部步骤的全部输出端口。
        /// </param>
        /// <returns>键为"步骤名.端口名"的字典；值已转成 JSON 友好类型</returns>
        public Dictionary<string, object> Collect(FlowSession session, string outputsSpec)
        {
            var result = new Dictionary<string, object>(StringComparer.Ordinal);

            var engine = session?.ExecutionEngine;
            if (engine?.PluginLookup == null) return result;

            var stepNameById = BuildStepNameIndex(session);
            if (stepNameById.Count == 0) return result;

            var specs = SplitSpecs(outputsSpec);
            if (specs.Count == 0)
            {
                // 未指定：全量返回（客户端还没摸清步骤名时的探路方式）
                foreach (var pair in engine.PluginLookup)
                    CollectAllPorts(stepNameById, pair.Key, pair.Value, result);

                return result;
            }

            foreach (var spec in specs)
            {
                // 步骤名里可能含点（用户随手起的名），端口名不会 —— 所以从最后一个点切开
                var dot = spec.LastIndexOf('.');
                if (dot <= 0 || dot == spec.Length - 1)
                {
                    result[spec] = $"[格式错误] 应为「步骤名.端口名」，实际为「{spec}」";
                    continue;
                }

                var stepName = spec.Substring(0, dot);
                var portName = spec.Substring(dot + 1);

                var plugin = FindPlugin(engine, stepNameById, stepName);
                if (plugin == null)
                {
                    result[spec] = $"[未找到] 流程中没有名为「{stepName}」的步骤";
                    continue;
                }

                if (plugin.Outputs == null || !plugin.Outputs.TryGetValue(portName, out var port))
                {
                    result[spec] = $"[未找到] 步骤「{stepName}」没有名为「{portName}」的输出端口";
                    continue;
                }

                result[spec] = ToJsonValue(port.Value);
            }

            return result;
        }

        /// <summary>把会话的扁平步骤清单整理成「步骤名 → StepID」（同名步骤保留第一个）</summary>
        private static Dictionary<string, Guid> BuildStepNameIndex(FlowSession session)
        {
            var index = new Dictionary<string, Guid>(StringComparer.Ordinal);
            if (session.Blueprints == null) return index;

            foreach (var step in session.Blueprints)
            {
                if (step == null || string.IsNullOrEmpty(step.StepName)) continue;
                if (!index.ContainsKey(step.StepName))
                    index[step.StepName] = step.StepID;
            }

            return index;
        }

        private static IVisionPlugin FindPlugin(
            CompiledFlow engine,
            Dictionary<string, Guid> stepNameById,
            string stepName)
        {
            if (!stepNameById.TryGetValue(stepName, out var stepId)) return null;
            return engine.PluginLookup.TryGetValue(stepId, out var plugin) ? plugin : null;
        }

        private static void CollectAllPorts(
            Dictionary<string, Guid> stepNameById,
            Guid stepId,
            IVisionPlugin plugin,
            Dictionary<string, object> into)
        {
            if (plugin?.Outputs == null) return;

            // 反查步骤名只为了拼可读的键；查不到就退回 StepID 前 8 位（正常编译流程查得到）
            string stepName = null;
            foreach (var pair in stepNameById)
            {
                if (pair.Value != stepId) continue;
                stepName = pair.Key;
                break;
            }
            if (stepName == null) stepName = stepId.ToString("N").Substring(0, 8);

            foreach (var port in plugin.Outputs)
                into[$"{stepName}.{port.Key}"] = ToJsonValue(port.Value?.Value);
        }

        private static List<string> SplitSpecs(string outputsSpec)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(outputsSpec)) return list;

            foreach (var raw in outputsSpec.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var spec = raw.Trim();
                if (spec.Length > 0) list.Add(spec);
            }

            return list;
        }

        /// <summary>
        /// 把端口值转成 JSON 友好类型。
        /// 只放行"序列化器本来就认"的类型，其余一律 ToString()：
        /// 端口里可能挂着任意用户类型，硬序列化会抛异常把整个响应打成 500，
        /// 而一次取数失败不该让整张回包失败。
        /// </summary>
        private static object ToJsonValue(object value)
        {
            switch (value)
            {
                case null:
                    return null;

                case HImage image:
                    // 只回描述，不回像素（理由见类注释）
                    try
                    {
                        image.GetImageSize(out int width, out int height);
                        return new { type = "image", width, height };
                    }
                    catch
                    {
                        return new { type = "image" };
                    }

                case string s:
                    return s;

                case bool b:
                    return b;

                case int i:
                    return i;

                case long l:
                    return l;

                case short sh:
                    return sh;

                case byte by:
                    return by;

                case sbyte sb:
                    return sb;

                case uint ui:
                    return ui;

                case ulong ul:
                    return ul;

                case ushort us:
                    return us;

                case decimal m:
                    return m;

                // System.Text.Json 默认拒绝 NaN / Infinity，会把整个回包打成 500；
                // 而 HALCON 的未初始化 double 恰好就是 NaN（如 score 未赋值），
                // 所以这两个值降级成字符串回传，信息不丢、回包不炸
                case double d:
                    return double.IsNaN(d) || double.IsInfinity(d)
                        ? d.ToString(CultureInfo.InvariantCulture)
                        : d;

                case float f:
                    return float.IsNaN(f) || float.IsInfinity(f)
                        ? f.ToString(CultureInfo.InvariantCulture)
                        : f;

                default:
                    var type = value.GetType();
                    if (type.IsArray && type.GetElementType()?.IsPrimitive == true)
                        return value;

                    return value.ToString();
            }
        }
    }
}
