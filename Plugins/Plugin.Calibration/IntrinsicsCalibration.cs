using Core.Interfaces;
using HalconDotNet;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Plugin.Calibration
{
    /// <summary>
    /// 标定板**内参标定**的算子层（二期③，纯 HALCON 封装：不碰端口、不碰 UI、不持有配置）。
    ///
    /// 职责边界
    /// ---------
    /// 本文件只回答三个问题：
    /// ① <see cref="TryCalibrate"/>：一批标定板照片 → 相机内参（<see cref="CameraIntrinsics"/>）+ 每张图的诊断；
    /// ② <see cref="TrySelfVerify"/>：合成已知内参的标定板图 → 走完整标定 → 与真值比对（**现场自证环境可用**的入口）；
    /// ③ <see cref="TryListCaltabDescriptors"/>：扫 HALCON 的 `calib\` 目录给界面做候选。
    /// 「什么时候调、调完怎么存、开关打开时怎么用」全部由 <see cref="CalibrationPlugin"/> 决定。
    ///
    /// 为什么单独一层（与 <c>CalibrationMath</c> 同一纪律）
    /// ---------
    /// 断言与探针要能**直接调这一层**验精度（合成自验证），不需要构造插件、不需要 UI。
    /// 插件侧只剩"取参数 → 调这一层 → 落 DTO → 刷新界面"。
    ///
    /// 实测配方与陷阱（2026-10-16 本机 HALCON 23.05 实测，**不要凭直觉改**）
    /// ---------
    /// · **板文件必须与灰度极性配对**，配错必失败且码不同（下面的常量表与
    ///   <see cref="DescribeFindCalibObjectFailure"/> 都按这个写）：
    ///   - `calplate_*.cpd`（新式板，文件里 `p light`）→ **标记亮、板暗**：bg=128, plate=80, marks=224；
    ///   - `caltab_*.descr`（旧式板，文件里没有 `p` 行）→ **标记暗、板亮**：bg=128, plate=200, marks=20。
    ///   实测：cpd 板配旧式极性 → **#8397**（分割失败）；descr 板配新式极性 → **#8431**（定不出朝向）。
    /// · **初始猜测的容差窗口实测为 [1/64, 4] × 真焦距**（12 张倾斜位姿、calplate_160mm @1.2m）：
    ///   窗口内恢复精度与给真值完全一样（0.003%）；给到 8× 就收敛到错误的焦距（误差 686%）。
    ///   所以默认猜测不能"随便给个 8mm"，必须按图幅与板宽估（见 <see cref="TryDefaultStartCamPar"/>）。
    /// · **calibrate_cameras 把 sx/sy 当已知量固定**（不是自由参数）：start 给 3.45µm 就回收 3.45µm，
    ///   焦距按 5.2/3.45 等比放大、kappa 按平方反比——即单相机下 focus 与 sx 只以**比值**出现，
    ///   物理上不可分。**但去畸变结果对这套重参数化是精确不变的**（实测 sx 差 2×、8× 时逐点最大差
    ///   **0 px**），所以"像元尺寸"这个猜测值只决定 focus/kappa 的报告单位，不影响去畸变映射。
    /// · 图数下限：HALCON 的硬下限是 2（不足报 **#8498** No observation data），
    ///   本层按设计稿取更严的 <see cref="MinImageCount"/> = 6（见常量注释）。
    ///
    /// HALCON 资源所有权（**已核对，不是猜的**）
    /// ---------
    /// 用反射核对本机 `DLL\Halcon\halcondotnet.dll`：`HalconDotNet.HCalibData` 的基类是
    /// `HalconDotNet.HHandle`，接口表里有 **`System.IDisposable`**，`Dispose()` 定义在 `HHandleBase`——
    /// 所以 `using` 是正确写法（本文件所有 HCalibData 都走 `using`）。
    /// 另有 `ClearCalibData()`（清空数据但保留句柄）——本层每轮都新建句柄，不需要它。
    /// 300 轮"建 → find_calib_object → 释放"的私有内存增量实测 **-7.2MB**（GC 后净降），无泄漏迹象。
    /// </summary>
    public static class IntrinsicsCalibration
    {
        /// <summary>
        /// 参与标定的**最少图数**。
        ///
        /// 为什么是 6 而不是 HALCON 的硬下限 2：division 模型（焦距 + kappa + 主点 2 个）已经是
        /// 4 个自由度、polynomial 是 7 个；2 张图**在数学上能出解但没有任何冗余**，噪声会直接进参数
        /// （现场表现为"重投影 RMS 很小但测量不对"这种最危险的失败模式）。设计稿 §四 c 把"&lt; 6 明确失败"
        /// 列为验收条件，本层照更严的 6 执行；界面文案建议 12 张以上（实测 12 张倾斜位姿可到 0.002% 焦距误差）。
        /// </summary>
        public const int MinImageCount = 6;

        /// <summary>
        /// 默认猜测的阶梯倍数。
        ///
        /// **实测容差窗口 = [1/64, 4] × 真焦距**（12 张倾斜位姿、calplate_160mm @1.2m，逐个 factor 试）：
        /// 窗口内恢复精度与直接给真值**完全一样**（0.003%）；给到 8× 就收敛到错误的焦距（误差 686%）。
        /// 这一串取值都落在窗口内（×1 是主档；其余是"主档偏了"时的保险）。
        ///
        /// ⚠ **实测中没有任何一档被触发过**（9 个"板 × 距离"组合全部主档即 12/12 全找到，
        /// 见 <see cref="TryDefaultStartCamPar"/> 的注释）——它的价值是"默认公式万一偏出窗口时不至于直接失败"。
        /// 所以它**不是"必须靠它才能跑通"的机制**，别在文档里写成"靠阶梯兜底才通"。
        /// </summary>
        private static readonly double[] GuessLadder = { 1.0, 0.5, 2.0, 0.25, 4.0 };

        /// <summary>
        /// 默认像元尺寸（米）：5.2µm。工业相机最常见的像元尺寸之一（如 IMX264 系列）。
        /// **它只决定 focus / kappa 的报告单位**——去畸变映射对"整组等比重参数化"精确不变（类注释有实测），
        /// 所以现场不知道真实像元尺寸时用默认值不影响去畸变正确性；知道就填（填了 focus 读数才与镜头标称焦距可比）。
        /// </summary>
        public const double DefaultPixelSizeMeters = 5.2e-6;

        #region 自验证配方（实测常量表）

        /// <summary>
        /// 合成自验证的配方：**板文件 / 灰度 / 距离必须按这套配**（配错的后果写在
        /// <see cref="DescribeFindCalibObjectFailure"/> 的 #8431 / #8397 两条里）。
        ///
        /// 位姿构造（<c>sim_caltab</c>）：<c>new HPose(tx, ty, tz, rxDeg, ryDeg, rzDeg, "Rp+T", "gba", "point")</c>
        /// ——三个字符串写错会得到 **#1307 / #1203**（不是"参数无效"这种含糊报错），**旋转单位是度**。
        /// </summary>
        public sealed class SelfVerifyRecipe
        {
            /// <summary>板描述文件名（在 `%HALCONROOT%\calib\` 下）</summary>
            public string Board { get; init; } = "";

            /// <summary>背景灰度（合成图）</summary>
            public int GrayBackground { get; init; } = 128;

            /// <summary>板面灰度（合成图）</summary>
            public int GrayPlate { get; init; } = 80;

            /// <summary>标记灰度（合成图）</summary>
            public int GrayMarks { get; init; } = 224;

            /// <summary>基准工作距离（米）；实际 12 张会在 ±6% 内浮动</summary>
            public double Tz { get; init; } = 1.2;

            /// <summary>极性一句话（上屏/日志用）</summary>
            public string PolarityNote { get; init; } = "";
        }

        /// <summary>
        /// 候选配方（**按实测精度从高到低**）。第一条是默认配方。
        /// 精度数据来自 2026-10-16 的可行性探针（1280×1024、真值 focus=0.052、kappa=-1500、主点 (640,512)、12 张倾斜位姿）：
        ///   ① calplate_320mm.cpd @1.2 → focus 相对误差 0.0018%、主点 0.0001px、RMS 0.0039px（**默认**）
        ///   ② calplate_160mm.cpd @1.2 → 0.0029% / 0.0003px / 0.0056px
        ///   ③ calplate_80mm.cpd  @0.8 → 0.003%  / 0.0016px / 0.0062px
        ///   ④ calplate_40mm.cpd  @0.5 → 0.0038% / 0.0026px / 0.0068px（**只在 tz≈0.5 通**，远了必失败）
        ///   ⑤ caltab_100mm.descr @1.8 → 0.009%  / 0.0065px / 0.0032px
        ///   ⑥ caltab_200mm.descr @2.5 → 0.0052% / 0.0018px / 0.0025px
        ///   ⑦ caltab_30mm.descr  @1.2 → 0.1025% / 0.0149px / 0.0054px（板小，精度最差，仅作对照）
        /// </summary>
        public static IReadOnlyList<SelfVerifyRecipe> SelfVerifyRecipes { get; } = new[]
        {
            new SelfVerifyRecipe { Board = "calplate_320mm.cpd", GrayBackground = 128, GrayPlate = 80,  GrayMarks = 224, Tz = 1.2,
                PolarityNote = "新式板：标记亮 / 板面暗" },
            new SelfVerifyRecipe { Board = "calplate_160mm.cpd", GrayBackground = 128, GrayPlate = 80,  GrayMarks = 224, Tz = 1.2,
                PolarityNote = "新式板：标记亮 / 板面暗" },
            new SelfVerifyRecipe { Board = "calplate_80mm.cpd",  GrayBackground = 128, GrayPlate = 80,  GrayMarks = 224, Tz = 0.8,
                PolarityNote = "新式板：标记亮 / 板面暗（只在近距离通）" },
            new SelfVerifyRecipe { Board = "calplate_40mm.cpd",  GrayBackground = 128, GrayPlate = 80,  GrayMarks = 224, Tz = 0.5,
                PolarityNote = "新式板：标记亮 / 板面暗（只在 tz≈0.5 通，远了报 #8397）" },
            new SelfVerifyRecipe { Board = "caltab_100mm.descr", GrayBackground = 128, GrayPlate = 200, GrayMarks = 20,  Tz = 1.8,
                PolarityNote = "旧式板：标记暗 / 板面亮" },
            new SelfVerifyRecipe { Board = "caltab_200mm.descr", GrayBackground = 128, GrayPlate = 200, GrayMarks = 20,  Tz = 2.5,
                PolarityNote = "旧式板：标记暗 / 板面亮" },
            new SelfVerifyRecipe { Board = "caltab_30mm.descr",  GrayBackground = 128, GrayPlate = 200, GrayMarks = 20,  Tz = 1.2,
                PolarityNote = "旧式板：标记暗 / 板面亮（板小、精度最差）" },
        };

        /// <summary>自验证的合成图尺寸（与可行性探针一致：1280×1024）</summary>
        public const int SelfVerifyImageWidth = 1280;
        public const int SelfVerifyImageHeight = 1024;

        /// <summary>自验证合成图张数（12 张倾斜位姿；实测约 1.5s 合成 + 0.18s 找标记 + 0.06s 求解）</summary>
        public const int SelfVerifyImageCount = 12;

        /// <summary>自验证用的真值内参（合成时写死；比对时用它当基准）</summary>
        public static CameraIntrinsics TrueSelfVerifyIntrinsics() => new()
        {
            CameraType = CameraIntrinsics.DivisionType,
            Focus = 0.052,
            Kappa = -1500.0,
            Sx = 5.2e-6,
            Sy = 5.2e-6,
            Cx = SelfVerifyImageWidth / 2.0,
            Cy = SelfVerifyImageHeight / 2.0,
            ImageWidth = SelfVerifyImageWidth,
            ImageHeight = SelfVerifyImageHeight,
            ImageCount = SelfVerifyImageCount,
        };

        #endregion

        #region 结果模型

        /// <summary>单张标定图的诊断（**逐张记**：用户要看到"哪几张没找到、为什么"）</summary>
        public sealed class ImageDiagnostic
        {
            /// <summary>图路径</summary>
            public string Path { get; init; } = "";

            /// <summary>这张图里找到标定板了吗</summary>
            public bool Found { get; init; }

            /// <summary>没找到时的中文原因 + 下一步（含 HALCON 错误码）</summary>
            public string? FailureReason { get; init; }

            /// <summary>HALCON 错误码（0 = 成功/非 HALCON 错误）</summary>
            public int HalconCode { get; init; }
        }

        /// <summary>一次内参标定的完整产物（成功与失败都带诊断——"为什么失败"与"哪张图不行"是同一件事的两面）</summary>
        public sealed class CalibrateResult
        {
            /// <summary>相机内参（失败时为 null）</summary>
            public CameraIntrinsics? Intrinsics { get; init; }

            /// <summary>逐张诊断（顺序与输入一致）</summary>
            public List<ImageDiagnostic> Diagnostics { get; init; } = new();

            /// <summary>成功找到标定板的图数</summary>
            public int FoundCount { get; init; }

            /// <summary>参与尝试的图总数</summary>
            public int TotalCount { get; init; }

            /// <summary>失败原因（中文 + 下一步；成功时为 null）</summary>
            public string? Error { get; init; }

            /// <summary>实际生效的初始猜测说明（"用默认公式 focus=…" / "用阶梯第 n 档 ×…"）——排障用</summary>
            public string StartGuessNote { get; init; } = "";

            /// <summary>成功与否</summary>
            public bool Succeeded => Intrinsics != null && Error == null;
        }

        /// <summary>合成自验证报告（真值 vs 恢复值 + 判据结论）</summary>
        public sealed class SelfVerifyReport
        {
            public string Board { get; init; } = "";
            public string PolarityNote { get; init; } = "";
            public double Tz { get; init; }
            public int ImageCount { get; init; }

            public double TrueFocus { get; init; }
            public double RecoveredFocus { get; init; }
            public double FocusRelErrorPercent { get; init; }

            public double TrueKappa { get; init; }
            public double RecoveredKappa { get; init; }

            public double TrueCx { get; init; }
            public double TrueCy { get; init; }
            public double RecoveredCx { get; init; }
            public double RecoveredCy { get; init; }
            public double CxDeviationPx { get; init; }
            public double CyDeviationPx { get; init; }

            public double RmsPx { get; init; }

            /// <summary>四条判据是否全过（阈值见 <see cref="TrySelfVerify(SelfVerifyRecipe, out SelfVerifyReport, out string?)"/>）</summary>
            public bool Passed { get; init; }

            /// <summary>可上屏的误差表（多行）</summary>
            public string Text { get; init; } = "";
        }

        #endregion

        #region ① 标定

        /// <summary>
        /// 一批标定板照片 → 相机内参。
        ///
        /// 流程：逐张 <c>find_calib_object</c>（**逐张记失败原因，不整体退出**）→
        /// 有效图数达 <see cref="MinImageCount"/> → <c>calibrate_cameras</c> → 读回参数填 <see cref="CameraIntrinsics"/>。
        ///
        /// 初始猜测：给了 <paramref name="initialGuess"/>（且良构）就用它；没给就按
        /// <see cref="TryDefaultStartCamPar"/> 的公式估，估不准时按 <see cref="GuessLadder"/> 兜底重试。
        /// </summary>
        /// <param name="imagePaths">标定图路径（顺序即诊断顺序）</param>
        /// <param name="caltabDescrPath">标定板描述文件全路径</param>
        /// <param name="model">相机模型（division / polynomial）</param>
        /// <param name="pixelSizeMeters">像元尺寸假设（米）——只决定 focus/kappa 的报告单位，见类注释</param>
        /// <param name="initialGuess">初始猜测（可选）；给了且良构就用它当 start cam par</param>
        /// <param name="caltabDescrTag">写进产物 <see cref="CameraIntrinsics.CaltabDescr"/> 的标签（通常传文件名）</param>
        /// <param name="cameraSerial">相机序列号（写进产物，多相机核对用）</param>
        public static CalibrateResult TryCalibrate(
            IReadOnlyList<string>? imagePaths,
            string? caltabDescrPath,
            IntrinsicsCameraModel model,
            double pixelSizeMeters = DefaultPixelSizeMeters,
            CameraIntrinsics? initialGuess = null,
            string caltabDescrTag = "",
            string cameraSerial = "")
        {
            var paths = (imagePaths ?? Array.Empty<string>())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToArray();

            if (paths.Length == 0)
                return Fail("内参标定：没有可用的标定图——请在「内参标定」卡片里选择标定图文件夹（或填写图列表），"
                          + "文件夹里应有 12 张以上从不同角度拍到完整标定板的照片", 0, null);

            if (string.IsNullOrWhiteSpace(caltabDescrPath))
                return Fail("内参标定：未指定标定板描述文件——请在「内参标定」卡片的下拉里选一块板"
                          + "（列表来自 HALCON 的 calib 目录）", 0, paths);

            var descrPath = caltabDescrPath!.Trim();
            if (!File.Exists(descrPath))
                return Fail($"内参标定：标定板描述文件不存在——{descrPath}", 0, paths);

            // 像元尺寸必须是正的有限值（它进 camPar，给 0/负会让 HALCON 报参数错）
            if (!(pixelSizeMeters > 0) || double.IsInfinity(pixelSizeMeters))
                return Fail($"内参标定：像元尺寸假设非法（{pixelSizeMeters}）——必须是正的有限值（单位米，5.2µm 请填 5.2e-6）",
                    0, paths);

            // 读图（读不出来的图也逐张记诊断，不整体退出）
            var images = new HImage?[paths.Length];
            var readFailures = new string?[paths.Length];
            try
            {
                for (int i = 0; i < paths.Length; i++)
                {
                    try
                    {
                        var img = new HImage();
                        img.ReadImage(paths[i]);
                        images[i] = img;
                    }
                    catch (HalconException ex)
                    {
                        images[i] = null;
                        readFailures[i] = $"读图失败（HALCON #{ex.GetErrorCode()}：{ex.GetErrorMessage().Trim()}）"
                                        + "——请确认文件存在、格式受支持（bmp/jpg/png/tif）";
                    }
                    catch (Exception ex)
                    {
                        images[i] = null;
                        readFailures[i] = $"读图失败（{ex.GetType().Name}：{ex.Message}）——请确认文件可访问";
                    }
                }

                // 首图尺寸（进产物做失配自证；各图尺寸不一致时明确失败）
                int imgW = 0, imgH = 0;
                var firstOk = images.FirstOrDefault(x => x != null);
                if (firstOk == null)
                    return Fail("内参标定：所有标定图都读不出来——请检查文件夹里是不是图像文件（bmp/jpg/png/tif）", 0, paths);
                firstOk!.GetImageSize(out imgW, out imgH);
                for (int i = 0; i < images.Length; i++)
                {
                    if (images[i] == null) continue;
                    images[i]!.GetImageSize(out int w, out int h);
                    if (w != imgW || h != imgH)
                        return Fail($"内参标定：标定图尺寸不一致——第 1 张是 {imgW}×{imgH}，"
                                  + $"第 {i + 1} 张（{Path.GetFileName(paths[i])}）是 {w}×{h}。"
                                  + "畸变系数是**像素帧相关**的量，尺寸混用会标出错误的内参；请统一到同一台相机/同一分辨率",
                            0, paths);
                }

                // start cam par：优先用户给的初始猜测；否则默认公式 + 阶梯兜底
                var startCamPars = new List<(HTuple CamPar, string Note)>();
                if (initialGuess != null && CameraIntrinsics.IsUsable(initialGuess, out _))
                {
                    startCamPars.Add((BuildStartCamPar(initialGuess, model, imgW, imgH),
                        $"用「初始猜测」：focus={initialGuess.Focus:R}"));
                }
                else
                {
                    if (!TryDefaultStartCamPar(descrPath, imgW, imgH, model, pixelSizeMeters,
                            out var defFocus, out var defNote, out var defError))
                        return Fail(defError ?? "内参标定：无法给出默认初始猜测", 0, paths);

                    for (int k = 0; k < GuessLadder.Length; k++)
                    {
                        double factor = GuessLadder[k];
                        string note = factor == 1.0
                            ? $"默认公式 {defNote}"
                            : $"默认公式 {defNote}，按 ×{factor} 兜底（第 {k + 1} 档）";
                        startCamPars.Add((BuildCamPar(model, defFocus * factor, 0, pixelSizeMeters, imgW, imgH), note));
                    }
                }

                // 逐档尝试：取"找到最多"的那一档；全找到就停
                FindAttempt? best = null;
                foreach (var (camPar, note) in startCamPars)
                {
                    var attempt = FindAll(descrPath, images, paths, readFailures, camPar, note);
                    if (best == null || attempt.FoundCount > best.FoundCount)
                        best = attempt;
                    if (attempt.FoundCount == paths.Length)
                        break;   // 全找到：不用再试兜底档
                }

                var chosen = best!;
                if (chosen.FoundCount < MinImageCount)
                {
                    string detail = SummarizeDiagnostics(chosen.Diagnostics);
                    return new CalibrateResult
                    {
                        Diagnostics = chosen.Diagnostics,
                        FoundCount = chosen.FoundCount,
                        TotalCount = paths.Length,
                        StartGuessNote = chosen.Note,
                        Error = $"内参标定：成功找到标定板的图只有 {chosen.FoundCount} 张，少于要求的 {MinImageCount} 张"
                              + $"（HALCON 的硬下限是 2 张，但 6 张以下没有冗余、参数会被噪声带偏）。"
                              + $"已尝试的初始猜测：{chosen.Note}。{detail}"
                              + "下一步：① 确认照片里的标定板**完整**（四角都在画面内）、清晰、光照均匀；"
                              + "② 确认标定板描述文件与实际用的是同一块板；"
                              + "③ 确认灰度极性——旧式板（caltab_*.descr）标记暗、新式板（calplate_*.cpd）标记亮，配反会报 #8431/#8397；"
                              + "④ 板在画面里应占宽度 1/3 以上，太远会报 #8397。",
                    };
                }

                return Calibrate(descrPath, images, paths, chosen, model, caltabDescrTag, cameraSerial, imgW, imgH);
            }
            finally
            {
                foreach (var img in images)
                {
                    try { img?.Dispose(); } catch { /* 释放失败不打断返回 */ }
                }
            }
        }

        /// <summary>
        /// 一档初始猜测下的"找标记"结果（find 与 calibrate 必须在**同一个** HCalibData 上，
        /// 所以这一档把 camPar 一起带出来，交给 <see cref="Calibrate"/> 原地续跑）。
        /// </summary>
        private sealed class FindAttempt
        {
            public HTuple CamPar = new();
            public string Note = "";
            public List<ImageDiagnostic> Diagnostics = new();
            public int FoundCount;
        }

        /// <summary>逐档"找标记"：返回这一档的诊断（不改动输入）</summary>
        private static FindAttempt FindAll(
            string descrPath, HImage?[] images, string[] paths, string?[] readFailures, HTuple camPar, string note)
        {
            var attempt = new FindAttempt { CamPar = camPar, Note = note };

            try
            {
                using var calibData = new HCalibData();
                calibData.CreateCalibData("calibration_object", 1, 1);
                calibData.SetCalibDataCamParam(new HTuple(0), new HTuple(), new HCamPar(camPar));
                calibData.SetCalibDataCalibObject(0, new HTuple(descrPath));

                // 观测序号用"成功找到的计数"（不是图下标）：失败图留下空档时 HALCON 的观测索引仍连续
                for (int i = 0; i < images.Length; i++)
                {
                    if (images[i] == null)
                    {
                        attempt.Diagnostics.Add(new ImageDiagnostic
                        {
                            Path = paths[i],
                            Found = false,
                            FailureReason = readFailures[i] ?? "读图失败——请确认文件存在、格式受支持（bmp/jpg/png/tif）",
                        });
                        continue;
                    }

                    try
                    {
                        calibData.FindCalibObject(images[i]!, 0, 0, attempt.FoundCount, new HTuple(), new HTuple());
                        attempt.FoundCount++;
                        attempt.Diagnostics.Add(new ImageDiagnostic { Path = paths[i], Found = true });
                    }
                    catch (HalconException ex)
                    {
                        attempt.Diagnostics.Add(new ImageDiagnostic
                        {
                            Path = paths[i],
                            Found = false,
                            HalconCode = ex.GetErrorCode(),
                            FailureReason = DescribeFindCalibObjectFailure(ex.GetErrorCode(), ex.GetErrorMessage().Trim()),
                        });
                    }
                }
            }
            catch (HalconException ex)
            {
                // 建标定数据/读板描述文件就失败（如 #8403）——整档都不可用
                string reason = ex.GetErrorCode() switch
                {
                    8403 => $"标定板描述文件读不出来（HALCON #8403：{ex.GetErrorMessage().Trim()}）"
                          + "——文件可能不存在或已损坏，请从 HALCON 的 calib 目录重新选择",
                    _ => $"初始化标定数据失败（HALCON #{ex.GetErrorCode()}：{ex.GetErrorMessage().Trim()}）"
                       + "——请检查标定板描述文件与 HALCON 安装",
                };
                attempt.Diagnostics.Clear();
                attempt.FoundCount = 0;
                foreach (var p in paths)
                    attempt.Diagnostics.Add(new ImageDiagnostic
                    {
                        Path = p, Found = false, HalconCode = ex.GetErrorCode(), FailureReason = reason,
                    });
            }

            return attempt;
        }

        /// <summary>
        /// 求解并读回参数。入参 <paramref name="found"/> 是"找到最多"的那一档。
        ///
        /// **find 与 calibrate 必须在同一个 HCalibData 上**——`calibrate_cameras` 消费的是这个句柄里
        /// 攒下的观测（标定点与位姿）。所以这里**重跑一遍 find**（把这一档的 camPar 与观测重新灌进去），
        /// 而不是把上一轮的句柄留着——句柄跨函数持有就得管生命周期，重跑一遍 find 只花约 0.18s/12 张，
        /// 换来"每次 calibrate 用的观测与诊断逐张对得上"。
        /// </summary>
        private static CalibrateResult Calibrate(
            string descrPath, HImage?[] images, string[] paths, FindAttempt found,
            IntrinsicsCameraModel model, string caltabDescrTag, string cameraSerial, int imgW, int imgH)
        {
            try
            {
                using var calibData = new HCalibData();
                calibData.CreateCalibData("calibration_object", 1, 1);
                calibData.SetCalibDataCamParam(new HTuple(0), new HTuple(), new HCamPar(found.CamPar));
                calibData.SetCalibDataCalibObject(0, new HTuple(descrPath));

                var diagnostics = new List<ImageDiagnostic>(paths.Length);
                int foundCount = 0;
                for (int i = 0; i < images.Length; i++)
                {
                    if (images[i] == null)
                    {
                        diagnostics.Add(new ImageDiagnostic { Path = paths[i], Found = false, FailureReason = "读图失败" });
                        continue;
                    }
                    try
                    {
                        calibData.FindCalibObject(images[i]!, 0, 0, foundCount, new HTuple(), new HTuple());
                        foundCount++;
                        diagnostics.Add(new ImageDiagnostic { Path = paths[i], Found = true });
                    }
                    catch (HalconException ex)
                    {
                        diagnostics.Add(new ImageDiagnostic
                        {
                            Path = paths[i], Found = false, HalconCode = ex.GetErrorCode(),
                            FailureReason = DescribeFindCalibObjectFailure(ex.GetErrorCode(), ex.GetErrorMessage().Trim()),
                        });
                    }
                }

                double rms;
                try
                {
                    rms = calibData.CalibrateCameras();
                }
                catch (HalconException ex)
                {
                    return new CalibrateResult
                    {
                        Diagnostics = diagnostics, FoundCount = foundCount, TotalCount = paths.Length,
                        StartGuessNote = found.Note,
                        Error = DescribeCalibrateFailure(ex.GetErrorCode(), ex.GetErrorMessage().Trim(), foundCount),
                    };
                }

                var raw = calibData.GetCalibData(new HTuple("camera"), new HTuple(0), new HTuple("params"));
                if (!TryBuildIntrinsics(raw, model, caltabDescrTag, cameraSerial, foundCount, rms, imgW, imgH,
                        out var intrinsics, out var parseError))
                {
                    return new CalibrateResult
                    {
                        Diagnostics = diagnostics, FoundCount = foundCount, TotalCount = paths.Length,
                        StartGuessNote = found.Note, Error = parseError,
                    };
                }

                return new CalibrateResult
                {
                    Intrinsics = intrinsics,
                    Diagnostics = diagnostics,
                    FoundCount = foundCount,
                    TotalCount = paths.Length,
                    StartGuessNote = found.Note,
                };
            }
            catch (HalconException ex)
            {
                return new CalibrateResult
                {
                    Diagnostics = found.Diagnostics, FoundCount = found.FoundCount, TotalCount = found.Diagnostics.Count,
                    StartGuessNote = found.Note,
                    Error = $"内参标定求解失败（HALCON #{ex.GetErrorCode()}：{ex.GetErrorMessage().Trim()}）"
                          + "——请检查标定图与标定板描述文件是否配套",
                };
            }
        }

        #endregion

        #region ② 合成自验证

        /// <summary>
        /// 用默认配方（<see cref="SelfVerifyRecipes"/>[0]）做一次合成自验证。
        /// 现场自检入口：不接相机也能证明"本机 HALCON 的标定链 + 本插件的封装"是通的。
        /// </summary>
        public static bool TrySelfVerify(out SelfVerifyReport report, out string? error)
            => TrySelfVerify(SelfVerifyRecipes[0], out report, out error);

        /// <summary>
        /// 合成自验证：用 <c>sim_caltab</c> 生成已知内参的 12 张倾斜位姿标定板图 → **落盘为 PNG** →
        /// 走 <see cref="TryCalibrate"/> 完整链路 → 与真值比对。
        ///
        /// 为什么落盘再读（而不是把内存图直接喂 find_calib_object）
        /// ---------
        /// 落盘再读能把"读图 → 尺寸检查 → find_calib_object → calibrate_cameras → 读回参数"整条链
        /// 都走一遍——这才是现场点「开始标定」时真正执行的那条路径。多花约 0.2s 换"自验证结论可迁移"，
        /// 值得。
        ///
        /// 判据（设计稿 §四 a，量化为四条，全部通过才 <see cref="SelfVerifyReport.Passed"/>）：
        /// ① 焦距相对误差 &lt; 0.5%；② 主点偏差 &lt; 1px；③ 重投影 RMS &lt; 0.3px；④ kappa 相对误差 &lt; 5%。
        /// 默认配方（calplate_320mm.cpd @1.2m）的实测值是 0.0018% / 0.0001px / 0.0039px / 0.002%，
        /// 距判据有一个数量级以上的余量——所以这四条红了就是**环境或代码真的坏了**，不是"阈值太紧"。
        /// </summary>
        public static bool TrySelfVerify(SelfVerifyRecipe recipe, out SelfVerifyReport report, out string? error)
        {
            report = null!;
            error = null;

            if (recipe == null) { error = "内参自验证：配方为空"; return false; }

            var trueIntrinsics = TrueSelfVerifyIntrinsics();
            string? tempDir = null;

            try
            {
                var calibDir = ResolveCalibDir();
                if (calibDir == null)
                {
                    error = "内参自验证：找不到 HALCON 的 calib 目录——请确认已安装 HALCON 且 HALCONROOT 环境变量正确";
                    return false;
                }

                string descrPath = Path.Combine(calibDir, recipe.Board);
                if (!File.Exists(descrPath))
                {
                    error = $"内参自验证：标定板描述文件不存在——{descrPath}（该 HALCON 安装里没有这块板）";
                    return false;
                }

                // 合成 12 张倾斜位姿 → 落盘 PNG（位姿构造见 SelfVerifyRecipe 注释；旋转单位是度）
                tempDir = Path.Combine(Path.GetTempPath(), "VM_IntrinsicsSelfVerify", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);

                var trueCam = CameraDistortionLike(trueIntrinsics, recipe);
                var paths = new List<string>(SelfVerifyImageCount);
                for (int i = 0; i < SelfVerifyImageCount; i++)
                {
                    double a = 2 * Math.PI * i / SelfVerifyImageCount;
                    double tz = recipe.Tz * (0.94 + 0.12 * (i % 3) / 2.0);
                    var pose = new HPose(
                        0.016 * Math.Cos(a), 0.012 * Math.Sin(a), tz,
                        16.0 * Math.Sin(a), 14.0 * Math.Cos(a), 0.0,
                        "Rp+T", "gba", "point");

                    var img = new HImage();
                    try
                    {
                        img.SimCaltab(descrPath, new HCamPar(trueCam), pose,
                            recipe.GrayBackground, recipe.GrayPlate, recipe.GrayMarks, 1.0);
                        string p = Path.Combine(tempDir, $"{i + 1:00}.png");
                        HOperatorSet.WriteImage(img, "png", 0, p);
                        paths.Add(p);
                    }
                    finally
                    {
                        img.Dispose();
                    }
                }

                var result = TryCalibrate(paths, descrPath, IntrinsicsCameraModel.Division,
                    pixelSizeMeters: trueIntrinsics.Sx, initialGuess: null,
                    caltabDescrTag: recipe.Board, cameraSerial: "自验证");

                if (!result.Succeeded)
                {
                    error = "内参自验证：完整标定链没能跑通（这**不是**阈值问题，是环境或代码坏了）——"
                          + (result.Error ?? "未知原因");
                    return false;
                }

                var got = result.Intrinsics!;
                double focusErr = Math.Abs(got.Focus - trueIntrinsics.Focus) / trueIntrinsics.Focus * 100.0;
                double cxDev = Math.Abs(got.Cx - trueIntrinsics.Cx);
                double cyDev = Math.Abs(got.Cy - trueIntrinsics.Cy);
                double kappaErr = trueIntrinsics.Kappa != 0
                    ? Math.Abs(got.Kappa - trueIntrinsics.Kappa) / Math.Abs(trueIntrinsics.Kappa) * 100.0
                    : Math.Abs(got.Kappa - trueIntrinsics.Kappa);

                bool pass = focusErr < 0.5 && cxDev < 1.0 && cyDev < 1.0 && got.ReprojectionRmsPx < 0.3 && kappaErr < 5.0;

                var sb = new StringBuilder();
                sb.AppendLine($"配方：{recipe.Board}（{recipe.PolarityNote}）@ {recipe.Tz:0.##}m，{result.FoundCount}/{SelfVerifyImageCount} 张找到标定板");
                sb.AppendLine($"初始猜测：{result.StartGuessNote}");
                sb.AppendLine($"焦距   : 恢复 {got.Focus:G6} vs 真值 {trueIntrinsics.Focus:G6} → 相对误差 {focusErr:0.####}%（判据 < 0.5%）");
                sb.AppendLine($"主点   : 恢复 ({got.Cx:0.####}, {got.Cy:0.####}) vs 真值 ({trueIntrinsics.Cx:0.#}, {trueIntrinsics.Cy:0.#}) → 偏差 ({cxDev:0.####}, {cyDev:0.####}) px（判据 < 1px）");
                sb.AppendLine($"kappa  : 恢复 {got.Kappa:G6} vs 真值 {trueIntrinsics.Kappa:G6} → 相对误差 {kappaErr:0.####}%（判据 < 5%）");
                sb.AppendLine($"重投影 : RMS {got.ReprojectionRmsPx:0.####} px（判据 < 0.3px）");
                sb.AppendLine(pass ? "结论   : 通过——本机标定链可用，现场按同一套方法标你的相机即可" : "结论   : 未通过——见上面超判据的项（余量本应有一个数量级，红了多半是环境变了）");

                report = new SelfVerifyReport
                {
                    Board = recipe.Board,
                    PolarityNote = recipe.PolarityNote,
                    Tz = recipe.Tz,
                    ImageCount = result.FoundCount,
                    TrueFocus = trueIntrinsics.Focus,
                    RecoveredFocus = got.Focus,
                    FocusRelErrorPercent = focusErr,
                    TrueKappa = trueIntrinsics.Kappa,
                    RecoveredKappa = got.Kappa,
                    TrueCx = trueIntrinsics.Cx,
                    TrueCy = trueIntrinsics.Cy,
                    RecoveredCx = got.Cx,
                    RecoveredCy = got.Cy,
                    CxDeviationPx = cxDev,
                    CyDeviationPx = cyDev,
                    RmsPx = got.ReprojectionRmsPx,
                    Passed = pass,
                    Text = sb.ToString().TrimEnd(),
                };
                return true;
            }
            catch (HalconException ex)
            {
                error = $"内参自验证失败（HALCON #{ex.GetErrorCode()}：{ex.GetErrorMessage().Trim()}）"
                      + "——若码是 #1307/#1203，说明本机的 HALCON 版本不接受位姿构造的字符串写法";
                return false;
            }
            catch (Exception ex)
            {
                error = $"内参自验证失败（{ex.GetType().Name}：{ex.Message}）";
                return false;
            }
            finally
            {
                if (tempDir != null)
                {
                    try { Directory.Delete(tempDir, recursive: true); }
                    catch { /* 临时目录删不掉不影响结论 */ }
                }
            }
        }

        /// <summary>合成用的真值 camPar（division 形状；自验证只走 division）</summary>
        private static HTuple CameraDistortionLike(CameraIntrinsics c, SelfVerifyRecipe _)
            => new(CameraIntrinsics.DivisionType, c.Focus, c.Kappa, c.Sx, c.Sy, c.Cx, c.Cy, c.ImageWidth, c.ImageHeight);

        #endregion

        #region ③ 板描述文件候选

        /// <summary>板描述文件候选（界面下拉用）</summary>
        public sealed class CaltabDescriptorEntry
        {
            /// <summary>文件名（如 <c>calplate_160mm.cpd</c>）——**这是落盘的值**（跨机器可移植）</summary>
            public string FileName { get; init; } = "";

            /// <summary>全路径（本机 HALCON 安装下）</summary>
            public string FullPath { get; init; } = "";

            /// <summary>板式：新式（cpd）/ 旧式（descr）</summary>
            public bool IsNewStyle { get; init; }

            /// <summary>板宽度（米）；量不出来为 0</summary>
            public double PlateWidthMeters { get; init; }

            /// <summary>下拉显示文本（含板式与灰度极性提示——配反了会报 #8431/#8397）</summary>
            public string Display => IsNewStyle
                ? $"{FileName}（新式板：标记亮 / 板面暗）"
                : $"{FileName}（旧式板：标记暗 / 板面亮）";
        }

        /// <summary>
        /// 扫 `%HALCONROOT%\calib\` 下的 `caltab_*.descr` 与 `calplate_*.cpd`。
        /// HALCONROOT 没配 / 目录不存在 → false + 中文原因（界面据此提示，而不是给一个空下拉）。
        /// </summary>
        public static bool TryListCaltabDescriptors(out List<CaltabDescriptorEntry> entries, out string? error)
        {
            entries = new List<CaltabDescriptorEntry>();
            error = null;

            var calibDir = ResolveCalibDir();
            if (calibDir == null)
            {
                error = "找不到 HALCON 的 calib 目录：环境变量 HALCONROOT 未设置或指向不存在的路径"
                      + "——请确认已安装 HALCON（本插件的内参标定依赖它的标定算子与标定板描述文件）";
                return false;
            }

            try
            {
                foreach (var file in Directory.EnumerateFiles(calibDir)
                             .Where(f => f.EndsWith(".cpd", StringComparison.OrdinalIgnoreCase)
                                      || f.EndsWith(".descr", StringComparison.OrdinalIgnoreCase))
                             .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase))
                {
                    string name = Path.GetFileName(file);
                    bool isNewStyle = name.StartsWith("calplate_", StringComparison.OrdinalIgnoreCase);
                    bool isOldStyle = name.StartsWith("caltab_", StringComparison.OrdinalIgnoreCase);
                    if (!isNewStyle && !isOldStyle) continue;

                    double width = 0;
                    try
                    {
                        HOperatorSet.CaltabPoints(file, out HTuple px, out _, out _);
                        var xs = px.ToDArr();
                        if (xs.Length > 0) width = xs.Max() - xs.Min();
                    }
                    catch (HalconException)
                    {
                        width = 0;   // 量不出来不拦（候选照样列出来，标定时再报）
                    }

                    entries.Add(new CaltabDescriptorEntry
                    {
                        FileName = name,
                        FullPath = file,
                        IsNewStyle = isNewStyle,
                        PlateWidthMeters = width,
                    });
                }

                if (entries.Count == 0)
                {
                    error = $"HALCON 的 calib 目录里没有标定板描述文件（caltab_*.descr / calplate_*.cpd）：{calibDir}";
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                error = $"扫描标定板描述文件失败（{ex.GetType().Name}：{ex.Message}）——目录：{calibDir}";
                return false;
            }
        }

        /// <summary>
        /// 把"落盘的值"（可能是文件名，也可能是全路径）解析成本机的全路径。
        /// 文件名 → 在 `%HALCONROOT%\calib\` 下找（这样方案文件跨机器可移植）；找不到给中文原因 + 本机有哪些候选。
        /// </summary>
        public static bool TryResolveCaltabPath(string? value, out string fullPath, out string? error)
        {
            fullPath = "";
            error = null;

            var v = (value ?? string.Empty).Trim();
            if (v.Length == 0)
            {
                error = "未指定标定板描述文件：请在「内参标定」卡片的下拉里选一块板";
                return false;
            }

            // 全路径：直接用（允许现场用自己的板文件）
            if (v.IndexOf(Path.DirectorySeparatorChar) >= 0 || v.IndexOf(Path.AltDirectorySeparatorChar) >= 0)
            {
                if (File.Exists(v)) { fullPath = v; return true; }
                error = $"标定板描述文件不存在：{v}";
                return false;
            }

            var calibDir = ResolveCalibDir();
            if (calibDir == null)
            {
                error = "找不到 HALCON 的 calib 目录（HALCONROOT 未设置或路径不存在）——无法解析标定板文件名：" + v;
                return false;
            }

            string candidate = Path.Combine(calibDir, v);
            if (File.Exists(candidate)) { fullPath = candidate; return true; }

            string available = TryListCaltabDescriptors(out var list, out _)
                ? string.Join("、", list.Select(e => e.FileName).Take(12))
                : "（列不出候选）";
            error = $"HALCON 的 calib 目录里没有这块板：{v}（目录：{calibDir}）。本机可用：{available}";
            return false;
        }

        /// <summary>HALCON 的 calib 目录（HALCONROOT 未配/不存在 → null）</summary>
        public static string? ResolveCalibDir()
        {
            var root = (Environment.GetEnvironmentVariable("HALCONROOT") ?? string.Empty).Trim();
            if (root.Length == 0) return null;
            var dir = Path.Combine(root, "calib");
            return Directory.Exists(dir) ? dir : null;
        }

        #endregion

        #region 初始猜测

        /// <summary>
        /// 默认初始猜测：<c>focus = 像元尺寸 × 图宽 ÷ 板宽</c>，kappa = 0，主点 = 图中心。
        ///
        /// 这条公式的物理含义："标定板横跨整个画面宽度、且距离 1m"时的焦距。
        /// **实测（2026-10-16，9 个"板 × 距离"组合，1280×1024）**：本公式给出的值相对真值的倍率
        /// 落在 0.41~5.69 之间，**每一个组合都靠这一档就 12/12 全找到**（无需 <see cref="GuessLadder"/> 兜底），
        /// 精度与直接给真值一致（如 calplate_320mm @1.2 得 0.0018%、caltab_30mm 得 0.089%）。
        /// 板宽取自 HALCON 的 <c>caltab_points</c>（板文件自带，不靠文件名猜）。
        ///
        /// <see cref="GuessLadder"/> 因此是**纯保险**：实测没有触发过（倍率 5.69 也在窗口 [1/64,4] 的
        /// 宽容侧——见 <see cref="GuessLadder"/> 的注释），留给"像元尺寸被填错一个数量级"这类现场失误。
        /// </summary>
        private static bool TryDefaultStartCamPar(
            string descrPath, int imageWidth, int imageHeight, IntrinsicsCameraModel model,
            double pixelSizeMeters, out double focus, out string note, out string? error)
        {
            focus = 0;
            note = "";
            error = null;

            if (imageWidth <= 0 || imageHeight <= 0)
            {
                error = "内参标定：拿不到标定图的尺寸（读图失败）——无法估计初始焦距";
                return false;
            }

            double plateWidth;
            try
            {
                HOperatorSet.CaltabPoints(descrPath, out HTuple px, out _, out _);
                var xs = px.ToDArr();
                if (xs.Length == 0)
                {
                    error = $"内参标定：标定板描述文件里没有任何标记点——{Path.GetFileName(descrPath)} 可能已损坏";
                    return false;
                }
                plateWidth = xs.Max() - xs.Min();
            }
            catch (HalconException ex)
            {
                error = $"内参标定：读标定板描述文件失败（HALCON #{ex.GetErrorCode()}：{ex.GetErrorMessage().Trim()}）"
                      + "——请从 HALCON 的 calib 目录重新选择标定板";
                return false;
            }

            if (!(plateWidth > 0))
            {
                error = "内参标定：标定板宽度算出来是 0——描述文件可能已损坏";
                return false;
            }

            focus = pixelSizeMeters * imageWidth / plateWidth;
            note = $"focus={focus:R}（= 像元 {pixelSizeMeters * 1e6:0.##}µm × 图宽 {imageWidth} ÷ 板宽 {plateWidth * 1000:0.##}mm）";
            return true;
        }

        /// <summary>由用户给的初始猜测构造 start cam par（借它的焦距与畸变系数量级；主点/尺寸用本次要标的图）</summary>
        private static HTuple BuildStartCamPar(CameraIntrinsics guess, IntrinsicsCameraModel model, int imgW, int imgH)
        {
            double sx = guess.Sx > 0 ? guess.Sx : DefaultPixelSizeMeters;
            double sy = guess.Sy > 0 ? guess.Sy : sx;
            double cx = guess.Cx > 0 ? guess.Cx : imgW / 2.0;
            double cy = guess.Cy > 0 ? guess.Cy : imgH / 2.0;

            if (model == IntrinsicsCameraModel.Division)
                return new HTuple(CameraIntrinsics.DivisionType, guess.Focus, guess.Kappa, sx, sy, cx, cy, imgW, imgH);

            // polynomial：猜测里的畸变系数长度对才用，否则从 0 起步（形状不符就是"没给"）
            var d = guess.Distortion is { Length: CameraIntrinsics.PolynomialCoeffCount } arr
                ? arr : new double[CameraIntrinsics.PolynomialCoeffCount];
            return new HTuple(CameraIntrinsics.PolynomialType, guess.Focus,
                d[0], d[1], d[2], d[3], d[4], sx, sy, cx, cy, imgW, imgH);
        }

        /// <summary>构造 start cam par（kappa=0、主点=图中心）</summary>
        private static HTuple BuildCamPar(
            IntrinsicsCameraModel model, double focus, double kappa, double pixelSize, int imageWidth, int imageHeight)
            => model == IntrinsicsCameraModel.Division
                ? new HTuple(CameraIntrinsics.DivisionType, focus, kappa, pixelSize, pixelSize,
                    imageWidth / 2.0, imageHeight / 2.0, imageWidth, imageHeight)
                : new HTuple(CameraIntrinsics.PolynomialType, focus, 0.0, 0.0, 0.0, 0.0, 0.0,
                    pixelSize, pixelSize, imageWidth / 2.0, imageHeight / 2.0, imageWidth, imageHeight);

        #endregion

        #region 参数读回与失败文案

        /// <summary>
        /// HALCON 读回的 camPar tuple → <see cref="CameraIntrinsics"/>。
        /// 形状（实测）：division = [type, focus, kappa, sx, sy, cx, cy, w, h]（9 元）；
        /// polynomial = [type, focus, k1, k2, k3, p1, p2, sx, sy, cx, cy, w, h]（13 元）。
        /// 长度/类型对不上 = 契约损坏 → 明确失败（绝不按位置硬读，读错位置就是"看起来合理的错内参"）。
        /// </summary>
        private static bool TryBuildIntrinsics(
            HTuple raw, IntrinsicsCameraModel model, string caltabDescrTag, string cameraSerial,
            int imageCount, double rms, int imageWidth, int imageHeight,
            out CameraIntrinsics? intrinsics, out string? error)
        {
            intrinsics = null;
            error = null;

            bool division = model == IntrinsicsCameraModel.Division;
            int expected = division ? 9 : 13;
            if (raw.Length != expected)
            {
                error = $"内参标定：HALCON 读回的相机参数长度不符（期望 {expected}，实际 {raw.Length}）"
                      + "——这是 HALCON 版本差异或数据损坏，请把这条信息反馈给开发者";
                return false;
            }

            string type = raw[0].S ?? "";
            string expectedType = division ? CameraIntrinsics.DivisionType : CameraIntrinsics.PolynomialType;
            if (!string.Equals(type, expectedType, StringComparison.Ordinal))
            {
                error = $"内参标定：HALCON 读回的相机模型是「{type}」，与请求的「{expectedType}」不符"
                      + "——请重试；若持续出现请把这条信息反馈给开发者";
                return false;
            }

            var c = new CameraIntrinsics
            {
                CameraType = type,
                Focus = raw[1].D,
                ImageCount = imageCount,
                ReprojectionRmsPx = rms,
                CaltabDescr = caltabDescrTag,
                CameraSerial = cameraSerial,
                CreatedAtUtc = DateTime.UtcNow,
                // 标定图的尺寸：**用实测的图尺寸**（HALCON 读回的 w/h 是相机参数里的，两者一致；
                // 用实测值更贴合"下游拿当前图尺寸比对"的语义）
                ImageWidth = imageWidth > 0 ? imageWidth : (int)raw[division ? 7 : 11].D,
                ImageHeight = imageHeight > 0 ? imageHeight : (int)raw[division ? 8 : 12].D,
            };

            if (division)
            {
                c.Kappa = raw[2].D;
                c.Sx = raw[3].D;
                c.Sy = raw[4].D;
                c.Cx = raw[5].D;
                c.Cy = raw[6].D;
                c.Distortion = null;   // division 模型不用它（null = "本模型不用它"，见契约注释）
            }
            else
            {
                c.Kappa = 0;   // polynomial 模型不用 kappa
                c.Distortion = new[] { raw[2].D, raw[3].D, raw[4].D, raw[5].D, raw[6].D };
                c.Sx = raw[7].D;
                c.Sy = raw[8].D;
                c.Cx = raw[9].D;
                c.Cy = raw[10].D;
            }

            if (!CameraIntrinsics.IsUsable(c, out var why))
            {
                error = $"内参标定：HALCON 读回的相机参数不合法——{why}";
                return false;
            }

            intrinsics = c;
            return true;
        }

        /// <summary>
        /// <c>find_calib_object</c> 失败码 → 中文原因 + 下一步。
        /// 三个码的**实测语义**（不是猜的，2026-10-16 本机合成图逐条复现）：
        /// #8397 分割不出标记（板太小/太远、对比度不足）；#8431 定不出板朝向（**灰度极性配反**的典型症状）；
        /// #8404 椭圆搜索失败（板太大/太近或过曝）。
        /// </summary>
        public static string DescribeFindCalibObjectFailure(int code, string english)
            => code switch
            {
                8397 => $"在图上分割不出标定标记（HALCON #8397：{english}）——"
                      + "最常见是标定板在画面里太小/太远（板宽应占画面宽度 1/3 以上），或板与背景对比度不足、打光过曝。"
                      + "请把板靠近相机重拍，确保板面平整、无反光遮挡",
                8431 => $"找到了标记但定不出标定板朝向（HALCON #8431：{english}）——"
                      + "最常见是**灰度极性配反**：旧式板（caltab_*.descr）标记是暗的、板面是亮的；"
                      + "新式板（calplate_*.cpd）标记是亮的、板面是暗的。"
                      + "请核对「标定板描述文件」与实际成像的明暗关系是否配套",
                8404 => $"椭圆搜索没找到标定标记（HALCON #8404：{english}）——"
                      + "板可能太大/太近（标记跑出画面或互相粘连），或光照过曝使标记边缘糊掉。请把板挪远一点/调暗照明重拍",
                _ => $"找标定板失败（HALCON #{code}：{english}）——"
                   + "请检查该图里的标定板是否完整、清晰、光照均匀，并与标定板描述文件配套",
            };

        /// <summary><c>calibrate_cameras</c> 失败码 → 中文原因 + 下一步</summary>
        public static string DescribeCalibrateFailure(int code, string english, int foundCount)
            => code switch
            {
                8498 => $"没有可用的标定观测数据（HALCON #8498：{english}）——"
                      + $"成功找到标定板的图有 {foundCount} 张（HALCON 至少要 2 张、本插件要求 {MinImageCount} 张）。"
                      + "请按逐张诊断里的原因补拍（多数是板太小/太远或灰度极性配反）",
                _ => $"内参求解失败（HALCON #{code}：{english}）——"
                   + "请检查标定图是否覆盖了板的不同姿态（平放 + 各方向倾斜），以及标定板描述文件是否与实际板配套",
            };

        /// <summary>把逐张诊断压成一句可上屏的摘要（"3 张报 #8397、1 张报 #8431"这种）</summary>
        public static string SummarizeDiagnostics(IReadOnlyList<ImageDiagnostic>? diagnostics)
        {
            if (diagnostics == null || diagnostics.Count == 0) return "";
            var groups = diagnostics.Where(d => !d.Found && d.HalconCode != 0)
                .GroupBy(d => d.HalconCode)
                .OrderByDescending(g => g.Count())
                .Select(g => $"{g.Count()} 张报 #{g.Key}")
                .ToList();
            int readFail = diagnostics.Count(d => !d.Found && d.HalconCode == 0);
            if (readFail > 0) groups.Add($"{readFail} 张读图失败");
            return groups.Count == 0 ? "" : $"失败分布：{string.Join("、", groups)}。";
        }

        private static CalibrateResult Fail(string error, int found, IReadOnlyList<string>? paths)
            => new()
            {
                Error = error,
                FoundCount = found,
                TotalCount = paths?.Count ?? 0,
                Diagnostics = (paths ?? Array.Empty<string>())
                    .Select(p => new ImageDiagnostic { Path = p, Found = false, FailureReason = error })
                    .ToList(),
            };

        #endregion
    }
}
