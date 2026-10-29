using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using VisionMaster.Scada;
using VisionMaster.Scada.Controls;
using VisionMaster.Services;

namespace ScadaChecks
{
    internal partial class Program
    {
        /// <summary>
        /// v6 六项的暴力/压力测试（只在 <c>--stress-v6</c> 时运行，不进常规闸门）。
        ///
        /// 与既有断言的分工：常规断言钉"该做的做对了"，本组钉"乱来也不崩、边界不静默、量大不塌"。
        /// 每一组都刻意包含：空值/非法值/极端值、边界值、重复与并发调用、大数据量、依赖失败。
        /// </summary>
        private static class V6StressChecks
        {
            public static void Run() => RunOnSta(RunChecks);

            private static void RunOnSta(Action body)
            {
                Exception? failure = null;
                var thread = new Thread(() =>
                {
                    try { body(); }
                    catch (Exception ex) { failure = ex; }
                });
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                thread.Join();

                if (failure != null)
                    Check("[V6S-0] 压测主体不抛异常（抛了就说明有一条路径没兜住）", false, failure.ToString());
            }

            private static void RunChecks()
            {
                Section("[V6S] v6 六项暴力/压力测试（XY 曲线 / 报警汇总表 / 激活事件 / 文本列表模式 / 多语言 / 动作四条）");
                ScadaEditHistory.Clear();

                XyChart();
                AlarmTable();
                ActivationEvents();
                StateListModes();
                Languages();
                Actions();
                ExtraBrutal();
            }

            // ==================================================================
            // ⑦ 第二轮：更刁钻的输入（NaN 容量、全等值量程、链式延时、千次翻转、空键）
            // ==================================================================
            private static void ExtraBrutal()
            {
                Section("[V6S-XB] 第二轮暴力输入：NaN/全等值/链式延时/千次翻转/空键");

                var source = new FakeValueSource();
                var hx = new FakeValueHandle { VariableId = Guid.NewGuid(), Name = "X2", DataType = typeof(double), Value = 1d };
                var hy = new FakeValueHandle { VariableId = Guid.NewGuid(), Name = "Y2", DataType = typeof(double), Value = 1d };
                source.ByName["X2"] = hx;
                source.ByName["Y2"] = hy;
                var engine = new ScadaAlarmEngine(new ScadaDocument(), source, () => DateTime.UtcNow);
                var context = new ScadaRuntimeContext(engine, new ScadaBeatSource(Dispatcher.CurrentDispatcher), values: source);

                // XB-01 容量极端值（属性是 int，NaN 不可表达）
                var nanCapacity = NewXy(context, "X2", "Y2");
                bool nanOk = true;
                try
                {
                    nanCapacity.MaxPoints = int.MaxValue;
                    for (int i = 0; i < 10; i++) { hx.Value = i; hy.Value = i; nanCapacity.Sample(); }
                    nanCapacity.MaxPoints = int.MinValue;
                    for (int i = 0; i < 10; i++) { hx.Value = i + 100; hy.Value = i; nanCapacity.Sample(); }
                }
                catch { nanOk = false; }
                Check("[V6S-XB01] 容量填 int.MaxValue / int.MinValue：不抛、点数落在合法区间",
                    nanOk && nanCapacity.PointCount >= 0 && nanCapacity.PointCount <= 5000,
                    $"points={nanCapacity.PointCount}");

                // XB-02 全等值 + 自动量程（不能除零）
                var flat = NewXy(context, "X2", "Y2");
                bool flatOk = true;
                try
                {
                    for (int i = 0; i < 5; i++) { hx.Value = 42; hy.Value = 42; flat.Sample(); }
                }
                catch { flatOk = false; }
                Check("[V6S-XB02] 所有点完全相同 + 自动量程：不除零、不抛",
                    flatOk && flat.PointCount == 1, $"points={flat.PointCount}");

                // XB-03 10000 次同值采样（去重路径的性能）
                var dedupeBulk = NewXy(context, "X2", "Y2");
                hx.Value = 7; hy.Value = 7;
                var sw = Stopwatch.StartNew();
                for (int i = 0; i < 10000; i++) dedupeBulk.Sample();
                sw.Stop();
                Check("[V6S-XB03] 10000 次同值采样：只 1 点且 < 2s（去重不随次数变慢）",
                    dedupeBulk.PointCount == 1 && sw.ElapsedMilliseconds < 2000,
                    $"points={dedupeBulk.PointCount} / {sw.ElapsedMilliseconds}ms");

                // XB-04 报警表 MaxRows 极端值（属性是 int，NaN 不可表达；测 0/-1/int.MaxValue）
                var table = (AlarmTableElement)ElementRegistry.CreateControl(new ScadaElement { TypeKey = "Hmi.AlarmTable" });
                table.RuntimeContext = context;
                bool rowsOk = true;
                try
                {
                    table.MaxRows = 0; table.Sample();
                    table.MaxRows = -1; table.Sample();
                    table.MaxRows = int.MaxValue; table.Sample();
                }
                catch { rowsOk = false; }
                Check("[V6S-XB04] 报警表 MaxRows 填 0 / -1 / int.MaxValue：不抛、行数不越界",
                    rowsOk && table.Rows.Count <= 200, $"rows={table.Rows.Count}");

                // XB-05 千次翻转：事件一条不少、不崩
                var flip = NewButton();
                int flips = 0;
                flip.AddHandler(ScadaElementBase.ScadaEventEvent, new RoutedEventHandler((_, e) =>
                {
                    if (e is ScadaElementEventArgs) flips++;
                }));
                flip.RuntimeContext = context;
                var enable = new ScadaAnimation { Type = ScadaAnimationType.Enable };
                var flipSw = Stopwatch.StartNew();
                // 首拍是基线（不发），所以先单独推一次 0 把基线立起来，再跑 1000 次交替翻转。
                // 循环从 i=1 起（首个值是 1），否则 i=0 推的还是 0、与基线同值，只会有 999 次翻转。
                flip.TryApplyAnimationValue(enable, 0, out _);
                int baselineEvents = flips;
                for (int i = 1; i <= 1000; i++) flip.TryApplyAnimationValue(enable, i % 2, out _);
                flipSw.Stop();
                Check("[V6S-XB05] 基线之后 1000 次翻转：事件一条不少（1000 条）且 < 2s",
                    baselineEvents == 0 && flips == 1000 && flipSw.ElapsedMilliseconds < 2000,
                    $"baseline={baselineEvents} events={flips} / {flipSw.ElapsedMilliseconds}ms");

                // XB-06 链式延时：Delay → Log → Delay → Log
                var log = new StressLog();
                var bare = new ScadaActionDispatcher(log, source, new FakeNavigator());
                bare.Dispatch(Hook(
                    new ScadaAction { Type = ScadaActionType.Delay, DelayMs = 20 },
                    new ScadaAction { Type = ScadaActionType.Log, Text = "甲" },
                    new ScadaAction { Type = ScadaActionType.Delay, DelayMs = 20 },
                    new ScadaAction { Type = ScadaActionType.Log, Text = "乙" }), "按钮");
                PumpDispatcher(400);
                Check("[V6S-XB06] 链式延时（延时→动作→延时→动作）：四步全部执行、顺序不乱，且「（延时后）」只出现一次",
                    log.Lines.Count == 4
                    && log.Lines[0].Contains("延时") && log.Lines[1].Contains("甲")
                    && log.Lines[2].Contains("延时") && log.Lines[3].Contains("乙")
                    // F2 修复：第二段延时拿到的 Subject 已带后缀，幂等后不许叠加成
                    // 「按钮（延时后）（延时后）」。
                    && CountOccurrences(log.Lines[3], "（延时后）") == 1,
                    string.Join(" | ", log.Lines));

                // XB-07 延时不拖住调用方：Dispatch 必须立即返回
                log.Clear();
                var quick = Stopwatch.StartNew();
                bare.Dispatch(Hook(
                    new ScadaAction { Type = ScadaActionType.Delay, DelayMs = 500 },
                    new ScadaAction { Type = ScadaActionType.Log, Text = "慢" }), "按钮");
                quick.Stop();
                Check("[V6S-XB07] 延时 500ms：Dispatch 立即返回（< 100ms），不阻塞调用线程",
                    quick.ElapsedMilliseconds < 100,
                    $"{quick.ElapsedMilliseconds}ms");

                // XB-08 空键 / 纯 @
                var doc = new ScadaDocument();
                var zh = doc.AddLanguage("中文");
                zh.Entries.Add(new ScadaLanguageEntry { Key = string.Empty, Text = "空键" });
                Check("[V6S-XB08] 空键条目不会把 @ 解析成文案；单独一个 @ 原样返回",
                    ScadaTextCatalog.Resolve("@", doc.Languages, zh.LanguageId) == "@"
                    && ScadaTextCatalog.Resolve("", doc.Languages, zh.LanguageId) == string.Empty,
                    $"at={ScadaTextCatalog.Resolve("@", doc.Languages, zh.LanguageId)}");

                // XB-09 范围模式但条目只填了 Value（Min/Max 都是 0）
                var misconfigured = new ScadaStateList { Mode = ScadaStateList.ModeRange };
                misconfigured.Entries.Add(new ScadaStateEntry { Value = 5, Text = "只填了值" });
                Check("[V6S-XB09] 范围模式下只填了「值」：只有 0 命中（0~0），其余回落默认，不崩",
                    misconfigured.DisplayText(0) == "只填了值"
                    && misconfigured.DisplayText(5) == ScadaStateList.DefaultFallbackText,
                    $"0→{misconfigured.DisplayText(0)} 5→{misconfigured.DisplayText(5)}");

                // XB-10 语言表同名（管理器允许重名时的按名切换行为）
                var dup = new ScadaDocument();
                dup.AddLanguage("同名");
                dup.AddLanguage("同名");
                Check("[V6S-XB10] 同名语言：按名查找取第一张（可预测），不抛",
                    ReferenceEquals(dup.FindLanguageByName("同名"), dup.Languages[0]), "");
            }

            // ==================================================================
            // ① XY 曲线
            // ==================================================================
            private static void XyChart()
            {
                Section("[V6S-XY] XY 曲线：空/非法/极端输入、边界容量、大数据量、重复值");

                var source = new FakeValueSource();
                var hx = new FakeValueHandle { VariableId = Guid.NewGuid(), Name = "X", DataType = typeof(double), Value = 0d };
                var hy = new FakeValueHandle { VariableId = Guid.NewGuid(), Name = "Y", DataType = typeof(double), Value = 0d };
                source.ByName["X"] = hx;
                source.ByName["Y"] = hy;

                var engine = new ScadaAlarmEngine(new ScadaDocument(), source, () => DateTime.UtcNow);
                var beat = new ScadaBeatSource(Dispatcher.CurrentDispatcher);
                var context = new ScadaRuntimeContext(engine, beat, values: source);

                // XS-01 无变量名：不采样、不抛
                var blank = (XYChartElement)ElementRegistry.CreateControl(new ScadaElement { TypeKey = "Hmi.XYChart" });
                blank.RuntimeContext = context;
                blank.Sample();
                Check("[V6S-XY01] 空配置（没填 X/Y 变量名）：不采样、不抛",
                    blank.PointCount == 0, $"points={blank.PointCount}");

                // XS-02 设计期（Values=null）：0 点、渲染不抛
                var design = (XYChartElement)ElementRegistry.CreateControl(new ScadaElement { TypeKey = "Hmi.XYChart" });
                design.XVariable = "X";
                design.YVariable = "Y";
                design.Sample();
                Check("[V6S-XY02] 设计期（无运行上下文）：0 点、不抛",
                    design.PointCount == 0, $"points={design.PointCount}");

                // XS-03 变量名不存在
                var missing = NewXy(context, "NoSuchVar", "Y");
                missing.Sample();
                Check("[V6S-XY03] 变量解析不到：不追加、不抛",
                    missing.PointCount == 0, $"points={missing.PointCount}");

                // XS-04 只有 X 解析成功
                hy.Value = 5d;
                var half = NewXy(context, "X", "Y");
                source.ByName.Remove("Y");
                half.Sample();
                source.ByName["Y"] = hy;
                Check("[V6S-XY04] 只有一半能解析（Y 缺失）：不追加",
                    half.PointCount == 0, $"points={half.PointCount}");

                // XS-05 同一对值重复 1000 次
                hx.Value = 1d; hy.Value = 2d;
                var dedupe = NewXy(context, "X", "Y");
                for (int i = 0; i < 1000; i++) dedupe.Sample();
                Check("[V6S-XY05] 同一对值推 1000 次：只长 1 点（去重生效）",
                    dedupe.PointCount == 1, $"points={dedupe.PointCount}");

                // XS-06 值变化才追加
                hx.Value = 2d; dedupe.Sample();
                Check("[V6S-XY06] 值变了才追加：第 2 点",
                    dedupe.PointCount == 2, $"points={dedupe.PointCount}");

                // XS-07 容量边界 MaxPoints=2
                var tiny = NewXy(context, "X", "Y");
                tiny.MaxPoints = 2;
                for (int i = 0; i < 10; i++) { hx.Value = 100 + i; hy.Value = i; tiny.Sample(); }
                Check("[V6S-XY07] MaxPoints=2：环形缓冲截到 2 点",
                    tiny.PointCount == 2, $"points={tiny.PointCount}");

                // XS-08 非法容量：0 / 负数 / 超大
                var zero = NewXy(context, "X", "Y"); zero.MaxPoints = 0;
                var neg = NewXy(context, "X", "Y"); neg.MaxPoints = -5;
                var huge = NewXy(context, "X", "Y"); huge.MaxPoints = 99999;
                bool clampedOk = true;
                foreach (var c in new[] { zero, neg, huge })
                {
                    for (int i = 0; i < 20; i++) { hx.Value = 200 + i; hy.Value = i; c.Sample(); }
                    clampedOk &= c.PointCount >= 2 && c.PointCount <= 20;
                }
                Check("[V6S-XY08] 非法容量 0/-5/99999：夹紧到合法区间、不抛",
                    clampedOk, $"0→{zero.PointCount} / -5→{neg.PointCount} / 99999→{huge.PointCount}");

                // XS-09 极端值：NaN / Infinity / double 极限
                var extreme = NewXy(context, "X", "Y");
                bool extremeOk = true;
                foreach (var v in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, double.MaxValue, double.MinValue })
                {
                    hx.Value = v; hy.Value = v;
                    try { extreme.Sample(); }
                    catch { extremeOk = false; }
                }
                Check("[V6S-XY09] 极端值 NaN/±Inf/Max/Min：不抛异常",
                    extremeOk, $"points={extreme.PointCount}");

                // XS-10 大数据量：10000 拍
                var bulk = NewXy(context, "X", "Y");
                bulk.MaxPoints = 500;
                var sw = Stopwatch.StartNew();
                for (int i = 0; i < 10000; i++) { hx.Value = i; hy.Value = i * 2; bulk.Sample(); }
                sw.Stop();
                Check("[V6S-XY10] 10000 拍：点数被截到 MaxPoints=500，耗时 < 3s",
                    bulk.PointCount == 500 && sw.ElapsedMilliseconds < 3000,
                    $"points={bulk.PointCount} / {sw.ElapsedMilliseconds}ms");

                // XS-11 固定量程写反（XMin>XMax）
                var reversed = NewXy(context, "X", "Y");
                reversed.AutoScale = false;
                reversed.XMin = 100; reversed.XMax = -100;
                reversed.YMin = 50; reversed.YMax = -50;
                bool reversedOk = true;
                try { hx.Value = 10; hy.Value = 10; reversed.Sample(); }
                catch { reversedOk = false; }
                Check("[V6S-XY11] 固定量程写反（Min>Max）：不除零、不抛",
                    reversedOk, "");

                // XS-12 换运行上下文（→ null）清空缓冲
                hx.Value = 300; hy.Value = 300;
                bulk.Sample();
                bulk.RuntimeContext = null;
                Check("[V6S-XY12] 摘掉运行上下文：缓冲清空（下一轮不继承旧轨迹）",
                    bulk.PointCount == 0, $"points={bulk.PointCount}");

                // XS-13 改容量后 Refresh：缓冲清空
                var refreshed = NewXy(context, "X", "Y");
                hx.Value = 400; hy.Value = 400; refreshed.Sample();
                refreshed.MaxPoints = 10;
                refreshed.Refresh();
                Check("[V6S-XY13] 改配置后 Refresh：旧轨迹作废",
                    refreshed.PointCount == 0, $"points={refreshed.PointCount}");
            }

            private static XYChartElement NewXy(ScadaRuntimeContext context, string x, string y)
            {
                var element = (XYChartElement)ElementRegistry.CreateControl(new ScadaElement { TypeKey = "Hmi.XYChart" });
                element.XVariable = x;
                element.YVariable = y;
                element.RuntimeContext = context;
                return element;
            }

            // ==================================================================
            // ② 报警汇总表
            // ==================================================================
            private static void AlarmTable()
            {
                Section("[V6S-AT] 报警汇总表：空态/截断/重复确认/无权限/大数据量/指纹短路");

                // AT-01 设计期
                var design = (AlarmTableElement)ElementRegistry.CreateControl(new ScadaElement { TypeKey = "Hmi.AlarmTable" });
                design.Sample();
                Check("[V6S-AT01] 设计期（无引擎）：0 行 + 设计期提示，不冒充「无报警」",
                    design.Rows.Count == 0 && design.BodyText == AlarmTableElement.DesignTimeHint,
                    $"rows={design.Rows.Count} body=\"{design.BodyText}\"");

                // 造 3 条报警
                var source = new FakeValueSource();
                var tempId = Guid.NewGuid(); var pressId = Guid.NewGuid(); var speedId = Guid.NewGuid();
                var temp = new FakeValueHandle { VariableId = tempId, Name = "T", DataType = typeof(double), Value = 0d };
                var press = new FakeValueHandle { VariableId = pressId, Name = "P", DataType = typeof(double), Value = 0d };
                var speed = new FakeValueHandle { VariableId = speedId, Name = "S", DataType = typeof(double), Value = 0d };
                source.ById[tempId] = temp; source.ById[pressId] = press; source.ById[speedId] = speed;

                var doc = new ScadaDocument();
                doc.AddAlarm("提示A", ScadaAlarmKind.High).Bind(tempId, "T");
                doc.AddAlarm("严重B", ScadaAlarmKind.HighHigh).Bind(pressId, "P");
                doc.AddAlarm("警告C", ScadaAlarmKind.High).Bind(speedId, "S");
                doc.Alarms[0].Threshold = 10; doc.Alarms[0].Severity = ScadaAlarmSeverity.Info;
                doc.Alarms[1].Threshold = 10; doc.Alarms[1].Severity = ScadaAlarmSeverity.Critical;
                doc.Alarms[2].Threshold = 10; doc.Alarms[2].Severity = ScadaAlarmSeverity.Warning;

                var engine = new ScadaAlarmEngine(doc, source, () => DateTime.UtcNow);
                engine.Attach();
                var beat = new ScadaBeatSource(Dispatcher.CurrentDispatcher);
                var context = new ScadaRuntimeContext(engine, beat, values: source);

                var table = (AlarmTableElement)ElementRegistry.CreateControl(new ScadaElement { TypeKey = "Hmi.AlarmTable" });
                table.RuntimeContext = context;

                // AT-02 空报警
                table.Sample();
                Check("[V6S-AT02] 空报警：0 行 + EmptyText",
                    table.Rows.Count == 0 && table.BodyText == "当前无报警",
                    $"rows={table.Rows.Count} body=\"{table.BodyText}\"");

                // AT-03 三条
                temp.Raise(100); press.Raise(100); speed.Raise(100);
                table.Sample();
                Check("[V6S-AT03] 三条报警：三行、严重度降序、未确认 3",
                    table.Rows.Count == 3 && table.Rows[0].NameText == "严重B"
                    && table.Rows[2].NameText == "提示A" && engine.UnacknowledgedCount == 3,
                    $"rows={table.Rows.Count} first={table.Rows.FirstOrDefault()?.NameText} unack={engine.UnacknowledgedCount}");

                // AT-04 指纹短路
                var before = table.Rows;
                table.Sample();
                Check("[V6S-AT04] 数据没变：Rows 是同一个列表对象（不重建行，不抢走正在点的按钮）",
                    ReferenceEquals(before, table.Rows), "");

                // AT-05 截断：MaxRows=1
                var one = (AlarmTableElement)ElementRegistry.CreateControl(new ScadaElement { TypeKey = "Hmi.AlarmTable" });
                one.MaxRows = 1;
                one.RuntimeContext = context;
                one.Sample();
                Check("[V6S-AT05] MaxRows=1 + 3 条：只渲染 1 行，页脚给出剩余条数",
                    one.Rows.Count == 1 && one.FooterVisibility == Visibility.Visible,
                    $"rows={one.Rows.Count} footer=\"{one.FooterText}\"");

                // AT-06 重复确认同一条：只成功一次
                var first = engine.ActiveAlarms[0];
                bool ack1 = table.AcknowledgeOne(first.AlarmId);
                bool ack2 = table.AcknowledgeOne(first.AlarmId);
                table.Sample();
                Check("[V6S-AT06] 同一条连确认两次：第一次成功、第二次拒绝（且给出可读原因）",
                    ack1 && !ack2, $"first={ack1} second={ack2} status=\"{table.StatusText}\"");

                // AT-07 确认不存在的 Id
                bool ghost = table.AcknowledgeOne(Guid.NewGuid());
                Check("[V6S-AT07] 确认一条根本不存在的报警：返回 false，不抛",
                    !ghost, $"ghost={ghost} status=\"{table.StatusText}\"");

                // AT-08 全部确认：0 待确认时不报错（AcknowledgeAll 无返回值，用引擎计数当判据）
                // 注意 AT06 已经确认掉一条，所以这里起点是 2 而不是 3。
                int unackBefore = engine.UnacknowledgedCount;
                table.AcknowledgeAll();
                table.Sample();
                int unackAfterFirst = engine.UnacknowledgedCount;
                bool secondThrew = false;
                try { table.AcknowledgeAll(); } catch { secondThrew = true; }
                table.Sample();
                Check("[V6S-AT08] 全部确认：第一次清空待确认（2→0），第二次不抛",
                    unackBefore == 2 && unackAfterFirst == 0 && !secondThrew,
                    $"before={unackBefore} after={unackAfterFirst} secondThrew={secondThrew}");

                // AT-09 无权限
                var denied = (AlarmTableElement)ElementRegistry.CreateControl(new ScadaElement { TypeKey = "Hmi.AlarmTable" });
                denied.Element.RequiredPermission = ScadaPermissionKeys.ControlRun;
                denied.RuntimeContext = new ScadaRuntimeContext(engine, beat, values: source, accessPolicy: new DenyPolicy());
                temp.Raise(0); temp.Raise(100);
                denied.Sample();
                Check("[V6S-AT09] 无权限：行内确认不可用、全部确认不可用（不是点了才报错）",
                    !denied.CanAcknowledgeAll && denied.Rows.All(r => !r.CanAcknowledge),
                    $"canAll={denied.CanAcknowledgeAll} rows={denied.Rows.Count}");

                // AT-10 大数据量：60 条报警
                var bulkDoc = new ScadaDocument();
                var bulkSource = new FakeValueSource();
                var handles = new List<FakeValueHandle>();
                for (int i = 0; i < 60; i++)
                {
                    var id = Guid.NewGuid();
                    var handle = new FakeValueHandle { VariableId = id, Name = "V" + i, DataType = typeof(double), Value = 0d };
                    bulkSource.ById[id] = handle;
                    handles.Add(handle);
                    var alarm = bulkDoc.AddAlarm("报警" + i, ScadaAlarmKind.High);
                    alarm.Bind(id, "V" + i);
                    alarm.Threshold = 10;
                }

                var bulkEngine = new ScadaAlarmEngine(bulkDoc, bulkSource, () => DateTime.UtcNow);
                bulkEngine.Attach();
                foreach (var handle in handles) handle.Raise(100);

                var bulkTable = (AlarmTableElement)ElementRegistry.CreateControl(new ScadaElement { TypeKey = "Hmi.AlarmTable" });
                bulkTable.MaxRows = 8;
                bulkTable.RuntimeContext = new ScadaRuntimeContext(bulkEngine, new ScadaBeatSource(Dispatcher.CurrentDispatcher), values: bulkSource);
                var sw = Stopwatch.StartNew();
                for (int i = 0; i < 200; i++) bulkTable.Sample();
                sw.Stop();
                Check("[V6S-AT10] 60 条报警 × 200 拍：只渲染 8 行、耗时 < 3s",
                    bulkTable.Rows.Count == 8 && sw.ElapsedMilliseconds < 3000,
                    $"rows={bulkTable.Rows.Count} / {sw.ElapsedMilliseconds}ms");
            }

            // ==================================================================
            // ③ 激活 / 取消激活事件
            // ==================================================================
            private static void ActivationEvents()
            {
                Section("[V6S-EV] 激活/取消激活：边沿、重复、无动画、复位、反向、运行态闸门");

                var engine = new ScadaAlarmEngine(new ScadaDocument(), new FakeValueSource(), () => DateTime.UtcNow);
                var beat = new ScadaBeatSource(Dispatcher.CurrentDispatcher);
                var context = new ScadaRuntimeContext(engine, beat);

                var animation = new ScadaAnimation { Type = ScadaAnimationType.Enable };

                // EV-01/02/03/04：边沿
                var control = NewButton();
                var raised = new List<ScadaEventType>();
                control.AddHandler(ScadaElementBase.ScadaEventEvent, new RoutedEventHandler((_, e) =>
                {
                    if (e is ScadaElementEventArgs args) raised.Add(args.ScadaEvent);
                }));
                control.RuntimeContext = context;

                // EV-00：**首拍只建立基线、不发事件**（F1 修复后的语义）。
                // 首拍直接推 0（启动即禁用）：状态要落成禁用，但不许发 Deactivated——
                // 否则"失能时清标志位/发提示"这类配置每次运行都会收到一次没人操作过的误报。
                control.TryApplyAnimationValue(animation, 0, out _);
                Check("[V6S-EV00] 启动首拍即禁用：落成禁用但**不发**事件（首拍=基线）",
                    raised.Count == 0 && !control.IsEnabled,
                    $"count={raised.Count} enabled={control.IsEnabled}");

                int afterOffTwice = raised.Count;
                control.TryApplyAnimationValue(animation, 0, out _);
                afterOffTwice = raised.Count;

                control.TryApplyAnimationValue(animation, 1, out _);
                int afterOnTwice = raised.Count;
                control.TryApplyAnimationValue(animation, 1, out _);
                afterOnTwice = raised.Count;

                Check("[V6S-EV01] 基线之后同值重复推不发、翻转才发（0→0 不发、1→1 不发）",
                    afterOffTwice == 0 && afterOnTwice == 1 && raised[0] == ScadaEventType.Activated,
                    $"offTwice={afterOffTwice} onTwice={afterOnTwice} seq={string.Join(",", raised)}");

                control.TryApplyAnimationValue(animation, 0, out _);
                Check("[V6S-EV02] 1→0 发一次 Deactivated",
                    raised.Count == 2 && raised[1] == ScadaEventType.Deactivated,
                    $"seq={string.Join(",", raised)}");

                // EV-03 换运行上下文后基线重置：停一轮、再起一轮，首拍仍然不发事件
                raised.Clear();
                control.RuntimeContext = null;     // 停运行：ResetAnimationVisuals 把基线清空
                control.RuntimeContext = context;  // 下一轮
                control.TryApplyAnimationValue(animation, 0, out _);
                Check("[V6S-EV03] 换运行上下文后基线重置：新一轮首拍仍不发事件（两轮行为一致）",
                    raised.Count == 0, $"count={raised.Count}");

                // EV-05 连续翻转 6 次 → 6 条，交替
                raised.Clear();
                for (int i = 0; i < 3; i++)
                {
                    control.TryApplyAnimationValue(animation, 1, out _);
                    control.TryApplyAnimationValue(animation, 0, out _);
                }
                Check("[V6S-EV05] 连翻 3 轮（6 次翻转）：恰好 6 条，激活/取消交替",
                    raised.Count == 6
                    && raised.Where((t, i) => i % 2 == 0 ? t == ScadaEventType.Activated : t == ScadaEventType.Deactivated).Count() == 6,
                    $"seq={string.Join(",", raised)}");

                // EV-06 无 Enable 动画（刷普通值）不发
                raised.Clear();
                var plain = NewButton();
                plain.AddHandler(ScadaElementBase.ScadaEventEvent, new RoutedEventHandler((_, e) =>
                {
                    if (e is ScadaElementEventArgs args) raised.Add(args.ScadaEvent);
                }));
                plain.RuntimeContext = context;
                var textAnimation = new ScadaAnimation { Type = ScadaAnimationType.Visibility };
                plain.TryApplyAnimationValue(textAnimation, 1, out _);
                plain.TryApplyAnimationValue(textAnimation, 0, out _);
                Check("[V6S-EV06] 只配可见性动画：不发激活/取消激活",
                    raised.Count == 0, $"count={raised.Count}");

                // EV-07 复位（停运行）不发 Deactivated
                // 先制造一次真翻转（当前态在 EV-05 结束时是禁用）：1 → 启用，0 → 禁用
                raised.Clear();
                control.TryApplyAnimationValue(animation, 1, out _);
                control.TryApplyAnimationValue(animation, 0, out _);
                int beforeReset = raised.Count;
                control.Refresh();
                Check("[V6S-EV07] Refresh（停止运行/设计期重铺）：不再补发 Deactivated",
                    beforeReset == 2 && raised.Count == 2,
                    $"before={beforeReset} after={raised.Count}");

                // EV-08 设计期（无上下文）不发
                raised.Clear();
                var designControl = NewButton();
                designControl.AddHandler(ScadaElementBase.ScadaEventEvent, new RoutedEventHandler((_, e) =>
                {
                    if (e is ScadaElementEventArgs args) raised.Add(args.ScadaEvent);
                }));
                designControl.TryApplyAnimationValue(animation, 0, out _);
                designControl.TryApplyAnimationValue(animation, 1, out _);
                Check("[V6S-EV08] 设计期（RuntimeContext=null）：一条都不发",
                    raised.Count == 0, $"count={raised.Count}");

                // EV-09 反向（Invert）：变量 1 → 禁用、变量 0 → 启用
                // 首拍 0 只建立基线（反转后=启用），之后 1→取消激活、0→激活。
                raised.Clear();
                var inverted = NewButton();
                inverted.AddHandler(ScadaElementBase.ScadaEventEvent, new RoutedEventHandler((_, e) =>
                {
                    if (e is ScadaElementEventArgs args) raised.Add(args.ScadaEvent);
                }));
                inverted.RuntimeContext = context;
                var invertAnimation = new ScadaAnimation { Type = ScadaAnimationType.Enable, Invert = true };
                inverted.TryApplyAnimationValue(invertAnimation, 0, out _);
                bool invertBaseline = raised.Count == 0;
                inverted.TryApplyAnimationValue(invertAnimation, 1, out _);
                bool invertOff = raised.Count == 1 && raised[0] == ScadaEventType.Deactivated;
                inverted.TryApplyAnimationValue(invertAnimation, 0, out _);
                Check("[V6S-EV09] 反转启用：首拍基线不发；变量 1→取消激活、变量 0→激活（与正向相反）",
                    invertBaseline && invertOff && raised.Count == 2 && raised[1] == ScadaEventType.Activated,
                    $"baseline={invertBaseline} seq={string.Join(",", raised)}");

                // EV-10 运行态闸门：不在当前页的图元，事件被拦
                var doc = new ScadaDocument();
                var pageA = doc.AddPage("A");
                var pageB = doc.AddPage("B");
                var elA = new ScadaElement { TypeKey = "Hmi.Button", Name = "在A" };
                var elB = new ScadaElement { TypeKey = "Hmi.Button", Name = "在B" };
                pageA.Elements.Add(elA);
                pageB.Elements.Add(elB);
                elA.GetOrAddEventHook(ScadaEventType.Activated).Actions.Add(new ScadaAction { Type = ScadaActionType.Log, Text = "激活" });

                var runtime = new ScadaRuntime(doc);
                runtime.Start();
                int hitA = runtime.RaiseElementEvent(elA, ScadaEventType.Activated);
                int hitB = runtime.RaiseElementEvent(elB, ScadaEventType.Activated);
                Check("[V6S-EV10] 激活事件走运行态闸门：当前页命中 1 条、非当前页命中 0 条",
                    hitA == 1 && hitB == 0, $"A={hitA} B={hitB}");
            }

            private static ScadaElementBase NewButton()
            {
                var control = ElementRegistry.CreateControl(new ScadaElement { TypeKey = "Hmi.Button" });
                return control;
            }

            // ==================================================================
            // ④ 文本列表三种模式
            // ==================================================================
            private static void StateListModes()
            {
                Section("[V6S-SL] 文本列表：四种模式边界、非法数据、克隆/往返、大数据量");

                // SL-01 默认精确
                var exact = new ScadaStateList();
                exact.Entries.Add(new ScadaStateEntry { Value = 1, Text = "一" });
                Check("[V6S-SL01] 默认模式 Exact，与 v5 行为一致",
                    exact.ModeKind == ScadaStateMatchMode.Exact && exact.DisplayText(1) == "一" && exact.DisplayText(2) == ScadaStateList.DefaultFallbackText,
                    $"mode={exact.Mode}");

                // SL-02 范围边界
                var range = new ScadaStateList { Mode = ScadaStateList.ModeRange };
                range.Entries.Add(new ScadaStateEntry { Min = 0, Max = 50, Text = "正常" });
                range.Entries.Add(new ScadaStateEntry { Min = 51, Max = 80, Text = "预警" });
                range.Entries.Add(new ScadaStateEntry { Min = 81, Max = 999, Text = "报警" });
                Check("[V6S-SL02] 范围：Min/Max 两端都含、区间外回落默认",
                    range.DisplayText(0) == "正常" && range.DisplayText(50) == "正常"
                    && range.DisplayText(51) == "预警" && range.DisplayText(80) == "预警"
                    && range.DisplayText(81) == "报警" && range.DisplayText(1000) == ScadaStateList.DefaultFallbackText,
                    $"{range.DisplayText(0)}/{range.DisplayText(50)}/{range.DisplayText(51)}/{range.DisplayText(1000)}");

                // SL-03 负区间
                var negative = new ScadaStateList { Mode = ScadaStateList.ModeRange };
                negative.Entries.Add(new ScadaStateEntry { Min = -100, Max = -1, Text = "负" });
                Check("[V6S-SL03] 范围：负区间命中",
                    negative.DisplayText(-50) == "负" && negative.DisplayText(0) == ScadaStateList.DefaultFallbackText, "");

                // SL-04 重叠取第一条
                var overlap = new ScadaStateList { Mode = ScadaStateList.ModeRange };
                overlap.Entries.Add(new ScadaStateEntry { Min = 0, Max = 100, Text = "甲" });
                overlap.Entries.Add(new ScadaStateEntry { Min = 50, Max = 150, Text = "乙" });
                Check("[V6S-SL04] 范围重叠：取第一条（可预测，不随机）",
                    overlap.DisplayText(60) == "甲", overlap.DisplayText(60));

                // SL-05 范围写反（Min>Max）不命中、不抛
                var bad = new ScadaStateList { Mode = ScadaStateList.ModeRange };
                bad.Entries.Add(new ScadaStateEntry { Min = 100, Max = 0, Text = "坏" });
                Check("[V6S-SL05] 范围写反（Min>Max）：不命中、不抛",
                    bad.DisplayText(50) == ScadaStateList.DefaultFallbackText, bad.DisplayText(50));

                // SL-06 位模式
                var bit = new ScadaStateList { Mode = ScadaStateList.ModeBit };
                bit.Entries.Add(new ScadaStateEntry { Value = 0, Text = "停" });
                bit.Entries.Add(new ScadaStateEntry { Value = 1, Text = "运行" });
                Check("[V6S-SL06] 位：0→停、1→运行、5→运行、-1→运行（非 0 都算 1）",
                    bit.DisplayText(0) == "停" && bit.DisplayText(1) == "运行"
                    && bit.DisplayText(5) == "运行" && bit.DisplayText(-1) == "运行", "");

                // SL-07 位号模式
                var bits = new ScadaStateList { Mode = ScadaStateList.ModeBitIndex };
                bits.Entries.Add(new ScadaStateEntry { Value = 0, Text = "位0" });
                bits.Entries.Add(new ScadaStateEntry { Value = 3, Text = "位3" });
                // 负数按补码看位：-1 = 全 1，所以位 0 命中——这是**已知语义**，钉住它免得被当成 bug 改坏。
                Check("[V6S-SL07] 位号：0/3 命中；变量 0 无命中；-1 命中位 0（补码语义）",
                    bits.DisplayText(1) == "位0" && bits.DisplayText(8) == "位3"
                    && bits.DisplayText(0) == ScadaStateList.DefaultFallbackText
                    && bits.DisplayText(-1) == "位0",
                    $"1→{bits.DisplayText(1)} 8→{bits.DisplayText(8)} 0→{bits.DisplayText(0)} -1→{bits.DisplayText(-1)}");

                // SL-07b 位号越界（32/33）不崩、不命中
                Check("[V6S-SL07b] 位号越界（32/33/64）：不崩、不命中（位号上限 31）",
                    bits.DisplayText(32) == ScadaStateList.DefaultFallbackText
                    && bits.DisplayText(64) == ScadaStateList.DefaultFallbackText, "");

                // SL-08 非法模式串
                var badMode = new ScadaStateList { Mode = "乱写" };
                var nullMode = new ScadaStateList { Mode = null! };
                Check("[V6S-SL08] 非法/空模式串：归一成 Exact，不抛",
                    badMode.ModeKind == ScadaStateMatchMode.Exact && nullMode.ModeKind == ScadaStateMatchMode.Exact,
                    $"{badMode.Mode}/{nullMode.Mode}");

                // SL-09 Clone / CopyFrom 带模式与范围
                var clone = range.Clone();
                var target = new ScadaStateList();
                target.CopyFrom(range);
                Check("[V6S-SL09] Clone/CopyFrom 带上模式与 Min/Max（改副本不回写原对象）",
                    clone.ModeKind == ScadaStateMatchMode.Range && clone.Entries[0].Max == 50
                    && target.ModeKind == ScadaStateMatchMode.Range && target.DisplayText(60) == "预警",
                    $"{clone.Mode}/{target.Mode}");

                // SL-10 大数据量：1000 条 × 20000 次查找
                var bulk = new ScadaStateList { Mode = ScadaStateList.ModeRange };
                for (int i = 0; i < 1000; i++)
                    bulk.Entries.Add(new ScadaStateEntry { Min = i * 10, Max = i * 10 + 9, Text = "档" + i });
                var sw = Stopwatch.StartNew();
                int hits = 0;
                for (int i = 0; i < 20000; i++) if (bulk.Find(i % 10000) != null) hits++;
                sw.Stop();
                Check("[V6S-SL10] 1000 条范围 × 20000 次查找：命中率正确且 < 2s",
                    hits == 20000 && sw.ElapsedMilliseconds < 2000,
                    $"hits={hits} / {sw.ElapsedMilliseconds}ms");
            }

            // ==================================================================
            // ⑤ 多语言
            // ==================================================================
            private static void Languages()
            {
                Section("[V6S-LG] 多语言：键语法、三级回落、空译文、重复键、悬空语言、端到端、大数据量");

                // LG-01 键语法
                Check("[V6S-LG01] 键语法：@a 是键；@ / @@a / a@ / 空 都不是",
                    ScadaTextCatalog.IsKey("@a", out var k1) && k1 == "a"
                    && !ScadaTextCatalog.IsKey("@", out _)
                    && !ScadaTextCatalog.IsKey("@@a", out _)
                    && !ScadaTextCatalog.IsKey("a@", out _)
                    && !ScadaTextCatalog.IsKey(null, out _)
                    && !ScadaTextCatalog.IsKey(string.Empty, out _), "");

                var doc = new ScadaDocument();
                var zh = doc.AddLanguage("中文");
                var en = doc.AddLanguage("English");
                zh.Entries.Add(new ScadaLanguageEntry { Key = "btn.start", Text = "启动" });
                en.Entries.Add(new ScadaLanguageEntry { Key = "btn.start", Text = "Start" });
                en.Entries.Add(new ScadaLanguageEntry { Key = "only.en", Text = "EnglishOnly" });

                // LG-02/03/04 三级回落
                Check("[V6S-LG02] 当前语言命中：@btn.start → 启动",
                    ScadaTextCatalog.Resolve("@btn.start", doc.Languages, zh.LanguageId) == "启动", "");
                Check("[V6S-LG03] 当前语言没有、别的语言有：回落到那一语言",
                    ScadaTextCatalog.Resolve("@only.en", doc.Languages, zh.LanguageId) == "EnglishOnly", "");
                Check("[V6S-LG04] 所有语言都没有：原样显示 @键（看得见哪个键没配）",
                    ScadaTextCatalog.Resolve("@missing", doc.Languages, zh.LanguageId) == "@missing", "");

                // LG-05 空译文
                zh.Entries.Add(new ScadaLanguageEntry { Key = "empty", Text = string.Empty });
                Check("[V6S-LG05] 空译文：不把界面刷成空白，回落到原文",
                    ScadaTextCatalog.Resolve("@empty", doc.Languages, zh.LanguageId) == "@empty", "");

                // LG-06 转义
                Check("[V6S-LG06] @@ 转义：@@a → @a（不是查表）",
                    ScadaTextCatalog.Resolve("@@a", doc.Languages, zh.LanguageId) == "@a", "");

                // LG-07 大小写敏感
                zh.Entries.Add(new ScadaLanguageEntry { Key = "Start", Text = "大写" });
                Check("[V6S-LG07] 键区分大小写：Start ≠ start",
                    ScadaTextCatalog.Resolve("@Start", doc.Languages, zh.LanguageId) == "大写"
                    && ScadaTextCatalog.Resolve("@start", doc.Languages, zh.LanguageId) == "@start", "");

                // LG-08 重复键取第一条
                zh.Entries.Add(new ScadaLanguageEntry { Key = "dup", Text = "第一" });
                zh.Entries.Add(new ScadaLanguageEntry { Key = "dup", Text = "第二" });
                Check("[V6S-LG08] 重复键：取第一条（可预测）",
                    ScadaTextCatalog.Resolve("@dup", doc.Languages, zh.LanguageId) == "第一", "");

                // LG-09 悬空当前语言
                Check("[V6S-LG09] 当前语言 Id 指向不存在的表：回落到任一语言，不空白",
                    ScadaTextCatalog.Resolve("@btn.start", doc.Languages, Guid.NewGuid()) == "启动", "");

                // LG-10 空语言表
                Check("[V6S-LG10] 语言表为空：@键 原样返回",
                    ScadaTextCatalog.Resolve("@btn.start", Array.Empty<ScadaLanguage>(), Guid.Empty) == "@btn.start", "");

                // LG-11 文档 CRUD 与当前语言
                var doc2 = new ScadaDocument();
                var l1 = doc2.AddLanguage("A");
                var autoCurrent = doc2.CurrentLanguageId == l1.LanguageId;
                var l2 = doc2.AddLanguage("B");
                doc2.TryRemoveLanguage(l1, out _);
                Check("[V6S-LG11] 首张语言自动设为当前；删掉当前语言后回落到剩下的第一张",
                    autoCurrent && doc2.CurrentLanguageId == l2.LanguageId, $"current={doc2.CurrentLanguageId}");

                // LG-12 EnsureIdentity 补 Id + 收回悬空
                var doc3 = new ScadaDocument();
                doc3.Languages.Add(new ScadaLanguage { LanguageId = Guid.Empty, Name = "X" });
                doc3.CurrentLanguageId = Guid.NewGuid();
                int repaired = doc3.EnsureIdentity();
                Check("[V6S-LG12] EnsureIdentity：补缺失 LanguageId、把悬空的当前语言收回第一张",
                    repaired >= 2 && doc3.Languages[0].LanguageId != Guid.Empty
                    && doc3.CurrentLanguageId == doc3.Languages[0].LanguageId,
                    $"repaired={repaired}");

                // LG-13 端到端：图元 Text=@键 经解析上屏
                var textModel = new ScadaElement { TypeKey = "Hmi.Text" };
                textModel.SetProperty("Text", "@btn.start");
                var textControl = ElementRegistry.CreateControl(textModel);
                textControl.TextResolver = t => ScadaTextCatalog.Resolve(t, doc.Languages, zh.LanguageId);
                textControl.Refresh();
                Check("[V6S-LG13] 图元端到端：Text=@btn.start → 控件文字「启动」",
                    textControl.Text == "启动", $"text=\"{textControl.Text}\"");

                // LG-14 绑定送来的运行值不被翻译
                var descriptor = ElementRegistry.Find("Hmi.Text")!.Properties.First(p => p.Key == "Text");
                textControl.TryApplyRuntimeValue(descriptor, "@btn.start", null, out _);
                Check("[V6S-LG14] 运行值（绑定）不被当文案翻：仍显示 @btn.start",
                    textControl.Text == "@btn.start", $"text=\"{textControl.Text}\"");

                // LG-15 大数据量：1000 键 × 5 语言 × 2000 次解析
                var bulk = new ScadaDocument();
                var langs = new List<ScadaLanguage>();
                for (int l = 0; l < 5; l++)
                {
                    var lang = bulk.AddLanguage("L" + l);
                    for (int i = 0; i < 1000; i++)
                        lang.Entries.Add(new ScadaLanguageEntry { Key = "k" + i, Text = "v" + l + "_" + i });
                    langs.Add(lang);
                }
                var sw = Stopwatch.StartNew();
                string last = string.Empty;
                for (int i = 0; i < 2000; i++)
                    last = ScadaTextCatalog.Resolve("@k" + (i % 1000), bulk.Languages, langs[4].LanguageId);
                sw.Stop();
                Check("[V6S-LG15] 1000 键 × 5 语言 × 2000 次解析：结果正确且 < 2s",
                    last.StartsWith("v4_", StringComparison.Ordinal) && sw.ElapsedMilliseconds < 2000,
                    $"last={last} / {sw.ElapsedMilliseconds}ms");
            }

            // ==================================================================
            // ⑥ 动作四条
            // ==================================================================
            private static void Actions()
            {
                Section("[V6S-CS] 动作四条：未接线/抛异常/找不到目标/空参数/延时语义/非 UI 线程");

                var log = new StressLog();
                var source = new FakeValueSource();
                var navigator = new FakeNavigator();

                // CS-01 Logon 未接线
                var bare = new ScadaActionDispatcher(log, source, navigator);
                bare.Dispatch(Hook(new ScadaAction { Type = ScadaActionType.Logon }), "按钮");
                Check("[V6S-CS01] 登录：没接登录窗 → 跳过并明说「未接入」，不假装成功",
                    log.Last().Contains("未接入登录窗"), log.Last());

                // CS-02/03 登录返回 true/false
                log.Clear();
                var okDispatcher = new ScadaActionDispatcher(log, source, navigator, loginPrompt: _ => true);
                okDispatcher.Dispatch(Hook(new ScadaAction { Type = ScadaActionType.Logon }), "按钮");
                string okLine = log.Last();
                log.Clear();
                var cancelDispatcher = new ScadaActionDispatcher(log, source, navigator, loginPrompt: _ => false);
                cancelDispatcher.Dispatch(Hook(new ScadaAction { Type = ScadaActionType.Logon }), "按钮");
                Check("[V6S-CS02] 登录成功记 Info、取消记跳过（两条措辞不同）",
                    okLine.StartsWith("I ", StringComparison.Ordinal) && log.Last().StartsWith("W ", StringComparison.Ordinal),
                    $"ok=\"{okLine}\" cancel=\"{log.Last()}\"");

                // CS-04 登录窗抛异常：分发器兜住，记 Warn 并按"未登录"处理（业务结果，不是程序故障）
                log.Clear();
                var throwDispatcher = new ScadaActionDispatcher(log, source, navigator, loginPrompt: _ => throw new InvalidOperationException("窗炸了"));
                bool threw = false;
                try { throwDispatcher.Dispatch(Hook(new ScadaAction { Type = ScadaActionType.Logon }), "按钮"); }
                catch { threw = true; }
                Check("[V6S-CS04] 登录窗抛异常：异常不外泄，记一条「登录窗异常」并如实说明",
                    !threw && log.Lines.Any(l => l.Contains("登录窗异常")),
                    string.Join(" | ", log.Lines));

                // CS-05/06/07 注销
                log.Clear();
                bare.Dispatch(Hook(new ScadaAction { Type = ScadaActionType.Logoff }), "按钮");
                bool noSession = log.Last().Contains("未接入用户会话");

                log.Clear();
                var okSession = new ScadaActionDispatcher(log, source, navigator,
                    userSession: new StressUserSession { LoggedIn = true, UserName = "张三", LogoutOk = true });
                okSession.Dispatch(Hook(new ScadaAction { Type = ScadaActionType.Logoff }), "按钮");
                bool okLogout = log.Last().Contains("张三");

                log.Clear();
                var failSession = new ScadaActionDispatcher(log, source, navigator,
                    userSession: new StressUserSession { LoggedIn = true, UserName = "李四", LogoutOk = false, LogoutError = "会话已失效" });
                failSession.Dispatch(Hook(new ScadaAction { Type = ScadaActionType.Logoff }), "按钮");
                Check("[V6S-CS05] 注销：未接线跳过 / 成功带原用户名 / 失败报原因",
                    noSession && okLogout && log.Last().Contains("会话已失效"),
                    $"noSession={noSession} ok={okLogout} fail=\"{log.Last()}\"");

                // CS-06 切换语言
                var langDoc = new ScadaDocument();
                var zh = langDoc.AddLanguage("中文");
                var en = langDoc.AddLanguage("English");
                var langService = new ScadaLanguageService(langDoc);

                log.Clear();
                new ScadaActionDispatcher(log, source, navigator, languages: langService)
                    .Dispatch(Hook(new ScadaAction { Type = ScadaActionType.SetLanguage, LanguageId = en.LanguageId, LanguageName = "English" }), "按钮");
                bool byId = langDoc.CurrentLanguageId == en.LanguageId;

                log.Clear();
                new ScadaActionDispatcher(log, source, navigator, languages: langService)
                    .Dispatch(Hook(new ScadaAction { Type = ScadaActionType.SetLanguage, LanguageName = "中文" }), "按钮");
                bool byName = langDoc.CurrentLanguageId == zh.LanguageId;

                log.Clear();
                new ScadaActionDispatcher(log, source, navigator, languages: langService)
                    .Dispatch(Hook(new ScadaAction { Type = ScadaActionType.SetLanguage, LanguageName = "不存在的语言" }), "按钮");
                bool missingFail = log.Last().StartsWith("E ", StringComparison.Ordinal);

                log.Clear();
                new ScadaActionDispatcher(log, source, navigator, languages: langService)
                    .Dispatch(Hook(new ScadaAction { Type = ScadaActionType.SetLanguage }), "按钮");
                bool noTarget = log.Last().Contains("未选语言");

                Check("[V6S-CS06] 切换语言：按 Id / 按名 / 目标不存在 / 没选目标 四种各走对分支",
                    byId && byName && missingFail && noTarget,
                    $"id={byId} name={byName} missing={missingFail} none={noTarget}");

                // CS-07 语言服务未接线
                log.Clear();
                bare.Dispatch(Hook(new ScadaAction { Type = ScadaActionType.SetLanguage, LanguageName = "中文" }), "按钮");
                Check("[V6S-CS07] 语言服务未接线：跳过并明说「未接入语言服务」",
                    log.Last().Contains("未接入语言服务"), log.Last());

                // CS-08 延时参数非法
                log.Clear();
                bare.Dispatch(Hook(new ScadaAction { Type = ScadaActionType.Delay }), "按钮");
                bool nullDelay = log.Last().Contains("未填毫秒");
                log.Clear();
                bare.Dispatch(Hook(new ScadaAction { Type = ScadaActionType.Delay, DelayMs = 0 }), "按钮");
                bool zeroDelay = log.Last().Contains("未填毫秒");
                log.Clear();
                bare.Dispatch(Hook(new ScadaAction { Type = ScadaActionType.Delay, DelayMs = -100 }), "按钮");
                bool negDelay = log.Last().Contains("未填毫秒");
                Check("[V6S-CS08] 延时：null / 0 / 负数 都按「未填」跳过",
                    nullDelay && zeroDelay && negDelay, $"{nullDelay}/{zeroDelay}/{negDelay}");

                // CS-09 延时正常：后续动作延后执行
                log.Clear();
                var delayHook = Hook(
                    new ScadaAction { Type = ScadaActionType.Log, Text = "前" },
                    new ScadaAction { Type = ScadaActionType.Delay, DelayMs = 60 },
                    new ScadaAction { Type = ScadaActionType.Log, Text = "后" });
                bare.Dispatch(delayHook, "按钮");
                int immediate = log.Lines.Count;
                PumpDispatcher(300);
                Check("[V6S-CS09] 延时 60ms：前一条立即执行、后一条在延时后才执行（日志含「延时后」）",
                    immediate == 2 && log.Lines.Count == 3 && log.Lines[2].Contains("后"),
                    $"immediate={immediate} total={log.Lines.Count} / {string.Join(" | ", log.Lines)}");

                // CS-10 延时是最后一条
                log.Clear();
                bare.Dispatch(Hook(new ScadaAction { Type = ScadaActionType.Delay, DelayMs = 30 }), "按钮");
                PumpDispatcher(150);
                Check("[V6S-CS10] 延时是最后一条：正常排队、不抛、没有多余动作",
                    log.Lines.Count == 1 && log.Lines[0].StartsWith("I ", StringComparison.Ordinal),
                    string.Join(" | ", log.Lines));

                // CS-11 非 UI 线程调用延时：必须带"后续动作"才会走到 Dispatcher 分支
                //（只给一条延时的话会先命中"没有后续动作"，等于没测到这条路径）
                log.Clear();
                Exception? threadFailure = null;
                var worker = new Thread(() =>
                {
                    try
                    {
                        bare.Dispatch(Hook(
                            new ScadaAction { Type = ScadaActionType.Delay, DelayMs = 30 },
                            new ScadaAction { Type = ScadaActionType.Log, Text = "后" }), "按钮");
                    }
                    catch (Exception ex) { threadFailure = ex; }
                });
                worker.Start();
                worker.Join();
                Check("[V6S-CS11] 非 UI 线程调延时：不抛，按「只能在 UI 线程排队」跳过",
                    threadFailure == null && log.Lines.Any(l => l.Contains("UI 线程")),
                    threadFailure?.Message ?? string.Join(" | ", log.Lines));

                // CS-12 枚举/文案边界
                var noLang = new ScadaAction { Type = ScadaActionType.SetLanguage };
                var noDelay = new ScadaAction { Type = ScadaActionType.Delay };
                Check("[V6S-CS12] 文案边界：未选语言/未填毫秒 在 Detail 与 Describe 里都说得清",
                    noLang.Detail == string.Empty && noLang.Describe().Contains("未选语言")
                    && noDelay.Detail == string.Empty && noDelay.Describe().Contains("未填毫秒"),
                    $"{noLang.Describe()} / {noDelay.Describe()}");

                // CS-13 序列化往返
                var action = new ScadaAction
                {
                    Type = ScadaActionType.SetLanguage,
                    LanguageId = en.LanguageId,
                    LanguageName = "English",
                    DelayMs = 250,
                };
                var json = Newtonsoft.Json.JsonConvert.SerializeObject(action);
                var back = Newtonsoft.Json.JsonConvert.DeserializeObject<ScadaAction>(json)!;
                Check("[V6S-CS13] 序列化往返：LanguageId/LanguageName/DelayMs 不丢",
                    back.LanguageId == en.LanguageId && back.LanguageName == "English" && back.DelayMs == 250,
                    json.Length > 300 ? json.Substring(0, 300) : json);

                // CS-14 枚举连续性
                Check("[V6S-CS14] 枚举 1..32 连续、四个新值各就各位",
                    Enum.GetValues<ScadaActionType>().Select(v => (int)v).SequenceEqual(Enumerable.Range(1, 32))
                    && (int)ScadaActionType.Logon == 29 && (int)ScadaActionType.Logoff == 30
                    && (int)ScadaActionType.SetLanguage == 31 && (int)ScadaActionType.Delay == 32,
                    string.Join(",", Enum.GetValues<ScadaActionType>().Select(v => (int)v)));
            }

            private static ScadaEventHook Hook(params ScadaAction[] actions)
            {
                var hook = new ScadaEventHook { Event = ScadaEventType.Pressed };
                foreach (var action in actions) hook.Actions.Add(action);
                return hook;
            }

            /// <summary>数一个标记在串里出现几次（F2：链式延时不许把「（延时后）」叠起来）</summary>
            private static int CountOccurrences(string text, string token)
            {
                if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(token))
                    return 0;

                int count = 0, index = 0;
                while ((index = text.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
                {
                    count++;
                    index += token.Length;
                }

                return count;
            }

            // ---------------------------------------------------------------- 夹具

            /// <summary>线程安全的日志捕获（延时回调可能落在别的线程上）</summary>
            private sealed class StressLog : Core.Interfaces.ILogService
            {
                private readonly object _gate = new();
                private readonly List<string> _lines = new();

                public IReadOnlyList<string> Lines { get { lock (_gate) return _lines.ToArray(); } }

                public string Last() { lock (_gate) return _lines.Count == 0 ? string.Empty : _lines[_lines.Count - 1]; }

                public void Clear() { lock (_gate) _lines.Clear(); }

                private void Add(string level, string message)
                {
                    lock (_gate) _lines.Add(level + " " + message);
                }

                public void Success(params string[] messages) { foreach (var m in messages) Add("S", m); }
                public void Error(params Exception[] messages) { foreach (var m in messages) Add("E", m?.Message ?? "null"); }
                public void Error(params string[] messages) { foreach (var m in messages) Add("E", m); }
                public void Info(params string[] messages) { foreach (var m in messages) Add("I", m); }
                public void Warn(params string[] messages) { foreach (var m in messages) Add("W", m); }
            }

            private sealed class StressUserSession : IScadaUserSession
            {
                public bool LoggedIn { get; set; }
                public string? UserName { get; set; }
                public bool LogoutOk { get; set; }
                public string? LogoutError { get; set; }

                public bool IsLoggedIn => LoggedIn;
                public string? CurrentUserName => UserName;

                public bool TryLogout(out string? error)
                {
                    error = LogoutOk ? null : LogoutError;
                    if (LogoutOk) LoggedIn = false;
                    return LogoutOk;
                }
            }

            /// <summary>拒绝一切非 Always 权限的策略（验证"无权限时按钮直接不可用"）</summary>
            private sealed class DenyPolicy : IScadaAccessPolicy
            {
                public string CurrentGroupId => string.Empty;
                public string CurrentGroupName => "未登录";
                public IReadOnlyList<string> Permissions => Array.Empty<string>();
                public bool IsLoggedIn => false;
                public string CurrentUserName => "未登录";
                public string LoginStateText => "未登录";

                public bool Can(string requiredKey, out string? reason)
                {
                    if (requiredKey == ScadaPermissionKeys.Always) { reason = null; return true; }
                    reason = "当前账号没有该权限";
                    return false;
                }
            }
        }
    }
}
