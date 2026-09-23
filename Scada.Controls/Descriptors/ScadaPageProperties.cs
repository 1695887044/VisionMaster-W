using System;
using System.Collections.Generic;
using System.Globalization;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// 画面级属性的一条声明：读写委托 + 面板要看的元数据。
    ///
    /// 为什么用委托而不是反射（"按属性名取 <see cref="ScadaPage"/> 的 CLR 属性"）：
    /// 反射看着省几行，代价是笔误到运行时才炸、而且每次刷新属性面板都要过一遍
    /// <c>PropertyInfo.GetValue</c>。这里每个属性一行 lambda，编译器直接校验名字和类型，
    /// 读一次就是一个字段访问。画面属性统共七条，省下来的那点字面量不值这个风险。
    /// </summary>
    public sealed class ScadaPagePropertySpec : IPropertySpec
    {
        public required string Key { get; init; }

        public required string DisplayName { get; init; }

        public ElementPropertyKind Kind { get; init; } = ElementPropertyKind.Text;

        public string Group { get; init; } = "画面";

        public string? Description { get; init; }

        public double Min { get; init; } = double.NegativeInfinity;

        public double Max { get; init; } = double.PositiveInfinity;

        public IReadOnlyList<string> Choices { get; init; } = Array.Empty<string>();

        /// <summary>
        /// 画面属性一律不可绑变量。
        ///
        /// 不是"做不到"——商业组态里"画面底色随报警等级变"是真需求。但绑定的落点是
        /// <c>ScadaElement.Bindings</c>（按图元挂），画面级绑定要另立一份集合和一套刷新路径，
        /// 那是 S4 运行态的事。现在放开一个"ƒx"按钮只会弹"还没接"，不如不显示。
        /// </summary>
        public bool IsBindable => false;

        /// <summary>
        /// 从画面读出字符串形态的当前值（数值一律不变文化，与 .vms 同一口径）。
        /// 第二个参数是画面所属的<b>方案</b>：「启动画面」这一行的真值存在文档上而不是画面上，
        /// 没有这个参数它就没法读写。给它而不是给行里塞个文档字段，是为了让"落在哪儿"这件事
        /// 留在声明里——面板不认识具体属性，它只按声明的委托调。
        /// </summary>
        public required Func<ScadaPage, ScadaDocument, string> Read { get; init; }

        /// <summary>把面板提交的文本写回画面（或方案）；解析不过就原样忽略，由行侧标红</summary>
        public required Action<ScadaPage, ScadaDocument, string> Write { get; init; }
    }

    /// <summary>
    /// 画面自身的可编辑属性清单（对齐 <see cref="GeometryProperties"/> 在图元侧的位置）。
    ///
    /// 只列<b>落盘</b>的画面属性。<c>ShowGrid</c> / <c>SnapToGrid</c> 虽然也落盘，但顶栏已经有
    /// 勾选框且绑的是同一个模型属性，面板里再来一份就成了"两个地方改同一个值"——
    /// 一个东西有两个入口，用户迟早会怀疑它们是不是不同步。所以这里只补顶栏没给的
    /// <c>GridSize</c>，开关留给顶栏。
    ///
    /// <c>Zoom</c> / <c>Offset</c> 更是压根不在列：那是"这台机器这一次的视角"，不是画面内容。
    ///
    /// <b>这里没有画面事件</b>：事件不是属性（一行"读一个值"与一块"挂一串动作"的表，
    /// 面板渲染方式都不同），画面事件住在 <see cref="ScadaPageEvents"/>，
    /// 与图元事件住在 <see cref="ElementDescriptor.Events"/> 是同一个安排。
    /// 两边的事件行汇到同一份实现上（宿主抽象见 <c>IScadaEventHost</c>）。
    /// </summary>
    public static class ScadaPageProperties
    {
        /// <summary>清单（面板按此顺序生成分组，组按首现次序）</summary>
        public static IReadOnlyList<ScadaPagePropertySpec> All { get; } = new[]
        {
            new ScadaPagePropertySpec
            {
                Key = "Name", DisplayName = "名称", Kind = ElementPropertyKind.Text, Group = "基本信息",
                Description = "画面名（画面下拉与运行态切画面都按它寻址）",
                Read = (p, _) => p.Name, Write = (p, _, v) => p.Name = v,
            },
            new ScadaPagePropertySpec
            {
                Key = "Description", DisplayName = "说明", Kind = ElementPropertyKind.MultilineText, Group = "基本信息",
                Read = (p, _) => p.Description, Write = (p, _, v) => p.Description = v,
            },
            new ScadaPagePropertySpec
            {
                Key = "Width", DisplayName = "宽", Kind = ElementPropertyKind.Number, Group = "画布尺寸", Min = 1,
                Description = "画面宽度（设计像素）。改小不会裁掉图元，只是画布边界挪了",
                Read = (p, _) => Format(p.Width), Write = (p, _, v) => { if (TryNumber(v, out var d)) p.Width = d; },
            },
            new ScadaPagePropertySpec
            {
                Key = "Height", DisplayName = "高", Kind = ElementPropertyKind.Number, Group = "画布尺寸", Min = 1,
                Description = "画面高度（设计像素）。运行时按画面等比缩放到显示区域",
                Read = (p, _) => Format(p.Height), Write = (p, _, v) => { if (TryNumber(v, out var d)) p.Height = d; },
            },
            new ScadaPagePropertySpec
            {
                Key = "Background", DisplayName = "背景色", Kind = ElementPropertyKind.Color, Group = "背景",
                Description = "画面底色（#AARRGGBB，也认 #RRGGBB 与颜色名）",
                Read = (p, _) => p.Background, Write = (p, _, v) => p.Background = v,
            },
            new ScadaPagePropertySpec
            {
                Key = "GridSize", DisplayName = "网格间距", Kind = ElementPropertyKind.Number, Group = "设计辅助", Min = 1,
                Description = "网格线间距，同时是吸附步距（设计像素）",
                Read = (p, _) => Format(p.GridSize), Write = (p, _, v) => { if (TryNumber(v, out var d)) p.GridSize = d; },
            },
            // ---- 运行：改的是"这一页跑起来是什么样"，不是画布上看到了什么 ----
            new ScadaPagePropertySpec
            {
                Key = "StartupPage", DisplayName = "启动画面", Kind = ElementPropertyKind.Bool, Group = "运行",
                Description = "运行态打开时先显示这一页。一个方案至多勾一页；一页都不勾就用画面列表的第一页",
                // 真值在方案上（ScadaDocument.StartupPageId），本行只是它在本画面上的一个投影：
                // 读 = "我是不是那个 Id"，写 = 勾上指我、取消只在我确实是时才清空。
                // 存 Id 而不存"每页一个 bool"，互斥才是白送的（详见 ScadaDocument.StartupPageId 注释）。
                Read = (p, d) => (d.StartupPageId == p.PageId).ToString(),
                Write = (p, d, v) =>
                {
                    if (!bool.TryParse(v, out var flag)) return;
                    if (flag) d.SetStartupPage(p);
                    else d.ClearStartupPage(p);
                },
            },
        };

        /// <summary>按不变文化格式化——和图元几何值同一个口径，否则 .vms 在中文系统上会写成 "1,5"</summary>
        private static string Format(double value)
            => value.ToString("0.######", CultureInfo.InvariantCulture);

        private static bool TryNumber(string? text, out double value)
            => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
