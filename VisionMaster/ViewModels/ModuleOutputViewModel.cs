﻿﻿﻿﻿﻿using Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using UI.Helper;
using VisionMaster.Helpers;
using VisionMaster.Models;
using VisionMaster.Services;

namespace VisionMaster.ViewModels
{
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

    public class ModuleOutputViewModel : BindableBase
    {
        private readonly IWorkspaceManager _workspace;
        private readonly IRuntimeManager _runtimeManager;

        public ObservableCollection<WatchPortWrapper> DisplayPorts { get; } = new();
        public ObservableCollection<SearchSuggestion> SuggestedItems { get; } = new();

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

        public ModuleOutputViewModel(IWorkspaceManager workspace, IRuntimeManager runtimeManager)
        {
            _workspace = workspace;
            _runtimeManager = runtimeManager;
            RemoveCommand = new DelegateCommand<WatchPortWrapper>(RemoveWatchItem);
            ClearAllCommand = new DelegateCommand(ClearAllWatchItems);
            QuickAddCommand = new DelegateCommand<string>(QuickAddWatchItem);
            AddWatchCommand = new DelegateCommand(ExecuteAddWatch);

            if (_workspace.CurrentSolution != null)
            {
                if (_workspace.CurrentSolution.WatchItems == null)
                    _workspace.CurrentSolution.WatchItems = new ObservableCollection<WatchItemModel>();

                _workspace.CurrentSolution.WatchItems.CollectionChanged += OnWatchItemsChanged;
                _workspace.GlobalVariables.CollectionChanged += OnGlobalVariablesChanged;
                RefreshDisplayPorts();
            }
        }

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
            }
            else
            {
                var pluginKvp = GetAllCompiledPlugins().FirstOrDefault(p => p.Key == config.StepId);
                if (pluginKvp.Value == null) return;

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
                }
            }
        }
    }
}
