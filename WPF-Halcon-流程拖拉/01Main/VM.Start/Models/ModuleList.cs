using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;

namespace VM.Start.Models
{
    [Serializable]
    public class ModuleList
    {
        /// <summary>
        /// 编号
        /// </summary>
        public int ModuleNo { get; set; }
        /// <summary>
        /// 显示的名称
        /// </summary>
        public string DisplayName { get; set; }
        /// <summary>
        /// 备注
        /// </summary>
        public string Remarks { get; set; }
        /// <summary>
        /// 图标
        /// </summary>
        [field:NonSerialized]
        public ImageSource IconImage { get; set; }
        public List<VarModel> VarModels { get; set; } = new List<VarModel>();

    }
}
