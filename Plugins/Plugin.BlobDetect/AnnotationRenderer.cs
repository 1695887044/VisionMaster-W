using System;
using Core.Halcon.Helpers;
using HalconDotNet;

namespace Plugin.BlobDetect
{
    /// <summary>
    /// 标注图渲染器：把"底图 + 缺陷红圈 + 逐个编号 + 清单联动高亮 + 左上角判定文字"
    /// 合成成一张可显示的 HImage。
    ///
    /// 本类只负责**画什么**（攒一份 <see cref="BakePlan"/>）；开窗/复用/SetPart/回读/释放这些**怎么画**
    /// 全在共享烘焙件 <see cref="AnnotationBaker"/> 里（六条硬纪律逐条落实在那儿，见其类注释）。
    ///
    /// 2026-10-13 迁入共享烘焙件：此前这里自带一份约 90 行、与另三个插件逐字相同的窗口基础设施，
    /// 而且**漏了 SetPart**（烘焙件纪律 3）——标注底图整片塌成窗口底色，FlowCanvasChecks 实测
    /// `(150,20)=0 (150,180)=0`（两档图，期望 100/200），连带把 BlobDetectChecks 那条
    /// "16 位图标注底图按实际灰度范围拉伸"逼成了"本环境离屏窗口不渲染底图层次"的**跳过**。
    /// 迁移后两条都转绿——那条"环境闸门"从来不是环境问题。
    /// </summary>
    internal sealed class AnnotationRenderer : IDisposable
    {
        private readonly AnnotationBaker _baker = new();

        /// <summary>
        /// 渲染标注图。返回的 HImage 所有权交给调用方；失败返回 null（由调用方决定是否降级）。
        /// </summary>
        /// <param name="baseImage">底图（原图或灰度图，尺寸即画布尺寸；本方法不释放它，所有权在调用方）。
        /// 通道数/位深不限，烘焙件内部会归一化（4 通道丢 alpha、非 byte 按实际灰度范围线性拉伸），
        /// 调用方不需要自己先转一遍</param>
        /// <param name="defects">缺陷区域（可为 null 或空集，空集不画）</param>
        /// <param name="lines">左上角多行文字</param>
        /// <param name="textColor">文字颜色（HALCON 颜色名，如 "green"/"red"）</param>
        /// <param name="highlight">清单联动高亮：被选中缺陷的外扩包围盒（黄粗框），null = 不画</param>
        public HImage? Render(HObject baseImage, HObject? defects, string[] lines, string textColor, HObject? highlight = null)
        {
            var plan = new BakePlan();

            // 缺陷描边：只画边界（margin），不遮挡产品图像，便于人眼判断缺陷位置
            if (defects != null && defects.IsInitialized())
                plan.Objects.Add(new BakeObj(defects, "red", 2, "margin"));

            // 清单联动高亮：黄粗框套在被选中的那根缺陷上（外扩包围盒，随框走）。
            // 烘焙件的绘制次序固定是"几何 → 对象 → 文字"，所以编号落在黄框之后 = 压得住框、不会被框盖掉
            if (highlight != null && highlight.IsInitialized())
                plan.Objects.Add(new BakeObj(highlight, "yellow", 3, "margin"));

            AddDefectNumbers(plan, defects);

            // 左上角判定横幅：白底框，任何背景上都读得清。字号 20 与迁移前一致
            plan.AddTextBlock(lines, textColor, 12, 12, 20, box: true);

            return _baker.Bake(baseImage, plan);
        }

        /// <summary>
        /// 逐个缺陷写编号（1 基，顺序 = 对象集顺序 = CenterRows / DefectAreas 等输出端口的顺序）。
        ///
        /// 为什么必须有：输出排序提供了"最大缺陷排第一 / 按行列编号"的语义，但图上不写号，
        /// 报表说 3 号超标时操作员在图上找不到 3 号——排序链路必须在标注图这最后一步落地。
        ///
        /// 锚点用外接矩形中心而不是质心：细长 / L 形区域的质心可能漂到区域外甚至图外，
        /// 外接矩形中心永远落在缺陷包围盒内，与红色描边的从属关系一眼可辨。
        ///
        /// 出图钳制与非有限值兜底都交给烘焙件（只有它知道画布尺寸），单个编号画不出来只丢那一个号——
        /// 烘焙件对每个元素都单独 try，绝不会让整张标注图失败（DefectImage 空掉 = 全部可视化都没了）。
        ///
        /// box:false —— 不画白底框：白框会把小缺陷整个盖住；红字与红描边同色已足够醒目。
        /// </summary>
        private static void AddDefectNumbers(BakePlan plan, HObject? defects)
        {
            if (defects == null || !defects.IsInitialized()) return;

            HOperatorSet.CountObj(defects, out HTuple count);
            int n = count.I;
            if (n <= 0) return;

            HOperatorSet.SmallestRectangle1(defects, out HTuple r1, out HTuple c1, out HTuple r2, out HTuple c2);
            for (int i = 0; i < n; i++)
            {
                plan.Texts.Add(new BakeText(
                    (i + 1).ToString(),
                    (r1[i].D + r2[i].D) / 2.0,
                    (c1[i].D + c2[i].D) / 2.0,
                    "red", 20, Box: false));
            }
        }

        public void Dispose() => _baker.Dispose();
    }
}
