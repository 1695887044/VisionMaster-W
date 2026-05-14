using Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using UI.Helper;
using VisionMaster.Helpers;
using VisionMaster.Models;
using VisionMaster.Services;

namespace VisionMaster.ViewModels
{
    // 用于下拉框提示的临时模型
    public class SearchSuggestion
    {
        public string DisplayText { get; set; } // UI 显示文字 (如: Match_1 [全部引脚])
        public Guid StepId { get; set; }
        public string StepName { get; set; }
        public string PortName { get; set; }
        public bool IsInput { get; set; }
    }

    public class ModuleOutputViewModel : BindableBase
    {
        private readonly IWorkspaceManager _workspace;
        private readonly IRuntimeManager _runtimeManager; // 🌟 直接注入 RuntimeManager 来获取所有的 ActiveSessions

        public ObservableCollection<WatchPortWrapper> DisplayPorts { get; } = new();
        public ObservableCollection<SearchSuggestion> SuggestedItems { get; } = new();

        public string SearchText
        {
            get => field;
            set
            {
                if (SetProperty(ref field, value)) UpdateSuggestions();
            }
        }

        public SearchSuggestion SelectedSuggestion
        {
            get => field;
            set
            {
                if (SetProperty(ref field, value) && value != null)
                {
                    AddWatchItem(value);
                    SearchText = string.Empty;
                }
            }
        }

        public DelegateCommand<WatchPortWrapper> RemoveCommand { get; }

        public ModuleOutputViewModel(IWorkspaceManager workspace, IRuntimeManager runtimeManager)
        {
            _workspace = workspace;
            _runtimeManager = runtimeManager;
            RemoveCommand = new DelegateCommand<WatchPortWrapper>(RemoveWatchItem);

            if (_workspace.CurrentSolution != null)
            {
                // 如果反序列化时 WatchItems 为空，给个默认实例
                if (_workspace.CurrentSolution.WatchItems == null)
                    _workspace.CurrentSolution.WatchItems = new ObservableCollection<WatchItemModel>();

                _workspace.CurrentSolution.WatchItems.CollectionChanged += OnWatchItemsChanged;
                RefreshDisplayPorts();
            }
        }

        // 🌟 核心突破：从你的多流程并发架构中，提取出所有已编译的算子
        private IEnumerable<KeyValuePair<Guid, IVisionPlugin>> GetAllCompiledPlugins()
        {
            if (_runtimeManager.ActiveSessions == null) yield break;

            foreach (var session in _runtimeManager.ActiveSessions)
            {
                // 你的 FlowEngineService.RunSessionAsync 里执行的是 session.ExecutionEngine.Run()
                // 说明 ExecutionEngine 的真实类型就是 CompiledFlow！
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

            // 获取全部流程的算子字典
            var allPlugins = GetAllCompiledPlugins().ToList();
            if (!allPlugins.Any()) return;

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
            }
            else
            {
                var matches = allPlugins.Where(p => p.Value.InstanceName.Contains(query, StringComparison.OrdinalIgnoreCase));
                foreach (var m in matches)
                {
                    SuggestedItems.Add(new SearchSuggestion { DisplayText = $"{m.Value.InstanceName} [监控全部引脚]", StepId = m.Key, StepName = m.Value.InstanceName, PortName = null });
                }
            }
        }

        private void AddWatchItem(SearchSuggestion suggestion)
        {
            var newItem = new WatchItemModel
            {
                StepId = suggestion.StepId,
                StepName = suggestion.StepName,
                PortName = suggestion.PortName,
                IsInput = suggestion.IsInput
            };
            _workspace.CurrentSolution.WatchItems.Add(newItem);
        }

        private void RemoveWatchItem(WatchPortWrapper wrapper)
        {
            if (wrapper != null)
                _workspace.CurrentSolution.WatchItems.Remove(wrapper.OriginalConfig);
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
                        wrapper.Dispose(); // 卸载 ValueChanged，极其关键
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

        private void RefreshDisplayPorts()
        {
            foreach (var wrapper in DisplayPorts) wrapper.Dispose();
            DisplayPorts.Clear();

            if (_workspace.CurrentSolution?.WatchItems == null) return;
            foreach (var item in _workspace.CurrentSolution.WatchItems) BuildWrappersForItem(item);
        }

        private void BuildWrappersForItem(WatchItemModel config)
        {
            var pluginKvp = GetAllCompiledPlugins().FirstOrDefault(p => p.Key == config.StepId);
            if (pluginKvp.Value == null) return; // 算子不存在（可能图纸被删了，或者未编译）

            var plugin = pluginKvp.Value;

            if (string.IsNullOrEmpty(config.PortName))
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
            }
        }
    }
}
