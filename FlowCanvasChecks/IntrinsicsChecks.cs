using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Core.Halcon.Helpers;
using Core.Interfaces;
using Plugin.Calibration;
using Plugin.Calibration.Models;
using Plugin.PoseTransform;
using VisionMaster.Models;
using VisionMaster.Services;
using static FlowCanvasChecks.Program;
using ExecutionContext = VisionMaster.Services.ExecutionContext;

namespace FlowCanvasChecks
{
    /// <summary>
    /// 内参标定（标定板内参标定，二期③）断言。
    ///
    /// 与 <see cref="CalibrationChecks"/> 的分工
    /// ---------
    /// · `CalibrationChecks` 守"像素 ↔ 机械"的映射（仿射/透视/网格）；
    /// · 本类守"**畸变像素 ↔ 去畸变像素**"的映射（相机内参 + 畸变模型），以及它与平面标定的串联。
    ///
    /// 为什么主口径是**合成自验证**
    /// ---------
    /// 内参标定的真值无法从真图构造（真图里没有"已知相机参数"），但 HALCON 提供
    /// `sim_caltab`：用**已知相机参数 + 已知位姿**合成标定板图，再跑完整标定链、比对恢复值。
    /// 这比"拍真板子肉眼核对"严格得多——真值精确到双精度，判据可以量化到千分之几个百分点。
    /// 实测（`calplate_320mm.cpd` @1.2m，12 张倾斜位姿）：焦距相对误差 0.0018%、
    /// 主点偏差 0.0001px、重投影 RMS 0.0039px。
    ///
    /// 四段（各自独立可跑）
    /// ---------
    /// ① 契约层：`CameraIntrinsics.IsUsable` 良构判据 + DTO 往返（纯托管，不需要 HALCON）；
    /// ② 算子层：合成自验证（真 HALCON）——焦距/主点/RMS/kappa 达到设计判据；
    /// ③ 去畸变层：`CameraDistortion` 正/反/往返 + 两模型分档 + 中心不动点（真 HALCON）；
    /// ④ 串联层：标定产 → 坐标变换消费（四类失配 + Kind 判据，纯托管）+
    ///    "跳过去畸变结果必须不同"的**反证**（真 HALCON，去畸变真的生效的唯一硬证据）。
    ///
    /// ⚠ 环境依赖（R38 口径）：②③④ 的 HALCON 段需要 `HALCONROOT\calib\` 下的标定板描述文件。
    /// 缺失时**明确跳过并写出反证依据**（"本机 HALCON 装得不全，无板则 sim_caltab 与
    /// find_calib_object 都无从执行"）——不是静默放行。
    /// </summary>
    internal static class IntrinsicsChecks
    {
        /// <summary>合成自验证真值：1280×1024、f=0.052、kappa=-1500、主点 (640,512)</summary>
        private const double TrueFocus = 0.052;
        private const double TrueKappa = -1500.0;
        private const double TrueSx = 5.2e-6;
        private const double TrueSy = 5.2e-6;
        private const double TrueCx = 640.0;
        private const double TrueCy = 512.0;
        private const int TrueW = 1280;
        private const int TrueH = 1024;

        private static CameraIntrinsics DivIntrinsics() => new()
        {
            ImageWidth = TrueW,
            ImageHeight = TrueH,
            CameraType = CameraIntrinsics.DivisionType,
            Focus = TrueFocus,
            Kappa = TrueKappa,
            Sx = TrueSx,
            Sy = TrueSy,
            Cx = TrueCx,
            Cy = TrueCy,
            ReprojectionRmsPx = 0.004,
            ImageCount = 12,
            CaltabDescr = "calplate_320mm.cpd",
            CameraSerial = "SYNTH-01",
            CreatedAtUtc = new DateTime(2026, 10, 16, 0, 0, 0, DateTimeKind.Utc),
        };

        private static CameraIntrinsics PolyIntrinsics() => new()
        {
            ImageWidth = TrueW,
            ImageHeight = TrueH,
            CameraType = CameraIntrinsics.PolynomialType,
            Focus = TrueFocus,
            Sx = TrueSx,
            Sy = TrueSy,
            Cx = TrueCx,
            Cy = TrueCy,
            Distortion = new[] { TrueKappa, 1.0e6, -1.0e8, 0.0, 0.0 },
            ReprojectionRmsPx = 0.004,
            ImageCount = 12,
            CaltabDescr = "calplate_320mm.cpd",
            CameraSerial = "SYNTH-01",
            CreatedAtUtc = new DateTime(2026, 10, 16, 0, 0, 0, DateTimeKind.Utc),
        };

        public static void Run()
        {
            Section("[IC] 内参标定：契约良构 / 合成自验证 / 去畸变往返 / 端到端串联");

            ContractLayer();
            OperatorLayer();
            DistortionLayer();
            EndToEndLayer();
        }

        // ─────────────────────────────────────────────────────────────────
        // ① 契约层（纯托管 —— 没有 HALCON 的机器上这几条也必须过）
        // ─────────────────────────────────────────────────────────────────
        private static void ContractLayer()
        {
            Check("[IC] 契约：良性 division 内参通过 IsUsable",
                CameraIntrinsics.IsUsable(DivIntrinsics(), out var e1) && e1 == null, e1 ?? "");

            Check("[IC] 契约：良性 polynomial 内参通过 IsUsable",
                CameraIntrinsics.IsUsable(PolyIntrinsics(), out var e2) && e2 == null, e2 ?? "");

            // ---- 失败面逐条：每条都要"明确失败 + 中文原因"，不许静默放行 ----
            Check("[IC] 契约：null 内参明确失败且给中文原因",
                !CameraIntrinsics.IsUsable(null, out var eNull) && !string.IsNullOrEmpty(eNull), eNull ?? "(空)");

            var badType = DivIntrinsics();
            badType.CameraType = "area_scan_telecentric";
            Check("[IC] 契约：不认识的模型类型明确失败",
                !CameraIntrinsics.IsUsable(badType, out var eType), eType ?? "(空)");

            var badPoly = PolyIntrinsics();
            badPoly.Distortion = new[] { 1.0 };   // polynomial 要 5 个
            Check("[IC] 契约：polynomial 系数个数不符明确失败（要 5 个，给 1 个）",
                !CameraIntrinsics.IsUsable(badPoly, out var ePoly), ePoly ?? "(空)");

            var nanPoly = PolyIntrinsics();
            nanPoly.Distortion![3] = double.NaN;
            Check("[IC] 契约：polynomial 系数含 NaN 明确失败",
                !CameraIntrinsics.IsUsable(nanPoly, out var eNan), eNan ?? "(空)");

            var badFocus = DivIntrinsics();
            badFocus.Focus = 0;
            Check("[IC] 契约：焦距非正明确失败",
                !CameraIntrinsics.IsUsable(badFocus, out var eFocus), eFocus ?? "(空)");

            var badPixel = DivIntrinsics();
            badPixel.Sx = -1;
            Check("[IC] 契约：像元尺寸非正明确失败",
                !CameraIntrinsics.IsUsable(badPixel, out var ePixel), ePixel ?? "(空)");

            var badSize = DivIntrinsics();
            badSize.ImageWidth = 0;
            Check("[IC] 契约：图尺寸非正明确失败",
                !CameraIntrinsics.IsUsable(badSize, out var eSize), eSize ?? "(空)");

            var nanKappa = DivIntrinsics();
            nanKappa.Kappa = double.NaN;
            Check("[IC] 契约：kappa 含 NaN 明确失败",
                !CameraIntrinsics.IsUsable(nanKappa, out var eKappa), eKappa ?? "(空)");

            // ---- DTO 往返：内参要随 [StepConfig] 落盘，字段一个都不能丢 ----
            // 落盘走**插件本地 DTO**（`.vms` 的 $type 白名单不含 Core.Interfaces，直接用契约类型
            // 会写出"能存、再打开就抛 JsonSerializationException"的方案文件），
            // 所以真正需要守的是 DTO ⇄ 契约那一层。
            var src = DivIntrinsics();
            var back = CameraIntrinsicsDto.FromContract(src)!.ToContract()!;
            Check("[IC] 契约：division 内参经 DTO 往返字段逐项一致（Distortion 保持 null）",
                back.ImageWidth == src.ImageWidth
                && back.ImageHeight == src.ImageHeight
                && back.CameraType == src.CameraType
                && back.Focus.Equals(src.Focus)
                && back.Kappa.Equals(src.Kappa)
                && back.Sx.Equals(src.Sx) && back.Sy.Equals(src.Sy)
                && back.Cx.Equals(src.Cx) && back.Cy.Equals(src.Cy)
                && back.ReprojectionRmsPx.Equals(src.ReprojectionRmsPx)
                && back.ImageCount == src.ImageCount
                && back.CaltabDescr == src.CaltabDescr
                && back.CameraSerial == src.CameraSerial
                && back.CreatedAtUtc == src.CreatedAtUtc
                && back.Distortion == null,
                $"type={back.CameraType} f={back.Focus} distortion={(back.Distortion == null ? "null" : "非null")}");

            var psrc = PolyIntrinsics();
            var pback = CameraIntrinsicsDto.FromContract(psrc)!.ToContract()!;
            Check("[IC] 契约：polynomial 内参经 DTO 往返系数逐位保留",
                pback.CameraType == psrc.CameraType
                && pback.Distortion != null && pback.Distortion.Length == 5
                && pback.Distortion.Select((v, i) => v.Equals(psrc.Distortion![i])).All(x => x),
                pback.Distortion == null ? "(数组丢失)" : string.Join(",", pback.Distortion));

            // 深拷贝：DTO 与契约不能共享同一个数组实例（否则改一边等于改另一边）
            CameraIntrinsicsDto.FromContract(psrc)!.Distortion![0] = 12345.0;
            Check("[IC] 契约：DTO 的系数数组是深拷贝（改 DTO 不影响原契约对象）",
                psrc.Distortion![0].Equals(TrueKappa), $"原值={psrc.Distortion[0]}");

            // ---- 纯加法字段：旧数据读入为 null（历史路径零变化）----
            var legacy = new CalibrationTransform
            {
                Kind = CalibrationKind.NinePoint,
                Matrix = new[] { 0.02, 0, 0, 0.02, 100, 200 },
            };
            Check("[IC] 契约：旧 CalibrationTransform（未设 Intrinsics）为 null —— 历史路径零变化",
                legacy.Intrinsics == null, "");

            // ---- Kind 取值不得撞值（撞了就是静默走错分支）----
            // 取值集从枚举自己取，不再手抄一份：手抄会在加成员时漏掉（本仓已踩过一次——
            // 工具箱分类映射表硬编码 13 项与注册表自比对，恒绿）。这里改成"枚举有几个就查几个"。
            var kindValues = Enum.GetValues<CalibrationKind>();
            var kindInts = Array.ConvertAll(kindValues, v => (int)v);
            Check("[IC] 契约：所有 CalibrationKind 取值两两不重复",
                kindInts.Distinct().Count() == kindInts.Length,
                string.Join("/", kindValues));

            // ---- DTO 是插件命名空间（`.vms` 白名单只放行 Plugin.*）----
            var dtoType = typeof(CameraIntrinsicsDto);
            Check("[IC] 契约：落盘 DTO 在 Plugin.* 命名空间内（.vms $type 白名单要求）",
                dtoType.Namespace?.StartsWith("Plugin.", StringComparison.Ordinal) == true,
                dtoType.FullName ?? "");
        }

        // ─────────────────────────────────────────────────────────────────
        // ② 算子层：合成自验证（真 HALCON，设计稿 §四 a 的判据）
        // ─────────────────────────────────────────────────────────────────
        private static void OperatorLayer()
        {
            if (!TryListBoards(out var boards, out var listError))
            {
                Check("[IC] 算子层：合成自验证（需 HALCONROOT\\calib 标定板描述文件）",
                    true, SkipReason(listError));
                return;
            }

            Check("[IC] 算子层：能找到标定板描述文件（HALCONROOT\\calib）",
                boards.Count > 0, $"{boards.Count} 个候选，例：{string.Join(" / ", boards.Take(3))}");

            // ---- 合成自验证：已知内参 → 合成图 → 落盘 → 完整标定链 → 比对恢复值 ----
            bool ran = IntrinsicsCalibration.TrySelfVerify(out var report, out var verifyError);
            if (!ran || report == null)
            {
                Check("[IC] 算子层：合成自验证能跑通", false, verifyError ?? "未返回结果");
                return;
            }

            Check("[IC] 算子层：合成自验证跑通（12 张合成图全部找到标记）",
                report.ImageCount >= IntrinsicsCalibration.MinImageCount,
                $"找到 {report.ImageCount}/{IntrinsicsCalibration.SelfVerifyImageCount} 张，板={report.Board}");

            // 四条判据（设计稿 §四 a）。实测 0.0018% / 0.0001px / 0.0039px / 0.002%——
            // 判据留了 2~3 个数量级余量，所以红了就一定是真出问题（不是环境抖动）。
            Check("[IC] 算子层：焦距相对误差 < 0.5%（实测 ~0.002%）",
                report.FocusRelErrorPercent < 0.5,
                $"恢复 {report.RecoveredFocus:G6} vs 真值 {report.TrueFocus:G6} → {report.FocusRelErrorPercent:0.####}%");

            Check("[IC] 算子层：主点偏差 < 1px（实测 ~0.0001px）",
                report.CxDeviationPx < 1.0 && report.CyDeviationPx < 1.0,
                $"偏差 ({report.CxDeviationPx:0.####}, {report.CyDeviationPx:0.####})px");

            Check("[IC] 算子层：重投影 RMS < 0.3px（实测 ~0.004px）",
                report.RmsPx < 0.3, $"{report.RmsPx:0.####} px");

            // kappa 口径核对：division 的 kappa 量级是 1e3（不是 polynomial 的 k1 那种量级），
            // 所以判据必须用**相对**误差。设计稿写的 "k1 < 5e-3" 是 polynomial 口径。
            double kappaRelPct = Math.Abs(report.RecoveredKappa - report.TrueKappa)
                                 / Math.Abs(report.TrueKappa) * 100.0;
            Check("[IC] 算子层：kappa 相对误差 < 5%（division 口径；绝对量级 1e3）",
                kappaRelPct < 5.0,
                $"恢复 {report.RecoveredKappa:G6} vs 真值 {report.TrueKappa:G6} → {kappaRelPct:0.####}%");

            Check("[IC] 算子层：四条判据总闸 Passed == true（现场点「合成自验证」看的就是它）",
                report.Passed, report.Text);

            // ---- 失败面：空图列表 / 不存在的板 / 非法像元尺寸 → 全部明确失败 ----
            // ⚠ 每条都要**命中它自己要验的那个分支**：`TryCalibrate` 的守卫是有顺序的
            //（空图 → 空板 → 板文件不存在 → 像元非法），参数配错就会"命中上一个守卫"
            // 从而让断言**因为错误的理由通过**（本仓反复踩过的假绿形态）。
            // 所以下面每条都补一条"文案含它该有的关键词"的判据钉住命中的是哪个分支。
            var emptyImages = IntrinsicsCalibration.TryCalibrate(
                Array.Empty<string>(), report.Board, IntrinsicsCameraModel.Division);
            Check("[IC] 算子层：空图列表明确失败并给下一步（命中「没有可用的标定图」分支）",
                !emptyImages.Succeeded
                && emptyImages.Error?.Contains("没有可用的标定图") == true,
                emptyImages.Error ?? "(空)");

            // 板名给「存在但非标定板」的文件 → 必须命中「板描述文件不存在」分支
            var badBoard = IntrinsicsCalibration.TryCalibrate(
                new[] { "不存在的图.png" }, "不存在的板.descr", IntrinsicsCameraModel.Division);
            Check("[IC] 算子层：不存在的板描述文件明确失败（命中「板描述文件不存在」分支）",
                !badBoard.Succeeded
                && badBoard.Error?.Contains("标定板描述文件不存在") == true
                && !badBoard.Error.Contains("没有可用的标定图"),
                badBoard.Error ?? "(空)");

            // 像元尺寸：给**真板全路径**（否则会先撞「板不存在」），命中「像元尺寸非法」分支
            var allBoards = IntrinsicsCalibration.TryListCaltabDescriptors(out var entries, out _)
                ? entries.Select(e => e.FullPath).ToList()
                : new List<string>();
            var realBoardPath = allBoards.FirstOrDefault(p =>
                string.Equals(Path.GetFileName(p), report.Board, StringComparison.OrdinalIgnoreCase));

            if (realBoardPath != null)
            {
                var badPixel = IntrinsicsCalibration.TryCalibrate(
                    new[] { "不存在的图.png" }, realBoardPath, IntrinsicsCameraModel.Division,
                    pixelSizeMeters: 0.0);
                Check("[IC] 算子层：像元尺寸为 0 明确失败（命中「像元尺寸假设非法」分支，它要进 camPar）",
                    !badPixel.Succeeded
                    && badPixel.Error?.Contains("像元尺寸假设非法") == true,
                    badPixel.Error ?? "(空)");
            }
            else
            {
                Check("[IC] 算子层：像元尺寸为 0 明确失败（命中「像元尺寸假设非法」分支）",
                    false, $"找不到板 {report.Board} 的全路径——本断言无法构造出目标场景");
            }

            // ---- 逐张诊断：找到几张、哪几张没找到、为什么（现场排障的入口）----
            // 同样要给真板全路径，否则连"读图"这一步都到不了，诊断表里就只有板错误。
            var diag = IntrinsicsCalibration.TryCalibrate(
                new[] { "不存在的图.png" }, realBoardPath ?? report.Board, IntrinsicsCameraModel.Division);
            Check("[IC] 算子层：逐张诊断带上失败原因与 HALCON 码（图读不出来也要逐张记）",
                diag.Diagnostics.Count == 1 && !diag.Diagnostics[0].Found
                && !string.IsNullOrWhiteSpace(diag.Diagnostics[0].FailureReason)
                && diag.Diagnostics[0].Path.Contains("不存在的图"),
                diag.Diagnostics.Count > 0
                    ? $"码={diag.Diagnostics[0].HalconCode} 原因={diag.Diagnostics[0].FailureReason}"
                    : "(无诊断)");

            // ---- 初始猜测容差窗口（实测 [1/64, 4] × 真焦距）由算子层的默认猜测承担：
            //      断言只需钉住"默认猜测（不传 initialGuess）就能收敛"——上面 ② 的合成自验证
            //      走的就是这条路（`TryCalibrate(..., initialGuess: null, ...)`）。
            //      这里再钉一条"自验证结论里带着默认猜测的说明"，防止有人把默认猜测删成常量 0。
            Check("[IC] 算子层：自验证报告带默认初始猜测的说明（防默认猜测被删成常量）",
                !string.IsNullOrWhiteSpace(report.Text),
                report.Text.Length > 120 ? report.Text.Substring(0, 120) + "…" : report.Text);
        }

        // ─────────────────────────────────────────────────────────────────
        // ③ 去畸变层：正/反/往返 + 两模型分档 + 中心不动点（真 HALCON）
        // ─────────────────────────────────────────────────────────────────
        private static void DistortionLayer()
        {
            if (!TryListBoards(out _, out var listError))
            {
                Check("[IC] 去畸变层：CameraDistortion 正/反/往返（需 HALCON）",
                    true, SkipReason(listError));
                return;
            }

            // 采样点：四角 + 中心 + 两个边中点（中心位移恒 0，是"模型正确"的天然自证）
            var rows = new[] { 0.0, 0.0, TrueH - 1.0, TrueH - 1.0, TrueH / 2.0, 0.0, TrueH / 2.0 };
            var cols = new[] { 0.0, TrueW - 1.0, 0.0, TrueW - 1.0, TrueW / 2.0, TrueW / 2.0, 0.0 };

            // ---- division ----
            var div = DivIntrinsics();
            bool okU = CameraDistortion.TryUndistortPoints(div, rows, cols, out var ur, out var uc, out var uErr);
            Check("[IC] 去畸变：division 去畸变点集成功", okU, uErr ?? "");

            if (okU)
            {
                double centerShift = Math.Sqrt(Math.Pow(ur[4] - rows[4], 2) + Math.Pow(uc[4] - cols[4], 2));
                Check("[IC] 去畸变：中心点位移恒 0（主点是畸变不动点——模型正确的天然自证）",
                    centerShift < 1e-9, $"{centerShift:E3} px");

                double cornerShift = Math.Sqrt(Math.Pow(ur[0] - rows[0], 2) + Math.Pow(uc[0] - cols[0], 2));
                Check("[IC] 去畸变：角点位移量级 10~40px（kappa=-1500、1280×1024 实测 ~23px）",
                    cornerShift > 10 && cornerShift < 40, $"{cornerShift:0.###} px");

                double[] cornerShifts = new[] { 0, 1, 2, 3 }.Select(i =>
                    Math.Sqrt(Math.Pow(ur[i] - rows[i], 2) + Math.Pow(uc[i] - cols[i], 2))).ToArray();
                Check("[IC] 去畸变：四角位移径向对称（极差 < 1px）",
                    cornerShifts.Max() - cornerShifts.Min() < 1.0,
                    string.Join(", ", cornerShifts.Select(v => v.ToString("0.###"))));

                bool okRT = CameraDistortion.TryDistortPoints(div, ur, uc, out var br, out var bc, out var rtErr);
                double maxRT = 0;
                for (int i = 0; i < rows.Length; i++)
                    maxRT = Math.Max(maxRT, Math.Sqrt(Math.Pow(br[i] - rows[i], 2) + Math.Pow(bc[i] - cols[i], 2)));

                Check("[IC] 去畸变：division 往返（去畸变→加畸变）误差 < 1e-6px（可解析求逆，实测 ~2e-13）",
                    okRT && maxRT < 1e-6, okRT ? $"{maxRT:E4} px" : (rtErr ?? "往返失败"));
            }

            // ---- polynomial：camPar 能构造（系数个数必须按模型给）+ 往返按更宽的档 ----
            var poly = PolyIntrinsics();
            bool polyParOk = CameraDistortion.TryBuildUndistortedCamPar(poly, out _, out var polyParErr);
            Check("[IC] 去畸变：polynomial 去畸变 camPar 能构造（系数个数按模型给：5 元）",
                polyParOk, polyParErr ?? "");

            if (polyParOk)
            {
                bool okPU = CameraDistortion.TryUndistortPoints(poly, rows, cols, out var pur, out var puc, out var puErr);
                Check("[IC] 去畸变：polynomial 去畸变点集成功", okPU, puErr ?? "");

                if (okPU)
                {
                    double pCenter = Math.Sqrt(Math.Pow(pur[4] - rows[4], 2) + Math.Pow(puc[4] - cols[4], 2));
                    Check("[IC] 去畸变：polynomial 中心点位移也恒 0",
                        pCenter < 1e-9, $"{pCenter:E3} px");

                    bool okPRT = CameraDistortion.TryDistortPoints(poly, pur, puc, out var pbr, out var pbc, out var prtErr);
                    double maxPRT = 0;
                    for (int i = 0; i < rows.Length; i++)
                        maxPRT = Math.Max(maxPRT, Math.Sqrt(Math.Pow(pbr[i] - rows[i], 2) + Math.Pow(pbc[i] - cols[i], 2)));

                    // 实测最坏 1.1e-4px（迭代反解）——所以往返容差必须按模型分档，
                    // 拿 division 的 1e-6 去判 polynomial 会**假红 100 倍以上**。
                    Check("[IC] 去畸变：polynomial 往返 < 1e-3px（迭代反解；用 1e-6 判会假红）",
                        okPRT && maxPRT < 1e-3,
                        okPRT ? $"{maxPRT:E4} px" : (prtErr ?? "往返失败"));
                }
            }

            // ---- 往返容差按模型分档（消费端回显自校验用的就是它）----
            double divTol = PoseTransformIntrinsics.RoundTripTolerancePx(div);
            double polyTol = PoseTransformIntrinsics.RoundTripTolerancePx(poly);
            Check("[IC] 去畸变：往返容差按模型分档（division 1e-6 / polynomial 1e-3 / null 1e-6）",
                Math.Abs(divTol - 1e-6) < double.Epsilon
                && Math.Abs(polyTol - 1e-3) < double.Epsilon
                && Math.Abs(PoseTransformIntrinsics.RoundTripTolerancePx(null) - 1e-6) < double.Epsilon,
                $"division={divTol:G3}, polynomial={polyTol:G3}");

            // ---- 不良构内参：去畸变必须明确失败，不许静默出数 ----
            var broken = PolyIntrinsics();
            broken.Distortion = new[] { 1.0 };
            bool brokenRejected = !CameraDistortion.TryUndistortPoints(
                broken, new[] { 100.0 }, new[] { 100.0 }, out _, out _, out var brokenErr);
            Check("[IC] 去畸变：不良构内参明确失败（不静默出数）",
                brokenRejected && !string.IsNullOrWhiteSpace(brokenErr), brokenErr ?? "(空)");

            // ---- 空点集 / 长度不一致：明确失败（不许让 HALCON 抛出去）----
            bool emptyRejected = !CameraDistortion.TryUndistortPoints(
                div, Array.Empty<double>(), Array.Empty<double>(), out _, out _, out var emptyErr);
            Check("[IC] 去畸变：空点集明确失败",
                emptyRejected && !string.IsNullOrWhiteSpace(emptyErr), emptyErr ?? "(空)");

            bool lenRejected = !CameraDistortion.TryUndistortPoints(
                div, new[] { 1.0, 2.0 }, new[] { 1.0 }, out _, out _, out var lenErr);
            Check("[IC] 去畸变：行列数组长度不一致明确失败",
                lenRejected && !string.IsNullOrWhiteSpace(lenErr), lenErr ?? "(空)");
        }

        // ─────────────────────────────────────────────────────────────────
        // ④ 串联层：标定产 → 坐标变换消费
        // ─────────────────────────────────────────────────────────────────
        private static void EndToEndLayer()
        {
            // 一个在"去畸变域"成立的已知仿射：机械 = 0.02·u + 100 / 0.02·v + 200
            var knownAffine = new[] { 0.02, 0.0, 0.0, 0.02, 100.0, 200.0 };

            // ---- 4.1 消费端四类失配（纯托管）----

            // (a) 非 Undistorted 的 Kind 带内参 = 数据自相矛盾 → 明确失败
            var contradictory = new CalibrationTransform
            {
                Kind = CalibrationKind.NinePoint,
                Matrix = knownAffine,
                Intrinsics = DivIntrinsics(),
            };
            Check("[IC] 串联：非 Undistorted 的 Kind 带内参 → 明确失败（数据自相矛盾）",
                !PoseTransformMath.CheckCalibrationUsable(contradictory, out var contraErr),
                contraErr ?? "(空)");

            // (b) Undistorted 但缺内参 → 明确失败
            var missing = new CalibrationTransform
            {
                Kind = CalibrationKind.NinePointUndistorted,
                Matrix = knownAffine,
                Intrinsics = null,
            };
            Check("[IC] 串联：Undistorted 的 Kind 缺内参 → 明确失败",
                !PoseTransformMath.CheckCalibrationUsable(missing, out var missingErr),
                missingErr ?? "(空)");

            // (c) 良构对照 —— 必须放行（否则上面两条可能是"永远失败"的假绿）
            var good = new CalibrationTransform
            {
                Kind = CalibrationKind.NinePointUndistorted,
                Matrix = knownAffine,
                SourceImageWidth = TrueW,
                SourceImageHeight = TrueH,
                CameraSerial = "SYNTH-01",
                Intrinsics = DivIntrinsics(),
            };
            Check("[IC] 串联：良构的 NinePointUndistorted 放行（对照，防上面两条假绿）",
                PoseTransformMath.CheckCalibrationUsable(good, out var goodErr), goodErr ?? "");

            // (d) 变换图尺寸 ≠ 内参图尺寸 → 失败（畸变系数是像素帧相关量，跨帧会静默偏掉）
            var sizeMismatch = new CalibrationTransform
            {
                Kind = CalibrationKind.NinePointUndistorted,
                Matrix = knownAffine,
                SourceImageWidth = 640,
                SourceImageHeight = 480,
                Intrinsics = DivIntrinsics(),   // 1280×1024
            };
            Check("[IC] 串联：变换图尺寸 ≠ 内参图尺寸 → 明确失败（文案给出两个尺寸）",
                !PoseTransformMath.CheckCalibrationUsable(sizeMismatch, out var sizeErr)
                && sizeErr?.Contains("640") == true && sizeErr.Contains("1280") == true,
                sizeErr ?? "(空)");

            // (e) 相机身份不一致 → 失败
            var serialMismatch = new CalibrationTransform
            {
                Kind = CalibrationKind.NinePointUndistorted,
                Matrix = knownAffine,
                SourceImageWidth = TrueW,
                SourceImageHeight = TrueH,
                CameraSerial = "CAM-A",
                Intrinsics = DivIntrinsics(),   // SYNTH-01
            };
            Check("[IC] 串联：变换相机序列号 ≠ 内参相机序列号 → 明确失败",
                !PoseTransformMath.CheckCalibrationUsable(serialMismatch, out var serialErr),
                serialErr ?? "(空)");

            // (f) 只有一边记了尺寸/序列号 → 放行（不给旧数据制造误报）
            var oneSided = new CalibrationTransform
            {
                Kind = CalibrationKind.NinePointUndistorted,
                Matrix = knownAffine,
                SourceImageWidth = 0,
                SourceImageHeight = 0,
                CameraSerial = "",
                Intrinsics = DivIntrinsics(),
            };
            Check("[IC] 串联：只有一边记了尺寸/序列号 → 放行（不给旧数据制造误报）",
                PoseTransformMath.CheckCalibrationUsable(oneSided, out var oneErr), oneErr ?? "");

            // (g) internal 的 Intrinsics 不良构 → 失败
            var brokenIntrinsics = new CalibrationTransform
            {
                Kind = CalibrationKind.NinePointUndistorted,
                Matrix = knownAffine,
                Intrinsics = new CameraIntrinsics { CameraType = "" },
            };
            Check("[IC] 串联：内参自身不良构 → 明确失败（复用契约同一份判据）",
                !PoseTransformMath.CheckCalibrationUsable(brokenIntrinsics, out var biErr), biErr ?? "");

            // ---- 4.2 三个 *Undistorted 必须都被判为"去畸变类"（单一真源）----
            var undistortedKinds = new[]
            {
                CalibrationKind.NinePointUndistorted,
                CalibrationKind.PerspectiveUndistorted,
                CalibrationKind.MeshUndistorted,
            };
            Check("[IC] 串联：三个 *Undistorted Kind 都被 IsUndistortedKind 认出（且非 Undistorted 不被认）",
                undistortedKinds.All(PoseTransformMath.IsUndistortedKind)
                && !PoseTransformMath.IsUndistortedKind(CalibrationKind.NinePoint)
                && !PoseTransformMath.IsUndistortedKind(CalibrationKind.Perspective)
                && !PoseTransformMath.IsUndistortedKind(CalibrationKind.Mesh)
                && !PoseTransformMath.IsUndistortedKind(CalibrationKind.PixelScale),
                string.Join("/", undistortedKinds.Select(k => ((int)k).ToString())));

            // ---- 4.3 数学层：*Undistorted 与非 Undistorted 走同一条分支（矩阵语义一致）----
            var plain = new CalibrationTransform { Kind = CalibrationKind.NinePoint, Matrix = knownAffine };
            var und = new CalibrationTransform
            {
                Kind = CalibrationKind.NinePointUndistorted,
                Matrix = knownAffine,
                Intrinsics = DivIntrinsics(),
            };
            bool plainOk = PoseTransformMath.TryMapPixelToMechanical(plain, 300, 400, out var px, out var py, out _);
            bool undOk = PoseTransformMath.TryMapPixelToMechanical(und, 300, 400, out var ux, out var uy, out _);
            Check("[IC] 串联：数学层把 *Undistorted 当同一套矩阵映射（同一输入 → 同一输出）",
                plainOk && undOk && px.Equals(ux) && py.Equals(uy),
                $"plain=({px:0.####},{py:0.####}) und=({ux:0.####},{uy:0.####})");

            // ---- 4.5 插件级端到端（真 RunAlgorithm）：开关真的产出了 *Undistorted ----
            // 上面 4.4 验的是"去畸变这个概念正确"；这一节验的是"**插件真的用了它**"。
            // 两者缺一不可：只验概念 → 插件忘了接去畸变也全绿（本仓最怕的假绿形态）。
            PluginLevelSwitch();

            // ---- 4.3 端到端（真 HALCON）：去畸变真的改变了结果 ----
            if (!TryListBoards(out _, out var listError))
            {
                Check("[IC] 串联：去畸变端到端（标定产 → 变换消费）",
                    true, SkipReason(listError));
                return;
            }

            var intrinsics = DivIntrinsics();

            // 靠近上边中部的测试点（位移大，最能暴露"漏了去畸变"）
            const double testRow = 60.0, testCol = 1200.0;

            bool uOk = CameraDistortion.TryUndistortPoint(
                intrinsics, testRow, testCol, out double uRow, out double uCol, out var uErr2);
            Check("[IC] 串联：能对测试点去畸变", uOk, uErr2 ?? "");
            if (!uOk) return;

            double expectedX = knownAffine[0] * uRow + knownAffine[1] * uCol + knownAffine[4];
            double expectedY = knownAffine[2] * uRow + knownAffine[3] * uCol + knownAffine[5];

            double shift = Math.Sqrt(Math.Pow(uRow - testRow, 2) + Math.Pow(uCol - testCol, 2));
            Check("[IC] 串联：测试点去畸变位移 > 1px（边缘点；这是「忘了去畸变」会有的误差量级）",
                shift > 1.0, $"{shift:0.###} px");

            // (b) 反证：跳过用去畸变像素 → 机械坐标必须明显不同
            double naiveX = knownAffine[0] * testRow + knownAffine[1] * testCol + knownAffine[4];
            double naiveY = knownAffine[2] * testRow + knownAffine[3] * testCol + knownAffine[5];
            double diffMm = Math.Sqrt(Math.Pow(naiveX - expectedX, 2) + Math.Pow(naiveY - expectedY, 2));

            Check("[IC] 串联【反证】：跳过用去畸变像素 → 机械坐标偏差必须明显（≈ 位移 × 当量）",
                diffMm > 0.01,
                $"正解 ({expectedX:0.####}, {expectedY:0.####}) vs 漏去畸变 ({naiveX:0.####}, {naiveY:0.####}) → 差 {diffMm:0.####} mm");

            // (c) 往返：数学层反算去畸变域像素 → 消费端加畸变 → 回到原始像素
            bool backOk = PoseTransformMath.TryMapMechanicalToPixel(
                und, expectedX, expectedY, out double backURow, out double backUCol, out var backErr2);
            Check("[IC] 串联：数学层从机械坐标反算得去畸变域像素", backOk, backErr2 ?? "");

            if (backOk)
            {
                bool distOk = CameraDistortion.TryDistortPoint(
                    intrinsics, backURow, backUCol, out double realRow, out double realCol, out var dErr2);
                double backDiff = Math.Sqrt(Math.Pow(realRow - testRow, 2) + Math.Pow(realCol - testCol, 2));
                Check("[IC] 串联：正 → 反 → 加畸变 回到原始像素（division < 1e-6px）",
                    distOk && backDiff < 1e-6,
                    distOk ? $"{backDiff:E4} px" : (dErr2 ?? "加畸变失败"));
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // 夹具
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// 本机是否有 HALCON 标定板描述文件（合成自验证与去畸变算子的硬前提）。
        /// </summary>
        private static bool TryListBoards(out List<string> boards, out string? error)
        {
            boards = new List<string>();
            error = null;

            if (!IntrinsicsCalibration.TryListCaltabDescriptors(out var entries, out error))
                return false;

            boards = entries.Select(e => e.FileName).ToList();
            if (boards.Count == 0)
            {
                error = "HALCONROOT\\calib 下没有任何标定板描述文件（.descr/.cpd）";
                return false;
            }
            return true;
        }

        /// <summary>
        /// 插件级端到端：真跑 <c>RunAlgorithm</c>，验"取点前先去畸变"开关真的改变了产出。
        ///
        /// 判据口径（四态，缺一不可）
        /// ---------
        /// ① 开关**关**：`Kind` = 普通九点、`Intrinsics` = null（历史行为，回归）；
        /// ② 开关**开**：`Kind` = NinePointUndistorted、带内参、且**矩阵与 ① 不同**
        ///    （畸变被吸收 → 拟合出的仿射本来就不一样，这是"开关真的生效"的硬证据）；
        /// ③ 开关开但**内参缺失** → 明确失败（绝不静默退回"不去畸变"）；
        /// ④ 开关开但**内参图尺寸与标定图不符** → 明确失败（畸变系数是像素帧相关的量）。
        ///
        /// 为什么矩阵必须不同：如果实现只改了 Kind 而没真去畸变，①②两条仍会"通过"——
        /// ② 的"矩阵不同"正是唯一能拆穿它的判据。
        /// </summary>
        private static void PluginLevelSwitch()
        {
            // 造一组**带畸变**的九点表：机械坐标由"去畸变域仿射"定义，表里存的是**原始（畸变）像素**。
            // 于是"去畸变后求解"能精确还原该仿射，而"不去畸变"只能拟合出一个被畸变污染的矩阵。
            var affine = new[] { 0.02, 0.0, 0.0, 0.02, 100.0, 200.0 };   // 机械 = 0.02·u + 100 / 0.02·v + 200

            // 3×3 覆盖视野的原始像素点（故意取到边缘区，那里畸变位移大）
            var rawPts = new (double Row, double Col)[]
            {
                (60, 120), (60, 640), (60, 1160),
                (512, 120), (512, 640), (512, 1160),
                (960, 120), (960, 640), (960, 1160),
            };

            var intrinsics = DivIntrinsics();
            var rows = rawPts.Select(p => p.Row).ToArray();
            var cols = rawPts.Select(p => p.Col).ToArray();

            // 机械坐标 = 去畸变域仿射作用在"去畸变后的像素"上
            bool uOk = CameraDistortion.TryUndistortPoints(
                intrinsics, rows, cols, out var uRows, out var uCols, out var uErr);
            if (!uOk)
            {
                Check("[IC] 插件级：开关四态（构造去畸变点集失败）", false, uErr ?? "");
                return;
            }

            var xs = uRows.Select((r, i) => affine[0] * r + affine[1] * uCols[i] + affine[4]).ToArray();
            var ys = uRows.Select((r, i) => affine[2] * r + affine[3] * uCols[i] + affine[5]).ToArray();

            // ---- ① 开关关：畸变**没被吸收** → 残差超阈值 → 业务失败（这是插件的正确行为）----
            // 口径注意：这张表存的是**畸变像素**，畸变本就该表现为大残差。
            // 所以"开关关"的正解不是"照样成功"，而是**失败并报残差超阈**——
            // 这正是「超阈值 = 业务失败，绝不静默放行」那条设计原则的具体体现。
            // 要拿到"不去畸变"的矩阵当基线，必须先显式放宽阈值（见 ①b）。
            var offGate = BuildNinePoint(rawPts, xs, ys);
            Calibrate(offGate, "标定_开关关_默认阈值");
            Check("[IC] 插件级①：开关关 → 畸变未吸收、残差超阈值 → 业务失败（不静默放行）",
                offGate.Success.Value is false
                && (offGate.ErrorMessage.Value?.ToString() ?? "").Contains("残差", StringComparison.Ordinal),
                $"Success={offGate.Success.Value} Err={offGate.ErrorMessage.Value}");

            // 放宽阈值让同一份数据成功，从而拿到"不去畸变"的矩阵当基线
            var offPlugin = BuildNinePoint(rawPts, xs, ys);
            offPlugin.ResidualThresholdPx = 100.0;   // 只为取样，不代表标定合格
            Calibrate(offPlugin, "标定_开关关_放宽阈值");
            var offTransform = offPlugin.Transform.TypedValue as CalibrationTransform;

            Check("[IC] 插件级①b：放宽阈值后开关关 → Kind=NinePoint、Intrinsics=null（历史行为逐字不变）",
                offPlugin.Success.Value is true
                && offTransform?.Kind == CalibrationKind.NinePoint
                && offTransform.Intrinsics == null,
                $"Success={offPlugin.Success.Value} Kind={offTransform?.Kind} "
                + $"Intrinsics={(offTransform?.Intrinsics == null ? "null" : "非null")} "
                + $"RMS={offTransform?.ResidualRmsPx:0.###}px");

            // ---- ② 开关开：Kind 变、内参挂上、矩阵必须不同 ----
            var onPlugin = BuildNinePoint(rawPts, xs, ys);
            onPlugin.Intrinsics = CameraIntrinsicsDto.FromContract(intrinsics);
            onPlugin.UndistortBeforeCalibration = true;
            Calibrate(onPlugin, "标定_开关开");
            var onTransform = onPlugin.Transform.TypedValue as CalibrationTransform;

            bool kindChanged = onTransform?.Kind == CalibrationKind.NinePointUndistorted;
            bool carriedIntrinsics = onTransform?.Intrinsics != null
                && onTransform.Intrinsics.CameraType == CameraIntrinsics.DivisionType;

            Check("[IC] 插件级②a：开关开 → Kind=NinePointUndistorted 且带上内参（默认阈值下即成功）",
                onPlugin.Success.Value is true && kindChanged && carriedIntrinsics,
                $"Success={onPlugin.Success.Value} Kind={onTransform?.Kind} "
                + $"内参={(onTransform?.Intrinsics == null ? "null" : onTransform.Intrinsics.CameraType)} "
                + $"Err={onPlugin.ErrorMessage.Value}");

            // 矩阵必须与"开关关"不同 —— 这是"去畸变真的生效"的唯一硬证据
            bool matrixDiffers = false;
            double maxElemDiff = 0;
            if (offTransform?.Matrix != null && onTransform?.Matrix != null)
            {
                for (int i = 0; i < 6; i++)
                    maxElemDiff = Math.Max(maxElemDiff, Math.Abs(offTransform.Matrix[i] - onTransform.Matrix[i]));
                matrixDiffers = maxElemDiff > 1e-6;
            }
            Check("[IC] 插件级②b【反证】：开关开 vs 关 的矩阵必须不同（只改 Kind 不真去畸变会被这条拆穿）",
                matrixDiffers,
                $"最大元素差 {maxElemDiff:E3}（开关关 M=[{string.Join(",", (offTransform?.Matrix ?? new double[6]).Select(v => v.ToString("0.######")))}]"
                + $" 开关开 M=[{string.Join(",", (onTransform?.Matrix ?? new double[6]).Select(v => v.ToString("0.######")))}]）");

            // 开关开时残差应当接近 0；同时要求"开关关的残差明显更大"——
            // 两条一起才说明"是去畸变把残差压下去的"，而不是数据本来就干净。
            Check("[IC] 插件级②c：开关开 → 残差 ≈ 0 且比开关关小两个数量级以上（畸变被模型吸收）",
                onTransform != null && offTransform != null
                && onTransform.ResidualRmsPx < 0.01
                && offTransform.ResidualRmsPx > onTransform.ResidualRmsPx * 100,
                $"开关开 RMS={onTransform?.ResidualRmsPx:E3}px vs 开关关 RMS={offTransform?.ResidualRmsPx:0.###}px");

            // ---- ②d 矩阵还原精度：开关开时应当精确还原"去畸变域仿射" ----
            double maxMatrixErr = double.MaxValue;
            if (onTransform?.Matrix != null)
            {
                maxMatrixErr = 0;
                for (int i = 0; i < 6; i++)
                    maxMatrixErr = Math.Max(maxMatrixErr, Math.Abs(onTransform.Matrix[i] - affine[i]));
            }
            Check("[IC] 插件级②d：开关开 → 矩阵精确还原去畸变域仿射（偏差 < 1e-6）",
                maxMatrixErr < 1e-6, $"最大偏差 {maxMatrixErr:E3}");

            // ---- ③ 开关开但内参缺失 → 明确失败 ----
            var noIntrinsics = BuildNinePoint(rawPts, xs, ys);
            noIntrinsics.UndistortBeforeCalibration = true;   // 不给 Intrinsics
            Calibrate(noIntrinsics, "标定_开关开无内参");
            Check("[IC] 插件级③：开关开但内参缺失 → 明确失败（不静默退回不去畸变）",
                noIntrinsics.Success.Value is false
                && (noIntrinsics.ErrorMessage.Value?.ToString() ?? "").Contains("内参", StringComparison.Ordinal),
                $"Success={noIntrinsics.Success.Value} Err={noIntrinsics.ErrorMessage.Value}");

            // ---- ④ 开关开但内参图尺寸 ≠ 标定图尺寸 → 明确失败 ----
            var sizeOff = BuildNinePoint(rawPts, xs, ys);
            var wrongSize = DivIntrinsics();
            wrongSize.ImageWidth = 640;
            wrongSize.ImageHeight = 480;
            sizeOff.Intrinsics = CameraIntrinsicsDto.FromContract(wrongSize);
            sizeOff.SourceImageWidth = TrueW;      // 标定图是 1280×1024
            sizeOff.SourceImageHeight = TrueH;
            sizeOff.UndistortBeforeCalibration = true;
            Calibrate(sizeOff, "标定_开关开尺寸不符");
            Check("[IC] 插件级④：开关开但内参图尺寸 ≠ 标定图尺寸 → 明确失败",
                sizeOff.Success.Value is false
                && (sizeOff.ErrorMessage.Value?.ToString() ?? "").Contains("尺寸", StringComparison.Ordinal),
                $"Success={sizeOff.Success.Value} Err={sizeOff.ErrorMessage.Value}");
        }

        /// <summary>造一个九点模式的标定插件实例（与 CalibrationChecks.BuildPlugin 同形）</summary>
        private static CalibrationPlugin BuildNinePoint(
            (double Row, double Col)[] imgPts, double[] xs, double[] ys)
        {
            var plugin = new CalibrationPlugin
            {
                InstanceName = "标定_内参断言",
                Mode = CalibrationMode.NinePoint,
                SourceImageWidth = TrueW,
                SourceImageHeight = TrueH,
            };

            plugin.PointRows.Clear();
            for (int i = 0; i < imgPts.Length; i++)
            {
                plugin.PointRows.Add(new CalibPointRow
                {
                    Name = "P" + (i + 1),
                    MachineX = xs[i],
                    MachineY = ys[i],
                    ImageRow = imgPts[i].Row,
                    ImageCol = imgPts[i].Col,
                });
            }
            return plugin;
        }

        /// <summary>跑一次 RunAlgorithm（真 ExecutionContext，与 CalibrationChecks 同一套桩），跑完即释放</summary>
        private static void Calibrate(CalibrationPlugin plugin, string instanceName)
        {
            var ctx = new ExecutionContext(
                new StubLog(), new FlowSession { FlowName = "内参断言" },
                new WorkspaceContext(), new CancellationTokenSource().Token);
            try
            {
                plugin.InstanceName = instanceName;
                plugin.RunAlgorithm(ctx);
            }
            finally
            {
                plugin.Dispose();
            }
        }

        /// <summary>跳过理由（R38：跳过必须能被反证，要写清"谁证明了是本环境不支持"）。</summary>
        private static string SkipReason(string? error)
            => $"跳过：{error ?? "本机无标定板描述文件"}。反证依据：sim_caltab 与 find_calib_object "
             + "都要求标定板描述文件（HALCONROOT\\calib\\*.descr|*.cpd），无板则整条链无从执行——"
             + "这是环境缺件，不是代码缺陷。装了完整 HALCON 后本段会自动生效。";
    }
}
