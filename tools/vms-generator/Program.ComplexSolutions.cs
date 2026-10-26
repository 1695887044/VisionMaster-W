using System;
using Newtonsoft.Json.Linq;

internal static partial class Program
{
    // ==================================================================
    //  三版复杂工程：柔性装配线 / 视觉分拣线 / 数据网关
    //  复用已验证的流程与图元构造器，SCADA 各自独立。
    // ==================================================================

    static JObject BuildAssemblySolution()
    {
        var flows = new JArray
        {
            BuildHomeFlow(),
            BuildMainFlow(),
            BuildAssemblyStationFlow(),
            BuildSortingStationFlow(),
            BuildEndFlow(),
        };

        return new JObject
        {
            ["SolutionName"] = "柔性装配线",
            ["Version"] = 1.0,
            ["Config"] = new JObject { ["ImageViewMode"] = 0 },
            ["Flows"] = flows,
            ["SolutionId"] = G("complex:assembly").ToString(),
            ["CommunicationConfigs"] = BuildComms(),
            ["IoBuses"] = new JArray(),
            ["IoDevices"] = new JArray(),
            ["CameraConfigs"] = new JArray(),
            ["MotionCards"] = BuildMotionCards(),
            ["CamTables"] = new JArray(),
            ["VariableSnapshots"] = BuildComplexVariables(
                ("AssemblyOk", T_BOOL, "装配工位通过", false),
                ("AssemblyCount", T_INT, "装配计数", 0),
                ("SortOk", T_BOOL, "分拣工位通过", false),
                ("SortCount", T_INT, "分拣计数", 0),
                ("RejectCount", T_INT, "剔除计数", 0)),
            ["VariableGroups"] = new JArray(),
            ["WatchItems"] = new JArray(),
            ["Scada"] = BuildAssemblyScada(),
        };
    }

    static JObject BuildSortingSolution()
    {
        var flows = new JArray
        {
            BuildHomeFlow(),
            BuildVisionSortingMainFlow(),
            BuildRecipeFlow(),
            BuildEndFlow(),
        };

        return new JObject
        {
            ["SolutionName"] = "视觉分拣线",
            ["Version"] = 1.0,
            ["Config"] = new JObject { ["ImageViewMode"] = 0 },
            ["Flows"] = flows,
            ["SolutionId"] = G("complex:sorting").ToString(),
            ["CommunicationConfigs"] = BuildComms(),
            ["IoBuses"] = new JArray(),
            ["IoDevices"] = new JArray(),
            ["CameraConfigs"] = new JArray(),
            ["MotionCards"] = BuildMotionCards(),
            ["CamTables"] = new JArray(),
            ["VariableSnapshots"] = BuildComplexVariables(
                ("SortOk", T_BOOL, "分拣结果", false),
                ("SortCount", T_INT, "分拣总数", 0),
                ("RejectCount", T_INT, "剔除数", 0),
                ("ProductType", T_STR, "当前产品类型", "")),
            ["VariableGroups"] = new JArray(),
            ["WatchItems"] = new JArray(),
            ["Scada"] = BuildSortingScada(),
        };
    }

    static JObject BuildGatewaySolution()
    {
        var flows = new JArray
        {
            BuildHomeFlow(),
            BuildGatewayMainFlow(),
            BuildGatewayDiagFlow(),
            BuildEndFlow(),
        };

        return new JObject
        {
            ["SolutionName"] = "数据网关",
            ["Version"] = 1.0,
            ["Config"] = new JObject { ["ImageViewMode"] = 0 },
            ["Flows"] = flows,
            ["SolutionId"] = G("complex:gateway").ToString(),
            ["CommunicationConfigs"] = BuildComms(),
            ["IoBuses"] = new JArray(),
            ["IoDevices"] = new JArray(),
            ["CameraConfigs"] = new JArray(),
            ["MotionCards"] = BuildMotionCards(),
            ["CamTables"] = new JArray(),
            ["VariableSnapshots"] = BuildComplexVariables(
                ("GatewayOk", T_BOOL, "网关数据有效", false),
                ("PollCount", T_INT, "轮询次数", 0),
                ("AlarmCount", T_INT, "报警次数", 0),
                ("LastError", T_STR, "最近错误", "")),
            ["VariableGroups"] = new JArray(),
            ["WatchItems"] = new JArray(),
            ["Scada"] = BuildGatewayScada(),
        };
    }

    // ==================================================================
    //  变量
    // ==================================================================
    static JArray BuildComplexVariables(params (string Name, string Type, string Desc, object Value)[] extra)
    {
        var list = BuildVariables();
        foreach (var (name, type, desc, value) in extra)
            list.Add(Var(name, type, desc, value));
        return list;
    }

    // ==================================================================
    //  装配线工位
    // ==================================================================
    static JObject BuildAssemblyStationFlow()
    {
        var thenSteps = new JArray
        {
            MotionEnable("ast:en", "X使能", "X", true, 10000),
            MotionMove("ast:toAssemble", "X到装配位", "X", "Absolute", 10, 10),
            Delay("ast:settle1", "装配位稳定", 300),
            MotionIo("ast:clamp", "夹爪夹紧", "ZMotion_Virtual", 0, true, "WriteOutput"),
            Delay("ast:clampWait", "夹紧保持", 300),
            MotionMove("ast:toHome", "X回原点", "X", "Absolute", 0, 10),
            Delay("ast:settle2", "回原点稳定", 300),
            MotionIo("ast:unclamp", "夹爪松开", "ZMotion_Virtual", 0, false, "WriteOutput"),
            CommWrite("ast:result", "写PLC结果", "PLC_Result", new JValue(true), true),
            VarAssign("ast:ok", "写装配通过", "AssemblyOk", null, "Global", new JValue(true)),
        };
        AddIncVar(thenSteps, "ast:count", "装配计数+1", "AssemblyCount");

        var elseSteps = new JArray
        {
            Delay("ast:idle", "无触发延时", 200),
            VarAssign("ast:ng", "写装配未触发", "AssemblyOk", null, "Global", new JValue(false)),
        };

        var steps = new JArray
        {
            CommRead("ast:trigger", "读PLC触发", "PLC_Trigger"),
            Compare("ast:cmp", "触发=1", "ast:trigger", "读PLC触发", "Value", 1, "Equal"),
            IfStep("ast:if", "触发判断", "Ok", G("ast:var:go"), "Ok", "System.Boolean",
                L("ast:cmp", "Result", "触发=1"), thenSteps, elseSteps),
            MotionStop("ast:stop", "X停止", "X", "Decelerate", false),
            CcgCsvRecord("ast:csv", "装配记录", DefaultCsvColumns()),
        };

        return Flow("Assemble", "装配工位：触发→夹紧→回原点→写PLC→计数", 0, 96, 0, steps);
    }

    static JObject BuildSortingStationFlow()
    {
        var thenSteps = new JArray
        {
            MotionEnable("sst:en", "X使能", "X", true, 10000),
            MotionMove("sst:okPos", "X到良品位", "X", "Absolute", 10, 10),
            Delay("sst:okDelay", "良品位稳定", 300),
            MotionIo("sst:okIo", "良品输出", "ZMotion_Virtual", 0, true, "WriteOutput"),
            VarAssign("sst:ok", "写分拣通过", "SortOk", null, "Global", new JValue(true)),
        };
        AddIncVar(thenSteps, "sst:count", "分拣计数+1", "SortCount");

        var elseSteps = new JArray
        {
            MotionEnable("sst:en2", "X使能", "X", true, 10000),
            MotionMove("sst:ngPos", "X到NG位", "X", "Absolute", -5, 10),
            Delay("sst:ngDelay", "NG位稳定", 300),
            MotionIo("sst:ngIo", "NG输出", "ZMotion_Virtual", 0, true, "WriteOutput"),
            VarAssign("sst:ng", "写分拣NG", "SortOk", null, "Global", new JValue(false)),
        };
        AddIncVar(elseSteps, "sst:reject", "剔除计数+1", "RejectCount");

        var steps = new JArray
        {
            LogicVars("sst:result", "读当前结果", "CurrentResult", "CurrentResult"),
            IfStep("sst:if", "结果分拣", "Ok", G("sst:var:ok"), "Ok", "System.Boolean",
                L("sst:result", "Result", "读当前结果"), thenSteps, elseSteps),
            MotionStop("sst:stop", "X停止", "X", "Decelerate", false),
            CcgCsvRecord("sst:csv", "分拣记录", DefaultCsvColumns()),
        };

        return Flow("Sorting", "分拣工位：按 CurrentResult 分良品/NG", 0, 96, 0, steps);
    }

    // ==================================================================
    //  视觉分拣主流程
    // ==================================================================
    static JObject BuildVisionSortingMainFlow()
    {
        var thenSteps = new JArray
        {
            MotionEnable("vsm:en", "X使能", "X", true, 10000),
            MotionMove("vsm:okPos", "X到A位", "X", "Absolute", 10, 10),
            Delay("vsm:okDelay", "A位稳定", 300),
            MotionIo("vsm:okIo", "A位输出", "ZMotion_Virtual", 0, true, "WriteOutput"),
            VarAssign("vsm:type", "产品类型A", "ProductType", null, "Global", new JValue("A")),
            VarAssign("vsm:ok", "分拣通过", "SortOk", null, "Global", new JValue(true)),
        };
        AddIncVar(thenSteps, "vsm:count", "分拣计数+1", "SortCount");

        var elseSteps = new JArray
        {
            MotionEnable("vsm:en2", "X使能", "X", true, 10000),
            MotionMove("vsm:ngPos", "X到NG位", "X", "Absolute", -5, 10),
            Delay("vsm:ngDelay", "NG位稳定", 300),
            MotionIo("vsm:ngIo", "NG输出", "ZMotion_Virtual", 0, true, "WriteOutput"),
            VarAssign("vsm:typeNg", "产品类型NG", "ProductType", null, "Global", new JValue("NG")),
            VarAssign("vsm:ng", "分拣NG", "SortOk", null, "Global", new JValue(false)),
        };
        AddIncVar(elseSteps, "vsm:reject", "剔除计数+1", "RejectCount");

        var steps = new JArray
        {
            ImgFile("vsm:acq", "采集板卡图", IMG_BOARD_01, 1),
            PreProcess("vsm:pre", "图像预处理", new JArray
            {
                Op("gauss", ("Size", "5")),
                Op("gray_closing", ("Width", "5"), ("Height", "5")),
                Op("emphasize", ("Width", "7"), ("Height", "7"), ("Factor", "1.2")),
            }, 0, "vsm:acq", "采集板卡图"),
            CreateRoi("vsm:roi", "ROI圆区域", new JArray { Roi("圆检测区", "Rectangle", 380, 560, 0, 80, 80) }, 0, "vsm:pre", "图像预处理"),
            GeometryFind("vsm:find", "找圆", "Circle", "vsm:roi", "ROI圆区域", 1, 20, 40, 30, false, 0, "vsm:pre", "图像预处理"),
            PoseFollow("vsm:pose", "跟随ROI", "vsm:roi", "ROI圆区域", "vsm:find", "找圆", 0),
            BlobDetect("vsm:blob", "表面缺陷检测", "vsm:pose", "跟随ROI", 0.05, 2, "vsm:pre", "图像预处理"),
            ImgFile("vsm:ocrAcq", "采集批号图", IMG_LOT, 0),
            Ocr("vsm:ocr", "OCR识别", new double[] { 272, 320, 0, 170, 40 }, 1, "vsm:ocrAcq", "采集批号图", true),
            ImgFile("vsm:qrAcq", "采集二维码图", IMG_QR, 0),
            Action("vsm:code", "二维码读取", "图像处理", P_CODE, "二维码读取", "\uf029", new JObject
            {
                ["Symbology"] = "QR Code",
                ["TimeoutMs"] = 500,
                ["Polarity"] = "any",
            }, new JObject { ["Image"] = L("vsm:qrAcq", "Image", "采集二维码图") }),
            Compare("vsm:cmpBlob", "缺陷数<=10", "vsm:blob", "表面缺陷检测", "DefectCount", 10, "LessThanOrEqual"),
            Compare("vsm:cmpOcr", "OCR有文本", "vsm:ocr", "OCR识别", "Count", 0, "GreaterThan"),
            Compare("vsm:cmpCode", "码有内容", "vsm:code", "二维码读取", "Count", 0, "GreaterThan"),
            Logic("vsm:and1", "视觉与1", "vsm:cmpBlob", "缺陷数<=10", "vsm:cmpOcr", "OCR有文本"),
            Logic("vsm:and2", "视觉综合", "vsm:and1", "视觉与1", "vsm:cmpCode", "码有内容"),
            IfStep("vsm:if", "分拣判断", "Ok", G("vsm:var:ok"), "Ok", "System.Boolean",
                L("vsm:and2", "Result", "视觉综合"), thenSteps, elseSteps),
            MotionStop("vsm:stop", "X停止", "X", "Decelerate", false),
            ResultDisplay("vsm:display", "分拣结果显示", new[]
            {
                ("r1", "缺陷数", "vsm:blob", "表面缺陷检测", "DefectCount"),
                ("r2", "OCR", "vsm:ocr", "OCR识别", "Text"),
                ("r3", "码数", "vsm:code", "二维码读取", "Count"),
                ("r4", "结果", "vsm:and2", "视觉综合", "Result"),
            }, 1, "vsm:acq", "采集板卡图"),
            CcgCsvRecord("vsm:csv", "分拣记录", DefaultCsvColumns()),
            Delay("vsm:delay", "分拣节拍", 300),
        };

        return Flow("Main", "视觉分拣主流程：采集/检测/分拣/记录", 2, 96, 0, steps);
    }

    static JObject BuildRecipeFlow()
    {
        var steps = new JArray
        {
            VarAssign("rc:name", "写配方名", "RecipeName", null, "Global", new JValue("Product_A")),
            VarAssign("rc:ok", "清零良品", "OKCount", null, "Global", new JValue(0)),
            VarAssign("rc:ng", "清零不良", "NGCount", null, "Global", new JValue(0)),
            VarAssign("rc:yield", "良率复位", "YieldRate", null, "Global", new JValue(100.0)),
            VarAssign("rc:type", "产品类型复位", "ProductType", null, "Global", new JValue("A")),
            Delay("rc:delay", "配方应用", 100),
        };
        return Flow("Recipe", "配方切换：产品 A / 清零计数 / 良率复位", 0, 96, 0, steps);
    }

    // ==================================================================
    //  数据网关主流程
    // ==================================================================
    static JObject BuildGatewayMainFlow()
    {
        var alarmThen = new JArray
        {
            VarAssign("gw:err", "写仪表超限", "LastError", null, "Global", new JValue("仪表读数超过 80")),
        };
        AddIncVar(alarmThen, "gw:alarm", "报警计数+1", "AlarmCount");

        var alarmElse = new JArray
        {
            VarAssign("gw:clear", "清空错误", "LastError", null, "Global", new JValue("")),
        };

        var steps = new JArray
        {
            CommRead("gw:trigger", "读PLC触发", "PLC_Trigger"),
            CommRead("gw:ack", "读PLC确认", "PLC_Ack"),
            CommRead("gw:meter", "读仪表", "Meter_Value"),
            CommRead("gw:s7hb", "读S7心跳", "S7_Heartbeat"),
            CommRead("gw:s7result", "读S7结果", "S7_Result"),
            Compare("gw:cmpMeter", "仪表<=80", "gw:meter", "读仪表", "Value", 80, "LessThanOrEqual"),
            Compare("gw:cmpS7", "S7结果>=0", "gw:s7result", "读S7结果", "Value", 0, "GreaterThanOrEqual"),
            Logic("gw:and", "数据有效", "gw:cmpMeter", "仪表<=80", "gw:cmpS7", "S7结果>=0"),
            VarAssign("gw:ok", "写网关结果", "GatewayOk", L("gw:and", "Result", "数据有效"), "Global", null),
            IfStep("gw:alarmIf", "仪表报警", "!Ok", G("gw:var:meterOk"), "Ok", "System.Boolean",
                L("gw:cmpMeter", "Result", "仪表<=80"), alarmThen, alarmElse),
            CommWrite("gw:result", "写PLC结果", "PLC_Result", new JValue(true), true),
            CommWrite("gw:heartbeat", "写S7心跳", "S7_Heartbeat", new JValue(true), true),
            CcgCsvRecord("gw:csv", "网关记录", DefaultCsvColumns()),
            CcgExcel("gw:excel", "网关报表", "CSV记录"),
            Upload("gw:upload", "上报MES(默认不触发)"),
            Delay("gw:delay", "轮询节拍", 500),
        };

        return Flow("Main", "数据网关：三协议轮询/阈值报警/记录/上报", 2, 96, 0, steps);
    }

    static JObject BuildGatewayDiagFlow()
    {
        var steps = new JArray
        {
            CommWrite("gd:trigger", "写PLC触发1", "PLC_Trigger", new JValue(1), true),
            CommWrite("gd:s7", "写S7结果7", "S7_Result", new JValue(7), true),
            CommRead("gd:triggerBack", "回读触发", "PLC_Trigger"),
            CommRead("gd:s7Back", "回读S7结果", "S7_Result"),
            Compare("gd:cmpTrigger", "触发回读=1", "gd:triggerBack", "回读触发", "Value", 1, "Equal"),
            Compare("gd:cmpS7", "S7回读=7", "gd:s7Back", "回读S7结果", "Value", 7, "Equal"),
            Logic("gd:and", "诊断通过", "gd:cmpTrigger", "触发回读=1", "gd:cmpS7", "S7回读=7"),
            VarAssign("gd:ok", "写诊断结果", "GatewayOk", L("gd:and", "Result", "诊断通过"), "Global", null),
            Delay("gd:delay", "诊断节拍", 100),
        };
        return Flow("Diag", "通讯诊断：Modbus/S7 写读回比对", 0, 96, 0, steps);
    }

    // ==================================================================
    //  SCADA：装配线
    // ==================================================================
    static JObject BuildAssemblyScada()
    {
        var overview = new JArray();
        ComplexHeader(overview, "装配总览", "柔性装配线", "装配 / 分拣 / 报警 一屏");
        overview.Add(El("装配总览", "KPI", "Hmi.KpiBoard", 60, 180, 900, 200, 10, new JObject
        {
            ["Columns"] = "4",
            ["Label1"] = "装配数", ["Value1"] = "0", ["Unit1"] = "pcs",
            ["Label2"] = "分拣数", ["Value2"] = "0", ["Unit2"] = "pcs",
            ["Label3"] = "剔除数", ["Value3"] = "0", ["Unit3"] = "pcs",
            ["Label4"] = "良率", ["Value4"] = "0", ["Unit4"] = "%",
        }, new JArray
        {
            Bind("Value1", "AssemblyCount"), Bind("Value2", "SortCount"),
            Bind("Value3", "RejectCount"), Bind("Value4", "YieldRate"),
        }));
        overview.Add(Indicator("装配总览", "装配", 80, 430, "AssemblyOk", "装配"));
        overview.Add(Indicator("装配总览", "分拣", 160, 430, "SortOk", "分拣"));
        overview.Add(Indicator("装配总览", "就绪", 240, 430, "SystemReady", "就绪"));
        overview.Add(TestButton("装配总览", "运行装配", 80, 560, "Assemble"));
        overview.Add(TestButton("装配总览", "运行分拣", 260, 560, "Sorting"));
        overview.Add(NavButton("装配总览", "装配工位", 80, 630, "装配工位"));
        overview.Add(NavButton("装配总览", "分拣工位", 240, 630, "分拣工位"));
        overview.Add(NavButton("装配总览", "报警中心", 400, 630, "报警中心"));

        var assembly = new JArray();
        ComplexHeader(assembly, "装配工位", "装配工位", "X 轴 / 夹爪 / PLC 握手");
        assembly.Add(IOField("装配工位", "X位置", 80, 220, "System.Motion.X.Position", "X位置", "F3"));
        assembly.Add(Indicator("装配工位", "X使能", 340, 220, "System.Motion.X.Enabled", "X使能"));
        assembly.Add(MotionButton("装配工位", "X使能", 80, 320, 7, "X"));
        assembly.Add(MotionButton("装配工位", "X到装配位", 220, 320, 9, "X", 10, 10));
        assembly.Add(MotionButton("装配工位", "X停止", 390, 320, 12, "X"));
        assembly.Add(WriteButton("装配工位", "写PLC结果", 80, 400, "PLC_Result", "true"));
        assembly.Add(NavButton("装配工位", "返回总览", 80, 480, "装配总览"));

        var sorting = new JArray();
        ComplexHeader(sorting, "分拣工位", "分拣工位", "按当前结果分良品/NG");
        sorting.Add(IOField("分拣工位", "良品数", 80, 220, "OKCount", "良品数", "0"));
        sorting.Add(IOField("分拣工位", "不良数", 320, 220, "NGCount", "不良数", "0"));
        sorting.Add(IOField("分拣工位", "剔除数", 560, 220, "RejectCount", "剔除数", "0"));
        sorting.Add(Indicator("分拣工位", "分拣", 80, 320, "SortOk", "分拣"));
        sorting.Add(MotionButton("分拣工位", "X到良品位", 220, 400, 9, "X", 10, 10));
        sorting.Add(MotionButton("分拣工位", "X到NG位", 390, 400, 9, "X", -5, 10));
        sorting.Add(NavButton("分拣工位", "返回总览", 80, 480, "装配总览"));

        var alarm = new JArray();
        ComplexHeader(alarm, "报警中心", "报警中心", "实时报警 / 一键确认");
        alarm.Add(El("报警中心", "报警列表", "Hmi.AlarmBanner", 60, 180, 1200, 420, 10, new JObject
        {
            ["MaxRows"] = "10", ["EmptyText"] = "当前无报警",
            ["NormalColor"] = "#FF16A34A", ["InfoColor"] = "#FF0EA5E9",
            ["WarningColor"] = "#FFF59E0B", ["CriticalColor"] = "#FFEF4444",
        }));
        alarm.Add(El("报警中心", "确认全部", "Hmi.AckButton", 60, 630, 180, 40, 10, new JObject
        {
            ["Scope"] = "All", ["Text"] = "确认全部报警", ["Fill"] = "#FFEF4444",
            ["Stroke"] = "#FFB91C1C", ["StrokeThickness"] = "1", ["CornerRadius"] = "6",
            ["Foreground"] = "#FFFFFFFF", ["FontSize"] = "14", ["FontWeight"] = "SemiBold",
        }));
        alarm.Add(NavButton("报警中心", "返回总览", 280, 630, "装配总览"));

        return ComplexScada(new JArray
        {
            ComplexPage("装配总览", overview),
            ComplexPage("装配工位", assembly),
            ComplexPage("分拣工位", sorting),
            ComplexPage("报警中心", alarm),
        }, "装配总览");
    }

    // ==================================================================
    //  SCADA：视觉分拣
    // ==================================================================
    static JObject BuildSortingScada()
    {
        var overview = new JArray();
        ComplexHeader(overview, "分拣总览", "视觉分拣线", "视觉判定 / 分拣 / 配方");
        overview.Add(El("分拣总览", "KPI", "Hmi.KpiBoard", 60, 180, 900, 200, 10, new JObject
        {
            ["Columns"] = "4",
            ["Label1"] = "分拣数", ["Value1"] = "0", ["Unit1"] = "pcs",
            ["Label2"] = "剔除数", ["Value2"] = "0", ["Unit2"] = "pcs",
            ["Label3"] = "良率", ["Value3"] = "0", ["Unit3"] = "%",
            ["Label4"] = "良品数", ["Value4"] = "0", ["Unit4"] = "pcs",
        }, new JArray
        {
            Bind("Value1", "SortCount"), Bind("Value2", "RejectCount"),
            Bind("Value3", "YieldRate"), Bind("Value4", "OKCount"),
        }));
        overview.Add(Indicator("分拣总览", "分拣结果", 80, 430, "SortOk", "结果"));
        overview.Add(Indicator("分拣总览", "就绪", 160, 430, "SystemReady", "就绪"));
        overview.Add(TextField("分拣总览", "产品类型", 80, 520, 300, "ProductType", "产品类型"));
        overview.Add(TestButton("分拣总览", "运行分拣", 80, 610, "Main"));
        overview.Add(TestButton("分拣总览", "应用配方", 260, 610, "Recipe"));
        overview.Add(NavButton("分拣总览", "分拣工位", 80, 690, "分拣工位"));
        overview.Add(NavButton("分拣总览", "配方页", 240, 690, "配方"));
        overview.Add(NavButton("分拣总览", "报警中心", 400, 690, "报警中心"));

        var station = new JArray();
        ComplexHeader(station, "分拣工位", "分拣工位", "X 轴 / IO / 结果");
        station.Add(IOField("分拣工位", "良品数", 80, 220, "OKCount", "良品数", "0"));
        station.Add(IOField("分拣工位", "不良数", 320, 220, "NGCount", "不良数", "0"));
        station.Add(IOField("分拣工位", "X位置", 80, 300, "System.Motion.X.Position", "X位置", "F3"));
        station.Add(Indicator("分拣工位", "分拣", 80, 380, "SortOk", "分拣"));
        station.Add(MotionButton("分拣工位", "X到A位", 220, 460, 9, "X", 10, 10));
        station.Add(MotionButton("分拣工位", "X到NG位", 390, 460, 9, "X", -5, 10));
        station.Add(NavButton("分拣工位", "返回总览", 80, 540, "分拣总览"));

        var recipe = new JArray();
        ComplexHeader(recipe, "配方", "配方管理", "产品 A / 计数清零");
        recipe.Add(TextField("配方", "配方名", 80, 220, 300, "RecipeName", "配方名"));
        recipe.Add(TextField("配方", "产品类型", 80, 280, 300, "ProductType", "产品类型"));
        recipe.Add(WriteButton("配方", "写入产品A", 80, 360, "ProductType", "A"));
        recipe.Add(NavButton("配方", "返回总览", 80, 440, "分拣总览"));

        var alarm = new JArray();
        ComplexHeader(alarm, "报警中心", "报警中心", "实时报警 / 一键确认");
        alarm.Add(El("报警中心", "报警列表", "Hmi.AlarmBanner", 60, 180, 1200, 420, 10, new JObject
        {
            ["MaxRows"] = "10", ["EmptyText"] = "当前无报警",
            ["NormalColor"] = "#FF16A34A", ["InfoColor"] = "#FF0EA5E9",
            ["WarningColor"] = "#FFF59E0B", ["CriticalColor"] = "#FFEF4444",
        }));
        alarm.Add(NavButton("报警中心", "返回总览", 80, 630, "分拣总览"));

        return ComplexScada(new JArray
        {
            ComplexPage("分拣总览", overview),
            ComplexPage("分拣工位", station),
            ComplexPage("配方", recipe),
            ComplexPage("报警中心", alarm),
        }, "分拣总览");
    }

    // ==================================================================
    //  SCADA：数据网关
    // ==================================================================
    static JObject BuildGatewayScada()
    {
        var overview = new JArray();
        ComplexHeader(overview, "网关总览", "数据网关", "Modbus / S7 / 数据管道");
        overview.Add(El("网关总览", "KPI", "Hmi.KpiBoard", 60, 180, 900, 200, 10, new JObject
        {
            ["Columns"] = "4",
            ["Label1"] = "轮询次数", ["Value1"] = "0", ["Unit1"] = "次",
            ["Label2"] = "报警次数", ["Value2"] = "0", ["Unit2"] = "次",
            ["Label3"] = "仪表值", ["Value3"] = "0", ["Unit3"] = "",
            ["Label4"] = "S7结果", ["Value4"] = "0", ["Unit4"] = "",
        }, new JArray
        {
            Bind("Value1", "PollCount"), Bind("Value2", "AlarmCount"),
            Bind("Value3", "Meter_Value", "F2"), Bind("Value4", "S7_Result"),
        }));
        overview.Add(Indicator("网关总览", "网关", 80, 430, "GatewayOk", "网关"));
        overview.Add(Indicator("网关总览", "通讯", 160, 430, "CommOk", "通讯"));
        overview.Add(Indicator("网关总览", "就绪", 240, 430, "SystemReady", "就绪"));
        overview.Add(TextField("网关总览", "最近错误", 80, 520, 500, "LastError", "最近错误"));
        overview.Add(TestButton("网关总览", "运行网关", 80, 610, "Main"));
        overview.Add(TestButton("网关总览", "通讯诊断", 260, 610, "Diag"));
        overview.Add(NavButton("网关总览", "诊断页", 80, 690, "通讯诊断"));
        overview.Add(NavButton("网关总览", "报警中心", 240, 690, "报警中心"));

        var diag = new JArray();
        ComplexHeader(diag, "通讯诊断", "通讯诊断", "写读回比对");
        diag.Add(IOField("通讯诊断", "PLC触发", 80, 220, "PLC_Trigger", "PLC触发", "0"));
        diag.Add(IOField("通讯诊断", "S7结果", 320, 220, "S7_Result", "S7结果", "0"));
        diag.Add(IOField("通讯诊断", "仪表", 80, 300, "Meter_Value", "仪表", "F2"));
        diag.Add(Indicator("通讯诊断", "S7心跳", 80, 380, "S7_Heartbeat", "S7心跳"));
        diag.Add(WriteButton("通讯诊断", "写触发1", 80, 460, "PLC_Trigger", "1"));
        diag.Add(WriteButton("通讯诊断", "写S7结果7", 240, 460, "S7_Result", "7"));
        diag.Add(NavButton("通讯诊断", "返回总览", 80, 540, "网关总览"));

        var alarm = new JArray();
        ComplexHeader(alarm, "报警中心", "报警中心", "阈值 / 断线 / 一键确认");
        alarm.Add(El("报警中心", "报警列表", "Hmi.AlarmBanner", 60, 180, 1200, 420, 10, new JObject
        {
            ["MaxRows"] = "10", ["EmptyText"] = "当前无报警",
            ["NormalColor"] = "#FF16A34A", ["InfoColor"] = "#FF0EA5E9",
            ["WarningColor"] = "#FFF59E0B", ["CriticalColor"] = "#FFEF4444",
        }));
        alarm.Add(El("报警中心", "确认全部", "Hmi.AckButton", 60, 630, 180, 40, 10, new JObject
        {
            ["Scope"] = "All", ["Text"] = "确认全部报警", ["Fill"] = "#FFEF4444",
            ["Stroke"] = "#FFB91C1C", ["StrokeThickness"] = "1", ["CornerRadius"] = "6",
            ["Foreground"] = "#FFFFFFFF", ["FontSize"] = "14", ["FontWeight"] = "SemiBold",
        }));
        alarm.Add(NavButton("报警中心", "返回总览", 280, 630, "网关总览"));

        return ComplexScada(new JArray
        {
            ComplexPage("网关总览", overview),
            ComplexPage("通讯诊断", diag),
            ComplexPage("报警中心", alarm),
        }, "网关总览");
    }

    // ==================================================================
    //  公共 SCADA 辅助
    // ==================================================================
    static JObject ComplexScada(JArray pages, string startupPage)
        => new()
        {
            ["SchemaVersion"] = 1,
            ["Pages"] = pages,
            ["Alarms"] = BuildScadaAlarms(),
            ["VariableEvents"] = new JArray(),
            ["StartupPageId"] = G("page:" + startupPage).ToString(),
            ["BasePages"] = new JArray(),
        };

    static JObject ComplexPage(string name, JArray elements) => ScadaPageObj(name, 1920, 1080, elements);

    static void ComplexHeader(JArray els, string page, string title, string subtitle)
    {
        els.Add(El(page, "标题栏", "Hmi.Rectangle", 0, 0, 1920, 80, 1, new JObject
        {
            ["Fill"] = "#FF111827", ["Stroke"] = "#FF1F2937", ["StrokeThickness"] = "0", ["CornerRadius"] = "0",
        }));
        els.Add(El(page, "标题", "Hmi.Text", 40, 14, 900, 48, 5, TextProps(title, 28, "#FFF8FAFC", "Bold")));
        els.Add(El(page, "副标题", "Hmi.Text", 40, 84, 1200, 24, 5, TextProps(subtitle, 13, "#FF94A3B8")));
        els.Add(El(page, "时钟", "Hmi.Clock", 1400, 16, 300, 44, 5, new JObject
        {
            ["Text"] = "", ["Foreground"] = "#FFE2E8F0", ["FontSize"] = "22",
            ["FontWeight"] = "SemiBold", ["TextAlignment"] = "Right",
        }));
        els.Add(El(page, "报警条", "Hmi.AlarmBanner", 0, 112, 1920, 48, 6, new JObject
        {
            ["MaxRows"] = "3", ["EmptyText"] = "无报警",
            ["NormalColor"] = "#FF16A34A", ["InfoColor"] = "#FF0EA5E9",
            ["WarningColor"] = "#FFF59E0B", ["CriticalColor"] = "#FFEF4444",
        }));
    }
}
