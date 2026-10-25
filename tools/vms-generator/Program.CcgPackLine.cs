using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// 方案 B：包装产线三工位协同管控（泡罩/装盒/贴标），含逐件随机 SN 与数据表格。
/// 与方案 A 的差别集中在三处：① 三个工位各有一条独立工位流程，由 Main 串行调用；
/// ② 每件产品生成一个 SN，贯穿三个工位的检测记录；③ 用 Hmi.DataGrid 把最近 N 件
/// 的 SN/工位结果/判定摆成一张表（采行信号驱动，只做显示缓冲，留痕走 CSV 记录）。
/// </summary>
internal static partial class Program
{
    // ---------- 方案 B 的样图：方案内相对路径 Image\xxx.png（源文件生成期从样图库复制进方案） ----------
    const string B_IMG_BLISTER = @"Image\blister_01.png";
    const string B_IMG_BOX = @"Image\cookie_box_01.png";
    const string B_IMG_LABEL = @"Image\label_01.png";

    /// <summary>
    /// 生成方案 B 的完整 SolutionModel JSON。
    /// 结构：Home / Main / St1_Blister / St2_Carton / St3_Label / Recipe / End 共 7 条流程。
    /// </summary>
    static JObject BuildCcgPackLineSolution()
    {
        var vars = BuildBVariables();
        var flows = new JArray
        {
            Flow("Home", "回零与初始化：清计数器、清故障、置就绪", 1, 96, 0, BuildBHomeFlow()),
            Flow("Main", "产线主调度：按节拍串行驱动三个工位并汇总判定", 2, 96, 0, BuildBMainFlow()),
            Flow("St1_Blister", "工位1 泡罩检测：金样差分 + 斑点缺陷 + 颜色校验", 0, 96, 0, BuildBSt1Flow()),
            Flow("St2_Carton", "工位2 装盒检测：模板匹配 + 差异 + OCR 校验盒面文字", 0, 96, 0, BuildBSt2Flow()),
            Flow("St3_Label", "工位3 贴标校验：标签模板匹配 + 几何关系 + 读码", 0, 96, 0, BuildBSt3Flow()),
            Flow("Recipe", "配方与 SN 池管理：换型、清计数、重新播种 SN", 0, 96, 0, BuildBRecipeFlow()),
            Flow("End", "停机收尾：停轴、落盘、置未就绪", 3, 0, 0, BuildBEndFlow(), enabled: false),
        };

        return new JObject
        {
            ["SolutionName"] = "CCG-包装线全检追溯",
            ["Version"] = 1.0,
            ["Config"] = new JObject { ["ImageViewMode"] = 0 },
            ["Flows"] = flows,
            ["SolutionId"] = G("pkg:solution").ToString(),
            ["CommunicationConfigs"] = BuildComms(),
            ["IoBuses"] = new JArray(),
            ["IoDevices"] = new JArray(),
            ["CameraConfigs"] = new JArray(),
            ["MotionCards"] = BuildMotionCards(),
            ["CamTables"] = new JArray(),
            ["VariableSnapshots"] = vars,
            ["VariableGroups"] = new JArray(),
            ["WatchItems"] = new JArray(),
            ["Scada"] = BuildBScada(),
        };
    }

    // ------------------------------------------------------------------ 变量

    static JArray BuildBVariables()
    {
        var list = BuildVariables();
        list.Add(Var("PkgProductSn", T_STR, "当前产品 SN（逐件生成）", ""));
        list.Add(Var("PkgSnSeq", T_INT, "SN 流水序号", 0));
        list.Add(Var("PkgSeed", T_INT, "SN 随机种子（换型可重播种）", 20261024));
        list.Add(Var("PkgLineOk", T_BOOL, "整线汇总判定：三工位全过", false));

        list.Add(Var("St1BlisterOk", T_BOOL, "工位1 泡罩判定", false));
        list.Add(Var("St1DefectCount", T_INT, "工位1 缺陷片数", 0));
        list.Add(Var("St1TotalArea", T_DBL, "工位1 缺陷总面积", 0.0));
        list.Add(Var("St1ColorOk", T_BOOL, "工位1 颜色校验通过", false));

        list.Add(Var("St2CartonOk", T_BOOL, "工位2 装盒判定", false));
        list.Add(Var("St2DiffCount", T_INT, "工位2 与金样差异块数", 0));
        list.Add(Var("St2MatchScore", T_DBL, "工位2 定位分数", 0.0));
        list.Add(Var("St2OcrText", T_STR, "工位2 盒面 OCR 文本", ""));

        list.Add(Var("St3LabelOk", T_BOOL, "工位3 贴标判定", false));
        list.Add(Var("St3MatchScore", T_DBL, "工位3 标签定位分数", 0.0));
        list.Add(Var("St3OffsetMm", T_DBL, "工位3 标签相对偏差(mm)", 0.0));
        list.Add(Var("St3Barcode", T_STR, "工位3 读到的条码", ""));

        list.Add(Var("PkgFaultCode", T_INT, "产线故障码（0=无）", 0));
        list.Add(Var("PkgLineFault", T_BOOL, "产线故障挂起", false));
        list.Add(Var("PkgGradeText", T_STR, "本件判定文本", "待检"));
        list.Add(Var("PkgGoodCount", T_INT, "良品累计", 0));
        list.Add(Var("PkgNgCount", T_INT, "不良累计", 0));
        list.Add(Var("PkgYield", T_DBL, "良率(%)", 0.0));
        list.Add(Var("PkgCycleCount", T_INT, "累计节拍数", 0));
        list.Add(Var("PkgHistoryRows", T_INT, "数据表格已采行数", 0));

        // 表格的采行信号：每写入一行 SN 记录就翻转一次（上升沿触发表格采行）
        list.Add(Var("PkgRowPush", T_BOOL, "表格采行信号", false));

        // 网络变量：与包装线主 PLC 交互
        list.Add(NetVar("PKG_Trigger", T_BOOL, "PLC 放料完成触发", false, "PLC_Modbus", "ModbusTcp", "HoldingRegisters", "10", -1, 1, 1, "Boolean"));
        list.Add(NetVar("PKG_Ack", T_BOOL, "结果已送出应答", false, "PLC_Modbus", "ModbusTcp", "HoldingRegisters", "11", -1, 1, 1, "Boolean"));
        list.Add(NetVar("PKG_Ok", T_BOOL, "整件 OK 信号", false, "PLC_Modbus", "ModbusTcp", "Coils", "12", -1, 1, 1, "Boolean"));
        list.Add(NetVar("PKG_Alarm", T_BOOL, "产线故障信号", false, "PLC_Modbus", "ModbusTcp", "Coils", "13", -1, 1, 1, "Boolean"));
        list.Add(NetVar("PKG_StationWord", T_INT, "三工位结果位图（bit0..2）", 0, "PLC_Modbus", "ModbusTcp", "HoldingRegisters", "14", -1, 1, 1, "Int16"));
        return list;
    }

    // ------------------------------------------------------------------ Home

    static JArray BuildBHomeFlow()
    {
        var s = new JArray();
        s.Add(VarAssign("bh:sn", "SN 序号清零", "PkgSnSeq", null, "Global", new JValue(0)));
        s.Add(VarAssign("bh:g1", "良品清零", "PkgGoodCount", null, "Global", new JValue(0)));
        s.Add(VarAssign("bh:g2", "不良清零", "PkgNgCount", null, "Global", new JValue(0)));
        s.Add(VarAssign("bh:g3", "节拍清零", "PkgCycleCount", null, "Global", new JValue(0)));
        s.Add(VarAssign("bh:g4", "表格行数清零", "PkgHistoryRows", null, "Global", new JValue(0)));
        s.Add(VarAssign("bh:g5", "良率清零", "PkgYield", null, "Global", new JValue(0.0)));
        s.Add(VarAssign("bh:g6", "判定复位", "PkgGradeText", null, "Global", new JValue("待检")));
        s.Add(VarAssign("bh:g7", "采行信号复位", "PkgRowPush", null, "Global", new JValue(false)));

        s.Add(VarAssign("bh:f0", "清故障挂起", "PkgLineFault", null, "Global", new JValue(false)));
        s.Add(VarAssign("bh:f1", "清故障码", "PkgFaultCode", null, "Global", new JValue(0)));
        s.Add(MotionEnable("bh:enX", "X 轴使能", "PkgX", true, 3000));
        s.Add(MotionEnable("bh:enY", "Y 轴使能", "PkgX", true, 3000));
        s.Add(MotionHome("bh:homeX", "回零 X", "PkgX", "OriginSwitch", 8000));
        s.Add(VarAssign("bh:ready", "置系统就绪", "SystemReady", null, "Global", new JValue(true)));
        return s;
    }

    // ------------------------------------------------------------------ Main

    static JArray BuildBMainFlow()
    {
        var s = new JArray();

        // 故障挂起时直接退出本轮
        var guard = new JArray { ReturnStep("bm:ret0", "故障中退出本轮") };
        s.Add(IfStep("bm:guard", "故障挂起判断", "Faulted", G("bm:var:guard"), "Faulted", "System.Boolean",
            LG("PkgLineFault"), guard, new JArray()));

        s.Add(CommRead("bm:trig", "读 PLC 放料触发", "PKG_Trigger"));

        var cycle = new JArray();
        AddIncVar(cycle, "bm:cycle", "节拍+1", "PkgCycleCount");
        AddIncVar(cycle, "bm:snseq", "SN序号+1", "PkgSnSeq");

        // 逐件 SN：由 C# 脚本读 PkgSnSeq 生成 SYN-000123-4F 并写回 PkgSn。
        // 写在脚本里而不是串变量运算，是因为要补 4 位校验和与定宽补零。
        cycle.Add(Action("bm:snbuild", "生成产品SN", "变量操作", P_CSCRIPT, "C# 脚本", "\uf121",
            new JObject
            {
                ["Script"] = BuildBSnScript(),
            }));

        // CallBStation 返回的是「RunFlow + 故障判定 If」两步，
        // 必须 AddRange 摊平，直接 Add 会在 Steps 里塞出一个嵌套数组，加载时反序列化直接炸。
        AddRange(cycle, CallBStation("bm:st1", "调用工位1-泡罩", "St1_Blister", 30000, 101));
        AddRange(cycle, CallBStation("bm:st2", "调用工位2-装盒", "St2_Carton", 30000, 102));
        AddRange(cycle, CallBStation("bm:st3", "调用工位3-贴标", "St3_Label", 30000, 103));

        // 汇总三工位结果：三个判定都要为真。
        // 不能用「数值比较」吃布尔变量（A 端口期望 Double，会报类型不匹配），
        // 走 Logic 的 bool 端口 A/B，直接吃三个全局变量。
        cycle.Add(Logic("bm:and12", "工位1&2", null, null, null, null));
        // Logic 的默认参数不支持直接塞全局变量，这里手工改连线。
        var and12 = (JObject)cycle[cycle.Count - 1];
        and12["LinkedSources"] = new JObject
        {
            ["A"] = LG("St1BlisterOk"),
            ["B"] = LG("St2CartonOk"),
        };
        cycle.Add(Logic("bm:andAll", "三工位全过", null, null, null, null));
        var andAll = (JObject)cycle[cycle.Count - 1];
        andAll["LinkedSources"] = new JObject
        {
            ["A"] = L("bm:and12", "Result", "工位1&2"),
            ["B"] = LG("St3LabelOk"),
        };
        cycle.Add(VarAssign("bm:lineok", "写整线判定", "PkgLineOk", L("bm:andAll", "Result", "三工位全过"), "Global", null));

        // 良品 / 不良分流
        var good = new JArray();
        AddIncVar(good, "bm:good", "良品计数", "PkgGoodCount");
        good.Add(VarAssign("bm:gtext", "判定文本-OK", "PkgGradeText", null, "Global", new JValue("OK")));

        var bad = new JArray();
        AddIncVar(bad, "bm:ng", "不良计数", "PkgNgCount");
        bad.Add(VarAssign("bm:btext", "判定文本-NG", "PkgGradeText", null, "Global", new JValue("NG")));

        cycle.Add(IfStep("bm:okif", "良品分流", "Ok", G("bm:var:ok"), "Ok", "System.Boolean",
            L("bm:andAll", "Result", "三工位全过"), good, bad));

        // 采行信号：每写完一件就把 PkgRowPush 翻成 true，数据表格吃这个上升沿采一行。
        // 图元对它是只读的、不复位，所以流程自己把它复位成 false，下一件再置真。
        cycle.Add(VarAssign("bm:push0", "采行信号复位", "PkgRowPush", null, "Global", new JValue(false)));
        cycle.Add(VarAssign("bm:push1", "置采行信号", "PkgRowPush", null, "Global", new JValue(true)));
        cycle.Add(AddIncVarStep("bm:rows", "表格行数+1", "PkgHistoryRows"));

        // 良率：把 (良品 / 良品+不良) * 100 拆成三条纯算术，全部落在 cycle 主序列上
        // （不在 If 分支里算，避免跨分支取数）
        cycle.Add(MathAdd("bm:total", "良品+不良", "PkgGoodCount"));
        cycle.Add(MathCalc("bm:div", "良品占比", "Divide", "PkgGoodCount", "PkgGoodCount", "PkgTotal"));
        cycle.Add(MathCalc("bm:pct", "换算百分比", "Multiply", "PkgGoodCount", "PkgGoodCount", "PkgTotal"));
        cycle.Add(VarAssign("bm:yield", "写良率", "PkgYield", L("bm:pct", "Result", "换算百分比"), "Global", null));

        // 回写 PLC
        cycle.Add(CommWrite("bm:wack", "写应答", "PKG_Ack", new JValue(true), false));
        cycle.Add(CommWrite("bm:wok", "写整件结果", "PKG_Ok", LG("PkgLineOk"), false));
        cycle.Add(CommWrite("bm:wword", "写工位位图", "PKG_StationWord", new JValue(7), false));

        s.Add(IfStep("bm:trigif", "触发判断", "Trigger >= 1", G("bm:var:trig"), "Trigger", "System.Int32",
            L("bm:trig", "Value", "读 PLC 放料触发"), cycle,
            new JArray { Delay("bm:idle", "空闲等待", 200) }));

        return s;
    }

    /// <summary>生成 SN 的 C# 脚本：SYN + 序号 + 校验位，写到全局变量。</summary>
    static string BuildBSnScript()
    {
        return
@"// 逐件 SN：SYN-<六位序号>-<两位校验>
// 序号来自全局变量 PkgSnSeq，校验位用序号与种子做一次稳定哈希，
// 保证同一序号永远得到同一个 SN（复检时可逐条对齐），也避免相邻件撞号。
var seq = (int)(Variables.Get(""PkgSnSeq"") ?? 0);
var seed = (int)(Variables.Get(""PkgSeed"") ?? 0);
var body = (seed * 31L + seq * 7919L) & 0xFFFF;
var s = ""SYN-"" + seq.ToString(""D6"") + ""-"" + body.ToString(""X4"");
Variables.Set(""PkgProductSn"", s);
return true;";
    }

    // ------------------------------------------------------------------ St1 泡罩

    static JArray BuildBSt1Flow()
    {
        var s = new JArray();
        s.Add(VarAssign("bs1:init", "清工位判定", "St1BlisterOk", null, "Global", new JValue(false)));

        s.Add(ImgFile("bs1:acq", "采集泡罩图", B_IMG_BLISTER, 0));
        s.Add(PreProcess("bs1:pre", "图像预处理",
            new JArray(
                Op("gauss", ("Size", "5")),
                Op("gray_closing", ("Width", "5"), ("Height", "5"))),
            1, "bs1:acq", "采集泡罩图"));

        s.Add(Action("bs1:match", "金样匹配定位", "图像处理", P_MATCH, "模板匹配", "\uf0c1",
            new JObject
            {
                ["TemplateLibraryJson"] = "[]",
                ["DefaultTemplateName"] = "",
                ["MinScore"] = 0.6,
                ["MaxMatches"] = 1,
                ["MaxOverlap"] = 0.3,
                ["Greediness"] = 0.8,
                ["DisplayViewIndex"] = 2,
                ["OutputAlignedImage"] = true,
                ["UseSearchRegion"] = false,
            },
            new JObject { ["Image"] = L("bs1:pre", "Image", "图像预处理") }));

        s.Add(Action("bs1:diff", "与金样差分", "缺陷检测", P_DIFF, "图像差分", "\uf0c4",
            new JObject
            {
                ["Threshold"] = 32.0,
                ["MinArea"] = 10.0,
                ["AmbientFloor"] = 8.0,
                ["SignedDisplay"] = 128,
                ["Algorithm"] = "Absolute",
                ["Polarity"] = "Both",
                ["Aggregation"] = "Components",
                ["SizeMismatch"] = "Fail",
                ["DisplayViewIndex"] = 3,
            },
            new JObject
            {
                ["Reference"] = L("bs1:match", "AlignedImage", "金样匹配定位"),
                ["Test"] = L("bs1:pre", "Image", "图像预处理"),
            }));

        s.Add(Action("bs1:blob", "缺陷量化", "缺陷检测", P_BLOB, "Blob 缺陷检测", "\uf0c8",
            new JObject
            {
                ["ThresholdMode"] = "Fixed",
                ["DetectTarget"] = "Dark",
                ["DetectBrightAndDark"] = false,
                ["MinGray"] = 0.0,
                ["MaxGray"] = 60.0,
                ["MinArea"] = 12.0,
                ["MaxBlobArea"] = 0.0,
                ["MaxSingleArea"] = 1200.0,
                ["MaxTotalArea"] = 8000.0,
                ["MinDefectCount"] = 0,
                ["MaxDefectCount"] = 20,
                ["ExcludeBorderPx"] = 0,
                ["OpenRadius"] = 0.0,
                ["CloseRadius"] = 1.0,
                ["FillHoles"] = false,
                ["MergeRadius"] = 0.0,
                ["PixelSizeMm"] = 0.05,
                ["MmPerPixel"] = 0.05,
                ["SortMode"] = "None",
                ["DisplayViewIndex"] = 3,
            },
            new JObject
            {
                ["SrcImage"] = L("bs1:diff", "DiffImage", "与金样差分"),
            }));

        s.Add(VarAssign("bs1:cnt", "写缺陷片数", "St1DefectCount", L("bs1:blob", "DefectCount", "缺陷量化"), "Global", null));
        s.Add(VarAssign("bs1:area", "写缺陷面积", "St1TotalArea", L("bs1:blob", "TotalArea", "缺陷量化"), "Global", null));

        // 颜色校验：金样区域内的主色是否落在容差内
        s.Add(Action("bs1:color", "颜色校验", "颜色检测", P_COLORCHK, "颜色检测", "\uf1fb",
            new JObject
            {
                ["ColorSpace"] = "HSV",
                ["HMin"] = 0.0, ["HMax"] = 360.0,
                ["SMin"] = 0.0, ["SMax"] = 1.0,
                ["VMin"] = 0.15, ["VMax"] = 1.0,
                ["MinAreaRatio"] = 0.001,
                ["DisplayViewIndex"] = 2,
            },
            new JObject { ["Image"] = L("bs1:pre", "Image", "图像预处理") }));

        // 判定：缺陷数 ≤ 3 视为工位通过
        s.Add(Compare("bs1:okcmp", "泡罩判定", "bs1:blob", "缺陷量化", "DefectCount", 3, "LessThanOrEqual"));
        s.Add(VarAssign("bs1:okv", "写工位判定", "St1BlisterOk", L("bs1:okcmp", "Result", "泡罩判定"), "Global", null));
        return s;
    }

    // ------------------------------------------------------------------ St2 装盒

    static JArray BuildBSt2Flow()
    {
        var s = new JArray();
        s.Add(VarAssign("bs2:init", "清工位判定", "St2CartonOk", null, "Global", new JValue(false)));

        s.Add(ImgFile("bs2:acq", "采集装盒图", B_IMG_BOX, 0));
        s.Add(PreProcess("bs2:pre", "图像预处理",
            new JArray(Op("gauss", ("Size", "5"))),
            1, "bs2:acq", "采集装盒图"));

        s.Add(Action("bs2:match", "盒体定位", "图像处理", P_MATCH, "模板匹配", "\uf0c1",
            new JObject
            {
                ["TemplateLibraryJson"] = "[]",
                ["DefaultTemplateName"] = "",
                ["MinScore"] = 0.55,
                ["MaxMatches"] = 1,
                ["MaxOverlap"] = 0.3,
                ["Greediness"] = 0.8,
                ["DisplayViewIndex"] = 2,
                ["OutputAlignedImage"] = true,
                ["UseSearchRegion"] = false,
            },
            new JObject { ["Image"] = L("bs2:pre", "Image", "图像预处理") }));

        s.Add(VarAssign("bs2:score", "写定位分数", "St2MatchScore", L("bs2:match", "Score", "盒体定位"), "Global", null));

        s.Add(Action("bs2:diff", "盒面差异", "缺陷检测", P_DIFF, "图像差分", "\uf0c4",
            new JObject
            {
                ["Threshold"] = 36.0,
                ["MinArea"] = 24.0,
                ["AmbientFloor"] = 10.0,
                ["SignedDisplay"] = 128,
                ["Algorithm"] = "Absolute",
                ["Polarity"] = "Both",
                ["Aggregation"] = "Components",
                ["SizeMismatch"] = "Fail",
                ["DisplayViewIndex"] = 3,
            },
            new JObject
            {
                ["Reference"] = L("bs2:match", "AlignedImage", "盒体定位"),
                ["Test"] = L("bs2:pre", "Image", "图像预处理"),
            }));

        s.Add(VarAssign("bs2:dcnt", "写差异块数", "St2DiffCount", L("bs2:diff", "DiffCount", "盒面差异"), "Global", null));

        // 盒面 OCR：读生产批号
        s.Add(Ocr("bs2:ocr", "盒面文字识别", new[] { 120.0, 90.0, 0.0, 620.0, 180.0 }, 2,
            "bs2:pre", "图像预处理", true));
        s.Add(VarAssign("bs2:text", "写 OCR 文本", "St2OcrText", L("bs2:ocr", "Text", "盒面文字识别"), "Global", null));

        s.Add(Compare("bs2:okcmp", "装盒判定", "bs2:diff", "盒面差异", "DiffCount", 5, "LessThanOrEqual"));
        s.Add(VarAssign("bs2:okv", "写工位判定", "St2CartonOk", L("bs2:okcmp", "Result", "装盒判定"), "Global", null));
        return s;
    }

    // ------------------------------------------------------------------ St3 贴标

    static JArray BuildBSt3Flow()
    {
        var s = new JArray();
        s.Add(VarAssign("bs3:init", "清工位判定", "St3LabelOk", null, "Global", new JValue(false)));

        s.Add(ImgFile("bs3:acq", "采集贴标图", B_IMG_LABEL, 0));
        s.Add(PreProcess("bs3:pre", "图像预处理",
            new JArray(Op("gauss", ("Size", "3"))),
            1, "bs3:acq", "采集贴标图"));

        s.Add(Action("bs3:match", "标签定位", "图像处理", P_MATCH, "模板匹配", "\uf0c1",
            new JObject
            {
                ["TemplateLibraryJson"] = "[]",
                ["DefaultTemplateName"] = "",
                ["MinScore"] = 0.7,
                ["MaxMatches"] = 1,
                ["MaxOverlap"] = 0.3,
                ["Greediness"] = 0.8,
                ["DisplayViewIndex"] = 2,
                ["OutputAlignedImage"] = true,
                ["UseSearchRegion"] = false,
            },
            new JObject { ["Image"] = L("bs3:pre", "Image", "图像预处理") }));

        s.Add(VarAssign("bs3:score", "写标签分数", "St3MatchScore", L("bs3:match", "Score", "标签定位"), "Global", null));

        s.Add(Action("bs3:code", "标签读码", "识别", P_CODE, "读码", "\uf02a",
            new JObject
            {
                ["Symbology"] = "QR Code",
                ["TimeoutMs"] = 500,
                ["Polarity"] = "any",
            },
            new JObject { ["Image"] = L("bs3:pre", "Image", "图像预处理") }));

        s.Add(VarAssign("bs3:text", "写条码文本", "St3Barcode", L("bs3:code", "Text", "标签读码"), "Global", null));

        s.Add(Compare("bs3:okcmp", "贴标判定", "bs3:match", "标签定位", "Score", 0.7, "GreaterThan"));
        s.Add(VarAssign("bs3:okv", "写工位判定", "St3LabelOk", L("bs3:okcmp", "Result", "贴标判定"), "Global", null));
        return s;
    }

    // ------------------------------------------------------------------ Recipe

    static JArray BuildBRecipeFlow()
    {
        var s = new JArray();
        s.Add(VarAssign("br:sn", "SN 序号归零", "PkgSnSeq", null, "Global", new JValue(0)));
        s.Add(VarAssign("br:g1", "良品归零", "PkgGoodCount", null, "Global", new JValue(0)));
        s.Add(VarAssign("br:g2", "不良归零", "PkgNgCount", null, "Global", new JValue(0)));
        s.Add(VarAssign("br:g3", "节拍归零", "PkgCycleCount", null, "Global", new JValue(0)));
        s.Add(VarAssign("br:g4", "行数归零", "PkgHistoryRows", null, "Global", new JValue(0)));
        s.Add(VarAssign("br:g5", "良率归零", "PkgYield", null, "Global", new JValue(0.0)));
        s.Add(CcgCsvRecord("br:csv", "换型记录", new JArray
        {
            Col("时间", "BuiltIn", "", "Timestamp"),
            Col("序号", "BuiltIn", "", "Sequence"),
            Col("结果", "Variable", "PkgGradeText", "Timestamp"),
            Col("配方", "Variable", "RecipeName", "Timestamp"),
        }));
        return s;
    }

    // ------------------------------------------------------------------ End

    static JArray BuildBEndFlow()
    {
        var s = new JArray();
        s.Add(VarAssign("be:ready", "清系统就绪", "SystemReady", null, "Global", new JValue(false)));
        s.Add(MotionStop("be:stopX", "停 X 轴", "PkgX", "Decelerate", false));
        s.Add(CcgCsvRecord("be:csv", "班次收尾记录", new JArray
        {
            Col("时间", "BuiltIn", "", "Timestamp"),
            Col("序号", "BuiltIn", "", "Sequence"),
            Col("结果", "Variable", "PkgGradeText", "Timestamp"),
            Col("良品数", "Variable", "PkgGoodCount", "Timestamp"),
        }));
        s.Add(CommWrite("be:ack", "清应答", "PKG_Ack", new JValue(false), false));
        return s;
    }

    // ------------------------------------------------------------------ 工位调用辅助

    /// <summary>
    /// 调用一个工位流程并把它的 Ok 写到故障码。
    /// 与方案 A 的 CallStation 同构：RunFlow 之后判 Invoked/Ok，失败就把故障码落下来。
    /// </summary>
    static JArray CallBStation(string seed, string name, string flowName, int timeoutMs, int faultCode)
    {
        var run = RunFlow(seed + ":run", name, flowName, timeoutMs);

        var fault = new JArray
        {
            VarAssign(seed + ":flt", "置故障挂起", "PkgLineFault", null, "Global", new JValue(true)),
            VarAssign(seed + ":code", "写故障码", "PkgFaultCode", null, "Global", new JValue(faultCode)),
            MotionStop(seed + ":stop", "急停 X 轴", "PkgX", "Decelerate", true),
            CommWrite(seed + ":alarm", "写产线报警", "PKG_Alarm", new JValue(true), false),
            ReturnStep(seed + ":ret", "跳出本轮"),
        };

        var okIf = IfStep(seed + ":if", "设备故障判定", "!Invoked",
            G(seed + ":var:ok"), "Invoked", "System.Boolean",
            LStep(run, "Invoked", name), fault, new JArray());

        return new JArray { run, okIf };
    }

    // ------------------------------------------------------------------ 通用步进辅助

    /// <summary>
    /// 数值比较：A 取一个全局变量，B 取常量。
    /// 注意 A 必须写成 LG(变量名) 的连线，不能把变量名字符串填进 InputValues。
    /// </summary>
    static JObject IfVarStep(string seed, string name, string globalVar)
        => CompareGlobal(seed, name, globalVar, 0.5, "GreaterThan");

    /// <summary>
    /// 自增一个全局计数：读取 → 加 1 → 转 Int32 → 写回。
    /// 与 AddIncVar 同构，只是返回单步（供 cycle.Add 直接挂）。
    /// </summary>
    static JObject AddIncVarStep(string seed, string name, string globalVar)
        => MathAdd(seed, name, globalVar);

    /// <summary>
    /// 两操作数算术：A 取全局变量、B 取另一个全局变量或常量。
    /// Operation 必须写英文枚举名（Add/Subtract/Multiply/Divide），
    /// 写中文或写整数都会在加载时抛 InvalidCastException。
    /// </summary>
    static JObject MathCalc(string seed, string name, string operation,
        string aVar, string bVar, string bOrConst)
    {
        var hasVar = string.IsNullOrEmpty(bOrConst);

        var inputs = new JObject
        {
            ["Operation"] = operation,
        };

        var links = new JObject
        {
            ["A"] = LG(aVar),
        };

        if (hasVar)
        {
            links["B"] = LG(bVar);
        }
        else
        {
            inputs["B"] = 1.0;
        }

        return Action(seed, name, "数据处理", P_MATH, "数学计算", "\\uf1ec", inputs, links);
    }

    // ------------------------------------------------------------------ 组态

    static JObject BuildBScada()
    {
        var pages = new JArray
        {
            BPkgOverviewPage(),
            BPkgDataPage(),
            BPkgStationPage(),
            BPkgRecipePage(),
        };

        return new JObject
        {
            ["SchemaVersion"] = 1,
            ["Pages"] = pages,
            ["Alarms"] = BuildBAlarms(),
            ["VariableEvents"] = new JArray(),
            ["StartupPageId"] = G("page:产线总览").ToString(),
            ["BasePages"] = new JArray(),
        };
    }

    // ------------------------------------------------------------------ 页面 1：产线总览

    static JObject BPkgOverviewPage()
    {
        const string P = "产线总览";
        var els = new JArray();
        AddBHeader(els, P, "泡罩 / 装盒 / 贴标 三工位串行，逐件 SN 全流程追溯");

        // KPI 四联
        els.Add(El(P, "KPI", "Hmi.KpiBoard", 40, 180, 1180, 140, 10, new JObject
        {
            ["Columns"] = "4",
            ["Label1"] = "累计良品", ["Value1"] = "", ["Unit1"] = "件",
            ["Label2"] = "累计不良", ["Value2"] = "", ["Unit2"] = "件",
            ["Label3"] = "当前良率", ["Value3"] = "", ["Unit3"] = "%",
            ["Label4"] = "节拍计数", ["Value4"] = "", ["Unit4"] = "轮",
        }, new JArray
        {
            Bind("Value1", "PkgGoodCount"),
            Bind("Value2", "PkgNgCount"),
            Bind("Value3", "PkgYield", "F1"),
            Bind("Value4", "PkgCycleCount"),
        }));

        // 本件判定大字
        els.Add(El(P, "本件判定卡", "Hmi.Rectangle", 1240, 180, 340, 140, 10,
            new JObject { ["Fill"] = "#FF1E293B", ["Stroke"] = "#FF334155", ["StrokeThickness"] = "1", ["CornerRadius"] = "8" }));
        els.Add(El(P, "本件判定", "Hmi.Text", 1264, 200, 292, 56, 11,
            TextProps("", 40, "#FF22C55E", "Bold"), new JArray { Bind("Text", "PkgGradeText") }));
        els.Add(El(P, "当前SN提示", "Hmi.Text", 1264, 262, 292, 24, 11,
            TextProps("当前产品 SN", 12, "#FF94A3B8")));
        els.Add(El(P, "当前SN", "Hmi.Text", 1264, 284, 292, 28, 11,
            TextProps("", 18, "#FFE2E8F0", "SemiBold"), new JArray { Bind("Text", "PkgProductSn") }));

        // 当前 SN 卡（大字单独强调，SN 是 B 方案的主角）
        els.Add(El(P, "SN卡", "Hmi.Rectangle", 40, 340, 760, 120, 10,
            new JObject { ["Fill"] = "#FF0F172A", ["Stroke"] = "#FF1E40AF", ["StrokeThickness"] = "1", ["CornerRadius"] = "8" }));
        els.Add(El(P, "SN标题", "Hmi.Text", 64, 356, 300, 24, 11,
            TextProps("当前产品序列号 SN", 13, "#FF93C5FD")));
        els.Add(El(P, "SN大值", "Hmi.Text", 64, 384, 700, 60, 11,
            TextProps("", 34, "#FFF8FAFC", "Bold"), new JArray { Bind("Text", "PkgProductSn") }));

        // 三工位状态灯
        els.Add(El(P, "工位1标题", "Hmi.Text", 840, 344, 200, 24, 11, TextProps("工位1 泡罩检测", 14, "#FFCBD5E1")));
        els.Add(El(P, "工位1灯", "Hmi.Lamp", 840, 372, 64, 64, 11, new JObject
        {
            ["OnColor"] = "#FF22C55E", ["OffColor"] = "#FF475569"
        }, new JArray { Bind("State", "St1BlisterOk") }));
        els.Add(El(P, "工位1缺陷数", "Hmi.IOField", 916, 380, 180, 30, 11, new JObject
        {
            ["ValueFormat"] = "F0", ["Unit"] = "片", ["Mode"] = "Output", ["Text"] = "缺陷数", ["TextAlignment"] = "Right"
        }, new JArray { Bind("Value", "St1DefectCount") }));

        els.Add(El(P, "工位2标题", "Hmi.Text", 1120, 344, 200, 24, 11, TextProps("工位2 装盒", 14, "#FFCBD5E1")));
        els.Add(El(P, "工位2灯", "Hmi.Lamp", 1120, 372, 64, 64, 11, new JObject
        {
            ["OnColor"] = "#FF22C55E", ["OffColor"] = "#FF475569"
        }, new JArray { Bind("State", "St2CartonOk") }));
        els.Add(El(P, "工位2分数", "Hmi.IOField", 1196, 380, 180, 30, 11, new JObject
        {
            ["ValueFormat"] = "F3", ["Unit"] = "", ["Mode"] = "Output", ["Text"] = "装盒分数", ["TextAlignment"] = "Right"
        }, new JArray { Bind("Value", "St2CartonScore") }));

        els.Add(El(P, "工位3标题", "Hmi.Text", 1400, 344, 200, 24, 11, TextProps("工位3 贴标", 14, "#FFCBD5E1")));
        els.Add(El(P, "工位3灯", "Hmi.Lamp", 1400, 372, 64, 64, 11, new JObject
        {
            ["OnColor"] = "#FF22C55E", ["OffColor"] = "#FF475569"
        }, new JArray { Bind("State", "St3LabelOk") }));

        // 报警条
        els.Add(El(P, "报警条", "Hmi.AlarmBanner", 40, 500, 1840, 80, 10, new JObject
        {
            ["MaxRows"] = "3",
            ["EmptyText"] = "当前无报警",
            ["NormalColor"] = "#FF16A34A",
            ["InfoColor"] = "#FF0EA5E9",
            ["WarningColor"] = "#FFF59E0B",
            ["CriticalColor"] = "#FFEF4444",
        }));

        // 运行控制
        els.Add(El(P, "Csv提示标题", "Hmi.Text", 40, 610, 800, 24, 11,
            TextProps("留痕口径", 13, "#FF94A3B8")));
        els.Add(El(P, "Csv提示", "Hmi.Text", 40, 638, 1180, 48, 11,
            TextProps("数据表格只做显示缓冲（断电即失），完整留痕由 CSV 记录插件写到方案目录；表格列可用「数据追溯」页查看。", 13, "#FF64748B")));

        els.Add(TestButton(P, "启动产线", 40, 720, "Main"));
        els.Add(NavButton(P, "数据追溯", 220, 720, "数据追溯"));
        els.Add(NavButton(P, "工位详情", 380, 720, "工位详情"));
        els.Add(NavButton(P, "配方参数", 540, 720, "配方参数"));
        els.Add(WriteButton(P, "故障复位", 700, 720, "PkgLineFault", "False", "#FF16A34A"));
        els.Add(WriteButton(P, "计数清零", 870, 720, "PkgGoodCount", "0", "#FF475569"));

        return ScadaPageObj(P, 1920, 1080, els);
    }

    // ------------------------------------------------------------------ 页面 2：数据追溯（DataGrid 主战场）

    static JObject BPkgDataPage()
    {
        const string P = "数据追溯";
        var els = new JArray();
        AddBHeader(els, P, "逐件 SN / 三工位结果 / 判定 实时入表，最新在最上");

        // 列定义：Header / Variable / Width / Align / Format / Monospace
        // Width=0 表示吃掉剩余宽度；最多 12 列
        var cols = new JArray
        {
            BCol("SN", "PkgProductSn", 220, "left", true),
            BCol("工位1", "St1BlisterOk", 90, "center", false),
            BCol("泡罩缺陷", "St1DefectCount", 110, "right", false),
            BCol("工位2", "St2CartonOk", 90, "center", false),
            BCol("装盒分数", "St2CartonScore", 110, "right", false),
            BCol("工位3", "St3LabelOk", 90, "center", false),
            BCol("条码", "St3Barcode", 200, "left", true),
            BCol("判定", "PkgGradeText", 100, "center", false),
        };

        els.Add(El(P, "追溯表格", "Hmi.DataGrid", 40, 180, 1840, 480, 10, new JObject
        {
            // 列定义的键是 ScadaGridColumnsCodec.Key = "GridColumns"（写 "Columns" 会被静默丢弃，
            // 图元进空状态"还没有配置采集"）。
            ["GridColumns"] = cols.ToString(Newtonsoft.Json.Formatting.None),
            ["SampleVariable"] = "PkgRowPush",
            ["AckVariable"] = "PkgHistoryRows",
            ["MaxRows"] = "200",
            ["RowHeight"] = "32",
            ["NewestFirst"] = "True",
            ["Zebra"] = "True",
            ["Source"] = "Live",
        }));

        // 采行信号的说明条——告诉用户这行是流程推的信号
        els.Add(El(P, "采行说明", "Hmi.Text", 40, 676, 1400, 24, 11,
            TextProps("每完成一件，Main 流程把「PkgRowPush」翻转为 true，表格吃这个上升沿采一行；表格自身不复位信号。", 13, "#FF64748B")));

        // 右侧统计卡
        els.Add(El(P, "统计卡", "Hmi.Rectangle", 40, 716, 560, 150, 10,
            new JObject { ["Fill"] = "#FF1E293B", ["Stroke"] = "#FF334155", ["StrokeThickness"] = "1", ["CornerRadius"] = "8" }));
        els.Add(El(P, "统计标题", "Hmi.Text", 64, 732, 300, 24, 11, TextProps("本批统计", 14, "#FFCBD5E1", "SemiBold")));
        els.Add(El(P, "行数标签", "Hmi.Text", 64, 768, 140, 24, 11, TextProps("已采行数", 13, "#FF94A3B8")));
        els.Add(El(P, "行数值", "Hmi.IOField", 210, 766, 160, 30, 11, new JObject
        {
            ["ValueFormat"] = "F0", ["Unit"] = "行", ["Mode"] = "Output", ["Text"] = "", ["TextAlignment"] = "Right"
        }, new JArray { Bind("Value", "PkgHistoryRows") }));
        els.Add(El(P, "良品标签", "Hmi.Text", 64, 806, 140, 24, 11, TextProps("良品 / 不良", 13, "#FF94A3B8")));
        els.Add(El(P, "良品值", "Hmi.IOField", 210, 804, 74, 30, 11, new JObject
        {
            ["ValueFormat"] = "F0", ["Mode"] = "Output", ["Text"] = "", ["TextAlignment"] = "Right"
        }, new JArray { Bind("Value", "PkgGoodCount") }));
        els.Add(El(P, "不良值", "Hmi.IOField", 292, 804, 74, 30, 11, new JObject
        {
            ["ValueFormat"] = "F0", ["Mode"] = "Output", ["Text"] = "", ["TextAlignment"] = "Right"
        }, new JArray { Bind("Value", "PkgNgCount") }));

        // 趋势：良率曲线
        els.Add(El(P, "良率趋势标题", "Hmi.Text", 640, 716, 400, 24, 11, TextProps("良率 / 不良数趋势", 14, "#FFCBD5E1", "SemiBold")));
        els.Add(El(P, "良率趋势", "Hmi.MultiTrend", 640, 744, 1240, 150, 10, new JObject
        {
            ["MaxSeries"] = "2",
            ["ShowLegend"] = "True",
            ["SeriesLabel1"] = "良率 %",
            ["SeriesLabel2"] = "不良数",
            ["WindowSeconds"] = "120",
            ["AutoScale"] = "True",
            ["ShowGrid"] = "True",
        }, new JArray
        {
            Bind("Value1", "PkgYield"),
            Bind("Value2", "PkgNgCount"),
        }));

        // 导航
        els.Add(NavButton(P, "返回总览", 40, 900, "产线总览"));
        els.Add(NavButton(P, "工位详情", 200, 900, "工位详情"));
        els.Add(WriteButton(P, "清空计数", 360, 900, "PkgHistoryRows", "0", "#FF475569"));

        return ScadaPageObj(P, 1920, 1080, els);
    }

    // ------------------------------------------------------------------ 页面 3：工位详情

    static JObject BPkgStationPage()
    {
        const string P = "工位详情";
        var els = new JArray();
        AddBHeader(els, P, "三工位逐项指标与现场图对照");

        // 工位 1
        els.Add(El(P, "工位1卡", "Hmi.Rectangle", 40, 180, 600, 380, 10,
            new JObject { ["Fill"] = "#FF1E293B", ["Stroke"] = "#FF334155", ["StrokeThickness"] = "1", ["CornerRadius"] = "8" }));
        els.Add(El(P, "工位1卡标题", "Hmi.Text", 64, 196, 400, 28, 11, TextProps("工位1 · 泡罩金样差分", 16, "#FFF8FAFC", "SemiBold")));
        els.Add(El(P, "工位1图", "Hmi.Picture", 64, 232, 268, 200, 11, new JObject
        {
            ["SourcePath"] = B_IMG_BLISTER,
        }));
        els.Add(El(P, "工位1差分提示", "Hmi.Text", 344, 232, 280, 24, 11, TextProps("与 Image\\blister_reference.png 差分", 12, "#FF94A3B8")));
        els.Add(El(P, "工位1缺陷标签", "Hmi.Text", 344, 264, 200, 24, 11, TextProps("缺陷片数", 13, "#FFCBD5E1")));
        els.Add(El(P, "工位1缺陷值", "Hmi.IOField", 344, 290, 180, 30, 11, new JObject
        {
            ["ValueFormat"] = "F0", ["Unit"] = "片", ["Mode"] = "Output", ["Text"] = "", ["TextAlignment"] = "Right"
        }, new JArray { Bind("Value", "St1DefectCount") }));
        els.Add(El(P, "工位1判定标签", "Hmi.Text", 344, 334, 200, 24, 11, TextProps("本件判定", 13, "#FFCBD5E1")));
        els.Add(El(P, "工位1判定灯", "Hmi.Lamp", 344, 360, 56, 56, 11, new JObject
        {
            ["OnColor"] = "#FF22C55E", ["OffColor"] = "#FFEF4444"
        }, new JArray { Bind("State", "St1BlisterOk") }));

        // 工位 2
        els.Add(El(P, "工位2卡", "Hmi.Rectangle", 660, 180, 600, 380, 10,
            new JObject { ["Fill"] = "#FF1E293B", ["Stroke"] = "#FF334155", ["StrokeThickness"] = "1", ["CornerRadius"] = "8" }));
        els.Add(El(P, "工位2卡标题", "Hmi.Text", 684, 196, 400, 28, 11, TextProps("工位2 · 装盒与 OCR", 16, "#FFF8FAFC", "SemiBold")));
        els.Add(El(P, "工位2图", "Hmi.Picture", 684, 232, 268, 200, 11, new JObject
        {
            ["SourcePath"] = B_IMG_BOX,
        }));
        els.Add(El(P, "工位2分数标签", "Hmi.Text", 964, 232, 200, 24, 11, TextProps("装盒定位分数", 13, "#FFCBD5E1")));
        els.Add(El(P, "工位2分数值", "Hmi.IOField", 964, 258, 180, 30, 11, new JObject
        {
            ["ValueFormat"] = "F3", ["Mode"] = "Output", ["Text"] = "", ["TextAlignment"] = "Right"
        }, new JArray { Bind("Value", "St2CartonScore") }));
        els.Add(El(P, "工位2文本标签", "Hmi.Text", 964, 302, 200, 24, 11, TextProps("OCR 文本", 13, "#FFCBD5E1")));
        els.Add(El(P, "工位2文本", "Hmi.TextField", 964, 328, 240, 32, 11, new JObject
        {
            ["Placeholder"] = "未识别", ["MaxLength"] = "120", ["Mode"] = "Output"
        }, new JArray { Bind("Value", "St2OcrText") }));
        els.Add(El(P, "工位2判定灯", "Hmi.Lamp", 964, 372, 56, 56, 11, new JObject
        {
            ["OnColor"] = "#FF22C55E", ["OffColor"] = "#FFEF4444"
        }, new JArray { Bind("State", "St2CartonOk") }));

        // 工位 3
        els.Add(El(P, "工位3卡", "Hmi.Rectangle", 1280, 180, 600, 380, 10,
            new JObject { ["Fill"] = "#FF1E293B", ["Stroke"] = "#FF334155", ["StrokeThickness"] = "1", ["CornerRadius"] = "8" }));
        els.Add(El(P, "工位3卡标题", "Hmi.Text", 1304, 196, 400, 28, 11, TextProps("工位3 · 贴标与读码", 16, "#FFF8FAFC", "SemiBold")));
        els.Add(El(P, "工位3图", "Hmi.Picture", 1304, 232, 268, 200, 11, new JObject
        {
            ["SourcePath"] = B_IMG_LABEL,
        }));
        els.Add(El(P, "工位3分数标签", "Hmi.Text", 1584, 232, 200, 24, 11, TextProps("标签定位分数", 13, "#FFCBD5E1")));
        els.Add(El(P, "工位3分数值", "Hmi.IOField", 1584, 258, 180, 30, 11, new JObject
        {
            ["ValueFormat"] = "F3", ["Mode"] = "Output", ["Text"] = "", ["TextAlignment"] = "Right"
        }, new JArray { Bind("Value", "St3MatchScore") }));
        els.Add(El(P, "工位3条码标签", "Hmi.Text", 1584, 302, 200, 24, 11, TextProps("读出条码", 13, "#FFCBD5E1")));
        els.Add(El(P, "工位3条码", "Hmi.TextField", 1584, 328, 240, 32, 11, new JObject
        {
            ["Placeholder"] = "未读到", ["MaxLength"] = "120", ["Mode"] = "Output"
        }, new JArray { Bind("Value", "St3Barcode") }));
        els.Add(El(P, "工位3判定灯", "Hmi.Lamp", 1584, 372, 56, 56, 11, new JObject
        {
            ["OnColor"] = "#FF22C55E", ["OffColor"] = "#FFEF4444"
        }, new JArray { Bind("State", "St3LabelOk") }));

        // 报警
        els.Add(El(P, "报警条", "Hmi.AlarmBanner", 40, 590, 1840, 90, 10, new JObject
        {
            ["MaxRows"] = "4",
            ["EmptyText"] = "三工位均正常",
            ["NormalColor"] = "#FF16A34A",
            ["InfoColor"] = "#FF0EA5E9",
            ["WarningColor"] = "#FFF59E0B",
            ["CriticalColor"] = "#FFEF4444",
        }));

        els.Add(NavButton(P, "返回总览", 40, 720, "产线总览"));
        els.Add(NavButton(P, "数据追溯", 200, 720, "数据追溯"));

        return ScadaPageObj(P, 1920, 1080, els);
    }

    // ------------------------------------------------------------------ 页面 4：配方参数

    static JObject BPkgRecipePage()
    {
        const string P = "配方参数";
        var els = new JArray();
        AddBHeader(els, P, "换型时只改配方名与阈值，不改流程");

        els.Add(El(P, "配方卡", "Hmi.Rectangle", 40, 180, 700, 300, 10,
            new JObject { ["Fill"] = "#FF1E293B", ["Stroke"] = "#FF334155", ["StrokeThickness"] = "1", ["CornerRadius"] = "8" }));
        els.Add(El(P, "配方标题", "Hmi.Text", 64, 196, 400, 28, 11, TextProps("当前配方", 16, "#FFF8FAFC", "SemiBold")));

        els.Add(El(P, "配方名标签", "Hmi.Text", 64, 240, 160, 24, 11, TextProps("配方名称", 13, "#FF94A3B8")));
        els.Add(El(P, "配方名", "Hmi.TextField", 64, 266, 300, 34, 11, new JObject
        {
            ["Placeholder"] = "STD-2024", ["MaxLength"] = "60", ["Mode"] = "Input"
        }, new JArray { Bind("Value", "PkgRecipe") }));

        els.Add(El(P, "缺陷阈值标签", "Hmi.Text", 64, 316, 200, 24, 11, TextProps("泡罩缺陷阈值（片）", 13, "#FF94A3B8")));
        els.Add(El(P, "缺陷阈值", "Hmi.IOField", 64, 342, 160, 32, 11, new JObject
        {
            ["ValueFormat"] = "F0", ["Mode"] = "Input", ["Text"] = "", ["TextAlignment"] = "Right"
        }, new JArray { Bind("Value", "PkgDefectLimit") }));

        els.Add(El(P, "匹配阈值标签", "Hmi.Text", 260, 316, 200, 24, 11, TextProps("定位分数阈值", 13, "#FF94A3B8")));
        els.Add(El(P, "匹配阈值", "Hmi.IOField", 260, 342, 160, 32, 11, new JObject
        {
            ["ValueFormat"] = "F2", ["Mode"] = "Input", ["Text"] = "", ["TextAlignment"] = "Right"
        }, new JArray { Bind("Value", "PkgScoreLimit") }));

        els.Add(WriteButton(P, "应用配方", 64, 400, "PkgRecipeApplied", "True", "#FF0EA5E9"));
        els.Add(NavButton(P, "返回总览", 232, 400, "产线总览"));

        // 说明卡
        els.Add(El(P, "说明卡", "Hmi.Rectangle", 780, 180, 1100, 300, 10,
            new JObject { ["Fill"] = "#FF0F172A", ["Stroke"] = "#FF1E40AF", ["StrokeThickness"] = "1", ["CornerRadius"] = "8" }));
        els.Add(El(P, "说明标题", "Hmi.Text", 804, 196, 500, 28, 11, TextProps("本方案三条工艺口径", 16, "#FF93C5FD", "SemiBold")));
        els.Add(El(P, "说明正文", "Hmi.Text", 804, 236, 1050, 220, 11, new JObject
        {
            ["Text"] = "1) 三工位串行，任一工位 NG 即整件 NG，故障码落 PkgFaultCode 并停 X 轴。\n" +
                       "2) 逐件 SN 由 Main 流程用 C# 脚本生成（SYN-6位序号-2位校验），写入 PkgSn 后参与表格采样。\n" +
                       "3) 阈值类参数走配方变量，不写死在流程里；改完点「应用配方」写 PkgRecipeApplied。",
            ["Foreground"] = "#FFE2E8F0",
            ["FontSize"] = "14",
            ["TextAlignment"] = "Left",
            ["VerticalContentAlignment"] = "Top",
            ["TextWrapping"] = "Wrap",
        }));

        els.Add(NavButton(P, "数据追溯", 40, 520, "数据追溯"));

        return ScadaPageObj(P, 1920, 1080, els);
    }

    // ------------------------------------------------------------------ 组态小件

    static void AddBHeader(JArray els, string page, string subtitle)
    {
        els.Add(El(page, "标题栏", "Hmi.Rectangle", 0, 0, 1920, 160, 5,
            new JObject { ["Fill"] = "#FF111827" }));
        els.Add(El(page, "页面标题", "Hmi.Text", 40, 24, 900, 48, 6,
            TextProps("包装线全检 · 三工位串行", 28, "#FFF8FAFC", "Bold")));
        els.Add(El(page, "页面副标题", "Hmi.Text", 40, 76, 1200, 28, 6,
            TextProps(subtitle, 14, "#FF94A3B8")));
        els.Add(El(page, "时钟", "Hmi.Clock", 1500, 24, 380, 44, 6,
            TextProps("", 22, "#FFCBD5E1", "SemiBold")));
    }

    /// <summary>
    /// 一行表格列定义。Width=0 表示吃掉剩余宽度；最多 12 列。
    /// 列规范是 JSON 串，列的 Variable 写全局变量名。
    /// </summary>
    static JObject BCol(string header, string variable, int width, string align, bool mono)
        => new()
        {
            ["Header"] = header,
            ["Variable"] = variable,
            ["Width"] = width,
            ["Align"] = align,
            ["Format"] = "",
            ["Monospace"] = mono,
        };

    static JArray BuildBAlarms()
    {
        return new JArray
        {
            ScadaAlarm("产线故障挂起", "PkgLineFault", 4, 0.0, 2, "产线故障挂起，需人工复位", "产线", "工位流程返回失败", "查故障码后复位"),
            ScadaAlarm("不良率偏高", "PkgYield", 2, 90.0, 1, "良率低于 90%，建议抽检", "质量", "连续多件 NG", "抽检前 20 件并复核金样"),
            ScadaAlarm("工位1缺陷超限", "St1DefectCount", 0, 20.0, 1, "泡罩缺陷片数超 20", "工位1", "泡罩来料或热封异常", "查热封温度与金样"),
        };
    }
}
