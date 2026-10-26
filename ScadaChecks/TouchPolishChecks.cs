using VisionMaster.Scada.Controls;

namespace ScadaChecks
{
    internal partial class Program
    {
        /// <summary>
        /// [IP] 触摸优化第二批的两条新行为（2026-10-25 评审 Q1/Q3 落地后的判据）：
        /// ① IOField 的数值与单位拆成两条只读属性（模板两列显示，小数点才对得齐）；
        /// ② Slider 的数值气泡跟手（在 SliderChecks 的 [SL12b] 里用真布局钉住）。
        ///
        /// 这里只钉 ①：它是纯属性语义，不需要模板与布局，造一个元素改属性即可。
        /// </summary>
        private static class TouchPolishChecks
        {
            // 图元控件要在 STA 线程上造（ScadaElementBase 的构造会碰 Dispatcher）
            public static void Run() => RunOnSta(RunChecks);

            private static void RunChecks()
            {
                Section("[IP] 数值域：数值与单位拆列");

                var field = new IOFieldElement { Unit = "%", Value = 62.4 };

                Check("[IP1] ValueOnlyText 只含数值、ValueText 仍含单位（老调用方语义不变）",
                    field.ValueOnlyText == "62.4"
                    && field.ValueText == "62.4 %",
                    $"only=[{field.ValueOnlyText}] full=[{field.ValueText}]");

                field.Unit = null;

                Check("[IP2] 单位清空：ValueOnlyText 不变、ValueText 退回纯数值（单位列宽 0，不占位）",
                    field.ValueOnlyText == "62.4"
                    && field.ValueText == "62.4",
                    $"only=[{field.ValueOnlyText}] full=[{field.ValueText}]");
            }

            private static void RunOnSta(System.Action action)
            {
                System.Exception? error = null;
                var thread = new System.Threading.Thread(() =>
                {
                    try { action(); }
                    catch (System.Exception ex) { error = ex; }
                });
                thread.SetApartmentState(System.Threading.ApartmentState.STA);
                thread.Start();
                thread.Join();

                if (error != null)
                    Check("[IP] 数值域断言全程未抛异常", false, error.ToString());
            }
        }
    }
}
