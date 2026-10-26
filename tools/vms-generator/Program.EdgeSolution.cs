using System;
using Newtonsoft.Json.Linq;

internal static partial class Program
{
    // ==================================================================
    //  边界测试流程（第二轮：更多异常/组合场景）
    // ==================================================================

    static JObject TestCommEdgeFlow()
    {
        var steps = new JArray
        {
            VarAssign("tce:init", "初始化通讯边界", "EdgeCommOk", null, "Global", new JValue(true)),
            Delay("tce:delay", "等连接", 300),

            // 类型不匹配：Int16 变量写入字符串
            CommWrite("tce:type", "写Int16为字符串(应失败)", "PLC_Trigger", new JValue("abc"), true),
            IfStep("tce:typeCheck", "类型错误判定", "!Ok", G("tce:var:typeOk"), "Ok", "System.Boolean",
                L("tce:type", "Success", "写Int16为字符串(应失败)"),
                new JArray { VarAssign("tce:typePass", "类型错误通过", "CommTypeOk", null, "Global", new JValue(true)) },
                new JArray { VarAssign("tce:typeFail", "类型错误异常", "CommTypeOk", null, "Global", new JValue(false)) }),

            // 数值越界：Int16 最大 32767
            CommWrite("tce:range", "写Int16超范围(应失败)", "PLC_Trigger", new JValue(40000), true),
            IfStep("tce:rangeCheck", "越界判定", "!Ok", G("tce:var:rangeOk"), "Ok", "System.Boolean",
                L("tce:range", "Success", "写Int16超范围(应失败)"),
                new JArray { VarAssign("tce:rangePass", "越界通过", "CommRangeOk", null, "Global", new JValue(true)) },
                new JArray { VarAssign("tce:rangeFail", "越界异常", "CommRangeOk", null, "Global", new JValue(false)) }),

            // 离线连接：PLC_Offline 的 AutoStart=false
            CommRead("tce:offline", "读离线变量(应失败)", "Offline_Value"),
            IfStep("tce:offlineCheck", "离线判定", "!Ok", G("tce:var:offlineOk"), "Ok", "System.Boolean",
                L("tce:offline", "Success", "读离线变量(应失败)"),
                new JArray { VarAssign("tce:offlinePass", "离线通过", "CommOfflineOk", null, "Global", new JValue(true)) },
                new JArray { VarAssign("tce:offlineFail", "离线异常", "CommOfflineOk", null, "Global", new JValue(false)) }),

            LogicVars("tce:and1", "通讯边界与1", "CommTypeOk", "CommRangeOk"),
            LogicVars("tce:offlineAnd", "离线与", "CommOfflineOk", "CommOfflineOk"),
            Logic("tce:and2", "通讯边界与2", "tce:and1", "通讯边界与1", "tce:offlineAnd", "离线与"),
            VarAssign("tce:set", "写通讯边界结果", "EdgeCommOk", L("tce:and2", "Result", "通讯边界与2"), "Global", null),
        };

        return Flow("Test_CommEdge", "通讯边界：类型不匹配/越界/离线连接", 0, 96, 0, steps);
    }

    static JObject TestMotionEdgeFlow()
    {
        var steps = new JArray
        {
            VarAssign("tme:init", "初始化运动边界", "EdgeMotionOk", null, "Global", new JValue(true)),

            MotionMove("tme:badAxis", "无效轴移动(应失败)", "Q", "Absolute", 0, 10),
            IfStep("tme:badAxisCheck", "无效轴移动判定", "!Ok", G("tme:var:badAxis"), "Ok", "System.Boolean",
                L("tme:badAxis", "Success", "无效轴移动(应失败)"),
                new JArray { VarAssign("tme:badAxisPass", "无效轴移动通过", "MotionBadAxisOk", null, "Global", new JValue(true)) },
                new JArray { VarAssign("tme:badAxisFail", "无效轴移动异常", "MotionBadAxisOk", null, "Global", new JValue(false)) }),

            MotionEnable("tme:badEnable", "无效轴使能(应失败)", "Q", true, 1000),
            IfStep("tme:badEnableCheck", "无效轴使能判定", "!Ok", G("tme:var:badEnable"), "Ok", "System.Boolean",
                L("tme:badEnable", "Success", "无效轴使能(应失败)"),
                new JArray { VarAssign("tme:badEnablePass", "无效轴使能通过", "MotionBadEnableOk", null, "Global", new JValue(true)) },
                new JArray { VarAssign("tme:badEnableFail", "无效轴使能异常", "MotionBadEnableOk", null, "Global", new JValue(false)) }),

            MotionIo("tme:ioRead", "读输入0", "ZMotion_Virtual", 0, false, "ReadInput"),
            IfStep("tme:ioReadCheck", "读输入判定", "Ok", G("tme:var:ioRead"), "Ok", "System.Boolean",
                L("tme:ioRead", "Success", "读输入0"),
                new JArray { VarAssign("tme:ioReadPass", "读输入通过", "MotionIoReadOk", null, "Global", new JValue(true)) },
                new JArray { VarAssign("tme:ioReadFail", "读输入异常", "MotionIoReadOk", null, "Global", new JValue(false)) }),

            MotionIo("tme:ioBad", "读无效输入(应失败)", "ZMotion_Virtual", 999, false, "ReadInput"),
            IfStep("tme:ioBadCheck", "无效输入判定", "!Ok", G("tme:var:ioBad"), "Ok", "System.Boolean",
                L("tme:ioBad", "Success", "读无效输入(应失败)"),
                new JArray { VarAssign("tme:ioBadPass", "无效输入通过", "MotionIoBadOk", null, "Global", new JValue(true)) },
                new JArray { VarAssign("tme:ioBadFail", "无效输入异常", "MotionIoBadOk", null, "Global", new JValue(false)) }),

            MotionStop("tme:badStop", "无效轴停止(应失败)", "Q", "Decelerate", false),
            IfStep("tme:badStopCheck", "无效轴停止判定", "!Ok", G("tme:var:badStop"), "Ok", "System.Boolean",
                L("tme:badStop", "Success", "无效轴停止(应失败)"),
                new JArray { VarAssign("tme:badStopPass", "无效轴停止通过", "MotionBadStopOk", null, "Global", new JValue(true)) },
                new JArray { VarAssign("tme:badStopFail", "无效轴停止异常", "MotionBadStopOk", null, "Global", new JValue(false)) }),

            MotionWait("tme:wait", "等待到位超时", "X", 200, 20),
            IfStep("tme:waitCheck", "等待超时判定", "!Ok", G("tme:var:waitTimeout"), "Ok", "System.Boolean",
                L("tme:wait", "Success", "等待到位超时"),
                new JArray { VarAssign("tme:waitPass", "等待超时通过", "MotionWaitTimeoutOk", null, "Global", new JValue(true)) },
                new JArray { VarAssign("tme:waitFail", "等待超时异常", "MotionWaitTimeoutOk", null, "Global", new JValue(false)) }),

            LogicVars("tme:and1", "运动边界与1", "MotionBadAxisOk", "MotionBadEnableOk"),
            LogicVars("tme:and2", "运动边界与2", "MotionIoReadOk", "MotionIoBadOk"),
            LogicVars("tme:and3", "运动边界与3", "MotionBadStopOk", "MotionWaitTimeoutOk"),
            Logic("tme:and4", "运动边界与4", "tme:and1", "运动边界与1", "tme:and2", "运动边界与2"),
            Logic("tme:and5", "运动边界与5", "tme:and4", "运动边界与4", "tme:and3", "运动边界与3"),
            VarAssign("tme:set", "写运动边界结果", "EdgeMotionOk", L("tme:and5", "Result", "运动边界与5"), "Global", null),
        };

        return Flow("Test_MotionEdge", "运动边界：无效轴/无效IO/等待超时", 0, 96, 0, steps);
    }

    static JObject TestVisionEdgeFlow()
    {
        var steps = new JArray
        {
            VarAssign("tve:init", "初始化视觉边界", "EdgeVisionOk", null, "Global", new JValue(true)),

            // 目录当图片：读取失败
            ImgFile("tve:corrupt", "读目录当图片(应失败)", @"Image", 0),
            IfStep("tve:corruptCheck", "损坏图判定", "!Ok", G("tve:var:corruptOk"), "Ok", "System.Boolean",
                L("tve:corrupt", "Success", "读目录当图片(应失败)"),
                new JArray { VarAssign("tve:corruptPass", "损坏图通过", "VisionCorruptOk", null, "Global", new JValue(true)) },
                new JArray { VarAssign("tve:corruptFail", "损坏图异常", "VisionCorruptOk", null, "Global", new JValue(false)) }),

            // 卡尺期望值故意写错：实测 40，期望 100
            ImgFile("tve:calAcq", "采集宽度图", IMG_CALIPER_1, 0),
            Caliper("tve:badCal", "卡尺期望错误(应NG)", "tve:calAcq", "采集宽度图", "Width",
                new[] { Reg("宽度区", "Rectangle", 240, 300, 0, 80, 100) }, 5, 30, 100, 0.5, -0.5, 1),
            IfStep("tve:badCalCheck", "卡尺NG判定", "!Ok", G("tve:var:badCal"), "Ok", "System.Boolean",
                L("tve:badCal", "IsOk", "卡尺期望错误(应NG)"),
                new JArray { VarAssign("tve:badCalPass", "卡尺NG通过", "VisionCaliperNegOk", null, "Global", new JValue(true)) },
                new JArray { VarAssign("tve:badCalFail", "卡尺NG异常", "VisionCaliperNegOk", null, "Global", new JValue(false)) }),

            // 二维码用 Data Matrix 码制读：应读不到
            ImgFile("tve:qr", "采集二维码图", IMG_QR, 0),
            Action("tve:codeBad", "错误码制(应0)", "图像处理", P_CODE, "二维码读取", "\uf029", new JObject
            {
                ["Symbology"] = "Data Matrix ECC 200",
                ["TimeoutMs"] = 500,
                ["Polarity"] = "any",
            }, new JObject { ["Image"] = L("tve:qr", "Image", "采集二维码图") }),
            Compare("tve:codeCmp", "错误码制=0", "tve:codeBad", "错误码制(应0)", "Count", 0, "Equal"),
            IfStep("tve:codeCheck", "错误码制判定", "Ok", G("tve:var:codeOk"), "Ok", "System.Boolean",
                L("tve:codeCmp", "Result", "错误码制=0"),
                new JArray { VarAssign("tve:codePass", "错误码制通过", "VisionCodeWrongOk", null, "Global", new JValue(true)) },
                new JArray { VarAssign("tve:codeFail", "错误码制异常", "VisionCodeWrongOk", null, "Global", new JValue(false)) }),

            LogicVars("tve:and1", "视觉边界与1", "VisionCorruptOk", "VisionCaliperNegOk"),
            LogicVars("tve:codeAnd", "错误码制与", "VisionCodeWrongOk", "VisionCodeWrongOk"),
            Logic("tve:and2", "视觉边界与2", "tve:and1", "视觉边界与1", "tve:codeAnd", "错误码制与"),
            VarAssign("tve:set", "写视觉边界结果", "EdgeVisionOk", L("tve:and2", "Result", "视觉边界与2"), "Global", null),
        };

        return Flow("Test_VisionEdge", "视觉边界：坏图/卡尺NG/错误码制", 0, 96, 0, steps);
    }

    static JObject TestFlowEdgeFlow()
    {
        var caseStep = CaseStep("tfe:case", "Case无默认", "CaseNum", new JArray
        {
            Branch(4, "1", "1", new JArray { VarAssign("tfe:case1", "Case1", "CaseOk", null, "Global", new JValue(false)) }),
            Branch(4, "2", "2", new JArray { VarAssign("tfe:case2", "Case2", "CaseOk", null, "Global", new JValue(false)) }),
        }, "System.Int32");

        var innerBody = new JArray
        {
            MathAddRuntime("tfe:innerAdd", "Inner+1", "Inner"),
            VarAssign("tfe:innerWrite", "写Inner", "Inner", L("tfe:innerAdd", "Result", "Inner+1"), "Runtime", null, true),
        };
        var outerBody = new JArray
        {
            MathAddRuntime("tfe:outerAdd", "Outer+1", "Outer"),
            VarAssign("tfe:outerWrite", "写Outer", "Outer", L("tfe:outerAdd", "Result", "Outer+1"), "Runtime", null, true),
            ForLoop("tfe:forInner", "嵌套内层", 3, innerBody, "tfe:forInner"),
        };
        var whileStep = WhileStep("tfe:while", "嵌套外层", "Outer < 2", "Outer", outerBody);

        var zeroBody = new JArray
        {
            MathAddRuntime("tfe:zeroAdd", "Zero+1", "ZeroCount"),
            VarAssign("tfe:zeroWrite", "写Zero", "ZeroCount", L("tfe:zeroAdd", "Result", "Zero+1"), "Runtime", null, true),
        };
        var forZero = ForLoop("tfe:forZero", "For零次", 0, zeroBody, "tfe:forZero");

        var parallel = Parallel("tfe:parallel", "并行失败测试", 1, 1, 3000, new JArray
        {
            Branch(0, "分支A", "", new JArray
            {
                Action("tfe:parallelConv", "并行类型转换失败(应失败)", "数据处理", P_CONVERT, "类型转换", "\uf0ec", new JObject
                {
                    ["Target"] = "Int32",
                    ["Format"] = "yyyy-MM-dd HH:mm:ss",
                    ["Input"] = "abc",
                }),
            }),
            Branch(0, "分支B", "", new JArray
            {
                Delay("tfe:parallelDelay", "并行B延时", 300),
                VarAssign("tfe:parallelB", "写并行B", "ParallelB", null, "Global", new JValue(true)),
            }),
        });

        var steps = new JArray
        {
            VarAssign("tfe:init", "初始化逻辑边界", "EdgeFlowOk", null, "Global", new JValue(true)),
            VarAssign("tfe:initCase", "初始化Case", "CaseOk", null, "Global", new JValue(false)),
            VarAssign("tfe:initB", "初始化并行B", "ParallelB", null, "Global", new JValue(false)),
            VarAssign("tfe:initOuter", "初始化Outer", "Outer", null, "Runtime", new JValue(0), true),
            VarAssign("tfe:initInner", "初始化Inner", "Inner", null, "Runtime", new JValue(0), true),
            VarAssign("tfe:initZero", "初始化Zero", "ZeroCount", null, "Runtime", new JValue(0), true),

            VarAssign("tfe:caseNum", "写Case判据", "CaseNum", null, "Runtime", new JValue(9), true),
            caseStep,
            LogicVars("tfe:caseCheck", "Case未命中", "CaseOk", "CaseOk"),
            IfStep("tfe:caseIf", "Case未命中判定", "!Ok", G("tfe:var:caseOk"), "Ok", "System.Boolean",
                L("tfe:caseCheck", "Result", "Case未命中"),
                new JArray { VarAssign("tfe:casePass", "Case未命中通过", "FlowCaseDefaultOk", null, "Global", new JValue(true)) },
                new JArray { VarAssign("tfe:caseFail", "Case未命中异常", "FlowCaseDefaultOk", null, "Global", new JValue(false)) }),

            whileStep,
            CompareRuntime("tfe:outerCmp", "Outer=2", "Outer", 2, "Equal"),
            CompareRuntime("tfe:innerCmp", "Inner=6", "Inner", 6, "Equal"),
            Logic("tfe:nestedAnd", "嵌套与", "tfe:outerCmp", "Outer=2", "tfe:innerCmp", "Inner=6"),
            IfStep("tfe:nestedIf", "嵌套判定", "Ok", G("tfe:var:nestedOk"), "Ok", "System.Boolean",
                L("tfe:nestedAnd", "Result", "嵌套与"),
                new JArray { VarAssign("tfe:nestedPass", "嵌套通过", "FlowNestedOk", null, "Global", new JValue(true)) },
                new JArray { VarAssign("tfe:nestedFail", "嵌套异常", "FlowNestedOk", null, "Global", new JValue(false)) }),

            forZero,
            CompareRuntime("tfe:zeroCmp", "ZeroCount=0", "ZeroCount", 0, "Equal"),
            IfStep("tfe:zeroIf", "For零次判定", "Ok", G("tfe:var:zeroOk"), "Ok", "System.Boolean",
                L("tfe:zeroCmp", "Result", "ZeroCount=0"),
                new JArray { VarAssign("tfe:zeroPass", "For零次通过", "FlowForZeroOk", null, "Global", new JValue(true)) },
                new JArray { VarAssign("tfe:zeroFail", "For零次异常", "FlowForZeroOk", null, "Global", new JValue(false)) }),

            parallel,
            LogicVars("tfe:parallelBCheck", "并行B未执行", "ParallelB", "ParallelB"),
            IfStep("tfe:parallelIf", "并行B判定", "!Ok", G("tfe:var:parallelB"), "Ok", "System.Boolean",
                L("tfe:parallelBCheck", "Result", "并行B未执行"),
                new JArray { VarAssign("tfe:parallelPass", "并行B通过", "FlowParallelFailOk", null, "Global", new JValue(true)) },
                new JArray { VarAssign("tfe:parallelFail", "并行B异常", "FlowParallelFailOk", null, "Global", new JValue(false)) }),

            LogicVars("tfe:and1", "逻辑边界与1", "FlowCaseDefaultOk", "FlowNestedOk"),
            LogicVars("tfe:and2", "逻辑边界与2", "FlowForZeroOk", "FlowParallelFailOk"),
            Logic("tfe:and3", "逻辑边界与3", "tfe:and1", "逻辑边界与1", "tfe:and2", "逻辑边界与2"),
            VarAssign("tfe:set", "写逻辑边界结果", "EdgeFlowOk", L("tfe:and3", "Result", "逻辑边界与3"), "Global", null),
        };

        return Flow("Test_FlowEdge", "逻辑边界：Case无默认/嵌套循环/For零次/并行失败", 0, 96, 0, steps);
    }

    static JObject TestDataEdgeFlow()
    {
        var overflowMath = Action("tde:mul", "乘法溢出", "数据处理", P_MATH, "数学计算", "\uf1ec", new JObject
        {
            ["Operation"] = "Multiply",
            ["A"] = 1e20,
            ["B"] = 1e20,
        });

        var csvBad = CcgCsvRecord("tde:csvBad", "CSV坏路径(应失败)", DefaultCsvColumns());
        // 负样本要的是"写盘必失败"，但不能用盘符绝对路径（方案内不许出现）：
        // `?` 在 Windows 文件名里非法，展开后建目录/写文件必抛。
        csvBad["InputValues"]["DirectoryTemplate"] = @"History\{StepName}\{yyyy-MM}\bad?path\";
        csvBad["InputValues"]["BlockOnFailure"] = true;

        var uploadBad = Upload("tde:uploadBad", "上报不可达(应失败)");
        uploadBad["InputValues"]["Url"] = "http://127.0.0.1:59998/api/inspect";
        uploadBad["InputValues"]["TimeoutMs"] = 500;
        uploadBad["InputValues"]["AsyncMode"] = false;
        uploadBad["InputValues"]["BlockOnFailure"] = true;
        uploadBad["InputValues"]["上报触发"] = true;

        var steps = new JArray
        {
            VarAssign("tde:init", "初始化数据边界", "EdgeDataOk", null, "Global", new JValue(true)),

            overflowMath,
            ConvertToInt("tde:toInt", "溢出转Int", "tde:mul", "乘法溢出"),
            IfStep("tde:overflowCheck", "溢出判定", "!Ok", G("tde:var:overflowOk"), "Ok", "System.Boolean",
                L("tde:toInt", "Success", "溢出转Int"),
                new JArray { VarAssign("tde:overflowPass", "溢出通过", "DataOverflowOk", null, "Global", new JValue(true)) },
                new JArray { VarAssign("tde:overflowFail", "溢出异常", "DataOverflowOk", null, "Global", new JValue(false)) }),

            csvBad,
            IfStep("tde:csvCheck", "CSV坏路径判定", "!Ok", G("tde:var:csvOk"), "Ok", "System.Boolean",
                L("tde:csvBad", "Success", "CSV坏路径(应失败)"),
                new JArray { VarAssign("tde:csvPass", "CSV坏路径通过", "DataCsvBadOk", null, "Global", new JValue(true)) },
                new JArray { VarAssign("tde:csvFail", "CSV坏路径异常", "DataCsvBadOk", null, "Global", new JValue(false)) }),

            uploadBad,
            IfStep("tde:uploadCheck", "上报不可达判定", "!Ok", G("tde:var:uploadOk"), "Ok", "System.Boolean",
                L("tde:uploadBad", "Success", "上报不可达(应失败)"),
                new JArray { VarAssign("tde:uploadPass", "上报不可达通过", "DataUploadBadOk", null, "Global", new JValue(true)) },
                new JArray { VarAssign("tde:uploadFail", "上报不可达异常", "DataUploadBadOk", null, "Global", new JValue(false)) }),

            LogicVars("tde:and1", "数据边界与1", "DataOverflowOk", "DataCsvBadOk"),
            LogicVars("tde:uploadAnd", "上报与", "DataUploadBadOk", "DataUploadBadOk"),
            Logic("tde:and2", "数据边界与2", "tde:and1", "数据边界与1", "tde:uploadAnd", "上报与"),
            VarAssign("tde:set", "写数据边界结果", "EdgeDataOk", L("tde:and2", "Result", "数据边界与2"), "Global", null),
        };

        return Flow("Test_DataEdge", "数据边界：溢出/CSV坏路径/上报不可达", 0, 96, 0, steps);
    }

    // ==================================================================
    //  测试结果汇总：把边界测试的每个判定写进 CSV，供报告取证
    // ==================================================================
    static JObject TestReportFlow()
    {
        var csv = CcgCsvRecord("tr:csv", "测试结果汇总", DefaultCsvColumns());
        csv["InputValues"]["FlushEveryRow"] = true;
        csv["InputValues"]["Columns"] = new JArray
        {
            Col("时间", "BuiltIn", "", "Timestamp"),
            Col("Comm", "Variable", "Rpt_Comm", "Timestamp"),
            Col("Motion", "Variable", "Rpt_Motion", "Timestamp"),
            Col("Vision", "Variable", "Rpt_Vision", "Timestamp"),
            Col("Logic", "Variable", "Rpt_Logic", "Timestamp"),
            Col("Data", "Variable", "Rpt_Data", "Timestamp"),
        };
        csv["InputValues"]["ResultColumn"] = "Data";

        var steps = new JArray
        {
            // CSV 的 Variable 列只读运行期变量；先把全局结果复制到运行期变量，
            // 否则列会是空的（这一条本身就是本轮测出来的边界）。
            VarAssign("tr:rComm", "复制通讯结果", "Rpt_Comm", LG("EdgeCommOk"), "Runtime", null, true),
            VarAssign("tr:rMotion", "复制运动结果", "Rpt_Motion", LG("EdgeMotionOk"), "Runtime", null, true),
            VarAssign("tr:rVision", "复制视觉结果", "Rpt_Vision", LG("EdgeVisionOk"), "Runtime", null, true),
            VarAssign("tr:rLogic", "复制逻辑结果", "Rpt_Logic", LG("EdgeFlowOk"), "Runtime", null, true),
            VarAssign("tr:rData", "复制数据结果", "Rpt_Data", LG("EdgeDataOk"), "Runtime", null, true),
            csv,
            Delay("tr:flush", "等待落盘", 1500),
        };

        return Flow("Test_Report", "测试结果汇总：写 CSV 供报告取证", 0, 96, 0, steps);
    }

    // ==================================================================
    //  测试台专用通讯：多加一条“永不连接”的离线连接
    // ==================================================================
    static JArray BuildTestComms()
    {
        var list = BuildComms();
        var offline = Comm("PLC_Offline", 0, "ModbusTcp", new JObject
        {
            ["$type"] = "VisionMaster.Communications.ModbusTcpConfig, VM.Core",
            ["ByteoRDER"] = 0,
            ["IpAddress"] = "127.0.0.1",
            ["Port"] = 59999,
            ["EnableKeepAlive"] = false,
            ["KeepAliveIntervalMs"] = 30000,
            ["Type"] = 0,
            ["TimeoutMs"] = 500,
            ["RetryCount"] = 0,
            ["RetryIntervalMs"] = 100,
            ["ReadTimeoutMs"] = 500,
        }, 1000, "离线测试连接（端口 59999，不自动启动）");
        offline["AutoStart"] = false;
        list.Add(offline);
        return list;
    }
}
