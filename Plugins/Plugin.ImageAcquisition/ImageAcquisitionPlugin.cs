using Core.Events;
using Core.Interfaces;
using HalconDotNet;
using Microsoft.Win32;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Prism.Commands;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace Plugin.ImageAcquisition
{
    /// <summary>
    /// 图像采集插件（Plugin 与配置 ViewModel 合一）
    /// 设计原则：一份实例只存一份数据——
    /// - 数据流参数（可被变量链接）：声明 InputPort，界面绑定 TypedValue
    /// - 纯配置项（永不链接）：[StepConfig] 普通属性，存在自身实例中
    /// - 纯界面状态（预览图/消息）：普通属性，不参与持久化
    /// 三者的持久化/灌值同步均由基类默认实现，键名 = 名称，无需映射
    /// - RunAlgorithm：唯一的执行方法，正式运行与试运行共用
    /// </summary>
    [Display(
        Name = "图像采集",
        GroupName = "常用工具",
        Description = "从文件或文件夹获取图像，供后续视觉处理使用",
        ShortName = "\uf1c5"
    )]
    public class ImageAcquisitionPlugin : VisionPluginBase, IPluginCustomViewProvider, IPluginConfigContextProvider
    {
        #region 配置属性（纯配置项：持久化到 InputValues、参与灌值，但不进端口/不可变量链接）

        private AcquisitionMode _mode;
        /// <summary>
        /// 采集模式: 0=单图文件, 1=文件夹, 2=网络推送
        /// </summary>
        [StepConfig]
        public AcquisitionMode Mode
        {
            get => _mode;
            set
            {
                if (!SetProperty(ref _mode, value)) return;

                // 换模式等于换数据源：旧模式的预览图/路径/计数在新模式下全对不上号，
                // 留着只会让界面自相矛盾（比如"文件目录"模式下还挂着上次的单图预览）
                ClearPreview();
                ValidatePathInputs();
            }
        }

        private int _displayViewIndex = 1;
        /// <summary>
        /// 显示窗口索引：采集图像发布到主界面几号视图窗口（1~9），0=不显示
        /// </summary>
        [StepConfig]
        public int DisplayViewIndex
        {
            get => _displayViewIndex;
            set => SetProperty(ref _displayViewIndex, value);
        }

        #endregion

        #region 输入端口（数据流参数：可被变量链接，界面绑定 TypedValue）

        /// <summary>
        /// 单张图像文件路径
        /// </summary>
        public InputPort<string> FilePathPort { get; } = new(
            "FilePath",
            "",
            "单张图像文件完整路径"
        )
        { IsRequired = false };

        /// <summary>
        /// 文件夹路径
        /// </summary>
        public InputPort<string> FolderPathPort { get; } = new(
            "FolderPath",
            "",
            "包含图像的文件夹路径"
        )
        { IsRequired = false };

        /// <summary>
        /// 文件夹内的文件索引（0-based）
        /// </summary>
        public InputPort<int> FileIndexPort { get; } = new(
            "FileIndex",
            0,
            "文件夹内的文件索引（0-based）"
        )
        { IsRequired = false };

        #endregion

        #region 输出端口（Success/ErrorMessage 由基类提供）

        /// <summary>
        /// 采集到的图像
        /// </summary>
        public OutputPort<HImage> OutputImage { get; } = new(
            "Image",
            "采集到的图像"
        );

        /// <summary>
        /// 当前文件路径
        /// </summary>
        public OutputPort<string> CurrentFilePath { get; } = new(
            "CurrentPath",
            "当前采集到的文件路径"
        );

        /// <summary>
        /// 当前文件索引
        /// </summary>
        public OutputPort<int> CurrentFileIndex { get; } = new(
            "CurrentIndex",
            "当前采集的文件索引"
        );

        /// <summary>
        /// 文件夹内文件总数
        /// </summary>
        public OutputPort<int> TotalFiles { get; } = new(
            "TotalFiles",
            "文件夹内符合条件的文件总数"
        );

        #endregion

        #region 预览与状态（纯界面属性，不参与流程数据流）

        private HImage _previewImage = new();
        /// <summary>
        /// 预览图像（换图即弃旧：SetProperty 成功后释放旧实例，避免非托管内存泄漏）
        /// 安全性：ImageReadOnly 的 HImage 依赖属性持引用不复制，绑定在 UI 线程同步刷新，
        /// SetProperty 已把 DP 切到新值并完成重绘，此处释放的是"已不被 UI 引用"的旧图
        /// </summary>
        public HImage PreviewImage
        {
            get => _previewImage;
            set
            {
                var old = _previewImage;
                if (ReferenceEquals(old, value)) return;
                if (SetProperty(ref _previewImage, value))
                    old?.Dispose();
            }
        }

        private string _previewImagePath = string.Empty;
        /// <summary>
        /// 预览图像来源路径
        /// </summary>
        public string PreviewImagePath
        {
            get => _previewImagePath;
            set => SetProperty(ref _previewImagePath, value);
        }

        private string _statusMessage = string.Empty;
        /// <summary>
        /// 操作状态消息（请用 SetStatus 写入：消息与级别必须成对更新）
        /// </summary>
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        private StatusLevel _statusLevel = StatusLevel.Info;
        /// <summary>
        /// 操作状态级别：决定信息栏文字颜色（Info 绿 / Warning 橙 / Error 红），
        /// 由视图的 DataTrigger 消费，插件自身不关心颜色
        /// </summary>
        public StatusLevel StatusLevel
        {
            get => _statusLevel;
            private set => SetProperty(ref _statusLevel, value);
        }

        /// <summary>
        /// 状态消息的唯一写入口。
        /// 为什么不让各处直接赋 StatusMessage：消息和级别是一对，分散赋值极易漏改级别——
        /// 典型症状是"失败变红之后再成功，颜色还停在红"。
        /// </summary>
        private void SetStatus(string message, StatusLevel level = StatusLevel.Info)
        {
            StatusMessage = message;
            StatusLevel = level;
        }

        private int _fileCount;
        /// <summary>
        /// 文件夹中图像文件总数
        /// </summary>
        public int FileCount
        {
            get => _fileCount;
            set => SetProperty(ref _fileCount, value);
        }

        private string _currentFileName = string.Empty;
        /// <summary>
        /// 当前索引对应的文件名
        /// </summary>
        public string CurrentFileName
        {
            get => _currentFileName;
            set => SetProperty(ref _currentFileName, value);
        }

        /// <summary>
        /// 浏览选择图像文件（供"指定图像"LinkableValueEditor 的 BrowseCommand 使用）
        /// </summary>
        public DelegateCommand BrowseFileCommand { get; }

        /// <summary>
        /// 浏览选择图像文件夹（供"文件目录"LinkableValueEditor 的 BrowseCommand 使用）
        /// </summary>
        public DelegateCommand BrowseFolderCommand { get; }

        /// <summary>
        /// 刷新文件夹预览（切换文件索引后重新载入预览图）
        /// </summary>
        public DelegateCommand RefreshPreviewCommand { get; }

        /// <summary>
        /// 上一张（文件索引 -1）
        /// </summary>
        public DelegateCommand PreviousImageCommand { get; }

        /// <summary>
        /// 下一张（文件索引 +1）
        /// </summary>
        public DelegateCommand NextImageCommand { get; }

        #endregion

        #region 本地图片推送测试（配置态，走真 HTTP POST 到宿主收图服务）

        private string _pushFlowName = string.Empty;
        /// <summary>
        /// 推送目标流程名（由宿主预填为当前流程名，可改：填个不存在的名字正好用来验证 404）
        /// </summary>
        public string PushFlowName
        {
            get => _pushFlowName;
            set => SetProperty(ref _pushFlowName, value);
        }

        private string _pushHost = "127.0.0.1";
        /// <summary>
        /// 推送目标主机（宿主监听地址写 0.0.0.0 时已由宿主换成回环地址）
        /// </summary>
        public string PushHost
        {
            get => _pushHost;
            set => SetProperty(ref _pushHost, value);
        }

        private string _pushPortText = "19000";
        /// <summary>
        /// 推送目标端口。
        /// 用文本而不是 int：允许填非法值来验证参数校验，也不必和 WPF 的绑定校验规则较劲
        /// （19000 是 HttpImageServerSettings.DefaultPort，插件不引用 Core 项目，只能照抄常量）
        /// </summary>
        public string PushPortText
        {
            get => _pushPortText;
            set => SetProperty(ref _pushPortText, value);
        }

        private string _pushToken = string.Empty;
        /// <summary>
        /// 访问令牌（可改：故意填错正好用来验证 401）
        /// </summary>
        public string PushToken
        {
            get => _pushToken;
            set => SetProperty(ref _pushToken, value);
        }

        private string _pushServiceHint = string.Empty;
        /// <summary>
        /// 宿主收图服务状态提示（由 SetConfigContext 写入，界面只读展示）
        /// </summary>
        public string PushServiceHint
        {
            get => _pushServiceHint;
            private set => SetProperty(ref _pushServiceHint, value);
        }

        private string _pushResultText = string.Empty;
        /// <summary>
        /// 最近一次推送的结果（多行只读文本：状态码 / 耗时 / 图像尺寸 / 输出摘要 / 错误原因）
        /// </summary>
        public string PushResultText
        {
            get => _pushResultText;
            private set => SetProperty(ref _pushResultText, value);
        }

        /// <summary>
        /// 服务端等待流程执行的上限（毫秒），由宿主透传；客户端超时按它 +5s
        /// </summary>
        private int _pushTimeoutMs = 30000;

        /// <summary>
        /// 选择本地图片并推送测试。
        /// 并发保护不用自己写：AsyncDelegateCommand 默认禁止并行执行，执行期间 CanExecute 为 false，
        /// 按钮会自动置灰（这正是没选 EnableParallelExecution 的意义）
        /// </summary>
        public AsyncDelegateCommand PushLocalImageCommand { get; }

        #endregion

        #region 私有状态

        /// <summary>
        /// 支持的图像扩展名（唯一事实源：执行核心、文件夹列表、浏览 filter 均由此派生）
        /// </summary>
        private const string ImageExtensions = ".bmp,.jpg,.jpeg,.png,.tif,.tiff";

        private List<string> _cachedFiles = new();
        private string _cachedFolderPath = string.Empty;
        private string _cachedExtensions = string.Empty;

        /// <summary>
        /// 预览载入轮次号：每次发起载图 +1，后台读完回来时对不上号说明已是"过期结果"，直接丢弃。
        /// 用户连点刷新、或翻页很快时会同时存在多个在途读图，没有它就会"后完成的旧图覆盖新图"。
        /// 只在 UI 线程读写（载图入口与 ClearPreview/Dispose 都在 UI 线程）。
        /// </summary>
        private int _previewLoadId;

        /// <summary>
        /// 配置实例是否已释放：关闭对话框后在途读图任务回来时，靠它拒绝往已释放的绑定上写图
        /// </summary>
        private volatile bool _disposed;

        #endregion

        public ImageAcquisitionPlugin()
        {
            BrowseFileCommand = new DelegateCommand(BrowseFile);
            BrowseFolderCommand = new DelegateCommand(BrowseFolder);
            RefreshPreviewCommand = new DelegateCommand(RefreshFolderFiles);
            PreviousImageCommand = new DelegateCommand(() => StepImage(-1));
            NextImageCommand = new DelegateCommand(() => StepImage(1));
            PushLocalImageCommand = new AsyncDelegateCommand(PushLocalImageAsync);

            // 路径端口的值可能来自手动输入，也可能来自变量链接/解除链接，这些都会触发 ValueChanged，
            // 订阅它就能"边改边提示"。注意这里**只做校验、不顺手载入预览**：
            // 载图是显式动作（浏览 / 刷新 / 翻页），混进事件里会和它们的显式调用重复读盘。
            FilePathPort.ValueChanged += (_, _) => ValidatePathInputs();
            FolderPathPort.ValueChanged += (_, _) => ValidatePathInputs();
        }

        #region IPluginCustomViewProvider

        /// <summary>
        /// 返回插件自定义配置视图（DataContext = 插件自身）
        /// </summary>
        public object GetConfigView(IStepConfigData stepData)
        {
           
            return new ImageAcquisitionView(stepData, this);
        }

        #endregion

        #region IPluginConfigContextProvider

        /// <summary>
        /// 接收宿主透传的配置上下文，预填推送表单。
        ///
        /// 插件 DLL 拿不到宿主的 AppSettingsService（不引用 Core 项目），
        /// 端口/令牌/流程名只能由宿主在打开配置窗口时送进来 —— 这也是"推送测试"能拼出正确 URL 的前提。
        /// 调用时机在 Initialize 之后，所以这里的值会盖过 Initialize 写的默认值。
        /// </summary>
        public void SetConfigContext(PluginConfigContext context)
        {
            if (context == null) return;

            PushFlowName = context.FlowName ?? string.Empty;
            PushHost = context.HttpHost ?? string.Empty;
            PushPortText = context.HttpPort.ToString();
            PushToken = context.HttpToken ?? string.Empty;
            _pushTimeoutMs = context.RequestTimeoutMs;

            // 服务有没有在听是"能不能推"的第一现场：没在听时推送必然连接失败，
            // 与其让用户点完按钮再猜原因，不如打开窗口就说清楚
            if (context.HttpListening)
            {
                PushServiceHint = $"宿主收图服务正在监听 {context.HttpHost}:{context.HttpPort}";
                SetStatus($"推送测试已就绪（目标 {context.HttpHost}:{context.HttpPort}）", StatusLevel.Info);
            }
            else
            {
                PushServiceHint = context.HttpEnabled
                    ? "宿主收图服务配置为启用但未在监听（端口被占用或启动失败），推送会连接失败"
                    : "宿主收图服务未启用（AppConfig.json → HttpImageServer.Enabled=false），推送会连接失败";
                SetStatus(PushServiceHint, StatusLevel.Warning);
            }
        }

        #endregion

        #region 执行核心（唯一一份）

        /// <summary>
        /// 采集执行核心的输出
        /// </summary>
        private sealed class CoreResult
        {
            public bool Success;
            public string Error = "";
            public HImage Image;
            public string CurrentPath;
            public int CurrentIndex;
            public int TotalFiles;

            /// <summary>
            /// 等图被"停止流程"打断（仅网络推送模式会置位）。
            /// 既不是成功也不是失败：流程去向由引擎的取消语义决定，插件不参与。
            /// </summary>
            public bool Cancelled;
        }

        /// <summary>
        /// 唯一的采集执行核心：参数进 → 结果出，不含端口/界面逻辑
        /// context 只被"网络推送"模式用到——流程名决定去哪个流程槽取图（见 ImageHub 的分槽说明），
        /// 取消令牌决定"等图"能被"停止流程"打断
        /// </summary>
        private CoreResult ExecuteCore(
            AcquisitionMode mode,
            string filePath,
            string folderPath,
            int fileIndex,
            IExecutionContext context)
        {
            switch (mode)
            {
                case AcquisitionMode.SingleFile:
                    return AcquireSingleFile(filePath);

                case AcquisitionMode.Folder:
                    return AcquireFromFolder(folderPath, fileIndex, ImageExtensions);

                case AcquisitionMode.Hub:
                    return AcquireFromHub(context);

                default:
                    return new CoreResult { Error = $"未知的采集模式: {mode}" };
            }
        }

        /// <summary>
        /// 网络推送取图：从本流程的图像槽里取最新一帧；槽里没有图就**阻塞等待**，直到有新图推来。
        ///
        /// 为什么槽空是"等"而不是"失败"
        /// ---------
        /// 这一模式的正常工作状态就是等图：
        ///   - 宿主 HTTP 收图链路是"先 Push 再触发流程"，正常必然命中快路径，不产生任何等待；
        ///   - 流程被连续运行、或手动单次运行时，槽里暂时没有图是常态。
        /// 旧实现把"暂时没图"报成失败，于是本步失败、后面每个吃图的步骤再各报一次
        /// "输入图像为空"——真正的根因（还没图）被一堆噪音盖住，这正是要修的问题。
        ///
        /// 唯一的退出方式
        /// ---------
        ///   1) 等到新图（正常路径）；
        ///   2) 用户点"停止流程"→ 取消令牌置位 → 结束等待。
        /// 插件**不具备终止流程的能力**：取消只是"本步没产出"，引擎的序列执行器
        /// 检测到取消令牌后自会结束当轮，这里绝不触碰流程控制状态。
        /// </summary>
        private CoreResult AcquireFromHub(IExecutionContext context)
        {
            var flowName = context.CurrentFlowName;
            var token = context.CancellationToken;
            var waited = false;

            // 先探一眼：槽里有图（HTTP 链路）直接取，不写任何等待日志
            if (!ImageHub.TryPop(flowName, out var item))
            {
                context.Logger?.Info($"{InstanceName} 槽内暂无图像，开始等待网络推送（流程「{flowName}」）...");
                waited = true;

                if (!ImageHub.WaitPop(flowName, token, out item))
                {
                    context.Logger?.Warn($"{InstanceName} 已停止等待网络图像（流程「{flowName}」）");
                    return new CoreResult { Cancelled = true };
                }
            }

            if (item?.PixelData == null || item.PixelData.Length == 0)
                return new CoreResult { Error = $"网络图像数据为空（流程「{flowName}」）" };

            return new CoreResult
            {
                Success = true,
                Image = ToHImage(item),
                CurrentPath = string.IsNullOrEmpty(item.SourceName) ? $"hub://{flowName}" : item.SourceName,
                CurrentIndex = 0,
                TotalFiles = 1,
                Error = waited
                    ? $"已等到网络图像: {item.Width}x{item.Height}x{item.Channels}"
                    : $"已从网络槽取图: {item.Width}x{item.Height}x{item.Channels}"
            };
        }

        /// <summary>
        /// 原始像素字节 → HImage。
        ///
        /// 为什么走非托管中转
        /// ---------
        /// HALCON 的 gen_image1 / gen_image_interleaved 只认裸指针，没有 byte[] 重载。
        /// gen_image1 会把指针指向的数据**复制**进新建的图（这正是它与 gen_image1_extern 的区别：
        /// 后者是"引用 + 归还回调"），所以 Copy 进 HGlobal 后立刻释放是安全的。
        ///
        /// 通道约定：彩色按 BGR 交错传 "bgr"——与宿主解码时选定的 PixelFormats.Bgr24 逐字节对应，
        /// 中间不做任何通道交换；灰度走 gen_image1 单通道。
        /// alignment 传 -1 表示"行间无填充"，正好匹配宿主输出的紧凑 stride。
        /// </summary>
        private static HImage ToHImage(HubImageItem item)
        {
            var image = new HImage();
            var length = item.PixelData.Length;
            var pointer = Marshal.AllocHGlobal(length);

            try
            {
                Marshal.Copy(item.PixelData, 0, pointer, length);

                if (item.Channels >= 3)
                {
                    image.GenImageInterleaved(
                        pointer,
                        "bgr",
                        item.Width,
                        item.Height,
                        -1,
                        "byte",
                        item.Width,
                        item.Height,
                        0,
                        0,
                        -1,
                        0);
                }
                else
                {
                    image.GenImage1("byte", item.Width, item.Height, pointer);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(pointer);
            }

            return image;
        }

        private CoreResult AcquireSingleFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return new CoreResult { Error = "文件路径不能为空" };

            if (!File.Exists(path))
                return new CoreResult { Error = $"文件不存在: {path}" };

            HImage image = new HImage();
            try
            {
                image.ReadImage(path);
                image.GetImageSize(out int width, out int height);

                return new CoreResult
                {
                    Success = true,
                    Image = image,
                    CurrentPath = path,
                    CurrentIndex = 0,
                    TotalFiles = 1,
                    Error = $"已加载图像: {path} ({width}x{height})"
                };
            }
            catch
            {
                // ReadImage 抛异常时 HALCON 对象内部可能已占住非托管内存，不释放会随失败次数累积
                image.Dispose();
                throw;
            }
        }

        private CoreResult AcquireFromFolder(string folderPath, int fileIndex, string extensions)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
                return new CoreResult { Error = "文件夹路径不能为空" };

            if (!Directory.Exists(folderPath))
                return new CoreResult { Error = $"文件夹不存在: {folderPath}" };

            var extSet = new HashSet<string>(
                (extensions ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(e => e.Trim().ToLower()),
                StringComparer.Ordinal
            );

            if (_cachedFolderPath != folderPath || _cachedExtensions != extensions)
            {
                _cachedFiles = Directory.GetFiles(folderPath)
                    .Where(f => extSet.Contains(Path.GetExtension(f).ToLower()))
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                _cachedFolderPath = folderPath;
                _cachedExtensions = extensions;
            }

            if (_cachedFiles.Count == 0)
                return new CoreResult { Error = $"文件夹内未找到符合条件的图像文件: {folderPath}", TotalFiles = 0 };

            if (fileIndex < 0) fileIndex = 0;
            if (fileIndex >= _cachedFiles.Count) fileIndex = _cachedFiles.Count - 1;

            var path = _cachedFiles[fileIndex];
            HImage image = new HImage();
            try
            {
                image.ReadImage(path);

                return new CoreResult
                {
                    Success = true,
                    Image = image,
                    CurrentPath = path,
                    CurrentIndex = fileIndex,
                    TotalFiles = _cachedFiles.Count,
                    Error = $"已加载图像 [{fileIndex + 1}/{_cachedFiles.Count}]: {path}"
                };
            }
            catch
            {
                // 同上：读图失败要自己收拾 HALCON 对象，否则连续失败会持续吃非托管内存
                image.Dispose();
                throw;
            }
        }

        #endregion

        #region 正式运行（唯一执行方法；数据源：端口 = InputValues 灌值 + 链接变量覆盖）

        public override void RunAlgorithm(IExecutionContext context)
        {
            Success.Value = false;
            ErrorMessage.Value = string.Empty;
            OutputImage.Value = null;
            CurrentFilePath.Value = string.Empty;
            // 索引与总数必须一起归零：否则本轮失败时它们还停在上轮的值，
            // 下游按"当前索引/总数"做判断的步骤会读到上一轮的残留数据（基类只清 IDisposable 端口值）
            CurrentFileIndex.Value = 0;
            TotalFiles.Value = 0;

            try
            {
                var result = ExecuteCore(
                    Mode,
                    FilePathPort.GetTypedValue(),
                    FolderPathPort.GetTypedValue(),
                    FileIndexPort.GetTypedValue(),
                    context);

                if (result.Cancelled)
                {
                    // 等图被"停止流程"打断：本步没产出，但不是业务失败——插件不决定流程去向，
                    // 引擎的序列执行器检测到取消令牌后自会结束当轮。
                    // 结果按"未成功"收尾（Success 保持 RunAlgorithm 开头的 false）：
                    // 本步确实没产出图像，标成成功会在运行记录里误导排查。
                    ErrorMessage.Value = "已停止等待网络图像";
                    return;
                }

                Success.Value = result.Success;

                if (result.Success)
                {
                    ErrorMessage.Value = string.Empty;
                    OutputImage.Value = result.Image;
                    CurrentFilePath.Value = result.CurrentPath;
                    CurrentFileIndex.Value = result.CurrentIndex;
                    TotalFiles.Value = result.TotalFiles;
                    context.Logger.Info($"{InstanceName} {result.Error}");

                    // 发布到主程序视图（DisplayViewIndex 已是真实窗口号 1~9；0=不显示则跳过）
                    if (DisplayViewIndex > 0)
                        this.PublishPreview(result.Image, DisplayViewIndex);
                }
                else
                {
                    ErrorMessage.Value = result.Error ?? string.Empty;
                    context.Logger.Error($"{InstanceName} {result.Error}");
                }
            }
            catch (Exception ex)
            {
                ErrorMessage.Value = ex.Message;
                context.Logger.Error($"{InstanceName} 图像采集异常: {ex.Message}");
            }
        }

        #endregion

        #region 配置生命周期（预览恢复；端口⇄InputValues 同步由基类默认实现）

        public override void Initialize(IStepConfigData stepData)
        {
            // 配置实例每次打开对话框都会 Initialize：先把上一次 Dispose 留下的标记清掉，
            // 否则新会话里的预览载入会被"已释放"守卫全部误杀
            _disposed = false;

            base.Initialize(stepData);

            // 恢复预览（值已在 base 中灌入端口与配置属性）
            if (Mode == AcquisitionMode.Folder)
            {
                RefreshFolderFiles();
                return;
            }

            if (Mode == AcquisitionMode.SingleFile && File.Exists(FilePathPort.TypedValue))
            {
                PreviewImagePath = FilePathPort.TypedValue;
                LoadPreview(FilePathPort.TypedValue);
                return;
            }

            // 其余情况（未选文件 / 路径已失效 / 网络推送）：交给统一校验给出提示，
            // 否则打开窗口时信息栏是空白的，用户不知道下一步该做什么
            ValidatePathInputs();
        }

        #endregion

        #region 浏览与预览辅助（配置态，直接读写端口）

        private void BrowseFile()
        {
            var dlg = new OpenFileDialog
            {
                Filter = $"图像文件|{string.Join(";", ImageExtensions.Split(',').Select(e => "*" + e))}|所有文件|*.*",
                Title = "选择图像文件"
            };

            if (dlg.ShowDialog() == true)
            {
                FilePathPort.Value = dlg.FileName;
                PreviewImagePath = dlg.FileName;
                LoadPreview(dlg.FileName);
            }
        }

        private void BrowseFolder()
        {
            var dlg = new OpenFolderDialog
            {
                Title = "选择图像文件夹"
            };

            if (dlg.ShowDialog() == true)
            {
                FolderPathPort.Value = dlg.FolderName;
                RefreshFolderFiles();
            }
        }

        private void RefreshFolderFiles()
        {
            var folderPath = FolderPathPort.TypedValue;
            if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
            {
                // 走统一的清理入口：这里原来漏清了 PreviewImagePath，
                // 于是"文件夹被删掉"之后信息栏还挂着一个早已不存在的路径
                ClearPreview();
                SetStatus(
                    string.IsNullOrEmpty(folderPath) ? "请先选择图像文件夹" : $"文件夹不存在: {folderPath}",
                    string.IsNullOrEmpty(folderPath) ? StatusLevel.Warning : StatusLevel.Error);
                return;
            }

            try
            {
                var files = ListFolderImages(folderPath, ImageExtensions);
                FileCount = files.Count;

                if (files.Count == 0)
                {
                    PreviewImage = null;
                    PreviewImagePath = string.Empty;
                    CurrentFileName = string.Empty;
                    SetStatus("文件夹中没有匹配的图像文件", StatusLevel.Warning);
                    return;
                }

                FileIndexPort.TypedValue = Math.Clamp(FileIndexPort.TypedValue, 0, files.Count - 1);
                var currentFile = files[FileIndexPort.TypedValue];
                CurrentFileName = Path.GetFileName(currentFile);
                PreviewImagePath = currentFile;
                SetStatus($"找到 {files.Count} 张图像", StatusLevel.Info);

                // 载图是异步的，它读完后会把状态改写成"预览: xxx"或"加载预览失败: ..."——
                // 谁后完成谁生效，正好让真正的失败信息盖过这句进度提示
                LoadPreview(currentFile);
            }
            catch (Exception ex)
            {
                SetStatus($"读取文件夹失败: {ex.Message}", StatusLevel.Error);
            }
        }

        /// <summary>
        /// 载入预览图（后台读盘 + 回 UI 线程赋值）。
        ///
        /// 为什么要异步
        /// ---------
        /// ReadImage 是同步 IO，遇到大图或网络盘会长时间占住调用线程。配置态下这个方法由
        /// 浏览、刷新、翻页、Initialize 触发，全都跑在 UI 线程上，同步读盘就是"窗口假死"。
        /// 这里把读盘丢给线程池，读完再切回 UI 线程写属性（HImage 属性绑定必须在 UI 线程改）。
        ///
        /// 为什么要轮次号 + 已释放守卫
        /// ---------
        /// 后台读图期间用户可能又点了一次刷新（发起新一轮），或者干脆关掉对话框（实例已 Dispose）。
        /// 两个守卫分别拦这两种情况：过期结果只释放、绝不赋值，
        /// 否则要么用旧图盖掉新图，要么往已关闭窗口的绑定上塞 HImage。
        /// </summary>
        private void LoadPreview(string path)
        {
            // 先占号：本次载图的有效期从这里开始
            var loadId = ++_previewLoadId;

            // 存在性检查留在 UI 线程做（开销极小），这样"文件不存在"能立刻反馈，不用等线程池往返
            if (!File.Exists(path))
            {
                PreviewImage = null;
                SetStatus($"文件不存在: {path}", StatusLevel.Error);
                return;
            }

            Task.Run(() =>
            {
                HImage loaded = null;
                string error = null;

                try
                {
                    loaded = new HImage();
                    loaded.ReadImage(path);
                }
                catch (Exception ex)
                {
                    // 失败也要把 HALCON 对象收拾干净
                    loaded?.Dispose();
                    loaded = null;
                    error = ex.Message;
                }

                PostToUI(() =>
                {
                    if (loadId != _previewLoadId || _disposed)
                    {
                        loaded?.Dispose();
                        return;
                    }

                    if (loaded == null)
                    {
                        PreviewImage = null;
                        SetStatus($"加载预览失败: {error}", StatusLevel.Error);
                        return;
                    }

                    // PreviewImage 的 setter 负责释放被换下的旧图，这里不用管
                    PreviewImage = loaded;
                    SetStatus($"预览: {Path.GetFileName(path)}", StatusLevel.Info);
                });
            });
        }

        /// <summary>
        /// 清空预览及其配套的路径/计数/状态（切换采集模式、文件夹失效时调用）。
        /// 顺带作废在途的读图任务：轮次号 +1 后，旧任务回来时对不上号，只会释放不赋值。
        /// </summary>
        private void ClearPreview()
        {
            _previewLoadId++;

            PreviewImage = null;
            PreviewImagePath = string.Empty;
            CurrentFileName = string.Empty;
            FileCount = 0;
            SetStatus(string.Empty);
        }

        /// <summary>
        /// 按索引翻页（delta = ±1）。越界不用在这里判：RefreshFolderFiles 内部会 Clamp 到有效区间，
        /// 到头了再按也只是"停在最后一张"。
        /// 刻意不加 CanExecute 门控——文件数是运行期才知道的，门控还得额外调 RaiseCanExecuteChanged，
        /// 收益不抵复杂度。
        /// </summary>
        private void StepImage(int delta)
        {
            if (FileCount <= 0) return;

            FileIndexPort.TypedValue += delta;
            RefreshFolderFiles();
        }

        /// <summary>
        /// 按当前采集模式校验路径端口，结果写进信息栏（B6：边改边提示）
        /// </summary>
        private void ValidatePathInputs()
        {
            switch (Mode)
            {
                case AcquisitionMode.SingleFile:
                    CheckPath(FilePathPort, "请选择图像文件", File.Exists);
                    break;

                case AcquisitionMode.Folder:
                    CheckPath(FolderPathPort, "请选择图像文件夹", Directory.Exists);
                    break;

                default:
                    // 网络推送模式没有本地路径可校验
                    SetStatus(string.Empty);
                    break;
            }
        }

        /// <summary>
        /// 校验一个路径端口。
        /// 端口已链接变量时直接放行：路径要运行时才由上游产出，配置态无从校验，
        /// 此时报"请选择…"会让人误以为链接失效。
        /// 空值给 Warning 而非 Error——刚切模式时端口本来就是空的，报红像是坏了。
        /// </summary>
        private void CheckPath(InputPort<string> port, string emptyHint, Func<string, bool> exists)
        {
            if (port.LinkedSource != null)
            {
                SetStatus(string.Empty);
                return;
            }

            var path = port.TypedValue;

            if (string.IsNullOrWhiteSpace(path))
            {
                SetStatus(emptyHint, StatusLevel.Warning);
                return;
            }

            if (!exists(path))
            {
                SetStatus($"路径不存在: {path}", StatusLevel.Error);
                return;
            }

            SetStatus(string.Empty);
        }

        /// <summary>
        /// 把动作切回 UI 线程执行。
        /// 配置实例由对话框持有，Application.Current.Dispatcher 就是 UI 线程；
        /// 没有 Application（设计态/单元测试）或本来就在 UI 线程时直接执行，避免无谓调度与死锁。
        /// </summary>
        private static void PostToUI(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
                action();
            else
                dispatcher.Invoke(action);
        }

        #endregion

        #region 推送实现

        /// <summary>
        /// 本地图片推送测试：选一张本机图片，按 HTTP 收图服务端的契约真发一次 POST。
        ///
        /// 为什么走真 HTTP 而不是直接把图塞进 ImageHub
        /// ---------
        /// 塞槽只能验证"插件能取到图"，验证不了鉴权、URL 转义、流程触发、回包这些真正容易出错的地方。
        /// 真发一次请求，走的就是外部相机 / PLC 走的那条路，测通了才算真通。
        ///
        /// 为什么推送前先调一次 OnConfirm
        /// ---------
        /// 与"执行"按钮行为一致：把界面上改过的配置先写回步骤（SetInputValue → 流程 Version++），
        /// 服务端发现会话图纸过期会重编译，于是本次跑的就是界面上看到的配置，而不是上一次保存的旧配置。
        /// 只写内存不落盘——方案没保存就不产生文件变更。
        /// </summary>
        private async Task PushLocalImageAsync()
        {
            // 1) 参数校验放在选文件之前：参数都不对就别让用户白选一次文件
            var flowName = (PushFlowName ?? string.Empty).Trim();
            if (flowName.Length == 0)
            {
                RejectPush("流程名不能为空");
                return;
            }

            var host = (PushHost ?? string.Empty).Trim();
            if (host.Length == 0)
            {
                RejectPush("主机地址不能为空");
                return;
            }

            var portText = (PushPortText ?? string.Empty).Trim();
            if (!int.TryParse(portText, out var port) || port <= 0 || port > 65535)
            {
                RejectPush($"端口不合法: {portText}（应为 1~65535）");
                return;
            }

            // 2) 选图（沿用与"指定图像"同一套扩展名过滤）
            var dlg = new OpenFileDialog
            {
                Filter = $"图像文件|{string.Join(";", ImageExtensions.Split(',').Select(e => "*" + e))}|所有文件|*.*",
                Title = "选择要推送的本地图片"
            };

            if (dlg.ShowDialog() != true)
            {
                SetStatus("已取消选择图片", StatusLevel.Info);
                return;
            }

            // 3) 写回界面配置（见方法注释）
            OnConfirm(StepData);

            var fileName = Path.GetFileName(dlg.FileName);
            var timeoutMs = _pushTimeoutMs > 0 ? _pushTimeoutMs : 30000;

            SetStatus($"正在推送 {fileName} ...", StatusLevel.Info);
            PushResultText = string.Empty;
            var stopwatch = Stopwatch.StartNew();

            try
            {
                var bytes = await File.ReadAllBytesAsync(dlg.FileName);

                // 流程名必须转义：URL 里直接放中文 / 空格会把请求行切坏。
                // 服务端拿到路由参数后才做 Uri.UnescapeDataString，所以转义由客户端负责
                var url = $"http://{host}:{port}/flow/{Uri.EscapeDataString(flowName)}";

                using var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + (PushToken ?? string.Empty).Trim());
                request.Headers.TryAddWithoutValidation("X-Image-Name", fileName);
                request.Content = new ByteArrayContent(bytes);
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

                // 客户端超时必须大于服务端等待上限：否则服务端还在跑流程、客户端先断，
                // 用户看到的"超时"就分不清是流程慢还是网络断
                using var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(timeoutMs + 5000) };
                using var response = await client.SendAsync(request);

                var body = await response.Content.ReadAsStringAsync();
                stopwatch.Stop();

                PushResultText = FormatPushResult(response.StatusCode, body, stopwatch.ElapsedMilliseconds);
                SetStatus(
                    response.IsSuccessStatusCode
                        ? $"推送成功（HTTP {(int)response.StatusCode}，耗时 {stopwatch.ElapsedMilliseconds} ms）"
                        : $"推送被拒绝（HTTP {(int)response.StatusCode}）",
                    response.IsSuccessStatusCode ? StatusLevel.Info : StatusLevel.Error);
            }
            catch (TaskCanceledException)
            {
                stopwatch.Stop();
                PushResultText = $"等待响应超时（{timeoutMs + 5000} ms）";
                SetStatus($"推送超时: {timeoutMs + 5000} ms 内未收到响应", StatusLevel.Error);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                PushResultText = $"{ex.GetType().Name}: {ex.Message}";
                SetStatus($"推送失败: {ex.Message}", StatusLevel.Error);
            }
        }

        /// <summary>
        /// 请求还没发出去就被本地校验拦下：结果区与信息栏一起说明原因
        /// </summary>
        private void RejectPush(string message)
        {
            PushResultText = message;
            SetStatus($"推送未发起: {message}", StatusLevel.Error);
        }

        /// <summary>
        /// 把服务端回包整理成可读的多行文本（状态码 + 关键字段 + 非 JSON 时原样回显）。
        /// 服务端成功回 { success, requestId, flow, elapsedMs, image{width,height,channels}, outputs }，
        /// 失败回 { success=false, message }（见 HttpImageServer.Fail）
        /// </summary>
        private static string FormatPushResult(HttpStatusCode statusCode, string body, long elapsedMs)
        {
            var lines = new List<string> { $"HTTP {(int)statusCode} {statusCode}    客户端耗时 {elapsedMs} ms" };

            JObject json = null;
            try
            {
                json = JObject.Parse(body);
            }
            catch
            {
                // 非 JSON 回包（网关 / 代理的 HTML 错误页等）：下面原样回显，比"解析失败"有用
            }

            if (json == null)
            {
                if (!string.IsNullOrWhiteSpace(body))
                    lines.Add(body.Trim());
                return string.Join(Environment.NewLine, lines);
            }

            var image = json["image"];
            if (json["flow"] != null) lines.Add($"流程: {json["flow"]}");
            if (image != null) lines.Add($"图像: {image["width"]}x{image["height"]}x{image["channels"]}");
            if (json["elapsedMs"] != null) lines.Add($"服务端耗时: {json["elapsedMs"]} ms");
            if (json["outputs"] != null) lines.Add($"输出: {json["outputs"].ToString(Formatting.None)}");
            if (json["message"] != null) lines.Add($"错误: {json["message"]}");

            return string.Join(Environment.NewLine, lines);
        }

        #endregion

        #region 辅助方法

        /// <summary>
        /// 列出文件夹中符合扩展名的图像文件（预览辅助与执行核心共用同一过滤逻辑）
        /// </summary>
        private static List<string> ListFolderImages(string folderPath, string extensions)
        {
            var extSet = new HashSet<string>(
                (extensions ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(e => e.Trim().ToLower()),
                StringComparer.Ordinal
            );

            return Directory.GetFiles(folderPath)
                .Where(f => extSet.Contains(Path.GetExtension(f).ToLower()))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        #endregion

        #region 生命周期

        public override void Initialize()
        {
            _cachedFiles.Clear();
            _cachedFolderPath = string.Empty;
            _cachedExtensions = string.Empty;
        }

        public override void Dispose()
        {
            // 先立标记再释放：在途的读图任务回来时会被守卫拦下（只释放不赋值），
            // 不会往已经关闭的对话框绑定上写图
            _disposed = true;
            _previewLoadId++;

            if (OutputImage.Value is HImage bmp)
            {
                bmp.Dispose();
                OutputImage.Value = null;
            }

            // 配置实例关闭时释放预览图：置 null 由 setter 统一释放旧实例，避免重复 Dispose
            PreviewImage = null;
        }

        #endregion
    }
}
