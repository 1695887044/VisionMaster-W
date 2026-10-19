namespace VisionMaster.Binding
{
    /// <summary>
    /// 变量命名规则：<b>创建</b>与<b>改名</b>共用同一份判据。
    ///
    /// 为什么必须抽出来：这两条路原先各写一份，结果创建侧漏掉了保留前缀检查
    /// （2026-10-19 交接里记的未决项）。漏掉的后果不是报错，而是静默造出一个
    /// <c>System.*</c> 用户变量把同名系统变量遮住 —— 现场表现是
    /// "轴在动、画面上的数值不变"，几乎不可能自己定位到重名。
    ///
    /// 只做"名字本身合不合法"的静态判断，不查重、不碰集合：
    /// 查重需要知道作用域（用户表/系统表），那是 <see cref="VariableRegistry"/> 的事。
    /// </summary>
    public static class VariableNameRules
    {
        /// <summary>
        /// 校验一个新变量名（创建与改名共用）。
        /// 返回 false 时 <paramref name="error"/> 是可直接上屏的中文原因。
        /// </summary>
        public static bool TryValidateNewName(string? name, out string error)
        {
            var target = (name ?? string.Empty).Trim();

            if (target.Length == 0)
            {
                error = "变量名不能为空";
                return false;
            }

            // `System.` 是系统变量的整体保留前缀（运动轴投影、IO 投影都在它下面）。
            // 用户变量同名会**遮住**系统变量（VariableRegistry 查找用户优先），
            // 而遮住之后没有任何报错，只有"数值不刷新"这种查不出根因的症状。
            if (SystemVariableCatalog.IsReservedName(target))
            {
                error = $"{SystemVariableCatalog.NamePrefix} 是系统变量保留前缀，请更换名称";
                return false;
            }

            error = string.Empty;
            return true;
        }
    }
}
