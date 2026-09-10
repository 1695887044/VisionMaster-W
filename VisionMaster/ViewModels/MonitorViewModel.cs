using Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using UI.CustomControl;
using UI.Helper;
using VisionMaster.Helpers;
using VisionMaster.Models;
using VisionMaster.Services;

namespace VisionMaster.ViewModels
{
    /// <summary>
    /// 搜索联想条目
    /// </summary>
    public class SearchSuggestion
    {
        public string DisplayText { get; set; }
        public Guid StepId { get; set; }
        public string StepName { get; set; }
        public string PortName { get; set; }
        public bool IsInput { get; set; }
        public bool IsGlobalVariable { get; set; }
        public string VariableName { get; set; }
    }

    /// <summary>
    /// 监控栏视图模型（整合原"数据栏 + 模块输出"）：
    /// 单一数据源 = 方案 WatchItems，按需添加，按方向分三组展示（全局变量/算子输入/算子输出），
    /// 每项经 WatchPortWrapper 订阅 ValueChanged 实现实时刷新
    /// </summary>
    public class MonitorViewModel : BindableBase
    {
        private readonly IWorkspaceManager _workspace;
        private readonly IRuntimeManager _runtimeManager;

        public ObservableCollection<WatchPortWrapper> DisplayPorts { get; } = new();
        public ObservableCollection<SearchSuggestion> SuggestedItems { get; } = new();

        /// <summary>
        /// 按 Direction 分组的展示视图（全局变量 → 输入 → 输出），供折叠分组 UI 绑定
        /// </summary>
        public ICollectionView GroupedPorts { get; }

        private string _searchText;
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    UpdateSuggestions();
                    IsDropDownOpen = SuggestedItems.Any();
                }
            }
        }

        private bool _isDropDownOpen;
        public bool IsDropDownOpen
        {
            get => _isDropDownOpen;
            set => SetProperty(ref _isDropDownOpen, value);
        }

        private SearchSuggestion _selectedSuggestion;
        public SearchSuggestion SelectedSuggestion
        {
            get => _selectedSuggestion;
            set
            {
                if (SetProperty(ref _selectedSuggestion, value) && value != null)
                {
                    AddWatchItem(value);
                    SearchText = string.Empty;
                    IsDropDownOpen = false;
                }
            }
        }

        public DelegateCommand<WatchPortWrapper> RemoveCommand { get; }
        public DelegateCommand ClearAllCommand { get; }
        public DelegateCommand<string> QuickAddCommand { get; }
        public DelegateCommand AddWatchCommand { get; }

        public MonitorViewModel(IWorkspaceManager workspace, IRuntimeManager runtimeManager)
        {
            _workspace = workspace;
            _runtimeManager = runtimeManager;
            RemoveCommand = new DelegateCommand<WatchPortWrapper>(RemoveWatchItem);
            ClearAllCommand = new DelegateCommand(ClearAllWatchItems);
            QuickAddCommand = new DelegateCommand<string>(QuickAddWatchItem);
            AddWatchCommand = new DelegateCommand(ExecuteAddWatch);

            // 按 Direction 分组（WatchPortWrapper.Direction = 全局/输入/输出），组序固定
            GroupedPorts = CollectionViewSource.GetDefaultView(DisplayPorts);
            GroupedPorts.GroupDescriptions.Add(new PropertyGroupDescription(nameof(WatchPortWrapper.Direction))
            {
                Converter = new DirectionToGroupConverter()
            });

            // 全局变量集合变化（增删变量）→ 重查监视项有效性
            _workspace.GlobalVariables.CollectionChanged += OnGlobalVariablesChanged;

            // 方案切换（启动自动加载/打开方案）→ 重挂监视集合订阅并刷新。
            // 关键1：订阅必须跟随 CurrentSolution 实例，否则方案加载后添加监视项写进新集合，
            //       事件挂在旧集合上无人响应（表现为"添加模块输出没反应"）
            // 关键2：SwitchSolution 可能在后台线程触发（await 续体），
            //       RefreshDisplayPorts 操作 ObservableCollection 必须切回 UI 线程
            if (_workspace is INotifyPropertyChanged workspaceInpc)
                workspaceInpc.PropertyChanged += OnWorkspacePropertyChanged;

            // 视图可能晚于方案加载才创建，构造时先同步一次当前方案
            SyncSolutionSubscriptions();

            // 会话注册/注销（编译完成、方案加载清空、删除流程）时重查监视项有效性：
            // 覆盖"启动加载方案后"与"用户修改流程后"两个检查时机
            _runtimeManager.ActiveSessions.CollectionChanged += OnSessionsChanged;
            _recheckTimer.Tick += (_, _) =>
            {
                _recheckTimer.Stop();
                RevalidateAll();
            };
        }

        /// <summary>当前已订阅 CollectionChanged 的监视集合（防重复订阅/悬挂订阅）</summary>
        private ObservableCollection<WatchItemModel> _subscribedWatchItems;

        /// <summary>
        /// 方案切换：重挂 WatchItems 订阅并全量刷新。
        /// 处理器在 SwitchSolution 内部被同步回调（SwitchFlow/SwitchStep 尚未完成，
        /// 绑定树处于中间状态），必须无条件延迟到 Dispatcher 空闲后再操作集合，避免重入
        /// </summary>
        private void OnWorkspacePropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(WorkspaceContext.CurrentSolution)) return;

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null) return;
            dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    SyncSolutionSubscriptions();
                    RefreshDisplayPorts();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Monitor] 方案切换刷新失败: {ex.Message}");
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        /// <summary>
        /// 对齐订阅到当前方案的 WatchItems（空方案补空集合）
        /// </summary>
        private void SyncSolutionSubscriptions()
        {
            var solution = _workspace.CurrentSolution;
            if (solution == null)
            {
                if (_subscribedWatchItems != null)
                {
                    _subscribedWatchItems.CollectionChanged -= OnWatchItemsChanged;
                    _subscribedWatchItems = null;
                }
                return;
            }

            if (solution.WatchItems == null)
                solution.WatchItems = new ObservableCollection<WatchItemModel>();

            if (ReferenceEquals(_subscribedWatchItems, solution.WatchItems)) return;

            if (_subscribedWatchItems != null)
                _subscribedWatchItems.CollectionChanged -= OnWatchItemsChanged;
            _subscribedWatchItems = solution.WatchItems;
            _subscribedWatchItems.CollectionChanged += OnWatchItemsChanged;
        }

        /// <summary>
        /// 会话集合变化（编译/方案切换）：300ms 防抖后重查——
        /// 编译是逐流程注册会话，等注册稳定后一次查完，避免连续 N 次全量刷新。
        /// 会话注册可能在后台线程，计时器启动须切回 UI 线程
        /// </summary>
        private void OnSessionsChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(() => { _recheckTimer.Stop(); _recheckTimer.Start(); });
                return;
            }
            _recheckTimer.Stop();
            _recheckTimer.Start();
        }

        /// <summary>监视项有效性重查防抖计时器</summary>
        private readonly System.Windows.Threading.DispatcherTimer _recheckTimer =
            new() { Interval = TimeSpan.FromMilliseconds(300) };

        private void ExecuteAddWatch()
        {
            if (string.IsNullOrEmpty(SearchText)) return;

            var trimmedText = SearchText.Trim();

            if (SuggestedItems.Any())
            {
                SelectedSuggestion = SuggestedItems.First();
            }
            else if (trimmedText.StartsWith("Global."))
            {
                var varName = trimmedText.Substring(7);
                QuickAddWatchItem("Global." + varName);
            }
            else if (trimmedText.Contains("."))
            {
                QuickAddWatchItem(trimmedText);
            }
            else
            {
                var allPlugins = GetAllCompiledPlugins().ToList();
                var kvp = allPlugins.FirstOrDefault(p => p.Value.InstanceName.Equals(trimmedText, StringComparison.OrdinalIgnoreCase));
                if (kvp.Value != null)
                {
                    var newItem = new WatchItemModel
                    {
                        ItemType = WatchItemType.PluginAll,
                        StepId = kvp.Key,
                        StepName = kvp.Value.InstanceName
                    };
                    _workspace.CurrentSolution.WatchItems.Add(newItem);
                }
                else
                {
                    // 无匹配：明确反馈，避免"点了没反应"
                    Notifier.ShowWarning($"未找到算子 [{trimmedText}]，请先编译流程后再添加监视");
                }
            }
        }

        private IEnumerable<KeyValuePair<Guid, IVisionPlugin>> GetAllCompiledPlugins()
        {
            if (_runtimeManager.ActiveSessions == null) yield break;

            foreach (var session in _runtimeManager.ActiveSessions)
            {
                if (session.ExecutionEngine is CompiledFlow compiledFlow && compiledFlow.PluginLookup != null)
                {
                    foreach (var kvp in compiledFlow.PluginLookup)
                    {
                        yield return kvp;
                    }
                }
            }
        }

        private void UpdateSuggestions()
        {
            SuggestedItems.Clear();
            string query = SearchText?.Trim() ?? "";
            if (string.IsNullOrEmpty(query)) return;

            var allPlugins = GetAllCompiledPlugins().ToList();

            if (query.Contains("."))
            {
                var parts = query.Split('.');
                string stepName = parts[0];
                string portQuery = parts.Length > 1 ? parts[1] : "";

                var kvp = allPlugins.FirstOrDefault(p => p.Value.InstanceName.Equals(stepName, StringComparison.OrdinalIgnoreCase));
                if (kvp.Value != null)
                {
                    if (kvp.Value.Inputs != null)
                    {
                        foreach (var pName in kvp.Value.Inputs.Keys.Where(k => k.Contains(portQuery, StringComparison.OrdinalIgnoreCase)))
                            SuggestedItems.Add(new SearchSuggestion { DisplayText = $"{stepName}.{pName} [输入]", StepId = kvp.Key, StepName = stepName, PortName = pName, IsInput = true });
                    }
                    if (kvp.Value.Outputs != null)
                    {
                        foreach (var pName in kvp.Value.Outputs.Keys.Where(k => k.Contains(portQuery, StringComparison.OrdinalIgnoreCase)))
                            SuggestedItems.Add(new SearchSuggestion { DisplayText = $"{stepName}.{pName} [输出]", StepId = kvp.Key, StepName = stepName, PortName = pName, IsInput = false });
                    }
                }

                // 全局变量联想：Global.变量名
                var varMatches = _workspace.GlobalVariables.Where(gv => gv.Name.Contains(portQuery, StringComparison.OrdinalIgnoreCase));
                foreach (var gv in varMatches)
                    SuggestedItems.Add(new SearchSuggestion { DisplayText = $"Global.{gv.Name} [全局变量]", IsGlobalVariable = true, VariableName = gv.Name });
            }
            else
            {
                var matches = allPlugins.Where(p => p.Value.InstanceName.Contains(query, StringComparison.OrdinalIgnoreCase));
                foreach (var m in matches)
                {
                    SuggestedItems.Add(new SearchSuggestion { DisplayText = $"{m.Value.InstanceName} [监控全部引脚]", StepId = m.Key, StepName = m.Value.InstanceName, PortName = null });
                }

                var globalVars = _workspace.GlobalVariables.Where(gv => gv.Name.Contains(query, StringComparison.OrdinalIgnoreCase));
                foreach (var gv in globalVars)
                {
                    SuggestedItems.Add(new SearchSuggestion { DisplayText = $"Global.{gv.Name} [全局变量]", IsGlobalVariable = true, VariableName = gv.Name });
                }
            }
        }

        private void AddWatchItem(SearchSuggestion suggestion)
        {
            if (suggestion.IsGlobalVariable)
            {
                var newItem = new WatchItemModel
                {
                    ItemType = WatchItemType.GlobalVariable,
                    GlobalVariableName = suggestion.VariableName
                };
                _workspace.CurrentSolution.WatchItems.Add(newItem);
            }
            else
            {
                var newItem = new WatchItemModel
                {
                    ItemType = string.IsNullOrEmpty(suggestion.PortName) ? WatchItemType.PluginAll : WatchItemType.PluginPort,
                    StepId = suggestion.StepId,
                    StepName = suggestion.StepName,
                    PortName = suggestion.PortName,
                    IsInput = suggestion.IsInput
                };
                _workspace.CurrentSolution.WatchItems.Add(newItem);
            }
        }

        private void QuickAddWatchItem(string portName)
        {
            if (portName == "Global.All")
            {
                foreach (var gv in _workspace.GlobalVariables)
                {
                    if (!_workspace.CurrentSolution.WatchItems.Any(w => w.ItemType == WatchItemType.GlobalVariable && w.GlobalVariableName == gv.Name))
                    {
                        var newItem = new WatchItemModel
                        {
                            ItemType = WatchItemType.GlobalVariable,
                            GlobalVariableName = gv.Name
                        };
                        _workspace.CurrentSolution.WatchItems.Add(newItem);
                    }
                }
            }
            else if (portName.StartsWith("Global."))
            {
                var varName = portName.Substring(7);
                if (!_workspace.CurrentSolution.WatchItems.Any(w => w.ItemType == WatchItemType.GlobalVariable && w.GlobalVariableName == varName))
                {
                    var newItem = new WatchItemModel
                    {
                        ItemType = WatchItemType.GlobalVariable,
                        GlobalVariableName = varName
                    };
                    _workspace.CurrentSolution.WatchItems.Add(newItem);
                }
            }
            else if (portName.Contains("."))
            {
                var parts = portName.Split('.');
                var allPlugins = GetAllCompiledPlugins().ToList();
                var kvp = allPlugins.FirstOrDefault(p => p.Value.InstanceName.Equals(parts[0], StringComparison.OrdinalIgnoreCase));
                if (kvp.Value != null && parts.Length > 1)
                {
                    bool isInput = kvp.Value.Inputs?.ContainsKey(parts[1]) ?? false;
                    bool exists = _workspace.CurrentSolution.WatchItems.Any(w =>
                        w.ItemType == WatchItemType.PluginPort &&
                        w.StepId == kvp.Key &&
                        w.PortName == parts[1]);

                    if (!exists)
                    {
                        var newItem = new WatchItemModel
                        {
                            ItemType = WatchItemType.PluginPort,
                            StepId = kvp.Key,
                            StepName = parts[0],
                            PortName = parts[1],
                            IsInput = isInput
                        };
                        _workspace.CurrentSolution.WatchItems.Add(newItem);
                    }
                }
            }
        }

        private void RemoveWatchItem(WatchPortWrapper wrapper)
        {
            if (wrapper != null)
                _workspace.CurrentSolution.WatchItems.Remove(wrapper.OriginalConfig);
        }

        private void ClearAllWatchItems()
        {
            foreach (var wrapper in DisplayPorts.ToList())
            {
                wrapper.Dispose();
                DisplayPorts.Remove(wrapper);
            }
            _workspace.CurrentSolution.WatchItems.Clear();
        }

        private void OnWatchItemsChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (WatchItemModel oldItem in e.OldItems)
                {
                    var toRemove = DisplayPorts.Where(p => p.OriginalConfig == oldItem).ToList();
                    foreach (var wrapper in toRemove)
                    {
                        wrapper.Dispose();
                        DisplayPorts.Remove(wrapper);
                    }
                }
            }
            if (e.NewItems != null)
            {
                foreach (WatchItemModel newItem in e.NewItems)
                {
                    BuildWrappersForItem(newItem);
                }
            }
        }

        private void OnGlobalVariablesChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            RefreshDisplayPorts();
        }

        /// <summary>
        /// 有效性重查（编译完成/方案加载后触发）：全量重建监视项，
        /// 已恢复的目标解除失效状态，仍不存在的保持变色提示
        /// </summary>
        private void RevalidateAll() => RefreshDisplayPorts();

        private void RefreshDisplayPorts()
        {
            foreach (var wrapper in DisplayPorts) wrapper.Dispose();
            DisplayPorts.Clear();

            if (_workspace.CurrentSolution?.WatchItems == null) return;
            foreach (var item in _workspace.CurrentSolution.WatchItems) BuildWrappersForItem(item);
        }

        private void BuildWrappersForItem(WatchItemModel config)
        {
            if (config.ItemType == WatchItemType.GlobalVariable)
            {
                var globalVar = _workspace.GlobalVariables.FirstOrDefault(gv => gv.Name == config.GlobalVariableName);
                if (globalVar != null)
                {
                    DisplayPorts.Add(new WatchPortWrapper(config, globalVar));
                }
                else
                {
                    // 变量不存在：保留条目并标记失效（变色提示），等用户清理或变量重新创建
                    DisplayPorts.Add(new WatchPortWrapper(
                        config, config.GlobalVariableName ?? "(未知变量)", "全局", null));
                }
            }
            else
            {
                var pluginKvp = GetAllCompiledPlugins().FirstOrDefault(p => p.Key == config.StepId);
                if (pluginKvp.Value == null)
                {
                    // 算子不存在或方案刚加载还未编译：标记失效，编译完成后 RevalidateAll 自动恢复
                    var name = string.IsNullOrEmpty(config.PortName)
                        ? config.StepName
                        : $"{config.StepName}.{config.PortName}";
                    DisplayPorts.Add(new WatchPortWrapper(
                        config, name, config.IsInput ? "输入" : "输出", null));
                    return;
                }

                var plugin = pluginKvp.Value;

                if (config.ItemType == WatchItemType.PluginAll || string.IsNullOrEmpty(config.PortName))
                {
                    if (plugin.Inputs != null)
                        foreach (var input in plugin.Inputs)
                            DisplayPorts.Add(new WatchPortWrapper(config, input.Key, true, input.Value));

                    if (plugin.Outputs != null)
                        foreach (var output in plugin.Outputs)
                            DisplayPorts.Add(new WatchPortWrapper(config, output.Key, false, output.Value));
                }
                else
                {
                    if (config.IsInput && plugin.Inputs != null && plugin.Inputs.TryGetValue(config.PortName, out var ip))
                        DisplayPorts.Add(new WatchPortWrapper(config, config.PortName, true, ip));
                    else if (!config.IsInput && plugin.Outputs != null && plugin.Outputs.TryGetValue(config.PortName, out var op))
                        DisplayPorts.Add(new WatchPortWrapper(config, config.PortName, false, op));
                    else
                    {
                        // 算子存在但端口不存在（如插件版本变化后端口被移除）
                        DisplayPorts.Add(new WatchPortWrapper(
                            config, $"{config.StepName}.{config.PortName}", config.IsInput ? "输入" : "输出", null));
                    }
                }
            }
        }
    }

    /// <summary>
    /// Direction → 分组顺序转换：全局变量(0) → 输入(1) → 输出(2)，保证组排序稳定
    /// </summary>
    public class DirectionToGroupConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => value switch
            {
                "全局" => 0,
                "输入" => 1,
                "输出" => 2,
                _ => 3
            };

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// 分组键(排序序号) → 分组标题：分组头部显示用
    /// </summary>
    public class GroupKeyToLabelConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => value switch
            {
                0 => "全局变量",
                1 => "算子输入",
                2 => "算子输出",
                _ => "其他"
            };

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => throw new NotSupportedException();
    }
}
