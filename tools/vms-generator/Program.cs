using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

internal static partial class Program
{
    // ---------- 插件类型名（与 Modules\ 下 DLL 的 AssemblyQualifiedName 逐字一致） ----------
    const string S = ", Version=1.0.0.0, Culture=neutral, PublicKeyToken=null";
    const string P_IMAGE = "Plugin.ImageAcquisition.ImageAcquisitionPlugin, Plugin.ImageAcquisition" + S;
    const string P_PRE = "Plugin.PreProcessing.PreProcessingPlugin, Plugin.PreProcessing" + S;
    const string P_ROI = "Plugin.CreateRoi.CreateRoiPlugin, Plugin.CreateRoi" + S;
    const string P_GF = "Plugin.GeometryFinder.GeometryFinderPlugin, Plugin.GeometryFinder" + S;
    const string P_GR = "Plugin.GeometryRelation.GeometryRelationPlugin, Plugin.GeometryRelation" + S;
    const string P_POSE = "Plugin.PoseTransform.PoseTransformPlugin, Plugin.PoseTransform" + S;
    const string P_BLOB = "Plugin.BlobDetect.BlobDetectPlugin, Plugin.BlobDetect" + S;
    const string P_OCR = "Plugin.Ocr.OcrPlugin, Plugin.Ocr" + S;
    const string P_CODE = "Plugin.CodeReader.CodeReaderPlugin, Plugin.CodeReader" + S;
    const string P_COLORCHK = "Plugin.ColorCheck.ColorCheckPlugin, Plugin.ColorCheck" + S;
    const string P_COLORREG = "Plugin.ColorRegion.RegionColorPlugin, Plugin.ColorRegion" + S;
    const string P_CALIPER = "Plugin.CaliperMeasure.CaliperMeasurePlugin, Plugin.CaliperMeasure" + S;
    const string P_BEAD = "Plugin.BeadInspect.BeadInspectPlugin, Plugin.BeadInspect" + S;
    const string P_DISPLAY = "Plugin.ResultDisplay.ResultDisplayPlugin, Plugin.ResultDisplay" + S;
    const string P_CSV = "Plugin.DataRecord.DataRecordPlugin, Plugin.DataRecord" + S;
    const string P_UPLOAD = "Plugin.ResultUpload.ResultUploadPlugin, Plugin.ResultUpload" + S;
    const string P_EXCEL = "Plugin.ExcelExport.ExcelExportPlugin, Plugin.ExcelExport" + S;
    const string P_VAR = "VisionMaster.Plugins.Util.VariableAssignmentPlugin, Plugin.Util" + S;
    const string P_MATH = "VisionMaster.Plugins.Util.MathCalculationPlugin, Plugin.Util" + S;
    const string P_LOGIC = "VisionMaster.Plugins.Util.LogicOperationPlugin, Plugin.Util" + S;
    const string P_CMP = "VisionMaster.Plugins.Util.ComparisonPlugin, Plugin.Util" + S;
    const string P_CONVERT = "VisionMaster.Plugins.Util.DataConversionPlugin, Plugin.Util" + S;
    const string P_DELAY = "VisionMaster.Plugins.Util.DelayPlugin, Plugin.Util" + S;
    const string P_CREAD = "Plugin.Comm.CommReadNowPlugin, Plugin.Comm" + S;
    const string P_CWRITE = "Plugin.Comm.CommWriteNowPlugin, Plugin.Comm" + S;
    const string P_MEN = "Plugin.Motion.Steps.MotionEnablePlugin, Plugin.Motion.Steps" + S;
    const string P_MHOME = "Plugin.Motion.Steps.MotionHomePlugin, Plugin.Motion.Steps" + S;
    const string P_MIO = "Plugin.Motion.Steps.MotionIoPlugin, Plugin.Motion.Steps" + S;
    const string P_MMOVE = "Plugin.Motion.Steps.MotionMovePlugin, Plugin.Motion.Steps" + S;
    const string P_MSTOP = "Plugin.Motion.Steps.MotionStopPlugin, Plugin.Motion.Steps" + S;
    const string P_MWAIT = "Plugin.Motion.Steps.MotionWaitPlugin, Plugin.Motion.Steps" + S;
    const string P_MSTATUS = "Plugin.Motion.Steps.MotionReadStatusPlugin, Plugin.Motion.Steps" + S;
    const string P_RUNFLOW = "VisionMaster.Plugins.RunFlow.RunFlowPlugin, Plugin.RunFlow" + S;
    const string P_CSCRIPT = "Plugin.CSharpScript.CSharpScriptPlugin, Plugin.CSharpScript" + S;

    // ---------- 类型字符串（变量快照） ----------
    const string T_INT = "System.Int32, System.Private.CoreLib, Version=9.0.0.0, Culture=neutral, PublicKeyToken=7cec85d7bea7798e";
    const string T_SHORT = "System.Int16, System.Private.CoreLib, Version=9.0.0.0, Culture=neutral, PublicKeyToken=7cec85d7bea7798e";
    const string T_DBL = "System.Double, System.Private.CoreLib, Version=9.0.0.0, Culture=neutral, PublicKeyToken=7cec85d7bea7798e";
    const string T_BOOL = "System.Boolean, System.Private.CoreLib, Version=9.0.0.0, Culture=neutral, PublicKeyToken=7cec85d7bea7798e";
    const string T_STR = "System.String, System.Private.CoreLib, Version=9.0.0.0, Culture=neutral, PublicKeyToken=7cec85d7bea7798e";
    const string T_SGL = "System.Single, System.Private.CoreLib, Version=9.0.0.0, Culture=neutral, PublicKeyToken=7cec85d7bea7798e";

    const string DIR_BOARD = @"D:\C#\VM\Image\board";
    const string IMG_COLOR = @"D:\C#\VM\Image\颜色\合成颜色_8bit.png";
    const string IMG_BEAD = @"D:\C#\VM\Image\bead\adhesive_bead_01.png";
    const string IMG_BEAD_REF = @"D:\C#\VM\Image\bead\adhesive_bead_ref.png";
    const string IMG_QR = @"D:\C#\VM\Image\images\datacode\qrcode\qr_workpiece_01.png";
    const string DIR_CALIPER = @"D:\C#\VM\Image\卡尺测试";

    static void Main(string[] args)
    {
        // 压测方案生成（--stress）：只在显式传参时跑，无参行为与历史完全一致
        if (args.Length > 0 && string.Equals(args[0], "--stress", StringComparison.OrdinalIgnoreCase))
        {
            RunStress(args);
            return;
        }

        // 以下三套走「一个方案一个文件夹」布局（见 Program.CcgSolutionFolder.cs）：
        //   解决方案\<方案名>\<方案名>.vms + Image\ Alarms\ History\ Logs\
        // 图片一律用方案内相对路径 Image\xxx.png，生成时把样图复制进方案自己的 Image\。
        if (args.Length > 0 && string.Equals(args[0], "--ccg-bga", StringComparison.OrdinalIgnoreCase))
        {
            WriteFolderSolution(BuildCcgBgaInspectSolution(), "CCG-BGA焊球全检");
            return;
        }

        if (args.Length > 0 && string.Equals(args[0], "--ccg-pack", StringComparison.OrdinalIgnoreCase))
        {
            WriteFolderSolution(BuildCcgPackLineSolution(), "CCG-包装线全检追溯");
            return;
        }

        if (args.Length > 0 && string.Equals(args[0], "--ccg-measure", StringComparison.OrdinalIgnoreCase))
        {
            WriteFolderSolution(BuildCcgMeasureStationSolution(), "CCG-精密尺寸测量站");
            return;
        }

        WriteSolution(BuildSolution(), @"D:\C#\VM\解决方案\综合检测线.vms");
        WriteSolution(BuildTestSolution(), @"D:\C#\VM\解决方案\综合测试台.vms");
        WriteSolution(BuildAssemblySolution(), @"D:\C#\VM\解决方案\柔性装配线.vms");
        WriteSolution(BuildSortingSolution(), @"D:\C#\VM\解决方案\视觉分拣线.vms");
        WriteSolution(BuildGatewaySolution(), @"D:\C#\VM\解决方案\数据网关.vms");
        WriteSolution(BuildAutomationLineSolution(), @"D:\C#\VM\解决方案\自动化产线.vms");
    }

    static void WriteSolution(JObject solution, string outPath)
    {
        var json = JsonConvert.SerializeObject(solution, Formatting.Indented);
        File.WriteAllText(outPath, json, new UTF8Encoding(false));
        Console.WriteLine("WROTE " + outPath + " bytes=" + new FileInfo(outPath).Length);
    }

    // ==================================================================
    //  解决方案
    // ==================================================================
    static JObject BuildSolution()
    {
        var flows = new JArray
        {
            BuildHomeFlow(),
            BuildMainFlow(),
            BuildEndFlow(),
        };

        return new JObject
        {
            ["SolutionName"] = "综合检测线演示",
            ["Version"] = 1.0,
            ["Config"] = new JObject { ["ImageViewMode"] = 0 },
            ["Flows"] = flows,
            ["SolutionId"] = G("solution").ToString(),
            ["CommunicationConfigs"] = BuildComms(),
            ["IoBuses"] = new JArray(),
            ["IoDevices"] = new JArray(),
            ["CameraConfigs"] = new JArray(),
            ["MotionCards"] = BuildMotionCards(),
            ["CamTables"] = new JArray(),
            ["VariableSnapshots"] = BuildVariables(),
            ["VariableGroups"] = new JArray(),
            ["WatchItems"] = new JArray(),
            ["Scada"] = new JObject
            {
                ["SchemaVersion"] = 1,
                ["Pages"] = new JArray(),
                ["Alarms"] = new JArray(),
                ["VariableEvents"] = new JArray(),
                ["StartupPageId"] = Guid.Empty.ToString(),
                ["BasePages"] = new JArray(),
            },
        };
    }

    // ==================================================================
    //  Home：轴使能 → 回零 → 待机位 → IO/PLC 就绪
    // ==================================================================
    static JObject BuildHomeFlow()
    {
        var steps = new JArray
        {
            MotionEnable("home:enX", "轴使能X", "X", true, 10000),
            MotionEnable("home:enY", "轴使能Y", "Y", true, 10000),
            MotionHome("home:homeX", "回零X", "X", "NegativeLimitIndex", 60000),
            MotionHome("home:homeY", "回零Y", "Y", "NegativeLimitIndex", 60000),
            MotionReadStatus("home:statusX", "读X轴状态", "X"),
            MotionReadStatus("home:statusY", "读Y轴状态", "Y"),
            MotionMove("home:moveX", "X到待机位", "X", "Absolute", 10, 10),
            MotionWait("home:waitX", "等待X到位", "X", 30000, 20),
            MotionIo("home:io", "就绪指示灯", "ZMotion_Virtual", 0, true, "WriteOutput"),
            CommWrite("home:plc", "写S7心跳", "S7_Heartbeat", new JValue(true), true),
            VarAssign("home:sys", "置系统就绪", "SystemReady", null, "Global", new JValue(true)),
        };

        return Flow("Home", "回原 / 回零", 1, 0, 0, steps, enabled: false);
    }

    // ==================================================================
    //  Main：PLC 触发 → 5 轮板卡检测 → 计数 → 并行上报/记账 → 报表 → 工位演示
    // ==================================================================
    static JObject BuildMainFlow()
    {
        var triggerId = G("main:var:Trigger");
        var okId = G("main:var:Ok");

        // ---- For 循环体 ----
        var loopBody = new JArray
        {
            ImgFolder("main:acq", "采集板卡图", DIR_BOARD, "main:loop", "循环检测", 1),
            PreProcess("main:pre", "图像预处理", new JArray
            {
                Op("gauss", ("Size", "5")),
                Op("gray_closing", ("Width", "5"), ("Height", "5")),
                Op("emphasize", ("Width", "7"), ("Height", "7"), ("Factor", "1.2")),
            }, 0),
            CreateRoi("main:roi", "ROI圆区域", new JArray
            {
                Roi("圆检测区", "Rectangle", 380, 560, 0, 80, 80),
            }, 0),
            CreateRoi("main:roiSmall", "ROI小圆区", new JArray
            {
                Roi("小圆区", "Rectangle", 60, 545, 0, 45, 45),
            }, 0),
            GeometryFind("main:findCircle", "找圆", "Circle", "main:roi", "ROI圆区域", 1, 20, 40, 30, false, 0),
            GeometryFind("main:findSmall", "找小圆", "Circle", "main:roiSmall", "ROI小圆区", 1, 20, 40, 20, false, 0),
            GeometryRelation("main:rel", "两圆心距", "CircleRelation", "main:findCircle", "找圆", "main:findSmall", "找小圆", 0.05),
            PoseFollow("main:pose", "跟随ROI", "main:roi", "ROI圆区域", "main:findCircle", "找圆", 0),
            BlobDetect("main:blob", "表面缺陷检测", "main:pose", "跟随ROI", 0.05, 2),
            ImgFile("main:ocrAcq", "采集批号图", @"D:\C#\VM\Image\images\ocr\lot_number_01.png", 0),
            Ocr("main:ocr", "OCR识别", new double[] { 272, 320, 0, 170, 40 }, 1, "main:ocrAcq", "采集批号图", true),
            Compare("main:cmpBlob", "缺陷数比较", "main:blob", "表面缺陷检测", "DefectCount", 10, "LessThanOrEqual"),
            Compare("main:cmpOcr", "OCR文本比较", "main:ocr", "OCR识别", "Count", 0, "GreaterThan"),
            Logic("main:and", "综合判定", "main:cmpBlob", "缺陷数比较", "main:cmpOcr", "OCR文本比较"),
            VarAssign("main:setResult", "写当前结果", "CurrentResult", L("main:and", "Result", "综合判定"), "Runtime", null, true),
            VarAssign("main:setOcr", "写OCR文本", "OcrText", L("main:ocr", "Text", "OCR识别"), "Runtime", null, true),
            VarAssign("main:setDefect", "写缺陷数", "DefectCount", L("main:blob", "DefectCount", "表面缺陷检测"), "Runtime", null, true),
            ResultDisplay("main:display", "结果显示", new[]
            {
                ("r1", "缺陷数", "main:blob", "表面缺陷检测", "DefectCount"),
                ("r2", "OCR", "main:ocr", "OCR识别", "Text"),
                ("r3", "判定", "main:and", "综合判定", "Result"),
                ("r4", "两圆心距", "main:rel", "两圆心距", "Value"),
            }, 2, "main:blob", "表面缺陷检测", "DefectImage"),
            CommWrite("main:writeResult", "写PLC结果", "PLC_Result", L("main:and", "Result", "综合判定"), true),
            // 分拣：If 判定
            IfStep("main:sort", "分拣判断", "Ok", okId, "Ok", "System.Boolean",
                L("main:and", "Result", "综合判定"),
                new JArray
                {
                    Disable(MotionMove("main:movePick", "X到取料位", "X", "Absolute", 100, 20)),
                    Disable(MotionWait("main:waitPick", "等待取料位", "X", 30000, 20)),
                    Disable(MotionMove("main:moveBack", "X回检测位", "X", "Absolute", 0, 20)),
                    Disable(MotionWait("main:waitBack", "等待回位", "X", 30000, 20)),
                },
                new JArray
                {
                    Disable(MotionStop("main:stopNg", "NG停止", "X", "Decelerate", false)),
                    Disable(MotionIo("main:ioNg", "NG报警灯", "ZMotion_Virtual", 1, true, "WriteOutput")),
                }),
            CommRead("main:readAck", "读PLC确认", "PLC_Ack"),
            WaitStep("main:waitAck", "等待PLC确认", "Ack", "Ack == 1",
                L("main:readAck", "Value", "读PLC确认"), 1000, 1, "PLC 未在 1 秒内给出确认，继续执行"),
        };

        // ---- 工位步骤：颜色/卡尺/胶路/码读取直接并入主流程（不再用「调用流程」） ----
        var stationSteps = new JArray();
        foreach (var s in ColorSteps()) stationSteps.Add(s);
        foreach (var s in CaliperSteps()) stationSteps.Add(s);
        foreach (var s in BeadSteps()) stationSteps.Add(s);
        foreach (var s in CodeSteps()) stationSteps.Add(s);
        stationSteps.Add(Compare("main:cmpCode", "码有内容", "code:read", "二维码读取", "Count", 0, "GreaterThan"));
        stationSteps.Add(Logic("main:andStation1", "工位与1", "color:and2", "颜色综合判定", "cal:and4", "卡尺综合判定"));
        stationSteps.Add(Logic("main:andStation2", "工位与2", "main:andStation1", "工位与1", "bead:inspect", "胶路检测", "Result", "IsOk"));
        stationSteps.Add(Logic("main:andStation3", "工位综合判定", "main:andStation2", "工位与2", "main:cmpCode", "码有内容"));
        stationSteps.Add(VarAssign("main:setStation", "写工位结果", "StationOk", L("main:andStation3", "Result", "工位综合判定"), "Global", null));
        var scriptStep = CSharp("main:script", "工位完成脚本");
        scriptStep["IsDisEnable"] = true;   // 默认停用：Roslyn 每次运行都会编译脚本，连续运行下是内存增长源
        stationSteps.Add(scriptStep);
        stationSteps.Add(ResultDisplay("main:stationDisplay", "工位结果显示", new[]
        {
            ("r1", "颜色", "color:and2", "颜色综合判定", "Result"),
            ("r2", "卡尺", "cal:and4", "卡尺综合判定", "Result"),
            ("r3", "胶路", "bead:inspect", "胶路检测", "IsOk"),
            ("r4", "码数", "code:read", "二维码读取", "Count"),
            ("r5", "工位", "main:andStation3", "工位综合判定", "Result"),
        }, 1, "color:acq", "采集颜色图"));

        // ---- 触发判断（If/Else）：触发后才做检测、计数、上报、工位演示 ----
        var thenSteps = new JArray
        {
            ForLoop("main:loop", "循环检测", 2, loopBody, "main:loop"),
        };
        foreach (var s in stationSteps) thenSteps.Add(s);

        // Case：按运行时结果分流计数（避免跨分支连线）
        thenSteps.Add(CaseStep("main:count", "结果计数", "CurrentResult", new JArray
        {
            Branch(4, "OK", "True", new JArray
            {
                MathAdd("main:okAdd", "OK计数+1", "OKCount"),
                ConvertToInt("main:okInt", "OK计数转Int", "main:okAdd", "OK计数+1"),
                VarAssign("main:okWrite", "写OKCount", "OKCount", L("main:okInt", "Result", "OK计数转Int"), "Global", null),
            }),
            Branch(4, "NG", "False", new JArray
            {
                MathAdd("main:ngAdd", "NG计数+1", "NGCount"),
                ConvertToInt("main:ngInt", "NG计数转Int", "main:ngAdd", "NG计数+1"),
                VarAssign("main:ngWrite", "写NGCount", "NGCount", L("main:ngInt", "Result", "NG计数转Int"), "Global", null),
            }),
            Branch(2, "默认", "", new JArray()),
        }));

        // 把方案变量 RecipeName 桥接进运行期变量池，供结果上报的 sn 字段读取
        thenSteps.Add(VarAssign("main:setRecipe", "写配方到运行时", "RecipeName", LG("RecipeName"), "Runtime", null, true));

        // 并行收尾：MES 上报（默认不触发）+ CSV 记账
        thenSteps.Add(Parallel("main:parallel", "并行收尾", 1, 0, 15000, new JArray
        {
            Branch(0, "上报MES", "", new JArray { Upload("main:upload", "上报MES") }),
            Branch(0, "CSV记账", "", new JArray { CsvRecord("main:csv", "CSV记录") }),
        }));
        thenSteps.Add(Excel("main:excel", "导出Excel报表", "CSV记录"));
        // 连续运行时给主流程一个节拍，避免空转把图像集/日志刷爆
        thenSteps.Add(Delay("main:cycleDelay", "循环节拍", 500));

        var triggerIf = IfStep("main:trigger", "触发判断", "Trigger >= 1", triggerId, "Trigger", "System.Int32",
            L("main:readTrigger", "Value", "读PLC触发"),
            thenSteps,
            new JArray
            {
                Delay("main:idle", "空闲延时", 200),
            });

        var steps = new JArray
        {
            CommRead("main:readTrigger", "读PLC触发", "PLC_Trigger"),
            triggerIf,
        };

        return Flow("Main", "主任务：板卡检测 + 计数 + 上报", 2, 64, 0, steps);
    }

    // ==================================================================
    //  颜色检查（并入 Main 主流程，避免“连续运行”把子流程当顶层流程并发跑）
    // ==================================================================
    static JArray ColorSteps()
    {
        var steps = new JArray
        {
            ImgFile("color:acq", "采集颜色图", IMG_COLOR, 1),
            ColorCheck("color:seq", "颜色序列检查", new double[] { 400, 276, 1.5708, 320, 210 },
                "12 芯 = *,*,*,*,*,*,*,*,*,*,*,*\r\n", 12, 1),
            ColorRegion("color:red", "区域红灯", "Circle", new double[] { 250, 830, 55, 0, 0 }, "红", 0.6, 1),
            ColorRegion("color:purple", "区域紫块", "Rectangle", new double[] { 505, 790, 0, 45, 45 }, "紫", 0.6, 1),
            Logic("color:and1", "颜色与1", "color:seq", "颜色序列检查", "color:red", "区域红灯", "Result", "Result"),
            Logic("color:and2", "颜色综合判定", "color:and1", "颜色与1", "color:purple", "区域紫块", "Result", "Result"),
            ResultDisplay("color:display", "颜色结果显示", new[]
            {
                ("r1", "序列", "color:seq", "颜色序列检查", "Verdict"),
                ("r2", "红占比", "color:red", "区域红灯", "Share"),
                ("r3", "判定", "color:and2", "颜色综合判定", "Result"),
            }, 1, "color:acq", "采集颜色图"),
        };
        return steps;
    }

    // ==================================================================
    //  卡尺测量（并入 Main 主流程，5 种测量类型）
    // ==================================================================
    static JArray CaliperSteps()
    {
        var steps = new JArray
        {
            ImgFile("cal:acq1", "采集宽度图", DIR_CALIPER + @"\1-宽度40px.png", 1),
            Caliper("cal:w", "宽度测量", "cal:acq1", "采集宽度图", "Width",
                new[] { Reg("宽度区", "Rectangle", 240, 300, 0, 80, 100) }, 5, 30, 40, 0.5, -0.5, 1),

            ImgFile("cal:acq2", "采集两点距图", DIR_CALIPER + @"\2-两点距240px.png", 1),
            Caliper("cal:pp", "两点距测量", "cal:acq2", "采集两点距图", "PointToPoint",
                new[] { Reg("左条", "Rectangle", 240, 100, 0, 60, 80), Reg("右条", "Rectangle", 240, 340, 0, 60, 80) },
                5, 30, 240, 0.5, -0.5, 1),

            ImgFile("cal:acq3", "采集点线距图", DIR_CALIPER + @"\3-点到线距200px.png", 1),
            Caliper("cal:pl", "点到线距测量", "cal:acq3", "采集点线距图", "PointToLineDistance",
                new[] { Reg("点区", "Rectangle", 250, 300, 0, 60, 60), Reg("线区", "Rectangle", 250, 100, 0, 60, 80) },
                5, 30, 200, 0.5, -0.5, 1),

            ImgFile("cal:acq4", "采集圆图", DIR_CALIPER + @"\4-圆直径200px.png", 1),
            Caliper("cal:dia", "圆直径测量", "cal:acq4", "采集圆图", "CircleDiameter",
                new[] { Reg("圆搜索区", "Circle", 240, 320, 100) }, 16, 30, 200, 1, -1, 1),

            ImgFile("cal:acq5", "采集角度图", DIR_CALIPER + @"\5-角度90.png", 1),
            Caliper("cal:ang", "角度测量", "cal:acq5", "采集角度图", "Angle",
                new[] { Reg("竖线区", "Rectangle", 120, 240, 1.5708, 60, 80), Reg("横线区", "Rectangle", 240, 440, 0, 60, 80) },
                5, 30, 90, 0.5, -0.5, 1),

            Logic("cal:and1", "卡尺与1", "cal:w", "宽度测量", "cal:pp", "两点距测量", "IsOk", "IsOk"),
            Logic("cal:and2", "卡尺与2", "cal:and1", "卡尺与1", "cal:pl", "点到线距测量", "Result", "IsOk"),
            Logic("cal:and3", "卡尺与3", "cal:and2", "卡尺与2", "cal:dia", "圆直径测量", "Result", "IsOk"),
            Logic("cal:and4", "卡尺综合判定", "cal:and3", "卡尺与3", "cal:ang", "角度测量", "Result", "IsOk"),
            ResultDisplay("cal:display", "卡尺结果显示", new[]
            {
                ("r1", "宽度", "cal:w", "宽度测量", "MeasureValue"),
                ("r2", "两点距", "cal:pp", "两点距测量", "MeasureValue"),
                ("r3", "圆直径", "cal:dia", "圆直径测量", "MeasureValue"),
                ("r4", "角度", "cal:ang", "角度测量", "MeasureValue"),
            }, 1, "cal:acq4", "采集圆图"),
        };
        return steps;
    }

    // ==================================================================
    //  胶路检测（并入 Main 主流程）
    // ==================================================================
    static JArray BeadSteps()
    {
        var recipe = new JArray
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
        };

        var steps = new JArray
        {
            ImgFile("bead:acq", "采集胶路图", IMG_BEAD, 1),
            Action("bead:inspect", "胶路检测", "缺陷检测", P_BEAD, "胶路检测", "\uf1b3", new JObject
            {
                ["RecipeLibraryJson"] = JsonConvert.SerializeObject(recipe),
                ["DefaultRecipeName"] = "BeadA",
                ["AlignMode"] = "None",
                ["Polarity"] = "Dark",
                ["TargetWidth"] = 15.0,
                ["WidthTolerance"] = 8.0,
                ["PositionTolerance"] = 30.0,
                ["MinErrorLength"] = 10.0,
                ["MinScore"] = 0.4,
                ["DisplayViewIndex"] = 1,
                ["OutputAlignedImage"] = false,
                ["UnitOutput"] = "Pixel",
                ["PixelSizeMm"] = 0.05,
            }, new JObject
            {
                ["SrcImage"] = L("bead:acq", "Image", "采集胶路图"),
            }),
            ResultDisplay("bead:display", "胶路结果显示", new[]
            {
                ("r1", "错误数", "bead:inspect", "胶路检测", "ErrorCount"),
                ("r2", "总长", "bead:inspect", "胶路检测", "TotalErrorLength"),
                ("r3", "判定", "bead:inspect", "胶路检测", "IsOk"),
            }, 1, "bead:acq", "采集胶路图"),
        };
        return steps;
    }

    // ==================================================================
    //  码读取（并入 Main 主流程）
    // ==================================================================
    static JArray CodeSteps()
    {
        var steps = new JArray
        {
            ImgFile("code:acq", "采集二维码图", IMG_QR, 1),
            Action("code:read", "二维码读取", "图像处理", P_CODE, "二维码读取", "\uf029", new JObject
            {
                ["Symbology"] = "QR Code",
                ["TimeoutMs"] = 500,
                ["Polarity"] = "any",
            }, new JObject
            {
                ["Image"] = L("code:acq", "Image", "采集二维码图"),
            }),
            VarAssign("code:setText", "写条码文本", "BarcodeText", L("code:read", "Text", "二维码读取"), "Global", null),
            ResultDisplay("code:display", "码结果显示", new[]
            {
                ("r1", "内容", "code:read", "二维码读取", "Text"),
                ("r2", "数量", "code:read", "二维码读取", "Count"),
            }, 1, "code:acq", "采集二维码图"),
        };
        return steps;
    }

    // ==================================================================
    //  End：停机收尾
    // ==================================================================
    static JObject BuildEndFlow()
    {
        var steps = new JArray
        {
            MotionStop("end:stopX", "停止X", "X", "Decelerate", false),
            MotionStop("end:stopY", "停止Y", "Y", "Decelerate", false),
            MotionIo("end:io", "熄灭指示灯", "ZMotion_Virtual", 0, false, "WriteOutput"),
            CommWrite("end:plc", "清S7心跳", "S7_Heartbeat", new JValue(false), true),
            VarAssign("end:sys", "清系统就绪", "SystemReady", null, "Global", new JValue(false)),
            Delay("end:delay", "收尾延时", 200),
        };
        return Flow("End", "收尾：停轴 / IO 复位 / PLC 收尾", 3, 0, 0, steps, enabled: false);
    }

    // ==================================================================
    //  通讯 / 运动卡 / 变量
    // ==================================================================
    static JArray BuildComms()
    {
        return new JArray
        {
            Comm("PLC_Modbus", 0, "ModbusTcp", new JObject
            {
                ["$type"] = "VisionMaster.Communications.ModbusTcpConfig, VM.Core",
                ["ByteoRDER"] = 0,
                ["IpAddress"] = "127.0.0.1",
                ["Port"] = 503,
                ["EnableKeepAlive"] = true,
                ["KeepAliveIntervalMs"] = 30000,
                ["Type"] = 0,
                ["TimeoutMs"] = 3000,
                ["RetryCount"] = 3,
                ["RetryIntervalMs"] = 1000,
                ["ReadTimeoutMs"] = 5000,
            }, 200, "主 PLC（Modbus TCP / ABCD 字序）"),
            Comm("Meter_Modbus", 0, "ModbusTcp", new JObject
            {
                ["$type"] = "VisionMaster.Communications.ModbusTcpConfig, VM.Core",
                ["ByteoRDER"] = 3,
                ["IpAddress"] = "127.0.0.1",
                ["Port"] = 504,
                ["EnableKeepAlive"] = true,
                ["KeepAliveIntervalMs"] = 30000,
                ["Type"] = 0,
                ["TimeoutMs"] = 3000,
                ["RetryCount"] = 3,
                ["RetryIntervalMs"] = 1000,
                ["ReadTimeoutMs"] = 5000,
            }, 500, "仪表（Modbus TCP / CDAB 字序）"),
            Comm("PLC_S7", 2, "SiemensS7", new JObject
            {
                ["$type"] = "VisionMaster.Communications.SiemensS7Config, VM.Core",
                ["S7CpuType"] = "S1200",
                ["Rack"] = 0,
                ["Slot"] = 0,
                ["IpAddress"] = "127.0.0.1",
                ["Port"] = 102,
                ["EnableKeepAlive"] = true,
                ["KeepAliveIntervalMs"] = 30000,
                ["Type"] = 2,
                ["TimeoutMs"] = 3000,
                ["RetryCount"] = 3,
                ["RetryIntervalMs"] = 1000,
                ["ReadTimeoutMs"] = 5000,
            }, 200, "S7-1200 虚拟 PLC"),
        };
    }

    static JObject Comm(string name, int protocol, string protocolName, JObject cfg, int cycle, string desc)
    {
        return new JObject
        {
            ["ConnectionName"] = name,
            ["Protocol"] = protocol,
            ["Config"] = cfg,
            ["ReadCycleMs"] = cycle,
            ["ScanGroups"] = new JArray(),
            ["AutoStart"] = true,
            ["IsolateFaultedSegments"] = false,
            ["AutoReconnect"] = true,
            ["IsEnabled"] = true,
            ["Description"] = desc,
        };
    }

    static JArray BuildMotionCards()
    {
        var axes = new JArray
        {
            Axis("X", 0, "X 轴：检测/取料"),
            Axis("Y", 1, "Y 轴：横移"),
            Axis("Z", 2, "Z 轴：升降"),
        };

        return new JArray
        {
            new JObject
            {
                ["Id"] = G("motion:card").ToString(),
                ["DriverTypeKey"] = "Plugin.Motion.ZMotion.ZMotionCard, Plugin.Motion.ZMotion" + S,
                ["DisplayName"] = "ZMotion_Virtual",
                ["Address"] = "127.0.0.1",
                ["CardModel"] = "VPLC532R",
                ["AutoConnect"] = true,
                ["Remarks"] = "正运动虚拟控制器（127.0.0.1:502）",
                ["Axes"] = axes,
                ["Points"] = new JArray(),
                ["Params"] = new JObject
                {
                    ["DefaultVelocityMmPerS"] = 10.0,
                    ["DefaultAccelMmPerS2"] = 100.0,
                    ["PollIntervalMs"] = 10,
                    ["CommandTimeoutMs"] = 30000,
                    ["WatchdogTimeoutMs"] = 3000,
                    ["AutoReconnect"] = true,
                    ["MaxQueueDepth"] = 256,
                },
            },
        };
    }

    static JObject Axis(string name, int index, string remark)
    {
        return new JObject
        {
            ["AxisId"] = G("axis:" + name).ToString(),
            ["LogicalName"] = name,
            ["PhysicalIndex"] = index,
            ["Enabled"] = true,
            ["UnitsPerMm"] = 100.0,
            ["SoftLimitMinMm"] = -500.0,
            ["SoftLimitMaxMm"] = 500.0,
            ["AxisRemark"] = remark,
            ["EncoderType"] = "",
            ["EncoderPpu"] = 0.0,
            ["MotorDriver"] = "",
            ["MotorRatedCurrentA"] = 0.0,
            ["FollowErrorUpperPulse"] = 0.0,
            ["FollowErrorLowerPulse"] = 0.0,
        };
    }

    static JArray BuildVariables()
    {
        var list = new JArray
        {
            Var("RecipeName", T_STR, "当前产品配方", "Product_Type_A"),
            Var("CurrentResult", T_BOOL, "本轮综合判定", false),
            Var("OcrText", T_STR, "本轮 OCR 文本", ""),
            Var("DefectCount", T_INT, "本轮缺陷数", 0),
            Var("StationOk", T_BOOL, "综合工位判定", false),
            Var("OKCount", T_INT, "累计良品数", 0),
            Var("NGCount", T_INT, "累计不良数", 0),
            Var("TotalCount", T_INT, "累计检测数", 0),
            Var("SystemReady", T_BOOL, "系统就绪", false),
            Var("CycleIndex", T_INT, "循环索引", 0),
            Var("MeasureWidth", T_DBL, "最近一次宽度测量", 0.0),
            Var("BarcodeText", T_STR, "最近一次读码内容", ""),
            Var("YieldRate", T_DBL, "实时良率(%)", 100.0),
        };

        list.Add(NetVar("PLC_Trigger", T_SHORT, "PLC 触发计数", 0, "PLC_Modbus", "ModbusTcp", "HoldingRegisters", "0", -1, 1, 0, "Int16"));
        list.Add(NetVar("PLC_Ack", T_SHORT, "PLC 完成确认", 0, "PLC_Modbus", "ModbusTcp", "HoldingRegisters", "1", -1, 1, 0, "Int16"));
        list.Add(NetVar("PLC_Result", T_BOOL, "PLC 判定结果（线圈）", false, "PLC_Modbus", "ModbusTcp", "Coils", "2", 0, 1, 0, "Boolean"));
        list.Add(NetVar("PLC_OKCount", T_INT, "PLC 良品计数", 0, "PLC_Modbus", "ModbusTcp", "HoldingRegisters", "4", -1, 1, 0, "Int32"));
        list.Add(NetVar("Meter_Value", T_SGL, "仪表读数（CDAB）", 0.0, "Meter_Modbus", "ModbusTcp", "InputRegisters", "0", -1, 1, 0, "Float"));
        list.Add(NetVar("S7_Heartbeat", T_BOOL, "S7 心跳", false, "PLC_S7", "SiemensS7", "M", "0", 0, 1, 1, "Boolean"));
        list.Add(NetVar("S7_Result", T_SHORT, "S7 判定字", 0, "PLC_S7", "SiemensS7", "DB", "0", -1, 1, 1, "Int16"));
        list.Add(NetVar("S7_Ready", T_BOOL, "S7 就绪", false, "PLC_S7", "SiemensS7", "M", "1", 0, 1, 1, "Boolean"));

        return list;
    }

    static JObject Var(string name, string type, string desc, object value)
    {
        return new JObject
        {
            ["VarType"] = "Local",
            ["Name"] = name,
            ["VariableId"] = G("var:" + name).ToString(),
            ["DataTypeString"] = type,
            ["Description"] = desc,
            ["DefaultValue"] = value == null ? JValue.CreateNull() : JToken.FromObject(value),
            ["Value"] = value == null ? JValue.CreateNull() : JToken.FromObject(value),
            ["Offset"] = "0",
            ["BitOffset"] = -1,
            ["Length"] = 0,
            ["AddressOffset"] = 0,
            ["ElementByteLength"] = 0,
            ["UseLegacyAddress"] = false,
            ["DbNumber"] = 1,
            ["ScanGroup"] = "",
            ["GroupId"] = Guid.Empty.ToString(),
        };
    }

    static JObject NetVar(string name, string type, string desc, object value,
        string conn, string protocol, string area, string offset, int bitOffset, int length, int db, string dataType)
    {
        return new JObject
        {
            ["VarType"] = "Network",
            ["Name"] = name,
            ["VariableId"] = G("var:" + name).ToString(),
            ["DataTypeString"] = type,
            ["Description"] = desc,
            ["DefaultValue"] = JToken.FromObject(value),
            ["Value"] = JToken.FromObject(value),
            ["ConnectionName"] = conn,
            ["Protocol"] = protocol,
            ["Area"] = area,
            ["Offset"] = offset,
            ["BitOffset"] = bitOffset,
            ["Length"] = length,
            ["AddressOffset"] = 0,
            ["ElementByteLength"] = 0,
            ["UseLegacyAddress"] = false,
            ["DbNumber"] = db,
            ["DataValueType"] = dataType,
            ["ScanGroup"] = "",
            ["GroupId"] = Guid.Empty.ToString(),
            ["MergeAdjacentRead"] = true,
        };
    }

    // ==================================================================
    //  步骤构造器
    // ==================================================================
    static JObject Action(string seed, string name, string category, string type, string pluginName,
        string icon, JObject inputs, JObject links = null)
    {
        return new JObject
        {
            ["$type"] = "VisionMaster.Models.ActionStep, VM.Core",
            ["StepID"] = G(seed).ToString(),
            ["Icon"] = icon,
            ["PluginName"] = pluginName,
            ["Category"] = category,
            ["PluginTypeName"] = type,
            ["StepName"] = name,
            ["Description"] = pluginName,
            ["SortId"] = 0,
            ["IsDisEnable"] = false,
            ["State"] = 0,
            ["InputValues"] = inputs ?? new JObject(),
            ["LinkedSources"] = links ?? new JObject(),
            ["OutputPortDefinitions"] = new JArray(),
        };
    }

    static JObject ImgFile(string seed, string name, string path, int view)
        => Action(seed, name, "常用工具", P_IMAGE, "图像采集", "\uf1c5", new JObject
        {
            ["Mode"] = "SingleFile",
            ["FilePath"] = path,
            ["DisplayViewIndex"] = view,
        });

    static JObject ImgFolder(string seed, string name, string folder, string forSeed, string forName, int view)
    {
        var inputs = new JObject
        {
            ["Mode"] = "Folder",
            ["FolderPath"] = folder,
            ["DisplayViewIndex"] = view,
        };
        var links = new JObject
        {
            ["FileIndex"] = L(forSeed, "Index", forName),
        };
        return Action(seed, name, "常用工具", P_IMAGE, "图像采集", "\uf1c5", inputs, links);
    }

    static JObject PreProcess(string seed, string name, JArray chain, int view,
        string imgSeed = "main:acq", string imgName = "采集板卡图")
        => Action(seed, name, "图像处理", P_PRE, "图像预处理", "\uf1fc", new JObject
        {
            ["Chain"] = chain,
            ["DisplayViewIndex"] = view,
        }, new JObject
        {
            ["SrcImage"] = L(imgSeed, "Image", imgName),
        });

    static JObject Op(string key, params (string K, string V)[] ps)
    {
        var p = new JObject();
        foreach (var (k, v) in ps) p[k] = v;
        return new JObject { ["OperatorKey"] = key, ["Enabled"] = true, ["Params"] = p };
    }

    static JObject Roi(string name, string shape, params double[] ps)
        => new() { ["Name"] = name, ["ShapeType"] = shape, ["Params"] = new JArray(ps) };

    static JObject CreateRoi(string seed, string name, JArray rois, int view,
        string imgSeed = "main:pre", string imgName = "图像预处理")
        => Action(seed, name, "常用工具", P_ROI, "ROI", "\uf125", new JObject
        {
            ["RoiList"] = rois,
            ["MaskInvert"] = false,
            ["DisplayViewIndex"] = view,
        }, new JObject
        {
            ["SrcImage"] = L(imgSeed, "Image", imgName),
        });

    static JObject GeometryFind(string seed, string name, string kind, string maskSeed, string maskName,
        double sigma, double low, double high, double minLen, bool failOnNotFound, int view,
        string imgSeed = "main:pre", string imgName = "图像预处理")
    {
        var links = new JObject
        {
            ["SrcImage"] = L(imgSeed, "Image", imgName),
        };
        if (maskSeed != null) links["MaskRegion"] = L(maskSeed, "MaskRegion", maskName);
        return Action(seed, name, "几何工具", P_GF, "几何查找", "\uf1b2", new JObject
        {
            ["Kind"] = kind,
            ["Sigma"] = sigma,
            ["LowThreshold"] = low,
            ["HighThreshold"] = high,
            ["MinContourLength"] = minLen,
            ["FitAlgorithm"] = "Tukey",
            ["FailOnNotFound"] = failOnNotFound,
            ["DisplayViewIndex"] = view,
        }, links);
    }

    static JObject GeometryRelation(string seed, string name, string op,
        string aSeed, string aName, string bSeed, string bName, double mmPerPixel)
        => Action(seed, name, "几何工具", P_GR, "几何计算", "\uf0c3", new JObject
        {
            ["Op"] = op,
            ["MmPerPixel"] = mmPerPixel,
            ["StandardValue"] = 0.0,
            ["UpperTolerance"] = 0.0,
            ["LowerTolerance"] = 0.0,
            ["AngleTolerance"] = 0.1,
            ["RelationKind"] = "Parallel",
            ["ConstructMode"] = "LineThroughTwoPoints",
            ["DisplayViewIndex"] = 0,
        }, new JObject
        {
            ["A"] = L(aSeed, "Geometry", aName),
            ["B"] = L(bSeed, "Geometry", bName),
        });

    static JObject PoseFollow(string seed, string name, string roiSeed, string roiName,
        string poseSeed, string poseName, int view)
        => Action(seed, name, "定位", P_POSE, "坐标变换", "\uf047", new JObject
        {
            ["Mode"] = "FollowRoi",
            ["EmitFollowedImage"] = false,
            ["TemplateRefFromRegionCenter"] = true,
            ["DisplayViewIndex"] = view,
        }, new JObject
        {
            ["BaseRegion"] = L(roiSeed, "MaskRegion", roiName),
            ["PoseRow"] = L(poseSeed, "Row", poseName),
            ["PoseCol"] = L(poseSeed, "Col", poseName),
        });

    static JObject BlobDetect(string seed, string name, string maskSeed, string maskName, double mmPerPixel, int view,
        string imgSeed = "main:pre", string imgName = "图像预处理")
        => Action(seed, name, "缺陷检测", P_BLOB, "Blob 缺陷检测", "\uf0c8", new JObject
        {
            ["ThresholdMode"] = "Fixed",
            ["DetectTarget"] = "Dark",
            ["DetectBrightAndDark"] = false,
            ["MinGray"] = 0.0,
            ["MaxGray"] = 60.0,
            ["MinArea"] = 10.0,
            ["MaxBlobArea"] = 0.0,
            ["MaxSingleArea"] = 1000.0,
            ["MaxTotalArea"] = 5000.0,
            ["MinDefectCount"] = 0,
            ["MaxDefectCount"] = 10,
            ["ExcludeBorderPx"] = 0,
            ["OpenRadius"] = 0.0,
            ["CloseRadius"] = 1.0,
            ["FillHoles"] = false,
            ["MergeRadius"] = 0.0,
            ["PixelSizeMm"] = mmPerPixel,
            ["MmPerPixel"] = mmPerPixel,
            ["SortMode"] = "None",
            ["DisplayViewIndex"] = view,
        }, new JObject
        {
            ["SrcImage"] = L(imgSeed, "Image", imgName),
            ["MaskRegion"] = L(maskSeed, "FollowedRegion", maskName),
        });

    static JObject Ocr(string seed, string name, double[] roi, int view,
        string imgSeed = "main:pre", string imgName = "图像预处理", bool textIsDark = false)
        => Action(seed, name, "图像处理", P_OCR, "OCR 文本识别", "\uf031", new JObject
        {
            ["RoiParams"] = new JArray(roi),
            ["TextIsDark"] = textIsDark,
            ["MinCharHeight"] = 8,
            ["MaxCharHeight"] = 300,
            ["MinCharWidth"] = 4,
            ["MaxCharWidth"] = 300,
            ["UseFixedThreshold"] = false,
            ["FixedThreshold"] = 128,
            ["MinArea"] = 20,
            ["MaxArea"] = 5000,
            ["ClosingRadius"] = 0.0,
            ["ErosionRadius"] = 0.0,
            ["DilationRadius"] = 0.0,
            ["ModelPath"] = "",
            ["ColumnFirst"] = false,
        }, new JObject
        {
            ["Image"] = L(imgSeed, "Image", imgName),
        });

    static JObject Compare(string seed, string name, string srcSeed, string srcName, string port,
        double b, string type, double tolerance = 1e-6)
        => Action(seed, name, "逻辑判断", P_CMP, "数值比较", "\uf047", new JObject
        {
            ["B"] = b,
            ["Type"] = type,
            ["Tolerance"] = tolerance,
        }, new JObject
        {
            ["A"] = L(srcSeed, port, srcName),
        });

    static JObject Logic(string seed, string name, string aSeed, string aName, string bSeed, string bName,
        string aPort = "Result", string bPort = "Result")
        => Action(seed, name, "逻辑判断", P_LOGIC, "逻辑运算", "\uf0e7", new JObject
        {
            ["Operation"] = "And",
        }, new JObject
        {
            ["A"] = L(aSeed, aPort, aName),
            ["B"] = L(bSeed, bPort, bName),
        });

    static JObject MathAdd(string seed, string name, string globalVar)
        => Action(seed, name, "数据处理", P_MATH, "数学计算", "\uf1ec", new JObject
        {
            ["Operation"] = "Add",
            ["B"] = 1.0,
        }, new JObject
        {
            ["A"] = LG(globalVar),
        });

    static JObject ConvertToInt(string seed, string name, string srcSeed, string srcName)
        => Action(seed, name, "数据处理", P_CONVERT, "类型转换", "\uf0ec", new JObject
        {
            ["Target"] = "Int32",
            ["Format"] = "yyyy-MM-dd HH:mm:ss",
        }, new JObject
        {
            ["Input"] = L(srcSeed, "Result", srcName),
        });

    static JObject Disable(JObject step)
    {
        step["IsDisEnable"] = true;
        return step;
    }

    static JObject VarAssign(string seed, string name, string varName, JObject valueLink,
        string scope, JToken constant, bool createIfNotExists = false)
    {
        var inputs = new JObject
        {
            ["Name"] = varName,
            ["CreateIfNotExists"] = createIfNotExists,
            ["Scope"] = scope,
        };
        var links = new JObject();
        if (valueLink != null) links["Value"] = valueLink;
        else if (constant != null) inputs["Value"] = constant;
        return Action(seed, name, "变量操作", P_VAR, "变量赋值", "\uf044", inputs, links);
    }

    static JObject ResultDisplay(string seed, string name, (string Id, string Label, string Seed, string SrcName, string Port)[] items, int view,
        string imgSeed, string imgName, string imgPort = "Image")
    {
        var defs = new JArray();
        var links = new JObject
        {
            ["SrcImage"] = L(imgSeed, imgPort, imgName),
        };
        foreach (var it in items)
        {
            defs.Add(new JObject
            {
                ["Id"] = it.Id,
                ["Label"] = it.Label,
                ["Format"] = "",
                ["Unit"] = "",
                ["Enabled"] = true,
            });
            links[it.Id] = L(it.Seed, it.Port, it.SrcName);
        }
        return Action(seed, name, "常用工具", P_DISPLAY, "结果显示", "\uf06e", new JObject
        {
            ["Items"] = defs,
            ["ShowTextOnImage"] = true,
            ["JudgeItemLabel"] = "判定",
            ["NgValue"] = "NG",
            ["MaxDrawElements"] = 300,
            ["TextAnchor"] = "TopLeft",
            ["TextMargin"] = 0,
            ["TextScale"] = 1.0,
            ["DisplayViewIndex"] = view,
        }, links);
    }

    static JObject ColorCheck(string seed, string name, double[] roi, string recipe, int wires, int view)
        => Action(seed, name, "图像处理", P_COLORCHK, "颜色序列检查", "\uf1fb", new JObject
        {
            ["RoiParams"] = new JArray(roi),
            ["ExpectedSequence"] = "",
            ["RecipeText"] = recipe,
            ["WireCount"] = wires,
            ["ReverseSequence"] = false,
            ["Regularize"] = true,
            ["DisplayViewIndex"] = view,
        }, new JObject
        {
            ["Image"] = L("color:acq", "Image", "采集颜色图"),
        });

    static JObject ColorRegion(string seed, string name, string shape, double[] roi, string colors, double minShare, int view)
        => Action(seed, name, "图像处理", P_COLORREG, "区域颜色检查", "\uf1fc", new JObject
        {
            ["RoiShape"] = shape,
            ["RoiParams"] = new JArray(roi),
            ["ExpectedColors"] = colors,
            ["MinShare"] = minShare,
            ["SamplePoints"] = 400,
            ["DisplayViewIndex"] = view,
        }, new JObject
        {
            ["Image"] = L("color:acq", "Image", "采集颜色图"),
        });

    static JObject Reg(string name, string shape, params double[] ps)
        => new() { ["Name"] = name, ["ShapeType"] = shape, ["Params"] = new JArray(ps) };

    static JObject Caliper(string seed, string name, string imgSeed, string imgName, string kind,
        JObject[] regions, int count, double annulus, double standard, double upper, double lower, double px)
        => Action(seed, name, "测量", P_CALIPER, "卡尺测量", "\uf0b3", new JObject
        {
            ["MeasureKind"] = kind,
            ["CaliperRegions"] = new JArray(regions),
            ["CaliperCount"] = count,
            ["Sigma"] = 1.0,
            ["EdgeThreshold"] = 30.0,
            ["Polarity"] = "All",
            ["Select"] = "All",
            ["Interpolation"] = "Bilinear",
            ["FitAlgorithm"] = "Tukey",
            ["FitMaxError"] = 1.0,
            ["FitMinPoints"] = 3,
            ["AnnulusWidth"] = annulus,
            ["StandardValue"] = standard,
            ["UpperTolerance"] = upper,
            ["LowerTolerance"] = lower,
            ["PixelSizeMm"] = px,
            ["MmPerPixel"] = px,
        }, new JObject
        {
            ["SrcImage"] = L(imgSeed, "Image", imgName),
        });

    static JObject CsvRecord(string seed, string name)
        => Action(seed, name, "数据处理", P_CSV, "CSV记录", "\uf15c", new JObject
        {
            ["DirectoryTemplate"] = @"D:\Records\{StepName}\{yyyy-MM}\",
            ["FileNamePattern"] = "{yyyy-MM-dd}.csv",
            ["WriteHeader"] = true,
            ["KeepDays"] = 30,
            ["BlockOnFailure"] = false,
            ["FlushEveryRow"] = false,
            ["Columns"] = new JArray
            {
                Col("时间", "BuiltIn", "", "Timestamp"),
                Col("序号", "BuiltIn", "", "Sequence"),
                Col("结果", "Variable", "CurrentResult", "Timestamp"),
                Col("OCR", "Variable", "OcrText", "Timestamp"),
                Col("缺陷数", "Variable", "DefectCount", "Timestamp"),
            },
            ["ImageSaveMode"] = "Off",
            ["ResultColumn"] = "结果",
            ["NgValue"] = "NG",
            ["ImageDirName"] = "img",
            ["ImageNameTemplate"] = "{序号}_{结果}",
            ["ImageFormat"] = "png",
        });

    static JObject Col(string name, string source, string variable, string builtIn)
        => new() { ["Name"] = name, ["Source"] = source, ["VariableName"] = variable, ["BuiltIn"] = builtIn, ["Format"] = "", ["Enabled"] = true };

    static JObject Upload(string seed, string name)
        => Action(seed, name, "数据处理", P_UPLOAD, "结果上报", "\uf093", new JObject
        {
            ["PayloadKind"] = "FieldTable",
            ["Url"] = "http://127.0.0.1:19001/api/inspect",
            ["TimeoutMs"] = 3000,
            ["Headers"] = new JArray(),
            ["Fields"] = new JArray
            {
                Field("sn", "Variable", "RecipeName", "Timestamp"),
                Field("result", "Variable", "CurrentResult", "Timestamp"),
                Field("time", "BuiltIn", "", "Timestamp"),
            },
            ["SendTiming"] = "OnTrigger",
            ["JudgeFieldName"] = "result",
            ["NgValue"] = "NG",
            ["AsyncMode"] = true,
            ["RetryCount"] = 0,
            ["BlockOnFailure"] = false,
            ["SpoolEnabled"] = false,
            ["TimestampFormat"] = "yyyy-MM-dd HH:mm:ss.fff",
            ["TimestampUtc"] = false,
            ["上报触发"] = false,
        });

    static JObject Field(string name, string source, string variable, string builtIn)
        => new() { ["Name"] = name, ["Source"] = source, ["VariableName"] = variable, ["BuiltIn"] = builtIn, ["Enabled"] = true };

    static JObject Excel(string seed, string name, string csvStep)
        => Action(seed, name, "数据处理", P_EXCEL, "Excel报表", "\uf1c3", new JObject
        {
            ["SourcePathTemplate"] = @"D:\Records\{源步骤}\{yyyy-MM}\{yyyy-MM-dd}.csv",
            ["SourceStepName"] = csvStep,
            ["OutputDirectoryTemplate"] = @"D:\Records\{StepName}\{yyyy-MM}\报表\",
            ["ReportFileNamePattern"] = "{yyyy-MM-dd}_报表.xlsx",
            ["LinkColumn"] = "图片路径",
            ["FreezeHeader"] = true,
            ["AutoFilter"] = true,
            ["OnlyRowsWithImage"] = false,
            ["ExportPolicy"] = "Manual",
            ["OpenAfterExport"] = false,
            ["BlockOnFailure"] = false,
        });

    static JObject CommRead(string seed, string name, string varName)
        => Action(seed, name, "通讯", P_CREAD, "立刻读", "\uf0a4", new JObject
        {
            ["VariableName"] = varName,
        });

    static JObject CommWrite(string seed, string name, string varName, JToken constantOrLink, bool wait)
    {
        var inputs = new JObject { ["VariableName"] = varName, ["WaitForCompletion"] = wait };
        var links = new JObject();
        if (constantOrLink is JObject link && link["Kind"] != null) links["Value"] = link;
        else inputs["Value"] = constantOrLink;
        return Action(seed, name, "通讯", P_CWRITE, "立刻写", "\uf0a5", inputs, links);
    }

    static JObject MotionEnable(string seed, string name, string axis, bool enable, int timeout)
        => Action(seed, name, "运动控制", P_MEN, "轴使能", "\uf085", new JObject
        {
            ["Axis"] = axis,
            ["Enable"] = enable,
            ["Timeout"] = timeout,
        });

    static JObject MotionHome(string seed, string name, string axis, string mode, int timeout)
        => Action(seed, name, "运动控制", P_MHOME, "轴回零", "\uf085", new JObject
        {
            ["Axis"] = axis,
            ["Mode"] = mode,
            ["TimeoutMs"] = timeout,
        });

    static JObject MotionMove(string seed, string name, string axis, string mode, double target, double velocity)
        => Action(seed, name, "运动控制", P_MMOVE, "轴运动", "\uf085", new JObject
        {
            ["Axis"] = axis,
            ["Mode"] = mode,
            ["Target"] = target,
            ["Velocity"] = velocity,
        });

    static JObject MotionWait(string seed, string name, string axis, int timeout, int poll)
        => Action(seed, name, "运动控制", P_MWAIT, "等待到位", "\uf085", new JObject
        {
            ["Axis"] = axis,
            ["TimeoutMs"] = timeout,
            ["PollIntervalMs"] = poll,
        });

    static JObject MotionStop(string seed, string name, string axis, string mode, bool emergency)
        => Action(seed, name, "运动控制", P_MSTOP, "轴停止", "\uf04d", new JObject
        {
            ["Axis"] = axis,
            ["Mode"] = mode,
            ["UseEmergencyStop"] = emergency,
        });

    static JObject MotionReadStatus(string seed, string name, string axis)
        => Action(seed, name, "运动控制", P_MSTATUS, "读轴状态", "\uf085", new JObject
        {
            ["Axis"] = axis,
            ["FailOnAlarm"] = false,
        });

    static JObject MotionIo(string seed, string name, string card, int port, bool value, string direction)
        => Action(seed, name, "运动控制", P_MIO, "轴卡 IO", "\uf085", new JObject
        {
            ["Card"] = card,
            ["IoPort"] = port,
            ["Value"] = value,
            ["Direction"] = direction,
        });

    static JObject Delay(string seed, string name, int ms)
        => Action(seed, name, "常用工具", P_DELAY, "延时", "\uf017", new JObject { ["Delay"] = ms });

    static JObject RunFlow(string seed, string name, string flowName, int timeout)
        => Action(seed, name, "流程控制", P_RUNFLOW, "调用流程", "\uf04b", new JObject
        {
            ["FlowName"] = flowName,
            ["TimeoutMs"] = timeout,
        });

    static JObject CSharp(string seed, string name)
        => Action(seed, name, "逻辑控制", P_CSCRIPT, "C#脚本", "\uf121", new JObject
        {
            ["ScriptText"] = "Context.Info(\"综合工位：颜色/卡尺/胶路/码读取 全部执行完毕\");",
            ["InputVars"] = new JArray(),
            ["OutputVars"] = new JArray(),
            ["SharedVars"] = new JArray(),
        });

    // ---------- 容器 ----------
    static JObject Branch(int type, string name, string expr, JArray steps)
        => new() { ["Steps"] = steps, ["BranchType"] = type, ["StepName"] = name, ["Expression"] = expr };

    static JObject IfStep(string seed, string name, string expr, Guid varId, string varName, string varType,
        JObject conditionLink, JArray thenSteps, JArray elseSteps)
    {
        var links = new JObject { [varId.ToString()] = conditionLink };
        return new JObject
        {
            ["$type"] = "VisionMaster.Models.ConditionStep, VM.Core",
            ["StepID"] = G(seed).ToString(),
            ["Icon"] = "\uf0e8",
            ["PluginName"] = "If",
            ["Category"] = "逻辑控制",
            ["PluginTypeName"] = "BuiltIn_If",
            ["StepName"] = name,
            ["Description"] = "条件判断",
            ["SortId"] = 0,
            ["IsDisEnable"] = false,
            ["State"] = 0,
            ["InputValues"] = new JObject(),
            ["LinkedSources"] = links,
            ["OutputPortDefinitions"] = new JArray(),
            ["LocalVariables"] = new JArray
            {
                new JObject { ["Id"] = varId.ToString(), ["Name"] = varName, ["DataTypeName"] = varType },
            },
            ["RuntimeVariableRefs"] = new JArray(),
            ["Children"] = new JArray
            {
                Branch(1, "If", expr, thenSteps),
                Branch(2, "Else", "", elseSteps),
            },
        };
    }

    static JObject CaseStep(string seed, string name, string judge, JArray branches, string judgeType = "System.Boolean")
    {
        return new JObject
        {
            ["$type"] = "VisionMaster.Models.CaseStep, VM.Core",
            ["StepID"] = G(seed).ToString(),
            ["Icon"] = "\uf126",
            ["PluginName"] = "分支匹配",
            ["Category"] = "逻辑控制",
            ["PluginTypeName"] = "BuiltIn_Case",
            ["StepName"] = name,
            ["Description"] = "按判据匹配分支",
            ["SortId"] = 0,
            ["IsDisEnable"] = false,
            ["State"] = 0,
            ["InputValues"] = new JObject(),
            ["LinkedSources"] = new JObject(),
            ["OutputPortDefinitions"] = new JArray(),
            ["LocalVariables"] = new JArray(),
            ["RuntimeVariableRefs"] = new JArray
            {
                new JObject { ["Id"] = G("casevar:" + judge).ToString(), ["Name"] = judge, ["DataTypeName"] = judgeType },
            },
            ["JudgeExpression"] = judge,
            ["Children"] = branches,
        };
    }

    static JObject ForLoop(string seed, string name, int count, JArray body, string forSeed)
    {
        return new JObject
        {
            ["$type"] = "VisionMaster.Models.ForStep, VM.Core",
            ["StepID"] = G(seed).ToString(),
            ["Icon"] = "\uf0ca",
            ["PluginName"] = "For",
            ["Category"] = "逻辑控制",
            ["PluginTypeName"] = "BuiltIn_For",
            ["StepName"] = name,
            ["Description"] = "按次数循环",
            ["SortId"] = 0,
            ["IsDisEnable"] = false,
            ["State"] = 0,
            ["InputValues"] = new JObject(),
            ["LinkedSources"] = new JObject(),
            ["OutputPortDefinitions"] = new JArray(),
            ["DefaultLoopCount"] = count,
            ["Children"] = new JArray { Branch(0, "循环体", "", body) },
        };
    }

    static JObject Parallel(string seed, string name, int mode, int failFast, int joinTimeout, JArray branches)
    {
        return new JObject
        {
            ["$type"] = "VisionMaster.Models.ParallelStep, VM.Core",
            ["StepID"] = G(seed).ToString(),
            ["Icon"] = "\uf0f7",
            ["PluginName"] = "并行分组",
            ["Category"] = "逻辑控制",
            ["PluginTypeName"] = "BuiltIn_Parallel",
            ["StepName"] = name,
            ["Description"] = "多分支并行执行",
            ["SortId"] = 0,
            ["IsDisEnable"] = false,
            ["State"] = 0,
            ["InputValues"] = new JObject(),
            ["LinkedSources"] = new JObject(),
            ["OutputPortDefinitions"] = new JArray(),
            ["ExecutionMode"] = mode,
            ["FailFastMode"] = failFast,
            ["JoinTimeoutMs"] = joinTimeout,
            ["Children"] = branches,
        };
    }

    static JObject WaitStep(string seed, string name, string varName, string expr, JObject link,
        int timeoutMs, int timeoutAction, string timeoutMessage, string varType = "System.Int32")
    {
        var varId = G("waitvar:" + seed);
        return new JObject
        {
            ["$type"] = "VisionMaster.Models.WaitStep, VM.Core",
            ["StepID"] = G(seed).ToString(),
            ["Icon"] = "\uf252",
            ["PluginName"] = "等待",
            ["Category"] = "逻辑控制",
            ["PluginTypeName"] = "BuiltIn_Wait",
            ["StepName"] = name,
            ["Description"] = "等待条件成立",
            ["SortId"] = 0,
            ["IsDisEnable"] = false,
            ["State"] = 0,
            ["InputValues"] = new JObject(),
            ["LinkedSources"] = new JObject { [varId.ToString()] = link },
            ["OutputPortDefinitions"] = new JArray(),
            ["LocalVariables"] = new JArray
            {
                new JObject { ["Id"] = varId.ToString(), ["Name"] = varName, ["DataTypeName"] = varType },
            },
            ["RuntimeVariableRefs"] = new JArray(),
            ["ConditionExpression"] = expr,
            ["PollIntervalMs"] = 50,
            ["TimeoutMs"] = timeoutMs,
            ["TimeoutAction"] = timeoutAction,
            ["TimeoutMessage"] = timeoutMessage,
            ["AlarmVariableName"] = "",
        };
    }

    static JObject Flow(string name, string desc, int role, int invokeType, int triggerMode, JArray steps, bool enabled = true)
    {
        return new JObject
        {
            ["FlowID"] = G("flow:" + name).ToString("N"),
            ["FlowName"] = name,
            ["Description"] = desc,
            ["Version"] = 0,
            ["IsEnabled"] = enabled,
            ["Role"] = role,
            ["InvokeType"] = invokeType,
            ["TimerIntervalMs"] = 0,
            ["StepsEncrypted"] = false,
            ["TriggerMode"] = triggerMode,
            ["Layout"] = new JObject { ["Nodes"] = new JObject(), ["Count"] = 0 },
            ["Steps"] = steps,
        };
    }

    // ---------- 连线 ----------
    static JObject L(string seed, string port, string srcStepName, string targetPort = null)
        => new()
        {
            ["Kind"] = 1,
            ["TargetStepId"] = G(seed).ToString(),
            ["TargetPortName"] = targetPort ?? port,
            ["TargetVariableId"] = Guid.Empty.ToString(),
            ["DisplayAddress"] = srcStepName + "." + (targetPort ?? port),
        };

    static JObject LG(string varName)
        => new()
        {
            ["Kind"] = 2,
            ["TargetStepId"] = Guid.Empty.ToString(),
            ["TargetPortName"] = varName,
            ["TargetVariableId"] = G("var:" + varName).ToString(),
            ["DisplayAddress"] = "全局变量 (Global)." + varName,
        };

    // ---------- 确定性 Guid ----------
    static Guid G(string seed)
    {
        using var md5 = MD5.Create();
        return new Guid(md5.ComputeHash(Encoding.UTF8.GetBytes("VM.Solution." + seed)));
    }
}

