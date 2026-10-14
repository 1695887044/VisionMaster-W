using System.Collections.ObjectModel;
using Core.Interfaces;
using VisionMaster.Models;

namespace VisionMaster.ViewModels
{
    // ==================================================================
    //  段3 撤销栈：改序 / 跨分支 / 连线 / 断线的逆操作
    //
    //  设计取舍：
    //  · 接口只声明 Undo/Redo 两方法——命令对象是在「操作已执行」之后构造压栈的，
    //    Redo 即「再执行一次同值写回」，因此无需独立 Execute，省一个口子也省一份状态。
    //  · 不做快照、不撤销坐标：坐标写回只进 FlowLayoutStore，不递增 Version、
    //    不触发重编译，撤销它会让用户拖一下就被回滚，体验上是反效果。
    //  · 跨分支仅同层：本组命令只记录「同一容器内的分支间移动」，
    //    跨层级场景由后续右键菜单「移入容器」接管，不进入本撤销栈。
    //  · 容量 50、切流程清栈：栈深过大会引用陈旧 StepModel 阻塞 GC；
    //    切流程时栈里的 StepModel 已经属于另一张图纸，逆操作会写错地方，必须清空。
    // ==================================================================

    /// <summary>
    /// 段3 撤销命令：操作已执行之后构造，压入撤销栈。
    /// Undo 反向写回，Redo 重放同一写回。
    /// </summary>
    internal interface IUndoCommand
    {
        void Undo();
        void Redo();
    }

    /// <summary>
    /// 改序：把 owner.Steps 中某步骤从 fromIndex 移到 toIndex。
    /// ObservableCollection.Move 触发 CollectionChanged → FlowModel.Version++ → 重编译。
    /// Undo 即反向 Move(to, from)；Move 在 ObservableCollection 上是原子的，不会丢元素。
    /// </summary>
    internal sealed class ReorderCommand : IUndoCommand
    {
        private readonly ObservableCollection<StepModel> _owner;
        private readonly int _fromIndex;
        private readonly int _toIndex;

        public ReorderCommand(ObservableCollection<StepModel> owner, int fromIndex, int toIndex)
        {
            _owner = owner;
            _fromIndex = fromIndex;
            _toIndex = toIndex;
        }

        public void Undo() => _owner.Move(_toIndex, _fromIndex);
        public void Redo() => _owner.Move(_fromIndex, _toIndex);
    }

    /// <summary>
    /// 跨分支移分支：从 fromOwner.RemoveAt(fromIndex) 后 Insert 到 toOwner 的 toIndex。
    /// 跨分支仅限同层（不允许跨层级）——调用方判定通过后再构造本命令。
    /// Remove + Insert 两次 CollectionChanged → Version 累加两次，重编译一次接收全部变化。
    /// </summary>
    internal sealed class MoveBranchCommand : IUndoCommand
    {
        private readonly StepModel _step;
        private readonly ObservableCollection<StepModel> _fromOwner;
        private readonly int _fromIndex;
        private readonly ObservableCollection<StepModel> _toOwner;
        private readonly int _toIndex;

        public MoveBranchCommand(StepModel step,
            ObservableCollection<StepModel> fromOwner, int fromIndex,
            ObservableCollection<StepModel> toOwner, int toIndex)
        {
            _step = step;
            _fromOwner = fromOwner;
            _fromIndex = fromIndex;
            _toOwner = toOwner;
            _toIndex = toIndex;
        }

        public void Undo()
        {
            _toOwner.RemoveAt(_toIndex);
            _fromOwner.Insert(_fromIndex, _step);
        }

        public void Redo()
        {
            _fromOwner.RemoveAt(_fromIndex);
            _toOwner.Insert(_toIndex, _step);
        }
    }

    /// <summary>
    /// 建线：consumerModel.SetLink(port, newLink)。
    /// 同一输入端口只允许一条线，所以建新线前一定先断旧线——
    /// Undo 时若 oldLink != null 恢复旧线，否则 RemoveLink（回到无连线状态）。
    /// LinkReference 是引用类型，但 SetLink 走「整个替换」而非「改字段」，
    /// 因此持有引用快照即可，不需要深拷贝。
    /// </summary>
    internal sealed class ConnectCommand : IUndoCommand
    {
        private readonly StepModel _consumer;
        private readonly string _port;
        private readonly LinkReference _newLink;
        private readonly LinkReference? _oldLink;

        public ConnectCommand(StepModel consumer, string port,
            LinkReference newLink, LinkReference? oldLink)
        {
            _consumer = consumer;
            _port = port;
            _newLink = newLink;
            _oldLink = oldLink;
        }

        public void Undo()
        {
            if (_oldLink != null) _consumer.SetLink(_port, _oldLink);
            else _consumer.RemoveLink(_port);
        }

        public void Redo() => _consumer.SetLink(_port, _newLink);
    }

    /// <summary>
    /// 复合撤销：同一批提交的多条命令（如删除一根聚合线承载的全部解绑）打包成一个撤销单元，
    /// Ctrl+Z 一次整批回滚。Undo 逆序、Redo 正序，与执行顺序对称。
    /// </summary>
    internal sealed class MultiUndoCommand : IUndoCommand
    {
        private readonly List<IUndoCommand> _commands;

        public MultiUndoCommand(IEnumerable<IUndoCommand> commands)
            => _commands = new List<IUndoCommand>(commands);

        public void Undo()
        {
            for (int i = _commands.Count - 1; i >= 0; i--)
                _commands[i].Undo();
        }

        public void Redo()
        {
            foreach (var command in _commands)
                command.Redo();
        }
    }

    /// <summary>
    /// 断线：consumerModel.RemoveLink(port)，Undo 时恢复 oldLink。
    /// 仅记录输入侧断线（一个输出可喂多个输入，从输出侧断是空操作，不入栈）。
    /// </summary>
    internal sealed class DisconnectCommand : IUndoCommand
    {
        private readonly StepModel _consumer;
        private readonly string _port;
        private readonly LinkReference _oldLink;

        public DisconnectCommand(StepModel consumer, string port, LinkReference oldLink)
        {
            _consumer = consumer;
            _port = port;
            _oldLink = oldLink;
        }

        public void Undo() => _consumer.SetLink(_port, _oldLink);
        public void Redo() => _consumer.RemoveLink(_port);
    }

    /// <summary>
    /// 新增步骤（画布拖入 DropToolAt，2026-10-11 撤销补口）：
    /// 记录 (owner 集合, step 引用, 插入下标)，Undo = owner.Remove(step)，Redo = owner.Insert(下标, step)。
    /// 与既有 ReorderCommand/MoveBranchCommand 的「下标型」差别：那两条记录的是
    /// "集合里原有的元素怎么挪"，Move/反向 Move 原子且不丢元素；本命令记录的是
    /// **元素引用本体**——Undo/Redo 都按引用增删，owner 内容变了（外部又删过/加过）时
    /// 按引用判存在性静默降级（Contains/IndexOf 守卫），不会错拿别的步骤。
    /// </summary>
    internal sealed class AddStepCommand : IUndoCommand
    {
        private readonly ObservableCollection<StepModel> _owner;
        private readonly StepModel _step;
        private readonly int _index;

        public AddStepCommand(ObservableCollection<StepModel> owner, StepModel step, int index)
        {
            _owner = owner;
            _step = step;
            _index = index;
        }

        public void Undo()
        {
            // 已被外部移走（或撤销栈里更早的命令回滚过）：静默降级，不抛不重复删
            if (!_owner.Contains(_step)) return;
            _owner.Remove(_step);
        }

        public void Redo()
        {
            // 已在集合里（外部手动加回过）：静默降级；下标越界钳到尾部
            if (_owner.Contains(_step)) return;

            int index = Math.Clamp(_index, 0, _owner.Count);
            _owner.Insert(index, _step);
        }
    }

    /// <summary>
    /// 删除步骤（画布 Delete / 节点右键菜单，2026-10-11 撤销补口）：
    /// 记录 [(owner, step, 原下标)] 列表——容器子树删除会涉及多个 owner（分支 Steps），
    /// 整批打包成一个撤销单元（与整批解绑的 MultiUndoCommand 同一批次粒度）。
    /// Undo = 按记录插回（同 owner 内按下标升序插，先低后高才不会挤动后记录的下标）；
    /// Redo = 按引用再删。删除不再清空撤销栈（LIFO 顺序下，更早的改序命令只有在
    /// 删除被撤销后才轮到，下标快照届时已恢复，不存在错位回滚）。
    /// </summary>
    internal sealed class DeleteStepCommand : IUndoCommand
    {
        /// <summary>一条删除记录：步骤被从哪个集合的哪个下标移走</summary>
        internal sealed record DeleteEntry(ObservableCollection<StepModel> Owner, StepModel Step, int Index);

        private readonly List<DeleteEntry> _entries;

        public DeleteStepCommand(IEnumerable<DeleteEntry> entries)
        {
            // 同 owner 内按下标升序：Undo 逐条 Insert 时，先插低下标不影响高下标的落位
            _entries = entries.OrderBy(e => e.Index).ToList();
        }

        public void Undo()
        {
            foreach (var entry in _entries)
            {
                // 已被外部放回（或更晚的命令动过）：静默降级；下标越界钳到尾部
                if (entry.Owner.Contains(entry.Step)) continue;

                int index = Math.Clamp(entry.Index, 0, entry.Owner.Count);
                entry.Owner.Insert(index, entry.Step);
            }
        }

        public void Redo()
        {
            foreach (var entry in _entries)
            {
                // 按引用现查现删：不信任记录的下标（撤销期间同 owner 可能增删过别的步骤）
                int index = entry.Owner.IndexOf(entry.Step);
                if (index >= 0)
                    entry.Owner.RemoveAt(index);
            }
        }
    }
}
