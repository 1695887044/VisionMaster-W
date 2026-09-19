using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VisionMaster.Models
{
    /// <summary>
    /// 端口定义
    /// 描述插件的输入/输出端口信息
    /// </summary>
    public class PortDefinition
    {
        /// <summary>
        /// 端口名称
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 端口描述
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// 数据类型名称（如 "System.Double"）
        /// </summary>
        public string DataTypeName { get; set; }

        /// <summary>
        /// 本端口若代表一个全局变量，这里是该变量的稳定身份。
        ///
        /// 为什么借用端口定义来捎带：变量绑定弹窗的候选列表本身就是 PortDefinition 列表
        /// （见 FlowQueryHelper.GetAvailableVariablesTree，全局变量分组把每个变量铺成一个端口），
        /// 绑定动作发生在端口被点中的那一刻——身份必须跟着端口一起走到 DoFinalBind，
        /// 否则那里只能拿到变量名，又退回按名寻址。
        /// 非变量来源的端口保持 Guid.Empty，消费者按 Guid.Empty 即"无身份"处理。
        /// </summary>
        public Guid VariableId { get; set; }

        /// <summary>
        /// 是否为功能性枚举端口
        /// 标记为 true 时，该端口会在变量绑定界面显示预设选项，不需要链接上游变量
        /// </summary>
        public bool IsFunctionalEnum { get; set; }

        /// <summary>
        /// 预设选项列表（当 IsFunctionalEnum 为 true 时使用）
        /// </summary>
        public List<string> PresetOptions { get; set; } = new List<string>();
    }
}
