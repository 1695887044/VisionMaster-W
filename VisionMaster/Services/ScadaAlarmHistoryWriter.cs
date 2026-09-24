using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using VisionMaster.Models;
using VisionMaster.Scada;
using VisionMaster.Scada.Controls;

namespace VisionMaster.Services
{
    /// <summary>
    /// 报警历史落盘：把运行态的每一次报警态迁移追加成 CSV 的一行，按天滚动。
    ///
    /// 为什么落盘归宿主、不归引擎
    /// ---------
    /// <see cref="ScadaAlarmEngine"/> 的类注释把这条边界写死了：文件 IO 不该拖慢值变化那条热路径。
    /// 引擎只发事实（哪条报警、到了哪个态），"记到哪儿、什么时候记、记不成怎么办"是宿主决策——
    /// 与"图元事件由宿主接、由分发器执行"同一分工。
    ///
    /// 为什么是<b>事件流水</b>而不是"一条报警一行"
    /// ---------
    /// 记录对象在引擎里是<b>原地更新</b>的（激活时建、确认/恢复时填时间戳），
    /// 所以"一条报警一行"只能等它了结才写得出来——正在报警的那条永远查不到，
    /// 而"现在到底在报什么"恰恰是现场最想查的。改成每次态迁移追加一行之后，
    /// 文件本身就是可重放的：按 <c>激活时间</c> 分组，就能还原出任意一条报警从生到死的全过程。
    /// 面板上"一行 = 一次报警"的聚合是<b>视图</b>，由 S11-c 的历史面板做。
    ///
    /// 为什么每次追加都"开文件 → 写一行 → 关文件"
    /// ---------
    /// 现场一定会有人在软件运行中双击这个 CSV 用 Excel 打开看。长开一个 StreamWriter
    /// 会让文件被进程独占（或者反过来——Excel 的锁让写入静默失败），而报警是低频事件
    /// （分钟级），一次 open/close 的开销完全不值得为它换一份常驻句柄。
    /// 本项目已经踩过一次"日志文件被占用导致启动崩溃"的坑，这里不再重复。
    ///
    /// 写不成怎么办
    /// ---------
    /// 磁盘满、文件被 Excel 锁住、目录没权限——一律<b>不抛</b>，返回 <c>false</c>，
    /// 并只在"成功 ↔ 失败"翻转的那一次上报一句诊断。理由：报警系统因为写不了历史文件而崩掉，
    /// 比丢几行历史严重得多；而磁盘故障时每行都报一次，日志会被刷满，真正的原因反而被埋掉。
    ///
    /// 编码
    /// ---------
    /// UTF-8 <b>带 BOM</b>。不带 BOM 的 UTF-8 CSV 用 Excel 打开中文必乱码——
    /// 这是"导出给现场看"这类功能最常见的投诉，而修复成本只是三个字节。
    /// BOM 由本类自己写在文件头（不依赖 StreamWriter 的前导码行为，那取决于内部实现细节）。
    ///
    /// 为什么<b>按天滚动还不够、还得有保留策略</b>
    /// ---------
    /// 与 <see cref="ScadaAuditWriter"/> 同一口径：按天滚动只保证"单个文件不会涨到打不开"，
    /// 目录本身仍会一直涨。报警虽比操作低频，但一台连跑几年的机器攒下上千份文件之后，
    /// 备份与排查都成了负担。所以跨天时顺手清掉超出保留期的旧文件。
    ///
    /// <b>与审计的区别只有一条</b>：报警历史里装着"这条报警当时是什么状态"，
    /// 现场偶尔要翻很久以前的同类故障作对比，所以保留期<b>默认给到一年</b>；
    /// 要留更久就构造时传大一点（传 0 或负数 = 永不清理）。
    /// </summary>
    public sealed class ScadaAlarmHistoryWriter
    {
        /// <summary>
        /// 默认保留天数（含今天）。报警历史是现场复盘故障的素材，比操作审计翻得更久，
        /// 所以默认一年；磁盘占用是几十兆的量级，不至于成为负担。
        ///
        /// 常量从配置模型的默认值取，保证"配置默认"与"构造兜底"永远是同一个数
        /// （理由见 <see cref="ScadaAuditWriter.DefaultRetentionDays"/>）。
        /// </summary>
        public const int DefaultRetentionDays = AppConfigModel.DefaultAlarmHistoryRetentionDays;

        /// <summary>文件名模板。用<b>本地</b>日期：现场是按本地时间查"昨天那次报警"的</summary>
        private const string FileNameFormat = "AlarmHistory-{0:yyyy-MM-dd}.csv";

        /// <summary>文件名前缀/后缀。清理时靠它俩 + 日期格式三重确认"这是本类建的文件"</summary>
        private const string FileNamePrefix = "AlarmHistory-";

        private const string FileNameSuffix = ".csv";

        /// <summary>文件名里那一段日期的格式（与 <see cref="FileNameFormat"/> 必须一致）</summary>
        private const string FileNameDateFormat = "yyyy-MM-dd";

        /// <summary>行内时间戳格式（不含毫秒：报警查询到秒足够，毫秒只会让表格变宽）</summary>
        private const string TimeFormat = "yyyy-MM-dd HH:mm:ss";

        /// <summary>
        /// CSV 表头。列序 = 现场看报警时的阅读顺序：先说是什么、再说多严重、最后是时间线。
        ///
        /// 组态补充信息（报警组 / 故障原因 / 解决措施 / 附加信息）一律<b>追加在末尾</b>：
        /// 插在中间会把既有列的下标整体挪位，而现场已经有人按列号取数做报表了。
        /// 追加在末尾，旧文件不重写、新文件多四列，Excel 按表头读两边都不受影响。
        /// </summary>
        private static readonly string[] Header =
        {
            "激活时间", "报警名称", "报警文本", "严重度", "报警类型",
            "变量名", "条件描述", "触发值", "状态",
            "确认时间", "恢复时间", "清除时间", "持续时长",
            "报警组", "故障原因", "解决措施", "附加信息",
        };

        /// <summary>CSV 里需要转义的字符（含分隔符、引号、换行）</summary>
        private static readonly char[] MustQuote = { ',', '"', '\r', '\n' };

        /// <summary>UTF-8 前导码（BOM）</summary>
        private static readonly byte[] Bom = { 0xEF, 0xBB, 0xBF };

        /// <summary>写文件用无 BOM 编码：BOM 由本类手工写一次，避免每次追加都插一遍</summary>
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        private readonly string _directory;
        private readonly Action<ScadaDiagnosticLevel, string>? _onDiagnostic;
        private readonly int _retentionDays;

        /// <summary>
        /// 保留期的<b>配置读取口</b>（宿主注入：读软件级配置里的保留天数）。
        /// 每次现读，于是"改完立刻生效"是结构上保证的（与审计落盘端同一条口径）。
        /// </summary>
        private readonly Func<int>? _retentionProvider;

        /// <summary>落盘当前是否处于故障态（只用来给诊断上报去重，不参与任何判定）</summary>
        private bool _faulted;

        /// <summary>上一次清理过的那一天。跨天才再清一次（理由见类注释）</summary>
        private DateTime? _lastPurgedDay;

        /// <param name="directory">历史文件所在目录（不存在会自动建）</param>
        /// <param name="onDiagnostic">故障上报口；只在"成功 ↔ 失败"翻转时回调一次，可能来自任意线程</param>
        /// <param name="retentionDays">
        /// 保留天数（含今天）。传 <b>0 或负数 = 不清理</b>；默认 <see cref="DefaultRetentionDays"/>。
        /// 仅在没传 <paramref name="retentionProvider"/> 时生效。
        /// </param>
        /// <param name="retentionProvider">
        /// 保留天的配置读取口；传了它就以它为准（<paramref name="retentionDays"/> 只作兜底）。
        /// 宿主应当注入它，让「系统 → 系统参数设置」改完立刻生效。
        /// </param>
        public ScadaAlarmHistoryWriter(string directory,
                                       Action<ScadaDiagnosticLevel, string>? onDiagnostic = null,
                                       int retentionDays = DefaultRetentionDays,
                                       Func<int>? retentionProvider = null)
        {
            _directory = string.IsNullOrWhiteSpace(directory)
                ? throw new ArgumentException("报警历史目录不能为空", nameof(directory))
                : directory;
            _onDiagnostic = onDiagnostic;
            _retentionDays = retentionDays;
            _retentionProvider = retentionProvider;
        }

        /// <summary>
        /// 保留天数；<c>&lt;= 0</c> 表示永不清理（见构造函数）。
        /// 注入了配置读取口时以配置为准——每次现读，改完立刻生效。
        /// </summary>
        public int RetentionDays => _retentionProvider?.Invoke() ?? _retentionDays;

        /// <summary>默认目录：软件目录下的 <c>Alarms</c>。
        /// 与 <c>Logs</c> 同口径（跟着软件走，不污染系统目录），但刻意<b>分开</b>——
        /// 报警历史是资产，运行日志是可以随时清掉的过程记录，混在一起早晚被一起删掉。</summary>
        public static string DefaultDirectory => Path.Combine(AppContext.BaseDirectory, "Alarms");

        /// <summary>落盘当前是否故障中（断言与状态栏可读）</summary>
        public bool IsFaulted => _faulted;

        /// <summary>最近一次失败的原因；正常时为 null</summary>
        public string? LastError { get; private set; }

        /// <summary>追加一条报警记录（按当前本地日期滚动）。返回是否写成功</summary>
        public bool Append(ScadaAlarmRecord record) => Append(record, DateTime.Now);

        /// <inheritdoc cref="Append(ScadaAlarmRecord)"/>
        /// <param name="nowLocal">当前本地时刻。断言里传假日期才能测按天滚动</param>
        public bool Append(ScadaAlarmRecord record, DateTime nowLocal)
        {
            if (record == null)
                return false;

            try
            {
                Directory.CreateDirectory(_directory);

                var path = Path.Combine(
                    _directory,
                    string.Format(CultureInfo.InvariantCulture, FileNameFormat, nowLocal));

                // FileShare.Read：运行中允许别人（Excel）读这份历史，但不许别人同时写。
                // 追加模式下文件位置天然在末尾，不存在覆盖既有行的可能。
                using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);

                // 空文件 = 本类刚建出来（或上次崩在写表头之前）→ 补 BOM 与表头。
                // 判"空"必须在写任何字节之前做，所以这两步合在一起、顺序不能换。
                //
                // leaveOpen: true 不是可选项：StreamWriter 默认会连底层流一起关掉，
                // 表头那个 writer 一出作用域就把 stream 关了，下面写行时必然抛 ObjectDisposedException——
                // 表现是"报警历史一行都写不进去，但目录和空文件建出来了"。
                if (stream.Length == 0)
                {
                    stream.Write(Bom, 0, Bom.Length);
                    using var head = new StreamWriter(stream, Utf8NoBom, 1024, leaveOpen: true) { AutoFlush = true };
                    head.WriteLine(string.Join(",", Header));
                }

                using (var writer = new StreamWriter(stream, Utf8NoBom, 1024, leaveOpen: true) { AutoFlush = true })
                    writer.WriteLine(BuildRow(record));

                ReportRecovered();

                // 清理放在写成功之后：反过来的话，写不成时（磁盘满）先删掉旧历史，
                // 等于把"唯一能作证的那份"赔进去了。清理本身绝不抛，也不会改变本次写入的结果。
                PurgeIfNewDay(nowLocal);
                return true;
            }
            catch (Exception ex)
            {
                // 任何 IO 异常都在这里收住：落盘失败不该让一次报警从报警系统里消失，
                // 更不该把异常抛回引擎的值变化回调（那会顺着变量订阅链炸到后台轮询线程上）。
                ReportFault(ex);
                return false;
            }
        }

        /// <summary>
        /// 清掉超出保留期的按天文件，返回删掉的份数。<b>不抛</b>，也不碰
        /// <see cref="IsFaulted"/>——"旧文件删不掉"（多半是现场正用 Excel 开着某天的历史在看）
        /// 与"新记录写不进去"是两件事，混成一档会让日志里出现一句误导人的"报警历史落盘失败"。
        ///
        /// 只删<b>本类自己建的文件</b>：文件名必须同时满足"AlarmHistory- 开头"、".csv 结尾"、
        /// 中间那段能按 <c>yyyy-MM-dd</c> 解析出日期。用户从面板导出的那份走的是同一个命名规则
        /// （见 <see cref="SuggestFileName"/>），所以它在 <c>Alarms</c> 目录里同样受保留期约束——
        /// 这是刻意的：导出的快照与流水账内容同源，留着两份只是让目录更快涨满。
        /// 其余任何名字的文件一个都不碰。
        /// </summary>
        /// <param name="nowLocal">当前本地时刻（断言里传假日期就能测保留期）</param>
        public int PurgeExpired(DateTime nowLocal)
        {
            // 0/负数 = 永不清理（现场要求留档）。这里必须先判：否则下面的截止日会算到"今天之后"，
            // 把今天这份也一起删掉——那是刚写进去的历史。
            //
            // 读一次存进局部变量：保留期现在来自配置（每次现读），同一次清理里两次读之间
            // 若有人正好在改配置，会出现"按 0 判不清理、却按 365 算截止日"这种半新半旧的组合。
            var retention = RetentionDays;
            if (retention <= 0)
                return 0;

            try
            {
                if (!Directory.Exists(_directory))
                    return 0;

                // 保留"最近 N 天（含今天）"：截止日 = 今天 - (N - 1)，早于它的才删。
                var cutoff = nowLocal.Date.AddDays(1 - retention);
                var removed = 0;
                var failed = 0;
                string? firstError = null;

                foreach (var path in Directory.GetFiles(_directory, FileNamePrefix + "*" + FileNameSuffix))
                {
                    if (!TryReadFileDate(Path.GetFileName(path), out var day) || day >= cutoff)
                        continue;

                    try
                    {
                        File.Delete(path);
                        removed++;
                    }
                    catch (Exception ex)
                    {
                        // 单个文件删不掉（被 Excel 占着 / 只读）不影响其余的：接着往下删。
                        failed++;
                        firstError ??= ex.Message;
                    }
                }

                if (failed > 0)
                {
                    _onDiagnostic?.Invoke(
                        ScadaDiagnosticLevel.Warning,
                        $"[报警] 有 {failed} 份过期报警历史没能删除（多半正被 Excel 打开），本次跳过；"
                        + $"下次跨天写入时会再试一次：{firstError}");
                }

                return removed;
            }
            catch (Exception ex)
            {
                _onDiagnostic?.Invoke(
                    ScadaDiagnosticLevel.Warning,
                    $"[报警] 清理过期报警历史失败（不影响新的记录写入）：{ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// 跨天时清一次（同一天里反复调用只做第一次）。<c>_lastPurgedDay</c> 在清理<b>之前</b>记下：
        /// 某个文件删不掉时不该每次追加都重扫一遍目录、重报一句警告。
        /// </summary>
        private void PurgeIfNewDay(DateTime nowLocal)
        {
            if (RetentionDays <= 0)
                return;

            var day = nowLocal.Date;
            if (_lastPurgedDay == day)
                return;

            _lastPurgedDay = day;
            PurgeExpired(nowLocal);
        }

        /// <summary>
        /// 从文件名里读回日期。<b>解析不出来就当它不是本类建的文件</b>（返回 false，不删）——
        /// 宁可漏清一个，也不能误删一个。
        /// </summary>
        private static bool TryReadFileDate(string fileName, out DateTime day)
        {
            day = default;

            if (fileName.Length <= FileNamePrefix.Length + FileNameSuffix.Length)
                return false;

            if (!fileName.StartsWith(FileNamePrefix, StringComparison.OrdinalIgnoreCase)
                || !fileName.EndsWith(FileNameSuffix, StringComparison.OrdinalIgnoreCase))
                return false;

            var core = fileName.Substring(
                FileNamePrefix.Length,
                fileName.Length - FileNamePrefix.Length - FileNameSuffix.Length);

            return DateTime.TryParseExact(
                core, FileNameDateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out day);
        }

        /// <summary>
        /// 把一批记录<b>一次性</b>导出到指定路径（覆盖写）。
        ///
        /// 与 <see cref="Append"/> 的分工
        /// ---------
        /// <c>Append</c> 是运行中的流水账：按天滚动、只追加、永不覆盖，路径由本类自己定；
        /// 本方法是"用户点了导出按钮"的一次性快照：路径由用户在保存对话框里挑，
        /// 所以既不管按天滚动，也不管目标文件里原来有什么（覆盖）。
        ///
        /// 为什么和 <c>Append</c> 一样不抛
        /// ---------
        /// 调用方是 WPF 命令。异常从命令里漏出去会顺着 <c>Dispatcher</c> 变成未处理异常弹框，
        /// 而"路径被占用/没有权限"这种错，现场只需要一句提示。所以失败一律返回 <c>false</c>，
        /// 并把原因从 <paramref name="error"/> 交出去——由面板决定弹提示还是记日志。
        /// </summary>
        /// <param name="filePath">目标文件全路径（所在目录不存在会自动建）</param>
        /// <param name="records">要导出的记录；为 null 时只写表头</param>
        public static bool Export(string filePath, IEnumerable<ScadaAlarmRecord>? records)
            => Export(filePath, records, out _);

        /// <inheritdoc cref="Export(string, IEnumerable{ScadaAlarmRecord})"/>
        /// <param name="error">失败原因；成功时为 null</param>
        public static bool Export(string filePath, IEnumerable<ScadaAlarmRecord>? records, out string? error)
        {
            error = null;

            if (string.IsNullOrWhiteSpace(filePath))
            {
                error = "导出路径为空";
                return false;
            }

            try
            {
                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                // FileShare.Read：导出过程中允许别人读（比如 Excel 正开着上一次导出的那份），
                // 但不许别人同时写——否则两边各写一半，出来的是谁也不是的文件。
                using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read);

                // BOM 与表头与流水账文件逐字节一致：导出的这份可以直接和按天的历史文件拼在一起看。
                stream.Write(Bom, 0, Bom.Length);

                using var writer = new StreamWriter(stream, Utf8NoBom, 1024, leaveOpen: true) { AutoFlush = true };

                writer.WriteLine(string.Join(",", Header));

                if (records != null)
                {
                    foreach (var record in records)
                    {
                        if (record != null)
                            writer.WriteLine(BuildRow(record));
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>导出的建议文件名。与按天滚动的流水账同名规则——放回 <c>Alarms</c> 目录时不会打架。</summary>
        public static string SuggestFileName(DateTime nowLocal)
            => string.Format(CultureInfo.InvariantCulture, FileNameFormat, nowLocal);

        /// <summary>
        /// 把一条记录拍成 CSV 的一行。状态列写的是<b>迁移之后</b>的态，
        /// 所以顺着文件往下读就是这条报警的完整时间线。
        /// </summary>
        private static string BuildRow(ScadaAlarmRecord record)
        {
            string[] fields =
            {
                FormatTime(record.ActivatedAtLocal),
                Escape(record.Name),
                Escape(record.Message),
                record.Severity.DisplayName(),
                record.Kind.DisplayName(),
                Escape(record.VariableName),
                Escape(record.ConditionText),
                Escape(record.TriggerValue),
                record.State.DisplayName(),
                FormatTime(record.AcknowledgedAtLocal),
                FormatTime(record.RecoveredAtLocal),
                FormatTime(record.ClearedAtLocal),

                // 持续时长只在<b>已恢复</b>之后才写。还在报警的那条若把"到此刻为止"写进文件，
                // 这个数字就永远冻在写入那一刻——过一天再翻这份历史，看到的是一句假话。
                // 空着才是诚实的：没恢复 = 时长还没有答案。
                record.RecoveredAtLocal == null ? string.Empty : Escape(record.DurationText),

                // 组态补充信息（列序与 Header 末尾四列严格对齐）。
                // 这些字段在报警激活时就从定义抄成了快照（见 ScadaAlarmRecord 的构造函数），
                // 所以事后改了组态也不会让历史里旧记录的"怎么修"跟着变——那正是快照存在的理由。
                Escape(record.AlarmGroup),
                Escape(record.Cause),
                Escape(record.Remedy),
                Escape(record.ExtraInfo),
            };

            return string.Join(",", fields);
        }

        /// <summary>
        /// RFC 4180 转义：含逗号/引号/换行的字段用双引号包起来，内部引号翻倍。
        /// 报警文本是用户手输的（"温度超限,请检查"这种带逗号的句子现场太常见），
        /// 不转义就会把一行劈成两列，而列错位在 Excel 里表现为"数据莫名其妙对不上"。
        /// </summary>
        private static string Escape(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value.IndexOfAny(MustQuote) >= 0
                ? "\"" + value.Replace("\"", "\"\"") + "\""
                : value;
        }

        private static string FormatTime(DateTime value)
            => value.ToString(TimeFormat, CultureInfo.InvariantCulture);

        private static string FormatTime(DateTime? value)
            => value?.ToString(TimeFormat, CultureInfo.InvariantCulture) ?? string.Empty;

        private void ReportFault(Exception ex)
        {
            LastError = ex.Message;

            if (_faulted)
                return;   // 已经在故障态：不再重复上报，避免磁盘故障时把日志刷满

            _faulted = true;
            _onDiagnostic?.Invoke(
                ScadaDiagnosticLevel.Error,
                $"[报警] 报警历史落盘失败，记录只留在内存（报警本身不受影响）：{ex.Message}");
        }

        private void ReportRecovered()
        {
            LastError = null;

            if (!_faulted)
                return;

            _faulted = false;
            _onDiagnostic?.Invoke(ScadaDiagnosticLevel.Warning, "[报警] 报警历史落盘已恢复正常");
        }
    }
}
