using System;
using System.Collections.Generic;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// 报警条的自动滚动计算（纯函数，供控件与断言共用）。
    ///
    /// 为什么按"累计时长"算而不是"每拍 +1"
    /// =========
    /// 节拍源给的是累计时长；按拍累加会在漏拍、窗口拖动、卡顿时越走越偏，
    /// 而按累计时长取模永远落在正确的那一行上（与闪烁相位同一个理由，见 OnBeat 的注释）。
    ///
    /// 口径
    /// =========
    /// · 行数不足一屏时不滚（<see cref="RowIndex"/> 返回 0）；
    /// · 滚到底回顶，不留空白：可滚位置数 = 行数 − 可见行数 + 1；
    /// · <c>secondsPerRow</c> ≤ 0 时按默认值处理，绝不除零。
    /// </summary>
    public static class AlarmBannerScroll
    {
        /// <summary>每行停留的默认秒数</summary>
        public const double DefaultSecondsPerRow = 2.5;

        /// <summary>行间距（行高 44 + 间距 3），与模板里的卡片高度一致</summary>
        public const double RowStride = 47d;

        /// <summary>当前应停在的滚动位置序号（0 起，已按可滚范围取模）</summary>
        public static int RowIndex(double elapsedSeconds, double secondsPerRow, int scrollPositions)
        {
            if (scrollPositions <= 1)
                return 0;

            var seconds = double.IsFinite(secondsPerRow) && secondsPerRow > 0
                ? secondsPerRow
                : DefaultSecondsPerRow;

            if (!double.IsFinite(elapsedSeconds) || elapsedSeconds < 0)
                elapsedSeconds = 0;

            var step = (int)Math.Floor(elapsedSeconds / seconds);
            var index = step % scrollPositions;

            return index < 0 ? index + scrollPositions : index;
        }

        /// <summary>可滚动的位置数（行数 − 可见行数 + 1）；不足一屏返回 0</summary>
        public static int ScrollPositions(int rowCount, int visibleRows)
        {
            if (rowCount <= 0 || visibleRows <= 0)
                return 0;

            return Math.Max(0, rowCount - visibleRows + 1);
        }

        /// <summary>纵向滚动偏移（像素）</summary>
        public static double Offset(double elapsedSeconds, double secondsPerRow, int rowCount, int visibleRows)
            => RowIndex(elapsedSeconds, secondsPerRow, ScrollPositions(rowCount, visibleRows)) * RowStride;

        // ------------------------------------------------------------------
        //  横向跑马灯（单行，从右往左）
        // ------------------------------------------------------------------

        /// <summary>跑马灯默认速度（像素/秒）</summary>
        public const double DefaultTickerSpeed = 60d;

        /// <summary>
        /// 跑马灯的横向位移（像素，负值 = 已向左移）。
        ///
        /// 从"视口右缘"出发，走过 `文本宽 + 视口宽` 算一圈，然后回到起点——
        /// 这样文字完全离开左边之后才会从右边重新进来，中间不留空白。
        /// 同样按累计时长取模：漏拍/卡顿不会越走越偏。
        /// </summary>
        public static double TickerX(double elapsedSeconds, double speed, double textWidth, double viewportWidth)
        {
            if (!double.IsFinite(textWidth) || textWidth <= 0)
                return 0;

            if (!double.IsFinite(viewportWidth) || viewportWidth <= 0)
                viewportWidth = 0;

            var v = double.IsFinite(speed) && speed > 0 ? speed : DefaultTickerSpeed;
            var t = double.IsFinite(elapsedSeconds) && elapsedSeconds > 0 ? elapsedSeconds : 0;

            var cycle = textWidth + viewportWidth;
            if (cycle <= 0)
                return 0;

            var travelled = (t * v) % cycle;

            return viewportWidth - travelled;
        }

        /// <summary>把若干条报警文字拼成一行（用分隔符隔开；空项跳过）</summary>
        public static string JoinTicker(IEnumerable<string?> parts, string separator = "　·　")
        {
            if (parts == null)
                return string.Empty;

            var list = new List<string>();

            foreach (var part in parts)
            {
                var text = (part ?? string.Empty).Trim();
                if (text.Length > 0)
                    list.Add(text);
            }

            return string.Join(separator, list);
        }

        /// <summary>
        /// 扣掉"被暂停"的时长后的有效累计秒数。
        ///
        /// 为什么不是"暂停时清零重来"：鼠标移开的一瞬间文字会跳回起点，读的人会跟丢。
        /// 扣掉暂停时长 = 从暂停位置继续走，这正是"悬浮让人看完再走"想要的效果。
        /// 参数非法（负数 / NaN）时按 0 处理，绝不把位移算成负。
        /// </summary>
        public static double EffectiveSeconds(double elapsedSeconds, double pausedSeconds)
        {
            var elapsed = double.IsFinite(elapsedSeconds) && elapsedSeconds > 0 ? elapsedSeconds : 0;
            var paused = double.IsFinite(pausedSeconds) && pausedSeconds > 0 ? pausedSeconds : 0;

            return Math.Max(0, elapsed - paused);
        }

        /// <summary>
        /// 空态该显示哪句话（2026-10-26 会审 P1）。
        ///
        /// "没有报警"与"根本没接报警源"是两件事：前者可以放心走开，后者是<b>静默假正常</b>——
        /// 工程忘了启用报警时，界面照样显示"系统正常"，比不显示更危险。
        /// </summary>
        public static string EmptyHintFor(bool hasAlarmSource, string? emptyText)
        {
            if (!hasAlarmSource)
                return "未接入报警源（设计期，或方案未启用报警）";

            return string.IsNullOrWhiteSpace(emptyText) ? "系统正常" : emptyText!.Trim();
        }
    }
}
