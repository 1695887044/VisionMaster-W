using System;
using System.Collections.Generic;
using System.Globalization;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// SCADA 文字图标库的<b>名字 → 字形</b>表（Font Awesome 6 Pro Solid）。
    ///
    /// 为什么要有名字表：Font Awesome 的码点是私有区（U+F000 起），XAML 里写
    /// <c>&amp;#xF013;</c> 谁也记不住；写 <c>Gear</c> 才能让界面代码可读。
    /// 值全部是 <b>FA6 Solid</b> 的码点，字体由 <see cref="ScadaIconHelper"/> 统一设置。
    ///
    /// 用法：
    /// <code>
    /// &lt;TextBlock scada:ScadaIconHelper.Glyph="Gear" /&gt;
    /// &lt;TextBlock FontFamily="{StaticResource ScadaIconFont}" Text="{x:Static scada:ScadaIconGlyphs.Gear}" /&gt;
    /// </code>
    /// 也接受原始码点写法（<c>"f013"</c> / <c>"\uf013"</c> / <c>"&amp;#xf013;"</c> / <c>"0xf013"</c>），
    /// 见 <see cref="TryResolve"/>。
    /// </summary>
    public static class ScadaIconGlyphs
    {
        // ---- 通用 ----
        public const string Gear = "\uF013";
        public const string Image = "\uF03E";
        public const string Xmark = "\uF00D";
        public const string Plus = "\uF067";
        public const string Minus = "\uF068";
        public const string Check = "\uF00C";
        public const string TriangleExclamation = "\uF071";
        public const string CircleInfo = "\uF05A";
        public const string CircleCheck = "\uF058";
        public const string CircleXmark = "\uF057";
        public const string MagnifyingGlass = "\uF002";
        public const string FolderOpen = "\uF07C";
        public const string FloppyDisk = "\uF0C7";
        public const string TrashCan = "\uF2ED";
        public const string Pen = "\uF304";
        public const string Link = "\uF0C1";
        public const string Copy = "\uF0C5";
        public const string Eye = "\uF06E";
        public const string Lock = "\uF023";
        public const string Unlock = "\uF09C";
        public const string User = "\uF007";
        public const string Users = "\uF0C0";
        public const string RightFromBracket = "\uF08B";
        public const string Wrench = "\uF0AD";
        public const string Sliders = "\uF1DE";
        public const string Filter = "\uF0B0";
        public const string ArrowsRotate = "\uF021";

        // ---- 运行 / 导航 ----
        public const string Play = "\uF04B";
        public const string Stop = "\uF04D";
        public const string Pause = "\uF04C";
        public const string PowerOff = "\uF011";
        public const string ArrowRight = "\uF061";
        public const string ArrowLeft = "\uF060";
        public const string ArrowUp = "\uF062";
        public const string ArrowDown = "\uF063";
        public const string House = "\uF015";
        public const string List = "\uF03A";
        public const string Expand = "\uF065";
        public const string Compress = "\uF066";

        // ---- 数据 / 设备 ----
        public const string Clock = "\uF017";
        public const string Calendar = "\uF133";
        public const string ChartLine = "\uF201";
        public const string ChartColumn = "\uF080";
        public const string Table = "\uF0CE";
        public const string TableCellsLarge = "\uF009";
        public const string Qrcode = "\uF029";
        public const string Barcode = "\uF02A";
        public const string Database = "\uF1C0";
        public const string Bell = "\uF0F3";
        public const string VolumeHigh = "\uF028";
        public const string VolumeXmark = "\uF6A9";
        public const string Cube = "\uF1B2";
        public const string Industry = "\uF275";
        public const string TemperatureHalf = "\uF2C7";
        public const string GaugeHigh = "\uF625";
        public const string ToggleOn = "\uF205";
        public const string ToggleOff = "\uF204";
        public const string Camera = "\uF030";
        public const string Video = "\uF03D";
        public const string Microchip = "\uF2DB";

        private static readonly IReadOnlyDictionary<string, string> ByName =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Gear"] = Gear, ["Settings"] = Gear,
                ["Image"] = Image,
                ["Xmark"] = Xmark, ["Close"] = Xmark,
                ["Plus"] = Plus,
                ["Minus"] = Minus,
                ["Check"] = Check, ["OK"] = Check,
                ["TriangleExclamation"] = TriangleExclamation, ["Warning"] = TriangleExclamation,
                ["CircleInfo"] = CircleInfo, ["Info"] = CircleInfo,
                ["CircleCheck"] = CircleCheck,
                ["CircleXmark"] = CircleXmark,
                ["MagnifyingGlass"] = MagnifyingGlass, ["Search"] = MagnifyingGlass,
                ["FolderOpen"] = FolderOpen, ["Folder"] = FolderOpen,
                ["FloppyDisk"] = FloppyDisk, ["Save"] = FloppyDisk,
                ["TrashCan"] = TrashCan, ["Delete"] = TrashCan,
                ["Pen"] = Pen, ["Edit"] = Pen,
                ["Link"] = Link,
                ["Copy"] = Copy,
                ["Eye"] = Eye,
                ["Lock"] = Lock,
                ["Unlock"] = Unlock,
                ["User"] = User,
                ["Users"] = Users,
                ["RightFromBracket"] = RightFromBracket, ["Logout"] = RightFromBracket,
                ["Wrench"] = Wrench,
                ["Sliders"] = Sliders,
                ["Filter"] = Filter,
                ["ArrowsRotate"] = ArrowsRotate, ["Refresh"] = ArrowsRotate,
                ["Play"] = Play, ["Start"] = Play,
                ["Stop"] = Stop,
                ["Pause"] = Pause,
                ["PowerOff"] = PowerOff,
                ["ArrowRight"] = ArrowRight,
                ["ArrowLeft"] = ArrowLeft,
                ["ArrowUp"] = ArrowUp,
                ["ArrowDown"] = ArrowDown,
                ["House"] = House, ["Home"] = House,
                ["List"] = List,
                ["Expand"] = Expand,
                ["Compress"] = Compress,
                ["Clock"] = Clock,
                ["Calendar"] = Calendar,
                ["ChartLine"] = ChartLine, ["Trend"] = ChartLine,
                ["ChartColumn"] = ChartColumn, ["BarChart"] = ChartColumn,
                ["Table"] = Table,
                ["TableCellsLarge"] = TableCellsLarge,
                ["Qrcode"] = Qrcode,
                ["Barcode"] = Barcode,
                ["Database"] = Database,
                ["Bell"] = Bell, ["Alarm"] = Bell,
                ["VolumeHigh"] = VolumeHigh, ["Sound"] = VolumeHigh,
                ["VolumeXmark"] = VolumeXmark, ["Mute"] = VolumeXmark,
                ["Cube"] = Cube,
                ["Industry"] = Industry,
                ["TemperatureHalf"] = TemperatureHalf, ["Temperature"] = TemperatureHalf,
                ["GaugeHigh"] = GaugeHigh, ["Gauge"] = GaugeHigh,
                ["ToggleOn"] = ToggleOn,
                ["ToggleOff"] = ToggleOff,
                ["Camera"] = Camera,
                ["Video"] = Video,
                ["Microchip"] = Microchip,
            };

        /// <summary>
        /// 把“名字 / 十六进制码点 / 已经是字形字符”解析成一个可直接放进 TextBlock.Text 的字符串。
        /// 解析不出来返回 false，<paramref name="glyph"/> 为空串（不抛异常）。
        /// </summary>
        public static bool TryResolve(string? text, out string glyph)
        {
            glyph = string.Empty;

            if (string.IsNullOrWhiteSpace(text))
                return false;

            var value = text.Trim();

            if (ByName.TryGetValue(value, out var named))
            {
                glyph = named;
                return true;
            }

            // 已经是私有区字形（直接粘了图标字符）
            try
            {
                if (value.Length > 0 && char.ConvertToUtf32(value, 0) >= 0xE000)
                {
                    glyph = value;
                    return true;
                }
            }
            catch (ArgumentException)
            {
                // 孤立代理项：不是合法字符，继续按码点解析
            }

            var clean = value
                .Replace("&#x", string.Empty)
                .Replace("&#X", string.Empty)
                .Replace(";", string.Empty)
                .Replace("\\u", string.Empty)
                .Replace("\\U", string.Empty)
                .Replace("0x", string.Empty)
                .Replace("0X", string.Empty)
                .Trim();

            if (int.TryParse(clean, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code)
                && code > 0 && code <= 0x10FFFF)
            {
                glyph = char.ConvertFromUtf32(code);
                return true;
            }

            return false;
        }

        /// <summary>解析；失败返回空串（界面上宁可空着，也不要显示 “Gear” 这五个字母）</summary>
        public static string Resolve(string? text)
            => TryResolve(text, out var glyph) ? glyph : string.Empty;
    }
}
