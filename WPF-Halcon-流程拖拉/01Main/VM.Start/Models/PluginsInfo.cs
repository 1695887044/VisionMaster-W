using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VM.Start.Models
{
    /// <summary>
    /// 插件信息
    /// </summary>
    [Serializable]
    public class PluginsInfo
    {
        /// <summary>
        /// 模块类型
        /// </summary>
        public Type ModuleType { get; set; }
        /// <summary>
        /// 视图类型
        /// </summary>
        public Type ModuleViewType { get; set; }
        /// <summary>
        /// 插件名称
        /// </summary>
        public string ModuleName { get; set; }
        /// <summary>
        /// 图片名称
        /// </summary>
        public string ImageName { get; set; }
        /// <summary>
        /// 插件分类
        /// </summary>
        public string Category { get; set; }
    }
}
