using System;
using System.ComponentModel;
using System.Text;
using Core.Interfaces;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VisionMaster.Binding;
using VisionMaster.Models;

namespace VisionMaster.Services
{
    /// <summary>
    /// <see cref="IRecipeVariableSource"/> 的宿主实现：把 <see cref="RecipeService"/> 的
    /// 当前状态摊成一份快照，供 <see cref="RecipeVariableCatalog"/> 投影成
    /// <c>System.Recipe.*</c> 只读系统变量。
    ///
    /// 为什么单独一层而不是让目录直接引用 RecipeService：
    /// 目录在 Core（不能反向依赖 VisionMaster），而配方服务在宿主层。
    /// 本类只做"读 + 摊平"，不写任何配方状态。
    ///
    /// 刷新时机：
    /// - <see cref="RecipeService.StateChanged"/>：应用/清除/捕获/增删参数/改名/删除；
    /// - 工作区换方案：<see cref="IWorkspaceManager.CurrentSolution"/> 变化。
    /// 参数值被外部（流程画布/属性面板）改动不会触发本类——偏离数要等下一次
    /// 配方状态变化才刷新，这是 v1 的已知边界。
    /// </summary>
    public sealed class RecipeVariableSource : IRecipeVariableSource, IDisposable
    {
        /// <summary>摘要最多显示多少项（超出只留一行"共 N 项"；文本图元放不下无限行）</summary>
        public const int MaxSummaryEntries = 50;

        private readonly RecipeService _service;
        private readonly IWorkspaceManager _workspace;

        public RecipeVariableSource(RecipeService service, IWorkspaceManager workspace)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));

            _service.StateChanged += OnChanged;

            if (_workspace is INotifyPropertyChanged inpc)
                inpc.PropertyChanged += OnWorkspacePropertyChanged;
        }

        public event EventHandler? Changed;

        public RecipeVariableSnapshot GetRecipeVariableSnapshot()
        {
            var solution = _service.Solution;
            var active = _service.ActiveRecipe;
            var recipes = _service.Recipes;
            var deviation = _service.ComputeDeviation();

            bool dangling = solution?.ActiveRecipeId != null && active == null;

            string status = dangling
                ? "配方悬空"
                : active != null
                    ? (deviation.Deviated > 0 ? $"偏离 {deviation.Deviated}" : "已生效")
                    : recipes.Count > 0
                        ? "未应用"
                        : "未启用";

            return new RecipeVariableSnapshot(
                active != null,
                active?.Name ?? string.Empty,
                active?.Code ?? string.Empty,
                active?.ContentVersion ?? 0,
                active != null ? deviation.Deviated : 0,
                active?.Entries?.Count ?? 0,
                BuildSummary(active),
                status);
        }

        /// <summary>
        /// 参数摘要：每行一项，形如 <c>检测.Threshold = 0.75</c>。
        /// 用 <see cref="RecipeEntry.DisplayTarget"/> / <see cref="RecipeEntry.DisplayKey"/> /
        /// <see cref="RecipeEntry.DisplayValue"/>，与配方管理器表格同一套展示口径。
        /// </summary>
        private static string BuildSummary(RecipeDefinition? recipe)
        {
            var entries = recipe?.Entries;
            if (entries == null || entries.Count == 0)
                return string.Empty;

            var sb = new StringBuilder();
            int shown = 0;

            foreach (var entry in entries)
            {
                if (entry == null) continue;

                if (shown >= MaxSummaryEntries)
                {
                    sb.Append("… 共 ").Append(entries.Count).Append(" 项");
                    break;
                }

                if (shown > 0)
                    sb.AppendLine();

                sb.Append(entry.DisplayTarget);

                if (entry.Kind == RecipeTargetKind.StepInput && !string.IsNullOrWhiteSpace(entry.Key))
                    sb.Append('.').Append(entry.DisplayKey);

                sb.Append(" = ").Append(FormatValue(entry));
                shown++;
            }

            return sb.ToString();
        }

        /// <summary>
        /// 值文本：字符串去掉 JSON 引号（HMI 上写 <c>Mode = "Circle"</c> 很刺眼），
        /// 其它类型按 JSON 字面量（数字/布尔/数组原样）。与配方管理器表格的
        /// <see cref="RecipeEntry.DisplayValue"/> 只差"字符串是否带引号"这一处。
        /// </summary>
        private static string FormatValue(RecipeEntry entry)
        {
            if (entry.ValueIsNull)
                return "null";

            var token = entry.Value;
            if (token == null)
                return "（缺失）";

            return token.Type == JTokenType.String
                ? token.Value<string>() ?? string.Empty
                : token.ToString(Formatting.None);
        }

        private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);

        private void OnWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(IWorkspaceManager.CurrentSolution)
                || e.PropertyName == nameof(IWorkspaceManager.CurrentFlow))
            {
                OnChanged();
            }
        }

        public void Dispose()
        {
            _service.StateChanged -= OnChanged;

            if (_workspace is INotifyPropertyChanged inpc)
                inpc.PropertyChanged -= OnWorkspacePropertyChanged;
        }
    }
}
