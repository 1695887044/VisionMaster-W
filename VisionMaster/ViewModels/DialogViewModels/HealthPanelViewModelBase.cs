using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Prism.Mvvm;
using VisionMaster.Services.Health;

namespace VisionMaster.ViewModels.DialogViewModels
{
    /// <summary>
    /// 健康面板契约：壳只认这五个成员，不认具体域。
    /// <para>放在宿主视图模型层而不是 <c>Core.Interfaces</c>：四个数据源全是宿主自有，
    /// 放进公共契约只会扩大 31 个插件的引用面（评审结论）。</para>
    /// </summary>
    public interface IHealthPanel
    {
        string Domain { get; }
        string Title { get; }
        HealthLevel Level { get; }
        string LevelKey { get; }
        string Summary { get; }
        string Detail { get; }
        int IssueCount { get; }
        bool HasData { get; }

        /// <summary>壳置位：false→true 时面板自行刷新一次（切页签懒刷新）。</summary>
        bool IsActive { get; set; }

        /// <summary>常规刷新；已经采集失败的面板会跳过，等 <see cref="ForceRefresh"/>。</summary>
        void Refresh();

        /// <summary>手动刷新：清掉失败状态后重试。</summary>
        void ForceRefresh();

        IReadOnlyList<HealthIssue> CollectIssues();
    }

    /// <summary>
    /// 健康面板基类：把"异常隔离 + 等级聚合 + 问题行"固定下来，子类只写 <see cref="RefreshCore"/>。
    ///
    /// <para><b>异常隔离为什么必须在基类</b>：一个相机驱动抛异常不能让通讯/运动/IO 三个页签
    /// 一起停表——那是"一个坏设备冻住整页诊断"。基类把异常变成该面板的一条 Error 问题行，
    /// 其余面板照常刷新。</para>
    /// </summary>
    public abstract class HealthPanelViewModelBase : BindableBase, IHealthPanel
    {
        public abstract string Domain { get; }
        public abstract string Title { get; }

        private HealthLevel _level = HealthLevel.Idle;
        public HealthLevel Level
        {
            get => _level;
            protected set
            {
                if (SetProperty(ref _level, value))
                    RaisePropertyChanged(nameof(LevelKey));
            }
        }

        public string LevelKey => HealthLevels.Key(Level);

        private string _summary = string.Empty;
        public string Summary
        {
            get => _summary;
            protected set => SetProperty(ref _summary, value);
        }

        private string _detail = string.Empty;
        public string Detail
        {
            get => _detail;
            protected set => SetProperty(ref _detail, value);
        }

        private int _issueCount;
        public int IssueCount
        {
            get => _issueCount;
            protected set
            {
                if (SetProperty(ref _issueCount, value))
                    RaisePropertyChanged(nameof(HasIssues));
            }
        }

        public bool HasIssues => IssueCount > 0;

        private bool _hasData;
        public bool HasData
        {
            get => _hasData;
            protected set => SetProperty(ref _hasData, value);
        }

        private bool _isActive;
        public bool IsActive
        {
            get => _isActive;
            set
            {
                bool was = _isActive;
                if (!SetProperty(ref _isActive, value)) return;
                if (value && !was) Refresh();
            }
        }

        private string? _fault;
        public bool HasFault => _fault != null;

        public void Refresh()
        {
            if (_fault != null) return;   // 已失败：等手动刷新，避免每秒重复撞同一个异常
            RefreshCoreSafe();
        }

        public void ForceRefresh()
        {
            _fault = null;
            RefreshCoreSafe();
        }

        private void RefreshCoreSafe()
        {
            try
            {
                RefreshCore();
            }
            catch (Exception ex)
            {
                _fault = ex.Message;
                Level = HealthLevel.Error;
                Summary = "采集失败";
                Detail = $"读取失败：{ex.Message}";
                IssueCount = 1;
            }
        }

        /// <summary>子类实现：只读采集 + 更新集合/摘要。异常交给基类兜。</summary>
        protected abstract void RefreshCore();

        public IReadOnlyList<HealthIssue> CollectIssues()
        {
            if (_fault != null)
            {
                return new[]
                {
                    new HealthIssue(Domain, Title, HealthLevel.Error, $"读取失败：{_fault}")
                };
            }

            return CollectIssuesCore();
        }

        protected abstract IReadOnlyList<HealthIssue> CollectIssuesCore();

        /// <summary>
        /// 按稳定键原位对齐：键相同则替换该位置的行，键消失则移除，键新增则插入。
        ///
        /// <para>不能每秒 <c>Clear()+Add()</c>：那会把滚动位置每秒重置一次；
        /// 也不能"只更新对象"——行是不可变 record，替换比就地改更不容易漏通知。</para>
        /// </summary>
        protected static void SyncRows<TRow>(
            ObservableCollection<TRow> target,
            IReadOnlyList<TRow> source,
            Func<TRow, string> keyOf)
        {
            var index = new Dictionary<string, TRow>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in target)
            {
                var key = keyOf(row);
                if (key != null) index[key] = row;
            }

            var wanted = new HashSet<string>(source.Select(keyOf), StringComparer.OrdinalIgnoreCase);

            for (int i = target.Count - 1; i >= 0; i--)
            {
                if (!wanted.Contains(keyOf(target[i])))
                    target.RemoveAt(i);
            }

            for (int i = 0; i < source.Count; i++)
            {
                var key = keyOf(source[i]);
                if (index.ContainsKey(key))
                {
                    // 原位替换：保持滚动位置；行对象换了，选中态会丢——只读表格可接受。
                    if (i < target.Count) target[i] = source[i];
                    else target.Add(source[i]);
                }
                else
                {
                    target.Insert(Math.Min(i, target.Count), source[i]);
                }
            }
        }

        /// <summary>由问题行反推域等级与计数（子类在 RefreshCore 末尾调用）。</summary>
        protected void ApplyIssues(IReadOnlyList<HealthIssue> issues, bool hasData)
        {
            HasData = hasData;
            IssueCount = issues.Count(i => i.Level is HealthLevel.Error or HealthLevel.Warn);
            Level = HealthLevels.Aggregate(issues.Select(i => i.Level));
            if (Level == HealthLevel.Idle && hasData) Level = HealthLevel.Ok;
        }
    }
}
