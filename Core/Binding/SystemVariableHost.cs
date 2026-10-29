using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using Core.Interfaces;
using VisionMaster.Models;

namespace VisionMaster.Binding
{
    /// <summary>
    /// 系统变量宿主：独占 <c>workspace.SystemVariables</c>，把多个"系统变量提供方"的快照合并成一份集合。
    ///
    /// 为什么需要它：系统变量现在有两个来源（运动轴状态、IO 点位），将来还会有相机/PLC/报警。
    /// 若让各提供方各自写同一个集合，任何一方的"清理不属于自己的项"都会误删别人的变量
    /// （2026-10-18 实测：改一次运动卡设置会把 IO 变量全部删光）。
    ///
    /// 契约：
    /// 1) 提供方只产出快照（有序、不可变），不直接写集合；
    /// 2) 合并顺序 = 提供方注册顺序，再按快照内顺序（保证界面顺序稳定）；
    /// 3) 只做增删移，不 Clear+Add（Clear 会发 Reset，注册表据此清空系统索引，
    ///    存在"系统变量全查不到"的窗口，界面也会整体闪一下）；
    /// 4) 集合改写串行化：运动发布器 100ms 一拍、IO 可能 20ms 一拍，并发改写会让
    ///    ObservableCollection 抛"集合已修改"。
    /// </summary>
    public sealed class SystemVariableHost
    {
        private readonly object _gate = new();

        /// <summary>
        /// 集合改写的串行闸：三个提供方（运动 100ms、通讯/相机各 500ms）各自在自己的定时器线程上
        /// 调 <see cref="Refresh"/>，若只有 <see cref="_gate"/>，两个线程会同时进入
        /// <see cref="ApplyToCollection"/>，ObservableCollection 直接抛"集合已修改"。
        /// 与 <see cref="_gate"/> 分开：合表与写集合是两件互不依赖的事，共用一把锁会让
        /// "写集合时订阅方回调 Refresh" 变成自锁。
        /// </summary>
        private readonly object _applyGate = new();
        private readonly ObservableCollection<IVariable> _target;
        private readonly List<ISystemVariableProvider> _providers = new();
        private IReadOnlyList<IVariable> _current = Array.Empty<IVariable>();

        public SystemVariableHost(ObservableCollection<IVariable> target)
        {
            _target = target ?? throw new ArgumentNullException(nameof(target));
        }

        /// <summary>已注册的提供方（诊断用）</summary>
        public IReadOnlyList<ISystemVariableProvider> Providers
        {
            get { lock (_gate) return _providers.ToList(); }
        }

        /// <summary>当前合并结果（不可变快照，换表时整份替换）</summary>
        public IReadOnlyList<IVariable> Snapshot()
        {
            lock (_gate) return _current;
        }

        public void Register(ISystemVariableProvider provider)
        {
            if (provider == null) throw new ArgumentNullException(nameof(provider));
            lock (_gate)
            {
                if (_providers.Any(p => ReferenceEquals(p, provider))) return;
                _providers.Add(provider);
            }

            provider.Changed += OnProviderChanged;
            Refresh();
        }

        public void Unregister(ISystemVariableProvider provider)
        {
            if (provider == null) return;
            provider.Changed -= OnProviderChanged;
            lock (_gate) _providers.Remove(provider);
            Refresh();
        }

        private void OnProviderChanged(object? sender, EventArgs e) => Refresh();

        /// <summary>由任一提供方在自身快照变化后调用；也可由宿主定期调用。</summary>
        public void Refresh()
        {
            // 整段串行化：合并 → 交换 _current → 写集合 必须是一个原子序列。
            //
            // 只锁"写集合"那一步会留下永久幽灵变量：T1 交换（previous=old）→ T2 交换
            // （previous=merged1）→ T2 写集合 → T1 写集合（按 old 算 managedSet，把 T2 刚删掉的
            // 项又插回去）。集合于是变成 merged2 ∪ merged1，而 _current=merged2 ——
            // 下一次 Refresh 的 managedSet 不含这些多出来的项，它们再也不会被删除。
            //
            // 锁顺序固定为 _applyGate → _gate：Register/Unregister 只拿 _gate 且不在持锁时调 Refresh，
            // 因此不存在反向路径，无死锁。
            lock (_applyGate)
            {
                List<ISystemVariableProvider> providers;
                lock (_gate) providers = _providers.ToList();

                var merged = new List<IVariable>();
                var seenIds = new HashSet<Guid>();
                var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var provider in providers)
                {
                    IReadOnlyList<IVariable> items;
                    try
                    {
                        items = provider.Snapshot() ?? Array.Empty<IVariable>();
                    }
                    catch
                    {
                        // 某个提供方炸掉不该让其它来源的变量消失
                        continue;
                    }

                    foreach (var item in items)
                    {
                        if (item == null) continue;

                        // 身份/名字冲突：先注册的提供方优先。
                        // 注意这是**本层自己的**合并规则（同名项根本不会进系统集合）；
                        // VariableRegistry 自 2026-10-29 起对名字不再"先出现者赢"——
                        // 它保留全部同名并在按名解析时报歧义，两处口径不同，别互相引用。
                        if (item.VariableId != Guid.Empty && !seenIds.Add(item.VariableId))
                            continue;
                        if (!string.IsNullOrEmpty(item.Name) && !seenNames.Add(item.Name))
                            continue;

                        merged.Add(item);
                    }
                }

                IReadOnlyList<IVariable> previous;
                lock (_gate)
                {
                    previous = _current;
                    _current = merged;
                }

                ApplyToCollection(previous, merged);
            }
        }

        /// <summary>
        /// 只做增删移。集合里不属于任何提供方的项（外部手工加入的）保留不动：
        /// 它们是别人的状态，宿主无权清理。
        /// </summary>
        private void ApplyToCollection(IReadOnlyList<IVariable> previous, IReadOnlyList<IVariable> desired)
        {
            var desiredSet = new HashSet<IVariable>(desired);
            var managedSet = new HashSet<IVariable>(previous);

            for (int i = _target.Count - 1; i >= 0; i--)
            {
                var item = _target[i];
                if (managedSet.Contains(item) && !desiredSet.Contains(item))
                    _target.RemoveAt(i);
            }

            for (int i = 0; i < desired.Count; i++)
            {
                int current = _target.IndexOf(desired[i]);
                if (current < 0)
                    _target.Insert(Math.Min(i, _target.Count), desired[i]);
                else if (current != i)
                    _target.Move(current, i);
            }
        }
    }

    /// <summary>系统变量提供方：只产出快照，不写集合。</summary>
    public interface ISystemVariableProvider
    {
        /// <summary>提供方名字（日志/诊断）</summary>
        string Name { get; }

        /// <summary>有序、不可变的变量快照</summary>
        IReadOnlyList<IVariable> Snapshot();

        /// <summary>快照发生变化（新增/删除/改名）。宿主订阅后统一刷新集合。</summary>
        event EventHandler? Changed;
    }
}
