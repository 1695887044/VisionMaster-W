using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Controls;
using Prism.Mvvm;
using VisionMaster.Scada;

namespace VisionMaster.Views.Controls
{
    /// <summary>
    /// 拖入参数化模板时的选项：实例序号、严格替换开关、要替换的数组根。
    /// </summary>
    public partial class ScadaTemplateInsertOptionsEditor : UserControl
    {
        private readonly ScadaTemplateInsertOptionsViewModel _viewModel;

        public ScadaTemplateInsertOptionsEditor(int defaultIndex, IReadOnlyList<string> arrayRoots)
        {
            InitializeComponent();

            _viewModel = new ScadaTemplateInsertOptionsViewModel(defaultIndex, arrayRoots);
            DataContext = _viewModel;
        }

        public bool TryApply(
            out int index,
            out bool strictReplacement,
            out IReadOnlySet<string>? selectedArrayRoots,
            out string? error)
            => _viewModel.TryApply(out index, out strictReplacement, out selectedArrayRoots, out error);
    }

    public sealed class ScadaTemplateInsertOptionsViewModel : BindableBase
    {
        private string _indexText;
        private bool _strictReplacement;

        public ScadaTemplateInsertOptionsViewModel(int defaultIndex, IReadOnlyList<string> arrayRoots)
        {
            _indexText = defaultIndex.ToString(CultureInfo.InvariantCulture);

            foreach (string root in arrayRoots ?? Array.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(root))
                    ArrayRoots.Add(new ScadaArrayRootOption(root));
            }
        }

        public string IndexText
        {
            get => _indexText;
            set => SetProperty(ref _indexText, value ?? string.Empty);
        }

        public ObservableCollection<ScadaArrayRootOption> ArrayRoots { get; } = new();

        public bool HasArrayRoots => ArrayRoots.Count > 0;

        public bool StrictReplacement
        {
            get => _strictReplacement;
            set
            {
                if (!SetProperty(ref _strictReplacement, value))
                    return;

                RaisePropertyChanged(nameof(StrictRootsVisible));
            }
        }

        public bool StrictRootsVisible => StrictReplacement && HasArrayRoots;

        public string HintText => HasArrayRoots
            ? "不勾严格替换：所有数组根都跟着实例序号走。勾上后只替换勾选的数组根，未勾选的保持模板原始下标。"
            : "这个模板没有数组根清单；只能做普通序号替换。";

        public bool TryApply(
            out int index,
            out bool strictReplacement,
            out IReadOnlySet<string>? selectedArrayRoots,
            out string? error)
        {
            index = 0;
            strictReplacement = StrictReplacement;
            selectedArrayRoots = null;
            error = null;

            if (!int.TryParse(IndexText.Trim(), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out index) || index < 0)
            {
                error = "实例序号必须是非负整数";
                return false;
            }

            if (StrictReplacement)
            {
                if (ArrayRoots.Count == 0)
                {
                    error = "这个模板没有数组根清单，不能使用严格替换；请重新存一次模板";
                    return false;
                }

                selectedArrayRoots = ArrayRoots
                    .Where(r => r.IsSelected)
                    .Select(r => r.Name)
                    .ToHashSet(StringComparer.Ordinal);
            }

            return true;
        }
    }
}
