using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Core.Interfaces;   // StepConfigOptionKind：端口候选类别（见 IPort.OptionKind）

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
        /// 本端口若代表一个**网络变量**，这里是它所属设备的连接名；本地/运行时变量为空串。
        ///
        /// 为什么候选行必须带设备：多设备允许同名点位（同一设备内唯一），绑定后的显示串若只有
        /// 「节点名.变量名」，两台设备的 <c>A</c> 会拼出**同一个地址**——用户看不出自己绑的是哪一台，
        /// 「已绑定」标记也会同时亮在两行上（旧实现按名字串比较，天然分不出）。
        /// </summary>
        public string Device { get; set; } = string.Empty;

        /// <summary>
        /// 是否为功能性枚举端口
        /// 标记为 true 时，该端口会在变量绑定界面显示预设选项，不需要链接上游变量
        /// </summary>
        public bool IsFunctionalEnum { get; set; }

        /// <summary>
        /// 候选项类别：非空时绑定界面会**现取**候选（方案里的卡/轴可能随时在变），
        /// 而不是用下面那份 <see cref="PresetOptions"/> 快照。
        /// </summary>
        public StepConfigOptionKind? OptionKind { get; set; }

        /// <summary>
        /// 预设选项列表（当 IsFunctionalEnum 为 true 时使用）
        /// </summary>
        public List<string> PresetOptions { get; set; } = new List<string>();

        // ── 以下两项是「变量绑定弹窗」的候选列表专用展示字段 ──

        /// <summary>
        /// 当前选中的输入端口能不能接住这个输出端口。
        /// 绑定弹窗按类型过滤后置 false（列表里仍然看得见，但置灰、双击不生效）。
        /// 默认 true —— 未被弹窗评估过的端口（比如插件自己的 InputDefinitions）一律按可绑定处理。
        /// </summary>
        public bool IsBindable { get; set; } = true;

        /// <summary>
        /// 不可绑定时的原因（如"HRegion 无法赋给 HImage 端口"），用作置灰项的 ToolTip。
        /// 可绑定时为空串（不弹提示）。
        /// </summary>
        public string IncompatibleReason { get; set; } = string.Empty;
    }
}
