using Core.Commands;
using Core.Events;
using Core.Halcon;
using Core.Halcon.Models;
using Core.Interfaces;
using HalconDotNet;
using Plugin.CreateRoi.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;

namespace Plugin.CreateRoi
{
    [Display(
        Name = "ROI",
        GroupName = "常用工具",
        Description = "在图像上创建多个ROI区域,输出裁剪图像",
        ShortName = "\uf1c5"
    )]
    public class CreateRoiPlugin : VisionPluginBase, IPluginCustomViewProvider, IDynamicOutputProvider
    {
        #region 配置参数

        [StepConfig]
        public int DisplayViewIndex { get; set; } = 1;

        private bool _maskInvert;
        [StepConfig]
        public bool MaskInvert
        {
            get => _maskInvert;
            set { if (SetProperty(ref _maskInvert, value)) ScheduleMaskPreview(); }
        }

        // ObservableCollection：增删触发列表绑定刷新（List<T> 不会通知 UI）
        private ObservableCollection<RoiItem> _roiList = new();
        [StepConfig]
        public ObservableCollection<RoiItem> RoiList
        {
            get => _roiList;
            set { _roiList = value ?? new(); OnPropertyChanged(); RebuildDynamicOutputs(); }
        }

        #endregion

        #region 输入输出端口

        public InputPort<HImage> SrcImage { get; } = new();

        public OutputPort<int> RoiCount { get; } = new();

        /// <summary>合并有效区域 = (ΣROI ∪ 涂抹) − 擦除（不随排除模式反转，区域语义恒为"有效区"）</summary>
        public OutputPort<HRegion> MaskRegion { get; } = new("MaskRegion", "合并有效区域（ROI∪涂抹−擦除）");

        /// <summary>二值掩膜图（有效区 255、其余 0；排除模式下反转），下游可做掩膜运算</summary>
        public OutputPort<HImage> MaskImage { get; } = new("MaskImage", "二值掩膜图（排除模式下反转）");

        #endregion

        #region 视图属性

        private HImage? _previewImage;
        public HImage? PreviewImage
        {
            get => _previewImage;
            set { SetProperty(ref _previewImage, value); UpdateDisplayImage(); }
        }

        /// <summary>当前选中 ROI（列表选中态；与 CanvasActiveRoi 双向联动）</summary>
        private RoiItem? _selectedRoi;
        public RoiItem? SelectedRoi
        {
            get => _selectedRoi;
            set
            {
                var old = _selectedRoi;
                // 先判断再退订： setter 会经 CanvasActiveRoi 联动回写同值重入，
                // 若无条件先退订，同值路径短路后 ParamEdited 订阅被摘走不再补回 —— 参数微调就此断链
                if (!SetProperty(ref _selectedRoi, value)) return;
                if (old != null) old.ParamEdited -= OnSelectedRoiParamEdited;
                if (_selectedRoi != null)
                {
                    _selectedRoi.ParamEdited += OnSelectedRoiParamEdited;
                    // 列表选中 → 画布挂接句柄（控件 ActiveRoi DP 回调完成 Attach）；
                    // 画布活动对象没变就不写入，避免 setter 互相回环
                    var info = CanvasRois.FirstOrDefault(x => x.RoiName == _selectedRoi.Name);
                    if (!ReferenceEquals(CanvasActiveRoi, info))
                        CanvasActiveRoi = info;
                }
            }
        }

        /// <summary>
        /// 画布集合（控件 DrawObjectList 的绑定源，VM 是唯一所有者）：
        /// 控件右键新建/删除直接改动此集合 → CollectionChanged 回写 RoiList
        /// </summary>
        public ObservableCollection<DrawingObjectInfo> CanvasRois { get; } = new();

        private DrawingObjectInfo? _canvasActiveRoi;
        /// <summary>
        /// 画布编辑中的 ROI（控件 ActiveRoi 双向绑定）：
        /// 画布点选回传 → 同步列表选中；列表选中写入 → 画布挂句柄
        /// </summary>
        public DrawingObjectInfo? CanvasActiveRoi
        {
            get => _canvasActiveRoi;
            set
            {
                if (!SetProperty(ref _canvasActiveRoi, value)) return;
                // 画布点选 → 列表定位；点空白（null）→ 列表同步取消选中（否则列表仍高亮却无句柄可拖）
                var roi = value == null ? null : RoiList.FirstOrDefault(x => x.Name == value.RoiName);
                if (!ReferenceEquals(SelectedRoi, roi))
                    SelectedRoi = roi;
            }
        }

        #endregion

        #region 显示管理（原图 / 掩膜预览，ViewModel）

        private bool _isMaskPreview;
        /// <summary>掩膜预览开关（视图 RadioButton 绑定）</summary>
        public bool IsMaskPreview
        {
            get => _isMaskPreview;
            set { if (SetProperty(ref _isMaskPreview, value)) { if (value) RefreshMaskPreview(); else UpdateDisplayImage(); } }
        }

        /// <summary>掩膜预览图（视图持有职责上提；DisplayImage 切换/刷新时统一 Dispose 旧图）</summary>
        private HImage? _maskPreviewImage;

        /// <summary>ImageEdit 绑定源：按模式返回原图或掩膜图</summary>
        public HImage? DisplayImage => IsMaskPreview ? _maskPreviewImage : _previewImage;

        /// <summary>拖拽等高频触发的掩膜重算防抖（全图像素运算，不节流会卡顿）</summary>
        private readonly System.Windows.Threading.DispatcherTimer _maskDebounce =
            new() { Interval = TimeSpan.FromMilliseconds(200) };

        /// <summary>重算掩膜预览并刷新显示（掩膜模式才生效）</summary>
        public void RefreshMaskPreview()
        {
            if (!IsMaskPreview || _previewImage == null || !_previewImage.IsInitialized()) return;
            _maskPreviewImage?.Dispose();
            _maskPreviewImage = null;

            using var merged = BuildMergedRegion();
            _maskPreviewImage = BuildMaskAndResult(_previewImage, merged, MaskInvert, out var mask, out _);
            mask?.Dispose();
            OnPropertyChanged(nameof(DisplayImage));
        }

        /// <summary>防抖调度（掩膜模式下 200ms 后重算）</summary>
        public void ScheduleMaskPreview()
        {
            if (!IsMaskPreview) return;
            _maskDebounce.Stop();
            _maskDebounce.Start();
        }

        /// <summary>底图变化（试运行回填/打开图片）后按模式刷新显示</summary>
        private void UpdateDisplayImage()
        {
            if (IsMaskPreview) ScheduleMaskPreview();
            else OnPropertyChanged(nameof(DisplayImage));
        }

        #endregion

        #region 画布同步（集合与选中联动，纯 VM）

        public CreateRoiPlugin()
        {
            CanvasRois.CollectionChanged += OnCanvasRoisChanged;
        }

        /// <summary>画布集合变更（控件新建/删除/清空驱动）→ 回写 RoiList 并联动端口/掩膜</summary>
        private void OnCanvasRoisChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (_seedingCanvas) return;
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add when e.NewItems != null:
                    foreach (DrawingObjectInfo info in e.NewItems)
                    {
                        info.PropertyChanged += OnCanvasRoiTuplesChanged;
                        // 重名防御：控件命名序号每开一次配置界面就归零，重开配置再画必然撞上历史名字
                        //（动态端口静默去重 → 输出互相覆盖 + 内存泄漏 + 按名查找歧义）。强制唯一名并回写控件标签
                        info.RoiName = NextFreeName(info.RoiName,
                            RoiList.Select(x => x.Name)
                                .Concat(CanvasRois.Where(x => !ReferenceEquals(x, info)).Select(x => x.RoiName)));
                        RoiList.Add(new RoiItem
                        {
                            Name = info.RoiName,
                            ShapeType = info.ShapeType,
                            Params = info.HTuples.Select(t => t.D).ToArray()
                        });
                    }
                    OnRoiListChanged();
                    break;

                case NotifyCollectionChangedAction.Remove when e.OldItems != null:
                    foreach (DrawingObjectInfo info in e.OldItems)
                    {
                        info.PropertyChanged -= OnCanvasRoiTuplesChanged;
                        var roi = RoiList.FirstOrDefault(x => x.Name == info.RoiName);
                        if (roi == null) continue;
                        RoiList.Remove(roi);
                        if (SelectedRoi == roi) SelectedRoi = null;
                    }
                    OnRoiListChanged();
                    break;

                case NotifyCollectionChangedAction.Reset:
                    RoiList.Clear();
                    SelectedRoi = null;
                    OnRoiListChanged();
                    break;
            }
        }

        /// <summary>
        /// 拖拽/松手回传：控件把句柄参数写进 info.HTuples（INPC）→ 同步 RoiItem.Params。
        /// 被删除的 ROI 已退订，删除后的回写自动跳过
        /// </summary>
        private void OnCanvasRoiTuplesChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(DrawingObjectInfo.HTuples)) return;
            var info = (DrawingObjectInfo)sender!;
            var roi = RoiList.FirstOrDefault(x => x.Name == info.RoiName);
            if (roi == null || info.HTuples == null) return;
            roi.Params = info.HTuples.Select(t => t.D).ToArray();
            ScheduleMaskPreview();
        }

        /// <summary>选中 ROI 的参数经数值框编辑 → 写入画布 info.HTuples（INPC → 控件应用句柄并重绘）</summary>
        private void OnSelectedRoiParamEdited(RoiItem roi)
        {
            var info = CanvasRois.FirstOrDefault(x => x.RoiName == roi.Name);
            if (info != null)
                info.HTuples = roi.Params.Select(p => new HTuple(p)).ToArray();
            ScheduleMaskPreview();
        }

        /// <summary>
        /// 删除当前选中 ROI（Del 键/删除按钮；集合 Remove → 画布摘句柄 + RoiList 回写）。
        /// CanExecute 守卫焦点在文本框时不触发（Del 是文本编辑键）
        /// </summary>
        public void DeleteSelectedRoi()
        {
            if (SelectedRoi is not RoiItem roi) return;
            var info = CanvasRois.FirstOrDefault(x => x.RoiName == roi.Name);
            if (info != null) CanvasRois.Remove(info);
        }

        public ICommand DeleteSelectedCommand => _deleteSelectedCommand ??=
            new RelayCommand(
                _ => DeleteSelectedRoi(),
                _ => System.Windows.Input.Keyboard.FocusedElement is not System.Windows.Controls.TextBox);
        private ICommand? _deleteSelectedCommand;

        /// <summary>
        /// 清空全部 ROI（按钮命令）：二次确认防误触（清空不可恢复）；
        /// 集合 Reset → 画布摘句柄释放 + RoiList 回写
        /// </summary>
        public ICommand ClearRoisCommand => _clearRoisCommand ??=
            new RelayCommand(
                _ =>
                {
                    if (MessageBox.Show("确认清空全部 ROI 区域？该操作不可撤销。", "清空 ROI",
                            MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                        return;
                    CanvasRois.Clear();
                },
                _ => CanvasRois.Count > 0);
        private ICommand? _clearRoisCommand;

        private void OnRoiListChanged()
        {
            RebuildDynamicOutputs();
            ScheduleMaskPreview();
        }

        #endregion

        public object GetConfigView(IStepConfigData stepData)
        {
            Initialize(stepData);
            return new CreateRoiView() { DataContext = this };
        }

        /// <summary>配置初始化：灌入配置后按 RoiList 播种画布集合并恢复涂擦（绑定建立时自动上屏）</summary>
        public override void Initialize(IStepConfigData stepData)
        {
            base.Initialize(stepData);
            _seedingCanvas = true;
            try
            {
                CanvasRois.Clear();
                // 旧方案可能带有历史重名（旧版控件命名序号重置 bug 所致），播种前统一去重，
                // 否则 Crop_ 动态端口静默去重后输出互相覆盖
                var used = new HashSet<string>();
                var renamed = false;
                foreach (var roi in RoiList)
                {
                    var free = NextFreeName(roi.Name, used);
                    if (free != roi.Name) renamed = true;
                    roi.Name = free;
                    used.Add(free);
                    CanvasRois.Add(new DrawingObjectInfo(
                        roi.ShapeType, roi.Params.Select(p => new HTuple(p)).ToArray(), roi.Name));
                }
                if (renamed)
                    RebuildDynamicOutputs(); // 有改名时端口与快照同步跟进
            }
            finally { _seedingCanvas = false; }
            RestoreSmear();
        }

        /// <summary>
        /// 取一个不在 taken 集合中的 ROI 名：不冲突原样返回；冲突则取同词干现有最大序号 +1
        /// （"ROI_0/ROI_2" 在场 → "ROI_3"；无序号名"缺陷区" → "缺陷区_1"）
        /// </summary>
        internal static string NextFreeName(string? desired, IEnumerable<string?> taken)
        {
            var name = string.IsNullOrWhiteSpace(desired) ? "ROI" : desired.Trim();
            var used = new HashSet<string>();
            foreach (var t in taken)
                if (!string.IsNullOrWhiteSpace(t)) used.Add(t);
            if (used.Count == 0 || !used.Contains(name)) return name;

            string stem;
            long next;
            var m = Regex.Match(name, @"^(.*?)(\d+)$");
            if (m.Success)
            {
                stem = m.Groups[1].Value;
                next = long.TryParse(m.Groups[2].Value, out var n0) ? n0 : 0;
            }
            else
            {
                stem = name + "_";
                next = 0;
            }
            foreach (var t in used)
            {
                var tm = Regex.Match(t, "^" + Regex.Escape(stem) + @"(\d+)$");
                if (tm.Success && long.TryParse(tm.Groups[1].Value, out var n))
                    next = Math.Max(next, n);
            }
            var candidate = stem + (next + 1);
            while (used.Contains(candidate))
            {
                next++;
                candidate = stem + (next + 1);
            }
            return candidate;
        }

        private bool _seedingCanvas;

        /// <summary>视图加载完成（生命周期信号，视图桥接调用）：回填输入图像</summary>
        public void OnViewLoaded()
        {
            var src = SrcImage.ActualValue;
            if (src != null && src.IsInitialized()) PreviewImage = src;
        }

        /// <summary>配置实例释放：掩膜预览图、防抖计时器、主窗口合成图与涂擦区域（直接清字段，避免析构期触发 INPC/持久化）</summary>
        public override void Dispose()
        {
            _maskDebounce.Stop();
            _maskPreviewImage?.Dispose();
            _maskPreviewImage = null;
            _runtimeMaskedImg?.Dispose();
            _runtimeMaskedImg = null;
            _smearDraw?.Dispose(); _smearDraw = null;
            _smearErase?.Dispose(); _smearErase = null;
            // 画布 ROI 兜底释放：正常路径控件 Unloaded 已处置，此处覆盖控件未挂接/异常路径（Dispose 幂等）
            foreach (var info in CanvasRois)
                info.Dispose();
            base.Dispose();
        }

        public override void RunAlgorithm(IExecutionContext context)
        {
            var src = SrcImage.ActualValue;
            if (src == null || !src.IsInitialized())
            {
                Fail("输入图像为空或未初始化");
                return;
            }
            PreviewImage = src;
            // 上一轮输出的 HImage 由基类轮首自动回收（AutoDisposeRoundOutputs），无需手写 Dispose
            // 发布到主窗口的合成图不走端口，自管生命周期（轮首 + Dispose 释放）
            _runtimeMaskedImg?.Dispose();
            _runtimeMaskedImg = null;

            src.GetImageSize(out int width, out int height);

            // 合并有效区域 = (ΣROI ∪ 涂抹) − 擦除，与配置界面掩膜预览同一口径；
            // 所有权移交 MaskRegion 端口，由基类轮首统一回收
            var merged = BuildMergedRegion();
            MaskRegion.Value = merged;

            // 二值掩膜（排除模式反转：ROI 内 0 外 255）+ 无效区置黑的合成图（发布主界面用）
            var visual = BuildMaskAndResult(src, merged, MaskInvert, out var mask, out _);
            MaskImage.Value = mask; // 所有权移交端口
            _runtimeMaskedImg = visual;

            // 发布主界面：0 = 不显示（此前选"不显示"仍会发布到窗口1）；发布合并掩膜合成图，
            // 一帧看清全部 ROI 与涂擦修正——此前逐 ROI 发到同一窗口互相覆盖，只能看到最后一个。
            // 窗口号必须直传：DisplayViewIndex 本身就是 1~9 的窗口号，事件消费端按"ViewIndex == 窗口号"
            // 等值筛选。此前照抄了扩展方法里的过期示例写成 +1，选"窗口1"会投进第 2 格、选"窗口9"则哪格都不显示
            if (DisplayViewIndex > 0 && visual != null && visual.IsInitialized())
                this.PublishPreview(visual, DisplayViewIndex);

            // 逐 ROI 裁剪：Crop_{ROI名} 动态端口；单个失败不阻断整体，但必须留痕供现场追查
            var written = new HashSet<string>(StringComparer.Ordinal);
            int writtenCount = 0;
            foreach (var roi in RoiList)
            {
                var portName = $"Crop_{roi.Name}";
                if (!written.Add(portName)) continue; // 重名防御：只输出第一个（正常路径已被唯一名校验拦截）
                if (!Outputs.TryGetValue(portName, out var port)) continue;

                // 先清零本轮的偏移端口：下面任一失败分支都要让下游读到 0，
                // 而不是上一轮残留的值（double 端口基类不回收，脏值会一直挂着）
                SetValuePort($"OffsetRow_{roi.Name}", 0d);
                SetValuePort($"OffsetCol_{roi.Name}", 0d);

                HRegion? region = BuildRegion(roi);
                if (region == null)
                {
                    context?.Logger?.Warn($"{InstanceName} ROI[{portName}] 形状参数无效，跳过裁剪");
                    continue;
                }

                HImage? crop = null;
                HImage? domain = null;
                try
                {
                    // 擦除涂抹同样作用于每个 ROI 的有效域（否则"擦掉的地方"仍原样出现在裁剪图里）
                    if (_smearErase != null && _smearErase.IsInitialized())
                    {
                        var cut = region.Difference(_smearErase);
                        region.Dispose();
                        region = cut;
                    }
                    HOperatorSet.AreaCenter(region, out HTuple area, out HTuple _, out HTuple _);
                    if (area.D <= 0)
                    {
                        context?.Logger?.Warn($"{InstanceName} ROI[{portName}] 有效区域为空（被擦除殆尽），跳过裁剪");
                        continue;
                    }

                    // 裁剪原点 = 有效域外接矩形的左上角（crop_domain 裁的就是它）。
                    // 不旋转图像，所以下游回变换只需平移：global = local + (row1, col1)
                    HOperatorSet.SmallestRectangle1(
                        region, out HTuple r1, out HTuple c1, out HTuple _, out HTuple _);
                    double originRow = r1.Length > 0 ? r1.D : 0;
                    double originCol = c1.Length > 0 ? c1.D : 0;

                    HOperatorSet.ReduceDomain(src, region, out HObject reduced);

                    // ② 带 domain 的原图：new HImage 生成独立句柄，之后释放 reduced 不影响它
                    domain = new HImage(reduced);
                    HImage? handedDomain = domain;
                    if (SetImagePort($"Domain_{roi.Name}", handedDomain))
                        domain = null;  // 所有权已移交端口（基类轮首统一回收）

                    // ③ 裁剪原点
                    SetValuePort($"OffsetRow_{roi.Name}", originRow);
                    SetValuePort($"OffsetCol_{roi.Name}", originCol);

                    HOperatorSet.CropDomain(reduced, out HObject croppedImg);
                    reduced.Dispose();
                    crop = new HImage(croppedImg);
                    croppedImg.Dispose();
                    port.Set(crop); // 免强转写动态端口：命中 OutputPort<HImage> 走 TypedValue 强类型路径
                    writtenCount++;
                    crop = null;    // 所有权已移交端口（轮首统一回收）
                }
                catch (Exception ex)
                {
                    context?.Logger?.Warn($"{InstanceName} ROI[{portName}] 裁剪失败：{ex.Message}");
                }
                finally
                {
                    region.Dispose();
                    if (crop != null) crop.Dispose(); // 端口写入前抛异常时不泄漏
                    if (domain != null) domain.Dispose(); // 同上：移交失败时不泄漏
                }
            }

            // RoiCount = 本轮实际成功输出的裁剪图数（配置数 ≠ 成功数，下游按此循环取数才不越界）
            RoiCount.Value = writtenCount;

            // Success 基类已预置 true（默认成功、显式失败），无需再写
        }

        /// <summary>发布到主窗口的掩膜合成图（不走端口，轮首与 Dispose 释放）</summary>
        private HImage? _runtimeMaskedImg;

        /// <summary>
        /// 按 RoiList 重建动态输出端口（Crop_{ROI名}），并同步定义快照（名字+类型）到 StepData
        /// - 配置实例：RoiList 变化时调用（setter + 视图 CollectionChanged）
        /// - 编译实例：FlowCompiler 在 ApplyConfigValues 后调用（从存盘快照恢复端口供接线）
        /// 重名 ROI 只建一个端口（正常路径已被唯一名校验拦截，此处兜底历史数据）
        /// </summary>
        public void RebuildDynamicOutputs()
        {
            ClearDynamicOutputs();
            var snapshot = new List<DynamicPortInfo>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var roi in RoiList)
            {
                string portName = $"Crop_{roi.Name}";
                if (!seen.Add(portName)) continue;

                // ① 裁剪图（既有端口，行为不变）
                AddDynamicOutput(new OutputPort<HImage>(portName, $"ROI '{roi.Name}' 的裁剪图"));
                snapshot.Add(new DynamicPortInfo
                {
                    Name = portName,
                    DataTypeName = typeof(HImage).AssemblyQualifiedName,
                    Description = $"ROI '{roi.Name}' 的裁剪图"
                });

                // ② 带 domain 的原图（新增）
                //
                // 为什么要有它：裁剪图把原点搬走了，下游所有坐标都变成本地坐标，
                // 每个插件都得自己把偏移加回来（漏一个就是静默偏移一个 ROI 左上角）。
                // 而"带 domain 的原图"坐标系没动 —— HALCON 的算子只在 domain 内干活，
                // 坐标仍然是全局的。实测：find_shape_model 在 domain 图上搜索，
                // 命中返回的仍是全局坐标，domain 外的目标直接不命中。
                // 于是**下游插件一个都不用改**就能吃到 ROI。
                string domainName = $"Domain_{roi.Name}";
                AddDynamicOutput(new OutputPort<HImage>(domainName,
                    $"ROI '{roi.Name}' 带 domain 的原图（全局坐标，下游零回变换）"));
                snapshot.Add(new DynamicPortInfo
                {
                    Name = domainName,
                    DataTypeName = typeof(HImage).AssemblyQualifiedName,
                    Description = $"ROI '{roi.Name}' 带 domain 的原图（只在 ROI 内有像素，坐标仍为全局坐标）"
                });

                // ③ 裁剪原点（新增）：给"必须用裁剪图"的场景兜底。
                // crop_domain 裁的是外接矩形、不旋转图像，所以回变换永远只是平移：
                //     global_row = local_row + OffsetRow_xxx
                //     global_col = local_col + OffsetCol_xxx
                // 拆成两个 double 而不是一个 HTuple：下游（脚本/变量/计算）可直接当标量连线，
                // 也保住端口的编译期类型检查（不引入 object 多态端口）。
                string rowName = $"OffsetRow_{roi.Name}";
                AddDynamicOutput(new OutputPort<double>(rowName,
                    $"ROI '{roi.Name}' 裁剪图原点在原图中的行（global_row = local_row + 本值）"));
                snapshot.Add(new DynamicPortInfo
                {
                    Name = rowName,
                    DataTypeName = typeof(double).AssemblyQualifiedName,
                    Description = $"ROI '{roi.Name}' 裁剪图左上角在原图中的行坐标"
                });

                string colName = $"OffsetCol_{roi.Name}";
                AddDynamicOutput(new OutputPort<double>(colName,
                    $"ROI '{roi.Name}' 裁剪图原点在原图中的列（global_col = local_col + 本值）"));
                snapshot.Add(new DynamicPortInfo
                {
                    Name = colName,
                    DataTypeName = typeof(double).AssemblyQualifiedName,
                    Description = $"ROI '{roi.Name}' 裁剪图左上角在原图中的列坐标"
                });
            }

            // 同步快照到 StepData（供编译器和绑定界面读取，不依赖配置实例存活）
            if (StepData != null)
            {
                StepData.OutputPortDefinitions = snapshot;
            }
        }

        /// <summary>
        /// 写动态 HImage 端口。端口不存在 / 类型不符时返回 false（调用方据此决定是否自行释放）。
        ///
        /// 为什么不一律用 port.Set()：那是给"免强转"准备的扩展，命中不了时行为不直观；
        /// 这里要明确的"到底有没有接管所有权"的返回值，否则 domain 图不是泄漏就是被提前释放。
        /// </summary>
        private bool SetImagePort(string portName, HImage? image)
        {
            if (!Outputs.TryGetValue(portName, out var port)) return false;
            if (port is not OutputPort<HImage> typed) return false;
            typed.Value = image;
            return true;
        }

        /// <summary>写动态 double 端口（OffsetRow/OffsetCol）</summary>
        private void SetValuePort(string portName, double value)
        {
            if (!Outputs.TryGetValue(portName, out var port)) return;
            if (port is OutputPort<double> typed) typed.Value = value;
        }

        /// <summary>
        /// 合并当前 RoiList 中的所有区域（ROI 为空时返回已初始化的空区域）
        /// </summary>
        public HRegion BuildMergedRegion()
        {
            HRegion? merged = null;
            foreach (var roi in RoiList)
            {
                var region = BuildRegion(roi);
                if (region == null) continue;

                if (merged == null)
                {
                    merged = region;
                }
                else
                {
                    var union = merged.Union2(region);
                    merged.Dispose();
                    region.Dispose();
                    merged = union;
                }
            }

            if (merged == null)
            {
                merged = new HRegion();
                merged.GenEmptyRegion(); // 防爆：显式初始化为空区域
            }

            // 涂擦修正：最终区域 = (ROI合并 ∪ 涂抹) − 擦除
            if (_smearDraw != null && _smearDraw.IsInitialized())
            {
                var u = merged.Union2(_smearDraw);
                merged.Dispose();
                merged = u;
            }
            if (_smearErase != null && _smearErase.IsInitialized())
            {
                var d = merged.Difference(_smearErase);
                merged.Dispose();
                merged = d;
            }

            return merged;
        }

        #region 画笔涂擦集成（属性即绑定通道，ImageEdit.SmearDraw/SmearErase 的绑定源）

        private HRegion? _smearDraw;

        /// <summary>
        /// 累计"涂抹"区域（ImageEdit.SmearDraw 双向绑定）：
        /// 控件笔画结束以新实例回写 → 本属性释放被替换的旧实例并持久化；
        /// VM 是唯一所有者，控件只读参与并集
        /// </summary>
        public HRegion? SmearDraw
        {
            get => _smearDraw;
            set
            {
                if (ReferenceEquals(_smearDraw, value)) return;
                _smearDraw?.Dispose();
                _smearDraw = value;
                OnPropertyChanged();
                SyncSmearData();
                if (IsMaskPreview) RefreshMaskPreview();
            }
        }

        private HRegion? _smearErase;

        /// <summary>累计"擦除"区域（所有权约定同 SmearDraw）</summary>
        public HRegion? SmearErase
        {
            get => _smearErase;
            set
            {
                if (ReferenceEquals(_smearErase, value)) return;
                _smearErase?.Dispose();
                _smearErase = value;
                OnPropertyChanged();
                SyncSmearData();
                if (IsMaskPreview) RefreshMaskPreview();
            }
        }

        private string _smearDrawData = "";
        /// <summary>涂抹区域持久化快照（游程编码文本，随方案存盘）</summary>
        [StepConfig]
        public string SmearDrawData
        {
            get => _smearDrawData;
            set { _smearDrawData = value ?? ""; OnPropertyChanged(); }
        }

        private string _smearEraseData = "";
        /// <summary>擦除区域持久化快照（游程编码文本，同上）</summary>
        [StepConfig]
        public string SmearEraseData
        {
            get => _smearEraseData;
            set { _smearEraseData = value ?? ""; OnPropertyChanged(); }
        }

        /// <summary>清除涂擦（按钮命令）：置空触发绑定向控件回推 null（显示层同步清除）并持久化</summary>
        public ICommand ClearSmearCommand => _clearSmearCommand ??=
            new RelayCommand(_ => { SmearDraw = null; SmearErase = null; });
        private ICommand? _clearSmearCommand;

        /// <summary>
        /// 配置窗口打开时调用：从持久化快照重建涂擦区域（绑定建立后自动上屏）。
        /// 必须直接写字段后一次性通知，不能逐个走 SmearDraw/SmearErase setter——
        /// setter 内的 SyncSmearData 会把另一边的持久化快照用"尚未恢复的 null"覆盖成空，
        /// 表现为擦除区域重开配置即丢、下次确认时方案数据被静默抹掉
        /// </summary>
        public void RestoreSmear()
        {
            _smearDraw = DataToRegion(_smearDrawData);
            _smearErase = DataToRegion(_smearEraseData);
            OnPropertyChanged(nameof(SmearDraw));
            OnPropertyChanged(nameof(SmearErase));
        }

        /// <summary>涂擦变化后同步持久化快照（区域是运行态，字段是存储态，单向同步）</summary>
        private void SyncSmearData()
        {
            _smearDrawData = RegionToData(_smearDraw);
            _smearEraseData = RegionToData(_smearErase);
            OnPropertyChanged(nameof(SmearDrawData));
            OnPropertyChanged(nameof(SmearEraseData));
        }

        /// <summary>
        /// HRegion → 游程编码文本（"行,起列,止列;..."，get_region_runs 标准算子）。
        /// 超过 4KB 自动 GZip+base64（"gz:" 前缀标识）——大图大范围涂擦的游程文本会让方案文件膨胀；
        /// 无前缀的旧格式照常解析，两代格式互认
        /// </summary>
        private static string RegionToData(HRegion? region)
        {
            if (region == null || !region.IsInitialized()) return "";
            try
            {
                HOperatorSet.GetRegionRuns(region, out HTuple rows, out HTuple colStart, out HTuple colEnd);
                if (rows.Length == 0) return "";
                var sb = new StringBuilder();
                for (int i = 0; i < rows.Length; i++)
                    sb.Append(rows[i].I).Append(',').Append(colStart[i].I).Append(',').Append(colEnd[i].I).Append(';');
                var rle = sb.ToString();
                return rle.Length <= 4096 ? rle : "gz:" + Convert.ToBase64String(GZipCompress(rle));
            }
            catch
            {
                return ""; // 序列化失败按无涂擦处理，不阻断编辑
            }
        }

        /// <summary>游程编码文本 → HRegion（加载方案时重建区域；"gz:" 前缀先解压）</summary>
        private static HRegion? DataToRegion(string data)
        {
            if (string.IsNullOrEmpty(data)) return null;
            try
            {
                var text = data.StartsWith("gz:", StringComparison.Ordinal)
                    ? GZipDecompress(Convert.FromBase64String(data.Substring(3)))
                    : data;
                var rows = new List<int>();
                var c1 = new List<int>();
                var c2 = new List<int>();
                foreach (var seg in text.Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    var p = seg.Split(',');
                    if (p.Length != 3) continue;
                    rows.Add(int.Parse(p[0]));
                    c1.Add(int.Parse(p[1]));
                    c2.Add(int.Parse(p[2]));
                }
                if (rows.Count == 0) return null;
                HOperatorSet.GenRegionRuns(out HObject obj,
                    new HTuple(rows.ToArray()), new HTuple(c1.ToArray()), new HTuple(c2.ToArray()));
                return new HRegion(obj);
            }
            catch
            {
                return null; // 数据损坏时静默降级为无涂擦
            }
        }

        private static byte[] GZipCompress(string text)
        {
            using var src = new MemoryStream(Encoding.UTF8.GetBytes(text));
            using var dst = new MemoryStream();
            using (var gz = new GZipStream(dst, CompressionLevel.Fastest))
                src.CopyTo(gz);
            return dst.ToArray();
        }

        private static string GZipDecompress(byte[] bytes)
        {
            using var src = new MemoryStream(bytes);
            using var gz = new GZipStream(src, CompressionMode.Decompress);
            using var dst = new MemoryStream();
            gz.CopyTo(dst);
            return Encoding.UTF8.GetString(dst.ToArray());
        }

        #endregion

        /// <summary>
        /// 基于底图与合并区域构建掩膜与抠图
        /// </summary>
        public HImage? BuildMaskAndResult(HImage src, HRegion mergedRegion, bool invert, out HImage? mask, out string message)
        {
            mask = null;
            if (src == null || !src.IsInitialized())
            {
                message = "底图无效";
                return null;
            }

            src.GetImageSize(out int width, out int height);

            // 1. 生成二值掩膜 (正常: 内255 外0; 反转: 内0 外255)
            HOperatorSet.RegionToBin(mergedRegion, out HObject maskObj, invert ? 0 : 255, invert ? 255 : 0, width, height);
            mask = new HImage(maskObj);

            // 2. 抠图应用：采用矩阵乘法，缩放因子设为 1/255.0 杜绝截断白屏
            HOperatorSet.CountChannels(src, out HTuple channels);
            HObject maskForMul = maskObj;
            HObject? maskColor = null;
            HObject? maskedObj = null;

            try
            {
                if (channels.I == 3)
                {
                    HOperatorSet.Compose3(maskObj, maskObj, maskObj, out maskColor);
                    maskForMul = maskColor;
                }

                HOperatorSet.MultImage(src, maskForMul, out maskedObj, 1.0 / 255.0, 0.0);
            }
            finally
            {
                maskColor?.Dispose();
                maskObj.Dispose();
            }

            message = $"ROI 数量: {RoiList.Count}" + (invert ? "（排除模式）" : "");
            return new HImage(maskedObj);
        }

        public static HRegion? BuildRegion(RoiItem roi)
        {
            if (roi?.Params == null || roi.Params.Length == 0) return null;
            try
            {
                var region = new HRegion();
                switch (roi.ShapeType)
                {
                    case DrawShapeType.Rectangle when roi.Params.Length >= 5:
                        // 可旋转矩形：中心(row,col) + 角度phi + 半长(length1/length2)
                        region.GenRectangle2(roi.Params[0], roi.Params[1], roi.Params[2], roi.Params[3], roi.Params[4]);
                        return region;

                    case DrawShapeType.Circle when roi.Params.Length >= 3:
                        region.GenCircle(roi.Params[0], roi.Params[1], roi.Params[2]);
                        return region;

                    case DrawShapeType.Ellipse when roi.Params.Length >= 5:
                        region.GenEllipse(roi.Params[0], roi.Params[1], roi.Params[2], roi.Params[3], roi.Params[4]);
                        return region;

                    default:
                        region.Dispose();
                        return null;
                }
            }
            catch
            {
                return null;
            }
        }
    }
}