using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VM.Start.Models
{
    /// <summary>
    /// UI设计
    /// </summary>
    [Serializable]
    public class UIElementInfo
    {
        /// <summary>
        /// 显示名称
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 控件类型 例如BUTTON
        /// </summary>
        public string Type { get; set; }

        // 添加其他需要保存的属性
    }
}
