using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VisionMaster.Models
{
    public record  class ToolItemModel
    {

        public string Icon { get; init; }

        public string Category { get; init; }

        public string Name { get; init; }

        public string Description { get; init; }

        public bool IsContainer { get; init; } = false;

        public string ModuleGroup { get; init; } = "Tool";

        public string ModuleTypeName {  get; init; }
    }
    public class ToolGroupModel
    {
        public string Name { get; set; }
        // 这一组下面的所有小工具
        public ObservableCollection<ToolItemModel> Children { get; set; } = new();
    }
}
