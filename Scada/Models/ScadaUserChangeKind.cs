namespace VisionMaster.Scada
{
    /// <summary>账号写操作的类型（B3：角色改为用户组）。</summary>
    public enum ScadaUserChangeKind
    {
        Added = 0,
        PasswordChanged = 1,
        PasswordReset = 2,
        GroupChanged = 3,
        Removed = 4
    }

    public static class ScadaUserChangeKindExtensions
    {
        /// <summary>审计/日志用中文名；未知值回显数值，不抛。</summary>
        public static string DisplayName(this ScadaUserChangeKind kind) => kind switch
        {
            ScadaUserChangeKind.Added => "新建账号",
            ScadaUserChangeKind.PasswordChanged => "修改密码",
            ScadaUserChangeKind.PasswordReset => "重置密码",
            ScadaUserChangeKind.GroupChanged => "变更用户组",
            ScadaUserChangeKind.Removed => "删除账号",
            _ => $"未知变更({(int)kind})",
        };
    }
}
