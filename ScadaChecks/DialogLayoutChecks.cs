using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace ScadaChecks
{
    internal partial class Program
    {
        /// <summary>
        /// [DL] 弹窗里"固定高度的输入控件"必须有可用的内边距。
        ///
        /// 为什么要有这条
        /// =========
        /// 2026-10-25 报表列编辑器事故：ComboBox 写了 <c>Height="26"</c>、没写 Padding，
        /// 于是继承隐式样式的 <c>Padding="10,8"</c>（上下合计 16px），内容区只剩 10px，
        /// 一行中文被上下切掉。同类事故此前已在图标字符按钮（R68）与 TextBox 二次内缩（R184）
        /// 各发生过一次——都是"显式尺寸 + 继承样式内边距"这一个病根。
        ///
        /// 判据
        /// =========
        /// 对每个带**字面量 Height** 的 ComboBox / TextBox：
        /// · 有 <c>Style="{StaticResource …}"</c> → 放行（内边距由样式负责，静态扫描解析不了样式）；
        /// · 否则必须有 <c>Padding</c>，且 <c>Height − 2×边框 − 上下内边距 ≥ 字号 × 1.4</c>；
        /// · 两者都没有 → 判红（它拿到的内边距是隐式样式的，不是作者想的）。
        ///
        /// 为什么阈值是 1.4
        /// =========
        /// 中文行高约等于字号 × 1.35~1.4（WPF 的 Microsoft YaHei UI 实测 13 号字约 18px）。
        /// 低于这个数就一定会裁字或压顶，不是"看起来有点挤"。
        /// </summary>
        private static class DialogLayoutChecks
        {
            private const double LineHeightFactor = 1.4;
            private const double DefaultFontSize = 13;

            private static readonly Regex TagPattern = new(
                @"<(?<tag>ComboBox|TextBox)\b(?<attrs>[^>]*?)/?>",
                RegexOptions.Compiled | RegexOptions.Singleline);

            public static void Run()
            {
                Section("[DL] 固定高度的输入控件必须有可用内边距（防裁字）");

                var views = FindViewsDirectory();
                if (views == null)
                {
                    Check("[DL0] 找到 VisionMaster\\Views 目录", false,
                        "从断言宿主向上找不到 VisionMaster.sln");
                    return;
                }

                var violations = new List<string>();
                var scanned = 0;

                foreach (var file in Directory.EnumerateFiles(views, "*.xaml", SearchOption.AllDirectories))
                {
                    if (file.Contains("\\obj\\") || file.Contains("\\bin\\"))
                        continue;

                    string text;
                    try { text = File.ReadAllText(file); }
                    catch (Exception) { continue; }

                    foreach (Match match in TagPattern.Matches(text))
                    {
                        var attrs = match.Groups["attrs"].Value;
                        var height = ReadDouble(attrs, "Height");
                        if (height is not { } h)
                            continue;

                        scanned++;

                        // 样式负责内边距：静态扫描解析不到样式内容，按"已收口"放行。
                        // 两种写法都算：属性 Style="{StaticResource …}"，或子元素 <ComboBox.Style>。
                        var tailStart = Math.Min(text.Length, match.Index + match.Length);
                        var tail = text.Substring(tailStart, Math.Min(400, text.Length - tailStart));
                        var hasChildStyle = tail.Contains("<" + match.Groups["tag"].Value + ".Style>", StringComparison.Ordinal);

                        if (hasChildStyle || attrs.Contains("Style=", StringComparison.Ordinal))
                            continue;

                        var fontSize = ReadDouble(attrs, "FontSize") ?? DefaultFontSize;
                        var vertical = ReadVerticalPadding(attrs);

                        if (vertical == null)
                        {
                            violations.Add($"{Path.GetFileName(file)}:{LineOf(text, match.Index)} "
                                           + $"{match.Groups["tag"].Value} Height={h:0.#} 既无 Style 也无 Padding（吃隐式样式 10,8）");
                            continue;
                        }

                        var available = h - 2 /* 边框 */ - vertical.Value;
                        var required = fontSize * LineHeightFactor;

                        if (available + 0.01 < required)
                        {
                            violations.Add($"{Path.GetFileName(file)}:{LineOf(text, match.Index)} "
                                           + $"{match.Groups["tag"].Value} Height={h:0.#} 内容区 {available:0.#} < 需要 {required:0.#}（字号 {fontSize:0.#}）");
                        }
                    }
                }

                Check("[DL1] 弹窗 XAML 里固定高度的 ComboBox/TextBox 都写了 Padding 或引用了样式",
                    violations.Count == 0,
                    violations.Count == 0
                        ? $"扫描 {scanned} 个固定高度控件，全部合规"
                        : $"扫描 {scanned} 个，{violations.Count} 处不合规：{string.Join("；", violations.Take(8))}");
            }

            /// <summary>从断言宿主向上找仓库根（含 VisionMaster.sln），再取 VisionMaster\Views</summary>
            private static string? FindViewsDirectory()
            {
                var dir = new DirectoryInfo(AppContext.BaseDirectory);

                while (dir != null)
                {
                    if (File.Exists(Path.Combine(dir.FullName, "VisionMaster.sln")))
                    {
                        var views = Path.Combine(dir.FullName, "VisionMaster", "Views");
                        return Directory.Exists(views) ? views : null;
                    }

                    dir = dir.Parent;
                }

                return null;
            }

            private static int LineOf(string text, int index)
            {
                var line = 1;
                for (var i = 0; i < index && i < text.Length; i++)
                {
                    if (text[i] == '\n')
                        line++;
                }

                return line;
            }

            private static double? ReadDouble(string attrs, string name)
            {
                var match = Regex.Match(attrs, name + @"\s*=\s*""(?<v>[-\d.]+)""");
                if (!match.Success)
                    return null;

                return double.TryParse(match.Groups["v"].Value, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var value) ? value : null;
            }

            /// <summary>Padding 的上下合计；没写返回 null（不是 0——0 表示"作者显式写了 0"）</summary>
            private static double? ReadVerticalPadding(string attrs)
            {
                var match = Regex.Match(attrs, @"Padding\s*=\s*""(?<v>[^""]*)""");
                if (!match.Success)
                    return null;

                var parts = match.Groups["v"].Value
                    .Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(p => double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0)
                    .ToArray();

                return parts.Length switch
                {
                    1 => parts[0] * 2,
                    2 => parts[1] * 2,
                    4 => parts[1] + parts[3],
                    _ => 0,
                };
            }
        }
    }
}
