using Core.Interfaces;
using HalconDotNet;
using System;

namespace Core.Halcon.Helpers
{
    /// <summary>
    /// 相机内参的**畸变换算**（唯一一份实现：标定插件与坐标变换插件共用）。
    ///
    /// 为什么放 Services\Core.Halcon 而不是各插件各写一份
    /// ---------
    /// 与 <see cref="Core.Interfaces.CalibrationMesh"/> 同一理由的 HALCON 版：
    /// **生产端与消费端必须是同一份实现**——两处各写一遍迟早分叉，而分叉的后果是
    /// "同一份标定在两个插件里算出不同坐标"这种静默不一致（本仓库反复立规要杜绝的形态）。
    /// 畸变换算必须调 HALCON（`change_radial_distortion_points`），契约程序集零 HALCON 依赖，
    /// 所以共享件落在这里（与 <see cref="AnnotationBaker"/> 的定位相同：跨插件的 HALCON 共享能力）。
    ///
    /// 两个方向只需对调 camParamIn / camParamOut
    /// ---------
    /// HALCON 的 `change_radial_distortion_points` 语义是"用 CamParamIn 把输入像素投到像平面、
    /// 再用 CamParamOut 投回另一张图的像素"。于是：
    /// · **去畸变**（畸变像素 → 理想针孔像素）：in = 带畸变的 camPar，out = 去畸变的 camPar；
    /// · **加畸变**（理想针孔像素 → 畸变像素）：in/out 对调。
    ///
    /// ⚠ 去畸变的 camPar 必须用 mode = `"fixed"`
    /// ---------
    /// `change_radial_distortion_cam_par` 的其它 mode（`fullsize` / `adaptive` / `preserve_resolution`）
    /// 会**改 sx/sy/cx/cy**——那意味着去畸变后的像素落在**另一个像素坐标系**里，
    /// 标定矩阵与 <see cref="CalibrationTransform.SourceImageWidth"/> 全部对不上。
    /// `"fixed"` 只改畸变系数、其余内参一字不动，帧定义不变（2026-10-16 实测：`fullsize` 把
    /// sx=5.2e-6 改成 5.3458e-6、主点也从 (640,512) 挪走）。
    ///
    /// 两个模型的**反解精度不同**（消费方按模型分档，别用同一个容差）
    /// ---------
    /// · `area_scan_division`：**可解析求逆**——实测往返误差 2.7e-13 px（1280×1024、kappa=-1500）；
    /// · `area_scan_polynomial`：**只能迭代反解**——同条件实测最坏 1.1e-4 px。
    /// 所以"正反互逆"类自校验的阈值必须按 <see cref="CameraIntrinsics.CameraType"/> 分档
    /// （见 `Plugin.PoseTransform.PoseTransformPlugin` 的回显自校验）。
    ///
    /// 线程与所有权：全部是纯函数（进出都是值数组，不持有 HALCON 句柄），无静态可变状态。
    /// </summary>
    public static class CameraDistortion
    {
        /// <summary>
        /// 由契约内参拼出 HALCON 的 CamPar tuple（**无损直译**，形状见 <see cref="CameraIntrinsics"/> 类注释）。
        /// 内参不良构 / 模型不认识 → false 并给出中文原因（与 <see cref="CameraIntrinsics.IsUsable"/> 同一份判据）。
        /// </summary>
        public static bool TryBuildCamPar(CameraIntrinsics? intrinsics, out HTuple camPar, out string? error)
        {
            camPar = new HTuple();
            if (!CameraIntrinsics.IsUsable(intrinsics, out error))
                return false;

            var c = intrinsics!;
            camPar = c.CameraType == CameraIntrinsics.DivisionType
                ? new HTuple(
                    CameraIntrinsics.DivisionType,
                    c.Focus, c.Kappa, c.Sx, c.Sy, c.Cx, c.Cy,
                    c.ImageWidth, c.ImageHeight)
                : new HTuple(
                    CameraIntrinsics.PolynomialType,
                    c.Focus,
                    c.Distortion![0], c.Distortion[1], c.Distortion[2], c.Distortion[3], c.Distortion[4],
                    c.Sx, c.Sy, c.Cx, c.Cy,
                    c.ImageWidth, c.ImageHeight);
            return true;
        }

        /// <summary>
        /// 去畸变后的 camPar（畸变系数置 0，其余内参一字不动）。
        /// 失败（内参不良构 / HALCON 拒绝）→ false 并给出中文原因。
        /// </summary>
        public static bool TryBuildUndistortedCamPar(CameraIntrinsics? intrinsics, out HTuple undistorted, out string? error)
        {
            undistorted = new HTuple();
            if (!TryBuildCamPar(intrinsics, out var camPar, out error))
                return false;

            try
            {
                // DistortionCoeffs 的个数**必须随模型**：division 只收 1 元、polynomial 只收 5 元，
                // 给错个数一律 #1403 "Wrong number of values of control parameter 3"
                //（2026-10-16 实测四组对照：div+1 成功 / div+5 失败 / poly+1 失败 / poly+5 成功）。
                var zeroCoeffs = intrinsics!.CameraType == CameraIntrinsics.PolynomialType
                    ? new HTuple(0.0, 0.0, 0.0, 0.0, 0.0)
                    : new HTuple(0.0);

                // "fixed"：只改畸变系数（见类注释——其它 mode 会改 sx/sy/cx/cy，帧定义就变了）
                var rect = new HCamPar(camPar).ChangeRadialDistortionCamPar("fixed", zeroCoeffs);
                undistorted = rect.RawData;
                return true;
            }
            catch (HalconException ex)
            {
                error = $"构造去畸变相机参数失败（HALCON #{ex.GetErrorCode()}：{ex.GetErrorMessage().Trim()}）"
                      + "——请重新运行「标定」步骤的内参标定";
                return false;
            }
        }

        /// <summary>
        /// **去畸变**：畸变图像像素 → 理想针孔像素（喂标定矩阵的那一侧）。
        /// 入参数组长度必须一致且非空；失败时给出中文原因（调用方直接上屏）。
        /// </summary>
        public static bool TryUndistortPoints(
            CameraIntrinsics? intrinsics, double[] rows, double[] cols,
            out double[] outRows, out double[] outCols, out string? error)
            => TryTransformPoints(intrinsics, rows, cols, undistort: true, out outRows, out outCols, out error);

        /// <summary>
        /// **加畸变**：理想针孔像素 → 畸变图像像素（把机械坐标反算回真实图像像素的那一侧）。
        /// </summary>
        public static bool TryDistortPoints(
            CameraIntrinsics? intrinsics, double[] rows, double[] cols,
            out double[] outRows, out double[] outCols, out string? error)
            => TryTransformPoints(intrinsics, rows, cols, undistort: false, out outRows, out outCols, out error);

        /// <summary>
        /// 单点去畸变（便利重载；内部走同一条批量路径，保证"单点"与"批量"逐位一致）。
        /// </summary>
        public static bool TryUndistortPoint(
            CameraIntrinsics? intrinsics, double row, double col,
            out double outRow, out double outCol, out string? error)
        {
            outRow = 0;
            outCol = 0;
            if (!TryUndistortPoints(intrinsics, new[] { row }, new[] { col }, out var rs, out var cs, out error))
                return false;
            outRow = rs[0];
            outCol = cs[0];
            return true;
        }

        /// <summary>单点加畸变（便利重载，同 <see cref="TryUndistortPoint"/> 的口径）。</summary>
        public static bool TryDistortPoint(
            CameraIntrinsics? intrinsics, double row, double col,
            out double outRow, out double outCol, out string? error)
        {
            outRow = 0;
            outCol = 0;
            if (!TryDistortPoints(intrinsics, new[] { row }, new[] { col }, out var rs, out var cs, out error))
                return false;
            outRow = rs[0];
            outCol = cs[0];
            return true;
        }

        /// <summary>
        /// 去畸变/加畸变的共同实现：只在 camParamIn / camParamOut 的对调上分叉。
        /// </summary>
        private static bool TryTransformPoints(
            CameraIntrinsics? intrinsics, double[] rows, double[] cols, bool undistort,
            out double[] outRows, out double[] outCols, out string? error)
        {
            outRows = Array.Empty<double>();
            outCols = Array.Empty<double>();

            if (rows == null || cols == null || rows.Length == 0 || rows.Length != cols.Length)
            {
                error = "去畸变点数非法（行列数组为空或长度不一致）：请检查调用方的输入";
                return false;
            }

            if (!TryBuildCamPar(intrinsics, out var camPar, out error))
                return false;

            if (!TryBuildUndistortedCamPar(intrinsics, out var undistorted, out error))
                return false;

            // 去畸变：in = 畸变帧、out = 理想帧；加畸变：对调
            var camParIn = undistort ? camPar : undistorted;
            var camParOut = undistort ? undistorted : camPar;

            try
            {
                HOperatorSet.ChangeRadialDistortionPoints(
                    new HTuple(rows), new HTuple(cols), camParIn, camParOut,
                    out HTuple newRows, out HTuple newCols);

                if (newRows.Length != rows.Length || newCols.Length != cols.Length)
                {
                    error = $"去畸变返回的点数不符（期望 {rows.Length}，实际 {newRows.Length}）"
                          + "——HALCON 相机参数可能已损坏，请重新运行内参标定";
                    return false;
                }

                var r = new double[rows.Length];
                var c = new double[cols.Length];
                for (int i = 0; i < rows.Length; i++)
                {
                    r[i] = newRows[i].D;
                    c[i] = newCols[i].D;
                    if (double.IsNaN(r[i]) || double.IsInfinity(r[i]) || double.IsNaN(c[i]) || double.IsInfinity(c[i]))
                    {
                        error = $"去畸变结果含非法数值（第 {i + 1} 点）：请检查该点是否落在相机模型的有效范围内"
                              + "（或重新运行内参标定）";
                        return false;
                    }
                }

                outRows = r;
                outCols = c;
                return true;
            }
            catch (HalconException ex)
            {
                error = $"去畸变换算失败（HALCON #{ex.GetErrorCode()}：{ex.GetErrorMessage().Trim()}）"
                      + "——请检查相机内参是否有效";
                return false;
            }
        }
    }
}
