using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Prism.Mvvm;
using VisionMaster.Scada;

namespace VisionMaster.ViewModels
{
    /// <summary>
    /// 匹配模式下拉项：<see cref="Value"/> 是落盘的字符串常量（<see cref="ScadaStateList.ModeExact"/> 等），
    /// <see cref="Name"/> 是上屏文字。
    ///
    /// 为什么值是字符串而不是枚举：<c>.vms</c> 里存的就是字符串（见 <see cref="ScadaStateList.Mode"/>），
    /// 这里再包一层枚举只会多一次"枚举 ↔ 字符串"的翻译，翻译漏了就是静默回落到精确值。
    /// </summary>
    public sealed class StateListModeOption
    {
        public StateListModeOption(string value, string name)
        {
            Value = value;
            Name = name;
        }

        /// <summary>落盘值（<see cref="ScadaStateList.ModeExact"/> 等四个常量之一）</summary>
        public string Value { get; }

        /// <summary>上屏文字（精确值 / 范围 / 位 / 位号）</summary>
        public string Name { get; }
    }

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
                {
                    RaisePropertyChanged(nameof(ReferenceText));
                    RaiseModeProperties();
                }
            }
        }

        /// <summary>
        /// 匹配模式下拉项（固定四项，不随选中列表变化）。
        ///
        /// 顺序按"从最常用到最专门"排：精确值是默认档，位/位号是现场排查时才切过去看。
        /// </summary>
        public IReadOnlyList<StateListModeOption> ModeOptions { get; } = new[]
        {
            new StateListModeOption(ScadaStateList.ModeExact, "精确值"),
            new StateListModeOption(ScadaStateList.ModeRange, "范围"),
            new StateListModeOption(ScadaStateList.ModeBit, "位（0-1）"),
            new StateListModeOption(ScadaStateList.ModeBitIndex, "位号（0-31）"),
        };

        /// <summary>
        /// 当前列表的匹配模式（落盘字符串）。
        /// 写入直接改副本的 <see cref="ScadaStateList.Mode"/>：归一（大小写/坏值→精确值）由模型做，
        /// 这里不重复一遍规则；写完把四个只读投影一起抬一遍，XAML 的列显隐与提示文案才会跟着变。
        /// </summary>
        public string Mode
        {
            get => _selectedList?.Mode ?? ScadaStateList.ModeExact;
            set
            {
                if (_selectedList == null || value == null)
                    return;

                if (string.Equals(_selectedList.Mode, value, StringComparison.Ordinal))
                    return;

                _selectedList.Mode = value;
                RaiseModeProperties();
            }
        }

        /// <summary>范围模式：条目看「最小~最大」，值列不参与（XAML 据此显隐两列）</summary>
        public bool IsRangeMode => ScadaStateList.ParseMode(Mode) == ScadaStateMatchMode.Range;

        /// <summary>位号模式：值列填位号 0~31</summary>
        public bool IsBitIndexMode => ScadaStateList.ParseMode(Mode) == ScadaStateMatchMode.BitIndex;

        /// <summary>精确值模式：与 v5 完全一致的行为</summary>
        public bool IsExactMode => ScadaStateList.ParseMode(Mode) == ScadaStateMatchMode.Exact;

        /// <summary>一句话说明当前模式怎么匹配（直接上屏，不在这里拼前缀/标点）</summary>
        public string ModeHint
        {
            get
            {
                switch (ScadaStateList.ParseMode(Mode))
                {
                    case ScadaStateMatchMode.Range:
                        return "条目按“最小~最大”闭区间匹配；“值”列不参与";

                    case ScadaStateMatchMode.Bit:
                        return "变量非 0 匹配“值=1”的条目，为 0 匹配“值=0”的条目";

                    case ScadaStateMatchMode.BitIndex:
                        // 补一句负数语义：变量为负时按补码看位（-1 全 1，会命中所有已配位号），
                        // 这是位运算的正常语义，但不写出来容易被当成"配错了"去查。
                        return "“值”列填位号 0~31；该位为 1 时命中（变量为负时按补码看位）";

                    default:
                        return "条目按“值”精确匹配（与 v5 一致）";
                }
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

        /// <summary>
        /// 模式相关的五个只读投影一起抬（换列表、改模式都走这里）。
        /// 分两处各写一遍必然漏一个——漏掉 <see cref="IsRangeMode"/> 就是"切了范围模式但列不出现"。
        /// </summary>
        private void RaiseModeProperties()
        {
            RaisePropertyChanged(nameof(Mode));
            RaisePropertyChanged(nameof(IsRangeMode));
            RaisePropertyChanged(nameof(IsBitIndexMode));
            RaisePropertyChanged(nameof(IsExactMode));
            RaisePropertyChanged(nameof(ModeHint));
        }
    }
}
