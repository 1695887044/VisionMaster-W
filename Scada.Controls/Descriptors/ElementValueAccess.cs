using System;
using System.Globalization;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// 属性袋 / 几何字段的统一读写入口。
    ///
    /// 存在的理由：图元的值有两个落脚点——<b>属性袋</b>（<see cref="ScadaElement.Properties"/>，
    /// 字符串键值对）和<b>强类型几何字段</b>（X/Y/Width/Height/Rotation/Name）。
    /// 属性面板、绑定引擎、控件渲染都只想"给个键读/写一个值"，不该各自判断这个键该去哪边。
    /// 这里就是那一处判断，两边用 "$" 前缀区分：
    ///
    /// <code>
    /// 键形态          去处                      例
    /// $X/$Y/…         ScadaElement 的强类型字段   "$Width" → element.Width（double）
    /// 其它            Properties 键值袋           "Fill"   → element.Properties["Fill"]
    /// </code>
    ///
    /// 为什么几何不塞进属性袋：拖动一个矩形每秒产生几十次位置变更，若走"字符串 →
    /// double.Parse → 再回写字符串"的路，编辑器手感会毁在解析上；而且 .vms 里
    /// <c>"X": "128.5"</c> 这种写法也无法参与数值排序/比较。
    /// </summary>
    public static class ElementValueAccess
    {
        /// <summary>保留键前缀（属性袋里的键不得以它开头）</summary>
        public const string ReservedPrefix = "$";

        /// <summary>保留键：图元名</summary>
        public const string NameKey = "$Name";

        /// <summary>保留键：左上角 X</summary>
        public const string XKey = "$X";

        /// <summary>保留键：左上角 Y</summary>
        public const string YKey = "$Y";

        /// <summary>保留键：宽</summary>
        public const string WidthKey = "$Width";

        /// <summary>保留键：高</summary>
        public const string HeightKey = "$Height";

        /// <summary>保留键：旋转角（度）</summary>
        public const string RotationKey = "$Rotation";

        /// <summary>全部已知几何键（注册期校验用）</summary>
        public static readonly string[] KnownGeometryKeys =
        {
            NameKey, XKey, YKey, WidthKey, HeightKey, RotationKey,
        };

        /// <summary>是不是保留键（"$" 开头）</summary>
        public static bool IsReservedKey(string? key)
            => !string.IsNullOrEmpty(key) && key!.StartsWith(ReservedPrefix, StringComparison.Ordinal);

        /// <summary>是不是已知的几何保留键</summary>
        public static bool IsKnownGeometryKey(string? key)
            => Array.IndexOf(KnownGeometryKeys, key) >= 0;

        /// <summary>
        /// 读一个值：保留键读强类型字段，其余读属性袋（缺失时用描述符声明的默认值）。
        /// </summary>
        public static string Read(ScadaElement element, ElementPropertyDescriptor property)
        {
            ArgumentNullException.ThrowIfNull(property);
            return IsReservedKey(property.Key)
                ? Read(element, property.Key)
                : element.GetProperty(property.Key, property.DefaultValue);
        }

        /// <summary>读一个值（不查默认值：缺失即空串）</summary>
        public static string Read(ScadaElement element, string key)
        {
            ArgumentNullException.ThrowIfNull(element);

            switch (key)
            {
                case NameKey:
                    return element.Name;
                case XKey:
                    return Format(element.X);
                case YKey:
                    return Format(element.Y);
                case WidthKey:
                    return Format(element.Width);
                case HeightKey:
                    return Format(element.Height);
                case RotationKey:
                    return Format(element.Rotation);
            }

            return IsReservedKey(key) ? string.Empty : element.GetProperty(key);
        }

        /// <summary>
        /// 写一个值。保留键写强类型字段（数值非法则忽略），其余写属性袋。
        ///
        /// 未知的保留键<b>直接丢弃</b>而不是漏进属性袋：否则一次拼错的 "$Widht" 会在 .vms 里
        /// 静静躺着，既不生效也无处报警。
        /// </summary>
        public static void Write(ScadaElement element, string key, string? value)
        {
            ArgumentNullException.ThrowIfNull(element);

            switch (key)
            {
                case NameKey:
                    element.Name = value ?? string.Empty;
                    return;
                case XKey:
                    if (TryParse(value, out var x)) element.X = x;
                    return;
                case YKey:
                    if (TryParse(value, out var y)) element.Y = y;
                    return;
                case WidthKey:
                    // 模型约定宽高恒正（见 ScadaElement.Width 注释）：负数取绝对值，不做翻转语义
                    if (TryParse(value, out var w)) element.Width = Math.Abs(w);
                    return;
                case HeightKey:
                    if (TryParse(value, out var h)) element.Height = Math.Abs(h);
                    return;
                case RotationKey:
                    if (TryParse(value, out var r)) element.Rotation = r;
                    return;
            }

            if (IsReservedKey(key))
                return;

            element.SetProperty(key, value);
        }

        /// <summary>按不变文化格式化（.vms 是跨机器交换的，小数点不能跟着系统区域设置变）</summary>
        internal static string Format(double value)
            => value.ToString("0.######", CultureInfo.InvariantCulture);

        private static bool TryParse(string? text, out double value)
            => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>
    /// 每个图元都有的几何属性清单（名称 / X / Y / 宽 / 高 / 旋转）。
    ///
    /// 抽出来是为了让描述符只写"自己特有的属性"：<c>Properties = GeometryProperties.With(…)</c>。
    /// 若哪天要给所有图元统一加一条（比如"可见性"），改这里一处即可。
    ///
    /// <b>哪几条可绑变量，是一条产品口径，不是随手填的默认值</b>
    /// ---------
    /// · <c>X</c> / <c>Y</c> —— <b>不可绑</b>。位置是<b>版面语言</b>：它决定"这个图元画在画面哪儿"，
    ///   是设计期一次定好的事实。把它交给变量驱动，等于让画面每次刷新都自己搬家，
    ///   操作员会以为界面出故障了；商业组态里也没有人拿位置做动画。
    ///   把 ƒx 从这里收掉，属性面板"位置与尺寸"组就只剩尺寸与角度还亮着，噪声立刻少一半。
    /// · <c>宽</c> / <c>高</c> —— 可绑。尺寸是<b>运行语言</b>：液位随变量涨落、进度条随产量伸缩，
    ///   都是"绑住一个长度"就能表达的量，这是组态里最常见的动画之一。
    /// · <c>旋转</c> —— 可绑。角度同理（指针 / 阀门开度盘），且它已带 -360~360 的合法区间。
    ///
    /// 判据一句话：<b>设计期版面语言不可绑，运行期动画语言可绑。</b>
    /// </summary>
    public static class GeometryProperties
    {
        /// <summary>标准几何属性（脚本按 名称 → 位置 → 尺寸 → 旋转 排列）</summary>
        public static IReadOnlyList<ElementPropertyDescriptor> Standard { get; } = new ElementPropertyDescriptor[]
        {
            new()
            {
                Key = ElementValueAccess.NameKey, DisplayName = "名称",
                Kind = ElementPropertyKind.Text, Group = "常规",
                Description = "图层列表里显示的名字（不参与寻址，改它不会断开绑定）",
            },
            new()
            {
                Key = ElementValueAccess.XKey, DisplayName = "X",
                Kind = ElementPropertyKind.Number, Group = "位置与尺寸",
                Description = "左上角 X（画面像素，原点在画面左上角）；位置属设计期版面，不参与变量绑定",
            },
            new()
            {
                Key = ElementValueAccess.YKey, DisplayName = "Y",
                Kind = ElementPropertyKind.Number, Group = "位置与尺寸",
                Description = "左上角 Y（画面像素）；位置属设计期版面，不参与变量绑定",
            },
            new()
            {
                Key = ElementValueAccess.WidthKey, DisplayName = "宽",
                Kind = ElementPropertyKind.Number, Group = "位置与尺寸", Min = 1, IsBindable = true,
                Description = "宽度（像素）；可绑变量，做液位 / 进度这类长度随值变化的动画",
            },
            new()
            {
                Key = ElementValueAccess.HeightKey, DisplayName = "高",
                Kind = ElementPropertyKind.Number, Group = "位置与尺寸", Min = 1, IsBindable = true,
                Description = "高度（像素）；可绑变量，做液位 / 柱状这类高度随值变化的动画",
            },
            new()
            {
                Key = ElementValueAccess.RotationKey, DisplayName = "旋转",
                Kind = ElementPropertyKind.Number, Group = "位置与尺寸", Min = -360, Max = 360, IsBindable = true,
                Description = "顺时针角度，绕图元中心；0 表示不旋转",
            },
        };

        /// <summary>标准几何属性 + 本图元特有的属性</summary>
        public static IReadOnlyList<ElementPropertyDescriptor> With(params ElementPropertyDescriptor[] extras)
        {
            var result = new ElementPropertyDescriptor[Standard.Count + (extras?.Length ?? 0)];
            for (int i = 0; i < Standard.Count; i++)
                result[i] = Standard[i];
            for (int i = 0; i < (extras?.Length ?? 0); i++)
                result[Standard.Count + i] = extras![i];
            return result;
        }
    }
}
