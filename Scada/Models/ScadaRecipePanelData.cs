using System;
using System.Collections.Generic;
using System.Globalization;

namespace VisionMaster.Scada
{
    /// <summary>
    /// 配方只读面板的<b>数据投影</b>（<c>Hmi.RecipePanel</c> 用）：把 <c>System.Recipe.*</c>
    /// 六条只读系统变量读成"可以直接画"的一份快照。
    ///
    /// <b>为什么放在领域层</b>
    /// ---------
    /// 同一份数据有两个消费者：WPF 控件（<c>RecipePanelElement</c>）与 Web 运行态
    /// （<c>ScadaWebHost</c> 的 <c>recipe</c> 消息）。两边各写一遍格式化，结果是
    /// "桌面端显示 <c>偏离 2</c>、Web 端显示 <c>Deviated 2</c>" 这种同一状态两种说法。
    /// 放进 <c>VM.Scada</c> 后，两者都只调这里；本层没有 WPF 类型，Web 也能直接用。
    ///
    /// <b>变量名是硬约定</b>
    /// ---------
    /// 名字由宿主投影（<c>RecipeVariableCatalog</c>）确定，面板按名字寻址——
    /// 这不是绕过绑定引擎，而是把"配方数据长什么样"收在一处：否则每个工程都要
    /// 记住 6 个变量名并手绑，改一个名字就整屏失效。
    /// </summary>
    public static class ScadaRecipePanelData
    {
        /// <summary>配方系统变量的名字（与宿主投影的契约，改这里等于改契约）</summary>
        public const string VarActive = "System.Recipe.Active";
        public const string VarName = "System.Recipe.Name";
        public const string VarVersion = "System.Recipe.Version";
        public const string VarEntryCount = "System.Recipe.EntryCount";
        public const string VarSummary = "System.Recipe.Summary";
        public const string VarStatus = "System.Recipe.Status";

        /// <summary>行数下限（少于 3 行连"配方名 + 两条参数"都放不下）</summary>
        public const int MinRows = 3;

        /// <summary>行数上限（再多就不是"看配方"而是"翻手册"）</summary>
        public const int MaxRowsLimit = 50;

        /// <summary>状态键：模板/客户端据此挑色（不做"文案包含偏离"这类字符串判断）</summary>
        public const string StatusKeyOk = "ok";
        public const string StatusKeyWarn = "warn";
        public const string StatusKeyDanger = "danger";
        public const string StatusKeyMuted = "muted";

        /// <summary>
        /// 读一次配方并摊成快照。<paramref name="values"/> 为 null（非运行态）时给出
        /// "未接入"文案，而不是一串 0 / 空配方。
        /// </summary>
        public static ScadaRecipePanelSnapshot Read(
            IScadaValueSource? values, bool showDetails, int maxRows)
        {
            if (values == null)
            {
                return new ScadaRecipePanelSnapshot(
                    string.Empty, string.Empty, StatusKeyMuted,
                    Array.Empty<ScadaRecipePanelLine>(),
                    "配方数据未接入（当前不在运行态）",
                    false);
            }

            bool known = TryReadBool(values, VarActive, out bool active);
            string name = ReadString(values, VarName);
            string status = ReadString(values, VarStatus);
            int version = ReadInt(values, VarVersion);
            int entryCount = ReadInt(values, VarEntryCount);
            string summary = ReadString(values, VarSummary);

            // 拿不到 Active = 方案里没有配方系统变量（旧宿主/精简宿主）：说"未接入"，
            // 而不是"未启用"——后者会被当成"方案里真没配方"。
            if (!known)
            {
                return new ScadaRecipePanelSnapshot(
                    string.Empty, string.Empty, StatusKeyMuted,
                    Array.Empty<ScadaRecipePanelLine>(),
                    "配方数据未接入（找不到 System.Recipe 变量）",
                    false);
            }

            bool dangling = string.Equals(status, "配方悬空", StringComparison.Ordinal);
            bool hasRecipe = active && !string.IsNullOrWhiteSpace(name);

            string header = dangling
                ? "配方已丢失"
                : hasRecipe
                    ? (version > 0 ? $"{name} · v{version}" : name)
                    : "未应用配方";

            var rows = showDetails
                ? BuildLines(summary, maxRows)
                : Array.Empty<ScadaRecipePanelLine>();

            string hint = !showDetails
                ? string.Empty
                : rows.Count > 0
                    ? string.Empty
                    : hasRecipe
                        ? (entryCount > 0 ? "配方参数加载中…" : "这份配方没有参数")
                        : "还没有应用任何配方";

            return new ScadaRecipePanelSnapshot(
                header, status, PickStatusKey(status, dangling), rows, hint, dangling);
        }

        /// <summary>状态文案 → 颜色档（<c>dangling</c> 优先：配方丢了比"偏离"更严重）</summary>
        public static string PickStatusKey(string? status, bool dangling)
        {
            if (dangling) return StatusKeyDanger;

            var text = status ?? string.Empty;

            if (text.StartsWith("偏离", StringComparison.Ordinal)) return StatusKeyWarn;
            if (text == "已生效") return StatusKeyOk;

            return StatusKeyMuted;
        }

        /// <summary>
        /// 把 <c>System.Recipe.Summary</c> 拆成行（每行一项，形如 <c>检测.Threshold = 0.75</c>）。
        ///
        /// 为什么按行拆而不是另开一条"条目列表"变量：系统变量只能是标量，
        /// 而 Summary 本来就是"给人看的一份参数清单"，按行拆是最小契约。
        /// 截断时保留一行汇总（"… 共 N 项"），让操作员知道下面还有东西。
        /// </summary>
        public static IReadOnlyList<ScadaRecipePanelLine> BuildLines(string? summary, int maxRows)
        {
            if (string.IsNullOrWhiteSpace(summary))
                return Array.Empty<ScadaRecipePanelLine>();

            var all = summary!.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            var limit = Math.Clamp(maxRows, MinRows, MaxRowsLimit);

            if (all.Length <= limit)
            {
                var same = new ScadaRecipePanelLine[all.Length];
                for (int i = 0; i < all.Length; i++)
                    same[i] = new ScadaRecipePanelLine(all[i]);
                return same;
            }

            var shown = new ScadaRecipePanelLine[limit];
            for (int i = 0; i < limit - 1; i++)
                shown[i] = new ScadaRecipePanelLine(all[i]);

            shown[limit - 1] = new ScadaRecipePanelLine($"… 共 {all.Length} 项", isTruncated: true);
            return shown;
        }

        private static bool TryReadBool(IScadaValueSource values, string name, out bool value)
        {
            value = false;

            if (!TryResolve(values, name, out var handle) || handle!.Value is null)
                return false;

            switch (handle.Value)
            {
                case bool flag:
                    value = flag;
                    return true;
                case string text when bool.TryParse(text.Trim(), out bool parsed):
                    value = parsed;
                    return true;
                default:
                    return false;
            }
        }

        private static int ReadInt(IScadaValueSource values, string name)
        {
            if (!TryResolve(values, name, out var handle) || handle!.Value is null)
                return 0;

            try
            {
                return Convert.ToInt32(handle.Value, CultureInfo.InvariantCulture);
            }
            catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
            {
                return 0;
            }
        }

        private static string ReadString(IScadaValueSource values, string name)
        {
            if (!TryResolve(values, name, out var handle) || handle!.Value is null)
                return string.Empty;

            return handle.Value as string ?? Convert.ToString(handle.Value, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        /// <summary>
        /// 按名字解析。身份用 <see cref="Guid.Empty"/>（"只能按名字找"）：系统变量的 Id 是
        /// 确定性推导的，复制一份到这里就多了一个会漂移的常量；名字才是宿主写进目录的那份契约。
        /// </summary>
        private static bool TryResolve(IScadaValueSource values, string name, out IScadaValueHandle? handle)
        {
            handle = null;

            if (string.IsNullOrWhiteSpace(name))
                return false;

            return values.TryResolve(Guid.Empty, name, out handle) && handle != null;
        }
    }

    /// <summary>配方面板的一份快照（不可变；WPF 控件与 Web 消息共用）。</summary>
    public sealed class ScadaRecipePanelSnapshot
    {
        public ScadaRecipePanelSnapshot(
            string header,
            string status,
            string statusKey,
            IReadOnlyList<ScadaRecipePanelLine> rows,
            string hint,
            bool dangling)
        {
            Header = header ?? string.Empty;
            Status = status ?? string.Empty;
            StatusKey = statusKey ?? ScadaRecipePanelData.StatusKeyMuted;
            Rows = rows ?? Array.Empty<ScadaRecipePanelLine>();
            Hint = hint ?? string.Empty;
            IsDangling = dangling;
        }

        /// <summary>主行：配方名 · 版本（无配方时"未应用配方"/"配方已丢失"）</summary>
        public string Header { get; }

        /// <summary>状态文案（已生效 / 偏离 N / 未应用 / 未启用 / 配方悬空）</summary>
        public string Status { get; }

        /// <summary>状态键：ok / warn / danger / muted</summary>
        public string StatusKey { get; }

        /// <summary>明细行（已按上限截断）</summary>
        public IReadOnlyList<ScadaRecipePanelLine> Rows { get; }

        /// <summary>空态 / 未接入提示（空串 = 不显示）</summary>
        public string Hint { get; }

        /// <summary>生效指针指向的配方已不存在</summary>
        public bool IsDangling { get; }
    }

    /// <summary>
    /// 明细里的一行：一段已经格式化好的文本（+ 是否是被截断时补的汇总行）。
    /// 模板/Web 靠 <see cref="IsTruncated"/> 把汇总行画成弱化色。
    /// </summary>
    public sealed class ScadaRecipePanelLine
    {
        public ScadaRecipePanelLine(string text, bool isTruncated = false)
        {
            Text = text ?? string.Empty;
            IsTruncated = isTruncated;
        }

        public string Text { get; }

        public bool IsTruncated { get; }
    }
}
