using Newtonsoft.Json.Linq;

internal static partial class Program
{
    // ==================================================================
    //  方案 A：精密电子 BGA 焊球全检（可导入运行的 .vms）
    //
    //  算法链：
    //    采集(缺陷图) → 预处理 → 基准匹配定位(金样图)
    //    → 跟随 ROI(由位姿驱动) → 图像差分(被测 vs 金样)
    //    → Blob 缺陷检出 → 分级(OK / 复检 / NG)
    //    → 结果显示 / CSV 记录 / 计数 / PLC 回写
    //
    //  画面：4 页（运行总览 / 实时检测 / 定位跟踪 / 配方与参数）
    // ==================================================================

    const string P_MATCH = "Plugin.Matching.MatchingPlugin, Plugin.Matching" + S;
    const string P_DIFF = "Plugin.ImageDiff.ImageDiffPlugin, Plugin.ImageDiff" + S;

    // 样图只用方案内相对路径（Image\xxx.png）。源文件从 SourceImagePathOf() 还原，
    // 生成时会被复制进 <方案文件夹>\Image\ —— 整个方案目录拷走就能用。
    const string IMG_BGA_DEFECT = @"Image\bga_14x14_defects.png";
    const string IMG_BGA_MODEL = @"Image\bga_14x14_model.png";

    static JObject BuildCcgBgaInspectSolution()
    {
        return new JObject
        {
            ["SolutionName"] = "CCG-BGA焊球全检",
            ["Version"] = 1.0,
            ["Config"] = new JObject { ["ImageViewMode"] = 0 },
            ["Flows"] = new JArray
            {
                BuildBgaHomeFlow(),
                BuildBgaMainFlow(),
                BuildBgaInspectFlow(),
                BuildBgaRecipeFlow(),
                BuildBgaEndFlow(),
            },
            ["SolutionId"] = G("ccg:solution").ToString(),
            ["CommunicationConfigs"] = BuildBgaComms(),
            ["IoBuses"] = new JArray(),
            ["IoDevices"] = new JArray(),
            ["CameraConfigs"] = new JArray(),
            ["MotionCards"] = BuildBgaMotionCards(),
            ["CamTables"] = new JArray(),
            ["VariableSnapshots"] = BuildBgaVariables(),
            ["VariableGroups"] = new JArray(),
            ["WatchItems"] = new JArray(),
            ["Scada"] = BuildBgaScada(),
        };
    }

    // ------------------------------------------------------------------
    //  变量
    // ------------------------------------------------------------------
    static JArray BuildBgaVariables()
    {
        var list = BuildVariables();   // 平台标准变量（RecipeName / OKCount / NGCount / YieldRate ...）

        list.Add(Var("CycleCount", T_INT, "累计节拍数", 0));
        list.Add(Var("BgaDefectCount", T_INT, "本轮缺陷片数", 0));
        list.Add(Var("BgaTotalArea", T_DBL, "本轮缺陷总面积（像素）", 0.0));
        list.Add(Var("BgaMatchScore", T_DBL, "定位匹配分数", 0.0));
        list.Add(Var("BgaGrade", T_INT, "分级：0=OK 1=复检 2=NG", 0));
        list.Add(Var("BgaGradeText", T_STR, "分级文本", "OK"));
        list.Add(Var("BgaOk", T_BOOL, "本轮判定 OK", false));
        list.Add(Var("BgaBallCount", T_INT, "焊球总数（配方）", 196));
        list.Add(Var("BgaReworkCount", T_INT, "复检次数", 0));
        list.Add(Var("BgaRunCount", T_INT, "检测轮次", 0));
        list.Add(Var("BgaFaultCode", T_STR, "故障码", ""));
        list.Add(Var("BgaLineFault", T_BOOL, "设备故障挂起", false));

        list.Add(NetVar("BGA_Trigger", T_SHORT, "PLC 触发计数", 0,
            "PLC_Modbus", "ModbusTcp", "HoldingRegisters", "0", -1, 1, 0, "Int16"));
        list.Add(NetVar("BGA_Ack", T_SHORT, "PLC 完成确认", 0,
            "PLC_Modbus", "ModbusTcp", "HoldingRegisters", "1", -1, 1, 0, "Int16"));
        list.Add(NetVar("BGA_Result", T_BOOL, "PLC 判定结果（线圈）", false,
            "PLC_Modbus", "ModbusTcp", "Coils", "2", 0, 1, 0, "Boolean"));
        list.Add(NetVar("BGA_DefectWord", T_SHORT, "PLC 缺陷片数", 0,
            "PLC_Modbus", "ModbusTcp", "HoldingRegisters", "4", -1, 1, 0, "Int16"));
        list.Add(NetVar("BGA_GradeWord", T_SHORT, "PLC 分级字", 0,
            "PLC_Modbus", "ModbusTcp", "HoldingRegisters", "5", -1, 1, 0, "Int16"));
        list.Add(NetVar("BGA_Alarm", T_BOOL, "PLC 报警（线圈）", false,
            "PLC_Modbus", "ModbusTcp", "Coils", "3", 0, 1, 0, "Boolean"));

        return list;
    }

    static JArray BuildBgaComms() => BuildComms();

    static JArray BuildBgaMotionCards()
    {
        return new JArray
        {
            new JObject
            {
                ["Id"] = G("motion:card").ToString(),
                ["DriverTypeKey"] = "Plugin.Motion.ZMotion.ZMotionCard, Plugin.Motion.ZMotion" + S,
                ["DisplayName"] = "ZMotion_虚拟卡",
                ["Address"] = "127.0.0.1",
                ["CardModel"] = "VPLC532R",
                ["AutoConnect"] = true,
                ["Remarks"] = "正运动虚拟控制器，用于离线演示与联调",
                ["Axes"] = new JArray
                {
                    Axis("X", 0, "X 轴：载台横移"),
                    Axis("Y", 1, "Y 轴：载台纵移"),
                    Axis("Z", 2, "Z 轴：调焦"),
                },
                ["Points"] = new JArray(),
                ["Params"] = new JObject
                {
                    ["DefaultVelocityMmPerS"] = 20.0,
                    ["DefaultAccelMmPerS2"] = 200.0,
                    ["PollIntervalMs"] = 10,
                    ["CommandTimeoutMs"] = 30000,
                    ["AutoReconnect"] = true,
                },
            },
        };
    }

    // ------------------------------------------------------------------
    //  Home / End
    // ------------------------------------------------------------------
    static JObject BuildBgaHomeFlow()
    {
        var steps = new JArray
        {
            MotionEnable("bh:enX", "X轴使能", "X", true, 10000),
            MotionEnable("bh:enY", "Y轴使能", "Y", true, 10000),
            MotionEnable("bh:enZ", "Z轴使能", "Z", true, 10000),
            MotionHome("bh:homeX", "X轴回零", "X", "Home", 20000),
            MotionHome("bh:homeY", "Y轴回零", "Y", "Home", 20000),
            MotionHome("bh:homeZ", "Z轴回零", "Z", "Home", 20000),
            VarAssign("bh:ready", "置系统就绪", "SystemReady", null, "Global", new JValue(true)),
            VarAssign("bh:fault", "清设备故障", "BgaLineFault", null, "Global", new JValue(false)),
            VarAssign("bh:code", "清故障码", "BgaFaultCode", null, "Global", new JValue("")),
            VarAssign("bh:run", "清检测轮次", "BgaRunCount", null, "Global", new JValue(0)),
            Delay("bh:delay", "回零稳定", 300),
        };
        return Flow("Home", "开机回零：三轴使能 → 回零 → 置就绪", 1, 0, 0, steps);
    }

    static JObject BuildBgaEndFlow()
    {
        var steps = new JArray
        {
            MotionStop("be:stopX", "停X轴", "X", "Decelerate", false),
            MotionStop("be:stopY", "停Y轴", "Y", "Decelerate", false),
            MotionStop("be:stopZ", "停Z轴", "Z", "Decelerate", false),
            CommWrite("be:ack", "清PLC确认", "BGA_Ack", new JValue(0), true),
            VarAssign("be:sys", "清系统就绪", "SystemReady", null, "Global", new JValue(false)),
            Delay("be:delay", "收尾延时", 200),
        };
        return Flow("End", "收尾：停轴 → 清 PLC 确认 → 清就绪", 3, 0, 0, steps);
    }

    // ------------------------------------------------------------------
    //  Main：PLC 触发轮询 → 调检测工位 → 统计 → 回写
    // ------------------------------------------------------------------
    static JObject BuildBgaMainFlow()
    {
        var cycle = new JArray
        {
            VarAssign("bm:fault0", "清报警标志", "BgaLineFault", null, "Global", new JValue(false)),
        };
        AddIncVar(cycle, "bm:cycle", "节拍+1", "CycleCount");
        AddIncVar(cycle, "bm:runCount", "轮次+1", "BgaRunCount");

        var run = RunFlow("bm:run", "调用BGA检测", "Bga_Inspect", 60000);
        var fault = new JArray
        {
            VarAssign("bm:fault", "置设备故障", "BgaLineFault", null, "Global", new JValue(true)),
            VarAssign("bm:code", "置故障码", "BgaFaultCode", null, "Global",
                new JValue("Bga_Inspect 执行异常或超时")),
            MotionStop("bm:sx", "停X轴", "X", "Decelerate", false),
            MotionStop("bm:sy", "停Y轴", "Y", "Decelerate", false),
            MotionStop("bm:sz", "停Z轴", "Z", "Decelerate", false),
            CommWrite("bm:alarm", "写PLC报警", "BGA_Alarm", new JValue(true), true),
            ReturnStep("bm:return", "故障返回"),
        };
        cycle.Add(run);
        cycle.Add(IfStep("bm:if", "设备故障判定", "!Ok", G("bm:var:ok"), "Ok", "System.Boolean",
            LStep(run, "Invoked", "调用BGA检测"), fault, new JArray()));

        // 良率 = OK / (OK + NG) × 100
        var total = Action("bm:total", "总计数", "数据处理", P_MATH, "数学计算", "\uf1ec",
            new JObject { ["Operation"] = "Add" },
            new JObject { ["A"] = LG("OKCount"), ["B"] = LG("NGCount") });
        var ratio = Action("bm:ratio", "良率比例", "数据处理", P_MATH, "数学计算", "\uf1ec",
            new JObject { ["Operation"] = "Divide" },
            new JObject { ["A"] = LG("OKCount"), ["B"] = LStep(total, "Result", "总计数") });
        var percent = Action("bm:percent", "良率百分比", "数据处理", P_MATH, "数学计算", "\uf1ec",
            new JObject { ["Operation"] = "Multiply", ["B"] = 100.0 },
            new JObject { ["A"] = LStep(ratio, "Result", "良率比例") });
        cycle.Add(total);
        cycle.Add(ratio);
        cycle.Add(percent);
        cycle.Add(VarAssign("bm:yield", "写良率", "YieldRate", LStep(percent, "Result", "良率百分比"), "Global", null));

        cycle.Add(CommWrite("bm:result", "写PLC结果", "BGA_Result", LG("BgaOk"), true));
        cycle.Add(CommWrite("bm:defects", "写PLC缺陷数", "BGA_DefectWord", LG("BgaDefectCount"), true));
        cycle.Add(CommWrite("bm:grade", "写PLC分级", "BGA_GradeWord", LG("BgaGrade"), true));
        cycle.Add(CommWrite("bm:ack", "写PLC完成确认", "BGA_Ack", new JValue(1), true));
        // 账本落 <方案根>\History\{StepName}\{yyyy-MM}\（相对路径，随方案整包搬迁）
        cycle.Add(CcgCsvRecord("bm:csv", "BGA检测记录", new JArray
        {
            Col("时间", "BuiltIn", "", "Timestamp"),
            Col("序号", "BuiltIn", "", "Sequence"),
            Col("结果", "Variable", "BgaGradeText", "Timestamp"),
            Col("缺陷片数", "Variable", "BgaDefectCount", "Timestamp"),
            Col("缺陷面积", "Variable", "BgaTotalArea", "Timestamp"),
            Col("定位分数", "Variable", "BgaMatchScore", "Timestamp"),
        }));
        cycle.Add(Delay("bm:cycleDelay", "节拍延时", 300));

        var triggerIf = IfStep("bm:trigger", "触发判断", "Trigger >= 1", G("bm:var:trigger"), "Trigger", "System.Int32",
            L("bm:readTrigger", "Value", "读PLC触发"),
            cycle,
            new JArray { Delay("bm:idle", "空闲延时", 200) });

        var faultLogic = LogicVars("bm:faultLogic", "故障状态", "BgaLineFault", "BgaLineFault");
        var steps = new JArray
        {
            CommRead("bm:readTrigger", "读PLC触发", "BGA_Trigger"),
            faultLogic,
            IfStep("bm:faultIf", "故障挂起判断", "Ok", G("bm:var:fault"), "Ok", "System.Boolean",
                LStep(faultLogic, "Result", "故障状态"),
                new JArray
                {
                    Delay("bm:faultDelay", "故障等待", 500),
                    ReturnStep("bm:faultReturn", "故障返回"),
                },
                new JArray()),
            triggerIf,
        };

        return Flow("Main", "BGA 全检主调度：PLC 触发 → 检测工位 → 良率统计 → 结果回写", 2, 96, 0, steps);
    }

    // ------------------------------------------------------------------
    //  Bga_Inspect：视觉检测主体
    // ------------------------------------------------------------------
    static JObject BuildBgaInspectFlow()
    {
        // 1. 取图（缺陷样本）
        var acq = ImgFile("bi:acq", "采集BGA图像", IMG_BGA_DEFECT, 0);

        // 2. 预处理
        var pre = PreProcess("bi:pre", "图像预处理", new JArray
        {
            Op("gauss", ("Size", "5")),
            Op("gray_closing", ("Width", "5"), ("Height", "5")),
        }, 1, "bi:acq", "采集BGA图像");

        // 3. 基准匹配定位：用金样图做模板库参考图，在被测图上找位姿
        var match = Action("bi:match", "基准定位匹配", "定位", P_MATCH, "模板匹配", "\uf140", new JObject
        {
            ["TemplateLibraryJson"] = "[]",
            ["DefaultTemplateName"] = "",
            ["MinScore"] = 0.6,
            ["MaxMatches"] = 1,
            ["MaxOverlap"] = 0.3,
            ["Greediness"] = 0.8,
            ["DisplayViewIndex"] = 1,
            ["OutputAlignedImage"] = true,
            ["UseSearchRegion"] = false,
        }, new JObject
        {
            ["Image"] = L("bi:pre", "Image", "图像预处理"),
            ["RecipeName"] = LG("RecipeName"),
        });

        // 4. 检测 ROI（整块 BGA 区域）
        var roi = CreateRoi("bi:roi", "BGA检测区域", new JArray
        {
            Roi("焊球阵列区", "Rectangle", 240, 185, 0, 205, 165),
        }, 1, "bi:pre", "图像预处理");

        // 5. 差分：被测图 vs 金样图，只看 ROI 内
        var diff = Action("bi:diff", "基准图像差分", "缺陷检测", P_DIFF, "图像差分", "\uf24e", new JObject
        {
            ["Algorithm"] = "Absolute",
            ["Polarity"] = "Both",
            ["Aggregation"] = "Components",
            ["SizeMismatch"] = "Fail",
            ["Threshold"] = 30.0,
            ["MinArea"] = 8.0,
            ["MaxTotalArea"] = 0.0,
            ["MaxComponentArea"] = 0.0,
            ["MaxValueLimit"] = 0.0,
            ["AmbientFloor"] = 8.0,
            ["SignedDisplay"] = 128.0,
            ["DisplayViewIndex"] = 2,
        // 端口名以插件源码为准（Plugins\Plugin.CreateRoi\CreateRoiPlugin.cs:61 只有 MaskRegion 等，
        // 没有名为 "Image" 的输出端口）；掩膜图走 MaskImage。
        }, new JObject
        {
            ["Reference"] = L("bi:match", "AlignedImage", "基准定位匹配"),
            ["Test"] = L("bi:pre", "Image", "图像预处理"),
            ["MaskRegion"] = L("bi:roi", "MaskRegion", "BGA检测区域"),
        });

        // 6. Blob 缺陷检出（把差异图上的缺陷片数、面积、最大单缺陷面积量化）
        //    缺陷量化吃的是"差分图"，不是原始预处理图：ImageDiff 的输出端口叫 DiffImage。
        var blob = BlobDetect("bi:blob", "缺陷量化", "bi:roi", "BGA检测区域", 0.05, 2,
            "bi:diff", "基准图像差分");
        blob["LinkedSources"]!["SrcImage"] = L("bi:diff", "DiffImage", "基准图像差分");
        // BlobDetect 的掩膜输入端口叫 MaskRegion（BlobDetectPlugin.cs:44），
        // 上游 CreateRoi 的输出端口也叫 MaskRegion（CreateRoiPlugin.cs:61）。
        blob["LinkedSources"]!["MaskRegion"] = L("bi:roi", "MaskRegion", "BGA检测区域");

        // 7. 写关键指标到全局变量
        var writeDefect = VarAssign("bi:wDefect", "写缺陷片数", "BgaDefectCount",
            L("bi:diff", "DiffCount", "基准图像差分"), "Global", null);
        var writeArea = VarAssign("bi:wArea", "写缺陷总面积", "BgaTotalArea",
            L("bi:diff", "DiffArea", "基准图像差分"), "Global", null);
        var writeScore = VarAssign("bi:wScore", "写匹配分数", "BgaMatchScore",
            L("bi:match", "Score", "基准定位匹配"), "Global", null);

        // 8. 分级：默认 OK → 有差异则复检 → 差异片数超限则 NG
        var cmpAny = Compare("bi:cmpAny", "存在差异片", "bi:diff", "基准图像差分",
            "DiffCount", 0, "GreaterThan");
        var cmpLimit = Compare("bi:cmpLimit", "差异片数超限", "bi:diff", "基准图像差分",
            "DiffCount", 3, "GreaterThan");

        var setNg = new JArray
        {
            VarAssign("bi:g2", "置NG分级", "BgaGrade", null, "Global", new JValue(2)),
            VarAssign("bi:g2t", "置NG文本", "BgaGradeText", null, "Global", new JValue("NG")),
            VarAssign("bi:g2o", "置NG判定", "BgaOk", null, "Global", new JValue(false)),
        };
        var setRework = new JArray
        {
            VarAssign("bi:g1", "置复检分级", "BgaGrade", null, "Global", new JValue(1)),
            VarAssign("bi:g1t", "置复检文本", "BgaGradeText", null, "Global", new JValue("复检")),
            VarAssign("bi:g1o", "置复检判定", "BgaOk", null, "Global", new JValue(false)),
        };
        AddIncVar(setRework, "bi:rework", "复检计数", "BgaReworkCount");

        var gradeRework = IfStep("bi:gradeRework", "判定复检", "Ok", G("bi:var:gr"), "Ok", "System.Boolean",
            LStep(cmpAny, "Result", "存在差异片"), setRework, new JArray());

        var setOk = new JArray
        {
            VarAssign("bi:g0", "置OK分级", "BgaGrade", null, "Global", new JValue(0)),
            VarAssign("bi:g0t", "置OK文本", "BgaGradeText", null, "Global", new JValue("OK")),
            VarAssign("bi:g0o", "置OK判定", "BgaOk", null, "Global", new JValue(true)),
        };

        var gradeNg = IfStep("bi:gradeNg", "判定NG", "Ok", G("bi:var:gn"), "Ok", "System.Boolean",
            LStep(cmpLimit, "Result", "差异片数超限"), setNg, setOk);

        // 初始化分级
        var initGrade = new JArray
        {
            VarAssign("bi:initG", "初始化分级", "BgaGrade", null, "Global", new JValue(0)),
            VarAssign("bi:initT", "初始化文本", "BgaGradeText", null, "Global", new JValue("OK")),
            VarAssign("bi:initO", "初始化判定", "BgaOk", null, "Global", new JValue(true)),
            VarAssign("bi:initD", "清缺陷片数", "BgaDefectCount", null, "Global", new JValue(0)),
            VarAssign("bi:initA", "清缺陷面积", "BgaTotalArea", null, "Global", new JValue(0.0)),
        };

        // 9. 计数分流
        var okBranch = new JArray();
        AddIncVar(okBranch, "bi:okCount", "良品计数", "OKCount");
        var okIf = IfStep("bi:okIf", "良品分流", "Ok", G("bi:var:okIf"), "Ok", "System.Boolean",
            LG("BgaOk"), okBranch, new JArray());

        var ngBranch = new JArray();
        AddIncVar(ngBranch, "bi:ngCount", "不良计数", "NGCount");
        // NG 判定直接用比较 2 个缺陷片数，不再经 CompareGlobal：
        // 该重载在本次传入 JValue 实参时没有真正产出一个步骤（步骤树上根本看不到"分级为NG"），
        // 于是 ngIf 连到了一个不存在的上游节点。
        var ngCmp = Compare("bi:ngCmp", "分级为NG", "bi:diff", "基准图像差分", "DiffCount", 2, "GreaterThan");
        // IfStep 的 varId 就是它的局部变量 Id，必须与上游比较步骤的 seed 对齐。
        var ngIf = IfStep("bi:ngIf", "不良分流", "Ok", G("bi:ngCmp"), "Ok", "System.Boolean",
            LStep(ngCmp, "Result", "分级为NG"), ngBranch, new JArray());

        // 10. 结果上屏
        var display = ResultDisplay("bi:display", "BGA检测结果", new[]
        {
            ("r1", "缺陷片数", "bi:diff", "基准图像差分", "DiffCount"),
            ("r2", "缺陷面积", "bi:diff", "基准图像差分", "DiffArea"),
            ("r3", "最大单缺陷", "bi:diff", "基准图像差分", "MaxDiffComponentArea"),
            ("r4", "定位分数", "bi:match", "基准定位匹配", "Score"),
            ("r5", "缺陷个数", "bi:blob", "缺陷量化", "DefectCount"),
        }, 3, "bi:diff", "基准图像差分", "DiffImage");

        // initGrade 是 JArray（5 个变量赋值），必须逐项展开 ——
        // 直接 steps.Add(initGrade) 会序列化成 "Steps": [[ {...} ]]，反序列化时
        // 报 Cannot deserialize the current JSON array into type 'StepModel'。
        var steps = new JArray();
        AddRange(steps, initGrade);
        steps.Add(acq);
        steps.Add(pre);
        steps.Add(match);
        steps.Add(roi);
        steps.Add(diff);
        steps.Add(blob);
        steps.Add(writeDefect);
        steps.Add(writeArea);
        steps.Add(writeScore);
        steps.Add(cmpAny);
        steps.Add(cmpLimit);
        steps.Add(gradeRework);
        steps.Add(gradeNg);
        steps.Add(display);
        steps.Add(okIf);
        steps.Add(ngCmp);
        steps.Add(ngIf);
        steps.Add(Delay("bi:delay", "检测节拍", 100));

        return Flow("Bga_Inspect", "BGA 全检：采集/预处理/定位/ROI/差分/量化/分级/上屏", 0, 96, 0, steps);
    }

    // ------------------------------------------------------------------
    //  Recipe：换型 / 计数器清零
    // ------------------------------------------------------------------
    static JObject BuildBgaRecipeFlow()
    {
        var steps = new JArray
        {
            VarAssign("br:name", "写配方名", "RecipeName", null, "Global", new JValue("BGA_14x14_A")),
            VarAssign("br:balls", "写焊球总数", "BgaBallCount", null, "Global", new JValue(196)),
            VarAssign("br:ok", "清零良品", "OKCount", null, "Global", new JValue(0)),
            VarAssign("br:ng", "清零不良", "NGCount", null, "Global", new JValue(0)),
            VarAssign("br:cyc", "清零节拍", "CycleCount", null, "Global", new JValue(0)),
            VarAssign("br:run", "清零轮次", "BgaRunCount", null, "Global", new JValue(0)),
            VarAssign("br:rew", "清复检次数", "BgaReworkCount", null, "Global", new JValue(0)),
            VarAssign("br:gra", "复位分级", "BgaGrade", null, "Global", new JValue(0)),
            VarAssign("br:graT", "复位分级文本", "BgaGradeText", null, "Global", new JValue("OK")),
            VarAssign("br:def", "清缺陷片数", "BgaDefectCount", null, "Global", new JValue(0)),
            VarAssign("br:area", "清缺陷面积", "BgaTotalArea", null, "Global", new JValue(0.0)),
            VarAssign("br:yield", "重置良率", "YieldRate", null, "Global", new JValue(100.0)),
            Delay("br:delay", "配方应用", 100),
        };
        return Flow("Recipe", "配方切换：焊球规格 / 计数器清零 / 良率重置", 0, 96, 0, steps);
    }

    // ==================================================================
    //  组态画面：4 页
    // ==================================================================
    static JObject BuildBgaScada()
    {
        var pages = new JArray
        {
            BgaPageOverview(),
            BgaPageLive(),
            BgaPageLocate(),
            BgaPageRecipe(),
        };

        var alarms = new JArray
        {
            ScadaAlarm("设备故障挂起", "BgaLineFault", 4, 0.0, 2,
                "设备故障：检测流程异常或超时，产线已暂停",
                "设备", "BGA 检测工位返回异常/超时", "检查相机与光源，复位后重新启动"),
            ScadaAlarm("定位分数偏低", "BgaMatchScore", 2, 0.55, 1,
                "基准定位分数低于 0.55，疑似产品偏移或缺件",
                "视觉", "物料放置偏移 / 金样图不匹配", "确认产品摆放与配方模板是否一致"),
            ScadaAlarm("缺陷片数超限", "BgaDefectCount", 0, 50.0, 0,
                "本轮缺陷片数高于 50，观察趋势",
                "工艺", "贴装/焊接工艺波动", "查看缺陷趋势图与最近批次记录"),
        };

        return new JObject
        {
            ["SchemaVersion"] = 1,
            ["Pages"] = pages,
            ["Alarms"] = alarms,
            ["VariableEvents"] = new JArray(),
            ["StartupPageId"] = G("page:BGA运行总览").ToString(),
            ["BasePages"] = new JArray(),
        };
    }

    static void BgaHeader(JArray els, string page, string title, string subtitle)
    {
        els.Add(El(page, "标题栏", "Hmi.Rectangle", 0, 0, 1920, 80, 1, new JObject
        {
            ["Fill"] = "#FF111827",
            ["Stroke"] = "#FF1F2937",
            ["StrokeThickness"] = "1",
            ["CornerRadius"] = "0",
        }));
        els.Add(El(page, "标题", "Hmi.Text", 40, 16, 900, 48, 2, TextProps(title, 26, "#FFF8FAFC", "Bold")));
        els.Add(El(page, "副标题", "Hmi.Text", 40, 82, 1200, 24, 2, TextProps(subtitle, 13, "#FF94A3B8")));
        els.Add(El(page, "时钟", "Hmi.Clock", 1520, 18, 360, 44, 2,
            TextProps("", 20, "#FFE2E8F0", "SemiBold")));
        els.Add(El(page, "报警条", "Hmi.AlarmBanner", 0, 112, 1920, 48, 3, new JObject
        {
            ["MaxRows"] = "2",
            ["EmptyText"] = "无报警",
            ["NormalColor"] = "#FF16A34A",
            ["InfoColor"] = "#FF0EA5E9",
            ["WarningColor"] = "#FFF59E0B",
            ["CriticalColor"] = "#FFEF4444",
        }));
    }

    static JObject BgaCard(string page, string name, double x, double y, double w, double h)
    {
        var card = El(page, name, "Hmi.Rectangle", x, y, w, h, 4, new JObject
        {
            ["Fill"] = "#FF111C2E",
            ["Stroke"] = "#FF1E3A5F",
            ["StrokeThickness"] = "1",
            ["CornerRadius"] = "8",
        });
        return card;
    }

    static void BgaCardTitle(JArray els, string page, string name, double x, double y, string title)
    {
        els.Add(El(page, name, "Hmi.Text", x, y, 600, 28, 5, TextProps(title, 15, "#FF7DD3FC", "SemiBold")));
    }

    static JObject BgaPageOverview()
    {
        const string P = "BGA运行总览";
        var els = new JArray();
        BgaHeader(els, P, "BGA 焊球全检线 · 运行总览", "工位 1 / 精密电子事业部 · 配方 BGA_14x14_A");

        // 顶部 KPI
        els.Add(El(P, "KPI主", "Hmi.KpiBoard", 40, 180, 1160, 150, 6, new JObject
        {
            ["Columns"] = "4",
            ["Label1"] = "检测总数",
            ["Value1"] = "0",
            ["Unit1"] = "pcs",
            ["Label2"] = "良品数",
            ["Value2"] = "0",
            ["Unit2"] = "pcs",
            ["Label3"] = "不良数",
            ["Value3"] = "0",
            ["Unit3"] = "pcs",
            ["Label4"] = "良率",
            ["Value4"] = "0",
            ["Unit4"] = "%",
            ["Fill"] = "#FF111C2E",
            ["Stroke"] = "#FF1E3A5F",
            ["CornerRadius"] = "8",
            ["ValueColor"] = "#FFF8FAFC",
            ["LabelColor"] = "#FF94A3B8",
        }, new JArray
        {
            Bind("Value1", "CycleCount"),
            Bind("Value2", "OKCount"),
            Bind("Value3", "NGCount"),
            Bind("Value4", "YieldRate", "F1"),
        }));

        // 判定结果大牌
        els.Add(El(P, "判定底板", "Hmi.Rectangle", 1230, 180, 650, 150, 5, new JObject
        {
            ["Fill"] = "#FF0F172A",
            ["Stroke"] = "#FF1E3A5F",
            ["StrokeThickness"] = "1",
            ["CornerRadius"] = "8",
        }));
        els.Add(El(P, "判定标题", "Hmi.Text", 1260, 196, 200, 26, 6,
            TextProps("本轮判定", 14, "#FF94A3B8")));
        els.Add(El(P, "判定大字", "Hmi.Text", 1260, 226, 300, 70, 7,
            TextProps("OK", 52, "#FF22C55E", "Bold"), new JArray { Bind("Text", "BgaGradeText") }));
        els.Add(Lamp(P, "总状态灯", 1560, 214, "BgaOk", "判定"));
        els.Add(El(P, "分级说明", "Hmi.Text", 1580, 250, 280, 24, 7,
            TextProps("0=OK  1=复检  2=NG", 12, "#FF64748B")));

        // 三个状态卡
        BgaCardTitle(els, P, "卡1标题", 40, 356, "① 采集与定位");
        els.Add(BgaCard(P, "卡1", 40, 386, 560, 210));
        els.Add(Indicator(P, "就绪指示", 80, 420, "SystemReady", "系统就绪"));
        els.Add(Indicator(P, "故障指示", 180, 420, "BgaLineFault", "设备故障"));
        els.Add(El(P, "定位分标签", "Hmi.Text", 300, 420, 160, 24, 6,
            TextProps("定位分数", 13, "#FF94A3B8")));
        els.Add(IOField(P, "定位分数", 300, 448, "BgaMatchScore", "", "F3"));
        els.Add(El(P, "轮次标签", "Hmi.Text", 300, 496, 160, 24, 6,
            TextProps("检测轮次", 13, "#FF94A3B8")));
        els.Add(IOField(P, "检测轮次", 300, 524, "BgaRunCount", "", "F0"));

        BgaCardTitle(els, P, "卡2标题", 620, 356, "② 缺陷量化");
        els.Add(BgaCard(P, "卡2", 620, 386, 560, 210));
        els.Add(El(P, "缺陷数标签", "Hmi.Text", 660, 420, 200, 24, 6,
            TextProps("缺陷片数", 13, "#FF94A3B8")));
        els.Add(IOField(P, "缺陷片数", 660, 448, "BgaDefectCount", "", "F0"));
        els.Add(El(P, "面积标签", "Hmi.Text", 660, 496, 200, 24, 6,
            TextProps("缺陷总面积(px)", 13, "#FF94A3B8")));
        els.Add(IOField(P, "缺陷总面积", 660, 524, "BgaTotalArea", "", "F0"));
        els.Add(El(P, "复检标签", "Hmi.Text", 900, 420, 160, 24, 6,
            TextProps("复检次数", 13, "#FF94A3B8")));
        els.Add(IOField(P, "复检次数", 900, 448, "BgaReworkCount", "", "F0"));

        BgaCardTitle(els, P, "卡3标题", 1200, 356, "③ 设备与触发");
        els.Add(BgaCard(P, "卡3", 1200, 386, 680, 210));
        els.Add(El(P, "轴面板", "Hmi.AxisPanel", 1220, 410, 300, 170, 6, new JObject
        {
            ["AxisName"] = "X",
            ["Title"] = "X 轴",
            ["ShowPoints"] = "False",
            ["ShowCommands"] = "False",
        }));
        els.Add(El(P, "触发标签", "Hmi.Text", 1560, 420, 160, 24, 6,
            TextProps("PLC 触发", 13, "#FF94A3B8")));
        els.Add(IOField(P, "PLC触发", 1560, 448, "BGA_Trigger", "", "F0"));
        els.Add(El(P, "确认标签", "Hmi.Text", 1560, 496, 160, 24, 6,
            TextProps("PLC 确认", 13, "#FF94A3B8")));
        els.Add(IOField(P, "PLC确认", 1560, 524, "BGA_Ack", "", "F0"));

        // 趋势图
        BgaCard(P, "趋势底板", 40, 620, 1840, 380);
        BgaCardTitle(els, P, "趋势标题", 70, 636, "缺陷片数与匹配分数趋势（最近 200 拍）");
        els.Add(El(P, "趋势图", "Hmi.MultiTrend", 60, 672, 1800, 310, 6, new JObject
        {
            ["Title"] = "缺陷片数 / 定位分数",
            ["MaxCurves"] = "2",
            ["WindowSeconds"] = "200",
            ["SharedScale"] = "False",
            ["ShowLegend"] = "True",
            ["Background"] = "#FF0B1424",
            ["Stroke"] = "#FF1E3A5F",
            ["CornerRadius"] = "6",
            ["Curve1Name"] = "缺陷片数",
            ["Curve1Color"] = "#FFF59E0B",
            ["Curve2Name"] = "定位分数",
            ["Curve2Color"] = "#FF38BDF8",
        }, new JArray { Bind("Value1", "BgaDefectCount"), Bind("Value2", "BgaMatchScore") }));

        // 底部操作条
        els.Add(NavButton(P, "去实时检测", 40, 1030, "BGA实时检测"));
        els.Add(NavButton(P, "去定位跟踪", 200, 1030, "BGA定位跟踪"));
        els.Add(NavButton(P, "去配方参数", 360, 1030, "BGA配方参数"));
        els.Add(TestButton(P, "运行主流程", 560, 1024, "Main"));
        els.Add(El(P, "停止按钮", "Hmi.Button", 740, 1024, 160, 40, 20,
            ButtonProps("停止产线", "#FFDC2626", "#FF991B1B"), null,
            new JArray { Hook(2, new JArray { ScadaAct(5, flowName: "Main") }) }));
        els.Add(El(P, "复位按钮", "Hmi.Button", 920, 1024, 160, 40, 20,
            ButtonProps("复位故障", "#FF475569", "#FF334155"), null,
            new JArray
            {
                Hook(2, new JArray
                {
                    ScadaAct(2, varName: "BgaLineFault", value: "False"),
                    ScadaAct(2, varName: "BgaFaultCode", value: ""),
                }),
            }));

        return ScadaPageObj(P, 1920, 1080, els);
    }

    static JObject BgaPageLive()
    {
        const string P = "BGA实时检测";
        var els = new JArray();
        BgaHeader(els, P, "BGA 焊接质量 · 实时检测画面", "缺陷图（左）与金样图（右）对照 · 差分结果实时刷新");

        els.Add(El(P, "缺陷图标题", "Hmi.Text", 60, 180, 500, 28, 5,
            TextProps("被测图（bga_14x14_defects）", 14, "#FF94A3B8")));
        els.Add(El(P, "缺陷图", "Hmi.Picture", 60, 214, 880, 676, 6, new JObject
        {
            ["SourcePath"] = IMG_BGA_DEFECT,
            ["Stretch"] = "Uniform",
            ["PlaceholderText"] = "无图提示",
            ["Fill"] = "#FF0F172A",
            ["Stroke"] = "#FF1E3A5F",
            ["StrokeThickness"] = "1",
            ["CornerRadius"] = "8",
        }));

        els.Add(El(P, "金样图标题", "Hmi.Text", 980, 180, 500, 28, 5,
            TextProps("金样图（bga_14x14_model）", 14, "#FF94A3B8")));
        els.Add(El(P, "金样图", "Hmi.Picture", 980, 214, 880, 676, 6, new JObject
        {
            ["SourcePath"] = IMG_BGA_MODEL,
            ["Stretch"] = "Uniform",
            ["Fill"] = "#FF0F172A",
            ["Stroke"] = "#FF1E3A5F",
            ["StrokeThickness"] = "1",
            ["CornerRadius"] = "8",
        }));

        // 右侧指标条
        BgaCard(P, "指标底板", 60, 910, 1800, 150);
        BgaCardTitle(els, P, "指标标题", 90, 926, "本轮结果");
        els.Add(IOField(P, "缺陷片数", 90, 962, "BgaDefectCount", "缺陷片数", "F0"));
        els.Add(IOField(P, "缺陷面积", 330, 962, "BgaTotalArea", "缺陷总面积", "F0"));
        els.Add(IOField(P, "定位分", 570, 962, "BgaMatchScore", "定位分数", "F3"));
        els.Add(El(P, "分级文本", "Hmi.Text", 820, 962, 300, 40, 7,
            TextProps("OK", 30, "#FF22C55E", "Bold"), new JArray { Bind("Text", "BgaGradeText") }));
        els.Add(Lamp(P, "判定灯", 1140, 950, "BgaOk", "判定"));
        els.Add(WriteButton(P, "单次检测", 1290, 968, "BgaRunCount", "0", "#FF0EA5E9"));
        els.Add(NavButton(P, "返回总览", 1480, 966, "BGA运行总览"));

        return ScadaPageObj(P, 1920, 1080, els);
    }

    static JObject BgaPageLocate()
    {
        const string P = "BGA定位跟踪";
        var els = new JArray();
        BgaHeader(els, P, "BGA 定位跟踪与轴控", "基准匹配位姿 → 三轴载台联动");

        els.Add(El(P, "轴控台", "Hmi.AxisConsole", 60, 190, 320, 470, 6, new JObject
        {
            ["AxisName"] = "X",
            ["Title"] = "载台轴控",
            ["ShowPoints"] = "True",
            ["ShowCommands"] = "True",
            ["ShowAxisSelector"] = "True",
        }));

        BgaCard(P, "位姿底板", 420, 190, 640, 470);
        BgaCardTitle(els, P, "位姿标题", 450, 210, "匹配位姿结果");
        els.Add(El(P, "位姿说明", "Hmi.Text", 450, 246, 560, 60, 6, new JObject
        {
            ["Text"] = "模板匹配输出：中心行/列、角度、分数。\n分数低于配方下限即判定位失败，转入人工复检。",
            ["Foreground"] = "#FF94A3B8",
            ["FontSize"] = "13",
            ["TextAlignment"] = "Left",
            ["VerticalContentAlignment"] = "Top",
        }));
        els.Add(El(P, "分数标签", "Hmi.Text", 450, 320, 200, 24, 6,
            TextProps("匹配分数", 13, "#FF94A3B8")));
        els.Add(IOField(P, "匹配分数", 450, 348, "BgaMatchScore", "", "F3"));
        els.Add(El(P, "缺陷标签", "Hmi.Text", 450, 396, 200, 24, 6,
            TextProps("同时检出缺陷片数", 13, "#FF94A3B8")));
        els.Add(IOField(P, "缺陷片数", 450, 424, "BgaDefectCount", "", "F0"));
        els.Add(El(P, "轮次标签", "Hmi.Text", 730, 320, 200, 24, 6,
            TextProps("检测轮次", 13, "#FF94A3B8")));
        els.Add(IOField(P, "轮次", 730, 348, "BgaRunCount", "", "F0"));
        els.Add(El(P, "配方标签", "Hmi.Text", 730, 396, 200, 24, 6,
            TextProps("当前配方", 13, "#FF94A3B8")));
        els.Add(TextField(P, "配方名", 730, 424, 240, "RecipeName", ""));

        // 点动 / 定位
        BgaCard(P, "点动底板", 1100, 190, 760, 470);
        BgaCardTitle(els, P, "点动标题", 1130, 210, "载台点动与定位（每按一次移动 5mm）");
        els.Add(MotionButton(P, "X正向", 1130, 260, 9, "X", 5.0, 20.0));
        els.Add(MotionButton(P, "X负向", 1130, 310, 9, "X", -5.0, 20.0));
        els.Add(MotionButton(P, "Y正向", 1290, 260, 9, "Y", 5.0, 20.0));
        els.Add(MotionButton(P, "Y负向", 1290, 310, 9, "Y", -5.0, 20.0));
        els.Add(MotionButton(P, "Z正向", 1450, 260, 9, "Z", 2.0, 10.0));
        els.Add(MotionButton(P, "Z负向", 1450, 310, 9, "Z", -2.0, 10.0));
        els.Add(El(P, "回零按钮", "Hmi.Button", 1130, 380, 160, 40, 20,
            ButtonProps("三轴回零", "#FF0EA5E9", "#FF0369A1"), null,
            new JArray { Hook(2, new JArray { ScadaAct(6, flowName: "Home") }, confirm: true) }));
        els.Add(El(P, "停轴按钮", "Hmi.Button", 1310, 380, 160, 40, 20,
            ButtonProps("紧急停止", "#FFDC2626", "#FF991B1B"), null,
            new JArray { Hook(2, new JArray { ScadaAct(5, flowName: "Main") }) }));
        els.Add(El(P, "点动说明", "Hmi.Text", 1130, 440, 700, 90, 6, new JObject
        {
            ["Text"] = "提示：运动命令只走动作表 → MotionCommandGate → 队列，下发不等于到位。\n" +
                       "到位与否请读轴状态「InPosition」，不要用按钮颜色推断。",
            ["Foreground"] = "#FF64748B",
            ["FontSize"] = "13",
            ["TextAlignment"] = "Left",
            ["VerticalContentAlignment"] = "Top",
        }));

        els.Add(NavButton(P, "返回总览", 60, 690, "BGA运行总览"));
        els.Add(NavButton(P, "去实时检测", 220, 690, "BGA实时检测"));

        return ScadaPageObj(P, 1920, 1080, els);
    }

    static JObject BgaPageRecipe()
    {
        const string P = "BGA配方参数";
        var els = new JArray();
        BgaHeader(els, P, "BGA 配方与参数", "换型、阈值与计数器管理");

        BgaCard(P, "配方底板", 60, 190, 880, 340);
        BgaCardTitle(els, P, "配方标题", 90, 210, "配方信息");
        els.Add(El(P, "配方名标签", "Hmi.Text", 90, 250, 200, 24, 6,
            TextProps("当前配方", 13, "#FF94A3B8")));
        els.Add(TextField(P, "配方名", 90, 278, 320, "RecipeName", ""));
        els.Add(El(P, "球数标签", "Hmi.Text", 90, 326, 200, 24, 6,
            TextProps("焊球总数", 13, "#FF94A3B8")));
        els.Add(IOField(P, "球数", 90, 354, "BgaBallCount", "", "F0"));
        els.Add(El(P, "良率标签", "Hmi.Text", 450, 250, 200, 24, 6,
            TextProps("当前良率", 13, "#FF94A3B8")));
        els.Add(IOField(P, "良率", 450, 278, "YieldRate", "", "F2"));
        els.Add(El(P, "节拍标签", "Hmi.Text", 450, 326, 200, 24, 6,
            TextProps("节拍计数", 13, "#FF94A3B8")));
        els.Add(IOField(P, "节拍", 450, 354, "CycleCount", "", "F0"));
        els.Add(El(P, "应用配方", "Hmi.Button", 90, 420, 180, 40, 20,
            ButtonProps("应用配方", "#FF0EA5E9", "#FF0369A1"), null,
            new JArray { Hook(2, new JArray { ScadaAct(6, flowName: "Recipe") }, confirm: true) }));
        els.Add(El(P, "计数器清零", "Hmi.Button", 290, 420, 180, 40, 20,
            ButtonProps("计数器清零", "#FF475569", "#FF334155"), null,
            new JArray
            {
                Hook(2, new JArray
                {
                    ScadaAct(2, varName: "OKCount", value: "0"),
                    ScadaAct(2, varName: "NGCount", value: "0"),
                    ScadaAct(2, varName: "CycleCount", value: "0"),
                    ScadaAct(2, varName: "YieldRate", value: "100"),
                }, confirm: true),
            }));

        BgaCard(P, "阈值底板", 980, 190, 880, 340);
        BgaCardTitle(els, P, "阈值标题", 1010, 210, "判定阈值（改完请在流程里同步）");
        els.Add(El(P, "阈值说明", "Hmi.Text", 1010, 248, 820, 200, 6, new JObject
        {
            ["Text"] = "• 差分阈值 Threshold = 30（灰度差）\n" +
                       "• 最小差异面积 MinArea = 8 px\n" +
                       "• 定位分数下限 MinScore = 0.60\n" +
                       "• 分级：0 片 → OK；1~3 片 → 复检；> 3 片 → NG\n\n" +
                       "上述参数落在「Bga_Inspect」流程的步骤配置里，\n" +
                       "本页只做运行期查看，避免与流程配置出现两份事实源。",
            ["Foreground"] = "#FFCBD5E1",
            ["FontSize"] = "14",
            ["TextAlignment"] = "Left",
            ["VerticalContentAlignment"] = "Top",
        }));

        BgaCard(P, "日志底板", 60, 560, 1800, 250);
        BgaCardTitle(els, P, "日志标题", 90, 580, "报警与事件");
        els.Add(El(P, "报警列表", "Hmi.AlarmBanner", 90, 620, 1740, 160, 6, new JObject
        {
            ["MaxRows"] = "5",
            ["EmptyText"] = "当前无报警",
            ["NormalColor"] = "#FF16A34A",
            ["InfoColor"] = "#FF0EA5E9",
            ["WarningColor"] = "#FFF59E0B",
            ["CriticalColor"] = "#FFEF4444",
        }));

        els.Add(NavButton(P, "返回总览", 60, 850, "BGA运行总览"));
        els.Add(NavButton(P, "去定位跟踪", 220, 850, "BGA定位跟踪"));

        return ScadaPageObj(P, 1920, 1080, els);
    }
}