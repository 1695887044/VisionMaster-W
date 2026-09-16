using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;

namespace VM.Start.Models
{
    /// <summary>
    /// 工具 树形类
    /// </summary>
    public class ModuleToolNode
    {
        public ModuleToolNode()
        {
            Children = new List<ModuleToolNode>();
        }
        public bool IsCategory { get; set; } = false;
        public ImageSource IconImage { get; set; }
        private string m_Name;
        public string Name
        {
            get { return m_Name?.Trim(); }
            set { m_Name = value; }
        }
        public int SortNO { get; set; }//排序使用
        public List<ModuleToolNode> Children { get; set; }
    }
}
