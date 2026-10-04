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
        /// 画布布局（eViewMode 枚举值：0~8 = 单画面~九宫格）。缺省单画面。
        /// 落在枚举定义之外的值（老方案存过已下线的 29）在恢复时回落单画面，
        /// 见 <c>SolutionConfigApplier.Restore</c>。
        /// </summary>
        public int ImageViewMode { get; set; } = (int)eViewMode.One;
    }
}
