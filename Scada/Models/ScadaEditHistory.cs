using System;
using System.Collections.Generic;

namespace VisionMaster.Scada
{
    /// <summary>
    /// 一条变更记录 = 用户视角的一次操作，内含若干条值级 diff。
    /// </summary>
    public sealed class ScadaChangeSet
    {
        private readonly List<ScadaChange> _changes;

        /// <summary>操作名（"移动图元"等），供撤销/重做按钮显示</summary>
        public string Label { get; }

        /// <summary>这条记录里包了多少次底层属性写（诊断/断言用）</summary>
        public int ChangeCount => _changes.Count;

        internal ScadaChangeSet(string label, List<ScadaChange> changes)
        {
            Label = label;
            _changes = changes;
        }

        /// <summary>
        /// 回写整条记录。<b>撤销必须逆序</b>：一次操作内部的写有先后依赖
        /// （比如"先改 LayerId 再改 ZIndex"），正序回滚会让中间态短暂自相矛盾，
        /// 而中间态会被集合订阅看见并触发索引重建——逆序是唯一稳妥的顺序。
        /// </summary>
        internal void Apply(bool undo)
        {
            // 双挂起：守卫（回写不应被"编辑器必须走入口"的规则拦下）
            // + 采集（回写不应被当成新改动再记一遍）。
            using (ScadaWriteGuard.Suspend())
            using (ScadaChangeScope.SuspendRecording())
            {
                if (undo)
                {
                    for (var i = _changes.Count - 1; i >= 0; i--)
                        _changes[i].Apply(true);
                }
                else
                {
                    for (var i = 0; i < _changes.Count; i++)
                        _changes[i].Apply(false);
                }
            }
        }
    }

    /// <summary>
    /// 撤销/重做栈（单栈双向，与 Flow 侧 <c>FlowCanvasViewModel</c> 的范式一致）。
    ///
    /// <b>为什么是静态的</b>：撤销是"编辑器级"能力而不是"页面级"能力——
    /// 用户按下 Ctrl+Z 时脑子里想的是"撤掉我刚才那一下"，
    /// 而不是"撤掉当前页面的最后一下"。而一次操作可能跨页（改页名）、
    /// 跨对象（删页面时连带删了里面的图层），把栈挂在 <see cref="ScadaPage"/> 上
    /// 就必须回答"这条记录算谁的"，答案会很难看。SCADA 编辑器同一时刻只开一个文档，
    /// 单一栈是这个场景下唯一说得通的模型。
    ///
    /// 容量对齐 Flow 侧 <c>MaxUndoStack</c>（50）：超出丢最早的一条。
    /// 切换文档时必须 <see cref="Clear"/>——否则新文档的 Ctrl+Z 会撤到旧文档的对象上。
    /// </summary>
    public static class ScadaEditHistory
    {
        /// <summary>撤销栈深度上限（与 Flow 画布一致）</summary>
        public const int MaxDepth = 50;

        private static readonly List<ScadaChangeSet> _undo = new();
        private static readonly List<ScadaChangeSet> _redo = new();

        /// <summary>撤销栈里还有几条</summary>
        public static int UndoCount => _undo.Count;

        /// <summary>重做栈里还有几条</summary>
        public static int RedoCount => _redo.Count;

        public static bool CanUndo => _undo.Count > 0;

        public static bool CanRedo => _redo.Count > 0;

        /// <summary>下一次撤销会撤掉的操作名（按钮提示用）；栈空时为 null</summary>
        public static string? NextUndoLabel => _undo.Count > 0 ? _undo[^1].Label : null;

        /// <summary>下一次重做会重放的操作名；栈空时为 null</summary>
        public static string? NextRedoLabel => _redo.Count > 0 ? _redo[^1].Label : null;

        /// <summary>栈发生变化（压栈/撤销/重做/清空）——UI 据此刷新按钮可用态</summary>
        public static event EventHandler? Changed;

        /// <summary>压入一条记录。压栈即清空重做栈：这是"单栈双向"的核心纪律——
        /// 新分支一旦产生，原来的重做路径就不再成立了。</summary>
        internal static void Push(ScadaChangeSet set)
        {
            _undo.Add(set);

            if (_undo.Count > MaxDepth)
                _undo.RemoveAt(0); // 丢最早的，保留最近的 MaxDepth 条

            _redo.Clear();
            RaiseChanged();
        }

        /// <summary>撤销一步；栈空返回 false（调用方据此决定是否响铃/置灰）</summary>
        public static bool Undo()
        {
            if (_undo.Count == 0)
                return false;

            var set = _undo[^1];
            _undo.RemoveAt(_undo.Count - 1);

            set.Apply(undo: true);
            _redo.Add(set);

            RaiseChanged();
            return true;
        }

        /// <summary>重做一步；栈空返回 false</summary>
        public static bool Redo()
        {
            if (_redo.Count == 0)
                return false;

            var set = _redo[^1];
            _redo.RemoveAt(_redo.Count - 1);

            set.Apply(undo: false);
            _undo.Add(set);

            RaiseChanged();
            return true;
        }

        /// <summary>清空两个栈（切换文档时调；断言段之间也应调，避免互相串）</summary>
        public static void Clear()
        {
            if (_undo.Count == 0 && _redo.Count == 0)
                return;

            _undo.Clear();
            _redo.Clear();
            RaiseChanged();
        }

        private static void RaiseChanged() => Changed?.Invoke(null, EventArgs.Empty);
    }
}
