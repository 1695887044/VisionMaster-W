using System;
using System.Collections.Generic;

namespace VisionMaster.Scada
{
    /// <summary>
    /// 权限目录的只读出口（B3）：组态属性面板「所需权限」下拉的候选来源。
    ///
    /// 目录本身由用户管理维护（15 条内置 + 自定义，上限 32），面板只读不写。
    /// 面板与账号本分属两个程序集，所以这里只暴露"当前有哪些权限"与"目录变了"，
    /// 不把 <c>ScadaUserStore</c> 的写事务、密码学参数带进 UI 层。
    /// </summary>
    public interface IScadaPermissionCatalog
    {
        /// <summary>当前权限目录（含内置与自定义）；每次返回一份快照。</summary>
        IReadOnlyList<ScadaPermissionInfo> PermissionCatalog { get; }

        /// <summary>
        /// 目录或用户组写盘后触发。属性面板据此刷新下拉候选——
        /// 用户刚在「用户管理」里新建的自定义权限，回到组态面板就能选到。
        /// </summary>
        event Action? CatalogChanged;
    }
}
