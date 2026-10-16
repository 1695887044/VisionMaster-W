using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using static FlowCanvasChecks.Program;

namespace FlowCanvasChecks
{
    /// <summary>
    /// 图标字符按钮（✕ × ＋ ↑ 这类一个字符的按钮）放进固定格子时必须留出字形空间
    /// （守 2026-10-16 真机三连报"关闭按钮的 X 显示不全"）。
    ///
    /// 【要拦的是什么】<c>App.xaml</c> 把 .NET 9 Fluent 主题并进了**应用级**资源，它自带一支隐式
    /// <c>Button</c> 样式，<c>Padding="11,5,11,6"</c>。于是"30×30 + 不挂样式 + 不写 Padding"的关闭按钮
    /// 只剩 <c>6×17</c> 的内容区，而「✕」在 14 号字下要 <c>11.4×17.8</c>——模板里那圈带圆角的 Border
    /// 把左右两截削掉，屏幕上就是一个不像 ✕ 的残字符。同一形状在三个地方各踩了一次：
    /// 帮助窗（已随换肤治掉）、运动看板标题栏、插件配置壳标题栏、报警历史收起按钮
    /// （那一颗最狠：24×24 减掉 11+11 后内容区宽 <c>0</c>，「×」整根不见）。
    ///
    /// 【为什么必须是静态闸】这条故障**编译期全绿、资源键全对、模板也正常**——错的是"格子宽度与
    /// 内边距的算术"，而且只有真机渲染才看得见。离屏探针能算出来（HelpProbe 本轮就是这么定的量），
    /// 但它要挂载整视图、覆盖不到全部视图，所以日常守门用静态扫描：
    /// **固定格子（写了 Width 或 Height）的图标按钮，要么挂 Style、要么显式写 Padding**，
    /// 两者都没有 = 吃 Fluent 那 11,5,11,6 = 判红。挂样式的（<c>DialogIconButton</c> 是 6,4,6,4）
    /// 本条不猜它的内边距，交给运行时渲染看。
    /// </summary>
    internal static class GlyphButtonClipCheck
    {
        /// <summary>
        /// 字形需求随字号的实测比例（Microsoft YaHei UI，HelpProbe 用无限宽参照 TextBlock 量出来的）：
        ///   14 号 → 11.4×17.8、12 号 → 9.8×15.2、16 号 → 13.1×20.3，即 宽≈0.82Fs、高≈1.27Fs。
        /// 这里各留一点余量（0.85 / 1.30）再 +0.5px，避免边界抖动误报。
        /// </summary>
        private const double NeedWidthPerFontSize = 0.85;
        private const double NeedHeightPerFontSize = 1.30;
        private const double DefaultFontSize = 12.0;

        /// <summary>三颗事故点（AGENTS.md 第 22 节记录）：摘掉它们的 Padding 本条必须变红。</summary>
        private static readonly (string File, string Tip)[] AccidentSites =
        {
            (@"VisionMaster\Views\DialogViews\MotionBoardView.xaml", "关闭"),
            (@"VisionMaster\Views\DialogViews\PluginConfigShellView.xaml", "关闭"),
            (@"VisionMaster\Views\ScadaAlarmHistoryView.xaml", "收起面板"),
        };

        public static void Run()
        {
            Section("[GB] 图标字符按钮放进固定格子必须挂样式或显式 Padding（静态扫描）");

            var root = RepoRoot();
            if (root == null)
            {
                Check("GB0 定位到仓库根", false, "找不到 AGENTS.md，后续扫描全部跳过");
                return;
            }
            Check("GB0 定位到仓库根", true, root);

            // 只扫第一方目录（DLL\HslCommunication、VM3.0-main 是随仓第三方源码，R29 同族噪音）
            var files = new[] { "Plugins", "Services", "Shard", "Engine", "Core", "UI", "VisionMaster" }
                .Select(d => Path.Combine(root, d))
                .Where(Directory.Exists)
                .SelectMany(d => Directory.GetFiles(d, "*.xaml", SearchOption.AllDirectories))
                .Where(f => !IsBuildDir(f))
                .ToList();
            Check("GB1 前置：扫到第一方 .xaml 文件", files.Count > 80, files.Count + " 个文件");

            var sites = files
                .SelectMany(f => GlyphButtons(Rel(root, f), File.ReadAllText(f)))
                .ToList();

            // 前置反证：解析器真的抓到了东西。这一条红了说明形状变了（属性换行写法、ContentTemplate…），
            // 而不是"大家都改好了"——少了它，"0 处违规"可能只是"0 次命中"的假绿。
            var fixedBox = sites.Where(s => s.Width.HasValue || s.Height.HasValue).ToList();
            Check("GB2 前置：图标按钮命中数与固定格子子集都非空（0 命中 = 解析口径过期，不代表没人犯）",
                sites.Count >= 10 && fixedBox.Count >= 3,
                $"图标按钮 {sites.Count} 颗，其中固定格子 {fixedBox.Count} 颗");

            // ---- 主判据：固定格子 + 无 Style + 无 Padding = 吃 Fluent 的 11,5,11,6 ----
            var bare = fixedBox.Where(s => !s.HasStyle && !s.HasPadding).ToList();
            Check("GB3【全仓】写了 Width/Height 的图标按钮必须挂 Style 或显式 Padding（都不写就吃 Fluent 隐式的 11,5,11,6，✕ 被模板圆角 Border 切掉）",
                bare.Count == 0,
                bare.Count == 0 ? "(无违规)" : string.Join(" | ", bare.Select(s => Describe(s))));

            // ---- 定量：固定格子 + 显式 Padding（没挂样式）的那些，格子减内边距还得放得下字形 ----
            var thin = fixedBox
                .Where(s => !s.HasStyle && s.HasPadding)
                .Select(s => (Site: s, Short: ContentAreaTooSmall(s)))
                .Where(t => t.Short != null)
                .ToList();
            Check("GB4 显式 Padding 也算过账：内容区（格子−内边距−边框）放不下字形照样判红（Padding 写了但写大了同样是切）",
                thin.Count == 0,
                thin.Count == 0 ? "(都放得下)" : string.Join(" | ", thin.Select(t => $"{Describe(t.Site)}：{t.Short}")));

            // ---- 反向钉住事故点：三颗修好的按钮必须仍在名单里、且仍带 Style/Padding ----
            foreach (var (file, tip) in AccidentSites)
            {
                var path = Path.Combine(root, file);
                var hits = File.Exists(path)
                    ? GlyphButtons(file, File.ReadAllText(path))
                        .Where(s => (s.Tip ?? "").StartsWith(tip, StringComparison.Ordinal))
                        .ToList()
                    : new List<GlyphSite>();
                var site = hits.FirstOrDefault();
                Check($"GB5 事故点复查：{file} 的「{tip}」按钮仍带 {(site?.HasStyle == true ? "Style" : "Padding")}（这条红了 = 防护被摘掉）",
                    site != null && site.FixedBox && (site.HasStyle || site.HasPadding),
                    site == null ? "(名单里找不到这颗按钮——改名/删掉/写法换了，本条该重审)" : Describe(site));
            }
        }

        // ---- 扫描 ----

        /// <summary>一个图标字符按钮点位：格子尺寸、内边距、字号、有没有挂样式。</summary>
        private sealed class GlyphSite
        {
            public string File = "";
            public int Line;
            public string Glyph = "";
            public string? Tip;
            public double? Width;
            public double? Height;
            public double FontSize = DefaultFontSize;
            public Thickness Pad = new Thickness();
            public Thickness Border = new Thickness();
            public bool HasStyle;
            public bool HasPadding;

            public bool FixedBox => Width.HasValue || Height.HasValue;
        }

        private sealed class Thickness
        {
            public double Left, Top, Right, Bottom;
        }

        /// <summary>
        /// 从一个 .xaml 里抓出"内容是单个图标字符"的按钮。
        /// 两种写法都要覆盖：<c>Content="✕"</c> 与元素内容 <c>&lt;TextBlock Text="✕"/&gt;</c>
        /// （标题栏那两颗用的是后者）；数字字符实体（<c>&amp;#xf00d;</c>）先解码再判。
        /// </summary>
        private static IEnumerable<GlyphSite> GlyphButtons(string relativeFile, string text)
        {
            var code = StripXmlComments(text);
            foreach (Match m in Regex.Matches(code, @"<(Button|ToggleButton)\b([^>]*?)(/>|>)", RegexOptions.Singleline))
            {
                var attrs = m.Groups[2].Value;
                var selfClosed = m.Groups[3].Value == "/>";
                var raw = Attr(attrs, "Content");

                double? fontSize = Number(Attr(attrs, "FontSize"));
                string? tip = Attr(attrs, "ToolTip");

                if (raw == null && !selfClosed)
                {
                    var close = code.IndexOf("</Button>", m.Index, StringComparison.Ordinal);
                    if (close < 0) continue;
                    var body = code.Substring(m.Index, close - m.Index);
                    raw = Attr(body, "Text");
                    if (fontSize == null) fontSize = Number(Attr(body, "FontSize"));
                    if (tip == null) tip = Attr(body, "ToolTip");
                }

                var glyph = Decode(raw);
                if (!IsIconGlyph(glyph)) continue;

                yield return new GlyphSite
                {
                    File = relativeFile,
                    Line = LineOf(code, m.Index),
                    Glyph = glyph!,
                    Tip = tip,
                    Width = Number(Attr(attrs, "Width")),
                    Height = Number(Attr(attrs, "Height")),
                    FontSize = fontSize ?? DefaultFontSize,
                    HasStyle = Regex.IsMatch(attrs, @"\sStyle\s*="),
                    HasPadding = Regex.IsMatch(attrs, @"\sPadding\s*="),
                    Pad = ParseThickness(Attr(attrs, "Padding")),
                    Border = ParseThickness(Attr(attrs, "BorderThickness")),
                };
            }
        }

        /// <summary>单个非字母、非数字、非空白的字符 = 图标字符（"取消"这类中文词按字母处理，不算）。</summary>
        private static bool IsIconGlyph(string? value)
        {
            if (value == null || value.Length != 1) return false;
            var c = value[0];
            return !char.IsLetterOrDigit(c) && !char.IsWhiteSpace(c);
        }

        private static string? Decode(string? value)
        {
            if (value == null) return null;
            var decoded = Regex.Replace(value, @"&#x([0-9a-fA-F]{1,6});?",
                m => ((char)int.Parse(m.Groups[1].Value, NumberStyles.HexNumber)).ToString());
            decoded = Regex.Replace(decoded, @"&#(\d{1,5});?",
                m => ((char)int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).ToString());
            return decoded.Trim();
        }

        private static string? Attr(string fragment, string name)
        {
            var m = Regex.Match(fragment, @"\s" + name + @"\s*=\s*""([^""]*)""");
            return m.Success ? m.Groups[1].Value : null;
        }

        /// <summary>只认纯数字（"Auto"、"*"、绑定表达式一律当"没写死尺寸"）。</summary>
        private static double? Number(string? value) =>
            value != null && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null;

        private static Thickness ParseThickness(string? value)
        {
            var t = new Thickness();
            if (value == null) return t;
            var parts = value.Split(',', ' ').Where(p => p.Length > 0)
                .Select(p => double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0.0)
                .ToArray();
            if (parts.Length == 0) return t;
            if (parts.Length == 1) { t.Left = t.Top = t.Right = t.Bottom = parts[0]; return t; }
            if (parts.Length == 2)
            {
                t.Left = t.Right = parts[0];
                t.Top = t.Bottom = parts[1];
                return t;
            }
            t.Left = parts[0];
            t.Top = parts[1];
            t.Right = parts[2];
            t.Bottom = parts.Length >= 4 ? parts[3] : parts[1];
            return t;
        }

        /// <summary>内容区放不下字形时给出说明，放得下返回 null。</summary>
        private static string? ContentAreaTooSmall(GlyphSite s)
        {
            var needW = NeedWidthPerFontSize * s.FontSize + 0.5;
            var needH = NeedHeightPerFontSize * s.FontSize + 0.5;
            // 没写死的那一维是自动长开的（Fluent 的 Padding 会被算进 DesiredSize），不设限
            var w = s.Width.HasValue ? s.Width.Value - s.Pad.Left - s.Pad.Right - s.Border.Left - s.Border.Right : double.MaxValue;
            var h = s.Height.HasValue ? s.Height.Value - s.Pad.Top - s.Pad.Bottom - s.Border.Top - s.Border.Bottom : double.MaxValue;
            return w < needW || h < needH
                ? $"内容区 {w:F1}×{h:F1} < 字形需求 {needW:F1}×{needH:F1}（字号 {s.FontSize}）"
                : null;
        }

        private static string Describe(GlyphSite s) =>
            $"{s.File}:{s.Line}「{s.Glyph}」格子 {s.Width?.ToString(CultureInfo.InvariantCulture) ?? "自动"}×" +
            $"{s.Height?.ToString(CultureInfo.InvariantCulture) ?? "自动"} " +
            $"Padding={s.Pad.Left},{s.Pad.Top},{s.Pad.Right},{s.Pad.Bottom} " +
            $"Style={(s.HasStyle ? "有" : "无")} Fs={s.FontSize}";

        // ---- 静态扫描辅助（与 LayoutVisibilityCheck 同一套口径）----

        private static string? RepoRoot()
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            for (int i = 0; i < 6 && dir != null; i++, dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "AGENTS.md"))) return dir.FullName;
            return null;
        }

        private static bool IsBuildDir(string file) =>
            Regex.IsMatch(file, @"\\(bin|obj)\\", RegexOptions.IgnoreCase);

        private static string Rel(string root, string file) =>
            file.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                ? file.Substring(root.Length).TrimStart('\\', '/')
                : file;

        private static int LineOf(string text, int index)
        {
            int line = 1;
            for (int i = 0; i < index && i < text.Length; i++) if (text[i] == '\n') line++;
            return line;
        }

        /// <summary>去 XML 注释但**保留行数与列偏移**（注释里写着"30×30 的格子只剩 6×17「✕」"，不去注释就是自己判自己红）。</summary>
        private static string StripXmlComments(string text) =>
            Regex.Replace(text, @"<!--.*?-->",
                m => new string('\n', m.Value.Count(c => c == '\n')), RegexOptions.Singleline);
    }
}
