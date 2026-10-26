using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

internal static partial class Program
{
    // ==================================================================
    //  综合测试台：五套测试流程 + 组态测试控制台
    // ==================================================================
    static JObject BuildTestSolution()
    {
        var flows = new JArray
        {
            BuildHomeFlow(),
            TestMainFlow(),
            TestCommFlow(),
            TestMotionFlow(),
            TestVisionFlow(),
            TestLogicFlow(),
            TestDataFlow(),
            TestCommEdgeFlow(),
            TestMotionEdgeFlow(),
            TestVisionEdgeFlow(),
            TestFlowEdgeFlow(),
            TestDataEdgeFlow(),
            TestReportFlow(),
            BuildEndFlow(),
        };

        return new JObject
        {
            ["SolutionName"] = "综合测试台",
            ["Version"] = 1.0,
            ["Config"] = new JObject { ["ImageViewMode"] = 0 },
            ["Flows"] = flows,
            ["SolutionId"] = G("test:solution").ToString(),
            ["CommunicationConfigs"] = BuildTestComms(),
            ["IoBuses"] = new JArray(),
            ["IoDevices"] = new JArray(),
            ["CameraConfigs"] = new JArray(),
            ["MotionCards"] = BuildMotionCards(),
            ["CamTables"] = new JArray(),
            ["VariableSnapshots"] = BuildTestVariables(),
            ["VariableGroups"] = new JArray(),
            ["WatchItems"] = new JArray(),
            ["Scada"] = BuildScadaDocument(),
        };
    }

    // ==================================================================
    //  测试结果变量
    // ==================================================================
    static JArray BuildTestVariables()
    {
        var list = BuildVariables();
        list.Add(Var("CommOk", T_BOOL, "通讯测试通过", false));
        list.Add(Var("MotionOk", T_BOOL, "运动测试通过", false));
        list.Add(Var("VisionOk", T_BOOL, "视觉测试通过", false));
        list.Add(Var("LogicOk", T_BOOL, "逻辑测试通过", false));
        list.Add(Var("DataOk", T_BOOL, "数据测试通过", false));
        list.Add(Var("TestPassCount", T_INT, "通过测试项数", 0));
        list.Add(Var("TestFailCount", T_INT, "失败测试项数", 0));
        list.Add(Var("TestSummary", T_STR, "最近一次测试摘要", "尚未运行测试"));
        list.Add(Var("LoopCounter", T_INT, "While 测试计数器", 0));
        list.Add(Var("ForIndex", T_INT, "For 测试索引", 0));
        list.Add(Var("ForCount", T_INT, "For 循环计数", 0));
        list.Add(Var("ParallelA", T_BOOL, "并行分支 A", false));
        list.Add(Var("ParallelB", T_BOOL, "并行分支 B", false));
        list.Add(Var("IfOk", T_BOOL, "If 测试", false));
        list.Add(Var("CaseOk", T_BOOL, "Case 测试", false));
        list.Add(Var("ForOk", T_BOOL, "For 测试", false));
        list.Add(Var("WhileOk", T_BOOL, "While 测试", false));
        list.Add(Var("WaitOk", T_BOOL, "Wait 测试", false));
        list.Add(Var("TimeoutOk", T_BOOL, "Wait 超时测试", false));
        list.Add(Var("NegMissingOk", T_BOOL, "缺图负例", false));
        list.Add(Var("NegCodeOk", T_BOOL, "无码负例", false));
        list.Add(Var("NegCalOk", T_BOOL, "空ROI负例", false));
        list.Add(Var("NegTextOk", T_BOOL, "无文本负例", false));
        list.Add(Var("EdgeCommOk", T_BOOL, "通讯边界测试通过", false));
        list.Add(Var("EdgeMotionOk", T_BOOL, "运动边界测试通过", false));
        list.Add(Var("EdgeVisionOk", T_BOOL, "视觉边界测试通过", false));
        list.Add(Var("EdgeFlowOk", T_BOOL, "逻辑边界测试通过", false));
        list.Add(Var("EdgeDataOk", T_BOOL, "数据边界测试通过", false));
        list.Add(Var("CommTypeOk", T_BOOL, "通讯类型不匹配负例", false));
        list.Add(Var("CommRangeOk", T_BOOL, "通讯数值越界负例", false));
        list.Add(Var("CommOfflineOk", T_BOOL, "通讯离线连接负例", false));
        list.Add(Var("MotionBadAxisOk", T_BOOL, "运动无效轴负例", false));
        list.Add(Var("MotionBadEnableOk", T_BOOL, "运动无效轴使能负例", false));
        list.Add(Var("MotionIoReadOk", T_BOOL, "运动IO读取正例", false));
        list.Add(Var("MotionIoBadOk", T_BOOL, "运动IO无效端口负例", false));
        list.Add(Var("MotionBadStopOk", T_BOOL, "运动无效轴停止负例", false));
        list.Add(Var("MotionWaitTimeoutOk", T_BOOL, "运动等待超时负例", false));
        list.Add(Var("VisionCorruptOk", T_BOOL, "视觉坏图负例", false));
        list.Add(Var("VisionCaliperNegOk", T_BOOL, "卡尺NG负例", false));
        list.Add(Var("VisionCodeWrongOk", T_BOOL, "错误码制负例", false));
        list.Add(Var("FlowCaseDefaultOk", T_BOOL, "Case无默认负例", false));
        list.Add(Var("FlowNestedOk", T_BOOL, "嵌套循环正例", false));
        list.Add(Var("FlowForZeroOk", T_BOOL, "For零次正例", false));
        list.Add(Var("FlowParallelFailOk", T_BOOL, "并行失败负例", false));
        list.Add(Var("DataOverflowOk", T_BOOL, "类型转换溢出负例", false));
        list.Add(Var("DataCsvBadOk", T_BOOL, "CSV坏路径负例", false));
        list.Add(Var("DataUploadBadOk", T_BOOL, "上报不可达负例", false));
        list.Add(NetVar("Offline_Value", T_SHORT, "离线连接测试变量", 0, "PLC_Offline", "ModbusTcp", "HoldingRegisters", "0", -1, 1, 0, "Int16"));
        return list;
    }

    // ==================================================================
    //  Main：测试台入口提示
    // ==================================================================
    static JObject TestMainFlow()
    {
        var steps = new JArray
        {
            VarAssign("tm:summary", "写测试提示", "TestSummary", null, "Global",
                new JValue("请从组态「测试总览」逐个运行测试项；不要用工具栏「循环运行」同时拉起所有测试流程")),
            Delay("tm:delay", "入口延时", 100),
        };
        return Flow("Main", "测试台入口：提示从组态运行测试项", 2, 0, 0, steps);
    }

    // ==================================================================
    //  通讯测试
    // ==================================================================
    static JObject TestCommFlow()
    {
        var steps = new JArray
        {
            ImgFile("tc:acq", "采集通讯测试图", IMG_COLOR, 0),
            VarAssign("tc:init", "初始化通讯结果", "CommOk", null, "Global", new JValue(true)),
            VarAssign("tc:summary0", "通讯摘要", "TestSummary", null, "Global", new JValue("通讯测试开始")),
            Delay("tc:delay", "等通讯连接就绪", 1000),

            CommRead("tc:readTrigger", "读PLC触发", "PLC_Trigger"),
            CommRead("tc:readAck", "读PLC确认", "PLC_Ack"),
            CommRead("tc:readMeter", "读仪表", "Meter_Value"),
            CommRead("tc:readS7Hb", "读S7心跳", "S7_Heartbeat"),
            CommRead("tc:readS7Result", "读S7结果", "S7_Result"),

            CommWrite("tc:writeResult", "写PLC结果True", "PLC_Result", new JValue(true), true),
            CommWrite("tc:writeTrigger", "写PLC触发1", "PLC_Trigger", new JValue(1), true),
            CommWrite("tc:writeS7Hb", "写S7心跳True", "S7_Heartbeat", new JValue(true), true),
            CommWrite("tc:writeS7Result", "写S7结果7", "S7_Result", new JValue(7), true),

            CommRead("tc:readBackTrigger", "回读触发", "PLC_Trigger"),
            CommRead("tc:readBackS7", "回读S7结果", "S7_Result"),

            Compare("tc:cmpTrigger", "触发回读=1", "tc:readBackTrigger", "回读触发", "Value", 1, "Equal"),
            Compare("tc:cmpS7", "S7结果=7", "tc:readBackS7", "回读S7结果", "Value", 7, "Equal"),
            Compare("tc:cmpMeter", "仪表>=0", "tc:readMeter", "读仪表", "Value", 0, "GreaterThanOrEqual"),

            Logic("tc:and1", "通讯与1", "tc:cmpTrigger", "触发回读=1", "tc:cmpS7", "S7结果=7"),
            Logic("tc:and2", "通讯与2", "tc:and1", "通讯与1", "tc:cmpMeter", "仪表>=0"),
            Logic("tc:andW1", "写成功与1", "tc:writeResult", "写PLC结果True", "tc:writeS7Hb", "写S7心跳True", "Success", "Success"),
            Logic("tc:andW2", "写成功与2", "tc:andW1", "写成功与1", "tc:writeTrigger", "写PLC触发1", "Result", "Success"),
            Logic("tc:andPos", "通讯正例", "tc:and2", "通讯与2", "tc:andW2", "写成功与2"),
            VarAssign("tc:setComm", "写通讯正例结果", "CommOk", L("tc:andPos", "Result", "通讯正例"), "Global", null),
            IfStep("tc:posCheck", "通讯正例判定", "Ok", G("tc:var:posOk"), "Ok", "System.Boolean",
                L("tc:andPos", "Result", "通讯正例"),
                new JArray
                {
                    VarAssign("tc:posPass", "通讯正例通过", "TestSummary", null, "Global", new JValue("通讯正例：通过")),
                },
                new JArray
                {
                    VarAssign("tc:posFail", "通讯正例失败", "TestSummary", null, "Global",
                        new JValue("通讯正例：失败（写成功但回读不一致，检查 PLC 地址/字序）")),
                }),

            CommWrite("tc:writeRo", "写只读仪表(应失败)", "Meter_Value", new JValue(1.0), true),
            IfStep("tc:roCheck", "只读拒写判定", "!Ok", G("tc:var:roOk"), "Ok", "System.Boolean",
                L("tc:writeRo", "Success", "写只读仪表(应失败)"),
                new JArray
                {
                    VarAssign("tc:roPass", "只读拒写通过", "TestSummary", null, "Global",
                        new JValue("只读区拒写：通过（预期失败）")),
                },
                new JArray
                {
                    VarAssign("tc:roFail", "只读拒写异常", "CommOk", null, "Global", new JValue(false)),
                    VarAssign("tc:roFailMsg", "只读拒写异常摘要", "TestSummary", null, "Global",
                        new JValue("只读区拒写：未按预期拒绝")),
                }),

            CommWrite("tc:writeMissing", "写不存在变量(应失败)", "NoSuchVariable", new JValue(1), true),
            IfStep("tc:missCheck", "不存在变量判定", "!Ok", G("tc:var:missOk"), "Ok", "System.Boolean",
                L("tc:writeMissing", "Success", "写不存在变量(应失败)"),
                new JArray
                {
                    VarAssign("tc:missPass", "不存在变量通过", "TestSummary", null, "Global",
                        new JValue("不存在变量拒写：通过（预期失败）")),
                },
                new JArray
                {
                    VarAssign("tc:missFail", "不存在变量异常", "CommOk", null, "Global", new JValue(false)),
                    VarAssign("tc:missFailMsg", "不存在变量异常摘要", "TestSummary", null, "Global",
                        new JValue("不存在变量拒写：未按预期拒绝")),
                }),

            IfStep("tc:count", "通讯结果计数", "Ok", G("tc:var:commCount"), "Ok", "System.Boolean",
                LG("CommOk"),
                IncVarSteps("tc:pass", "通过项+1", "TestPassCount"),
                IncVarSteps("tc:fail", "失败项+1", "TestFailCount")),

            ResultDisplay("tc:display", "通讯结果显示", new[]
            {
                ("r1", "触发", "tc:readBackTrigger", "回读触发", "Value"),
                ("r2", "S7结果", "tc:readBackS7", "回读S7结果", "Value"),
                ("r3", "仪表", "tc:readMeter", "读仪表", "Value"),
                ("r4", "结果", "tc:andPos", "通讯正例", "Result"),
            }, 1, "tc:acq", "采集通讯测试图"),
        };

        return Flow("Test_Comm", "通讯测试：Modbus 503/504 + S7 102 + 负例", 0, 96, 0, steps);
    }

    // ==================================================================
    //  运动测试
    // ==================================================================
    static JObject TestMotionFlow()
    {
        var steps = new JArray
        {
            ImgFile("tmot:acq", "采集运动测试图", IMG_COLOR, 0),
            VarAssign("tmot:init", "初始化运动结果", "MotionOk", null, "Global", new JValue(true)),
            MotionEnable("tmot:enX", "X使能", "X", true, 10000),
            MotionEnable("tmot:enY", "Y使能", "Y", true, 10000),
            Disable(MotionHome("tmot:homeX", "X回零(默认禁用)", "X", "NegativeLimitIndex", 60000)),

            MotionMove("tmot:move10", "X到10mm", "X", "Absolute", 10, 10),
            MotionWait("tmot:wait10", "等待X到10mm", "X", 2000, 20),
            MotionReadStatus("tmot:status10", "读X状态@10", "X"),
            Compare("tmot:cmp10", "X位置=10", "tmot:status10", "读X状态@10", "Position", 10, "Equal", 0.5),

            MotionMove("tmot:moveNeg5", "X到-5mm", "X", "Absolute", -5, 10),
            MotionWait("tmot:waitNeg5", "等待X到-5mm", "X", 2000, 20),
            MotionReadStatus("tmot:statusNeg5", "读X状态@-5", "X"),
            Compare("tmot:cmpNeg5", "X位置=-5", "tmot:statusNeg5", "读X状态@-5", "Position", -5, "Equal", 0.5),

            MotionIo("tmot:io", "IO输出0", "ZMotion_Virtual", 0, true, "WriteOutput"),
            MotionStop("tmot:stop", "X停止", "X", "Decelerate", false),

            Logic("tmot:and1", "运动与1", "tmot:cmp10", "X位置=10", "tmot:cmpNeg5", "X位置=-5"),
            Logic("tmot:and2", "运动与2", "tmot:and1", "运动与1", "tmot:status10", "读X状态@10", "Result", "Success"),
            Logic("tmot:and3", "运动与3", "tmot:and2", "运动与2", "tmot:statusNeg5", "读X状态@-5", "Result", "Success"),
            Logic("tmot:and4", "运动与4", "tmot:and3", "运动与3", "tmot:io", "IO输出0", "Result", "Accepted"),
            VarAssign("tmot:setMotion", "写运动正例结果", "MotionOk", L("tmot:and4", "Result", "运动与4"), "Global", null),

            MotionEnable("tmot:disX", "X失能", "X", false, 10000),
            MotionMove("tmot:moveDisabled", "失能后移动(应失败)", "X", "Absolute", 0, 10),
            MotionReadStatus("tmot:statusDisabled", "读X状态@失能", "X"),
            Compare("tmot:cmpDisabled", "失能后位置=-5", "tmot:statusDisabled", "读X状态@失能", "Position", -5, "Equal", 0.5),
            IfStep("tmot:disabledCheck", "失能移动判定", "Ok", G("tmot:var:disabledOk"), "Ok", "System.Boolean",
                L("tmot:cmpDisabled", "Result", "失能后位置=-5"),
                new JArray
                {
                    VarAssign("tmot:disabledPass", "失能移动通过", "TestSummary", null, "Global",
                        new JValue("失能后位置保持 -5：通过")),
                },
                new JArray
                {
                    VarAssign("tmot:disabledFail", "失能移动异常", "MotionOk", null, "Global", new JValue(false)),
                    VarAssign("tmot:disabledFailMsg", "失能移动异常摘要", "TestSummary", null, "Global",
                        new JValue("失能后轴仍移动：位置不在 -5")),
                }),
            MotionEnable("tmot:enX2", "X重新使能", "X", true, 10000),

            IfStep("tmot:count", "运动结果计数", "Ok", G("tmot:var:motionCount"), "Ok", "System.Boolean",
                LG("MotionOk"),
                IncVarSteps("tmot:pass", "通过项+1", "TestPassCount"),
                IncVarSteps("tmot:fail", "失败项+1", "TestFailCount")),

            ResultDisplay("tmot:display", "运动结果显示", new[]
            {
                ("r1", "位置", "tmot:statusNeg5", "读X状态@-5", "Position"),
                ("r2", "到位", "tmot:statusNeg5", "读X状态@-5", "InPosition"),
                ("r3", "使能", "tmot:statusNeg5", "读X状态@-5", "Enabled"),
                ("r4", "结果", "tmot:and4", "运动与4", "Result"),
            }, 1, "tmot:acq", "采集运动测试图"),
        };

        return Flow("Test_Motion", "运动测试：VPLC532R 轴使能/定位/等待/状态/IO/停止 + 负例", 0, 96, 0, steps);
    }

    // ==================================================================
    //  视觉测试
    // ==================================================================
    static JObject TestVisionFlow()
    {
        var steps = new JArray
        {
            VarAssign("tv:init", "初始化视觉结果", "VisionOk", null, "Global", new JValue(true)),

            ImgFile("tv:acq", "采集板卡图", IMG_BOARD_01, 1),
            PreProcess("tv:pre", "图像预处理", new JArray
            {
                Op("gauss", ("Size", "5")),
                Op("gray_closing", ("Width", "5"), ("Height", "5")),
                Op("emphasize", ("Width", "7"), ("Height", "7"), ("Factor", "1.2")),
            }, 0, "tv:acq", "采集板卡图"),
            CreateRoi("tv:roi", "ROI圆区域", new JArray { Roi("圆检测区", "Rectangle", 380, 560, 0, 80, 80) }, 0, "tv:pre", "图像预处理"),
            CreateRoi("tv:roiSmall", "ROI小圆区", new JArray { Roi("小圆区", "Rectangle", 60, 545, 0, 45, 45) }, 0, "tv:pre", "图像预处理"),
            GeometryFind("tv:findCircle", "找圆", "Circle", "tv:roi", "ROI圆区域", 1, 20, 40, 30, false, 0, "tv:pre", "图像预处理"),
            GeometryFind("tv:findSmall", "找小圆", "Circle", "tv:roiSmall", "ROI小圆区", 1, 20, 40, 20, false, 0, "tv:pre", "图像预处理"),
            GeometryRelation("tv:rel", "两圆心距", "CircleRelation", "tv:findCircle", "找圆", "tv:findSmall", "找小圆", 0.05),
            PoseFollow("tv:pose", "跟随ROI", "tv:roi", "ROI圆区域", "tv:findCircle", "找圆", 0),
            BlobDetect("tv:blob", "表面缺陷检测", "tv:pose", "跟随ROI", 0.05, 2, "tv:pre", "图像预处理"),
            ImgFile("tv:ocrAcq", "采集批号图", IMG_LOT, 0),
            Ocr("tv:ocr", "OCR识别", new double[] { 272, 320, 0, 170, 40 }, 1, "tv:ocrAcq", "采集批号图", true),
        };

        foreach (var s in ColorSteps()) steps.Add(s);
        foreach (var s in CaliperSteps()) steps.Add(s);
        foreach (var s in BeadSteps()) steps.Add(s);
        foreach (var s in CodeSteps()) steps.Add(s);

        Add(steps,
            Compare("tv:cmpBlob", "缺陷数<=10", "tv:blob", "表面缺陷检测", "DefectCount", 10, "LessThanOrEqual"),
            Compare("tv:cmpOcr", "OCR有文本", "tv:ocr", "OCR识别", "Count", 0, "GreaterThan"),
            Compare("tv:cmpCode", "码有内容", "code:read", "二维码读取", "Count", 0, "GreaterThan"),
            Logic("tv:and1", "视觉正例1", "tv:cmpBlob", "缺陷数<=10", "tv:cmpOcr", "OCR有文本"),
            Logic("tv:and2", "视觉正例2", "tv:and1", "视觉正例1", "tv:cmpCode", "码有内容"),
            Logic("tv:and3", "视觉正例3", "tv:and2", "视觉正例2", "color:and2", "颜色综合判定"),
            Logic("tv:and4", "视觉正例4", "tv:and3", "视觉正例3", "cal:and4", "卡尺综合判定"),
            Logic("tv:and5", "视觉正例5", "tv:and4", "视觉正例4", "bead:inspect", "胶路检测", "Result", "IsOk"),

            // 负例 1：缺图
            ImgFile("tv:missing", "读不存在图(应失败)", IMG_MISSING, 0),
            IfStep("tv:missingCheck", "缺图判定", "!Ok", G("tv:var:missingOk"), "Ok", "System.Boolean",
                L("tv:missing", "Success", "读不存在图(应失败)"),
                new JArray { VarAssign("tv:missingPass", "缺图通过", "NegMissingOk", null, "Global", new JValue(true)) },
                new JArray { VarAssign("tv:missingFail", "缺图异常", "NegMissingOk", null, "Global", new JValue(false)) }),

            // 负例 2：无码图
            ImgFile("tv:noCodeAcq", "采集无码图", IMG_COLOR, 0),
            Action("tv:noCodeRead", "无码图读码(应0)", "图像处理", P_CODE, "二维码读取", "\uf029", new JObject
            {
                ["Symbology"] = "QR Code",
                ["TimeoutMs"] = 500,
                ["Polarity"] = "any",
            }, new JObject { ["Image"] = L("tv:noCodeAcq", "Image", "采集无码图") }),
            Compare("tv:cmpNoCode", "无码图=0", "tv:noCodeRead", "无码图读码(应0)", "Count", 0, "Equal"),
            VarAssign("tv:setNegCode", "写无码负例", "NegCodeOk", L("tv:cmpNoCode", "Result", "无码图=0"), "Global", null),

            // 负例 3：卡尺空 ROI
            Caliper("tv:badCaliper", "空ROI卡尺(应失败)", "cal:acq1", "采集宽度图", "Width",
                new[] { Reg("空区", "Rectangle", 500, 500, 0, 20, 20) }, 5, 30, 0, 0.5, -0.5, 1),
            IfStep("tv:badCaliperCheck", "空ROI判定", "!Ok", G("tv:var:badCalOk"), "Ok", "System.Boolean",
                L("tv:badCaliper", "IsOk", "空ROI卡尺(应失败)"),
                new JArray { VarAssign("tv:badCalPass", "空ROI通过", "NegCalOk", null, "Global", new JValue(true)) },
                new JArray { VarAssign("tv:badCalFail", "空ROI异常", "NegCalOk", null, "Global", new JValue(false)) }),

            // 负例 4：无文本图 OCR
            ImgFile("tv:ocrNegAcq", "采集无文本图", IMG_BLANK, 0),
            Ocr("tv:ocrNeg", "无文本OCR(应0)", new double[] { 505, 790, 0, 40, 40 }, 0, "tv:ocrNegAcq", "采集无文本图", false),
            Compare("tv:cmpNoText", "无文本=0", "tv:ocrNeg", "无文本OCR(应0)", "Count", 0, "Equal"),
            VarAssign("tv:setNegText", "写无文本负例", "NegTextOk", L("tv:cmpNoText", "Result", "无文本=0"), "Global", null),

            LogicVars("tv:andNeg1", "负例与1", "NegMissingOk", "NegCodeOk"),
            LogicVars("tv:andNeg2", "负例与2", "NegCalOk", "NegTextOk"),
            Logic("tv:andNeg3", "负例与3", "tv:andNeg1", "负例与1", "tv:andNeg2", "负例与2"),
            Logic("tv:andAll", "视觉综合判定", "tv:and5", "视觉正例5", "tv:andNeg3", "负例与3"),
            VarAssign("tv:setVision", "写视觉结果", "VisionOk", L("tv:andAll", "Result", "视觉综合判定"), "Global", null),

            IfStep("tv:count", "视觉结果计数", "Ok", G("tv:var:visionCount"), "Ok", "System.Boolean",
                LG("VisionOk"),
                IncVarSteps("tv:pass", "通过项+1", "TestPassCount"),
                IncVarSteps("tv:fail", "失败项+1", "TestFailCount")),

            ResultDisplay("tv:display", "视觉结果显示", new[]
            {
                ("r1", "缺陷数", "tv:blob", "表面缺陷检测", "DefectCount"),
                ("r2", "OCR", "tv:ocr", "OCR识别", "Text"),
                ("r3", "码数", "code:read", "二维码读取", "Count"),
                ("r4", "结果", "tv:andAll", "视觉综合判定", "Result"),
            }, 2, "tv:blob", "表面缺陷检测", "DefectImage")
        );

        return Flow("Test_Vision", "视觉测试：全插件正例 + 缺图/无码/空ROI/无文本负例", 0, 96, 0, steps);
    }

    // ==================================================================
    //  逻辑测试
    // ==================================================================
    static JObject TestLogicFlow()
    {
        var ifStep = IfStep("tl:if", "If测试", "Ok", G("tl:var:ifOk"), "Ok", "System.Boolean",
            L("tl:trueCmp", "Result", "真条件"),
            new JArray { VarAssign("tl:ifTrue", "If真分支", "IfOk", null, "Global", new JValue(true)) },
            new JArray { VarAssign("tl:ifFalse", "If假分支", "IfOk", null, "Global", new JValue(false)) });

        var caseStep = CaseStep("tl:case", "Case测试", "CaseNum", new JArray
        {
            Branch(4, "1", "1", new JArray { VarAssign("tl:case1", "Case1", "CaseOk", null, "Global", new JValue(false)) }),
            Branch(4, "2", "2", new JArray { VarAssign("tl:case2", "Case2", "CaseOk", null, "Global", new JValue(true)) }),
            Branch(2, "默认", "", new JArray { VarAssign("tl:caseD", "Case默认", "CaseOk", null, "Global", new JValue(false)) }),
        }, "System.Int32");

        var forBody = new JArray
        {
            VarAssign("tl:forIndex", "写For索引", "ForIndex", L("tl:for", "Index", "For测试"), "Runtime", null, true),
        };
        AddIncVar(forBody, "tl:forCount", "For计数+1", "ForCount");
        forBody.Add(IfStep("tl:forContinue", "For Continue", "I == 1", G("tl:var:forI1"), "I", "System.Int32",
            L("tl:for", "Index", "For测试"),
            new JArray { ContinueStep("tl:continue", "Continue(索引1)") },
            new JArray()));
        forBody.Add(IfStep("tl:forBreak", "For Break", "I == 2", G("tl:var:forI2"), "I", "System.Int32",
            L("tl:for", "Index", "For测试"),
            new JArray { BreakStep("tl:break", "Break(索引2)") },
            new JArray()));
        var forStep = ForLoop("tl:for", "For测试", 3, forBody, "tl:for");

        var whileBody = new JArray
        {
            MathAddRuntime("tl:whileAdd", "While计数+1", "LoopCounter"),
            VarAssign("tl:whileWrite", "写While计数", "LoopCounter", L("tl:whileAdd", "Result", "While计数+1"), "Runtime", null, true),
        };
        var whileStep = WhileStep("tl:while", "While测试", "LoopCounter < 3", "LoopCounter", whileBody);

        var parallel = Parallel("tl:parallel", "并行测试", 1, 0, 5000, new JArray
        {
            Branch(0, "分支A", "", new JArray
            {
                Delay("tl:delayA", "分支A延时", 100),
                VarAssign("tl:setA", "写分支A", "ParallelA", null, "Global", new JValue(true)),
            }),
            Branch(0, "分支B", "", new JArray
            {
                Delay("tl:delayB", "分支B延时", 200),
                VarAssign("tl:setB", "写分支B", "ParallelB", null, "Global", new JValue(true)),
            }),
        });

        var steps = new JArray
        {
            ImgFile("tl:acq", "采集逻辑测试图", IMG_COLOR, 0),
            VarAssign("tl:init", "初始化逻辑结果", "LogicOk", null, "Global", new JValue(true)),
            VarAssign("tl:initIf", "初始化If", "IfOk", null, "Global", new JValue(false)),
            VarAssign("tl:initCase", "初始化Case", "CaseOk", null, "Global", new JValue(false)),
            VarAssign("tl:initFor", "初始化For", "ForOk", null, "Global", new JValue(false)),
            VarAssign("tl:initWhile", "初始化While", "WhileOk", null, "Global", new JValue(false)),
            VarAssign("tl:initWait", "初始化Wait", "WaitOk", null, "Global", new JValue(false)),
            VarAssign("tl:initTimeout", "初始化Timeout", "TimeoutOk", null, "Global", new JValue(false)),
            VarAssign("tl:initA", "初始化并行A", "ParallelA", null, "Global", new JValue(false)),
            VarAssign("tl:initB", "初始化并行B", "ParallelB", null, "Global", new JValue(false)),

            CompareConst("tl:trueCmp", "真条件", 1, 1, "Equal"),
            ifStep,
            VarAssign("tl:caseNum", "写Case判据", "CaseNum", null, "Runtime", new JValue(2), true),
            caseStep,
            forStep,
            CompareGlobal("tl:forCmp", "For计数=3", "ForCount", 3, "Equal"),
            VarAssign("tl:forOk", "写For结果", "ForOk", L("tl:forCmp", "Result", "For计数=3"), "Global", null),

            VarAssign("tl:whileInit", "初始化While计数", "LoopCounter", null, "Runtime", new JValue(0), true),
            whileStep,
            CompareRuntime("tl:whileCmp", "While计数=3", "LoopCounter", 3, "Equal"),
            VarAssign("tl:whileOk", "写While结果", "WhileOk", L("tl:whileCmp", "Result", "While计数=3"), "Global", null),

            parallel,
            LogicVars("tl:andParallel", "并行与", "ParallelA", "ParallelB"),

            CompareConst("tl:readyCmp", "就绪条件", 1, 1, "Equal"),
            WaitStep("tl:wait", "Wait成功", "Ready", "Ready",
                L("tl:readyCmp", "Result", "就绪条件"), 1000, 1, "就绪等待超时", "System.Boolean"),
            IfStep("tl:waitCheck", "Wait成功判定", "!T", G("tl:var:waitT"), "T", "System.Boolean",
                L("tl:wait", "TimedOut", "Wait成功"),
                new JArray { VarAssign("tl:waitOkTrue", "Wait通过", "WaitOk", null, "Global", new JValue(true)) },
                new JArray { VarAssign("tl:waitOkFalse", "Wait异常", "WaitOk", null, "Global", new JValue(false)) }),

            CompareConst("tl:neverCmp", "永不成立", 1, 2, "Equal"),
            WaitStep("tl:timeoutWait", "Wait超时", "Never", "Never",
                L("tl:neverCmp", "Result", "永不成立"), 500, 1, "超时继续", "System.Boolean"),
            IfStep("tl:timeoutCheck", "超时判定", "T", G("tl:var:timeoutT"), "T", "System.Boolean",
                L("tl:timeoutWait", "TimedOut", "Wait超时"),
                new JArray { VarAssign("tl:timeoutPass", "超时通过", "TimeoutOk", null, "Global", new JValue(true)) },
                new JArray { VarAssign("tl:timeoutFail", "超时异常", "TimeoutOk", null, "Global", new JValue(false)) }),

            LogicVars("tl:and1", "逻辑与1", "IfOk", "CaseOk"),
            LogicVars("tl:and2", "逻辑与2", "ForOk", "WhileOk"),
            Logic("tl:and3", "逻辑与3", "tl:and1", "逻辑与1", "tl:and2", "逻辑与2"),
            Logic("tl:and4", "逻辑与4", "tl:and3", "逻辑与3", "tl:andParallel", "并行与"),
            LogicVars("tl:and5", "逻辑与5", "WaitOk", "TimeoutOk"),
            Logic("tl:and6", "逻辑与6", "tl:and4", "逻辑与4", "tl:and5", "逻辑与5"),
            VarAssign("tl:setLogic", "写逻辑结果", "LogicOk", L("tl:and6", "Result", "逻辑与6"), "Global", null),

            IfStep("tl:count", "逻辑结果计数", "Ok", G("tl:var:logicCount"), "Ok", "System.Boolean",
                LG("LogicOk"),
                IncVarSteps("tl:pass", "通过项+1", "TestPassCount"),
                IncVarSteps("tl:fail", "失败项+1", "TestFailCount")),

            ResultDisplay("tl:display", "逻辑结果显示", new[]
            {
                ("r1", "If", "tl:trueCmp", "真条件", "Result"),
                ("r2", "For", "tl:forCmp", "For计数=3", "Result"),
                ("r3", "While", "tl:whileCmp", "While计数=3", "Result"),
                ("r4", "综合", "tl:and6", "逻辑与6", "Result"),
            }, 1, "tl:acq", "采集逻辑测试图"),
            ReturnStep("tl:return", "测试结束返回"),
        };

        return Flow("Test_Logic", "逻辑测试：If/Case/For/While/Parallel/Wait/Break/Continue/Return", 0, 96, 0, steps);
    }

    // ==================================================================
    //  数据测试
    // ==================================================================
    static JObject TestDataFlow()
    {
        var steps = new JArray
        {
            ImgFile("td:acq", "采集数据测试图", IMG_COLOR, 0),
            VarAssign("td:init", "初始化数据结果", "DataOk", null, "Global", new JValue(true)),
            Action("td:math", "数学计算 1+2", "数据处理", P_MATH, "数学计算", "\uf1ec", new JObject
            {
                ["Operation"] = "Add",
                ["A"] = 1.0,
                ["B"] = 2.0,
            }),
            Compare("td:cmpSum", "和=3", "td:math", "数学计算 1+2", "Result", 3, "Equal"),
            ConvertToInt("td:toInt", "和转Int", "td:math", "数学计算 1+2"),
            Compare("td:cmpInt", "Int=3", "td:toInt", "和转Int", "Result", 3, "Equal"),
            Logic("td:and", "数据与", "td:cmpSum", "和=3", "td:cmpInt", "Int=3"),
            VarAssign("td:setData", "写数据结果", "DataOk", L("td:and", "Result", "数据与"), "Global", null),
            VarAssign("td:result", "写结果到运行时", "CurrentResult", null, "Runtime", new JValue(true), true),
            VarAssign("td:ocr", "写OCR到运行时", "OcrText", null, "Runtime", new JValue("TEST"), true),
            VarAssign("td:defect", "写缺陷数到运行时", "DefectCount", null, "Runtime", new JValue(0), true),
            CcgCsvRecord("td:csv", "CSV记录", DefaultCsvColumns()),
            CcgExcel("td:excel", "导出Excel报表", "CSV记录"),
            Upload("td:upload", "上报MES(默认不触发)"),
            Delay("td:delay", "数据延时", 100),
            IfStep("td:count", "数据结果计数", "Ok", G("td:var:dataCount"), "Ok", "System.Boolean",
                LG("DataOk"),
                IncVarSteps("td:pass", "通过项+1", "TestPassCount"),
                IncVarSteps("td:fail", "失败项+1", "TestFailCount")),
            ResultDisplay("td:display", "数据结果显示", new[]
            {
                ("r1", "和", "td:math", "数学计算 1+2", "Result"),
                ("r2", "Int", "td:toInt", "和转Int", "Result"),
                ("r3", "结果", "td:and", "数据与", "Result"),
            }, 1, "td:acq", "采集数据测试图"),
        };

        return Flow("Test_Data", "数据测试：变量/数学/类型转换/CSV/Excel/上报", 0, 96, 0, steps);
    }

    // ==================================================================
    //  测试流程辅助
    // ==================================================================
    static void Add(JArray target, params JObject[] steps)
    {
        foreach (var s in steps) target.Add(s);
    }

    static JArray IncVarSteps(string seed, string name, string globalVar)
        => new()
        {
            MathAdd(seed, name, globalVar),
            ConvertToInt(seed + ":int", name + "转Int", seed, name),
            VarAssign(seed + ":write", name + "写回", globalVar,
                L(seed + ":int", "Result", name + "转Int"), "Global", null),
        };

    static void AddIncVar(JArray target, string seed, string name, string globalVar)
    {
        foreach (var s in IncVarSteps(seed, name, globalVar)) target.Add(s);
    }

    static JObject CompareConst(string seed, string name, double a, double b, string type)
        => Action(seed, name, "逻辑判断", P_CMP, "数值比较", "\uf047", new JObject
        {
            ["A"] = a,
            ["B"] = b,
            ["Type"] = type,
            ["Tolerance"] = 1e-6,
        });

    static JObject CompareGlobal(string seed, string name, string varName, double b, string type)
        => Action(seed, name, "逻辑判断", P_CMP, "数值比较", "\uf047", new JObject
        {
            ["B"] = b,
            ["Type"] = type,
            ["Tolerance"] = 1e-6,
        }, new JObject
        {
            ["A"] = LG(varName),
        });

    static JObject CompareRuntime(string seed, string name, string varName, double b, string type)
        => Action(seed, name, "逻辑判断", P_CMP, "数值比较", "\uf047", new JObject
        {
            ["B"] = b,
            ["Type"] = type,
            ["Tolerance"] = 1e-6,
        }, new JObject
        {
            ["A"] = LR(varName),
        });

    static JObject MathAddRuntime(string seed, string name, string runtimeVar)
        => Action(seed, name, "数据处理", P_MATH, "数学计算", "\uf1ec", new JObject
        {
            ["Operation"] = "Add",
            ["B"] = 1.0,
        }, new JObject
        {
            ["A"] = LR(runtimeVar),
        });

    static JObject LogicVars(string seed, string name, string aVar, string bVar)
        => Action(seed, name, "逻辑判断", P_LOGIC, "逻辑运算", "\uf0e7", new JObject
        {
            ["Operation"] = "And",
        }, new JObject
        {
            ["A"] = LG(aVar),
            ["B"] = LG(bVar),
        });

    static JObject ContinueStep(string seed, string name)
        => BuiltInStep("VisionMaster.Models.ContinueStep, VM.Core", "Continue", "BuiltIn_Continue", name, seed);

    static JObject BreakStep(string seed, string name)
        => BuiltInStep("VisionMaster.Models.BreakStep, VM.Core", "Break", "BuiltIn_Break", name, seed);

    static JObject ReturnStep(string seed, string name)
        => BuiltInStep("VisionMaster.Models.ReturnStep, VM.Core", "Return", "BuiltIn_Return", name, seed);

    static JObject BuiltInStep(string type, string pluginName, string pluginType, string name, string seed)
        => new()
        {
            ["$type"] = type,
            ["StepID"] = G(seed).ToString(),
            ["Icon"] = "\uf0e8",
            ["PluginName"] = pluginName,
            ["Category"] = "逻辑控制",
            ["PluginTypeName"] = pluginType,
            ["StepName"] = name,
            ["Description"] = pluginName,
            ["SortId"] = 0,
            ["IsDisEnable"] = false,
            ["State"] = 0,
            ["InputValues"] = new JObject(),
            ["LinkedSources"] = new JObject(),
            ["OutputPortDefinitions"] = new JArray(),
        };

    static JObject WhileStep(string seed, string name, string expr, string runtimeVar, JArray body)
        => new()
        {
            ["$type"] = "VisionMaster.Models.WhileStep, VM.Core",
            ["StepID"] = G(seed).ToString(),
            ["Icon"] = "\uf01e",
            ["PluginName"] = "While",
            ["Category"] = "逻辑控制",
            ["PluginTypeName"] = "BuiltIn_While",
            ["StepName"] = name,
            ["Description"] = "条件循环",
            ["SortId"] = 0,
            ["IsDisEnable"] = false,
            ["State"] = 0,
            ["InputValues"] = new JObject(),
            ["LinkedSources"] = new JObject(),
            ["OutputPortDefinitions"] = new JArray(),
            ["LocalVariables"] = new JArray(),
            ["RuntimeVariableRefs"] = new JArray
            {
                new JObject { ["Id"] = G("rtvar:" + runtimeVar).ToString(), ["Name"] = runtimeVar, ["DataTypeName"] = "System.Int32" },
            },
            ["Children"] = new JArray { Branch(5, "循环体", expr, body) },
        };

    static JObject LR(string runtimeVar)
        => new()
        {
            ["Kind"] = 3,
            ["TargetStepId"] = "D5A2E1B0-1111-4F8C-9B3F-2A6E0B6A9F01",
            ["TargetPortName"] = runtimeVar,
            ["TargetVariableId"] = Guid.Empty.ToString(),
            ["DisplayAddress"] = runtimeVar,
        };

    // ==================================================================
    //  SCADA 测试控制台
    // ==================================================================
    static JObject BuildScadaDocument()
    {
        var pages = new JArray
        {
            ScadaOverviewPage(),
            ScadaCommPage(),
            ScadaMotionPage(),
            ScadaAlarmPage(),
            ScadaConfirmPopup(),
        };

        return new JObject
        {
            ["SchemaVersion"] = 1,
            ["Pages"] = pages,
            ["Alarms"] = BuildScadaAlarms(),
            ["VariableEvents"] = BuildScadaVariableEvents(),
            ["StartupPageId"] = G("page:测试总览").ToString(),
            ["BasePages"] = new JArray(),
        };
    }

    static JObject ScadaPageObj(string name, double w, double h, JArray elements, bool popup = false)
    {
        var pageId = G("page:" + name).ToString();
        var layerId = G("layer:" + name).ToString();
        return new JObject
        {
            ["PageId"] = pageId,
            ["Name"] = name,
            ["Description"] = "",
            ["Width"] = w,
            ["Height"] = h,
            ["Background"] = "#FF0B1220",
            ["ShowGrid"] = true,
            ["GridSize"] = 10.0,
            ["SnapToGrid"] = true,
            ["BasePageIds"] = new JArray(),
            ["IsPopup"] = popup,
            ["PopupModal"] = true,
            ["PopupAnchor"] = 0,
            ["PopupOffsetX"] = 0.0,
            ["PopupOffsetY"] = 0.0,
            ["Elements"] = elements,
            ["Layers"] = new JArray
            {
                new JObject
                {
                    ["LayerId"] = layerId,
                    ["Name"] = "图层_1",
                    ["IsVisible"] = true,
                    ["IsLocked"] = false,
                },
            },
            ["EventHooks"] = new JArray(),
        };
    }

    static JObject El(string page, string name, string typeKey, double x, double y, double w, double h, int z,
        JObject props, JArray bindings = null, JArray hooks = null, JArray anims = null)
    {
        return new JObject
        {
            ["ElementId"] = G("el:" + page + ":" + name).ToString(),
            ["Name"] = name,
            ["LayerId"] = G("layer:" + page).ToString(),
            ["GroupId"] = Guid.Empty.ToString(),
            ["ParentId"] = Guid.Empty.ToString(),
            ["TemplateId"] = Guid.Empty.ToString(),
            ["TemplateIndex"] = -1,
            ["TemplateRevision"] = 0,
            ["TypeKey"] = typeKey,
            ["X"] = x,
            ["Y"] = y,
            ["Width"] = w,
            ["Height"] = h,
            ["Rotation"] = 0.0,
            ["ZIndex"] = z,
            ["IsLocked"] = false,
            ["Properties"] = props ?? new JObject(),
            ["Bindings"] = bindings ?? new JArray(),
            ["EventHooks"] = hooks ?? new JArray(),
            ["Animations"] = anims ?? new JArray(),
        };
    }

    static JObject Bind(string target, string varName, string format = null)
    {
        var id = varName.StartsWith("System.Motion.", StringComparison.Ordinal)
            ? SysVarIdFromName(varName)
            : G("var:" + varName);
        return new JObject
        {
            ["TargetProperty"] = target,
            ["VariableId"] = id.ToString(),
            ["VariableName"] = varName,
            ["ArrayIndex"] = -1,
            ["IsEnabled"] = true,
            ["DisplayFormat"] = format,
        };
    }

    static Guid SysVarIdFromName(string fullName)
    {
        // System.Motion.{轴名}.{字段}
        var parts = fullName.Split('.');
        var axisName = parts.Length >= 3 ? parts[2] : "X";
        var field = parts.Length >= 4 ? parts[3] : "Position";
        return SysVarId(G("axis:" + axisName), field);
    }

    static Guid SysVarId(Guid axisId, string field)
    {
        using var sha1 = SHA1.Create();
        var hash = sha1.ComputeHash(Encoding.UTF8.GetBytes($"motion-var|{axisId:N}|{field}"));
        return new Guid(hash.Take(16).ToArray());
    }

    static JObject ScadaAct(int type, string text = null, string varName = null, string value = null,
        string pageName = null, string flowName = null, string axisName = null,
        double? targetMm = null, double? velocity = null)
    {
        return new JObject
        {
            ["Type"] = type,
            ["Text"] = text,
            ["VariableId"] = varName == null ? Guid.Empty.ToString() : G("var:" + varName).ToString(),
            ["VariableName"] = varName,
            ["ArrayIndex"] = -1,
            ["Value"] = value,
            ["TargetPageId"] = pageName == null ? Guid.Empty.ToString() : G("page:" + pageName).ToString(),
            ["TargetPageName"] = pageName,
            ["FlowId"] = flowName == null ? null : G("flow:" + flowName).ToString("N"),
            ["FlowName"] = flowName,
            ["AxisId"] = axisName == null ? Guid.Empty.ToString() : G("axis:" + axisName).ToString(),
            ["AxisName"] = axisName,
            ["TargetMm"] = targetMm,
            ["VelocityMmPerS"] = velocity,
            ["OnlyIfPreviousSucceeded"] = false,
        };
    }

    static JObject Hook(int eventType, JArray actions, bool confirm = false)
        => new()
        {
            ["Event"] = eventType,
            ["RequireConfirmation"] = confirm,
            ["ConfirmationMessage"] = null,
            ["Actions"] = actions,
        };

    static JObject Anim(string varName, string low, string high, params (string Low, string High, string Fill, bool Flash)[] states)
    {
        var arr = new JArray();
        foreach (var s in states)
        {
            arr.Add(new JObject
            {
                ["ValueLow"] = s.Low,
                ["ValueHigh"] = s.High,
                ["Foreground"] = null,
                ["Fill"] = s.Fill,
                ["IsFlashing"] = s.Flash,
            });
        }

        return new JObject
        {
            ["Type"] = 1,
            ["IsEnabled"] = true,
            ["VariableId"] = G("var:" + varName).ToString(),
            ["VariableName"] = varName,
            ["ArrayIndex"] = -1,
            ["RangeLow"] = low,
            ["RangeHigh"] = high,
            ["EndX"] = 0.0,
            ["EndY"] = 0.0,
            ["VisibleInRange"] = true,
            ["States"] = arr,
        };
    }

    static JObject ScadaAlarm(string name, string varName, int kind, double threshold, int severity,
        string message, string group, string cause, string remedy)
    {
        return new JObject
        {
            ["AlarmId"] = G("alarm:" + name).ToString(),
            ["Name"] = name,
            ["VariableId"] = G("var:" + varName).ToString(),
            ["VariableName"] = varName,
            ["ArrayIndex"] = -1,
            ["Kind"] = kind,
            ["Threshold"] = threshold,
            ["Severity"] = severity,
            ["Deadband"] = 0.0,
            ["DelaySeconds"] = 0.0,
            ["StaleSeconds"] = 5.0,
            ["IsEnabled"] = true,
            ["Message"] = message,
            ["AlarmGroup"] = group,
            ["Cause"] = cause,
            ["Remedy"] = remedy,
            ["ExtraInfo"] = null,
        };
    }

    static JArray BuildScadaAlarms()
        => new()
        {
            ScadaAlarm("通讯-触发变量断线", "PLC_Trigger", 6, 0, 2, "PLC 触发变量超过 5 秒未刷新", "通讯", "Modbus 503 连接中断或 PLC 停止轮询", "检查 PLC_Modbus 连接与寄存器 0"),
            ScadaAlarm("通讯-S7心跳丢失", "S7_Heartbeat", 5, 0, 1, "S7 心跳为假", "通讯", "S7 连接中断或 PLC 未置心跳", "检查 PLC_S7 连接与 M0.0"),
            ScadaAlarm("视觉-单件缺陷超限", "DefectCount", 0, 10, 1, "单件缺陷数超过 10", "视觉", "工件表面异常或阈值过松", "检查来料与 Blob 阈值"),
            ScadaAlarm("质量-不良数超限", "NGCount", 0, 5, 1, "累计不良数超过 5", "质量", "连续 NG 或工艺漂移", "查看 CSV 记录与图像"),
            ScadaAlarm("质量-良率过低", "YieldRate", 2, 90, 2, "良率低于 90%", "质量", "批量不良或检测误判", "核对检测参数与来料"),
            ScadaAlarm("系统-未就绪", "SystemReady", 5, 0, 2, "系统未就绪", "系统", "Home 未执行或运动卡未连接", "执行 Home 并检查运动卡"),
            ScadaAlarm("仪表-读数过高", "Meter_Value", 0, 80, 1, "仪表读数超过 80", "通讯", "工艺参数超限", "检查 504 仪表与工艺"),
        };

    static JArray BuildScadaVariableEvents()
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
                    Hook(5, new JArray { ScadaAct(1, "本轮判定 OK") }),
                    Hook(6, new JArray { ScadaAct(1, "本轮判定 NG") }),
                },
            },
            new JObject
            {
                ["VariableId"] = G("var:StationOk").ToString(),
                ["VariableName"] = "StationOk",
                ["UpperLimit"] = null,
                ["LowerLimit"] = null,
                ["IsEnabled"] = true,
                ["EventHooks"] = new JArray
                {
                    Hook(6, new JArray { ScadaAct(1, "工位综合判定 NG") }),
                },
            },
            new JObject
            {
                ["VariableId"] = G("var:TestFailCount").ToString(),
                ["VariableName"] = "TestFailCount",
                ["UpperLimit"] = 0,
                ["LowerLimit"] = null,
                ["IsEnabled"] = true,
                ["EventHooks"] = new JArray
                {
                    Hook(7, new JArray { ScadaAct(1, "有测试项失败，请查看测试摘要") }),
                },
            },
        };

    static JObject ScadaOverviewPage()
    {
        var page = "测试总览";
        var els = new JArray();
        AddCommonHeader(els, page, "测试员控制台 · 逐个运行测试项");

        els.Add(El(page, "KPI", "Hmi.KpiBoard", 60, 180, 860, 200, 10, new JObject
        {
            ["Columns"] = "4",
            ["Label1"] = "良品", ["Value1"] = "0", ["Unit1"] = "pcs",
            ["Label2"] = "不良", ["Value2"] = "0", ["Unit2"] = "pcs",
            ["Label3"] = "缺陷", ["Value3"] = "0", ["Unit3"] = "pcs",
            ["Label4"] = "良率", ["Value4"] = "0", ["Unit4"] = "%",
        }, new JArray
        {
            Bind("Value1", "OKCount"),
            Bind("Value2", "NGCount"),
            Bind("Value3", "DefectCount"),
            Bind("Value4", "YieldRate"),
        }));

        els.Add(Indicator(page, "系统就绪", 60, 420, "SystemReady", "系统就绪"));
        els.Add(Indicator(page, "通讯", 140, 420, "CommOk", "通讯"));
        els.Add(Indicator(page, "运动", 220, 420, "MotionOk", "运动"));
        els.Add(Indicator(page, "视觉", 300, 420, "VisionOk", "视觉"));
        els.Add(Indicator(page, "逻辑", 380, 420, "LogicOk", "逻辑"));
        els.Add(Indicator(page, "数据", 460, 420, "DataOk", "数据"));

        els.Add(IOField(page, "通过项", 60, 530, "TestPassCount", "通过项", "0"));
        els.Add(IOField(page, "失败项", 300, 530, "TestFailCount", "失败项", "0"));
        els.Add(TextField(page, "测试摘要", 60, 580, 600, "TestSummary", "测试摘要"));

        els.Add(El(page, "良率表", "Hmi.Gauge", 940, 400, 180, 180, 10, new JObject
        {
            ["Value"] = "0", ["Minimum"] = "0", ["Maximum"] = "100",
            ["ValueFormat"] = "0.0", ["Unit"] = "%", ["ShowValue"] = "True",
            ["TrackColor"] = "#FF1E293B", ["ScaleColor"] = "#FF94A3B8",
            ["Fill"] = "#FFE03A2B", ["Stroke"] = "#FF475569", ["StrokeThickness"] = "1",
            ["Foreground"] = "#FFF8FAFC", ["FontSize"] = "14",
        }, new JArray { Bind("Value", "YieldRate") }));

        els.Add(El(page, "缺陷趋势", "Hmi.ChartTrend", 1160, 400, 680, 220, 10, new JObject
        {
            ["Value"] = "0", ["TimeWindow"] = "120", ["Minimum"] = "0", ["Maximum"] = "20",
            ["FixedRange"] = "True", ["CurveBrush"] = "#FF38BDF8", ["AxisBrush"] = "#FF475569",
            ["Title"] = "缺陷数趋势",
        }, new JArray { Bind("Value", "DefectCount") }));

        els.Add(El(page, "判定色带", "Hmi.Rectangle", 60, 650, 860, 18, 5, new JObject
        {
            ["Fill"] = "#FF334155", ["Stroke"] = "#00FFFFFF", ["StrokeThickness"] = "0", ["CornerRadius"] = "4",
        }, null, null, new JArray
        {
            Anim("CurrentResult", "0", "1",
                ("0", "0", "#FFEF4444", false),
                ("1", "1", "#FF22C55E", false)),
        }));

        els.Add(TestButton(page, "运行通讯测试", 60, 700, "Test_Comm"));
        els.Add(TestButton(page, "运行运动测试", 240, 700, "Test_Motion"));
        els.Add(TestButton(page, "运行视觉测试", 420, 700, "Test_Vision"));
        els.Add(TestButton(page, "运行逻辑测试", 600, 700, "Test_Logic"));
        els.Add(TestButton(page, "运行数据测试", 780, 700, "Test_Data"));

        els.Add(NavButton(page, "通讯页", 60, 760, "通讯测试"));
        els.Add(NavButton(page, "运动页", 220, 760, "运动测试"));
        els.Add(NavButton(page, "报警中心", 380, 760, "报警中心"));
        els.Add(PopupButton(page, "参数确认", 540, 760, "参数确认"));

        els.Add(El(page, "停止测试流程", "Hmi.Button", 700, 760, 180, 36, 20, ButtonProps("停止测试流程", "#FF475569", "#FF334155"), null,
            new JArray
            {
                Hook(2, new JArray
                {
                    ScadaAct(5, flowName: "Test_Comm"),
                    ScadaAct(5, flowName: "Test_Motion"),
                    ScadaAct(5, flowName: "Test_Vision"),
                    ScadaAct(5, flowName: "Test_Logic"),
                    ScadaAct(5, flowName: "Test_Data"),
                }),
            }));

        return ScadaPageObj(page, 1920, 1080, els);
    }

    static JObject ScadaCommPage()
    {
        var page = "通讯测试";
        var els = new JArray();
        AddCommonHeader(els, page, "三设备读写 · 只读区/不存在变量负例");

        els.Add(El(page, "说明", "Hmi.Text", 60, 180, 1200, 24, 5, TextProps("读三设备；写只读区/不存在变量应被拒绝", 14, "#FF94A3B8")));
        els.Add(Lamp(page, "PLC触发", 80, 230, "PLC_Trigger", "PLC触发"));
        els.Add(Lamp(page, "PLC确认", 150, 230, "PLC_Ack", "PLC确认"));
        els.Add(Indicator(page, "PLC结果", 220, 230, "PLC_Result", "PLC结果"));
        els.Add(IOField(page, "PLC良品", 80, 330, "PLC_OKCount", "PLC良品", "0"));
        els.Add(IOField(page, "仪表值", 80, 380, "Meter_Value", "仪表值", "F2"));
        els.Add(Indicator(page, "S7心跳", 80, 440, "S7_Heartbeat", "S7心跳"));
        els.Add(Indicator(page, "S7就绪", 150, 440, "S7_Ready", "S7就绪"));
        els.Add(IOField(page, "S7结果", 80, 530, "S7_Result", "S7结果", "0"));

        els.Add(WriteButton(page, "写触发1", 400, 230, "PLC_Trigger", "1"));
        els.Add(WriteButton(page, "写确认1", 570, 230, "PLC_Ack", "1"));
        els.Add(WriteButton(page, "写结果True", 740, 230, "PLC_Result", "true"));
        els.Add(WriteButton(page, "写心跳True", 910, 230, "S7_Heartbeat", "true"));
        els.Add(WriteButton(page, "写S7结果7", 1080, 230, "S7_Result", "7"));
        els.Add(WriteButton(page, "写只读仪表(应失败)", 400, 290, "Meter_Value", "1", "#FFB91C1C"));
        els.Add(NavButton(page, "返回总览", 400, 350, "测试总览"));

        els.Add(El(page, "提示", "Hmi.Text", 400, 410, 900, 60, 5, TextProps(
            "只读区写入应被拒绝；失败原因见运行日志。\n写成功后可在总览/本页看到变量值刷新。", 13, "#FFF59E0B")));

        return ScadaPageObj(page, 1920, 1080, els);
    }

    static JObject ScadaMotionPage()
    {
        var page = "运动测试";
        var els = new JArray();
        AddCommonHeader(els, page, "VPLC532R · X/Y/Z · 轴控台 + 系统变量");

        var axisOptions = JsonConvert.SerializeObject(new[]
        {
            new { AxisId = G("axis:X").ToString(), Name = "X" },
            new { AxisId = G("axis:Y").ToString(), Name = "Y" },
            new { AxisId = G("axis:Z").ToString(), Name = "Z" },
        });

        els.Add(El(page, "轴控台", "Hmi.AxisConsole", 60, 180, 520, 560, 10, new JObject
        {
            ["AxisOptions"] = axisOptions,
            ["ShowCommands"] = "True",
            ["ShowPoints"] = "True",
            ["PointsRows"] = "8",
            ["ConfirmCommands"] = "True",
            ["JogVelocity"] = "5",
        }));

        els.Add(IOField(page, "X位置", 620, 200, "System.Motion.X.Position", "X位置", "F3"));
        els.Add(IOField(page, "X速度", 620, 250, "System.Motion.X.Velocity", "X速度", "F2"));
        els.Add(TextField(page, "X状态", 620, 300, 260, "System.Motion.X.StatusText", "X状态"));
        els.Add(Indicator(page, "X使能", 900, 200, "System.Motion.X.Enabled", "X使能"));
        els.Add(Indicator(page, "X到位", 980, 200, "System.Motion.X.InPosition", "X到位"));
        els.Add(Indicator(page, "X报警", 1060, 200, "System.Motion.X.Alarm", "X报警"));

        els.Add(MotionButton(page, "X使能", 620, 380, 7, "X"));
        els.Add(MotionButton(page, "X定位10mm", 760, 380, 9, "X", 10, 10));
        els.Add(MotionButton(page, "X停止", 930, 380, 12, "X"));
        els.Add(MotionButton(page, "X回零", 1070, 380, 11, "X"));
        els.Add(MotionButton(page, "X点动+", 1210, 380, 14, "X", 1, null));
        els.Add(MotionButton(page, "X点动停", 1350, 380, 15, "X"));
        els.Add(NavButton(page, "返回总览", 620, 450, "测试总览"));

        els.Add(El(page, "提示", "Hmi.Text", 620, 520, 900, 80, 5, TextProps(
            "VPLC532R 的 0~5 轴为 Step Encoder；若「回零」失败，见方案说明文档的运动卡章节。\n"
            + "方案加载后会自动连接 ZMotion_Virtual；若仍显示未连接，再到「系统 → 硬件设置 → 运动卡」手动连接。", 13, "#FFF59E0B")));

        return ScadaPageObj(page, 1920, 1080, els);
    }

    static JObject ScadaAlarmPage()
    {
        var page = "报警中心";
        var els = new JArray();
        AddCommonHeader(els, page, "报警列表 · 一键确认");

        els.Add(El(page, "报警列表", "Hmi.AlarmBanner", 60, 180, 1200, 420, 10, new JObject
        {
            ["MaxRows"] = "10",
            ["EmptyText"] = "当前无报警",
            ["NormalColor"] = "#FF16A34A",
            ["InfoColor"] = "#FF0EA5E9",
            ["WarningColor"] = "#FFF59E0B",
            ["CriticalColor"] = "#FFEF4444",
        }));

        els.Add(El(page, "确认全部", "Hmi.AckButton", 60, 630, 180, 40, 10, new JObject
        {
            ["Scope"] = "All",
            ["Text"] = "确认全部报警",
            ["Fill"] = "#FFEF4444",
            ["Stroke"] = "#FFB91C1C",
            ["StrokeThickness"] = "1",
            ["CornerRadius"] = "6",
            ["Foreground"] = "#FFFFFFFF",
            ["FontSize"] = "14",
            ["FontWeight"] = "SemiBold",
        }));

        els.Add(NavButton(page, "返回总览", 280, 630, "测试总览"));

        return ScadaPageObj(page, 1920, 1080, els);
    }

    static JObject ScadaConfirmPopup()
    {
        var page = "参数确认";
        var els = new JArray
        {
            El(page, "标题", "Hmi.Text", 40, 36, 480, 40, 10, TextProps("确认写入测试参数？", 22, "#FFF8FAFC", "Bold")),
            El(page, "说明", "Hmi.Text", 40, 100, 480, 70, 10, TextProps(
                "将把 PLC_Ack 置 1，并关闭本弹窗。\n用于验证组态弹窗与写变量动作。", 14, "#FF94A3B8")),
            El(page, "确认", "Hmi.Button", 60, 210, 180, 44, 20, ButtonProps("确认", "#FF16A34A", "#FF15803D"), null,
                new JArray
                {
                    Hook(2, new JArray
                    {
                        ScadaAct(2, varName: "PLC_Ack", value: "1"),
                        ScadaAct(20),
                    }),
                }),
            El(page, "取消", "Hmi.Button", 300, 210, 180, 44, 20, ButtonProps("取消", "#FF475569", "#FF334155"), null,
                new JArray
                {
                    Hook(2, new JArray { ScadaAct(20) }),
                }),
        };

        var obj = ScadaPageObj(page, 560, 300, els, popup: true);
        obj["Background"] = "#FF1E293B";
        return obj;
    }

    // ---------- 组态图元辅助 ----------
    static void AddCommonHeader(JArray els, string page, string subtitle)
    {
        els.Add(El(page, "标题栏", "Hmi.Rectangle", 0, 0, 1920, 80, 1, new JObject
        {
            ["Fill"] = "#FF111827", ["Stroke"] = "#FF1F2937", ["StrokeThickness"] = "0", ["CornerRadius"] = "0",
        }));
        els.Add(El(page, "标题", "Hmi.Text", 40, 14, 900, 48, 5, TextProps("综合测试台 · 测试员控制台", 28, "#FFF8FAFC", "Bold")));
        els.Add(El(page, "副标题", "Hmi.Text", 40, 84, 1200, 24, 5, TextProps(subtitle, 13, "#FF94A3B8")));
        els.Add(El(page, "时钟", "Hmi.Clock", 1520, 16, 360, 44, 5, new JObject
        {
            ["Text"] = "", ["Foreground"] = "#FFE2E8F0", ["FontSize"] = "22", ["FontWeight"] = "SemiBold",
            ["TextAlignment"] = "Right",
        }));
        els.Add(El(page, "报警条", "Hmi.AlarmBanner", 0, 112, 1920, 48, 6, new JObject
        {
            ["MaxRows"] = "3",
            ["EmptyText"] = "无报警",
            ["NormalColor"] = "#FF16A34A",
            ["InfoColor"] = "#FF0EA5E9",
            ["WarningColor"] = "#FFF59E0B",
            ["CriticalColor"] = "#FFEF4444",
        }));
    }

    static JObject TextProps(string text, double size, string color, string weight = "Normal")
        => new()
        {
            ["Text"] = text,
            ["Foreground"] = color,
            ["FontSize"] = size.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
            ["FontWeight"] = weight,
            ["TextAlignment"] = "Left",
            ["VerticalContentAlignment"] = "Center",
        };

    static JObject ButtonProps(string text, string fill, string stroke)
        => new()
        {
            ["Text"] = text,
            ["Fill"] = fill,
            ["Stroke"] = stroke,
            ["StrokeThickness"] = "1",
            ["CornerRadius"] = "6",
            ["Foreground"] = "#FFFFFFFF",
            ["FontSize"] = "14",
            ["FontWeight"] = "SemiBold",
        };

    static JObject Indicator(string page, string name, double x, double y, string varName, string label)
        => El(page, name, "Hmi.Indicator", x, y, 64, 72, 10, new JObject
        {
            ["IsOn"] = "False",
            ["OnColor"] = "#FF22C55E",
            ["OffColor"] = "#FF475569",
            ["Shape"] = "Circle",
            ["Text"] = label,
            ["Foreground"] = "#FFE2E8F0",
            ["FontSize"] = "12",
        }, new JArray { Bind("IsOn", varName) });

    static JObject Lamp(string page, string name, double x, double y, string varName, string label)
        => El(page, name, "Hmi.Lamp", x, y, 64, 72, 10, new JObject
        {
            ["State"] = "Off",
            ["OffColor"] = "#FF475569",
            ["OnColor"] = "#FF22C55E",
            ["WarningColor"] = "#FFF59E0B",
            ["AlarmColor"] = "#FFEF4444",
            ["Shape"] = "Circle",
            ["Text"] = label,
            ["Foreground"] = "#FFE2E8F0",
            ["FontSize"] = "12",
        }, new JArray { Bind("State", varName) });

    static JObject IOField(string page, string name, double x, double y, string varName, string label, string format)
        => El(page, name, "Hmi.IOField", x, y, 220, 30, 10, new JObject
        {
            ["Value"] = "0",
            ["ValueFormat"] = format,
            ["Unit"] = "",
            ["Mode"] = "Output",
            ["Text"] = label,
            ["Foreground"] = "#FFE6EEF7",
            ["FontSize"] = "14",
            ["TextAlignment"] = "Right",
            ["Fill"] = "#00FFFFFF",
            ["Stroke"] = "#FF7A7A7A",
            ["StrokeThickness"] = "1",
            ["CornerRadius"] = "2",
        }, new JArray { Bind("Value", varName) });

    static JObject TextField(string page, string name, double x, double y, double w, string varName, string label)
        => El(page, name, "Hmi.TextField", x, y, w, 32, 10, new JObject
        {
            ["Value"] = "",
            ["Placeholder"] = "暂无",
            ["MaxLength"] = "120",
            ["Mode"] = "Output",
            ["Text"] = label,
            ["Foreground"] = "#FFE6EEF7",
            ["FontSize"] = "14",
            ["TextAlignment"] = "Left",
            ["Fill"] = "#00FFFFFF",
            ["Stroke"] = "#FF7A7A7A",
            ["StrokeThickness"] = "1",
            ["CornerRadius"] = "2",
        }, new JArray { Bind("Value", varName) });

    static JObject TestButton(string page, string name, double x, double y, string flowName)
        => El(page, name, "Hmi.Button", x, y, 160, 40, 20, ButtonProps(name, "#FF0EA5E9", "#FF0369A1"), null,
            new JArray { Hook(2, new JArray { ScadaAct(6, flowName: flowName) }) });

    static JObject NavButton(string page, string name, double x, double y, string targetPage)
        => El(page, name, "Hmi.Button", x, y, 140, 36, 20, ButtonProps(name, "#FF475569", "#FF334155"), null,
            new JArray { Hook(2, new JArray { ScadaAct(3, pageName: targetPage) }) });

    static JObject PopupButton(string page, string name, double x, double y, string popupPage)
        => El(page, name, "Hmi.Button", x, y, 140, 36, 20, ButtonProps(name, "#FF7C3AED", "#FF6D28D9"), null,
            new JArray { Hook(2, new JArray { ScadaAct(19, pageName: popupPage) }) });

    static JObject WriteButton(string page, string name, double x, double y, string varName, string value, string fill = "#FF0EA5E9")
        => El(page, name, "Hmi.Button", x, y, 150, 36, 20, ButtonProps(name, fill, "#FF0369A1"), null,
            new JArray { Hook(2, new JArray { ScadaAct(2, varName: varName, value: value) }) });

    static JObject MotionButton(string page, string name, double x, double y, int actionType, string axisName,
        double? targetMm = null, double? velocity = null)
        => El(page, name, "Hmi.Button", x, y, 130, 36, 20, ButtonProps(name, "#FF0EA5E9", "#FF0369A1"), null,
            new JArray
            {
                Hook(2, new JArray { ScadaAct(actionType, axisName: axisName, targetMm: targetMm, velocity: velocity) }),
            });
}
