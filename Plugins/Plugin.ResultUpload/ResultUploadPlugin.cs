using Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace Plugin.ResultUpload
{
    /// <summary>
    /// 结果上报插件：流程每测完一件，把「条码 + 结果 + 检测值」组一条 JSON 发出去。
    /// MES 上传、钉钉/企业微信群通知、自定义协议——底层都是"往一个 URL POST JSON"，
    /// 用报文预设（PayloadKind）一套界面覆盖，一个学习成本。
    ///
    /// 节拍纪律：流程线程只做"取值 + 组 JSON + 入队"（微秒级，不碰网络），
    /// HTTP + 重试退避全部交给插件内静态 <see cref="UploadQueue"/> 的后台线程（生产者-消费者）；
    /// MES 挂了、网线拔了都拖不垮检测。需要拿服务器响应给下游用时才勾"同步模式"。
    ///
    /// 与框架对齐的设计要点：
    /// - 字段表（Fields）里 来源=端口 的字段 → 动态输入端口（ApplyConfigValues 里先 base 再重建，赶在 LinkPorts 之前）；
    /// - 动态端口一律 IsRequired=false：允许"先定义、后连线"，未连线走变量/内置或空值；
    /// - 值类型用 InputPort&lt;object&gt;：FlowCompiler 连线不做类型静态校验，任何上游输出都接得进来，
    ///   组 JSON 时按真实类型保真（double 就写 12.3 不是 "12.3"——MES 数值型字段校验靠它）；
    /// - 失败契约：默认不阻断（上报失败只 WARN，宁漏报不停线）；需要"MES 没收到就停线"的场合勾选阻断。
    /// </summary>
    [Display(
        Name = "结果上报",
        GroupName = "数据处理",
        Description = "把检测结果 POST 成 JSON 上报：MES 字段表 / 钉钉·企业微信机器人 / 自定义报文，异步队列不占节拍",
        ShortName = "\uf093")]
    public class ResultUploadPlugin : VisionPluginBase, IPluginCustomViewProvider
    {
        #region 端口

        /// <summary>上报触发（为 true 时上报；仅"触发端口"时机生效。非必填：不连线不算参数缺失）。</summary>
        public InputPort<bool> TriggerPort { get; } =
            new InputPort<bool>("上报触发", false, "为 true 时上报（上报时机=触发端口 时生效）") { IsRequired = false };

        /// <summary>服务器返回体（失败时为错误描述；异步模式下是"最近一次"的结果）。</summary>
        public OutputPort<string> Response { get; } = new OutputPort<string>("响应", "服务器返回的响应体（失败时为错误描述）");

        /// <summary>HTTP 状态码（超时/网络错误为 0）。</summary>
        public OutputPort<int> StatusCode { get; } = new OutputPort<int>("状态码", "HTTP 状态码（超时/网络错误为 0）");

        /// <summary>最近一次上报是否成功（HTTP 2xx 且命中成功关键字）。</summary>
        public OutputPort<bool> IsUploaded { get; } = new OutputPort<bool>("已上报", "最近一次上报是否成功（2xx 且命中成功关键字）");

        #endregion

        public ResultUploadPlugin()
        {
            // 出厂默认三字段：条码/结果走端口（开箱即有一单最小可用的 MES 上报），时间内置自动供
            Fields.Add(new UploadFieldDef { Name = "sn", Source = FieldSource.Port });
            Fields.Add(new UploadFieldDef { Name = "result", Source = FieldSource.Port });
            Fields.Add(new UploadFieldDef { Name = "time", Source = FieldSource.BuiltIn, BuiltIn = BuiltInField.Timestamp });
            HookFields(Fields);
        }

        #region 内嵌配置（随 .vms 持久化）

        private PayloadKind _payloadKind = PayloadKind.FieldTable;
        /// <summary>报文预设（MES 字段表 / 钉钉文本 / 企业微信文本 / 原始 JSON）。</summary>
        [StepConfig]
        public PayloadKind PayloadKind
        {
            get => _payloadKind;
            set
            {
                if (SetProperty(ref _payloadKind, value))
                {
                    OnPropertyChanged(nameof(PayloadPreview));
                    OnPropertyChanged(nameof(IsFieldTable));
                    OnPropertyChanged(nameof(IsNotify));
                    OnPropertyChanged(nameof(IsRawJson));
                }
            }
        }

        private string _url = "";
        /// <summary>目标 URL（MES 接口或钉钉/企微 webhook 地址）。</summary>
        [StepConfig]
        public string Url
        {
            get => _url;
            set => SetProperty(ref _url, value ?? "");
        }

        private int _timeoutMs = 3000;
        /// <summary>单次请求超时（毫秒）。</summary>
        [StepConfig]
        public int TimeoutMs
        {
            get => _timeoutMs;
            set => SetProperty(ref _timeoutMs, value <= 0 ? 3000 : value);
        }

        private ObservableCollection<HttpHeaderDef> _headers = new ObservableCollection<HttpHeaderDef>();
        /// <summary>自定义请求头（MES 常要 Authorization；钉钉/企微 webhook 一般不用填）。</summary>
        [StepConfig]
        public ObservableCollection<HttpHeaderDef> Headers
        {
            get => _headers;
            set => SetProperty(ref _headers, value ?? new ObservableCollection<HttpHeaderDef>());
        }

        private ObservableCollection<UploadFieldDef> _fields = new ObservableCollection<UploadFieldDef>();
        /// <summary>字段表（MES 模式=JSON 载荷本身；通知/原始JSON模式=模板占位符的值池。来源=端口的字段同时是动态输入端口）。</summary>
        [StepConfig]
        public ObservableCollection<UploadFieldDef> Fields
        {
            get => _fields;
            set
            {
                UnhookFields(_fields);
                _fields = value ?? new ObservableCollection<UploadFieldDef>();
                HookFields(_fields);
                OnPropertyChanged();
                RebuildDynamicInputs();
                OnPropertyChanged(nameof(JudgeFieldNames));
            }
        }

        private string _messageTemplate = "件{sn}判定{result}";
        /// <summary>通知内容模板（钉钉/企微的 text.content；占位符：{字段名} {时间} {日期} {步骤名}）。</summary>
        [StepConfig]
        public string MessageTemplate
        {
            get => _messageTemplate;
            set
            {
                if (SetProperty(ref _messageTemplate, value ?? ""))
                    OnPropertyChanged(nameof(PayloadPreview));
            }
        }

        private string _rawJsonTemplate = "{\n  \"sn\": \"{sn}\",\n  \"result\": \"{result}\"\n}";
        /// <summary>原始 JSON 模板（{名}=转义字符串注入；{#名}=原样注入（数字/布尔/JSON片段不带引号））。</summary>
        [StepConfig]
        public string RawJsonTemplate
        {
            get => _rawJsonTemplate;
            set
            {
                if (SetProperty(ref _rawJsonTemplate, value ?? ""))
                    OnPropertyChanged(nameof(PayloadPreview));
            }
        }

        private SendTiming _sendTiming = SendTiming.EachRun;
        /// <summary>上报时机（每次执行 / 仅 NG / 触发端口）。</summary>
        [StepConfig]
        public SendTiming SendTiming
        {
            get => _sendTiming;
            set => SetProperty(ref _sendTiming, value);
        }

        private string _judgeFieldName = "result";
        /// <summary>判定字段名（"仅 NG"时机看它的值是否等于 NG 判定值）。</summary>
        [StepConfig]
        public string JudgeFieldName
        {
            get => _judgeFieldName;
            set => SetProperty(ref _judgeFieldName, value ?? "");
        }

        private string _ngValue = "NG";
        /// <summary>NG 判定值（判定字段的值等于它即视为 NG，不区分大小写）。</summary>
        [StepConfig]
        public string NgValue
        {
            get => _ngValue;
            set => SetProperty(ref _ngValue, value ?? "NG");
        }

        private bool _asyncMode = true;
        /// <summary>异步队列（推荐，不占节拍）；取消勾选=同步等响应（Response/状态码下游可用，超时会拖节拍）。</summary>
        [StepConfig]
        public bool AsyncMode
        {
            get => _asyncMode;
            set => SetProperty(ref _asyncMode, value);
        }

        private int _retryCount = 3;
        /// <summary>失败重试次数（退避 0.5s/1s/1.5s… 封顶 3s）。</summary>
        [StepConfig]
        public int RetryCount
        {
            get => _retryCount;
            set => SetProperty(ref _retryCount, Math.Clamp(value, 0, 10));
        }

        private bool _blockOnFailure = false;
        /// <summary>上报失败是否让本步骤失败阻断流程（默认 false：宁漏报不停线）。</summary>
        [StepConfig]
        public bool BlockOnFailure
        {
            get => _blockOnFailure;
            set => SetProperty(ref _blockOnFailure, value);
        }

        private string _successKeyword = "";
        /// <summary>业务成功关键字（非空时：HTTP 2xx 且响应体包含它才算业务成功；如钉钉 {"errcode":0} 里的 errcode）。</summary>
        [StepConfig]
        public string SuccessKeyword
        {
            get => _successKeyword;
            set => SetProperty(ref _successKeyword, value ?? "");
        }

        #endregion

        #region 配置生命周期 / 视图

        public object GetConfigView(IStepConfigData stepData)
        {
            Initialize(stepData);
            return new ResultUploadView { DataContext = this };
        }

        public override void ApplyConfigValues(IStepConfigData stepData)
        {
            // 先让基类灌入 [StepConfig]（含 Fields），再按字段定义重建动态端口（编译期必须先于 LinkPorts）
            base.ApplyConfigValues(stepData);
            RebuildDynamicInputs();
        }

        /// <summary>
        /// 本步骤的有效名字（用于 {步骤名} 占位符）：
        /// 运行期用引擎注入的 InstanceName；配置期实例由界面新建、引擎还没命名（仍是"未赋值"），
        /// 退回步骤自身名字让预览可用（与 CSV 记录/Excel 报表插件同一套规则）。
        /// </summary>
        public string EffectiveStepName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(InstanceName) && InstanceName != "未赋值") return InstanceName;
                return StepData?.StepName ?? "";
            }
        }

        /// <summary>报文预设枚举值（供视图下拉 ItemsSource）。</summary>
        public Array PayloadKinds => Enum.GetValues(typeof(PayloadKind));

        /// <summary>上报时机枚举值（供视图下拉 ItemsSource）。</summary>
        public Array SendTimings => Enum.GetValues(typeof(SendTiming));

        /// <summary>字段来源枚举值（供视图行内下拉 ItemsSource）。</summary>
        public Array FieldSources => Enum.GetValues(typeof(FieldSource));

        /// <summary>内置字段枚举值（供视图行内下拉 ItemsSource）。</summary>
        public Array BuiltInFields => Enum.GetValues(typeof(BuiltInField));

        /// <summary>预设切换的显示开关（供视图切换内容区）。</summary>
        public bool IsFieldTable => PayloadKind == PayloadKind.FieldTable;
        public bool IsNotify => PayloadKind == PayloadKind.DingTalkText || PayloadKind == PayloadKind.WeComText;
        public bool IsRawJson => PayloadKind == PayloadKind.RawJson;

        /// <summary>判定字段候选（= 启用字段的名单；供"仅 NG"判定下拉，可手填不强制选）。</summary>
        public IEnumerable<string> JudgeFieldNames =>
            Fields?.Where(f => f != null && f.Enabled && !string.IsNullOrWhiteSpace(f.Name))
                   .Select(f => f.Name.Trim())
                   .ToList() ?? Enumerable.Empty<string>();

        /// <summary>报文预览（配置面板实时看 JSON；端口/变量此刻没值就显示"(空)"占位，一眼看出没连上）。</summary>
        public string PayloadPreview
        {
            get
            {
                var payload = HttpPayloadBuilder.Build(
                    PayloadKind, Fields?.ToList(), CollectValues(null, DateTime.Now, useTestPlaceholder: true),
                    MessageTemplate, RawJsonTemplate, DateTime.Now, EffectiveStepName);
                return payload.Error != null ? "（无法预览：" + payload.Error + "）" : payload.Json;
            }
        }

        /// <summary>界面加载/确认后刷新预览（InstanceName 由引擎注入，构造时还拿不到）。</summary>
        public void RefreshPreview()
        {
            OnPropertyChanged(nameof(EffectiveStepName));
            OnPropertyChanged(nameof(PayloadPreview));
        }

        #endregion

        #region 动态端口重建

        private readonly List<string> _dynamicInputNames = new List<string>();

        /// <summary>按字段定义（来源=端口 且 启用）重建动态输入端口。</summary>
        public void RebuildDynamicInputs()
        {
            foreach (var name in _dynamicInputNames)
                RemoveDynamicInput(name);
            _dynamicInputNames.Clear();

            if (Fields != null)
            {
                foreach (var f in Fields)
                {
                    if (f == null || !f.Enabled) continue;
                    if (f.Source != FieldSource.Port) continue;
                    if (string.IsNullOrWhiteSpace(f.Name)) continue;
                    var portName = f.Name.Trim();
                    if (_dynamicInputNames.Contains(portName)) continue; // 重名字段只建一个端口
                    // object 端口 + 非必填：任意上游可连、未连线不报[参数缺失]
                    AddDynamicInput(new InputPort<object>(portName, null, portName) { IsRequired = false });
                    _dynamicInputNames.Add(portName);
                }
            }
        }

        private void HookFields(ObservableCollection<UploadFieldDef> collection)
        {
            if (collection == null) return;
            collection.CollectionChanged += OnFieldsCollectionChanged;
            foreach (var f in collection) HookField(f);
        }

        private void UnhookFields(ObservableCollection<UploadFieldDef> collection)
        {
            if (collection == null) return;
            collection.CollectionChanged -= OnFieldsCollectionChanged;
            foreach (var f in collection) UnhookField(f);
        }

        private void HookField(UploadFieldDef f)
        {
            if (f == null) return;
            f.PropertyChanged -= OnFieldDefPropertyChanged;
            f.PropertyChanged += OnFieldDefPropertyChanged;
        }

        private void UnhookField(UploadFieldDef f)
        {
            if (f != null) f.PropertyChanged -= OnFieldDefPropertyChanged;
        }

        // 字段名/来源/启用 都会改变动态端口集合 → 重建
        private void OnFieldDefPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(UploadFieldDef.Name) ||
                e.PropertyName == nameof(UploadFieldDef.Source) ||
                e.PropertyName == nameof(UploadFieldDef.Enabled))
            {
                RebuildDynamicInputs();
                OnPropertyChanged(nameof(JudgeFieldNames));
            }
        }

        private void OnFieldsCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
                foreach (UploadFieldDef f in e.OldItems) UnhookField(f);
            if (e.NewItems != null)
                foreach (UploadFieldDef f in e.NewItems) HookField(f);
            RebuildDynamicInputs();
            OnPropertyChanged(nameof(JudgeFieldNames));
        }

        #endregion

        #region 视图辅助

        /// <summary>新增一个字段（自动起不重名的默认字段名，供视图"添加字段"按钮）。</summary>
        public UploadFieldDef AddField()
        {
            string name = "field";
            int i = 1;
            while (Fields.Any(f => f != null && string.Equals(f.Name?.Trim(), name, StringComparison.OrdinalIgnoreCase)))
                name = "field" + (++i);
            var f = new UploadFieldDef { Name = name, Source = FieldSource.Port };
            Fields.Add(f);
            OnPropertyChanged(nameof(PayloadPreview));
            return f;
        }

        /// <summary>新增一个请求头（供视图"添加请求头"按钮）。</summary>
        public void AddHeader() => Headers.Add(new HttpHeaderDef { Key = "", Value = "" });

        private bool _isTesting;
        /// <summary>是否正在测试发送（置灰按钮）。</summary>
        public bool IsTesting
        {
            get => _isTesting;
            private set => SetProperty(ref _isTesting, value);
        }

        private string _lastResult = "";
        /// <summary>最近一次测试发送/运行上报的结果文本（供视图显示）。</summary>
        public string LastResult
        {
            get => _lastResult;
            private set => SetProperty(ref _lastResult, value);
        }

        /// <summary>
        /// 测试发送（配置面板按钮）：按当前配置真发一条，让用户当场验证 URL / 报文 / 鉴权。
        /// 端口/变量此刻没值 → 显示"(空)"占位，JSON 结构照样真实。后台线程跑，不卡界面。
        /// </summary>
        public void TestSend()
        {
            if (IsTesting) return;
            if (string.IsNullOrWhiteSpace(Url?.Trim()))
            {
                LastResult = "✘ 请先填目标 URL";
                return;
            }
            IsTesting = true;
            LastResult = "正在发送…";
            Task.Run(() =>
            {
                try
                {
                    var now = DateTime.Now;
                    var payload = HttpPayloadBuilder.Build(
                        PayloadKind, Fields?.ToList(), CollectValues(null, now, useTestPlaceholder: true),
                        MessageTemplate, RawJsonTemplate, now, EffectiveStepName);
                    if (payload.Error != null)
                    {
                        LastResult = "✘ " + payload.Error;
                        return;
                    }
                    var job = BuildJob(payload.Json);
                    var (ok, code, resp) = UploadQueue.SendNow(job);
                    bool bizOk = ok && KeywordHit(resp);
                    string summary = bizOk
                        ? $"✔ HTTP {code}（业务成功）"
                        : ok ? $"✘ HTTP {code}，但响应未命中成功关键字『{SuccessKeyword}』"
                             : $"✘ HTTP {code} {resp}";
                    LastResult = summary + "\n→ " + Shorten(payload.Json) + (string.IsNullOrEmpty(resp) ? "" : "\n← " + Shorten(resp));
                    ApplyPorts(bizOk, code, resp);
                }
                catch (Exception ex)
                {
                    LastResult = "✘ 测试异常：" + ex.Message;
                }
                finally
                {
                    IsTesting = false;
                }
            });
        }

        #endregion

        #region 执行

        public override void RunAlgorithm(IExecutionContext context)
        {
            // —— Hub 暂存日志投递（静态 Hub 没有 context，随每次执行转交宿主日志）——
            if (context?.Logger != null)
            {
                foreach (var log in UploadQueue.TakeLogs())
                {
                    switch (log.Level)
                    {
                        case "ERROR": context.Logger.Error(log.Message); break;
                        case "WARN": context.Logger.Warn(log.Message); break;
                        default: context.Logger.Info(log.Message); break;
                    }
                }
            }

            var now = DateTime.Now;
            var values = CollectValues(context, now, useTestPlaceholder: false);

            // —— 上报时机判定（仅 NG 需要值池，故先组值池再判时机）——
            bool shouldSend;
            switch (SendTiming)
            {
                case SendTiming.EachRun: shouldSend = true; break;
                case SendTiming.OnTrigger:
                    shouldSend = TriggerPort.GetActualValue() is bool b && b;
                    break;
                case SendTiming.OnlyNG: shouldSend = IsNg(values); break;
                default: shouldSend = false; break;
            }
            if (!shouldSend)
                return; // 静默通过：不上报也不写日志（高频流程里刷日志会淹没真正的问题）

            if (string.IsNullOrWhiteSpace(Url?.Trim()))
            {
                if (BlockOnFailure)
                {
                    Success.Value = false;
                    ErrorMessage.Value = "结果上报：未配置目标 URL";
                }
                else
                {
                    context?.Logger?.Warn("结果上报：未配置目标 URL，跳过本次上报");
                }
                return;
            }

            // —— 组报文（纯内存；失败按契约处置）——
            var payload = HttpPayloadBuilder.Build(
                PayloadKind, Fields?.ToList(), values, MessageTemplate, RawJsonTemplate, now, EffectiveStepName);
            if (payload.Error != null)
            {
                if (BlockOnFailure)
                {
                    Success.Value = false;
                    ErrorMessage.Value = "结果上报：" + payload.Error;
                }
                else
                {
                    context?.Logger?.Warn("结果上报：" + payload.Error);
                }
                return;
            }

            if (AsyncMode)
            {
                // —— 异步：入队即返回（微秒级）；结果经回调回写端口，失败由 Hub 记日志 ——
                var job = BuildJob(payload.Json);
                job.OnCompleted = (ok, code, resp) =>
                {
                    bool bizOk = ok && KeywordHit(resp);
                    ApplyPorts(bizOk, code, resp);
                    LastResult = bizOk ? $"✔ HTTP {code}" : $"✘ HTTP {code} {resp ?? ""}".Trim();
                };
                var enq = UploadQueue.Enqueue(job);
                if (enq != EnqueueResult.Accepted && BlockOnFailure)
                {
                    Success.Value = false;
                    ErrorMessage.Value = enq == EnqueueResult.QueueFull
                        ? "结果上报：发送队列已满，上报被拒（检查服务器是否长期不通）"
                        : "结果上报：发送队列正在关停，不再接收";
                    return;
                }
            }
            else
            {
                // —— 同步：等响应（阻塞流程线程，超时会拖节拍——配置面板已明示）——
                var (ok, code, resp) = UploadQueue.SendNow(BuildJob(payload.Json));
                bool bizOk = ok && KeywordHit(resp);
                ApplyPorts(bizOk, code, resp);
                if (!bizOk && BlockOnFailure)
                {
                    Success.Value = false;
                    ErrorMessage.Value = "结果上报：" + (string.IsNullOrEmpty(resp) ? "未知错误" : resp);
                    return;
                }
                if (!bizOk)
                    context?.Logger?.Warn("结果上报：" + resp);
            }

            Success.Value = true;
            ErrorMessage.Value = "";
        }

        #endregion

        #region 私有：取值 / 判定 / 组任务

        /// <summary>组值池：字段名 → 原始值（端口/变量/内置；键大小写不敏感，占位符写得随意点也不炸）。</summary>
        private Dictionary<string, object> CollectValues(IExecutionContext context, DateTime now, bool useTestPlaceholder)
        {
            var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            if (Fields == null) return dict;
            foreach (var f in Fields)
            {
                if (f == null || !f.Enabled || string.IsNullOrWhiteSpace(f.Name)) continue;
                object v;
                switch (f.Source)
                {
                    case FieldSource.Port:
                        v = Inputs.TryGetValue(f.Name.Trim(), out var p) ? p.GetActualValue() : null;
                        break;
                    case FieldSource.Variable:
                        if (context?.LocalVariables != null && !string.IsNullOrEmpty(f.VariableName) &&
                            context.LocalVariables.TryGetValue(f.VariableName, out var lv))
                            v = lv;
                        else
                            v = null;
                        break;
                    case FieldSource.BuiltIn:
                        // 步骤名用 EffectiveStepName（InstanceName 未注入时退回 StepData.StepName），防上报出去是"未赋值"
                        v = f.BuiltIn == BuiltInField.Timestamp ? (object)now : (object)EffectiveStepName;
                        break;
                    default:
                        v = null;
                        break;
                }
                if (useTestPlaceholder && v == null) v = "(空)";
                dict[f.Name.Trim()] = v;
            }
            return dict;
        }

        /// <summary>本件是否 NG：判定字段的值（字符串化后）等于 NG 判定值（不区分大小写）。</summary>
        private bool IsNg(Dictionary<string, object> values)
        {
            if (string.IsNullOrWhiteSpace(JudgeFieldName) || values == null) return false;
            if (!values.TryGetValue(JudgeFieldName.Trim(), out var v) || v == null) return false;
            return string.Equals(ToPlainString(v), NgValue?.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>任意值字符串化（与 HttpPayloadBuilder 的 ToText 同口径：数值文本就是 ToString，比较宽松够用）。</summary>
        private static string ToPlainString(object v) => v is string s ? s : (v?.ToString() ?? "");

        /// <summary>组上报任务（URL/超时/请求头/重试已在配置里）。</summary>
        private UploadJob BuildJob(string json)
        {
            return new UploadJob
            {
                OwnerName = InstanceName,
                Url = Url?.Trim(),
                TimeoutMs = TimeoutMs,
                Headers = BuildHeaders(),
                Json = json,
                RetryCount = RetryCount
            };
        }

        /// <summary>请求头集合 → 字典（Key 空白/重复跳过，首个生效）。</summary>
        private Dictionary<string, string> BuildHeaders()
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (Headers == null) return d;
            foreach (var h in Headers)
            {
                if (h == null || string.IsNullOrWhiteSpace(h.Key)) continue;
                var key = h.Key.Trim();
                if (!d.ContainsKey(key)) d[key] = h.Value ?? "";
            }
            return d;
        }

        /// <summary>业务成功判定：HTTP 2xx 之外再要求响应体命中成功关键字（未配置关键字则 2xx 即成功）。</summary>
        private bool KeywordHit(string response)
        {
            if (string.IsNullOrWhiteSpace(SuccessKeyword)) return true;
            return !string.IsNullOrEmpty(response) &&
                   response.IndexOf(SuccessKeyword.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>回写输出端口（异步模式在队列线程调、同步/测试在当前线程调；标量属性引擎会调度回 UI 线程）。</summary>
        private void ApplyPorts(bool bizOk, int code, string response)
        {
            Response.Value = response ?? "";
            StatusCode.Value = code;
            IsUploaded.Value = bizOk;
        }

        /// <summary>长文本截断（结果区展示用，防一条大响应把面板撑爆）。</summary>
        private static string Shorten(string s, int max = 300)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\n", " ").Replace("\r", "");
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }

        #endregion
    }
}
