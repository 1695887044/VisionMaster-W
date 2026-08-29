using Prism.Mvvm;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VisionMaster.Models;
using VisionMaster.Services;

namespace VisionMaster.ViewModels
{
    /// <summary>
    /// 工具箱 ViewModel：纯插件工具分组展示 + 关键字过滤
    /// </summary>
    public class ToolViewModel : BindableBase
    {
        private readonly IPluginProvider pluginProvider;

        private List<ToolGroupModel> toolBarSource = new();

        /// <summary>
        /// 过滤后的插件分组（SearchText 变化时重建）
        /// </summary>
        public ObservableCollection<ToolGroupModel> FilteredToolBarSource { get; } = new();

        private string searchText;
        public string SearchText
        {
            get { return searchText; }
            set
            {
                if (SetProperty(ref searchText, value))
                {
                    ApplyFilter();
                }
            }
        }

        public ToolViewModel(IPluginProvider pluginProvider)
        {
            this.pluginProvider = pluginProvider;
            loadTools();
        }

        /// <summary>
        /// 按关键字过滤：匹配插件名/描述/分组名，无匹配项的分组隐藏
        /// </summary>
        private void ApplyFilter()
        {
            FilteredToolBarSource.Clear();

            var keyword = searchText?.Trim();
            if (string.IsNullOrEmpty(keyword))
            {
                foreach (var group in toolBarSource)
                {
                    FilteredToolBarSource.Add(group);
                }
                return;
            }

            foreach (var group in toolBarSource)
            {
                var matched = group.Children
                    .Where(t => Contains(t.Name, keyword) || Contains(t.Description, keyword))
                    .ToList();
                if (matched.Count == 0) continue;

                var copy = new ToolGroupModel() { Name = group.Name };
                foreach (var tool in matched)
                {
                    copy.Children.Add(tool);
                }
                FilteredToolBarSource.Add(copy);
            }
        }

        private static bool Contains(string source, string keyword)
        {
            return source?.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        void loadTools()
        {
            pluginProvider.ModulePlugins.GroupBy(t => t.Value.Category).ToList().ForEach(g =>
            {
                ToolGroupModel toolGroup = new ToolGroupModel() { Name = g.Key };
                g.ToList().ForEach(p =>
                {
                    toolGroup.Children.Add(p.Value);
                });
                toolBarSource.Add(toolGroup);
            });
            ApplyFilter();
        }
    }
}
