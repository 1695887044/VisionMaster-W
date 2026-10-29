using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Newtonsoft.Json;
using Prism.Mvvm;

namespace VisionMaster.Scada
{
    /// <summary>
    /// 语言表的一条：文案键 → 该语言下的文字。
    ///
    /// 键就是图元文本属性里 <c>@</c> 后面那一段（见 <see cref="ScadaTextCatalog"/>）。
    /// 键区分大小写：现场常用 <c>btn.start</c> / <c>title.line1</c> 这种带点的小写键，
    /// 大小写不敏感会让 <c>Start</c> 与 <c>start</c> 变成同一个键、互相覆盖。
    /// </summary>
    public sealed class ScadaLanguageEntry : BindableBase
    {
        private string _key = string.Empty;
        private string _text = string.Empty;

        /// <summary>文案键（不含 <c>@</c> 前缀）</summary>
        [JsonProperty]
        public string Key
        {
            get => _key;
            set => SetProperty(ref _key, (value ?? string.Empty).Trim());
        }

        /// <summary>该语言下的文字；空串表示"这条在这个语言下还没翻"</summary>
        [JsonProperty]
        public string Text
        {
            get => _text;
            set => SetProperty(ref _text, value ?? string.Empty);
        }
    }

    /// <summary>
    /// 方案级语言表：一份 <c>键 → 文字</c> 的词表，对应一种界面语言。
    ///
    /// 为什么是方案级而不是每图元一份
    /// ---------
    /// 同一句「启动」会出现在按钮、报警条标题、报表表头等十几个地方；每处各存一份译文，
    /// 换语言就是"改十几处、漏一处"。词表放在 <see cref="ScadaDocument.Languages"/>，
    /// 图元文本只写 <c>@btn.start</c> 这样的键，语言表一换全画面跟着换。
    ///
    /// 与状态列表的关系
    /// ---------
    /// 两者都是"共享词表"，但职责不同：状态列表是**值 → 文字/图/色**（按变量值查），
    /// 语言表是**键 → 文字**（按当前语言查）。同一个 StateView 可以两条都用：
    /// 状态条目里的文字本身也可以写成 <c>@state.run</c>，由文本解析再翻一次。
    ///
    /// 落盘
    /// ---------
    /// <see cref="LanguageId"/> 是稳定身份，动作「切换语言」存它；改名不打断引用。
    /// 旧 <c>.vms</c> 没有 <c>Languages</c> 集合时反序列化得到空表，所有文本按字面量显示，
    /// 与 v5 行为完全一致，不需要迁移代码。
    /// </summary>
    public sealed class ScadaLanguage : BindableBase
    {
        private Guid _languageId = Guid.NewGuid();
        private string _name = "新建语言";
        private ObservableCollection<ScadaLanguageEntry> _entries = new();
        private int _version;

        [JsonConstructor]
        public ScadaLanguage()
        {
            AttachEntries();
        }

        /// <summary>稳定身份；动作「切换语言」存它，改名不打断引用</summary>
        [JsonProperty]
        public Guid LanguageId
        {
            get => _languageId;
            set => SetProperty(ref _languageId, value);
        }

        /// <summary>显示名（如「简体中文」「English」）；空值归一成「未命名语言」</summary>
        [JsonProperty]
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, string.IsNullOrWhiteSpace(value) ? "未命名语言" : value.Trim());
        }

        /// <summary>词表条目（键 → 文字）</summary>
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public ObservableCollection<ScadaLanguageEntry> Entries
        {
            get => _entries;
            set
            {
                if (ReferenceEquals(_entries, value))
                    return;

                DetachEntries();
                _entries = value ?? new ObservableCollection<ScadaLanguageEntry>();
                AttachEntries();
                BumpVersion();
            }
        }

        /// <summary>变更计数；订阅方只看它是否变了</summary>
        [JsonIgnore]
        public int Version
        {
            get => _version;
            private set => SetProperty(ref _version, value);
        }

        /// <summary>按键取值；键不存在或译文为空都返回 false（空译文不该把界面刷成空白）</summary>
        public bool TryGet(string? key, out string text)
        {
            text = string.Empty;
            if (string.IsNullOrEmpty(key))
                return false;

            foreach (var entry in _entries)
            {
                if (entry != null && string.Equals(entry.Key, key, StringComparison.Ordinal))
                {
                    if (string.IsNullOrEmpty(entry.Text))
                        return false;

                    text = entry.Text;
                    return true;
                }
            }

            return false;
        }

        /// <summary>深拷贝（管理器用副本编辑，取消不影响原表）</summary>
        public ScadaLanguage Clone()
        {
            var clone = new ScadaLanguage
            {
                LanguageId = _languageId,
                Name = _name,
            };

            foreach (var entry in _entries)
                clone.Entries.Add(new ScadaLanguageEntry { Key = entry.Key, Text = entry.Text });

            return clone;
        }

        /// <summary>把另一份语言表原地覆盖到本对象（保留引用，订阅不断）</summary>
        public void CopyFrom(ScadaLanguage source)
        {
            if (source == null)
                return;

            Name = source.Name;

            _entries.Clear();
            foreach (var entry in source.Entries)
                _entries.Add(new ScadaLanguageEntry { Key = entry.Key, Text = entry.Text });

            BumpVersion();
        }

        /// <summary>管理器删除时调用：让仍引用旧对象的控件知道"这份语言表已经没了"</summary>
        public void MarkDeleted()
        {
            _entries.Clear();
            _name = "已删除的语言";
            RaisePropertyChanged(nameof(Name));
            BumpVersion();
        }

        private void AttachEntries()
        {
            _entries.CollectionChanged += OnEntriesChanged;
            foreach (var entry in _entries)
                entry.PropertyChanged += OnEntryPropertyChanged;
        }

        private void DetachEntries()
        {
            _entries.CollectionChanged -= OnEntriesChanged;
            foreach (var entry in _entries)
                entry.PropertyChanged -= OnEntryPropertyChanged;
        }

        private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (ScadaLanguageEntry entry in e.OldItems)
                    entry.PropertyChanged -= OnEntryPropertyChanged;
            }

            if (e.NewItems != null)
            {
                foreach (ScadaLanguageEntry entry in e.NewItems)
                    entry.PropertyChanged += OnEntryPropertyChanged;
            }

            BumpVersion();
        }

        private void OnEntryPropertyChanged(object? sender, PropertyChangedEventArgs e) => BumpVersion();

        private void BumpVersion() => Version++;
    }

    /// <summary>
    /// 文本解析：把图元属性里的 <c>@键</c> 换成当前语言下的文字。
    ///
    /// 为什么用"前缀约定"而不是给每个文本属性再加一个 TextKey
    /// ---------
    /// 加 TextKey 意味着：每个图元的每条文本属性都要多一列、属性面板多一行、描述符快照多一项，
    /// 而收益只是"少打一个 @"。前缀约定零字段、零面板行，且对**所有**文本属性一次生效
    /// （按钮文字、报警条标题、报表表头、空态提示……都走同一条通道）。
    ///
    /// 代价与边界（写清楚，免得后面有人踩）：
    /// ① 文本真要以 <c>@</c> 开头当字面量时会被当成键 —— 用 <c>@@</c> 转义（见 <see cref="Unescape"/>）；
    /// ② 只解析**设计期存的属性值**，不解析绑定变量送来的运行值（数据不该被当文案翻）；
    /// ③ 键在当前语言里缺失时，先找其它语言有没有这个键，再没有就原样显示 <c>@键</c>——
    ///    "看得见哪个键没配"比"静默显示空白"好排查。
    /// </summary>
    public static class ScadaTextCatalog
    {
        /// <summary>文案键前缀</summary>
        public const char Marker = '@';

        /// <summary>判断一段文本是不是 <c>@键</c>；是则返回键（不含 @）</summary>
        public static bool IsKey(string? text, out string key)
        {
            key = string.Empty;

            if (string.IsNullOrEmpty(text) || text.Length < 2 || text[0] != Marker)
                return false;

            // @@ 开头是转义的字面量，不是键
            if (text[1] == Marker)
                return false;

            key = text.Substring(1);
            return key.Length > 0;
        }

        /// <summary>把 <c>@@</c> 开头的转义还原成单个 <c>@</c>；不是转义则原样返回</summary>
        public static string Unescape(string? text)
        {
            if (!string.IsNullOrEmpty(text) && text.Length >= 2 && text[0] == Marker && text[1] == Marker)
                return text.Substring(1);

            return text ?? string.Empty;
        }

        /// <summary>
        /// 解析一段文本：不是 <c>@键</c> 就原样返回；是则按"当前语言 → 任一语言 → 原文"三级回落。
        /// <paramref name="languages"/> 为空（旧方案 / 设计期未注入）时只做 <c>@@</c> 转义还原。
        /// </summary>
        public static string Resolve(string? text, IReadOnlyList<ScadaLanguage>? languages, Guid currentLanguageId)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            if (!IsKey(text, out var key))
                return Unescape(text);

            if (languages != null && languages.Count > 0)
            {
                var current = FindById(languages, currentLanguageId);
                if (current != null && current.TryGet(key, out var value))
                    return value;

                for (int i = 0; i < languages.Count; i++)
                {
                    var language = languages[i];
                    if (language != null && !ReferenceEquals(language, current) && language.TryGet(key, out value))
                        return value;
                }
            }

            return text;
        }

        /// <summary>按稳定 Id 找语言表；<c>Guid.Empty</c> 或找不到返回 null</summary>
        public static ScadaLanguage? FindById(IReadOnlyList<ScadaLanguage>? languages, Guid languageId)
        {
            if (languages == null || languageId == Guid.Empty)
                return null;

            for (int i = 0; i < languages.Count; i++)
            {
                if (languages[i] != null && languages[i].LanguageId == languageId)
                    return languages[i];
            }

            return null;
        }

        /// <summary>按名字找语言表（动作里只存了名字时的兜底；忽略大小写与首尾空白）</summary>
        public static ScadaLanguage? FindByName(IReadOnlyList<ScadaLanguage>? languages, string? name)
        {
            if (languages == null || string.IsNullOrWhiteSpace(name))
                return null;

            var trimmed = name!.Trim();
            for (int i = 0; i < languages.Count; i++)
            {
                var language = languages[i];
                if (language != null && string.Equals(language.Name, trimmed, StringComparison.OrdinalIgnoreCase))
                    return language;
            }

            return null;
        }

        /// <summary>
        /// 收集方案里所有用到的键（语言表管理器用来提示"哪些键还没翻"）。
        /// 只扫图元属性袋与画面属性里的字符串，不扫绑定与运行值。
        /// </summary>
        public static IReadOnlyList<string> CollectKeys(ScadaDocument? document)
        {
            var keys = new SortedSet<string>(StringComparer.Ordinal);
            if (document == null)
                return keys.ToList();

            foreach (var page in document.Pages)
            {
                if (page == null)
                    continue;

                foreach (var element in page.Elements)
                {
                    if (element == null)
                        continue;

                    foreach (var value in element.Properties.Values)
                    {
                        if (IsKey(value, out var key))
                            keys.Add(key);
                    }
                }
            }

            return keys.ToList();
        }
    }
}
