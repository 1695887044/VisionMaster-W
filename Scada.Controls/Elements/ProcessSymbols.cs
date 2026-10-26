using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace VisionMaster.Scada.Controls
{
    /// <summary>工艺符号的种类（<c>Hmi.Symbol</c> 的 <c>SymbolKind</c> 候选）。</summary>
    public enum SymbolKind
    {
        // ---- 一期：流程图最常画 ----
        Tower = 0,
        Reactor = 1,
        ShellTube = 2,
        PlateHx = 3,
        Compressor = 4,
        Fan = 5,
        Filter = 6,
        Separator = 7,
        TankHorizontal = 8,
        Sphere = 9,
        ControlValve = 10,
        GateValve = 11,

        // ---- 二期 ----
        Burner = 12,
        Stack = 13,
        Conveyor = 14,
        Agitator = 15,
        Cyclone = 16,
        CoolingTower = 17,
        BallValve = 18,
        ButterflyValve = 19,
        SafetyValve = 20,
        CheckValve = 21,

        // ---- 三期：管道与仪表 ----
        Tee = 22,
        Cross = 23,
        Flange = 24,
        Blind = 25,
        Reducer = 26,
        FlowArrow = 27,
        Gauge = 28,
        PidLoop = 29,
    }

    /// <summary>图元里各笔的角色：轮廓用主色描边，装饰用状态色填充。</summary>
    public enum SymbolRole
    {
        /// <summary>轮廓（描边，用 Stroke）</summary>
        Outline = 0,

        /// <summary>装饰/内部件（填充，用状态色/AccentColor）</summary>
        Accent = 1,
    }

    /// <summary>几何基元（归一化 0~1 坐标，成对存放）。</summary>
    public readonly record struct SymbolPrimitive(
        SymbolRole Role, bool Closed, bool Filled, double[] Points);

    /// <summary>重复细部（塔板 / 叶片 / 散热片）的画法。</summary>
    public enum SymbolDetailKind
    {
        None = 0,

        /// <summary>水平细线（塔板、冷却塔填料）</summary>
        HorizontalLines = 1,

        /// <summary>竖直细线（皮带机托辊、过滤器滤芯）</summary>
        VerticalLines = 2,

        /// <summary>径向细线（风机/搅拌叶片）</summary>
        RadialLines = 3,
    }

    /// <summary>
    /// 一个符号的几何定义：基元表 + 可选重复细部。
    ///
    /// 坐标一律归一化到 0~1（左上角原点），运行时按控件实际尺寸缩放——
    /// 与多边形/贝塞尔同一套口径，拖动缩放时不需要重算绝对坐标。
    /// </summary>
    public sealed class SymbolDefinition
    {
        public SymbolDefinition(
            SymbolPrimitive[] primitives,
            SymbolDetailKind detail = SymbolDetailKind.None,
            double detailLeft = 0, double detailTop = 0, double detailRight = 1, double detailBottom = 1,
            double detailCx = 0.5, double detailCy = 0.5, double detailRadius = 0.3,
            int defaultDetailCount = 0)
        {
            Primitives = primitives;
            Detail = detail;
            DetailLeft = detailLeft;
            DetailTop = detailTop;
            DetailRight = detailRight;
            DetailBottom = detailBottom;
            DetailCx = detailCx;
            DetailCy = detailCy;
            DetailRadius = detailRadius;
            DefaultDetailCount = defaultDetailCount;
        }

        public SymbolPrimitive[] Primitives { get; }

        public SymbolDetailKind Detail { get; }

        public double DetailLeft { get; }
        public double DetailTop { get; }
        public double DetailRight { get; }
        public double DetailBottom { get; }
        public double DetailCx { get; }
        public double DetailCy { get; }
        public double DetailRadius { get; }

        /// <summary>该符号建议的细部数量（换符号时用它初始化 <c>DetailCount</c>）</summary>
        public int DefaultDetailCount { get; }
    }

    /// <summary>
    /// 工艺符号几何表（<b>唯一事实源</b>）。
    ///
    /// 为什么是"一个 TypeKey + 一张表"而不是 30 个图元类
    /// ---------
    /// 30 个近义 TypeKey 会把工具箱撑成一面墙、把 90% 相同的属性复制 30 遍、
    /// 还要 30 个 Web 渲染器。符号之间的差别只是"几笔画在哪"，本质是数据不是类型。
    ///
    /// 坐标约定：0~1 归一化，左上角原点，Y 向下（与 WPF 一致）。
    /// 换符号只换这张表里的条目，控件、模板、属性面板都不动。
    /// </summary>
    public static class ProcessSymbolCatalog
    {
        private static readonly Dictionary<SymbolKind, SymbolDefinition> Table = new()
        {
            // ================= 一期 =================

            // 精馏塔：细高塔体 + 上下封头 + 塔板（细部）
            [SymbolKind.Tower] = new(
                new[]
                {
                    Poly(SymbolRole.Outline, 0.30, 0.08, 0.70, 0.08, 0.70, 0.92, 0.30, 0.92),
                    Line(SymbolRole.Outline, 0.30, 0.08, 0.42, 0.02, 0.58, 0.02, 0.70, 0.08),
                    Line(SymbolRole.Outline, 0.30, 0.92, 0.42, 0.98, 0.58, 0.98, 0.70, 0.92),
                    Line(SymbolRole.Outline, 0.30, 0.30, 0.22, 0.30),
                    Line(SymbolRole.Outline, 0.70, 0.14, 0.80, 0.14),
                },
                SymbolDetailKind.HorizontalLines, 0.32, 0.16, 0.68, 0.86, defaultDetailCount: 6),

            // 反应釜：釜体 + 上封头 + 搅拌轴（细部径向）+ 底出口
            [SymbolKind.Reactor] = new(
                new[]
                {
                    Rect(SymbolRole.Outline, 0.28, 0.18, 0.44, 0.60),
                    Line(SymbolRole.Outline, 0.28, 0.18, 0.38, 0.08, 0.62, 0.08, 0.72, 0.18),
                    Line(SymbolRole.Outline, 0.50, 0.02, 0.50, 0.18),
                    Line(SymbolRole.Outline, 0.50, 0.78, 0.50, 0.94),
                    Line(SymbolRole.Outline, 0.40, 0.78, 0.60, 0.78),
                },
                SymbolDetailKind.RadialLines, detailCx: 0.5, detailCy: 0.55, detailRadius: 0.14,
                defaultDetailCount: 3),

            // 管壳式换热器：壳体 + 两端管板 + 内部折流（细部水平线）
            [SymbolKind.ShellTube] = new(
                new[]
                {
                    Rect(SymbolRole.Outline, 0.14, 0.30, 0.72, 0.40),
                    Line(SymbolRole.Outline, 0.22, 0.30, 0.22, 0.70),
                    Line(SymbolRole.Outline, 0.78, 0.30, 0.78, 0.70),
                    Line(SymbolRole.Outline, 0.14, 0.50, 0.02, 0.50),
                    Line(SymbolRole.Outline, 0.86, 0.50, 0.98, 0.50),
                },
                SymbolDetailKind.HorizontalLines, 0.24, 0.36, 0.76, 0.64, defaultDetailCount: 4),

            // 板式换热器：外框 + 一组斜板（细部竖线近似）
            [SymbolKind.PlateHx] = new(
                new[]
                {
                    Rect(SymbolRole.Outline, 0.20, 0.16, 0.60, 0.68),
                    Line(SymbolRole.Outline, 0.20, 0.16, 0.10, 0.26, 0.10, 0.74, 0.20, 0.84),
                    Line(SymbolRole.Outline, 0.80, 0.16, 0.90, 0.26, 0.90, 0.74, 0.80, 0.84),
                },
                SymbolDetailKind.VerticalLines, 0.26, 0.22, 0.74, 0.78, defaultDetailCount: 6),

            // 离心压缩机：蜗壳圆 + 出口蜗舌 + 底座
            [SymbolKind.Compressor] = new(
                new[]
                {
                    Ellipse(SymbolRole.Outline, 0.46, 0.50, 0.28, 0.28),
                    Line(SymbolRole.Outline, 0.46, 0.22, 0.46, 0.06, 0.82, 0.06, 0.82, 0.22),
                    Rect(SymbolRole.Outline, 0.20, 0.78, 0.56, 0.10),
                    Line(SymbolRole.Outline, 0.46, 0.50, 0.46, 0.78),
                },
                SymbolDetailKind.RadialLines, detailCx: 0.46, detailCy: 0.50, detailRadius: 0.18,
                defaultDetailCount: 4),

            // 风机：蜗壳 + 叶片（径向）+ 出口
            [SymbolKind.Fan] = new(
                new[]
                {
                    Ellipse(SymbolRole.Outline, 0.42, 0.54, 0.26, 0.26),
                    Line(SymbolRole.Outline, 0.42, 0.28, 0.42, 0.10, 0.74, 0.10, 0.74, 0.28),
                    Rect(SymbolRole.Outline, 0.20, 0.80, 0.46, 0.08),
                },
                SymbolDetailKind.RadialLines, detailCx: 0.42, detailCy: 0.54, detailRadius: 0.17,
                defaultDetailCount: 4),

            // 过滤器：壳体 + 滤芯（竖线细部）+ 上下接管
            [SymbolKind.Filter] = new(
                new[]
                {
                    Rect(SymbolRole.Outline, 0.26, 0.22, 0.48, 0.56),
                    Line(SymbolRole.Outline, 0.50, 0.02, 0.50, 0.22),
                    Line(SymbolRole.Outline, 0.50, 0.78, 0.50, 0.98),
                },
                SymbolDetailKind.VerticalLines, 0.30, 0.28, 0.70, 0.72, defaultDetailCount: 4),

            // 气液分离器：立式筒体 + 上下封头 + 丝网（水平细部）
            [SymbolKind.Separator] = new(
                new[]
                {
                    Poly(SymbolRole.Outline, 0.32, 0.16, 0.68, 0.16, 0.68, 0.84, 0.32, 0.84),
                    Line(SymbolRole.Outline, 0.32, 0.16, 0.42, 0.08, 0.58, 0.08, 0.68, 0.16),
                    Line(SymbolRole.Outline, 0.32, 0.84, 0.42, 0.92, 0.58, 0.92, 0.68, 0.84),
                    Line(SymbolRole.Outline, 0.50, 0.02, 0.50, 0.08),
                    Line(SymbolRole.Outline, 0.50, 0.92, 0.50, 0.98),
                },
                SymbolDetailKind.HorizontalLines, 0.34, 0.24, 0.66, 0.36, defaultDetailCount: 3),

            // 卧式储罐：长圆筒 + 两端封头 + 鞍座
            [SymbolKind.TankHorizontal] = new(
                new[]
                {
                    Rect(SymbolRole.Outline, 0.18, 0.30, 0.64, 0.34),
                    Poly(SymbolRole.Outline, 0.18, 0.30, 0.10, 0.38, 0.10, 0.56, 0.18, 0.64),
                    Poly(SymbolRole.Outline, 0.82, 0.30, 0.90, 0.38, 0.90, 0.56, 0.82, 0.64),
                    Line(SymbolRole.Outline, 0.28, 0.64, 0.28, 0.76),
                    Line(SymbolRole.Outline, 0.72, 0.64, 0.72, 0.76),
                    Line(SymbolRole.Outline, 0.50, 0.30, 0.50, 0.16),
                }),

            // 球罐：圆 + 支柱
            [SymbolKind.Sphere] = new(
                new[]
                {
                    Ellipse(SymbolRole.Outline, 0.50, 0.44, 0.30, 0.30),
                    Line(SymbolRole.Outline, 0.28, 0.62, 0.22, 0.90),
                    Line(SymbolRole.Outline, 0.72, 0.62, 0.78, 0.90),
                    Line(SymbolRole.Outline, 0.50, 0.74, 0.50, 0.92),
                    Line(SymbolRole.Outline, 0.50, 0.14, 0.50, 0.04),
                }),

            // 调节阀：两个三角（阀体）+ 阀杆 + 执行机构
            [SymbolKind.ControlValve] = new(
                new[]
                {
                    Poly(SymbolRole.Outline, 0.18, 0.52, 0.46, 0.40, 0.46, 0.64),
                    Poly(SymbolRole.Outline, 0.46, 0.40, 0.46, 0.64, 0.74, 0.52),
                    Line(SymbolRole.Outline, 0.46, 0.40, 0.46, 0.22),
                    Rect(SymbolRole.Outline, 0.34, 0.10, 0.24, 0.12),
                    Line(SymbolRole.Outline, 0.02, 0.52, 0.18, 0.52),
                    Line(SymbolRole.Outline, 0.74, 0.52, 0.98, 0.52),
                }),

            // 闸阀：两个三角 + 中间闸板
            [SymbolKind.GateValve] = new(
                new[]
                {
                    Poly(SymbolRole.Outline, 0.18, 0.58, 0.46, 0.42, 0.46, 0.74),
                    Poly(SymbolRole.Outline, 0.46, 0.42, 0.46, 0.74, 0.74, 0.58),
                    Line(SymbolRole.Outline, 0.46, 0.42, 0.46, 0.26),
                    Line(SymbolRole.Outline, 0.36, 0.26, 0.56, 0.26),
                    Line(SymbolRole.Outline, 0.02, 0.58, 0.18, 0.58),
                    Line(SymbolRole.Outline, 0.74, 0.58, 0.98, 0.58),
                }),

            // ================= 二期 =================

            // 燃烧器：炉膛 + 火焰（装饰填充）
            [SymbolKind.Burner] = new(
                new[]
                {
                    Rect(SymbolRole.Outline, 0.24, 0.30, 0.52, 0.44),
                    Line(SymbolRole.Outline, 0.10, 0.52, 0.24, 0.52),
                    Poly(SymbolRole.Accent, 0.50, 0.36, 0.60, 0.52, 0.50, 0.68, 0.40, 0.52),
                }),

            // 烟囱：梯形筒身 + 顶部
            [SymbolKind.Stack] = new(
                new[]
                {
                    Poly(SymbolRole.Outline, 0.38, 0.06, 0.62, 0.06, 0.72, 0.94, 0.28, 0.94),
                    Line(SymbolRole.Outline, 0.30, 0.86, 0.70, 0.86),
                }),

            // 皮带机：机架 + 托辊（竖线细部）+ 料流
            [SymbolKind.Conveyor] = new(
                new[]
                {
                    Line(SymbolRole.Outline, 0.08, 0.52, 0.92, 0.52),
                    Line(SymbolRole.Outline, 0.08, 0.66, 0.92, 0.66),
                    Line(SymbolRole.Outline, 0.16, 0.66, 0.16, 0.86),
                    Line(SymbolRole.Outline, 0.84, 0.66, 0.84, 0.86),
                    Line(SymbolRole.Outline, 0.16, 0.86, 0.84, 0.86),
                },
                SymbolDetailKind.VerticalLines, 0.12, 0.54, 0.88, 0.64, defaultDetailCount: 6),

            // 搅拌器：电机 + 轴 + 桨叶（径向）
            [SymbolKind.Agitator] = new(
                new[]
                {
                    Rect(SymbolRole.Outline, 0.38, 0.06, 0.24, 0.14),
                    Line(SymbolRole.Outline, 0.50, 0.20, 0.50, 0.70),
                },
                SymbolDetailKind.RadialLines, detailCx: 0.5, detailCy: 0.72, detailRadius: 0.18,
                defaultDetailCount: 3),

            // 旋风分离器：筒体 + 锥底 + 切向进口
            [SymbolKind.Cyclone] = new(
                new[]
                {
                    Rect(SymbolRole.Outline, 0.32, 0.14, 0.36, 0.32),
                    Poly(SymbolRole.Outline, 0.32, 0.46, 0.68, 0.46, 0.50, 0.88),
                    Line(SymbolRole.Outline, 0.68, 0.22, 0.92, 0.22),
                    Line(SymbolRole.Outline, 0.50, 0.02, 0.50, 0.14),
                }),

            // 冷却塔：梯形塔身 + 填料（水平细部）+ 风机
            [SymbolKind.CoolingTower] = new(
                new[]
                {
                    Poly(SymbolRole.Outline, 0.30, 0.22, 0.70, 0.22, 0.80, 0.92, 0.20, 0.92),
                    Line(SymbolRole.Outline, 0.34, 0.22, 0.34, 0.12, 0.66, 0.12, 0.66, 0.22),
                },
                SymbolDetailKind.HorizontalLines, 0.26, 0.36, 0.74, 0.84, defaultDetailCount: 4),

            // 球阀：两个三角 + 圆形球体
            [SymbolKind.BallValve] = new(
                new[]
                {
                    Poly(SymbolRole.Outline, 0.18, 0.58, 0.46, 0.42, 0.46, 0.74),
                    Poly(SymbolRole.Outline, 0.46, 0.42, 0.46, 0.74, 0.74, 0.58),
                    Ellipse(SymbolRole.Accent, 0.46, 0.58, 0.10, 0.10, filled: true),
                    Line(SymbolRole.Outline, 0.46, 0.42, 0.46, 0.26),
                    Line(SymbolRole.Outline, 0.02, 0.58, 0.18, 0.58),
                    Line(SymbolRole.Outline, 0.74, 0.58, 0.98, 0.58),
                }),

            // 蝶阀：两个三角 + 斜板
            [SymbolKind.ButterflyValve] = new(
                new[]
                {
                    Poly(SymbolRole.Outline, 0.18, 0.58, 0.46, 0.42, 0.46, 0.74),
                    Poly(SymbolRole.Outline, 0.46, 0.42, 0.46, 0.74, 0.74, 0.58),
                    Line(SymbolRole.Accent, 0.40, 0.70, 0.54, 0.46),
                    Line(SymbolRole.Outline, 0.02, 0.58, 0.18, 0.58),
                    Line(SymbolRole.Outline, 0.74, 0.58, 0.98, 0.58),
                }),

            // 安全阀：阀体 + 弹簧 + 排放口
            [SymbolKind.SafetyValve] = new(
                new[]
                {
                    Poly(SymbolRole.Outline, 0.18, 0.70, 0.46, 0.58, 0.46, 0.82),
                    Poly(SymbolRole.Outline, 0.46, 0.58, 0.46, 0.82, 0.74, 0.70),
                    Line(SymbolRole.Outline, 0.46, 0.58, 0.46, 0.34),
                    Line(SymbolRole.Outline, 0.36, 0.34, 0.56, 0.34),
                    Line(SymbolRole.Outline, 0.46, 0.34, 0.62, 0.14),
                    Line(SymbolRole.Outline, 0.02, 0.70, 0.18, 0.70),
                    Line(SymbolRole.Outline, 0.74, 0.70, 0.98, 0.70),
                }),

            // 止回阀：三角 + 竖线（单向）+ 箭头
            [SymbolKind.CheckValve] = new(
                new[]
                {
                    Poly(SymbolRole.Outline, 0.20, 0.42, 0.20, 0.74, 0.52, 0.58),
                    Line(SymbolRole.Outline, 0.52, 0.34, 0.52, 0.82),
                    Line(SymbolRole.Outline, 0.02, 0.58, 0.20, 0.58),
                    Line(SymbolRole.Outline, 0.52, 0.58, 0.98, 0.58),
                }),

            // ================= 三期 =================

            // 三通
            [SymbolKind.Tee] = new(
                new[]
                {
                    Line(SymbolRole.Outline, 0.06, 0.62, 0.94, 0.62),
                    Line(SymbolRole.Outline, 0.50, 0.62, 0.50, 0.10),
                }),

            // 四通
            [SymbolKind.Cross] = new(
                new[]
                {
                    Line(SymbolRole.Outline, 0.06, 0.50, 0.94, 0.50),
                    Line(SymbolRole.Outline, 0.50, 0.06, 0.50, 0.94),
                }),

            // 法兰：两片 + 螺栓
            [SymbolKind.Flange] = new(
                new[]
                {
                    Rect(SymbolRole.Outline, 0.30, 0.20, 0.10, 0.60),
                    Rect(SymbolRole.Outline, 0.60, 0.20, 0.10, 0.60),
                    Line(SymbolRole.Outline, 0.40, 0.50, 0.60, 0.50),
                    Line(SymbolRole.Accent, 0.02, 0.50, 0.30, 0.50),
                    Line(SymbolRole.Accent, 0.70, 0.50, 0.98, 0.50),
                }),

            // 盲板：法兰 + 封板
            [SymbolKind.Blind] = new(
                new[]
                {
                    Rect(SymbolRole.Outline, 0.26, 0.20, 0.08, 0.60),
                    Rect(SymbolRole.Accent, 0.34, 0.26, 0.10, 0.48),
                    Line(SymbolRole.Outline, 0.44, 0.50, 0.98, 0.50),
                    Line(SymbolRole.Accent, 0.02, 0.50, 0.26, 0.50),
                }),

            // 异径管：梯形
            [SymbolKind.Reducer] = new(
                new[]
                {
                    Poly(SymbolRole.Outline, 0.10, 0.30, 0.90, 0.42, 0.90, 0.58, 0.10, 0.70),
                }),

            // 流向箭头
            [SymbolKind.FlowArrow] = new(
                new[]
                {
                    Line(SymbolRole.Outline, 0.06, 0.50, 0.62, 0.50),
                    Poly(SymbolRole.Accent, 0.62, 0.34, 0.94, 0.50, 0.62, 0.66),
                }),

            // 就地压力表：圆表 + 指针
            [SymbolKind.Gauge] = new(
                new[]
                {
                    Ellipse(SymbolRole.Outline, 0.50, 0.50, 0.30, 0.30),
                    Line(SymbolRole.Accent, 0.50, 0.50, 0.66, 0.36),
                    Line(SymbolRole.Outline, 0.50, 0.80, 0.50, 0.96),
                }),

            // PID 回路标记框：圆 + 横隔线
            [SymbolKind.PidLoop] = new(
                new[]
                {
                    Ellipse(SymbolRole.Outline, 0.50, 0.50, 0.32, 0.32),
                    Line(SymbolRole.Outline, 0.18, 0.50, 0.82, 0.50),
                }),
        };

        /// <summary>全部符号（顺序即枚举顺序，属性面板下拉按它排）</summary>
        public static IReadOnlyDictionary<SymbolKind, SymbolDefinition> All => Table;

        /// <summary>取一个符号的定义；未知种类返回塔（不抛：坏值不该让整页打不开）</summary>
        public static SymbolDefinition Get(SymbolKind kind)
            => Table.TryGetValue(kind, out var definition) ? definition : Table[SymbolKind.Tower];

        /// <summary>符号种类名（ASCII，写进 .vms）</summary>
        public static string[] KindNames { get; } = Enum.GetNames<SymbolKind>();

        // ---- 基元构造助手（表里只写数字，读起来像画图） ----

        private static SymbolPrimitive Line(SymbolRole role, params double[] points)
            => new(role, Closed: false, Filled: false, points);

        private static SymbolPrimitive Poly(SymbolRole role, params double[] points)
            => new(role, Closed: true, Filled: true, points);

        private static SymbolPrimitive Rect(SymbolRole role, double x, double y, double w, double h)
            => new(role, Closed: true, Filled: true, new[] { x, y, x + w, y, x + w, y + h, x, y + h });

        private static SymbolPrimitive Ellipse(SymbolRole role, double cx, double cy, double rx, double ry, bool filled = false)
        {
            // 圆用 8 段折线近似（够看清，且不用 ArcSegment 的 size/rotation 参数）
            var points = new double[16];
            for (var i = 0; i < 8; i++)
            {
                var angle = Math.PI * 2 * i / 8;
                points[i * 2] = cx + Math.Cos(angle) * rx;
                points[i * 2 + 1] = cy + Math.Sin(angle) * ry;
            }

            return new SymbolPrimitive(role, Closed: true, Filled: filled, points);
        }

        /// <summary>
        /// 按控件尺寸把符号画成几何。轮廓与装饰分成两个 Geometry（模板里是两个 Path，
        /// 分别用 Stroke 与状态色）。
        /// </summary>
        public static Geometry? BuildGeometry(
            SymbolDefinition definition, SymbolRole role, double width, double height, int detailCount)
        {
            if (width <= 0 || height <= 0)
                return null;

            var group = new GeometryGroup { FillRule = FillRule.Nonzero };
            var any = false;

            foreach (var primitive in definition.Primitives)
            {
                if (primitive.Role != role || primitive.Points.Length < 4)
                    continue;

                var figure = new PathFigure
                {
                    StartPoint = new Point(primitive.Points[0] * width, primitive.Points[1] * height),
                    IsClosed = primitive.Closed,
                    IsFilled = primitive.Filled && role == SymbolRole.Accent,
                };

                var segment = new PolyLineSegment();
                for (var i = 2; i + 1 < primitive.Points.Length; i += 2)
                    segment.Points.Add(new Point(primitive.Points[i] * width, primitive.Points[i + 1] * height));

                figure.Segments.Add(segment);

                var geometry = new PathGeometry();
                geometry.Figures.Add(figure);
                group.Children.Add(geometry);
                any = true;
            }

            if (role == SymbolRole.Accent && definition.Detail != SymbolDetailKind.None && detailCount > 0)
            {
                if (BuildDetail(definition, width, height, detailCount) is { } detail)
                {
                    // 摊平进外层组：细部每条线一个子几何，断言按"子几何数 = 细部条数"核
                    foreach (var child in detail.Children)
                        group.Children.Add(child);

                    any = true;
                }
            }

            return any ? group : null;
        }

        private static GeometryGroup? BuildDetail(SymbolDefinition definition, double width, double height, int count)
        {
            var group = new GeometryGroup();

            switch (definition.Detail)
            {
                case SymbolDetailKind.HorizontalLines:
                {
                    var top = definition.DetailTop * height;
                    var bottom = definition.DetailBottom * height;
                    var left = definition.DetailLeft * width;
                    var right = definition.DetailRight * width;
                    var step = count == 1 ? 0 : (bottom - top) / (count + 1);

                    for (var i = 1; i <= count; i++)
                    {
                        var y = count == 1 ? (top + bottom) / 2 : top + step * i;
                        group.Children.Add(LineGeometry(left, y, right, y));
                    }

                    break;
                }

                case SymbolDetailKind.VerticalLines:
                {
                    var top = definition.DetailTop * height;
                    var bottom = definition.DetailBottom * height;
                    var left = definition.DetailLeft * width;
                    var right = definition.DetailRight * width;
                    var step = count == 1 ? 0 : (right - left) / (count + 1);

                    for (var i = 1; i <= count; i++)
                    {
                        var x = count == 1 ? (left + right) / 2 : left + step * i;
                        group.Children.Add(LineGeometry(x, top, x, bottom));
                    }

                    break;
                }

                case SymbolDetailKind.RadialLines:
                {
                    var cx = definition.DetailCx * width;
                    var cy = definition.DetailCy * height;
                    var radius = definition.DetailRadius * Math.Min(width, height);

                    for (var i = 0; i < count; i++)
                    {
                        var angle = Math.PI * 2 * i / Math.Max(1, count);
                        group.Children.Add(LineGeometry(
                            cx, cy,
                            cx + Math.Cos(angle) * radius,
                            cy + Math.Sin(angle) * radius));
                    }

                    break;
                }
            }

            return group.Children.Count > 0 ? group : null;
        }

        private static Geometry LineGeometry(double x1, double y1, double x2, double y2)
        {
            var figure = new PathFigure { StartPoint = new Point(x1, y1), IsClosed = false, IsFilled = false };
            figure.Segments.Add(new LineSegment(new Point(x2, y2), true));

            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            return geometry;
        }
    }
}
