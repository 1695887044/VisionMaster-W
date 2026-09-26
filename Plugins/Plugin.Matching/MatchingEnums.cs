using System.ComponentModel.DataAnnotations;

namespace Plugin.Matching
{
    /// <summary>
    /// 配置界面信息栏的消息级别（只影响颜色，不参与任何匹配逻辑）。
    /// 与 BlobDetect 的同名枚举同语义、各插件自带一份（插件间不共享类型，避免跨程序集耦合）。
    /// </summary>
    public enum StatusLevel
    {
        /// <summary>正常（绿）：模板已创建、路径有效</summary>
        Info,

        /// <summary>提示（橙）：还没画模板/没载图等"还能继续"的状态</summary>
        Warning,

        /// <summary>错误（红）：载图失败、创建模板失败</summary>
        Error,
    }
}
