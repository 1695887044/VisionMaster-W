using System.Collections.Generic;

namespace Core.Interfaces
{
    /// <summary>
    /// 插件自带帮助手册的提供者（<b>可选实现</b>）。
    ///
    /// 实现者：帮助内容不落在插件程序集里（例如要在运行时按现场环境生成正文），
    /// 或需要显式指定章节标题与顺序的插件。
    ///
    /// 不实现此接口的插件<b>不受任何影响</b>——宿主找手册的顺序是：
    ///   ① 约定路径：插件程序集里 <c>Help\</c> 下的内嵌资源（零配置，加文件即可）；
    ///   ② 本接口：类型实现了才创建实例调用一次；
    ///   ③ 兜底：用 <c>[Display]</c> 元数据自动生成一页说明（端口表 + 描述）。
    /// 三步都不需要改 <see cref="IVisionPlugin"/>，属于非破坏性扩展
    /// （与 <see cref="IPluginConfigContextProvider"/> 同一姿态）。
    ///
    /// 调用时机：宿主启动自检链里"帮助目录合并"一步，每个插件至多调用一次，
    /// 实现方应只做取数（不要读现场设备、不要起线程）。
    /// </summary>
    public interface IPluginHelpProvider
    {
        /// <summary>
        /// 返回本插件的帮助章节（顺序即目录顺序；返回空集合等同于未提供，宿主转兜底页）。
        /// </summary>
        IReadOnlyList<PluginHelpSection> GetHelpSections();
    }

    /// <summary>插件帮助的一个章节：标题 + Markdown 正文（由宿主渲染，无需插件关心排版）。</summary>
    public sealed class PluginHelpSection
    {
        /// <summary>章节标题（目录树里显示的文字）</summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>正文，Markdown 子集（标题/段落/列表/表格/代码块/图片/链接）</summary>
        public string Markdown { get; set; } = string.Empty;
    }
}
