using System;
using Core.Interfaces;
using Newtonsoft.Json;
using Plugin.GeometryRelation.Services;
using static FlowCanvasChecks.Program;

namespace FlowCanvasChecks
{
    /// <summary>
    /// 几何工具（Plugin.GeometryRelation / Plugin.GeometryFinder）断言——**纯托管层**。
    ///
    /// 只钉死两件"改坏了却很难被发现"的面：
    ///   · <see cref="GeometryMath"/> 的公式（求交/垂足/三点构圆/夹角 [0,90]/距离）——用合成已知量对解析解；
    ///   · <see cref="GeometryParam"/> 契约（工厂构造 / Phi 归一化 [0,180) / JSON 往返）。
    ///
    /// 为什么用合成数据：几何真值无法从真图构造，而合成数据能把误差控到 1e-9，才守得住
    /// "公式写反 / 坐标轴交换 / 夹角补角差 180°" 这类静默错误。
    /// HALCON 找边/拟合（GeometryFinder 的算子层）不在此断言——它依赖真算子，另由现场/探针验。
    /// </summary>
    internal static class GeometryChecks
    {
        private const double Eps = 1e-9;

        public static void Run()
        {
            Section("[GEO] 几何工具：GeometryMath 纯函数 + GeometryParam 契约");

            // ── 距离 ──
            var p00 = GeometryParam.Point(0, 0);
            var p34 = GeometryParam.Point(3, 4);
            Check("点-点距 (0,0)-(3,4) = 5",
                Math.Abs(GeometryMath.DistancePointPoint(p00, p34) - 5.0) < Eps,
                GeometryMath.DistancePointPoint(p00, p34).ToString("0.####"));

            // 水平直线（phi=0 → 方向 +Col，即 row=0 这条线），点 (5,0) 到它 = 5
            var hLine = GeometryParam.Line(0, 0, 0);
            Check("点-线距 点(5,0) 到 row=0 水平线 = 5",
                Math.Abs(GeometryMath.DistancePointLine(GeometryParam.Point(5, 0), hLine) - 5.0) < Eps,
                GeometryMath.DistancePointLine(GeometryParam.Point(5, 0), hLine).ToString("0.####"));

            // ── 夹角（无向 [0,90]） ──
            Check("夹角 0° 与 90° = 90", Math.Abs(GeometryMath.AngleBetweenLines(GeometryParam.Line(0, 0, 0), GeometryParam.Line(0, 0, 90)) - 90) < Eps, "");
            Check("夹角 0° 与 10° = 10", Math.Abs(GeometryMath.AngleBetweenLines(GeometryParam.Line(0, 0, 0), GeometryParam.Line(0, 0, 10)) - 10) < Eps, "");
            Check("夹角 0° 与 170° = 10（补角归一，不是 170）",
                Math.Abs(GeometryMath.AngleBetweenLines(GeometryParam.Line(0, 0, 0), GeometryParam.Line(0, 0, 170)) - 10) < Eps, "");
            Check("夹角恒在 [0,90]", GeometryMath.AngleBetweenLines(GeometryParam.Line(0, 0, 30), GeometryParam.Line(0, 0, 120)) <= 90 + Eps, "");

            // ── 线-线求交 ──
            // 线1：row=5（phi=0，方向 +Col）；线2：col=3（phi=90，方向 +Row）→ 交点 (5,3)
            var ok = GeometryMath.TryIntersectLines(GeometryParam.Line(5, 0, 0), GeometryParam.Line(0, 3, 90), out var ir, out var ic);
            Check("线-线求交 row=5 与 col=3 → (5,3)", ok && Math.Abs(ir - 5) < 1e-6 && Math.Abs(ic - 3) < 1e-6, $"({ir:0.###},{ic:0.###})");

            // 近平行 → 明确 false（不硬算）
            var parallel = GeometryMath.TryIntersectLines(GeometryParam.Line(0, 0, 0), GeometryParam.Line(1, 0, 0), out _, out _);
            Check("平行线求交 → false（明确失败）", !parallel, "");

            // ── 垂足 ──
            var (fr, fc) = GeometryMath.PerpendicularFoot(GeometryParam.Point(5, 0), hLine);
            Check("垂足 点(5,0) → row=0 线上 = (0,0)", Math.Abs(fr) < 1e-6 && Math.Abs(fc) < 1e-6, $"({fr:0.###},{fc:0.###})");

            // ── 线-圆求交 ──
            // 圆 心(0,0) R=5；线 col=0（phi=90）→ 交点 (±5,0)
            var lok = GeometryMath.TryIntersectLineCircle(GeometryParam.Line(0, 0, 90), GeometryParam.Circle(0, 0, 5), out var lr, out var lc, out var lcount);
            Check("线-圆求交 圆心(0,0) R=5 与 col=0 线 → 交点在圆上", lok && Math.Abs(Math.Sqrt(lr * lr + lc * lc) - 5) < 1e-6 && lcount == 2, $"({lr:0.###},{lc:0.###}) n={lcount}");

            // ── 圆-圆求交 ──
            var cok = GeometryMath.TryIntersectCircles(GeometryParam.Circle(0, 0, 5), GeometryParam.Circle(0, 8, 5), out var cr, out var cc, out var ccount);
            Check("圆-圆求交 两 R=5 圆心距 8 → 交点在两圆上", cok
                && Math.Abs(Math.Sqrt(cr * cr + cc * cc) - 5) < 1e-6
                && Math.Abs(Math.Sqrt(cr * cr + (cc - 8) * (cc - 8)) - 5) < 1e-6, $"({cr:0.###},{cc:0.###}) n={ccount}");
            Check("同心圆求交 → false", !GeometryMath.TryIntersectCircles(GeometryParam.Circle(0, 0, 5), GeometryParam.Circle(0, 0, 3), out _, out _, out _), "");

            // ── 三点构圆 ──
            // 直角三角形 (0,0),(4,0),(0,4) → 外接圆心 (2,2)、半径 √8
            var tok = GeometryMath.TryCircleThroughThreePoints(GeometryParam.Point(0, 0), GeometryParam.Point(4, 0), GeometryParam.Point(0, 4), out var circle);
            Check("三点构圆 直角三角 → 圆心(2,2) r=√8", tok
                && Math.Abs(circle.Row - 2) < 1e-6 && Math.Abs(circle.Col - 2) < 1e-6
                && Math.Abs(circle.Radius - Math.Sqrt(8)) < 1e-6, circle.ToString());

            Check("三点共线构圆 → false（明确失败）",
                !GeometryMath.TryCircleThroughThreePoints(GeometryParam.Point(0, 0), GeometryParam.Point(1, 1), GeometryParam.Point(2, 2), out _), "");

            // ── 过两点构线 ──
            var ln = GeometryMath.LineThroughTwoPoints(GeometryParam.Point(0, 0), GeometryParam.Point(0, 4));
            Check("过两点构线 (0,0)-(0,4) → 中点(0,2) phi=0", Math.Abs(ln.Row) < 1e-6 && Math.Abs(ln.Col - 2) < 1e-6 && Math.Abs(ln.Phi) < 1e-6, ln.ToString());

            // ── GeometryParam 契约 ──
            Check("Phi 归一化 [0,180)：-10→170, 190→10, 180→0",
                Math.Abs(GeometryParam.NormalizePhi(-10) - 170) < Eps
                && Math.Abs(GeometryParam.NormalizePhi(190) - 10) < Eps
                && Math.Abs(GeometryParam.NormalizePhi(180) - 0) < Eps, "");

            var line = GeometryParam.Line(1.5, 2.5, 200);
            Check("工厂构造：Line 的 Phi 已被归一化（200→20）", Math.Abs(line.Phi - 20) < Eps, line.Phi.ToString("0.###"));

            // JSON 往返（端口值/全局变量序列化安全）
            var json = JsonConvert.SerializeObject(line);
            var back = JsonConvert.DeserializeObject<GeometryParam>(json)!;
            Check("GeometryParam JSON 往返一致（Newtonsoft）",
                back.Kind == line.Kind && Math.Abs(back.Row - line.Row) < Eps && Math.Abs(back.Col - line.Col) < Eps && Math.Abs(back.Phi - line.Phi) < Eps,
                json);

            var contour = GeometryParam.Contour(new[] { 1.0, 2.0, 3.0, 4.0 });
            var cback = JsonConvert.DeserializeObject<GeometryParam>(JsonConvert.SerializeObject(contour))!;
            Check("GeometryParam 轮廓 JSON 往返（Points 数组）",
                cback.Kind == GeometryKind.Contour && cback.Points != null && cback.Points.Length == 4 && Math.Abs(cback.Points[3] - 4.0) < Eps, "");

            // ── 枚举端口回写（参数面板下拉 → 端口值）──
            // 走共享 ValueConverter；裸 Convert.ChangeType 不认字符串→枚举，会让"选了枚举、确定后还原默认值"。
            Check("ValueConverter 字符串→枚举（参数面板下拉回写路径）",
                ValueConverter.Convert("Circle", typeof(Plugin.GeometryFinder.GeometryFindKind)) is Plugin.GeometryFinder.GeometryFindKind.Circle,
                "");
            Check("ValueConverter 数值→枚举（JSON 加载路径，Int64→枚举）",
                ValueConverter.Convert(1L, typeof(Plugin.GeometryFinder.GeometryFindKind)) is Plugin.GeometryFinder.GeometryFindKind.Circle,
                "");

            // 模拟参数面板下拉回写那一行：port.Value = ValueConverter.Convert(文本, 类型)
            var finder = new Plugin.GeometryFinder.GeometryFinderPlugin();
            finder.Kind.Value = ValueConverter.Convert("Circle", typeof(Plugin.GeometryFinder.GeometryFindKind));
            Check("枚举端口接受下拉回写的值（Kind → 圆）",
                finder.Kind.Value is Plugin.GeometryFinder.GeometryFindKind.Circle,
                finder.Kind.Value?.ToString() ?? "null");
        }
    }
}
