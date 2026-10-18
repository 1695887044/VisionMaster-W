using System;
using Core.Halcon.Helpers;
using Core.Interfaces;

namespace Plugin.PoseTransform
{
    /// <summary>
    /// 内参去畸变的 **HALCON 包装层**（二期③，2026-10-16）。
    ///
    /// 为什么单独一个文件（不写进 PoseTransformMath.cs）
    /// ---------
    /// <see cref="PoseTransformMath"/> 的立身纪律是"零 HALCON 依赖 → 断言可直接构造输入跑、不依赖引擎"。
    /// 去畸变必须调 HALCON（`change_radial_distortion_points`），所以落在这里——
    /// 与 <see cref="PoseTransformHalcon"/> 同一分工（那边管区域/图像变换，这边管像素域的畸变换算）。
    ///
    /// 契约语义（见 <see cref="CalibrationKind.NinePointUndistorted"/> 的契约注释）
    /// ---------
    /// Kind 是三个 `*Undistorted` 之一时，Matrix / ProjectiveMatrix / MeshNodes 里的像素定义在
    /// **去畸变后**的理想针孔坐标系上，且 Intrinsics 必非 null。于是本插件的两条路径是：
    /// · **正向**（真实图像像素 → 机械）：输入点先经 <see cref="TryUndistortForCalibration"/> 去畸变，再喂数学层；
    /// · **反向**（机械 → 真实图像像素）：数学层算出去畸变域像素后，再经 <see cref="TryDistortBack"/> 施加**逆向**畸变
    ///   ——否则回显拿"去畸变域的点"与"真实图像的点"比，必红。
    ///
    /// 两个方向只是 camParamIn/Out 对调：一律走共享件 <see cref="CameraDistortion"/>
    /// （唯一一份实现；自己拼 HALCON tuple = 生产端与消费端迟早分叉，后果是同一份标定两处算出不同坐标）。
    ///
    /// ⚠ `change_radial_distortion_cam_par` 必须 mode = `"fixed"`
    /// ---------
    /// 其它 mode（fullsize / adaptive / preserve_resolution）会**改 sx/sy/cx/cy**——去畸变后的像素就落到
    /// 另一个像素坐标系里，标定矩阵与 <see cref="CalibrationTransform.SourceImageWidth"/> 全部对不上
    /// （2026-10-16 实测：`fullsize` 把 sx=5.2e-6 改成 5.3458e-6、主点也从 (640,512) 挪走）。
    /// <see cref="CameraDistortion"/> 已封装成 `"fixed"`，本层不绕过它。
    ///
    /// 判据的单一真源
    /// ---------
    /// "这个 Kind 是不是去畸变域"只有一处实现：<see cref="PoseTransformMath.IsUndistortedKind"/>
    /// （放在零 HALCON 的数学层，因为 <c>CheckCalibrationUsable</c> 也要用它；本层不另写一份）。
    /// </summary>
    public static class PoseTransformIntrinsics
    {
        /// <summary>
        /// 方向取模的**步进长度**（像素）：角度换算需要"方向向量在去畸变域里朝哪"，
        /// 本层用"原始像素域走一小步 → 两点都去畸变 → 差分"数值取方向（局部雅可比对方向向量的作用）。
        ///
        /// 为什么是 4px（2026-10-16 本机实测步长扫描，division / polynomial 各 13 档，30°@(300,400)）：
        ///
        /// | 差分步长 | division | polynomial |
        /// |---|---|---|
        /// | 1e-6 px | 30.092418527° | 29.906440750° |
        /// | 0.001 px | 30.092415511° | 29.906440054° |
        /// | 0.25 px | 30.092379142° | 29.906477799° |
        /// | 1 px | 30.092269596° | 29.906591484° |
        /// | **4 px** | **30.091831392°** | **29.907046152°** |
        /// | 16 px | 30.090078243° | 29.908863718° |
        /// | 64 px | 30.083060306° | 29.916116941° |
        /// | 256 px | 30.054902822° | 29.944897878° |
        ///
        /// 读数（这份数据是这条注释的唯一依据，别凭印象改数）：
        /// · **误差是纯截断误差、随步长单调增长**：相对 h→0 极限（1e-6px 那一档），
        ///   4px 偏移 **5.87e-4°**（division）/ **6.05e-4°**（polynomial）；16px 涨到 **2.34e-3° / 2.42e-3°**、
        ///   64px 到 **9.36e-3° / 9.68e-3°**。
        /// · **小步长一侧没有观测到噪声地板**：从 1e-6px 到 0.001px 读数单调且差 3e-9° 量级
        ///   （去畸变调用本身是确定性的——同点两次逐位相同），说明理论上"步长越小越准"。
        /// · **仍取 4px 而不是更小**，理由有三条：① 4px 的 5.9e-4° 已远低于角度输出的实用分辨率
        ///   （机械角报 0.001° 都嫌多，机器人重复定位在 0.01° 量级）；② 4px 在去畸变域留下约
        ///   4.05px 的两点间距（实测该处局部缩放因子 1.0122），比 double 在该坐标量级（~1e3）的精度
        ///   （~1e-13px）高 13 个数量级——结果不依赖 HALCON 内部实现细节，换个版本/换个模型也稳；
        ///   ③ 局部尺度因子很强的模型（去畸变把邻域拉伸数倍）下 4px 仍有两点的实际间距可测，
        ///   1e-6px 则可能落进"看起来是 0"的区间。
        /// 想更准就改这一个常量（代价是截断误差更小、精度余量更薄），**不要改成解析求导**——
        /// 那要求在本层重写 HALCON 的投影公式，属于"复制共享件"（R36 那族）。
        /// </summary>
        private const double DirectionProbeStepPx = 4.0;

        /// <summary>
        /// **正向**：真实图像像素 → 本标定矩阵的像素定义域。
        /// · Kind 是 `*Undistorted`：按 <see cref="CalibrationTransform.Intrinsics"/> 去畸变；
        /// · 其余 Kind：**原样通过**——它们的像素定义域就是原始像素，这一步是恒等（不是"忘了去畸变"）。
        /// 失败（内参缺失/损坏、HALCON 拒绝、结果含 NaN/Inf）→ false；文案直接来自
        /// <see cref="CameraDistortion"/>（已是"上屏级"、含下一步，本层不再包一层）。
        /// </summary>
        public static bool TryUndistortForCalibration(
            CalibrationTransform? t, double row, double col,
            out double mapRow, out double mapCol, out string? error)
        {
            mapRow = row;
            mapCol = col;
            error = null;

            if (t == null)
            {
                error = PoseTransformMath.MissingCalibrationMessage;
                return false;
            }

            if (!PoseTransformMath.IsUndistortedKind(t.Kind))
                return true;   // 像素定义域 = 原始像素，恒等通过

            return CameraDistortion.TryUndistortPoint(t.Intrinsics, row, col, out mapRow, out mapCol, out error);
        }

        /// <summary>
        /// **反向**：本标定矩阵的像素定义域 → 真实图像像素（正向的逆）。
        /// 非 `*Undistorted` 的 Kind 同样原样通过（恒等）。
        /// 回显自校验必须走这一步才能拿"真实图像像素"与输入比（见 <see cref="PoseTransformPlugin"/> 的回显段）。
        /// </summary>
        public static bool TryDistortBack(
            CalibrationTransform? t, double row, double col,
            out double imageRow, out double imageCol, out string? error)
        {
            imageRow = row;
            imageCol = col;
            error = null;

            if (t == null)
            {
                error = PoseTransformMath.MissingCalibrationMessage;
                return false;
            }

            if (!PoseTransformMath.IsUndistortedKind(t.Kind))
                return true;

            return CameraDistortion.TryDistortPoint(t.Intrinsics, row, col, out imageRow, out imageCol, out error);
        }

        /// <summary>
        /// 像素系角度（度）→ **去畸变域**里的方向角（度）：喂给
        /// <see cref="PoseTransformMath.TryPixelAngleToMechanical"/> 的那个角度。
        ///
        /// 为什么要换算：角度是"方向"的量。畸变在像素域里是一个**非共形**的局部映射
        /// （径向缩放各向异性 + polynomial 的切向项），所以同一个物理方向在"畸变像素域"与
        /// "去畸变域"里的**方向角不同**。做法是把角度定义的步进向量 (Row=sinθ, Col=cosθ)
        /// （契约口径，见数学层注释）在原始像素域走 <see cref="DirectionProbeStepPx"/> 一小步，
        /// 两点都去畸变后差分 → 去畸变域的方向 → 反解回角度。
        ///
        /// 非 `*Undistorted` 的 Kind：原样返回（像素域没变）。
        /// v1 边界：本换算只回答"方向在去畸变域朝哪"，不回答"畸变把直线弯成曲线"这类高阶效应
        /// （对目标角度输出足够；如需严格共形性请走图像域去畸变后再取角度）。
        /// </summary>
        public static bool TryUndistortDirection(
            CalibrationTransform? t, double pixelAngleDeg, double row, double col,
            out double undistortedAngleDeg, out string? error)
        {
            undistortedAngleDeg = pixelAngleDeg;
            error = null;

            if (t == null)
            {
                error = PoseTransformMath.MissingCalibrationMessage;
                return false;
            }

            if (!PoseTransformMath.IsUndistortedKind(t.Kind))
                return true;

            if (!double.IsFinite(pixelAngleDeg))
            {
                error = "像素角度无效（NaN/Inf）——请检查上游角度来源";
                return false;
            }

            if (!double.IsFinite(row) || !double.IsFinite(col))
            {
                error = "像素位置无效（NaN/Inf）：去畸变方向换算需要有效位置（局部映射随点变化）";
                return false;
            }

            double rad = pixelAngleDeg * Math.PI / 180.0;
            double stepRow = Math.Sin(rad);   // 契约口径：方向向量 = (Row=sinθ, Col=cosθ)
            double stepCol = Math.Cos(rad);

            if (!TryUndistortForCalibration(t, row, col, out double baseRow, out double baseCol, out error))
                return false;
            if (!TryUndistortForCalibration(
                    t, row + DirectionProbeStepPx * stepRow, col + DirectionProbeStepPx * stepCol,
                    out double probeRow, out double probeCol, out error))
                return false;

            double vecRow = probeRow - baseRow;
            double vecCol = probeCol - baseCol;
            if (!double.IsFinite(vecRow) || !double.IsFinite(vecCol)
                || Math.Sqrt(vecRow * vecRow + vecCol * vecCol) < 1e-9)
            {
                error = "去畸变后的方向退化（两点差分≈0）：该点附近的相机模型可能已损坏，请重新运行内参标定";
                return false;
            }

            undistortedAngleDeg = PoseTransformMath.NormalizeDegrees(Math.Atan2(vecRow, vecCol) * 180.0 / Math.PI);
            return true;
        }

        /// <summary>
        /// 反向回显自校验的**容差**（像素）：按畸变模型分档（数学层不知道相机模型，判据只能落在这里）。
        ///
        /// 为什么必须分档（2026-10-16 实测，1280×1024、45 个采样点，往返 = 去畸变后再加畸变）：
        /// · `area_scan_division`：**可解析求逆**——往返最大误差 **2.681e-13 px** → 取 1e-6（留 7 个数量级余量）；
        /// · `area_scan_polynomial`：**只能迭代反解**——同条件（k1=-1500, k2=1e6, k3=-1e8）
        ///   往返最大误差 **1.142e-4 px** → 必须取 1e-3（是 1e-6 的 114 倍，用 1e-6 会**假红**，
        ///   而且失败文案会指向"标定矩阵已损坏，请重新标定"——**现场重标定解决不了**，是容差错不是数据错）。
        /// · null（非 `*Undistorted` 的 Kind，压根没有畸变往返）：沿用历史口径 1e-6，行为与改动前逐字一致。
        ///
        /// 分档口径与 <see cref="CameraDistortion"/> 的类注释、<see cref="CameraIntrinsics"/> 的注释同源。
        /// </summary>
        public static double RoundTripTolerancePx(CameraIntrinsics? intrinsics)
        {
            if (intrinsics == null)
                return 1e-6;

            return intrinsics.CameraType == CameraIntrinsics.PolynomialType ? 1e-3 : 1e-6;
        }
    }
}
