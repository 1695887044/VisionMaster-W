using System;
using System.Collections.Generic;

namespace VisionMaster.Scada
{
    /// <summary>图元边上的连接口（管道连线的落点）。</summary>
    public enum ScadaPipePort
    {
        Top = 0,
        Right = 1,
        Bottom = 2,
        Left = 3,
    }

    /// <summary>管道走线方式。</summary>
    public enum ScadaPipeRouteMode
    {
        /// <summary>横平竖直 + 圆角拐点（工艺图默认）</summary>
        Orthogonal = 0,

        /// <summary>两点直连（斜线，用于示意）</summary>
        Straight = 1,
    }

    /// <summary>设计坐标里的一个点（管道走线只做几何，不依赖 WPF）。</summary>
    public readonly record struct ScadaPipePoint(double X, double Y);

    /// <summary>设计坐标里的一个矩形（图元外框）。</summary>
    public readonly record struct ScadaPipeRect(double X, double Y, double Width, double Height)
    {
        public double Right => X + Width;

        public double Bottom => Y + Height;

        public double CenterX => X + Width / 2d;

        public double CenterY => Y + Height / 2d;

        public bool IsEmpty => Width <= 0 || Height <= 0;
    }

    /// <summary>
    /// 管道走线（<b>纯几何，无 WPF、无 IO</b>）。
    ///
    /// 为什么要独立一层
    /// ---------
    /// 同一条路线有两个消费者：画布（算管道外框，好让图元仍是一个"有位置的子元素"）
    /// 与管道控件（算本地折点）。两处各写一遍，迟早出现"外框按一种算法、折线按另一种"，
    /// 表现为管线拖出框、或者选中框和线不重合。所以算法只在这里一份，断言也只测这一份。
    ///
    /// 三条口径
    /// ---------
    /// ① <b>出线方向跟着端口法线</b>：先沿法线走 <see cref="Stub"/> 再拐弯，线不会从设备身体里穿进去。
    /// ② <b>相对而望、间距不够出线时绕行</b>：两个端口互指但直段会打架，就绕到目标设备另一侧
    ///    （否则只能"倒着插回设备"）。
    /// ③ <b>避让只在穿模时启动</b>：给了一组障碍矩形时，基础路线穿模才改走"上方/下方（或左/右）"，
    ///    取第一条不穿模的；都不行就用基础路线（宁可穿模也不画一条乱绕的线）。
    /// </summary>
    public static class ScadaPipeRouter
    {
        /// <summary>端口出线直段长度（设计像素）</summary>
        public const double Stub = 22d;

        /// <summary>判断"同一水平/垂直线"的容差</summary>
        public const double Epsilon = 0.5d;

        /// <summary>端口在图元边框上的落点</summary>
        public static ScadaPipePoint PortPoint(ScadaPipeRect rect, ScadaPipePort port) => port switch
        {
            ScadaPipePort.Top => new ScadaPipePoint(rect.CenterX, rect.Y),
            ScadaPipePort.Bottom => new ScadaPipePoint(rect.CenterX, rect.Bottom),
            ScadaPipePort.Left => new ScadaPipePoint(rect.X, rect.CenterY),
            _ => new ScadaPipePoint(rect.Right, rect.CenterY),
        };

        /// <summary>端口法线（朝外）</summary>
        public static (double Nx, double Ny) Normal(ScadaPipePort port) => port switch
        {
            ScadaPipePort.Top => (0, -1),
            ScadaPipePort.Bottom => (0, 1),
            ScadaPipePort.Left => (-1, 0),
            _ => (1, 0),
        };

        /// <summary>
        /// 走线。<paramref name="obstacles"/> 为空 = 不避让；
        /// 端点自身所在的两个矩形由调用方排除（它们必然"穿模"）。
        /// </summary>
        public static IReadOnlyList<ScadaPipePoint> Route(
            ScadaPipeRect from,
            ScadaPipePort fromPort,
            ScadaPipeRect to,
            ScadaPipePort toPort,
            ScadaPipeRouteMode mode,
            IReadOnlyList<ScadaPipeRect>? obstacles = null)
        {
            var a = PortPoint(from, fromPort);
            var b = PortPoint(to, toPort);

            if (mode == ScadaPipeRouteMode.Straight)
                return Compact(new[] { a, b });

            var baseRoute = Orthogonal(a, fromPort, b, toPort, to);

            if (obstacles == null || obstacles.Count == 0)
                return baseRoute;

            if (IsClean(baseRoute, obstacles))
                return baseRoute;

            foreach (var candidate in Detours(a, fromPort, b, toPort))
            {
                if (IsClean(candidate, obstacles))
                    return candidate;
            }

            return baseRoute;
        }

        private static IReadOnlyList<ScadaPipePoint> Orthogonal(
            ScadaPipePoint a, ScadaPipePort fromPort,
            ScadaPipePoint b, ScadaPipePort toPort,
            ScadaPipeRect toRect)
        {
            var (anx, any) = Normal(fromPort);
            var (bnx, bny) = Normal(toPort);

            var s = new ScadaPipePoint(a.X + anx * Stub, a.Y + any * Stub);
            var e = new ScadaPipePoint(b.X + bnx * Stub, b.Y + bny * Stub);

            var points = new List<ScadaPipePoint>(8) { a, s };
            bool aHorizontal = Math.Abs(anx) > 0;
            bool bHorizontal = Math.Abs(bnx) > 0;

            if (aHorizontal && bHorizontal)
            {
                if (Math.Abs(s.Y - e.Y) > Epsilon)
                {
                    double midX = (s.X + e.X) / 2d;
                    bool okA = (midX - s.X) * anx >= -Epsilon;
                    bool okB = (midX - e.X) * bnx >= -Epsilon;

                    if (okA && okB)
                    {
                        points.Add(new ScadaPipePoint(midX, s.Y));
                        points.Add(new ScadaPipePoint(midX, e.Y));
                    }
                    else
                    {
                        // 相对而望但中间不够出线：绕到目标设备另一侧
                        double loopX = bnx < 0 ? toRect.X - Stub : toRect.Right + Stub;
                        points.Add(new ScadaPipePoint(loopX, s.Y));
                        points.Add(new ScadaPipePoint(loopX, e.Y));
                    }
                }
            }
            else if (!aHorizontal && !bHorizontal)
            {
                if (Math.Abs(s.X - e.X) > Epsilon)
                {
                    double midY = (s.Y + e.Y) / 2d;
                    bool okA = (midY - s.Y) * any >= -Epsilon;
                    bool okB = (midY - e.Y) * bny >= -Epsilon;

                    if (okA && okB)
                    {
                        points.Add(new ScadaPipePoint(s.X, midY));
                        points.Add(new ScadaPipePoint(e.X, midY));
                    }
                    else
                    {
                        double loopY = bny < 0 ? toRect.Y - Stub : toRect.Bottom + Stub;
                        points.Add(new ScadaPipePoint(s.X, loopY));
                        points.Add(new ScadaPipePoint(e.X, loopY));
                    }
                }
            }
            else
            {
                // 一横一竖：拐点固定在对端的 X、本端出线直段的 Y。
                // 两个方向都适用：横向出线先横着走到对端 X 再竖着进；竖向出线先竖着出直段、
                // 再横着走到对端 X、最后竖着进。写成同一个点，就不会出现"拐回去穿过自己"的线。
                points.Add(new ScadaPipePoint(e.X, s.Y));
            }

            points.Add(e);
            points.Add(b);
            return Compact(points);
        }

        private static IEnumerable<IReadOnlyList<ScadaPipePoint>> Detours(
            ScadaPipePoint a, ScadaPipePort fromPort,
            ScadaPipePoint b, ScadaPipePort toPort)
        {
            var (anx, any) = Normal(fromPort);
            var (bnx, bny) = Normal(toPort);
            var s = new ScadaPipePoint(a.X + anx * Stub, a.Y + any * Stub);
            var e = new ScadaPipePoint(b.X + bnx * Stub, b.Y + bny * Stub);

            if (Math.Abs(anx) > 0)
            {
                // 横向出线：绕上方 / 绕下方
                double top = Math.Min(a.Y, b.Y) - 3 * Stub;
                double bottom = Math.Max(a.Y, b.Y) + 3 * Stub;

                yield return Compact(new[] { a, s, new ScadaPipePoint(s.X, top), new ScadaPipePoint(e.X, top), e, b });
                yield return Compact(new[] { a, s, new ScadaPipePoint(s.X, bottom), new ScadaPipePoint(e.X, bottom), e, b });
            }
            else
            {
                double left = Math.Min(a.X, b.X) - 3 * Stub;
                double right = Math.Max(a.X, b.X) + 3 * Stub;

                yield return Compact(new[] { a, s, new ScadaPipePoint(left, s.Y), new ScadaPipePoint(left, e.Y), e, b });
                yield return Compact(new[] { a, s, new ScadaPipePoint(right, s.Y), new ScadaPipePoint(right, e.Y), e, b });
            }
        }

        private static bool IsClean(IReadOnlyList<ScadaPipePoint> points, IReadOnlyList<ScadaPipeRect> obstacles)
        {
            for (var i = 1; i < points.Count; i++)
            {
                foreach (var rect in obstacles)
                {
                    if (SegmentHitsRect(points[i - 1], points[i], rect))
                        return false;
                }
            }

            return true;
        }

        private static bool SegmentHitsRect(ScadaPipePoint p1, ScadaPipePoint p2, ScadaPipeRect rect)
        {
            double minX = Math.Min(p1.X, p2.X), maxX = Math.Max(p1.X, p2.X);
            double minY = Math.Min(p1.Y, p2.Y), maxY = Math.Max(p1.Y, p2.Y);

            return maxX > rect.X && minX < rect.Right && maxY > rect.Y && minY < rect.Bottom;
        }

        /// <summary>去掉重合点（相邻两点距离小于容差视为同一点）</summary>
        private static IReadOnlyList<ScadaPipePoint> Compact(IReadOnlyList<ScadaPipePoint> points)
        {
            var result = new List<ScadaPipePoint>(points.Count);

            foreach (var point in points)
            {
                if (result.Count > 0)
                {
                    var last = result[result.Count - 1];
                    if (Math.Abs(last.X - point.X) < Epsilon && Math.Abs(last.Y - point.Y) < Epsilon)
                        continue;
                }

                result.Add(point);
            }

            return result;
        }

        /// <summary>折线的外框（画布用它给管道图元定位；宽高至少 1，避免零尺寸控件）</summary>
        public static ScadaPipeRect Bounds(IReadOnlyList<ScadaPipePoint> points)
        {
            if (points == null || points.Count == 0)
                return new ScadaPipeRect(0, 0, 1, 1);

            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;

            foreach (var point in points)
            {
                if (point.X < minX) minX = point.X;
                if (point.Y < minY) minY = point.Y;
                if (point.X > maxX) maxX = point.X;
                if (point.Y > maxY) maxY = point.Y;
            }

            return new ScadaPipeRect(minX, minY, Math.Max(1, maxX - minX), Math.Max(1, maxY - minY));
        }

        /// <summary>折线总长（页脚/属性面板显示"管长 412px"）</summary>
        public static double Length(IReadOnlyList<ScadaPipePoint> points)
        {
            if (points == null || points.Count < 2)
                return 0;

            double total = 0;
            for (var i = 1; i < points.Count; i++)
                total += Math.Sqrt(
                    Math.Pow(points[i].X - points[i - 1].X, 2) +
                    Math.Pow(points[i].Y - points[i - 1].Y, 2));

            return total;
        }

        /// <summary>按比例取折线上的一点（标签定位用；ratio 夹到 0~1）</summary>
        public static ScadaPipePoint PointAt(IReadOnlyList<ScadaPipePoint> points, double ratio)
        {
            if (points == null || points.Count == 0)
                return default;

            if (points.Count == 1)
                return points[0];

            ratio = Math.Clamp(ratio, 0d, 1d);
            double target = Length(points) * ratio;
            double walked = 0;

            for (var i = 1; i < points.Count; i++)
            {
                var p1 = points[i - 1];
                var p2 = points[i];
                double segment = Math.Sqrt(Math.Pow(p2.X - p1.X, 2) + Math.Pow(p2.Y - p1.Y, 2));

                if (segment <= 0)
                    continue;

                if (walked + segment >= target)
                {
                    double t = (target - walked) / segment;
                    return new ScadaPipePoint(p1.X + (p2.X - p1.X) * t, p1.Y + (p2.Y - p1.Y) * t);
                }

                walked += segment;
            }

            return points[points.Count - 1];
        }

        /// <summary>端口名 ↔ 枚举（落盘用字符串，读回容忍大小写与空白）</summary>
        public static ScadaPipePort ParsePort(string? text, ScadaPipePort fallback = ScadaPipePort.Right)
            => Enum.TryParse<ScadaPipePort>((text ?? string.Empty).Trim(), ignoreCase: true, out var port)
                ? port
                : fallback;

        /// <summary>走线方式 ↔ 枚举</summary>
        public static ScadaPipeRouteMode ParseMode(string? text, ScadaPipeRouteMode fallback = ScadaPipeRouteMode.Orthogonal)
            => Enum.TryParse<ScadaPipeRouteMode>((text ?? string.Empty).Trim(), ignoreCase: true, out var mode)
                ? mode
                : fallback;
    }
}
