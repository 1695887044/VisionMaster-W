using System;
using System.Windows.Media;
using VM.Start.Common.Enums;

namespace VM.Start.Models
{
    public class LogModel
    {
        /// <summary>
        /// 日志类型
        /// </summary>
        public eMsgType LogType { get; set; }
        /// <summary>
        /// 内容
        /// </summary>
        public string Content { get; set; }
        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreateTime { get; set; }
        /// <summary>
        /// 字体颜色
        /// </summary>
        public Brush LogColor { get; set; }
    }
}
