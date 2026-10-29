using System;

namespace Plugin.Util.Models
{
    /// <summary>
    /// 变量名下拉的一行候选。
    /// 全局作用域只允许从变量管理里选；当前值失效时补一条 <see cref="IsInvalid"/> 的只读项，
    /// 显示「原名（已失效）」，不清空、不自动替换。
    /// </summary>
    public sealed class NameCandidate
    {
        public NameCandidate(
            string name,
            string typeName,
            string note,
            bool isInvalid = false,
            Guid variableId = default,
            string device = "")
        {
            Name = name ?? string.Empty;
            TypeName = typeName ?? string.Empty;
            Note = note ?? string.Empty;
            IsInvalid = isInvalid;
            VariableId = variableId;
            Device = device ?? string.Empty;
        }

        public string Name { get; }
        public string TypeName { get; }
        public string Note { get; }
        public bool IsInvalid { get; }

        /// <summary>
        /// 变量稳定身份（仅全局变量有；运行时变量/失效项为空）。
        /// 选中候选时一并落进 <see cref="AssignmentRow.VariableId"/>，运行期按 Id 寻址。
        /// </summary>
        public Guid VariableId { get; }

        /// <summary>所属设备（网络变量的连接名）；本地/运行时变量为空</summary>
        public string Device { get; }

        /// <summary>
        /// 不带类型、但**带设备**的显示名：<c>Alarm1[48] · L1</c>。
        ///
        /// 候选浮层的标题行、行内变量名格子、右侧标题都用它——多设备同名点位只靠裸名字分不出，
        /// 而这三处旁边已经各自有类型标签，再拼类型就重复了。
        /// </summary>
        public string DisplayName
        {
            get
            {
                var name = string.IsNullOrWhiteSpace(Name) ? "未命名变量" : Name.Trim();
                return string.IsNullOrWhiteSpace(Device) ? name : $"{name} · {Device}";
            }
        }

        /// <summary>
        /// 下拉显示文本：名字（类型）；有设备时补「· 设备」。
        /// 多设备同名点位必须能在这里分辨，否则两条一模一样的候选等于没得选。
        /// </summary>
        public string Caption
        {
            get
            {
                var head = string.IsNullOrWhiteSpace(TypeName) ? Name : $"{Name}（{TypeName}）";
                return string.IsNullOrWhiteSpace(Device) ? head : $"{head} · {Device}";
            }
        }
    }
}
