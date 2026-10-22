using Newtonsoft.Json.Linq;

internal static partial class Program
{
    static JObject BuildAutomationScada()
    {
        var overview = new JArray();
        ComplexHeader(overview, "产线总览", "自动化产线", "9 工位 · 检测 / 装配 / 分拣");
        overview.Add(El("产线总览", "KPI", "Hmi.KpiBoard", 60, 180, 900, 200, 10, new JObject
        {
            ["Columns"] = "4",
            ["Label1"] = "节拍", ["Value1"] = "0", ["Unit1"] = "次",
            ["Label2"] = "良品", ["Value2"] = "0", ["Unit2"] = "pcs",
            ["Label3"] = "不良", ["Value3"] = "0", ["Unit3"] = "pcs",
            ["Label4"] = "良率", ["Value4"] = "0", ["Unit4"] = "%",
        }, new JArray
        {
            Bind("Value1", "CycleCount"), Bind("Value2", "OKCount"),
            Bind("Value3", "NGCount"), Bind("Value4", "YieldRate"),
        }));
        overview.Add(Indicator("产线总览", "故障", 80, 430, "LineFault", "故障"));
        overview.Add(Indicator("产线总览", "上料", 160, 430, "St1_Ok", "上料"));
        overview.Add(Indicator("产线总览", "定位", 240, 430, "St2_Ok", "定位"));
        overview.Add(Indicator("产线总览", "装配", 320, 430, "St3_Ok", "装配"));
        overview.Add(Indicator("产线总览", "尺寸", 400, 430, "St4_Ok", "尺寸"));
        overview.Add(Indicator("产线总览", "表面", 480, 430, "St5_Ok", "表面"));
        overview.Add(TestButton("产线总览", "运行整线", 80, 540, "Main"));
        overview.Add(TestButton("产线总览", "应用配方", 260, 540, "Recipe"));
        overview.Add(NavButton("产线总览", "上料定位", 80, 620, "上料与定位"));
        overview.Add(NavButton("产线总览", "尺寸检测", 240, 620, "尺寸检测"));
        overview.Add(NavButton("产线总览", "报警统计", 400, 620, "报警与统计"));

        var load = new JArray();
        ComplexHeader(load, "上料与定位", "上料 / 定位", "载具识别 / X-Y-Z / 视觉定位");
        load.Add(TextField("上料与定位", "载具号", 80, 220, 320, "CarrierId", "载具号"));
        load.Add(IOField("上料与定位", "产品类型", 80, 280, "ProductTypeCode", "产品类型", "0"));
        load.Add(Indicator("上料与定位", "上料", 80, 350, "St1_Ok", "上料"));
        load.Add(Indicator("上料与定位", "定位", 160, 350, "St2_Ok", "定位"));
        load.Add(MotionButton("上料与定位", "X上料位", 80, 450, 9, "X", 0, 10));
        load.Add(MotionButton("上料与定位", "Y上料位", 230, 450, 9, "Y", 0, 10));
        load.Add(MotionButton("上料与定位", "Z上料位", 380, 450, 9, "Z", 0, 10));
        load.Add(NavButton("上料与定位", "返回总览", 80, 540, "产线总览"));

        var assemble = new JArray();
        ComplexHeader(assemble, "装配", "装配 / 拧紧", "Z 压装 / 扭矩");
        assemble.Add(IOField("装配", "扭矩", 80, 220, "Torque", "扭矩", "F2"));
        assemble.Add(Indicator("装配", "装配", 80, 300, "St3_Ok", "装配"));
        assemble.Add(MotionButton("装配", "Z压装", 80, 400, 9, "Z", 5, 10));
        assemble.Add(MotionButton("装配", "Z回原点", 230, 400, 9, "Z", 0, 10));
        assemble.Add(NavButton("装配", "返回总览", 80, 490, "产线总览"));

        var dimension = new JArray();
        ComplexHeader(dimension, "尺寸检测", "尺寸检测", "5 种卡尺 + 圆关系 / A-B-C 配方");
        dimension.Add(TextField("尺寸检测", "尺寸说明", 80, 220, 600, "St4_Message", "尺寸说明"));
        dimension.Add(Indicator("尺寸检测", "尺寸", 80, 300, "St4_Ok", "尺寸"));
        dimension.Add(IOField("尺寸检测", "产品类型", 80, 380, "ProductTypeCode", "产品类型", "0"));
        dimension.Add(NavButton("尺寸检测", "返回总览", 80, 470, "产线总览"));

        var surface = new JArray();
        ComplexHeader(surface, "表面与颜色", "表面 / 颜色", "Blob / 线序 / 区域颜色");
        surface.Add(IOField("表面与颜色", "缺陷数", 80, 220, "DefectCount", "缺陷数", "0"));
        surface.Add(Indicator("表面与颜色", "线序", 80, 300, "ColorSeqOk", "线序"));
        surface.Add(Indicator("表面与颜色", "区域色", 160, 300, "ColorRegionOk", "区域色"));
        surface.Add(Indicator("表面与颜色", "表面", 240, 300, "St5_Ok", "表面"));
        surface.Add(NavButton("表面与颜色", "返回总览", 80, 400, "产线总览"));

        var trace = new JArray();
        ComplexHeader(trace, "追溯与功能", "追溯 / 功能", "OCR / QR / 胶路 / 仪表");
        trace.Add(TextField("追溯与功能", "OCR", 80, 220, 400, "OcrText", "OCR"));
        trace.Add(TextField("追溯与功能", "条码", 80, 280, 400, "BarcodeText", "条码"));
        trace.Add(IOField("追溯与功能", "胶路错误", 80, 340, "BeadErrors", "胶路错误", "0"));
        trace.Add(IOField("追溯与功能", "功能值", 80, 400, "FunctionValue", "功能值", "F2"));
        trace.Add(Indicator("追溯与功能", "追溯", 80, 470, "St6_Ok", "追溯"));
        trace.Add(Indicator("追溯与功能", "功能", 160, 470, "St7_Ok", "功能"));
        trace.Add(NavButton("追溯与功能", "返回总览", 80, 550, "产线总览"));

        var sort = new JArray();
        ComplexHeader(sort, "分拣与下料", "分拣 / 下料", "OK / NG 仓位 / 贴标");
        sort.Add(IOField("分拣与下料", "良品", 80, 220, "OKCount", "良品", "0"));
        sort.Add(IOField("分拣与下料", "不良", 320, 220, "NGCount", "不良", "0"));
        sort.Add(TextField("分拣与下料", "仓位", 80, 290, 300, "SortBin", "仓位"));
        sort.Add(Indicator("分拣与下料", "分拣", 80, 370, "St8_Ok", "分拣"));
        sort.Add(Indicator("分拣与下料", "下料", 160, 370, "St9_Ok", "下料"));
        sort.Add(MotionButton("分拣与下料", "X下料位", 80, 460, 9, "X", 220, 20));
        sort.Add(MotionButton("分拣与下料", "Y良品位", 230, 460, 9, "Y", 10, 20));
        sort.Add(NavButton("分拣与下料", "返回总览", 80, 550, "产线总览"));

        var alarm = new JArray();
        ComplexHeader(alarm, "报警与统计", "报警 / 统计", "故障码 / 节拍 / 复位");
        alarm.Add(El("报警与统计", "报警列表", "Hmi.AlarmBanner", 60, 180, 1200, 360, 10, new JObject
        {
            ["MaxRows"] = "8", ["EmptyText"] = "当前无报警",
            ["NormalColor"] = "#FF16A34A", ["InfoColor"] = "#FF0EA5E9",
            ["WarningColor"] = "#FFF59E0B", ["CriticalColor"] = "#FFEF4444",
        }));
        alarm.Add(TextField("报警与统计", "故障码", 60, 570, 400, "FaultCode", "故障码"));
        alarm.Add(IOField("报警与统计", "节拍", 60, 630, "CycleCount", "节拍", "0"));
        alarm.Add(IOField("报警与统计", "良率", 300, 630, "YieldRate", "良率", "F1"));
        alarm.Add(El("报警与统计", "复位", "Hmi.Button", 560, 570, 160, 40, 20,
            ButtonProps("复位", "#FF0EA5E9", "#FF0369A1"), null, new JArray
            {
                Hook(2, new JArray
                {
                    ScadaAct(2, varName: "LineFault", value: "false"),
                    ScadaAct(2, varName: "LineReset", value: "true"),
                    ScadaAct(2, varName: "FaultCode", value: ""),
                }),
            }));
        alarm.Add(NavButton("报警与统计", "返回总览", 60, 720, "产线总览"));

        return ComplexScada(new JArray
        {
            ComplexPage("产线总览", overview),
            ComplexPage("上料与定位", load),
            ComplexPage("装配", assemble),
            ComplexPage("尺寸检测", dimension),
            ComplexPage("表面与颜色", surface),
            ComplexPage("追溯与功能", trace),
            ComplexPage("分拣与下料", sort),
            ComplexPage("报警与统计", alarm),
        }, "产线总览");
    }
}
