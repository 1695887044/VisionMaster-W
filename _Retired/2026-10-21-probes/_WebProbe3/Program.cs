// _WebProbe3 — M0 thread-model probe (isolated experiment, no production code touched).
// Question: does "network threads -> per-session bounded queue -> ONE global consumer ->
// UI dispatcher -> single audit writer" hold up on .NET 9 WPF without deadlock,
// interleaving or cross-thread exceptions?
//
// Design notes that matter for the result:
//  * The consumer loop runs on a thread-pool thread (SynchronizationContext == null).
//    If it ran on the UI thread the `await` would capture DispatcherSynchronizationContext
//    and re-enter the dispatcher -> classic self-deadlock.
//  * The StreamWriter is opened exactly once and only touched from the UI thread.
//  * Debug.Assert uses a recording TraceListener (DefaultTraceListener would pop a modal
//    dialog and hang a headless run).

using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Channels;
using System.Windows.Threading;

namespace WebProbe3;

internal sealed record Command(int Id, string Producer, string Payload, long EnqueuedTicks);

internal sealed class AssertRecorder : TraceListener
{
    private readonly List<string> _messages = new();
    private int _printed;

    public IReadOnlyList<string> Messages
    {
        get { lock (_messages) { return _messages.ToArray(); } }
    }

    public override void Write(string? message) { }

    public override void WriteLine(string? message)
    {
        if (message is null) return;
        lock (_messages) { _messages.Add(message); }
        if (Interlocked.Increment(ref _printed) <= 5) Console.WriteLine("[ASSERT-LINE] " + message);
    }

    public override void Fail(string? message, string? detailMessage)
    {
        string text = (message ?? "") + " | " + (detailMessage ?? "");
        lock (_messages) { _messages.Add(text); }
        if (Interlocked.Increment(ref _printed) <= 5) Console.WriteLine("[ASSERT-FAIL] " + text);
    }
}

internal sealed class ExecStats
{
    public int Count;
    public int CheckAccessFailures;
    public readonly List<double> LatenciesMs = new();
    public readonly HashSet<int> ThreadIds = new();
    public readonly List<string> Exceptions = new();
}

internal sealed class Round1Stats
{
    public long AttemptsA;
    public long AttemptsB;
    public long SpinsA;
    public long SpinsB;
    public bool ConsumerSyncContextNull;
    public readonly List<string> ConsumerErrors = new();
    public double TotalMs;
    public long MutationExtraWrites;
    public readonly ExecStats Exec = new();
}

internal sealed class Round2Stats
{
    public long Accepted;
    public long Rejected;
    public int Executed;
    public readonly HashSet<int> ExecutedIds = new();
    public readonly List<string> ConsumerErrors = new();
    public double TotalMs;
    public double MaxQueueLatencyMs;
}

internal sealed class Check
{
    public required string Id;
    public required bool Pass;
    public required string Detail;
}

internal static class Program
{
    private const string ProjectDirFallback = @"D:\C#\VM\_WebProbe3";

    private static Dispatcher Ui = null!;
    private static Thread UiThread = null!;
    private static int UiThreadId;
    private static string OutDir = "";
    private static readonly AssertRecorder Asserts = new();
    private static readonly List<Check> Checks = new();

    // --mutation: deliberately break the model (execute on the consumer thread + a second CSV
    // writer on the producer threads) so we can prove checks A/B/C are able to go red.
    private static bool MutationMode;

    private static int Main(string[] args)
    {
        try { Console.OutputEncoding = new UTF8Encoding(false); } catch { /* redirected / no console */ }
        MutationMode = args.Any(a => string.Equals(a, "--mutation", StringComparison.OrdinalIgnoreCase));
        Trace.Listeners.Clear();
        Trace.Listeners.Add(Asserts);

        OutDir = ResolveOutDir();
        Directory.CreateDirectory(OutDir);
        Console.WriteLine("=== _WebProbe3  M0 thread-model probe ===");
        Console.WriteLine("OutDir: " + OutDir);
        Console.WriteLine("Mode: " + (MutationMode ? "MUTATION (expected A/B/C red)" : "normal"));
        Console.WriteLine("Runtime: " + Environment.Version + "  OS: " + Environment.OSVersion.VersionString);
        Console.WriteLine("Cores: " + Environment.ProcessorCount);

        StartWatchdog(180);

        var exit = 0;
        var total = Stopwatch.StartNew();
        try
        {
            RunAllAsync().GetAwaiter().GetResult();
            exit = Checks.Any(c => !c.Pass) ? 1 : 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("[FATAL] " + ex);
            Checks.Add(new Check { Id = "X0", Pass = false, Detail = "探针自身异常: " + ex.GetType().Name + ": " + ex.Message });
            exit = 2;
        }

        Console.WriteLine();
        Console.WriteLine("--- 汇总 ---");
        Console.WriteLine("总墙钟耗时: " + total.Elapsed.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture) + "ms");
        foreach (var c in Checks)
        {
            Console.WriteLine("[" + (c.Pass ? "PASS" : "FAIL") + "] " + c.Id + " " + c.Detail);
        }

        int pass = Checks.Count(c => c.Pass);
        int fail = Checks.Count - pass;
        Console.WriteLine("RESULT: " + pass + " PASS / " + fail + " FAIL");
        return fail == 0 ? exit : 1;
    }

    private static string ResolveOutDir()
    {
        // bin/Debug/net9.0-windows/ -> project dir -> out/
        try
        {
            var candidate = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "out"));
            if (Directory.Exists(Path.GetDirectoryName(candidate))) return candidate;
        }
        catch
        {
            // fall through
        }

        return Path.Combine(ProjectDirFallback, "out");
    }

    private static void StartWatchdog(int seconds)
    {
        var t = new Thread(() =>
        {
            Thread.Sleep(TimeSpan.FromSeconds(seconds));
            Console.WriteLine("[WATCHDOG] 超过 " + seconds + " 秒仍未结束 —— 判定挂死");
            Console.Out.Flush();
            Environment.Exit(4);
        })
        { IsBackground = true, Name = "Watchdog" };
        t.Start();
    }

    private static async Task RunAllAsync()
    {
        StartUiThread();

        // 0) 基础设施测量（后面解释数字时要用）
        MeasureSleepGranularity();

        Console.WriteLine();
        Console.WriteLine("### Round 1: 2 producers x 500, capacity 256, 1ms per command ###");
        var r1 = await Round1Async();
        ValidateRound1(r1);

        Console.WriteLine();
        Console.WriteLine("### Round 2: bounded queue saturation (capacity 8, 20ms consumer, no retry) ###");
        var r2 = await Round2Async();
        ValidateRound2(r2);

        Console.WriteLine();
        Console.WriteLine("### Round 2b: BoundedChannelFullMode.DropWrite — TryWrite 会不会静默丢 ###");
        ValidateDropWrite();

        Console.WriteLine();
        Console.WriteLine("### Round 3: dispatcher timeout / abort semantics ###");
        await Round3Async();

        Console.WriteLine();
        Console.WriteLine("### Round 4: queue latency while the UI thread is busy ###");
        await Round4Async();

        Console.WriteLine();
        Console.WriteLine("### Round 5: production topology — 4 per-session bounded queues -> ONE global pump ###");
        await Round5Async();

        // shutdown
        Console.WriteLine();
        Console.WriteLine("### Shutdown ###");
        Ui.InvokeShutdown();
        bool joined = UiThread.Join(TimeSpan.FromSeconds(10));
        Console.WriteLine("dispatcher InvokeShutdown -> thread joined=" + joined);
        Checks.Add(new Check
        {
            Id = "G1",
            Pass = joined,
            Detail = "InvokeShutdown 后 UI 线程 10s 内退出（joined=" + joined + "）"
        });
    }

    private static void StartUiThread()
    {
        using var ready = new ManualResetEventSlim(false);
        Dispatcher? dispatcher = null;

        var thread = new Thread(() =>
        {
            dispatcher = Dispatcher.CurrentDispatcher;
            UiThreadId = Environment.CurrentManagedThreadId;
            ready.Set();
            Dispatcher.Run();
        })
        { IsBackground = true, Name = "ProbeUI" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        if (!ready.Wait(TimeSpan.FromSeconds(15)) || dispatcher is null)
        {
            throw new TimeoutException("UI 线程未在 15s 内就绪");
        }

        Ui = dispatcher!;
        UiThread = thread;
        Ui.UnhandledException += (_, e) =>
        {
            Console.WriteLine("[UI-UNHANDLED] " + e.Exception.GetType().Name + ": " + e.Exception.Message);
            e.Handled = false;
        };

        Console.WriteLine("UI thread: id=" + UiThreadId + " apartment=" + thread.GetApartmentState() +
                          " dispatcher.ThreadId=" + Ui.Thread.ManagedThreadId);
        Checks.Add(new Check
        {
            Id = "G0",
            Pass = Ui.Thread.ManagedThreadId == UiThreadId && thread.GetApartmentState() == ApartmentState.STA,
            Detail = "STA 线程 + Dispatcher.Run() 就绪（tid=" + UiThreadId + "）"
        });
    }

    // ---------------------------------------------------------------------
    // Round 1 — the actual production shape
    // ---------------------------------------------------------------------
    private static async Task<Round1Stats> Round1Async()
    {
        const int PerProducer = 500;
        const int Capacity = 256;

        var stats = new Round1Stats();
        string path = Round1CsvPath;

        var channel = Channel.CreateBounded<Command>(new BoundedChannelOptions(Capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false
        });

        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        using var csv = new StreamWriter(fs, new UTF8Encoding(false), 64 * 1024) { AutoFlush = false, NewLine = "\n" };
        csv.Write("Id,Producer,ManagedThreadId,CheckAccess,Payload\n");
        csv.Flush();

        var sw = Stopwatch.StartNew();

        // ONE global consumer.
        Task consumer = Task.Run(async () =>
        {
            stats.ConsumerSyncContextNull = SynchronizationContext.Current is null;
            await foreach (Command cmd in channel.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                try
                {
                    if (MutationMode)
                    {
                        // MUTATION M1: run the domain call on the consumer thread (no dispatcher hop).
                        ExecuteCommand(cmd, csv, stats.Exec);
                    }
                    else
                    {
                        DispatcherOperation op = Ui.InvokeAsync(() => ExecuteCommand(cmd, csv, stats.Exec));
                        await op.Task.ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    stats.ConsumerErrors.Add(ex.GetType().Name + ": " + ex.Message);
                }
            }
        });

        void Produce(bool isA, string name, int firstId)
        {
            for (int i = 0; i < PerProducer; i++)
            {
                if (MutationMode && (i % 25) == 0)
                {
                    // MUTATION M2: a second/third writer on the same StreamWriter (the thing the
                    // single-writer rule forbids). 300 chars makes tearing near-certain.
                    try
                    {
                        csv.Write("MUT," + new string('x', 300) + "\n");
                        csv.Flush();
                    }
                    catch
                    {
                        // a corrupted StreamWriter is exactly what we expect here
                    }

                    Interlocked.Increment(ref stats.MutationExtraWrites);
                }

                int id = firstId + i;
                string payload = PayloadFor(id);
                while (true)
                {
                    if (isA) Interlocked.Increment(ref stats.AttemptsA);
                    else Interlocked.Increment(ref stats.AttemptsB);

                    var cmd = new Command(id, name, payload, Stopwatch.GetTimestamp());
                    if (channel.Writer.TryWrite(cmd)) break;

                    if (isA) Interlocked.Increment(ref stats.SpinsA);
                    else Interlocked.Increment(ref stats.SpinsB);
                    Thread.Yield();
                }
            }
        }

        var producerA = new Thread(() => Produce(true, "A", 0)) { IsBackground = true, Name = "ProducerA" };
        var producerB = new Thread(() => Produce(false, "B", PerProducer)) { IsBackground = true, Name = "ProducerB" };
        producerA.Start();
        producerB.Start();

        bool joinedA = producerA.Join(TimeSpan.FromSeconds(60));
        bool joinedB = producerB.Join(TimeSpan.FromSeconds(60));
        Console.WriteLine("producers joined: A=" + joinedA + " B=" + joinedB);

        channel.Writer.Complete();
        await consumer.WaitAsync(TimeSpan.FromSeconds(60)).ConfigureAwait(false);
        sw.Stop();
        stats.TotalMs = sw.Elapsed.TotalMilliseconds;

        csv.Flush();
        fs.Flush(true);

        Console.WriteLine("elapsed=" + F(stats.TotalMs) + "ms  executed=" + stats.Exec.Count +
                          "  consumerErrors=" + stats.ConsumerErrors.Count +
                          "  checkAccessFailures=" + stats.Exec.CheckAccessFailures +
                          "  execExceptions=" + stats.Exec.Exceptions.Count);
        Console.WriteLine("tryWrite attempts: A=" + stats.AttemptsA + " B=" + stats.AttemptsB +
                          "  backpressure yields: A=" + stats.SpinsA + " B=" + stats.SpinsB);
        Console.WriteLine("consumer SynchronizationContext==null: " + stats.ConsumerSyncContextNull);
        if (MutationMode)
        {
            Console.WriteLine("MUTATION: extra raw CSV writes from producer threads=" + stats.MutationExtraWrites +
                              "  (executed on consumer thread, no dispatcher hop)");
        }
        PrintLatency("Round1 queue latency (enqueue->execute start)", stats.Exec.LatenciesMs);
        Console.WriteLine("CSV: " + path);

        return stats;
    }

    private static void ExecuteCommand(Command cmd, StreamWriter csv, ExecStats exec)
    {
        try
        {
            long started = Stopwatch.GetTimestamp();
            bool access = Ui.CheckAccess();
            Debug.Assert(access, "Execute 不在 UI 线程上执行（跨线程碰 UI）");

            if (!access) exec.CheckAccessFailures++;
            exec.LatenciesMs.Add(Stopwatch.GetElapsedTime(cmd.EnqueuedTicks, started).TotalMilliseconds);
            exec.ThreadIds.Add(Environment.CurrentManagedThreadId);

            // ONE writer, touched only from this (UI) thread.
            csv.Write(cmd.Id.ToString(CultureInfo.InvariantCulture));
            csv.Write(',');
            csv.Write(cmd.Producer);
            csv.Write(',');
            csv.Write(Environment.CurrentManagedThreadId.ToString(CultureInfo.InvariantCulture));
            csv.Write(',');
            csv.Write(access ? "True" : "False");
            csv.Write(',');
            csv.Write(CsvEscape(cmd.Payload));
            csv.Write('\n');

            exec.Count++;
            SpinWork(1.0); // simulated 1ms of domain work
        }
        catch (Exception ex)
        {
            exec.Exceptions.Add(ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static string ReadAllTextShared(string path)
    {
        // the writer may still be open (FileShare.Read only permits readers that also share write)
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var sr = new StreamReader(fs, Encoding.UTF8);
        return sr.ReadToEnd();
    }

    private static string Round1CsvPath =>
        Path.Combine(OutDir, MutationMode ? "round1-commands-mutation.csv" : "round1-commands.csv");

    private static void ValidateRound1(Round1Stats stats)
    {
        string path = Round1CsvPath;
        string text = ReadAllTextShared(path);

        List<string[]> records;
        string? parseError = null;
        try
        {
            records = Csv.Parse(text);
        }
        catch (Exception ex)
        {
            records = new List<string[]>();
            parseError = ex.Message;
        }

        string[] header = records.Count > 0 ? records[0] : Array.Empty<string>();
        var body = records.Skip(1).ToList();

        // A. 1000 rows, unique ids, nothing lost.
        var ids = body.Where(r => r.Length >= 1 && int.TryParse(r[0], out _))
                      .Select(r => int.Parse(r[0], CultureInfo.InvariantCulture))
                      .ToList();
        var idSet = new HashSet<int>(ids);
        bool idsExact = idSet.Count == 1000 && ids.Count == 1000 && idSet.Min() == 0 && idSet.Max() == 999;
        bool countOk = body.Count == 1000 && stats.Exec.Count == 1000;
        Checks.Add(new Check
        {
            Id = "A",
            Pass = countOk && idsExact && parseError is null,
            Detail = "1000 行/唯一 Id 0..999 无丢：records=" + body.Count + " parsedIds=" + ids.Count +
                     " unique=" + idSet.Count + " executed=" + stats.Exec.Count +
                     (parseError is null ? "" : " parseError=" + parseError)
        });

        // B. every row CheckAccess=True, and exactly one thread ever executed.
        int badAccess = body.Count(r => r.Length < 4 || r[3] != "True");
        var threadIds = new HashSet<int>(body.Where(r => r.Length >= 3 && int.TryParse(r[2], out _))
                                            .Select(r => int.Parse(r[2], CultureInfo.InvariantCulture)));
        bool onlyUiThread = threadIds.Count == 1 && threadIds.Contains(UiThreadId);
        Checks.Add(new Check
        {
            Id = "B",
            Pass = badAccess == 0 && onlyUiThread && stats.Exec.CheckAccessFailures == 0 &&
                   Asserts.Messages.Count == 0,
            Detail = "CheckAccess=True 行=" + (body.Count - badAccess) + "/" + body.Count +
                     " 执行线程数=" + threadIds.Count + " (" + string.Join(",", threadIds) + ")" +
                     " Debug.Assert 失败=" + Asserts.Messages.Count
        });

        // C. strict CSV: shape + escaping round-trip + no torn/interleaved record.
        int badShape = body.Count(r => r.Length != 5);
        int badPayload = 0;
        int badProducer = 0;
        foreach (string[] r in body)
        {
            if (r.Length != 5) continue;
            if (!int.TryParse(r[0], out int id)) continue;
            if (r[4] != PayloadFor(id)) badPayload++;
            string expectedProducer = id < 500 ? "A" : "B";
            if (r[1] != expectedProducer) badProducer++;
        }

        bool headerOk = header.Length == 5 && header[0] == "Id" && header[1] == "Producer" &&
                        header[2] == "ManagedThreadId" && header[3] == "CheckAccess" && header[4] == "Payload";
        Checks.Add(new Check
        {
            Id = "C",
            Pass = parseError is null && headerOk && badShape == 0 && badPayload == 0 && badProducer == 0,
            Detail = "严格 CSV 解析：header=" + headerOk + " 列数≠5 行=" + badShape +
                     " payload 回读不符=" + badPayload + " producer 不符=" + badProducer +
                     " (含逗号/引号的 Payload 必须原样还原)"
        });

        // D. no deadlock / bounded wall time.
        Checks.Add(new Check
        {
            Id = "D",
            Pass = stats.TotalMs < 10_000,
            Detail = "总耗时=" + F(stats.TotalMs) + "ms (<10000) 无挂死；单条吞吐≈" +
                     F(1000.0 / Math.Max(0.001, stats.TotalMs / 1000.0)) + " 条/秒"
        });

        // Extra: producer backpressure observed (bounded queue really was full at times).
        Checks.Add(new Check
        {
            Id = "D2",
            Pass = stats.SpinsA + stats.SpinsB > 0,
            Detail = "有界队列确实施压过生产者：TryWrite 失败重试次数=" + (stats.SpinsA + stats.SpinsB) +
                     "（说明 256 容量 + 1ms/条 会顶满）"
        });

        Checks.Add(new Check
        {
            Id = "D3",
            Pass = stats.ConsumerSyncContextNull && stats.ConsumerErrors.Count == 0 && stats.Exec.Exceptions.Count == 0,
            Detail = "消费者无 SynchronizationContext（不会重入 UI 线程）=" + stats.ConsumerSyncContextNull +
                     " consumerErrors=" + stats.ConsumerErrors.Count + " execExceptions=" + stats.Exec.Exceptions.Count
        });

        if (parseError is not null) Console.WriteLine("[C] parseError: " + parseError);
        Console.WriteLine("[C] CSV 校验明细: headerOk=" + headerOk + " body=" + body.Count +
                          " badShape=" + badShape + " badPayload=" + badPayload + " badProducer=" + badProducer +
                          " badAccess=" + badAccess);
        Console.WriteLine("[C] 首行: " + (body.Count > 0 ? string.Join(" | ", body[0]) : "<empty>"));
    }

    // ---------------------------------------------------------------------
    // Round 2 — saturation / rejection accounting
    // ---------------------------------------------------------------------
    private static async Task<Round2Stats> Round2Async()
    {
        const int Capacity = 8;
        const int PerProducer = 500;

        var stats = new Round2Stats();
        var channel = Channel.CreateBounded<Command>(new BoundedChannelOptions(Capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });

        var sw = Stopwatch.StartNew();
        Task consumer = Task.Run(async () =>
        {
            await foreach (Command cmd in channel.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                try
                {
                    await Ui.InvokeAsync(() =>
                    {
                        stats.ExecutedIds.Add(cmd.Id);
                        stats.Executed++;
                        double lat = Stopwatch.GetElapsedTime(cmd.EnqueuedTicks).TotalMilliseconds;
                        if (lat > stats.MaxQueueLatencyMs) stats.MaxQueueLatencyMs = lat;
                        Thread.Sleep(20); // slow consumer
                    }).Task.ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    stats.ConsumerErrors.Add(ex.GetType().Name + ": " + ex.Message);
                }
            }
        });

        void Produce(string name, int firstId)
        {
            for (int i = 0; i < PerProducer; i++)
            {
                var cmd = new Command(firstId + i, name, PayloadFor(firstId + i), Stopwatch.GetTimestamp());
                if (channel.Writer.TryWrite(cmd)) Interlocked.Increment(ref stats.Accepted);
                else
                {
                    Interlocked.Increment(ref stats.Rejected);
                    Thread.Yield();
                }
            }
        }

        var a = new Thread(() => Produce("A", 0)) { IsBackground = true, Name = "ProducerA2" };
        var b = new Thread(() => Produce("B", PerProducer)) { IsBackground = true, Name = "ProducerB2" };
        a.Start();
        b.Start();
        bool ja = a.Join(TimeSpan.FromSeconds(60));
        bool jb = b.Join(TimeSpan.FromSeconds(60));

        channel.Writer.Complete();
        await consumer.WaitAsync(TimeSpan.FromSeconds(60)).ConfigureAwait(false);
        sw.Stop();
        stats.TotalMs = sw.Elapsed.TotalMilliseconds;

        Console.WriteLine("producers joined A=" + ja + " B=" + jb +
                          "  attempts=" + (PerProducer * 2) +
                          "  accepted=" + stats.Accepted + "  rejected=" + stats.Rejected +
                          "  executed=" + stats.Executed + "  elapsed=" + F(stats.TotalMs) + "ms");
        Console.WriteLine("executedIds=" + stats.ExecutedIds.Count +
                          "  maxQueueLatency=" + F(stats.MaxQueueLatencyMs) + "ms" +
                          "  consumerErrors=" + stats.ConsumerErrors.Count);
        return stats;
    }

    private static void ValidateRound2(Round2Stats stats)
    {
        bool rejectedAtLeastOnce = stats.Rejected > 0;
        bool accounting = stats.Accepted + stats.Rejected == 1000;
        bool noSilentLoss = stats.Executed == stats.Accepted && stats.ExecutedIds.Count == (int)stats.Accepted;
        bool noException = stats.ConsumerErrors.Count == 0;

        Checks.Add(new Check
        {
            Id = "E",
            Pass = rejectedAtLeastOnce && accounting && noSilentLoss && noException,
            Detail = "有界满时 TryWrite=false：rejected=" + stats.Rejected + " accepted=" + stats.Accepted +
                     " accepted+rejected=" + (stats.Accepted + stats.Rejected) +
                     " executed=" + stats.Executed + "（已接受零丢失=" + noSilentLoss +
                     "） 未抛异常=" + noException
        });
    }

    private static void ValidateDropWrite()
    {
        var channel = Channel.CreateBounded<int>(new BoundedChannelOptions(4)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropWrite
        });

        int tryWriteTrue = 0;
        for (int i = 0; i < 100; i++)
        {
            if (channel.Writer.TryWrite(i)) tryWriteTrue++;
        }

        channel.Writer.Complete();
        int drained = 0;
        while (channel.Reader.TryRead(out _)) drained++;

        Console.WriteLine("DropWrite: TryWrite 返回 true=" + tryWriteTrue + "  实际可读出=" + drained);
        Checks.Add(new Check
        {
            Id = "E2",
            Pass = drained == 4 && tryWriteTrue >= drained,
            Detail = "FullMode=DropWrite 的实测：TryWrite 返回 true=" + tryWriteTrue + "，真正入队=" + drained +
                     "（静默丢 " + (tryWriteTrue - drained) + " 条）→ TryWrite 的返回值不能当“已入队”判据"
        });
    }

    // ---------------------------------------------------------------------
    // Round 3 — timeout / abort semantics
    // ---------------------------------------------------------------------
    private static async Task Round3Async()
    {
        // F1: token cancels while the callback is already EXECUTING.
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            long t0 = Stopwatch.GetTimestamp();
            var op = Ui.InvokeAsync(() => Thread.Sleep(3000), DispatcherPriority.Normal, cts.Token);
            string outcome;
            try
            {
                await op.Task.WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
                outcome = "completed";
            }
            catch (OperationCanceledException)
            {
                outcome = "canceled";
            }
            catch (TimeoutException)
            {
                outcome = "still-hung>20s";
            }

            double elapsed = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
            Console.WriteLine("F1 token(2s) while executing 3s work: outcome=" + outcome +
                              " elapsed=" + F(elapsed) + "ms tokenFired=" + cts.IsCancellationRequested +
                              " opStatus=" + op.Status);
            Checks.Add(new Check
            {
                Id = "F1",
                Pass = outcome == "completed" && elapsed >= 2500,
                Detail = "InvokeAsync(ct) 在回调已开始执行后取消【不会】缩短等待：outcome=" + outcome +
                         " elapsed=" + F(elapsed) + "ms（token 已触发=" + cts.IsCancellationRequested +
                         "）→ 取消只对 Pending 状态有效"
            });
        }

        // F1b: token cancels while PENDING -> operation is aborted, never runs.
        {
            bool ran = false;
            var blocker = Ui.InvokeAsync(() => Thread.Sleep(400), DispatcherPriority.Normal);
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(80));
            var op = Ui.InvokeAsync(() => { ran = true; }, DispatcherPriority.Normal, cts.Token);
            string statusQueued = op.Status.ToString();   // sampled before the 80ms token fires
            await Task.Delay(250).ConfigureAwait(false);
            string statusAfterCancel = op.Status.ToString();
            string outcome;
            try
            {
                await op.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                outcome = "completed";
            }
            catch (OperationCanceledException)
            {
                outcome = "canceled";
            }

            await blocker.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            await Task.Delay(50).ConfigureAwait(false);
            Console.WriteLine("F1b token(80ms) while pending behind a 400ms blocker: statusQueued=" +
                              statusQueued + " statusAfterCancel=" + statusAfterCancel +
                              " outcome=" + outcome + " callbackRan=" + ran);
            Checks.Add(new Check
            {
                Id = "F1b",
                Pass = statusQueued == "Pending" && outcome == "canceled" && !ran,
                Detail = "InvokeAsync(ct) 在 Pending 阶段取消【有效】：入队时=" + statusQueued +
                         "、取消后=" + statusAfterCancel + "、await 结果=" + outcome +
                         "、回调是否执行=" + ran
            });
        }

        // F2: WaitAsync bounds the WAIT, not the work.
        {
            long completedAt = -1;
            long t0 = Stopwatch.GetTimestamp();
            var op = Ui.InvokeAsync(() =>
            {
                Thread.Sleep(3000);
                completedAt = Stopwatch.GetTimestamp();
            }, DispatcherPriority.Normal);

            string outcome;
            double atTimeout = -1;
            try
            {
                await op.Task.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                outcome = "completed";
            }
            catch (TimeoutException)
            {
                outcome = "timeout";
                atTimeout = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
            }

            string statusAtTimeout = op.Status.ToString();
            // the abandoned operation still runs to completion on the UI thread
            await Task.Delay(2000).ConfigureAwait(false);
            double workCompletedMs = completedAt < 0 ? -1 : Stopwatch.GetElapsedTime(t0, completedAt).TotalMilliseconds;
            Console.WriteLine("F2 WaitAsync(2s) over 3s work: outcome=" + outcome +
                              " atTimeout=" + F(atTimeout) + "ms statusAtTimeout=" + statusAtTimeout +
                              " workCompletedAt=" + F(workCompletedMs) + "ms finalStatus=" + op.Status);
            Checks.Add(new Check
            {
                Id = "F2",
                Pass = outcome == "timeout" && atTimeout < 2600 && workCompletedMs >= 2500,
                Detail = "op.Task.WaitAsync(2s) 能把【等待】截断：timeout@" + F(atTimeout) +
                         "ms，但被放弃的操作仍在 UI 线程跑完（workCompleted@" + F(workCompletedMs) +
                         "ms, status=" + op.Status + "）→ 超时不等于“没执行”"
            });
        }

        // F3: explicit Abort() on a pending operation.
        {
            bool ran = false;
            var blocker = Ui.InvokeAsync(() => Thread.Sleep(400), DispatcherPriority.Normal);
            var op = Ui.InvokeAsync(() => { ran = true; }, DispatcherPriority.Normal);
            await Task.Delay(80).ConfigureAwait(false);
            string statusBefore = op.Status.ToString();
            bool aborted = op.Abort();
            string outcome;
            try
            {
                await op.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                outcome = "completed";
            }
            catch (OperationCanceledException)
            {
                outcome = "canceled";
            }

            await blocker.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            await Task.Delay(50).ConfigureAwait(false);
            Console.WriteLine("F3 Abort() while pending: statusBefore=" + statusBefore +
                              " abortReturned=" + aborted + " outcome=" + outcome + " callbackRan=" + ran);
            Checks.Add(new Check
            {
                Id = "F3",
                Pass = aborted && !ran && outcome == "canceled",
                Detail = "DispatcherOperation.Abort() 对 Pending 操作有效：abort()=" + aborted +
                         " 回调是否执行=" + ran + " await 结果=" + outcome
            });
        }

        // F4: Abort() while already executing.
        {
            var op = Ui.InvokeAsync(() => Thread.Sleep(300), DispatcherPriority.Normal);
            await Task.Delay(60).ConfigureAwait(false);
            string statusBefore = op.Status.ToString();
            bool aborted = op.Abort();
            string outcome;
            try
            {
                await op.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                outcome = "completed";
            }
            catch (OperationCanceledException)
            {
                outcome = "canceled";
            }

            Console.WriteLine("F4 Abort() while executing: statusBefore=" + statusBefore +
                              " abortReturned=" + aborted + " outcome=" + outcome);
            Checks.Add(new Check
            {
                Id = "F4",
                Pass = outcome == "completed",
                Detail = "DispatcherOperation.Abort() 对正在执行的操作【无效】：abort() 返回 " + aborted +
                         "，回调仍然跑完（结果=" + outcome + "）→ 无法从 UI 线程外部抢回控制权"
            });
        }

        // F5: negative control — synchronous Dispatcher.Invoke from a worker while the UI thread
        // is waiting on that worker. This is the deadlock the "await InvokeAsync" shape avoids.
        {
            using var uiInCallback = new ManualResetEventSlim(false);
            using var release = new ManualResetEventSlim(false);

            var blocker = Ui.InvokeAsync(() =>
            {
                uiInCallback.Set();
                release.Wait(TimeSpan.FromSeconds(10)); // UI thread held hostage on purpose
            }, DispatcherPriority.Normal);

            if (!uiInCallback.Wait(TimeSpan.FromSeconds(5)))
            {
                Console.WriteLine("F5 setup failed: UI callback never started");
            }

            Task worker = Task.Run(() => Ui.Invoke(() => { }, DispatcherPriority.Normal));
            Task winner = await Task.WhenAny(worker, Task.Delay(1500)).ConfigureAwait(false);
            bool stuck = winner != worker;
            Console.WriteLine("F5 sync Invoke while UI blocked: stuckAfter1.5s=" + stuck);

            release.Set();
            bool unwound = true;
            try
            {
                await blocker.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                await worker.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                unwound = false;
                Console.WriteLine("F5 unwind failed: " + ex.GetType().Name + ": " + ex.Message);
            }

            Checks.Add(new Check
            {
                Id = "F5",
                Pass = stuck && unwound,
                Detail = "负对照：工作线程用【同步】Dispatcher.Invoke 派发、而 UI 线程正阻塞等待该工作线程 → " +
                         "1.5s 内不返回（stuck=" + stuck + "），释放后能解开=" + unwound +
                         "；本设计的 await InvokeAsync 不阻塞调用线程，所以不落这个坑"
            });
        }
    }

    // ---------------------------------------------------------------------
    // Round 4 — queue latency while the UI thread is occupied
    // ---------------------------------------------------------------------
    private static async Task Round4Async()
    {
        var latencies = new List<double>();
        long blockerStart = Stopwatch.GetTimestamp();

        var blocker = Ui.InvokeAsync(() => Thread.Sleep(800), DispatcherPriority.Normal);
        await Task.Delay(60).ConfigureAwait(false); // let the blocker start

        for (int i = 0; i < 10; i++)
        {
            long enqueued = Stopwatch.GetTimestamp();
            await Ui.InvokeAsync(() =>
            {
                latencies.Add(Stopwatch.GetElapsedTime(enqueued).TotalMilliseconds);
                SpinWork(1.0);
            }).Task.ConfigureAwait(false);
        }

        await blocker.Task.WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
        double blockerMs = Stopwatch.GetElapsedTime(blockerStart).TotalMilliseconds;

        PrintLatency("Round4 latency behind an 800ms blocker", latencies);
        double max = latencies.Count == 0 ? 0 : latencies.Max();
        Checks.Add(new Check
        {
            Id = "H1",
            Pass = max >= 600 && max < 3000,
            Detail = "UI 线程被 800ms 慢命令占住（实测 " + F(blockerMs) + "ms）时，第 1 条命令排队 " +
                     F(max) + "ms、其余 " + (latencies.Count - 1) + " 条 " + F(latencies.Count > 1 ? latencies[1] : 0) +
                     "ms（消费者串行 await，只有排在最前面的那条吃满阻塞）→ 命令延迟直接相加，成本是 UI 占用时长"
        });
    }

    // ---------------------------------------------------------------------
    // Round 5 — the exact production topology:
    //   N per-session bounded queues -> ONE global pump -> UI dispatcher -> ONE audit writer
    // ---------------------------------------------------------------------
    private static async Task Round5Async()
    {
        const int Sessions = 4;
        const int PerSession = 250;
        const int Capacity = 64;

        var channels = new List<Channel<Command>>(Sessions);
        for (int i = 0; i < Sessions; i++)
        {
            channels.Add(Channel.CreateBounded<Command>(new BoundedChannelOptions(Capacity)
            {
                SingleReader = true,
                SingleWriter = true, // one network thread per session
                FullMode = BoundedChannelFullMode.Wait
            }));
        }

        string path = Path.Combine(OutDir, "round5-audit.csv");
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        using var csv = new StreamWriter(fs, new UTF8Encoding(false), 32 * 1024) { AutoFlush = false, NewLine = "\n" };
        csv.Write("Seq,Session,CommandId,ThreadId,CheckAccess\n");

        var stats = new ExecStats();
        int seq = 0;
        long backpressureSpins = 0;
        var sw = Stopwatch.StartNew();

        var producers = new List<Thread>();
        for (int s = 0; s < Sessions; s++)
        {
            int sid = s;
            Channel<Command> ch = channels[sid];
            var t = new Thread(() =>
            {
                for (int i = 0; i < PerSession; i++)
                {
                    var cmd = new Command(i, "S" + sid, "v" + i, Stopwatch.GetTimestamp());
                    while (!ch.Writer.TryWrite(cmd))
                    {
                        Interlocked.Increment(ref backpressureSpins);
                        Thread.Yield();
                    }
                }

                ch.Writer.Complete();
            })
            { IsBackground = true, Name = "Session-" + sid };
            producers.Add(t);
            t.Start();
        }

        // ONE pump for all sessions. Merge by WaitToReadAsync + WhenAny; every domain call still
        // goes through the single UI dispatcher, so the audit writer keeps a single writing thread.
        var waits = channels.Select(c => (Task)c.Reader.WaitToReadAsync().AsTask()).ToList();
        int retired = 0;
        while (retired < Sessions)
        {
            Task done = await Task.WhenAny(waits).ConfigureAwait(false);
            int idx = waits.IndexOf(done);
            ChannelReader<Command> reader = channels[idx].Reader;

            while (reader.TryRead(out Command? cmd) && cmd is not null)
            {
                Command captured = cmd;
                await Ui.InvokeAsync(() =>
                {
                    bool access = Ui.CheckAccess();
                    Debug.Assert(access, "pump 未在 UI 线程上执行");
                    if (!access) stats.CheckAccessFailures++;
                    stats.ThreadIds.Add(Environment.CurrentManagedThreadId);

                    seq++;
                    csv.Write(seq.ToString(CultureInfo.InvariantCulture));
                    csv.Write(',');
                    csv.Write(captured.Producer);
                    csv.Write(',');
                    csv.Write(captured.Id.ToString(CultureInfo.InvariantCulture));
                    csv.Write(',');
                    csv.Write(Environment.CurrentManagedThreadId.ToString(CultureInfo.InvariantCulture));
                    csv.Write(',');
                    csv.Write(access ? "True" : "False");
                    csv.Write('\n');
                    stats.Count++;
                    SpinWork(0.2);
                }).Task.ConfigureAwait(false);
            }

            bool completedAndEmpty = done is Task<bool> tb && tb.IsCompletedSuccessfully && !tb.Result;
            if (completedAndEmpty)
            {
                retired++;
                waits[idx] = Task.Delay(Timeout.Infinite);
            }
            else
            {
                waits[idx] = reader.WaitToReadAsync().AsTask();
            }
        }

        sw.Stop();
        bool allJoined = producers.All(p => p.Join(TimeSpan.FromSeconds(30)));
        csv.Flush();
        fs.Flush(true);

        Console.WriteLine("sessions=" + Sessions + " x " + PerSession + "  capacity/session=" + Capacity +
                          "  executed=" + stats.Count + "  elapsed=" + F(sw.Elapsed.TotalMilliseconds) + "ms" +
                          "  producerBackpressureSpins=" + backpressureSpins +
                          "  allProducersJoined=" + allJoined +
                          "  checkAccessFailures=" + stats.CheckAccessFailures);
        Console.WriteLine("audit writer thread(s)=" + stats.ThreadIds.Count + " (tid=" + string.Join(",", stats.ThreadIds) + ")");

        ValidateRound5(path, stats, sw.Elapsed.TotalMilliseconds, Sessions, PerSession);
    }

    private static void ValidateRound5(string path, ExecStats stats, double totalMs, int sessions, int perSession)
    {
        List<string[]> records = Csv.Parse(ReadAllTextShared(path));
        string[] header = records[0];
        List<string[]> body = records.Skip(1).ToList();

        bool headerOk = header.Length == 5 && header[0] == "Seq" && header[1] == "Session" &&
                        header[2] == "CommandId" && header[3] == "ThreadId" && header[4] == "CheckAccess";

        int badShape = body.Count(r => r.Length != 5);
        bool seqOk = body.Count == sessions * perSession;
        int lastSeq = 0;
        int badAccess = 0;
        var seen = new HashSet<(string, int)>();
        var lastIdPerSession = new Dictionary<string, int>();
        bool perSessionFifo = true;
        int badThread = 0;

        foreach (string[] r in body)
        {
            if (r.Length != 5) continue;
            if (!int.TryParse(r[0], out int seqNo) || seqNo != lastSeq + 1) seqOk = false;
            lastSeq = seqNo;

            if (r[4] != "True") badAccess++;
            if (!int.TryParse(r[3], out int tid) || tid != UiThreadId) badThread++;

            if (!int.TryParse(r[2], out int cid)) { seqOk = false; continue; }
            if (!seen.Add((r[1], cid))) seqOk = false; // duplicate => a command ran twice / was lost

            if (lastIdPerSession.TryGetValue(r[1], out int prev) && cid <= prev) perSessionFifo = false;
            lastIdPerSession[r[1]] = cid;
        }

        bool allSessionsComplete = lastIdPerSession.Count == sessions &&
                                   lastIdPerSession.Values.All(v => v == perSession - 1) &&
                                   seen.Count == sessions * perSession;

        Checks.Add(new Check
        {
            Id = "I1",
            Pass = headerOk && badShape == 0 && seqOk && allSessionsComplete && perSessionFifo &&
                   badAccess == 0 && badThread == 0 && stats.Count == sessions * perSession,
            Detail = "每会话有界队列(" + sessions + "x" + perSession + ", cap=64) + 全局单泵：" +
                     "记录=" + body.Count + " 全局 Seq 严格递增=" + seqOk +
                     " 唯一(会话,命令)=" + seen.Count + " 每会话 FIFO 保持=" + perSessionFifo +
                     " CheckAccess 全 True=" + (badAccess == 0) + " 单写线程=" + (stats.ThreadIds.Count == 1) +
                     " 总耗时=" + F(totalMs) + "ms"
        });
    }

    // ---------------------------------------------------------------------
    // helpers
    // ---------------------------------------------------------------------
    private static string PayloadFor(int id) => "p," + id + ",\"quoted\"#" + (id % 7) + ", end ";

    private static string CsvEscape(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";

    private static void SpinWork(double milliseconds)
    {
        long sw = Stopwatch.GetTimestamp();
        while (Stopwatch.GetElapsedTime(sw).TotalMilliseconds < milliseconds)
        {
            Thread.SpinWait(200);
        }
    }

    private static void MeasureSleepGranularity()
    {
        var samples = new List<double>();
        for (int i = 0; i < 20; i++)
        {
            long t = Stopwatch.GetTimestamp();
            Thread.Sleep(1);
            samples.Add(Stopwatch.GetElapsedTime(t).TotalMilliseconds);
        }

        Console.WriteLine("Thread.Sleep(1) 实测: min=" + F(samples.Min()) + " avg=" + F(samples.Average()) +
                          " max=" + F(samples.Max()) + "ms（→ 1ms 的“工作”必须用自旋模拟，Sleep(1) 会被时钟粒度放大）");
    }

    private static void PrintLatency(string label, List<double> values)
    {
        if (values.Count == 0)
        {
            Console.WriteLine(label + ": <no samples>");
            return;
        }

        double[] a = values.ToArray();
        Array.Sort(a);
        double p = a[Math.Min(a.Length - 1, (int)(a.Length * 0.50))];
        double p95 = a[Math.Min(a.Length - 1, (int)(a.Length * 0.95))];
        Console.WriteLine(label + ": n=" + a.Length + " p50=" + F(p) + " p95=" + F(p95) +
                          " max=" + F(a[^1]) + " avg=" + F(a.Average()) + "ms");
    }

    private static string F(double v) => v.ToString("0.00", CultureInfo.InvariantCulture);
}

/// <summary>Strict RFC4180-style parser: quoted fields, doubled quotes, no bare quotes.</summary>
internal static class Csv
{
    private enum State { FieldStart, Unquoted, Quoted, AfterQuote }

    public static List<string[]> Parse(string text)
    {
        var records = new List<string[]>();
        var fields = new List<string>();
        var sb = new StringBuilder();
        State state = State.FieldStart;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            switch (state)
            {
                case State.FieldStart:
                    if (c == '"') state = State.Quoted;
                    else if (c == ',') { fields.Add(""); }
                    else if (c == '\r') { }
                    else if (c == '\n') { fields.Add(""); records.Add(fields.ToArray()); fields.Clear(); }
                    else { sb.Append(c); state = State.Unquoted; }
                    break;

                case State.Unquoted:
                    if (c == ',') { fields.Add(sb.ToString()); sb.Clear(); state = State.FieldStart; }
                    else if (c == '\n') { fields.Add(sb.ToString()); sb.Clear(); records.Add(fields.ToArray()); fields.Clear(); state = State.FieldStart; }
                    else if (c == '\r') { }
                    else if (c == '"') throw new FormatException("裸引号出现在未加引号的字段中，位置 " + i);
                    else sb.Append(c);
                    break;

                case State.Quoted:
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"') { sb.Append('"'); i++; }
                        else state = State.AfterQuote;
                    }
                    else sb.Append(c);
                    break;

                case State.AfterQuote:
                    if (c == ',') { fields.Add(sb.ToString()); sb.Clear(); state = State.FieldStart; }
                    else if (c == '\n') { fields.Add(sb.ToString()); sb.Clear(); records.Add(fields.ToArray()); fields.Clear(); state = State.FieldStart; }
                    else if (c == '\r') { }
                    else throw new FormatException("引号闭合后出现非法字符 '" + c + "'，位置 " + i);
                    break;
            }
        }

        if (state == State.Quoted) throw new FormatException("引号未闭合");
        if (state != State.FieldStart || fields.Count > 0)
        {
            fields.Add(sb.ToString());
            records.Add(fields.ToArray());
        }

        return records;
    }
}
