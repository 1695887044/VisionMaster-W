using System;
using System.Globalization;
using System.IO;
using System.Text;
using VisionMaster.Models;
using VisionMaster.Scada.Controls;

namespace VisionMaster.Services
{
    /// <summary>
    /// 一条操作记录的<b>结果档</b>。
    ///
    /// 为什么把"没执行"和"失败"分成两档
    /// ---------
    /// 它们对现场是两件事：<b>未执行</b>是配置没对（没选变量、没选画面、本版本不认识这个动作），
    /// 去属性面板上补一下就完了，软件没坏；<b>失败</b>是这次操作真没成（变量改名了、值转不过、
    /// 设备离线、动作抛异常），要有人去查。合成一档"没成"，用户就得把这四条路各试一遍。
    /// （与 <see cref="ScadaActionDispatcher"/> 里"配置问题记 Warn、真失败记 Error"同一口径。）
    ///
    /// 为什么"被拒绝"要单独一档、不能并进"失败"
    /// ---------
    /// 权限不足<b>不是故障</b>，是产品设计如此。它在审计里回答的是另一个问题——
    /// 不是"这个按钮好不好使"，而是"这个人有没有碰过这个按钮"。并进"失败"会让
    /// "现场偶发故障"和"操作员越权尝试"看起来是同一件事。
    /// </summary>
    public enum ScadaAuditOutcome
    {
        /// <summary>动作真跑成了</summary>
        Success,

        /// <summary>没跑：配置没对（没选变量/画面、本版本不认识这个动作）</summary>
        Skipped,

        /// <summary>跑了但没成：变量找不到、值转不过、设备侧拒绝、动作抛异常</summary>
        Failed,

        /// <summary>压根没让跑：权限不够（S12 的闸门拦下的）</summary>
        Denied,
    }

    /// <summary>
    /// <see cref="ScadaAuditOutcome"/> 的中文名。落进 CSV 的就是这几个词，
    /// 与运行日志里那几行的措辞同源，用户才能把"日志里那条"和"审计表里那条"对上号。
    /// </summary>
    public static class ScadaAuditOutcomeExtensions
    {
        /// <summary>人话结果名。取值超出枚举范围时回"未知"，不抛</summary>
        public static string DisplayName(this ScadaAuditOutcome outcome) => outcome switch
        {
            ScadaAuditOutcome.Success => "成功",
            ScadaAuditOutcome.Skipped => "未执行",
            ScadaAuditOutcome.Failed => "失败",
            ScadaAuditOutcome.Denied => "被拒绝",
            _ => "未知",
        };
    }

    /// <summary>
    /// 一条待落盘的操作记录（一次动作执行，或一次被拦下的越权操作）。
    ///
    /// 为什么<b>不带时间戳</b>
    /// ---------
    /// 时间由 <see cref="ScadaAuditWriter"/> 在真正落笔的那一刻盖（见其 <c>Append</c> 的
    /// <c>nowLocal</c> 参数）。让调用方自己盖，就会出现"记录造好了、隔了几毫秒才写"这种偏差，
    /// 而审计要回答的恰恰是"这件事发生在什么时候"——这个时刻只能是落笔那一刻。
    /// 顺带的好处：断言里传一个假日期就能测按天滚动，不必去改系统时钟。
    ///
    /// 为什么是<b>一次动作一行</b>、而不是"一次点击一行"
    /// ---------
    /// 一次点击可以配一串动作（写变量 + 记日志 + 跳画面），而它们<b>各自的成败是独立的</b>：
    /// 可能前两条成了、第三条没成。攒成一行就只能写一个笼统的结果，
    /// 而现场要问的是"哪一条没成、为什么"。一行一条，文件本身就是执行流水，
    /// 按"时间 + 操作者 + 对象"分组就能还原出那一次点击的全貌（与
    /// <see cref="ScadaAlarmHistoryWriter"/> 的"事件流水"同一思路）。
    /// </summary>
    public sealed class ScadaAuditEntry
    {
        /// <param name="actor">操作者显示名。<b>承诺不为空</b>（取不到时调用方落"未登录"）</param>
        /// <param name="subject">出事的对象名（图元名或画面名）</param>
        /// <param name="eventText">发生了什么事件的人话名（"按下""松开"…）</param>
        /// <param name="actionText">执行了什么动作的摘要（<see cref="ScadaAction.Describe"/> 的产物）</param>
        /// <param name="outcome">结果档</param>
        /// <param name="detail">
        /// 说明：失败时是原因（可直接展示给操作员的中文），成功时是这次操作的<b>净效果</b>
        /// （写变量那条会写"变量：旧值 → 新值"）。没有可说的就留 null。
        /// </param>
        public ScadaAuditEntry(
            string? actor,
            string? subject,
            string? eventText,
            string? actionText,
            ScadaAuditOutcome outcome,
            string? detail = null)
        {
            Actor = string.IsNullOrWhiteSpace(actor) ? "未登录" : actor.Trim();
            Subject = string.IsNullOrWhiteSpace(subject) ? "未命名对象" : subject.Trim();
            EventText = eventText ?? string.Empty;
            ActionText = actionText ?? string.Empty;
            Outcome = outcome;
            Detail = detail;
        }

        /// <summary>操作者显示名（谁）</summary>
        public string Actor { get; }

        /// <summary>出事的对象名（对谁）</summary>
        public string Subject { get; }

        /// <summary>事件人话名（怎么触发的）</summary>
        public string EventText { get; }

        /// <summary>动作摘要（干了什么）</summary>
        public string ActionText { get; }

        /// <summary>结果档（成没成）</summary>
        public ScadaAuditOutcome Outcome { get; }

        /// <summary>说明：失败原因，或成功时的净效果（"启动：0 → 1"）；没有可说的时为 null</summary>
        public string? Detail { get; }
    }

    /// <summary>
    /// 操作审计落盘：把运行态的每一次动作执行、每一次越权拦截追加成 CSV 的一行，按天滚动。
    ///
    /// 为什么要有这份文件（运行日志里不是已经有一行了吗）
    /// ---------
    /// 运行日志是<b>过程记录</b>：它会按大小滚动、会被现场随手清掉、混着通讯与流程的各种噪音。
    /// 审计是<b>凭证</b>：出了问题要回答"谁在什么时候按了哪个按钮、值从多少改到多少"，
    /// 这份答案不能被日志滚动冲掉，也不该逼着人去几万行日志里捞。
    /// 所以它与运行日志<b>记同样的事、但分开存</b>——与"报警历史不并进 Logs"同一个理由
    /// （见 <see cref="ScadaAlarmHistoryWriter"/> 的类注释）。
    ///
    /// 为什么落盘归这一层、不归分发器自己写文件
    /// ---------
    /// 分发器管的是"动作怎么执行"（产品规则），"记到哪个文件、写不成怎么办"是宿主决策。
    /// 这与报警的分工完全一致：引擎发事实、宿主记文件。分发器只交出
    /// <see cref="ScadaAuditEntry"/> 这个纯数据，落盘细节一概不知道。
    ///
    /// 时间戳为什么带毫秒（与报警历史刻意不同）
    /// ---------
    /// 报警历史是"同一个对象的状态迁移"，前后两条至少隔几秒，秒足够。
    /// 审计是"人的操作"，连点两下、或者一次点击里三条动作，都挤在同一秒里；
    /// 秒精度一旦有人在 Excel 里排个序、筛一下，先后就丢了。毫秒让每一行自足。
    ///
    /// 为什么每次追加都"开文件 → 写一行 → 关文件"
    /// ---------
    /// 与 <see cref="ScadaAlarmHistoryWriter"/> 逐条同理由：现场一定会有人在软件运行中
    /// 双击这个 CSV 用 Excel 打开看。长开一个 StreamWriter 会让文件被进程独占
    /// （或者反过来——Excel 的锁让写入静默失败），而操作审计是低频事件，
    /// 一次 open/close 的开销完全不值得为它换一份常驻句柄。
    ///
    /// 写不成怎么办
    /// ---------
    /// 磁盘满、文件被 Excel 锁住、目录没权限——一律<b>不抛</b>，返回 <c>false</c>，
    /// 并只在"成功 ↔ 失败"翻转的那一次上报一句诊断。理由：<b>审计写不成不能反过来
    /// 让操作失败</b>——磁盘故障时把异常抛回动作执行链，表现是"点了按钮程序崩了"，
    /// 比丢几行审计严重得多；而每行都报一次会把日志刷满，真正的原因反而被埋掉。
    ///
    /// 编码
    /// ---------
    /// UTF-8 <b>带 BOM</b>，与报警历史同一口径：不带 BOM 的 UTF-8 CSV 用 Excel 打开中文必乱码。
    ///
    /// 为什么<b>按天滚动还不够、还得有保留策略</b>
    /// ---------
    /// 按天滚动只解决了"单个文件不会涨到打不开"，没解决"目录会一直涨"。
    /// 这台机器是要连跑几年的：一天一份文件、一年三百多份，从不清理——
    /// 最后不是"审计写不进去"（磁盘满了），就是"备份要拷一整天"。
    /// 所以每次跨到新的一天时顺手清掉超出保留期的旧文件（见 <see cref="PurgeExpired"/>）。
    ///
    /// 为什么是<b>跨天那一刻清</b>、而不是每次追加都清
    /// ---------
    /// 每次追加都扫一遍目录，等于给"点一下按钮"叠上一次目录枚举——审计是低频事件，
    /// 但这个代价换不来任何东西：需要清的文件一天只多一份，跨天清一次刚好。
    /// 进程刚起来时 <c>_lastPurgedDay</c> 是空的，所以当天第一次写入就会清一次，
    /// 现场"攒了一年没开过机"的情况也能在启动后第一次操作时收掉。
    ///
    /// <b>线程</b>：不锁。调用方只有两处，都在 UI 线程上（事件来自鼠标与渲染，
    /// 越权拦截也在同一条线上，见 <see cref="ScadaActionDispatcher"/> 的线程注释）。
    /// 将来若要在后台线程上写（比如流程引擎也来记一笔），必须先在这里补锁——
    /// 两条线程同时以 <c>FileMode.Append</c> 打开同一个文件，出来的是谁也不是的行。
    /// </summary>
    public sealed class ScadaAuditWriter
    {
        /// <summary>
        /// 默认保留天数（含今天）。三个月是"够查一次季度复盘、又不至于让目录无限涨"的折中；
        /// 真要留一年，改软件级配置里的保留期即可（见 <see cref="AppConfigModel.AuditRetentionDays"/>）。
        ///
        /// 常量从配置模型的默认值取，保证"配置默认"与"构造兜底"永远是同一个数——
        /// 两处各写一个 90，早晚改一处忘一处，表现是"新建的机器留 90 天、
        /// 老机器升级后留 180 天"，谁也说不清哪个才是本意。
        /// </summary>
        public const int DefaultRetentionDays = AppConfigModel.DefaultAuditRetentionDays;

        /// <summary>文件名模板。用<b>本地</b>日期：现场是按本地时间查"昨天下午谁动过"的</summary>
        private const string FileNameFormat = "Audit-{0:yyyy-MM-dd}.csv";

        /// <summary>文件名前缀/后缀。清理时靠它俩 + 日期格式三重确认"这是本类建的文件"</summary>
        private const string FileNamePrefix = "Audit-";

        private const string FileNameSuffix = ".csv";

        /// <summary>文件名里那一段日期的格式（与 <see cref="FileNameFormat"/> 必须一致）</summary>
        private const string FileNameDateFormat = "yyyy-MM-dd";

        /// <summary>行内时间戳格式。带毫秒的理由见类注释</summary>
        private const string TimeFormat = "yyyy-MM-dd HH:mm:ss.fff";

        /// <summary>CSV 表头。列序 = 现场追责时的提问顺序：谁 → 对谁 → 怎么触发的 → 干了什么 → 成没成 → 为什么</summary>
        private static readonly string[] Header =
        {
            "时间", "操作者", "对象", "事件", "动作", "结果", "说明",
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
        /// 与 <see cref="ScadaAccessPolicy"/> 的空闲超时同一条口径——每次现读，
        /// 于是"改完立刻生效"是结构上保证的，不必重启软件，也不靠谁记得把新值推过来。
        /// </summary>
        private readonly Func<int>? _retentionProvider;

        /// <summary>落盘当前是否处于故障态（只用来给诊断上报去重，不参与任何判定）</summary>
        private bool _faulted;

        /// <summary>上一次清理过的那一天。跨天才再清一次（理由见类注释）</summary>
        private DateTime? _lastPurgedDay;

        /// <param name="directory">审计文件所在目录（不存在会自动建）</param>
        /// <param name="onDiagnostic">故障上报口；只在"成功 ↔ 失败"翻转时回调一次</param>
        /// <param name="retentionDays">
        /// 保留天数（含今天）。传 <b>0 或负数 = 不清理</b>——留给"法规要求留档、磁盘也够"的现场；
        /// 默认 <see cref="DefaultRetentionDays"/>。仅在没传 <paramref name="retentionProvider"/> 时生效。
        /// </param>
        /// <param name="retentionProvider">
        /// 保留天的配置读取口；传了它就以它为准（<paramref name="retentionDays"/> 只作兜底）。
        /// 宿主应当注入它，让「系统 → 系统参数设置」改完立刻生效。
        /// </param>
        public ScadaAuditWriter(string directory,
                                Action<ScadaDiagnosticLevel, string>? onDiagnostic = null,
                                int retentionDays = DefaultRetentionDays,
                                Func<int>? retentionProvider = null)
        {
            _directory = string.IsNullOrWhiteSpace(directory)
                ? throw new ArgumentException("审计目录不能为空", nameof(directory))
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

        /// <summary>
        /// 默认目录：软件目录下的 <c>Audit</c>。
        /// 与 <c>Logs</c>（可随时清掉）、<c>Alarms</c>（报警资产）三者<b>各占一个目录</b>：
        /// 混在一起早晚被"清理日志"这类操作一起删掉，而审计恰恰是事后唯一能作证的那份。
        /// </summary>
        public static string DefaultDirectory => Path.Combine(AppContext.BaseDirectory, "Audit");

        /// <summary>落盘当前是否故障中（断言与状态栏可读）</summary>
        public bool IsFaulted => _faulted;

        /// <summary>最近一次失败的原因；正常时为 null</summary>
        public string? LastError { get; private set; }

        /// <summary>追加一条操作记录（按当前本地日期滚动）。返回是否写成功</summary>
        public bool Append(ScadaAuditEntry entry) => Append(entry, DateTime.Now);

        /// <inheritdoc cref="Append(ScadaAuditEntry)"/>
        /// <param name="nowLocal">当前本地时刻。断言里传假日期才能测按天滚动</param>
        public bool Append(ScadaAuditEntry entry, DateTime nowLocal)
        {
            if (entry == null)
                return false;

            try
            {
                Directory.CreateDirectory(_directory);

                var path = Path.Combine(
                    _directory,
                    string.Format(CultureInfo.InvariantCulture, FileNameFormat, nowLocal));

                // FileShare.Read：运行中允许别人（Excel）读这份审计，但不许别人同时写。
                // 追加模式下文件位置天然在末尾，不存在覆盖既有行的可能。
                using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);

                // 空文件 = 本类刚建出来（或上次崩在写表头之前）→ 补 BOM 与表头。
                // 判"空"必须在写任何字节之前做，所以这两步合在一起、顺序不能换。
                //
                // leaveOpen: true 不是可选项：StreamWriter 默认会连底层流一起关掉，
                // 表头那个 writer 一出作用域就把 stream 关了，下面写行时必然抛 ObjectDisposedException——
                // 表现是"审计一行都写不进去，但目录和空文件建出来了"。
                if (stream.Length == 0)
                {
                    stream.Write(Bom, 0, Bom.Length);
                    using var head = new StreamWriter(stream, Utf8NoBom, 1024, leaveOpen: true) { AutoFlush = true };
                    head.WriteLine(string.Join(",", Header));
                }

                using (var writer = new StreamWriter(stream, Utf8NoBom, 1024, leaveOpen: true) { AutoFlush = true })
                    writer.WriteLine(BuildRow(entry, nowLocal));

                ReportRecovered();

                // 清理放在写成功之后：反过来的话，写不成时（磁盘满）先删掉旧凭证，等于把
                // "唯一能作证的那份"赔进去了。清理本身绝不抛，也不会改变本次写入的结果。
                PurgeIfNewDay(nowLocal);
                return true;
            }
            catch (Exception ex)
            {
                // 任何 IO 异常都在这里收住：审计写不成不该让操作失败，
                // 更不该把异常抛回动作执行链（那会顺着 UI 事件变成"点按钮程序崩了"）。
                ReportFault(ex);
                return false;
            }
        }

        /// <summary>
        /// 清掉超出保留期的按天文件，返回删掉的份数。<b>不抛</b>，也不碰
        /// <see cref="IsFaulted"/>——"旧文件删不掉"与"新记录写不进去"是两件事：
        /// 前者（多半是现场正用 Excel 开着某天的凭证在看）不该把落盘端标成故障，
        /// 否则接下来的写入会被当成"审计坏了"，而它其实好着。
        ///
        /// 只删<b>本类自己建的文件</b>：文件名必须同时满足"Audit- 开头"、".csv 结尾"、
        /// 中间那段能按 <c>yyyy-MM-dd</c> 解析出日期。现场往这个目录里丢的任何东西
        /// （手工备份、导出的汇总、备注）一个都不碰——清理策略把用户的文件删掉，
        /// 是这类功能最不能犯的错。
        /// </summary>
        /// <param name="nowLocal">当前本地时刻（断言里传假日期就能测保留期）</param>
        public int PurgeExpired(DateTime nowLocal)
        {
            // 0/负数 = 永不清理（现场要求留档）。这里必须先判：否则下面的截止日会算到"今天之后"，
            // 把今天这份也一起删掉——那是刚写进去的凭证。
            //
            // 读一次存进局部变量：保留期现在来自配置（每次现读），同一次清理里两次读之间
            // 若有人正好在改配置，会出现"按 0 判不清理、却按 90 算截止日"这种半新半旧的组合。
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
                        $"[审计] 有 {failed} 份过期审计文件没能删除（多半正被 Excel 打开），本次跳过；"
                        + $"下次跨天写入时会再试一次：{firstError}");
                }

                return removed;
            }
            catch (Exception ex)
            {
                _onDiagnostic?.Invoke(
                    ScadaDiagnosticLevel.Warning,
                    $"[审计] 清理过期审计文件失败（不影响新的记录写入）：{ex.Message}");
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
        /// 导出的建议文件名。与按天滚动的流水账同名规则——放回 <c>Audit</c> 目录时不会打架。
        /// </summary>
        public static string SuggestFileName(DateTime nowLocal)
            => string.Format(CultureInfo.InvariantCulture, FileNameFormat, nowLocal);

        /// <summary>
        /// 把一条记录拍成 CSV 的一行。时间戳由<b>落笔这一刻</b>给出（见 <see cref="ScadaAuditEntry"/>
        /// 的类注释），其余列全部来自记录快照。
        /// </summary>
        private static string BuildRow(ScadaAuditEntry entry, DateTime nowLocal)
        {
            string[] fields =
            {
                FormatTime(nowLocal),
                Escape(entry.Actor),
                Escape(entry.Subject),
                Escape(entry.EventText),
                Escape(entry.ActionText),
                entry.Outcome.DisplayName(),
                Escape(entry.Detail),
            };

            return string.Join(",", fields);
        }

        /// <summary>
        /// RFC 4180 转义：含逗号/引号/换行的字段用双引号包起来，内部引号翻倍。
        /// 说明列里会出现用户手输的内容（日志动作的文本、变量名），
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

        private void ReportFault(Exception ex)
        {
            LastError = ex.Message;

            if (_faulted)
                return;   // 已经在故障态：不再重复上报，避免磁盘故障时把日志刷满

            _faulted = true;
            _onDiagnostic?.Invoke(
                ScadaDiagnosticLevel.Error,
                $"[审计] 操作审计落盘失败，记录只留在运行日志里（操作本身不受影响）：{ex.Message}");
        }

        private void ReportRecovered()
        {
            LastError = null;

            if (!_faulted)
                return;

            _faulted = false;
            _onDiagnostic?.Invoke(ScadaDiagnosticLevel.Warning, "[审计] 操作审计落盘已恢复正常");
        }
    }
}
