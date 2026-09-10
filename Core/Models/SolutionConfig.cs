using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VisionMaster.Models
{
    /// <summary>
    /// 解决方案级系统配置（随 .vms 持久化）：
    /// 保存解决方案的界面属性——主界面面板布局、图像视图布局等，加载方案时恢复
    /// </summary>
    public class SolutionConfig
    {
        /// <summary>
        /// 主界面面板布局（AvalonDock 序列化 XML 文本；空 = 使用默认布局）
        /// </summary>
        public string DockLayoutXml { get; set; }

        /// <summary>
        /// 图像视图宫格模式（eViewMode 枚举值）
        /// </summary>
        public int ImageViewMode { get; set; } = 0; // eViewMode.One
    }
}
