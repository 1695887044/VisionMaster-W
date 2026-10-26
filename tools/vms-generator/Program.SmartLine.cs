using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// 「智能装配检测线」：6 工位 + 追溯/报表 + 15 页组态。
/// 生成目标：一个可直接运行的复杂方案，覆盖全部内置图元与全部可运行插件。
/// </summary>
internal static partial class Program
{
    // ---------- 方案与资源 ----------
    const string SL_NAME = "智能装配检测线";
    const string SL_QR1 = @"Image\qr_workpiece_01.png";
    const string SL_QR2 = @"Image\qr_workpiece_02.png";
    const string SL_LOT1 = @"Image\lot_number_01.png";
    const string SL_LOT2 = @"Image\lot_number_02.png";
    const string SL_BOARD1 = @"Image\board_01.png";
    const string SL_BOARD2 = @"Image\board_02.png";
    const string SL_CALIPER1 = @"Image\1-宽度40px.png";
    const string SL_CALIPER2 = @"Image\2-两点距240px.png";
    const string SL_CALIPER3 = @"Image\3-点到线距200px.png";
    const string SL_CALIPER4 = @"Image\4-圆直径200px.png";
    const string SL_CALIPER5 = @"Image\5-角度90.png";
    const string SL_BEAD = @"Image\adhesive_bead_01.png";
    const string SL_BEAD_REF = @"Image\adhesive_bead_ref.png";
    const string SL_COLOR1 = @"Image\color_fuses_01.png";
    const string SL_COLOR2 = @"Image\color_pieces_01.png";
    const string SL_COLOR_SYN = @"Image\synthetic_color_8bit.png";
    const string SL_PCB_GOOD = @"Image\pcb_board_good_01.png";
    const string SL_PCB_BAD = @"Image\pcb_board_anomaly_01.png";
    const string SL_LOGO = @"Image\mvtec_logo.png";
    const string SL_GIF = @"Image\line_flow.gif";

    // ---------- 插件类型（AQN） ----------
    const string P_ASSIGN = "Plugin.ImageAssign.ImageAssignPlugin, Plugin.ImageAssign" + S;
    const string P_ISCRIPT = "Plugin.ImageScript.ImageScriptPlugin, Plugin.ImageScript" + S;
    const string P_YOLO = "Plugin.Yolo.YoloPlugin, Plugin.Yolo" + S;
    const string P_DLANOMALY = "Plugin.DlAnomaly.DlAnomalyPlugin, Plugin.DlAnomaly" + S;
    const string P_DLCLASSIFY = "Plugin.DlClassify.DlClassifyPlugin, Plugin.DlClassify" + S;
    const string P_DLSEGMENT = "Plugin.DlSegment.DlSegmentPlugin, Plugin.DlSegment" + S;
    const string P_DBQUERY = "Plugin.Database.DatabaseQueryPlugin, Plugin.Database" + S;
    const string P_DBWRITE = "Plugin.Database.DatabaseWriteRowPlugin, Plugin.Database" + S;
    const string P_DBEXEC = "Plugin.Database.DatabaseExecutePlugin, Plugin.Database" + S;
    const string P_HANDEYE = "Plugin.HandEye.HandEyePlugin, Plugin.HandEye" + S;

    // ---------- 变量类型 ----------
    const string SL_T_INT = T_INT;
    const string SL_T_SHORT = T_SHORT;
    const string SL_T_DBL = T_DBL;
    const string SL_T_BOOL = T_BOOL;
    const string SL_T_STR = T_STR;
    const string SL_T_SGL = T_SGL;
    const string SL_T_IMG = "HalconDotNet.HImage, halcondotnet, Version=23.5.0.0, Culture=neutral, PublicKeyToken=4973bed59ddbf2b8";

    static JObject BuildSmartLineSolution()
    {
        var vars = BuildLineVariables();
        return new JObject
        {
            ["SolutionName"] = SL_NAME,
            ["Version"] = 1.0,
            ["Config"] = new JObject { ["ImageViewMode"] = 0 },
            ["Flows"] = new JArray
            {
                BuildLineHomeFlow(),
                BuildLineCalibFlow(),
                BuildLineMainFlow(),
                BuildLineSt1Flow(),
                BuildLineSt2Flow(),
                BuildLineSt3Flow(),
                BuildLineSt4Flow(),
                BuildLineSt5Flow(),
                BuildLineSt6Flow(),
                BuildLineAiFlow(),
                BuildLineRecipeFlow(),
                BuildLineDiagFlow(),
                BuildLineAlarmResetFlow(),
                BuildLineEndFlow(),
            },
            ["SolutionId"] = G("solution:smart-line").ToString(),
            ["CommunicationConfigs"] = BuildLineComms(),
            ["IoBuses"] = new JArray(),
            ["IoDevices"] = new JArray(),
            ["CameraConfigs"] = BuildLineCameras(),
            ["MotionCards"] = BuildLineMotionCards(),
            ["CamTables"] = new JArray(),
            ["Calibrations"] = new JArray { LineCalibrationAsset() },
            ["VariableSnapshots"] = vars,
            ["VariableGroups"] = new JArray(),
            ["WatchItems"] = new JArray(),
            ["Scada"] = BuildLineScada(),
        };
    }

    // ==================================================================
    //  变量
    // ==================================================================
    static JArray BuildLineVariables()
    {
        var list = new JArray
        {
            Var("ProductSN", SL_T_STR, "当前产品 SN（追溯主键）", "SN-000000"),
            Var("ProductType", SL_T_STR, "产品类型 A/B", "A"),
            Var("BatchNo", SL_T_STR, "生产批次", "B20261025"),
            Var("RecipeName", SL_T_STR, "当前配方", "Recipe_A"),
            Var("OperatorId", SL_T_STR, "操作员", "OP-001"),
            Var("LineRunning", SL_T_BOOL, "产线运行中", false),
            Var("SystemReady", SL_T_BOOL, "系统就绪", false),
            Var("LineFault", SL_T_BOOL, "产线故障", false),
            Var("FaultCode", SL_T_STR, "当前故障码", ""),
            Var("AutoMode", SL_T_INT, "运行模式 0手动/1自动/2检修", 1),
            Var("Shift", SL_T_INT, "班次 1/2/3", 1),
            Var("S1_Ok", SL_T_BOOL, "工位1 上料读码通过", false),
            Var("S2_Ok", SL_T_BOOL, "工位2 视觉定位通过", false),
            Var("S3_Ok", SL_T_BOOL, "工位3 精密测量通过", false),
            Var("S4_Ok", SL_T_BOOL, "工位4 表面检测通过", false),
            Var("S5_Ok", SL_T_BOOL, "工位5 装配压装通过", false),
            Var("S6_Ok", SL_T_BOOL, "工位6 识别追溯通过", false),
            Var("StationOk", SL_T_BOOL, "本件综合判定", false),
            Var("CurrentResult", SL_T_BOOL, "本件结果（布尔）", false),
            Var("ResultText", SL_T_STR, "本件结果（OK/NG）", "OK"),
            Var("OKCount", SL_T_INT, "累计良品", 0),
            Var("NGCount", SL_T_INT, "累计不良", 0),
            Var("TotalCount", SL_T_INT, "累计检测", 0),
            Var("YieldRate", SL_T_DBL, "良率 %", 100.0),
            Var("CycleCount", SL_T_INT, "节拍计数", 0),
            Var("CycleTimeMs", SL_T_INT, "单件节拍 ms", 850),
            Var("OEE", SL_T_DBL, "OEE %", 0.0),
            Var("SpeedSetpoint", SL_T_DBL, "线速度设定 mm/s", 50.0),
            Var("Torque", SL_T_SGL, "拧紧扭矩 N·m", 0.0f),
            Var("Pressure", SL_T_SGL, "压装压力 MPa", 0.0f),
            Var("Temperature", SL_T_SGL, "环境温度 ℃", 0.0f),
            Var("BeadErrorCount", SL_T_INT, "胶路错误数", 0),
            Var("BeadTotalLength", SL_T_DBL, "胶路错误总长 mm", 0.0),
            Var("WidthMm", SL_T_DBL, "宽度测量 mm", 0.0),
            Var("DistanceMm", SL_T_DBL, "两点距 mm", 0.0),
            Var("PointLineMm", SL_T_DBL, "点线距 mm", 0.0),
            Var("DiameterMm", SL_T_DBL, "圆直径 mm", 0.0),
            Var("AngleDeg", SL_T_DBL, "角度 °", 0.0),
            Var("CalMmPerPixel", SL_T_DBL, "像素当量 mm/px", 0.0),
            Var("CircleRelationMm", SL_T_DBL, "圆心距/关系值 mm", 0.0),
            Var("MatchScore", SL_T_DBL, "匹配得分", 0.0),
            Var("MatchRow", SL_T_DBL, "定位行", 0.0),
            Var("MatchCol", SL_T_DBL, "定位列", 0.0),
            Var("MatchAngle", SL_T_DBL, "定位角度", 0.0),
            Var("AlignOffsetX", SL_T_DBL, "机械 X 偏移 mm", 0.0),
            Var("AlignOffsetY", SL_T_DBL, "机械 Y 偏移 mm", 0.0),
            Var("DefectCount", SL_T_INT, "表面缺陷数", 0),
            Var("DefectArea", SL_T_DBL, "缺陷总面积 mm²", 0.0),
            Var("DiffCount", SL_T_INT, "差分缺陷数", 0),
            Var("MaxDiff", SL_T_DBL, "最大差分值", 0.0),
            Var("ColorShare", SL_T_DBL, "目标颜色占比", 0.0),
            Var("ColorVerdict", SL_T_STR, "颜色判定", ""),
            Var("OcrText", SL_T_STR, "OCR 文本", ""),
            Var("OcrConfidence", SL_T_DBL, "OCR 置信度", 0.0),
            Var("BarcodeText", SL_T_STR, "条码内容", ""),
            Var("CodeCount", SL_T_INT, "读码数量", 0),
            Var("SortBin", SL_T_STR, "分拣仓位", "A"),
            Var("ReportPath", SL_T_STR, "最近报表路径", ""),
            Var("LastUploadStatus", SL_T_STR, "最近上报状态", ""),
            Var("CommOk", SL_T_BOOL, "通讯正常", false),
            Var("VisionOk", SL_T_BOOL, "视觉正常", false),
            Var("MotionOk", SL_T_BOOL, "运动正常", false),
            Var("DataOk", SL_T_BOOL, "数据链路正常", false),
            Var("AlarmCount", SL_T_INT, "报警次数", 0),
            Var("WarnCount", SL_T_INT, "警告次数", 0),
            Var("DefectTypeA", SL_T_INT, "缺陷类型 A", 0),
            Var("DefectTypeB", SL_T_INT, "缺陷类型 B", 0),
            Var("DefectTypeC", SL_T_INT, "缺陷类型 C", 0),
            Var("PartCounter", SL_T_INT, "过站件数", 0),
            Var("GoodRateTarget", SL_T_DBL, "良率目标 %", 98.0),
            Var("AxisXPos", SL_T_DBL, "X 当前位置 mm", 0.0),
            Var("AxisYPos", SL_T_DBL, "Y 当前位置 mm", 0.0),
            Var("AxisZPos", SL_T_DBL, "Z 当前位置 mm", 0.0),
            Var("AxisRPos", SL_T_DBL, "R 当前位置 °", 0.0),
            Var("St1Count", SL_T_INT, "工位1 过站", 0),
            Var("St2Count", SL_T_INT, "工位2 过站", 0),
            Var("St3Count", SL_T_INT, "工位3 过站", 0),
            Var("St4Count", SL_T_INT, "工位4 过站", 0),
            Var("St5Count", SL_T_INT, "工位5 过站", 0),
            Var("St6Count", SL_T_INT, "工位6 过站", 0),
            Var("LastImage", SL_T_IMG, "最近一张检测图（SCADA 图像显示）", null),
        };

        list.Add(NetVar("PLC_Trigger", SL_T_SHORT, "PLC 触发计数 HR0", 0, "PLC_Line", "ModbusTcp", "HoldingRegisters", "0", -1, 1, 0, "Int16"));
        list.Add(NetVar("PLC_Ack", SL_T_SHORT, "PLC 完成确认 HR1", 0, "PLC_Line", "ModbusTcp", "HoldingRegisters", "1", -1, 1, 0, "Int16"));
        list.Add(NetVar("PLC_Start", SL_T_BOOL, "PLC 启动线圈 0", false, "PLC_Line", "ModbusTcp", "Coils", "0", 0, 1, 0, "Boolean"));
        list.Add(NetVar("PLC_Stop", SL_T_BOOL, "PLC 停止线圈 1", false, "PLC_Line", "ModbusTcp", "Coils", "1", 0, 1, 0, "Boolean"));
        list.Add(NetVar("PLC_Result", SL_T_BOOL, "PLC 判定线圈 2", false, "PLC_Line", "ModbusTcp", "Coils", "2", 0, 1, 0, "Boolean"));
        list.Add(NetVar("PLC_OKCount", SL_T_INT, "PLC 良品计数 HR4", 0, "PLC_Line", "ModbusTcp", "HoldingRegisters", "4", -1, 1, 0, "Int32"));
        list.Add(NetVar("PLC_CycleTime", SL_T_INT, "PLC 节拍 HR6", 0, "PLC_Line", "ModbusTcp", "HoldingRegisters", "6", -1, 1, 0, "Int32"));
        list.Add(NetVar("PLC_NGCount", SL_T_INT, "PLC 不良计数 HR8", 0, "PLC_Line", "ModbusTcp", "HoldingRegisters", "8", -1, 1, 0, "Int32"));
        list.Add(NetVar("Meter_Value", SL_T_SGL, "仪表读数 IR0", 0.0, "Meter_Station", "ModbusTcp", "InputRegisters", "0", -1, 1, 0, "Float"));
        list.Add(NetVar("Pressure_Value", SL_T_SGL, "压力变送器 IR10", 0.0, "Meter_Station", "ModbusTcp", "InputRegisters", "10", -1, 1, 0, "Float"));
        list.Add(NetVar("Torque_Value", SL_T_SGL, "拧紧扭矩变送器 IR20", 0.0, "Meter_Station", "ModbusTcp", "InputRegisters", "20", -1, 1, 0, "Float"));
        list.Add(NetVar("S7_Heartbeat", SL_T_BOOL, "S7 心跳 M0.0", false, "PLC_S7_Sim", "SiemensS7", "M", "0", 0, 1, 1, "Boolean"));
        list.Add(NetVar("S7_Ready", SL_T_BOOL, "S7 就绪 M1.0", false, "PLC_S7_Sim", "SiemensS7", "M", "1", 0, 1, 1, "Boolean"));
        list.Add(NetVar("S7_Result", SL_T_SHORT, "S7 结果字 DB1.0", 0, "PLC_S7_Sim", "SiemensS7", "DB", "0", -1, 1, 1, "Int16"));
        return list;
    }

    // ==================================================================
    //  硬件
    // ==================================================================
    static JArray BuildLineComms()
        => new()
        {
            Comm("PLC_Line", 0, "ModbusTcp", new JObject
            {
                ["$type"] = "VisionMaster.Communications.ModbusTcpConfig, VM.Core",
                ["ByteoRDER"] = 0,
                ["IpAddress"] = "127.0.0.1",
                ["Port"] = 503,
                ["EnableKeepAlive"] = true,
                ["KeepAliveIntervalMs"] = 30000,
                ["Type"] = 0,
                ["TimeoutMs"] = 1000,
                ["RetryCount"] = 1,
                ["RetryIntervalMs"] = 200,
                ["ReadTimeoutMs"] = 1000,
            }, 100, "产线虚拟 PLC（Modbus TCP 503）"),
            Comm("Meter_Station", 0, "ModbusTcp", new JObject
            {
                ["$type"] = "VisionMaster.Communications.ModbusTcpConfig, VM.Core",
                ["ByteoRDER"] = 3,
                ["IpAddress"] = "127.0.0.1",
                ["Port"] = 504,
                ["EnableKeepAlive"] = true,
                ["KeepAliveIntervalMs"] = 30000,
                ["Type"] = 0,
                ["TimeoutMs"] = 1000,
                ["RetryCount"] = 1,
                ["RetryIntervalMs"] = 200,
                ["ReadTimeoutMs"] = 1000,
            }, 200, "压力/仪表虚拟站（Modbus TCP 504 / CDAB）"),
            Comm("PLC_S7_Sim", 2, "SiemensS7", new JObject
            {
                ["$type"] = "VisionMaster.Communications.SiemensS7Config, VM.Core",
                ["S7CpuType"] = "S1200",
                ["Rack"] = 0,
                ["Slot"] = 1,
                ["IpAddress"] = "127.0.0.1",
                ["Port"] = 1102,
                ["EnableKeepAlive"] = true,
                ["KeepAliveIntervalMs"] = 30000,
                ["Type"] = 2,
                ["TimeoutMs"] = 1000,
                ["RetryCount"] = 1,
                ["RetryIntervalMs"] = 200,
                ["ReadTimeoutMs"] = 1000,
            }, 200, "S7-1200 虚拟 PLC（端口 1102，避开特权端口）"),
        };

    static JArray BuildLineCameras()
        => new()
        {
            new JObject
            {
                ["Id"] = G("camera:sim").ToString(),
                ["SerialNo"] = "SIM-LINE-01",
                ["DisplayName"] = "产线仿真相机",
                ["DriverTypeKey"] = "Plugin.Camera.Sim.SimCameraDevice, Plugin.Camera.Sim" + S,
                ["Remarks"] = "工位 1/4 用产线仿真图库",
                ["AutoConnect"] = false,
            },
            new JObject
            {
                ["Id"] = G("camera:network").ToString(),
                ["SerialNo"] = "NET-STATION-02",
                ["DisplayName"] = "网络相机（虚拟）",
                ["DriverTypeKey"] = "Plugin.Camera.Network.NetworkCameraDevice, Plugin.Camera.Network" + S,
                ["Remarks"] = "工位 2/3 的高精度工位相机（HTTP 推图）",
                ["AutoConnect"] = false,
            },
            new JObject
            {
                ["Id"] = G("camera:hik").ToString(),
                ["SerialNo"] = "HK-STATION-03",
                ["DisplayName"] = "海康相机（现场接入位）",
                ["DriverTypeKey"] = "Plugin.Camera.Hikvision.HikvisionCameraDevice, Plugin.Camera.Hikvision" + S,
                ["Remarks"] = "现场替换为真实海康相机；默认不自动连接",
                ["AutoConnect"] = false,
            },
        };

    static JArray BuildLineMotionCards()
    {
        var axes = new JArray
        {
            Axis("X", 0, "X 轴：上料/检测位"),
            Axis("Y", 1, "Y 轴：横移"),
            Axis("Z", 2, "Z 轴：压装"),
        };

        return new JArray
        {
            new JObject
            {
                ["Id"] = G("motion:smart-line").ToString(),
                ["DriverTypeKey"] = "Plugin.Motion.Virtual.VirtualMotionCard, Plugin.Motion.Virtual" + S,
                ["DisplayName"] = "VirtualMotion",
                ["Address"] = "inproc",
                ["CardModel"] = "VPLC532R",
                ["AutoConnect"] = true,
                ["Remarks"] = "进程内虚拟运动卡：4 轴直线/回零/IO，全部可跑",
                ["Axes"] = axes,
                ["Points"] = new JArray
                {
                    Point("X", "取料位", 0.0),
                    Point("X", "读码位", 12.0),
                    Point("X", "检测位", 40.0),
                    Point("X", "下料位", 180.0),
                    Point("Y", "中心线", 0.0),
                    Point("Y", "A 仓位", 10.0),
                    Point("Y", "B 仓位", -10.0),
                    Point("Z", "安全高度", 0.0),
                    Point("Z", "压装位", 5.0),
                },
                ["Params"] = new JObject
                {
                    ["DefaultVelocityMmPerS"] = 20.0,
                    ["DefaultAccelMmPerS2"] = 200.0,
                    ["PollIntervalMs"] = 10,
                    ["CommandTimeoutMs"] = 30000,
                    ["WatchdogTimeoutMs"] = 3000,
                    ["AutoReconnect"] = true,
                    ["MaxQueueDepth"] = 256,
                },
            },
        };
    }

    static JObject Point(string axis, string name, double position)
        => new()
        {
            ["Id"] = G("point:" + axis + ":" + name).ToString(),
            ["AxisName"] = axis,
            ["Name"] = name,
            ["Position"] = position,
            ["Velocity"] = 20.0,
            ["Remark"] = "",
        };

    // ==================================================================
    //  流程
    // ==================================================================
    static JObject BuildLineHomeFlow()
    {
        var steps = new JArray
        {
            MotionEnable("home:enx", "X 轴使能", "X", true, 10000),
            MotionEnable("home:eny", "Y 轴使能", "Y", true, 10000),
            MotionEnable("home:enz", "Z 轴使能", "Z", true, 10000),
            MotionHome("home:hx", "X 回零", "X", "PresetZero", 30000),
            MotionHome("home:hy", "Y 回零", "Y", "PresetZero", 30000),
            MotionHome("home:hz", "Z 回零", "Z", "PresetZero", 30000),
            MotionMove("home:mx", "X 到取料位", "X", "Absolute", 0, 30),
            MotionMove("home:my", "Y 到中心线", "Y", "Absolute", 0, 30),
            MotionMove("home:mz", "Z 到安全高度", "Z", "Absolute", 0, 30),
            MotionWaitAt("home:wx", "等待 X 到位", "X", 0, 30000, 10),
            MotionWaitAt("home:wy", "等待 Y 到位", "Y", 0, 30000, 10),
            MotionWaitAt("home:wz", "等待 Z 到位", "Z", 0, 30000, 10),
            MotionIo("home:io", "就绪灯输出", "VirtualMotion", 0, true, "WriteOutput"),
            CommWrite("home:plcres", "清 PLC 判定", "PLC_Result", new JValue(false), true),
            CommWrite("home:plcack", "清 PLC 确认", "PLC_Ack", new JValue(0), true),
            VarSet("home:ready", "置系统就绪", "SystemReady", null, "Global", new JValue(true)),
            VarSet("home:run", "清运行中", "LineRunning", null, "Global", new JValue(false)),
            VarSet("home:fault", "清产线故障", "LineFault", null, "Global", new JValue(false)),
            VarSet("home:fc", "清故障码", "FaultCode", null, "Global", new JValue("")),
            Delay("home:delay", "回零完成稳定", 100),
        };

        return Flow("Home", "回零：四轴使能/回零/待机位 + 通讯复位", 1, 0, 0, steps, enabled: false);
    }

    /// <summary>
    /// 预置一份方案级标定资产：首次打开方案时下游「标定引用」就能工作，
    /// 运行 Calib 流程后再由插件按同一 Id 覆盖更新。
    /// </summary>
    static JObject LineCalibrationAsset()
    {
        var transform = new JObject
        {
            ["Kind"] = 0,
            ["MmPerPixel"] = 1.0,
            ["Matrix"] = new JArray(1.0, 0.0, 0.0, 1.0, 0.0, 0.0),
            ["ProjectiveMatrix"] = JValue.CreateNull(),
            ["MeshNodes"] = JValue.CreateNull(),
            ["MeshSize"] = 0,
            ["Intrinsics"] = JValue.CreateNull(),
            ["SourceImageWidth"] = 640,
            ["SourceImageHeight"] = 480,
            ["SourceTag"] = "LineCalib",
            ["CameraSerial"] = "NET-STATION-02",
            ["CreatedAtUtc"] = "2026-10-25T00:00:00Z",
            ["ResidualRmsPx"] = 0.0,
            ["MaxResidualPx"] = 0.0,
            ["ResidualByRadiusBands"] = new JArray(0.0, 0.0, 0.0),
            ["ResidualBandCounts"] = new JArray(0, 0, 0),
            ["HandEyeMatrix"] = JValue.CreateNull(),
            ["HandEyePose"] = 0,
            ["HandEyeRotationRmsRad"] = 0.0,
            ["HandEyeTranslationRmsMm"] = 0.0,
            ["HandEyeSampleCount"] = 0,
        };
        return new JObject
        {
            ["CalibrationId"] = G("calib:LineCalib").ToString(),
            ["Name"] = "LineCalib",
            ["Description"] = "产线像素当量标定（预置基线；运行 Calib 流程后由插件更新）",
            ["IsLocked"] = false,
            ["Version"] = 1,
            ["Transform"] = transform,
        };
    }

    static JObject BuildLineCalibFlow()
    {
        var s = new JArray
        {
            ImgFile("cal:acq", "采集标定图", SL_CALIPER1, 0),
            Action("cal:run", "标定计算并写入方案", "标定", P_CALIB, "标定", "\uf05b", new JObject
            {
                ["UseSavedCalibration"] = false,
                ["SaveToCatalog"] = true,
                ["CatalogAssetName"] = "LineCalib",
                ["CatalogAssetId"] = G("calib:LineCalib").ToString(),
                ["Mode"] = "PixelScale",
                ["KnownLengthMm"] = 100.0,
                ["ScaleARow"] = 240.0,
                ["ScaleACol"] = 100.0,
                ["ScaleBRow"] = 240.0,
                ["ScaleBCol"] = 200.0,
                ["SourceImageWidth"] = 640,
                ["SourceImageHeight"] = 480,
                ["MmPerPixelMin"] = 0.001,
                ["MmPerPixelMax"] = 2.0,
                ["IsCalibrationLocked"] = false,
                ["DisplayViewIndex"] = 0,
            }, new JObject
            {
                ["SourceImage"] = L("cal:acq", "Image", "采集标定图"),
            }),
            VarSet("cal:mpp", "写标定当量", "CalMmPerPixel", L("cal:run", "MmPerPixel", "标定计算并写入方案"), "Global", null),
            Delay("cal:delay", "标定节拍", 100),
        };
        return Flow("Calib", "标定：采集标定图 → 计算像素当量 → 写入方案标定资产 LineCalib", 0, 96, 0, s);
    }

    static JObject BuildLineMainFlow()
    {
        var then = new JArray
        {
            RunFlow("main:st1", "调用 上料读码", "ST1_LoadCode", 60000),
            RunFlow("main:st2", "调用 视觉定位", "ST2_Position", 60000),
            RunFlow("main:st3", "调用 精密测量", "ST3_Measure", 90000),
            RunFlow("main:st4", "调用 表面检测", "ST4_Surface", 90000),
            RunFlow("main:st5", "调用 装配压装", "ST5_Assembly", 90000),
            RunFlow("main:st6", "调用 识别追溯", "ST6_Trace", 60000),
        };

        then.Add(Logic("main:and1", "工位1&2", "main:st1", "调用 上料读码", "main:st2", "调用 视觉定位", "Success", "Success"));
        // RunFlow 的 Success 只表示"调起成功"；工位结果统一取全局变量，避免跨流程端口取数。
        then.Add(LogicVars("main:s12", "工位1&2结果", "S1_Ok", "S2_Ok"));
        then.Add(LogicVars("main:s34", "工位3&4结果", "S3_Ok", "S4_Ok"));
        then.Add(LogicVars("main:s56", "工位5&6结果", "S5_Ok", "S6_Ok"));
        then.Add(Logic("main:s1234", "工位1-4", "main:s12", "工位1&2结果", "main:s34", "工位3&4结果"));
        then.Add(Logic("main:all", "六工位综合", "main:s1234", "工位1-4", "main:s56", "工位5&6结果"));
        then.Add(VarSet("main:okv", "写本件判定", "StationOk", L("main:all", "Result", "六工位综合"), "Global", null));
        then.Add(VarSet("main:curv", "写布尔结果", "CurrentResult", L("main:all", "Result", "六工位综合"), "Global", null));

        var okThen = new JArray
        {
            MathAdd("main:okadd", "良品+1", "OKCount"),
            ConvertToInt("main:okint", "良品转 Int", "main:okadd", "良品+1"),
            VarSet("main:okwrite", "写良品数", "OKCount", L("main:okint", "Result", "良品转 Int"), "Global", null),
            VarSet("main:textok", "结果文本 OK", "ResultText", null, "Global", new JValue("OK")),
        };
        var okElse = new JArray
        {
            MathAdd("main:ngadd", "不良+1", "NGCount"),
            ConvertToInt("main:ngint", "不良转 Int", "main:ngadd", "不良+1"),
            VarSet("main:ngwrite", "写不良数", "NGCount", L("main:ngint", "Result", "不良转 Int"), "Global", null),
            VarSet("main:textng", "结果文本 NG", "ResultText", null, "Global", new JValue("NG")),
        };
        then.Add(IfSmart("main:okif", "良品/不良分流", "Ok", "Ok", "System.Boolean",
            L("main:all", "Result", "六工位综合"), okThen, okElse));

        // 总检 = 良品 + 不良
        then.Add(MathBinary("main:totadd", "总检数相加", "Add", LG("OKCount"), LG("NGCount")));
        then.Add(ConvertToInt("main:totint", "总检转 Int", "main:totadd", "总检数相加"));
        then.Add(VarSet("main:totwrite", "写总检数", "TotalCount", L("main:totint", "Result", "总检转 Int"), "Global", null));

        // 良率 = 良品 * 100 / 总检（总检为 0 时保持 100）
        then.Add(Compare("main:totcmp", "总检>0", "main:totint", "总检转 Int", "Result", 0, "GreaterThan"));
        var yieldThen = new JArray
        {
            MathWithConstant("main:ymul", "良品×100", "Multiply", LG("OKCount"), 100.0),
            ConvertStep("main:yint", "良品×100 转 Double", "main:ymul", "良品×100", "Double"),
        };
        yieldThen.Add(MathBinary("main:ydiv", "除以总检", "Divide", L("main:yint", "Result", "良品×100 转 Double"), LG("TotalCount")));
        yieldThen.Add(VarSet("main:ywrite", "写良率", "YieldRate", L("main:ydiv", "Result", "除以总检"), "Global", null));
        var yieldElse = new JArray { VarSet("main:y100", "良率置 100", "YieldRate", null, "Global", new JValue(100.0)) };
        then.Add(IfSmart("main:yif", "良率保护", "Ok", "Ok", "System.Boolean", L("main:totcmp", "Result", "总检>0"), yieldThen, yieldElse));

        // 产品类型 → 分拣仓位
        then.Add(CaseSmart("main:bin", "按产品类型分仓", "ProductType", new JArray
        {
            Branch(4, "A", "A", new JArray { VarSet("main:binA", "A 仓", "SortBin", null, "Global", new JValue("A")) }),
            Branch(4, "B", "B", new JArray { VarSet("main:binB", "B 仓", "SortBin", null, "Global", new JValue("B")) }),
            Branch(2, "默认", "", new JArray { VarSet("main:binC", "C 仓", "SortBin", null, "Global", new JValue("C")) }),
        }, "System.String"));

        then.Add(MathAdd("main:cycadd", "节拍+1", "CycleCount"));
        then.Add(ConvertToInt("main:cycint", "节拍转 Int", "main:cycadd", "节拍+1"));
        then.Add(VarSet("main:cycwrite", "写节拍", "CycleCount", L("main:cycint", "Result", "节拍转 Int"), "Global", null));
        then.Add(MathWithConstant("main:oee", "OEE 估算", "Multiply", LG("YieldRate"), 0.95));
        then.Add(VarSet("main:oeew", "写 OEE", "OEE", L("main:oee", "Result", "OEE 估算"), "Global", null));
        then.Add(MathAdd("main:partadd", "过站+1", "PartCounter"));
        then.Add(ConvertToInt("main:partint", "过站转 Int", "main:partadd", "过站+1"));
        then.Add(VarSet("main:partwrite", "写过站", "PartCounter", L("main:partint", "Result", "过站转 Int"), "Global", null));

        then.Add(CommWrite("main:wresult", "写 PLC 判定", "PLC_Result", LG("StationOk"), true));
        then.Add(CommWrite("main:wok", "写 PLC 良品数", "PLC_OKCount", LG("OKCount"), true));
        then.Add(CommWrite("main:wng", "写 PLC 不良数", "PLC_NGCount", LG("NGCount"), true));
        then.Add(CommWrite("main:wcyc", "写 PLC 节拍", "PLC_CycleTime", LG("CycleTimeMs"), true));
        then.Add(CommWrite("main:wack", "写 PLC 确认", "PLC_Ack", new JValue(1), true));
        then.Add(CommRead("main:rack", "读 PLC 确认", "PLC_Ack"));
        then.Add(WaitSmart("main:waitack", "等待 PLC 确认", "Ack", "Ack >= 1", L("main:rack", "Value", "读 PLC 确认"), 1000, 1,
            "PLC 未在 1 秒内确认，继续执行"));

        then.Add(Parallel("main:parallel", "并行落盘与上报", 1, 0, 15000, new JArray
        {
            Branch(0, "CSV 账本", "", new JArray { LineCsv("main:csv", "产线主记录") }),
            Branch(0, "MES 上报", "", new JArray { LineUpload("main:upload", "MES 上报") }),
        }));
        then.Add(LineExcel("main:excel", "生产报表导出"));
        then.Add(VarSet("main:reportpath", "写报表路径", "ReportPath", L("main:excel", "报表路径", "生产报表导出"), "Global", null));
        then.Add(VarSet("main:runflag", "置运行中", "LineRunning", null, "Global", new JValue(true)));
        then.Add(Delay("main:delay", "循环节拍", 120));

        var elseSteps = new JArray
        {
            Delay("main:idle", "空闲节拍", 150),
            CommWrite("main:idleack", "清 PLC 确认", "PLC_Ack", new JValue(0), false),
            VarSet("main:notrun", "清运行中", "LineRunning", null, "Global", new JValue(false)),
        };

        var steps = new JArray
        {
            CommRead("main:rtrigger", "读 PLC 触发", "PLC_Trigger"),
            CommRead("main:rstart", "读 PLC 启动", "PLC_Start"),
            CommRead("main:rmeter", "读仪表值", "Meter_Value"),
            CommRead("main:rpress", "读压力值", "Pressure_Value"),
            CommRead("main:rs7hb", "读 S7 心跳", "S7_Heartbeat"),
            CommRead("main:rs7res", "读 S7 结果", "S7_Result"),
            VarSet("main:temp", "写温度", "Temperature", LG("Meter_Value"), "Global", null),
            VarSet("main:press", "写压力", "Pressure", LG("Pressure_Value"), "Global", null),
            Compare("main:cmptrig", "触发>=1", "main:rtrigger", "读 PLC 触发", "Value", 1, "GreaterThanOrEqual"),
            Compare("main:cmpstart", "启动=1", "main:rstart", "读 PLC 启动", "Value", 1, "Equal"),
            Logic("main:runor", "允许运行", "main:cmptrig", "触发>=1", "main:cmpstart", "启动=1", "Result", "Result"),
            IfSmart("main:runif", "运行判断", "Ok", "Ok", "System.Boolean", L("main:runor", "Result", "允许运行"), then, elseSteps),
            CommWrite("main:s7hb", "写 S7 心跳", "S7_Heartbeat", new JValue(true), false),
            CommWrite("main:s7res", "写 S7 结果", "S7_Result", new JValue(1), false),
        };

        return Flow("Main", "主任务：六工位联动 + 计数 + 落盘 + 上报 + 报表", 2, 96, 0, steps);
    }

    // ---------------- 工位 1：上料读码 ----------------
    static JObject BuildLineSt1Flow()
    {
        var s = new JArray
        {
            MotionEnable("st1:en", "X 使能", "X", true, 10000),
            MotionMove("st1:move", "X 到读码位", "X", "Absolute", 12, 30),
            MotionWaitAt("st1:wait", "等待读码位", "X", 12, 10000, 10),
            MotionReadStatus("st1:status", "读 X 轴状态", "X"),
            VarSet("st1:xpos", "写 X 位置", "AxisXPos", L("st1:status", "Position", "读 X 轴状态"), "Global", null),
            ImgFile("st1:qr", "采集二维码图", SL_QR1, 0),
            PreProcess("st1:pre", "读码前处理", new JArray(
                Op("gauss", ("Size", "3")),
                Op("emphasize", ("Width", "5"), ("Height", "5"), ("Factor", "1.2"))), 1,
                "st1:qr", "采集二维码图"),
            Action("st1:code", "二维码读取", "图像处理", P_CODE, "二维码读取", "\uf029", new JObject
            {
                ["Symbology"] = "QR Code",
                ["TimeoutMs"] = 500,
                ["Polarity"] = "any",
            }, new JObject { ["Image"] = L("st1:pre", "Image", "读码前处理") }),
            Compare("st1:cmpcode", "码数量>0", "st1:code", "二维码读取", "Count", 0, "GreaterThan"),
            ImgFile("st1:ocrAcq", "采集批号图", SL_LOT1, 0),
            PreProcess("st1:ocrPre", "批号前处理", new JArray(
                Op("gauss", ("Size", "3")),
                Op("emphasize", ("Width", "5"), ("Height", "5"), ("Factor", "1.0"))), 1,
                "st1:ocrAcq", "采集批号图"),
            Ocr("st1:ocr", "OCR 识别批号", new double[] { 272, 320, 0, 170, 40 }, 1, "st1:ocrPre", "批号前处理", true),
            Compare("st1:cmpocr", "OCR 字符>0", "st1:ocr", "OCR 识别批号", "Count", 0, "GreaterThan"),
            Logic("st1:ok", "读码工位判定", "st1:cmpcode", "码数量>0", "st1:cmpocr", "OCR 字符>0"),
            VarSet("st1:okv", "写工位1结果", "S1_Ok", L("st1:ok", "Result", "读码工位判定"), "Global", null),
            VarSet("st1:sn", "写产品 SN", "ProductSN", L("st1:code", "Text", "二维码读取"), "Global", null),
            VarSet("st1:bar", "写条码文本", "BarcodeText", L("st1:code", "Text", "二维码读取"), "Global", null),
            VarSet("st1:codec", "写读码数量", "CodeCount", L("st1:code", "Count", "二维码读取"), "Global", null),
            VarSet("st1:ocrt", "写 OCR 文本", "OcrText", L("st1:ocr", "Text", "OCR 识别批号"), "Global", null),
            VarSet("st1:ocrconf", "写 OCR 置信度", "OcrConfidence", L("st1:ocr", "Confidence", "OCR 识别批号"), "Global", null),
            ImageAssign("st1:assign", "缓存检测图", "st1:pre", "读码前处理", "LastImage"),
            ResultDisplay("st1:display", "读码结果显示", new[]
            {
                ("r1", "条码", "st1:code", "二维码读取", "Text"),
                ("r2", "码数", "st1:code", "二维码读取", "Count"),
                ("r3", "OCR", "st1:ocr", "OCR 识别批号", "Text"),
                ("r4", "判定", "st1:ok", "读码工位判定", "Result"),
            }, 0, "st1:qr", "采集二维码图"),
            Delay("st1:delay", "工位1节拍", 30),
        };
        AppendCount(s, "st1:cnt", "St1Count");
        return Flow("ST1_LoadCode", "工位1：上料定位 / 二维码 / OCR 批号", 0, 96, 0, s);
    }

    // ---------------- 工位 2：视觉定位 ----------------
    static JObject BuildLineSt2Flow()
    {
        var s = new JArray
        {
            MotionEnable("st2:en", "Y 使能", "Y", true, 10000),
            MotionMove("st2:move", "Y 到定位位", "Y", "Absolute", 8, 30),
            MotionWaitAt("st2:wait", "等待定位位", "Y", 8, 10000, 10),
            MotionReadStatus("st2:status", "读 Y 轴状态", "Y"),
            VarSet("st2:ypos", "写 Y 位置", "AxisYPos", L("st2:status", "Position", "读 Y 轴状态"), "Global", null),
            ImgFile("st2:acq", "采集定位图", SL_BOARD1, 0),
            PreProcess("st2:pre", "定位前处理", new JArray(
                Op("gauss", ("Size", "5")),
                Op("gray_closing", ("Width", "5"), ("Height", "5")),
                Op("emphasize", ("Width", "7"), ("Height", "7"), ("Factor", "1.2"))), 1,
                "st2:acq", "采集定位图"),
            CreateRoi("st2:roi", "定位 ROI", new JArray
            {
                Roi("圆检测区", "Rectangle", 380, 560, 0, 90, 90),
            }, 1, "st2:pre", "定位前处理"),
            GeometryFind("st2:circ", "找圆定位", "Circle", "st2:roi", "定位 ROI", 1, 20, 40, 30, false, 1,
                "st2:pre", "定位前处理"),
            GeometryRelation("st2:rel", "圆心关系", "CircleRelation", "st2:circ", "找圆定位", "st2:circ", "找圆定位", 1.0),
            VarSet("st2:relv", "写圆心关系", "CircleRelationMm", L("st2:rel", "Value", "圆心关系"), "Global", null),
            PoseFollow("st2:pose", "跟随定位", "st2:roi", "定位 ROI", "st2:circ", "找圆定位", 1),
            VarSet("st2:row", "写定位行", "MatchRow", L("st2:circ", "Row", "找圆定位"), "Global", null),
            VarSet("st2:col", "写定位列", "MatchCol", L("st2:circ", "Col", "找圆定位"), "Global", null),
            VarSet("st2:phi", "写定位角度", "MatchAngle", L("st2:circ", "Phi", "找圆定位"), "Global", null),
            VarSet("st2:mx", "写机械 X", "AlignOffsetX", L("st2:pose", "MechanicalX", "跟随定位"), "Global", null),
            VarSet("st2:my", "写机械 Y", "AlignOffsetY", L("st2:pose", "MechanicalY", "跟随定位"), "Global", null),
            Logic("st2:ok", "定位综合", "st2:circ", "找圆定位", "st2:rel", "圆心关系", "IsFound", "IsOk"),
            VarSet("st2:okv", "写工位2结果", "S2_Ok", L("st2:ok", "Result", "定位综合"), "Global", null),
            ResultDisplay("st2:display", "定位结果显示", new[]
            {
                ("r1", "定位行", "st2:circ", "找圆定位", "Row"),
                ("r2", "定位列", "st2:circ", "找圆定位", "Col"),
                ("r3", "角度", "st2:circ", "找圆定位", "Phi"),
                ("r4", "机械X", "st2:pose", "跟随定位", "MechanicalX"),
                ("r5", "判定", "st2:circ", "找圆定位", "IsFound"),
            }, 0, "st2:pre", "定位前处理"),
            Delay("st2:delay", "工位2节拍", 30),
        };
        AppendCount(s, "st2:cnt", "St2Count");
        return Flow("ST2_Position", "工位2：几何找圆 / 姿态跟随（标定由 Calib 流程引用）", 0, 96, 0, s);
    }

    // ---------------- 工位 3：精密测量 ----------------
    static JObject BuildLineSt3Flow()
    {
        var s = new JArray
        {
            ImgFile("st3:acq1", "采集宽度图", SL_CALIPER1, 0),
            PreProcess("st3:pre1", "宽度前处理", new JArray(Op("gauss", ("Size", "5"))), 1, "st3:acq1", "采集宽度图"),
            Action("st3:calib", "加载方案标定资产", "标定", P_CALIB, "标定", "\uf05b", new JObject
            {
                ["UseSavedCalibration"] = true,
                ["SavedCalibrationName"] = "LineCalib",
                ["SavedCalibrationId"] = G("calib:LineCalib").ToString(),
                ["SaveToCatalog"] = false,
                ["DisplayViewIndex"] = 0,
            }),
            VarSet("st3:mpp", "写测量当量", "CalMmPerPixel", L("st3:calib", "MmPerPixel", "测量标定"), "Global", null),
            CaliperStep("st3:w", "宽度测量", "st3:pre1", "宽度前处理", "Width",
                new[] { Reg("宽度区", "Rectangle", 240, 300, 0, 80, 100) }, 5, 30, 40, 0.5, -0.5, "st3:calib", "测量标定"),
            VarSet("st3:wv", "写宽度", "WidthMm", L("st3:w", "MeasureValue", "宽度测量"), "Global", null),

            ImgFile("st3:acq2", "采集两点距图", SL_CALIPER2, 0),
            PreProcess("st3:pre2", "两点距前处理", new JArray(Op("gauss", ("Size", "5"))), 1, "st3:acq2", "采集两点距图"),
            CaliperStep("st3:pp", "两点距测量", "st3:pre2", "两点距前处理", "PointToPoint",
                new[] { Reg("左条", "Rectangle", 240, 100, 0, 60, 80), Reg("右条", "Rectangle", 240, 340, 0, 60, 80) },
                5, 30, 240, 0.5, -0.5, "st3:calib", "测量标定"),
            VarSet("st3:ppv", "写两点距", "DistanceMm", L("st3:pp", "MeasureValue", "两点距测量"), "Global", null),

            ImgFile("st3:acq3", "采集点线距图", SL_CALIPER3, 0),
            PreProcess("st3:pre3", "点线距前处理", new JArray(Op("gauss", ("Size", "5"))), 1, "st3:acq3", "采集点线距图"),
            CaliperStep("st3:pl", "点到线距测量", "st3:pre3", "点线距前处理", "PointToLineDistance",
                new[] { Reg("点区", "Rectangle", 250, 300, 0, 60, 60), Reg("线区", "Rectangle", 250, 100, 0, 60, 80) },
                5, 30, 200, 0.5, -0.5, "st3:calib", "测量标定"),
            VarSet("st3:plv", "写点线距", "PointLineMm", L("st3:pl", "MeasureValue", "点到线距测量"), "Global", null),

            ImgFile("st3:acq4", "采集圆图", SL_CALIPER4, 0),
            PreProcess("st3:pre4", "圆前处理", new JArray(Op("gauss", ("Size", "5"))), 1, "st3:acq4", "采集圆图"),
            CaliperStep("st3:dia", "圆直径测量", "st3:pre4", "圆前处理", "CircleDiameter",
                new[] { Reg("圆搜索区", "Circle", 240, 320, 100) }, 16, 30, 200, 1, -1, "st3:calib", "测量标定"),
            VarSet("st3:diav", "写圆直径", "DiameterMm", L("st3:dia", "MeasureValue", "圆直径测量"), "Global", null),

            ImgFile("st3:acq5", "采集角度图", SL_CALIPER5, 0),
            PreProcess("st3:pre5", "角度前处理", new JArray(Op("gauss", ("Size", "5"))), 1, "st3:acq5", "采集角度图"),
            CaliperStep("st3:ang", "角度测量", "st3:pre5", "角度前处理", "Angle",
                new[] { Reg("竖线区", "Rectangle", 120, 240, 1.5708, 60, 80), Reg("横线区", "Rectangle", 240, 440, 0, 60, 80) },
                5, 30, 90, 0.5, -0.5, "st3:calib", "测量标定"),
            VarSet("st3:angv", "写角度", "AngleDeg", L("st3:ang", "MeasureValue", "角度测量"), "Global", null),

            Logic("st3:and1", "测量与1", "st3:w", "宽度测量", "st3:pp", "两点距测量", "IsOk", "IsOk"),
            Logic("st3:and2", "测量与2", "st3:and1", "测量与1", "st3:pl", "点到线距测量", "Result", "IsOk"),
            Logic("st3:and3", "测量与3", "st3:and2", "测量与2", "st3:dia", "圆直径测量", "Result", "IsOk"),
            Logic("st3:and4", "测量综合", "st3:and3", "测量与3", "st3:ang", "角度测量", "Result", "IsOk"),
            VarSet("st3:okv", "写工位3结果", "S3_Ok", L("st3:and4", "Result", "测量综合"), "Global", null),
            ResultDisplay("st3:display", "测量结果显示", new[]
            {
                ("r1", "宽度", "st3:w", "宽度测量", "MeasureValue"),
                ("r2", "两点距", "st3:pp", "两点距测量", "MeasureValue"),
                ("r3", "点线距", "st3:pl", "点到线距测量", "MeasureValue"),
                ("r4", "圆直径", "st3:dia", "圆直径测量", "MeasureValue"),
                ("r5", "角度", "st3:ang", "角度测量", "MeasureValue"),
                ("r6", "判定", "st3:and4", "测量综合", "Result"),
            }, 0, "st3:acq4", "采集圆图"),
            Delay("st3:delay", "工位3节拍", 30),
        };
        AppendCount(s, "st3:cnt", "St3Count");
        return Flow("ST3_Measure", "工位3：五种卡尺 / 像素当量标定 / 综合判定", 0, 96, 0, s);
    }

    // ---------------- 工位 4：表面检测 ----------------
    static JObject BuildLineSt4Flow()
    {
        var s = new JArray
        {
            ImgFile("st4:acq1", "采集表面图", SL_BOARD1, 0),
            PreProcess("st4:pre1", "表面前处理", new JArray(
                Op("gauss", ("Size", "5")),
                Op("gray_closing", ("Width", "5"), ("Height", "5"))), 1, "st4:acq1", "采集表面图"),
            CreateRoi("st4:roi", "表面 ROI", new JArray
            {
                Roi("表面区", "Rectangle", 480, 640, 0, 300, 300),
            }, 1, "st4:pre1", "表面前处理"),
            BlobStep("st4:blob", "表面缺陷检测", "st4:roi", "表面 ROI", 0.05, 1,
                "st4:pre1", "表面前处理"),
            VarSet("st4:dc", "写缺陷数", "DefectCount", L("st4:blob", "DefectCount", "表面缺陷检测"), "Global", null),
            VarSet("st4:da", "写缺陷面积", "DefectArea", L("st4:blob", "TotalAreaMm2", "表面缺陷检测"), "Global", null),

            ImgFile("st4:acq2", "采集对比图", SL_BOARD2, 0),
            PreProcess("st4:pre2", "对比前处理", new JArray(Op("gauss", ("Size", "3"))), 1, "st4:acq2", "采集对比图"),
            ImageDiff("st4:diff", "图像差分", "st4:pre1", "表面前处理", "st4:pre2", "对比前处理", 30, 20),
            VarSet("st4:diffc", "写差分缺陷数", "DiffCount", L("st4:diff", "DiffCount", "图像差分"), "Global", null),
            VarSet("st4:diffmax", "写最大差分", "MaxDiff", L("st4:diff", "MaxDiffValue", "图像差分"), "Global", null),
            Compare("st4:cmpdiff", "差分<=2000000", "st4:diff", "图像差分", "DiffCount", 2000000, "LessThanOrEqual"),

            ImgFile("st4:acq3", "采集颜色图", SL_COLOR_SYN, 0),
            ColorCheckStep("st4:color", "颜色序列检查", "st4:acq3", "采集颜色图",
                new double[] { 400, 276, 1.5708, 320, 210 }, "*,*,*,*,*,*,*,*,*,*,*,*", 12),
            VarSet("st4:cv", "写颜色判定", "ColorVerdict", L("st4:color", "Verdict", "颜色序列检查"), "Global", null),

            ImgFile("st4:acq4", "采集色块图", SL_COLOR2, 0),
            ColorRegionStep("st4:region", "区域颜色检查", "st4:acq4", "采集色块图", "Rectangle",
                new double[] { 240, 320, 0, 120, 120 }, "红", 0.5),
            VarSet("st4:cs", "写颜色占比", "ColorShare", L("st4:region", "Share", "区域颜色检查"), "Global", null),

            Logic("st4:a1", "表面与差分", "st4:blob", "表面缺陷检测", "st4:cmpdiff", "差分<=2000000", "IsOk", "Result"),
            Logic("st4:a2", "颜色与区域", "st4:color", "颜色序列检查", "st4:region", "区域颜色检查", "Result", "Result"),
            Logic("st4:a3", "表面综合", "st4:a1", "表面与差分", "st4:a2", "颜色与区域"),
            VarSet("st4:okv", "写工位4结果", "S4_Ok", L("st4:a3", "Result", "表面综合"), "Global", null),
            ResultDisplay("st4:display", "表面检测结果", new[]
            {
                ("r1", "缺陷数", "st4:blob", "表面缺陷检测", "DefectCount"),
                ("r2", "缺陷面积", "st4:blob", "表面缺陷检测", "TotalAreaMm2"),
                ("r3", "差分", "st4:diff", "图像差分", "DiffCount"),
                ("r4", "颜色", "st4:color", "颜色序列检查", "Verdict"),
                ("r5", "判定", "st4:a3", "表面综合", "Result"),
            }, 0, "st4:pre1", "表面前处理"),
            Delay("st4:delay", "工位4节拍", 30),
        };
        AppendCount(s, "st4:cnt", "St4Count");
        return Flow("ST4_Surface", "工位4：Blob / 图像差分 / 颜色序列 / 区域颜色", 0, 96, 0, s);
    }

    // ---------------- 工位 5：装配压装 ----------------
    static JObject BuildLineSt5Flow()
    {
        var recipe = new JArray
        {
            new JObject
            {
                ["Name"] = "BeadA",
                ["RefImagePath"] = SL_BEAD.Replace("\\", "/"),
                ["RefNoBeadImagePath"] = SL_BEAD_REF.Replace("\\", "/"),
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

        var s = new JArray
        {
            MotionEnable("st5:en", "Z 使能", "Z", true, 10000),
            MotionMove("st5:movez", "Z 到压装位", "Z", "Absolute", 5, 10),
            MotionWaitAt("st5:waitz", "等待 Z 到位", "Z", 5, 10000, 10),
            MotionIo("st5:clamp", "夹爪夹紧", "VirtualMotion", 1, true, "WriteOutput"),
            Delay("st5:hold", "夹紧保持", 200),
            ImgFile("st5:acq", "采集胶路图", SL_BEAD, 0),
            PreProcess("st5:pre", "胶路前处理", new JArray(Op("gauss", ("Size", "3"))), 1, "st5:acq", "采集胶路图"),
            Action("st5:bead", "胶路检测", "缺陷检测", P_BEAD, "胶路检测", "\uf1b3", new JObject
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
            }, new JObject { ["SrcImage"] = L("st5:pre", "Image", "胶路前处理") }),
            VarSet("st5:bec", "写胶路错误数", "BeadErrorCount", L("st5:bead", "ErrorCount", "胶路检测"), "Global", null),
            VarSet("st5:bel", "写胶路错误长", "BeadTotalLength", L("st5:bead", "TotalErrorLength", "胶路检测"), "Global", null),
            CommRead("st5:rtorque", "读扭矩", "Torque_Value"),
            CommRead("st5:rpress", "读压力", "Pressure_Value"),
            VarSet("st5:torque", "写扭矩全局", "Torque", L("st5:rtorque", "Value", "读扭矩"), "Global", null),
            Compare("st5:cmpt", "扭矩在范围内", "st5:rtorque", "读扭矩", "Value", 2.0, "LessThanOrEqual"),
            Logic("st5:ok", "装配综合", "st5:bead", "胶路检测", "st5:cmpt", "扭矩在范围内", "IsOk", "Result"),
            VarSet("st5:okv", "写工位5结果", "S5_Ok", L("st5:ok", "Result", "装配综合"), "Global", null),
            MotionMove("st5:backz", "Z 回安全高度", "Z", "Absolute", 0, 20),
            MotionWaitAt("st5:backw", "等待 Z 回位", "Z", 0, 10000, 10),
            MotionIo("st5:unclamp", "夹爪松开", "VirtualMotion", 1, false, "WriteOutput"),
            ResultDisplay("st5:display", "装配结果显示", new[]
            {
                ("r1", "胶路错误", "st5:bead", "胶路检测", "ErrorCount"),
                ("r2", "错误总长", "st5:bead", "胶路检测", "TotalErrorLength"),
                ("r3", "扭矩", "st5:rtorque", "读扭矩", "Value"),
                ("r4", "判定", "st5:ok", "装配综合", "Result"),
            }, 0, "st5:pre", "胶路前处理"),
            Delay("st5:delay", "工位5节拍", 50),
        };
        AppendCount(s, "st5:cnt", "St5Count");
        return Flow("ST5_Assembly", "工位5：Z 轴压装 / 夹爪 IO / 胶路 / 扭矩", 0, 96, 0, s);
    }

    // ---------------- 工位 6：识别追溯 ----------------
    static JObject BuildLineSt6Flow()
    {
        var s = new JArray
        {
            ImgFile("st6:acq1", "采集追溯码图", SL_QR2, 0),
            PreProcess("st6:pre1", "追溯码前处理", new JArray(Op("gauss", ("Size", "3"))), 1, "st6:acq1", "采集追溯码图"),
            Action("st6:code", "追溯码读取", "图像处理", P_CODE, "二维码读取", "\uf029", new JObject
            {
                ["Symbology"] = "QR Code",
                ["TimeoutMs"] = 500,
                ["Polarity"] = "any",
            }, new JObject { ["Image"] = L("st6:pre1", "Image", "追溯码前处理") }),
            Compare("st6:cmpcode", "追溯码数量>0", "st6:code", "追溯码读取", "Count", 0, "GreaterThan"),
            ImgFile("st6:acq2", "采集批号图2", SL_LOT2, 0),
            PreProcess("st6:pre2", "批号前处理2", new JArray(Op("gauss", ("Size", "3"))), 1, "st6:acq2", "采集批号图2"),
            Ocr("st6:ocr", "OCR 批号复核", new double[] { 272, 320, 0, 170, 40 }, 1, "st6:pre2", "批号前处理2", true),
            Compare("st6:cmpocr", "OCR 字符>0", "st6:ocr", "OCR 批号复核", "Count", 0, "GreaterThan"),
            Logic("st6:ok", "追溯工位判定", "st6:cmpcode", "追溯码数量>0", "st6:cmpocr", "OCR 字符>0"),
            VarSet("st6:okv", "写工位6结果", "S6_Ok", L("st6:ok", "Result", "追溯工位判定"), "Global", null),
            VarSet("st6:bar", "写追溯码", "BarcodeText", L("st6:code", "Text", "追溯码读取"), "Global", null),
            VarSet("st6:ocrt", "写OCR复核", "OcrText", L("st6:ocr", "Text", "OCR 批号复核"), "Global", null),
            VarSet("st6:codec", "写码数", "CodeCount", L("st6:code", "Count", "追溯码读取"), "Global", null),

            // 追溯明细：数据库写行（未配置语句时停用，作为接入位保留）
            Disable(Action("st6:db", "写入追溯数据库", "数据处理", P_DBWRITE, "数据库写行", "\uf1c0", new JObject
            {
                ["ProfileId"] = Guid.Empty.ToString(),
                ["StatementId"] = Guid.Empty.ToString(),
                ["FailFast"] = false,
                ["TimeoutSeconds"] = 5,
            })),

            ResultDisplay("st6:display", "追溯结果显示", new[]
            {
                ("r1", "追溯码", "st6:code", "追溯码读取", "Text"),
                ("r2", "码数", "st6:code", "追溯码读取", "Count"),
                ("r3", "OCR", "st6:ocr", "OCR 批号复核", "Text"),
                ("r4", "判定", "st6:ok", "追溯工位判定", "Result"),
            }, 0, "st6:pre1", "追溯码前处理"),
            Delay("st6:delay", "工位6节拍", 30),
        };
        AppendCount(s, "st6:cnt", "St6Count");
        return Flow("ST6_Trace", "工位6：追溯码复核 / OCR 复核 / 数据库接入位", 0, 96, 0, s);
    }

    // ---------------- AI 扩展（模型未部署，默认停用） ----------------
    static JObject BuildLineAiFlow()
    {
        var s = new JArray
        {
            Action("ai:script", "统计脚本", "逻辑控制", P_CSCRIPT, "C# 脚本", "\uf121", new JObject
            {
                ["ScriptText"] =
                    "var ok = Context.GetGlobal<int>(\"OKCount\");\n" +
                    "var ng = Context.GetGlobal<int>(\"NGCount\");\n" +
                    "var total = ok + ng;\n" +
                    "Context.SetGlobal(\"TotalCount\", total);\n" +
                    "Context.SetGlobal(\"YieldRate\", total > 0 ? ok * 100.0 / total : 100.0);\n" +
                    "Context.SetGlobal(\"OEE\", total > 0 ? Math.Min(99.0, ok * 100.0 / total) : 0.0);\n" +
                    "Context.Info($\"统计刷新 OK={ok} NG={ng} 良率={(total > 0 ? ok * 100.0 / total : 100.0):F2}%\");",
                ["InputVars"] = new JArray(),
                ["OutputVars"] = new JArray(),
                ["SharedVars"] = new JArray
                {
                    SharedVar("OKCount", false),
                    SharedVar("NGCount", false),
                    SharedVar("TotalCount", true),
                    SharedVar("YieldRate", true),
                    SharedVar("OEE", true),
                },
            }),

            Disable(Action("ai:yolo", "YOLO 目标检测", "图像处理", P_YOLO, "YOLO 目标检测", "\uf03d", new JObject
            {
                ["ModelPath"] = "",
                ["InputSize"] = 0,
                ["ConfidenceThreshold"] = 0.25,
                ["NmsIoU"] = 0.45,
                ["MaxDetections"] = 100,
                ["CropMargin"] = 0,
                ["DisplayViewIndex"] = 1,
            }, new JObject { ["Image"] = L("st4:pre1", "Image", "表面前处理") })),

            Disable(Action("ai:anomaly", "深度学习异常检测", "图像处理", P_DLANOMALY, "深度学习异常检测", "\uf188", new JObject
            {
                ["ModelPath"] = "",
                ["InputSize"] = 0,
                ["DisplayViewIndex"] = 1,
            }, new JObject { ["Image"] = L("st4:pre1", "Image", "表面前处理") })),

            Disable(Action("ai:classify", "深度学习分类", "图像处理", P_DLCLASSIFY, "深度学习分类", "\uf02d", new JObject
            {
                ["ModelPath"] = "",
                ["InputSize"] = 0,
                ["TopK"] = 3,
            }, new JObject { ["Image"] = L("st4:pre1", "Image", "表面前处理") })),

            Disable(Action("ai:segment", "深度学习分割", "图像处理", P_DLSEGMENT, "深度学习分割", "\uf1c5", new JObject
            {
                ["ModelPath"] = "",
                ["InputSize"] = 0,
                ["ClassIndex"] = 1,
                ["DisplayViewIndex"] = 1,
            }, new JObject { ["Image"] = L("st4:pre1", "Image", "表面前处理") })),

            Disable(Action("ai:imagescript", "图像脚本", "图像处理", P_ISCRIPT, "图像脚本", "\uf121", new JObject
            {
                ["Procedures"] = new JArray(),
                ["SelectedProcedure"] = "",
                ["InputVars"] = new JArray(),
                ["OutputVars"] = new JArray(),
            })),

            Disable(Action("ai:match", "模板匹配（模板待学习）", "定位", P_MATCH, "模板匹配", "\uf1b2", new JObject
            {
                ["TemplateLibraryJson"] = "[]",
                ["DefaultTemplateName"] = "",
                ["MinScore"] = 0.5,
                ["MaxMatches"] = 1,
                ["MaxOverlap"] = 0.3,
                ["Greediness"] = 0.8,
                ["DisplayViewIndex"] = 1,
                ["OutputAlignedImage"] = true,
                ["UseSearchRegion"] = false,
            }, new JObject { ["Image"] = L("st2:pre", "Image", "定位前处理") })),

            Disable(Action("ai:handeye", "手眼标定（样本待采集）", "标定", P_HANDEYE, "手眼标定", "\uf0b1", new JObject
            {
                ["PoseKind"] = "EyeInHand",
                ["Samples"] = new JArray(),
                ["MaxTranslationRmsMm"] = 1.0,
                ["MaxRotationRmsDeg"] = 0.5,
                ["RequireRecommendedPairs"] = true,
                ["SavedResult"] = "",
            })),

            Disable(Action("ai:dbquery", "数据库查询（未配置连接）", "数据处理", P_DBQUERY, "数据库查询", "\uf002", new JObject
            {
                ["ProfileId"] = Guid.Empty.ToString(),
                ["StatementId"] = Guid.Empty.ToString(),
                ["FailFast"] = false,
                ["TimeoutSeconds"] = 5,
            })),

            Disable(Action("ai:dbexec", "数据库执行（未配置连接）", "数据处理", P_DBEXEC, "数据库执行", "\uf085", new JObject
            {
                ["ProfileId"] = Guid.Empty.ToString(),
                ["StatementId"] = Guid.Empty.ToString(),
                ["FailFast"] = false,
                ["TimeoutSeconds"] = 5,
            })),
        };
        return Flow("AI_Extension", "AI 扩展位：YOLO/DL/手眼/数据库（模型与连接未部署，默认停用）", 0, 96, 0, s);
    }

    // ---------------- 配方 ----------------
    static JObject BuildLineRecipeFlow()
    {
        var s = new JArray
        {
            VarSet("rc:name", "写配方名", "RecipeName", null, "Global", new JValue("Recipe_A")),
            VarSet("rc:type", "写产品类型", "ProductType", null, "Global", new JValue("A")),
            VarSet("rc:batch", "写批次", "BatchNo", null, "Global", new JValue("B20261025")),
            VarSet("rc:ok", "清零良品", "OKCount", null, "Global", new JValue(0)),
            VarSet("rc:ng", "清零不良", "NGCount", null, "Global", new JValue(0)),
            VarSet("rc:total", "清零总检", "TotalCount", null, "Global", new JValue(0)),
            VarSet("rc:cyc", "清零节拍", "CycleCount", null, "Global", new JValue(0)),
            VarSet("rc:yield", "良率复位", "YieldRate", null, "Global", new JValue(100.0)),
            VarSet("rc:oee", "OEE 复位", "OEE", null, "Global", new JValue(0.0)),
            RunFlow("rc:calib", "执行标定并写入方案", "Calib", 60000),
            Delay("rc:delay", "配方生效", 100),
        };
        return Flow("Recipe", "配方切换：产品 A / 清零计数 / 良率复位", 0, 96, 0, s);
    }

    // ---------------- 通讯诊断 ----------------
    static JObject BuildLineDiagFlow()
    {
        var loopBody = new JArray
        {
            CommWrite("dg:wt", "写触发 1", "PLC_Trigger", new JValue(1), true),
            CommRead("dg:rt", "回读触发", "PLC_Trigger"),
            Compare("dg:ct", "触发回读=1", "dg:rt", "回读触发", "Value", 1, "Equal"),
            CommWrite("dg:ws7", "写 S7 结果 7", "S7_Result", new JValue(7), true),
            CommRead("dg:rs7", "回读 S7 结果", "S7_Result"),
            Compare("dg:cs7", "S7 回读=7", "dg:rs7", "回读 S7 结果", "Value", 7, "Equal"),
            Logic("dg:and", "回环通过", "dg:ct", "触发回读=1", "dg:cs7", "S7 回读=7"),
            VarSet("dg:ok", "写诊断结果", "CommOk", L("dg:and", "Result", "回环通过"), "Global", null),
            Delay("dg:delay", "诊断节拍", 50),
        };

        var s = new JArray
        {
            CommRead("dg:trigger", "读 PLC 触发", "PLC_Trigger"),
            CommRead("dg:meter", "读仪表", "Meter_Value"),
            CommRead("dg:press", "读压力", "Pressure_Value"),
            CommRead("dg:s7hb", "读 S7 心跳", "S7_Heartbeat"),
            ForSmart("dg:for", "通讯回环 3 次", 3, loopBody),
            Compare("dg:cmpmeter", "仪表<=80", "dg:meter", "读仪表", "Value", 80, "LessThanOrEqual"),
            Logic("dg:commok", "通讯链正常", "dg:s7hb", "读 S7 心跳", "dg:cmpmeter", "仪表<=80", "Value", "Result"),
            VarSet("dg:commw", "写通讯正常", "CommOk", L("dg:commok", "Result", "通讯链正常"), "Global", null),
            VarSet("dg:visionw", "写视觉正常", "VisionOk", null, "Global", new JValue(true)),
            VarSet("dg:motionw", "写运动正常", "MotionOk", null, "Global", new JValue(true)),
            VarSet("dg:dataw", "写数据正常", "DataOk", null, "Global", new JValue(true)),
            Delay("dg:end", "诊断结束", 100),
        };
        return Flow("Diag", "通讯诊断：Modbus/S7 写读回环 + 状态汇总", 0, 96, 0, s);
    }

    // ---------------- 报警复位 ----------------
    static JObject BuildLineAlarmResetFlow()
    {
        var s = new JArray
        {
            VarSet("ar:fault", "清产线故障", "LineFault", null, "Global", new JValue(false)),
            VarSet("ar:code", "清故障码", "FaultCode", null, "Global", new JValue("")),
            VarSet("ar:alarm", "清报警计数", "AlarmCount", null, "Global", new JValue(0)),
            CommWrite("ar:plc", "清 PLC 判定", "PLC_Result", new JValue(false), true),
            CommWrite("ar:ack", "清 PLC 确认", "PLC_Ack", new JValue(0), true),
            MotionStop("ar:stop", "停止全部轴", "", "Decelerate", false),
            Delay("ar:delay", "复位完成", 200),
        };
        return Flow("AlarmReset", "报警复位：清故障 / 清确认 / 停轴", 0, 96, 0, s);
    }

    // ---------------- 收尾 ----------------
    static JObject BuildLineEndFlow()
    {
        var s = new JArray
        {
            MotionStop("end:stopx", "停止 X", "X", "Decelerate", false),
            MotionStop("end:stopy", "停止 Y", "Y", "Decelerate", false),
            MotionStop("end:stopz", "停止 Z", "Z", "Decelerate", false),
            MotionIo("end:io", "熄灭就绪灯", "VirtualMotion", 0, false, "WriteOutput"),
            CommWrite("end:res", "清 PLC 判定", "PLC_Result", new JValue(false), true),
            VarSet("end:ready", "清系统就绪", "SystemReady", null, "Global", new JValue(false)),
            VarSet("end:run", "清运行中", "LineRunning", null, "Global", new JValue(false)),
            Delay("end:delay", "收尾延时", 100),
        };
        return Flow("End", "收尾：停轴 / IO 复位 / 清除就绪", 3, 96, 0, s, enabled: false);
    }

    // ==================================================================
    //  步骤辅助
    // ==================================================================
    static JObject A(string seed, string name, string category, string type, string pluginName,
        string icon, JObject inputs, JObject links = null)
        => Action(seed, name, category, type, pluginName, icon, inputs, links);

    static JObject VarSet(string seed, string name, string varName, JObject valueLink,
        string scope, JToken constant, bool createIfNotExists = false)
        => VarAssign(seed, name, varName, valueLink, scope, constant, createIfNotExists);

    static JObject IfSmart(string seed, string name, string expr, string varName, string varType,
        JObject conditionLink, JArray thenSteps, JArray elseSteps)
    {
        var id = G("ifvar:" + seed);
        return IfStep(seed, name, expr, id, varName, varType, conditionLink, thenSteps, elseSteps);
    }

    static JObject CaseSmart(string seed, string name, string judge, JArray branches, string judgeType)
        => CaseStep(seed, name, judge, branches, judgeType);

    static JObject ForSmart(string seed, string name, int count, JArray body)
        => ForLoop(seed, name, count, body, seed);

    static JObject WaitSmart(string seed, string name, string varName, string expr, JObject link,
        int timeoutMs, int timeoutAction, string timeoutMessage, string varType = "System.Int32")
        => WaitStep(seed, name, varName, expr, link, timeoutMs, timeoutAction, timeoutMessage, varType);

    static JObject ParallelSmart(string seed, string name, int mode, int failFast, int timeout, JArray branches)
        => Parallel(seed, name, mode, failFast, timeout, branches);

    static void AppendCount(JArray steps, string seed, string varName)
    {
        steps.Add(MathAdd(seed + ":add", "过站计数+1", varName));
        steps.Add(ConvertToInt(seed + ":int", "过站计数转 Int", seed + ":add", "过站计数+1"));
        steps.Add(VarSet(seed + ":set", "写回过站计数", varName, L(seed + ":int", "Result", "过站计数转 Int"), "Global", null));
    }

    static JObject SharedVar(string name, bool readWrite)
        => new()
        {
            ["VariableId"] = G("var:" + name).ToString(),
            ["Name"] = name,
            ["Alias"] = name,
            ["Access"] = readWrite ? "ReadWrite" : "Read",
            ["Remark"] = "",
        };

    static JObject MathBinary(string seed, string name, string op, JObject a, JObject b)
        => Action(seed, name, "数据处理", P_MATH, "数学计算", "\uf1ec",
            new JObject { ["Operation"] = op },
            new JObject { ["A"] = a, ["B"] = b });

    static JObject MathWithConstant(string seed, string name, string op, JObject a, double b)
        => Action(seed, name, "数据处理", P_MATH, "数学计算", "\uf1ec",
            new JObject { ["Operation"] = op, ["B"] = b },
            new JObject { ["A"] = a });

    static JObject ConvertStep(string seed, string name, string srcSeed, string srcName, string target)
        => Action(seed, name, "数据处理", P_CONVERT, "类型转换", "\uf0ec",
            new JObject { ["Target"] = target, ["Format"] = "yyyy-MM-dd HH:mm:ss" },
            new JObject { ["Input"] = L(srcSeed, "Result", srcName) });

    static JObject MotionWaitAt(string seed, string name, string axis, double target, int timeoutMs, int pollMs)
        => Action(seed, name, "运动控制", P_MWAIT, "等待到位", "\uf085", new JObject
        {
            ["Axis"] = axis,
            ["Target"] = target,
            ["Tolerance"] = 0.05,
            ["TimeoutMs"] = timeoutMs,
            ["PollIntervalMs"] = pollMs,
        });

    static JObject BlobStep(string seed, string name, string maskSeed, string maskName, double mmPerPixel,
        int view, string imgSeed, string imgName)
        => Action(seed, name, "缺陷检测", P_BLOB, "Blob 缺陷检测", "\uf0c8", new JObject
        {
            ["ThresholdMode"] = "Fixed",
            ["DetectTarget"] = "Dark",
            ["DetectBrightAndDark"] = false,
            ["MinGray"] = 0.0,
            ["MaxGray"] = 60.0,
            ["MinArea"] = 20.0,
            ["MaxBlobArea"] = 0.0,
            ["MaxSingleArea"] = 1000000.0,
            ["MaxTotalArea"] = 1000000.0,
            ["MinDefectCount"] = 0,
            ["MaxDefectCount"] = 500,
            ["ExcludeBorderPx"] = 0,
            ["OpenRadius"] = 0.0,
            ["CloseRadius"] = 1.0,
            ["FillHoles"] = false,
            ["MergeRadius"] = 0.0,
            ["PixelSizeMm"] = mmPerPixel,
            ["SortMode"] = "None",
            ["DisplayViewIndex"] = view,
            ["MmPerPixel"] = mmPerPixel,
        }, new JObject
        {
            ["SrcImage"] = L(imgSeed, "Image", imgName),
            ["MaskRegion"] = L(maskSeed, "MaskRegion", maskName),
        });

    static JObject ImageAssign(string seed, string name, string imgSeed, string imgName, string targetVar)
        => Action(seed, name, "图像处理", P_ASSIGN, "图像赋值", "\uf0e2",
            new JObject { ["VariableName"] = targetVar },
            new JObject { ["InputImage"] = L(imgSeed, "Image", imgName) });

    static JObject ImageDiff(string seed, string name, string refSeed, string refName,
        string testSeed, string testName, double threshold, double minArea)
        => Action(seed, name, "图像处理", P_DIFF, "图像差分", "\uf0c5", new JObject
        {
            ["Algorithm"] = "Absolute",
            ["Polarity"] = "Both",
            ["Aggregation"] = "Components",
            ["SizeMismatch"] = "Fail",
            ["Threshold"] = threshold,
            ["MinArea"] = minArea,
            ["MaxTotalArea"] = 0,
            ["MaxComponentArea"] = 0,
            ["MaxValueLimit"] = 0,
            ["AmbientFloor"] = 8,
            ["SignedDisplay"] = 128,
            ["DisplayViewIndex"] = 1,
        }, new JObject
        {
            ["Reference"] = L(refSeed, "Image", refName),
            ["Test"] = L(testSeed, "Image", testName),
        });

    static JObject ColorCheckStep(string seed, string name, string imgSeed, string imgName,
        double[] roi, string recipe, int wires)
        => Action(seed, name, "图像处理", P_COLORCHK, "颜色序列检查", "\uf1fb", new JObject
        {
            ["RoiParams"] = new JArray(roi),
            ["ExpectedSequence"] = "",
            ["RecipeText"] = recipe,
            ["WireCount"] = wires,
            ["ReverseSequence"] = false,
            ["Regularize"] = true,
            ["DisplayViewIndex"] = 1,
        }, new JObject { ["Image"] = L(imgSeed, "Image", imgName) });

    static JObject ColorRegionStep(string seed, string name, string imgSeed, string imgName,
        string shape, double[] roi, string colors, double minShare)
        => Action(seed, name, "图像处理", P_COLORREG, "区域颜色检查", "\uf1fc", new JObject
        {
            ["RoiShape"] = shape,
            ["RoiParams"] = new JArray(roi),
            ["ExpectedColors"] = colors,
            ["MinShare"] = minShare,
            ["SamplePoints"] = 400,
            ["DisplayViewIndex"] = 1,
        }, new JObject { ["Image"] = L(imgSeed, "Image", imgName) });

    static JObject CaliperStep(string seed, string name, string imgSeed, string imgName, string kind,
        JObject[] regions, int count, double annulus, double standard, double upper, double lower,
        string calibSeed, string calibName)
        => Action(seed, name, "测量", P_CALIPER, "卡尺测量", "\uf0b3", new JObject
        {
            ["MeasureKind"] = kind,
            ["CaliperRegions"] = new JArray(regions),
            ["CaliperCount"] = count,
            ["Sigma"] = 1.0,
            ["EdgeThreshold"] = 30.0,
            ["Polarity"] = "All",
            ["Select"] = "First",
            ["Interpolation"] = "Bilinear",
            ["FitAlgorithm"] = "Tukey",
            ["FitMaxError"] = 1.0,
            ["FitMinPoints"] = 3,
            ["AnnulusWidth"] = annulus,
            ["StandardValue"] = standard,
            ["UpperTolerance"] = upper,
            ["LowerTolerance"] = lower,
            ["PixelSizeMm"] = 1.0,
            ["DisplayViewIndex"] = 0,
        }, new JObject
        {
            ["SrcImage"] = L(imgSeed, "Image", imgName),
            ["MmPerPixel"] = L(calibSeed, "MmPerPixel", calibName),
        });

    static JObject LineCsv(string seed, string name)
        => Action(seed, name, "数据处理", P_CSV, "CSV记录", "\uf15c", new JObject
        {
            ["DirectoryTemplate"] = @"History\{StepName}\{yyyy-MM}\",
            ["FileNamePattern"] = "{yyyy-MM-dd}.csv",
            ["WriteHeader"] = true,
            ["KeepDays"] = 60,
            ["BlockOnFailure"] = false,
            ["FlushEveryRow"] = true,
            ["Columns"] = new JArray
            {
                Col("时间", "BuiltIn", "", "Timestamp"),
                Col("序号", "BuiltIn", "", "Sequence"),
                Col("产品SN", "Variable", "ProductSN", "Timestamp"),
                Col("产品类型", "Variable", "ProductType", "Timestamp"),
                Col("工位1", "Variable", "S1_Ok", "Timestamp"),
                Col("工位2", "Variable", "S2_Ok", "Timestamp"),
                Col("工位3", "Variable", "S3_Ok", "Timestamp"),
                Col("工位4", "Variable", "S4_Ok", "Timestamp"),
                Col("工位5", "Variable", "S5_Ok", "Timestamp"),
                Col("工位6", "Variable", "S6_Ok", "Timestamp"),
                Col("综合判定", "Variable", "ResultText", "Timestamp"),
                Col("宽度(mm)", "Variable", "WidthMm", "Timestamp"),
                Col("直径(mm)", "Variable", "DiameterMm", "Timestamp"),
                Col("角度(°)", "Variable", "AngleDeg", "Timestamp"),
                Col("缺陷数", "Variable", "DefectCount", "Timestamp"),
                Col("OCR", "Variable", "OcrText", "Timestamp"),
                Col("二维码", "Variable", "BarcodeText", "Timestamp"),
                Col("良率(%)", "Variable", "YieldRate", "Timestamp"),
                Col("班次", "Variable", "Shift", "Timestamp"),
                Col("操作员", "Variable", "OperatorId", "Timestamp"),
            },
            ["ImageSaveMode"] = "Off",
            ["ResultColumn"] = "综合判定",
            ["NgValue"] = "NG",
            ["ImageDirName"] = "img",
            ["ImageNameTemplate"] = "{序号}_{结果}",
            ["ImageFormat"] = "png",
        });

    static JObject LineExcel(string seed, string name)
        => Action(seed, name, "数据处理", P_EXCEL, "Excel报表", "\uf1c3", new JObject
        {
            ["SourcePathTemplate"] = @"History\{源步骤}\{yyyy-MM}\{yyyy-MM-dd}.csv",
            ["SourceStepName"] = "Main.产线主记录",
            ["OutputDirectoryTemplate"] = @"History\{StepName}\{yyyy-MM}\报表\",
            ["ReportFileNamePattern"] = "{yyyy-MM-dd}_产线报表.xlsx",
            ["LinkColumn"] = "",
            ["FreezeHeader"] = true,
            ["AutoFilter"] = true,
            ["OnlyRowsWithImage"] = false,
            ["ExportPolicy"] = "EachRun",
            ["OpenAfterExport"] = false,
            ["BlockOnFailure"] = false,
        });

    static JObject LineUpload(string seed, string name)
        => Action(seed, name, "数据处理", P_UPLOAD, "结果上报", "\uf093", new JObject
        {
            ["PayloadKind"] = "FieldTable",
            ["Url"] = "http://127.0.0.1:19001/api/upload",
            ["TimeoutMs"] = 2000,
            ["Headers"] = new JArray(),
            ["Fields"] = new JArray
            {
                Field("sn", "Variable", "ProductSN", "Timestamp"),
                Field("product", "Variable", "ProductType", "Timestamp"),
                Field("result", "Variable", "ResultText", "Timestamp"),
                Field("yield", "Variable", "YieldRate", "Timestamp"),
                Field("defect", "Variable", "DefectCount", "Timestamp"),
                Field("time", "BuiltIn", "", "Timestamp"),
            },
            ["SendTiming"] = "EachRun",
            ["JudgeFieldName"] = "result",
            ["NgValue"] = "NG",
            ["AsyncMode"] = true,
            ["RetryCount"] = 0,
            ["BlockOnFailure"] = false,
            ["SpoolEnabled"] = false,
            ["TimestampFormat"] = "yyyy-MM-dd HH:mm:ss.fff",
            ["TimestampUtc"] = false,
        });
}
