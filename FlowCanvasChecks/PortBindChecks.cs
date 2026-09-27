using System;
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
        }
    }
}
