using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Core.Interfaces;
using HalconDotNet;
using VisionMaster.Helpers;
using static FlowCanvasChecks.Program;

namespace FlowCanvasChecks
{
    /// <summary>
    /// 端口绑定类型判据（TypeHelper.CanBindTo）的断言。
    ///
    /// 为什么值得单独守它：变量绑定弹窗的"列表过滤"与"双击时校验"现在共用这一个函数。
    /// 一把尺子一旦被改坏，症状是**静默的**——要么把能绑的过滤掉（用户找不到变量），
    /// 要么把不能绑的放过去（运行时才炸）。两条路都很难归因到这个函数上。
    ///
    /// 最容易被后人"顺手改坏"的是数组那条：看起来 double[] 不该能绑到 double，
    /// 但绑定时会弹索引框取下标，是合法且常用的路径。
    /// </summary>
    internal static class PortBindChecks
    {
        public static void Run()
        {
            Section("[PortBind] 端口绑定类型判据（列表过滤与双击校验共用同一把尺子）");

            // ---- ① 直接兼容 ----
            Check("【判据】同类型可绑", TypeHelper.CanBindTo(typeof(double), typeof(double)), "");
            Check("【判据】派生类 → 基类可绑（HImage → HObject）",
                TypeHelper.CanBindTo(typeof(HImage), typeof(HObject)), "");
            Check("【判据】整型 → 浮点可绑（数值拓宽）",
                TypeHelper.CanBindTo(typeof(int), typeof(double)), "");
            Check("【判据】任意类型 → object 端口可绑",
                TypeHelper.CanBindTo(typeof(HRegion), typeof(object)), "");
            Check("【判据】任意类型 → string 端口可绑（字符串化是有意放开的）",
                TypeHelper.CanBindTo(typeof(HImage), typeof(string)), "");

            // ---- ② 数组取元素（最容易误杀的一条） ----
            Check("【判据】数组 → 同元素标量可绑（绑定时按下标取一个）",
                TypeHelper.CanBindTo(typeof(double[]), typeof(double)), "");
            Check("【判据】数组 → 数组仍走常规兼容判断",
                TypeHelper.CanBindTo(typeof(double[]), typeof(double[])), "");
            Check("【判据】元素类型不兼容时，数组也不能绑（double[] → HImage）",
                !TypeHelper.CanBindTo(typeof(double[]), typeof(HImage)), "");

            // ---- ③ 不兼容 ----
            Check("【判据】HRegion → HImage 不可绑",
                !TypeHelper.CanBindTo(typeof(HRegion), typeof(HImage)), "");
            Check("【判据】HImage → 数值端口不可绑",
                !TypeHelper.CanBindTo(typeof(HImage), typeof(int)), "");
            // ★ 这条判据在实测后**反转**了（原为"不可绑"）。
            //   原因：全局变量的默认类型就是 object，而绑定弹窗按"选中输入端口的类型"过滤候选。
            //   判它不可绑时，用户选中一个 Double 输入端口后所有全局变量都会消失 ——
            //   界面表现是"点了一个上游节点、右边列表却空着"（用户反馈原话："点击返回原有内容无响应"）。
            //   object 的语义是"**运行期**才知道真实类型"，静态这层拦不住真错配，只会禁掉合法用法。
            Check("【判据】object → 具体类型端口**可绑**（类型待定，交给运行期校验）",
                TypeHelper.CanBindTo(typeof(object), typeof(HImage)),
                "改回「不可绑」会让全局变量选不中任何强类型端口，表现为绑定弹窗右侧列表空着");
            Check("【判据】object 作为目标端口同样放行（装箱永远成立）",
                TypeHelper.CanBindTo(typeof(HImage), typeof(object)), "");

            // ---- ④ 防御 ----
            Check("【判据】null 入参不抛、返回 false",
                !TypeHelper.CanBindTo(null!, typeof(double)) && !TypeHelper.CanBindTo(typeof(double), null!), "");
            Check("【判据】取元素类型：数组给元素类型、非数组给自身",
                TypeHelper.GetBindableElementType(typeof(double[])) == typeof(double)
                && TypeHelper.GetBindableElementType(typeof(HImage)) == typeof(HImage), "");

            // ---- ⑤ 空候选陷阱（P0-2）----
            RunEmptyOptionFallback();
        }

        // ==================================================================
        //  ⑤ 空候选陷阱：IsFunctionalEnum 端口取不到候选时必须可手填
        // ==================================================================

        /// <summary>
        /// 探针用枚举：带 <c>[Display]</c> 才能同时验"显示中文、值仍是成员名"（R40）。
        /// 不用插件里的枚举，免得本类多一条对插件的依赖。
        /// </summary>
        private enum ProbeOption
        {
            [System.ComponentModel.DataAnnotations.Display(Name = "圆")]
            Circle,

            [System.ComponentModel.DataAnnotations.Display(Name = "直线")]
            Line,
        }

        /// <summary>
        /// 【P0-2 空候选陷阱】`IsFunctionalEnum` 端口在候选为空时必须退化成**可手填**。
        ///
        /// 此前的行为：`IsOptionPort` 对任意 `IsFunctionalEnum` 置 true，而 XAML 在
        /// `IsOptionPort` 为真时折叠 TextBox、只留一个**不可编辑**的 ComboBox ——
        /// 候选为空时（最常见的原因就是"还没配运动卡 / 宿主没注册候选来源"）
        /// 这一行是一个打不开的空下拉：既选不了也输不了，用户只能先去别处配好方案再回来。
        /// 控件注释里写的"取不到就空着退化成可手填"从来没有实现过。
        ///
        /// 判据必须落在**真控件**上：`IsOptionPort` 由"端口类型 + 候选数"共同决定，
        /// 静态读源码证明不了 DataTrigger 真的把 TextBox 切了回来。
        /// STA 线程 + 手动 Measure/Arrange（同 MotionZMotionChecks 的探针写法），
        /// **不 new Application**（断言宿主不是 WPF 应用）。
        /// </summary>
        private static void RunEmptyOptionFallback()
        {
            Section("[PortBind] 空候选退化：没有候选时回到可手填（不再把人堵在空下拉里）");

            string probe = "未执行";
            var thread = new Thread(() =>
            {
                try
                {
                    // ---- ① 空候选：IsFunctionalEnum，但方案里还没有任何轴（未配卡 / 未注册来源）----
                    var port = new InputPort<string>("Axis", string.Empty, "轴名") { IsFunctionalEnum = true };
                    var editor = new Core.Controls.LinkableValueEditor { Port = port };
                    editor.Measure(new System.Windows.Size(520, 40));
                    editor.Arrange(new System.Windows.Rect(0, 0, 520, 40));
                    editor.UpdateLayout();

                    var valueBox = editor.FindName("PART_ValueBox") as System.Windows.Controls.TextBox;
                    var enumBox = editor.FindName("PART_EnumBox") as System.Windows.Controls.ComboBox;
                    var hint = editor.FindName("PART_NoOptionHint") as System.Windows.Controls.TextBlock;
                    var linkBtn = editor.FindName("PART_LinkBtn") as System.Windows.Controls.Button;
                    string linkVisibility = linkBtn == null ? "null" : linkBtn.Visibility.ToString();

                    var parts = new List<string>
                    {
                        $"空候选只能选={editor.IsOptionPort}",
                        $"空候选手填={editor.IsHandFillFallback}",
                        $"空候选数={editor.EnumOptions.Count}",
                        $"主值框={valueBox?.Visibility}",
                        $"主下拉={enumBox?.Visibility}",
                        $"提示={hint?.Visibility}",
                        $"提示语={hint?.Text}",
                        $"链接按钮={linkVisibility}",
                    };

                    // 走真实写路径：PortValue 就是 TextBox 的绑定目标（用户敲进去的那一路）
                    editor.PortValue = "X";
                    parts.Add($"写回={port.Value}");

                    // ---- ② 有候选：仍必须是下拉，且写回的是成员名（显示中文、值稳定键）----
                    var enumPort = new InputPort<ProbeOption>("Kind", ProbeOption.Line, "查找类型");
                    editor.Port = enumPort;
                    editor.UpdateLayout();

                    var enumOptions = string.Join("/", editor.EnumOptions.Select(o => $"{o.Value}→{o.Display}"));
                    editor.PortValue = "Circle";
                    parts.Add($"枚举只能选={editor.IsOptionPort}");
                    parts.Add($"枚举值框={valueBox?.Visibility}");
                    parts.Add($"枚举下拉={enumBox?.Visibility}");
                    parts.Add($"枚举候选={enumOptions}");
                    parts.Add($"枚举写回={enumPort.Value}");

                    // ---- ③ 候选被清空（模拟方案变化后重取候选）----
                    //  形态要跟着退回手填，且**端口已有值不许被清空**（R35：重填候选不能抹掉用户值）。
                    editor.EnumOptions.Clear();
                    parts.Add($"清空后只能选={editor.IsOptionPort}");
                    parts.Add($"清空后手填={editor.IsHandFillFallback}");
                    parts.Add($"清空后端口值={enumPort.Value}");

                    probe = string.Join(" ", parts);
                }
                catch (Exception ex)
                {
                    probe = "探测异常：" + ex.GetType().Name + "：" + ex.Message;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join(8000);

            Check("★空候选的 IsFunctionalEnum 端口退化为可手填（输入框可见 / 下拉折叠 / 有「无候选，可手填」提示）",
                probe.Contains("空候选只能选=False") && probe.Contains("空候选手填=True")
                && probe.Contains("空候选数=0")
                && probe.Contains("主值框=Visible") && probe.Contains("主下拉=Collapsed")
                && probe.Contains("提示=Visible") && probe.Contains("无候选"),
                probe);

            Check("★空候选不是死路：手填的值真的写进了端口",
                probe.Contains("写回=X"), probe);

            Check("★空候选仍不许链接（名字类端口挂绑定弹窗 = 运行期拿值当名字，R33）",
                probe.Contains("链接按钮=Collapsed"), probe);

            Check("★有候选的枚举端口仍是下拉，写回的是成员名（显示中文、值稳定键 —— R40 口径不变）",
                probe.Contains("枚举只能选=True") && probe.Contains("枚举值框=Collapsed")
                && probe.Contains("枚举下拉=Visible")
                && probe.Contains("Circle→圆") && probe.Contains("Line→直线")
                && probe.Contains("枚举写回=Circle"),
                probe);

            Check("★候选被清空后形态自动退回手填，且端口已有值不被清空（R35：重填候选不能抹掉用户值）",
                probe.Contains("清空后只能选=False") && probe.Contains("清空后手填=True")
                && probe.Contains("清空后端口值=Circle"),
                probe);
        }
    }
}
