using Core.Commands;
using Core.Events;
using Core.Halcon;
using Core.Halcon.Models;
using Core.Interfaces;
using HalconDotNet;
using Microsoft.Win32;
using Plugin.PreProcessing.Models;
using Plugin.PreProcessing.Operators;
using Plugin.PreProcessing.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Input;
using System.Windows.Threading;

namespace Plugin.PreProcessing
{
    /// <summary>
    /// 图像预处理插件：把"一串算子"像流水线一样串起来，每一步都能单独看效果。
    ///
    /// 本类的职责边界（务必看清，否则容易改坏）：
    /// 1. 它同时扮演两个角色 ——
    ///    · 流程节点（正式运行/试运行）：只干 RunAlgorithm 这一件事，跑链、出图、给端口赋值；
    ///    · 配置界面的 ViewModel：承载算子链集合、选中项、逐步预览图、增删改命令。
    ///    两个角色共用一份代码没问题，因为主程序给"配置"和"编译执行"各造一个实例（见 CreateRoiPlugin 同样写法）；
    /// 2. 所有对外接入只走既有扩展点：VisionPluginBase + IPluginCustomViewProvider + [StepConfig]，
    ///    不改主程序、不改属性面板、不改 Core.Interfaces —— 这是本次迁移的硬约束。
    /// </summary>
    [Display(
        Name = "图像预处理",
        GroupName = "图像处理",
        Description = "串接色彩/几何/滤波/形态学/增强/二值化算子，支持逐步预览与单步启停",
        ShortName = "\uf085"
    )]
    public class PreProcessingPlugin : VisionPluginBase, IPluginCustomViewProvider
    {
        #region 存盘配置（[StepConfig] 由基类统一读写 InputValues）

        private int _displayViewIndex = 1;
        /// <summary>显示窗口：0 = 不显示，1~9 = 主界面视图号（与采集插件同一套语义）</summary>
        [StepConfig]
        public int DisplayViewIndex
        {
            get => _displayViewIndex;
            set => SetProperty(ref _displayViewIndex, value);
        }

        private List<OperatorStepItem> _chain = new();
        /// <summary>
        /// 算子链的存盘快照（弱类型：Key + 参数字典）。
        /// 界面上真正用的是 <see cref="Operators"/>，这里只是"落盘用的影子"，
        /// 两者靠 RebuildOperators()/WriteChain() 双向同步。
        /// </summary>
        [StepConfig]
        public List<OperatorStepItem> Chain
        {
            get => _chain;
            set { _chain = value ?? new(); OnPropertyChanged(); }
        }

        #endregion

        #region 端口

        /// <summary>输入图像（可链接上游，也可在界面里直接指定变量）</summary>
        public InputPort<HImage> SrcImage { get; } = new();

        /// <summary>预处理后的图像</summary>
        public OutputPort<HImage> Image { get; } = new("Image", "预处理后的图像");

        /// <summary>链上算子总数</summary>
        public OutputPort<int> StepCount { get; } = new("StepCount", "算子总步数");

        /// <summary>实际生效的算子步数（启用的个数）</summary>
        public OutputPort<int> ActiveCount { get; } = new("ActiveCount", "启用的算子个数");

        /// <summary>生效算子的名字串，便于日志与下游判断</summary>
        public OutputPort<string> ActiveOperators { get; } = new("ActiveOperators", "启用的算子（按执行顺序）");

        #endregion

        #region 视图模型：算子链

        /// <summary>界面上的活算子链（不贴 [StepConfig]：它带 HImage/事件，序列化进去只会出事）</summary>
        public ObservableCollection<PreprocessOperator> Operators { get; } = new();

        /// <summary>算子库（左栏菜单数据源），来自注册表反射，启动只扫一次</summary>
        public List<OperatorCategoryGroup> OperatorLibrary => OperatorRegistry.Categories;

        /// <summary>加载时被跳过的算子 Key（版本回退/手工改过方案文件），用于界面提示</summary>
        public string LoadWarning { get; private set; } = string.Empty;

        /// <summary>
        /// 操作反馈（复制/粘贴/导出）。
        /// 配置期的 ViewModel 拿不到 ILogService（全仓没有可取的入口），
        /// 所以这类"刚才那一下成没成"只能走界面文字，与 <see cref="LoadWarning"/> 同一套路。
        /// </summary>
        public string ActionHint { get; private set; } = string.Empty;

        private void SetHint(string text)
        {
            ActionHint = text;
            OnPropertyChanged(nameof(ActionHint));
        }

        private PreprocessOperator? _selectedOperator;

        /// <summary>
        /// 当前选中的算子（右侧参数面板的数据源）。
        /// 注意这里不订阅算子事件 —— 链上每个算子的 PropertyChanged 已由
        /// <see cref="OnOperatorsChanged"/> 统一挂接，重复订阅会让一次改参触发两遍回写。
        /// </summary>
        public PreprocessOperator? SelectedOperator
        {
            get => _selectedOperator;
            set
            {
                if (ReferenceEquals(_selectedOperator, value)) return;
                _selectedOperator = value;
                OnPropertyChanged();

                // 选中哪一步，预览就定位到那一步。带框算子例外：它的框是相对"那一步的输入图"定义的，
                // 停在输出图上会因为裁剪/缩放改了尺寸而画歪，所以选中即切到"输入图"这一态。
                int index = value != null ? Operators.IndexOf(value) : -1;
                var target = value switch
                {
                    BoxOperatorBase => PreviewTarget.Input,
                    null => PreviewTarget.Source,
                    _ => PreviewTarget.Output,
                };
                ApplyPreviewState(target, index, force: true);
            }
        }

        /// <summary>正在重建链（读盘回填期间屏蔽 CollectionChanged 回写，避免自激）</summary>
        private bool _rebuilding;

        #endregion

        #region 视图模型：逐步预览（仅配置界面用，UI 线程）

        private readonly Dictionary<int, HImage> _stepImages = new();
        private HImage? _sourceImage;

        /// <summary>链上各算子的输入原图（不持有所有权，来自 SrcImage.ActualValue）</summary>
        public HImage? SourceImage => _sourceImage;

        /// <summary>预览看哪一张：整张原图 / 选中那一步的输入图 / 选中那一步的输出图</summary>
        private enum PreviewTarget { Source, Input, Output }

        private PreviewTarget _previewTarget = PreviewTarget.Output;

        private int _previewStepIndex = -1;
        /// <summary>-1 = 看原图；0..n-1 = 看第 n 步</summary>
        public int PreviewStepIndex
        {
            get => _previewStepIndex;
            // 从外部按"步号"切预览时，语义沿用旧版：给了步号就是看那一步的输出，-1 就是原图
            set => ApplyPreviewState(value < 0 ? PreviewTarget.Source : PreviewTarget.Output, value);
        }

        /// <summary>
        /// 预览状态唯一写入口：改完状态一次性把"画面 + 三枚单选 + 画布框"全部刷齐。
        /// 三处状态分开通知最容易出的 bug 是"单选框双双落空"和"框还在但画面已经换了一张"。
        /// </summary>
        /// <param name="force">状态没变也要重刷画布：换选中算子时，框换了"主人"，参数得重新推一遍</param>
        private void ApplyPreviewState(PreviewTarget target, int stepIndex, bool force = false)
        {
            bool changed = force || _previewTarget != target || _previewStepIndex != stepIndex;
            _previewTarget = target;
            SetProperty(ref _previewStepIndex, stepIndex);
            if (!changed) return;
            NotifyPreview();
            RefreshCanvasBox();
        }

        /// <summary>回刷画面与三枚单选框的选中态</summary>
        private void NotifyPreview()
        {
            OnPropertyChanged(nameof(PreviewStepIndex));
            OnPropertyChanged(nameof(DisplayImage));
            OnPropertyChanged(nameof(ShowSourcePreview));
            OnPropertyChanged(nameof(ShowInputPreview));
            OnPropertyChanged(nameof(ShowOutputPreview));
        }

        /// <summary>看原图开关（视图 RadioButton 绑定）</summary>
        public bool ShowSourcePreview
        {
            get => _previewTarget == PreviewTarget.Source;
            set { if (value) ApplyPreviewState(PreviewTarget.Source, _previewStepIndex); }
        }

        /// <summary>
        /// 看"选中那一步的输入图"开关（视图 RadioButton 绑定）。
        /// 带框算子要在这一态下拖框 —— 框的坐标基准就是这张输入图。
        /// </summary>
        public bool ShowInputPreview
        {
            get => _previewTarget == PreviewTarget.Input;
            set
            {
                if (!value) return;
                int index = _previewStepIndex >= 0
                    ? _previewStepIndex
                    : Operators.Count > 0 ? 0 : -1;
                ApplyPreviewState(PreviewTarget.Input, index);
            }
        }

        /// <summary>
        /// 看算子输出开关（视图 RadioButton 绑定）。
        /// 单选组互斥带来的"取消勾选"一律忽略 —— 那一侧必然是另外两枚被勾选，由它负责回写。
        /// </summary>
        public bool ShowOutputPreview
        {
            get => _previewTarget == PreviewTarget.Output;
            set
            {
                if (!value || Operators.Count == 0) return;
                int index = _selectedOperator != null ? Operators.IndexOf(_selectedOperator) : -1;
                ApplyPreviewState(PreviewTarget.Output, index >= 0 ? index : Operators.Count - 1);
            }
        }

        /// <summary>ImageEdit 绑定源</summary>
        public HImage? DisplayImage
        {
            get
            {
                switch (_previewTarget)
                {
                    case PreviewTarget.Source: return _sourceImage;
                    case PreviewTarget.Input: return InputImage;
                    default:
                        return _stepImages.TryGetValue(_previewStepIndex, out var img) && img != null
                            ? img
                            : _sourceImage;   // 该步透传/未产出时退回原图，画面不会"空白"
                }
            }
        }

        /// <summary>
        /// 选中那一步的输入图（不持有所有权）。
        /// 往前找第一张"真正产出了图"的步骤：<see cref="_stepImages"/> 里故意不存透传结果，
        /// 所以第 i-1 步查不到时不能直接退回原图，得继续往前找，否则画面会跳回上游。
        /// </summary>
        private HImage? InputImage
        {
            get
            {
                for (int k = _previewStepIndex - 1; k >= 0; k--)
                {
                    if (_stepImages.TryGetValue(k, out var img) && img != null) return img;
                }
                return _sourceImage;
            }
        }

        /// <summary>参数框逐字符刷新，全图运算不防抖会明显卡顿（照抄 ROI 插件的做法）</summary>
        private readonly DispatcherTimer _previewDebounce = new() { Interval = TimeSpan.FromMilliseconds(200) };

        public PreProcessingPlugin()
        {
            Operators.CollectionChanged += OnOperatorsChanged;
            CanvasRois.CollectionChanged += OnCanvasRoisChanged;
            _previewDebounce.Tick += (_, _) => { _previewDebounce.Stop(); RefreshPreview(); };
        }

        /// <summary>视图就绪信号：把上游/变量里的图取来做预览底图</summary>
        public void OnViewLoaded()
        {
            _sourceImage = SrcImage.ActualValue;
            RefreshPreview();
        }

        private void SchedulePreview()
        {
            // 流程线程上跑算子时，参数纠偏也会发通知，一路调到这儿。
            // DispatcherTimer 只能在创建它的线程上启停，所以非 UI 线程先投递回去。
            var dispatcher = UiDispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(new Action(SchedulePreview));
                return;
            }
            _previewDebounce.Stop();
            _previewDebounce.Start();
        }

        /// <summary>UI 线程调度器；没有 Application（单元测试/离线跑流程）时为 null，此时按"就在 UI 线程"处理</summary>
        private static Dispatcher? UiDispatcher => System.Windows.Application.Current?.Dispatcher;

        /// <summary>
        /// 重跑整条链，把每一步的输出留一份给界面看。
        /// 全程在 UI 线程、同步执行 —— 因此不存在"图被流程线程释放掉"的竞态，
        /// 这也是为什么正式运行路径（RunAlgorithm）不往主界面逐步推图：
        /// PublishPreview 内部是 Dispatcher.BeginInvoke 异步投递，逐步推图得自己管住生命周期，得不偿失。
        /// </summary>
        public void RefreshPreview()
        {
            DisposeStepImages();
            _sourceImage = SrcImage.ActualValue;

            var src = _sourceImage;
            if (src == null || !src.IsInitialized() || Operators.Count == 0)
            {
                OnPropertyChanged(nameof(DisplayImage));
                return;
            }

            var results = new HImage?[Operators.Count];
            try
            {
                // 预览全程在 UI 线程，直接跑活链即可（快照只给流程线程用）
                ExecuteChain(src, Operators, results, logger: null);
            }
            catch
            {
                // 预览路径的异常不弹框：能显示多少步就显示多少步，剩下的步留空
            }

            for (int i = 0; i < results.Length; i++)
            {
                var img = results[i];
                // 与原图同一引用（透传）时不入字典：否则 DisposeStepImages 会把别人的图释放掉
                if (img != null && !ReferenceEquals(img, src)) _stepImages[i] = img;
            }

            OnPropertyChanged(nameof(DisplayImage));
        }

        private void DisposeStepImages()
        {
            foreach (var img in _stepImages.Values)
            {
                try { img?.Dispose(); } catch { /* 预览图释放失败不影响编辑 */ }
            }
            _stepImages.Clear();
        }

        #endregion

        #region 画布编辑框同步（纯 VM，宿主控件一行不改）

        /// <summary>画布上那个唯一编辑框的名字（链上算子再多，同一时刻只有选中那个有框）</summary>
        private const string BoxRoiName = "编辑框";

        /// <summary>
        /// 画布集合（ImageEdit 的 DrawObjectList 绑定源，VM 是唯一所有者）。
        /// 宿主控件负责集合变更时挂/摘句柄、并在 Remove/Reset 时 Dispose 条目，
        /// 所以这里只管"内容对不对"，不碰渲染。
        /// </summary>
        public ObservableCollection<DrawingObjectInfo> CanvasRois { get; } = new();

        private DrawingObjectInfo? _canvasActiveRoi;
        /// <summary>当前可拖拽编辑的对象（控件 ActiveRoi 双向绑定；画布点选也会回写这里）</summary>
        public DrawingObjectInfo? CanvasActiveRoi
        {
            get => _canvasActiveRoi;
            set => SetProperty(ref _canvasActiveRoi, value);
        }

        /// <summary>VM 正在往画布写参数：屏蔽一次 HTuples 回写，否则"写进去→又读回来"自激</summary>
        private bool _syncingCanvas;

        /// <summary>正在把画布拖拽结果写进算子参数：屏蔽一次"参数→画布"的回推，免得拖中途被 SetParams 拽住</summary>
        private bool _fromCanvas;

        /// <summary>
        /// 让画布上的框与"当前选中算子 + 当前预览态"对齐。
        ///
        /// 调用时机只有三个：选中项变了、链的结构变了、预览态或参数变了。
        /// 预览重算（RefreshPreview）里【故意】不调它 —— 跑一遍链不会改框参数，
        /// 而在用户手还按在框上时重建句柄，会把这次拖拽直接打断。
        /// </summary>
        private void RefreshCanvasBox()
        {
            // 只有界面线程能动这个集合（画布控件绑定着它）；流程线程上的参数纠偏不需要同步画布
            var dispatcher = UiDispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess()) return;

            var box = SelectedOperator as BoxOperatorBase;
            bool showBox = box != null && !box.IsBoxEmpty && IsBoxBaseDisplayed();

            var info = CanvasRois.FirstOrDefault(x => x.RoiName == BoxRoiName);
            if (!showBox)
            {
                if (info != null) CanvasRois.Remove(info);  // 框只是不显示，参数还留在算子身上
                return;
            }

            var tuples = box!.ToBoxTuples();
            _syncingCanvas = true;
            try
            {
                if (info == null)
                {
                    info = new DrawingObjectInfo(DrawShapeType.Rectangle, tuples, BoxRoiName);
                    CanvasRois.Add(info);
                }
                else
                {
                    info.HTuples = tuples;   // 复用同一实例：控件那边句柄不重建，改数值框不会掉焦点
                }
                CanvasActiveRoi = info;      // 交给控件 Attach 成可拖拽对象并高亮
            }
            finally { _syncingCanvas = false; }
        }

        /// <summary>框的坐标基准是"这一步的输入图"，只有画面对得上时才该显示框（第 0 步的输入图就是原图）</summary>
        private bool IsBoxBaseDisplayed() =>
            _previewTarget == PreviewTarget.Input
            || (_previewTarget == PreviewTarget.Source && _previewStepIndex <= 0);

        /// <summary>
        /// 画布集合变更：Add 挂拖拽回写、Remove/Reset 退订。
        /// 与 ROI 插件不同，这里不拿集合回写算子链 —— 链是数据源，画布只是它的一个"投影"。
        /// </summary>
        private void OnCanvasRoisChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add when e.NewItems != null:
                    foreach (DrawingObjectInfo info in e.NewItems)
                        info.PropertyChanged += OnCanvasRoiTuplesChanged;
                    break;

                case NotifyCollectionChangedAction.Remove when e.OldItems != null:
                    foreach (DrawingObjectInfo info in e.OldItems)
                    {
                        info.PropertyChanged -= OnCanvasRoiTuplesChanged;
                        ClearBoxParamIfUserRemoved(info.RoiName);
                    }
                    break;

                case NotifyCollectionChangedAction.Reset:
                    // 用户在画布右键"删除/清空"：不把框参数归零的话，下一次同步又会把框长回来
                    ClearBoxParamIfUserRemoved(BoxRoiName);
                    break;
            }
        }

        /// <summary>画布侧删除框 → 归零选中算子的框参数（置 0 即"未框选"，算子透传）</summary>
        private void ClearBoxParamIfUserRemoved(string? roiName)
        {
            if (_syncingCanvas || roiName != BoxRoiName) return;
            if (SelectedOperator is not BoxOperatorBase box || box.IsBoxEmpty) return;
            box.BoxWidth = 0;
            box.BoxHeight = 0;
        }

        /// <summary>
        /// 拖拽/缩放手柄回传：控件把新参数写进 info.HTuples（INPC）→ 换算回算子的中心行列与宽高。
        /// 属性 setter 会顺带把新参数写进存盘快照，所以拖完框直接保存方案就能带出去。
        /// </summary>
        private void OnCanvasRoiTuplesChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_syncingCanvas) return;                                   // 是 VM 自己写进去的
            if (e.PropertyName != nameof(DrawingObjectInfo.HTuples)) return;
            if (sender is not DrawingObjectInfo { HTuples: { } } info) return;
            if (SelectedOperator is not BoxOperatorBase box) return;

            _fromCanvas = true;
            try
            {
                box.ApplyBoxTuples(info.HTuples);
            }
            finally { _fromCanvas = false; }

            SchedulePreview();   // 拖完立刻重算，做到"松手见结果"
        }

        /// <summary>删掉当前编辑框（Del 键 / 按钮）。焦点在文本框时不抢键，Delete 是正常编辑键</summary>
        public ICommand DeleteBoxCommand => _deleteBoxCommand ??=
            new RelayCommand(
                _ =>
                {
                    if (SelectedOperator is not BoxOperatorBase box) return;
                    box.BoxWidth = 0;
                    box.BoxHeight = 0;
                },
                _ => SelectedOperator is BoxOperatorBase b && !b.IsBoxEmpty
                     && System.Windows.Input.Keyboard.FocusedElement is not System.Windows.Controls.TextBox);
        private ICommand? _deleteBoxCommand;

        #endregion

        #region 命令（算子库添加 / 链编辑）

        public ICommand AddOperatorCommand => _addOperatorCommand ??=
            new RelayCommand(p => { if (p is OperatorDescriptor d) AppendOperator(d); });
        private ICommand? _addOperatorCommand;

        public ICommand RemoveOperatorCommand => _removeOperatorCommand ??=
            new RelayCommand(_ => RemoveSelected(), _ => SelectedOperator != null);
        private ICommand? _removeOperatorCommand;

        public ICommand MoveUpCommand => _moveUpCommand ??=
            new RelayCommand(_ => MoveSelected(-1), _ => Operators.IndexOf(SelectedOperator!) > 0);
        private ICommand? _moveUpCommand;

        public ICommand MoveDownCommand => _moveDownCommand ??=
            new RelayCommand(_ => MoveSelected(1),
                _ => { int i = Operators.IndexOf(SelectedOperator!); return i >= 0 && i < Operators.Count - 1; });
        private ICommand? _moveDownCommand;

        public ICommand ClearChainCommand => _clearChainCommand ??=
            new RelayCommand(_ => Operators.Clear(), _ => Operators.Count > 0);
        private ICommand? _clearChainCommand;

        public ICommand RefreshPreviewCommand => _refreshPreviewCommand ??=
            new RelayCommand(_ => RefreshPreview());
        private ICommand? _refreshPreviewCommand;

        private void AppendOperator(OperatorDescriptor descriptor)
        {
            var op = descriptor.Factory();
            Operators.Add(op);
            SelectedOperator = op;   // 新加的算子立刻选中，右手边属性面板直接可改
        }

        private void RemoveSelected()
        {
            if (SelectedOperator is not PreprocessOperator op) return;
            int index = Operators.IndexOf(op);
            Operators.Remove(op);
            if (Operators.Count > 0)
                SelectedOperator = Operators[Math.Min(index, Operators.Count - 1)];
            else
                SelectedOperator = null;   // 删空了还不置空，画布上会继续挂着已删算子的框
        }

        private void MoveSelected(int delta)
        {
            if (SelectedOperator is not PreprocessOperator op) return;
            int from = Operators.IndexOf(op);
            int to = from + delta;
            if (from < 0 || to < 0 || to >= Operators.Count) return;
            Operators.Move(from, to);   // 列表原地移动，CollectionChanged 会带回写
        }

        #endregion

        #region 链 ⇄ 快照 同步

        private void OnOperatorsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            // 订阅/退订逐个算子的变更通知（参数一改就刷新摘要与预览）
            if (e.OldItems != null)
                foreach (PreprocessOperator op in e.OldItems) op.PropertyChanged -= OnOperatorPropertyChanged;
            if (e.NewItems != null)
                foreach (PreprocessOperator op in e.NewItems) op.PropertyChanged += OnOperatorPropertyChanged;

            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Move:
                case NotifyCollectionChangedAction.Add:
                case NotifyCollectionChangedAction.Remove:
                case NotifyCollectionChangedAction.Reset:
                    WriteChain();
                    SchedulePreview();
                    break;
            }
            // 增删之后画布上该显示谁的框可能变了（被删的那个也许正带着框）
            if (e.Action != NotifyCollectionChangedAction.Move) RefreshCanvasBox();
            // 上移/下移按钮的可用态跟着列表走（RelayCommand 挂在 CommandManager 上，通常会自动重查，这里兜底）
            CommandManager.InvalidateRequerySuggested();
        }

        private void OnOperatorPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // 只认"启用"与"参数"两类变化；Summary 是派生显示属性，不用回写
            if (e.PropertyName == nameof(PreprocessOperator.Summary)) return;
            WriteChain();
            // 数值框里手改框参数 → 推到画布。拖拽回来的那一路不推（值本来就是画布给的，
            // 拖的过程中再 SetParams 一次会把正在跟手的手柄拽住）
            if (!_fromCanvas && ReferenceEquals(sender, SelectedOperator)) RefreshCanvasBox();
            SchedulePreview();
        }

        /// <summary>活链 → 存盘快照（OnConfirm 前、链或参数变更后都会调）</summary>
        public void WriteChain()
        {
            if (_rebuilding) return;
            Chain = SnapshotOperators();
        }

        /// <summary>
        /// 只做"活链 → 快照"这一步，不碰 <see cref="Chain"/>。
        /// 复制整链、导出清单要的正是这份数据，但不该顺手改掉存盘配置，所以拆成两半。
        /// </summary>
        private List<OperatorStepItem> SnapshotOperators()
        {
            var items = new List<OperatorStepItem>(Operators.Count);
            foreach (var op in Operators)
            {
                items.Add(new OperatorStepItem
                {
                    OperatorKey = op.Key,
                    Enabled = op.Enabled,
                    Params = op.SaveParams(),
                });
            }
            return items;
        }

        /// <summary>存盘快照 → 活链（读配置时调用；认不出的 Key 直接跳过并在界面留提示）</summary>
        private void RebuildOperators()
        {
            _rebuilding = true;
            var unknown = new List<string>();
            try
            {
                foreach (var op in Operators) op.PropertyChanged -= OnOperatorPropertyChanged;
                Operators.Clear();
                AppendStepsCore(Chain, unknown);
            }
            finally { _rebuilding = false; }

            LoadWarning = unknown.Count == 0
                ? string.Empty
                : $"有 {unknown.Count} 个算子在当前版本中不存在，已跳过：{string.Join("、", unknown.Distinct())}";
            OnPropertyChanged(nameof(LoadWarning));

            SelectedOperator = Operators.FirstOrDefault();   // 空链时 setter 会把预览退回"原图"
            CommandManager.InvalidateRequerySuggested();
        }

        /// <summary>
        /// 快照 → 活链的唯一内核：按 Key 造算子、灌参数、追加到链尾。
        /// 读盘重建与"粘贴"共用它，两者差别只在前面有没有先清链。
        /// </summary>
        /// <remarks>
        /// 调用方负责用 <see cref="_rebuilding"/> 罩住本方法（否则每 Add 一条就回写一遍快照、重算一遍预览），
        /// 并在放开之后统一补一次 WriteChain/SchedulePreview。
        /// </remarks>
        /// <param name="items">要追加的快照条目</param>
        /// <param name="unknown">收集认不出的 Key（注册表里没有），由调用方决定怎么提示</param>
        /// <returns>真正追加进链的算子（顺序与入参一致）</returns>
        private List<PreprocessOperator> AppendStepsCore(IEnumerable<OperatorStepItem> items, List<string> unknown)
        {
            var added = new List<PreprocessOperator>();
            foreach (var item in items)
            {
                if (item == null || string.IsNullOrEmpty(item.OperatorKey)) continue;
                var op = OperatorRegistry.Create(item.OperatorKey);
                if (op == null) { unknown.Add(item.OperatorKey); continue; }

                op.LoadParams(item.Params);
                op.Enabled = item.Enabled;   // 放后面：LoadParams 里会整体刷新一次通知
                Operators.Add(op);
                added.Add(op);
            }
            return added;
        }

        #endregion

        #region 链搬运与出图（复制 / 粘贴 / 导出）

        /// <summary>
        /// 剪贴板与导出文件里的链格式。
        /// 用 <see cref="OperatorStepItem"/> 而不是算子对象：弱类型、不含 CLR 类型名，
        /// 换版本、改类名都不影响已经复制出去的文本（与 [StepConfig] 落盘同一套哲学）。
        /// </summary>
        private static readonly JsonSerializerOptions ChainJson = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,   // 允许现场手工改这段 JSON
        };

        public ICommand CopyChainCommand => _copyChainCommand ??=
            new RelayCommand(_ => CopyChain(), _ => Operators.Count > 0);
        private ICommand? _copyChainCommand;

        /// <summary>粘贴命令恒可用：能不能粘要读了剪贴板才知道，而 CanExecute 会被高频重查，不适合去碰剪贴板</summary>
        public ICommand PasteChainCommand => _pasteChainCommand ??=
            new RelayCommand(_ => PasteChain());
        private ICommand? _pasteChainCommand;

        public ICommand ExportChainCommand => _exportChainCommand ??=
            new RelayCommand(_ => ExportChain(), _ => Operators.Count > 0);
        private ICommand? _exportChainCommand;

        /// <summary>把当前链拷成文本进剪贴板（同方案内复用、跨方案搬运都够用）</summary>
        private void CopyChain()
        {
            var snapshot = SnapshotOperators();
            string json = JsonSerializer.Serialize(snapshot, ChainJson);
            if (!TrySetClipboardText(json))
            {
                SetHint("复制失败：剪贴板被其它程序占用，关掉剪贴板工具后重试");
                return;
            }
            SetHint($"已复制 {snapshot.Count} 步到剪贴板（文本格式，可粘到其它方案的本节点）");
        }

        /// <summary>把剪贴板里的链追加到当前链尾（不清空原有步骤 —— 要清空先按"清空"）</summary>
        private void PasteChain()
        {
            string text;
            try
            {
                if (!System.Windows.Clipboard.ContainsText())
                {
                    SetHint("剪贴板里没有文本，先用「复制整链」");
                    return;
                }
                text = System.Windows.Clipboard.GetText();
            }
            catch (Exception ex)
            {
                SetHint($"读取剪贴板失败：{ex.Message}");
                return;
            }

            List<OperatorStepItem>? items = null;
            try { items = JsonSerializer.Deserialize<List<OperatorStepItem>>(text, ChainJson); }
            catch (JsonException) { /* 不是本插件的链文本，下面统一提示 */ }

            if (items == null || items.Count == 0)
            {
                SetHint("剪贴板里的文本不是处理链，需由本插件的「复制整链」得到");
                return;
            }

            var unknown = new List<string>();
            List<PreprocessOperator> pasted;
            _rebuilding = true;   // 一次粘 N 条：屏蔽逐条回写与逐条重算
            try { pasted = AppendStepsCore(items, unknown); }
            finally { _rebuilding = false; }

            if (pasted.Count == 0)
            {
                SetHint($"剪贴板里的 {items.Count} 步在当前版本中都不存在：{string.Join("、", unknown.Distinct())}");
                return;
            }

            WriteChain();
            SchedulePreview();
            SelectedOperator = pasted[0];   // 粘完停在第一条新算子上，参数面板直接可改
            SetHint(unknown.Count == 0
                ? $"已粘贴 {pasted.Count} 步到链尾"
                : $"已粘贴 {pasted.Count} 步，跳过 {unknown.Count} 步不存在的算子：{string.Join("、", unknown.Distinct())}");
        }

        /// <summary>
        /// WPF 写剪贴板偶尔抛 CLIPBRD_E_CANT_OPEN（别的进程正开着剪贴板，典型是剪贴板管理器和远程桌面）。
        /// 这是瞬态故障，重试几次比弹异常框有用。
        /// </summary>
        private static bool TrySetClipboardText(string text)
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    System.Windows.Clipboard.SetText(text);
                    return true;
                }
                catch (COMException) { System.Threading.Thread.Sleep(40); }
                catch (Exception) { return false; }
            }
            return false;
        }

        /// <summary>
        /// 导出整条链：每一步的输出图按"步号_算子名"写成 PNG，外加一份参数清单(txt)和链快照(json)。
        ///
        /// 为什么不做"另存当前预览图"？画布用的 ImageEdit 右键菜单里本来就有
        /// 「保存原始图像 / 保存缩略图像」，重复造没意义。真正的缺口是一次调完参数后，
        /// 想把"每一步长什么样"整份拿走去复盘、去给算法同学看 —— 那必须按步命名、一把导出。
        /// </summary>
        private void ExportChain()
        {
            var src = _sourceImage;
            if (src == null || !src.IsInitialized())
            {
                SetHint("没有输入图像，无法导出：先让上游节点出一张图，或在界面里指定图像变量");
                return;
            }

            // 借"存清单"这个对话框定位目录 + 起个统一前缀，只弹一次框
            var dialog = new SaveFileDialog
            {
                Title = "导出处理链",
                Filter = "参数清单|*.txt",
                FileName = $"预处理链_{DateTime.Now:yyyyMMdd_HHmmss}.txt",
            };
            if (dialog.ShowDialog() != true) return;

            WriteExport(dialog.FileName);
        }

        /// <summary>
        /// 真正干活的导出体（与"弹框选路径"分开，是为了能脱离界面单测）。
        /// </summary>
        /// <param name="manifestPath">清单文件全路径；图片与链快照写在同一目录、用同一个文件名前缀</param>
        internal void WriteExport(string manifestPath)
        {
            // 先按当前参数重跑一遍：导出的必须是"此刻的效果"，顺带把 _sourceImage 刷新到位
            RefreshPreview();
            var src = _sourceImage;
            if (src == null || !src.IsInitialized())
            {
                SetHint("没有输入图像，无法导出：先让上游节点出一张图，或在界面里指定图像变量");
                return;
            }

            string directory = Path.GetDirectoryName(manifestPath) ?? Environment.CurrentDirectory;
            string stem = Path.GetFileNameWithoutExtension(manifestPath);

            int enabled = 0;
            foreach (var op in Operators) { if (op.Enabled) enabled++; }

            var lines = new List<string>
            {
                $"导出时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                $"输入图像：{DescribeImage(src)}",
                $"算子步数：{Operators.Count}（启用 {enabled}）",
                string.Empty,
                "步号\t算子\t参数摘要\t图像文件\t尺寸/通道",
            };

            int saved = 0;
            // "当前图"跟着链走：禁用与透传都不产生新对象，因此下一步的输入仍是它。
            // 用它做引用比对判断"这一步到底有没有产出新图"，比只查 _stepImages 更准 ——
            // 中间的透传步（如缩放因子 1.0、框正好盖住整图）在 _stepImages 里存的是上一步那张，
            // 不管的话会给同一张图重复写出一个文件。
            HImage current = src;
            for (int i = 0; i < Operators.Count; i++)
            {
                var op = Operators[i];
                string step = $"{i + 1:00}";

                if (!op.Enabled)
                {
                    lines.Add($"{step}\t{op.DisplayName}\t{op.Summary}\t已禁用，未出图\t-");
                    continue;
                }

                var image = _stepImages.TryGetValue(i, out var shot) && shot != null && shot.IsInitialized() ? shot : null;
                if (image == null || ReferenceEquals(image, current))
                {
                    // 透传步在 _stepImages 里通常是故意留空的（不持有所有权），这里如实写清楚，别让人以为丢文件了
                    lines.Add($"{step}\t{op.DisplayName}\t{op.Summary}\t透传，未单独出图\t-");
                    continue;
                }

                string imagePath = Path.Combine(directory, $"{stem}_{step}_{Sanitize(op.DisplayName)}.png");
                try
                {
                    PreprocessHService.SaveImage(image, imagePath);
                    saved++;
                    current = image;
                    lines.Add($"{step}\t{op.DisplayName}\t{op.Summary}\t{Path.GetFileName(imagePath)}\t{DescribeImage(image)}");
                }
                catch (Exception ex)
                {
                    lines.Add($"{step}\t{op.DisplayName}\t{op.Summary}\t写出失败：{ex.Message}\t{DescribeImage(image)}");
                }
            }

            try
            {
                File.WriteAllLines(manifestPath, lines);
                // 顺带留一份可回放的链文本：图没了还能把参数照抄回来（格式与剪贴板完全一致）
                File.WriteAllText(Path.ChangeExtension(manifestPath, ".json"),
                    JsonSerializer.Serialize(SnapshotOperators(), ChainJson));
            }
            catch (Exception ex)
            {
                SetHint($"图片已导出 {saved} 张，但清单写入失败：{ex.Message}");
                return;
            }

            SetHint(saved == 0
                ? $"没有一张图可导出（{Operators.Count} 步全是透传或禁用），清单见 {Path.GetFileName(manifestPath)}"
                : $"已导出 {saved}/{Operators.Count} 步图片 + 清单到 {directory}");
        }

        /// <summary>尺寸与通道数，拼成一行给人看（拿不准导出的图对不对时，这行最有用）</summary>
        private static string DescribeImage(HImage image)
        {
            try
            {
                PreprocessHService.ImageSize(image, out int width, out int height);
                return $"{width}×{height}，{PreprocessHService.CountChannels(image)}通道";
            }
            catch (Exception) { return "尺寸未知"; }
        }

        /// <summary>算子名会进文件名，中文没问题，但冒号斜杠这类非法字符得替掉</summary>
        private static string Sanitize(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }

        #endregion

        #region 生命周期

        /// <summary>配置灌值：基类把 Chain 还原成快照列表后，再据此重建活链</summary>
        public override void ApplyConfigValues(IStepConfigData stepData)
        {
            base.ApplyConfigValues(stepData);
            RebuildOperators();
        }

        /// <summary>确认：先把界面上的活链写回快照，再交给基类落盘</summary>
        public override void OnConfirm(IStepConfigData stepData)
        {
            WriteChain();
            base.OnConfirm(stepData);
        }

        public object GetConfigView(IStepConfigData stepData)
        {
            Initialize(stepData);
            return new PreProcessingView { DataContext = this };
        }

        public override void Dispose()
        {
            _previewDebounce.Stop();
            DisposeStepImages();
            _sourceImage = null;
            // 编辑框兜底释放：正常路径由控件在集合变更/Unloaded 时处置，这里覆盖"控件没挂上"和异常路径。
            // DrawingObjectInfo.Dispose 内部有 disposed 标志，重复调用安全。
            foreach (var info in CanvasRois)
                info.Dispose();
            // 输出端口里的图交给 base.Dispose() 统一释放
            base.Dispose();
        }

        #endregion

        #region 流程执行

        /// <summary>
        /// 跑链并输出。
        /// 契约提醒：进入本方法时基类已把 Success 预置为 true，只有失败路径才需要显式写 false。
        /// </summary>
        public override void RunAlgorithm(IExecutionContext context)
        {
            var src = SrcImage.ActualValue;
            if (src == null || !src.IsInitialized())
            {
                Success.Value = false;
                ErrorMessage.Value = "输入图像为空或未初始化";
                return;
            }

            DisposeOldOutput();

            HImage final;
            PreprocessOperator[] steps;
            try
            {
                // 先拍快照再跑：本网关在流程线程执行，配置界面可能同时在往 Operators 里增删步骤。
                // ObservableCollection 不是线程安全的，流程线程绝不碰活链，只读这份快照
                steps = Operators.ToArray();
                // 传 null 表示"不保留中间图"：跑一步扔一步，正式运行不会攒一堆大图在内存里
                final = ExecuteChain(src, steps, stepResults: null, context.Logger);
            }
            catch (Exception ex)
            {
                Success.Value = false;
                ErrorMessage.Value = $"图像预处理失败：{ex.Message}";
                context.Logger.Error($"{InstanceName} {ErrorMessage.Value}");
                return;
            }

            // 整条链都禁用/全透传时，final 就是上游那张图。
            // 输出端口挂"别人的图"等于让基类 Dispose() 去释放它，上游下次拿到的就是一张死图 —— 必须复制一份。
            if (ReferenceEquals(final, src)) final = PreprocessHService.Clone(src);

            Image.TypedValue = final;

            // 端口统计同样只读快照：这里是流程线程，枚举活链同样有跨线程风险
            var active = steps.Where(o => o.Enabled).Select(o => o.DisplayName).ToList();
            StepCount.Value = steps.Length;
            ActiveCount.Value = active.Count;
            ActiveOperators.Value = active.Count == 0 ? "(全部禁用)" : string.Join(" → ", active);

            if (DisplayViewIndex > 0)
                TryPublishPreview(final, context);

            Success.Value = true;
        }

        /// <summary>
        /// 推图到主界面。
        /// 显示层拿到事件后自己 CopyImage 并独占那份副本，所以这里传的仍是端口持有的图；
        /// 但推图属于"锦上添花"，投递失败绝不能把一次正常的预处理判成失败。
        /// </summary>
        private void TryPublishPreview(HImage image, IExecutionContext context)
        {
            try { this.PublishPreview(image, DisplayViewIndex); }
            catch (Exception ex) { context.Logger.Warn($"{InstanceName} 预览推送失败：{ex.Message}"); }
        }

        /// <summary>
        /// 链执行引擎（预览与正式运行共用，保证"所见即所得"）。
        ///
        /// 所有权约定（本次迁移最较真的地方）：
        /// · 入参 src 永远归调用方，本方法一个都不会释放它；
        /// · 算子返回新图 ⇒ 归本链；<see cref="stepResults"/> 非空时全部留住给界面逐步看，
        ///   为空时"用完即弃"，只把最后一张交出去；
        /// · 算子抛异常 ⇒ 记日志、跳过这一步，图像继续往下流（现场调参时一条链里某个算子参数填错，
        ///   不应该让整条流程红掉，这是参考实现最招骂的行为之一）。
        /// </summary>
        /// <param name="src">输入图（不拥有）</param>
        /// <param name="steps">要执行的算子序列；流程线程必须传快照数组，UI 线程预览可直接传活的 Operators</param>
        /// <param name="stepResults">长度与 steps 一致的数组；传 null 表示不保留中间结果</param>
        /// <param name="logger">日志通道，可为 null（预览路径）</param>
        /// <returns>链的最终图像：可能仍是 src（全透传），此时由调用方决定是否复制</returns>
        private HImage ExecuteChain(HImage src, IReadOnlyList<PreprocessOperator> steps, HImage?[]? stepResults, ILogService? logger)
        {
            var current = src;
            var currentOwned = false;

            for (int i = 0; i < steps.Count; i++)
            {
                var op = steps[i];
                if (!op.Enabled)
                {
                    // 禁用 = 原样透传。参考实现这里是"不给输出赋值"，
                    // 于是下一个算子拿到的是再上一步的陈旧图像，现象极难查
                    if (stepResults != null) stepResults[i] = current;
                    continue;
                }

                HImage result;
                try
                {
                    result = op.Apply(current);
                }
                catch (Exception ex)
                {
                    logger?.Warn($"{InstanceName} 算子[{op.DisplayName}] 执行失败，已跳过：{ex.Message}");
                    if (stepResults != null) stepResults[i] = current;
                    continue;
                }

                if (stepResults != null) stepResults[i] = result;

                // 框越界的处置是"自动夹取"而不是报错，所以必须留话：
                // 现场最常见的现象是把框拖出图外，然后纳闷"裁出来的怎么是空的/偏的"。
                // 预览路径没有 logger，靠算子摘要里的"（越界已夹取）"提示。
                if (op is BoxOperatorBase { WasBoxClamped: true } clamped)
                    logger?.Warn($"{InstanceName} 算子[{clamped.DisplayName}] 编辑框超出图像范围，已自动夹取到图内执行");

                if (ReferenceEquals(result, current)) continue; // 该算子对当前输入无从下手，透传

                if (currentOwned && stepResults == null)
                {
                    try { current.Dispose(); } catch { /* 中间图释放失败不影响后续 */ }
                }

                current = result;
                currentOwned = true;
            }

            return current;
        }

        /// <summary>
        /// 释放上一次运行留在输出端口里的图。
        /// 先置空再释放：OutputPort 的 TypedValue 走 SetProperty 相等判断，
        /// 直接赋新值时若引用恰好相同就不会发通知，下游会读到旧数据。
        /// </summary>
        private void DisposeOldOutput()
        {
            var old = Image.TypedValue;
            if (old == null) return;
            // 端口的 Value 被声明成非空 object，但"清空"本就是它的合法状态，这里按语义强置空
            Image.Value = null!;  // 走 object 通道置空，TypedValue 会被换成 default
            try { old.Dispose(); } catch { }
        }

        #endregion
    }
}
