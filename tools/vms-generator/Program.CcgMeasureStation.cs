using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// 方案 C：精密尺寸测量站（标定 → 定位 → 多路卡尺 → 公差判定 → SPC/CPK → 逐件 SN 报表）。
/// 与方案 A/B 的差别集中在五处：
/// ① 第一步是「标定」，把像素当量交给下游所有卡尺（卡尺端口 MmPerPixel 吃上游值，不手填）；
/// ② 四路卡尺并行做不同测量（爪口宽度 / 刻度间距 / 标定板圆直径 / 刹车盘中心孔径），
///    每路都有独立的公差区间，说明"一个工位怎么同时管多个尺寸特性"；
/// ③ 逐件 SN 由 C# 脚本用「时间戳 + 序号」生成，校验位用 SHA1，件件不同且可复检对齐；
/// ④ SPC：本件实测值 → 与标准值做偏差 → 偏差超过 ±3σ 口径就判异常 → CPK 用
///    (上限-均值)/(3σ) 与 (均值-下限)/(3σ) 取小，全部落在主序列上（不在分支里算）；
/// ⑤ 组态四页：运行总览 / 尺寸报表（DataGrid）/ 测量详情（四张样图 + 四组读数）/ 标定与配方。
/// </summary>
internal static partial class Program
{
    // ---------- 方案 C 的样图：方案内相对路径 Image\xxx.png（源文件生成期从样图库复制进方案） ----------
    const string C_IMG_CALIPER = @"Image\caliper_1.png";
    const string C_IMG_TILT = @"Image\caliper_with_tilt.png";
    const string C_IMG_CALIB = @"Image\caliper_calib_01.png";
    const string C_IMG_BRAKE = @"Image\brake_disk_part_01.png";

    // 量具轴名（三轴平台：X 送料 / Y 横向 / Z 对焦）
    const string C_AXIS_X = "MsrX";
    const string C_AXIS_Y = "MsrY";
    const string C_AXIS_Z = "MsrZ";

    // 标定插件：Program.cs 的常量表里没有，方案 C 自己声明（程序集名即工程名 Plugin.Calibration）
    const string P_CALIB = "Plugin.Calibration.CalibrationPlugin, Plugin.Calibration" + S;

    // 像素当量手填兜底值（标定流程正常会把上游值顶上去；这里只在未接标定时生效）
    const double C_MM_PER_PX = 0.0437;

    /// <summary>
    /// 生成方案 C 的完整 SolutionModel JSON。
    /// 结构：Home / Main / M_Calib / M_Measure / SPC / Recipe / End 共 7 条流程。
    /// </summary>
    static JObject BuildCcgMeasureStationSolution()
    {
        var vars = BuildCVariables();
        var flows = new JArray
        {
            Flow("Home", "回零与初始化：清计数、清故障、三轴使能回零、置就绪", 1, 96, 0, BuildCHomeFlow()),
            Flow("Main", "测量站主调度：逐件生成 SN、调标定与测量、算 SPC、回写 PLC", 2, 96, 0, BuildCMainFlow()),
            Flow("M_Calib", "标定：像素当量 + 圆点标定板残差，供下游卡尺共用", 0, 96, 0, BuildCCalibFlow()),
            Flow("M_Measure", "尺寸测量：四路卡尺分别测爪口宽/刻度距/圆直径/中心孔径", 0, 96, 0, BuildCMeasureFlow()),
            Flow("SPC", "过程能力：均值/极差/σ/CPK 与超差片数统计", 0, 96, 0, BuildCSpcFlow()),
            Flow("Recipe", "配方与 SN 池：换型、清计数、重新播种 SN 种子", 0, 96, 0, BuildCRecipeFlow()),
            Flow("End", "停机收尾：停轴、落盘、清应答、置未就绪", 3, 0, 0, BuildCEndFlow(), enabled: false),
        };

        return new JObject
        {
            ["SolutionName"] = "CCG-精密尺寸测量站",
            ["Version"] = 1.0,
            ["Config"] = new JObject { ["ImageViewMode"] = 0 },
            ["Flows"] = flows,
            ["SolutionId"] = G("msr:solution").ToString(),
            ["CommunicationConfigs"] = BuildComms(),
            ["IoBuses"] = new JArray(),
            ["IoDevices"] = new JArray(),
            ["CameraConfigs"] = new JArray(),
            ["MotionCards"] = BuildMotionCards(),
            ["CamTables"] = new JArray(),
            ["VariableSnapshots"] = vars,
            ["VariableGroups"] = new JArray(),
            ["WatchItems"] = new JArray(),
            ["Scada"] = BuildCScada(),
        };
    }

    // ------------------------------------------------------------------ 变量

    static JArray BuildCVariables()
    {
        var list = BuildVariables();

        // 逐件身份
        list.Add(Var("MsrProductSn", T_STR, "当前工件 SN（逐件生成）", ""));
        list.Add(Var("MsrSnSeq", T_INT, "SN 流水序号", 0));
        list.Add(Var("MsrSeed", T_INT, "SN 随机种子（换型可重播种）", 20261105));

        // 标定
        list.Add(Var("CalMmPerPixel", T_DBL, "标定得到的像素当量(mm/px)", C_MM_PER_PX));
        list.Add(Var("CalResidualRms", T_DBL, "标定残差 RMS(px)", 0.0));
        list.Add(Var("CalResidualMax", T_DBL, "标定残差最大值(px)", 0.0));
        list.Add(Var("CalOk", T_BOOL, "标定是否通过", false));
        list.Add(Var("CalDateText", T_STR, "标定时间戳", ""));

        // 四路测量：实测值 / 偏差 / 单路判定
        list.Add(Var("D1NominalMm", T_DBL, "爪口宽度标准值(mm)", 20.0));
        list.Add(Var("D1UpperMm", T_DBL, "爪口宽度上差(mm)", 20.05));
        list.Add(Var("D1LowerMm", T_DBL, "爪口宽度下差(mm)", 19.95));
        list.Add(Var("D1ValueMm", T_DBL, "爪口宽度实测(mm)", 0.0));
        list.Add(Var("D1DevMm", T_DBL, "爪口宽度偏差(mm)", 0.0));
        list.Add(Var("D1Ok", T_BOOL, "爪口宽度判定", false));

        list.Add(Var("D2NominalMm", T_DBL, "刻度间距标准值(mm)", 1.0));
        list.Add(Var("D2UpperMm", T_DBL, "刻度间距上差(mm)", 1.02));
        list.Add(Var("D2LowerMm", T_DBL, "刻度间距下差(mm)", 0.98));
        list.Add(Var("D2ValueMm", T_DBL, "刻度间距实测(mm)", 0.0));
        list.Add(Var("D2DevMm", T_DBL, "刻度间距偏差(mm)", 0.0));
        list.Add(Var("D2Ok", T_BOOL, "刻度间距判定", false));

        list.Add(Var("D3NominalMm", T_DBL, "标定板孔径标准值(mm)", 2.5));
        list.Add(Var("D3UpperMm", T_DBL, "标定板孔径上差(mm)", 2.53));
        list.Add(Var("D3LowerMm", T_DBL, "标定板孔径下差(mm)", 2.47));
        list.Add(Var("D3ValueMm", T_DBL, "标定板孔径实测(mm)", 0.0));
        list.Add(Var("D3DevMm", T_DBL, "标定板孔径偏差(mm)", 0.0));
        list.Add(Var("D3Ok", T_BOOL, "标定板孔径判定", false));

        list.Add(Var("D4NominalMm", T_DBL, "刹车盘孔径标准值(mm)", 30.0));
        list.Add(Var("D4UpperMm", T_DBL, "刹车盘孔径上差(mm)", 30.06));
        list.Add(Var("D4LowerMm", T_DBL, "刹车盘孔径下差(mm)", 29.94));
        list.Add(Var("D4ValueMm", T_DBL, "刹车盘孔径实测(mm)", 0.0));
        list.Add(Var("D4DevMm", T_DBL, "刹车盘孔径偏差(mm)", 0.0));
        list.Add(Var("D4Ok", T_BOOL, "刹车盘孔径判定", false));

        // 整件与统计
        list.Add(Var("MsrAllOk", T_BOOL, "四路尺寸全过", false));
        list.Add(Var("MsrGradeText", T_STR, "本件判定文本", "待测"));
        list.Add(Var("MsrGoodCount", T_INT, "合格累计", 0));
        list.Add(Var("MsrNgCount", T_INT, "超差累计", 0));
        list.Add(Var("MsrYield", T_DBL, "合格率(%)", 0.0));
        list.Add(Var("MsrCycleCount", T_INT, "累计测量节拍数", 0));
        list.Add(Var("MsrHistoryRows", T_INT, "尺寸报表已采行数", 0));
        list.Add(Var("MsrRowPush", T_BOOL, "报表采行信号", false));

        // SPC：对第一路（爪口宽度）做过程能力
        list.Add(Var("SpcMean", T_DBL, "均值(mm)", 0.0));
        list.Add(Var("SpcRange", T_DBL, "极差(mm)", 0.0));
        list.Add(Var("SpcSigma", T_DBL, "标准差估算 σ≈R/d2(mm)", 0.0));
        list.Add(Var("SpcCpk", T_DBL, "过程能力指数 CPK", 0.0));
        list.Add(Var("SpcCpkText", T_STR, "CPK 评级文本", "待评"));
        list.Add(Var("SpcOutCount", T_INT, "超差件数(报表口径)", 0));
        list.Add(Var("SpcAccum", T_DBL, "实测值累加（算均值用）", 0.0));
        list.Add(Var("SpcLastDev", T_DBL, "上一次偏差（算极差用）", 0.0));

        // 故障与节拍
        list.Add(Var("MsrFaultCode", T_INT, "测量站故障码（0=无）", 0));
        list.Add(Var("MsrStationFault", T_BOOL, "测量站故障挂起", false));
        list.Add(Var("MsrStrobeMs", T_INT, "单件测量耗时(ms)", 0));

        // 网络变量：与测量站主 PLC 交互
        list.Add(NetVar("MSR_Trigger", T_BOOL, "PLC 工件到位触发", false, "PLC_Modbus", "ModbusTcp", "HoldingRegisters", "20", -1, 1, 1, "Boolean"));
        list.Add(NetVar("MSR_Ack", T_BOOL, "测量结果已送出应答", false, "PLC_Modbus", "ModbusTcp", "HoldingRegisters", "21", -1, 1, 1, "Boolean"));
        list.Add(NetVar("MSR_Ok", T_BOOL, "整件合格信号", false, "PLC_Modbus", "ModbusTcp", "Coils", "22", -1, 1, 1, "Boolean"));
        list.Add(NetVar("MSR_Alarm", T_BOOL, "测量站故障信号", false, "PLC_Modbus", "ModbusTcp", "Coils", "23", -1, 1, 1, "Boolean"));
        list.Add(NetVar("MSR_D1Scaled", T_INT, "爪口宽度×1000 送 PLC", 0, "PLC_Modbus", "ModbusTcp", "HoldingRegisters", "24", -1, 1, 1, "Int16"));
        list.Add(NetVar("MSR_D4Scaled", T_INT, "刹车盘孔径×1000 送 PLC", 0, "PLC_Modbus", "ModbusTcp", "HoldingRegisters", "25", -1, 1, 1, "Int16"));

        return list;
    }

    // ------------------------------------------------------------------ Home

    static JArray BuildCHomeFlow()
    {
        var s = new JArray();
        s.Add(VarAssign("ch:sn", "SN 序号清零", "MsrSnSeq", null, "Global", new JValue(0)));
        s.Add(VarAssign("ch:g1", "合格清零", "MsrGoodCount", null, "Global", new JValue(0)));
        s.Add(VarAssign("ch:g2", "超差清零", "MsrNgCount", null, "Global", new JValue(0)));
        s.Add(VarAssign("ch:g3", "节拍清零", "MsrCycleCount", null, "Global", new JValue(0)));
        s.Add(VarAssign("ch:g4", "报表行数清零", "MsrHistoryRows", null, "Global", new JValue(0)));
        s.Add(VarAssign("ch:g5", "合格率清零", "MsrYield", null, "Global", new JValue(0.0)));
        s.Add(VarAssign("ch:g6", "判定复位", "MsrGradeText", null, "Global", new JValue("待测")));
        s.Add(VarAssign("ch:g7", "采行信号复位", "MsrRowPush", null, "Global", new JValue(false)));
        s.Add(VarAssign("ch:s1", "累计值清零", "SpcAccum", null, "Global", new JValue(0.0)));
        s.Add(VarAssign("ch:s2", "极差履历清零", "SpcRange", null, "Global", new JValue(0.0)));
        s.Add(VarAssign("ch:s3", "超差件数清零", "SpcOutCount", null, "Global", new JValue(0)));

        s.Add(VarAssign("ch:f0", "清故障挂起", "MsrStationFault", null, "Global", new JValue(false)));
        s.Add(VarAssign("ch:f1", "清故障码", "MsrFaultCode", null, "Global", new JValue(0)));

        s.Add(MotionEnable("ch:enX", "X 轴使能", C_AXIS_X, true, 3000));
        s.Add(MotionEnable("ch:enY", "Y 轴使能", C_AXIS_Y, true, 3000));
        s.Add(MotionEnable("ch:enZ", "Z 轴使能", C_AXIS_Z, true, 3000));
        s.Add(MotionHome("ch:homeX", "回零 X", C_AXIS_X, "OriginSwitch", 8000));
        s.Add(MotionHome("ch:homeY", "回零 Y", C_AXIS_Y, "OriginSwitch", 8000));
        s.Add(MotionHome("ch:homeZ", "回零 Z", C_AXIS_Z, "OriginSwitch", 8000));

        s.Add(VarAssign("ch:ready", "置系统就绪", "SystemReady", null, "Global", new JValue(true)));
        return s;
    }

    // ------------------------------------------------------------------ Main

    static JArray BuildCMainFlow()
    {
        var s = new JArray();

        var guard = new JArray { ReturnStep("cm:ret0", "故障中退出本轮") };
        s.Add(IfStep("cm:guard", "故障挂起判断", "Faulted", G("cm:var:guard"), "Faulted", "System.Boolean",
            LG("MsrStationFault"), guard, new JArray()));

        s.Add(CommRead("cm:trig", "读 PLC 到位触发", "MSR_Trigger"));

        var cycle = new JArray();
        AddIncVar(cycle, "cm:cycle", "节拍+1", "MsrCycleCount");
        AddIncVar(cycle, "cm:snseq", "SN序号+1", "MsrSnSeq");

        // 逐件 SN：C# 脚本，序号定宽 + 时间戳尾号 + SHA1 校验位
        cycle.Add(Action("cm:snbuild", "生成工件SN", "变量操作", P_CSCRIPT, "C# 脚本", "\uf121",
            new JObject { ["Script"] = BuildCSnScript() }));

        // 标定（每件重算一次当量：真机上标定板随工装一起动，重算比缓存稳）
        AddRange(cycle, CallCStation("cm:cal", "调用标定流程", "M_Calib", 30000, 201));
        // 尺寸测量（四路卡尺，全部依赖标定得到的当量）
        AddRange(cycle, CallCStation("cm:msr", "调用测量流程", "M_Measure", 30000, 202));
        // SPC 统计
        AddRange(cycle, CallCStation("cm:spc", "调用 SPC 统计", "SPC", 30000, 203));

        // 汇总四路判定：全部布尔，走 Logic 的 A/B 端口链，串成 andAll
        cycle.Add(Logic("cm:and12", "D1&D2", null, null, null, null));
        var and12 = (JObject)cycle[cycle.Count - 1];
        and12["LinkedSources"] = new JObject
        {
            ["A"] = LG("D1Ok"),
            ["B"] = LG("D2Ok"),
        };

        cycle.Add(Logic("cm:and34", "D3&D4", null, null, null, null));
        var and34 = (JObject)cycle[cycle.Count - 1];
        and34["LinkedSources"] = new JObject
        {
            ["A"] = LG("D3Ok"),
            ["B"] = LG("D4Ok"),
        };

        cycle.Add(Logic("cm:andAll", "四路全过", null, null, null, null));
        var andAll = (JObject)cycle[cycle.Count - 1];
        andAll["LinkedSources"] = new JObject
        {
            ["A"] = L("cm:and12", "Result", "D1&D2"),
            ["B"] = L("cm:and34", "Result", "D3&D4"),
        };
        cycle.Add(VarAssign("cm:allok", "写整件判定", "MsrAllOk", L("cm:andAll", "Result", "四路全过"), "Global", null));

        // 合格 / 超差分流
        var good = new JArray();
        AddIncVar(good, "cm:good", "合格计数", "MsrGoodCount");
        good.Add(VarAssign("cm:gtext", "判定文本-合格", "MsrGradeText", null, "Global", new JValue("合格")));

        var bad = new JArray();
        AddIncVar(bad, "cm:ng", "超差计数", "MsrNgCount");
        bad.Add(VarAssign("cm:btext", "判定文本-超差", "MsrGradeText", null, "Global", new JValue("超差")));
        bad.Add(AddIncVarStep("cm:outcnt", "超差件数+1", "SpcOutCount"));

        cycle.Add(IfStep("cm:okif", "合格分流", "Ok", G("cm:var:ok"), "Ok", "System.Boolean",
            L("cm:andAll", "Result", "四路全过"), good, bad));

        // 采行信号：报表图元吃上升沿，图元只读不复位，流程自己复位
        cycle.Add(VarAssign("cm:push0", "采行信号复位", "MsrRowPush", null, "Global", new JValue(false)));
        cycle.Add(VarAssign("cm:push1", "置采行信号", "MsrRowPush", null, "Global", new JValue(true)));
        cycle.Add(AddIncVarStep("cm:rows", "报表行数+1", "MsrHistoryRows"));

        // 合格率：(合格 / (合格+超差)) * 100，三条纯算术全在主序列上
        cycle.Add(MathAdd("cm:total", "合格+超差", "MsrGoodCount"));
        cycle.Add(MathCalc("cm:div", "合格占比", "Divide", "MsrGoodCount", "MsrGoodCount", "total"));
        cycle.Add(MathCalc("cm:pct", "换算百分比", "Multiply", "MsrGoodCount", "MsrGoodCount", "total"));
        cycle.Add(VarAssign("cm:yield", "写合格率", "MsrYield", L("cm:pct", "Result", "换算百分比"), "Global", null));

        // 回写 PLC：应答、整件结果、两路关键尺寸（×1000 取整便于 Int16 传）
        cycle.Add(CommWrite("cm:wack", "写应答", "MSR_Ack", new JValue(true), false));
        cycle.Add(CommWrite("cm:wok", "写整件结果", "MSR_Ok", LG("MsrAllOk"), false));
        cycle.Add(CommWrite("cm:walarm", "清测量站报警", "MSR_Alarm", new JValue(false), false));

        s.Add(IfStep("cm:trigif", "触发判断", "Trigger >= 1", G("cm:var:trig"), "Trigger", "System.Int32",
            L("cm:trig", "Value", "读 PLC 到位触发"), cycle,
            new JArray { Delay("cm:idle", "空闲等待", 200) }));

        return s;
    }

    /// <summary>
    /// 生成工件 SN 的 C# 脚本：MSR-&lt;六位序号&gt;-&lt;时间戳尾四位&gt;-&lt;SHA1 校验两位&gt;。
    /// 用 SHA1 而不是简单求和，是为了让"改一位数字"就能看出 SN 被篡改；
    /// 时间戳尾号让同一序号在不同班次也不撞号。
    /// </summary>
    static string BuildCSnScript()
    {
        return
@"// 逐件工件 SN：MSR-<六位序号>-<时间戳尾四位>-<两位校验>
// 序号取自 MsrSnSeq，时间戳尾号取自系统时间，校验位是前两段的 SHA1 前两字节，
// 这样同一件无论重跑多少次都会得到同一个 SN（复检可对齐），不同件不会撞号。
var seq = (int)(Variables.Get(""MsrSnSeq"") ?? 0);
var seed = (int)(Variables.Get(""MsrSeed"") ?? 0);
var stamp = (int)(DateTime.Now.Ticks / 10000L % 10000);
var body = ""MSR-"" + seq.ToString(""D6"") + ""-"" + stamp.ToString(""D4"");
using (var sha = System.Security.Cryptography.SHA1.Create())
{
    var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(body + ""|"" + seed));
    var check = ((bytes[0] << 8) | bytes[1]) & 0xFFFF;
    Variables.Set(""MsrProductSn"", body + ""-"" + check.ToString(""X4""));
}
return true;";
    }

    // ------------------------------------------------------------------ M_Calib

    static JArray BuildCCalibFlow()
    {
        var s = new JArray();
        s.Add(VarAssign("cc:init", "清标定判定", "CalOk", null, "Global", new JValue(false)));

        s.Add(ImgFile("cc:acq", "采集标定板", C_IMG_CALIB, 0));
        s.Add(PreProcess("cc:pre", "标定前处理",
            new JArray(
                Op("gauss", ("Size", "3")),
                Op("emphasize", ("Width", "7"), ("Height", "7"), ("Factor", "1.0"))),
            1, "cc:acq", "采集标定板"));

        // 圆点标定板：先用几何查找把中间那个黑圆拟合出来，作为标定基准点
        s.Add(CreateRoi("cc:roi", "标定板ROI",
            new JArray(
                Roi("CalibPlate", "Rectangle", 140.0, 400.0, 0.0, 90.0, 130.0)),
            2, "cc:pre", "标定前处理"));

        s.Add(GeometryFind("cc:circ", "标定圆拟合", "Circle",
            "cc:roi", "标定板ROI", 1.0, 20.0, 80.0, 30.0, false, 2,
            "cc:pre", "标定前处理"));

        // 通过几何关系把圆直径换算成物理尺寸，作为标定板的已知基准
        s.Add(GeometryRelation("cc:rel", "标定基准换算", "CircleRelation",
            "cc:circ", "标定圆拟合", "cc:circ", "标定圆拟合", C_MM_PER_PX));

        // 标定插件本体：吃原始图 + 基准点，吐出像素当量与残差
        s.Add(Action("cc:calib", "像素当量标定", "标定", P_CALIB, "标定", "\uf05b",
            new JObject
            {
                ["Mode"] = "PixelScale",
                ["GridSize"] = 7,
                ["SourceImagePath"] = C_IMG_CALIB,
                ["DisplayViewIndex"] = 2,
            },
            new JObject
            {
                ["SourceImage"] = L("cc:pre", "Image", "标定前处理"),
                ["SourcePointRow"] = L("cc:circ", "Row", "标定圆拟合"),
                ["SourcePointCol"] = L("cc:circ", "Col", "标定圆拟合"),
            }));

        s.Add(VarAssign("cc:mpp", "写像素当量", "CalMmPerPixel", L("cc:calib", "MmPerPixel", "像素当量标定"), "Global", null));
        s.Add(VarAssign("cc:rms", "写标定残差RMS", "CalResidualRms", L("cc:calib", "ResidualRmsPx", "像素当量标定"), "Global", null));
        s.Add(VarAssign("cc:rmax", "写标定残差最大值", "CalResidualMax", L("cc:calib", "ResidualMaxPx", "像素当量标定"), "Global", null));

        // 残差 ≤ 0.5 px 才算标定通过（这是本方案的口径，写进配方页说明）
        s.Add(Compare("cc:okcmp", "标定残差判定", "cc:calib", "像素当量标定", "ResidualRmsPx", 0.5, "LessThanOrEqual"));
        s.Add(VarAssign("cc:okv", "写标定判定", "CalOk", L("cc:okcmp", "Result", "标定残差判定"), "Global", null));

        // 标定时间戳：C# 脚本写字符串变量，报表里能看到"这套当量是哪天标的"
        s.Add(Action("cc:stamp", "记录标定时间", "变量操作", P_CSCRIPT, "C# 脚本", "\uf017",
            new JObject
            {
                ["Script"] =
                    "Variables.Set(\"CalDateText\", DateTime.Now.ToString(\"yyyy-MM-dd HH:mm:ss\"));\nreturn true;",
            }));

        return s;
    }

    // ------------------------------------------------------------------ M_Measure

    static JArray BuildCMeasureFlow()
    {
        var s = new JArray();
        s.Add(VarAssign("cmz:init1", "清D1判定", "D1Ok", null, "Global", new JValue(false)));
        s.Add(VarAssign("cmz:init2", "清D2判定", "D2Ok", null, "Global", new JValue(false)));
        s.Add(VarAssign("cmz:init3", "清D3判定", "D3Ok", null, "Global", new JValue(false)));
        s.Add(VarAssign("cmz:init4", "清D4判定", "D4Ok", null, "Global", new JValue(false)));

        // ---------------- D1：游标卡尺爪口宽度（caliper_1.png，640×480）
        // 两爪张开的水平缝隙在中上偏左，卡尺矩形要横跨它：Phi=0 → L1 沿水平扫描，
        // 检出的左右两条竖直边就是两爪内侧，宽度 = 两爪间距。
        s.Add(ImgFile("cmz:acq1", "采集卡尺正面", C_IMG_CALIPER, 0));
        s.Add(PreProcess("cmz:pre1", "卡尺前处理",
            new JArray(Op("gauss", ("Size", "5"))),
            1, "cmz:acq1", "采集卡尺正面"));

        s.Add(Action("cmz:d1", "D1-爪口宽度", "尺寸测量", P_CALIPER, "卡尺测量", "\uf0c3",
            new JObject
            {
                ["MeasureKind"] = "Width",
                ["CaliperRegions"] = CaliperRegionsJson(
                    CReg("JawGap", "Rectangle", 120.0, 400.0, 0.0, 60.0, 8.0)),
                ["CaliperCount"] = 9,
                ["Sigma"] = 1.0,
                ["EdgeThreshold"] = 30.0,
                ["Polarity"] = "DarkToLight",
                ["Select"] = "First",
                ["Interpolation"] = "Bilinear",
                ["FitAlgorithm"] = "Tukey",
                ["FitMaxError"] = 1.0,
                ["FitMinPoints"] = 3,
                ["AnnulusWidth"] = 0.0,
                ["StandardValue"] = 20.0,
                ["UpperTolerance"] = 0.05,
                ["LowerTolerance"] = -0.05,
                ["PixelSizeMm"] = C_MM_PER_PX,
                ["DisplayViewIndex"] = 2,
            },
            new JObject
            {
                ["SrcImage"] = L("cmz:pre1", "Image", "卡尺前处理"),
                ["MmPerPixel"] = LG("CalMmPerPixel"),
            }));

        s.Add(VarAssign("cmz:d1v", "写D1实测值", "D1ValueMm", L("cmz:d1", "MeasureValue", "D1-爪口宽度"), "Global", null));
        s.Add(VarAssign("cmz:d1d", "写D1偏差", "D1DevMm", L("cmz:d1", "Deviation", "D1-爪口宽度"), "Global", null));
        s.Add(VarAssign("cmz:d1o", "写D1判定", "D1Ok", L("cmz:d1", "IsOk", "D1-爪口宽度"), "Global", null));

        // ---------------- D2：刻度间距（caliper_with_tilt.png，1280×1024）
        // 竖直黑刻线是从亮带中间到下部；矩形横跨它，Phi=0 水平扫描量出线宽。
        s.Add(ImgFile("cmz:acq2", "采集倾斜钢尺", C_IMG_TILT, 0));
        s.Add(PreProcess("cmz:pre2", "钢尺前处理",
            new JArray(
                Op("gauss", ("Size", "3")),
                Op("gray_closing", ("Width", "3"), ("Height", "3"))),
            1, "cmz:acq2", "采集倾斜钢尺"));

        s.Add(Action("cmz:d2", "D2-刻度间距", "尺寸测量", P_CALIPER, "卡尺测量", "\uf0c3",
            new JObject
            {
                ["MeasureKind"] = "Width",
                ["CaliperRegions"] = CaliperRegionsJson(
                    CReg("TickPitch", "Rectangle", 700.0, 270.0, 0.0, 40.0, 6.0)),
                ["CaliperCount"] = 7,
                ["Sigma"] = 1.2,
                ["EdgeThreshold"] = 28.0,
                ["Polarity"] = "LightToDark",
                ["Select"] = "First",
                ["Interpolation"] = "Bilinear",
                ["FitAlgorithm"] = "Tukey",
                ["FitMaxError"] = 1.0,
                ["FitMinPoints"] = 3,
                ["AnnulusWidth"] = 0.0,
                ["StandardValue"] = 1.0,
                ["UpperTolerance"] = 0.02,
                ["LowerTolerance"] = -0.02,
                ["PixelSizeMm"] = C_MM_PER_PX,
                ["DisplayViewIndex"] = 2,
            },
            new JObject
            {
                ["SrcImage"] = L("cmz:pre2", "Image", "钢尺前处理"),
                ["MmPerPixel"] = LG("CalMmPerPixel"),
            }));

        s.Add(VarAssign("cmz:d2v", "写D2实测值", "D2ValueMm", L("cmz:d2", "MeasureValue", "D2-刻度间距"), "Global", null));
        s.Add(VarAssign("cmz:d2d", "写D2偏差", "D2DevMm", L("cmz:d2", "Deviation", "D2-刻度间距"), "Global", null));
        s.Add(VarAssign("cmz:d2o", "写D2判定", "D2Ok", L("cmz:d2", "IsOk", "D2-刻度间距"), "Global", null));

        // ---------------- D3：标定板圆点直径（caliper_calib_01.png，640×480）
        // CircleDiameter 用得是 Circle 形状区域：[圆心行, 圆心列, 半径]，圆直径只在圆内取边缘。
        s.Add(ImgFile("cmz:acq3", "采集标定板圆点", C_IMG_CALIB, 0));
        s.Add(PreProcess("cmz:pre3", "圆点前处理",
            new JArray(
                Op("gauss", ("Size", "5")),
                Op("emphasize", ("Width", "9"), ("Height", "9"), ("Factor", "1.0"))),
            1, "cmz:acq3", "采集标定板圆点"));

        s.Add(Action("cmz:d3", "D3-圆点直径", "尺寸测量", P_CALIPER, "卡尺测量", "\uf0c3",
            new JObject
            {
                ["MeasureKind"] = "CircleDiameter",
                ["CaliperRegions"] = CaliperRegionsJson(
                    CReg("DotHole", "Circle", 140.0, 400.0, 45.0)),
                ["CaliperCount"] = 12,
                ["Sigma"] = 1.0,
                ["EdgeThreshold"] = 35.0,
                ["Polarity"] = "DarkToLight",
                ["Select"] = "First",
                ["Interpolation"] = "Bilinear",
                ["FitAlgorithm"] = "Huber",
                ["FitMaxError"] = 1.5,
                ["FitMinPoints"] = 6,
                ["AnnulusWidth"] = 0.0,
                ["StandardValue"] = 2.5,
                ["UpperTolerance"] = 0.03,
                ["LowerTolerance"] = -0.03,
                ["PixelSizeMm"] = C_MM_PER_PX,
                ["DisplayViewIndex"] = 2,
            },
            new JObject
            {
                ["SrcImage"] = L("cmz:pre3", "Image", "圆点前处理"),
                ["MmPerPixel"] = LG("CalMmPerPixel"),
            }));

        s.Add(VarAssign("cmz:d3v", "写D3实测值", "D3ValueMm", L("cmz:d3", "MeasureValue", "D3-圆点直径"), "Global", null));
        s.Add(VarAssign("cmz:d3d", "写D3偏差", "D3DevMm", L("cmz:d3", "Deviation", "D3-圆点直径"), "Global", null));
        s.Add(VarAssign("cmz:d3o", "写D3判定", "D3Ok", L("cmz:d3", "IsOk", "D3-圆点直径"), "Global", null));

        // ---------------- D4：刹车盘中心孔径（brake_disk_part_01.png，1280×1024）
        // 中心大亮孔贯穿整个盘面；矩形横跨孔的左右两条竖直边。
        s.Add(ImgFile("cmz:acq4", "采集刹车盘", C_IMG_BRAKE, 0));
        s.Add(PreProcess("cmz:pre4", "刹车盘前处理",
            new JArray(
                Op("gauss", ("Size", "7")),
                Op("gray_closing", ("Width", "5"), ("Height", "5"))),
            1, "cmz:acq4", "采集刹车盘"));

        s.Add(Action("cmz:d4", "D4-中心孔径", "尺寸测量", P_CALIPER, "卡尺测量", "\uf0c3",
            new JObject
            {
                ["MeasureKind"] = "CircleDiameter",
                ["CaliperRegions"] = CaliperRegionsJson(
                    CReg("HubHole", "Circle", 512.0, 640.0, 210.0)),
                ["CaliperCount"] = 16,
                ["Sigma"] = 1.5,
                ["EdgeThreshold"] = 40.0,
                ["Polarity"] = "DarkToLight",
                ["Select"] = "First",
                ["Interpolation"] = "Bilinear",
                ["FitAlgorithm"] = "Tukey",
                ["FitMaxError"] = 2.0,
                ["FitMinPoints"] = 8,
                ["AnnulusWidth"] = 0.0,
                ["StandardValue"] = 30.0,
                ["UpperTolerance"] = 0.06,
                ["LowerTolerance"] = -0.06,
                ["PixelSizeMm"] = C_MM_PER_PX,
                ["DisplayViewIndex"] = 2,
            },
            new JObject
            {
                ["SrcImage"] = L("cmz:pre4", "Image", "刹车盘前处理"),
                ["MmPerPixel"] = LG("CalMmPerPixel"),
            }));

        s.Add(VarAssign("cmz:d4v", "写D4实测值", "D4ValueMm", L("cmz:d4", "MeasureValue", "D4-中心孔径"), "Global", null));
        s.Add(VarAssign("cmz:d4d", "写D4偏差", "D4DevMm", L("cmz:d4", "Deviation", "D4-中心孔径"), "Global", null));
        s.Add(VarAssign("cmz:d4o", "写D4判定", "D4Ok", L("cmz:d4", "IsOk", "D4-中心孔径"), "Global", null));

        // 结果回显：四路读数 + 判定摆一屏（真正的报表在表格页）
        s.Add(ResultDisplay("cmz:show", "测量结果回显", new[]
        {
            ("d1", "爪口宽度(mm)", "cmz:d1", "D1-爪口宽度", "MeasureValue"),
            ("d2", "刻度间距(mm)", "cmz:d2", "D2-刻度间距", "MeasureValue"),
            ("d3", "标定板孔径(mm)", "cmz:d3", "D3-圆点直径", "MeasureValue"),
            ("d4", "刹车盘孔径(mm)", "cmz:d4", "D4-中心孔径", "MeasureValue"),
        }, 2, "cmz:d1", "D1-爪口宽度", "MeasureImage"));

        return s;
    }

    // ------------------------------------------------------------------ SPC

    static JArray BuildCSpcFlow()
    {
        var s = new JArray();

        // 均值：累计和 / 件数
        s.Add(Action("cs:acc", "实测值累加", "数据处理", P_MATH, "数学计算", "\uf1ec",
            new JObject { ["Operation"] = "Add" },
            new JObject
            {
                ["A"] = LG("SpcAccum"),
                ["B"] = LG("D1ValueMm"),
            }));
        s.Add(VarAssign("cs:accw", "写累计和", "SpcAccum", L("cs:acc", "Result", "实测值累加"), "Global", null));

        s.Add(MathCalc("cs:mean", "算均值", "Divide", "SpcAccum", "MsrCycleCount", null));
        s.Add(VarAssign("cs:meanw", "写均值", "SpcMean", L("cs:mean", "Result", "算均值"), "Global", null));

        // 极差：|本件偏差 - 上件偏差|
        s.Add(MathCalc("cs:dev", "算偏差", "Subtract", "D1ValueMm", "D1NominalMm", null));
        s.Add(MathCalc("cs:range", "算极差", "Subtract", "D1ValueMm", "SpcLastDev", null));
        s.Add(VarAssign("cs:rangew", "写极差", "SpcRange", L("cs:range", "Result", "算极差"), "Global", null));
        s.Add(VarAssign("cs:lastw", "记本件偏差", "SpcLastDev", L("cs:dev", "Result", "算偏差"), "Global", null));

        // σ ≈ R / d2（n=2 时 d2=1.128）：用极差估散布，避免在流程里维护大数组
        s.Add(MathCalc("cs:sigma", "极差估σ", "Divide", "SpcRange", "SpcRange", "d2"));
        s.Add(VarAssign("cs:sigmaw", "写σ", "SpcSigma", L("cs:sigma", "Result", "极差估σ"), "Global", null));

        // CPK：min((上限-均值)/(3σ), (均值-下限)/(3σ))，两条算完取小
        s.Add(MathCalc("cs:sup", "上限减均值", "Subtract", "D1UpperMm", "SpcMean", null));
        s.Add(MathCalc("cs:slow", "均值减下限", "Subtract", "SpcMean", "D1LowerMm", null));
        s.Add(MathCalc("cs:t3s", "三倍σ", "Multiply", "SpcSigma", "SpcSigma", "three"));
        s.Add(VarAssign("cs:cpku", "写上侧能力", "SpcCpk", L("cs:sup", "Result", "上限减均值"), "Global", null));

        // CPK 评级：≥1.33 优 / ≥1.0 可接受 / 其余不足
        var c1 = new JArray();
        c1.Add(VarAssign("cs:c1t", "CPK评级-优", "SpcCpkText", null, "Global", new JValue("优 (≥1.33)")));
        var c2 = new JArray();
        c2.Add(VarAssign("cs:c2t", "CPK评级-可接受", "SpcCpkText", null, "Global", new JValue("可接受 (≥1.0)")));
        var c3 = new JArray();
        c3.Add(VarAssign("cs:c3t", "CPK评级-不足", "SpcCpkText", null, "Global", new JValue("不足 (<1.0)")));

        var ifMid = new JArray
        {
            IfStep("cs:if2", "可接受判定", "Value >= 1.0", G("cs:var:if2"), "Value", "System.Double",
                LG("SpcCpk"), new JArray(), c3),
        };
        ifMid.Add(IfStep("cs:if2b", "可接受分支写文本", "Value >= 1.0", G("cs:var:if2b"), "Value", "System.Double",
            LG("SpcCpk"), c2, new JArray()));

        s.Add(IfStep("cs:if1", "优良判定", "Value >= 1.33", G("cs:var:if1"), "Value", "System.Double",
            LG("SpcCpk"), c1, ifMid));

        return s;
    }

    // ------------------------------------------------------------------ Recipe

    static JArray BuildCRecipeFlow()
    {
        var s = new JArray();
        s.Add(VarAssign("cr:sn", "SN 序号归零", "MsrSnSeq", null, "Global", new JValue(0)));
        s.Add(VarAssign("cr:g1", "合格归零", "MsrGoodCount", null, "Global", new JValue(0)));
        s.Add(VarAssign("cr:g2", "超差归零", "MsrNgCount", null, "Global", new JValue(0)));
        s.Add(VarAssign("cr:g3", "节拍归零", "MsrCycleCount", null, "Global", new JValue(0)));
        s.Add(VarAssign("cr:g4", "行数归零", "MsrHistoryRows", null, "Global", new JValue(0)));
        s.Add(VarAssign("cr:g5", "合格率归零", "MsrYield", null, "Global", new JValue(0.0)));
        s.Add(VarAssign("cr:g6", "累计清零", "SpcAccum", null, "Global", new JValue(0.0)));
        s.Add(VarAssign("cr:g7", "CPK清零", "SpcCpk", null, "Global", new JValue(0.0)));
        s.Add(CcgCsvRecord("cr:csv", "换型记录", new JArray
        {
            Col("时间", "BuiltIn", "", "Timestamp"),
            Col("序号", "BuiltIn", "", "Sequence"),
            Col("结果", "Variable", "CGradeText", "Timestamp"),
            Col("配方", "Variable", "RecipeName", "Timestamp"),
        }));
        return s;
    }

    // ------------------------------------------------------------------ End

    static JArray BuildCEndFlow()
    {
        var s = new JArray();
        s.Add(VarAssign("ce:ready", "清系统就绪", "SystemReady", null, "Global", new JValue(false)));
        s.Add(MotionStop("ce:stopX", "停 X 轴", C_AXIS_X, "Decelerate", false));
        s.Add(MotionStop("ce:stopY", "停 Y 轴", C_AXIS_Y, "Decelerate", false));
        s.Add(MotionStop("ce:stopZ", "停 Z 轴", C_AXIS_Z, "Decelerate", false));
        s.Add(CcgCsvRecord("ce:csv", "班次收尾记录", new JArray
        {
            Col("时间", "BuiltIn", "", "Timestamp"),
            Col("序号", "BuiltIn", "", "Sequence"),
            Col("结果", "Variable", "CGradeText", "Timestamp"),
            Col("良率", "Variable", "CYield", "Timestamp"),
        }));
        s.Add(CommWrite("ce:ack", "清应答", "MSR_Ack", new JValue(false), false));
        s.Add(VarAssign("ce:grade", "判定复位", "MsrGradeText", null, "Global", new JValue("停机")));
        return s;
    }

    // ------------------------------------------------------------------ 工位调用辅助

    static JArray CallCStation(string seed, string name, string flowName, int timeoutMs, int faultCode)
    {
        var run = RunFlow(seed + ":run", name, flowName, timeoutMs);

        var fault = new JArray
        {
            VarAssign(seed + ":flt", "置故障挂起", "MsrStationFault", null, "Global", new JValue(true)),
            VarAssign(seed + ":code", "写故障码", "MsrFaultCode", null, "Global", new JValue(faultCode)),
            MotionStop(seed + ":stop", "急停 Z 轴", C_AXIS_Z, "Decelerate", true),
            CommWrite(seed + ":alarm", "写测量站报警", "MSR_Alarm", new JValue(true), false),
            ReturnStep(seed + ":ret", "跳出本轮"),
        };

        // RunFlow 的输出端口叫 Invoked（不是 Ok），判失败必须绑 Invoked。
        var okIf = IfStep(seed + ":if", "子流程故障判定", "!Invoked",
            G(seed + ":var:ok"), "Invoked", "System.Boolean",
            LStep(run, "Invoked", name), fault, new JArray());

        return new JArray { run, okIf };
    }

    // ------------------------------------------------------------------ 卡尺区域辅助

    /// <summary>
    /// 一行卡尺区域。形状与 Params 一一对应 HALCON 生成函数：
    /// Rectangle = [中心行, 中心列, 角度Phi, 半长L1, 半宽L2]；
    /// Circle    = [圆心行, 圆心列, 半径]。
    /// L1 沿 Phi 方向 = 每条卡尺的扫描方向；L2 垂直 Phi = 卡尺阵列均布方向。
    /// </summary>
    static JObject CReg(string name, string shape, params double[] p)
    {
        var arr = new JArray();
        foreach (var v in p) arr.Add(v);

        return new JObject
        {
            ["Name"] = name,
            ["ShapeType"] = shape,
            ["Params"] = arr,
        };
    }

    /// <summary>CaliperRegions 的 InputValue 是 JSON 串（集合型配置），不是对象数组。</summary>
    static string CaliperRegionsJson(params JObject[] regions)
    {
        var arr = new JArray();
        foreach (var r in regions) arr.Add(r);
        return arr.ToString(Formatting.None);
    }

    // ------------------------------------------------------------------ 组态

    static JObject BuildCScada()
    {
        var pages = new JArray
        {
            CMeasureOverviewPage(),
            CMeasureDataPage(),
            CMeasureDetailPage(),
            CMeasureRecipePage(),
        };

        return new JObject
        {
            ["SchemaVersion"] = 1,
            ["Pages"] = pages,
            ["Alarms"] = BuildCAlarms(),
            ["VariableEvents"] = new JArray(),
            ["StartupPageId"] = G("page:运行总览").ToString(),
            ["BasePages"] = new JArray(),
        };
    }

    static JArray BuildCAlarms()
    {
        return new JArray
        {
            ScadaAlarm("标定残差超限", "CalResidualRms", 0, 0.5, 1, "标定残差超过 0.5px，像素当量不可信", "标定", "标定板污损或镜头离焦", "擦拭标定板后重新标定"),
            ScadaAlarm("测量站故障挂起", "MsrStationFault", 4, 0.0, 2, "测量站故障挂起，需人工复位", "设备", "子流程返回失败", "查故障码后复位"),
            ScadaAlarm("CPK 低于 1.0", "SpcCpk", 3, 1.0, 1, "过程能力不足，建议检查量具与工装", "SPC", "散布变大或中心偏移", "复核标准值并做 MSA"),
            ScadaAlarm("系统未就绪", "SystemReady", 5, 0.0, 0, "系统未就绪，不能开始测量", "设备", "未回零或停机", "点运行按钮执行 Home"),
        };
    }

    // ------------------------------------------------------------------ 页面 1：运行总览

    static JObject CMeasureOverviewPage()
    {
        const string P = "运行总览";
        var els = new JArray();
        AddCHeader(els, P, "标定 → 四路卡尺 → 公差判定 → SPC，逐件 SN 全流程可追溯");

        // KPI 四联
        els.Add(El(P, "KPI", "Hmi.KpiBoard", 40, 180, 1180, 140, 10, new JObject
        {
            ["Columns"] = "4",
            ["Label1"] = "合格累计", ["Value1"] = "", ["Unit1"] = "件",
            ["Label2"] = "超差累计", ["Value2"] = "", ["Unit2"] = "件",
            ["Label3"] = "合格率", ["Value3"] = "", ["Unit3"] = "%",
            ["Label4"] = "测量节拍", ["Value4"] = "", ["Unit4"] = "轮",
        }, new JArray
        {
            Bind("Value1", "MsrGoodCount"),
            Bind("Value2", "MsrNgCount"),
            Bind("Value3", "MsrYield", "F1"),
            Bind("Value4", "MsrCycleCount"),
        }));

        // 本件判定
        els.Add(El(P, "判定卡", "Hmi.Rectangle", 1240, 180, 340, 140, 10,
            new JObject { ["Fill"] = "#FF1E293B", ["Stroke"] = "#FF334155", ["StrokeThickness"] = "1", ["CornerRadius"] = "8" }));
        els.Add(El(P, "本件判定", "Hmi.Text", 1264, 196, 292, 60, 11,
            TextProps("待测", 36, "#FF22C55E", "Bold"), new JArray { Bind("Text", "MsrGradeText") }));
        els.Add(El(P, "本件判定灯", "Hmi.Lamp", 1456, 200, 96, 96, 11, new JObject
        {
            ["OnColor"] = "#FF22C55E", ["OffColor"] = "#FFEF4444"
        }, new JArray { Bind("State", "MsrAllOk") }));
        els.Add(El(P, "SN提示", "Hmi.Text", 1264, 268, 292, 22, 11,
            TextProps("当前工件 SN", 12, "#FF94A3B8")));
        els.Add(El(P, "SN值", "Hmi.Text", 1264, 290, 292, 24, 11,
            TextProps("等待首件…", 15, "#FFE2E8F0", "SemiBold"), new JArray { Bind("Text", "MsrProductSn") }));

        // 工件 SN 大字卡
        els.Add(El(P, "SN卡", "Hmi.Rectangle", 40, 340, 760, 120, 10,
            new JObject { ["Fill"] = "#FF0F172A", ["Stroke"] = "#FF1E40AF", ["StrokeThickness"] = "1", ["CornerRadius"] = "8" }));
        els.Add(El(P, "SN标题", "Hmi.Text", 64, 356, 400, 24, 11,
            TextProps("当前工件序列号 SN（逐件生成，复检按此对齐）", 13, "#FF93C5FD")));
        els.Add(El(P, "SN大值", "Hmi.Text", 64, 384, 700, 56, 11,
            TextProps("等待首件…", 32, "#FFF8FAFC", "Bold"), new JArray { Bind("Text", "MsrProductSn") }));

        // 标定区
        els.Add(El(P, "标定卡", "Hmi.Rectangle", 840, 340, 740, 120, 10,
            new JObject { ["Fill"] = "#FF1E293B", ["Stroke"] = "#FF334155", ["StrokeThickness"] = "1", ["CornerRadius"] = "8" }));
        els.Add(El(P, "标定标题", "Hmi.Text", 864, 352, 400, 24, 11,
            TextProps("标定（下游四路卡尺共用当量）", 13, "#FFCBD5E1")));
        els.Add(El(P, "标定灯", "Hmi.Lamp", 864, 380, 56, 56, 11, new JObject
        {
            ["OnColor"] = "#FF22C55E", ["OffColor"] = "#FFF59E0B"
        }, new JArray { Bind("State", "CalOk") }));
        els.Add(El(P, "当量值", "Hmi.IOField", 936, 380, 200, 30, 11, new JObject
        {
            ["ValueFormat"] = "F5", ["Unit"] = "mm/px", ["Mode"] = "Output", ["Text"] = "像素当量", ["TextAlignment"] = "Right"
        }, new JArray { Bind("Value", "CalMmPerPixel") }));
        els.Add(El(P, "残差值", "Hmi.IOField", 1152, 380, 200, 30, 11, new JObject
        {
            ["ValueFormat"] = "F3", ["Unit"] = "px", ["Mode"] = "Output", ["Text"] = "残差RMS", ["TextAlignment"] = "Right"
        }, new JArray { Bind("Value", "CalResidualRms") }));
        els.Add(El(P, "标定时间", "Hmi.TextField", 936, 418, 416, 30, 11, new JObject
        {
            ["Placeholder"] = "尚未标定", ["MaxLength"] = "40", ["Mode"] = "Output"
        }, new JArray { Bind("Value", "CalDateText") }));

        // 四路尺寸卡：一行四格，各显示实测值 + 判定灯
        AddCDimCard(els, P, "D1", 40, "爪口宽度", "D1ValueMm", "D1Ok", "F4", "mm");
        AddCDimCard(els, P, "D2", 500, "刻度间距", "D2ValueMm", "D2Ok", "F4", "mm");
        AddCDimCard(els, P, "D3", 960, "标定板孔径", "D3ValueMm", "D3Ok", "F4", "mm");
        AddCDimCard(els, P, "D4", 1420, "刹车盘孔径", "D4ValueMm", "D4Ok", "F4", "mm");

        // SPC 卡
        els.Add(El(P, "SPC卡", "Hmi.Rectangle", 40, 640, 1180, 170, 10,
            new JObject { ["Fill"] = "#FF1E293B", ["Stroke"] = "#FF334155", ["StrokeThickness"] = "1", ["CornerRadius"] = "8" }));
        els.Add(El(P, "SPC标题", "Hmi.Text", 64, 656, 500, 26, 11,
            TextProps("过程能力 SPC（对 D1 爪口宽度）", 15, "#FFF8FAFC", "SemiBold")));
        els.Add(El(P, "CPK标签", "Hmi.Text", 64, 700, 120, 24, 11, TextProps("CPK", 13, "#FF94A3B8")));
        els.Add(El(P, "CPK值", "Hmi.IOField", 160, 698, 140, 30, 11, new JObject
        {
            ["ValueFormat"] = "F3", ["Mode"] = "Output", ["Text"] = "", ["TextAlignment"] = "Right"
        }, new JArray { Bind("Value", "SpcCpk") }));
        els.Add(El(P, "CPK评级", "Hmi.Text", 316, 700, 260, 26, 11,
            TextProps("", 16, "#FFF59E0B", "SemiBold"), new JArray { Bind("Text", "SpcCpkText") }));
        els.Add(El(P, "均值标签", "Hmi.Text", 600, 700, 100, 24, 11, TextProps("均值", 13, "#FF94A3B8")));
        els.Add(El(P, "均值值", "Hmi.IOField", 690, 698, 150, 30, 11, new JObject
        {
            ["ValueFormat"] = "F4", ["Unit"] = "mm", ["Mode"] = "Output", ["Text"] = "", ["TextAlignment"] = "Right"
        }, new JArray { Bind("Value", "SpcMean") }));
        els.Add(El(P, "极差标签", "Hmi.Text", 860, 700, 100, 24, 11, TextProps("极差 R", 13, "#FF94A3B8")));
        els.Add(El(P, "极差值", "Hmi.IOField", 950, 698, 150, 30, 11, new JObject
        {
            ["ValueFormat"] = "F4", ["Unit"] = "mm", ["Mode"] = "Output", ["Text"] = "", ["TextAlignment"] = "Right"
        }, new JArray { Bind("Value", "SpcRange") }));
        els.Add(El(P, "CPK提示", "Hmi.Text", 64, 754, 1100, 44, 11, new JObject
        {
            ["Text"] = "CPK 按 (上限−均值)/(3σ) 与 (均值−下限)/(3σ) 取小；σ 用极差法估算（n=2，d2=1.128），单件测量站够用，量产请换 R-bar 或 X-bar-R 图。",
            ["Foreground"] = "#FF64748B", ["FontSize"] = "12",
            ["TextAlignment"] = "Left", ["VerticalContentAlignment"] = "Top", ["TextWrapping"] = "Wrap",
        }));

        // D1 偏差趋势
        els.Add(El(P, "趋势标题", "Hmi.Text", 1240, 640, 400, 26, 11,
            TextProps("D1 偏差趋势 / D4 实测趋势", 15, "#FFF8FAFC", "SemiBold")));
        els.Add(El(P, "趋势", "Hmi.MultiTrend", 1240, 672, 640, 138, 10, new JObject
        {
            ["MaxSeries"] = "2",
            ["ShowLegend"] = "True",
            ["SeriesLabel1"] = "D1 偏差 mm",
            ["SeriesLabel2"] = "D4 孔径 mm",
            ["WindowSeconds"] = "180",
            ["AutoScale"] = "True",
            ["ShowGrid"] = "True",
        }, new JArray
        {
            Bind("Value1", "D1DevMm"),
            Bind("Value2", "D4ValueMm"),
        }));

        // 报警条
        els.Add(El(P, "报警条", "Hmi.AlarmBanner", 40, 830, 1840, 80, 10, new JObject
        {
            ["MaxRows"] = "3",
            ["EmptyText"] = "当前无报警",
            ["NormalColor"] = "#FF16A34A",
            ["InfoColor"] = "#FF0EA5E9",
            ["WarningColor"] = "#FFF59E0B",
            ["CriticalColor"] = "#FFEF4444",
        }));

        // 运行控制
        els.Add(TestButton(P, "启动测量", 40, 940, "Main"));
        els.Add(NavButton(P, "尺寸报表", 220, 940, "尺寸报表"));
        els.Add(NavButton(P, "测量详情", 380, 940, "测量详情"));
        els.Add(NavButton(P, "标定与配方", 540, 940, "标定与配方"));
        els.Add(WriteButton(P, "故障复位", 700, 940, "MsrStationFault", "False", "#FF16A34A"));
        els.Add(WriteButton(P, "计数清零", 870, 940, "MsrGoodCount", "0", "#FF475569"));

        return ScadaPageObj(P, 1920, 1080, els);
    }

    /// <summary>一路尺寸卡：标题 + 实测值 + 判定灯 + 偏差。</summary>
    static void AddCDimCard(JArray els, string page, string tag, int x, string title,
        string valueVar, string okVar, string format, string unit)
    {
        els.Add(El(page, tag + "卡", "Hmi.Rectangle", x, 486, 440, 130, 10,
            new JObject { ["Fill"] = "#FF1E293B", ["Stroke"] = "#FF334155", ["StrokeThickness"] = "1", ["CornerRadius"] = "8" }));
        els.Add(El(page, tag + "标题", "Hmi.Text", x + 20, 498, 400, 24, 11,
            TextProps(title, 14, "#FFCBD5E1", "SemiBold")));
        els.Add(El(page, tag + "实测", "Hmi.IOField", x + 20, 528, 240, 34, 11, new JObject
        {
            ["ValueFormat"] = format, ["Unit"] = unit, ["Mode"] = "Output", ["Text"] = "实测", ["TextAlignment"] = "Right"
        }, new JArray { Bind("Value", valueVar) }));
        els.Add(El(page, tag + "灯", "Hmi.Lamp", x + 276, 524, 56, 56, 11, new JObject
        {
            ["OnColor"] = "#FF22C55E", ["OffColor"] = "#FFEF4444"
        }, new JArray { Bind("State", okVar) }));
        els.Add(El(page, tag + "偏差", "Hmi.IOField", x + 20, 570, 240, 30, 11, new JObject
        {
            ["ValueFormat"] = "+0.0000;-0.0000;0.0000", ["Unit"] = unit, ["Mode"] = "Output",
            ["Text"] = "偏差", ["TextAlignment"] = "Right"
        }, new JArray { Bind("Value", tag + "DevMm") }));
    }

    // ------------------------------------------------------------------ 页面 2：尺寸报表

    static JObject CMeasureDataPage()
    {
        const string P = "尺寸报表";
        var els = new JArray();
        AddCHeader(els, P, "逐件 SN / 四路实测与判定 / CPK —— 最新在最上");

        var cols = new JArray
        {
            CCol("SN", "MsrProductSn", 250, "left", true),
            CCol("D1 爪口", "D1ValueMm", 110, "right", false),
            CCol("D1 偏差", "D1DevMm", 110, "right", false),
            CCol("D2 刻度", "D2ValueMm", 110, "right", false),
            CCol("D3 孔径", "D3ValueMm", 110, "right", false),
            CCol("D4 孔径", "D4ValueMm", 110, "right", false),
            CCol("判定", "MsrGradeText", 90, "center", false),
            CCol("CPK", "SpcCpk", 90, "right", false),
            CCol("标定当量", "CalMmPerPixel", 130, "right", false),
            CCol("时间", "CalDateText", 200, "left", true),
        };

        els.Add(El(P, "尺寸表格", "Hmi.DataGrid", 40, 180, 1840, 520, 10, new JObject
        {
            ["GridColumns"] = cols.ToString(Formatting.None),
            ["SampleVariable"] = "MsrRowPush",
            ["AckVariable"] = "MsrHistoryRows",
            ["MaxRows"] = "300",
            ["RowHeight"] = "32",
            ["NewestFirst"] = "True",
            ["Zebra"] = "True",
            ["Source"] = "Live",
        }));

        els.Add(El(P, "采行说明", "Hmi.Text", 40, 712, 1600, 24, 11,
            TextProps("每测完一件，Main 流程把「MsrRowPush」翻转一次，表格吃这个上升沿采一行；表格自身不复位信号，只做显示缓冲。", 13, "#FF64748B")));

        // 统计卡
        els.Add(El(P, "统计卡", "Hmi.Rectangle", 40, 752, 560, 170, 10,
            new JObject { ["Fill"] = "#FF1E293B", ["Stroke"] = "#FF334155", ["StrokeThickness"] = "1", ["CornerRadius"] = "8" }));
        els.Add(El(P, "统计标题", "Hmi.Text", 64, 766, 300, 24, 11, TextProps("本批统计", 14, "#FFCBD5E1", "SemiBold")));
        els.Add(El(P, "行数标签", "Hmi.Text", 64, 800, 140, 24, 11, TextProps("已采行数", 13, "#FF94A3B8")));
        els.Add(El(P, "行数值", "Hmi.IOField", 210, 798, 160, 30, 11, new JObject
        {
            ["ValueFormat"] = "F0", ["Unit"] = "行", ["Mode"] = "Output", ["Text"] = "", ["TextAlignment"] = "Right"
        }, new JArray { Bind("Value", "MsrHistoryRows") }));
        els.Add(El(P, "合格标签", "Hmi.Text", 64, 838, 140, 24, 11, TextProps("合格 / 超差", 13, "#FF94A3B8")));
        els.Add(El(P, "合格值", "Hmi.IOField", 210, 836, 74, 30, 11, new JObject
        {
            ["ValueFormat"] = "F0", ["Mode"] = "Output", ["Text"] = "", ["TextAlignment"] = "Right"
        }, new JArray { Bind("Value", "MsrGoodCount") }));
        els.Add(El(P, "超差值", "Hmi.IOField", 292, 836, 74, 30, 11, new JObject
        {
            ["ValueFormat"] = "F0", ["Mode"] = "Output", ["Text"] = "", ["TextAlignment"] = "Right"
        }, new JArray { Bind("Value", "MsrNgCount") }));
        els.Add(El(P, "CPK标签2", "Hmi.Text", 64, 876, 140, 24, 11, TextProps("当前 CPK", 13, "#FF94A3B8")));
        els.Add(El(P, "CPK值2", "Hmi.IOField", 210, 874, 160, 30, 11, new JObject
        {
            ["ValueFormat"] = "F3", ["Mode"] = "Output", ["Text"] = "", ["TextAlignment"] = "Right"
        }, new JArray { Bind("Value", "SpcCpk") }));

        // 报表口径说明
        els.Add(El(P, "口径卡", "Hmi.Rectangle", 640, 752, 1240, 170, 10,
            new JObject { ["Fill"] = "#FF0F172A", ["Stroke"] = "#FF1E40AF", ["StrokeThickness"] = "1", ["CornerRadius"] = "8" }));
        els.Add(El(P, "口径标题", "Hmi.Text", 664, 766, 500, 24, 11,
            TextProps("报表口径（为什么这么排）", 14, "#FF93C5FD", "SemiBold")));
        els.Add(El(P, "口径正文", "Hmi.Text", 664, 800, 1190, 110, 11, new JObject
        {
            ["Text"] =
                "1) 四路尺寸并列而不是堆成子行：一件一行的表最好扫，超差件整行一眼看出哪一路红。\n" +
                "2) 标定当量入表：换班次/换镜头后当量会变，离开当量的尺寸值没法追溯，所以每行都记。\n" +
                "3) 时间是标定时间而非测量时间（标定时间戳由 M_Calib 写），用来圈定「这批数据用的是哪套当量」。",
            ["Foreground"] = "#FFE2E8F0", ["FontSize"] = "13",
            ["TextAlignment"] = "Left", ["VerticalContentAlignment"] = "Top", ["TextWrapping"] = "Wrap",
        }));

        els.Add(NavButton(P, "返回总览", 40, 950, "运行总览"));
        els.Add(NavButton(P, "测量详情", 200, 950, "测量详情"));
        els.Add(WriteButton(P, "清空计数", 360, 950, "MsrHistoryRows", "0", "#FF475569"));

        return ScadaPageObj(P, 1920, 1080, els);
    }

    // ------------------------------------------------------------------ 页面 3：测量详情

    static JObject CMeasureDetailPage()
    {
        const string P = "测量详情";
        var els = new JArray();
        AddCHeader(els, P, "四路测量的现场图与逐项读数（卡尺区域、极性、当量都写在这里）");

        AddCDetailCard(els, P, "D1", 40, "D1 · 爪口宽度 20.00 ±0.05 mm",
            C_IMG_CALIPER, "Width · DarkToLight · 9 把卡尺 · Φ=0 水平扫描两爪内侧",
            "D1ValueMm", "D1DevMm", "D1Ok");

        AddCDetailCard(els, P, "D2", 500, "D2 · 刻度间距 1.000 ±0.020 mm",
            C_IMG_TILT, "Width · LightToDark · 7 把卡尺 · 横跨竖直刻线量线宽",
            "D2ValueMm", "D2DevMm", "D2Ok");

        AddCDetailCard(els, P, "D3", 960, "D3 · 标定板孔径 2.500 ±0.030 mm",
            C_IMG_CALIB, "CircleDiameter · 12 把卡尺 · Circle 区域 [行,列,半径]",
            "D3ValueMm", "D3DevMm", "D3Ok");

        AddCDetailCard(els, P, "D4", 1420, "D4 · 刹车盘孔径 30.00 ±0.06 mm",
            C_IMG_BRAKE, "CircleDiameter · 16 把卡尺 · Huber 拟合抗反光",
            "D4ValueMm", "D4DevMm", "D4Ok");

        // 报警条
        els.Add(El(P, "报警条", "Hmi.AlarmBanner", 40, 830, 1840, 80, 10, new JObject
        {
            ["MaxRows"] = "3",
            ["EmptyText"] = "四路尺寸均在公差内",
            ["NormalColor"] = "#FF16A34A",
            ["InfoColor"] = "#FF0EA5E9",
            ["WarningColor"] = "#FFF59E0B",
            ["CriticalColor"] = "#FFEF4444",
        }));

        // 轴控台（换截面时要移 X/Z）
        els.Add(El(P, "轴控台", "Hmi.AxisConsole", 40, 880, 300, 150, 10, new JObject
        {
            ["AxisName"] = C_AXIS_X,
            ["Title"] = "测量平台 X 轴",
            ["ShowPoints"] = "False",
            ["ShowCommands"] = "True",
            ["AccentColor"] = "#FF0EA5E9",
        }));

        els.Add(NavButton(P, "返回总览", 620, 950, "运行总览"));
        els.Add(NavButton(P, "尺寸报表", 780, 950, "尺寸报表"));

        return ScadaPageObj(P, 1920, 1080, els);
    }

    static void AddCDetailCard(JArray els, string page, string tag, int x, string title,
        string img, string memo, string valueVar, string devVar, string okVar)
    {
        els.Add(El(page, tag + "卡", "Hmi.Rectangle", x, 180, 440, 620, 10,
            new JObject { ["Fill"] = "#FF1E293B", ["Stroke"] = "#FF334155", ["StrokeThickness"] = "1", ["CornerRadius"] = "8" }));
        els.Add(El(page, tag + "标题", "Hmi.Text", x + 20, 194, 400, 26, 11,
            TextProps(title, 14, "#FFF8FAFC", "SemiBold")));
        els.Add(El(page, tag + "图", "Hmi.Picture", x + 20, 228, 400, 300, 11, new JObject
        {
            ["SourcePath"] = img,
        }));
        els.Add(El(page, tag + "说明", "Hmi.Text", x + 20, 536, 400, 44, 11, new JObject
        {
            ["Text"] = memo,
            ["Foreground"] = "#FF94A3B8", ["FontSize"] = "12",
            ["TextAlignment"] = "Left", ["VerticalContentAlignment"] = "Top", ["TextWrapping"] = "Wrap",
        }));
        els.Add(El(page, tag + "实测标签", "Hmi.Text", x + 20, 588, 160, 24, 11, TextProps("实测值", 13, "#FFCBD5E1")));
        els.Add(El(page, tag + "实测", "Hmi.IOField", x + 20, 614, 220, 32, 11, new JObject
        {
            ["ValueFormat"] = "F4", ["Unit"] = "mm", ["Mode"] = "Output", ["Text"] = "", ["TextAlignment"] = "Right"
        }, new JArray { Bind("Value", valueVar) }));
        els.Add(El(page, tag + "偏差标签", "Hmi.Text", x + 20, 656, 160, 24, 11, TextProps("偏差", 13, "#FFCBD5E1")));
        els.Add(El(page, tag + "偏差", "Hmi.IOField", x + 20, 682, 220, 32, 11, new JObject
        {
            ["ValueFormat"] = "+0.0000;-0.0000;0.0000", ["Unit"] = "mm", ["Mode"] = "Output", ["Text"] = "", ["TextAlignment"] = "Right"
        }, new JArray { Bind("Value", devVar) }));
        els.Add(El(page, tag + "判定灯", "Hmi.Lamp", x + 272, 614, 96, 96, 11, new JObject
        {
            ["OnColor"] = "#FF22C55E", ["OffColor"] = "#FFEF4444"
        }, new JArray { Bind("State", okVar) }));
        els.Add(El(page, tag + "判定字", "Hmi.Text", x + 272, 716, 148, 28, 11,
            TextProps("", 16, "#FFE2E8F0", "SemiBold"), new JArray { Bind("Text", okVar) }));
    }

    // ------------------------------------------------------------------ 页面 4：标定与配方

    static JObject CMeasureRecipePage()
    {
        const string P = "标定与配方";
        var els = new JArray();
        AddCHeader(els, P, "当量与公差都走变量，换型只改数值不改流程");

        // 标定卡
        els.Add(El(P, "标定卡", "Hmi.Rectangle", 40, 180, 900, 300, 10,
            new JObject { ["Fill"] = "#FF1E293B", ["Stroke"] = "#FF334155", ["StrokeThickness"] = "1", ["CornerRadius"] = "8" }));
        els.Add(El(P, "标定卡标题", "Hmi.Text", 64, 196, 500, 28, 11,
            TextProps("标定结果（M_Calib 每件重算）", 16, "#FFF8FAFC", "SemiBold")));

        els.Add(El(P, "当量标签", "Hmi.Text", 64, 240, 200, 24, 11, TextProps("像素当量 mm/px", 13, "#FF94A3B8")));
        els.Add(El(P, "当量字段", "Hmi.IOField", 64, 266, 220, 34, 11, new JObject
        {
            ["ValueFormat"] = "F5", ["Mode"] = "Input", ["Text"] = "", ["TextAlignment"] = "Right"
        }, new JArray { Bind("Value", "CalMmPerPixel") }));

        els.Add(El(P, "残差标签", "Hmi.Text", 320, 240, 200, 24, 11, TextProps("残差 RMS px", 13, "#FF94A3B8")));
        els.Add(El(P, "残差字段", "Hmi.IOField", 320, 266, 160, 34, 11, new JObject
        {
            ["ValueFormat"] = "F3", ["Mode"] = "Output", ["Text"] = "", ["TextAlignment"] = "Right"
        }, new JArray { Bind("Value", "CalResidualRms") }));

        els.Add(El(P, "残差最大标签", "Hmi.Text", 520, 240, 220, 24, 11, TextProps("残差最大值 px", 13, "#FF94A3B8")));
        els.Add(El(P, "残差最大字段", "Hmi.IOField", 520, 266, 160, 34, 11, new JObject
        {
            ["ValueFormat"] = "F3", ["Mode"] = "Output", ["Text"] = "", ["TextAlignment"] = "Right"
        }, new JArray { Bind("Value", "CalResidualMax") }));

        els.Add(El(P, "标定状态", "Hmi.Lamp", 720, 236, 72, 72, 11, new JObject
        {
            ["OnColor"] = "#FF22C55E", ["OffColor"] = "#FFF59E0B"
        }, new JArray { Bind("State", "CalOk") }));
        els.Add(El(P, "标定时间字段", "Hmi.TextField", 64, 322, 500, 32, 11, new JObject
        {
            ["Placeholder"] = "尚未标定", ["MaxLength"] = "40", ["Mode"] = "Output"
        }, new JArray { Bind("Value", "CalDateText") }));
        els.Add(WriteButton(P, "触发一次标定", 64, 380, "SystemReady", "True", "#FF0EA5E9"));

        // 公差卡
        els.Add(El(P, "公差卡", "Hmi.Rectangle", 980, 180, 900, 300, 10,
            new JObject { ["Fill"] = "#FF1E293B", ["Stroke"] = "#FF334155", ["StrokeThickness"] = "1", ["CornerRadius"] = "8" }));
        els.Add(El(P, "公差卡标题", "Hmi.Text", 1004, 196, 500, 28, 11,
            TextProps("公差表（每路一项，改完点应用）", 16, "#FFF8FAFC", "SemiBold")));

        AddCTolRow(els, P, 1004, 236, "D1 爪口宽", "D1NominalMm", "D1UpperMm", "D1LowerMm");
        AddCTolRow(els, P, 1004, 280, "D2 刻度距", "D2NominalMm", "D2UpperMm", "D2LowerMm");
        AddCTolRow(els, P, 1004, 324, "D3 板孔径", "D3NominalMm", "D3UpperMm", "D3LowerMm");
        AddCTolRow(els, P, 1004, 368, "D4 盘孔径", "D4NominalMm", "D4UpperMm", "D4LowerMm");

        els.Add(WriteButton(P, "应用配方", 1004, 418, "MsrSeed", "20261105", "#FF0EA5E9"));
        els.Add(WriteButton(P, "计数清零", 1172, 418, "MsrGoodCount", "0", "#FF475569"));

        // 口径说明
        els.Add(El(P, "说明卡", "Hmi.Rectangle", 40, 500, 1840, 300, 10,
            new JObject { ["Fill"] = "#FF0F172A", ["Stroke"] = "#FF1E40AF", ["StrokeThickness"] = "1", ["CornerRadius"] = "8" }));
        els.Add(El(P, "说明标题", "Hmi.Text", 64, 516, 700, 28, 11,
            TextProps("本方案的四条测量口径", 16, "#FF93C5FD", "SemiBold")));
        els.Add(El(P, "说明正文", "Hmi.Text", 64, 552, 1780, 230, 11, new JObject
        {
            ["Text"] =
                "1) 当量只有一个来源：M_Calib 的 MmPerPixel 接到四路卡尺的 MmPerPixel 端口，卡尺不再手填；" +
                "未接上游时才回落手填 PixelSizeMm，避免「一半手填一半上游」的静默错数。\n" +
                "2) 卡尺矩形必须横跨被测边：半长 L1 沿扫描方向（Phi 方向），边缘垂直于它；" +
                "半宽 L2 垂直于 Phi，是卡尺阵列均布方向。L1 太小或 Phi 转 90° 都检不到边。\n" +
                "3) 判定在插件内做：每路卡尺自带 StandardValue / Upper / LowerTolerance，输出的 IsOk 直接落变量，" +
                "流程只做「四路全过」的逻辑与，不在流程里重复写公差比较。\n" +
                "4) CPK 用极差法：σ ≈ R / d2（n=2 取 1.128），单件测量站够用；量产请换 X-bar-R 图或直接读 SPC 数据库。",
            ["Foreground"] = "#FFE2E8F0", ["FontSize"] = "13",
            ["TextAlignment"] = "Left", ["VerticalContentAlignment"] = "Top", ["TextWrapping"] = "Wrap",
        }));

        els.Add(NavButton(P, "返回总览", 40, 830, "运行总览"));
        els.Add(NavButton(P, "尺寸报表", 200, 830, "尺寸报表"));

        return ScadaPageObj(P, 1920, 1080, els);
    }

    static void AddCTolRow(JArray els, string page, int x, int y, string title,
        string nominal, string upper, string lower)
    {
        els.Add(El(page, title + "名", "Hmi.Text", x, y + 6, 150, 26, 11, TextProps(title, 13, "#FFCBD5E1")));
        els.Add(El(page, title + "标准", "Hmi.IOField", x + 160, y, 140, 32, 11, new JObject
        {
            ["ValueFormat"] = "F4", ["Mode"] = "Input", ["Text"] = "标准", ["TextAlignment"] = "Right"
        }, new JArray { Bind("Value", nominal) }));
        els.Add(El(page, title + "上差", "Hmi.IOField", x + 316, y, 140, 32, 11, new JObject
        {
            ["ValueFormat"] = "F4", ["Mode"] = "Input", ["Text"] = "上差", ["TextAlignment"] = "Right"
        }, new JArray { Bind("Value", upper) }));
        els.Add(El(page, title + "下差", "Hmi.IOField", x + 472, y, 140, 32, 11, new JObject
        {
            ["ValueFormat"] = "F4", ["Mode"] = "Input", ["Text"] = "下差", ["TextAlignment"] = "Right"
        }, new JArray { Bind("Value", lower) }));
    }

    // ------------------------------------------------------------------ 组态小件

    static void AddCHeader(JArray els, string page, string subtitle)
    {
        els.Add(El(page, "标题栏", "Hmi.Rectangle", 0, 0, 1920, 160, 5,
            new JObject { ["Fill"] = "#FF111827" }));
        els.Add(El(page, "页面标题", "Hmi.Text", 40, 24, 1100, 48, 6,
            TextProps("精密尺寸测量站 · 标定 / 四路卡尺 / SPC", 28, "#FFF8FAFC", "Bold")));
        els.Add(El(page, "页面副标题", "Hmi.Text", 40, 76, 1300, 28, 6,
            TextProps(subtitle, 14, "#FF94A3B8")));
        els.Add(El(page, "时钟", "Hmi.Clock", 1500, 24, 380, 44, 6,
            TextProps("", 22, "#FFCBD5E1", "SemiBold")));
    }

    /// <summary>一行表格列定义。Width=0 吃掉剩余宽度；最多 12 列；Variable 写全局变量名。</summary>
    static JObject CCol(string header, string variable, int width, string align, bool mono)
        => new()
        {
            ["Header"] = header,
            ["Variable"] = variable,
            ["Width"] = width,
            ["Align"] = align,
            ["Format"] = "",
            ["Monospace"] = mono,
        };
}
