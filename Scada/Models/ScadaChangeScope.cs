using System;
using System.Collections.Generic;
using System.Threading;

namespace VisionMaster.Scada
{
    /// <summary>
    /// 一次可撤销编辑的作用域。用法固定成一行：
    /// <code>
    /// using (page.BeginEdit("移动图元"))
    /// {
    ///     element.X = 120;
    ///     element.Y = 80;
    /// }
    /// </code>
    /// 作用域结束时若真有改动，产出一条变更记录压入撤销栈；没改动则什么都不发生。
    ///
    /// <b>为什么要一个作用域对象，而不是每次写都压栈</b>：
    /// 拖一次图元会产生几十次属性写（鼠标每动一像素一次），逐次压栈就成了
    /// "撤销要按几十下 Ctrl+Z"——那不是撤销，那是逐帧回放。作用域把
    /// "用户视角的一次操作"和"程序视角的 N 次属性写"重新对齐。
    ///
    /// <b>嵌套自动合并</b>：内层作用域不产记录，改动一律汇进最外层。
    /// 这样"批量对齐 10 个图元"只需外层包一次，内层每个图元各走自己的
    /// <c>Try*</c> 入口（它们内部也开作用域），最终仍然只产生一条记录。
    /// </summary>
    public interface IScadaChangeScope : IDisposable
    {
        /// <summary>操作名（撤销/重做按钮的提示文字用，如"移动图元"）</summary>
        string Label { get; }

        /// <summary>本作用域（含嵌套子作用域）到目前为止是否记录到了真实改动</summary>
        bool HasChanges { get; }
    }

    /// <summary>
    /// <see cref="IScadaChangeScope"/> 的实现。
    ///
    /// 当前作用域用 <see cref="ThreadStaticAttribute"/> 持有——SCADA 编辑在 UI 线程，
    /// 而断言可能在别的线程构造数据，两条线各有各的当前作用域才不会串。
    /// </summary>
    public sealed class ScadaChangeScope : IScadaChangeScope
    {
        [ThreadStatic] private static ScadaChangeScope? _current;
        [ThreadStatic] private static int _suspendRecording;

        private readonly ScadaChangeScope? _parent;

        /// <summary>
        /// 变更的落点容器。<b>内层共享父级的这个 list</b>——这就是"嵌套合并"的实现：
        /// 只有最外层 Dispose 时才会把这个 list 打包压栈，内层 Dispose 只是退栈。
        /// </summary>
        private readonly List<ScadaChange> _sink;

        private bool _disposed;

        public string Label { get; }

        private ScadaChangeScope(string label, ScadaChangeScope? parent)
        {
            Label = label;
            _parent = parent;
            _sink = parent?._sink ?? new List<ScadaChange>();
        }

        /// <summary>当前线程的活动作用域；没有则为 null（此时模型写不进任何撤销栈）</summary>
        internal static ScadaChangeScope? Current => _current;

        /// <summary>是否正在执行"回写"（撤销/重做）——回写期间不采变更，否则撤销自己会再进栈</summary>
        internal static bool IsRecordingSuspended => _suspendRecording > 0;

        /// <summary>
        /// 打开一个编辑作用域。<paramref name="label"/> 是操作名（会显示在撤销按钮上），
        /// 建议用"动词 + 对象"的短句：<c>"移动图元"</c>、<c>"改图层可见性"</c>。
        /// </summary>
        public static IScadaChangeScope Begin(string label)
        {
            var scope = new ScadaChangeScope(string.IsNullOrWhiteSpace(label) ? "编辑" : label, _current);
            _current = scope;
            ScadaWriteGuard.EnterScope();
            return scope;
        }

        /// <summary>
        /// 在"非编辑"语义下造一个模型对象：既不触发写守卫，也不产生变更记录。
        ///
        /// <b>为什么需要它</b>：对象初始化器（<c>new ScadaLayer { Name = "图层_1" }</c>）走的是属性
        /// setter，一样会经过 <see cref="ScadaModelBase.SetProperty"/>。但此刻这个对象<b>还没进文档</b>——
        /// 给它填初始值既不是"编辑"（不该进撤销栈，否则撤销一次"新建图层"要先冒出几条
        /// <c>Name: "" → "图层_1"</c> 的垃圾记录），也不是"越权写"（不该被
        /// <see cref="ScadaWriteGuard.Strict"/> 拦下）。<b>构造与编辑是两件事</b>，本方法就是那条分界线。
        ///
        /// 用法：<c>var layer = ScadaChangeScope.Detached(() =&gt; new ScadaLayer { Name = n });</c>
        /// </summary>
        public static T Detached<T>(Func<T> factory)
        {
            if (factory == null)
                throw new ArgumentNullException(nameof(factory));

            using (ScadaWriteGuard.Suspend())
            using (SuspendRecording())
            {
                return factory();
            }
        }

        public bool HasChanges => _sink.Count > 0;

        /// <summary>登记一条变更（由各采集点调用）</summary>
        internal void Record(ScadaChange change)
        {
            if (change == null || _suspendRecording > 0)
                return;

            _sink.Add(change);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            ScadaWriteGuard.ExitScope();
            _current = _parent;

            // 内层作用域：只退栈，不产记录（改动已经在共享的 _sink 里，留给最外层收口）
            if (_parent != null)
                return;

            // 空作用域不产记录：对齐"同值不刷 Version"的既有口径——
            // 用户点了一下但没真改（拖回原位、把属性改成原值），不该占一次撤销位。
            if (_sink.Count == 0)
                return;

            ScadaEditHistory.Push(new ScadaChangeSet(Label, _sink));
        }

        /// <summary>
        /// 回写期间挂起采集。撤销/重做会重新走一遍属性 setter，
        /// 不挂起的话这些回写会被当成新改动再记一遍，栈就越撤越多。
        /// </summary>
        internal static IDisposable SuspendRecording()
        {
            _suspendRecording++;
            return new RecordingToken();
        }

        private sealed class RecordingToken : IDisposable
        {
            private bool _disposed;

            public void Dispose()
            {
                if (_disposed)
                    return;

                _disposed = true;
                _suspendRecording = _suspendRecording > 0 ? _suspendRecording - 1 : 0;
            }
        }
    }
}
