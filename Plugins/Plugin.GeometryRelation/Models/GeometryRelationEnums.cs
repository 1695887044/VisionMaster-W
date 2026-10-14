using System.ComponentModel.DataAnnotations;

namespace Plugin.GeometryRelation
{
    /// <summary>几何计算的关系模式（单算子多模式，仿卡尺的 MeasureKind）。</summary>
    public enum GeometryOp
    {
        /// <summary>距离：点-点 / 点-线 / 平行线距（单位=像素，接当量后 mm）</summary>
        [Display(Name = "距离")]
        Distance = 0,

        /// <summary>夹角：两线（无向夹角，[0,90] 度）</summary>
        [Display(Name = "夹角")]
        Angle = 1,

        /// <summary>求交：线-线 / 线-圆 / 圆-圆（输出交点）</summary>
        [Display(Name = "求交")]
        Intersect = 2,

        /// <summary>垂足：点到直线的垂足（输出点）</summary>
        [Display(Name = "垂足")]
        PerpendicularFoot = 3,

        /// <summary>平行/垂直判定：两线（输出 IsOk）</summary>
        [Display(Name = "平行/垂直")]
        LineRelation = 4,

        /// <summary>圆关系：圆心距（Value）+ 同心判定（IsOk）</summary>
        [Display(Name = "圆关系")]
        CircleRelation = 5,

        /// <summary>构造：过两点构线 / 三点构圆（输出基元）</summary>
        [Display(Name = "构造")]
        Construct = 6,
    }

    /// <summary>平行/垂直判定的目标关系。</summary>
    public enum LineRelationKind
    {
        [Display(Name = "平行")]
        Parallel = 0,

        [Display(Name = "垂直")]
        Perpendicular = 1,
    }

    /// <summary>构造方式。</summary>
    public enum ConstructKind
    {
        /// <summary>过两点构线（A、B 为点）</summary>
        [Display(Name = "过两点构线")]
        LineThroughTwoPoints = 0,

        /// <summary>三点构圆（A、B、C 为点）</summary>
        [Display(Name = "三点构圆")]
        CircleThroughThreePoints = 1,
    }
}
