using Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VisionMaster.Models
{
    public class TreeNodeModel
    {
        public string NodeName { get; set; } // 节点名称，如 "视觉工具" 或 "模板匹配_1"
        public string IconCode { get; set; } // 图标编码

        public ObservableCollection<TreeNodeModel> Children { get; set; }
            = new ObservableCollection<TreeNodeModel>();

        public ObservableCollection<IOutputPort> AvailablePorts { get; set; }
            = new ObservableCollection<IOutputPort>();
    }
}
