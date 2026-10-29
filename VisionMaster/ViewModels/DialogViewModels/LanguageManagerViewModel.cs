using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Prism.Mvvm;
using VisionMaster.Scada;

namespace VisionMaster.ViewModels
{
    /// <summary>
    /// 多语言管理器：编辑方案级语言表的<b>副本</b>，确认后原地写回。
    ///
    /// 为什么编辑副本
    /// ---------
    /// 与 <see cref="StateListManagerViewModel"/> 同一条理由：取消要能完整丢弃。
    /// 直接改文档的话，"取消"只能靠再改回去，而语言表一多（每种语言一张、每张几十条词条），
    /// 回滚必然漏一条。副本确认时才落回文档。
    ///
    /// 为什么确认时原地覆盖已存在的语言表
    /// ---------
    /// 运行态/画布订阅的是语言表对象的 <see cref="ScadaLanguage.Version"/>（控件据此重解析文本）。
    /// 如果确认时整批换成新对象，订阅会断——画布上要等整页重建才换文字。
    /// 所以按 <see cref="ScadaLanguage.LanguageId"/> 找到原对象、<see cref="ScadaLanguage.CopyFrom"/>；
    /// 只有新增的语言才 Add 新对象（与文本列表管理器的 <c>Commit</c> 逐条对应）。
    ///
    /// 当前语言也存在副本里
    /// ---------
    /// 「设为当前语言」改的是本对象里的 <see cref="_currentLanguageId"/>，确认时才写回
    /// <see cref="ScadaDocument.CurrentLanguageId"/>——否则用户点了「取消」，
    /// 画面已经换了语言、而语言表没变，成了半截提交。
    /// </summary>
    public sealed class LanguageManagerViewModel : BindableBase
    {
        private readonly ScadaDocument _document;

        /// <summary>正在编辑的"当前语言"（确认时才写回文档）</summary>
        private Guid _currentLanguageId;

        private LanguageRow? _selectedLanguage;

        /// <summary>方案里实际用到的键（构造时扫一次，点「从方案收集键」时重扫）</summary>
        private IReadOnlyList<string> _documentKeys;

        public LanguageManagerViewModel(ScadaDocument document)
        {
            _document = document ?? throw new ArgumentNullException(nameof(document));

            _currentLanguageId = document.CurrentLanguageId;

            Languages = new ObservableCollection<LanguageRow>(
                document.Languages.Select(language => new LanguageRow(language.Clone())));

            _documentKeys = ScadaTextCatalog.CollectKeys(document);

            RefreshCurrentFlags();
            SelectedLanguage = Languages.FirstOrDefault();
        }

        /// <summary>正在编辑的语言表副本（不直接指向文档里的对象）</summary>
        public ObservableCollection<LanguageRow> Languages { get; }

        /// <summary>当前选中的语言表</summary>
        public LanguageRow? SelectedLanguage
        {
            get => _selectedLanguage;
            set
            {
                if (SetProperty(ref _selectedLanguage, value))
                    RaisePropertyChanged(nameof(SelectedIsCurrent));
            }
        }

        /// <summary>选中的是不是当前语言（「设为当前语言」按钮据此判灰）</summary>
        public bool SelectedIsCurrent
            => _selectedLanguage != null && _selectedLanguage.Language.LanguageId == _currentLanguageId;

        /// <summary>
        /// 语言表的覆盖情况，直接上屏：<c>方案里共用到 12 个键，当前语言已填 9 个</c>。
        /// 数字来自属性袋扫描（见 <see cref="ScadaTextCatalog.CollectKeys"/>），不含绑定与运行值。
        /// </summary>
        public string CoverageText
        {
            get
            {
                if (_selectedLanguage == null)
                    return string.Empty;

                var filled = CountFilled(_selectedLanguage.Language, _documentKeys);
                return _documentKeys.Count == 0
                    ? "方案里还没有用到 @键（图元文本写成 @键 之后会出现在这里）"
                    : $"方案里共用到 {_documentKeys.Count} 个键，本语言已填 {filled} 个";
            }
        }

        /// <summary>当前语言的显示名（左侧列表的「当前」徽标之外的文字说明）</summary>
        public string CurrentLanguageText
        {
            get
            {
                var current = Languages.FirstOrDefault(row => row.Language.LanguageId == _currentLanguageId);
                return current == null ? "未选择语言" : current.Language.Name;
            }
        }

        /// <summary>新建一张语言表并选中（名字取当前未被占用的最小「语言_N」）</summary>
        public LanguageRow AddLanguage()
        {
            var row = new LanguageRow(new ScadaLanguage { Name = NextLanguageName() });
            Languages.Add(row);

            // 首张语言表直接设为当前：只做一种语言时用户不必再手动点一次
            // （"建了词表但没选当前语言 → 画面还是显示 @键"是最常见的第一次踩坑，
            //  与 ScadaDocument.AddLanguage 的首张自动设为当前同一条口径）。
            if (_currentLanguageId == Guid.Empty)
                _currentLanguageId = row.Language.LanguageId;

            RefreshCurrentFlags();
            SelectedLanguage = row;
            return row;
        }

        /// <summary>删除当前选中的语言表（调用方负责二次确认）</summary>
        public void RemoveSelectedLanguage()
        {
            if (_selectedLanguage == null)
                return;

            var index = Languages.IndexOf(_selectedLanguage);
            var removedId = _selectedLanguage.Language.LanguageId;

            Languages.Remove(_selectedLanguage);

            // 删的正好是当前语言 → 回落到剩下的第一张（与 ScadaDocument.TryRemoveLanguage 同口径）
            if (_currentLanguageId == removedId)
                _currentLanguageId = Languages.Count > 0 ? Languages[0].Language.LanguageId : Guid.Empty;

            RefreshCurrentFlags();

            SelectedLanguage = Languages.Count == 0
                ? null
                : Languages[Math.Clamp(index, 0, Languages.Count - 1)];
        }

        /// <summary>给当前语言加一条空词条</summary>
        public ScadaLanguageEntry? AddEntry()
        {
            if (_selectedLanguage == null)
                return null;

            var entry = new ScadaLanguageEntry { Key = NextKey(_selectedLanguage.Language), Text = string.Empty };
            _selectedLanguage.Language.Entries.Add(entry);
            RaiseCoverageChanged();
            return entry;
        }

        /// <summary>删除一条词条（调用方负责二次确认）</summary>
        public void RemoveEntry(ScadaLanguageEntry? entry)
        {
            if (_selectedLanguage == null || entry == null)
                return;

            _selectedLanguage.Language.Entries.Remove(entry);
            RaiseCoverageChanged();
        }

        /// <summary>
        /// 把选中语言设为「当前语言」（只改副本，确认时才写回文档）。
        /// 传 null = 按当前选中项。
        /// </summary>
        public void SetCurrent(LanguageRow? row = null)
        {
            var target = row ?? _selectedLanguage;
            if (target == null)
                return;

            _currentLanguageId = target.Language.LanguageId;
            RefreshCurrentFlags();
        }

        /// <summary>
        /// 「从方案收集键」：把方案里已用到、但当前语言还没有的键补成空行。
        /// <b>只补缺失的，不覆盖已有译文</b>——否则点一下就把用户翻好的文字清空了。
        /// 返回补了几条（0 = 都已经有了，界面据此给一句"没有新增"而不是静默无反应）。
        /// </summary>
        public int CollectKeys()
        {
            if (_selectedLanguage == null)
                return 0;

            _documentKeys = ScadaTextCatalog.CollectKeys(_document);

            var existing = new HashSet<string>(
                _selectedLanguage.Language.Entries
                    .Where(entry => !string.IsNullOrEmpty(entry.Key))
                    .Select(entry => entry.Key),
                StringComparer.Ordinal);

            int added = 0;
            foreach (var key in _documentKeys)
            {
                if (!existing.Add(key))
                    continue;

                _selectedLanguage.Language.Entries.Add(new ScadaLanguageEntry { Key = key, Text = string.Empty });
                added++;
            }

            RaiseCoverageChanged();
            return added;
        }

        /// <summary>
        /// 确认：把副本原地写回文档（与 <see cref="StateListManagerViewModel.Commit"/> 同一套顺序）。
        /// 删除的语言先 <see cref="ScadaLanguage.MarkDeleted"/>（让仍订阅它的控件立刻进入"已删除"），
        /// 再移出集合；已存在的语言 <see cref="ScadaLanguage.CopyFrom"/>；新增的 Add 新对象。
        /// 最后写当前语言——它跟着方案一起存盘。
        /// </summary>
        public void Commit()
        {
            // 副本里没有"当前语言"指向的那张（用户在管理器里删掉了它）→ 回落到第一张
            if (_currentLanguageId != Guid.Empty
                && !Languages.Any(row => row.Language.LanguageId == _currentLanguageId))
            {
                _currentLanguageId = Languages.Count > 0 ? Languages[0].Language.LanguageId : Guid.Empty;
            }

            using (_document.BeginEdit("编辑语言表"))
            {
                var editedIds = new HashSet<Guid>(Languages.Select(row => row.Language.LanguageId));

                foreach (var existing in _document.Languages.ToList())
                {
                    if (editedIds.Contains(existing.LanguageId))
                        continue;

                    existing.MarkDeleted();
                    _document.Languages.Remove(existing);
                }

                foreach (var edited in Languages)
                {
                    var existing = _document.Languages
                        .FirstOrDefault(language => language.LanguageId == edited.Language.LanguageId);

                    if (existing == null)
                        _document.Languages.Add(edited.Language.Clone());
                    else
                        existing.CopyFrom(edited.Language);
                }

                _document.CurrentLanguageId = _currentLanguageId;
            }
        }

        /// <summary>把「当前」徽标刷成与 <see cref="_currentLanguageId"/> 一致</summary>
        private void RefreshCurrentFlags()
        {
            foreach (var row in Languages)
                row.IsCurrent = row.Language.LanguageId == _currentLanguageId;

            RaisePropertyChanged(nameof(SelectedIsCurrent));
            RaisePropertyChanged(nameof(CurrentLanguageText));
        }

        private void RaiseCoverageChanged() => RaisePropertyChanged(nameof(CoverageText));

        /// <summary>取当前未被占用的最小「语言_N」（副本之间自己判重，不依赖文档）</summary>
        private string NextLanguageName()
        {
            var used = new HashSet<string>(
                Languages.Select(row => row.Language.Name),
                StringComparer.OrdinalIgnoreCase);

            int index = 1;
            while (used.Contains($"语言_{index}"))
                index++;

            return $"语言_{index}";
        }

        /// <summary>新词条的键：<c>key.N</c>，保证不与已有键撞</summary>
        private static string NextKey(ScadaLanguage language)
        {
            var used = new HashSet<string>(
                language.Entries.Where(entry => !string.IsNullOrEmpty(entry.Key)).Select(entry => entry.Key),
                StringComparer.Ordinal);

            int index = 1;
            while (used.Contains($"key.{index}"))
                index++;

            return $"key.{index}";
        }

        /// <summary>本语言里已填（非空译文）的键数，只统计方案里真用到的那些</summary>
        private static int CountFilled(ScadaLanguage language, IReadOnlyList<string> keys)
        {
            int filled = 0;
            foreach (var key in keys)
            {
                if (language.TryGet(key, out _))
                    filled++;
            }

            return filled;
        }
    }

    /// <summary>
    /// 左侧语言列表的一行：把 <see cref="ScadaLanguage"/> 包一层，多带一个 <see cref="IsCurrent"/>。
    ///
    /// 为什么要包：列表里要给当前语言打「当前」徽标，而"是不是当前"是<b>管理器</b>的状态
    /// （副本里选中的那张），不属于语言表本身——往模型上加一个只服务于本界面的字段，
    /// 它会跟着 <c>.vms</c> 一起落盘，下次打开就成了脏数据。
    /// 与 <c>BasePageOption</c> 包 <see cref="ScadaPage"/> 是同一个理由。
    /// </summary>
    public sealed class LanguageRow : BindableBase
    {
        private bool _isCurrent;

        public LanguageRow(ScadaLanguage language)
        {
            Language = language ?? throw new ArgumentNullException(nameof(language));
        }

        /// <summary>语言表副本（改它就是改副本，确认时才写回文档）</summary>
        public ScadaLanguage Language { get; }

        /// <summary>语言名（转发到模型；TextBox 双向绑定用）</summary>
        public string Name
        {
            get => Language.Name;
            set
            {
                Language.Name = value;
                RaisePropertyChanged();
            }
        }

        /// <summary>是不是当前语言（左侧列表的「当前」徽标）</summary>
        public bool IsCurrent
        {
            get => _isCurrent;
            set => SetProperty(ref _isCurrent, value);
        }
    }
}
