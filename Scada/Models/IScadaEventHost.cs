using System.Collections.ObjectModel;

namespace VisionMaster.Scada
{
    /// <summary>
    /// "能挂事件钩子的宿主"——<see cref="ScadaElement"/> 与 <see cref="ScadaPage"/> 共有的那一小块能力。
    ///
    /// 为什么值得单独抽一个接口（而不是让属性面板认识两个具体类型）：
    /// 事件这套东西从一开始就是"宿主 + 事件 + 一串动作"，图元和画面只是宿主不同。
    /// 两边各自实现了同一组方法（语义逐字一致，<c>FindEventHook</c> / <c>GetOrAddEventHook</c> /
    /// <c>AddEventHook</c> / <c>RemoveEventHook</c>），却没有任何共同类型可依赖，
    /// 于是"把动作表编辑出来"这件事就只能在图元侧存在——画面级事件因此长期只能手改 .vms。
    /// 抽这个接口，就是为了让<b>一份</b>编辑逻辑能同时服务两种宿主，而不是再抄一份。
    ///
    /// 为什么它住在领域层：接口上这几个成员全是领域概念（钩子、事件、变更作用域），
    /// 一个 WPF 类型都没有，所以依赖方向不变（<c>Core → Scada → Scada.Controls → VisionMaster</c>），
    /// 面板只是它的一个消费者。
    ///
    /// 为什么不干脆让两个类继承同一个基类：它们各自还继承着 <see cref="ScadaModelBase"/>，
    /// 而"能挂钩子"并不是每个模型都成立的事实（图层就不能），塞进基类等于给所有模型发一张
    /// 它们用不上的能力票。接口恰好表达"这一部分模型可以"。
    /// </summary>
    public interface IScadaEventHost
    {
        /// <summary>
        /// 已配的事件钩子集合（用户配了什么）。
        /// 只读暴露即可：接口的消费者只做查询与增删，整体替换集合是反序列化的事，
        /// 不该由面板这一层做（两个实现类都带 setter，是为 <c>Json.NET</c> 准备的）。
        /// </summary>
        ObservableCollection<ScadaEventHook> EventHooks { get; }

        /// <summary>找某个事件已配的钩子；没配过返回 null（同事件多条时返回靠前者）</summary>
        ScadaEventHook? FindEventHook(ScadaEventType eventType);

        /// <summary>找某个事件的钩子，没有就地建一条（属性面板"勾上某个事件"走这个）</summary>
        ScadaEventHook GetOrAddEventHook(ScadaEventType eventType);

        /// <summary>新建一条空动作钩子并加入集合</summary>
        ScadaEventHook AddEventHook(ScadaEventType eventType);

        /// <summary>摘掉某个事件的钩子（含它下面所有动作），返回是否真删掉了东西</summary>
        bool RemoveEventHook(ScadaEventType eventType);

        /// <summary>
        /// 开一个变更作用域，把"勾一个框"这类用户眼里的<b>一次</b>操作合成一条撤销记录。
        /// 放上接口的理由：面板在两种宿主上做的是同一件事（建钩子 + 塞首条动作），
        /// 若只能从具体类型拿到作用域，那份逻辑就又要分叉了。
        /// </summary>
        IScadaChangeScope BeginEdit(string label);
    }
}
