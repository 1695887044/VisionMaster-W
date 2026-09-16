using Core.Interfaces;
using System;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Plugin.ExcelExport
{
    /// <summary>导出触发方式。</summary>
    public enum ExportPolicy
    {
        /// <summary>仅手动：流程执行时不导出，只在配置面板点"立即导出"</summary>
        Manual,
        /// <summary>触发端口：输入端口"导出触发"为 true 的那一次执行才导出（适合放在流程末尾，由收工信号触发）</summary>
        OnTrigger,
        /// <summary>每次执行：本步骤每次执行都导出（适合"一个班次只跑一次"的报表流程；高频循环流程慎用——每次都要重写整本报表）</summary>
        EachRun
    }

    /// <summary>
    /// Excel 报表导出插件：把 CSV 账本加工成"带图片超链接的 xlsx"给人看。
    ///
    /// 为什么是"事后导出"而不是"边测边写 xlsx"：
    /// - 产线上写 xlsx 要整份重写、还得防 Excel 占用，代价高且没意义（机器不看报表）；
    /// - 人要看的时候再导出，报表就是"当时那份账"的快照，文件小、能筛选排序、点一下看高清原图。
    ///
    /// 报表长什么样（对应 CSV 记录插件写出的账本）：
    ///   第 1 行 = 表头（冻结 + 自动筛选）
    ///   第 2 行起 = 每件一行的数据，其中"图片路径"列不再显示一长串路径，
    ///              而是蓝色下划线的短文件名 —— 点击即用系统看图软件打开磁盘上的原图。
    ///
    /// 失败契约：报表是给"人"看的附属产物，默认**不**阻断生产（BlockOnFailure=false），
    /// 导出失败只写 WARN 日志；需要"报表导出不了就停线"的场合可勾选阻断。
    /// </summary>
    [Display(
        Name = "Excel报表",
        GroupName = "数据处理",
        Description = "把 CSV 账本导出成带图片超链接的 Excel 报表：表头冻结可筛选，点文件名看高清原图",
        ShortName = "\uf1c3")]
    public class ExcelExportPlugin : VisionPluginBase, IPluginCustomViewProvider
    {
        #region 端口

        /// <summary>导出触发（为 true 时导出；仅"触发端口"方式生效。非必填：不连线不算参数缺失）。</summary>
        public InputPort<bool> TriggerPort { get; } =
            new InputPort<bool>("导出触发", false, "为 true 时导出报表（导出方式=触发端口 时生效）") { IsRequired = false };

        /// <summary>本次导出的 xlsx 完整路径（失败时为空串）。</summary>
        public OutputPort<string> ReportPath { get; } = new OutputPort<string>("报表路径", "本次导出的 xlsx 完整路径");

        /// <summary>本次写入报表的数据行数（不含表头）。</summary>
        public OutputPort<int> ExportedRows { get; } = new OutputPort<int>("导出行数", "本次写入报表的数据行数（不含表头）");

        #endregion

        #region 内嵌配置（随 .vms 持久化）

        private string _sourcePathTemplate = @"D:\Records\{源步骤}\{yyyy-MM}\{yyyy-MM-dd}.csv";
        /// <summary>源账本路径模板（占位符：{源步骤} {yyyy-MM-dd} {yyyy-MM} {yyyy} …）。</summary>
        [StepConfig]
        public string SourcePathTemplate
        {
            get => _sourcePathTemplate;
            set { if (SetProperty(ref _sourcePathTemplate, value ?? "")) OnPropertyChanged(nameof(SourcePathPreview)); }
        }

        private string _sourceStepName = "";
        /// <summary>源账本所属的步骤名（即"CSV记录"步骤的实例名，供模板里的 {源步骤} 展开）。</summary>
        [StepConfig]
        public string SourceStepName
        {
            get => _sourceStepName;
            set { if (SetProperty(ref _sourceStepName, value ?? "")) OnPropertyChanged(nameof(SourcePathPreview)); }
        }

        private string _outputDirectoryTemplate = @"D:\Records\{StepName}\{yyyy-MM}\报表\";
        /// <summary>报表输出目录模板（{StepName} 是"本步骤"的名字）。</summary>
        [StepConfig]
        public string OutputDirectoryTemplate
        {
            get => _outputDirectoryTemplate;
            set { if (SetProperty(ref _outputDirectoryTemplate, value ?? @"D:\Records\")) OnPropertyChanged(nameof(OutputPathPreview)); }
        }

        private string _reportFileNamePattern = "{yyyy-MM-dd}_报表.xlsx";
        /// <summary>报表文件名模板（含 .xlsx；不加也行，会自动补）。</summary>
        [StepConfig]
        public string ReportFileNamePattern
        {
            get => _reportFileNamePattern;
            set { if (SetProperty(ref _reportFileNamePattern, string.IsNullOrWhiteSpace(value) ? "{yyyy-MM-dd}_报表.xlsx" : value)) OnPropertyChanged(nameof(OutputPathPreview)); }
        }

        private string _linkColumn = "图片路径";
        /// <summary>要做成超链接的列名（默认"图片路径"，与 CSV 记录插件的内置字段列同名）。</summary>
        [StepConfig]
        public string LinkColumn
        {
            get => _linkColumn;
            set => SetProperty(ref _linkColumn, value ?? "");
        }

        private bool _freezeHeader = true;
        /// <summary>冻结首行（滚动时表头不跑）。</summary>
        [StepConfig]
        public bool FreezeHeader
        {
            get => _freezeHeader;
            set => SetProperty(ref _freezeHeader, value);
        }

        private bool _autoFilter = true;
        /// <summary>首行自动筛选（可按结果/日期筛）。</summary>
        [StepConfig]
        public bool AutoFilter
        {
            get => _autoFilter;
            set => SetProperty(ref _autoFilter, value);
        }

        private bool _onlyRowsWithImage = false;
        /// <summary>只导出图片列有有效路径的行（抽检看 NG 图时用）。</summary>
        [StepConfig]
        public bool OnlyRowsWithImage
        {
            get => _onlyRowsWithImage;
            set => SetProperty(ref _onlyRowsWithImage, value);
        }

        private ExportPolicy _exportPolicy = ExportPolicy.Manual;
        /// <summary>导出方式（仅手动 / 触发端口 / 每次执行）。</summary>
        [StepConfig]
        public ExportPolicy ExportPolicy
        {
            get => _exportPolicy;
            set => SetProperty(ref _exportPolicy, value);
        }

        private bool _openAfterExport = false;
        /// <summary>导出成功后用系统默认程序打开报表（仅手动导出时建议勾选）。</summary>
        [StepConfig]
        public bool OpenAfterExport
        {
            get => _openAfterExport;
            set => SetProperty(ref _openAfterExport, value);
        }

        private bool _blockOnFailure = false;
        /// <summary>导出失败是否让本步骤失败阻断流程（默认 false：报表不阻断生产）。</summary>
        [StepConfig]
        public bool BlockOnFailure
        {
            get => _blockOnFailure;
            set => SetProperty(ref _blockOnFailure, value);
        }

        #endregion

        #region 配置生命周期 / 视图

        public object GetConfigView(IStepConfigData stepData)
        {
            Initialize(stepData);
            return new ExcelExportView { DataContext = this };
        }

        /// <summary>
        /// 本步骤的有效名字（用于替换 {StepName}）：
        /// 运行期用引擎注入的 InstanceName（与 CSV 记录插件同一套命名规则，路径才对得上）；
        /// 配置期实例由界面新建、引擎还没命名（InstanceName 仍是"未赋值"），退回步骤自身名字让预览可用。
        /// </summary>
        public string EffectiveStepName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(InstanceName) && InstanceName != "未赋值") return InstanceName;
                return StepData?.StepName ?? "";
            }
        }

        /// <summary>源步骤名（未填则退回本步骤名，让模板"开箱可用"）。</summary>
        public string EffectiveSourceStepName =>
            string.IsNullOrWhiteSpace(SourceStepName) ? EffectiveStepName : SourceStepName;

        /// <summary>源账本解析后的实际路径（配置面板实时预览，防手抖打错路径）。</summary>
        public string SourcePathPreview =>
            ExcelReportExporter.ExpandTemplate(SourcePathTemplate, DateTime.Now, EffectiveStepName, EffectiveSourceStepName);

        /// <summary>报表输出解析后的实际路径（配置面板实时预览）。</summary>
        public string OutputPathPreview
        {
            get
            {
                string dir = ExcelReportExporter.ExpandTemplate(OutputDirectoryTemplate, DateTime.Now, EffectiveStepName, EffectiveSourceStepName);
                string name = ExcelReportExporter.ExpandTemplate(ReportFileNamePattern, DateTime.Now, EffectiveStepName, EffectiveSourceStepName);
                if (string.IsNullOrWhiteSpace(name)) name = DateTime.Now.ToString("yyyy-MM-dd") + "_报表.xlsx";
                if (!name.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)) name += ".xlsx";
                try { return Path.GetFullPath(Path.Combine(string.IsNullOrEmpty(dir) ? "." : dir, name)); }
                catch { return Path.Combine(dir ?? "", name); }
            }
        }

        /// <summary>界面加载/确认后刷新预览（InstanceName 由引擎注入，构造时还拿不到）。</summary>
        public void RefreshPreview()
        {
            OnPropertyChanged(nameof(EffectiveStepName));
            OnPropertyChanged(nameof(EffectiveSourceStepName));
            OnPropertyChanged(nameof(SourcePathPreview));
            OnPropertyChanged(nameof(OutputPathPreview));
        }

        /// <summary>导出方式枚举值（供视图下拉 ItemsSource）。</summary>
        public Array ExportPolicies => Enum.GetValues(typeof(ExportPolicy));

        private bool _isExporting;
        /// <summary>是否正在导出（手动导出期间置灰按钮）。</summary>
        public bool IsExporting
        {
            get => _isExporting;
            private set => SetProperty(ref _isExporting, value);
        }

        private string _lastResult = "";
        /// <summary>最近一次手动导出的结果文本（供视图显示）。</summary>
        public string LastResult
        {
            get => _lastResult;
            private set => SetProperty(ref _lastResult, value);
        }

        #endregion

        #region 执行

        public override void RunAlgorithm(IExecutionContext context)
        {
            bool shouldExport = ExportPolicy == ExportPolicy.EachRun
                                || (ExportPolicy == ExportPolicy.OnTrigger && TriggerPort.GetActualValue() is bool b && b);
            if (!shouldExport)
                return; // 静默通过：不导出也不写日志（高频流程里刷日志会淹没真正的问题）

            var result = RunExport(context);
            if (result.Success)
            {
                ReportPath.Value = result.OutputPath;
                ExportedRows.Value = result.Rows;
                context?.Logger?.Info("Excel报表：" + result.Message + " → " + result.OutputPath);
                if (OpenAfterExport) TryOpen(result.OutputPath, context);
            }
            else if (BlockOnFailure)
            {
                Success.Value = false;
                ErrorMessage.Value = "Excel报表：" + result.Message;
            }
            else
            {
                context?.Logger?.Warn("Excel报表：" + result.Message);
            }
        }

        /// <summary>
        /// 手动导出（配置面板按钮）：放后台线程跑，避免几万行的读+写+改包把界面卡住。
        /// 绑定引擎会自动把非 UI 线程的属性变更调度回 UI 线程（标量属性安全；集合不行，故这里不发集合通知）。
        /// </summary>
        public void ExportNow()
        {
            if (IsExporting) return;
            IsExporting = true;
            LastResult = "正在导出…";
            Task.Run(() =>
            {
                try
                {
                    var result = RunExport(null);
                    var sb = new System.Text.StringBuilder();
                    sb.Append(result.Success ? "✔ " : "✘ ").Append(result.Message);
                    if (result.Success) sb.Append("\n→ ").Append(result.OutputPath);
                    LastResult = sb.ToString();
                    if (result.Success && OpenAfterExport) TryOpen(result.OutputPath, null);
                }
                finally
                {
                    IsExporting = false;
                }
            });
        }

        /// <summary>组参数 → 交给导出器（本方法内不做任何界面/日志决策）。</summary>
        private ExcelExportResult RunExport(IExecutionContext context)
        {
            var now = DateTime.Now;
            var request = new ExcelExportRequest
            {
                SourceCsvPath = SourcePathPreview,
                OutputXlsxPath = OutputPathPreview,
                LinkColumn = LinkColumn,
                FreezeHeader = FreezeHeader,
                AutoFilter = AutoFilter,
                OnlyRowsWithImage = OnlyRowsWithImage
            };
            var result = ExcelReportExporter.Export(request);
            // 手动导出时也回写输出端口，方便用户在下游/日志里看到最近一次报表路径
            if (result.Success)
            {
                ReportPath.Value = result.OutputPath;
                ExportedRows.Value = result.Rows;
            }
            else if (context != null)
            {
                context.Logger?.Warn("Excel报表导出失败：" + result.Message);
            }
            return result;
        }

        /// <summary>用系统默认程序打开报表（失败不影响导出结果）。</summary>
        private static void TryOpen(string path, IExecutionContext context)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                context?.Logger?.Warn("Excel报表：自动打开失败（报表已生成，可手动打开）：" + ex.Message);
            }
        }

        #endregion
    }
}
