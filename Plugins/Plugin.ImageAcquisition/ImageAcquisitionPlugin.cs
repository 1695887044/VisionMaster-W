using Core.Events;
using Core.Interfaces;
using HalconDotNet;
using Microsoft.Win32;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Prism.Commands;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
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
    public partial class ImageAcquisitionPlugin : VisionPluginBase, IPluginCustomViewProvider, IPluginConfigContextProvider
    {
        #region 配置属性（纯配置项：持久化到 InputValues、参与灌值，但不进端口/不可变量链接）

        /// <summary>
        /// 采集模式：指定图像 / 文件目录 / 网络推送 / 相机采集（见 <see cref="AcquisitionMode"/>）。
        /// 换模式会清空预览与状态：旧模式的数据源在新模式下对不上号。
        /// </summary>
        [StepConfig]
        public partial AcquisitionMode Mode { get; set; }

partial void OnModeChanged(AcquisitionMode value)
{
    // 换模式等于换数据源：旧模式的预览图/路径/计数在新模式下全对不上号，
    // 留着只会让界面自相矛盾（比如"文件目录"模式下还挂着上次的单图预览）
    ClearPreview();
    ValidatePathInputs();
    if (value == AcquisitionMode.Folder)
    {
        // 进入文件夹模式后立即刷新目录，防止旧目录列表残留
        RefreshFolderFiles();
    }
}


        /// <summary>
        /// 显示窗口索引：采集图像发布到主界面几号视图窗口（1~9），0=不显示。
        ///
        /// 写入即夹取到 0~9：这是落盘的 [StepConfig]，手改方案文件可以塞进 10/负数，
        /// 而消费端是按"ViewIndex == 窗口号(1~9)"等值筛选的——越界值不会报错，
        /// 表现为"配了显示但哪一格都不显示"，属于最难归因的一类。
        /// </summary>
        [StepConfig, DefaultValue(1)]
        public partial int DisplayViewIndex { get; set; }

        partial void OnDisplayViewIndexChanging(ref int value) => value = Math.Clamp(value, 0, 9);

        /// <summary>
        /// 相机采集：目标相机的序列号。
        ///
        /// 为什么存序列号而不是内部 Id / 相机名
        /// ---------
        /// 序列号是对外寻址键（客户端收图 URL 里的那一段），也是方案里唯一稳定的相机标识：
        /// 存 Id 没法在界面上显示也没法人工核对；存显示名则改个名就断链，而且重名无约束。
        /// 宿主侧 CameraProvider.TryGetDeviceBySerial 与它同口径，本步骤不必自己查表。
        /// </summary>
        [StepConfig, DefaultValue("")]
        public partial string CameraSerial { get; set; }

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

        /// <summary>
        /// 相机采集：等一帧的超时（毫秒）。
        ///
        /// 做成端口而不是纯配置项，是为了让"等多久"可以被上游变量控制（不同产品的节拍不一样）；
        /// 填 0 表示"用相机自身配置里的超时"（见 CameraSettings.FrameTimeoutMs）。
        /// 这个口存在的意义就是**绝不能没有超时**：相机一旦不触发，没有超时的等图会让
        /// 流程线程永久卡死，现场只能杀进程。
        /// </summary>
        public InputPort<int> FrameTimeoutPort { get; } = new(
            "FrameTimeoutMs",
            0,
            "相机取图超时（毫秒）；0 = 使用相机自身配置的超时"
        )
        { IsRequired = false };

        #endregion

        #region 输出端口（Success/ErrorMessage 由基类提供）

        /// <summary>
        /// 采集到的图像
        /// </summary>
        public OutputPort<HImage?> OutputImage { get; } = new(
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

        /// <summary>
        /// 相机帧序号（从 1 开始）。仅相机模式有效，其余模式为 0。
        ///
        /// 为什么与 CurrentIndex 并存：CurrentIndex 在相机模式下被复用为"第几帧"（见 AcquisitionMode.Camera
        /// 的说明），那是为了不让既有下游接线全部重连的兼容做法；本端口给出**不做任何语义重载**的帧号，
        /// 帧溯源（日志对账 / 结果归档）一律用它，不必再记住"哪种模式下 CurrentIndex 是什么"。
        /// </summary>
        public OutputPort<long> FrameId { get; } = new(
            "FrameId",
            "相机帧序号（从 1 开始）；非相机模式为 0"
        );

        /// <summary>
        /// 相机累计收到的帧数（含溢出被丢弃的帧）。仅相机模式有效，其余模式为 0。
        /// 与 TotalFiles 的分工同上：一个是"这台相机一共收了多少帧"，一个是"目录里有多少张图"。
        /// </summary>
        public OutputPort<long> ReceivedCount { get; } = new(
            "ReceivedCount",
            "相机累计收到帧数；非相机模式为 0"
        );

        /// <summary>
        /// 相机序列号（多相机身份，供下游自动核对）：相机模式 = 本步骤配置的相机序列号；其余模式为空串。
        /// 用途：把本输出接到「标定」/「坐标变换」的 SourceSerial 输入——换相机（换镜头/换工位）后，
        /// "这份标定属于哪台"与"当前图像来自哪台"对不上会**明确失败**，旧标定自证失效。
        /// </summary>
        public OutputPort<string> SourceSerial { get; } = new(
            "SourceSerial",
            "相机序列号（相机模式=本步骤配置的相机；非相机模式为空）"
        );

        #endregion

        #region 预览与状态（纯界面属性，不参与流程数据流）

        private HImage? _previewImage;
        /// <summary>
        /// 预览图像（换图即弃旧：SetProperty 成功后释放旧实例，避免非托管内存泄漏）
        /// 安全性：ImageReadOnly 的 HImage 依赖属性持引用不复制，绑定在 UI 线程同步刷新，
        /// SetProperty 已把 DP 切到新值并完成重绘，此处释放的是"已不被 UI 引用"的旧图
        ///
        /// 初值是 null 而不是 new HImage()：构造 HImage 本身就要碰 HALCON 原生库，
        /// 而本类会被 PluginService 在**启动扫描端口**时实例化——无 HALCON 的机器上
        /// 一个从不使用的空预览图会把整个插件 DLL 判成"加载失败"（见 PluginService 的按 DLL 捕获），
        /// 与"无 HALCON 时流程编排照常可用、只降级视觉功能"的既定口径冲突。
        /// 用到时再建（LoadPreview 里建），不用就不碰引擎。
        /// </summary>
        public HImage? PreviewImage
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

        #region 相机采集（配置态：相机候选列表 + 状态提示）

        /// <summary>
        /// 可选相机列表（由宿主通过 SetConfigContext 推过来）。
        /// 快照而非活对象：插件不该持有宿主方案里的相机描述符（见 PluginConfigContext.Cameras）。
        ///
        /// 注意这里**刻意没有"预览相机图像"按钮**：预览需要拿到设备对象，而设备只存在于宿主的
        /// CameraProvider 里（配置上下文给的是快照，不含设备）。硬要预览就得让插件持有宿主对象，
        /// 破坏"插件只认契约"这条线。相机图像预览因此归于「系统 → 相机设置」——
        /// 那里本来就有 Live 预览，也是更该检查相机的地方。本步骤要验证"能不能取到图"，
        /// 用配置窗口的「执行（试运行）」即可，它跑的就是正式运行的同一段代码。
        /// </summary>
        public ObservableCollection<CameraOption> CameraOptions { get; } = new();

        private CameraOption? _selectedCameraOption;
        /// <summary>
        /// 下拉选中的相机。
        ///
        /// 为什么要有这个"影子属性"而不是直接双向绑定 CameraSerial：
        /// 下拉的候选项是 CameraOption 对象，而落盘的只有序列号。中间隔一层，
        /// 才能做到"序列号在方案里、候选列表在运行期"，并且序列号对应的相机被删掉时
        /// 下拉能诚实显示为"空"而不是停在一个不存在的项上。
        /// 类型可空是刻意的：没选相机 / 序列号对不上任何候选项时它就是 null。
        /// </summary>
        public CameraOption? SelectedCameraOption
        {
            get => _selectedCameraOption;
            set
            {
                if (!SetProperty(ref _selectedCameraOption, value)) return;

                CameraSerial = value?.SerialNo ?? string.Empty;
                ValidatePathInputs();
            }
        }

        private string _cameraStateText = string.Empty;
        /// <summary>
        /// 相机状态提示（"采流中 / 等待接入 / 未连接"），由 ValidateCameraSelection 写入
        /// </summary>
        public string CameraStateText
        {
            get => _cameraStateText;
            private set => SetProperty(ref _cameraStateText, value);
        }

        /// <summary>
        /// 接收宿主下发的相机候选列表，并按序列号回显选中项。
        ///
        /// 为什么必须做"回显"这一步：落盘的是序列号，而下拉的候选项是运行时对象，
        /// 两者只能按序列号对齐。不做这一步，重新打开配置窗口时下拉永远是空的，
        /// 用户会以为自己的配置丢了——其实它在方案里好好存着。
        /// </summary>
        private void ApplyCameraOptions(IReadOnlyList<CameraOption> options)
        {
            CameraOptions.Clear();
            if (options != null)
            {
                foreach (var option in options) CameraOptions.Add(option);
            }

            var serial = (CameraSerial ?? string.Empty).Trim();
            _selectedCameraOption = serial.Length == 0
                ? null
                : CameraOptions.FirstOrDefault(o => string.Equals(o.SerialNo, serial, StringComparison.OrdinalIgnoreCase));
            OnPropertyChanged(nameof(SelectedCameraOption));

            ValidatePathInputs();
        }

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

        private StatusLevel _pushServiceLevel = StatusLevel.Info;
        /// <summary>
        /// 推送服务状态等级：驱动"网络推送"页状态提示的颜色。
        /// "正在监听"是正常态，用 Info（绿）；没在监听才是 Warning（橙）——
        /// 原先这里恒挂警告色，服务好好的也像出了问题。
        /// </summary>
        public StatusLevel PushServiceLevel
        {
            get => _pushServiceLevel;
            private set => SetProperty(ref _pushServiceLevel, value);
        }

        /// <summary>
        /// 推送服务状态的一句话 + 级别：由 SetConfigContext 写入，
        /// 是否进信息栏由 <see cref="ValidatePathInputs"/> 按当前采集模式决定——
        /// 只在"网络推送"模式下显示（其他模式下用户看到的是路径/相机提示，不该被它盖掉）。
        /// </summary>
        private string _pushServiceMessage = string.Empty;
        private StatusLevel _pushServiceMessageLevel = StatusLevel.Info;

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
            // 与其让用户点完按钮再猜原因，不如打开窗口就说清楚。
            // 文案与级别一起记下，信息栏是否采纳由 ValidatePathInputs 按模式决定
            // （只有"网络推送"模式才该在信息栏说这件事）。
            if (context.HttpListening)
            {
                PushServiceHint = $"宿主收图服务正在监听 {context.HttpHost}:{context.HttpPort}";
                _pushServiceMessage = $"推送测试已就绪（目标 {context.HttpHost}:{context.HttpPort}）";
                _pushServiceMessageLevel = StatusLevel.Info;
            }
            else
            {
                PushServiceHint = context.HttpEnabled
                    ? "宿主收图服务配置为启用但未在监听（端口被占用或启动失败），推送会连接失败"
                    : "宿主收图服务未启用（AppConfig.json → HttpImageServer.Enabled=false），推送会连接失败";
                _pushServiceMessage = PushServiceHint;
                _pushServiceMessageLevel = StatusLevel.Warning;
            }
            PushServiceLevel = _pushServiceMessageLevel;

            // 相机候选列表随上下文一起下发。放在这里而不是 Initialize：
            // Initialize 只拿到步骤的已存配置，拿不到"宿主现在有哪些相机"——
            // 而这一步正是"序列号回显对不对得上"的判定依据。
            // ApplyCameraOptions 内部会调 ValidatePathInputs：Hub 模式下它会把上面的
            // 推送服务状态写进信息栏，其他模式则保持各自的路径/相机提示不被覆盖。
            ApplyCameraOptions(context.Cameras);
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
            public HImage? Image;
            public string CurrentPath = "";
            public int CurrentIndex;
            public int TotalFiles;

            /// <summary>相机帧序号（仅相机模式写入；见 FrameId 输出口的说明）</summary>
            public long FrameId;

            /// <summary>相机累计收帧数（仅相机模式写入）</summary>
            public long ReceivedCount;

            /// <summary>
            /// 等图被"停止流程"打断（相机 / 网络推送模式会置位）。
            /// 既不是成功也不是失败：流程去向由引擎的取消语义决定，插件不参与。
            /// </summary>
            public bool Cancelled;

            /// <summary>取消时的人话说明（相机 / 网络两种模式的文案在这里区分）</summary>
            public string? CancelMessage;
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

                case AcquisitionMode.Camera:
                    return AcquireFromCamera(context);

                default:
                    return new CoreResult { Error = $"未知的采集模式: {mode}" };
            }
        }

        /// <summary>
        /// 相机取图：从方案里已配置的相机**消费**一帧（每帧只会被取到一次）。
        ///
        /// 三种收尾必须分清楚，它们的处置完全不同：
        ///   1) 取消（用户点了停止流程）—— 本步没产出，但不是业务失败，交回引擎按取消语义处理；
        ///   2) 超时（相机没出图）—— 业务失败，错误信息里必须带上相机状态与队列帧数，
        ///      否则现场只知道"超时了"，分不清是掉线、没触发还是流程太慢没消费完；
        ///   3) 配置错（序列号没选 / 相机不存在 / 未连接）—— 业务失败，并给出可操作的下一步。
        ///
        /// 为什么不做"重试等下一帧"：这一帧没等到就是没等到，继续等只会让流程越来越滞后于生产。
        /// 产线上"这一件没检出"必须被明确记录，而不是靠等待把它掩盖过去。
        /// </summary>
        private CoreResult AcquireFromCamera(IExecutionContext context)
        {
            var serial = (CameraSerial ?? string.Empty).Trim();
            if (serial.Length == 0)
                return new CoreResult { Error = "尚未选择相机，请在「相机采集」页选择一个已配置的相机" };

            // 插件只认契约：相机仓库由宿主通过执行上下文递过来（插件够不到宿主程序集）
            if (!context.Cameras.TryGetDeviceBySerial(serial, out var device) || device == null)
                return new CoreResult
                {
                    Error = $"找不到序列号为「{serial}」的相机。请到「系统 → 相机设置」添加该相机，"
                          + "或回到本步骤的「相机采集」页重新选择"
                };

            if (device.State == CameraConnectionState.Closed)
                return new CoreResult
                {
                    Error = $"相机「{device.Descriptor.Caption}」未连接（{device.StateDetail}）。"
                          + "请到「系统 → 相机设置」连接该相机后再运行"
                };

            var timeoutMs = FrameTimeoutPort.GetTypedValue();
            if (timeoutMs <= 0) timeoutMs = device.ReadSettings().FrameTimeoutMs;

            if (!device.WaitNextFrame(out var frame, timeoutMs, context.CancellationToken))
            {
                // 取消优先判定：令牌已取消时超时是"被取消顺带产生的"，报超时会把用户自己的操作说成故障
                if (context.CancellationToken.IsCancellationRequested)
                    return new CoreResult { Cancelled = true, CancelMessage = "已停止等待相机图像" };

                return new CoreResult
                {
                    Error = $"等待相机「{device.Descriptor.Caption}」出图超时（{timeoutMs} ms）"
                          + $"；相机状态：{device.StateDetail}，待消费 {device.PendingFrameCount} 帧，"
                          + $"累计收帧 {device.ReceivedFrameCount}，溢出丢帧 {device.OverflowCount}"
                };
            }

            if (frame == null || !frame.IsValid)
                return new CoreResult { Error = "相机返回的帧数据无效（宽高与像素字节数不自洽）" };

            return new CoreResult
            {
                Success = true,
                Image = ToHImage(frame),
                // 来源名由客户端声明（通常就是文件名），拿不到时回落到伪路径，保证输出口不为空
                CurrentPath = string.IsNullOrEmpty(frame.SourceName) ? $"camera://{serial}" : frame.SourceName,
                // CurrentIndex/TotalFiles 在相机模式下沿用"第几帧 / 累计收到多少帧"的重载语义：
                // 这套语义 2026-09 就随相机模式上线了，改成 0 会悄悄打断已有接线。
                // 精确值由 FrameId / ReceivedCount 两个专用输出口给出，新接线一律用那对。
                CurrentIndex = frame.FrameId > int.MaxValue ? int.MaxValue : (int)frame.FrameId,
                TotalFiles = device.ReceivedFrameCount > int.MaxValue ? int.MaxValue : (int)device.ReceivedFrameCount,
                FrameId = frame.FrameId,
                ReceivedCount = device.ReceivedFrameCount,
                Error = $"已从相机取图: {frame.Width}x{frame.Height}x{frame.Channels}"
                      + $"（第 {frame.FrameId} 帧，来源 {frame.SourceName}）"
            };
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
                    return new CoreResult { Cancelled = true, CancelMessage = "已停止等待网络图像" };
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

        /// <summary>网络推送帧 → HImage（与相机采集共用同一个转换核心）</summary>
        private static HImage ToHImage(HubImageItem item)
            => ToHImage(item.PixelData, item.Width, item.Height, item.Channels);

        /// <summary>相机帧 → HImage（像素约定与网络推送完全一致，所以共用同一个转换核心）</summary>
        private static HImage ToHImage(CameraFrame frame)
            => ToHImage(frame.PixelData, frame.Width, frame.Height, frame.Channels);

/// <summary>
        /// 原始像素字节 → HImage 的**唯一**转换核心（网络推送与相机采集共用）。
        ///
        /// 为什么必须只有一处：两条链路的像素约定必须逐字节一致（灰度单通道、彩色 BGR 交错）。
        /// 各写一份的话，早晚出现"相机采的彩色图颜色是反的、网络推送的却是对的"——
        /// 而灰度图上完全看不出来，只表现为彩色判别类算子结果莫名不对。
        ///
        /// 入参校验（这一版补上的三处）
        /// ---------
        ///   · 尺寸上限与长度：宽高来自外部（相机 SDK / HTTP 客户端），必须用 long 算
        ///     `宽×高×通道` 再比——int 乘法溢出成负数时，"长度不足"这道校验反而会放行，
        ///     最后以"分配负数长度"的形式抛一句看不懂的错；
        ///   · 数据长度必须 ≥ 宽×高×通道数：不足时 HALCON 会越界读那块缓冲，
        ///     轻则图像错乱、重则进程崩掉 —— 必须在进非托管世界之前拦下；
        ///   · 4 通道及以上（BGRA，很多相机 SDK 的默认输出）不能直接交给 GenImageInterleaved：
        ///     它按 3 字节/像素读 4 字节/像素的缓冲，不报错但图像整体错位花屏
        ///    （灰度图上同样看不出来）。先抽成紧凑 BGR 再走同一条路。
        ///     但 2 通道没有对应的解释方式：与其让 GenImage1 把前半段数据当灰度图静默显示，
        ///     不如在入口明确拒绝（契约允许的只有 1=灰度 / 3=BGR / ≥4=BGRA/RGBA）。
        ///
        /// 为什么走非托管中转
        /// ---------
        /// HALCON 的 gen_image1 / gen_image_interleaved 只认裸指针，没有 byte[] 重载。
        /// gen_image1 会把指针指向的数据**复制**进新建的图（这正是它与 gen_image1_extern 的区别：
        /// 后者是"引用 + 归还回调"），所以指针只要在调用期间有效即可——
        /// 用 GCHandle(Pinned) 钉住托管数组就能满足，不需要再 AllocHGlobal + Copy 一次。
        ///
        /// alignment 传 -1 表示"行间无填充"，正好匹配宿主/客户端输出的紧凑 stride。
        /// </summary>
        private static HImage ToHImage(byte[] pixelData, int width, int height, int channels)
        {
            if (width <= 0 || height <= 0)
                throw new ArgumentException($"图像尺寸非法: {width}x{height}");
            if (channels < 1)
                throw new ArgumentException($"图像通道数非法: {channels}");
            if (channels == 2)
                throw new ArgumentException(
                    "不支持的图像通道数: 2（仅支持 1=灰度 / 3=BGR / 4=BGRA；"
                    + "2 通道没有约定好的解释方式，直接按灰度读会把图读错半张）");

            var expected = (long)width * height * channels;
            if (expected > int.MaxValue)
                throw new ArgumentException(
                    $"图像过大: {width}x{height}x{channels} = {expected} 字节，超过单幅图像上限");

            if (pixelData.Length < expected)
                throw new ArgumentException(
                    $"像素数据不足: {pixelData.Length} 字节，{width}x{height}x{channels} 应为 {expected} 字节"
                    + "（图像的宽高与像素字节数不自洽）");

            // 4 通道及以上（BGRA/RGBA）：抽掉 alpha 通道，压成紧凑的 BGR 三通道。
            // pixelCount 不会溢出：expected = pixelCount×channels ≤ int.MaxValue，且压缩后更小
            if (channels > 3)
            {
                var pixelCount = width * height;
                var compact = new byte[pixelCount * 3];
                for (int p = 0; p < pixelCount; p++)
                {
                    compact[p * 3] = pixelData[p * channels];
                    compact[p * 3 + 1] = pixelData[p * channels + 1];
                    compact[p * 3 + 2] = pixelData[p * channels + 2];
                }
                pixelData = compact;
                channels = 3;
            }

            var image = new HImage();
            try
            {
                // 用 GCHandle 把托管数组钉住，直接把它首地址交给 HALCON，省掉一次
                // "AllocHGlobal + 全帧 Copy" 的中转。
                //
                // 为什么 pin 到调用返回就可以解：gen_image1 / gen_image_interleaved 是**复制**语义
                // （与 gen_image1_extern 的"引用 + 归还回调"相对），返回前像素已进 HALCON 自己的内存，
                // 所以本次转换的生存期模型与原来的 HGlobal 版本完全一致——
                // byte[] 队列语义、溢出丢弃、基类 AutoDisposeRoundOutputs 都不受影响。
                //
                // 注意必须用 AddrOfPinnedObject()：GCHandle.ToIntPtr() 返回的是"句柄令牌"
                // （供 GCHandle.FromIntPtr 还原用），不是被钉住对象的数据地址。
                // GCHandle 是结构体、不实现 IDisposable，所以这里用 try/finally 而不是 using，
                // 保证 HALCON 调用抛异常时也一定 Free 掉（钉住的数组会阻止 GC 压缩堆）。
                var handle = GCHandle.Alloc(pixelData, GCHandleType.Pinned);
                try
                {
                    var pointer = handle.AddrOfPinnedObject();

                    if (channels >= 3)
                    {
                        image.GenImageInterleaved(
                            pointer,
                            "bgr",
                            width,
                            height,
                            -1,
                            "byte",
                            width,
                            height,
                            0,
                            0,
                            -1,
                            0);
                    }
                    else
                    {
                        image.GenImage1("byte", width, height, pointer);
                    }
                }
                finally
                {
                    handle.Free();
                }

                return image;
            }
            catch
            {
                // 与 AcquireSingleFile / AcquireFromFolder 同一约定：进过非托管世界的对象，
                // 失败要自己收拾干净再上抛，否则连续失败会持续吃 HALCON 的非托管内存
                image.Dispose();
                throw;
            }
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

            // 每轮重新枚举（与预览的 ListFolderImages 共用同一份过滤+排序）。
            // 早期版本在这里做过目录缓存且永不失效："拍图落盘"式工作目录里新增的文件永远读不到、
            // 被删的文件还占着索引，ReadImage 直接抛"文件不存在"变整步失败 —— 得不偿失，
            // 本地目录的一次 GetFiles 也就几毫秒。
            var files = ListFolderImages(folderPath, extensions);

            if (files.Count == 0)
                return new CoreResult { Error = $"文件夹内未找到符合条件的图像文件: {folderPath}", TotalFiles = 0 };

            int requested = fileIndex;
            if (fileIndex < 0) fileIndex = 0;
            if (fileIndex >= files.Count) fileIndex = files.Count - 1;

            var path = files[fileIndex];
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
                    TotalFiles = files.Count,
                    Error = $"已加载图像 [{fileIndex + 1}/{files.Count}]: {path}"
                          // 越界夹取必须留痕：批处理场景里上游算错的索引不该被静默吞掉，
                          // 否则表现为"同一张产品被反复检测"，现场极难归因
                          + (requested == fileIndex ? "" : $"（索引 {requested} 超出 0~{files.Count - 1}，已夹取）")
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
            // 用 TypedValue（强类型口）写：Value 是弱类型 object 口，写 null 会触发可空性告警；
            // 两者最终落到同一个字段，通知也等价
            OutputImage.TypedValue = null;
            CurrentFilePath.Value = string.Empty;
            // 索引/总数/帧号必须一起归零：否则本轮失败时它们还停在上轮的值，
            // 下游按"当前索引/总数"做判断的步骤会读到上一轮的残留数据（基类只清 IDisposable 端口值）
            CurrentFileIndex.Value = 0;
            TotalFiles.Value = 0;
            FrameId.Value = 0;
            ReceivedCount.Value = 0;
            SourceSerial.Value = string.Empty;   // 相机身份：失败轮不得留上一轮的序列号（下游会据此自动核对）

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
                    ErrorMessage.Value = string.IsNullOrEmpty(result.CancelMessage)
                        ? "已停止等待图像"
                        : result.CancelMessage;
                    return;
                }

                Success.Value = result.Success;

                if (result.Success)
                {
                    ErrorMessage.Value = string.Empty;
                    OutputImage.TypedValue = result.Image;
                    CurrentFilePath.Value = result.CurrentPath;
                    CurrentFileIndex.Value = result.CurrentIndex;
                    TotalFiles.Value = result.TotalFiles;
                    FrameId.Value = result.FrameId;
                    ReceivedCount.Value = result.ReceivedCount;
                    SourceSerial.Value = Mode == AcquisitionMode.Camera
                        ? (CameraSerial ?? string.Empty).Trim()
                        : string.Empty;
                    context.Logger?.Info($"{InstanceName} {result.Error}");

                    // 发布到主程序视图（DisplayViewIndex 已是真实窗口号 1~9；0=不显示则跳过）。
                    // 推图是"锦上添花"：事件总线在无 UI 环境会就地同步执行订阅者，
                    // 投递失败只记日志，绝不能把一次成功的采集判成异常
                    if (DisplayViewIndex > 0 && result.Image != null)
                        TryPublishPreview(result.Image, DisplayViewIndex, context);
                }
                else
                {
                    ErrorMessage.Value = result.Error ?? string.Empty;
                    context.Logger?.Error($"{InstanceName} {result.Error}");
                }
            }
            catch (Exception ex)
            {
                ErrorMessage.Value = ex.Message;
                context.Logger?.Error($"{InstanceName} 图像采集异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 推图到主界面。
        /// 显示层拿到事件后自己 CopyImage 并独占那份副本，所以这里传的仍是端口持有的图；
        /// 但推图属于"锦上添花"，投递失败绝不能把一次正常的采集判成失败
        /// （与预处理插件 TryPublishPreview 同一约定）。
        /// </summary>
        private void TryPublishPreview(HImage image, int viewIndex, IExecutionContext context)
        {
            try { this.PublishPreview(image, viewIndex); }
            catch (Exception ex) { context.Logger?.Warn($"{InstanceName} 预览推送失败: {ex.Message}"); }
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

            // 其余情况（未选文件 / 路径已失效 / 相机未选 / 网络推送）：交给统一校验给出提示，
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
            // 端口被变量链接时，配置态拿不到"运行期到底是哪个目录"（手动值是空的或过期的），
            // 预览必然文不对题。这里给中性说明并跳过，与 CheckPath"链接端口直接放行"
            // 同一口径——绝不能报成"请先选择图像文件夹"，那会让人误以为链接失效了
            if (FolderPathPort.LinkedSource != null)
            {
                ClearPreview();
                SetStatus("文件夹路径来自变量链接，运行期生效；配置态无法预览", StatusLevel.Info);
                return;
            }

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
                HImage? loaded = null;
                string? error = null;

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
                    SetStatus($"预览: {Path.GetFileName(path)}{IndexLinkNote()}", StatusLevel.Info);
                });
            });
        }

        /// <summary>
        /// 文件索引被变量链接时给预览加一句说明：预览按手动值载图只是"看个大概"，
        /// 运行期以链接值为准。不加这句，用户翻页翻得热闹、运行起来却不是这张图。
        /// </summary>
        private string IndexLinkNote()
            => FileIndexPort.LinkedSource != null
                ? "（文件索引来自变量链接，运行期以链接值为准）"
                : string.Empty;

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

                case AcquisitionMode.Camera:
                    ValidateCameraSelection();
                    break;

                case AcquisitionMode.Hub:
                    // 网络推送模式没有本地路径可校验：信息栏改说"推送服务能不能用"
                    // （文案/级别由 SetConfigContext 写入；未收到上下文时为空，等同原来的清空）
                    ClearCameraState();
                    SetStatus(_pushServiceMessage, _pushServiceMessageLevel);
                    break;

                default:
                    ClearCameraState();
                    SetStatus(string.Empty);
                    break;
            }
        }

        /// <summary>
        /// 校验相机选择（B6：边改边提示，切到相机页立刻告诉用户"现在能不能跑"）。
        ///
        /// 判据全部来自宿主下发的快照（CameraOptions），不在这里查设备——
        /// 配置态本来就够不到设备，硬查只会引入"有时查得到有时查不到"的不确定性。
        /// 状态文字与状态等级分开给：等级决定颜色，文字解释"该做什么"。
        /// </summary>
        private void ValidateCameraSelection()
        {
            var serial = (CameraSerial ?? string.Empty).Trim();

            if (serial.Length == 0)
            {
                CameraStateText = "未选择";
                SetStatus(
                    CameraOptions.Count == 0
                        ? "当前方案还没有配置任何相机。请先到「系统 → 相机设置」添加并连接一台相机"
                        : "请选择一个相机",
                    CameraOptions.Count == 0 ? StatusLevel.Error : StatusLevel.Warning);
                return;
            }

            var option = CameraOptions.FirstOrDefault(o =>
                string.Equals(o.SerialNo, serial, StringComparison.OrdinalIgnoreCase));

            if (option == null)
            {
                // 序列号配了但候选里没有：相机被删了或被改了序列号。此时必须报错而不是静默通过，
                // 否则要等到运行时才以"找不到相机"暴露，而那时用户早已忘了自己动过什么
                CameraStateText = "不在当前方案中";
                SetStatus(
                    $"本步骤引用的相机「{serial}」不在当前方案的相机列表里。"
                    + "请到「系统 → 相机设置」确认，或回到本页重新选择",
                    StatusLevel.Error);
                return;
            }

            CameraStateText = option.StateText;

            switch (option.State)
            {
                case CameraConnectionState.Streaming:
                    SetStatus(string.Empty);
                    break;

                case CameraConnectionState.Online:
                    // 已连接但没开始采流：程序上不算错，但运行必然取不到图，必须提前说清楚
                    SetStatus(
                        $"相机「{option.Caption}」已连接但未开始采流，运行时会取不到图像。"
                        + "请到「系统 → 相机设置」点「开始采流」",
                        StatusLevel.Warning);
                    break;

                case CameraConnectionState.Connecting:
                    SetStatus(
                        $"相机「{option.Caption}」{option.StateText}，运行时会因取不到图像而失败。"
                        + "网络相机请确认客户端已启动且序列号填写一致",
                        StatusLevel.Warning);
                    break;

                default:
                    SetStatus(
                        $"相机「{option.Caption}」尚未连接，运行时会失败。请到「系统 → 相机设置」连接该相机",
                        StatusLevel.Error);
                    break;
            }
        }

        /// <summary>切走相机模式时清掉相机状态栏，避免"文件夹模式下还挂着相机的状态"</summary>
        private void ClearCameraState()
        {
            CameraStateText = string.Empty;
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

            JObject? json = null;
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

            // is { } 模式一次拿到非空 token：JObject 的索引器可空，直接 json["x"].ToString() 会被判空警告
            if (json["flow"] is { } flow) lines.Add($"流程: {flow}");
            var image = json["image"];
            if (image != null) lines.Add($"图像: {image["width"]}x{image["height"]}x{image["channels"]}");
            if (json["elapsedMs"] is { } serverElapsedMs) lines.Add($"服务端耗时: {serverElapsedMs} ms");
            if (json["outputs"] is { } outputs) lines.Add($"输出: {outputs.ToString(Formatting.None)}");
            if (json["message"] is { } message) lines.Add($"错误: {message}");

            return string.Join(Environment.NewLine, lines);
        }

        #endregion

        #region 辅助方法

        /// <summary>
        /// 列出文件夹中符合扩展名的图像文件（预览辅助与执行核心共用同一过滤逻辑）。
        /// 排序用自然序（见 <see cref="NaturalOrderComparer"/>）：产线序列图普遍是 1.bmp…10.bmp，
        /// 纯字符串序会把 10 排到 2 前面——而"文件索引"是喂给生产用的，顺序错了就是工件顺序错了。
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
                .OrderBy(f => f, NaturalOrderComparer.Instance)
                .ToList();
        }

        /// <summary>
        /// 路径自然序比较器：把连续数字段按数值比较（img2 &lt; img10），其余字符忽略大小写按序数比较，
        /// 与资源管理器对文件名的排序观感一致。
        /// 实现不做区域/文化敏感比较（不用 CultureInfo）：同一台设备上排序必须永远稳定。
        /// </summary>
        private sealed class NaturalOrderComparer : IComparer<string>
        {
            public static readonly NaturalOrderComparer Instance = new();

            public int Compare(string? x, string? y)
            {
                if (ReferenceEquals(x, y)) return 0;
                if (x == null) return -1;
                if (y == null) return 1;

                int i = 0, j = 0;
                while (i < x.Length && j < y.Length)
                {
                    if (char.IsDigit(x[i]) && char.IsDigit(y[j]))
                    {
                        int iStart = i, jStart = j;
                        while (i < x.Length && char.IsDigit(x[i])) i++;
                        while (j < y.Length && char.IsDigit(y[j])) j++;

                        string nx = x.Substring(iStart, i - iStart);
                        string ny = y.Substring(jStart, j - jStart);

                        // 去前导零后按"长度 → 字典序"比数值（不解析成整数，避免超长数字溢出）
                        string tx = nx.TrimStart('0');
                        string ty = ny.TrimStart('0');
                        if (tx.Length != ty.Length) return tx.Length - ty.Length;
                        int c = string.CompareOrdinal(tx, ty);
                        if (c != 0) return c;

                        // 数值相同（"1" vs "01"）：前导零少的在前，保证排序结果唯一
                        if (nx.Length != ny.Length) return nx.Length - ny.Length;
                    }
                    else
                    {
                        int c = char.ToUpperInvariant(x[i]).CompareTo(char.ToUpperInvariant(y[j]));
                        if (c != 0) return c;
                        i++;
                        j++;
                    }
                }

                return (x.Length - i) - (y.Length - j);
            }
        }

        #endregion

        #region 生命周期

        public override void Dispose()
        {
            // 先立标记再释放：在途的读图任务回来时会被守卫拦下（只释放不赋值），
            // 不会往已经关闭的对话框绑定上写图
            _disposed = true;
            _previewLoadId++;

            // 配置实例关闭时释放预览图：置 null 由 setter 统一释放旧实例，避免重复 Dispose。
            // 输出口（OutputImage 等）的非托管值交给基类统一回收 —— 这里不再手动挑着释放，
            // 否则以后新增的图像类输出口会漏掉这一份
            PreviewImage = null;

            base.Dispose();
        }

        #endregion
    }
}
