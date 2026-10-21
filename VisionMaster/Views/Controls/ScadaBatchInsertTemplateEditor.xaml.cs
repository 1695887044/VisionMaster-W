using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Controls;
using Prism.Mvvm;
using VisionMaster.Scada;
using VisionMaster.Services;

namespace VisionMaster.Views.Controls
{
    /// <summary>
    /// 「批量插入模板」的参数编辑器。字段刻意保持扁平：模板、数量、起始序号、列数、
    /// 间距、起点。确认时算一次展开计划；失败原因留在 StatusText 上，调用方据此决定
    /// 是重新打开还是放弃。
    /// </summary>
    public partial class ScadaBatchInsertTemplateEditor : UserControl
    {
        private readonly ScadaBatchInsertTemplateViewModel _viewModel;

        public ScadaBatchInsertTemplateEditor(
            IScadaTemplateVariableResolver resolver,
            IReadOnlyList<ScadaTemplateInfo>? templates = null)
        {
            InitializeComponent();

            _viewModel = new ScadaBatchInsertTemplateViewModel(
                resolver,
                templates ?? ScadaTemplateStore.Shared.Templates);

            DataContext = _viewModel;
        }

        /// <summary>当前选中的模板名（插入撤销记录/诊断文案用）</summary>
        public string? SelectedTemplateName => _viewModel.SelectedTemplate?.Name;

        /// <summary>按当前表单内容生成展开计划；失败时把原因写进 StatusText 并返回 false。</summary>
        public bool TryBuildPlan(
            out ScadaTemplateExpansionPlan? plan,
            out ScadaTemplateExpansionOptions? options,
            out IReadOnlyList<ScadaTemplateExpansionError> errors,
            out IReadOnlyList<string> warnings)
            => _viewModel.TryBuildPlan(out plan, out options, out errors, out warnings);
    }

    /// <summary>
    /// 参数编辑器的一次性视图模型：没有跨窗口状态，也没有要挂到别处的命令。
    /// 数值字段一律存文本（用户打到一半不该被立刻纠正），解析推迟到 <see cref="TryBuildPlan"/>。
    /// </summary>
    public sealed class ScadaBatchInsertTemplateViewModel : BindableBase
    {
        private readonly IScadaTemplateVariableResolver _resolver;

        private ScadaTemplateInfo? _selectedTemplate;
        private string _countText = "10";
        private string _startIndexText = "0";
        private string _columnsText = "5";
        private string _gapXText = "20";
        private string _gapYText = "20";
        private string _originXText = "0";
        private string _originYText = "0";
        private string _statusText = "确认后会在当前画面按上面的参数生成实例；任何一个变量解析失败都不会插入。";

        public ScadaBatchInsertTemplateViewModel(
            IScadaTemplateVariableResolver resolver,
            IReadOnlyList<ScadaTemplateInfo> templates)
        {
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            Templates = templates ?? Array.Empty<ScadaTemplateInfo>();
            _selectedTemplate = Templates.FirstOrDefault();

            if (_selectedTemplate != null)
                _startIndexText = _selectedTemplate.ParameterIndex.ToString(CultureInfo.InvariantCulture);
        }

        public IReadOnlyList<ScadaTemplateInfo> Templates { get; }

        public ScadaTemplateInfo? SelectedTemplate
        {
            get => _selectedTemplate;
            set
            {
                if (!SetProperty(ref _selectedTemplate, value))
                    return;

                // 模板保存时选的下标作为默认起始序号：从 Control[3] 参数化的模板，
                // 默认就应该从 3 开始，而不是静默生成 Control[0]。
                StartIndexText = value?.ParameterIndex.ToString(CultureInfo.InvariantCulture) ?? "0";
                RaisePropertyChanged(nameof(TemplateSummary));
            }
        }

        public string TemplateSummary => SelectedTemplate == null
            ? "请选择模板"
            : $"{SelectedTemplate.Name} · {SelectedTemplate.ItemCount} 个图元";

        public string CountText
        {
            get => _countText;
            set => SetProperty(ref _countText, value ?? string.Empty);
        }

        public string StartIndexText
        {
            get => _startIndexText;
            set => SetProperty(ref _startIndexText, value ?? string.Empty);
        }

        public string ColumnsText
        {
            get => _columnsText;
            set => SetProperty(ref _columnsText, value ?? string.Empty);
        }

        public string GapXText
        {
            get => _gapXText;
            set => SetProperty(ref _gapXText, value ?? string.Empty);
        }

        public string GapYText
        {
            get => _gapYText;
            set => SetProperty(ref _gapYText, value ?? string.Empty);
        }

        public string OriginXText
        {
            get => _originXText;
            set => SetProperty(ref _originXText, value ?? string.Empty);
        }

        public string OriginYText
        {
            get => _originYText;
            set => SetProperty(ref _originYText, value ?? string.Empty);
        }

        public string StatusText
        {
            get => _statusText;
            private set => SetProperty(ref _statusText, value ?? string.Empty);
        }

        /// <summary>
        /// 解析表单 → 取模板负载 → 展开。所有失败都写成可读的中文，留在 StatusText 上。
        /// </summary>
        public bool TryBuildPlan(
            out ScadaTemplateExpansionPlan? plan,
            out ScadaTemplateExpansionOptions? options,
            out IReadOnlyList<ScadaTemplateExpansionError> errors,
            out IReadOnlyList<string> warnings)
        {
            plan = null;
            options = null;
            errors = Array.Empty<ScadaTemplateExpansionError>();
            warnings = Array.Empty<string>();

            if (!TryReadOptions(out var parsedOptions, out string? optionError))
            {
                StatusText = optionError!;
                return false;
            }

            if (SelectedTemplate == null)
            {
                StatusText = "请先选择一个模板";
                return false;
            }

            var payload = ScadaTemplateStore.Shared.GetPayload(SelectedTemplate.TemplateId);

            if (payload == null || payload.IsEmpty)
            {
                StatusText = "模板内容为空或已被删除，请重新选择";
                return false;
            }

            if (!ScadaTemplateExpansion.TryPlan(payload, parsedOptions, _resolver,
                    out plan, out errors, out warnings))
            {
                StatusText = FormatErrors(errors);
                return false;
            }

            options = parsedOptions;
            string summary = $"校验通过：{plan!.Count} 个实例，每个 {payload.Items.Count} 个图元。";

            if (warnings.Count > 0)
                summary += Environment.NewLine + string.Join(Environment.NewLine, warnings.Select(w => "• " + w));

            StatusText = summary;
            return true;
        }

        private bool TryReadOptions(out ScadaTemplateExpansionOptions? options, out string? error)
        {
            options = null;
            error = null;

            if (!TryInt(CountText, "生成数量", out int count, out error) || count < 1 || count > 500)
            {
                error = string.IsNullOrEmpty(error) ? "生成数量必须在 1 ~ 500 之间" : error;
                return false;
            }

            if (!TryInt(StartIndexText, "起始序号", out int start, out error) || start < 0)
            {
                error = string.IsNullOrEmpty(error) ? "起始序号不能为负数" : error;
                return false;
            }

            if (!TryInt(ColumnsText, "每行列数", out int columns, out error) || columns < 1)
            {
                error = string.IsNullOrEmpty(error) ? "每行列数必须大于 0" : error;
                return false;
            }

            if (!TryDouble(GapXText, "水平间距", out double gapX, out error) || gapX < 0)
            {
                error = string.IsNullOrEmpty(error) ? "水平间距不能为负数" : error;
                return false;
            }

            if (!TryDouble(GapYText, "垂直间距", out double gapY, out error) || gapY < 0)
            {
                error = string.IsNullOrEmpty(error) ? "垂直间距不能为负数" : error;
                return false;
            }

            if (!TryDouble(OriginXText, "起点 X", out double originX, out error))
                return false;

            if (!TryDouble(OriginYText, "起点 Y", out double originY, out error))
                return false;

            options = new ScadaTemplateExpansionOptions
            {
                Count = count,
                StartIndex = start,
                Columns = columns,
                GapX = gapX,
                GapY = gapY,
                OriginX = originX,
                OriginY = originY,
            };

            return true;
        }

        private static bool TryInt(string? text, string field, out int value, out string? error)
        {
            error = null;

            if (int.TryParse((text ?? string.Empty).Trim(), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out value))
                return true;

            error = $"{field}「{text}」不是有效整数";
            return false;
        }

        private static bool TryDouble(string? text, string field, out double value, out string? error)
        {
            error = null;

            if (double.TryParse((text ?? string.Empty).Trim(), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out value) && double.IsFinite(value))
                return true;

            error = $"{field}「{text}」不是有效数字";
            return false;
        }

        private static string FormatErrors(IReadOnlyList<ScadaTemplateExpansionError> errors)
        {
            if (errors.Count == 0)
                return "模板展开失败";

            var lines = errors.Take(8).Select(e =>
                $"实例 {e.InstanceIndex} / 图元「{e.ElementName}」 / {e.Reference}：{e.Reason}");

            string text = "模板校验失败，整批不会插入：" + Environment.NewLine
                          + string.Join(Environment.NewLine, lines);

            if (errors.Count > 8)
                text += Environment.NewLine + $"……另有 {errors.Count - 8} 条错误";

            return text;
        }
    }
}
