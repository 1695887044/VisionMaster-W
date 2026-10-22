using System.Collections.Generic;
using VisionMaster.Scada;

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
    /// <b>「布局」与「外观」两组一律不声明可绑</b>——它们是设计期语言（版面 + 皮肤），
    /// 交给变量驱动只会让画面自己乱变；可绑的是数值 / 状态 / 文字这类运行期语言。
    /// 口径由 <see cref="ScadaPropertyGroups.NonBindable"/> 在注册期兜底校验（见
    /// <see cref="ElementDescriptor.Validate"/>），写错会直接注册失败。
    /// 理由见 <see cref="GeometryProperties"/> 的类注释。
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
                // 纯打包图元：它的全部属性都指向基类/Control 已有的依赖属性，
                // 没有一条私有依赖属性 → 不需要任何 [ScadaProperty] 声明，描述符全由打包助手产出。
                Properties = GeometryProperties.WithGroups(
                    AppearanceProperties.Fill("#FFFFFFFF"),
                    AppearanceProperties.Stroke("#FF7A7A7A"),
                    AppearanceProperties.StrokeThickness(),
                    AppearanceProperties.CornerRadius("0", description: "半径超过短边一半时 WPF 会自行收敛成胶囊形"),
                    TextProperties.Text(),
                    TextProperties.Foreground(),
                    TextProperties.FontSize()),
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
                Properties = GeometryProperties.WithGroups(
                    AppearanceProperties.Fill("#FFFFFFFF"),
                    AppearanceProperties.Stroke("#FF7A7A7A"),
                    AppearanceProperties.StrokeThickness(),
                    TextProperties.Text(),
                    TextProperties.Foreground(),
                    TextProperties.FontSize()),
            },

            new()
            {
                TypeKey = "Hmi.Text",
                DisplayName = "文本",
                ControlType = typeof(TextElement),
                Category = "基础",
                DefaultWidth = 120,
                DefaultHeight = 24,
                Description = "静态标签：标题、说明、单位。它不认识数值，也不接变量——要显示会变的数就用「数值域」",
                Properties = GeometryProperties.WithGroups(
                    // 静态标签刻意<b>不</b>可绑：它存在的意义就是"不接变量"，要绑就该用「数值域」。
                    TextProperties.Text("内容", "文本", isBindable: false,
                        kind: ElementPropertyKind.MultilineText),
                    TextProperties.Foreground(),
                    TextProperties.FontSize("14"),
                    TextProperties.FontWeight(description: "标题常用 Bold；Normal 与 SemiBold 在小字号下区别不大"),
                    TextProperties.TextAlignment(),
                    TextProperties.VerticalContentAlignment(
                        description: "Stretch 不列出：多行文本在 Stretch 下与 Top 表现一致，列出来只会让人犹豫")),
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
                Properties = GeometryProperties.WithGroups(
                    TextProperties.Text("文字", "按钮"),
                    AppearanceProperties.Fill("#FF2D7DD2"),
                    AppearanceProperties.Stroke("#FF1F5C9E"),
                    AppearanceProperties.StrokeThickness(),
                    AppearanceProperties.CornerRadius("3"),
                    TextProperties.Foreground("#FFFFFFFF"),
                    TextProperties.FontSize(),
                    TextProperties.FontWeight()),
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
                Properties = GeometryProperties.WithGroups(
                    ScadaPropertyCatalog.Collect(typeof(BitButtonElement)),
                    TextProperties.Text("文字", "按钮",
                        description: "两种状态文字都留空时的共用文字（如「启动/停止」同一个标签）"),
                    TextProperties.FontSize(),
                    TextProperties.FontWeight(),
                    AppearanceProperties.Stroke("#FF1F5C9E"),
                    AppearanceProperties.StrokeThickness(),
                    AppearanceProperties.CornerRadius("3")),
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
                Properties = GeometryProperties.WithGroups(
                    ScadaPropertyCatalog.Collect(typeof(IndicatorElement)),
                    AppearanceProperties.Stroke("#FF5A5A5A"),
                    AppearanceProperties.StrokeThickness(),
                    TextProperties.Text("标签", description: "显示在灯下方的一行字（如「1#电机」）"),
                    TextProperties.Foreground(displayName: "标签颜色"),
                    TextProperties.FontSize(displayName: "标签字号")),
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
                Properties = GeometryProperties.WithGroups(
                    ScadaPropertyCatalog.Collect(typeof(ProgressBarElement)),
                    AppearanceProperties.Fill("#FF2D7DD2", "已填充那一段的颜色"),
                    AppearanceProperties.Stroke("#FF5A5A5A"),
                    AppearanceProperties.StrokeThickness(),
                    AppearanceProperties.CornerRadius("2"),
                    TextProperties.Foreground("#FFFFFFFF", displayName: "数值颜色"),
                    TextProperties.FontSize("11", displayName: "数值字号"),
                    TextProperties.FontWeight("Bold", displayName: "数值字重",
                        description: "默认 Bold：条上的字压在填充色上，不加粗在小尺寸下容易糊")),
            },

            new()
            {
                TypeKey = "Hmi.Slider",
                DisplayName = "模拟量滑块",
                ControlType = typeof(SliderElement),
                Category = "数值",
                DefaultWidth = 280,
                DefaultHeight = 48,
                Description = "连续量拖动即写：拖动只改显示，松手写一次变量（也可选 100ms 节流）；写失败回弹并显示原因。" +
                              "把「当前值」绑到变量上即可；量程、步长、走向、只读/读写、写入前确认都在属性面板",
                Events = new[] { ScadaEventType.InputCompleted },
                Properties = GeometryProperties.WithGroups(
                    ScadaPropertyCatalog.Collect(typeof(SliderElement)),
                    AppearanceProperties.Fill("#FF2D7DD2", "已填充那一段的颜色"),
                    AppearanceProperties.Stroke("#FF5A5A5A"),
                    AppearanceProperties.StrokeThickness(),
                    AppearanceProperties.CornerRadius("4"),
                    TextProperties.Foreground("#FFE6EEF7", displayName: "数值颜色"),
                    TextProperties.FontSize("12", displayName: "数值字号"),
                    TextProperties.FontWeight()),
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
                              "模式选「InputOutput」后，运行态点一下框子就能就地改数并写回变量（还需当前角色的权限够，见属性面板「交互」那一行）",
                // 手册 7.5.2 把"输入完成时"挂在数字IO域 / 字符IO域 / 日期时间域上；本库当前只有
                // 这一个可输入图元，所以只在这一条描述符上声明。将来加了字符域/日期域，各自加一行即可。
                Events = new[] { ScadaEventType.InputCompleted },
                Properties = GeometryProperties.WithGroups(
                    ScadaPropertyCatalog.Collect(typeof(IOFieldElement)),
                    TextProperties.Text("说明字",
                        description: "显示在数值左边的一行字（如「温度」「1#工位」）；留空则数值独占整行"),
                    TextProperties.Foreground("#FFE6EEF7", displayName: "文字颜色",
                        description: "默认浅色：运行画面是深色底，深色字会糊成一片"),
                    TextProperties.FontSize("14"),
                    TextProperties.FontWeight(),
                    TextProperties.TextAlignment("Right", displayName: "数值对齐",
                        description: "默认 Right：小数点在右边排成一条线，一列数值扫起来才快；说明字不受它影响，恒靠左"),
                    AppearanceProperties.Fill("#00FFFFFF",
                        "框内底色；默认全透明（只读态露出画面底色，可写态靠 1px 边框辨识）"),
                    AppearanceProperties.Stroke("#FF7A7A7A"),
                    AppearanceProperties.StrokeThickness(),
                    AppearanceProperties.CornerRadius("2")),
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
                Properties = GeometryProperties.WithGroups(
                    ScadaPropertyCatalog.Collect(typeof(LampElement)),
                    AppearanceProperties.Stroke("#FF5A5A5A"),
                    AppearanceProperties.StrokeThickness(),
                    TextProperties.Text("标签", description: "显示在灯下方的一行字（如「1#主轴」）"),
                    TextProperties.Foreground(displayName: "标签颜色"),
                    TextProperties.FontSize(displayName: "标签字号"),
                    TextProperties.FontWeight(displayName: "标签字重")),
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
                Properties = GeometryProperties.WithGroups(
                    ScadaPropertyCatalog.Collect(typeof(ClockElement)),
                    TextProperties.Text("标签", description: "显示在时间下方的一行字（如「系统时间」「1#线」）；留空则只显时间"),
                    TextProperties.Foreground(),
                    TextProperties.FontSize("16"),
                    TextProperties.FontWeight("SemiBold",
                        description: "默认 SemiBold：时间是要一眼扫到的信息，全 Normal 在深色底上偏虚"),
                    TextProperties.TextAlignment()),
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
                Properties = GeometryProperties.WithGroups(
                    ScadaPropertyCatalog.Collect(typeof(GaugeElement)),
                    AppearanceProperties.Fill("#FFE03A2B",
                        "指针与中心轴环的颜色；深色盘面上红色指针一眼扫得到", displayName: "指针色"),
                    AppearanceProperties.Stroke("#FF5A5A5A", displayName: "外圈色"),
                    AppearanceProperties.StrokeThickness(displayName: "外圈粗细"),
                    TextProperties.Foreground("#FFFFFFFF", displayName: "数值颜色"),
                    TextProperties.FontSize("13", displayName: "数值字号"),
                    TextProperties.FontWeight("SemiBold", displayName: "数值字重",
                        description: "默认 SemiBold：读数压在盘面上，全 Normal 在小表盘上偏虚"),
                    TextProperties.TextAlignment("Center", displayName: "数值对齐")),
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
                Properties = GeometryProperties.WithGroups(
                    ScadaPropertyCatalog.Collect(typeof(ValveElement)),
                    AppearanceProperties.Stroke("#FF5A5A5A", displayName: "轮廓色"),
                    AppearanceProperties.StrokeThickness(displayName: "轮廓粗细"),
                    TextProperties.Text("位号",
                        description: "压在阀体上方凹口的一行字（如「FV-101」）；管路图上靠它认设备"),
                    TextProperties.Foreground("#FFFFFFFF", displayName: "文字颜色",
                        description: "默认白色：文字压在深色阀体上，深色字会糊成一片"),
                    TextProperties.FontSize(),
                    TextProperties.FontWeight("SemiBold")),
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
                Properties = GeometryProperties.WithGroups(
                    AppearanceProperties.Rename(ScadaPropertyCatalog.Collect(typeof(PumpElement)),
                        ("State", null, "运行态把工程变量绑到这里：整数 0=停机 1=运行 2=故障，" +
                                        "写枚举名（Running/Fault）也认；认不出的值按停机收敛，坏数据不会把整页渲染打断"),
                        ("TrackColor", "泵壳色", "泵壳（叶轮之外露出来的那一圈）的底色"),
                        ("StoppedColor", "停机色", "停机时叶轮的颜色；灰色 = 没在转，与多态灯的熄灭色同一个语汇"),
                        ("RunningColor", "运行色", "运行时叶轮的颜色；绿色 = 设备在转"),
                        ("FaultColor", "故障色", "故障时叶轮的颜色；红色 = 跳闸 / 过载 / 联锁断开")),
                    AppearanceProperties.Stroke("#FF5A5A5A", displayName: "轮廓色"),
                    AppearanceProperties.StrokeThickness(displayName: "轮廓粗细"),
                    TextProperties.Text("位号",
                        description: "压在泵壳下方的一行字（如「P-101」）；管路图上靠它认设备"),
                    TextProperties.Foreground("#FF202020", displayName: "文字颜色",
                        description: "默认深灰：位号落在泵壳之外、页面底色之上，白字在浅色页面上看不见"),
                    TextProperties.FontSize(),
                    TextProperties.FontWeight("SemiBold")),
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
                Properties = GeometryProperties.WithGroups(
                    AppearanceProperties.Rename(ScadaPropertyCatalog.Collect(typeof(MotorElement)),
                        ("State", null, "运行态把工程变量绑到这里：整数 0=停机 1=运行 2=故障，" +
                                        "写枚举名（Running/Fault）也认；认不出的值按停机收敛，坏数据不会把整页渲染打断"),
                        ("TrackColor", "机身色", "机身与接线盒的底色（M 之外露出来的那一层）"),
                        ("StoppedColor", "停机色", "停机时 M 的颜色；灰色 = 没在转，与泵、多态灯同一个语汇"),
                        ("RunningColor", "运行色", "运行时 M 的颜色；绿色 = 设备在转"),
                        ("FaultColor", "故障色", "故障时 M 的颜色；红色 = 跳闸 / 过载 / 联锁断开")),
                    AppearanceProperties.Stroke("#FF5A5A5A", displayName: "轮廓色"),
                    AppearanceProperties.StrokeThickness(displayName: "轮廓粗细"),
                    TextProperties.Text("位号",
                        description: "压在机身下方的一行字（如「M-101」）；管路图上靠它认设备"),
                    TextProperties.Foreground("#FF202020", displayName: "文字颜色",
                        description: "默认深灰：位号落在机身之外、页面底色之上，白字在浅色页面上看不见"),
                    TextProperties.FontSize(),
                    TextProperties.FontWeight("SemiBold")),
            },

            new()
            {
                TypeKey = "Hmi.Pipe",
                DisplayName = "管道",
                ControlType = typeof(PipeElement),
                Category = "工艺",
                DefaultWidth = 160,
                DefaultHeight = 32,
                Description = "工艺流程图上的一段管子（管身 + 一组流向箭头）；流向可选正向/反向/无，状态绑变量后箭头随流动变色",
                Properties = GeometryProperties.WithGroups(
                    AppearanceProperties.Rename(ScadaPropertyCatalog.Collect(typeof(PipeElement)),
                        ("State", "流动状态", "运行态把工程变量绑到这里：整数 0=停流 1=流动 2=异常，" +
                                              "写枚举名（Running/Fault）也认；认不出的值按停流收敛，坏数据不会把整页渲染打断"),
                        ("TrackColor", "管身色", "管腔底色（箭头之外露出来的那一层）"),
                        ("StoppedColor", "停流色", "停流时箭头的颜色；灰色 = 介质没在走，与泵、电机、多态灯同一个语汇"),
                        ("RunningColor", "流动色", "流动时箭头的颜色；绿色 = 介质在走"),
                        ("FaultColor", "异常色", "异常时箭头的颜色；红色 = 堵管 / 超压 / 联锁断开")),
                    AppearanceProperties.Stroke("#FF5A5A5A", displayName: "管壁色"),
                    AppearanceProperties.StrokeThickness(displayName: "管壁粗细")),
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
                Properties = GeometryProperties.WithGroups(
                    ScadaPropertyCatalog.Collect(typeof(AlarmBannerElement)),
                    AppearanceProperties.Fill("#FF151C23", displayName: "底色",
                        description: "默认深色表面：报警条通常钉在深色运行画面上"),
                    AppearanceProperties.Stroke("#FF2A3745"),
                    AppearanceProperties.StrokeThickness(),
                    AppearanceProperties.CornerRadius("8"),
                    TextProperties.Text("标题", "实时报警",
                        description: "表头左侧那一行字；留空则表头只剩右侧的计数"),
                    TextProperties.Foreground("#FFE6EEF7", displayName: "文字色",
                        description: "标题与各行文字的颜色；行内的次要信息（时间、条件、状态）按七成不透明度显示"),
                    TextProperties.FontSize(
                        description: "表头与所有行的字号：调大给大屏用时，行高、徽标、时间会一起放大，不会错位"),
                    TextProperties.FontWeight("SemiBold",
                        description: "报警名与标题的字重；行内次要信息固定用常规字重，层级靠字重与透明度拉开")),
            },

            new()
            {
                TypeKey = "Hmi.Panel",
                DisplayName = "底板",
                ControlType = typeof(PanelElement),
                Category = "基础",
                DefaultWidth = 360,
                DefaultHeight = 240,
                Description = "一块能把别的图元装进去的面板（带标题的区域框）。"
                            + "把图元拖到它上面即成它的子图元：超出边界会被裁掉，拖动底板时整块一起走。"
                            + "底板不能再装进另一只底板（只支持一层）",
                Properties = GeometryProperties.WithGroups(
                    ScadaPropertyCatalog.Collect(typeof(PanelElement)),
                    AppearanceProperties.Fill("#FF1E2530", "默认深蓝灰：底板上通常压着浅色的设备符号，深底才不会让它们糊在一起"),
                    AppearanceProperties.Stroke("#FF3A4455"),
                    AppearanceProperties.StrokeThickness(),
                    AppearanceProperties.CornerRadius("3")),
            },

            new()
            {
                TypeKey = "Hmi.ImageView",
                DisplayName = "图像显示",
                ControlType = typeof(ImageViewElement),
                Category = "基础",
                DefaultWidth = 200,
                DefaultHeight = 160,
                Description = "把一张位图摆到画面上：绑一个图像变量，流程每次产出新图就自动刷新；" +
                              "还没绑变量时显示「无图像」占位提示",
                Properties = GeometryProperties.WithGroups(
                    ScadaPropertyCatalog.Collect(typeof(ImageViewElement)),
                    AppearanceProperties.Fill("#00FFFFFF", "图像背后的底色；默认全透明，让图像直接落在画面上",
                        displayName: "底色"),
                    AppearanceProperties.Stroke("#FF7A7A7A"),
                    AppearanceProperties.StrokeThickness(),
                    AppearanceProperties.CornerRadius("0")),
            },

            new()
            {
                TypeKey = "Hmi.AxisConsole",
                DisplayName = "轴控台",
                ControlType = typeof(AxisPanelElement),
                Category = "运动",
                DefaultWidth = 320,
                DefaultHeight = 470,
                Description = "完整的轴调试台：状态（位置/速度/使能/到位/限位/报警）+ 操作（使能/点动/回零/停止/清报警）" +
                              "+ 点位表（示教/定位/改速度）。运动与监控共用同一个轴选择器，不会出现「看的是 X、动的是 Y」",
                Properties = GeometryProperties.WithGroups(
                    new[]
                    {
                        new ElementPropertyDescriptor
                        {
                            Key = ScadaAxisOptionsCodec.Key,
                            DisplayName = "候选轴",
                            Kind = ElementPropertyKind.AxisList,
                            Group = ScadaPropertyGroups.Data,
                            DefaultValue = string.Empty,
                            IsBindable = false,
                            Description = "整个轴控台（监控 + 运动 + 点位）共用的候选轴；只能从工程里已启用的轴中多选",
                        },
                    },
                    // 预设：点位区默认打开（同一个控件类，只覆盖默认值）
                    ScadaPropertyCatalog.WithDefaults(
                        ScadaPropertyCatalog.Collect(typeof(AxisPanelElement)),
                        ("ShowPoints", "True")),
                    AppearanceProperties.Fill("#FF0F1112", "卡片底色；参考模板的近黑控制台"),
                    AppearanceProperties.Stroke("#FF15171A"),
                    AppearanceProperties.StrokeThickness(),
                    AppearanceProperties.CornerRadius("12"),
                    TextProperties.Text("标题", "轴控", isBindable: false,
                        description: "面板左上角的标题；留空则只剩选择器"),
                    TextProperties.Foreground("#FFFAFAFA"),
                    TextProperties.FontSize("12")),
            },
            new()
            {
                TypeKey = "Hmi.AxisPanel",
                DisplayName = "轴面板",
                ControlType = typeof(AxisPanelElement),
                Category = "运动",
                DefaultWidth = 280,
                DefaultHeight = 280,
                Description = "紧凑预设：只显示状态与手动操作（使能/点动/回零/停止/清报警），不显示点位表。" +
                              "需要点位时用「轴控台」",
                Properties = GeometryProperties.WithGroups(
                    new[]
                    {
                        new ElementPropertyDescriptor
                        {
                            Key = ScadaAxisOptionsCodec.Key,
                            DisplayName = "候选轴",
                            Kind = ElementPropertyKind.AxisList,
                            Group = ScadaPropertyGroups.Data,
                            DefaultValue = string.Empty,
                            IsBindable = false,
                            Description = "运行期下拉框里的轴。只能从工程里已启用的轴中多选；" +
                                          "顺序即下拉顺序，默认选中第一根。单根轴时下拉框收起，只显示轴名",
                        },
                    },
                    // 预设：点位区默认收起（元素上的 [ScadaProperty] 默认值就是 False，这里不覆盖）
                    ScadaPropertyCatalog.Collect(typeof(AxisPanelElement)),
                    AppearanceProperties.Fill("#FF0F1112", "卡片底色；参考模板的近黑控制台"),
                    AppearanceProperties.Stroke("#FF15171A"),
                    AppearanceProperties.StrokeThickness(),
                    AppearanceProperties.CornerRadius("12"),
                    TextProperties.Text("标题", "轴控", isBindable: false,
                        description: "面板左上角的标题；留空则只剩选择器"),
                    TextProperties.Foreground("#FFFAFAFA"),
                    TextProperties.FontSize("12")),
            },
            new()
            {
                // 旧 TypeKey：老方案里已有这种图元，必须能打开、行为不变；
                // 但工具箱不再列出（新用户请用「轴控台」）。
                TypeKey = "Hmi.AxisPointTable",
                DisplayName = "轴点位表（旧）",
                ControlType = typeof(AxisPanelElement),
                Category = "运动",
                DefaultWidth = 340,
                DefaultHeight = 300,
                ShowInToolbox = false,
                Description = "旧版独立点位表。已合并进「轴控台」——这里保留只为打开旧方案：" +
                              "预设为显示操作与点位（与旧版行为一致）",
                Properties = GeometryProperties.WithGroups(
                    new[]
                    {
                        new ElementPropertyDescriptor
                        {
                            Key = ScadaAxisOptionsCodec.Key,
                            DisplayName = "候选轴",
                            Kind = ElementPropertyKind.AxisList,
                            Group = ScadaPropertyGroups.Data,
                            DefaultValue = string.Empty,
                            IsBindable = false,
                            Description = "点位表显示哪几根轴的点位；运行期用下拉框切换。单根轴时下拉框收起",
                        },
                    },
                    // 旧版点位表有示教/定位/改速度，所以操作与点位都打开。
                    ScadaPropertyCatalog.WithDefaults(
                        ScadaPropertyCatalog.Collect(typeof(AxisPanelElement)),
                        ("ShowCommands", "True"),
                        ("ShowPoints", "True")),
                    AppearanceProperties.Fill("#FF0F1112", "卡片底色；与轴面板同一套暗色控制台"),
                    AppearanceProperties.Stroke("#FF15171A"),
                    AppearanceProperties.StrokeThickness(),
                    AppearanceProperties.CornerRadius("12"),
                    TextProperties.Text("标题", "点位表", isBindable: false),
                    TextProperties.Foreground("#FFFAFAFA"),
                    TextProperties.FontSize("11")),
            },

            new()
            {
                TypeKey = "Hmi.Tank",
                DisplayName = "罐体/料仓",
                ControlType = typeof(TankElement),
                Category = "工艺",
                DefaultWidth = 160,
                DefaultHeight = 200,
                Description = "带液位填充的罐体/料仓符号；把「液位」绑到变量上即可，" +
                              "读数按「容量 × 液位」显示（液位 50% × 容量 1000 L = 500 L）",
                Properties = GeometryProperties.WithGroups(
                    ScadaPropertyCatalog.Collect(typeof(TankElement)),
                    // Fill 在这个图元里是「液体色」，不是罐体底色；罐体空腔由 TrackColor 负责。
                    AppearanceProperties.Fill("#FF38BDF8", "液位填充色（液体那一段）", displayName: "液位色"),
                    AppearanceProperties.Stroke("#FF3A4A5C", displayName: "罐体轮廓色"),
                    AppearanceProperties.StrokeThickness(displayName: "轮廓粗细"),
                    AppearanceProperties.CornerRadius("12",
                        description: "罐体圆角半径；超过短边一半时 WPF 会自行收敛成胶囊形"),
                    TextProperties.Text("位号",
                        description: "压在罐体下方的一行字（如「T-101」）；工艺图上靠它认设备"),
                    TextProperties.Foreground("#FFE6EEF7", displayName: "文字颜色",
                        description: "默认浅色：位号与读数压在深色罐体/深色画面上，深色字会糊成一片"),
                    TextProperties.FontSize(),
                    TextProperties.FontWeight("SemiBold")),
            },

            new()
            {
                TypeKey = "Hmi.KpiBoard",
                DisplayName = "KPI 看板",
                ControlType = typeof(KpiBoardElement),
                Category = "数值",
                DefaultWidth = 360,
                DefaultHeight = 180,
                Description = "把 2~4 组「数值 + 标签 + 单位」排成一块卡片（OEE、产量、良率、停机时长）；" +
                              "四组数值都能单独绑变量，列数可选 2×2 或 4×1",
                Properties = GeometryProperties.WithGroups(
                    ScadaPropertyCatalog.Collect(typeof(KpiBoardElement)),
                    AppearanceProperties.Fill("#FF151C23", "卡片底色；深色运行画面用 surface 档"),
                    AppearanceProperties.Stroke("#FF2A3745"),
                    AppearanceProperties.StrokeThickness(),
                    AppearanceProperties.CornerRadius("8"),
                    TextProperties.Text("标题",
                        description: "卡片左上角的标题（如「2# 灌装单元」）；留空则只显示四格数据"),
                    TextProperties.Foreground("#FFE6EEF7", displayName: "文字颜色",
                        description: "数值与标题的主色；标签与单位用弱化色，层级靠透明度拉开")),
            },

            // ==================================================================
            //  Phase 2 P0：输入/选择/报警确认四件套
            // ==================================================================

            new()
            {
                TypeKey = "Hmi.SelectorSwitch",
                DisplayName = "多档旋钮",
                ControlType = typeof(SelectorSwitchElement),
                Category = "操作",
                DefaultWidth = 120,
                DefaultHeight = 36,
                Description = "2~4 档保持式转换开关：点哪一档就把档位号写回变量（手/自动、本地/远程、工位选择）。" +
                              "选中档用「填充色」高亮、未选档用「未选中底色」；写失败时原因留在图元上，不会假装已经拨过去",
                // 刻意不声明 Pressed：画布在运行态已对命中的图元统一发一次 Pressed（见 ScadaCanvas.Interaction），
                // 本控件又按契约在“档位写成功”时发一次。两条同时可配会让同一个动作执行两遍；
                // 描述符不声明它，属性面板就不会长出这条永远双响的钩子（控件仍按契约在成功路径 raise）。
                Properties = GeometryProperties.WithGroups(
                    ScadaPropertyCatalog.Collect(typeof(SelectorSwitchElement)),
                    AppearanceProperties.Fill("#FF15803D",
                        "选中档位的底色（设计基线的 success-fill 深绿；与白色文字对比足够）"),
                    AppearanceProperties.Stroke("#FF2A3745"),
                    AppearanceProperties.StrokeThickness(),
                    AppearanceProperties.CornerRadius("8"),
                    TextProperties.FontSize(),
                    TextProperties.FontWeight("SemiBold")),
            },

            new()
            {
                TypeKey = "Hmi.TextField",
                DisplayName = "文本输入域",
                ControlType = typeof(TextFieldElement),
                Category = "数值",
                DefaultWidth = 160,
                DefaultHeight = 32,
                Description = "字符串输入域：绑一个字符串变量，点一下就地编辑，回车/失焦提交、Esc 放弃；" +
                              "提交时按 trim 后的长度校验（超长不写入），空串表示清除文本。批号、操作备注、配方名用它",
                Events = new[] { ScadaEventType.InputCompleted },
                Properties = GeometryProperties.WithGroups(
                    ScadaPropertyCatalog.Collect(typeof(TextFieldElement)),
                    AppearanceProperties.Fill("#FF151C23", "输入框底色；深色运行画面用 surface 档"),
                    AppearanceProperties.Stroke("#FF3A4A5C"),
                    AppearanceProperties.StrokeThickness(),
                    AppearanceProperties.CornerRadius("4"),
                    TextProperties.Text("标签", "",
                        description: "显示在输入框左侧的一行说明字（如「批号」）；留空则输入框独占整行"),
                    TextProperties.Foreground("#FFE6EEF7", displayName: "文字颜色",
                        description: "默认浅色：运行画面是深色底，深色字会糊成一片"),
                    TextProperties.FontSize(),
                    TextProperties.FontWeight()),
            },

            new()
            {
                TypeKey = "Hmi.ComboBox",
                DisplayName = "下拉选择器（枚举型）",
                ControlType = typeof(ComboBoxElement),
                Category = "数值",
                DefaultWidth = 160,
                DefaultHeight = 32,
                Description = "枚举型下拉：绑一个 int 变量，用「选项映射」（0=手动;1=自动;2=检修）把数值翻译成显示名。" +
                              "用户选择后写回变量；运行态变量变化只更新显示、不回写（否则会形成回写环）。" +
                              "把「呈现方式」切到 Text 就是只读状态文本：不写变量、不带 chevron/原始值",
                Properties = GeometryProperties.WithGroups(
                    ScadaPropertyCatalog.WithVisibility(
                        ScadaPropertyCatalog.Collect(typeof(ComboBoxElement)),
                        ("ShowSeparator", "Presentation", "Text"),
                        ("Mode", "Presentation", "Field")),
                    ScadaPropertyCatalog.WithVisibility(
                        TextProperties.Text("标签", isBindable: false,
                            description: "Text 呈现时显示在值左侧的说明字；留空则值独占整行。" +
                                         "与 IOField 的「说明字」是同一个基类属性"),
                        ("Text", "Presentation", "Text")),
                    ScadaPropertyCatalog.WithVisibility(
                        TextProperties.TextAlignment("Left",
                            description: "Text 呈现的水平对齐；基类默认 Center，这里覆盖为 Left"),
                        ("TextAlignment", "Presentation", "Text")),
                    ScadaPropertyCatalog.WithVisibility(
                        AppearanceProperties.Fill("#FF151C23", "下拉框底色；深色运行画面用 surface 档"),
                        ("Fill", "Presentation", "Field")),
                    ScadaPropertyCatalog.WithVisibility(
                        AppearanceProperties.Stroke("#FF3A4A5C"),
                        ("Stroke", "Presentation", "Field")),
                    ScadaPropertyCatalog.WithVisibility(
                        AppearanceProperties.StrokeThickness(),
                        ("StrokeThickness", "Presentation", "Field")),
                    ScadaPropertyCatalog.WithVisibility(
                        AppearanceProperties.CornerRadius("4"),
                        ("CornerRadius", "Presentation", "Field")),
                    TextProperties.Foreground("#FFE6EEF7", displayName: "文字颜色",
                        description: "默认浅色：运行画面是深色底，深色字会糊成一片"),
                    TextProperties.FontSize("14"),
                    TextProperties.FontWeight()),
            },

            new()
            {
                TypeKey = "Hmi.AckButton",
                DisplayName = "报警确认按钮",
                ControlType = typeof(AckButtonElement),
                Category = "报警",
                DefaultWidth = 120,
                DefaultHeight = 36,
                Description = "一键确认报警：范围可选「全部」或「当前列表第一条」；按钮上可显示待确认条数，" +
                              "操作结果（已确认 N 条 / 没有待确认报警 / 没有权限）通过 CommandMessage 反馈",
                Properties = GeometryProperties.WithGroups(
                    ScadaPropertyCatalog.Collect(typeof(AckButtonElement)),
                    AppearanceProperties.Fill("#FFEF4444", "确认按钮底色；报警语义红，与报警条严重色同族"),
                    AppearanceProperties.Stroke("#FFB91C1C"),
                    AppearanceProperties.StrokeThickness(),
                    AppearanceProperties.CornerRadius("6"),
                    TextProperties.Text("按钮文字", "确认报警"),
                    TextProperties.Foreground("#FFFFFFFF"),
                    TextProperties.FontSize(),
                    TextProperties.FontWeight("SemiBold")),
            },

            // ---- 数据表格（2026-10-21，设计基线 docs/UI设计基线/scada-datagrid/）----
            //
            // 它刻意<b>不声明</b> Fill / Stroke / FontSize：外观由模板固定（深色画面里的一张表），
            // 声明了却不生效等于给用户一个"改了没反应"的假开关（R72 同族）。
            // 可配的只有"数据怎么来"（采行信号 / 确认 / 缓存 / 行高 / 行序）与"列长什么样"。
            new()
            {
                TypeKey = "Hmi.DataGrid",
                DisplayName = "数据表格",
                ControlType = typeof(DataGridElement),
                Category = "数据",
                DefaultWidth = 720,
                DefaultHeight = 300,
                Description = "把最近 N 条记录按列摆出来：列在设计期配（表头/宽度/对齐/格式/绑哪个变量），" +
                              "采行信号每来一个上升沿就采一行。它是显示缓冲、不是记录系统——" +
                              "要留痕请用「CSV 记录」插件",
                Properties = GeometryProperties.WithGroups(
                    new[]
                    {
                        new ElementPropertyDescriptor
                        {
                            Key = ScadaGridColumnsCodec.Key,
                            DisplayName = "列定义",
                            Kind = ElementPropertyKind.ColumnList,
                            Group = ScadaPropertyGroups.Data,
                            DefaultValue = string.Empty,
                            IsBindable = false,
                            Description = "列的规范 JSON：[{ \"Header\":\"SN\", \"Variable\":\"产品SN\", \"Width\":160, " +
                                          "\"Align\":\"left\", \"Format\":\"\", \"Monospace\":false }, …]。宽度 0 = 吃掉剩余宽度；最多 12 列",
                        },
                        new ElementPropertyDescriptor
                        {
                            Key = "SampleVariable",
                            DisplayName = "采行信号",
                            Kind = ElementPropertyKind.Text,
                            Group = ScadaPropertyGroups.Data,
                            DefaultValue = string.Empty,
                            IsBindable = false,
                            TargetProperty = DataGridElement.SampleVariableProperty,
                            Description = "这个变量每来一个上升沿（false→true）就采一行（现场一般是「结果已存」位）。" +
                                          "图元对它是只读的、不复位：信号由 PLC / 流程自己落",
                        },
                        new ElementPropertyDescriptor
                        {
                            Key = "AckVariable",
                            DisplayName = "收录确认",
                            Kind = ElementPropertyKind.Text,
                            Group = ScadaPropertyGroups.Data,
                            DefaultValue = string.Empty,
                            IsBindable = false,
                            TargetProperty = DataGridElement.AckVariableProperty,
                            Description = "留空 = 不写。填了就在每采一行后把它 +1（累加计数而不是脉冲：脉冲总得有人复位）",
                        },
                        new ElementPropertyDescriptor
                        {
                            Key = "MaxRows",
                            DisplayName = "缓存行数",
                            Kind = ElementPropertyKind.Number,
                            Group = ScadaPropertyGroups.Data,
                            DefaultValue = "100",
                            Min = 1,
                            Max = DataGridElement.MaxRowsLimit,
                            TargetProperty = DataGridElement.MaxRowsProperty,
                            Description = "超过就丢最旧的一行。它是显示缓冲，不是记录系统（断电即失）",
                        },
                        new ElementPropertyDescriptor
                        {
                            Key = "RowHeight",
                            DisplayName = "行高",
                            Kind = ElementPropertyKind.Number,
                            Group = ScadaPropertyGroups.Data,
                            DefaultValue = "28",
                            Min = 18,
                            Max = 60,
                            TargetProperty = DataGridElement.RowHeightProperty,
                        },
                        new ElementPropertyDescriptor
                        {
                            Key = "NewestFirst",
                            DisplayName = "新行在上",
                            Kind = ElementPropertyKind.Bool,
                            Group = ScadaPropertyGroups.Data,
                            DefaultValue = "True",
                            TargetProperty = DataGridElement.NewestFirstProperty,
                            Description = "最新一条永远第一行、不抢滚动——现场正在看历史时被拽走是最烦的",
                        },
                        new ElementPropertyDescriptor
                        {
                            Key = "Zebra",
                            DisplayName = "斑马纹",
                            Kind = ElementPropertyKind.Bool,
                            Group = ScadaPropertyGroups.Data,
                            DefaultValue = "True",
                            TargetProperty = DataGridElement.ZebraProperty,
                        },
                    },
                    TextProperties.Text("标题", "产品记录", isBindable: false,
                        description: "表格左上角的标题；留空则只剩右侧的行数")),
            },
        };
    }
}
