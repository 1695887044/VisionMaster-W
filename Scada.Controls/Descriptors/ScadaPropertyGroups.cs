using System;
using System.Collections.Generic;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// 属性面板的<b>分组目录</b>：组名的唯一出处、显示顺序、以及"这一组能不能绑变量"。
    ///
    /// 为什么要有这一层
    /// ---------
    /// 组名原先是一串散落在各处的字面量：几何属性写在 <see cref="GeometryProperties"/>，
    /// 外观 / 文字写在 <see cref="AppearanceProperties"/> / <see cref="TextProperties"/> 的默认参数里，
    /// 其余写在二十多个图元类的 <c>[ScadaProperty(Group = …)]</c> 上，事件 / 动画 / 权限又写在
    /// 面板视图模型里。由此生出三个问题，每一个都<b>不报错</b>：
    /// ① <c>NonBindableGroups</c> 原先按<b>字符串精确匹配</b>判"这一组不许绑变量"——
    ///    把「位置与尺寸」改名成「布局」，编译期全绿，那条校验却静默失效，面板上凭空多出一个 ƒx；
    /// ② 分组次序是"声明里首次出现的次序"，同一个「外观」组在按钮面板排第 4、在灯面板可能排第 2，
    ///    用户每换一个图元都要重新找位置；
    /// ③ 新加一个图元时把组名写成目录里没有的近义变体（「外观」写成「皮肤」这类），
    ///    两段看起来一样的分组并存，谁都不会发现。
    ///
    /// 所以这里把组名收成<b>常量 + 一条声明顺序</b>，并在 <see cref="ElementDescriptor.Validate"/> 里
    /// 要求"每个属性声明的组必须在本目录里"：写错组名 = 注册失败（图元进不了工具箱），
    /// 而不是等用户发现面板上多了一段莫名其妙的分组。
    ///
    /// 值的口径
    /// ---------
    /// 常量值就是<b>上屏文字</b>。改显示名只改这里一处，描述符、面板、断言都跟着走——
    /// 这也是这套目录存在的全部理由：让"改名"变成一行改动，而不是一次全仓搜索替换。
    ///
    /// 顺序的排法（为什么是现在这样）
    /// ---------
    /// 前四组对齐 Web 设计基线的「基本 / 外观 / 文本 / 布局」；中间是图元自己的功能数据
    /// （状态 / 数据 / 图像 / 刻度 / 点位 / 操作 / 限制）；末尾是"配置类"（交互 / 事件 / 动画）——
    /// 前两类回答"这个图元是什么"，后一类回答"它怎么响应"，末尾那几组位置稳定比夹在中间更有用。
    /// 画面属性（基本信息 / 画布尺寸 / 背景 / 设计辅助 / 运行）排在最后：它们与图元属性
    /// <b>互斥出现</b>（选中图元时面板不显示画面属性），相对位置不影响观感，
    /// 合在一张表里只是为了让"查一个组在第几位"只有一份实现。
    ///
    /// 组名与"这一组有哪些行"是两件事
    /// ---------
    /// 本目录只约束<b>组名与次序</b>，不约束每组里有哪些属性：泵的「泵壳色」、电机的「机身色」、
    /// 管道的「管身色」都是同一个 <c>Fill</c> 键按图元换的显示名（见
    /// <see cref="AppearanceProperties.Rename"/>），照旧各叫各的。图元之间"有没有这一组"也不同
    /// ——纯装饰图元没有「数据」组是正常的，不是漏声明。
    ///
    /// 守门人：<c>ScadaChecks</c> 的 <c>ScadaPropertyGroupCatalogChecks</c>——目录自身的自检
    /// （无重名 / 顺序完整）、"每个内置图元的每个属性组都在目录里"、"画面属性与画面事件也在目录里"、
    /// 以及一份<b>显示名字面量快照</b>（改显示名必须是有意改快照，不能是手滑）。
    /// </summary>
    public static class ScadaPropertyGroups
    {
        // ---- 图元属性分组 ----

        /// <summary>基本：名称、类型键等"这个图元是谁"</summary>
        public const string General = "基本";

        /// <summary>外观：填充 / 边框 / 粗细 / 圆角（设计期皮肤，不参与变量绑定）</summary>
        public const string Appearance = "外观";

        /// <summary>文本：内容 / 颜色 / 字号 / 字重 / 对齐</summary>
        public const string Text = "文本";

        /// <summary>布局：X / Y / 宽 / 高 / 旋转（设计期版面，不参与变量绑定）</summary>
        public const string Layout = "布局";

        /// <summary>状态：状态值 + 各状态的颜色（指示灯 / 泵 / 电机 / 管道…）</summary>
        public const string State = "状态";

        /// <summary>数据：值、格式、小数位等"运行期要显示的那个数"</summary>
        public const string Data = "数据";

        /// <summary>图像：图元要显示的图从哪来、怎么铺</summary>
        public const string Image = "图像";

        /// <summary>刻度：表盘的角度范围与刻度数</summary>
        public const string Scale = "刻度";

        /// <summary>点位：轴点位表的列与显示条数</summary>
        public const string Points = "点位";

        /// <summary>操作：运行态可做的动作（点动 / 回零…）</summary>
        public const string Operation = "操作";

        /// <summary>限制：上下限与越限配色</summary>
        public const string Limits = "限制";

        /// <summary>交互：操作权限（以及将来的设计期可见开关）</summary>
        public const string Interaction = "交互";

        /// <summary>事件：图元能配的事件钩子（点击 / 按下 / 松开…）</summary>
        public const string Event = "事件";

        /// <summary>动画：外观变化 / 水平移动 / 垂直移动 / 可见性</summary>
        public const string Animation = "动画";

        // ---- 画面属性分组（与图元属性互斥出现，排在最后）----

        /// <summary>基本信息：画面名 / 说明</summary>
        public const string PageBasic = "基本信息";

        /// <summary>画布尺寸：宽 / 高</summary>
        public const string PageCanvas = "画布尺寸";

        /// <summary>背景：画面底色</summary>
        public const string PageBackground = "背景";

        /// <summary>设计辅助：网格间距</summary>
        public const string PageDesign = "设计辅助";

        /// <summary>运行：基座 / 启动画面 / 画面事件</summary>
        public const string PageRun = "运行";

        /// <summary>
        /// 面板上的分组顺序（先图元后画面）。面板按本表排序，<b>不再</b>按"声明里首次出现的次序"。
        ///
        /// 表里没有的组排在最后并保持各自的首次出现次序（<c>OrderBy</c> 是稳定排序）——
        /// 给"以后真出现外部图元"留一条活路：分组位置不理想，但不至于整组消失。
        /// </summary>
        public static IReadOnlyList<string> Order { get; } = new[]
        {
            General, Appearance, Text, Layout,
            State, Data, Image, Scale, Points, Operation, Limits,
            Interaction, Event, Animation,
            PageBasic, PageCanvas, PageBackground, PageDesign, PageRun,
        };

        /// <summary>
        /// 这些组一律不许绑变量（设计期语言，不是运行期数据）。
        ///
        /// 原先这份名单是 <c>ElementDescriptor.NonBindableGroups</c>，按字符串精确匹配——
        /// 组名一改就静默失效（编译期全绿、运行期只是面板上多出一个不该有的 ƒx）。
        /// 现在名单与组名同处一份目录，改名不会再漏。
        /// </summary>
        public static IReadOnlyList<string> NonBindable { get; } = new[] { Layout, Appearance };

        /// <summary>这个组名在目录里吗（注册期校验用；<c>null</c> / 空串一律算不在）</summary>
        public static bool IsKnown(string? group)
        {
            if (string.IsNullOrEmpty(group)) return false;

            for (int i = 0; i < Order.Count; i++)
            {
                if (string.Equals(Order[i], group, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 本组在面板上的次序；未知组返回 <see cref="int.MaxValue"/>（排到最后）。
        /// 调用方用稳定排序（<c>OrderBy</c>），未知组之间因此保持各自的首次出现次序。
        /// </summary>
        public static int OrderOf(string? group)
        {
            for (int i = 0; i < Order.Count; i++)
            {
                if (string.Equals(Order[i], group, StringComparison.Ordinal))
                    return i;
            }

            return int.MaxValue;
        }

        /// <summary>
        /// 本组能不能绑变量（见 <see cref="NonBindable"/>）。
        /// 未知组按"可绑"处理——它本来就过不了注册期校验，这里再拦一次只会把错误原因说岔。
        /// </summary>
        public static bool AllowsVariableBinding(string? group)
        {
            for (int i = 0; i < NonBindable.Count; i++)
            {
                if (string.Equals(NonBindable[i], group, StringComparison.Ordinal))
                    return false;
            }

            return true;
        }
    }
}
