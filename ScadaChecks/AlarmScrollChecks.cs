using System;
using VisionMaster.Scada.Controls;

namespace ScadaChecks
{
    internal partial class Program
    {
        /// <summary>
        /// [AB-S] 报警条自动滚动的纯计算（2026-10-25 用户反馈"效果不理想"后的改法）。
        ///
        /// 判据只落在"该停在哪一行"这件事上：按累计时长取模、到底回顶、不足一屏不滚。
        /// 真正的 ScrollViewer 偏移由控件每拍应用，断言不依赖窗口。
        /// </summary>
        private static class AlarmScrollChecks
        {
            public static void Run()
            {
                Section("[AB-S] 报警条自动滚动");

                Check("[AB-S1] 按累计时长推进：0s→第1行、2.5s→第2行、到底回顶",
                    AlarmBannerScroll.RowIndex(0, 2.5, 4) == 0
                    && AlarmBannerScroll.RowIndex(2.4, 2.5, 4) == 0
                    && AlarmBannerScroll.RowIndex(2.6, 2.5, 4) == 1
                    && AlarmBannerScroll.RowIndex(10.1, 2.5, 4) == 0,
                    $"{AlarmBannerScroll.RowIndex(0, 2.5, 4)}/{AlarmBannerScroll.RowIndex(2.6, 2.5, 4)}/{AlarmBannerScroll.RowIndex(10.1, 2.5, 4)}");

                Check("[AB-S2] 不足一屏不滚（可滚位置 = 行数 − 可见行数 + 1；≤1 时恒为 0）",
                    AlarmBannerScroll.ScrollPositions(10, 4) == 7
                    && AlarmBannerScroll.ScrollPositions(3, 4) == 0
                    && AlarmBannerScroll.ScrollPositions(4, 4) == 1
                    && AlarmBannerScroll.RowIndex(99, 2.5, 1) == 0,
                    $"{AlarmBannerScroll.ScrollPositions(10, 4)}/{AlarmBannerScroll.ScrollPositions(3, 4)}");

                Check("[AB-S3] 偏移 = 行号 × 47px；每行秒数非法时回落默认值、绝不除零",
                    Math.Abs(AlarmBannerScroll.Offset(5.2, 2.5, 10, 4) - 2 * AlarmBannerScroll.RowStride) < 1e-9
                    && AlarmBannerScroll.RowIndex(5, 0, 10) == AlarmBannerScroll.RowIndex(5, AlarmBannerScroll.DefaultSecondsPerRow, 10)
                    && AlarmBannerScroll.RowIndex(-3, 2.5, 10) == 0,
                    $"offset={AlarmBannerScroll.Offset(5.2, 2.5, 10, 4)}");

                // ---------------- 横向跑马灯（2026-10-25 用户裁定：从右往左、不带确认） ----------------

                var x0 = AlarmBannerScroll.TickerX(0, 60, 400, 320);
                var x1 = AlarmBannerScroll.TickerX(1, 60, 400, 320);
                var xWrap = AlarmBannerScroll.TickerX((400 + 320) / 60.0, 60, 400, 320);

                Check("[AB-T1] 跑马灯从右缘出发向左走；走过「文本宽 + 视口宽」回到起点（不留空白）",
                    Math.Abs(x0 - 320) < 1e-9
                    && Math.Abs(x1 - 260) < 1e-9
                    && Math.Abs(xWrap - 320) < 1e-6
                    && AlarmBannerScroll.TickerX(1, 0, 400, 320) == AlarmBannerScroll.TickerX(1, AlarmBannerScroll.DefaultTickerSpeed, 400, 320),
                    $"x0={x0} x1={x1} wrap={xWrap:0.###}");

                // 空态文案（2026-10-26 会审 P1）：没接报警源 ≠ 系统正常
                Check("[AB-E1] 空态区分「未接报警源」与「系统正常」（前者是静默假正常）",
                    AlarmBannerScroll.EmptyHintFor(false, "系统正常").Contains("未接入报警源")
                    && AlarmBannerScroll.EmptyHintFor(true, "系统正常") == "系统正常"
                    && AlarmBannerScroll.EmptyHintFor(true, "  ") == "系统正常"
                    && AlarmBannerScroll.EmptyHintFor(true, null) == "系统正常",
                    AlarmBannerScroll.EmptyHintFor(false, null) + " / " + AlarmBannerScroll.EmptyHintFor(true, null));

                Check("[AB-T2] 文字为空时不产生位移（不画一个看不见的滚动）；拼接跳过空项",
                    AlarmBannerScroll.TickerX(5, 60, 0, 320) == 0
                    && AlarmBannerScroll.JoinTicker(new[] { "A", "", "  ", "B" }) == "A　·　B",
                    AlarmBannerScroll.JoinTicker(new[] { "A", "", "B" }));

                // 悬浮暂停（2026-10-26）：扣掉暂停时长 = 从暂停位置继续走，不是清零重来
                Check("[AB-T3] 悬浮暂停：有效时长 = 累计 − 暂停（恢复后不跳回起点）；负数/NaN 按 0",
                    Math.Abs(AlarmBannerScroll.EffectiveSeconds(10, 4) - 6) < 1e-9
                    && Math.Abs(AlarmBannerScroll.EffectiveSeconds(3, 5) - 0) < 1e-9
                    && Math.Abs(AlarmBannerScroll.EffectiveSeconds(-2, 1) - 0) < 1e-9
                    && Math.Abs(AlarmBannerScroll.EffectiveSeconds(double.NaN, double.NaN) - 0) < 1e-9
                    && Math.Abs(AlarmBannerScroll.TickerX(6, 60, 400, 320) + 40) < 1e-9,
                    $"eff={AlarmBannerScroll.EffectiveSeconds(10, 4)}");
            }
        }
    }
}
