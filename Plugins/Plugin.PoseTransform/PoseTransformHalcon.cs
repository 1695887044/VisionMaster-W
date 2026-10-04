using System;
using HalconDotNet;

namespace Plugin.PoseTransform
{
    /// <summary>
    /// "位姿作用"的 HALCON 实现（与纯托管数学分开：这一层只做区域/图像变换，便于断言直呼）。
    ///
    /// 唯一公式（方案 §六·1，仓库既有脚本范式见 ScriptTemplates.cs:420）：
    ///   vector_angle_to_rigid(模板参考点, 0°, 当前位姿) → HomMat2D
    ///   affine_trans_region(基准 ROI, HomMat2D)          → 当前坐标系下的 ROI
    ///
    /// 角度单位只在这一层换算：插件侧一律**度**，HALCON 侧要**弧度**（×π/180）。
    /// </summary>
    public static class PoseTransformHalcon
    {
        /// <summary>
        /// 位姿跟随：基准 ROI（画在模板图上、模板坐标系）× 当前位姿 → 当前坐标系下的 ROI。
        /// 模板态（位姿=模板参考点、角度 0）时是恒等变换 → 输出与基准 ROI 完全重合（验收自检）。
        /// </summary>
        public static HRegion FollowRegion(
            HRegion baseRegion,
            double templateRefRow, double templateRefCol,
            double poseRow, double poseCol, double poseAngleDegrees)
        {
            HOperatorSet.VectorAngleToRigid(
                templateRefRow, templateRefCol, 0,
                poseRow, poseCol, poseAngleDegrees * Math.PI / 180.0,
                out HTuple hom);

            HOperatorSet.AffineTransRegion(baseRegion, out HObject followed, hom, "nearest_neighbor");
            return new HRegion(followed);
        }

        /// <summary>
        /// 对齐图（可选输出）：把当前图**反向**重采样回模板参考位姿。
        ///
        /// 方向说明（实现勘误，见变更记录）：
        /// 区域活在模板坐标系、图活在当前坐标系——"同一变换"对两者的作用方向互逆：
        ///   · 区域：用正变换 T（模板 → 当前）；
        ///   · 图  ：用逆变换 T⁻¹（当前 → 模板），才叫"对齐"。
        /// 若对图也用 T，内容会被推离参考位姿**两倍**偏差（看着像"跟反了"）。
        /// </summary>
        public static HImage FollowImageBack(
            HImage src,
            double templateRefRow, double templateRefCol,
            double poseRow, double poseCol, double poseAngleDegrees)
        {
            HOperatorSet.VectorAngleToRigid(
                poseRow, poseCol, poseAngleDegrees * Math.PI / 180.0,
                templateRefRow, templateRefCol, 0,
                out HTuple hom);

            // 参数顺序（实测踩坑，MatchingPlugin.cs:1577 有记录）：(HomMat2D, Interpolate, AdaptImageSize)；
            // "false" = 输出与输入同幅面（目标回参考位姿、画幅不变），出界部分填黑
            HOperatorSet.AffineTransImage(src, out HObject followed, hom, "bilinear", "false");
            return new HImage(followed);
        }
    }
}
