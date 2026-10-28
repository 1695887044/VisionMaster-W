namespace Plugin.Util.Models
{
    /// <summary>
    /// 变量名下拉的一行候选。
    /// 全局作用域只允许从变量管理里选；当前值失效时补一条 <see cref="IsInvalid"/> 的只读项，
    /// 显示「原名（已失效）」，不清空、不自动替换。
    /// </summary>
    public sealed class NameCandidate
    {
        public NameCandidate(string name, string typeName, string note, bool isInvalid = false)
        {
            Name = name ?? string.Empty;
            TypeName = typeName ?? string.Empty;
            Note = note ?? string.Empty;
            IsInvalid = isInvalid;
        }

        public string Name { get; }
        public string TypeName { get; }
        public string Note { get; }
        public bool IsInvalid { get; }

        /// <summary>下拉显示文本：名字（类型）；失效项显示「名字（已失效）」。</summary>
        public string Caption => string.IsNullOrWhiteSpace(TypeName) ? Name : $"{Name}（{TypeName}）";
    }
}
