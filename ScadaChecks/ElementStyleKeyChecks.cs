using System;
using System.Linq;
using System.Reflection;
using System.Windows;
using VisionMaster.Scada.Controls;

namespace ScadaChecks
{
    internal partial class Program
    {
        /// <summary>
        /// [DSK] 每个进了工具箱的图元控件，都必须把 WPF 的默认样式键钉到自己的类型上。
        ///
        /// 为什么要有这条：主题里的模板是 <c>&lt;Style TargetType="{x:Type local:XxxElement}"&gt;</c>，
        /// WPF 按 <c>DefaultStyleKey</c> 查隐式样式。类里不写
        /// <c>DefaultStyleKeyProperty.OverrideMetadata</c> 就继承基类的键 → 那份模板永远查不到 →
        /// 画布上只剩基类的空模板，**拖上去什么都看不见**，而且不抛任何异常。
        ///
        /// 2026-10-25 实锤：Report / ProcessSymbol / PipeRun 三个类型漏了这一句。
        /// 断言宿主当时全绿，因为它显式赋了 <c>Style = theme[typeof(...)]</c>——
        /// 显式赋值完全绕过 DefaultStyleKey 这条查找路径，只有真机画布才走得出来。
        ///
        /// 判据取"控件实例上 DefaultStyleKey 的实际值"，不是扫源码里有没有那句话
        /// （写错类型的 OverrideMetadata 一样能骗过文本扫描）。
        /// </summary>
        private static class ElementStyleKeyChecks
        {
            public static void Run() => RunOnSta(RunChecks);

            private static void RunChecks()
            {
                Section("[DSK] 图元控件的默认样式键必须指向自己的类型");

                var dp = (DependencyProperty?)typeof(FrameworkElement)
                    .GetField("DefaultStyleKeyProperty", BindingFlags.NonPublic | BindingFlags.Static)
                    ?.GetValue(null);

                if (dp == null)
                {
                    // 框架内部改名时宁可红着说清楚，也不静默跳过（R38）
                    Check("[DSK0] 能读到 FrameworkElement.DefaultStyleKeyProperty", false,
                        "反射取不到该字段，本组断言无法成立");
                    return;
                }

                var wrong = new System.Collections.Generic.List<string>();
                var checkedCount = 0;

                foreach (var descriptor in ElementRegistry.All.Where(d => d.ShowInToolbox))
                {
                    ScadaElementBase control;
                    try
                    {
                        control = descriptor.CreateControl();
                    }
                    catch (Exception ex)
                    {
                        wrong.Add($"{descriptor.TypeKey}（造控件抛异常：{ex.Message}）");
                        continue;
                    }

                    checkedCount++;
                    var key = control.GetValue(dp);
                    if (!Equals(key, control.GetType()))
                        wrong.Add($"{descriptor.TypeKey} → {key?.GetType().Name ?? "null"}");
                }

                Check("[DSK1] 工具箱里每个图元控件都覆盖了 DefaultStyleKeyProperty（否则画布上拖上去不可见）",
                    wrong.Count == 0 && checkedCount > 0,
                    wrong.Count == 0
                        ? $"checked={checkedCount}"
                        : $"checked={checkedCount} / 未钉住：{string.Join("；", wrong)}");
            }

            private static void RunOnSta(Action action)
            {
                Exception? error = null;
                var thread = new System.Threading.Thread(() =>
                {
                    try { action(); }
                    catch (Exception ex) { error = ex; }
                });
                thread.SetApartmentState(System.Threading.ApartmentState.STA);
                thread.Start();
                thread.Join();

                if (error != null)
                    Check("[DSK] 默认样式键断言全程未抛异常", false, error.ToString());
            }
        }
    }
}
