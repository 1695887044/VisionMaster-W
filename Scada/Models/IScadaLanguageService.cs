using System;
using System.Collections.Generic;

namespace VisionMaster.Scada
{
    /// <summary>
    /// 运行态"当前语言"出口：谁在提供词表、现在用哪一种、以及怎么切。
    ///
    /// 为什么单独一个接口而不是把 <see cref="ScadaDocument.Languages"/> 直接塞给控件：
    /// 设计期读的是文档里的集合，运行态读的是**一份快照 + 一个当前语言**——
    /// 两者生命周期不同（运行中改语言表不该让画面闪一下重画）。
    /// 控件只认这个接口，宿主决定背后是文档、是副本、还是将来的远端词表。
    ///
    /// 线程：只允许在 UI 线程调用（与 <see cref="IScadaAccessPolicy"/> 同一条纪律）。
    /// </summary>
    public interface IScadaLanguageService
    {
        /// <summary>可用语言表（顺序即界面下拉顺序；空表 = 方案没配多语言）</summary>
        IReadOnlyList<ScadaLanguage> Languages { get; }

        /// <summary>当前语言 Id；<c>Guid.Empty</c> = 没选（所有 <c>@键</c> 原样显示）</summary>
        Guid CurrentLanguageId { get; }

        /// <summary>当前语言显示名；没选时给「未选择语言」这类可直接上屏的文案</summary>
        string CurrentLanguageName { get; }

        /// <summary>语言或当前语言变化时触发（控件据此重刷文本）</summary>
        event Action? Changed;

        /// <summary>按稳定 Id 切换；目标不存在返回 false 并给可上屏原因</summary>
        bool TrySetLanguage(Guid languageId, out string? error);

        /// <summary>按名字切换（动作里只存了名字时的兜底）；找不到返回 false 并给原因</summary>
        bool TrySetLanguage(string? languageName, out string? error);

        /// <summary>按当前语言解析一段文本（<c>@键</c> 约定，见 <see cref="ScadaTextCatalog"/>）</summary>
        string Resolve(string? text);
    }
}
