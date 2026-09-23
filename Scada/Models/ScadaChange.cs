using System;
using System.Collections;
using System.Collections.Specialized;
using System.Reflection;

namespace VisionMaster.Scada
{
    /// <summary>
    /// 一次模型改动的最小记录单位。
    ///
    /// <b>为什么是"值级 diff"而不是"对象快照"</b>：
    /// 快照（<c>CloneHelper.DeepCopy</c>）看着省事，但在本项目里会踩三个坑——
    /// ① 换对象：全仓多处用 <c>ReferenceEquals</c> 认身份（<c>ScadaDocument.FindPage</c>、
    ///    <c>ScadaCanvas.Contains</c>、<c>ScadaRuntime</c> 的当前页判定），撤销后对象换了一批，
    ///    这些判定全落空；② <c>Version</c> 标了 <c>[JsonIgnore]</c>，快照回填会把它抹掉；
    /// ③ <c>[JsonConstructor]</c> 会重挂一遍集合订阅，跟登记表对不上。
    /// 值级 diff 只改字段、不换对象，三个坑一个都不踩。
    ///
    /// <b>回滚方向由 <see cref="Apply"/> 的入参决定</b>，不为撤销/重做各写一份逻辑——
    /// 一份逻辑两处用，就不会出现"撤销对了、重做错了"这种只在第二次点才暴露的 bug。
    /// </summary>
    public abstract class ScadaChange
    {
        /// <summary>回写这条变更。<paramref name="undo"/> = true 回旧值，false 回新值。</summary>
        public abstract void Apply(bool undo);
    }

    /// <summary>
    /// 标量属性变更（位置/尺寸/名称/可见性……）。
    ///
    /// 回写走 <b>反射调属性 setter</b> 而不是直写字段：属性 setter 里挂着
    /// <c>SetProperty</c> 的完整链路（变更通知 + <c>ScadaPage</c> 的 <c>Version++</c> +
    /// <c>ElementId</c> 的索引修正）。直写字段会绕过这些，表现为"撤销后画布不重绘"。
    /// 反射比委托慢，但撤销是人工低频操作（点一次 Ctrl+Z），这点开销换来的正确性划算。
    /// </summary>
    internal sealed class ScalarChange : ScadaChange
    {
        private readonly object _target;
        private readonly string _propertyName;
        private readonly object? _oldValue;
        private readonly object? _newValue;

        public ScalarChange(object target, string propertyName, object? oldValue, object? newValue)
        {
            _target = target;
            _propertyName = propertyName;
            _oldValue = oldValue;
            _newValue = newValue;
        }

        public override void Apply(bool undo)
        {
            var property = _target.GetType().GetProperty(
                _propertyName,
                BindingFlags.Public | BindingFlags.Instance);

            // 属性被改名/删掉时静默跳过：宁可少回滚一条，也不要在撤销时抛异常把栈卡死。
            if (property == null || !property.CanWrite)
                return;

            property.SetValue(_target, undo ? _oldValue : _newValue);
        }
    }

    /// <summary>
    /// 图元属性袋（<see cref="ScadaElement.Properties"/>）里一个键的变更。
    ///
    /// 为什么不复用 <see cref="ScalarChange"/>：属性袋的写不走属性 setter，
    /// 走的是 <c>ScadaElement.SetProperty(key, value)</c> 这个普通方法，
    /// 反射找不到它，得按"键级"单独记。
    /// </summary>
    internal sealed class PropertyBagChange : ScadaChange
    {
        private readonly ScadaElement _element;
        private readonly string _key;
        private readonly string? _oldValue;
        private readonly string? _newValue;

        public PropertyBagChange(ScadaElement element, string key, string? oldValue, string? newValue)
        {
            _element = element;
            _key = key;
            _oldValue = oldValue;
            _newValue = newValue;
        }

        public override void Apply(bool undo)
        {
            // 传 null 即删键——与 SetProperty 的"空值等于没配过"口径一致，
            // 撤销"新增一个键"时不会在文件里留下一个空串条目。
            _element.SetProperty(_key, undo ? _oldValue : _newValue);
        }
    }

    /// <summary>
    /// 集合增删变更（图元/图层/钩子/绑定/动作）。
    ///
    /// <b>只记 Add / Remove，不记 Reset</b>：<c>Clear()</c> 走 Reset 分支且
    /// <c>NotifyCollectionChangedEventArgs.OldItems</c> 为 null，事件到达时集合已经空了，
    /// 拿不到"被清掉的是什么"、更拿不到顺序。当前编辑器没有"清空画面"这类入口
    /// （图元删除都还没做），所以不给 Reset 编一套不可靠的恢复逻辑——
    /// 等真加了"清空"功能，那时它自然会被写成"逐条 Remove"（可撤销），而不是 <c>Clear()</c>。
    ///
    /// <b>撤销移除时按 <c>Remove(item)</c> 而不是 <c>RemoveAt(index)</c></b>：
    /// 索引会随其他操作漂移，按对象移除才稳。
    /// </summary>
    internal sealed class CollectionChange : ScadaChange
    {
        private readonly IList _collection;
        private readonly object _item;
        private readonly int _index;
        private readonly bool _isAdd;

        public CollectionChange(IList collection, object item, int index, bool isAdd)
        {
            _collection = collection;
            _item = item;
            _index = index;
            _isAdd = isAdd;
        }

        public override void Apply(bool undo)
        {
            // 记录时是"添加"，撤销就该"移除"，重做再"添加"——异或一下方向。
            var shouldAdd = _isAdd ^ undo;

            if (shouldAdd)
            {
                if (_collection.Contains(_item))
                    return; // 已在集合里（重复撤销/顺序错乱）时不重复插，避免出现两份

                var at = _index < 0 || _index > _collection.Count ? _collection.Count : _index;
                _collection.Insert(at, _item);
            }
            else
            {
                _collection.Remove(_item);
            }
        }
    }

    /// <summary>
    /// 把 <c>ObservableCollection</c> 的变更事件转成撤销记录。
    ///
    /// 全仓 6 个集合回调（<c>ScadaPage</c> 的图元/图层/钩子、<c>ScadaElement</c> 的绑定/钩子、
    /// <c>ScadaEventHook</c> 的动作）共用这一段，避免六份各自演化的记账逻辑
    /// ——那种代码最容易出现"某个集合忘了记，撤销时只回去一半"。
    /// </summary>
    internal static class ScadaCollectionRecorder
    {
        public static void Record(IList collection, NotifyCollectionChangedEventArgs e)
        {
            var scope = ScadaChangeScope.Current;

            // 没有活动作用域（反序列化、测试夹具构造）或整体清空（拿不到被清的是谁）时不记。
            if (scope == null || e.Action == NotifyCollectionChangedAction.Reset)
                return;

            // 先记"被移除的"，再记"新增的"：撤销时逆序执行，正好是"先撤新增、再恢复移除"。
            if (e.OldItems != null)
            {
                foreach (var item in e.OldItems)
                    scope.Record(new CollectionChange(collection, item, e.OldStartingIndex, isAdd: false));
            }

            if (e.NewItems != null)
            {
                foreach (var item in e.NewItems)
                    scope.Record(new CollectionChange(collection, item, e.NewStartingIndex, isAdd: true));
            }
        }
    }
}
