using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
// UseWPF 的隐式 using 会同时带来 System.IO.Path 与 System.Windows.Shapes.Path，显式指向 IO 的那个
using Path = System.IO.Path;

namespace VirtualCameraClient
{
    /// <summary>图片源里的一张图片：列表显示文件名，推送时用全路径。</summary>
    public sealed class ImageFile
    {
        public string FullPath { get; set; }
        public string FileName { get; set; }
        public override string ToString() => FileName;
    }

    /// <summary>
    /// 主界面的 ViewModel：连接参数、图片源、预览、推图节拍、心跳、统计、日志。
    ///
    /// 线程模型（本类最容易出 bug 的地方，务必看清）
    /// ---------
    /// - 心跳循环、推图循环都跑在后台线程（Task + PeriodicTimer），绝不占用 UI 线程；
    /// - 后台线程要改界面绑定的属性时，统一走 RunOnUi()，由 Dispatcher 切回 UI 线程；
    /// - Images 是 ObservableCollection，只在 UI 线程（选择文件夹时）增删；
    ///   推图线程不直接读 Images，而是读一份"快照数组" _frames（数组引用读取是原子的），
    ///   从而避免"后台线程枚举集合时 UI 线程改了集合"这种偶发崩溃。
    /// </summary>
    public sealed class MainViewModel : ObservableObject, IDisposable
    {
        // 支持的图片扩展名（与主软件"解码后按像素送 Hub"的口径一致：常见格式都能收）
        private static readonly string[] SupportedExtensions =
            { ".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff" };

        private readonly CameraPushClient _client = new CameraPushClient();
        private readonly Dispatcher _dispatcher;
        private readonly Queue<string> _logLines = new Queue<string>();

        private CancellationTokenSource _heartbeatCts;
        private CancellationTokenSource _pushCts;

        // 推图游标：用 Interlocked 自增，保证"连续推图"与"手动推一帧"同时触发时也不会读坏
        private int _pushCounter;

        // 计数用的真实字段（后台线程直接改，原子读写安全）
        private int _hbSentCount;
        private int _hbFailCount;
        private int _pushSuccessCount;
        private int _pushFailCount;

        // 推图线程读取的图片快照（引用赋值是原子的，UI 线程换整份，后台线程读整份）
        private volatile ImageFile[] _frames = Array.Empty<ImageFile>();

        private bool _disposed;

        #region 连接参数

        private string _host = "127.0.0.1";
        private int _port = 19100;
        private string _serial = "VIRTUAL-001";
        private string _token = "visionmaster";

        /// <summary>主机（默认 127.0.0.1）</summary>
        public string Host { get => _host; set => SetProperty(ref _host, value); }

        /// <summary>端口（默认 19100）</summary>
        public int Port { get => _port; set => SetProperty(ref _port, value); }

        /// <summary>相机序列号（默认 VIRTUAL-001，会拼进 URL 路径）</summary>
        public string Serial { get => _serial; set => SetProperty(ref _serial, value); }

        /// <summary>访问令牌（默认 visionmaster，作为 Bearer 头）</summary>
        public string Token { get => _token; set => SetProperty(ref _token, value); }

        #endregion

        #region 图片源与预览

        private string _imageFolder;
        private int _imageCount;
        private ImageFile _selectedImage;
        private BitmapImage _previewImage;

        /// <summary>当前选择的图片文件夹</summary>
        public string ImageFolder { get => _imageFolder; set => SetProperty(ref _imageFolder, value); }

        /// <summary>扫描到的图片数量</summary>
        public int ImageCount { get => _imageCount; set => SetProperty(ref _imageCount, value); }

        /// <summary>文件列表绑定的集合（仅在 UI 线程修改）</summary>
        public ObservableCollection<ImageFile> Images { get; } = new ObservableCollection<ImageFile>();

        /// <summary>列表中选中的图片：变化时顺带更新预览</summary>
        public ImageFile SelectedImage
        {
            get => _selectedImage;
            set
            {
                if (SetProperty(ref _selectedImage, value) && value != null)
                    SetPreviewOnUi(value.FullPath);
            }
        }

        /// <summary>预览图（当前选中 / 正在推送的那张）</summary>
        public BitmapImage PreviewImage { get => _previewImage; set => SetProperty(ref _previewImage, value); }

        /// <summary>由 View 注入的"弹出文件夹选择框并返回路径"的委托（对话框属于 UI 层，不进 VM）</summary>
        public Func<string> FolderPicker { get; set; }

        #endregion

        #region 推图节拍与状态

        private int _intervalMs = 200;
        private bool _isPushing;

        /// <summary>连续推图节拍（毫秒，默认 200）</summary>
        public int IntervalMs { get => _intervalMs; set => SetProperty(ref _intervalMs, value); }

        /// <summary>是否正在连续推图</summary>
        public bool IsPushing
        {
            get => _isPushing;
            private set
            {
                if (SetProperty(ref _isPushing, value))
                    OnPropertyChanged(nameof(StartStopText));
            }
        }

        /// <summary>开始/暂停按钮的显示文字</summary>
        public string StartStopText => IsPushing ? "暂停推图" : "开始连续推图";

        #endregion

        #region 统计与心跳展示

        private int _pushSuccess;
        private int _pushFail;
        private long _lastElapsedMs;
        private string _lastServerSummary = "（暂无）";

        private int _heartbeatSent;
        private int _heartbeatFail;
        private string _heartbeatState = "未连接";

        /// <summary>成功推帧数</summary>
        public int PushSuccess { get => _pushSuccess; private set => SetProperty(ref _pushSuccess, value); }

        /// <summary>失败推帧数</summary>
        public int PushFail { get => _pushFail; private set => SetProperty(ref _pushFail, value); }

        /// <summary>最近一次推帧耗时（毫秒）</summary>
        public long LastElapsedMs { get => _lastElapsedMs; private set => SetProperty(ref _lastElapsedMs, value); }

        /// <summary>最近一次服务端响应摘要（一行）</summary>
        public string LastServerSummary { get => _lastServerSummary; private set => SetProperty(ref _lastServerSummary, value); }

        /// <summary>心跳已发送次数</summary>
        public int HeartbeatSent
        {
            get => _heartbeatSent;
            private set { if (SetProperty(ref _heartbeatSent, value)) OnPropertyChanged(nameof(HeartbeatText)); }
        }

        /// <summary>心跳失败次数</summary>
        public int HeartbeatFail
        {
            get => _heartbeatFail;
            private set { if (SetProperty(ref _heartbeatFail, value)) OnPropertyChanged(nameof(HeartbeatText)); }
        }

        /// <summary>心跳状态文案：已发 N 次 / 失败 M 次</summary>
        public string HeartbeatText => $"已发 {HeartbeatSent} 次 / 失败 {HeartbeatFail} 次";

        /// <summary>服务端返回的相机状态（如 Streaming）</summary>
        public string HeartbeatState { get => _heartbeatState; private set => SetProperty(ref _heartbeatState, value); }

        #endregion

        #region 日志

        private string _logText = string.Empty;

        /// <summary>日志区文本（最多保留 200 条）</summary>
        public string LogText { get => _logText; private set => SetProperty(ref _logText, value); }

        #endregion

        #region 命令

        public RelayCommand BrowseFolderCommand { get; }
        public RelayCommand TogglePushCommand { get; }
        public RelayCommand PushOnceCommand { get; }

        public MainViewModel()
        {
            // 构造发生在 UI 线程，这里捕获 Dispatcher，供后台线程回切 UI 用
            _dispatcher = Dispatcher.CurrentDispatcher;

            BrowseFolderCommand = new RelayCommand(BrowseFolder);
            TogglePushCommand = new RelayCommand(TogglePush);
            // 命令是同步签名（void Execute），这里用"发射后不管"的安全包装，异常在内部吞掉并记日志
            PushOnceCommand = new RelayCommand(() => _ = PushOnceSafeAsync());
        }

        #endregion

        #region 生命周期

        /// <summary>窗口打开时调用：启动心跳（心跳与推图开关无关）</summary>
        public void Start()
        {
            StartHeartbeat();
            Log($"工具已启动，心跳已开始（每 1 秒一次，独立于推图开关，服务端 3 秒收不到心跳即判掉线）");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            StopPush();

            try { _heartbeatCts?.Cancel(); } catch { /* 关闭阶段的取消异常忽略 */ }
            _heartbeatCts?.Dispose();
            _heartbeatCts = null;

            _client.Dispose();
            Log("已停止推图、取消心跳并释放 HttpClient");
        }

        #endregion

        #region 心跳（独立循环，窗口一开就转）

        private void StartHeartbeat()
        {
            if (_heartbeatCts != null) return;
            _heartbeatCts = new CancellationTokenSource();
            var token = _heartbeatCts.Token;
            _ = Task.Run(() => HeartbeatLoopAsync(token), token);
        }

        private async Task HeartbeatLoopAsync(CancellationToken ct)
        {
            // 为什么用 PeriodicTimer 而不是 Thread.Sleep：
            // Thread.Sleep 会占住线程且不响应取消；PeriodicTimer 的 WaitForNextTickAsync 支持 CancellationToken，
            // 关窗时能立刻退出，也不会像 DispatcherTimer 那样把 HTTP 等待压回 UI 线程。
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            try
            {
                do
                {
                    // 先立即发一次（窗口一打开就上报在线），之后每 1 秒一次
                    await SendHeartbeatOnceAsync(ct).ConfigureAwait(false);
                }
                while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false));
            }
            catch (OperationCanceledException)
            {
                // 关窗/停止：正常退出
            }
            catch (Exception ex)
            {
                Log($"心跳循环异常退出：{ex.Message}");
            }
        }

        private async Task SendHeartbeatOnceAsync(CancellationToken ct)
        {
            // 每次发之前同步一次连接参数，这样界面上改端口/令牌能立刻生效，无需重启
            _client.Configure(Host, Port, Serial, Token);

            var result = await _client.SendHeartbeatAsync(ct).ConfigureAwait(false);

            if (result.Ok)
            {
                _hbSentCount++;
                var sent = _hbSentCount;
                var state = result.State ?? "Online";
                RunOnUi(() => { HeartbeatSent = sent; HeartbeatState = state; });
            }
            else
            {
                _hbFailCount++;
                var fail = _hbFailCount;
                RunOnUi(() => { HeartbeatFail = fail; HeartbeatState = "失败"; });
                // 失败才写日志：否则每秒一条会把 200 条日志刷满，真正有用的信息被冲走
                Log($"心跳失败：{result.Summary}");
            }
        }

        #endregion

        #region 推图（连续 / 手动）

        private void TogglePush()
        {
            if (IsPushing) StopPush();
            else StartPush();
        }

        private void StartPush()
        {
            if (_frames.Length == 0)
            {
                Log("未扫描到图片，请先选择图片文件夹");
                return;
            }
            if (IsPushing) return;

            IsPushing = true;
            _pushCts = new CancellationTokenSource();
            var token = _pushCts.Token;
            _ = Task.Run(() => PushLoopAsync(token), token);
            Log($"开始连续推图：节拍 {Math.Max(20, IntervalMs)}ms，共 {_frames.Length} 张，按文件名循环推送");
        }

        private void StopPush()
        {
            if (!IsPushing && _pushCts == null) return;
            IsPushing = false;

            try { _pushCts?.Cancel(); } catch { /* 忽略 */ }
            _pushCts?.Dispose();
            _pushCts = null;

            Log("已暂停连续推图（注意：心跳仍在继续，客户端不会被判掉线）");
        }

        private async Task PushLoopAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var interval = Math.Max(20, IntervalMs);
                    using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(interval));

                    // 先立即推一帧，之后按节拍推
                    do
                    {
                        await PushOneFrameAsync(ct).ConfigureAwait(false);

                        // 节拍在运行中被改：跳出内层重建 timer，让新节拍尽快生效
                        if (Math.Max(20, IntervalMs) != interval)
                            break;
                    }
                    while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false));
                }
            }
            catch (OperationCanceledException)
            {
                // 停止推图：正常退出
            }
            catch (Exception ex)
            {
                Log($"推图循环异常退出：{ex.Message}");
            }
        }

        private async Task PushOnceSafeAsync()
        {
            try
            {
                await PushOneFrameAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log($"手动推帧异常：{ex.Message}");
            }
        }

        /// <summary>
        /// 推一帧：取当前游标对应的图片 → 发送 → 更新统计/预览/日志。
        /// 单帧失败不抛出、不中断循环，只累加失败数并写日志。
        /// </summary>
        private async Task PushOneFrameAsync(CancellationToken ct)
        {
            var frames = _frames;   // 读一次快照引用，避免循环中快照被替换导致越界
            if (frames.Length == 0)
            {
                Log("图片列表为空，自动停止连续推图");
                RunOnUi(StopPush);
                return;
            }

            var n = Interlocked.Increment(ref _pushCounter);
            var file = frames[(n - 1) % frames.Length];

            _client.Configure(Host, Port, Serial, Token);

            var sw = Stopwatch.StartNew();
            var result = await _client.PushFrameAsync(file.FullPath, ct).ConfigureAwait(false);
            sw.Stop();

            var elapsed = sw.ElapsedMilliseconds;
            if (result.Ok) _pushSuccessCount++;
            else _pushFailCount++;

            var success = _pushSuccessCount;
            var fail = _pushFailCount;

            RunOnUi(() =>
            {
                PushSuccess = success;
                PushFail = fail;
                LastElapsedMs = elapsed;
                LastServerSummary = result.Summary;
            });

            // 预览跟随"正在推送"的那张图
            SetPreviewOnUi(file.FullPath);

            Log(result.Ok
                ? $"推帧成功 {file.FileName} 耗时 {elapsed}ms | {result.Summary}"
                : $"推帧失败 {file.FileName} 耗时 {elapsed}ms | {result.Summary}");
        }

        #endregion

        #region 图片扫描

        private void BrowseFolder()
        {
            var picker = FolderPicker;
            if (picker == null)
            {
                Log("未注册文件夹选择器，无法选择文件夹");
                return;
            }

            var folder = picker();
            if (string.IsNullOrWhiteSpace(folder)) return;

            ImageFolder = folder;

            var files = ScanImages(folder);
            Images.Clear();
            foreach (var f in files)
                Images.Add(f);

            _frames = files.ToArray();
            Interlocked.Exchange(ref _pushCounter, 0);   // 换文件夹后从头开始推
            ImageCount = files.Count;

            Log($"已选择文件夹：{folder}，扫描到 {files.Count} 张图片");
            if (files.Count == 0)
                Log("该文件夹下没有支持的图片（jpg / jpeg / png / bmp / tif / tiff）");
        }

        private List<ImageFile> ScanImages(string folder)
        {
            var list = new List<ImageFile>();
            try
            {
                foreach (var file in Directory.EnumerateFiles(folder))
                {
                    var ext = Path.GetExtension(file);
                    if (Array.Exists(SupportedExtensions, e => string.Equals(e, ext, StringComparison.OrdinalIgnoreCase)))
                        list.Add(new ImageFile { FullPath = file, FileName = Path.GetFileName(file) });
                }
            }
            catch (Exception ex)
            {
                Log($"扫描文件夹失败：{ex.Message}");
            }

            // 按文件名排序：连续推送顺序稳定、可预期（最后一张推完回到第一张）
            list.Sort((a, b) => string.Compare(a.FileName, b.FileName, StringComparison.OrdinalIgnoreCase));
            return list;
        }

        #endregion

        #region 预览与日志（统一回切 UI 线程）

        /// <summary>加载并显示预览图。加载可在后台线程做，赋给绑定属性必须回 UI 线程。</summary>
        private void SetPreviewOnUi(string path)
        {
            try
            {
                var bmp = LoadBitmap(path);
                RunOnUi(() => PreviewImage = bmp);
            }
            catch
            {
                // 预览失败不影响推图（可能文件正被写入/损坏），静默忽略
            }
        }

        private static BitmapImage LoadBitmap(string path)
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            // OnLoad：解码时一次性读入内存并释放文件句柄，避免"文件被占用"；
            // IgnoreImageCache：同一路径内容被替换后能重新加载，不被 WPF 图片缓存挡住；
            // Freeze：冻结后可跨线程安全地把 BitmapImage 交给 UI 线程使用。
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }

        private void Log(string message)
        {
            var line = $"{DateTime.Now:HH:mm:ss.fff}  {message}";
            RunOnUi(() =>
            {
                _logLines.Enqueue(line);
                while (_logLines.Count > 200)
                    _logLines.Dequeue();
                LogText = string.Join(Environment.NewLine, _logLines);
            });
        }

        /// <summary>
        /// 把动作切回 UI 线程执行。
        /// 为什么必须切：ObservableCollection 与绑定属性都带线程亲和性，
        /// 从后台线程直接改会抛 InvalidOperationException（"调用线程无法访问此对象，因为另一个线程拥有它"）。
        /// </summary>
        private void RunOnUi(Action action)
        {
            if (action == null) return;
            try
            {
                if (_dispatcher == null || _dispatcher.CheckAccess())
                    action();
                else
                    _dispatcher.BeginInvoke(action);
            }
            catch
            {
                // 关窗过程中 Dispatcher 可能已停止，忽略
            }
        }

        #endregion
    }
}
