using System;
using System.Collections.Generic;
using VisionMaster.Scada;

namespace VisionMaster.Services
{
    /// <summary>
    /// <see cref="IScadaLanguageService"/> 的默认实现：<b>直接读写方案文档里的语言表</b>。
    ///
    /// 它解决的是什么问题
    /// ---------
    /// 运行态图元只认 <see cref="IScadaLanguageService"/>（见 <see cref="ScadaRuntimeContext.Languages"/>），
    /// 而"方案里配了哪几种语言、现在用哪一种"这两件事的唯一事实源是 <see cref="ScadaDocument"/>。
    /// 本类就是把文档那两个字段翻译成接口的形状，判定规则一条都不在这里写——
    /// 键的解析、<c>@@</c> 转义、三级回落全部委托给 <see cref="ScadaTextCatalog"/>，
    /// 查找委托给 <see cref="ScadaTextCatalog.FindById"/> / <see cref="ScadaTextCatalog.FindByName"/>。
    /// 两份实现分头演化的后果是"编辑器里显示对了、运行起来还是 @键"。
    ///
    /// 为什么收 <see cref="Func{TResult}"/> 而不是一个文档实例
    /// ---------
    /// 宿主是<b>方案级单例</b>：换方案时文档整个被替换（<c>IReadOnlyWorkspaceContext.CurrentSolution</c>），
    /// 而语言服务在 App 里只注册一次。构造时抓一个文档实例，换方案之后它还在读旧文档——
    /// 表现是"切了方案，画面上的 @键 一个都不解析"，且不报错。
    /// 所以默认走 <see cref="Func{TResult}"/>，每次现取；<b>方案不换的宿主</b>（运行窗口、断言）
    /// 可以直接用收文档实例的那个重载，它只是把同一个实例包成常量委托。
    ///
    /// 持久化
    /// ---------
    /// <see cref="CurrentLanguageId"/> 的 getter 读文档、写入走 <see cref="TrySetLanguage(Guid, out string?)"/>
    /// → 落的是 <see cref="ScadaDocument.CurrentLanguageId"/>，因此"切换语言"会跟着方案一起存盘
    /// （与 <c>ScadaDocument.StartupPageId</c> 同一条口径：状态存在文档里，服务只是出口）。
    ///
    /// 线程
    /// ---------
    /// <b>只允许在 UI 线程调用</b>（与 <see cref="IScadaAccessPolicy"/> 同一条纪律）：
    /// 文档的集合与属性通知都会回到绑定上，跨线程写就是"偶发一次 InvalidOperationException"。
    /// 本类因此不加锁——加锁只会把"跨线程写"从崩溃变成更难查的数据竞争。
    /// </summary>
    public sealed class ScadaLanguageService : IScadaLanguageService
    {
        /// <summary>没有文档时的空表（不返回 null：调用方一律按"空表 = 没配多语言"处理）</summary>
        private static readonly IReadOnlyList<ScadaLanguage> EmptyLanguages = Array.Empty<ScadaLanguage>();

        /// <summary>
        /// 当前文档的读取口。返回 null 表示"此刻没有打开任何方案"——
        /// 这时所有查询退化成空表 / <see cref="Guid.Empty"/>，不抛。
        /// </summary>
        private readonly Func<ScadaDocument?> _documentProvider;

        /// <summary>
        /// 收一个<b>固定文档</b>的宿主用（运行窗口、断言、单方案预览）。
        /// 文档换实例的场景请改用 <see cref="ScadaLanguageService(Func{ScadaDocument?})"/>。
        /// </summary>
        public ScadaLanguageService(ScadaDocument? document)
            : this(() => document)
        {
        }

        /// <summary>
        /// 收一个<b>文档读取口</b>的宿主用（App 级单例：换方案后要读到新文档）。
        /// </summary>
        public ScadaLanguageService(Func<ScadaDocument?> documentProvider)
        {
            _documentProvider = documentProvider ?? throw new ArgumentNullException(nameof(documentProvider));
        }

        /// <summary>当前文档；没有方案时为 null</summary>
        private ScadaDocument? Document => _documentProvider();

        /// <inheritdoc/>
        /// <remarks>
        /// 返回的是<b>文档里的那个集合本身</b>（不是副本）：语言表管理器改完之后，
        /// 运行态立刻读到新表，不需要再"推一遍"。空表（旧 .vms 没配多语言）时返回空数组。
        /// </remarks>
        public IReadOnlyList<ScadaLanguage> Languages => Document?.Languages ?? EmptyLanguages;

        /// <inheritdoc/>
        /// <remarks>写入只走 <see cref="TrySetLanguage(Guid, out string?)"/>，保证切换必然落盘并广播 <see cref="Changed"/></remarks>
        public Guid CurrentLanguageId => Document?.CurrentLanguageId ?? Guid.Empty;

        /// <inheritdoc/>
        public string CurrentLanguageName
        {
            get
            {
                var current = ScadaTextCatalog.FindById(Languages, CurrentLanguageId);
                return current?.Name ?? "未选择语言";
            }
        }

        /// <inheritdoc/>
        public event Action? Changed;

        /// <inheritdoc/>
        public bool TrySetLanguage(Guid languageId, out string? error)
        {
            if (Document is not { } document)
            {
                error = "还没有打开方案，无法切换语言";
                return false;
            }

            if (document.Languages.Count == 0)
            {
                error = "还没有配置任何语言表";
                return false;
            }

            if (languageId == Guid.Empty)
            {
                error = "没有指定要切换到的语言";
                return false;
            }

            var target = document.FindLanguage(languageId);
            if (target == null)
            {
                error = "语言表不存在（可能已被删除），请重新选择";
                return false;
            }

            Apply(document, target);
            error = null;
            return true;
        }

        /// <inheritdoc/>
        public bool TrySetLanguage(string? languageName, out string? error)
        {
            if (Document is not { } document)
            {
                error = "还没有打开方案，无法切换语言";
                return false;
            }

            if (document.Languages.Count == 0)
            {
                error = "还没有配置任何语言表";
                return false;
            }

            if (string.IsNullOrWhiteSpace(languageName))
            {
                error = "没有指定要切换到的语言名";
                return false;
            }

            var target = document.FindLanguageByName(languageName);
            if (target == null)
            {
                error = $"找不到名为「{languageName!.Trim()}」的语言表";
                return false;
            }

            Apply(document, target);
            error = null;
            return true;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// 三级回落（当前语言 → 任一语言 → 原文 <c>@键</c>）全在 <see cref="ScadaTextCatalog.Resolve"/> 里，
        /// 这里只是把"当前文档的语言表 + 当前语言"喂给它——两处各写一份回落逻辑，
        /// 就会变成"编辑器里能看到译文、运行态看不到"。
        /// </remarks>
        public string Resolve(string? text)
            => ScadaTextCatalog.Resolve(text, Languages, CurrentLanguageId);

        /// <summary>
        /// 写入当前语言并广播。成功一律广播（即使切到的就是当前语言）：
        /// 订阅方拿它当"重新解析一遍文本"的信号，多刷一次只是白做一遍解析，
        /// 而漏刷一次就是"切了语言画面没变"——两种代价不对等。
        /// </summary>
        private void Apply(ScadaDocument document, ScadaLanguage language)
        {
            document.CurrentLanguageId = language.LanguageId;
            Changed?.Invoke();
        }
    }
}
