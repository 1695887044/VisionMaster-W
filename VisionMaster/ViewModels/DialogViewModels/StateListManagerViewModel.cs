using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Prism.Mvvm;
using VisionMaster.Scada;

namespace VisionMaster.ViewModels
{
    /// <summary>
    /// 文本列表管理器（P1-1）：编辑方案级共享列表的副本，确认后原地写回。
    ///
    /// 为什么编辑副本
    /// ---------
    /// 取消要能完整丢弃；直接改文档的话，取消只能靠“再改回去”，
    /// 而条目增删/改名一多，回滚必然漏。副本确认时才落回文档。
    ///
    /// 为什么确认时原地覆盖已存在的列表
    /// ---------
    /// ComboBox 订阅的是列表对象的 <see cref="ScadaStateList.Version"/>；
    /// 如果确认时整批换成新对象，订阅会断，画布上的下拉框要等重建才更新。
    /// 已存在的列表按 Id 找到原对象、<see cref="ScadaStateList.CopyFrom"/>；
    /// 只有新增的列表才 Add 新对象。
    /// </summary>
    public sealed class StateListManagerViewModel : BindableBase
    {
        private readonly ScadaDocument _document;
        private ScadaStateList? _selectedList;

        public StateListManagerViewModel(ScadaDocument document)
        {
            _document = document ?? throw new ArgumentNullException(nameof(document));
            Lists = new ObservableCollection<ScadaStateList>(
                document.StateLists.Select(list => list.Clone()));

            SelectedList = Lists.FirstOrDefault();
        }

        /// <summary>正在编辑的列表副本（不直接指向文档里的对象）</summary>
        public ObservableCollection<ScadaStateList> Lists { get; }

        /// <summary>当前选中的列表</summary>
        public ScadaStateList? SelectedList
        {
            get => _selectedList;
            set
            {
                if (SetProperty(ref _selectedList, value))
                    RaisePropertyChanged(nameof(ReferenceText));
            }
        }

        /// <summary>当前列表被多少个图元引用（删除前必须让用户看见）</summary>
        public string ReferenceText
        {
            get
            {
                if (_selectedList == null)
                    return string.Empty;

                var count = CountReferences(_selectedList.StateListId);
                return count == 0 ? "未被任何控件引用" : $"被 {count} 个控件引用";
            }
        }

        /// <summary>新建列表并选中</summary>
        public ScadaStateList AddList()
        {
            var list = new ScadaStateList { Name = "新建文本列表" };
            list.Entries.Add(new ScadaStateEntry { Value = 0, Text = "状态 0" });
            list.Entries.Add(new ScadaStateEntry { Value = 1, Text = "状态 1" });
            Lists.Add(list);
            SelectedList = list;
            return list;
        }

        /// <summary>删除当前列表（调用方负责二次确认；有引用时提示引用数）</summary>
        public void RemoveSelectedList()
        {
            if (_selectedList == null)
                return;

            var index = Lists.IndexOf(_selectedList);
            Lists.Remove(_selectedList);
            SelectedList = Lists.Count == 0
                ? null
                : Lists[Math.Clamp(index, 0, Lists.Count - 1)];
        }

        /// <summary>给当前列表加一条（值取现有最大值 +1，避免新行撞已有值）</summary>
        public ScadaStateEntry? AddEntry()
        {
            if (_selectedList == null)
                return null;

            var next = _selectedList.Entries.Count == 0
                ? 0
                : _selectedList.Entries.Max(entry => entry.Value) + 1;

            var entry = new ScadaStateEntry
            {
                Value = next,
                Text = "状态 " + (_selectedList.Entries.Count + 1),
            };

            _selectedList.Entries.Add(entry);
            RaisePropertyChanged(nameof(ReferenceText));
            return entry;
        }

        /// <summary>删除一条</summary>
        public void RemoveEntry(ScadaStateEntry? entry)
        {
            if (_selectedList == null || entry == null)
                return;

            _selectedList.Entries.Remove(entry);
        }

        /// <summary>当前列表被多少图元引用（按属性袋里的 StateListId 字符串精确比较）</summary>
        public int CountReferences(Guid stateListId)
        {
            var key = stateListId.ToString("D");
            return _document.Pages
                .Concat(_document.BasePages)
                .SelectMany(page => page.Elements)
                .Count(element => string.Equals(
                    element.GetProperty("StateListId"),
                    key,
                    StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 确认：把副本原地写回文档。
        /// 删除的列表先 MarkDeleted（让仍订阅它的 ComboBox 立刻进入“未找到”），再移出集合；
        /// 已存在的列表 CopyFrom；新增的列表 Add。
        /// </summary>
        public void Commit()
        {
            using (_document.BeginEdit("编辑文本列表"))
            {
                var editedIds = new HashSet<Guid>(Lists.Select(list => list.StateListId));

                foreach (var existing in _document.StateLists.ToList())
                {
                    if (editedIds.Contains(existing.StateListId))
                        continue;

                    existing.MarkDeleted();
                    _document.StateLists.Remove(existing);
                }

                foreach (var edited in Lists)
                {
                    var existing = _document.StateLists
                        .FirstOrDefault(list => list.StateListId == edited.StateListId);

                    if (existing == null)
                        _document.StateLists.Add(edited.Clone());
                    else
                        existing.CopyFrom(edited);
                }
            }
        }
    }
}
