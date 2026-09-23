using System.Collections.Generic;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// 内置图元清单（S2 交付的第一批：矩形 / 椭圆 / 文本 / 按钮 / 指示灯；S7 起陆续扩充）。
    ///
    /// 这个类只回答"我们自带哪些图元、每个图元有哪些属性"，不负责注册——
    /// 注册由 <see cref="ElementRegistry"/> 在类型初始化时自动完成（见其静态构造），
    /// 于是宿主（主程序、断言工程、将来任何一个引用本库的程序）都不需要记得
    /// "先注册再使用"这一步，也就不会出现"少调一行，工具箱空了"这种问题。
    ///
    /// 新增一类图元要动的地方，一共三处，都在本库内：
    /// ① Elements/ 下写控件类（继承 <see cref="ScadaElementBase"/>，自带静态构造换 DefaultStyleKey）；
    /// ② Themes/ 下写样式并在 Generic.xaml 里加一行合并；
    /// ③ 本清单里加一条描述符。
    /// 领域层（VM.Scada）、序列化层、编辑器一行不改——.vms 里只留一个字符串 TypeKey。
    ///
    /// TypeKey 用 "Hmi." 前缀：它是写进 .vms 的长期标识，一旦发布就不能再改，
    /// 所以取值要能表达"这是人机界面的图元"，而不是实现细节（类名/命名空间都可能重构）。
    ///
    /// 关于 <c>IsBindable</c>（属性面板那一列 ƒx）的声明口径：
    /// <b>「位置与尺寸」与「外观」两组一律不声明可绑</b>——它们是设计期语言（版面 + 皮肤），
    /// 交给变量驱动只会让画面自己乱变；可绑的是数值 / 状态 / 文字这类运行期语言。
    /// 口径由 <see cref="ElementDescriptor.NonBindableGroups"/> 在注册期兜底校验，
    /// 写错会直接注册失败。理由见 <see cref="GeometryProperties"/> 的类注释。
    ///
    /// 关于 <see cref="ElementDescriptor.Events"/>（本图元能配哪些事件钩子）的声明口径：
    /// <b>只声明"已经有人发得出来"的事件</b>。属性面板按这份清单长行，清单里有一条发不出来的事件，
    /// 用户就会配出一个永远不响的钩子——那比少一条更糟（他会怀疑自己配错了，而不是软件没做）。
    /// 于是：
    /// ① 按下/释放由画布在运行态代发（见 ScadaCanvas 的鼠标处理），本阶段先只开给"操作"类图元；
    ///    底图、区域框这类纯装饰图元不声明——技术上画布点谁都发得出来，但"能点"不等于"该配"，
    ///    这条产品规则就写在描述符上。哪天要开放给矩形，这里加一行即可，别处都不用改。
    /// ② <b>变量级</b>的值事件（更改数值 / 值为真 / 值为假 / 上限 / 下限）<b>一律不声明在图元上</b>：
    ///    手册 7.5.2 的"可组态对象"列把它们挂在"变量"上，宿主是变量事件记录
    ///    （见 <see cref="ScadaVariableEvent"/>，由 ScadaVariableEventEngine 判边沿）。
    ///    挂到某一个图元上会变成"绑了它的那个图元才能配这个事件"——同一个变量被十个图元绑着，
    ///    同一件事就要配十份，改一处漏九处。
    /// ③ 输入完成时（<see cref="ScadaEventType.InputCompleted"/>）只开给<b>可输入</b>的图元，
    ///    且只在"提交成功"那一刻发（失败留在编辑态，见 IOFieldElement.CommitEdit）。
    /// </summary>
    public static class BuiltInElements
    {
        /// <summary>全部内置图元描述符</summary>
        public static IReadOnlyList<ElementDescriptor> All { get; } = new ElementDescriptor[]
        {
            new()
            {
                TypeKey = "Hmi.Rectangle",
                DisplayName = "矩形",
                ControlType = typeof(RectangleElement),
                Category = "基础",
                DefaultWidth = 120,
                DefaultHeight = 60,
                Description = "带填充与边框的矩形，中间可放一段文字（设备本体、区域背景、状态块）",
                Properties = GeometryProperties.With(
                    new()
                    {
                        Key = "Fill", DisplayName = "填充色", Kind = ElementPropertyKind.Color,
                        Group = "外观", DefaultValue = "#FFFFFFFF",
                        TargetProperty = ScadaElementBase.FillProperty,
                    },
                    new()
                    {
                        Key = "Stroke", DisplayName = "边框色", Kind = ElementPropertyKind.Color,
                        Group = "外观", DefaultValue = "#FF7A7A7A",
                        TargetProperty = ScadaElementBase.StrokeProperty,
                    },
                    new()
                    {
                        Key = "StrokeThickness", DisplayName = "边框粗细", Kind = ElementPropertyKind.Number,
                        Group = "外观", DefaultValue = "1", Min = 0, Max = 20,
                        TargetProperty = ScadaElementBase.StrokeThicknessProperty,
                    },
                    new()
                    {
                        Key = "CornerRadius", DisplayName = "圆角", Kind = ElementPropertyKind.Number,
                        Group = "外观", DefaultValue = "0", Min = 0, Max = 200,
                        Description = "半径超过短边一半时 WPF 会自行收敛成胶囊形",
                        TargetProperty = ScadaElementBase.CornerRadiusProperty,
                    },
                    new()
                    {
                        Key = "Text", DisplayName = "文字", Kind = ElementPropertyKind.Text,
                        Group = "文字", DefaultValue = string.Empty, IsBindable = true,
                        TargetProperty = ScadaElementBase.TextProperty,
                    },
                    new()
                    {
                        Key = "Foreground", DisplayName = "文字颜色", Kind = ElementPropertyKind.Color,
                        Group = "文字", DefaultValue = "#FF202020",
                        TargetProperty = ScadaElementBase.ForegroundProperty,
                    },
                    new()
                    {
                        Key = "FontSize", DisplayName = "字号", Kind = ElementPropertyKind.Number,
                        Group = "文字", DefaultValue = "12", Min = 6, Max = 200,
                        TargetProperty = ScadaElementBase.FontSizeProperty,
                    }),
            },

            new()
            {
                TypeKey = "Hmi.Ellipse",
                DisplayName = "椭圆",
                ControlType = typeof(EllipseElement),
                Category = "基础",
                DefaultWidth = 100,
                DefaultHeight = 100,
                Description = "圆形/椭圆的设备或状态点（罐体、电机、风机）",
                Properties = GeometryProperties.With(
                    new()
                    {
                        Key = "Fill", DisplayName = "填充色", Kind = ElementPropertyKind.Color,
                        Group = "外观", DefaultValue = "#FFFFFFFF",
                        TargetProperty = ScadaElementBase.FillProperty,
                    },
                    new()
                    {
                        Key = "Stroke", DisplayName = "边框色", Kind = ElementPropertyKind.Color,
                        Group = "外观", DefaultValue = "#FF7A7A7A",
                        TargetProperty = ScadaElementBase.StrokeProperty,
                    },
                    new()
                    {
                        Key = "StrokeThickness", DisplayName = "边框粗细", Kind = ElementPropertyKind.Number,
                        Group = "外观", DefaultValue = "1", Min = 0, Max = 20,
                        TargetProperty = ScadaElementBase.StrokeThicknessProperty,
                    },
                    new()
                    {
                        Key = "Text", DisplayName = "文字", Kind = ElementPropertyKind.Text,
                        Group = "文字", DefaultValue = string.Empty, IsBindable = true,
                        TargetProperty = ScadaElementBase.TextProperty,
                    },
                    new()
                    {
                        Key = "Foreground", DisplayName = "文字颜色", Kind = ElementPropertyKind.Color,
                        Group = "文字", DefaultValue = "#FF202020",
                        TargetProperty = ScadaElementBase.ForegroundProperty,
                    },
                    new()
                    {
                        Key = "FontSize", DisplayName = "字号", Kind = ElementPropertyKind.Number,
                        Group = "文字", DefaultValue = "12", Min = 6, Max = 200,
                        TargetProperty = ScadaElementBase.FontSizeProperty,
                    }),
            },

            new()
            {
                TypeKey = "Hmi.Text",
                DisplayName = "文本",
                ControlType = typeof(TextElement),
                Category = "基础",
                DefaultWidth = 120,
                DefaultHeight = 24,
                Description = "标题、说明、单位；把文字属性绑到变量上就是动态数值显示",
                Properties = GeometryProperties.With(
                    new()
                    {
                        Key = "Text", DisplayName = "内容", Kind = ElementPropertyKind.MultilineText,
                        Group = "文字", DefaultValue = "文本", IsBindable = true,
                        TargetProperty = ScadaElementBase.TextProperty,
                    },
                    new()
                    {
                        Key = "Foreground", DisplayName = "文字颜色", Kind = ElementPropertyKind.Color,
                        Group = "文字", DefaultValue = "#FF202020", IsBindable = true,
                        TargetProperty = ScadaElementBase.ForegroundProperty,
                    },
                    new()
                    {
                        Key = "FontSize", DisplayName = "字号", Kind = ElementPropertyKind.Number,
                        Group = "文字", DefaultValue = "14", Min = 6, Max = 200,
                        TargetProperty = ScadaElementBase.FontSizeProperty,
                    },
                    new()
                    {
                        Key = "FontWeight", DisplayName = "字重", Kind = ElementPropertyKind.Choice,
                        Group = "文字", DefaultValue = "Normal",
                        Choices = new[] { "Normal", "SemiBold", "Bold" },
                        Description = "标题常用 Bold；Normal 与 SemiBold 在小字号下区别不大",
                        TargetProperty = ScadaElementBase.FontWeightProperty,
                    },
                    new()
                    {
                        Key = "TextAlignment", DisplayName = "水平对齐", Kind = ElementPropertyKind.Choice,
                        Group = "文字", DefaultValue = "Center",
                        Choices = new[] { "Left", "Center", "Right", "Justify" },
                        TargetProperty = ScadaElementBase.TextAlignmentProperty,
                    },
                    new()
                    {
                        Key = "VerticalContentAlignment", DisplayName = "垂直对齐", Kind = ElementPropertyKind.Choice,
                        Group = "文字", DefaultValue = "Center",
                        Choices = new[] { "Top", "Center", "Bottom" },
                        Description = "Stretch 不列出：多行文本在 Stretch 下与 Top 表现一致，列出来只会让人犹豫",
                        TargetProperty = ScadaElementBase.VerticalContentAlignmentProperty,
                    }),
            },

            new()
            {
                TypeKey = "Hmi.Button",
                DisplayName = "按钮",
                ControlType = typeof(ButtonElement),
                Category = "操作",
                DefaultWidth = 88,
                DefaultHeight = 32,
                Description = "操作员按下的动作入口；按下/释放各是一个事件钩子，点下去干什么在属性面板「事件」里配",
                Events = new[] { ScadaEventType.Pressed, ScadaEventType.Released },
                Properties = GeometryProperties.With(
                    new()
                    {
                        Key = "Text", DisplayName = "文字", Kind = ElementPropertyKind.Text,
                        Group = "文字", DefaultValue = "按钮", IsBindable = true,
                        TargetProperty = ScadaElementBase.TextProperty,
                    },
                    new()
                    {
                        Key = "Fill", DisplayName = "填充色", Kind = ElementPropertyKind.Color,
                        Group = "外观", DefaultValue = "#FF2D7DD2",
                        TargetProperty = ScadaElementBase.FillProperty,
                    },
                    new()
                    {
                        Key = "Stroke", DisplayName = "边框色", Kind = ElementPropertyKind.Color,
                        Group = "外观", DefaultValue = "#FF1F5C9E",
                        TargetProperty = ScadaElementBase.StrokeProperty,
                    },
                    new()
                    {
                        Key = "StrokeThickness", DisplayName = "边框粗细", Kind = ElementPropertyKind.Number,
                        Group = "外观", DefaultValue = "1", Min = 0, Max = 20,
                        TargetProperty = ScadaElementBase.StrokeThicknessProperty,
                    },
                    new()
                    {
                        Key = "CornerRadius", DisplayName = "圆角", Kind = ElementPropertyKind.Number,
                        Group = "外观", DefaultValue = "3", Min = 0, Max = 200,
                        TargetProperty = ScadaElementBase.CornerRadiusProperty,
                    },
                    new()
                    {
                        Key = "Foreground", DisplayName = "文字颜色", Kind = ElementPropertyKind.Color,
                        Group = "文字", DefaultValue = "#FFFFFFFF",
                        TargetProperty = ScadaElementBase.ForegroundProperty,
                    },
                    new()
                    {
                        Key = "FontSize", DisplayName = "字号", Kind = ElementPropertyKind.Number,
                        Group = "文字", DefaultValue = "12", Min = 6, Max = 200,
                        TargetProperty = ScadaElementBase.FontSizeProperty,
                    },
                    new()
                    {
                        Key = "FontWeight", DisplayName = "字重", Kind = ElementPropertyKind.Choice,
                        Group = "文字", DefaultValue = "Normal",
                        Choices = new[] { "Normal", "SemiBold", "Bold" },
                        TargetProperty = ScadaElementBase.FontWeightProperty,
                    }),
            },

            new()
            {
                TypeKey = "Hmi.BitButton",
                DisplayName = "位按钮",
                ControlType = typeof(BitButtonElement),
                Category = "操作",
                DefaultWidth = 88,
                DefaultHeight = 32,
                Description = "一根开关量变量的操作化身：绑上变量后点击即对该位做置位/复位/取反/按下ON/按下OFF，"
                            + "并按变量值显示两种状态（启停、手自动切换、复位、点动）。"
                            + "与「按钮」的分工：按钮是动作入口（事件里配动作表），位按钮是变量的化身（直接读写这根量）",
                Events = new[] { ScadaEventType.Pressed, ScadaEventType.Released },
                Properties = GeometryProperties.With(
                    new()
                    {
                        Key = "Mode", DisplayName = "模式", Kind = ElementPropertyKind.Choice,
                        Group = "常规", DefaultValue = BitButtonElement.ModePressOn,
                        Choices = new[]
                        {
                            BitButtonElement.ModeSet, BitButtonElement.ModeReset, BitButtonElement.ModeInvert,
                            BitButtonElement.ModePressOn, BitButtonElement.ModePressOff,
                        },
                        Description = "置位/复位/取反在释放时写一次；按下ON/按下OFF是瞬动（按下写一侧、释放写另一侧）。"
                                    + "「切换窗口」不在本控件里：切画面要动运行态导航，那是按钮动作表的活",
                        TargetProperty = BitButtonElement.ModeProperty,
                    },
                    new()
                    {
                        Key = "IsOn", DisplayName = "读变量", Kind = ElementPropertyKind.Bool,
                        Group = "状态", DefaultValue = "False", IsBindable = true,
                        Description = "运行态把工程变量的布尔值绑到这里；本控件也按它回写（读写同一根变量）",
                        TargetProperty = BitButtonElement.IsOnProperty,
                    },
                    new()
                    {
                        Key = "OutputInvert", DisplayName = "输出反向", Kind = ElementPropertyKind.Bool,
                        Group = "状态", DefaultValue = "False",
                        Description = "对读取的值取反后再判状态（只影响显示与取反写回，不改写出去的字面量）",
                        TargetProperty = BitButtonElement.OutputInvertProperty,
                    },
                    new()
                    {
                        Key = "OnText", DisplayName = "状态1文字", Kind = ElementPropertyKind.Text,
                        Group = "状态", DefaultValue = string.Empty, IsBindable = true,
                        Description = "状态为 1 时显示的文字；留空则沿用「文字」",
                        TargetProperty = BitButtonElement.OnTextProperty,
                    },
                    new()
                    {
                        Key = "OffText", DisplayName = "状态0文字", Kind = ElementPropertyKind.Text,
                        Group = "状态", DefaultValue = string.Empty, IsBindable = true,
                        Description = "状态为 0 时显示的文字；留空则沿用「文字」",
                        TargetProperty = BitButtonElement.OffTextProperty,
                    },
                    new()
                    {
                        Key = "OnFill", DisplayName = "状态1背景", Kind = ElementPropertyKind.Color,
                        Group = "状态", DefaultValue = "#FF2D7DD2", IsBindable = true,
                        TargetProperty = BitButtonElement.OnFillProperty,
                    },
                    new()
                    {
                        Key = "OffFill", DisplayName = "状态0背景", Kind = ElementPropertyKind.Color,
                        Group = "状态", DefaultValue = "#FF4A5568", IsBindable = true,
                        TargetProperty = BitButtonElement.OffFillProperty,
                    },
                    new()
                    {
                        Key = "OnForeground", DisplayName = "状态1文字色", Kind = ElementPropertyKind.Color,
                        Group = "状态", DefaultValue = "#FFFFFFFF", IsBindable = true,
                        TargetProperty = BitButtonElement.OnForegroundProperty,
                    },
                    new()
                    {
                        Key = "OffForeground", DisplayName = "状态0文字色", Kind = ElementPropertyKind.Color,
                        Group = "状态", DefaultValue = "#FFFFFFFF", IsBindable = true,
                        TargetProperty = BitButtonElement.OffForegroundProperty,
                    },
                    new()
                    {
                        Key = "Text", DisplayName = "文字", Kind = ElementPropertyKind.Text,
                        Group = "文字", DefaultValue = "按钮", IsBindable = true,
                        Description = "两种状态文字都留空时的共用文字（如「启动/停止」同一个标签）",
                        TargetProperty = ScadaElementBase.TextProperty,
                    },
                    new()
                    {
                        Key = "FontSize", DisplayName = "字号", Kind = ElementPropertyKind.Number,
                        Group = "文字", DefaultValue = "12", Min = 6, Max = 200,
                        TargetProperty = ScadaElementBase.FontSizeProperty,
                    },
                    new()
                    {
                        Key = "FontWeight", DisplayName = "字重", Kind = ElementPropertyKind.Choice,
                        Group = "文字", DefaultValue = "Normal",
                        Choices = new[] { "Normal", "SemiBold", "Bold" },
                        TargetProperty = ScadaElementBase.FontWeightProperty,
                    },
                    new()
                    {
                        Key = "Stroke", DisplayName = "边框色", Kind = ElementPropertyKind.Color,
                        Group = "外观", DefaultValue = "#FF1F5C9E",
                        TargetProperty = ScadaElementBase.StrokeProperty,
                    },
                    new()
                    {
                        Key = "StrokeThickness", DisplayName = "边框粗细", Kind = ElementPropertyKind.Number,
                        Group = "外观", DefaultValue = "1", Min = 0, Max = 20,
                        TargetProperty = ScadaElementBase.StrokeThicknessProperty,
                    },
                    new()
                    {
                        Key = "CornerRadius", DisplayName = "圆角", Kind = ElementPropertyKind.Number,
                        Group = "外观", DefaultValue = "3", Min = 0, Max = 200,
                        TargetProperty = ScadaElementBase.CornerRadiusProperty,
                    }),
            },

            new()
            {
                TypeKey = "Hmi.Indicator",
                DisplayName = "指示灯",
                ControlType = typeof(IndicatorElement),
                Category = "指示",
                DefaultWidth = 40,
                DefaultHeight = 56,
                Description = "一根开关量变量的可视化身；把「点亮」属性绑到变量即可",
                Properties = GeometryProperties.With(
                    new()
                    {
                        Key = "IsOn", DisplayName = "点亮", Kind = ElementPropertyKind.Bool,
                        Group = "状态", DefaultValue = "False", IsBindable = true,
                        Description = "运行态把工程变量的布尔值绑到这里",
                        TargetProperty = IndicatorElement.IsOnProperty,
                    },
                    new()
                    {
                        Key = "OnColor", DisplayName = "点亮色", Kind = ElementPropertyKind.Color,
                        Group = "状态", DefaultValue = "#FF34C759", IsBindable = true,
                        TargetProperty = IndicatorElement.OnColorProperty,
                    },
                    new()
                    {
                        Key = "OffColor", DisplayName = "熄灭色", Kind = ElementPropertyKind.Color,
                        Group = "状态", DefaultValue = "#FF3A3A3A", IsBindable = true,
                        TargetProperty = IndicatorElement.OffColorProperty,
                    },
                    new()
                    {
                        Key = "Shape", DisplayName = "形状", Kind = ElementPropertyKind.Choice,
                        Group = "外观", DefaultValue = "Circle",
                        Choices = new[] { "Circle", "Square" },
                        TargetProperty = IndicatorElement.ShapeProperty,
                    },
                    new()
                    {
                        Key = "Stroke", DisplayName = "边框色", Kind = ElementPropertyKind.Color,
                        Group = "外观", DefaultValue = "#FF5A5A5A",
                        TargetProperty = ScadaElementBase.StrokeProperty,
                    },
                    new()
                    {
                        Key = "StrokeThickness", DisplayName = "边框粗细", Kind = ElementPropertyKind.Number,
                        Group = "外观", DefaultValue = "1", Min = 0, Max = 20,
                        TargetProperty = ScadaElementBase.StrokeThicknessProperty,
                    },
                    new()
                    {
                        Key = "Text", DisplayName = "标签", Kind = ElementPropertyKind.Text,
                        Group = "文字", DefaultValue = string.Empty, IsBindable = true,
                        Description = "显示在灯下方的一行字（如「1#电机」）",
                        TargetProperty = ScadaElementBase.TextProperty,
                    },
                    new()
                    {
                        Key = "Foreground", DisplayName = "标签颜色", Kind = ElementPropertyKind.Color,
                        Group = "文字", DefaultValue = "#FF202020",
                        TargetProperty = ScadaElementBase.ForegroundProperty,
                    },
                    new()
                    {
                        Key = "FontSize", DisplayName = "标签字号", Kind = ElementPropertyKind.Number,
                        Group = "文字", DefaultValue = "12", Min = 6, Max = 200,
                        TargetProperty = ScadaElementBase.FontSizeProperty,
                    }),
            },

            new()
            {
                TypeKey = "Hmi.ProgressBar",
                DisplayName = "棒图",
                ControlType = typeof(ProgressBarElement),
                Category = "数值",
                DefaultWidth = 160,
                DefaultHeight = 20,
                Description = "一个数值在量程里的可视化身；把「当前值」绑到变量上即可（产量完成率、料仓余量、阀门开度）",
                Properties = GeometryProperties.With(
                    new()
                    {
                        Key = "Value", DisplayName = "当前值", Kind = ElementPropertyKind.Number,
                        Group = "数据", DefaultValue = "0", IsBindable = true,
                        Description = "运行态把工程变量绑到这里；条长 = (当前值 − 下限) ÷ (上限 − 下限)，超出量程按两端收敛",
                        TargetProperty = ProgressBarElement.ValueProperty,
                    },
                    new()
                    {
                        Key = "Minimum", DisplayName = "量程下限", Kind = ElementPropertyKind.Number,
                        Group = "数据", DefaultValue = "0",
                        Description = "下限不小于上限时画空条，而不是让非法量程把整页渲染打断",
                        TargetProperty = ProgressBarElement.MinimumProperty,
                    },
                    new()
                    {
                        Key = "Maximum", DisplayName = "量程上限", Kind = ElementPropertyKind.Number,
                        Group = "数据", DefaultValue = "100",
                        TargetProperty = ProgressBarElement.MaximumProperty,
                    },
                    new()
                    {
                        Key = "ValueFormat", DisplayName = "数值格式", Kind = ElementPropertyKind.Text,
                        Group = "数据", DefaultValue = "0.#",
                        Description = "标准 .NET 数字格式串，可带单位（如 0.0 ℃）；写错了退回通用格式，不会让画面出错",
                        TargetProperty = ProgressBarElement.ValueFormatProperty,
                    },
                    new()
                    {
                        Key = "Orientation", DisplayName = "走向", Kind = ElementPropertyKind.Choice,
                        Group = "外观", DefaultValue = "Horizontal",
                        Choices = new[] { "Horizontal", "Vertical" },
                        Description = "横向从左往右长；纵向从下往上长（液位、料位天生是竖着的）",
                        TargetProperty = ProgressBarElement.OrientationProperty,
                    },
                    new()
                    {
                        Key = "Fill", DisplayName = "填充色", Kind = ElementPropertyKind.Color,
                        Group = "外观", DefaultValue = "#FF2D7DD2",
                        Description = "已填充那一段的颜色",
                        TargetProperty = ScadaElementBase.FillProperty,
                    },
                    new()
                    {
                        Key = "TrackColor", DisplayName = "空槽色", Kind = ElementPropertyKind.Color,
                        Group = "外观", DefaultValue = "#FF3A3A3A",
                        Description = "未填充那一段的颜色；浅色画面上要调亮，否则整条看不出边界",
                        TargetProperty = ProgressBarElement.TrackColorProperty,
                    },
                    new()
                    {
                        Key = "Stroke", DisplayName = "边框色", Kind = ElementPropertyKind.Color,
                        Group = "外观", DefaultValue = "#FF5A5A5A",
                        TargetProperty = ScadaElementBase.StrokeProperty,
                    },
                    new()
                    {
                        Key = "StrokeThickness", DisplayName = "边框粗细", Kind = ElementPropertyKind.Number,
                        Group = "外观", DefaultValue = "1", Min = 0, Max = 20,
                        TargetProperty = ScadaElementBase.StrokeThicknessProperty,
                    },
                    new()
                    {
                        Key = "CornerRadius", DisplayName = "圆角", Kind = ElementPropertyKind.Number,
                        Group = "外观", DefaultValue = "2", Min = 0, Max = 200,
                        TargetProperty = ScadaElementBase.CornerRadiusProperty,
                    },
                    new()
                    {
                        Key = "ShowValue", DisplayName = "显示数值", Kind = ElementPropertyKind.Bool,
                        Group = "文字", DefaultValue = "True",
                        TargetProperty = ProgressBarElement.ShowValueProperty,
                    },
                    new()
                    {
                        Key = "Foreground", DisplayName = "数值颜色", Kind = ElementPropertyKind.Color,
                        Group = "文字", DefaultValue = "#FFFFFFFF",
                        TargetProperty = ScadaElementBase.ForegroundProperty,
                    },
                    new()
                    {
                        Key = "FontSize", DisplayName = "数值字号", Kind = ElementPropertyKind.Number,
                        Group = "文字", DefaultValue = "11", Min = 6, Max = 200,
                        TargetProperty = ScadaElementBase.FontSizeProperty,
                    },
                    new()
                    {
                        Key = "FontWeight", DisplayName = "数值字重", Kind = ElementPropertyKind.Choice,
                        Group = "文字", DefaultValue = "Bold",
                        Choices = new[] { "Normal", "SemiBold", "Bold" },
                        Description = "默认 Bold：条上的字压在填充色上，不加粗在小尺寸下容易糊",
                        TargetProperty = ScadaElementBase.FontWeightProperty,
                    }),
            },

            new()
            {
                TypeKey = "Hmi.IOField",
                DisplayName = "数值域",
                ControlType = typeof(IOFieldElement),
                Category = "数值",
                DefaultWidth = 140,
                DefaultHeight = 28,
                Description = "带边框的数值框：左边一行说明字，右边「数值 + 单位」；把「数值」绑到变量上即可（温度、计数、位置）。" +
                              "模式选「Input / InputOutput」后，运行态点一下框子就能就地改数并写回变量",
                // 手册 7.5.2 把"输入完成时"挂在数字IO域 / 字符IO域 / 日期时间域上；本库当前只有
                // 这一个可输入图元，所以只在这一条描述符上声明。将来加了字符域/日期域，各自加一行即可。
                Events = new[] { ScadaEventType.InputCompleted },
                Properties = GeometryProperties.With(
                    new()
                    {
                        Key = "Value", DisplayName = "数值", Kind = ElementPropertyKind.Number,
                        Group = "数据", DefaultValue = "0", IsBindable = true,
                        Description = "运行态把工程变量绑到这里；框里显示的是它按「数值格式」格式化后的样子",
                        TargetProperty = IOFieldElement.ValueProperty,
                    },
                    new()
                    {
                        Key = "ValueFormat", DisplayName = "数值格式", Kind = ElementPropertyKind.Text,
                        Group = "数据", DefaultValue = "0.##",
                        Description = "标准 .NET 数字格式串：0.0 定一位小数、F2 补零到两位、#,##0 加千分位；" +
                                      "写错了退回通用格式，不会让画面出错",
                        TargetProperty = IOFieldElement.ValueFormatProperty,
                    },
                    new()
                    {
                        Key = "Unit", DisplayName = "单位", Kind = ElementPropertyKind.Text,
                        Group = "数据", DefaultValue = string.Empty,
                        Description = "跟在数值后面的一小段字（℃ / mm / pcs）；留空则只显数值",
                        TargetProperty = IOFieldElement.UnitProperty,
                    },
                    new()
                    {
                        Key = "Mode", DisplayName = "模式", Kind = ElementPropertyKind.Choice,
                        Group = "数据", DefaultValue = "Output",
                        Choices = new[] { "Output", "Input", "InputOutput" },
                        Description = "Output = 只读显示（老行为，运行态点不动）；" +
                                      "Input = 只写（框里的数不跟变量走，只把操作员敲的值送下去）；" +
                                      "InputOutput = 可读可写。后两种要运行态能点进输入，还必须把「数值」绑到变量上",
                        TargetProperty = IOFieldElement.ModeProperty,
                    },
                    new()
                    {
                        Key = "FormatType", DisplayName = "格式类型", Kind = ElementPropertyKind.Choice,
                        Group = "数据", DefaultValue = "Decimal",
                        Choices = new[] { "Decimal", "Hex", "Binary" },
                        Description = "Decimal 走「数值格式」串；Hex / Binary 按整数显示（位状态、字状态、设备地址用得上）。" +
                                      "只改显示与就地输入的进制，写回变量的一律是十进制数值",
                        TargetProperty = IOFieldElement.FormatTypeProperty,
                    },
                    new()
                    {
                        Key = "Gain", DisplayName = "增益", Kind = ElementPropertyKind.Number,
                        Group = "数据", DefaultValue = "1",
                        Description = "现场量纲与画面量纲的换算系数：显示值 = 变量值 × 增益 + 偏移量；" +
                                      "写回时反过来算。默认 1（不换算）；填 0 会让输入被拒（算不出该写多少）",
                        TargetProperty = IOFieldElement.GainProperty,
                    },
                    new()
                    {
                        Key = "Offset", DisplayName = "偏移量", Kind = ElementPropertyKind.Number,
                        Group = "数据", DefaultValue = "0",
                        Description = "换算公式里的加数（公式见「增益」）。典型用法：PLC 存 0.1℃ 整数，增益 0.1、偏移量 0 即得 ℃",
                        TargetProperty = IOFieldElement.OffsetProperty,
                    },
                    new()
                    {
                        Key = "HasMinimum", DisplayName = "启用下限", Kind = ElementPropertyKind.Bool,
                        Group = "限制", DefaultValue = "False",
                        Description = "打开后，低于下限的输入不写下去（不是夹到下限——静默改掉操作员敲的数比拒绝更危险）",
                        TargetProperty = IOFieldElement.HasMinimumProperty,
                    },
                    new()
                    {
                        Key = "Minimum", DisplayName = "下限", Kind = ElementPropertyKind.Number,
                        Group = "限制", DefaultValue = "0",
                        Description = "按显示量纲填（换算后的值）；低于它则输入被拒，且数值文字显示成「下限以下颜色」",
                        TargetProperty = IOFieldElement.MinimumProperty,
                    },
                    new()
                    {
                        Key = "UnderMinColor", DisplayName = "下限以下颜色", Kind = ElementPropertyKind.Color,
                        Group = "限制", DefaultValue = "#FFFB8C00",
                        Description = "数值低于下限时文字换成这个颜色（默认橙）；只在下限启用时生效",
                        TargetProperty = IOFieldElement.UnderMinColorProperty,
                    },
                    new()
                    {
                        Key = "HasMaximum", DisplayName = "启用上限", Kind = ElementPropertyKind.Bool,
                        Group = "限制", DefaultValue = "False",
                        Description = "打开后，高于上限的输入不写下去",
                        TargetProperty = IOFieldElement.HasMaximumProperty,
                    },
                    new()
                    {
                        Key = "Maximum", DisplayName = "上限", Kind = ElementPropertyKind.Number,
                        Group = "限制", DefaultValue = "100",
                        Description = "按显示量纲填（换算后的值）；高于它则输入被拒，且数值文字显示成「上限以上颜色」",
                        TargetProperty = IOFieldElement.MaximumProperty,
                    },
                    new()
                    {
                        Key = "OverMaxColor", DisplayName = "上限以上颜色", Kind = ElementPropertyKind.Color,
                        Group = "限制", DefaultValue = "#FFE53935",
                        Description = "数值高于上限时文字换成这个颜色（默认红）；只在上限启用时生效",
                        TargetProperty = IOFieldElement.OverMaxColorProperty,
                    },
                    new()
                    {
                        Key = "Text", DisplayName = "说明字", Kind = ElementPropertyKind.Text,
                        Group = "文字", DefaultValue = string.Empty, IsBindable = true,
                        Description = "显示在数值左边的一行字（如「温度」「1#工位」）；留空则数值独占整行",
                        TargetProperty = ScadaElementBase.TextProperty,
                    },
                    new()
                    {
                        Key = "Foreground", DisplayName = "文字颜色", Kind = ElementPropertyKind.Color,
                        Group = "文字", DefaultValue = "#FF202020",
                        TargetProperty = ScadaElementBase.ForegroundProperty,
                    },
                    new()
                    {
                        Key = "FontSize", DisplayName = "字号", Kind = ElementPropertyKind.Number,
                        Group = "文字", DefaultValue = "14", Min = 6, Max = 200,
                        TargetProperty = ScadaElementBase.FontSizeProperty,
                    },
                    new()
                    {
                        Key = "FontWeight", DisplayName = "字重", Kind = ElementPropertyKind.Choice,
                        Group = "文字", DefaultValue = "Normal",
                        Choices = new[] { "Normal", "SemiBold", "Bold" },
                        TargetProperty = ScadaElementBase.FontWeightProperty,
                    },
                    new()
                    {
                        Key = "TextAlignment", DisplayName = "数值对齐", Kind = ElementPropertyKind.Choice,
                        Group = "文字", DefaultValue = "Right",
                        Choices = new[] { "Left", "Center", "Right", "Justify" },
                        Description = "默认 Right：小数点在右边排成一条线，一列数值扫起来才快；说明字不受它影响，恒靠左",
                        TargetProperty = ScadaElementBase.TextAlignmentProperty,
                    },
                    new()
                    {
                        Key = "Fill", DisplayName = "填充色", Kind = ElementPropertyKind.Color,
                        Group = "外观", DefaultValue = "#FFFFFFFF",
                        Description = "框内底色；深色画面上要跟着调暗，否则一列白框会盖过画面主体",
                        TargetProperty = ScadaElementBase.FillProperty,
                    },
                    new()
                    {
                        Key = "Stroke", DisplayName = "边框色", Kind = ElementPropertyKind.Color,
                        Group = "外观", DefaultValue = "#FF7A7A7A",
                        TargetProperty = ScadaElementBase.StrokeProperty,
                    },
                    new()
                    {
                        Key = "StrokeThickness", DisplayName = "边框粗细", Kind = ElementPropertyKind.Number,
                        Group = "外观", DefaultValue = "1", Min = 0, Max = 20,
                        TargetProperty = ScadaElementBase.StrokeThicknessProperty,
                    },
                    new()
                    {
                        Key = "CornerRadius", DisplayName = "圆角", Kind = ElementPropertyKind.Number,
                        Group = "外观", DefaultValue = "2", Min = 0, Max = 200,
                        TargetProperty = ScadaElementBase.CornerRadiusProperty,
                    }),
            },

            new()
            {
                TypeKey = "Hmi.Lamp",
                DisplayName = "多态灯",
                ControlType = typeof(LampElement),
                Category = "指示",
                DefaultWidth = 40,
                DefaultHeight = 56,
                Description = "停机/运行/警告/报警四态合一的状态灯；把「状态」绑到变量即可（枚举名或 0~3 的整数都认）",
                Properties = GeometryProperties.With(
                    new()
                    {
                        Key = "State", DisplayName = "状态", Kind = ElementPropertyKind.Choice,
                        Group = "状态", DefaultValue = "Off",
                        Choices = new[] { "Off", "On", "Warning", "Alarm" },
                        IsBindable = true,
                        Description = "运行态把工程变量绑到这里：整数 0=熄灭 1=点亮 2=警告 3=报警，写枚举名（On/Warning/Alarm）也认",
                        TargetProperty = LampElement.StateProperty,
                    },
                    new()
                    {
                        Key = "OffColor", DisplayName = "熄灭色", Kind = ElementPropertyKind.Color,
                        Group = "状态", DefaultValue = "#FF3A3A3A", IsBindable = true,
                        TargetProperty = LampElement.OffColorProperty,
                    },
                    new()
                    {
                        Key = "OnColor", DisplayName = "点亮色", Kind = ElementPropertyKind.Color,
                        Group = "状态", DefaultValue = "#FF34C759", IsBindable = true,
                        TargetProperty = LampElement.OnColorProperty,
                    },
                    new()
                    {
                        Key = "WarningColor", DisplayName = "警告色", Kind = ElementPropertyKind.Color,
                        Group = "状态", DefaultValue = "#FFFFB020", IsBindable = true,
                        TargetProperty = LampElement.WarningColorProperty,
                    },
                    new()
                    {
                        Key = "AlarmColor", DisplayName = "报警色", Kind = ElementPropertyKind.Color,
                        Group = "状态", DefaultValue = "#FFE03A2B", IsBindable = true,
                        TargetProperty = LampElement.AlarmColorProperty,
                    },
                    new()
                    {
                        Key = "Shape", DisplayName = "形状", Kind = ElementPropertyKind.Choice,
                        Group = "外观", DefaultValue = "Circle",
                        Choices = new[] { "Circle", "Square" },
                        Description = "与指示灯共用同一套形状词汇：多灯密集排列时方形更省地方",
                        TargetProperty = LampElement.ShapeProperty,
                    },
                    new()
                    {
                        Key = "Stroke", DisplayName = "边框色", Kind = ElementPropertyKind.Color,
                        Group = "外观", DefaultValue = "#FF5A5A5A",
                        TargetProperty = ScadaElementBase.StrokeProperty,
                    },
                    new()
                    {
                        Key = "StrokeThickness", DisplayName = "边框粗细", Kind = ElementPropertyKind.Number,
                        Group = "外观", DefaultValue = "1", Min = 0, Max = 20,
                        TargetProperty = ScadaElementBase.StrokeThicknessProperty,
                    },
                    new()
                    {
                        Key = "Text", DisplayName = "标签", Kind = ElementPropertyKind.Text,
                        Group = "文字", DefaultValue = string.Empty, IsBindable = true,
                        Description = "显示在灯下方的一行字（如「1#主轴」）",
                        TargetProperty = ScadaElementBase.TextProperty,
                    },
                    new()
                    {
                        Key = "Foreground", DisplayName = "标签颜色", Kind = ElementPropertyKind.Color,
                        Group = "文字", DefaultValue = "#FF202020",
                        TargetProperty = ScadaElementBase.ForegroundProperty,
                    },
                    new()
                    {
                        Key = "FontSize", DisplayName = "标签字号", Kind = ElementPropertyKind.Number,
                        Group = "文字", DefaultValue = "12", Min = 6, Max = 200,
                        TargetProperty = ScadaElementBase.FontSizeProperty,
                    },
                    new()
                    {
                        Key = "FontWeight", DisplayName = "标签字重", Kind = ElementPropertyKind.Choice,
                        Group = "文字", DefaultValue = "Normal",
                        Choices = new[] { "Normal", "SemiBold", "Bold" },
                        TargetProperty = ScadaElementBase.FontWeightProperty,
                    }),
            },

            new()
            {
                TypeKey = "Hmi.Clock",
                DisplayName = "时钟",
                ControlType = typeof(ClockElement),
                Category = "指示",
                DefaultWidth = 180,
                DefaultHeight = 28,
                Description = "走时的系统日期时间；格式串自己写（如 HH:mm:ss），刷新节拍默认每秒一次",
                Properties = GeometryProperties.With(
                    new()
                    {
                        Key = "Format", DisplayName = "格式", Kind = ElementPropertyKind.Text,
                        Group = "数据", DefaultValue = "yyyy-MM-dd HH:mm:ss",
                        Description = "标准 .NET 日期时间格式串：HH:mm:ss 只显时间，带 dddd 会出星期；" +
                                      "写错了退回默认形状，不会让整页渲染中断",
                        TargetProperty = ClockElement.FormatProperty,
                    },
                    new()
                    {
                        Key = "Interval", DisplayName = "刷新节拍", Kind = ElementPropertyKind.Number,
                        Group = "数据", DefaultValue = "1", Min = 0.1, Max = 60,
                        Description = "单位秒。落在 0.1~60 之外会被收敛进区间（0 或负数不会变成一个失控的定时器）；" +
                                      "跳动会对齐整拍，不慢半拍",
                        TargetProperty = ClockElement.IntervalProperty,
                    },
                    new()
                    {
                        Key = "Text", DisplayName = "标签", Kind = ElementPropertyKind.Text,
                        Group = "文字", DefaultValue = string.Empty, IsBindable = true,
                        Description = "显示在时间下方的一行字（如「系统时间」「1#线」）；留空则只显时间",
                        TargetProperty = ScadaElementBase.TextProperty,
                    },
                    new()
                    {
                        Key = "Foreground", DisplayName = "文字颜色", Kind = ElementPropertyKind.Color,
                        Group = "文字", DefaultValue = "#FF202020",
                        TargetProperty = ScadaElementBase.ForegroundProperty,
                    },
                    new()
                    {
                        Key = "FontSize", DisplayName = "字号", Kind = ElementPropertyKind.Number,
                        Group = "文字", DefaultValue = "16", Min = 6, Max = 200,
                        TargetProperty = ScadaElementBase.FontSizeProperty,
                    },
                    new()
                    {
                        Key = "FontWeight", DisplayName = "字重", Kind = ElementPropertyKind.Choice,
                        Group = "文字", DefaultValue = "SemiBold",
                        Choices = new[] { "Normal", "SemiBold", "Bold" },
                        Description = "默认 SemiBold：时间是要一眼扫到的信息，全 Normal 在深色底上偏虚",
                        TargetProperty = ScadaElementBase.FontWeightProperty,
                    },
                    new()
                    {
                        Key = "TextAlignment", DisplayName = "水平对齐", Kind = ElementPropertyKind.Choice,
                        Group = "文字", DefaultValue = "Center",
                        Choices = new[] { "Left", "Center", "Right", "Justify" },
                        TargetProperty = ScadaElementBase.TextAlignmentProperty,
                    }),
            },

            new()
            {
                TypeKey = "Hmi.Gauge",
                DisplayName = "表盘",
                ControlType = typeof(GaugeElement),
                Category = "数值",
                DefaultWidth = 140,
                DefaultHeight = 140,
                Description = "圆盘刻度上的一根指针；把「当前值」绑到变量上即可（压力、转速、温度）",
                Properties = GeometryProperties.With(
                    new()
                    {
                        Key = "Value", DisplayName = "当前值", Kind = ElementPropertyKind.Number,
                        Group = "数据", DefaultValue = "0", IsBindable = true,
                        Description = "运行态把工程变量绑到这里；指针角度 = 起始角 + 扫过角 × " +
                                      "(当前值 − 下限) ÷ (上限 − 下限)，超出量程按两端收敛",
                        TargetProperty = GaugeElement.ValueProperty,
                    },
                    new()
                    {
                        Key = "Minimum", DisplayName = "量程下限", Kind = ElementPropertyKind.Number,
                        Group = "数据", DefaultValue = "0",
                        Description = "下限不小于上限时指针停在起始角，而不是让非法量程把整页渲染打断",
                        TargetProperty = GaugeElement.MinimumProperty,
                    },
                    new()
                    {
                        Key = "Maximum", DisplayName = "量程上限", Kind = ElementPropertyKind.Number,
                        Group = "数据", DefaultValue = "100",
                        TargetProperty = GaugeElement.MaximumProperty,
                    },
                    new()
                    {
                        Key = "ValueFormat", DisplayName = "数值格式", Kind = ElementPropertyKind.Text,
                        Group = "数据", DefaultValue = "0.#",
                        Description = "标准 .NET 数字格式串（0.0 定一位小数、F2 补零到两位）；" +
                                      "写错了退回通用格式，不会让画面出错",
                        TargetProperty = GaugeElement.ValueFormatProperty,
                    },
                    new()
                    {
                        Key = "Unit", DisplayName = "单位", Kind = ElementPropertyKind.Text,
                        Group = "数据", DefaultValue = string.Empty,
                        Description = "跟在数值后面的一小段字（MPa / rpm / ℃）；留空则只显数值",
                        TargetProperty = GaugeElement.UnitProperty,
                    },
                    new()
                    {
                        Key = "StartAngle", DisplayName = "起始角", Kind = ElementPropertyKind.Number,
                        Group = "刻度", DefaultValue = "135", Min = -360, Max = 360,
                        Description = "度。0° 指向右（3 点钟方向）、角度顺时针增加，所以 135° 是左下（7 点半）；" +
                                      "默认 135° 配 270° 缺口落在正下方，正是放读数的地方",
                        TargetProperty = GaugeElement.StartAngleProperty,
                    },
                    new()
                    {
                        Key = "SweepAngle", DisplayName = "扫过角", Kind = ElementPropertyKind.Number,
                        Group = "刻度", DefaultValue = "270", Min = -360, Max = 360,
                        Description = "度。正数顺时针、负数逆时针，绝对值 360 画整圈；超出 ±360 会收敛（刻度不会绕回来叠在一起）",
                        TargetProperty = GaugeElement.SweepAngleProperty,
                    },
                    new()
                    {
                        Key = "TickDivisions", DisplayName = "刻度格数", Kind = ElementPropertyKind.Number,
                        Group = "刻度", DefaultValue = "5", Min = 0, Max = 60,
                        Description = "格数 + 1 = 刻度线根数（含两端），所以默认 5 格就是 0/20/40/60/80/100 六个读数点；0 = 不画刻度",
                        TargetProperty = GaugeElement.TickDivisionsProperty,
                    },
                    new()
                    {
                        Key = "Fill", DisplayName = "指针色", Kind = ElementPropertyKind.Color,
                        Group = "外观", DefaultValue = "#FFE03A2B",
                        Description = "指针与中心轴环的颜色；深色盘面上红色指针一眼扫得到",
                        TargetProperty = ScadaElementBase.FillProperty,
                    },
                    new()
                    {
                        Key = "TrackColor", DisplayName = "盘底色", Kind = ElementPropertyKind.Color,
                        Group = "外观", DefaultValue = "#FF3A3A3A",
                        Description = "表盘面的填充色；浅色画面上要调亮，否则刻度线看不出来",
                        TargetProperty = GaugeElement.TrackColorProperty,
                    },
                    new()
                    {
                        Key = "ScaleColor", DisplayName = "刻度色", Kind = ElementPropertyKind.Color,
                        Group = "外观", DefaultValue = "#FF9AA5B1",
                        Description = "量程弧与刻度线的颜色；要和盘底色拉开，否则操作员读不出格",
                        TargetProperty = GaugeElement.ScaleColorProperty,
                    },
                    new()
                    {
                        Key = "Stroke", DisplayName = "外圈色", Kind = ElementPropertyKind.Color,
                        Group = "外观", DefaultValue = "#FF5A5A5A",
                        TargetProperty = ScadaElementBase.StrokeProperty,
                    },
                    new()
                    {
                        Key = "StrokeThickness", DisplayName = "外圈粗细", Kind = ElementPropertyKind.Number,
                        Group = "外观", DefaultValue = "1", Min = 0, Max = 20,
                        TargetProperty = ScadaElementBase.StrokeThicknessProperty,
                    },
                    new()
                    {
                        Key = "ShowValue", DisplayName = "显示数值", Kind = ElementPropertyKind.Bool,
                        Group = "文字", DefaultValue = "True",
                        Description = "关掉只留指针（读数由旁边的数值域承担），一个没有读数的表盘在工业现场几乎没用，所以默认开",
                        TargetProperty = GaugeElement.ShowValueProperty,
                    },
                    new()
                    {
                        Key = "Foreground", DisplayName = "数值颜色", Kind = ElementPropertyKind.Color,
                        Group = "文字", DefaultValue = "#FFFFFFFF",
                        TargetProperty = ScadaElementBase.ForegroundProperty,
                    },
                    new()
                    {
                        Key = "FontSize", DisplayName = "数值字号", Kind = ElementPropertyKind.Number,
                        Group = "文字", DefaultValue = "13", Min = 6, Max = 200,
                        TargetProperty = ScadaElementBase.FontSizeProperty,
                    },
                    new()
                    {
                        Key = "FontWeight", DisplayName = "数值字重", Kind = ElementPropertyKind.Choice,
                        Group = "文字", DefaultValue = "SemiBold",
                        Choices = new[] { "Normal", "SemiBold", "Bold" },
                        Description = "默认 SemiBold：读数压在盘面上，全 Normal 在小表盘上偏虚",
                        TargetProperty = ScadaElementBase.FontWeightProperty,
                    },
                    new()
                    {
                        Key = "TextAlignment", DisplayName = "数值对齐", Kind = ElementPropertyKind.Choice,
                        Group = "文字", DefaultValue = "Center",
                        Choices = new[] { "Left", "Center", "Right", "Justify" },
                        TargetProperty = ScadaElementBase.TextAlignmentProperty,
                    }),
            },

            new()
            {
                TypeKey = "Hmi.Valve",
                DisplayName = "阀门",
                ControlType = typeof(ValveElement),
                Category = "工艺",
                DefaultWidth = 72,
                DefaultHeight = 64,
                Description = "管路上一只阀（蝴蝶结符号）；开度绑变量，0=全关、100=全开，中间值就是调节阀",
                Properties = GeometryProperties.With(
                    new()
                    {
                        Key = "Opening", DisplayName = "开度", Kind = ElementPropertyKind.Number,
                        Group = "数据", DefaultValue = "0", Min = 0, Max = 100, IsBindable = true,
                        Description = "运行态把工程变量绑到这里（0~100 的实数）；开关阀只给 0 或 100，" +
                                      "中间值就是调节阀。超出 0~100 按两端收敛，坏值不会把整页渲染打断",
                        TargetProperty = ValveElement.OpeningProperty,
                    },
                    new()
                    {
                        Key = "OpeningFormat", DisplayName = "开度格式", Kind = ElementPropertyKind.Text,
                        Group = "数据", DefaultValue = "0.#",
                        Description = "标准 .NET 数字格式串（0.0 定一位小数、F0 取整）；" +
                                      "写错了退回通用格式，不会让画面出错",
                        TargetProperty = ValveElement.OpeningFormatProperty,
                    },
                    new()
                    {
                        Key = "TrackColor", DisplayName = "空腔色", Kind = ElementPropertyKind.Color,
                        Group = "外观", DefaultValue = "#FF3A3A3A",
                        Description = "阀体空腔的底色，也是全关时阀体的样子（没开就没有介质）",
                        TargetProperty = ValveElement.TrackColorProperty,
                    },
                    new()
                    {
                        Key = "ThrottleColor", DisplayName = "中间位色", Kind = ElementPropertyKind.Color,
                        Group = "外观", DefaultValue = "#FF2F80ED",
                        Description = "0 < 开度 < 100 时介质填充的颜色；蓝色是「正在调节」的通用语汇",
                        TargetProperty = ValveElement.ThrottleColorProperty,
                    },
                    new()
                    {
                        Key = "OpenColor", DisplayName = "全开色", Kind = ElementPropertyKind.Color,
                        Group = "外观", DefaultValue = "#FF34C759",
                        Description = "开度 ≥ 100 时介质填充的颜色；绿色 = 通路打开",
                        TargetProperty = ValveElement.OpenColorProperty,
                    },
                    new()
                    {
                        Key = "Stroke", DisplayName = "轮廓色", Kind = ElementPropertyKind.Color,
                        Group = "外观", DefaultValue = "#FF5A5A5A",
                        TargetProperty = ScadaElementBase.StrokeProperty,
                    },
                    new()
                    {
                        Key = "StrokeThickness", DisplayName = "轮廓粗细", Kind = ElementPropertyKind.Number,
                        Group = "外观", DefaultValue = "1", Min = 0, Max = 20,
                        TargetProperty = ScadaElementBase.StrokeThicknessProperty,
                    },
                    new()
                    {
                        Key = "Text", DisplayName = "位号", Kind = ElementPropertyKind.Text,
                        Group = "文字", DefaultValue = string.Empty, IsBindable = true,
                        Description = "压在阀体上方凹口的一行字（如「FV-101」）；管路图上靠它认设备",
                        TargetProperty = ScadaElementBase.TextProperty,
                    },
                    new()
                    {
                        Key = "ShowValue", DisplayName = "显示开度", Kind = ElementPropertyKind.Bool,
                        Group = "文字", DefaultValue = "True",
                        Description = "关掉只留阀体（读数交给旁边的数值域），默认开：阀开没开全靠这个数说话",
                        TargetProperty = ValveElement.ShowValueProperty,
                    },
                    new()
                    {
                        Key = "Foreground", DisplayName = "文字颜色", Kind = ElementPropertyKind.Color,
                        Group = "文字", DefaultValue = "#FFFFFFFF",
                        Description = "默认白色：文字压在深色阀体上，深色字会糊成一片",
                        TargetProperty = ScadaElementBase.ForegroundProperty,
                    },
                    new()
                    {
                        Key = "FontSize", DisplayName = "字号", Kind = ElementPropertyKind.Number,
                        Group = "文字", DefaultValue = "12", Min = 6, Max = 200,
                        TargetProperty = ScadaElementBase.FontSizeProperty,
                    },
                    new()
                    {
                        Key = "FontWeight", DisplayName = "字重", Kind = ElementPropertyKind.Choice,
                        Group = "文字", DefaultValue = "SemiBold",
                        Choices = new[] { "Normal", "SemiBold", "Bold" },
                        TargetProperty = ScadaElementBase.FontWeightProperty,
                    }),
            },

            new()
            {
                TypeKey = "Hmi.Pump",
                DisplayName = "泵",
                ControlType = typeof(PumpElement),
                Category = "工艺",
                DefaultWidth = 72,
                DefaultHeight = 72,
                Description = "管路上一台泵（圆壳里一只尖角朝右的叶轮）；状态绑变量，停机=灰、运行=绿、故障=红",
                Properties = GeometryProperties.With(
                    new()
                    {
                        Key = "State", DisplayName = "状态", Kind = ElementPropertyKind.Choice,
                        Group = "数据", DefaultValue = "Stopped",
                        Choices = new[] { "Stopped", "Running", "Fault" },
                        IsBindable = true,
                        Description = "运行态把工程变量绑到这里：整数 0=停机 1=运行 2=故障，写枚举名（Running/Fault）也认；" +
                                      "认不出的值按停机收敛，坏数据不会把整页渲染打断",
                        TargetProperty = PumpElement.StateProperty,
                    },
                    new()
                    {
                        Key = "TrackColor", DisplayName = "泵壳色", Kind = ElementPropertyKind.Color,
                        Group = "外观", DefaultValue = "#FF3A3A3A",
                        Description = "泵壳（叶轮之外露出来的那一圈）的底色",
                        TargetProperty = PumpElement.TrackColorProperty,
                    },
                    new()
                    {
                        Key = "StoppedColor", DisplayName = "停机色", Kind = ElementPropertyKind.Color,
                        Group = "外观", DefaultValue = "#FF7A7A7A",
                        Description = "停机时叶轮的颜色；灰色 = 没在转，与多态灯的熄灭色同一个语汇",
                        TargetProperty = PumpElement.StoppedColorProperty,
                    },
                    new()
                    {
                        Key = "RunningColor", DisplayName = "运行色", Kind = ElementPropertyKind.Color,
                        Group = "外观", DefaultValue = "#FF34C759",
                        Description = "运行时叶轮的颜色；绿色 = 设备在转",
                        TargetProperty = PumpElement.RunningColorProperty,
                    },
                    new()
                    {
                        Key = "FaultColor", DisplayName = "故障色", Kind = ElementPropertyKind.Color,
                        Group = "外观", DefaultValue = "#FFE03A2B",
                        Description = "故障时叶轮的颜色；红色 = 跳闸 / 过载 / 联锁断开",
                        TargetProperty = PumpElement.FaultColorProperty,
                    },
                    new()
                    {
                        Key = "Stroke", DisplayName = "轮廓色", Kind = ElementPropertyKind.Color,
                        Group = "外观", DefaultValue = "#FF5A5A5A",
                        TargetProperty = ScadaElementBase.StrokeProperty,
                    },
                    new()
                    {
                        Key = "StrokeThickness", DisplayName = "轮廓粗细", Kind = ElementPropertyKind.Number,
                        Group = "外观", DefaultValue = "1", Min = 0, Max = 20,
                        TargetProperty = ScadaElementBase.StrokeThicknessProperty,
                    },
                    new()
                    {
                        Key = "Text", DisplayName = "位号", Kind = ElementPropertyKind.Text,
                        Group = "文字", DefaultValue = string.Empty, IsBindable = true,
                        Description = "压在泵壳下方的一行字（如「P-101」）；管路图上靠它认设备",
                        TargetProperty = ScadaElementBase.TextProperty,
                    },
                    new()
                    {
                        Key = "Foreground", DisplayName = "文字颜色", Kind = ElementPropertyKind.Color,
                        Group = "文字", DefaultValue = "#FF202020",
                        Description = "默认深灰：位号落在泵壳之外、页面底色之上，白字在浅色页面上看不见",
                        TargetProperty = ScadaElementBase.ForegroundProperty,
                    },
                    new()
                    {
                        Key = "FontSize", DisplayName = "字号", Kind = ElementPropertyKind.Number,
                        Group = "文字", DefaultValue = "12", Min = 6, Max = 200,
                        TargetProperty = ScadaElementBase.FontSizeProperty,
                    },
                    new()
                    {
                        Key = "FontWeight", DisplayName = "字重", Kind = ElementPropertyKind.Choice,
                        Group = "文字", DefaultValue = "SemiBold",
                        Choices = new[] { "Normal", "SemiBold", "Bold" },
                        TargetProperty = ScadaElementBase.FontWeightProperty,
                    }),
            },

            new()
            {
                TypeKey = "Hmi.Motor",
                DisplayName = "电机",
                ControlType = typeof(MotorElement),
                Category = "工艺",
                DefaultWidth = 72,
                DefaultHeight = 80,
                Description = "管路上一台电机（圆机身里一个 M，顶上带接线盒）；状态绑变量，停机=灰、运行=绿、故障=红",
                Properties = GeometryProperties.With(
                    new()
                    {
                        Key = "State", DisplayName = "状态", Kind = ElementPropertyKind.Choice,
                        Group = "数据", DefaultValue = "Stopped",
                        Choices = new[] { "Stopped", "Running", "Fault" },
                        IsBindable = true,
                        Description = "运行态把工程变量绑到这里：整数 0=停机 1=运行 2=故障，写枚举名（Running/Fault）也认；" +
                                      "认不出的值按停机收敛，坏数据不会把整页渲染打断",
                        TargetProperty = MotorElement.StateProperty,
                    },
                    new()
                    {
                        Key = "TrackColor", DisplayName = "机身色", Kind = ElementPropertyKind.Color,
                        Group = "外观", DefaultValue = "#FF3A3A3A",
                        Description = "机身与接线盒的底色（M 之外露出来的那一层）",
                        TargetProperty = MotorElement.TrackColorProperty,
                    },
                    new()
                    {
                        Key = "StoppedColor", DisplayName = "停机色", Kind = ElementPropertyKind.Color,
                        Group = "外观", DefaultValue = "#FF7A7A7A",
                        Description = "停机时 M 的颜色；灰色 = 没在转，与泵、多态灯同一个语汇",
                        TargetProperty = MotorElement.StoppedColorProperty,
                    },
                    new()
                    {
                        Key = "RunningColor", DisplayName = "运行色", Kind = ElementPropertyKind.Color,
                        Group = "外观", DefaultValue = "#FF34C759",
                        Description = "运行时 M 的颜色；绿色 = 设备在转",
                        TargetProperty = MotorElement.RunningColorProperty,
                    },
                    new()
                    {
                        Key = "FaultColor", DisplayName = "故障色", Kind = ElementPropertyKind.Color,
                        Group = "外观", DefaultValue = "#FFE03A2B",
                        Description = "故障时 M 的颜色；红色 = 跳闸 / 过载 / 联锁断开",
                        TargetProperty = MotorElement.FaultColorProperty,
                    },
                    new()
                    {
                        Key = "Stroke", DisplayName = "轮廓色", Kind = ElementPropertyKind.Color,
                        Group = "外观", DefaultValue = "#FF5A5A5A",
                        TargetProperty = ScadaElementBase.StrokeProperty,
                    },
                    new()
                    {
                        Key = "StrokeThickness", DisplayName = "轮廓粗细", Kind = ElementPropertyKind.Number,
                        Group = "外观", DefaultValue = "1", Min = 0, Max = 20,
                        TargetProperty = ScadaElementBase.StrokeThicknessProperty,
                    },
                    new()
                    {
                        Key = "Text", DisplayName = "位号", Kind = ElementPropertyKind.Text,
                        Group = "文字", DefaultValue = string.Empty, IsBindable = true,
                        Description = "压在机身下方的一行字（如「M-101」）；管路图上靠它认设备",
                        TargetProperty = ScadaElementBase.TextProperty,
                    },
                    new()
                    {
                        Key = "Foreground", DisplayName = "文字颜色", Kind = ElementPropertyKind.Color,
                        Group = "文字", DefaultValue = "#FF202020",
                        Description = "默认深灰：位号落在机身之外、页面底色之上，白字在浅色页面上看不见",
                        TargetProperty = ScadaElementBase.ForegroundProperty,
                    },
                    new()
                    {
                        Key = "FontSize", DisplayName = "字号", Kind = ElementPropertyKind.Number,
                        Group = "文字", DefaultValue = "12", Min = 6, Max = 200,
                        TargetProperty = ScadaElementBase.FontSizeProperty,
                    },
                    new()
                    {
                        Key = "FontWeight", DisplayName = "字重", Kind = ElementPropertyKind.Choice,
                        Group = "文字", DefaultValue = "SemiBold",
                        Choices = new[] { "Normal", "SemiBold", "Bold" },
                        TargetProperty = ScadaElementBase.FontWeightProperty,
                    }),
            },

            new()
            {
                TypeKey = "Hmi.Pipe",
                DisplayName = "管道",
                ControlType = typeof(PipeElement),
                Category = "工艺",
                DefaultWidth = 160,
                DefaultHeight = 32,
                Description = "工艺流程图上的一段管子（管身 + 一组流向箭头）；流向可选正向/反向/不显示，状态绑变量后箭头随流动变色",
                Properties = GeometryProperties.With(
                    new()
                    {
                        Key = "Direction", DisplayName = "流向", Kind = ElementPropertyKind.Choice,
                        Group = "数据", DefaultValue = "LeftToRight",
                        Choices = new[] { "LeftToRight", "RightToLeft", "None" },
                        Description = "介质往哪走：LeftToRight=箭头朝右，RightToLeft=箭头朝左，None=只要管身不画箭头；" +
                                      "配管定死的走向，属于设计期属性，所以不参与变量绑定",
                        TargetProperty = PipeElement.DirectionProperty,
                    },
                    new()
                    {
                        Key = "State", DisplayName = "流动状态", Kind = ElementPropertyKind.Choice,
                        Group = "数据", DefaultValue = "Stopped",
                        Choices = new[] { "Stopped", "Running", "Fault" },
                        IsBindable = true,
                        Description = "运行态把工程变量绑到这里：整数 0=停流 1=流动 2=异常，写枚举名（Running/Fault）也认；" +
                                      "认不出的值按停流收敛，坏数据不会把整页渲染打断",
                        TargetProperty = PipeElement.StateProperty,
                    },
                    new()
                    {
                        Key = "TrackColor", DisplayName = "管身色", Kind = ElementPropertyKind.Color,
                        Group = "外观", DefaultValue = "#FF3A3A3A",
                        Description = "管腔底色（箭头之外露出来的那一层）",
                        TargetProperty = PipeElement.TrackColorProperty,
                    },
                    new()
                    {
                        Key = "StoppedColor", DisplayName = "停流色", Kind = ElementPropertyKind.Color,
                        Group = "外观", DefaultValue = "#FF7A7A7A",
                        Description = "停流时箭头的颜色；灰色 = 介质没在走，与泵、电机、多态灯同一个语汇",
                        TargetProperty = PipeElement.StoppedColorProperty,
                    },
                    new()
                    {
                        Key = "RunningColor", DisplayName = "流动色", Kind = ElementPropertyKind.Color,
                        Group = "外观", DefaultValue = "#FF34C759",
                        Description = "流动时箭头的颜色；绿色 = 介质在走",
                        TargetProperty = PipeElement.RunningColorProperty,
                    },
                    new()
                    {
                        Key = "FaultColor", DisplayName = "异常色", Kind = ElementPropertyKind.Color,
                        Group = "外观", DefaultValue = "#FFE03A2B",
                        Description = "异常时箭头的颜色；红色 = 堵管 / 超压 / 联锁断开",
                        TargetProperty = PipeElement.FaultColorProperty,
                    },
                    new()
                    {
                        Key = "Stroke", DisplayName = "管壁色", Kind = ElementPropertyKind.Color,
                        Group = "外观", DefaultValue = "#FF5A5A5A",
                        TargetProperty = ScadaElementBase.StrokeProperty,
                    },
                    new()
                    {
                        Key = "StrokeThickness", DisplayName = "管壁粗细", Kind = ElementPropertyKind.Number,
                        Group = "外观", DefaultValue = "1", Min = 0, Max = 20,
                        TargetProperty = ScadaElementBase.StrokeThicknessProperty,
                    }),
            },

            new()
            {
                TypeKey = "Hmi.AlarmBanner",
                DisplayName = "报警条",
                ControlType = typeof(AlarmBannerElement),
                Category = "报警",
                DefaultWidth = 320,
                DefaultHeight = 140,
                Description = "把运行态挂着的报警按严重度排成一列实时刷新：严重且未确认时左侧色条会闪，" +
                              "表头同时给出未确认条数与总条数；没有任何报警时显示「系统正常」",
                Properties = GeometryProperties.With(
                    new()
                    {
                        Key = "MaxRows", DisplayName = "最多显示", Kind = ElementPropertyKind.Number,
                        Group = "数据", DefaultValue = "5", Min = 1, Max = 50,
                        Description = "最多画几行，超出的不画（表头计数仍报全量）；" +
                                      "行数多到超出控件高度时会出现滚动条，所以这里不必留余量",
                        TargetProperty = AlarmBannerElement.MaxRowsProperty,
                    },
                    new()
                    {
                        Key = "EmptyText", DisplayName = "无报警提示", Kind = ElementPropertyKind.Text,
                        Group = "文字", DefaultValue = "系统正常",
                        Description = "一条报警都没有时显示的字；设计期预览看到的也是它",
                        TargetProperty = AlarmBannerElement.EmptyTextProperty,
                    },
                    new()
                    {
                        Key = "NormalColor", DisplayName = "正常色", Kind = ElementPropertyKind.Color,
                        Group = "状态", DefaultValue = "#FF34C759",
                        Description = "没有报警时左侧色条的颜色；与多态灯、泵、管道的「运行绿」是同一个色",
                        TargetProperty = AlarmBannerElement.NormalColorProperty,
                    },
                    new()
                    {
                        Key = "InfoColor", DisplayName = "提示色", Kind = ElementPropertyKind.Color,
                        Group = "状态", DefaultValue = "#FF3B82F6",
                        TargetProperty = AlarmBannerElement.InfoColorProperty,
                    },
                    new()
                    {
                        Key = "WarningColor", DisplayName = "警告色", Kind = ElementPropertyKind.Color,
                        Group = "状态", DefaultValue = "#FFFFB020",
                        Description = "警告级报警的颜色；与多态灯的「警告黄」同色，整幅画面一套语汇",
                        TargetProperty = AlarmBannerElement.WarningColorProperty,
                    },
                    new()
                    {
                        Key = "CriticalColor", DisplayName = "严重色", Kind = ElementPropertyKind.Color,
                        Group = "状态", DefaultValue = "#FFE03A2B",
                        Description = "严重级报警的颜色（也是触发闪烁的那一档）；与多态灯的「报警红」同色",
                        TargetProperty = AlarmBannerElement.CriticalColorProperty,
                    },
                    new()
                    {
                        Key = "Fill", DisplayName = "底色", Kind = ElementPropertyKind.Color,
                        Group = "外观", DefaultValue = "#FFFFFFFF",
                        TargetProperty = ScadaElementBase.FillProperty,
                    },
                    new()
                    {
                        Key = "Stroke", DisplayName = "边框色", Kind = ElementPropertyKind.Color,
                        Group = "外观", DefaultValue = "#FF7A7A7A",
                        TargetProperty = ScadaElementBase.StrokeProperty,
                    },
                    new()
                    {
                        Key = "StrokeThickness", DisplayName = "边框粗细", Kind = ElementPropertyKind.Number,
                        Group = "外观", DefaultValue = "1", Min = 0, Max = 20,
                        TargetProperty = ScadaElementBase.StrokeThicknessProperty,
                    },
                    new()
                    {
                        Key = "CornerRadius", DisplayName = "圆角", Kind = ElementPropertyKind.Number,
                        Group = "外观", DefaultValue = "4", Min = 0, Max = 200,
                        TargetProperty = ScadaElementBase.CornerRadiusProperty,
                    },
                    new()
                    {
                        Key = "Text", DisplayName = "标题", Kind = ElementPropertyKind.Text,
                        Group = "文字", DefaultValue = "实时报警",
                        Description = "表头左侧那一行字；留空则表头只剩右侧的计数",
                        TargetProperty = ScadaElementBase.TextProperty,
                    },
                    new()
                    {
                        Key = "Foreground", DisplayName = "文字色", Kind = ElementPropertyKind.Color,
                        Group = "文字", DefaultValue = "#FF202020",
                        Description = "标题与各行文字的颜色；行内的次要信息（时间、条件、状态）按七成不透明度显示",
                        TargetProperty = ScadaElementBase.ForegroundProperty,
                    },
                    new()
                    {
                        Key = "FontSize", DisplayName = "字号", Kind = ElementPropertyKind.Number,
                        Group = "文字", DefaultValue = "12", Min = 6, Max = 200,
                        Description = "表头与所有行的字号：调大给大屏用时，行高、徽标、时间会一起放大，不会错位",
                        TargetProperty = ScadaElementBase.FontSizeProperty,
                    },
                    new()
                    {
                        Key = "FontWeight", DisplayName = "字重", Kind = ElementPropertyKind.Choice,
                        Group = "文字", DefaultValue = "SemiBold",
                        Choices = new[] { "Normal", "SemiBold", "Bold" },
                        Description = "报警名与标题的字重；行内次要信息固定用常规字重，层级靠字重与透明度拉开",
                        TargetProperty = ScadaElementBase.FontWeightProperty,
                    }),
            },
        };
    }
}
