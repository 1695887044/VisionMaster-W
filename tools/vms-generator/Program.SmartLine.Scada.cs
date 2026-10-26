using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

internal static partial class Program
{
    // ==================================================================
    //  组态：15 页 / 50 类图元 / 报警 / 变量事件 / 文本列表
    // ==================================================================
    static JObject BuildLineScada()
        => new()
        {
            ["SchemaVersion"] = 1,
            ["Pages"] = new JArray
            {
                LineOverviewPage(),
                LineProcessPage(),
                LineSt1Page(),
                LineSt2Page(),
                LineSt3Page(),
                LineSt4Page(),
                LineSt5Page(),
                LineSt6Page(),
                LineReportPage(),
                LineAlarmPage(),
                LineDiagPage(),
                LineRecipePage(),
                LineConfirmPopup(),
                LineTracePopup(),
                LineHelpPopup(),
            },
            ["Alarms"] = LineAlarms(),
            ["VariableEvents"] = LineVariableEvents(),
            ["StartupPageId"] = G("page:产线总览").ToString(),
            ["BasePages"] = new JArray(),
            ["StateLists"] = LineStateLists(),
        };

    static JArray LineStateLists()
        => new()
        {
            new JObject
            {
                ["StateListId"] = G("state:station").ToString(),
                ["Name"] = "工位状态",
                ["DefaultText"] = "未知",
                ["DefaultImage"] = "",
                ["Entries"] = new JArray
                {
                    Entry(0, "停机", "#FF64748B"),
                    Entry(1, "运行", "#FF22C55E"),
                    Entry(2, "警告", "#FFF59E0B"),
                    Entry(3, "故障", "#FFEF4444"),
                },
            },
            new JObject
            {
                ["StateListId"] = G("state:mode").ToString(),
                ["Name"] = "运行模式",
                ["DefaultText"] = "未知",
                ["DefaultImage"] = "",
                ["Entries"] = new JArray
                {
                    Entry(0, "手动", "#FF64748B"),
                    Entry(1, "自动", "#FF22C55E"),
                    Entry(2, "检修", "#FFF59E0B"),
                },
            },
            new JObject
            {
                ["StateListId"] = G("state:product").ToString(),
                ["Name"] = "产品类型",
                ["DefaultText"] = "未定义",
                ["DefaultImage"] = "",
                ["Entries"] = new JArray
                {
                    Entry(0, "A 型", "#FF38BDF8"),
                    Entry(1, "B 型", "#FFA855F7"),
                    Entry(2, "C 型", "#FFF59E0B"),
                },
            },
        };

    static JObject Entry(int value, string text, string color)
        => new() { ["Value"] = value, ["Text"] = text, ["Image"] = "", ["Color"] = color };

    static JArray LineAlarms()
        => new()
        {
            ScadaAlarm("通讯-触发变量断线", "PLC_Trigger", 6, 0, 2, "PLC 触发变量超过 5 秒未刷新", "通讯", "Modbus 503 连接中断或 PLC 停止轮询", "检查虚拟 PLC 控制台与寄存器 HR0"),
            ScadaAlarm("通讯-S7 心跳丢失", "S7_Heartbeat", 5, 0, 1, "S7 心跳为假", "通讯", "S7 连接中断或 PLC 未置心跳", "检查 1102 端口与 M0.0"),
            ScadaAlarm("质量-缺陷数超限", "DefectCount", 0, 8, 1, "单件缺陷数超过 8", "质量", "来料表面异常或 Blob 阈值过松", "检查来料与表面检测参数"),
            ScadaAlarm("质量-累计不良超限", "NGCount", 0, 5, 1, "累计不良数超过 5", "质量", "连续 NG 或工艺漂移", "查看 CSV 账本与缺陷图"),
            ScadaAlarm("质量-良率过低", "YieldRate", 2, 95, 2, "实时良率低于 95%", "质量", "批量不良或检测误判", "核对检测参数与来料批次"),
            ScadaAlarm("设备-温度过高", "Temperature", 0, 40, 1, "环境温度超过 40℃", "设备", "散热异常", "检查电柜风扇与空调"),
            ScadaAlarm("设备-压力超限", "Pressure", 0, 0.8, 1, "压装压力超过 0.8MPa", "设备", "气路压力异常", "检查气源与减压阀"),
            ScadaAlarm("系统-未就绪", "SystemReady", 5, 0, 2, "系统未就绪", "系统", "未执行 Home 或运动卡离线", "执行回零并检查虚拟运动卡"),
            ScadaAlarm("系统-产线故障", "LineFault", 4, 0, 2, "产线故障位为真", "系统", "设备联锁或急停", "查看报警中心并复位"),
            ScadaAlarm("效率-节拍超时", "CycleTimeMs", 0, 5000, 1, "单件节拍超过 5 秒", "效率", "工位阻塞或通讯超时", "查看各工位耗时"),
        };

    static JArray LineVariableEvents()
        => new()
        {
            new JObject
            {
                ["VariableId"] = G("var:CurrentResult").ToString(),
                ["VariableName"] = "CurrentResult",
                ["UpperLimit"] = null,
                ["LowerLimit"] = null,
                ["IsEnabled"] = true,
                ["EventHooks"] = new JArray
                {
                    Hook(5, new JArray { ScadaAct(1, "本件判定 OK") }),
                    Hook(6, new JArray { ScadaAct(1, "本件判定 NG") }),
                },
            },
            new JObject
            {
                ["VariableId"] = G("var:LineFault").ToString(),
                ["VariableName"] = "LineFault",
                ["UpperLimit"] = null,
                ["LowerLimit"] = null,
                ["IsEnabled"] = true,
                ["EventHooks"] = new JArray
                {
                    Hook(5, new JArray
                    {
                        ScadaAct(1, "产线故障：打开报警中心"),
                        ScadaAct(19, pageName: "报警中心"),
                    }),
                },
            },
            new JObject
            {
                ["VariableId"] = G("var:YieldRate").ToString(),
                ["VariableName"] = "YieldRate",
                ["UpperLimit"] = null,
                ["LowerLimit"] = 90.0,
                ["IsEnabled"] = true,
                ["EventHooks"] = new JArray
                {
                    Hook(8, new JArray { ScadaAct(1, "良率低于 90%，请检查工艺") }),
                },
            },
        };

    // ---------------- 公共组态辅助 ----------------
    static JObject LinePage(string name, JArray els, bool popup = false)
        => ScadaPageObj(name, 1920, 1080, els, popup);

    static JObject Card(string page, string name, double x, double y, double w, double h, string title,
        string accent = "#FF22D3EE")
        => El(page, name, "Hmi.Panel", x, y, w, h, 5, new JObject
        {
            ["Fill"] = "#FF121A24",
            ["Stroke"] = "#FF263447",
            ["StrokeThickness"] = "1",
            ["CornerRadius"] = "12",
            ["TitleText"] = title,
            ["TitleHeight"] = "40",
            ["TitleFill"] = "#FF0F1722",
            ["TitleForeground"] = accent,
        });

    static JObject Txt(string page, string name, double x, double y, double w, double h, string text,
        double size, string color, string weight = "Normal", string align = "Left")
    {
        var p = TextProps(text, size, color, weight);
        p["TextAlignment"] = align;
        return El(page, name, "Hmi.Text", x, y, w, h, 10, p);
    }

    static JObject Val(string page, string name, double x, double y, double w, string varName, string label, string format = "F2")
        => IOField(page, name, x, y, varName, label, format);

    static JObject Btn(string page, string name, double x, double y, double w, double h, string text,
        string fill, int actionType, string flowName = null, string pageName = null,
        string varName = null, string value = null)
        => El(page, name, "Hmi.Button", x, y, w, h, 20, ButtonProps(text, fill, "#FF1E293B"), null,
            new JArray { Hook(2, new JArray { ScadaAct(actionType, text: null, varName: varName, value: value, pageName: pageName, flowName: flowName) }) });

    static JObject Nav(string page, string name, double x, double y, string target)
        => NavButton(page, name, x, y, target);

    static JObject StatusLamp(string page, string name, double x, double y, string varName, string label)
        => Lamp(page, name, x, y, varName, label);

    static JObject MiniIndicator(string page, string name, double x, double y, string varName, string label)
        => Indicator(page, name, x, y, varName, label);

    // ---------------- 1. 产线总览 ----------------
    static JObject LineOverviewPage()
    {
        const string page = "产线总览";
        var els = new JArray();
        ComplexHeader(els, page, "智能装配检测线 · 生产总览", "6 工位 · 定位 / 测量 / 检测 / 识别 · 全流程追溯");

        els.Add(El(page, "装饰圆", "Hmi.Ellipse", 1760, 150, 120, 120, 2, new JObject
        {
            ["Fill"] = "#2222D3EE", ["Stroke"] = "#0022D3EE", ["StrokeThickness"] = "0", ["Text"] = "", ["FontSize"] = "12",
        }));

        els.Add(El(page, "KPI", "Hmi.KpiBoard", 60, 180, 880, 180, 10, new JObject
        {
            ["Columns"] = "4",
            ["Label1"] = "良品", ["Value1"] = "0", ["Unit1"] = "pcs",
            ["Label2"] = "不良", ["Value2"] = "0", ["Unit2"] = "pcs",
            ["Label3"] = "良率", ["Value3"] = "100", ["Unit3"] = "%",
            ["Label4"] = "节拍", ["Value4"] = "0", ["Unit4"] = "次",
            ["Text"] = "实时生产 KPI",
            ["Fill"] = "#FF121A24", ["Stroke"] = "#FF263447", ["StrokeThickness"] = "1", ["CornerRadius"] = "12",
            ["Foreground"] = "#FFF8FAFC",
        }, new JArray
        {
            Bind("Value1", "OKCount"),
            Bind("Value2", "NGCount"),
            Bind("Value3", "YieldRate", "F1"),
            Bind("Value4", "CycleCount"),
        }));

        els.Add(El(page, "OEE表盘", "Hmi.Gauge", 960, 180, 180, 180, 10, new JObject
        {
            ["Value"] = "0", ["Minimum"] = "0", ["Maximum"] = "100", ["ValueFormat"] = "0.0", ["Unit"] = "%",
            ["ShowValue"] = "True", ["TrackColor"] = "#FF1E293B", ["ScaleColor"] = "#FF94A3B8",
            ["Fill"] = "#FFE03A2B", ["Stroke"] = "#FF475569", ["StrokeThickness"] = "1",
            ["Foreground"] = "#FFF8FAFC", ["FontSize"] = "14", ["FontWeight"] = "SemiBold",
        }, new JArray { Bind("Value", "OEE") }));

        els.Add(Card(page, "运行控制卡", 1160, 180, 700, 180, "运行控制", "#FF22D3EE"));
        els.Add(El(page, "产线启停", "Hmi.BitButton", 1185, 235, 150, 44, 20, new JObject
        {
            ["Text"] = "产线启停", ["OnText"] = "运行中", ["OffText"] = "已停止",
            ["Mode"] = "Toggle", ["OnFill"] = "#FF15803D", ["OffFill"] = "#FF334155",
            ["OnForeground"] = "#FFFFFFFF", ["OffForeground"] = "#FFE2E8F0",
            ["FontSize"] = "14", ["FontWeight"] = "SemiBold", ["CornerRadius"] = "8",
            ["Stroke"] = "#FF1F5C9E", ["StrokeThickness"] = "1",
        }, new JArray { Bind("IsOn", "LineRunning") }));
        els.Add(El(page, "运行模式", "Hmi.SelectorSwitch", 1350, 235, 230, 44, 20, new JObject
        {
            ["Value"] = "1", ["OptionCount"] = "3",
            ["Option0Text"] = "手动", ["Option1Text"] = "自动", ["Option2Text"] = "检修",
            ["Mode"] = "InputOutput", ["Fill"] = "#FF15803D", ["TrackColor"] = "#FF26313D",
            ["Stroke"] = "#FF2A3745", ["StrokeThickness"] = "1", ["CornerRadius"] = "8",
            ["FontSize"] = "14", ["FontWeight"] = "SemiBold",
        }, new JArray { Bind("Value", "AutoMode") }));
        els.Add(Btn(page, "运行主流程", 1595, 235, 120, 44, "运行主流程", "#FF0EA5E9", 6, flowName: "Main"));
        els.Add(Btn(page, "报警复位", 1725, 235, 110, 44, "报警复位", "#FFEF4444", 6, flowName: "AlarmReset"));
        els.Add(MiniIndicator(page, "系统就绪", 1195, 290, "SystemReady", "系统就绪"));
        els.Add(MiniIndicator(page, "通讯状态", 1275, 290, "CommOk", "通讯"));
        els.Add(MiniIndicator(page, "视觉状态", 1355, 290, "VisionOk", "视觉"));
        els.Add(MiniIndicator(page, "运动状态", 1435, 290, "MotionOk", "运动"));
        els.Add(Val(page, "OEE数值", 1535, 300, 280, "OEE", "OEE", "F1"));

        string[] stationNames = { "下料", "读码", "定位", "测量", "检测", "装配", "追溯" };
        for (int i = 0; i < 6; i++)
        {
            double x = 60 + i * 300;
            string n = "工位" + (i + 1);
            els.Add(Card(page, n + "卡片", x, 390, 280, 170, n, i % 2 == 0 ? "#FF22D3EE" : "#FFA855F7"));
            els.Add(StatusLamp(page, n + "灯", x + 18, 438, "S" + (i + 1) + "_Ok", n));
            els.Add(Txt(page, n + "名", x + 82, 440, 180, 30, stationNames[i + 1] + "工位", 18, "#FFF8FAFC", "Bold"));
            els.Add(Val(page, n + "过站", x + 82, 480, 180, "St" + (i + 1) + "Count", "过站", "0"));
            els.Add(Indicator(page, n + "状态", x + 18, 515, "S" + (i + 1) + "_Ok", "OK"));
            els.Add(Txt(page, n + "说明", x + 90, 525, 180, 24, i switch
            {
                0 => "上料 / 二维码 / OCR",
                1 => "找圆 / 标定 / 跟随",
                2 => "五种卡尺 / 判定",
                3 => "Blob / 差分 / 颜色",
                4 => "压装 / 胶路 / 扭矩",
                _ => "追溯码 / 数据库",
            }, 11, "#FF94A3B8"));
        }

        els.Add(El(page, "温度压力趋势", "Hmi.MultiTrend", 60, 590, 880, 300, 10, new JObject
        {
            ["Value1"] = "0", ["Value2"] = "0", ["Value3"] = "0", ["Value4"] = "0",
            ["Series1Name"] = "温度 ℃", ["Series2Name"] = "压力 MPa", ["Series3Name"] = "良率 %", ["Series4Name"] = "OEE %",
            ["Series1Color"] = "#FFF59E0B", ["Series2Color"] = "#FF38BDF8", ["Series3Color"] = "#FF22C55E", ["Series4Color"] = "#FFA855F7",
            ["TimeWindow"] = "180", ["RangeMode"] = "Shared", ["ShowLegend"] = "True",
            ["Fill"] = "#FF151C23", ["Stroke"] = "#FF2A3745", ["StrokeThickness"] = "1", ["CornerRadius"] = "8",
        }, new JArray
        {
            Bind("Value1", "Temperature", "F1"),
            Bind("Value2", "Pressure", "F3"),
            Bind("Value3", "YieldRate", "F1"),
            Bind("Value4", "OEE", "F1"),
        }));

        els.Add(El(page, "工位产出", "Hmi.BarChart", 960, 590, 420, 300, 10, new JObject
        {
            ["BarSeries"] = JsonConvert.SerializeObject(new object[]
            {
                new { Label = "工位1", Variable = "St1Count", Color = "#FF38BDF8" },
                new { Label = "工位2", Variable = "St2Count", Color = "#FF22C55E" },
                new { Label = "工位3", Variable = "St3Count", Color = "#FFF59E0B" },
                new { Label = "工位4", Variable = "St4Count", Color = "#FFEF4444" },
                new { Label = "工位5", Variable = "St5Count", Color = "#FFA855F7" },
                new { Label = "工位6", Variable = "St6Count", Color = "#FF14B8A6" },
            }),
            ["Horizontal"] = "False", ["MaxValue"] = "0", ["TargetValue"] = "0",
            ["ShowValues"] = "True", ["SortMode"] = "None", ["ValueFormat"] = "0",
            ["Fill"] = "#FF151C23", ["Stroke"] = "#FF2A3745", ["CornerRadius"] = "8",
        }));

        els.Add(El(page, "缺陷分布", "Hmi.PieChart", 1400, 590, 460, 300, 10, new JObject
        {
            ["PieSlices"] = JsonConvert.SerializeObject(new object[]
            {
                new { Label = "类型A", Variable = "DefectTypeA", Color = "#FFEF4444" },
                new { Label = "类型B", Variable = "DefectTypeB", Color = "#FFF59E0B" },
                new { Label = "类型C", Variable = "DefectTypeC", Color = "#FFA855F7" },
                new { Label = "差分", Variable = "DiffCount", Color = "#FF38BDF8" },
            }),
            ["Donut"] = "True", ["ShowPercent"] = "True", ["MergeBelow"] = "0.05", ["ValueFormat"] = "0",
            ["Fill"] = "#FF151C23", ["Stroke"] = "#FF2A3745", ["CornerRadius"] = "8",
        }));

        els.Add(El(page, "良率条", "Hmi.ProgressBar", 1400, 950, 460, 22, 10, new JObject
        {
            ["Value"] = "100", ["Minimum"] = "0", ["Maximum"] = "100", ["ValueFormat"] = "F1",
            ["Orientation"] = "Horizontal", ["TrackColor"] = "#FF1E293B", ["ShowValue"] = "True",
            ["ShowTicks"] = "False", ["ShowLimits"] = "True", ["LowLimit"] = "90", ["HighLimit"] = "100",
            ["LowColor"] = "#FFEF4444", ["HighColor"] = "#FF22C55E",
            ["Fill"] = "#FF22C55E", ["Stroke"] = "#FF2A3745", ["StrokeThickness"] = "1", ["CornerRadius"] = "4",
            ["Foreground"] = "#FFFFFFFF", ["FontSize"] = "11",
        }, new JArray { Bind("Value", "YieldRate") }));

        els.Add(Nav(page, "工艺流程", 60, 930, "工艺流程"));
        els.Add(Nav(page, "工位1", 220, 930, "工位1-上料读码"));
        els.Add(Nav(page, "工位3", 380, 930, "工位3-精密测量"));
        els.Add(Nav(page, "生产报表", 540, 930, "生产报表"));
        els.Add(Nav(page, "报警中心", 700, 930, "报警中心"));
        els.Add(Nav(page, "设备诊断", 860, 930, "设备诊断"));
        els.Add(Nav(page, "配方设置", 1020, 930, "配方与设置"));
        els.Add(PopupButton(page, "追溯详情", 1180, 930, "追溯详情"));
        return LinePage(page, els);
    }

    // ---------------- 2. 工艺流程 ----------------
    static JObject LineProcessPage()
    {
        const string page = "工艺流程";
        var els = new JArray();
        ComplexHeader(els, page, "产线工艺流程 · 设备拓扑", "上料 → 读码 → 定位 → 测量 → 检测 → 压装 → 追溯");

        els.Add(El(page, "流程底板", "Hmi.Polygon", 40, 170, 1840, 830, 1, new JObject
        {
            ["Points"] = "0,0;1,0;1,1;0,1",
            ["Fill"] = "#FF0E1621", ["Stroke"] = "#FF263447", ["StrokeThickness"] = "1",
        }));

        // 主设备（先放端点，管线随后）
        els.Add(El(page, "原料罐", "Hmi.Tank", 100, 250, 140, 190, 12, new JObject
        {
            ["Level"] = "80", ["Capacity"] = "1000", ["Unit"] = "L", ["ShowValue"] = "True",
            ["TrackColor"] = "#FF0B1220", ["Fill"] = "#FF38BDF8", ["Stroke"] = "#FF3A4A5C", ["StrokeThickness"] = "2",
            ["CornerRadius"] = "14", ["Text"] = "T-101", ["Foreground"] = "#FFE6EEF7", ["FontSize"] = "12", ["FontWeight"] = "SemiBold",
        }, new JArray { Bind("Level", "YieldRate", "F0") }));

        els.Add(El(page, "上料泵", "Hmi.Pump", 300, 280, 80, 80, 12, new JObject
        {
            ["State"] = "0", ["Text"] = "P-101", ["TrackColor"] = "#FF1E293B",
            ["StoppedColor"] = "#FF64748B", ["RunningColor"] = "#FF22C55E", ["FaultColor"] = "#FFEF4444",
            ["Stroke"] = "#FF3A4A5C", ["StrokeThickness"] = "2", ["Foreground"] = "#FFE6EEF7", ["FontSize"] = "11",
        }, new JArray { Bind("State", "LineRunning") }));

        els.Add(El(page, "输送电机", "Hmi.Motor", 430, 280, 80, 90, 12, new JObject
        {
            ["State"] = "0", ["Text"] = "M-101", ["TrackColor"] = "#FF1E293B",
            ["StoppedColor"] = "#FF64748B", ["RunningColor"] = "#FF22C55E", ["FaultColor"] = "#FFEF4444",
            ["Stroke"] = "#FF3A4A5C", ["StrokeThickness"] = "2", ["Foreground"] = "#FFE6EEF7", ["FontSize"] = "11",
        }, new JArray { Bind("State", "LineRunning") }));

        els.Add(El(page, "调节阀", "Hmi.Valve", 560, 290, 80, 72, 12, new JObject
        {
            ["Opening"] = "0", ["OpeningFormat"] = "F0", ["Text"] = "FV-101",
            ["TrackColor"] = "#FF1E293B", ["ThrottleColor"] = "#FFF59E0B", ["OpenColor"] = "#FF22C55E",
            ["Stroke"] = "#FF3A4A5C", ["StrokeThickness"] = "2", ["Foreground"] = "#FFE6EEF7", ["FontSize"] = "11", ["ShowValue"] = "True",
        }, new JArray { Bind("Opening", "SpeedSetpoint", "F0") }));

        els.Add(El(page, "反应釜", "Hmi.Symbol", 700, 240, 130, 150, 12, new JObject
        {
            ["SymbolKind"] = "Reactor", ["State"] = "0", ["AccentColor"] = "#FF22C55E", ["StoppedColor"] = "#FF64748B",
            ["FaultColor"] = "#FFEF4444", ["BlinkOnFault"] = "True", ["DetailCount"] = "3", ["TagPlacement"] = "Bottom",
            ["ShowValue"] = "True", ["Value"] = "0", ["ValueFormat"] = "F1", ["Unit"] = "MPa", ["Text"] = "R-201",
            ["Stroke"] = "#FF9AA5B1", ["StrokeThickness"] = "1.5", ["Foreground"] = "#FFE6EEF7", ["FontSize"] = "11", ["Interactive"] = "False",
        }, new JArray { Bind("State", "LineRunning"), Bind("Value", "Pressure", "F2") }));

        els.Add(El(page, "换热器", "Hmi.Symbol", 880, 240, 130, 150, 12, new JObject
        {
            ["SymbolKind"] = "ShellTube", ["State"] = "0", ["AccentColor"] = "#FF22C55E", ["StoppedColor"] = "#FF64748B",
            ["FaultColor"] = "#FFEF4444", ["DetailCount"] = "2", ["Text"] = "E-202",
            ["Stroke"] = "#FF9AA5B1", ["StrokeThickness"] = "1.5", ["Foreground"] = "#FFE6EEF7", ["FontSize"] = "11",
        }));

        els.Add(El(page, "过滤器", "Hmi.Symbol", 1060, 260, 120, 120, 12, new JObject
        {
            ["SymbolKind"] = "Filter", ["State"] = "0", ["AccentColor"] = "#FF22C55E", ["StoppedColor"] = "#FF64748B",
            ["FaultColor"] = "#FFEF4444", ["Text"] = "F-203",
            ["Stroke"] = "#FF9AA5B1", ["StrokeThickness"] = "1.5", ["Foreground"] = "#FFE6EEF7", ["FontSize"] = "11",
        }));

        els.Add(El(page, "压缩机", "Hmi.Symbol", 1230, 240, 140, 140, 12, new JObject
        {
            ["SymbolKind"] = "Compressor", ["State"] = "0", ["AccentColor"] = "#FF22C55E", ["StoppedColor"] = "#FF64748B",
            ["FaultColor"] = "#FFEF4444", ["Text"] = "C-204",
            ["Stroke"] = "#FF9AA5B1", ["StrokeThickness"] = "1.5", ["Foreground"] = "#FFE6EEF7", ["FontSize"] = "11",
        }));

        els.Add(El(page, "成品罐", "Hmi.Symbol", 1430, 240, 130, 150, 12, new JObject
        {
            ["SymbolKind"] = "TankHorizontal", ["State"] = "0", ["AccentColor"] = "#FF38BDF8", ["StoppedColor"] = "#FF64748B",
            ["FaultColor"] = "#FFEF4444", ["Text"] = "T-301",
            ["Stroke"] = "#FF9AA5B1", ["StrokeThickness"] = "1.5", ["Foreground"] = "#FFE6EEF7", ["FontSize"] = "11",
        }));

        els.Add(El(page, "流程管道1", "Hmi.Pipe", 180, 360, 120, 36, 8, new JObject
        {
            ["Direction"] = "Forward", ["State"] = "0", ["TrackColor"] = "#FF1E293B",
            ["StoppedColor"] = "#FF64748B", ["RunningColor"] = "#FF22C55E", ["FaultColor"] = "#FFEF4444",
            ["Stroke"] = "#FF3A4A5C", ["StrokeThickness"] = "2",
        }, new JArray { Bind("State", "LineRunning") }));
        els.Add(El(page, "流程管道2", "Hmi.Pipe", 380, 360, 130, 36, 8, new JObject
        {
            ["Direction"] = "Forward", ["State"] = "0", ["TrackColor"] = "#FF1E293B",
            ["StoppedColor"] = "#FF64748B", ["RunningColor"] = "#FF22C55E", ["FaultColor"] = "#FFEF4444",
            ["Stroke"] = "#FF3A4A5C", ["StrokeThickness"] = "2",
        }, new JArray { Bind("State", "LineRunning") }));
        els.Add(El(page, "流程管道3", "Hmi.Pipe", 640, 360, 60, 36, 8, new JObject
        {
            ["Direction"] = "Forward", ["State"] = "0", ["TrackColor"] = "#FF1E293B",
            ["StoppedColor"] = "#FF64748B", ["RunningColor"] = "#FF22C55E", ["FaultColor"] = "#FFEF4444",
            ["Stroke"] = "#FF3A4A5C", ["StrokeThickness"] = "2",
        }, new JArray { Bind("State", "LineRunning") }));

        els.Add(El(page, "软管", "Hmi.Bezier", 830, 430, 240, 100, 9, new JObject
        {
            ["Points"] = "0,0.2;0.35,1;0.65,0;1,0.8",
            ["Stroke"] = "#FF38BDF8", ["StrokeThickness"] = "3",
        }));

        els.Add(El(page, "振动示意", "Hmi.DynamicLine", 1120, 430, 260, 80, 9, new JObject
        {
            ["Length"] = "220", ["Angle"] = "0", ["Segments"] = "8", ["Amplitude"] = "5",
            ["Stroke"] = "#FF34C759", ["StrokeThickness"] = "2",
        }, new JArray { Bind("Length", "SpeedSetpoint", "F0") }));

        els.Add(El(page, "分区线", "Hmi.Line", 60, 520, 1800, 4, 7, new JObject
        {
            ["Points"] = "0,0.5;1,0.5", ["Dash"] = "Dash", ["Arrow"] = "None",
            ["Stroke"] = "#FF263447", ["StrokeThickness"] = "1",
        }));

        els.Add(El(page, "工艺流程标识", "Hmi.Symbol", 100, 560, 120, 130, 11, new JObject
        {
            ["SymbolKind"] = "Conveyor", ["State"] = "0", ["AccentColor"] = "#FF22C55E", ["StoppedColor"] = "#FF64748B",
            ["FaultColor"] = "#FFEF4444", ["Text"] = "LINE-01",
            ["Stroke"] = "#FF9AA5B1", ["StrokeThickness"] = "1.5", ["Foreground"] = "#FFE6EEF7", ["FontSize"] = "11",
        }, new JArray { Bind("State", "LineRunning") }));

        els.Add(El(page, "仪表符号", "Hmi.Symbol", 260, 570, 100, 110, 11, new JObject
        {
            ["SymbolKind"] = "Gauge", ["State"] = "0", ["AccentColor"] = "#FFF59E0B", ["StoppedColor"] = "#FF64748B",
            ["FaultColor"] = "#FFEF4444", ["ShowValue"] = "True", ["Value"] = "0", ["ValueFormat"] = "F2", ["Unit"] = "MPa", ["Text"] = "PI-101",
            ["Stroke"] = "#FF9AA5B1", ["StrokeThickness"] = "1.5", ["Foreground"] = "#FFE6EEF7", ["FontSize"] = "11",
        }, new JArray { Bind("Value", "Pressure", "F3") }));

        els.Add(El(page, "球罐", "Hmi.Symbol", 400, 560, 130, 140, 11, new JObject
        {
            ["SymbolKind"] = "Sphere", ["State"] = "0", ["AccentColor"] = "#FF38BDF8", ["Text"] = "V-401",
            ["Stroke"] = "#FF9AA5B1", ["StrokeThickness"] = "1.5", ["Foreground"] = "#FFE6EEF7", ["FontSize"] = "11",
        }));

        els.Add(El(page, "泵符号", "Hmi.Symbol", 570, 570, 110, 120, 11, new JObject
        {
            ["SymbolKind"] = "Agitator", ["State"] = "0", ["AccentColor"] = "#FF22C55E", ["Text"] = "AG-402",
            ["Stroke"] = "#FF9AA5B1", ["StrokeThickness"] = "1.5", ["Foreground"] = "#FFE6EEF7", ["FontSize"] = "11",
        }));

        els.Add(El(page, "冷却塔", "Hmi.Symbol", 730, 550, 140, 160, 11, new JObject
        {
            ["SymbolKind"] = "CoolingTower", ["State"] = "0", ["AccentColor"] = "#FF22C55E", ["Text"] = "CT-403",
            ["Stroke"] = "#FF9AA5B1", ["StrokeThickness"] = "1.5", ["Foreground"] = "#FFE6EEF7", ["FontSize"] = "11",
        }));

        els.Add(El(page, "蝶阀", "Hmi.Symbol", 920, 580, 110, 110, 11, new JObject
        {
            ["SymbolKind"] = "ButterflyValve", ["State"] = "0", ["AccentColor"] = "#FF22C55E", ["Text"] = "BV-404",
            ["Stroke"] = "#FF9AA5B1", ["StrokeThickness"] = "1.5", ["Foreground"] = "#FFE6EEF7", ["FontSize"] = "11",
        }));

        els.Add(El(page, "安全阀", "Hmi.Symbol", 1080, 580, 110, 110, 11, new JObject
        {
            ["SymbolKind"] = "SafetyValve", ["State"] = "0", ["AccentColor"] = "#FFF59E0B", ["Text"] = "PSV-405",
            ["Stroke"] = "#FF9AA5B1", ["StrokeThickness"] = "1.5", ["Foreground"] = "#FFE6EEF7", ["FontSize"] = "11",
        }));

        els.Add(El(page, "塔器", "Hmi.Symbol", 1240, 550, 130, 160, 11, new JObject
        {
            ["SymbolKind"] = "Tower", ["State"] = "0", ["AccentColor"] = "#FF22C55E", ["Text"] = "T-406",
            ["Stroke"] = "#FF9AA5B1", ["StrokeThickness"] = "1.5", ["Foreground"] = "#FFE6EEF7", ["FontSize"] = "11",
        }));

        els.Add(El(page, "LOGO", "Hmi.Picture", 1620, 210, 200, 70, 10, new JObject
        {
            ["SourcePath"] = SL_LOGO, ["Stretch"] = "Uniform", ["PlaceholderText"] = "LOGO",
            ["Fill"] = "#00FFFFFF", ["Stroke"] = "#00FFFFFF", ["StrokeThickness"] = "0", ["CornerRadius"] = "0",
        }));

        els.Add(El(page, "动态标识", "Hmi.Gif", 1640, 300, 160, 110, 10, new JObject
        {
            ["SourcePath"] = SL_GIF, ["Stretch"] = "Uniform", ["PlaceholderText"] = "动图",
            ["Loop"] = "True", ["Fill"] = "#00FFFFFF", ["Stroke"] = "#00FFFFFF", ["StrokeThickness"] = "0", ["CornerRadius"] = "0",
        }));

        els.Add(Txt(page, "图例", 100, 760, 800, 24, "工艺符号：罐 / 泵 / 电机 / 阀 / 换热器 / 塔器 / 过滤器 / 压缩机 / 输送机（30 种符号库）", 13, "#FF94A3B8"));
        els.Add(El(page, "运行计时", "Hmi.RunMeter", 100, 800, 320, 100, 10, new JObject
        {
            ["Mode"] = "RunTime", ["Format"] = "HH:mm:ss", ["Source"] = "False", ["Target"] = "0",
            ["AccumulatedSeconds"] = "0", ["Count"] = "0", ["Label"] = "产线运行时长",
            ["Fill"] = "#FF151C23", ["Stroke"] = "#FF2A3745", ["StrokeThickness"] = "1", ["CornerRadius"] = "8",
            ["Foreground"] = "#FFE6EEF7", ["FontSize"] = "24",
        }, new JArray { Bind("Source", "LineRunning") }));

        // 管线（端点必须已存在）
        els.Add(PipeRun(page, "管线-罐到泵", "原料罐", "Right", "上料泵", "Left", "PL-01"));
        els.Add(PipeRun(page, "管线-泵到阀", "上料泵", "Right", "调节阀", "Left", "PL-02"));
        els.Add(PipeRun(page, "管线-阀到釜", "调节阀", "Right", "反应釜", "Left", "PL-03"));
        els.Add(PipeRun(page, "管线-釜到换热", "反应釜", "Right", "换热器", "Left", "PL-04"));
        els.Add(PipeRun(page, "管线-换热到过滤", "换热器", "Right", "过滤器", "Left", "PL-05"));
        els.Add(PipeRun(page, "管线-过滤到压缩", "过滤器", "Right", "压缩机", "Left", "PL-06"));
        els.Add(PipeRun(page, "管线-压缩到成品", "压缩机", "Right", "成品罐", "Left", "PL-07"));

        els.Add(Nav(page, "返回总览", 60, 930, "产线总览"));
        els.Add(Nav(page, "设备诊断", 220, 930, "设备诊断"));
        return LinePage(page, els);
    }

    static JObject PipeRun(string page, string name, string fromName, string fromPort,
        string toName, string toPort, string label)
        => El(page, name, "Hmi.PipeRun", 0, 0, 10, 10, 6, new JObject
        {
            ["FromId"] = G("el:" + page + ":" + fromName).ToString(),
            ["FromPort"] = fromPort,
            ["ToId"] = G("el:" + page + ":" + toName).ToString(),
            ["ToPort"] = toPort,
            ["RouteMode"] = "Orthogonal",
            ["Detour"] = "True",
            ["ShowLabel"] = "True",
            ["LabelAt"] = "0.5",
            ["LabelOffset"] = "8",
            ["Text"] = label,
            ["Stroke"] = "#FF38BDF8",
            ["StrokeThickness"] = "2",
            ["Foreground"] = "#FFE6EEF7",
            ["FontSize"] = "11",
        });

    // ---------------- 3. 工位1 上料读码 ----------------
    static JObject LineSt1Page()
    {
        const string page = "工位1-上料读码";
        var els = new JArray();
        ComplexHeader(els, page, "工位 1 · 上料与读码", "X 轴定位 / 二维码 / OCR 批号 / 图像缓存");

        els.Add(Card(page, "读码图卡", 60, 180, 760, 520, "读码与批号图像", "#FF22D3EE"));
        els.Add(El(page, "读码图像", "Hmi.ImageView", 80, 230, 720, 450, 10, new JObject
        {
            ["Source"] = "", ["Stretch"] = "Uniform", ["PlaceholderText"] = "等待工位 1 采集…",
            ["Fill"] = "#00000000", ["Stroke"] = "#FF2A3745", ["StrokeThickness"] = "1", ["CornerRadius"] = "8",
        }, new JArray { Bind("Source", "LastImage") }));

        els.Add(Card(page, "读码数据卡", 860, 180, 500, 260, "读码数据", "#FF22C55E"));
        els.Add(TextField(page, "产品SN", 885, 230, 450, "ProductSN", "产品 SN"));
        els.Add(TextField(page, "条码内容", 885, 275, 450, "BarcodeText", "条码内容"));
        els.Add(TextField(page, "OCR文本", 885, 320, 450, "OcrText", "OCR 文本"));
        els.Add(Val(page, "码数量", 885, 365, 220, "CodeCount", "读码数量", "0"));
        els.Add(Val(page, "置信度", 1115, 365, 220, "OcrConfidence", "OCR 置信度", "F3"));

        els.Add(Card(page, "读码判定卡", 860, 460, 500, 240, "判定与控制", "#FFF59E0B"));
        els.Add(Indicator(page, "工位1OK", 890, 510, "S1_Ok", "工位 1 OK"));
        els.Add(Lamp(page, "工位1灯", 990, 505, "S1_Ok", "运行状态"));
        els.Add(TextField(page, "批次", 890, 590, 420, "BatchNo", "批次"));
        els.Add(Btn(page, "重跑工位1", 890, 640, 180, 40, "重跑工位 1", "#FF0EA5E9", 6, flowName: "ST1_LoadCode"));
        els.Add(Nav(page, "返回总览", 1090, 640, "产线总览"));

        els.Add(Card(page, "批号图卡", 60, 720, 760, 260, "批号原图（方案内资产）", "#FFA855F7"));
        els.Add(El(page, "批号图", "Hmi.Picture", 80, 765, 340, 190, 10, new JObject
        {
            ["SourcePath"] = SL_LOT1, ["Stretch"] = "Uniform", ["PlaceholderText"] = "批号图",
            ["Fill"] = "#00000000", ["Stroke"] = "#00FFFFFF", ["StrokeThickness"] = "0", ["CornerRadius"] = "6",
        }));
        els.Add(El(page, "二维码图", "Hmi.Picture", 440, 765, 340, 190, 10, new JObject
        {
            ["SourcePath"] = SL_QR1, ["Stretch"] = "Uniform", ["PlaceholderText"] = "二维码图",
            ["Fill"] = "#00000000", ["Stroke"] = "#00FFFFFF", ["StrokeThickness"] = "0", ["CornerRadius"] = "6",
        }));

        els.Add(El(page, "X轴面板", "Hmi.AxisPanel", 1380, 180, 480, 360, 10, new JObject
        {
            ["AxisOptions"] = AxisOptions("X"),
            ["ShowCommands"] = "True", ["ShowPoints"] = "False",
            ["JogVelocity"] = "5", ["JogMode"] = "Continuous", ["JogStepMm"] = "1",
            ["ConfirmCommands"] = "False",
            ["Fill"] = "#FF0F1112", ["Stroke"] = "#FF15171A", ["StrokeThickness"] = "1", ["CornerRadius"] = "12",
            ["Text"] = "X 轴控制", ["Foreground"] = "#FFFAFAFA", ["FontSize"] = "12",
        }));

        els.Add(Card(page, "码值位表", 1380, 560, 480, 180, "码值位表", "#FF38BDF8"));
        els.Add(El(page, "码值位", "Hmi.BitTable", 1400, 605, 440, 120, 10, new JObject
        {
            ["Value"] = "0", ["BitCount"] = "16", ["StartBit"] = "0", ["Columns"] = "8",
            ["ShowIndex"] = "True", ["ShowValue"] = "True", ["ShowLabels"] = "False",
            ["OnColor"] = "#FF22C55E", ["OffColor"] = "#FF334155",
            ["Fill"] = "#FF151C23", ["Stroke"] = "#FF2A3745", ["StrokeThickness"] = "1", ["CornerRadius"] = "6",
            ["Text"] = "CodeCount 低 16 位", ["Foreground"] = "#FFE6EEF7", ["FontSize"] = "12",
        }, new JArray { Bind("Value", "CodeCount") }));

        els.Add(Nav(page, "工位2", 1380, 780, "工位2-视觉定位"));
        els.Add(Nav(page, "工位3", 1540, 780, "工位3-精密测量"));
        return LinePage(page, els);
    }

    // ---------------- 4. 工位2 视觉定位 ----------------
    static JObject LineSt2Page()
    {
        const string page = "工位2-视觉定位";
        var els = new JArray();
        ComplexHeader(els, page, "工位 2 · 视觉定位", "几何找圆 / 像素当量 / 姿态跟随 / 机械坐标");

        els.Add(Card(page, "定位图卡", 60, 180, 820, 560, "定位图像与十字线", "#FF22D3EE"));
        els.Add(El(page, "定位图像", "Hmi.ImageView", 80, 230, 780, 490, 10, new JObject
        {
            ["Source"] = "", ["Stretch"] = "Uniform", ["PlaceholderText"] = "等待工位 2 采集…",
            ["Fill"] = "#00000000", ["Stroke"] = "#FF2A3745", ["StrokeThickness"] = "1", ["CornerRadius"] = "8",
        }, new JArray { Bind("Source", "LastImage") }));
        els.Add(El(page, "十字横线", "Hmi.Line", 80, 470, 780, 2, 15, new JObject
        {
            ["Points"] = "0,0.5;1,0.5", ["Dash"] = "Dash", ["Arrow"] = "None",
            ["Stroke"] = "#FF38BDF8", ["StrokeThickness"] = "1",
        }));
        els.Add(El(page, "十字竖线", "Hmi.Line", 470, 230, 2, 490, 15, new JObject
        {
            ["Points"] = "0.5,0;0.5,1", ["Dash"] = "Dash", ["Arrow"] = "None",
            ["Stroke"] = "#FF38BDF8", ["StrokeThickness"] = "1",
        }));

        els.Add(Card(page, "定位值卡", 920, 180, 460, 330, "定位结果", "#FF22C55E"));
        els.Add(Val(page, "定位行", 945, 230, 410, "MatchRow", "定位行", "F2"));
        els.Add(Val(page, "定位列", 945, 275, 410, "MatchCol", "定位列", "F2"));
        els.Add(Val(page, "定位角", 945, 320, 410, "MatchAngle", "定位角度", "F3"));
        els.Add(Val(page, "机械X", 945, 365, 410, "AlignOffsetX", "机械 X", "F3"));
        els.Add(Val(page, "机械Y", 945, 410, 410, "AlignOffsetY", "机械 Y", "F3"));
        els.Add(Val(page, "当量", 945, 455, 410, "CalMmPerPixel", "像素当量 mm/px", "F5"));

        els.Add(El(page, "匹配分表盘", "Hmi.Gauge", 920, 540, 200, 200, 10, new JObject
        {
            ["Value"] = "0", ["Minimum"] = "0", ["Maximum"] = "1", ["ValueFormat"] = "F2", ["Unit"] = "",
            ["ShowValue"] = "True", ["TrackColor"] = "#FF1E293B", ["ScaleColor"] = "#FF94A3B8",
            ["Fill"] = "#FF22C55E", ["Stroke"] = "#FF475569", ["StrokeThickness"] = "1",
            ["Foreground"] = "#FFF8FAFC", ["FontSize"] = "14",
        }, new JArray { Bind("Value", "MatchScore") }));

        els.Add(Card(page, "定位状态卡", 1140, 540, 240, 200, "状态", "#FFF59E0B"));
        els.Add(El(page, "定位状态", "Hmi.StateView", 1160, 600, 200, 80, 10, new JObject
        {
            ["Value"] = "1", ["StateListId"] = G("state:station").ToString(),
            ["Presentation"] = "Text", ["Interaction"] = "None", ["ClickValue"] = "0", ["ImageStretch"] = "Uniform",
            ["Fill"] = "#FF151C23", ["Stroke"] = "#FF2A3745", ["StrokeThickness"] = "1", ["CornerRadius"] = "8",
            ["Foreground"] = "#FFE6EEF7", ["FontSize"] = "20", ["FontWeight"] = "SemiBold", ["TextAlignment"] = "Center",
        }, new JArray { Bind("Value", "AutoMode") }));

        els.Add(El(page, "Y轴控台", "Hmi.AxisConsole", 1400, 180, 460, 620, 10, new JObject
        {
            ["AxisOptions"] = AxisOptions("X", "Y"),
            ["ShowCommands"] = "True", ["ShowPoints"] = "True", ["PointsRows"] = "6",
            ["ConfirmCommands"] = "True", ["JogVelocity"] = "5", ["JogMode"] = "Continuous", ["JogStepMm"] = "1",
            ["Fill"] = "#FF0F1112", ["Stroke"] = "#FF15171A", ["StrokeThickness"] = "1", ["CornerRadius"] = "12",
            ["Text"] = "定位轴控", ["Foreground"] = "#FFFAFAFA", ["FontSize"] = "12",
        }));
        els.Add(Nav(page, "工位1", 1400, 820, "工位1-上料读码"));
        els.Add(Nav(page, "工位3", 1560, 820, "工位3-精密测量"));
        return LinePage(page, els);
    }

    // ---------------- 5. 工位3 精密测量 ----------------
    static JObject LineSt3Page()
    {
        const string page = "工位3-精密测量";
        var els = new JArray();
        ComplexHeader(els, page, "工位 3 · 精密尺寸测量", "宽度 / 两点距 / 点线距 / 圆直径 / 角度 · 五路卡尺");

        els.Add(Card(page, "测量图卡", 60, 180, 720, 520, "测量图像", "#FF22D3EE"));
        els.Add(El(page, "测量图像", "Hmi.ImageView", 80, 230, 680, 450, 10, new JObject
        {
            ["Source"] = "", ["Stretch"] = "Uniform", ["PlaceholderText"] = "等待工位 3 采集…",
            ["Fill"] = "#00000000", ["Stroke"] = "#FF2A3745", ["StrokeThickness"] = "1", ["CornerRadius"] = "8",
        }, new JArray { Bind("Source", "LastImage") }));

        els.Add(Card(page, "测量值卡", 820, 180, 520, 520, "五路测量值", "#FF22C55E"));
        els.Add(El(page, "宽度表", "Hmi.Gauge", 845, 230, 150, 150, 10, new JObject
        {
            ["Value"] = "0", ["Minimum"] = "0", ["Maximum"] = "60", ["ValueFormat"] = "F2", ["Unit"] = "mm",
            ["ShowValue"] = "True", ["TrackColor"] = "#FF1E293B", ["ScaleColor"] = "#FF94A3B8",
            ["Fill"] = "#FFE03A2B", ["Stroke"] = "#FF475569", ["StrokeThickness"] = "1",
            ["Foreground"] = "#FFF8FAFC", ["FontSize"] = "13",
        }, new JArray { Bind("Value", "WidthMm") }));
        els.Add(El(page, "直径表", "Hmi.Gauge", 1005, 230, 150, 150, 10, new JObject
        {
            ["Value"] = "0", ["Minimum"] = "0", ["Maximum"] = "260", ["ValueFormat"] = "F1", ["Unit"] = "mm",
            ["ShowValue"] = "True", ["TrackColor"] = "#FF1E293B", ["ScaleColor"] = "#FF94A3B8",
            ["Fill"] = "#FF38BDF8", ["Stroke"] = "#FF475569", ["StrokeThickness"] = "1",
            ["Foreground"] = "#FFF8FAFC", ["FontSize"] = "13",
        }, new JArray { Bind("Value", "DiameterMm") }));
        els.Add(El(page, "角度表", "Hmi.Gauge", 1165, 230, 150, 150, 10, new JObject
        {
            ["Value"] = "0", ["Minimum"] = "0", ["Maximum"] = "180", ["ValueFormat"] = "F1", ["Unit"] = "°",
            ["ShowValue"] = "True", ["TrackColor"] = "#FF1E293B", ["ScaleColor"] = "#FF94A3B8",
            ["Fill"] = "#FFF59E0B", ["Stroke"] = "#FF475569", ["StrokeThickness"] = "1",
            ["Foreground"] = "#FFF8FAFC", ["FontSize"] = "13",
        }, new JArray { Bind("Value", "AngleDeg") }));

        els.Add(Val(page, "两点距", 845, 410, 460, "DistanceMm", "两点距", "F3"));
        els.Add(Val(page, "点线距", 845, 455, 460, "PointLineMm", "点到线距", "F3"));
        els.Add(Val(page, "像素当量", 845, 500, 460, "CalMmPerPixel", "像素当量", "F5"));
        els.Add(Indicator(page, "测量OK", 845, 560, "S3_Ok", "测量综合 OK"));
        els.Add(Btn(page, "重跑测量", 845, 630, 180, 40, "重跑工位 3", "#FF0EA5E9", 6, flowName: "ST3_Measure"));
        els.Add(Nav(page, "返回总览", 1045, 630, "产线总览"));

        els.Add(El(page, "测量趋势", "Hmi.MultiTrend", 60, 720, 900, 260, 10, new JObject
        {
            ["Value1"] = "0", ["Value2"] = "0", ["Value3"] = "0", ["Value4"] = "0",
            ["Series1Name"] = "宽度", ["Series2Name"] = "两点距", ["Series3Name"] = "圆直径", ["Series4Name"] = "角度",
            ["Series1Color"] = "#FF38BDF8", ["Series2Color"] = "#FF22C55E", ["Series3Color"] = "#FFF59E0B", ["Series4Color"] = "#FFA855F7",
            ["TimeWindow"] = "180", ["RangeMode"] = "Independent", ["ShowLegend"] = "True",
            ["Fill"] = "#FF151C23", ["Stroke"] = "#FF2A3745", ["StrokeThickness"] = "1", ["CornerRadius"] = "8",
        }, new JArray
        {
            Bind("Value1", "WidthMm", "F2"),
            Bind("Value2", "DistanceMm", "F2"),
            Bind("Value3", "DiameterMm", "F1"),
            Bind("Value4", "AngleDeg", "F1"),
        }));

        els.Add(El(page, "测量对比", "Hmi.BarChart", 980, 720, 420, 260, 10, new JObject
        {
            ["BarSeries"] = JsonConvert.SerializeObject(new object[]
            {
                new { Label = "宽度", Variable = "WidthMm", Color = "#FF38BDF8" },
                new { Label = "两点距", Variable = "DistanceMm", Color = "#FF22C55E" },
                new { Label = "点线距", Variable = "PointLineMm", Color = "#FFF59E0B" },
                new { Label = "圆直径", Variable = "DiameterMm", Color = "#FFEF4444" },
                new { Label = "角度", Variable = "AngleDeg", Color = "#FFA855F7" },
            }),
            ["Horizontal"] = "True", ["MaxValue"] = "0", ["TargetValue"] = "0", ["ShowValues"] = "True",
            ["SortMode"] = "None", ["ValueFormat"] = "F2",
            ["Fill"] = "#FF151C23", ["Stroke"] = "#FF2A3745", ["CornerRadius"] = "8",
        }));

        els.Add(Nav(page, "工位2", 1420, 820, "工位2-视觉定位"));
        els.Add(Nav(page, "工位4", 1580, 820, "工位4-表面检测"));
        return LinePage(page, els);
    }

    // ---------------- 6. 工位4 表面检测 ----------------
    static JObject LineSt4Page()
    {
        const string page = "工位4-表面检测";
        var els = new JArray();
        ComplexHeader(els, page, "工位 4 · 表面缺陷检测", "Blob / 图像差分 / 颜色序列 / 区域颜色");

        els.Add(Card(page, "表面图卡", 60, 180, 760, 480, "表面图像", "#FF22D3EE"));
        els.Add(El(page, "表面图像", "Hmi.ImageView", 80, 230, 720, 410, 10, new JObject
        {
            ["Source"] = "", ["Stretch"] = "Uniform", ["PlaceholderText"] = "等待工位 4 采集…",
            ["Fill"] = "#00000000", ["Stroke"] = "#FF2A3745", ["StrokeThickness"] = "1", ["CornerRadius"] = "8",
        }, new JArray { Bind("Source", "LastImage") }));

        els.Add(Card(page, "缺陷KPI卡", 860, 180, 520, 260, "缺陷统计", "#FFEF4444"));
        els.Add(Val(page, "缺陷数", 885, 230, 230, "DefectCount", "缺陷数", "0"));
        els.Add(Val(page, "缺陷面积", 1135, 230, 220, "DefectArea", "缺陷面积 mm²", "F2"));
        els.Add(Val(page, "差分数量", 885, 280, 230, "DiffCount", "差分缺陷数", "0"));
        els.Add(Val(page, "最大差分", 1135, 280, 220, "MaxDiff", "最大差分值", "F1"));
        els.Add(Val(page, "颜色占比", 885, 330, 230, "ColorShare", "颜色占比", "F3"));
        els.Add(TextField(page, "颜色判定", 1135, 330, 220, "ColorVerdict", "颜色判定"));
        els.Add(El(page, "缺陷进度", "Hmi.ProgressBar", 885, 380, 470, 22, 10, new JObject
        {
            ["Value"] = "0", ["Minimum"] = "0", ["Maximum"] = "20", ["ValueFormat"] = "0",
            ["Orientation"] = "Horizontal", ["TrackColor"] = "#FF1E293B", ["ShowValue"] = "True",
            ["ShowTicks"] = "True", ["TickCount"] = "10", ["TickColor"] = "#FF475569",
            ["ShowLimits"] = "True", ["LowLimit"] = "0", ["HighLimit"] = "8",
            ["LowColor"] = "#FF22C55E", ["HighColor"] = "#FFEF4444", ["LimitColor"] = "#FF94A3B8",
            ["Fill"] = "#FFEF4444", ["Stroke"] = "#FF2A3745", ["StrokeThickness"] = "1", ["CornerRadius"] = "4",
            ["Foreground"] = "#FFFFFFFF", ["FontSize"] = "11",
        }, new JArray { Bind("Value", "DefectCount") }));

        els.Add(Card(page, "缺陷分布卡", 860, 460, 520, 260, "缺陷类型分布", "#FFF59E0B"));
        els.Add(El(page, "缺陷饼图", "Hmi.PieChart", 880, 505, 480, 200, 10, new JObject
        {
            ["PieSlices"] = JsonConvert.SerializeObject(new object[]
            {
                new { Label = "划伤", Variable = "DefectTypeA", Color = "#FFEF4444" },
                new { Label = "脏污", Variable = "DefectTypeB", Color = "#FFF59E0B" },
                new { Label = "凹坑", Variable = "DefectTypeC", Color = "#FFA855F7" },
                new { Label = "差分", Variable = "DiffCount", Color = "#FF38BDF8" },
            }),
            ["Donut"] = "True", ["ShowPercent"] = "True", ["MergeBelow"] = "0.05", ["ValueFormat"] = "0",
            ["Fill"] = "#FF151C23", ["Stroke"] = "#FF2A3745", ["CornerRadius"] = "8",
        }));

        els.Add(El(page, "缺陷位表", "Hmi.BitTable", 1420, 180, 440, 180, 10, new JObject
        {
            ["Value"] = "0", ["BitCount"] = "16", ["StartBit"] = "0", ["Columns"] = "8",
            ["ShowIndex"] = "True", ["ShowValue"] = "True", ["ShowLabels"] = "False",
            ["OnColor"] = "#FFEF4444", ["OffColor"] = "#FF334155",
            ["Fill"] = "#FF151C23", ["Stroke"] = "#FF2A3745", ["StrokeThickness"] = "1", ["CornerRadius"] = "6",
            ["Text"] = "缺陷位图", ["Foreground"] = "#FFE6EEF7", ["FontSize"] = "12",
        }, new JArray { Bind("Value", "DefectCount") }));

        els.Add(Card(page, "表面判定卡", 1420, 380, 440, 160, "判定", "#FF22C55E"));
        els.Add(Indicator(page, "表面OK", 1450, 430, "S4_Ok", "表面综合 OK"));
        els.Add(El(page, "表面状态", "Hmi.StateView", 1560, 425, 260, 70, 10, new JObject
        {
            ["Value"] = "1", ["StateListId"] = G("state:station").ToString(),
            ["Presentation"] = "Text", ["Interaction"] = "None", ["ClickValue"] = "0", ["ImageStretch"] = "Uniform",
            ["Fill"] = "#FF151C23", ["Stroke"] = "#FF2A3745", ["StrokeThickness"] = "1", ["CornerRadius"] = "8",
            ["Foreground"] = "#FFE6EEF7", ["FontSize"] = "18", ["FontWeight"] = "SemiBold", ["TextAlignment"] = "Center",
        }, new JArray { Bind("Value", "AutoMode") }));

        els.Add(Card(page, "对比图卡", 1420, 560, 440, 380, "参考 / 实测", "#FFA855F7"));
        els.Add(El(page, "参考图", "Hmi.Picture", 1440, 610, 190, 150, 10, new JObject
        {
            ["SourcePath"] = SL_BOARD1, ["Stretch"] = "Uniform", ["PlaceholderText"] = "参考图",
            ["Fill"] = "#00000000", ["Stroke"] = "#00FFFFFF", ["StrokeThickness"] = "0", ["CornerRadius"] = "6",
        }));
        els.Add(El(page, "实测图", "Hmi.Picture", 1650, 610, 190, 150, 10, new JObject
        {
            ["SourcePath"] = SL_BOARD2, ["Stretch"] = "Uniform", ["PlaceholderText"] = "实测图",
            ["Fill"] = "#00000000", ["Stroke"] = "#00FFFFFF", ["StrokeThickness"] = "0", ["CornerRadius"] = "6",
        }));
        els.Add(Txt(page, "对比说明", 1440, 780, 400, 60, "图像差分：参考图与实测图逐像素比较，\n差异连通域数量 / 面积 / 最大值写入追溯账本。", 12, "#FF94A3B8"));

        els.Add(Btn(page, "重跑表面", 60, 700, 180, 40, "重跑工位 4", "#FF0EA5E9", 6, flowName: "ST4_Surface"));
        els.Add(Nav(page, "返回总览", 260, 700, "产线总览"));
        els.Add(Nav(page, "报警中心", 420, 700, "报警中心"));
        return LinePage(page, els);
    }

    // ---------------- 7. 工位5 装配压装 ----------------
    static JObject LineSt5Page()
    {
        const string page = "工位5-装配压装";
        var els = new JArray();
        ComplexHeader(els, page, "工位 5 · 装配与压装", "Z 轴压装 / 夹爪 IO / 胶路检测 / 扭矩监控");

        els.Add(Card(page, "装配工艺卡", 60, 180, 780, 520, "装配工艺拓扑", "#FF22D3EE"));
        els.Add(El(page, "压装罐", "Hmi.Tank", 100, 240, 130, 170, 12, new JObject
        {
            ["Level"] = "65", ["Capacity"] = "500", ["Unit"] = "L", ["ShowValue"] = "True",
            ["TrackColor"] = "#FF0B1220", ["Fill"] = "#FFA855F7", ["Stroke"] = "#FF3A4A5C", ["StrokeThickness"] = "2",
            ["CornerRadius"] = "12", ["Text"] = "T-501", ["Foreground"] = "#FFE6EEF7", ["FontSize"] = "12",
        }, new JArray { Bind("Level", "Pressure", "F0") }));
        els.Add(El(page, "压装泵", "Hmi.Pump", 290, 270, 80, 80, 12, new JObject
        {
            ["State"] = "0", ["Text"] = "P-501", ["TrackColor"] = "#FF1E293B",
            ["StoppedColor"] = "#FF64748B", ["RunningColor"] = "#FF22C55E", ["FaultColor"] = "#FFEF4444",
            ["Stroke"] = "#FF3A4A5C", ["StrokeThickness"] = "2", ["Foreground"] = "#FFE6EEF7", ["FontSize"] = "11",
        }, new JArray { Bind("State", "LineRunning") }));
        els.Add(El(page, "压装电机", "Hmi.Motor", 420, 260, 80, 90, 12, new JObject
        {
            ["State"] = "0", ["Text"] = "M-502", ["TrackColor"] = "#FF1E293B",
            ["StoppedColor"] = "#FF64748B", ["RunningColor"] = "#FF22C55E", ["FaultColor"] = "#FFEF4444",
            ["Stroke"] = "#FF3A4A5C", ["StrokeThickness"] = "2", ["Foreground"] = "#FFE6EEF7", ["FontSize"] = "11",
        }, new JArray { Bind("State", "LineRunning") }));
        els.Add(El(page, "压装阀", "Hmi.Valve", 550, 270, 80, 72, 12, new JObject
        {
            ["Opening"] = "0", ["OpeningFormat"] = "F0", ["Text"] = "FV-502", ["ShowValue"] = "True",
            ["TrackColor"] = "#FF1E293B", ["ThrottleColor"] = "#FFF59E0B", ["OpenColor"] = "#FF22C55E",
            ["Stroke"] = "#FF3A4A5C", ["StrokeThickness"] = "2", ["Foreground"] = "#FFE6EEF7", ["FontSize"] = "11",
        }, new JArray { Bind("Opening", "SpeedSetpoint", "F0") }));
        els.Add(El(page, "工艺管道", "Hmi.Pipe", 180, 430, 180, 36, 8, new JObject
        {
            ["Direction"] = "Forward", ["State"] = "0", ["TrackColor"] = "#FF1E293B",
            ["StoppedColor"] = "#FF64748B", ["RunningColor"] = "#FF22C55E", ["FaultColor"] = "#FFEF4444",
            ["Stroke"] = "#FF3A4A5C", ["StrokeThickness"] = "2",
        }, new JArray { Bind("State", "LineRunning") }));
        els.Add(El(page, "工艺符号泵", "Hmi.Symbol", 430, 420, 110, 120, 11, new JObject
        {
            ["SymbolKind"] = "Compressor", ["State"] = "0", ["AccentColor"] = "#FF22C55E", ["StoppedColor"] = "#FF64748B",
            ["FaultColor"] = "#FFEF4444", ["Text"] = "C-503", ["Stroke"] = "#FF9AA5B1", ["StrokeThickness"] = "1.5",
            ["Foreground"] = "#FFE6EEF7", ["FontSize"] = "11",
        }, new JArray { Bind("State", "LineRunning") }));
        els.Add(El(page, "无胶参考图", "Hmi.Picture", 100, 560, 200, 130, 10, new JObject
        {
            ["SourcePath"] = SL_BEAD_REF, ["Stretch"] = "Uniform", ["PlaceholderText"] = "无胶参考",
            ["Fill"] = "#00000000", ["Stroke"] = "#00FFFFFF", ["StrokeThickness"] = "0", ["CornerRadius"] = "6",
        }));
        els.Add(El(page, "胶路状态", "Hmi.Symbol", 600, 420, 120, 130, 11, new JObject
        {
            ["SymbolKind"] = "FlowArrow", ["State"] = "0", ["AccentColor"] = "#FF22C55E", ["StoppedColor"] = "#FF64748B",
            ["FaultColor"] = "#FFEF4444", ["ShowValue"] = "True", ["Value"] = "0", ["ValueFormat"] = "0", ["Unit"] = "处", ["Text"] = "胶路",
            ["Stroke"] = "#FF9AA5B1", ["StrokeThickness"] = "1.5", ["Foreground"] = "#FFE6EEF7", ["FontSize"] = "11",
        }, new JArray { Bind("State", "LineRunning"), Bind("Value", "BeadErrorCount") }));

        els.Add(Card(page, "装配参数卡", 880, 180, 500, 320, "压装参数", "#FF22C55E"));
        els.Add(Val(page, "扭矩", 905, 230, 450, "Torque", "拧紧扭矩 N·m", "F2"));
        els.Add(Val(page, "压力", 905, 275, 450, "Pressure", "压装压力 MPa", "F3"));
        els.Add(Val(page, "胶路错误数", 905, 320, 220, "BeadErrorCount", "胶路错误数", "0"));
        els.Add(Val(page, "胶路总长", 1135, 320, 220, "BeadTotalLength", "错误总长", "F1"));
        els.Add(El(page, "Y轴压力表", "Hmi.Gauge", 1000, 365, 120, 120, 10, new JObject
        {
            ["Value"] = "0", ["Minimum"] = "0", ["Maximum"] = "1", ["ValueFormat"] = "F3", ["Unit"] = "MPa",
            ["ShowValue"] = "True", ["TrackColor"] = "#FF1E293B", ["ScaleColor"] = "#FF94A3B8",
            ["Fill"] = "#FFF59E0B", ["Stroke"] = "#FF475569", ["StrokeThickness"] = "1",
            ["Foreground"] = "#FFF8FAFC", ["FontSize"] = "12",
        }, new JArray { Bind("Value", "Pressure") }));
        els.Add(Indicator(page, "装配OK", 1160, 390, "S5_Ok", "装配 OK"));

        els.Add(Card(page, "装配控制卡", 880, 520, 500, 500, "手动控制", "#FFF59E0B"));
        els.Add(El(page, "速度滑块", "Hmi.Slider", 905, 570, 450, 48, 10, new JObject
        {
            ["Value"] = "50", ["Minimum"] = "0", ["Maximum"] = "100", ["Step"] = "1", ["ValueFormat"] = "F0",
            ["Orientation"] = "Horizontal", ["TrackColor"] = "#FF1E293B", ["ShowValue"] = "True",
            ["Mode"] = "InputOutput", ["WriteMode"] = "Release", ["ConfirmDanger"] = "False",
            ["Fill"] = "#FF0EA5E9", ["Stroke"] = "#FF2A3745", ["StrokeThickness"] = "1", ["CornerRadius"] = "4",
            ["Foreground"] = "#FFE6EEF7", ["FontSize"] = "12",
        }, new JArray { Bind("Value", "SpeedSetpoint") }));
        els.Add(El(page, "速度棒图", "Hmi.ProgressBar", 905, 630, 450, 20, 10, new JObject
        {
            ["Value"] = "50", ["Minimum"] = "0", ["Maximum"] = "100", ["ValueFormat"] = "F0",
            ["Orientation"] = "Horizontal", ["TrackColor"] = "#FF1E293B", ["ShowValue"] = "True", ["ShowTicks"] = "True",
            ["TickCount"] = "10", ["TickColor"] = "#FF475569",
            ["Fill"] = "#FF22C55E", ["Stroke"] = "#FF2A3745", ["StrokeThickness"] = "1", ["CornerRadius"] = "4",
            ["Foreground"] = "#FFFFFFFF", ["FontSize"] = "11",
        }, new JArray { Bind("Value", "SpeedSetpoint") }));
        els.Add(El(page, "Z轴面板", "Hmi.AxisPanel", 905, 665, 450, 300, 10, new JObject
        {
            ["AxisOptions"] = AxisOptions("Z"),
            ["ShowCommands"] = "True", ["ShowPoints"] = "False", ["JogVelocity"] = "5",
            ["JogMode"] = "Step", ["JogStepMm"] = "1", ["ConfirmCommands"] = "True",
            ["Fill"] = "#FF0F1112", ["Stroke"] = "#FF15171A", ["StrokeThickness"] = "1", ["CornerRadius"] = "12",
            ["Text"] = "Z 轴压装", ["Foreground"] = "#FFFAFAFA", ["FontSize"] = "12",
        }));

        els.Add(El(page, "配方只读", "Hmi.RecipePanel", 1400, 180, 460, 300, 10, new JObject
        {
            ["Title"] = "当前配方", ["ShowDetails"] = "True", ["MaxRows"] = "6",
            ["Fill"] = "#FF151C23", ["Stroke"] = "#FF2A3745", ["StrokeThickness"] = "1", ["CornerRadius"] = "6",
            ["Foreground"] = "#FFE6EEF7",
        }));
        els.Add(Card(page, "装配状态卡", 1400, 500, 460, 320, "装配状态", "#FFA855F7"));
        els.Add(Indicator(page, "装配OK2", 1430, 550, "S5_Ok", "装配"));
        els.Add(El(page, "装配状态图", "Hmi.StateView", 1540, 545, 290, 70, 10, new JObject
        {
            ["Value"] = "1", ["StateListId"] = G("state:station").ToString(),
            ["Presentation"] = "Text", ["Interaction"] = "None", ["ClickValue"] = "0", ["ImageStretch"] = "Uniform",
            ["Fill"] = "#FF151C23", ["Stroke"] = "#FF2A3745", ["StrokeThickness"] = "1", ["CornerRadius"] = "8",
            ["Foreground"] = "#FFE6EEF7", ["FontSize"] = "18", ["FontWeight"] = "SemiBold", ["TextAlignment"] = "Center",
        }, new JArray { Bind("Value", "AutoMode") }));
        els.Add(Btn(page, "重跑装配", 1430, 640, 180, 40, "重跑工位 5", "#FF0EA5E9", 6, flowName: "ST5_Assembly"));
        els.Add(Nav(page, "返回总览", 1630, 640, "产线总览"));
        els.Add(Nav(page, "配方设置", 1430, 700, "配方与设置"));
        return LinePage(page, els);
    }

    // ---------------- 8. 工位6 识别追溯 ----------------
    static JObject LineSt6Page()
    {
        const string page = "工位6-识别追溯";
        var els = new JArray();
        ComplexHeader(els, page, "工位 6 · 识别与追溯", "追溯码 / OCR 复核 / 二维码 / 条形码 / 记录表");

        els.Add(Card(page, "追溯码卡", 60, 180, 420, 420, "追溯码", "#FF22D3EE"));
        els.Add(El(page, "追溯二维码", "Hmi.QRCode", 130, 235, 280, 280, 10, new JObject
        {
            ["CodeContent"] = "", ["ShowText"] = "True", ["QuietZone"] = "4", ["Ecc"] = "M",
            ["PlaceholderText"] = "等待追溯码",
            ["Fill"] = "#00000000", ["Stroke"] = "#00000000", ["StrokeThickness"] = "0", ["CornerRadius"] = "6",
            ["Foreground"] = "#FFE6EEF7", ["FontSize"] = "11",
        }, new JArray { Bind("CodeContent", "BarcodeText") }));
        els.Add(El(page, "产品条码", "Hmi.Barcode", 100, 520, 340, 80, 10, new JObject
        {
            ["CodeContent"] = "", ["ShowText"] = "True", ["QuietZone"] = "4", ["Format"] = "Code128",
            ["PlaceholderText"] = "等待 SN",
            ["Fill"] = "#00000000", ["Stroke"] = "#00000000", ["StrokeThickness"] = "0", ["CornerRadius"] = "6",
            ["Foreground"] = "#FFE6EEF7", ["FontSize"] = "11",
        }, new JArray { Bind("CodeContent", "ProductSN") }));

        els.Add(Card(page, "追溯数据卡", 510, 180, 520, 420, "追溯数据", "#FF22C55E"));
        els.Add(TextField(page, "追溯SN", 535, 230, 470, "ProductSN", "产品 SN"));
        els.Add(TextField(page, "追溯批号", 535, 275, 470, "BatchNo", "批次"));
        els.Add(TextField(page, "追溯OCR", 535, 320, 470, "OcrText", "OCR 文本"));
        els.Add(TextField(page, "操作员", 535, 365, 470, "OperatorId", "操作员"));
        els.Add(Val(page, "追溯码数", 535, 410, 220, "CodeCount", "码数量", "0"));
        els.Add(Val(page, "追溯判定", 775, 410, 230, "OcrConfidence", "OCR 置信度", "F3"));
        els.Add(El(page, "追溯状态", "Hmi.StateView", 535, 465, 470, 70, 10, new JObject
        {
            ["Value"] = "1", ["StateListId"] = G("state:product").ToString(),
            ["Presentation"] = "Text", ["Interaction"] = "None", ["ClickValue"] = "0", ["ImageStretch"] = "Uniform",
            ["Fill"] = "#FF151C23", ["Stroke"] = "#FF2A3745", ["StrokeThickness"] = "1", ["CornerRadius"] = "8",
            ["Foreground"] = "#FFE6EEF7", ["FontSize"] = "18", ["FontWeight"] = "SemiBold", ["TextAlignment"] = "Center",
        }, new JArray { Bind("Value", "AutoMode") }));

        els.Add(Card(page, "实时记录卡", 1060, 180, 800, 420, "实时过站记录（显示缓冲）", "#FFF59E0B"));
        els.Add(El(page, "实时记录表", "Hmi.DataGrid", 1080, 225, 760, 360, 10, new JObject
        {
            ["GridColumns"] = JsonConvert.SerializeObject(new object[]
            {
                new { Header = "SN", Variable = "ProductSN", Width = 220, Align = "left", Format = "", Monospace = true },
                new { Header = "类型", Variable = "ProductType", Width = 70, Align = "center", Format = "", Monospace = false },
                new { Header = "判定", Variable = "ResultText", Width = 80, Align = "center", Format = "", Monospace = true },
                new { Header = "良率", Variable = "YieldRate", Width = 90, Align = "right", Format = "F1", Monospace = true },
                new { Header = "缺陷", Variable = "DefectCount", Width = 70, Align = "right", Format = "0", Monospace = true },
                new { Header = "批次", Variable = "BatchNo", Width = 0, Align = "left", Format = "", Monospace = false },
            }),
            ["SampleVariable"] = "StationOk", ["AckVariable"] = "", ["MaxRows"] = "50",
            ["RowHeight"] = "28", ["NewestFirst"] = "True", ["Zebra"] = "True",
            ["Source"] = "Live", ["Text"] = "实时过站",
        }));

        els.Add(Card(page, "IO监视卡", 60, 640, 600, 300, "I/O 监视", "#FF38BDF8"));
        els.Add(El(page, "IO监视", "Hmi.IoMonitor", 80, 685, 560, 110, 10, new JObject
        {
            ["Value"] = "0", ["BitCount"] = "8", ["Columns"] = "8", ["ShowIndex"] = "True", ["ShowValue"] = "True",
            ["ShowLabels"] = "True", ["Labels"] = "I0.0,I0.1,I0.2,I0.3,I0.4,I0.5,I0.6,I0.7",
            ["OnColor"] = "#FF22C55E", ["OffColor"] = "#FF334155",
            ["Fill"] = "#FF151C23", ["Stroke"] = "#FF2A3745", ["StrokeThickness"] = "1", ["CornerRadius"] = "6",
            ["Text"] = "输入字节", ["Foreground"] = "#FFE6EEF7", ["FontSize"] = "12",
        }, new JArray { Bind("Value", "PLC_Trigger") }));
        els.Add(El(page, "变量监视", "Hmi.VariableMonitor", 80, 805, 560, 120, 10, new JObject
        {
            ["Value"] = "0", ["BitCount"] = "16", ["Columns"] = "8", ["ShowIndex"] = "True", ["ShowValue"] = "True",
            ["OnColor"] = "#FF38BDF8", ["OffColor"] = "#FF334155",
            ["Fill"] = "#FF151C23", ["Stroke"] = "#FF2A3745", ["StrokeThickness"] = "1", ["CornerRadius"] = "6",
            ["Text"] = "PLC_Trigger 字", ["Foreground"] = "#FFE6EEF7", ["FontSize"] = "12",
        }, new JArray { Bind("Value", "PLC_Trigger") }));

        els.Add(El(page, "过站计时", "Hmi.RunMeter", 700, 660, 400, 120, 10, new JObject
        {
            ["Mode"] = "Counter", ["Format"] = "0", ["Target"] = "0", ["AccumulatedSeconds"] = "0", ["Count"] = "0",
            ["Label"] = "过站总数",
            ["Fill"] = "#FF151C23", ["Stroke"] = "#FF2A3745", ["StrokeThickness"] = "1", ["CornerRadius"] = "8",
            ["Foreground"] = "#FFE6EEF7", ["FontSize"] = "28",
        }, new JArray { Bind("Count", "PartCounter") }));
        els.Add(Btn(page, "追溯详情弹窗", 700, 800, 180, 44, "查看追溯详情", "#FFA855F7", 19, pageName: "追溯详情"));
        els.Add(Nav(page, "生产报表", 900, 800, "生产报表"));
        els.Add(Nav(page, "返回总览", 1060, 800, "产线总览"));
        return LinePage(page, els);
    }

    // ---------------- 9. 生产报表 ----------------
    static JObject LineReportPage()
    {
        const string page = "生产报表";
        var els = new JArray();
        ComplexHeader(els, page, "生产报表与追溯账本", "CSV 账本 / 明细报表 / 汇总 / 历史查询");

        string reportCols = JsonConvert.SerializeObject(new object[]
        {
            new { Source = "时间", Title = "时间", Format = "HH:mm:ss", Aggregate = 0, Width = 120 },
            new { Source = "序号", Title = "序号", Format = "0", Aggregate = 0, Width = 70 },
            new { Source = "产品SN", Title = "产品 SN", Format = "", Aggregate = 0, Width = 220 },
            new { Source = "产品类型", Title = "类型", Format = "", Aggregate = 0, Width = 70 },
            new { Source = "综合判定", Title = "判定", Format = "", Aggregate = 0, Width = 80 },
            new { Source = "宽度(mm)", Title = "宽度", Format = "F2", Aggregate = 2, Width = 90 },
            new { Source = "直径(mm)", Title = "直径", Format = "F1", Aggregate = 2, Width = 90 },
            new { Source = "角度(°)", Title = "角度", Format = "F1", Aggregate = 2, Width = 80 },
            new { Source = "缺陷数", Title = "缺陷", Format = "0", Aggregate = 1, Width = 70 },
            new { Source = "良率(%)", Title = "良率", Format = "F1", Aggregate = 2, Width = 0 },
        });

        els.Add(Card(page, "明细报表卡", 60, 180, 1180, 620, "生产明细报表（按时间范围）", "#FF22D3EE"));
        els.Add(El(page, "明细报表", "Hmi.Report", 80, 225, 1140, 560, 10, new JObject
        {
            ["Mode"] = "Detail", ["From"] = "-1d", ["To"] = "now", ["Grouping"] = "None", ["ShiftStarts"] = "08:00,20:00",
            ["Keyword"] = "", ["ReportColumns"] = reportCols, ["AutoQuery"] = "True", ["MaxRows"] = "5000",
            ["PageSize"] = "200", ["AllowExport"] = "True", ["AllowPrint"] = "True", ["ShowToolbar"] = "True",
            ["PageOrientation"] = "Landscape", ["Zebra"] = "True",
            ["Fill"] = "#FF151C23", ["Stroke"] = "#FF2A3745", ["StrokeThickness"] = "1", ["CornerRadius"] = "8",
            ["Text"] = "生产明细报表", ["Foreground"] = "#FFE6EEF7", ["FontSize"] = "12",
        }));

        els.Add(Card(page, "汇总报表卡", 60, 800, 1180, 190, "当班汇总（按班次聚合）", "#FF22C55E"));
        els.Add(El(page, "汇总报表", "Hmi.Report", 80, 835, 1140, 135, 10, new JObject
        {
            ["Mode"] = "Summary", ["From"] = "-1d", ["To"] = "now", ["Grouping"] = "Shift", ["ShiftStarts"] = "08:00,20:00",
            ["Keyword"] = "", ["ReportColumns"] = reportCols, ["AutoQuery"] = "True", ["MaxRows"] = "2000",
            ["PageSize"] = "100", ["AllowExport"] = "True", ["AllowPrint"] = "True", ["ShowToolbar"] = "False",
            ["PageOrientation"] = "Landscape", ["Zebra"] = "False",
            ["Fill"] = "#FF151C23", ["Stroke"] = "#FF2A3745", ["StrokeThickness"] = "1", ["CornerRadius"] = "8",
            ["Text"] = "当班汇总", ["Foreground"] = "#FFE6EEF7", ["FontSize"] = "12",
        }));

        els.Add(Card(page, "历史查询卡", 1260, 180, 600, 620, "历史账本查询（History\\）", "#FFF59E0B"));
        els.Add(El(page, "历史表格", "Hmi.DataGrid", 1280, 225, 560, 560, 10, new JObject
        {
            ["GridColumns"] = JsonConvert.SerializeObject(new object[]
            {
                new { Header = "时间", Variable = "TimestampText", Width = 150, Align = "left", Format = "", Monospace = true },
                new { Header = "SN", Variable = "ProductSN", Width = 200, Align = "left", Format = "", Monospace = true },
                new { Header = "判定", Variable = "ResultText", Width = 80, Align = "center", Format = "", Monospace = true },
                new { Header = "缺陷", Variable = "DefectCount", Width = 70, Align = "right", Format = "0", Monospace = true },
            }),
            ["SampleVariable"] = "", ["AckVariable"] = "", ["MaxRows"] = "200",
            ["RowHeight"] = "28", ["NewestFirst"] = "True", ["Zebra"] = "True",
            ["Source"] = "History", ["HistoryRange"] = "Last7Days", ["HistoryPageSize"] = "50",
            ["HistoryTimestampColumn"] = "时间", ["HistoryKeyword"] = "",
            ["Text"] = "历史记录",
        }));
        els.Add(El(page, "报表产出", "Hmi.BarChart", 1260, 800, 600, 220, 10, new JObject
        {
            ["BarSeries"] = JsonConvert.SerializeObject(new object[]
            {
                new { Label = "良品", Variable = "OKCount", Color = "#FF22C55E" },
                new { Label = "不良", Variable = "NGCount", Color = "#FFEF4444" },
                new { Label = "过站", Variable = "PartCounter", Color = "#FF38BDF8" },
            }),
            ["Horizontal"] = "False", ["MaxValue"] = "0", ["TargetValue"] = "0", ["ShowValues"] = "True",
            ["SortMode"] = "Desc", ["ValueFormat"] = "0",
            ["Fill"] = "#FF151C23", ["Stroke"] = "#FF2A3745", ["CornerRadius"] = "8",
        }));

        els.Add(TextField(page, "报表路径", 60, 1005, 900, "ReportPath", "最近 Excel 报表"));
        els.Add(Btn(page, "导出报表", 1000, 1000, 150, 40, "运行一轮并导出", "#FF0EA5E9", 6, flowName: "Main"));
        els.Add(Nav(page, "返回总览", 1170, 1000, "产线总览"));
        return LinePage(page, els);
    }

    // ---------------- 10. 报警中心 ----------------
    static JObject LineAlarmPage()
    {
        const string page = "报警中心";
        var els = new JArray();
        ComplexHeader(els, page, "报警中心 · 实时报警", "按严重度排序 / 一键确认 / 报警统计");

        els.Add(Card(page, "报警列表卡", 60, 180, 1180, 620, "实时报警列表", "#FFEF4444"));
        els.Add(El(page, "报警列表", "Hmi.AlarmBanner", 80, 225, 1140, 560, 10, new JObject
        {
            ["MaxRows"] = "16", ["EmptyText"] = "系统正常，无报警",
            ["NormalColor"] = "#FF16A34A", ["InfoColor"] = "#FF0EA5E9",
            ["WarningColor"] = "#FFF59E0B", ["CriticalColor"] = "#FFEF4444",
            ["Fill"] = "#FF151C23", ["Stroke"] = "#FF2A3745", ["StrokeThickness"] = "1", ["CornerRadius"] = "8",
            ["Text"] = "实时报警", ["Foreground"] = "#FFE6EEF7", ["FontSize"] = "13", ["FontWeight"] = "SemiBold",
        }));
        els.Add(El(page, "确认全部", "Hmi.AckButton", 60, 830, 200, 44, 10, new JObject
        {
            ["Scope"] = "All", ["Text"] = "确认全部报警",
            ["Fill"] = "#FFEF4444", ["Stroke"] = "#FFB91C1C", ["StrokeThickness"] = "1", ["CornerRadius"] = "6",
            ["Foreground"] = "#FFFFFFFF", ["FontSize"] = "14", ["FontWeight"] = "SemiBold",
        }));
        els.Add(Btn(page, "报警复位", 280, 830, 180, 44, "报警复位流程", "#FF475569", 6, flowName: "AlarmReset"));
        els.Add(Nav(page, "返回总览", 480, 830, "产线总览"));

        els.Add(Card(page, "报警统计卡", 1260, 180, 600, 460, "报警次数统计", "#FFF59E0B"));
        els.Add(El(page, "报警柱图", "Hmi.BarChart", 1280, 225, 560, 390, 10, new JObject
        {
            ["BarSeries"] = JsonConvert.SerializeObject(new object[]
            {
                new { Label = "报警", Variable = "AlarmCount", Color = "#FFEF4444" },
                new { Label = "警告", Variable = "WarnCount", Color = "#FFF59E0B" },
                new { Label = "不良", Variable = "NGCount", Color = "#FFA855F7" },
            }),
            ["Horizontal"] = "True", ["MaxValue"] = "0", ["TargetValue"] = "0", ["ShowValues"] = "True",
            ["SortMode"] = "Desc", ["ValueFormat"] = "0",
            ["Fill"] = "#FF151C23", ["Stroke"] = "#FF2A3745", ["CornerRadius"] = "8",
        }));

        els.Add(Card(page, "报警趋势卡", 1260, 660, 600, 380, "质量与节拍趋势", "#FF38BDF8"));
        els.Add(El(page, "报警趋势", "Hmi.MultiTrend", 1280, 705, 560, 310, 10, new JObject
        {
            ["Value1"] = "0", ["Value2"] = "0", ["Value3"] = "0", ["Value4"] = "0",
            ["Series1Name"] = "缺陷数", ["Series2Name"] = "不良数", ["Series3Name"] = "良率", ["Series4Name"] = "节拍",
            ["Series1Color"] = "#FFEF4444", ["Series2Color"] = "#FFF59E0B", ["Series3Color"] = "#FF22C55E", ["Series4Color"] = "#FF38BDF8",
            ["TimeWindow"] = "300", ["RangeMode"] = "Independent", ["ShowLegend"] = "True",
            ["Fill"] = "#FF151C23", ["Stroke"] = "#FF2A3745", ["StrokeThickness"] = "1", ["CornerRadius"] = "8",
        }, new JArray
        {
            Bind("Value1", "DefectCount", "0"),
            Bind("Value2", "NGCount", "0"),
            Bind("Value3", "YieldRate", "F1"),
            Bind("Value4", "CycleCount", "0"),
        }));
        return LinePage(page, els);
    }

    // ---------------- 11. 设备诊断 ----------------
    static JObject LineDiagPage()
    {
        const string page = "设备诊断";
        var els = new JArray();
        ComplexHeader(els, page, "设备诊断 · 通讯 / 运动 / IO", "Modbus / S7 / 轴控台 / 位监视");

        els.Add(El(page, "诊断轴控台", "Hmi.AxisConsole", 60, 180, 520, 560, 10, new JObject
        {
            ["AxisOptions"] = AxisOptions("X", "Y", "Z"),
            ["ShowCommands"] = "True", ["ShowPoints"] = "True", ["PointsRows"] = "8",
            ["ConfirmCommands"] = "True", ["JogVelocity"] = "5", ["JogMode"] = "Continuous", ["JogStepMm"] = "1",
            ["Fill"] = "#FF0F1112", ["Stroke"] = "#FF15171A", ["StrokeThickness"] = "1", ["CornerRadius"] = "12",
            ["Text"] = "三轴调试台", ["Foreground"] = "#FFFAFAFA", ["FontSize"] = "12",
        }));

        els.Add(El(page, "旧点位表", "Hmi.AxisPointTable", 60, 760, 520, 300, 10, new JObject
        {
            ["AxisOptions"] = AxisOptions("X", "Y", "Z"),
            ["ShowCommands"] = "True", ["ShowPoints"] = "True", ["PointsRows"] = "6",
            ["Fill"] = "#FF0F1112", ["Stroke"] = "#FF15171A", ["StrokeThickness"] = "1", ["CornerRadius"] = "12",
            ["Text"] = "点位表（旧版兼容）", ["Foreground"] = "#FFFAFAFA", ["FontSize"] = "11",
        }));

        els.Add(Card(page, "通讯面板", 620, 180, 620, 260, "通讯寄存器", "#FF22D3EE"));
        els.Add(Val(page, "PLC触发", 645, 230, 270, "PLC_Trigger", "PLC 触发", "0"));
        els.Add(Val(page, "PLC确认", 645, 275, 270, "PLC_Ack", "PLC 确认", "0"));
        els.Add(Val(page, "仪表", 645, 320, 270, "Meter_Value", "仪表", "F3"));
        els.Add(Val(page, "压力", 645, 365, 270, "Pressure_Value", "压力", "F3"));
        els.Add(Val(page, "S7结果", 940, 230, 270, "S7_Result", "S7 结果", "0"));
        els.Add(Indicator(page, "S7心跳", 940, 285, "S7_Heartbeat", "S7 心跳"));

        els.Add(Card(page, "IO监视卡", 620, 460, 620, 300, "IO / 位 / 变量监视", "#FF22C55E"));
        els.Add(El(page, "诊断IO", "Hmi.IoMonitor", 640, 505, 580, 110, 10, new JObject
        {
            ["Value"] = "0", ["BitCount"] = "8", ["Columns"] = "8", ["ShowIndex"] = "True", ["ShowValue"] = "True",
            ["ShowLabels"] = "True", ["Labels"] = "I0.0,I0.1,I0.2,I0.3,I0.4,I0.5,I0.6,I0.7",
            ["OnColor"] = "#FF22C55E", ["OffColor"] = "#FF334155",
            ["Fill"] = "#FF151C23", ["Stroke"] = "#FF2A3745", ["StrokeThickness"] = "1", ["CornerRadius"] = "6",
            ["Text"] = "输入字节", ["Foreground"] = "#FFE6EEF7", ["FontSize"] = "12",
        }, new JArray { Bind("Value", "PLC_Trigger") }));
        els.Add(El(page, "诊断位表", "Hmi.BitTable", 640, 625, 580, 120, 10, new JObject
        {
            ["Value"] = "0", ["BitCount"] = "32", ["StartBit"] = "0", ["Columns"] = "8",
            ["ShowIndex"] = "True", ["ShowValue"] = "True", ["ShowLabels"] = "False",
            ["OnColor"] = "#FFF59E0B", ["OffColor"] = "#FF334155",
            ["Fill"] = "#FF151C23", ["Stroke"] = "#FF2A3745", ["StrokeThickness"] = "1", ["CornerRadius"] = "6",
            ["Text"] = "PLC 结果位", ["Foreground"] = "#FFE6EEF7", ["FontSize"] = "12",
        }, new JArray { Bind("Value", "PLC_Result") }));

        els.Add(Card(page, "诊断按钮卡", 1260, 180, 300, 580, "诊断操作", "#FFF59E0B"));
        els.Add(Btn(page, "运行诊断", 1285, 230, 250, 44, "运行通讯诊断", "#FF0EA5E9", 6, flowName: "Diag"));
        els.Add(WriteButton(page, "写触发1", 1285, 290, "PLC_Trigger", "1"));
        els.Add(WriteButton(page, "清触发0", 1285, 340, "PLC_Trigger", "0"));
        els.Add(WriteButton(page, "写S7=7", 1285, 390, "S7_Result", "7"));
        els.Add(WriteButton(page, "写确认1", 1285, 440, "PLC_Ack", "1"));
        els.Add(Btn(page, "运行主流程", 1285, 500, 250, 44, "运行主流程", "#FF22C55E", 6, flowName: "Main"));
        els.Add(Btn(page, "报警复位", 1285, 560, 250, 44, "报警复位", "#FFEF4444", 6, flowName: "AlarmReset"));
        els.Add(Btn(page, "执行标定", 1285, 700, 250, 44, "执行标定并写入方案", "#FFA855F7", 6, flowName: "Calib"));
        els.Add(Nav(page, "返回总览", 1285, 640, "产线总览"));

        els.Add(Card(page, "运动状态卡", 1580, 180, 280, 580, "运动状态", "#FFA855F7"));
        els.Add(Val(page, "X位置", 1605, 230, 230, "System.Motion.X.Position", "X 位置", "F3"));
        els.Add(Val(page, "Y位置", 1605, 275, 230, "System.Motion.Y.Position", "Y 位置", "F3"));
        els.Add(Val(page, "Z位置", 1605, 320, 230, "System.Motion.Z.Position", "Z 位置", "F3"));
        els.Add(Indicator(page, "X使能", 1605, 410, "System.Motion.X.Enabled", "X 使能"));
        els.Add(Indicator(page, "Y使能", 1725, 410, "System.Motion.Y.Enabled", "Y 使能"));
        els.Add(Indicator(page, "Z使能", 1605, 480, "System.Motion.Z.Enabled", "Z 使能"));
        els.Add(TextField(page, "X状态", 1605, 560, 230, "System.Motion.X.StatusText", "X 状态"));
        els.Add(MotionButton(page, "X回零", 1605, 600, 11, "X"));
        els.Add(MotionButton(page, "X停止", 1740, 600, 12, "X"));
        els.Add(MotionButton(page, "全轴使能", 1605, 650, 7, "X"));
        return LinePage(page, els);
    }

    // ---------------- 12. 配方与设置 ----------------
    static JObject LineRecipePage()
    {
        const string page = "配方与设置";
        var els = new JArray();
        ComplexHeader(els, page, "配方与参数设置", "配方 / 产品类型 / 速度 / 嵌入画面");

        els.Add(El(page, "配方只读", "Hmi.RecipePanel", 60, 180, 420, 320, 10, new JObject
        {
            ["Title"] = "当前生效配方", ["ShowDetails"] = "True", ["MaxRows"] = "8",
            ["Fill"] = "#FF151C23", ["Stroke"] = "#FF2A3745", ["StrokeThickness"] = "1", ["CornerRadius"] = "6",
            ["Foreground"] = "#FFE6EEF7",
        }));

        els.Add(Card(page, "参数编辑卡", 60, 520, 420, 420, "参数编辑", "#FF22D3EE"));
        els.Add(TextField(page, "配方名", 85, 570, 370, "RecipeName", "配方名"));
        els.Add(TextField(page, "批次号", 85, 615, 370, "BatchNo", "批次"));
        els.Add(TextField(page, "产品类型", 85, 660, 370, "ProductType", "产品类型"));
        els.Add(El(page, "运行模式下拉", "Hmi.ComboBox", 85, 705, 370, 40, 10, new JObject
        {
            ["Value"] = "1", ["Options"] = "0=手动;1=自动;2=检修", ["Mode"] = "InputOutput", ["Presentation"] = "Field",
            ["ShowSeparator"] = "False",
            ["Fill"] = "#FF151C23", ["Stroke"] = "#FF3A4A5C", ["StrokeThickness"] = "1", ["CornerRadius"] = "4",
            ["Foreground"] = "#FFE6EEF7", ["FontSize"] = "14", ["FontWeight"] = "Normal",
        }, new JArray { Bind("Value", "AutoMode") }));
        els.Add(El(page, "模式旋钮", "Hmi.SelectorSwitch", 85, 760, 370, 44, 10, new JObject
        {
            ["Value"] = "1", ["OptionCount"] = "3",
            ["Option0Text"] = "手动", ["Option1Text"] = "自动", ["Option2Text"] = "检修",
            ["Mode"] = "InputOutput", ["Fill"] = "#FF15803D", ["TrackColor"] = "#FF26313D",
            ["Stroke"] = "#FF2A3745", ["StrokeThickness"] = "1", ["CornerRadius"] = "8",
            ["FontSize"] = "14", ["FontWeight"] = "SemiBold",
        }, new JArray { Bind("Value", "AutoMode") }));
        els.Add(Btn(page, "应用配方", 85, 820, 180, 40, "应用配方", "#FF22C55E", 6, flowName: "Recipe"));
        els.Add(Btn(page, "清空计数", 285, 820, 170, 40, "清空计数", "#FFEF4444", 2, varName: "OKCount", value: "0"));

        els.Add(Card(page, "速度卡", 520, 180, 420, 320, "速度与目标", "#FF22C55E"));
        els.Add(El(page, "速度滑块", "Hmi.Slider", 545, 230, 370, 48, 10, new JObject
        {
            ["Value"] = "50", ["Minimum"] = "0", ["Maximum"] = "100", ["Step"] = "1", ["ValueFormat"] = "F0",
            ["Orientation"] = "Horizontal", ["TrackColor"] = "#FF1E293B", ["ShowValue"] = "True",
            ["Mode"] = "InputOutput", ["WriteMode"] = "Release", ["ConfirmDanger"] = "False",
            ["Fill"] = "#FF0EA5E9", ["Stroke"] = "#FF2A3745", ["StrokeThickness"] = "1", ["CornerRadius"] = "4",
            ["Foreground"] = "#FFE6EEF7", ["FontSize"] = "12",
        }, new JArray { Bind("Value", "SpeedSetpoint") }));
        els.Add(El(page, "速度棒图", "Hmi.ProgressBar", 545, 290, 370, 22, 10, new JObject
        {
            ["Value"] = "50", ["Minimum"] = "0", ["Maximum"] = "100", ["ValueFormat"] = "F0",
            ["Orientation"] = "Horizontal", ["TrackColor"] = "#FF1E293B", ["ShowValue"] = "True", ["ShowTicks"] = "True",
            ["TickCount"] = "10", ["TickColor"] = "#FF475569",
            ["Fill"] = "#FF22C55E", ["Stroke"] = "#FF2A3745", ["StrokeThickness"] = "1", ["CornerRadius"] = "4",
            ["Foreground"] = "#FFFFFFFF", ["FontSize"] = "11",
        }, new JArray { Bind("Value", "SpeedSetpoint") }));
        els.Add(Val(page, "良率目标", 545, 340, 370, "GoodRateTarget", "良率目标", "F1"));
        els.Add(Val(page, "当前良率", 545, 385, 370, "YieldRate", "当前良率", "F1"));
        els.Add(Val(page, "当前OEE", 545, 430, 370, "OEE", "当前 OEE", "F1"));

        els.Add(Card(page, "嵌入画面卡", 980, 180, 880, 520, "嵌入画面（生产报表只读）", "#FFF59E0B"));
        els.Add(El(page, "嵌入报表", "Hmi.SubPage", 1000, 225, 840, 450, 10, new JObject
        {
            ["TargetPageId"] = G("page:生产报表").ToString(),
            ["TargetPageName"] = "生产报表",
            ["PageSource"] = "Fixed", ["PageName"] = "",
            ["Interactive"] = "False", ["Fit"] = "Uniform", ["ShowBackground"] = "True",
            ["MissingBehavior"] = "Placeholder",
            ["Fill"] = "#FF0E1318", ["Stroke"] = "#FF2A3745", ["StrokeThickness"] = "1", ["CornerRadius"] = "8",
        }));

        els.Add(Card(page, "品牌卡", 980, 720, 880, 220, "方案资产", "#FFA855F7"));
        els.Add(El(page, "品牌图", "Hmi.Picture", 1010, 765, 260, 150, 10, new JObject
        {
            ["SourcePath"] = SL_LOGO, ["Stretch"] = "Uniform", ["PlaceholderText"] = "LOGO",
            ["Fill"] = "#00FFFFFF", ["Stroke"] = "#00FFFFFF", ["StrokeThickness"] = "0", ["CornerRadius"] = "6",
        }));
        els.Add(El(page, "动态图", "Hmi.Gif", 1300, 765, 220, 150, 10, new JObject
        {
            ["SourcePath"] = SL_GIF, ["Stretch"] = "Uniform", ["PlaceholderText"] = "动图",
            ["Loop"] = "True", ["Fill"] = "#00FFFFFF", ["Stroke"] = "#00FFFFFF", ["StrokeThickness"] = "0", ["CornerRadius"] = "6",
        }));
        els.Add(Txt(page, "资产说明", 1550, 780, 280, 120, "方案图片全部位于方案内 Image\\，\n整包搬迁不断图。\n配方与速度参数可随方案保存。", 13, "#FF94A3B8"));
        els.Add(Nav(page, "返回总览", 520, 580, "产线总览"));
        els.Add(Nav(page, "报警中心", 680, 580, "报警中心"));
        return LinePage(page, els);
    }

    // ---------------- 13-15. 弹窗 ----------------
    static JObject LineConfirmPopup()
    {
        const string page = "参数确认";
        var els = new JArray
        {
            Txt(page, "标题", 40, 36, 480, 40, "确认应用配方？", 24, "#FFF8FAFC", "Bold"),
            Txt(page, "说明", 40, 100, 480, 70, "将把产品类型写为 A、批次写入当前批次，\n并清零良品/不良计数。", 14, "#FF94A3B8"),
            El(page, "确认", "Hmi.Button", 60, 210, 180, 44, 20, ButtonProps("确认", "#FF16A34A", "#FF15803D"), null,
                new JArray { Hook(2, new JArray { ScadaAct(2, varName: "ProductType", value: "A"), ScadaAct(20) }) }),
            El(page, "取消", "Hmi.Button", 300, 210, 180, 44, 20, ButtonProps("取消", "#FF475569", "#FF334155"), null,
                new JArray { Hook(2, new JArray { ScadaAct(20) }) }),
        };
        var obj = ScadaPageObj(page, 560, 300, els, popup: true);
        obj["Background"] = "#FF1E293B";
        return obj;
    }

    static JObject LineTracePopup()
    {
        const string page = "追溯详情";
        var els = new JArray
        {
            Txt(page, "标题", 40, 28, 680, 40, "追溯详情 · 当前产品", 24, "#FFF8FAFC", "Bold"),
            TextField(page, "SN", 40, 88, 320, "ProductSN", "产品 SN"),
            TextField(page, "批次", 380, 88, 320, "BatchNo", "批次"),
            El(page, "二维码", "Hmi.QRCode", 40, 150, 160, 160, 10, new JObject
            {
                ["CodeContent"] = "", ["ShowText"] = "False", ["QuietZone"] = "4", ["Ecc"] = "M", ["PlaceholderText"] = "码",
                ["Fill"] = "#00000000", ["Stroke"] = "#00000000", ["StrokeThickness"] = "0", ["CornerRadius"] = "6",
                ["Foreground"] = "#FFE6EEF7", ["FontSize"] = "11",
            }, new JArray { Bind("CodeContent", "ProductSN") }),
            El(page, "追溯表", "Hmi.DataGrid", 220, 150, 480, 280, 10, new JObject
            {
                ["GridColumns"] = JsonConvert.SerializeObject(new object[]
                {
                new { Header = "项目", Variable = "ProductSN", Width = 180, Align = "left", Format = "", Monospace = false },
                new { Header = "判定", Variable = "ResultText", Width = 0, Align = "left", Format = "", Monospace = true },
                }),
                ["SampleVariable"] = "", ["AckVariable"] = "", ["MaxRows"] = "100",
                ["RowHeight"] = "28", ["NewestFirst"] = "False", ["Zebra"] = "True",
                ["Source"] = "Live", ["Text"] = "追溯明细",
            }),
            El(page, "关闭", "Hmi.Button", 540, 450, 160, 40, 20, ButtonProps("关闭", "#FF475569", "#FF334155"), null,
                new JArray { Hook(2, new JArray { ScadaAct(20) }) }),
        };
        var obj = ScadaPageObj(page, 760, 520, els, popup: true);
        obj["Background"] = "#FF1E293B";
        return obj;
    }

    static JObject LineHelpPopup()
    {
        const string page = "操作帮助";
        var els = new JArray
        {
            Txt(page, "标题", 40, 28, 620, 40, "操作帮助", 24, "#FFF8FAFC", "Bold"),
            Txt(page, "正文", 40, 88, 640, 300,
                "1. 总览页「产线启停」控制运行状态，模式旋钮切换手动/自动/检修。\n" +
                "2. 「运行主流程」会依次调用 6 个工位流程并写入 CSV/MES/Excel。\n" +
                "3. 工位页可单独重跑当前工位，便于调试。\n" +
                "4. 报警中心「确认全部」清除已确认报警；「报警复位流程」清故障与轴停止。\n" +
                "5. 设备诊断页可做 Modbus/S7 写读回环与四轴手动操作。\n" +
                "6. 生产报表按时间范围查询 History\\ 下的 CSV 账本，可导出 Excel/打印。",
                14, "#FFCBD5E1"),
            El(page, "关闭", "Hmi.Button", 290, 400, 160, 40, 20, ButtonProps("关闭", "#FF475569", "#FF334155"), null,
                new JArray { Hook(2, new JArray { ScadaAct(20) }) }),
        };
        var obj = ScadaPageObj(page, 720, 470, els, popup: true);
        obj["Background"] = "#FF1E293B";
        obj["PopupModal"] = false;
        return obj;
    }

    static string AxisOptions(params string[] names)
    {
        var arr = new JArray();
        foreach (var n in names)
            arr.Add(new JObject { ["AxisId"] = G("axis:" + n).ToString(), ["Name"] = n });
        return JsonConvert.SerializeObject(arr);
    }
}
