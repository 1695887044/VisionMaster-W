using System;
using Core.Halcon.Helpers;
using HalconDotNet;

namespace Plugin.CaliperMeasure
{
    /// <summary>
    /// 标注图渲染器：把"底图 + 卡尺小矩形 + 测量连线 + 拟合几何 + 边缘点十字 + 左上角文字"
    /// 合成成一张可显示的 HImage。
    ///
    /// 本类只负责**画什么**（攒一份 <see cref="BakePlan"/>）；开窗/复用/SetPart/回读/释放这些**怎么画**
    /// 全在共享烘焙件 <see cref="AnnotationBaker"/> 里（六条硬纪律逐条落实在那儿，见其类注释）。
    ///
    /// 2026-10-13 迁入共享烘焙件：此前这里自带一份约 90 行、与另三个插件逐字相同的窗口基础设施，
    /// 而且**漏了 SetPart**（烘焙件纪律 3）——标注底图整片塌成窗口底色，
    /// FlowCanvasChecks 实测 `(470,320)=0`（亮带内，期望 255）。迁移后同一条断言转绿。
    /// </summary>
    internal sealed class AnnotationRenderer : IDisposable
    {
        private readonly AnnotationBaker _baker = new();

        /// <summary>
        /// 渲染标注图。返回的 HImage 所有权交给调用方；失败返回 null（由调用方决定是否降级）。
        /// </summary>
        /// <param name="baseImage">底图（原图或灰度图，尺寸即画布尺寸；本方法不释放它，所有权在调用方）。
        /// 通道数/位深不限，烘焙件内部会归一化，调用方不需要自己先转 byte</param>
        /// <param name="caliperRects">卡尺位置小矩形（XLD 轮廓，可为 null/空集；空集不画）</param>
        /// <param name="measureLines">测量连线（XLD 轮廓，可为 null/空集；空集不画）——量的是哪两点/哪段距离</param>
        /// <param name="fittedGeometry">拟合几何（XLD，可为 null/空集；空集不画）——拟合出的直线/圆</param>
        /// <param name="edgePoints">边缘点十字（XLD，可为 null/空集；空集不画）</param>
        /// <param name="lines">左上角多行文字</param>
        /// <param name="textColor">文字颜色（HALCON 颜色名，如 "green"/"red"）</param>
        public HImage? Render(HObject baseImage, HObject? caliperRects, HObject? measureLines, HObject? fittedGeometry, HObject? edgePoints, string[] lines, string textColor)
        {
            var plan = new BakePlan();

            // 清单顺序 = 叠放顺序。全部 margin 描边：只画轮廓，不遮挡被检特征。
            AddLayer(plan, caliperRects, "yellow", 1);      // 卡尺位置小矩形
            AddLayer(plan, measureLines, "cyan", 2);        // 测量连线：量的是哪两点/哪段距离
            // 拟合几何与"测量连线"分层着色，一眼区分"拟合到的几何"与"测量的那段距离"
            AddLayer(plan, fittedGeometry, "magenta", 2);
            AddLayer(plan, edgePoints, "green", 2);         // 边缘点：找没找到、找到在哪

            // 白底框：复杂背景上也要读得清。字号 20 与迁移前一致
            plan.AddTextBlock(lines, textColor, 12, 12, 20, box: true);

            return _baker.Bake(baseImage, plan);
        }

        /// <summary>
        /// 缺席的图层不进计划：null/未初始化在这里挡掉，免得烘焙件逐条告警"对象元素未初始化"
        /// （空对象集不用挡——画它是合法的，烘焙件自己会跳过且不告警）。
        /// </summary>
        private static void AddLayer(BakePlan plan, HObject? obj, string color, int lineWidth)
        {
            if (obj == null || !obj.IsInitialized()) return;
            plan.Objects.Add(new BakeObj(obj, color, lineWidth, "margin"));
        }

        public void Dispose() => _baker.Dispose();
    }
}
