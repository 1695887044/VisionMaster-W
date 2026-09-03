using Core.Events;
using Core.Halcon;
using Core.Halcon.Models;
using Core.Interfaces;
using HalconDotNet;
using Plugin.CreateRoi.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;

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
        private System.Collections.ObjectModel.ObservableCollection<RoiItem> _roiList = new();
        [StepConfig]
        public System.Collections.ObjectModel.ObservableCollection<RoiItem> RoiList
        {
            get => _roiList;
            set { _roiList = value ?? new(); OnPropertyChanged(); RebuildDynamicOutputs(); }
        }

        #endregion

        #region 输入输出端口

        public InputPort<HImage> SrcImage { get; } = new();

        public OutputPort<HRegion> RoiRegion { get; } = new();
        public OutputPort<HImage> RoiMask { get; } = new();
        public OutputPort<HImage> MaskedImage { get; } = new();
        public OutputPort<int> RoiCount { get; } = new();

        #endregion

        #region 视图属性

        private HImage? _previewImage;
        public HImage? PreviewImage
        {
            get => _previewImage;
            set { SetProperty(ref _previewImage, value); UpdateDisplayImage(); }
        }

        /// <summary>当前选中 ROI（列表/画布双向联动枢纽，视图 XAML 直接绑定）</summary>
        private RoiItem? _selectedRoi;
        public RoiItem? SelectedRoi
        {
            get => _selectedRoi;
            set
            {
                if (_selectedRoi != null) _selectedRoi.ParamEdited -= OnSelectedRoiParamEdited;
                SetProperty(ref _selectedRoi, value);
                if (_selectedRoi != null) _selectedRoi.ParamEdited += OnSelectedRoiParamEdited;
            }
        }

        /// <summary>选中 ROI 的参数经数值框编辑 → 请求视图同步画布并刷新掩膜</summary>
        public event Action<RoiItem>? RoiParamEditedRequested;

        private void OnSelectedRoiParamEdited(RoiItem roi) => RoiParamEditedRequested?.Invoke(roi);

        /// <summary>画布 ROI 被点选（视图桥接：控件 RoiSelected 事件 → 这里 → 列表绑定自动定位）</summary>
        public void SelectFromCanvas(string roiName) =>
            SelectedRoi = RoiList.FirstOrDefault(x => x.Name == roiName);

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

        #region 画布同步（视图桥接调用的业务方法）

        /// <summary>画布新建 ROI → 存入 RoiList（RoiItem 为单一数据源）</summary>
        public void SyncFromCanvasAdd(DrawingObjectInfo info)
        {
            if (info.HTuples == null || info.HTuples.Length == 0) return;
            RoiList.Add(new RoiItem
            {
                Name = info.RoiName,
                ShapeType = info.ShapeType,
                Params = info.HTuples.Select(t => t.D).ToArray()
            });
            OnRoiListChanged();
        }

        /// <summary>画布删除 ROI → 从 RoiList 移除</summary>
        public void SyncFromCanvasRemove(DrawingObjectInfo info)
        {
            var roi = RoiList.FirstOrDefault(x => x.Name == info.RoiName);
            if (roi == null) return;
            RoiList.Remove(roi);
            if (SelectedRoi == roi) SelectedRoi = null;
            OnRoiListChanged();
        }

        /// <summary>画布清空 → RoiList 清空</summary>
        public void SyncFromCanvasReset()
        {
            RoiList.Clear();
            SelectedRoi = null;
            OnRoiListChanged();
        }

        /// <summary>拖拽句柄修改 → 回写 Params；选中项的参数面板同步刷新</summary>
        public void UpdateRoiFromDrag(DrawingObjectInfo info)
        {
            if (info?.HTuples == null) return;
            var roi = RoiList.FirstOrDefault(x => x.Name == info.RoiName);
            if (roi == null) return;

            roi.Params = info.HTuples.Select(t => t.D).ToArray(); // setter 同长度替换 → entry.Value INPC 通知 → 面板数值刷新
            ScheduleMaskPreview();
        }

        /// <summary>删除当前选中 ROI（视图 Del 键/删除按钮桥接；画布 Remove 事件回环同步 RoiList）</summary>
        public void DeleteSelectedRoi() => SelectedRoi = null; // 视图按 SelectedRoi 找画布对象移除

        /// <summary>RoiList 增删后的统一后处理：重建动态端口 + 掩膜联动</summary>
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

        /// <summary>配置实例释放：掩膜预览图与防抖计时器</summary>
        public override void Dispose()
        {
            _maskDebounce.Stop();
            _maskPreviewImage?.Dispose();
            _maskPreviewImage = null;
            base.Dispose();
        }

        public override void RunAlgorithm(IExecutionContext context)
        {
            var src = SrcImage.ActualValue;
            if (src == null || !src.IsInitialized())
            {
                Success.Value = false;
                ErrorMessage.Value = "输入图像为空或未初始化";
                return;
            }

            // 1. 生成并合并所有 ROI 区域（确保空区域句柄被正确初始化）
            HRegion merged = BuildMergedRegion();

            // 2. 安全清理上一次输出端口的旧对象（杜绝显存/内存堆积）
            DisposeOldOutputs();

            // 3. 赋值区域与数量
            RoiRegion.Value = merged;
            RoiCount.Value = RoiList.Count;

            // 4. 生成二值掩膜与黑底裁剪图
            var masked = BuildMaskAndResult(src, merged, MaskInvert, out var mask, out string maskMsg);
            RoiMask.Value = mask;
            MaskedImage.Value = masked;

            // 5. 视图与发布
            PreviewImage = src;
            this.PublishPreview(src, DisplayViewIndex);

            // 6. 动态输出端口填值：每个 ROI 裁剪一张图，按端口名 Crop_{ROI名} 输出
            for (int i = 0; i < RoiList.Count && i < _dynamicPortNames.Count; i++)
            {
                var roi = RoiList[i];
                var portName = _dynamicPortNames[i];
                using var region = BuildRegion(roi);
                if (region == null || !Outputs.TryGetValue(portName, out var port)) continue;

                try
                {
                    HOperatorSet.ReduceDomain(src, region, out HObject cropped);
                    HOperatorSet.CropDomain(cropped, out HObject croppedImg);
                    cropped.Dispose();
                    ((OutputPort<HImage>)port).Value = new HImage(croppedImg);
                    croppedImg.Dispose();
                }
                catch { /* 单个 ROI 裁剪失败不阻断整体 */ }
            }

            Success.Value = true;
            ErrorMessage.Value = maskMsg;
        }

        private void DisposeOldOutputs()
        {
            try { RoiRegion.TypedValue?.Dispose(); } catch { }
            try { RoiMask.TypedValue?.Dispose(); } catch { }
            try { MaskedImage.TypedValue?.Dispose(); } catch { }
            // 动态端口的旧值（HImage）也清理
            foreach (var name in _dynamicPortNames)
            {
                if (Outputs.TryGetValue(name, out var port) && port is OutputPort<HImage> imgPort)
                    try { imgPort.TypedValue?.Dispose(); } catch { }
            }
        }

        /// <summary>动态端口名缓存（DisposeOldOutputs 清理用）</summary>
        private readonly List<string> _dynamicPortNames = new();

        /// <summary>
        /// 按 RoiList 重建动态输出端口（Crop_{ROI名}），并同步定义快照（名字+类型）到 StepData
        /// - 配置实例：RoiList 变化时调用（setter + 视图 CollectionChanged）
        /// - 编译实例：FlowCompiler 在 ApplyConfigValues 后调用（从存盘快照恢复端口供接线）
        /// </summary>
        public void RebuildDynamicOutputs()
        {
            ClearDynamicOutputs();
            _dynamicPortNames.Clear();
            var snapshot = new List<DynamicPortInfo>();

            foreach (var roi in RoiList)
            {
                string portName = $"Crop_{roi.Name}";
                AddDynamicOutput(new OutputPort<HImage>(portName));
                _dynamicPortNames.Add(portName);
                snapshot.Add(new DynamicPortInfo
                {
                    Name = portName,
                    DataTypeName = typeof(HImage).AssemblyQualifiedName,
                    Description = $"ROI '{roi.Name}' 的裁剪图"
                });
            }

            // 同步快照到 StepData（供编译器和绑定界面读取，不依赖配置实例存活）
            if (StepData != null)
            {
                StepData.OutputPortDefinitions = snapshot;
            }
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

        #region 画笔涂擦集成（配置窗口桥接）

        private HRegion? _smearDraw;
        private HRegion? _smearErase;

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

        /// <summary>
        /// 配置窗口涂擦笔画结束后调用：替换涂/擦区域副本并刷新掩膜预览
        /// 区域由插件持有副本，窗口关闭后运行时仍可用
        /// </summary>
        public void UpdateSmear(HRegion? draw, HRegion? erase)
        {
            _smearDraw?.Dispose(); _smearDraw = draw;
            _smearErase?.Dispose(); _smearErase = erase;
            SyncSmearData();
            if (IsMaskPreview) RefreshMaskPreview();
        }

        /// <summary>清除涂擦（列表"清除涂抹"按钮调用）</summary>
        public void ClearSmear()
        {
            _smearDraw?.Dispose(); _smearDraw = null;
            _smearErase?.Dispose(); _smearErase = null;
            SyncSmearData();
            if (IsMaskPreview) RefreshMaskPreview();
        }

        /// <summary>导出插件持有的涂擦区域副本（配置窗口恢复显示用，调用方负责 Dispose）</summary>
        public (HRegion?, HRegion?) CopySmearRegions()
        {
            HRegion? d = _smearDraw != null && _smearDraw.IsInitialized() ? new HRegion(_smearDraw) : null;
            HRegion? e = _smearErase != null && _smearErase.IsInitialized() ? new HRegion(_smearErase) : null;
            return (d, e);
        }

        /// <summary>配置窗口打开时调用：从持久化快照重建涂擦区域</summary>
        public void RestoreSmear()
        {
            _smearDraw?.Dispose(); _smearDraw = DataToRegion(_smearDrawData);
            _smearErase?.Dispose(); _smearErase = DataToRegion(_smearEraseData);
        }

        /// <summary>涂擦变化后同步持久化快照（区域是运行态，字段是存储态，单向同步）</summary>
        private void SyncSmearData()
        {
            _smearDrawData = RegionToData(_smearDraw);
            _smearEraseData = RegionToData(_smearErase);
            OnPropertyChanged(nameof(SmearDrawData));
            OnPropertyChanged(nameof(SmearEraseData));
        }

        /// <summary>HRegion → 游程编码文本（"行,起列,止列;..."，get_region_runs 标准算子）</summary>
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
                return sb.ToString();
            }
            catch
            {
                return ""; // 序列化失败按无涂擦处理，不阻断编辑
            }
        }

        /// <summary>游程编码文本 → HRegion（加载方案时 gen_region_runs 重建区域）</summary>
        private static HRegion? DataToRegion(string data)
        {
            if (string.IsNullOrEmpty(data)) return null;
            try
            {
                var rows = new List<int>();
                var c1 = new List<int>();
                var c2 = new List<int>();
                foreach (var seg in data.Split(';', StringSplitOptions.RemoveEmptyEntries))
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