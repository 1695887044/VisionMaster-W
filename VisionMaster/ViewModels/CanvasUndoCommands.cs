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
}
