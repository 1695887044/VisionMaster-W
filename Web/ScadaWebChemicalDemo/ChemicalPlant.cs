using System;
using System.Collections.Generic;
using System.Globalization;
using VisionMaster.Models;
using VisionMaster.Scada;

namespace ScadaWebChemicalDemo
{
    /// <summary>
    /// 化工装置画面集：6 张运行画面 + 1 张跨页基座。
    ///
    /// <b>工艺设定（虚构但按真实化工装置的量纲与位号习惯给）</b>
    /// ---------
    /// 一套"乙烯裂解 + 精馏"装置：
    /// <code>
    ///   原料罐区 → 进料泵 P-1102 → 预热器 E-1301 → 反应釜 R-1201 → 精馏塔 T-1401
    ///                                            ↓                    ↓
    ///                                     冷却器 E-1302         回流泵 P-1403
    ///                                            ↓                    ↓
    ///                                       成品罐 T-1501 ←──── 塔顶冷凝器 E-1404
    /// </code>
    /// 位号遵循 ISA-5.1：TT 温度变送、PT 压力变送、FT 流量变送、LT 液位变送、
    /// FV 流量调节阀、TV 温度调节阀、PV 压力调节阀、P 泵、E 换热器、R 反应器、T 储罐。
    ///
    /// <b>为什么值要用随机游走而不是常量</b>
    /// ---------
    /// 常量值下"值帧"永远只发一帧就稳定了，压不出合并/去重/带宽；
    /// 而真实现场的模拟量一直在小幅度抖动，那才是值帧的真实负载。
    /// 游走带软性回归（向设定值回拉），所以跑几小时也不会漂到量程外。
    /// </summary>
    internal sealed class ChemicalPlant
    {
        private const double CanvasWidth = 1920;
        private const double CanvasHeight = 1080;

        private readonly List<PlantVariable> _variables = new();

        public IReadOnlyList<PlantVariable> Variables => _variables;

        public ScadaDocument Build()
        {
            var document = new ScadaDocument();

            // ── 变量先建好（画面与报警都要引用它们的 Guid）──────────────────────
            BuildVariables();

            // ── 基座画面：所有页共用的顶栏 + 底部状态条 ───────────────────────
            var basePage = document.AddPage("公共基座");
            BuildBasePage(basePage);
            document.TryAddBasePage(basePage, out _);

            // ── 六张运行画面 ─────────────────────────────────────────────────
            var overview = document.AddPage("装置总览", basePage.PageId);
            BuildOverview(overview);

            var reactor = document.AddPage("反应釜 R-1201", basePage.PageId);
            BuildReactor(reactor);

            var column = document.AddPage("精馏塔 T-1401", basePage.PageId);
            BuildColumn(column);

            var heat = document.AddPage("换热站 E-1301", basePage.PageId);
            BuildHeatStation(heat);

            var alarmPage = document.AddPage("报警与趋势", basePage.PageId);
            BuildAlarmPage(alarmPage);

            var recipe = document.AddPage("配方与联锁", basePage.PageId);
            BuildRecipePage(recipe);

            // ── 导航按钮：每页都摆一条"去其它页"的按钮带（基座里做不到——基座不能有事件靶）──
            var pages = new[] { overview, reactor, column, heat, alarmPage, recipe };
            foreach (var page in pages)
                AddNavigation(page, pages);

            // ── 报警定义：真实限值 + 回差 + 延时 ──────────────────────────────
            BuildAlarms(document);

            document.SetStartupPage(overview);
            document.EnsureIdentity();

            return document;
        }

        // ======================================================================
        //  变量
        // ======================================================================

        private void BuildVariables()
        {
            // 反应釜 R-1201
            Add("TT-1201 反应温度", "℃", 168.0, 150, 190, drift: 0.6, isText: false);
            Add("PT-1202 反应压力", "MPa", 1.62, 1.2, 2.2, drift: 0.012);
            Add("LT-1203 釜液位", "%", 62.0, 0, 100, drift: 0.8);
            Add("FT-1204 进料流量", "m³/h", 42.5, 0, 80, drift: 0.7);
            Add("ST-1205 搅拌转速", "rpm", 320.0, 0, 600, drift: 4.0);
            Add("TT-1206 夹套温度", "℃", 145.0, 100, 180, drift: 0.5);

            // 精馏塔 T-1401
            Add("TT-1401 塔顶温度", "℃", 78.5, 50, 120, drift: 0.4);
            Add("TT-1402 塔底温度", "℃", 142.0, 100, 180, drift: 0.5);
            Add("PT-1403 塔顶压力", "MPa", 0.42, 0.2, 0.8, drift: 0.006);
            Add("LT-1404 塔釜液位", "%", 55.0, 0, 100, drift: 0.9);
            Add("FT-1405 回流量", "m³/h", 28.0, 0, 60, drift: 0.5);
            Add("FT-1406 塔顶采出", "m³/h", 12.5, 0, 30, drift: 0.35);

            // 换热站 E-1301 / E-1302
            Add("TT-1301 壳程出口", "℃", 132.0, 80, 170, drift: 0.6);
            Add("TT-1302 管程出口", "℃", 96.0, 60, 140, drift: 0.5);
            Add("PT-1303 蒸汽压力", "MPa", 0.86, 0.4, 1.4, drift: 0.01);
            Add("FT-1304 蒸汽流量", "kg/h", 2450.0, 0, 5000, drift: 28);
            Add("TT-1305 冷却水回水", "℃", 38.5, 20, 60, drift: 0.4);

            // 罐区 / 公用工程
            Add("LT-1501 成品罐液位", "%", 71.0, 0, 100, drift: 0.6);
            Add("LT-1101 原料罐液位", "%", 44.0, 0, 100, drift: 0.7);
            Add("FT-1102 原料进料", "m³/h", 46.0, 0, 90, drift: 0.6);
            Add("PT-1601 仪表风压力", "MPa", 0.62, 0.4, 0.9, drift: 0.008);
            Add("TT-1602 环境温度", "℃", 24.5, -10, 50, drift: 0.3);

            // 设备状态（0=停机 1=运行 2=故障）——按整数走，绑到 State
            AddState("P-1102 进料泵");
            AddState("P-1403 回流泵");
            AddState("P-1504 成品泵");
            AddState("M-1201 搅拌电机");
            AddState("E-1301 换热器");
            AddState("E-1404 塔顶冷凝器");

            // 阀门开度（0~100）
            AddPercent("FV-1103 进料调节阀", 64);
            AddPercent("FV-1405 回流调节阀", 52);
            AddPercent("TV-1301 蒸汽调节阀", 48);
            AddPercent("PV-1403 塔压调节阀", 41);
            AddPercent("FV-1501 成品采出阀", 35);

            // 开关量
            AddBool("XS-1201 反应釜运行", true);
            AddBool("XS-1401 精馏塔运行", true);
            AddBool("XA-1201 高温联锁", false);
            AddBool("XA-1401 高压联锁", false);

            // 文本：批号 / 配方 / 班组
            AddText("BATCH-1201 当前批号", "CR-20261021-047");
            AddText("RECIPE-1201 配方名", "EP-高抗冲-A");
            AddText("SHIFT-01 班组", "甲班 · 张工");
            AddText("LINE-01 产线", "1# 乙烯裂解线");

            // 操作模式（0/1/2 三档）：给多档旋钮用的专用变量。
            //
            // 为什么不复用阀门开度：旋钮的 Value 是"档位号 0~3"，而阀门开度是 0~100。
            // 绑到开度上会有两个后果——① 旋钮显示"第 47 档"（越界），
            // ② 用户拨档位时把阀门开度写成 0/1/2，阀门几乎全关。
            // 这类"类型对得上、语义对不上"的错绑在界面上看不出来，只有跑起来才露。
            Add("MODE-1201 控制方式", "", 1, 0, 2, drift: 0);
            Add("MODE-1405 回流模式", "", 1, 0, 2, drift: 0);
            Add("MODE-1301 控制模式", "", 1, 0, 2, drift: 0);
            Add("MODE-0101 班组", "", 0, 0, 2, drift: 0);

            // 操作备注：给文本输入域用的**可写**文本变量。
            //
            // 为什么不能绑「SHIFT-01 班组」：那个变量是只读的展示串（PlantVariable.IsText
            // 的写入一律拒绝），绑到输入域上会让"输入备注"必然失败。
            // 夹具里另开一个可写的备注变量，写入路径才是通的。
            AddText("REMARK-0101 操作备注", "接班检查完成");

            // 计数类（KPI 看板）
            Add("KPI-01 本班产量", "t", 1284.0, 0, 5000, drift: 2.0);
            Add("KPI-02 良品率", "%", 98.6, 90, 100, drift: 0.05);
            Add("KPI-03 单位能耗", "kWh/t", 412.0, 300, 600, drift: 1.2);
            Add("KPI-04 运行时长", "h", 186.0, 0, 8000, drift: 0.02);
        }

        private PlantVariable Add(string name, string unit, double initial, double low, double high,
            double drift, bool isText = false)
        {
            var variable = new PlantVariable(Guid.NewGuid(), name, unit, initial, low, high, drift);
            _variables.Add(variable);
            return variable;
        }

        private PlantVariable AddState(string name)
            => Add(name, string.Empty, 1, 0, 2, drift: 0);

        private PlantVariable AddPercent(string name, double initial)
            => Add(name, "%", initial, 0, 100, drift: 1.4);

        private PlantVariable AddBool(string name, bool initial)
        {
            var variable = new PlantVariable(Guid.NewGuid(), name, string.Empty, initial ? 1 : 0, 0, 1, 0)
            {
                IsBoolean = true
            };
            _variables.Add(variable);
            return variable;
        }

        private PlantVariable AddText(string name, string text)
        {
            var variable = new PlantVariable(Guid.NewGuid(), name, string.Empty, 0, 0, 0, 0)
            {
                IsText = true
            };

            variable.SetInitialText(text);
            _variables.Add(variable);
            return variable;
        }

        /// <summary>按变量名找（画面组装时用；名字写错会当场抛，不会静默绑空）</summary>
        private PlantVariable V(string name)
        {
            foreach (var variable in _variables)
                if (string.Equals(variable.Name, name, StringComparison.Ordinal))
                    return variable;

            throw new InvalidOperationException($"夹具里没有名为「{name}」的变量");
        }

        /// <summary>
        /// 推进一轮模拟量（模拟 PLC 轮询）。带软性回归：向设定值回拉一点，
        /// 所以连跑几小时也不会漂出量程，而短期看仍然是"活的数"。
        /// </summary>
        public void Advance()
        {
            foreach (var variable in _variables)
                variable.Advance();
        }

        // ======================================================================
        //  基座画面：顶栏 + 底部状态条（所有页共用）
        // ======================================================================

        private static void BuildBasePage(ScadaPage page)
        {
            page.Width = CanvasWidth;
            page.Height = CanvasHeight;
            page.Background = "#FF0E1318";

            // 顶栏底纹
            Element(page, "Hmi.Rectangle", "顶栏底纹", 0, 0, CanvasWidth, 92)
                .Props(("Fill", "#FF111A24"), ("Stroke", "#FF1E2A36"), ("StrokeThickness", "1"),
                       ("CornerRadius", "0"), ("Text", ""));

            // 顶栏左侧：装置名（文字）
            Element(page, "Hmi.Text", "装置名", 28, 18, 620, 34)
                .Props(("Text", "1# 乙烯裂解装置 · 运行监控"),
                       ("Foreground", "#FFE6EEF7"), ("FontSize", "22"), ("FontWeight", "Bold"),
                       ("TextAlignment", "Left"), ("VerticalContentAlignment", "Center"));

            // 顶栏右侧：时钟（走时，纯客户端）
            Element(page, "Hmi.Clock", "系统时间", 1620, 16, 272, 32)
                .Props(("Format", "yyyy-MM-dd HH:mm:ss"), ("Interval", "1000"),
                       ("Foreground", "#FFE6EEF7"), ("FontSize", "15"), ("FontWeight", "SemiBold"),
                       ("TextAlignment", "Right"));

            // 顶栏右侧：班组
            Element(page, "Hmi.IOField", "当前班组", 1400, 52, 240, 26)
                .Props(("Text", "班组"), ("Value", "甲班 · 张工"), ("Unit", ""),
                       ("Foreground", "#FFA9B7C6"), ("FontSize", "13"), ("TextAlignment", "Right"));

            // 底部状态条
            Element(page, "Hmi.Rectangle", "底栏底纹", 0, CanvasHeight - 44, CanvasWidth, 44)
                .Props(("Fill", "#FF111A24"), ("Stroke", "#FF1E2A36"), ("StrokeThickness", "1"),
                       ("CornerRadius", "0"), ("Text", ""));

            Element(page, "Hmi.Text", "底栏说明", 28, CanvasHeight - 36, 700, 28)
                .Props(("Text", "数据来源：PLC 轮询 500ms · 模拟装置（非真实设备）"),
                       ("Foreground", "#FF7C8A99"), ("FontSize", "12"),
                       ("TextAlignment", "Left"), ("VerticalContentAlignment", "Center"));

            // 底栏右侧：在线人数由顶栏显示，这里放一个运行指示
            Element(page, "Hmi.Indicator", "系统运行", 1800, CanvasHeight - 40, 92, 32)
                .Props(("IsOn", "True"), ("OnColor", "#FF34C759"), ("OffColor", "#FF3A3A3A"),
                       ("Shape", "Circle"), ("Stroke", "#FF5A5A5A"), ("StrokeThickness", "1"),
                       ("Text", ""), ("FontSize", "11"));
        }

        // ======================================================================
        //  画面 1：装置总览
        // ======================================================================

        private void BuildOverview(ScadaPage page)
        {
            page.Width = CanvasWidth;
            page.Height = CanvasHeight;
            page.Background = "#FF0E1318";

            Element(page, "Hmi.Text", "画面标题", 28, 104, 800, 40)
                .Props(("Text", "装置总览"), ("Foreground", "#FFE6EEF7"), ("FontSize", "26"),
                       ("FontWeight", "Bold"), ("TextAlignment", "Left"));

            // ── 左侧：工艺主流程（罐 → 泵 → 预热器 → 反应釜 → 塔 → 成品罐）──
            var flow = Element(page, "Hmi.Panel", "主工艺流程", 28, 152, 1160, 620)
                .Props(("Fill", "#FF151C23"), ("Stroke", "#FF2A3745"), ("StrokeThickness", "1"),
                       ("CornerRadius", "10"), ("TitleText", "主工艺流程"),
                       ("TitleHeight", "38"), ("TitleFill", "#FF1B2530"),
                       ("TitleForeground", "#FFE6EEF7"), ("FontSize", "12"));

            // 管道（先把管子画出来，设备压在上面）
            Pipe(page, "进料管线", 130, 300, 150, 26, "LeftToRight", "V-1102 进料泵");
            Pipe(page, "预热器出口", 400, 300, 130, 26, "LeftToRight", "V-1301 预热器");
            Pipe(page, "反应釜出口", 700, 300, 150, 26, "LeftToRight", "V-1201 反应釜");
            Pipe(page, "塔顶回流", 980, 300, 150, 26, "LeftToRight", "V-1401 精馏塔");

            // 原料罐
            Tank(page, "原料罐 T-1101", 40, 210, 96, 170, "LT-1101 原料罐液位", "T-1101", 1000, "m³");
            // 进料泵
            Pump(page, "进料泵 P-1102", 120, 348, 76, 76, "P-1102 进料泵");
            // 预热器
            Ellipse(page, "预热器 E-1301", 290, 250, 96, 96, "E-1301", "#FF1B2530");
            // 反应釜
            Tank(page, "反应釜 R-1201", 560, 190, 130, 210, "LT-1203 釜液位", "R-1201", 12, "m³");
            // 精馏塔
            Tank(page, "精馏塔 T-1401", 850, 160, 130, 260, "LT-1404 塔釜液位", "T-1401", 30, "m³");
            // 成品罐
            Tank(page, "成品罐 T-1501", 1050, 210, 96, 170, "LT-1501 成品罐液位", "T-1501", 2000, "m³");

            // 设备状态灯
            Lamp(page, "反应釜状态", 600, 420, 52, 84, "XS-1201 反应釜运行", "运行");
            Lamp(page, "精馏塔状态", 890, 440, 52, 84, "XS-1401 精馏塔运行", "运行");

            // 关键工艺量（数值域）
            IOField(page, "反应温度", 60, 500, 250, 34, "TT-1201 反应温度", "℃", "0.0");
            IOField(page, "反应压力", 60, 546, 250, 34, "PT-1202 反应压力", "MPa", "0.00");
            IOField(page, "进料流量", 330, 500, 250, 34, "FT-1204 进料流量", "m³/h", "0.0");
            IOField(page, "搅拌转速", 330, 546, 250, 34, "ST-1205 搅拌转速", "rpm", "0");
            IOField(page, "塔顶温度", 600, 500, 250, 34, "TT-1401 塔顶温度", "℃", "0.0");
            IOField(page, "塔顶压力", 600, 546, 250, 34, "PT-1403 塔顶压力", "MPa", "0.000");
            IOField(page, "回流量", 870, 500, 250, 34, "FT-1405 回流量", "m³/h", "0.0");
            IOField(page, "塔顶采出", 870, 546, 250, 34, "FT-1406 塔顶采出", "m³/h", "0.0");

            // ── 右侧：KPI 看板 + 报警条 ────────────────────────────────────
            Kpi(page, "本班生产指标", 1216, 152, 676, 220,
                ("KPI-01 本班产量", "本班产量", "t", "N0"),
                ("KPI-02 良品率", "良品率", "%", "0.0"),
                ("KPI-03 单位能耗", "单位能耗", "kWh/t", "0.0"),
                ("KPI-04 运行时长", "连续运行", "h", "N1"));

            AlarmBanner(page, "实时报警", 1216, 392, 676, 380);

            // ── 底部：四台关键设备的状态灯带 ────────────────────────────────
            Element(page, "Hmi.Panel", "设备状态", 28, 792, 1864, 230)
                .Props(("Fill", "#FF151C23"), ("Stroke", "#FF2A3745"), ("StrokeThickness", "1"),
                       ("CornerRadius", "10"), ("TitleText", "关键设备状态"),
                       ("TitleHeight", "34"), ("TitleFill", "#FF1B2530"),
                       ("TitleForeground", "#FFE6EEF7"), ("FontSize", "12"));

            Pump(page, "回流泵 P-1403", 80, 848, 78, 78, "P-1403 回流泵");
            Pump(page, "成品泵 P-1504", 240, 848, 78, 78, "P-1504 成品泵");
            Motor(page, "搅拌电机 M-1201", 400, 840, 78, 90, "M-1201 搅拌电机");

            Valve(page, "进料调节阀 FV-1103", 560, 856, 72, 64, "FV-1103 进料调节阀");
            Valve(page, "回流调节阀 FV-1405", 680, 856, 72, 64, "FV-1405 回流调节阀");
            Valve(page, "蒸汽调节阀 TV-1301", 800, 856, 72, 64, "TV-1301 蒸汽调节阀");
            Valve(page, "塔压调节阀 PV-1403", 920, 856, 72, 64, "PV-1403 塔压调节阀");
            Valve(page, "成品采出阀 FV-1501", 1040, 856, 72, 64, "FV-1501 成品采出阀");

            // 底部右侧：三块仪表（表盘）
            Gauge(page, "反应温度表", 1200, 830, 150, 150, "TT-1201 反应温度", "℃", 100, 220, "0.0");
            Gauge(page, "塔顶压力表", 1380, 830, 150, 150, "PT-1403 塔顶压力", "MPa", 0, 1.0, "0.00");
            Gauge(page, "进料流量表", 1560, 830, 150, 150, "FT-1204 进料流量", "m³/h", 0, 100, "0.0");

            // 进度条：成品罐液位
            ProgressBar(page, "成品罐液位", 1740, 856, 130, 30, "LT-1501 成品罐液位", 0, 100, "0.0");

            _ = flow;
        }

        // ======================================================================
        //  画面 2：反应釜 R-1201（单设备深度页）
        // ======================================================================

        private void BuildReactor(ScadaPage page)
        {
            page.Width = CanvasWidth;
            page.Height = CanvasHeight;
            page.Background = "#FF0E1318";

            Element(page, "Hmi.Text", "画面标题", 28, 104, 900, 40)
                .Props(("Text", "反应釜 R-1201 · 工艺参数"), ("Foreground", "#FFE6EEF7"),
                       ("FontSize", "26"), ("FontWeight", "Bold"), ("TextAlignment", "Left"));

            // 左：釜体大图 + 夹套 + 搅拌
            Element(page, "Hmi.Panel", "釜体", 28, 152, 620, 640)
                .Props(("Fill", "#FF151C23"), ("Stroke", "#FF2A3745"), ("StrokeThickness", "1"),
                       ("CornerRadius", "10"), ("TitleText", "釜体与搅拌"),
                       ("TitleHeight", "38"), ("TitleFill", "#FF1B2530"),
                       ("TitleForeground", "#FFE6EEF7"), ("FontSize", "12"));

            Tank(page, "反应釜本体", 200, 210, 260, 420, "LT-1203 釜液位", "R-1201", 12, "m³");
            Motor(page, "搅拌电机", 280, 640, 100, 116, "M-1201 搅拌电机");

            // 中：参数表
            Element(page, "Hmi.Panel", "工艺参数", 676, 152, 560, 640)
                .Props(("Fill", "#FF151C23"), ("Stroke", "#FF2A3745"), ("StrokeThickness", "1"),
                       ("CornerRadius", "10"), ("TitleText", "工艺参数"),
                       ("TitleHeight", "38"), ("TitleFill", "#FF1B2530"),
                       ("TitleForeground", "#FFE6EEF7"), ("FontSize", "12"));

            IOField(page, "反应温度", 700, 210, 512, 40, "TT-1201 反应温度", "℃", "0.0");
            IOField(page, "反应压力", 700, 262, 512, 40, "PT-1202 反应压力", "MPa", "0.00");
            IOField(page, "釜液位", 700, 314, 512, 40, "LT-1203 釜液位", "%", "0.0");
            IOField(page, "进料流量", 700, 366, 512, 40, "FT-1204 进料流量", "m³/h", "0.0");
            IOField(page, "搅拌转速", 700, 418, 512, 40, "ST-1205 搅拌转速", "rpm", "0");
            IOField(page, "夹套温度", 700, 470, 512, 40, "TT-1206 夹套温度", "℃", "0.0");

            // 联锁指示
            Lamp(page, "高温联锁", 700, 540, 56, 92, "XA-1201 高温联锁", "高温联锁");
            Lamp(page, "运行状态", 800, 540, 56, 92, "XS-1201 反应釜运行", "运行");
            ProgressBar(page, "釜液位棒图", 700, 660, 512, 34, "LT-1203 釜液位", 0, 100, "0.0");

            // 右：调节阀 + 表盘 + 报警
            Element(page, "Hmi.Panel", "调节与监视", 1264, 152, 628, 640)
                .Props(("Fill", "#FF151C23"), ("Stroke", "#FF2A3745"), ("StrokeThickness", "1"),
                       ("CornerRadius", "10"), ("TitleText", "调节与监视"),
                       ("TitleHeight", "38"), ("TitleFill", "#FF1B2530"),
                       ("TitleForeground", "#FFE6EEF7"), ("FontSize", "12"));

            Gauge(page, "反应温度表", 1300, 210, 170, 170, "TT-1201 反应温度", "℃", 100, 220, "0.0");
            Gauge(page, "反应压力表", 1490, 210, 170, 170, "PT-1202 反应压力", "MPa", 1.0, 2.4, "0.00");
            Valve(page, "进料调节阀", 1330, 410, 92, 82, "FV-1103 进料调节阀");
            Valve(page, "蒸汽调节阀", 1470, 410, 92, 82, "TV-1301 蒸汽调节阀");
            Valve(page, "塔压调节阀", 1610, 410, 92, 82, "PV-1403 塔压调节阀");

            AlarmBanner(page, "本页报警", 1292, 512, 572, 258);

            // 底部：操作按钮（会真的写变量 / 跳页）
            Element(page, "Hmi.Panel", "操作台", 28, 816, 1864, 200)
                .Props(("Fill", "#FF151C23"), ("Stroke", "#FF2A3745"), ("StrokeThickness", "1"),
                       ("CornerRadius", "10"), ("TitleText", "操作台（危险动作需确认）"),
                       ("TitleHeight", "34"), ("TitleFill", "#FF1B2530"),
                       ("TitleForeground", "#FFE6EEF7"), ("FontSize", "12"));

            Button(page, "启动搅拌", 60, 872, 170, 56, "启动搅拌", "P-1201 启动搅拌");
            Button(page, "停止搅拌", 250, 872, 170, 56, "停止搅拌", "P-1201 停止搅拌");
            BitButton(page, "高温联锁复位", 440, 872, 190, 56, "XA-1201 高温联锁", "复位");
            SelectorSwitch(page, "控制方式", 660, 872, 240, 56, "MODE-1201 控制方式", "手动", "自动", "串级");
            TextField(page, "批号输入", 930, 872, 320, 56, "BATCH-1201 当前批号", "批号");
            AckButton(page, "确认报警", 1290, 872, 200, 56);
            Button(page, "去精馏塔", 1530, 872, 170, 56, "去精馏塔", "请求查看精馏塔 T-1401");
            Button(page, "回总览", 1720, 872, 140, 56, "回总览", "请求返回装置总览");
        }

        // ======================================================================
        //  画面 3：精馏塔 T-1401
        // ======================================================================

        private void BuildColumn(ScadaPage page)
        {
            page.Width = CanvasWidth;
            page.Height = CanvasHeight;
            page.Background = "#FF0E1318";

            Element(page, "Hmi.Text", "画面标题", 28, 104, 900, 40)
                .Props(("Text", "精馏塔 T-1401 · 温度与回流"), ("Foreground", "#FFE6EEF7"),
                       ("FontSize", "26"), ("FontWeight", "Bold"), ("TextAlignment", "Left"));

            // 左：塔体剖面（多个罐体叠成塔段）
            Element(page, "Hmi.Panel", "塔体剖面", 28, 152, 700, 800)
                .Props(("Fill", "#FF151C23"), ("Stroke", "#FF2A3745"), ("StrokeThickness", "1"),
                       ("CornerRadius", "10"), ("TitleText", "塔体剖面"),
                       ("TitleHeight", "38"), ("TitleFill", "#FF1B2530"),
                       ("TitleForeground", "#FFE6EEF7"), ("FontSize", "12"));

            Tank(page, "塔顶段", 300, 206, 160, 180, "TT-1401 塔顶温度", "T-1401 顶", 6, "m³");
            Tank(page, "塔中段", 300, 396, 160, 180, "LT-1404 塔釜液位", "T-1401 中", 10, "m³");
            Tank(page, "塔釜段", 300, 586, 160, 220, "LT-1404 塔釜液位", "T-1401 釜", 14, "m³");

            // 塔内上升蒸汽：用三段**短横管**竖排，而不是一根窄高图元。
            //
            // 为什么不能用"窄而高"的 Hmi.Pipe：管道的箭头永远沿水平方向画
            // （`Direction` 只表达"朝左/朝右"，没有"朝上"这一档），
            // 摆成 24×596 就是一条竖直细管配一堆横向箭头，读起来像"水平管被立起来"，
            // 而不是"介质自下而上"。改成三段短横管叠起来——每一段都是正常水平管，
            // 位置上的上下关系表达"往上走"，这是工艺图上画竖直管路的常见画法。
            var steamState = V("E-1301 换热器");
            // 每段落在对应塔段的**上沿**（离顶 54px）：压在段中部会盖住底部的液位读数与位号
            // （第一版 y=580 正好压在"塔釜段"的顶边上，屏幕上读数被切了一半）。
            foreach (var (segmentName, top) in new[] { ("塔内蒸汽-下", 640), ("塔内蒸汽-中", 450), ("塔内蒸汽-上", 260) })
            {
                var segment = Element(page, "Hmi.Pipe", segmentName, 330, top, 100, 22)
                    .Props(("Direction", "RightToLeft"),
                           ("TrackColor", "#FF1B2733"), ("StoppedColor", "#FF7A7A7A"),
                           ("RunningColor", "#FF38BDF8"), ("FaultColor", "#FFE03A2B"),
                           ("Stroke", "#FF3A4A5C"), ("StrokeThickness", "1"));

                Bind(segment, "State", steamState);
            }

            // 中：参数
            Element(page, "Hmi.Panel", "塔参数", 756, 152, 520, 800)
                .Props(("Fill", "#FF151C23"), ("Stroke", "#FF2A3745"), ("StrokeThickness", "1"),
                       ("CornerRadius", "10"), ("TitleText", "塔参数"),
                       ("TitleHeight", "38"), ("TitleFill", "#FF1B2530"),
                       ("TitleForeground", "#FFE6EEF7"), ("FontSize", "12"));

            IOField(page, "塔顶温度", 780, 210, 472, 40, "TT-1401 塔顶温度", "℃", "0.0");
            IOField(page, "塔底温度", 780, 262, 472, 40, "TT-1402 塔底温度", "℃", "0.0");
            IOField(page, "塔顶压力", 780, 314, 472, 40, "PT-1403 塔顶压力", "MPa", "0.000");
            IOField(page, "塔釜液位", 780, 366, 472, 40, "LT-1404 塔釜液位", "%", "0.0");
            IOField(page, "回流量", 780, 418, 472, 40, "FT-1405 回流量", "m³/h", "0.0");
            IOField(page, "塔顶采出", 780, 470, 472, 40, "FT-1406 塔顶采出", "m³/h", "0.0");

            Gauge(page, "塔顶温度表", 790, 530, 190, 190, "TT-1401 塔顶温度", "℃", 40, 130, "0.0");
            Gauge(page, "塔顶压力表", 1000, 530, 190, 190, "PT-1403 塔顶压力", "MPa", 0, 1.0, "0.000");

            Valve(page, "回流调节阀", 800, 740, 92, 82, "FV-1405 回流调节阀");
            Valve(page, "塔压调节阀", 920, 740, 92, 82, "PV-1403 塔压调节阀");
            Pump(page, "回流泵", 1040, 740, 78, 78, "P-1403 回流泵");

            ProgressBar(page, "塔釜液位棒图", 780, 850, 472, 34, "LT-1404 塔釜液位", 0, 100, "0.0");

            // 右：报警 + 操作
            Element(page, "Hmi.Panel", "报警与操作", 1304, 152, 588, 800)
                .Props(("Fill", "#FF151C23"), ("Stroke", "#FF2A3745"), ("StrokeThickness", "1"),
                       ("CornerRadius", "10"), ("TitleText", "报警与操作"),
                       ("TitleHeight", "38"), ("TitleFill", "#FF1B2530"),
                       ("TitleForeground", "#FFE6EEF7"), ("FontSize", "12"));

            AlarmBanner(page, "塔报警", 1328, 206, 540, 420);

            Button(page, "启动回流泵", 1332, 660, 250, 56, "启动回流泵", "P-1403 启动");
            Button(page, "停止回流泵", 1600, 660, 250, 56, "停止回流泵", "P-1403 停止");
            SelectorSwitch(page, "回流模式", 1332, 736, 518, 56, "MODE-1405 回流模式", "全回流", "部分回流", "采出");
            AckButton(page, "确认报警", 1332, 812, 250, 56);
            Button(page, "回总览", 1600, 812, 250, 56, "回总览", "请求返回装置总览");
        }

        // ======================================================================
        //  画面 4：换热站 E-1301
        // ======================================================================

        private void BuildHeatStation(ScadaPage page)
        {
            page.Width = CanvasWidth;
            page.Height = CanvasHeight;
            page.Background = "#FF0E1318";

            Element(page, "Hmi.Text", "画面标题", 28, 104, 900, 40)
                .Props(("Text", "换热站 E-1301 / E-1302 · 蒸汽与冷却水"), ("Foreground", "#FFE6EEF7"),
                       ("FontSize", "26"), ("FontWeight", "Bold"), ("TextAlignment", "Left"));

            // 上：两台换热器 + 管路
            Element(page, "Hmi.Panel", "换热流程", 28, 152, 1220, 420)
                .Props(("Fill", "#FF151C23"), ("Stroke", "#FF2A3745"), ("StrokeThickness", "1"),
                       ("CornerRadius", "10"), ("TitleText", "换热流程"),
                       ("TitleHeight", "38"), ("TitleFill", "#FF1B2530"),
                       ("TitleForeground", "#FFE6EEF7"), ("FontSize", "12"));

            // 蒸汽进 → 预热器 → 冷凝器 → 塔顶冷凝器（一条水平主管，设备骑在管线上）
            Pipe(page, "蒸汽进管", 90, 250, 200, 26, "LeftToRight", "V-1301 蒸汽");
            Ellipse(page, "预热器 E-1301", 310, 200, 140, 140, "E-1301", "#FF1B2530");
            Pipe(page, "预热器出管", 470, 250, 190, 26, "LeftToRight", "V-1302 出料");
            Ellipse(page, "冷却器 E-1302", 680, 200, 140, 140, "E-1302", "#FF1B2530");
            Pipe(page, "冷凝器进管", 840, 250, 190, 26, "LeftToRight", "V-1404 冷凝");
            Ellipse(page, "塔顶冷凝器 E-1404", 1050, 200, 140, 140, "E-1404", "#FF1B2530");

            // 回水管（反向流动）：压在主管下方一条独立通道上，不与主管交叉
            Pipe(page, "冷却水回水", 680, 400, 320, 26, "RightToLeft", "V-1305 回水");

            // 阀门：放在两条管路之间的空档（y=320 起），既压不到换热器、也不压回水管。
            // 之前摆在 (200,330) 会与 E-1301 的圆（200..340 竖向范围）交叠，
            // 屏幕上就是"阀门骑在换热器边缘上"，读图时容易误认成同一台设备的一部分。
            Valve(page, "蒸汽调节阀", 180, 322, 86, 76, "TV-1301 蒸汽调节阀");
            Valve(page, "冷却水阀", 900, 322, 86, 76, "FV-1501 成品采出阀");

            // 下左：参数
            Element(page, "Hmi.Panel", "换热参数", 28, 596, 700, 420)
                .Props(("Fill", "#FF151C23"), ("Stroke", "#FF2A3745"), ("StrokeThickness", "1"),
                       ("CornerRadius", "10"), ("TitleText", "换热参数"),
                       ("TitleHeight", "38"), ("TitleFill", "#FF1B2530"),
                       ("TitleForeground", "#FFE6EEF7"), ("FontSize", "12"));

            IOField(page, "壳程出口温度", 52, 654, 652, 40, "TT-1301 壳程出口", "℃", "0.0");
            IOField(page, "管程出口温度", 52, 706, 652, 40, "TT-1302 管程出口", "℃", "0.0");
            IOField(page, "蒸汽压力", 52, 758, 652, 40, "PT-1303 蒸汽压力", "MPa", "0.000");
            IOField(page, "蒸汽流量", 52, 810, 652, 40, "FT-1304 蒸汽流量", "kg/h", "0");
            IOField(page, "冷却水回水温度", 52, 862, 652, 40, "TT-1305 冷却水回水", "℃", "0.0");
            IOField(page, "仪表风压力", 52, 914, 652, 40, "PT-1601 仪表风压力", "MPa", "0.000");

            // 下中：表盘
            Element(page, "Hmi.Panel", "换热监视", 756, 596, 592, 420)
                .Props(("Fill", "#FF151C23"), ("Stroke", "#FF2A3745"), ("StrokeThickness", "1"),
                       ("CornerRadius", "10"), ("TitleText", "换热监视"),
                       ("TitleHeight", "38"), ("TitleFill", "#FF1B2530"),
                       ("TitleForeground", "#FFE6EEF7"), ("FontSize", "12"));

            Gauge(page, "壳程温度表", 780, 654, 180, 180, "TT-1301 壳程出口", "℃", 60, 180, "0.0");
            Gauge(page, "蒸汽压力表", 980, 654, 180, 180, "PT-1303 蒸汽压力", "MPa", 0.2, 1.6, "0.00");

            // 棒图与状态灯各占一行：棒图在 856，灯在 908（原先把灯放在 840 会被棒图压掉一半）
            ProgressBar(page, "蒸汽流量棒图", 780, 856, 560, 34, "FT-1304 蒸汽流量", 0, 5000, "0");
            Lamp(page, "换热器状态", 1000, 906, 56, 92, "E-1301 换热器", "E-1301");

            // 下右：报警
            Element(page, "Hmi.Panel", "换热报警", 1376, 152, 516, 864)
                .Props(("Fill", "#FF151C23"), ("Stroke", "#FF2A3745"), ("StrokeThickness", "1"),
                       ("CornerRadius", "10"), ("TitleText", "换热报警"),
                       ("TitleHeight", "38"), ("TitleFill", "#FF1B2530"),
                       ("TitleForeground", "#FFE6EEF7"), ("FontSize", "12"));

            AlarmBanner(page, "换热报警条", 1400, 206, 468, 620);
            Button(page, "确认报警", 1400, 850, 220, 56, "确认报警", "ACK");
            Button(page, "回总览", 1650, 850, 218, 56, "回总览", "请求返回装置总览");
        }

        // ======================================================================
        //  画面 5：报警与趋势
        // ======================================================================

        private void BuildAlarmPage(ScadaPage page)
        {
            page.Width = CanvasWidth;
            page.Height = CanvasHeight;
            page.Background = "#FF0E1318";

            Element(page, "Hmi.Text", "画面标题", 28, 104, 900, 40)
                .Props(("Text", "报警与趋势"), ("Foreground", "#FFE6EEF7"),
                       ("FontSize", "26"), ("FontWeight", "Bold"), ("TextAlignment", "Left"));

            Element(page, "Hmi.Panel", "实时报警列表", 28, 152, 900, 700)
                .Props(("Fill", "#FF151C23"), ("Stroke", "#FF2A3745"), ("StrokeThickness", "1"),
                       ("CornerRadius", "10"), ("TitleText", "实时报警列表"),
                       ("TitleHeight", "38"), ("TitleFill", "#FF1B2530"),
                       ("TitleForeground", "#FFE6EEF7"), ("FontSize", "12"));

            AlarmBanner(page, "报警主列表", 52, 206, 852, 500);

            Button(page, "确认全部", 52, 730, 220, 56, "确认全部报警", "ACK");
            Button(page, "回总览", 300, 730, 220, 56, "回总览", "请求返回装置总览");

            // 右：关键趋势（用 KPI 看板表达"当前值 + 上下限"，曲线图元 Web 端未实现）
            Element(page, "Hmi.Panel", "关键量当前值", 952, 152, 940, 340)
                .Props(("Fill", "#FF151C23"), ("Stroke", "#FF2A3745"), ("StrokeThickness", "1"),
                       ("CornerRadius", "10"), ("TitleText", "关键量当前值"),
                       ("TitleHeight", "38"), ("TitleFill", "#FF1B2530"),
                       ("TitleForeground", "#FFE6EEF7"), ("FontSize", "12"));

            Kpi(page, "反应关键量", 976, 206, 892, 260,
                ("TT-1201 反应温度", "反应温度", "℃", "0.0"),
                ("PT-1202 反应压力", "反应压力", "MPa", "0.00"),
                ("LT-1203 釜液位", "釜液位", "%", "0.0"),
                ("ST-1205 搅拌转速", "搅拌转速", "rpm", "N0"));

            Element(page, "Hmi.Panel", "限值监视", 952, 516, 940, 336)
                .Props(("Fill", "#FF151C23"), ("Stroke", "#FF2A3745"), ("StrokeThickness", "1"),
                       ("CornerRadius", "10"), ("TitleText", "限值监视"),
                       ("TitleHeight", "38"), ("TitleFill", "#FF1B2530"),
                       ("TitleForeground", "#FFE6EEF7"), ("FontSize", "12"));

            ProgressBar(page, "反应温度 100~220℃", 976, 574, 892, 40, "TT-1201 反应温度", 100, 220, "0.0");
            ProgressBar(page, "反应压力 1.0~2.4MPa", 976, 630, 892, 40, "PT-1202 反应压力", 1.0, 2.4, "0.00");
            ProgressBar(page, "釜液位 0~100%", 976, 686, 892, 40, "LT-1203 釜液位", 0, 100, "0.0");
            ProgressBar(page, "塔顶压力 0~1.0MPa", 976, 742, 892, 40, "PT-1403 塔顶压力", 0, 1.0, "0.000");
            ProgressBar(page, "成品罐液位 0~100%", 976, 798, 892, 40, "LT-1501 成品罐液位", 0, 100, "0.0");

            // 底部：设备状态总览
            Element(page, "Hmi.Panel", "设备状态总览", 28, 872, 1864, 144)
                .Props(("Fill", "#FF151C23"), ("Stroke", "#FF2A3745"), ("StrokeThickness", "1"),
                       ("CornerRadius", "10"), ("TitleText", "设备状态总览"),
                       ("TitleHeight", "34"), ("TitleFill", "#FF1B2530"),
                       ("TitleForeground", "#FFE6EEF7"), ("FontSize", "12"));

            var statusVars = new[]
            {
                "P-1102 进料泵", "P-1403 回流泵", "P-1504 成品泵",
                "M-1201 搅拌电机", "E-1301 换热器", "E-1404 塔顶冷凝器"
            };

            for (var i = 0; i < statusVars.Length; i++)
            {
                // 宽 60 装不下「P-1102 进料泵」这种"位号 + 中文名"的标签（屏幕上被省略成
                // 「P-1102 ...」，位号后面的设备名全丢）。灯体是按短边取正方形的，
                // 所以加宽只会让灯更靠左、标签更完整，不会把灯拉成椭圆。
                Lamp(page, statusVars[i], 60 + i * 300, 916, 220, 88, statusVars[i], statusVars[i]);
            }
        }

        // ======================================================================
        //  画面 6：配方与联锁
        // ======================================================================

        private void BuildRecipePage(ScadaPage page)
        {
            page.Width = CanvasWidth;
            page.Height = CanvasHeight;
            page.Background = "#FF0E1318";

            Element(page, "Hmi.Text", "画面标题", 28, 104, 900, 40)
                .Props(("Text", "配方与联锁"), ("Foreground", "#FFE6EEF7"),
                       ("FontSize", "26"), ("FontWeight", "Bold"), ("TextAlignment", "Left"));

            // 左：配方参数（可编辑的输入域 + 下拉）
            Element(page, "Hmi.Panel", "配方参数", 28, 152, 900, 520)
                .Props(("Fill", "#FF151C23"), ("Stroke", "#FF2A3745"), ("StrokeThickness", "1"),
                       ("CornerRadius", "10"), ("TitleText", "配方参数（只读运行态）"),
                       ("TitleHeight", "38"), ("TitleFill", "#FF1B2530"),
                       ("TitleForeground", "#FFE6EEF7"), ("FontSize", "12"));

            IOField(page, "配方名", 52, 210, 852, 40, "RECIPE-1201 配方名", "", null);
            IOField(page, "当前批号", 52, 262, 852, 40, "BATCH-1201 当前批号", "", null);
            IOField(page, "反应温度设定", 52, 314, 852, 40, "TT-1201 反应温度", "℃", "0.0");
            IOField(page, "反应压力设定", 52, 366, 852, 40, "PT-1202 反应压力", "MPa", "0.00");
            IOField(page, "进料流量设定", 52, 418, 852, 40, "FT-1204 进料流量", "m³/h", "0.0");
            IOField(page, "搅拌转速设定", 52, 470, 852, 40, "ST-1205 搅拌转速", "rpm", "0");
            IOField(page, "塔顶温度设定", 52, 522, 852, 40, "TT-1401 塔顶温度", "℃", "0.0");
            IOField(page, "回流量设定", 52, 574, 852, 40, "FT-1405 回流量", "m³/h", "0.0");

            // 中：联锁状态
            Element(page, "Hmi.Panel", "联锁状态", 952, 152, 940, 520)
                .Props(("Fill", "#FF151C23"), ("Stroke", "#FF2A3745"), ("StrokeThickness", "1"),
                       ("CornerRadius", "10"), ("TitleText", "联锁与安全"),
                       ("TitleHeight", "38"), ("TitleFill", "#FF1B2530"),
                       ("TitleForeground", "#FFE6EEF7"), ("FontSize", "12"));

            Lamp(page, "高温联锁", 990, 210, 70, 108, "XA-1201 高温联锁", "高温联锁");
            Lamp(page, "高压联锁", 1110, 210, 70, 108, "XA-1401 高压联锁", "高压联锁");
            Lamp(page, "反应釜运行", 1230, 210, 70, 108, "XS-1201 反应釜运行", "反应釜");
            Lamp(page, "精馏塔运行", 1350, 210, 70, 108, "XS-1401 精馏塔运行", "精馏塔");

            SelectorSwitch(page, "控制模式", 990, 360, 380, 56, "MODE-1301 控制模式", "手动", "自动", "串级");
            SelectorSwitch(page, "班组", 1390, 360, 380, 56, "MODE-0101 班组", "甲班", "乙班", "丙班");

            TextField(page, "操作备注", 990, 440, 780, 56, "REMARK-0101 操作备注", "操作备注");

            Button(page, "高温联锁复位", 990, 530, 250, 56, "高温联锁复位", "XA-1201 复位");
            Button(page, "高压联锁复位", 1260, 530, 250, 56, "高压联锁复位", "XA-1401 复位");
            Button(page, "确认全部报警", 1530, 530, 240, 56, "确认全部", "ACK");

            // 下：操作日志说明 + 导航
            Element(page, "Hmi.Panel", "操作说明", 28, 700, 1864, 316)
                .Props(("Fill", "#FF151C23"), ("Stroke", "#FF2A3745"), ("StrokeThickness", "1"),
                       ("CornerRadius", "10"), ("TitleText", "操作说明"),
                       ("TitleHeight", "34"), ("TitleFill", "#FF1B2530"),
                       ("TitleForeground", "#FFE6EEF7"), ("FontSize", "12"));

            Element(page, "Hmi.Text", "说明正文", 52, 752, 1800, 200)
                .Props(("Text",
                        "· 本画面为 SCADA Web 运行态压力测试夹具，数据由模拟装置产生，不对应真实设备。\n" +
                        "· 危险动作（启动/停止设备、联锁复位）需要二次确认；确认令牌一次性、30 秒内有效。\n" +
                        "· 默认策略为 HighestRoleOnly：只有当前在线的最高角色可以操作，其余浏览器为只读。\n" +
                        "· 每一条命令都会写入审计（操作者、图元、事件、结果），可在服务端控制台看到。"),
                       ("Foreground", "#FFA9B7C6"), ("FontSize", "15"), ("FontWeight", "Normal"),
                       ("TextAlignment", "Left"), ("VerticalContentAlignment", "Top"));

            ProgressBar(page, "本班产量进度", 52, 950, 900, 34, "KPI-01 本班产量", 0, 5000, "0");
            Kpi(page, "运行指标", 990, 900, 880, 96,
                ("KPI-02 良品率", "良品率", "%", "0.0"),
                ("KPI-03 单位能耗", "单位能耗", "kWh/t", "0.0"));
        }

        // ======================================================================
        //  导航带：每页一组"去其它页"按钮
        // ======================================================================

        private void AddNavigation(ScadaPage page, ScadaPage[] pages)
        {
            // ── 导航带：**不做成按钮** ──────────────────────────────────────
            //
            // 这里原本摆的是一排「去其它页」按钮（Navigate 动作）。实测证明那是个错误设计：
            // Web 侧是**镜像模式**（方案 §7），`ScadaWebActionFilter.IsMirrorOnlyPageAction`
            // 把 `Navigate` / `NavigateBack` **无条件剔除**——切页是"现场动作"（桌面运行窗口那一次），
            // 不是某个浏览器的视图偏好；允许一个 Web 点击切页会同时改掉所有其它客户端看到的画面。
            // 所以那些按钮在网页上只会得到 `Skipped/mirror-mode`（动作全被剔除）与
            // `E_NOT_OPERABLE`（另一条：Web 按钮发 Pressed **和** Released 两个事件，
            // 而夹具只配了 Pressed）。
            //
            // 真正能验证"多画面"的路径是**宿主侧切页**：由桌面端（或本夹具的 --auto-page）
            // 调 ScadaRuntime.Navigate，Web 端在下一拍比较 PageKey 后收到整份 `page` 消息。
            // 这条路径才压得到"结构重建 + 定时器回收 + 基座重发"，见 Program.cs 的轮播定时器。
            //
            // 于是在这里放一条"画面索引"文字带，让浏览器知道自己正在看第几页，
            // 而不是放一排点了没反应的按钮。
            var index = Array.FindIndex(pages, p => ReferenceEquals(p, page)) + 1;

            Element(page, "Hmi.Text", "画面索引", 940, 108, 900, 34)
                .Props(("Text", $"画面 {index} / {pages.Length}　·　切页由现场（桌面运行端）发起，网页为镜像跟随"),
                       ("Foreground", "#FF7C8A99"), ("FontSize", "13"),
                       ("FontWeight", "Normal"), ("TextAlignment", "Right"),
                       ("VerticalContentAlignment", "Center"));
        }

        private static string ShortName(string pageName)
        {
            // 按钮上只放短名（"反应釜 R-1201" → "反应釜"），完整名在画面标题里
            var space = pageName.IndexOf(' ');
            return space > 0 ? pageName[..space] : pageName;
        }

        // ======================================================================
        //  报警定义
        // ======================================================================

        private void BuildAlarms(ScadaDocument document)
        {
            Alarm(document, "反应温度高限", "TT-1201 反应温度", ScadaAlarmKind.High,
                threshold: 180, severity: ScadaAlarmSeverity.Warning, deadband: 2, delay: 3,
                message: "反应温度超过 180℃，请检查冷却水与搅拌");

            Alarm(document, "反应温度高高限", "TT-1201 反应温度", ScadaAlarmKind.HighHigh,
                threshold: 186, severity: ScadaAlarmSeverity.Critical, deadband: 2, delay: 1,
                message: "反应温度高高限！立即执行降温联锁");

            Alarm(document, "反应压力高限", "PT-1202 反应压力", ScadaAlarmKind.High,
                threshold: 2.0, severity: ScadaAlarmSeverity.Warning, deadband: 0.03, delay: 3,
                message: "反应压力超过 2.0MPa，请检查放空阀");

            Alarm(document, "釜液位低限", "LT-1203 釜液位", ScadaAlarmKind.Low,
                threshold: 25, severity: ScadaAlarmSeverity.Warning, deadband: 2, delay: 5,
                message: "反应釜液位低于 25%，请及时补料");

            Alarm(document, "塔顶温度高限", "TT-1401 塔顶温度", ScadaAlarmKind.High,
                threshold: 92, severity: ScadaAlarmSeverity.Warning, deadband: 1.5, delay: 3,
                message: "塔顶温度超过 92℃，产品纯度可能下降");

            Alarm(document, "塔釜液位高限", "LT-1404 塔釜液位", ScadaAlarmKind.High,
                threshold: 88, severity: ScadaAlarmSeverity.Warning, deadband: 2, delay: 5,
                message: "塔釜液位超过 88%，注意液泛风险");

            Alarm(document, "蒸汽压力低限", "PT-1303 蒸汽压力", ScadaAlarmKind.Low,
                threshold: 0.55, severity: ScadaAlarmSeverity.Warning, deadband: 0.03, delay: 5,
                message: "蒸汽压力低于 0.55MPa，换热能力不足");

            Alarm(document, "成品罐液位高限", "LT-1501 成品罐液位", ScadaAlarmKind.High,
                threshold: 92, severity: ScadaAlarmSeverity.Warning, deadband: 2, delay: 5,
                message: "成品罐液位超过 92%，请安排外送");

            Alarm(document, "仪表风压力低限", "PT-1601 仪表风压力", ScadaAlarmKind.Low,
                threshold: 0.45, severity: ScadaAlarmSeverity.Critical, deadband: 0.02, delay: 3,
                message: "仪表风压力低！气动阀可能失位");

            Alarm(document, "进料泵故障", "P-1102 进料泵", ScadaAlarmKind.BoolOn,
                threshold: 2, severity: ScadaAlarmSeverity.Critical, deadband: 0, delay: 0,
                message: "进料泵 P-1102 故障停机");
        }

        private void Alarm(ScadaDocument document, string name, string variableName,
            ScadaAlarmKind kind, double threshold, ScadaAlarmSeverity severity,
            double deadband, double delay, string message)
        {
            var variable = V(variableName);

            var definition = document.AddAlarm(name, kind);
            definition.Bind(variable.VariableId, variable.Name);
            definition.Threshold = threshold;
            definition.Severity = severity;
            definition.Deadband = deadband;
            definition.DelaySeconds = delay;
            definition.Message = message;
            definition.IsEnabled = true;
        }

        // ======================================================================
        //  图元工厂
        // ======================================================================

        private static ScadaElement Element(ScadaPage page, string typeKey, string name,
            double x, double y, double width, double height)
        {
            var element = new ScadaElement
            {
                TypeKey = typeKey,
                Name = name,
                X = x,
                Y = y,
                Width = width,
                Height = height,
                ZIndex = (int)Math.Round(y)
            };

            page.Elements.Add(element);
            return element;
        }

        private static void Bind(ScadaElement element, string targetProperty, PlantVariable variable, string? format = null)
        {
            var binding = new ScadaBinding
            {
                TargetProperty = targetProperty,
                VariableId = variable.VariableId,
                VariableName = variable.Name,
                DisplayFormat = format,
                IsEnabled = true
            };

            element.Bindings.Add(binding);
        }

        private void Text(ScadaPage page, string name, double x, double y, double w, double h, string text,
            string color = "#FFE6EEF7", double fontSize = 14, string weight = "Normal")
        {
            Element(page, "Hmi.Text", name, x, y, w, h)
                .Props(("Text", text), ("Foreground", color), ("FontSize", F(fontSize)),
                       ("FontWeight", weight), ("TextAlignment", "Left"),
                       ("VerticalContentAlignment", "Center"));
        }

        private void IOField(ScadaPage page, string name, double x, double y, double w, double h,
            string variableName, string unit, string? format)
        {
            var element = Element(page, "Hmi.IOField", name, x, y, w, h)
                .Props(("Text", name), ("Unit", unit),
                       ("Foreground", "#FFE6EEF7"), ("FontSize", "16"),
                       ("FontWeight", "Normal"), ("TextAlignment", "Right"),
                       ("Fill", "#00151C23"), ("Stroke", "#FF3A4A5C"), ("StrokeThickness", "1"),
                       ("CornerRadius", "4"));

            Bind(element, "Value", V(variableName), format);
        }

        private void ProgressBar(ScadaPage page, string name, double x, double y, double w, double h,
            string variableName, double minimum, double maximum, string format)
        {
            var element = Element(page, "Hmi.ProgressBar", name, x, y, w, h)
                .Props(("Minimum", F(minimum)), ("Maximum", F(maximum)), ("ValueFormat", format),
                       ("Orientation", "Horizontal"), ("TrackColor", "#FF1B2733"),
                       ("ShowValue", "True"), ("Fill", "#FF0E7490"), ("Stroke", "#FF3A4A5C"),
                       ("StrokeThickness", "1"), ("CornerRadius", "6"),
                       ("Foreground", "#FFFFFFFF"), ("FontSize", "14"), ("FontWeight", "SemiBold"));

            Bind(element, "Value", V(variableName), format);
        }

        private void Gauge(ScadaPage page, string name, double x, double y, double w, double h,
            string variableName, string unit, double minimum, double maximum, string format)
        {
            var element = Element(page, "Hmi.Gauge", name, x, y, w, h)
                .Props(("Minimum", F(minimum)), ("Maximum", F(maximum)), ("ValueFormat", format),
                       ("Unit", unit), ("StartAngle", "135"), ("SweepAngle", "270"),
                       ("TickDivisions", "8"), ("TrackColor", "#FF1B2733"),
                       ("ScaleColor", "#FF7C8A99"), ("ShowValue", "True"),
                       ("Fill", "#FFE03A2B"), ("Stroke", "#FF3A4A5C"), ("StrokeThickness", "1"),
                       ("Foreground", "#FFE6EEF7"), ("FontSize", "13"), ("FontWeight", "SemiBold"),
                       ("TextAlignment", "Center"));

            Bind(element, "Value", V(variableName), format);
        }

        private void Tank(ScadaPage page, string name, double x, double y, double w, double h,
            string levelVariable, string tag, double capacity, string unit)
        {
            var element = Element(page, "Hmi.Tank", name, x, y, w, h)
                .Props(("Capacity", F(capacity)), ("Unit", unit), ("ShowValue", "True"),
                       ("TrackColor", "#FF1B2733"), ("Fill", "#FF38BDF8"),
                       ("Stroke", "#FF3A4A5C"), ("StrokeThickness", "1"), ("CornerRadius", "12"),
                       ("Text", tag), ("Foreground", "#FFE6EEF7"),
                       ("FontSize", "13"), ("FontWeight", "SemiBold"));

            Bind(element, "Level", V(levelVariable), "0.0");
        }

        private void Valve(ScadaPage page, string name, double x, double y, double w, double h,
            string openingVariable)
        {
            var element = Element(page, "Hmi.Valve", name, x, y, w, h)
                .Props(("OpeningFormat", "0"), ("TrackColor", "#FF1B2733"),
                       ("ThrottleColor", "#FFF59E0B"), ("OpenColor", "#FF22C55E"),
                       ("ShowValue", "True"), ("Fill", "#FF2D7DD2"),
                       ("Stroke", "#FF3A4A5C"), ("StrokeThickness", "1"),
                       ("Text", TagOf(name)), ("Foreground", "#FFE6EEF7"),
                       ("FontSize", "12"), ("FontWeight", "SemiBold"));

            Bind(element, "Opening", V(openingVariable), "0");
        }

        private void Pump(ScadaPage page, string name, double x, double y, double w, double h,
            string stateVariable)
        {
            var element = Element(page, "Hmi.Pump", name, x, y, w, h)
                .Props(("TrackColor", "#FF1B2530"), ("StoppedColor", "#FF7A7A7A"),
                       ("RunningColor", "#FF34C759"), ("FaultColor", "#FFE03A2B"),
                       ("Stroke", "#FF3A4A5C"), ("StrokeThickness", "1"),
                       ("Text", TagOf(name)), ("Foreground", "#FFE6EEF7"),
                       ("FontSize", "12"), ("FontWeight", "SemiBold"));

            Bind(element, "State", V(stateVariable));
        }

        private void Motor(ScadaPage page, string name, double x, double y, double w, double h,
            string stateVariable)
        {
            var element = Element(page, "Hmi.Motor", name, x, y, w, h)
                .Props(("TrackColor", "#FF1B2530"), ("StoppedColor", "#FF7A7A7A"),
                       ("RunningColor", "#FF34C759"), ("FaultColor", "#FFE03A2B"),
                       ("Stroke", "#FF3A4A5C"), ("StrokeThickness", "1"),
                       ("Text", TagOf(name)), ("Foreground", "#FFE6EEF7"),
                       ("FontSize", "12"), ("FontWeight", "SemiBold"));

            Bind(element, "State", V(stateVariable));
        }

        private void Pipe(ScadaPage page, string name, double x, double y, double w, double h,
            string direction, string tag)
        {
            var element = Element(page, "Hmi.Pipe", name, x, y, w, h)
                .Props(("Direction", direction), ("State", "Running"),
                       ("TrackColor", "#FF1B2733"), ("StoppedColor", "#FF7A7A7A"),
                       ("RunningColor", "#FF22C55E"), ("FaultColor", "#FFE03A2B"),
                       ("Stroke", "#FF3A4A5C"), ("StrokeThickness", "1"));

            _ = tag;
            _ = element;
        }

        private void Ellipse(ScadaPage page, string name, double x, double y, double w, double h,
            string tag, string fill)
        {
            Element(page, "Hmi.Ellipse", name, x, y, w, h)
                .Props(("Fill", fill), ("Stroke", "#FF3A4A5C"), ("StrokeThickness", "1"),
                       ("Text", tag), ("Foreground", "#FFE6EEF7"), ("FontSize", "12"));
        }

        private void Lamp(ScadaPage page, string name, double x, double y, double w, double h,
            string variableName, string label)
        {
            var variable = V(variableName);
            var element = Element(page, "Hmi.Lamp", name, x, y, w, h)
                .Props(("OffColor", "#FF3A3A3A"), ("OnColor", "#FF34C759"),
                       ("WarningColor", "#FFFFB020"), ("AlarmColor", "#FFE03A2B"),
                       ("Shape", "Circle"), ("Stroke", "#FF5A5A5A"), ("StrokeThickness", "1"),
                       ("Text", label), ("Foreground", "#FFE6EEF7"),
                       ("FontSize", "12"), ("FontWeight", "Normal"));

            Bind(element, "State", variable);
        }

        private void Kpi(ScadaPage page, string name, double x, double y, double w, double h,
            params (string VariableName, string Label, string Unit, string Format)[] slots)
        {
            var element = Element(page, "Hmi.KpiBoard", name, x, y, w, h)
                .Props(("Columns", slots.Length >= 4 ? "4" : "2"),
                       ("Fill", "#FF151C23"), ("Stroke", "#FF2A3745"), ("StrokeThickness", "1"),
                       ("CornerRadius", "8"), ("Text", name), ("Foreground", "#FFE6EEF7"));

            for (var i = 0; i < slots.Length && i < 4; i++)
            {
                var number = i + 1;
                var slot = slots[i];

                element.Properties["Label" + number] = slot.Label;
                element.Properties["Unit" + number] = slot.Unit;

                // 格式串必须给：KPI 格子只有一两百像素宽，未格式化的 double
                // 会以 "1284.692413..." 这种全精度上屏并被省略号截断——屏幕上只剩一串数字，
                // 读不出量级。这也是桌面端绑定默认要带 DisplayFormat 的同一条理由。
                Bind(element, "Value" + number, V(slot.VariableName), slot.Format);
            }
        }

        private void AlarmBanner(ScadaPage page, string name, double x, double y, double w, double h)
        {
            Element(page, "Hmi.AlarmBanner", name, x, y, w, h)
                .Props(("MaxRows", "8"), ("EmptyText", "系统正常 · 无活动报警"),
                       ("NormalColor", "#FF34C759"), ("InfoColor", "#FF2D7DD2"),
                       ("WarningColor", "#FFFFB020"), ("CriticalColor", "#FFE03A2B"),
                       ("Fill", "#FF151C23"), ("Stroke", "#FF2A3745"), ("StrokeThickness", "1"),
                       ("CornerRadius", "8"), ("Text", "实时报警"),
                       ("Foreground", "#FFE6EEF7"), ("FontSize", "12"), ("FontWeight", "SemiBold"));
        }

        /// <summary>
        /// 摆一个按钮。
        ///
        /// <b>两个必须同时给的钩子</b>
        /// ---------
        /// Web 的按钮渲染器（<c>elements-interactive.js</c>）在**按下**与**松手**时各发一次命令
        /// （<c>Pressed</c> / <c>Released</c>），这是刻意与桌面画布"按下、松手各是一个事件"对齐的。
        /// 只配 <c>Pressed</c> 的话，松手那一下会收到
        /// <c>E_NOT_OPERABLE：图元「…」没有为事件「Released」配置动作</c> ——
        /// 屏幕上就是点一下弹一个红条（2026-10-21 实测，浏览器截图与审计日志都能看到）。
        /// 所以夹具里凡是可点的按钮，两个事件配同一串动作：
        /// 动作本身是幂等的（记日志 / 写变量），按两次不会得到两种结果。
        ///
        /// <b>为什么没有 Navigate</b>
        /// ---------
        /// Web 侧是镜像模式，`Navigate` / `NavigateBack` 被 <c>ScadaWebActionFilter</c>
        /// 无条件剔除（见 AddNavigation 的注释）。夹具不再摆"点了能切页"的按钮，
        /// 切页由宿主侧的轮播定时器发起——那才是框架真正支持的路径。
        /// </summary>
        private void Button(ScadaPage page, string name, double x, double y, double w, double h,
            string text, string action)
        {
            var element = Element(page, "Hmi.Button", name, x, y, w, h)
                .Props(("Text", text), ("Fill", "#FF2D7DD2"), ("Stroke", "#FF1F5C9E"),
                       ("StrokeThickness", "1"), ("CornerRadius", "5"),
                       ("Foreground", "#FFFFFFFF"), ("FontSize", "15"), ("FontWeight", "SemiBold"));

            foreach (var eventType in new[] { ScadaEventType.Pressed, ScadaEventType.Released })
            {
                var hook = new ScadaEventHook { Event = eventType };

                hook.Actions.Add(new ScadaAction
                {
                    Type = ScadaActionType.Log,
                    // 事件名进日志：审计里能看出"按下"和"松手"各到了一次，
                    // 而不是只看到一条含义不明的"点了按钮"。
                    Text = string.Equals(action, "ACK", StringComparison.Ordinal)
                        ? $"{eventType}：操作员请求确认全部报警（请在报警确认按钮上操作）"
                        : $"{eventType}：{action}"
                });

                element.EventHooks.Add(hook);
            }
        }

        private void BitButton(ScadaPage page, string name, double x, double y, double w, double h,
            string variableName, string text)
        {
            var variable = V(variableName);
            var element = Element(page, "Hmi.BitButton", name, x, y, w, h)
                .Props(("Mode", "Toggle"), ("Text", text),
                       ("OnText", "已复位"), ("OffText", "未复位"),
                       ("OnFill", "#FF15803D"), ("OffFill", "#FF334155"),
                       ("OnForeground", "#FFFFFFFF"), ("OffForeground", "#FFE6EEF7"),
                       ("Stroke", "#FF1F5C9E"), ("StrokeThickness", "1"), ("CornerRadius", "5"),
                       ("FontSize", "14"), ("FontWeight", "SemiBold"));

            Bind(element, "IsOn", variable);
        }

        /// <summary>
        /// 多档旋钮。
        ///
        /// <b>属性键必须是 <c>Value</c>，不是 <c>SelectedIndex</c></b>
        /// ---------
        /// 桌面控件 <c>SelectorSwitchElement</c> 的档位号属性键就是 <c>Value</c>
        /// （<c>ScadaProperty(Key = "Value", DisplayName = "当前档位")</c>），
        /// Web 渲染器也按 <c>Value</c> 读写。第一版照"下拉框"的习惯写成
        /// <c>SelectedIndex</c>，绑定的目标属性在图元上根本不存在 ——
        /// 屏幕上点档位会得到
        /// <c>图元「回流模式」的「Value」没有启用全量模式，无法写入</c>
        /// （写入器按 Value 找绑定，找不到可写的那条）。ComboBox 才用 SelectedIndex，
        /// 两者不能互抄。
        ///
        /// <b>Mode 必须显式给 InputOutput</b>：默认值虽然就是 InputOutput，
        /// 但夹具把它写出来，是为了让"这个旋钮是可写的"在 .vms 里一眼可见——
        /// 只读旋钮（Mode=Display）点下去不会写任何东西。
        /// </summary>
        private void SelectorSwitch(ScadaPage page, string name, double x, double y, double w, double h,
            string variableName, params string[] options)
        {
            var element = Element(page, "Hmi.SelectorSwitch", name, x, y, w, h)
                .Props(("Mode", "InputOutput"),
                       ("OptionCount", options.Length.ToString(CultureInfo.InvariantCulture)),
                       ("Fill", "#FF15803D"), ("Stroke", "#FF2A3745"), ("StrokeThickness", "1"),
                       ("CornerRadius", "8"), ("FontSize", "14"), ("FontWeight", "SemiBold"));

            // 每档文字是独立键（Option0Text / Option1Text / …），不是逗号串
            for (var i = 0; i < options.Length && i < 4; i++)
                element.Properties["Option" + i + "Text"] = options[i];

            Bind(element, "Value", V(variableName));
        }

        private void TextField(ScadaPage page, string name, double x, double y, double w, double h,
            string variableName, string label)
        {
            var element = Element(page, "Hmi.TextField", name, x, y, w, h)
                .Props(("Text", label), ("MaxLength", "32"),
                       ("Placeholder", "请输入…"),
                       ("Fill", "#FF151C23"), ("Stroke", "#FF3A4A5C"), ("StrokeThickness", "1"),
                       ("CornerRadius", "4"), ("Foreground", "#FFE6EEF7"),
                       ("FontSize", "15"), ("FontWeight", "Normal"));

            Bind(element, "Value", V(variableName));
        }

        private void AckButton(ScadaPage page, string name, double x, double y, double w, double h)
        {
            Element(page, "Hmi.AckButton", name, x, y, w, h)
                .Props(("Scope", "All"), ("Text", "确认报警"),
                       ("Fill", "#FFEF4444"), ("Stroke", "#FFB91C1C"), ("StrokeThickness", "1"),
                       ("CornerRadius", "6"), ("Foreground", "#FFFFFFFF"),
                       ("FontSize", "15"), ("FontWeight", "SemiBold"));
        }

        /// <summary>从图元名里取位号（"进料泵 P-1102" → "P-1102"）；取不到就用原名</summary>
        private static string TagOf(string name)
        {
            var space = name.LastIndexOf(' ');
            return space > 0 && space < name.Length - 1 ? name[(space + 1)..] : name;
        }

        private static string F(double value)
            => value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// 图元组装的链式助手（<c>Element(...).Props(...)</c> / <c>.Rotate(...)</c>）。
    ///
    /// <b>为什么单独一个静态类</b>：C# 的扩展方法只能定义在非泛型静态类里，
    /// 而 <see cref="ChemicalPlant"/> 是个有实例状态的普通类（要往变量表里加东西）。
    /// 把这两个纯函数式的"属性袋赋值 / 旋转"挪出来，调用点就能保持
    /// <c>Element(...).Props(("Fill", "#FF151C23"), …)</c> 这种一眼看得出"这是同一个图元"的写法，
    /// 而不是每摆一个图元就多出三行 <c>element.Properties["…"] = "…"</c>。
    /// </summary>
    internal static class ScadaElementBuilderExtensions
    {
        /// <summary>批量写属性袋（键值对按顺序覆盖）</summary>
        public static ScadaElement Props(this ScadaElement element, params (string Key, string Value)[] pairs)
        {
            foreach (var (key, value) in pairs)
                element.Properties[key] = value;

            return element;
        }

        /// <summary>设旋转角（度，顺时针，绕图元中心）</summary>
        public static ScadaElement Rotate(this ScadaElement element, double degrees)
        {
            element.Rotation = degrees;
            return element;
        }
    }
}
