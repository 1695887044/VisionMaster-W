using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

internal static partial class Program
{
    // ==================================================================
    //  自动化产线：9 工位 + 返修 + 配方 + Main 调度
    // ==================================================================

    static JObject BuildAutomationLineSolution()
    {
        var flows = new JArray
        {
            BuildHomeFlow(),
            BuildAutomationMainFlow(),
            BuildSt1LoadFlow(),
            BuildSt2AlignFlow(),
            BuildSt3AssembleFlow(),
            BuildSt4DimensionFlow(),
            BuildSt5SurfaceFlow(),
            BuildSt6TraceFlow(),
            BuildSt7FunctionFlow(),
            BuildSt8SortFlow(),
            BuildSt9UnloadFlow(),
            BuildSt10ReworkFlow(),
            BuildAutomationRecipeFlow(),
            BuildEndFlow(),
        };

        return new JObject
        {
            ["SolutionName"] = "自动化产线",
            ["Version"] = 1.0,
            ["Config"] = new JObject { ["ImageViewMode"] = 0 },
            ["Flows"] = flows,
            ["SolutionId"] = G("auto:solution").ToString(),
            ["CommunicationConfigs"] = BuildComms(),
            ["IoBuses"] = new JArray(),
            ["IoDevices"] = new JArray(),
            ["CameraConfigs"] = new JArray(),
            ["MotionCards"] = BuildMotionCards(),
            ["CamTables"] = new JArray(),
            ["VariableSnapshots"] = BuildAutomationVariables(),
            ["VariableGroups"] = new JArray(),
            ["WatchItems"] = new JArray(),
            ["Scada"] = BuildAutomationScada(),
        };
    }

    static JArray BuildAutomationVariables()
    {
        var list = BuildVariables();
        list.Add(Var("LineFault", T_BOOL, "线体故障", false));
        list.Add(Var("LineReset", T_BOOL, "线体复位", false));
        list.Add(Var("CycleCount", T_INT, "节拍计数", 0));
        list.Add(Var("ProductTypeCode", T_INT, "产品类型 1/2/3", 1));
        list.Add(Var("CarrierId", T_STR, "载具条码", ""));
        list.Add(Var("St1_Ok", T_BOOL, "上料工位 OK", false));
        list.Add(Var("St2_Ok", T_BOOL, "定位工位 OK", false));
        list.Add(Var("St3_Ok", T_BOOL, "装配工位 OK", false));
        list.Add(Var("St4_Ok", T_BOOL, "尺寸工位 OK", false));
        list.Add(Var("St5_Ok", T_BOOL, "表面工位 OK", false));
        list.Add(Var("St6_Ok", T_BOOL, "追溯工位 OK", false));
        list.Add(Var("St7_Ok", T_BOOL, "功能工位 OK", false));
        list.Add(Var("St8_Ok", T_BOOL, "分拣工位 OK", false));
        list.Add(Var("St9_Ok", T_BOOL, "下料工位 OK", false));
        list.Add(Var("ProductOk", T_BOOL, "产品综合 OK", false));
        list.Add(Var("FinalOk", T_BOOL, "最终判定", false));
        list.Add(Var("FaultCode", T_STR, "故障码", ""));
        list.Add(Var("St4_Message", T_STR, "尺寸工位说明", ""));
        list.Add(Var("SortBin", T_STR, "分拣仓位", ""));
        list.Add(Var("ReworkCount", T_INT, "返修次数", 0));
        list.Add(Var("ReworkDone", T_BOOL, "返修完成", false));
        list.Add(Var("Torque", T_DBL, "装配扭矩", 0.0));
        list.Add(Var("DimValues", T_STR, "尺寸摘要", ""));
        list.Add(Var("ColorSeqOk", T_BOOL, "颜色线序 OK", false));
        list.Add(Var("ColorRegionOk", T_BOOL, "区域颜色 OK", false));
        list.Add(Var("BeadErrors", T_INT, "胶路错误段", 0));
        list.Add(Var("FunctionValue", T_DBL, "功能测试值", 0.0));
        list.Add(Var("LineCycleMs", T_INT, "本节拍耗时", 0));
        list.Add(NetVar("PLC_Alarm", T_BOOL, "PLC 线体报警（线圈）", false, "PLC_Modbus", "ModbusTcp", "Coils", "3", 0, 1, 0, "Boolean"));
        list.Add(NetVar("PLC_CycleCount", T_INT, "PLC 节拍计数", 0, "PLC_Modbus", "ModbusTcp", "HoldingRegisters", "6", -1, 1, 0, "Int32"));
        return list;
    }

    // ==================================================================
    //  Main：整线调度
    // ==================================================================
    static JObject BuildAutomationMainFlow()
    {
        var cycle = new JArray
        {
            VarAssign("am:fault0", "清故障", "LineFault", null, "Global", new JValue(false)),
            VarAssign("am:reset", "清复位", "LineReset", null, "Global", new JValue(false)),
        };
        AddIncVar(cycle, "am:cycle", "节拍+1", "CycleCount");

        // 上料/定位/装配
        AddRange(cycle, CallStation("am:st1", "St1_Load", 30000, "St1 上料设备故障"));
        AddRange(cycle, CallStation("am:st2", "St2_Align", 30000, "St2 定位设备故障"));
        AddRange(cycle, CallStation("am:st3", "St3_Assemble", 30000, "St3 装配设备故障"));

        // 检测 + 返修：最多 2 轮
        var forBody = new JArray();
        AddRange(forBody, CallStation("am:st4", "St4_Dimension", 60000, "St4 尺寸设备故障"));
        AddRange(forBody, CallStation("am:st5", "St5_Surface", 60000, "St5 表面设备故障"));
        AddRange(forBody, CallStation("am:st6", "St6_Trace", 60000, "St6 追溯设备故障"));
        AddRange(forBody, CallStation("am:st7", "St7_Function", 30000, "St7 功能设备故障"));
        forBody.Add(ImgFile("am:displayImg", "产线显示图", DIR_BOARD + @"\board-01.png", 0));
        forBody.Add(ResultDisplay("am:display", "产线结果显示", new[]
        {
            ("r1", "尺寸", "am:st4run", "调用St4_Dimension", "Invoked"),
            ("r2", "表面", "am:st5run", "调用St5_Surface", "Invoked"),
            ("r3", "追溯", "am:st6run", "调用St6_Trace", "Invoked"),
            ("r4", "功能", "am:st7run", "调用St7_Function", "Invoked"),
        }, 0, "am:displayImg", "产线显示图"));

        forBody.Add(LogicVars("am:and1", "检测与1", "St4_Ok", "St5_Ok"));
        forBody.Add(LogicVars("am:and2", "检测与2", "St6_Ok", "St7_Ok"));
        forBody.Add(Logic("am:and3", "产品综合", "am:and1", "检测与1", "am:and2", "检测与2"));
        forBody.Add(VarAssign("am:productOk", "写产品综合", "ProductOk", L("am:and3", "Result", "产品综合"), "Global", null));
        forBody.Add(IfStep("am:okBreak", "检测通过则退出返修", "Ok", G("am:var:ok"), "Ok", "System.Boolean",
            L("am:and3", "Result", "产品综合"),
            new JArray { BreakStep("am:break", "跳出返修") },
            new JArray()));
        forBody.Add(IfStep("am:rework", "返修判断", "Ok", G("am:var:rework"), "Ok", "System.Boolean",
            L("am:and3", "Result", "产品综合"),
            new JArray(),
            new JArray
            {
                VarAssign("am:reworkCode", "写返修故障码", "FaultCode", null, "Global", new JValue("产品 NG，进入返修")),
            }));
        AddIncVar(forBody, "am:reworkInc", "返修次数+1", "ReworkCount");
        AddRange(forBody, CallStation("am:st10", "St10_Rework", 20000, "St10 返修设备故障"));

        cycle.Add(ForLoop("am:for", "检测/返修循环", 2, forBody, "am:for"));

        // 最终判定 = 最后一轮的 ProductOk
        cycle.Add(VarAssign("am:final", "写最终判定", "FinalOk", LG("ProductOk"), "Global", null));
        AddRange(cycle, CallStation("am:st8", "St8_Sort", 30000, "St8 分拣设备故障"));
        AddRange(cycle, CallStation("am:st9", "St9_Unload", 30000, "St9 下料设备故障"));

        // PLC 结果 / 计数 / 记录
        cycle.Add(CommWrite("am:result", "写PLC结果", "PLC_Result", LG("FinalOk"), true));
        cycle.Add(CommWrite("am:ack", "写PLC确认", "PLC_Ack", new JValue(1), true));
        cycle.Add(CommWrite("am:alarm", "写PLC报警", "PLC_Alarm", LG("LineFault"), true));
        cycle.Add(CommWrite("am:cycleCount", "写PLC节拍", "PLC_CycleCount", LG("CycleCount"), true));
        var total = Action("am:total", "总计数", "数据处理", P_MATH, "数学计算", "\uf1ec",
            new JObject { ["Operation"] = "Add" },
            new JObject { ["A"] = LG("OKCount"), ["B"] = LG("NGCount") });
        var ratio = Action("am:ratio", "良率比例", "数据处理", P_MATH, "数学计算", "\uf1ec",
            new JObject { ["Operation"] = "Divide" },
            new JObject { ["A"] = LG("OKCount"), ["B"] = LStep(total, "Result", "总计数") });
        var percent = Action("am:percent", "良率百分比", "数据处理", P_MATH, "数学计算", "\uf1ec",
            new JObject { ["Operation"] = "Multiply", ["B"] = 100.0 },
            new JObject { ["A"] = LStep(ratio, "Result", "良率比例") });
        cycle.Add(total);
        cycle.Add(ratio);
        cycle.Add(percent);
        cycle.Add(VarAssign("am:yield", "写良率", "YieldRate", LStep(percent, "Result", "良率百分比"), "Global", null));
        cycle.Add(CsvRecord("am:csv", "产线节拍记录"));
        cycle.Add(Delay("am:cycleDelay", "节拍延时", 500));

        var triggerIf = IfStep("am:trigger", "触发判断", "Trigger >= 1", G("am:var:trigger"), "Trigger", "System.Int32",
            L("am:readTrigger", "Value", "读PLC触发"),
            cycle,
            new JArray { Delay("am:idle", "空闲延时", 200) });

        var faultLogic = LogicVars("am:faultLogic", "故障状态", "LineFault", "LineFault");
        var steps = new JArray
        {
            CommRead("am:readTrigger", "读PLC触发", "PLC_Trigger"),
            faultLogic,
            IfStep("am:fault", "故障挂起", "Ok", G("am:var:fault"), "Ok", "System.Boolean",
                LStep(faultLogic, "Result", "故障状态"),
                new JArray { Delay("am:faultDelay", "故障等待", 500), ReturnStep("am:faultReturn", "故障返回") },
                new JArray()),
            triggerIf,
        };

        return Flow("Main", "自动化产线：9 工位 + 返修 + 统计", 2, 96, 0, steps);
    }

    /// <summary>调用工位；Invoked=false 视为设备故障：停轴、报警、返回本轮。</summary>
    static JArray CallStation(string seed, string flowName, int timeoutMs, string faultCode)
    {
        var run = RunFlow(seed + "run", "调用" + flowName, flowName, timeoutMs);
        var fault = new JArray
        {
            VarAssign(seed + ":fault", "写故障", "LineFault", null, "Global", new JValue(true)),
            VarAssign(seed + ":code", "写故障码", "FaultCode", null, "Global", new JValue(faultCode)),
            MotionStop(seed + ":sx", "停X", "X", "Decelerate", false),
            MotionStop(seed + ":sy", "停Y", "Y", "Decelerate", false),
            MotionStop(seed + ":sz", "停Z", "Z", "Decelerate", false),
            CommWrite(seed + ":alarm", "写报警", "PLC_Alarm", new JValue(true), true),
            ReturnStep(seed + ":return", "故障返回"),
        };
        var check = IfStep(seed + "if", "设备故障判定", "!Ok", G(seed + "var:ok"), "Ok", "System.Boolean",
            LStep(run, "Invoked", "调用" + flowName), fault, new JArray());
        return new JArray { run, check };
    }

    // ==================================================================
    //  St1 上料 / 载具识别
    // ==================================================================
    static JObject BuildSt1LoadFlow()
    {
        var steps = new JArray
        {
            MotionEnable("s1:enX", "X使能", "X", true, 10000),
            MotionEnable("s1:enY", "Y使能", "Y", true, 10000),
            MotionEnable("s1:enZ", "Z使能", "Z", true, 10000),
            MotionMove("s1:x", "X上料位", "X", "Absolute", 0, 10),
            MotionMove("s1:y", "Y上料位", "Y", "Absolute", 0, 10),
            MotionMove("s1:z", "Z上料位", "Z", "Absolute", 0, 10),
            Delay("s1:settle", "上料稳定", 300),
            MotionIo("s1:clamp", "夹紧", "ZMotion_Virtual", 0, true, "WriteOutput"),
            MotionIo("s1:vac", "真空", "ZMotion_Virtual", 1, true, "WriteOutput"),
            ImgFile("s1:qr", "采集载具码", IMG_QR, 0),
            Action("s1:read", "读载具码", "图像处理", P_CODE, "二维码读取", "\uf029", new JObject
            {
                ["Symbology"] = "QR Code",
                ["TimeoutMs"] = 500,
                ["Polarity"] = "any",
            }, new JObject { ["Image"] = L("s1:qr", "Image", "采集载具码") }),
            VarAssign("s1:carrier", "写载具号", "CarrierId", L("s1:read", "Text", "读载具码"), "Global", null),
            Compare("s1:cmp", "载具码有内容", "s1:read", "读载具码", "Count", 0, "GreaterThan"),
            VarAssign("s1:ok", "写上料OK", "St1_Ok", L("s1:cmp", "Result", "载具码有内容"), "Global", null),
            ResultDisplay("s1:display", "上料结果", new[]
            {
                ("r1", "载具", "s1:read", "读载具码", "Text"),
                ("r2", "数量", "s1:read", "读载具码", "Count"),
            }, 0, "s1:qr", "采集载具码"),
            Delay("s1:delay", "上料节拍", 200),
        };
        return Flow("St1_Load", "工位1：上料/夹紧/载具识别", 0, 96, 0, steps);
    }

    // ==================================================================
    //  St2 视觉定位
    // ==================================================================
    static JObject BuildSt2AlignFlow()
    {
        var steps = new JArray
        {
            ImgFile("s2:acq", "采集板卡图", DIR_BOARD + @"\board-01.png", 0),
            PreProcess("s2:pre", "图像预处理", new JArray
            {
                Op("gauss", ("Size", "5")),
                Op("gray_closing", ("Width", "5"), ("Height", "5")),
                Op("emphasize", ("Width", "7"), ("Height", "7"), ("Factor", "1.2")),
            }, 0, "s2:acq", "采集板卡图"),
            CreateRoi("s2:roi", "定位ROI", new JArray { Roi("定位区", "Rectangle", 380, 560, 0, 80, 80) }, 0, "s2:pre", "图像预处理"),
            GeometryFind("s2:find", "找定位圆", "Circle", "s2:roi", "定位ROI", 1, 20, 40, 30, false, 0, "s2:pre", "图像预处理"),
            PoseFollow("s2:pose", "跟随定位", "s2:roi", "定位ROI", "s2:find", "找定位圆", 0),
            IfStep("s2:if", "定位判定", "Ok", G("s2:var:ok"), "Ok", "System.Boolean",
                L("s2:find", "Success", "找定位圆"),
                new JArray
                {
                    VarAssign("s2:ok", "写定位OK", "St2_Ok", null, "Global", new JValue(true)),
                    MotionMove("s2:x", "X补偿", "X", "Absolute", 5, 10),
                    MotionMove("s2:y", "Y补偿", "Y", "Absolute", 5, 10),
                },
                new JArray
                {
                    VarAssign("s2:ng", "写定位NG", "St2_Ok", null, "Global", new JValue(false)),
                }),
            Delay("s2:delay", "定位节拍", 300),
        };
        return Flow("St2_Align", "工位2：视觉定位/坐标补偿", 0, 96, 0, steps);
    }

    // ==================================================================
    //  St3 装配 / 拧紧
    // ==================================================================
    static JObject BuildSt3AssembleFlow()
    {
        var steps = new JArray
        {
            ImgFile("s3:acq", "采集装配图", DIR_BOARD + @"\board-02.png", 0),
            MotionEnable("s3:enZ", "Z使能", "Z", true, 10000),
            MotionMove("s3:zDown", "Z压装", "Z", "Absolute", 5, 10),
            Delay("s3:press", "压装保持", 300),
            MotionIo("s3:screw", "拧紧输出", "ZMotion_Virtual", 2, true, "WriteOutput"),
            Delay("s3:screwWait", "拧紧保持", 300),
            MotionIo("s3:screwOff", "拧紧关闭", "ZMotion_Virtual", 2, false, "WriteOutput"),
            MotionMove("s3:zUp", "Z回原点", "Z", "Absolute", 0, 10),
            CommRead("s3:torque", "读扭矩", "Meter_Value"),
            VarAssign("s3:torqueVal", "写扭矩", "Torque", L("s3:torque", "Value", "读扭矩"), "Global", null),
            Compare("s3:cmp", "扭矩<=80", "s3:torque", "读扭矩", "Value", 80, "LessThanOrEqual"),
            VarAssign("s3:ok", "写装配OK", "St3_Ok", L("s3:cmp", "Result", "扭矩<=80"), "Global", null),
            ResultDisplay("s3:display", "装配结果", new[]
            {
                ("r1", "扭矩", "s3:torque", "读扭矩", "Value"),
                ("r2", "判定", "s3:cmp", "扭矩<=80", "Result"),
            }, 0, "s3:acq", "采集装配图"),
            Delay("s3:delay", "装配节拍", 200),
        };
        return Flow("St3_Assemble", "工位3：Z压装/拧紧/扭矩判定", 0, 96, 0, steps);
    }

    // ==================================================================
    //  St4 尺寸检测：A/B/C 三套配方
    // ==================================================================
    static JObject BuildSt4DimensionFlow()
    {
        var typeAssign = VarAssign("s4:type", "写产品类型", "DimProductType", LG("ProductTypeCode"), "Runtime", null, true);

        var branchA = CaliperSet("s4a", 40, 240, 200, 200, 90);
        var branchB = CaliperSet("s4b", 41, 241, 201, 201, 90.5);
        var branchC = new JArray
        {
            ImgFile("s4c:acq1", "采集宽度图", DIR_CALIPER + @"\1-宽度40px.png", 0),
            Caliper("s4c:w", "宽度测量", "s4c:acq1", "采集宽度图", "Width",
                new[] { Reg("宽度区", "Rectangle", 240, 300, 0, 80, 100) }, 5, 30, 40, 0.5, -0.5, 1),
            ImgFile("s4c:acq4", "采集圆图", DIR_CALIPER + @"\4-圆直径200px.png", 0),
            Caliper("s4c:dia", "圆直径测量", "s4c:acq4", "采集圆图", "CircleDiameter",
                new[] { Reg("圆搜索区", "Circle", 240, 320, 100) }, 16, 30, 200, 1, -1, 1),
            Logic("s4c:and", "尺寸综合C", "s4c:w", "宽度测量", "s4c:dia", "圆直径测量", "IsOk", "IsOk"),
            VarAssign("s4c:ok", "写尺寸OK", "St4_Ok", L("s4c:and", "Result", "尺寸综合C"), "Global", null),
            VarAssign("s4c:msg", "写尺寸说明", "St4_Message", null, "Global", new JValue("配方C：宽度+圆直径")),
        };

        var caseStep = CaseStep("s4:case", "尺寸配方", "DimProductType", new JArray
        {
            Branch(4, "1", "1", branchA),
            Branch(4, "2", "2", branchB),
            Branch(4, "3", "3", branchC),
            Branch(2, "默认", "", new JArray
            {
                VarAssign("s4:defOk", "默认尺寸NG", "St4_Ok", null, "Global", new JValue(false)),
                VarAssign("s4:defMsg", "默认尺寸说明", "St4_Message", null, "Global", new JValue("未知产品类型")),
            }),
        }, "System.Int32");

        var steps = new JArray { typeAssign, caseStep, Delay("s4:delay", "尺寸节拍", 200) };
        return Flow("St4_Dimension", "工位4：5 种卡尺 + 圆关系，A/B/C 配方", 0, 96, 0, steps);
    }

    static JArray CaliperSet(string p, double w, double pp, double pl, double dia, double ang)
    {
        var steps = new JArray
        {
            ImgFile(p + ":acq1", "采集宽度图", DIR_CALIPER + @"\1-宽度40px.png", 0),
            Caliper(p + ":w", "宽度测量", p + ":acq1", "采集宽度图", "Width",
                new[] { Reg("宽度区", "Rectangle", 240, 300, 0, 80, 100) }, 5, 30, w, 0.5, -0.5, 1),
            ImgFile(p + ":acq2", "采集两点距图", DIR_CALIPER + @"\2-两点距240px.png", 0),
            Caliper(p + ":pp", "两点距测量", p + ":acq2", "采集两点距图", "PointToPoint",
                new[] { Reg("左条", "Rectangle", 240, 100, 0, 60, 80), Reg("右条", "Rectangle", 240, 340, 0, 60, 80) },
                5, 30, pp, 0.5, -0.5, 1),
            ImgFile(p + ":acq3", "采集点线距图", DIR_CALIPER + @"\3-点到线距200px.png", 0),
            Caliper(p + ":pl", "点到线距测量", p + ":acq3", "采集点线距图", "PointToLineDistance",
                new[] { Reg("点区", "Rectangle", 250, 300, 0, 60, 60), Reg("线区", "Rectangle", 250, 100, 0, 60, 80) },
                5, 30, pl, 0.5, -0.5, 1),
            ImgFile(p + ":acq4", "采集圆图", DIR_CALIPER + @"\4-圆直径200px.png", 0),
            Caliper(p + ":dia", "圆直径测量", p + ":acq4", "采集圆图", "CircleDiameter",
                new[] { Reg("圆搜索区", "Circle", 240, 320, 100) }, 16, 30, dia, 1, -1, 1),
            ImgFile(p + ":acq5", "采集角度图", DIR_CALIPER + @"\5-角度90.png", 0),
            Caliper(p + ":ang", "角度测量", p + ":acq5", "采集角度图", "Angle",
                new[] { Reg("竖线区", "Rectangle", 120, 240, 1.5708, 60, 80), Reg("横线区", "Rectangle", 240, 440, 0, 60, 80) },
                5, 30, ang, 0.5, -0.5, 1),
            Logic(p + ":and1", "尺寸与1", p + ":w", "宽度测量", p + ":pp", "两点距测量", "IsOk", "IsOk"),
            Logic(p + ":and2", "尺寸与2", p + ":and1", "尺寸与1", p + ":pl", "点到线距测量", "Result", "IsOk"),
            Logic(p + ":and3", "尺寸与3", p + ":and2", "尺寸与2", p + ":dia", "圆直径测量", "Result", "IsOk"),
            Logic(p + ":and4", "尺寸综合", p + ":and3", "尺寸与3", p + ":ang", "角度测量", "Result", "IsOk"),
            VarAssign(p + ":ok", "写尺寸OK", "St4_Ok", L(p + ":and4", "Result", "尺寸综合"), "Global", null),
            VarAssign(p + ":msg", "写尺寸说明", "St4_Message", null, "Global", new JValue("配方：" + p)),
        };
        return steps;
    }

    // ==================================================================
    //  St5 表面 + 颜色（工位内并行）
    // ==================================================================
    static JObject BuildSt5SurfaceFlow()
    {
        var pre = new JArray
        {
            ImgFile("s5:acq", "采集板卡图", DIR_BOARD + @"\board-01.png", 0),
            PreProcess("s5:pre", "图像预处理", new JArray
            {
                Op("gauss", ("Size", "5")),
                Op("gray_closing", ("Width", "5"), ("Height", "5")),
                Op("emphasize", ("Width", "7"), ("Height", "7"), ("Factor", "1.2")),
            }, 0, "s5:acq", "采集板卡图"),
            CreateRoi("s5:roi", "表面ROI", new JArray { Roi("表面区", "Rectangle", 380, 560, 0, 80, 80) }, 0, "s5:pre", "图像预处理"),
            GeometryFind("s5:find", "找表面圆", "Circle", "s5:roi", "表面ROI", 1, 20, 40, 30, false, 0, "s5:pre", "图像预处理"),
            PoseFollow("s5:pose", "跟随表面", "s5:roi", "表面ROI", "s5:find", "找表面圆", 0),
            ImgFile("s5:color", "采集颜色图", IMG_COLOR, 0),
        };

        var parallel = Parallel("s5:parallel", "表面与颜色并行", 1, 0, 8000, new JArray
        {
            Branch(0, "缺陷", "", new JArray
            {
                BlobDetect("s5:blob", "表面缺陷检测", "s5:pose", "跟随表面", 0.05, 2, "s5:pre", "图像预处理"),
                VarAssign("s5:def", "写缺陷数", "DefectCount", L("s5:blob", "DefectCount", "表面缺陷检测"), "Global", null),
            }),
            Branch(0, "线序", "", new JArray
            {
                Action("s5:seq", "颜色序列检查", "图像处理", P_COLORCHK, "颜色序列检查", "\uf1fb", new JObject
                {
                    ["RoiParams"] = new JArray { 400, 276, 1.5708, 320, 210 },
                    ["ExpectedSequence"] = "",
                    ["RecipeText"] = "12 芯 = *,*,*,*,*,*,*,*,*,*,*,*\r\n",
                    ["WireCount"] = 12,
                    ["ReverseSequence"] = false,
                    ["Regularize"] = true,
                    ["DisplayViewIndex"] = 0,
                }, new JObject { ["Image"] = L("s5:color", "Image", "采集颜色图") }),
                VarAssign("s5:seqOk", "写线序", "ColorSeqOk", L("s5:seq", "Result", "颜色序列检查"), "Global", null),
            }),
            Branch(0, "区域颜色", "", new JArray
            {
                Action("s5:red", "区域红灯", "图像处理", P_COLORREG, "区域颜色检查", "\uf1fc", new JObject
                {
                    ["RoiShape"] = "Circle",
                    ["RoiParams"] = new JArray { 250, 830, 55, 0, 0 },
                    ["ExpectedColors"] = "红",
                    ["MinShare"] = 0.6,
                    ["SamplePoints"] = 400,
                    ["DisplayViewIndex"] = 0,
                }, new JObject { ["Image"] = L("s5:color", "Image", "采集颜色图") }),
                VarAssign("s5:redOk", "写区域颜色", "ColorRegionOk", L("s5:red", "Result", "区域红灯"), "Global", null),
            }),
        });

        var steps = new JArray();
        foreach (var s in pre) steps.Add(s);
        steps.Add(parallel);
        steps.Add(CompareGlobal("s5:defCmp", "缺陷<=10", "DefectCount", 10, "LessThanOrEqual"));
        steps.Add(LogicVars("s5:colorAnd", "颜色与", "ColorSeqOk", "ColorRegionOk"));
        steps.Add(Logic("s5:and", "表面综合", "s5:defCmp", "缺陷<=10", "s5:colorAnd", "颜色与"));
        steps.Add(VarAssign("s5:ok", "写表面OK", "St5_Ok", L("s5:and", "Result", "表面综合"), "Global", null));
        steps.Add(Delay("s5:delay", "表面节拍", 200));
        return Flow("St5_Surface", "工位5：Blob + 颜色线序 + 区域颜色（并行）", 0, 96, 0, steps);
    }

    // ==================================================================
    //  St6 追溯
    // ==================================================================
    static JObject BuildSt6TraceFlow()
    {
        var steps = new JArray
        {
            ImgFile("s6:ocrAcq", "采集批号图", @"D:\C#\VM\Image\images\ocr\lot_number_01.png", 0),
            Ocr("s6:ocr", "OCR识别", new double[] { 272, 320, 0, 170, 40 }, 0, "s6:ocrAcq", "采集批号图", true),
            ImgFile("s6:qrAcq", "采集二维码图", IMG_QR, 0),
            Action("s6:qr", "二维码读取", "图像处理", P_CODE, "二维码读取", "\uf029", new JObject
            {
                ["Symbology"] = "QR Code", ["TimeoutMs"] = 500, ["Polarity"] = "any",
            }, new JObject { ["Image"] = L("s6:qrAcq", "Image", "采集二维码图") }),
            ImgFile("s6:beadAcq", "采集胶路图", IMG_BEAD, 0),
            Action("s6:bead", "胶路检测", "缺陷检测", P_BEAD, "胶路检测", "\uf1b3", BuildBeadConfig(), new JObject
            {
                ["SrcImage"] = L("s6:beadAcq", "Image", "采集胶路图"),
            }),
            VarAssign("s6:ocrText", "写OCR", "OcrText", L("s6:ocr", "Text", "OCR识别"), "Global", null),
            VarAssign("s6:barcode", "写条码", "BarcodeText", L("s6:qr", "Text", "二维码读取"), "Global", null),
            VarAssign("s6:beadErr", "写胶路错误", "BeadErrors", L("s6:bead", "ErrorCount", "胶路检测"), "Global", null),
            Compare("s6:ocrCmp", "OCR有文本", "s6:ocr", "OCR识别", "Count", 0, "GreaterThan"),
            Compare("s6:qrCmp", "码有内容", "s6:qr", "二维码读取", "Count", 0, "GreaterThan"),
            Logic("s6:and1", "追溯与1", "s6:ocrCmp", "OCR有文本", "s6:qrCmp", "码有内容"),
            Logic("s6:and2", "追溯综合", "s6:and1", "追溯与1", "s6:bead", "胶路检测", "Result", "IsOk"),
            VarAssign("s6:ok", "写追溯OK", "St6_Ok", L("s6:and2", "Result", "追溯综合"), "Global", null),
            Delay("s6:delay", "追溯节拍", 200),
        };
        return Flow("St6_Trace", "工位6：OCR + 二维码 + 胶路", 0, 96, 0, steps);
    }

    static JObject BuildBeadConfig()
        => new()
        {
            ["RecipeLibraryJson"] = JsonConvert.SerializeObject(new JArray
            {
                new JObject
                {
                    ["Name"] = "BeadA",
                    ["RefImagePath"] = IMG_BEAD,
                    ["RefNoBeadImagePath"] = IMG_BEAD_REF,
                    ["RefPointsJson"] = JsonConvert.SerializeObject(new[]
                    {
                        new[] { 610, 419 }, new[] { 570, 421 }, new[] { 530, 425 }, new[] { 490, 433 },
                        new[] { 450, 445 }, new[] { 430, 454 }, new[] { 394, 475 }, new[] { 358, 500 },
                        new[] { 331, 525 }, new[] { 309, 550 }, new[] { 294, 575 }, new[] { 284, 600 },
                        new[] { 277, 625 }, new[] { 273, 650 }, new[] { 271, 675 }, new[] { 269, 700 },
                        new[] { 267, 725 }, new[] { 267, 750 }, new[] { 274, 775 }, new[] { 285, 800 },
                        new[] { 304, 825 }, new[] { 323, 850 }, new[] { 343, 875 }, new[] { 364, 900 },
                        new[] { 388, 925 }, new[] { 419, 950 }, new[] { 456, 975 }, new[] { 470, 984 },
                        new[] { 490, 993 }, new[] { 510, 997 }, new[] { 530, 997 },
                    }),
                    ["RectifyQuadJson"] = "",
                    ["TargetWidth"] = 15.0,
                    ["WidthTolerance"] = 8.0,
                    ["PositionTolerance"] = 30.0,
                    ["Polarity"] = "dark",
                    ["LearnedSignature"] = "",
                },
            }),
            ["DefaultRecipeName"] = "BeadA",
            ["AlignMode"] = "None",
            ["Polarity"] = "Dark",
            ["TargetWidth"] = 15.0,
            ["WidthTolerance"] = 8.0,
            ["PositionTolerance"] = 30.0,
            ["MinErrorLength"] = 10.0,
            ["MinScore"] = 0.4,
            ["DisplayViewIndex"] = 0,
            ["OutputAlignedImage"] = false,
            ["UnitOutput"] = "Pixel",
            ["PixelSizeMm"] = 0.05,
        };

    // ==================================================================
    //  St7 功能测试
    // ==================================================================
    static JObject BuildSt7FunctionFlow()
    {
        var steps = new JArray
        {
            CommRead("s7:meter", "读仪表", "Meter_Value"),
            Compare("s7:cmp", "仪表<=80", "s7:meter", "读仪表", "Value", 80, "LessThanOrEqual"),
            VarAssign("s7:value", "写功能值", "FunctionValue", L("s7:meter", "Value", "读仪表"), "Global", null),
            VarAssign("s7:ok", "写功能OK", "St7_Ok", L("s7:cmp", "Result", "仪表<=80"), "Global", null),
            CommWrite("s7:s7", "写S7结果", "S7_Result", new JValue(7), true),
            CommRead("s7:s7Back", "回读S7", "S7_Result"),
            Compare("s7:s7Cmp", "S7=7", "s7:s7Back", "回读S7", "Value", 7, "Equal"),
            Delay("s7:delay", "功能节拍", 100),
        };
        return Flow("St7_Function", "工位7：仪表阈值 + S7 回读", 0, 96, 0, steps);
    }

    // ==================================================================
    //  St8 分拣
    // ==================================================================
    static JObject BuildSt8SortFlow()
    {
        var okSteps = new JArray
        {
            MotionMove("s8:xOk", "X良品位", "X", "Absolute", 200, 20),
            MotionMove("s8:yOk", "Y良品位", "Y", "Absolute", 10, 20),
            MotionIo("s8:vacOff", "真空释放", "ZMotion_Virtual", 1, false, "WriteOutput"),
            VarAssign("s8:bin", "写良品仓", "SortBin", null, "Global", new JValue("OK")),
        };
        AddIncVar(okSteps, "s8:okCount", "良品计数+1", "OKCount");

        var ngSteps = new JArray
        {
            MotionMove("s8:xNg", "X NG位", "X", "Absolute", 200, 20),
            MotionMove("s8:yNg", "Y NG位", "Y", "Absolute", -10, 20),
            MotionIo("s8:reject", "NG剔除", "ZMotion_Virtual", 3, true, "WriteOutput"),
            VarAssign("s8:binNg", "写NG仓", "SortBin", null, "Global", new JValue("NG")),
        };
        AddIncVar(ngSteps, "s8:ngCount", "不良计数+1", "NGCount");

        var steps = new JArray
        {
            LogicVars("s8:final", "读最终判定", "FinalOk", "FinalOk"),
            IfStep("s8:if", "分拣判断", "Ok", G("s8:var:ok"), "Ok", "System.Boolean",
                L("s8:final", "Result", "读最终判定"), okSteps, ngSteps),
            MotionStop("s8:stopX", "X停止", "X", "Decelerate", false),
            MotionStop("s8:stopY", "Y停止", "Y", "Decelerate", false),
            VarAssign("s8:ok", "写分拣OK", "St8_Ok", null, "Global", new JValue(true)),
            Delay("s8:delay", "分拣节拍", 200),
        };
        return Flow("St8_Sort", "工位8：OK/NG 分拣", 0, 96, 0, steps);
    }

    // ==================================================================
    //  St9 下料
    // ==================================================================
    static JObject BuildSt9UnloadFlow()
    {
        var steps = new JArray
        {
            MotionMove("s9:x", "X下料位", "X", "Absolute", 220, 20),
            MotionMove("s9:y", "Y下料位", "Y", "Absolute", 0, 20),
            MotionMove("s9:z", "Z下料位", "Z", "Absolute", 0, 20),
            MotionIo("s9:label", "贴标", "ZMotion_Virtual", 4, true, "WriteOutput"),
            Delay("s9:labelWait", "贴标保持", 200),
            MotionIo("s9:labelOff", "贴标关闭", "ZMotion_Virtual", 4, false, "WriteOutput"),
            MotionIo("s9:clampOff", "夹具松开", "ZMotion_Virtual", 0, false, "WriteOutput"),
            MotionIo("s9:vacOff", "真空关闭", "ZMotion_Virtual", 1, false, "WriteOutput"),
            VarAssign("s9:ok", "写下料OK", "St9_Ok", null, "Global", new JValue(true)),
            Delay("s9:delay", "下料节拍", 200),
        };
        return Flow("St9_Unload", "工位9：下料/贴标/夹具复位", 0, 96, 0, steps);
    }

    // ==================================================================
    //  St10 返修
    // ==================================================================
    static JObject BuildSt10ReworkFlow()
    {
        var steps = new JArray
        {
            MotionIo("s10:clampOff", "松开夹具", "ZMotion_Virtual", 0, false, "WriteOutput"),
            MotionMove("s10:x", "X返修位", "X", "Absolute", 150, 10),
            MotionMove("s10:y", "Y返修位", "Y", "Absolute", 0, 10),
            Delay("s10:settle", "返修稳定", 300),
            MotionIo("s10:clamp", "重新夹紧", "ZMotion_Virtual", 0, true, "WriteOutput"),
            VarAssign("s10:done", "写返修完成", "ReworkDone", null, "Global", new JValue(true)),
            Delay("s10:delay", "返修节拍", 200),
        };
        return Flow("St10_Rework", "工位10：返修机械复位", 0, 96, 0, steps);
    }

    // ==================================================================
    //  Recipe
    // ==================================================================
    static JObject BuildAutomationRecipeFlow()
    {
        var steps = new JArray
        {
            VarAssign("ar:name", "写配方名", "RecipeName", null, "Global", new JValue("Product_A")),
            VarAssign("ar:type", "写产品类型", "ProductTypeCode", null, "Global", new JValue(1)),
            VarAssign("ar:ok", "清零良品", "OKCount", null, "Global", new JValue(0)),
            VarAssign("ar:ng", "清零不良", "NGCount", null, "Global", new JValue(0)),
            VarAssign("ar:cycle", "清零节拍", "CycleCount", null, "Global", new JValue(0)),
            VarAssign("ar:rework", "清零返修", "ReworkCount", null, "Global", new JValue(0)),
            VarAssign("ar:fault", "清故障", "LineFault", null, "Global", new JValue(false)),
            Delay("ar:delay", "配方应用", 100),
        };
        return Flow("Recipe", "配方切换：产品类型/计数清零", 0, 96, 0, steps);
    }

    static void AddRange(JArray target, JArray source)
    {
        foreach (var step in source) target.Add(step);
    }

    static JObject LStep(JObject step, string port, string srcName)
        => new()
        {
            ["Kind"] = 1,
            ["TargetStepId"] = step["StepID"].ToString(),
            ["TargetPortName"] = port,
            ["TargetVariableId"] = Guid.Empty.ToString(),
            ["DisplayAddress"] = srcName + "." + port,
        };
}
