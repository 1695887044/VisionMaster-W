using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Controls;
using Prism.Commands;
using Prism.Mvvm;
using VisionMaster.Scada;

namespace VisionMaster.Views.Controls
{
    /// <summary>
    /// 存模板时的参数化编辑器：数组下标 / 名称与界面文字的多行查找替换。
    /// 生命周期只有一次弹窗（构造 → 编辑 → TryApply → 丢弃），所以没有单独命令。
    /// </summary>
    public partial class ScadaTemplateParameterizeEditor : UserControl
    {
        private readonly ScadaTemplateParameterizeViewModel _viewModel;

        public ScadaTemplateParameterizeEditor(int? detectedIndex = null)
        {
            InitializeComponent();

            _viewModel = new ScadaTemplateParameterizeViewModel(detectedIndex);
            DataContext = _viewModel;
        }

        public bool TryApply(
            ScadaClipboardPayload payload,
            out int changed,
            out IReadOnlyList<string> changes,
            out int parameterIndex,
            out IReadOnlyList<string> arrayRoots,
            out string? error)
            => _viewModel.TryApply(payload, out changed, out changes, out parameterIndex, out arrayRoots, out error);
    }

    /// <summary>一条替换规则的编辑态；Find/Replace 都是文本，空 Find 的行会被忽略。</summary>
    public sealed class ScadaTemplateReplaceRuleViewModel : BindableBase
    {
        private string _find = string.Empty;
        private string _replace = "{i}";

        public string Find
        {
            get => _find;
            set => SetProperty(ref _find, value ?? string.Empty);
        }

        public string Replace
        {
            get => _replace;
            set => SetProperty(ref _replace, value ?? string.Empty);
        }
    }

    /// <summary>参数化编辑器的字段模型；数值字段存文本，解析推迟到 TryApply。</summary>
    public sealed class ScadaTemplateParameterizeViewModel : BindableBase
    {
        private string _indexText;
        private string _statusText =
            "数组下标、名称替换、界面文字替换可以只用一种，也可以同时用；都留空=按原样保存模板。";

        public ScadaTemplateParameterizeViewModel(int? detectedIndex)
        {
            _indexText = detectedIndex is >= 0
                ? detectedIndex.Value.ToString(CultureInfo.InvariantCulture)
                : string.Empty;

            NameRules.Add(new ScadaTemplateReplaceRuleViewModel());
            TextRules.Add(new ScadaTemplateReplaceRuleViewModel());

            AddNameRuleCommand = new DelegateCommand(
                () => NameRules.Add(new ScadaTemplateReplaceRuleViewModel()));
            AddTextRuleCommand = new DelegateCommand(
                () => TextRules.Add(new ScadaTemplateReplaceRuleViewModel()));
            RemoveNameRuleCommand = new DelegateCommand<object?>(RemoveNameRule);
            RemoveTextRuleCommand = new DelegateCommand<object?>(RemoveTextRule);
        }

        public string IndexText
        {
            get => _indexText;
            set => SetProperty(ref _indexText, value ?? string.Empty);
        }

        public ObservableCollection<ScadaTemplateReplaceRuleViewModel> NameRules { get; } = new();

        public ObservableCollection<ScadaTemplateReplaceRuleViewModel> TextRules { get; } = new();

        public DelegateCommand AddNameRuleCommand { get; }

        public DelegateCommand AddTextRuleCommand { get; }

        public DelegateCommand<object?> RemoveNameRuleCommand { get; }

        public DelegateCommand<object?> RemoveTextRuleCommand { get; }

        public string StatusText
        {
            get => _statusText;
            private set => SetProperty(ref _statusText, value ?? string.Empty);
        }

        public bool TryApply(
            ScadaClipboardPayload payload,
            out int changed,
            out IReadOnlyList<string> changes,
            out int parameterIndex,
            out IReadOnlyList<string> arrayRoots,
            out string? error)
        {
            changed = 0;
            changes = Array.Empty<string>();
            parameterIndex = 0;
            arrayRoots = Array.Empty<string>();
            error = null;

            var changeList = new List<string>();
            int index = -1;

            if (!string.IsNullOrWhiteSpace(_indexText))
            {
                if (!int.TryParse(_indexText.Trim(), NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out index)
                    || index < 0)
                {
                    error = "数组下标必须是非负整数；留空表示不按数组下标参数化";
                    StatusText = error;
                    return false;
                }

                if (!ScadaTemplateParameterizer.TryParameterize(
                        payload, index, out int indexChanged, out var roots, out error))
                {
                    StatusText = error ?? "数组下标参数化失败";
                    return false;
                }

                changed += indexChanged;
                parameterIndex = index;
                arrayRoots = roots;

                if (indexChanged > 0)
                    changeList.Add($"数组下标 {index} → {{i}}（{indexChanged} 条引用）");
            }

            var nameRules = NameRules
                .Where(r => !string.IsNullOrWhiteSpace(r.Find))
                .Select(r => new ScadaTemplateReplaceRule(r.Find.Trim(), r.Replace))
                .ToList();

            var textRules = TextRules
                .Where(r => !string.IsNullOrWhiteSpace(r.Find))
                .Select(r => new ScadaTemplateReplaceRule(r.Find.Trim(), r.Replace))
                .ToList();

            if (nameRules.Count > 0 || textRules.Count > 0)
            {
                if (!ScadaTemplateParameterizer.TryReplace(
                        payload, nameRules, textRules,
                        out int replaceChanged, out var replaceChanges, out error))
                {
                    StatusText = error ?? "名称/文字替换失败";
                    return false;
                }

                changed += replaceChanged;

                if (replaceChanges.Count > 0)
                    changeList.AddRange(replaceChanges);
            }

            changes = changeList;

            if (changed == 0)
            {
                StatusText = "没有产生任何替换；如果这是有意的，请清空所有查找条件后确认，按原样保存。";
                return true;
            }

            // changes 只保留前 20 条做预览，剩余条数必须按 changed 算，不能按 changes.Count 算。
            string preview = string.Join(Environment.NewLine, changes.Take(8).Select(c => "• " + c));
            StatusText = $"将改写 {changed} 处：{Environment.NewLine}{preview}"
                         + (changed > 8 ? $"{Environment.NewLine}……另有 {changed - 8} 条" : string.Empty);

            return true;
        }

        private void RemoveNameRule(object? parameter)
        {
            if (parameter is ScadaTemplateReplaceRuleViewModel rule)
                NameRules.Remove(rule);
        }

        private void RemoveTextRule(object? parameter)
        {
            if (parameter is ScadaTemplateReplaceRuleViewModel rule)
                TextRules.Remove(rule);
        }
    }
}
