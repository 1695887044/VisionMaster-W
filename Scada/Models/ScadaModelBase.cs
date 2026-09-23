using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Prism.Mvvm;

namespace VisionMaster.Scada
{
    /// <summary>
    /// 所有<b>可落盘文档模型</b>的公共基类：在 Prism <see cref="BindableBase"/> 之上，
    /// 给每一次真实属性写挂上"守卫 + 记账"两个钩子。
    ///
    /// <b>为什么值得插这一层</b>：全仓 8 个模型的所有标量属性 setter 都是同一句
    /// <c>set =&gt; SetProperty(ref _x, value);</c>——也就是说 <c>SetProperty</c> 是
    /// 所有标量写的<b>唯一汇聚点</b>。在这里 override 一次，
    /// 就同时拿到了"全局写守卫"和"全局变更记账"，不必在 8 个类几十个 setter 里各写一遍
    /// （那样每加一个属性都是一次漏记的机会）。
    ///
    /// <b>谁不该继承它</b>：<see cref="ScadaRuntime"/>——它是运行态（当前页、页栈），
    /// 不是要落盘的文档数据。运行态改了不该进撤销栈，也不该被"编辑器写守卫"约束。
    ///
    /// <b>拦不住的地方（务必知情）</b>：
    /// <list type="bullet">
    /// <item><see cref="ScadaElement.Properties"/> 的 setter 直接给字段赋值（不走 <c>SetProperty</c>），
    /// 所以它由 <see cref="ScadaElement"/> 自己补守卫与记账；</item>
    /// <item>集合增删（<c>Elements.Add</c> 等）走 <c>CollectionChanged</c>，由各集合的回调负责记账。</item>
    /// </list>
    /// </summary>
    public abstract class ScadaModelBase : BindableBase
    {
        /// <summary>
        /// 标量属性写的统一收口。三个动作的顺序有讲究：
        /// <list type="number">
        /// <item><b>同值短路</b>（与基类同口径）：没变就是没写，既不拦也不记。
        /// 这条很关键——<see cref="ScadaPage.TryMoveElementZ"/> 会遍历全画面每个图元写一次
        /// <c>ZIndex</c>，绝大多数图元的值其实没动，靠这条才不会产生几百条垃圾记录。</item>
        /// <item><b>守卫</b>在真正落值<b>之前</b>：违规时抛异常，状态保持干净，
        /// 调用方 catch 之后还能继续用（若先落值再抛，对象就留在半改状态）。</item>
        /// <item><b>记账</b>在落值<b>之后</b>：先记后写的话，写失败就留下一条指向不存在状态的记录。</item>
        /// </list>
        /// </summary>
        protected override bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string propertyName = null!)
        {
            if (EqualityComparer<T>.Default.Equals(storage, value))
                return false;

            ScadaWriteGuard.OnWrite(this, propertyName);

            var oldValue = storage;
            var changed = base.SetProperty(ref storage, value, propertyName);

            if (changed)
                ScadaChangeScope.Current?.Record(new ScalarChange(this, propertyName, oldValue, value));

            return changed;
        }

        /// <summary>
        /// 带回调的重载（Prism 用它做"值变了顺带做点别的"，如联动通知 <c>Detail</c>）。
        /// 语义与三参版本完全一致，只是多传一个 <paramref name="onChanged"/> 给基类。
        /// </summary>
        protected override bool SetProperty<T>(ref T storage, T value, Action onChanged, [CallerMemberName] string propertyName = null!)
        {
            if (EqualityComparer<T>.Default.Equals(storage, value))
                return false;

            ScadaWriteGuard.OnWrite(this, propertyName);

            var oldValue = storage;
            var changed = base.SetProperty(ref storage, value, onChanged, propertyName);

            if (changed)
                ScadaChangeScope.Current?.Record(new ScalarChange(this, propertyName, oldValue, value));

            return changed;
        }
    }
}
