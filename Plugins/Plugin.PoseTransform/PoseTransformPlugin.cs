using Core.Interfaces;
using HalconDotNet;
using Microsoft.Win32;
using Prism.Commands;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace Plugin.PoseTransform
{
    /// <summary>
    /// 坐标变换插件（Plugin 与配置 ViewModel 合一）。
    ///
    /// 两个模式、一段共同纪律
    /// ---------
    /// · **位姿跟随**：基准 ROI（模板坐标系）× 当前位姿 → 当前坐标系 ROI；
    ///   接下游 BlobDetect.MaskRegion，也把"定位 → 测量"的桥补上（可选输出对齐图）。
    /// · **像素 ↔ 机械**：接「标定」插件的 <see cref="CalibrationTransform"/>，
    ///   把匹配的 Row/Col（+角度）换成机械 X/Y/θ——引导/纠偏的最后一公里；含反向回显自校验。
    ///
    /// 纪律：失配（未接标定 / 当量标定当九点用 / 图像尺寸不符 / 位姿 NaN 或全 0）**必须明确失败**，
    /// 绝不用旧标定或静默恒等变换糊过去；失败时输出端口保持开轮清零值（下游读不到上一轮残留）。
    ///
    /// 与 Matching.AlignedImage 的分工（方案 §二·1，界面提示也写了）：
    /// AlignedImage = 把目标**重采样**成标准位姿的新图（适合"内部 ROI 固定"的算子）；
    /// 本插件跟随 = **保留原图坐标**，只变换 ROI/点（适合 MaskRegion、机器人引导、多特征测量）。
    /// </summary>
    [Display(
        Name = "坐标变换",
        GroupName = "定位",
        Description = "位姿跟随与像素↔机械换算：把定位位姿变成下游能用的跟随 ROI 与机械坐标",
        ShortName = "\uf047"
    )]
    [ParallelSafe] // 二期真并行：chun shijue jisuan
    public partial class PoseTransformPlugin : VisionPluginBase, IPluginCustomViewProvider
    {
        #region 输入端口

        /// <summary>参考图（可选）：①ToMechanical 的图像尺寸失配校验 ②FollowRoi 的输出对齐图</summary>
        public InputPort<HImage> SrcImage { get; } = new(
            "SrcImage",
            description: "参考图（可选）：尺寸失配校验 / 输出对齐图；不接也能工作"
        )
        { IsRequired = false };

        /// <summary>
        /// 基准 ROI（FollowRoi 必填）：接 CreateRoi 的 MaskRegion——**画在模板图上的那个 ROI**。
        /// 必填性按模式判定（ToMechanical 不要求），所以端口声明为 false、运行时把关。
        /// </summary>
        public InputPort<HRegion> BaseRegion { get; } = new(
            "BaseRegion",
            description: "基准 ROI（位姿跟随必填）：接 ROI 插件的 MaskRegion，画在模板图上"
        )
        { IsRequired = false };

        /// <summary>模板参考点 Row：模板坐标系里"位姿原点"对应的像素位置（默认由基准 ROI 中心派生）</summary>
        public InputPort<double> TemplateRefRow { get; } = new(
            "TemplateRefRow", 0, "模板参考点 Row（模板坐标系里的位姿原点）")
        { IsRequired = false };

        /// <summary>模板参考点 Col</summary>
        public InputPort<double> TemplateRefCol { get; } = new(
            "TemplateRefCol", 0, "模板参考点 Col（模板坐标系里的位姿原点）")
        { IsRequired = false };

        /// <summary>当前位姿 Row（接 Matching.Row）</summary>
        public InputPort<double> PoseRow { get; } = new("PoseRow", 0, "当前位姿 Row（接模板匹配 Row）")
        { IsRequired = false };

        /// <summary>当前位姿 Col（接 Matching.Column）</summary>
        public InputPort<double> PoseCol { get; } = new("PoseCol", 0, "当前位姿 Col（接模板匹配 Column）")
        { IsRequired = false };

        /// <summary>当前位姿角度（接 Matching.Angle；**单位：度**）</summary>
        public InputPort<double> PoseAngle { get; } = new("PoseAngle", 0, "当前位姿角度（度，接模板匹配 Angle）")
        { IsRequired = false };

        /// <summary>标定（ToMechanical 必填）：接「标定」插件的 Transform 输出（不允许手填矩阵——单一事实源）</summary>
        public InputPort<CalibrationTransform?> Transform { get; } = new(
            "Transform",
            null,
            "标定（接「标定」插件的 Transform 输出）")
        { IsRequired = false };

        /// <summary>
        /// 图像来源相机序列号（可选）：接「图像采集」的 SourceSerial 输出。
        /// 与标定自带的 <see cref="CalibrationTransform.CameraSerial"/> 不符时明确失败——
        /// 多相机共线时"拿错相机的标定"是静默错位的经典来源，这里让它当场暴露。
        /// 两端任一为空视为"不检查"（离线/单相机场景不打扰）。
        /// </summary>
        public InputPort<string> SourceSerial { get; } = new(
            "SourceSerial",
            "",
            "图像相机序列号（可选，接图像采集 SourceSerial；与标定记录的相机不符则明确失败）")
        { IsRequired = false };

        /// <summary>待换算的像素点 Row（接 Matching.Row，即目标中心）</summary>
        public InputPort<double> PixelPointRow { get; } = new(
            "PixelPointRow", 0, "待换算的像素点 Row（接模板匹配 Row）")
        { IsRequired = false };

        /// <summary>待换算的像素点 Col</summary>
        public InputPort<double> PixelPointCol { get; } = new(
            "PixelPointCol", 0, "待换算的像素点 Col（接模板匹配 Column）")
        { IsRequired = false };

        /// <summary>像素系角度（可选）：接了（或有非零值）才输出 MechanicalAngle</summary>
        public InputPort<double> PixelAngle { get; } = new(
            "PixelAngle", 0, "像素系角度（度，可选）：接了才输出 MechanicalAngle")
        { IsRequired = false };

        #endregion

        #region 输出端口

        /// <summary>当前坐标系下的 ROI（接 BlobDetect.MaskRegion）</summary>
        public OutputPort<HRegion> FollowedRegion { get; } = new(
            "FollowedRegion", "位姿跟随后的 ROI（当前坐标系）");

        /// <summary>可选：把当前图反向重采样回模板参考位姿（"对齐图"，EmitFollowedImage=true 时输出）</summary>
        public OutputPort<HImage> FollowedImage { get; } = new(
            "FollowedImage", "按位姿对齐回模板参考位姿的图（可选输出）");

        /// <summary>机械坐标 X（mm）</summary>
        public OutputPort<double> MechanicalX { get; } = new("MechanicalX", "机械坐标 X（mm）");

        /// <summary>机械坐标 Y（mm）</summary>
        public OutputPort<double> MechanicalY { get; } = new("MechanicalY", "机械坐标 Y（mm）");

        /// <summary>机械系角度（度）：像素角度由标定矩阵线性部分映射而来</summary>
        public OutputPort<double> MechanicalAngle { get; } = new("MechanicalAngle", "机械系角度（度）");

        /// <summary>反向回显 Row（自校验：把机械坐标再算回像素；偏差应 &lt; 1e-6px）</summary>
        public OutputPort<double> PixelEchoRow { get; } = new("PixelEchoRow", "反向回显 Row（自校验）");

        /// <summary>反向回显 Col</summary>
        public OutputPort<double> PixelEchoCol { get; } = new("PixelEchoCol", "反向回显 Col（自校验）");

        #endregion

        #region 配置项（[StepConfig]：随步骤落盘；纯配置，不进端口）

        /// <summary>模式（切换只影响界面与执行分支；不会清掉另一种模式已填的参数）</summary>
        [StepConfig, DefaultValue(TransformMode.FollowRoi)]
        public partial TransformMode Mode { get; set; }

        partial void OnModeChanged(TransformMode value) => RefreshConfigState();

        /// <summary>是否输出对齐图（默认关：少一次重采样、少一次内存拷贝；需要时才付代价）</summary>
        [StepConfig]
        public partial bool EmitFollowedImage { get; set; }

        partial void OnEmitFollowedImageChanged(bool value) => RefreshConfigState();

        /// <summary>true（默认）= 模板参考点由基准 ROI 中心派生，避免手填两张数；false = 用 TemplateRefRow/Col 端口值</summary>
        [StepConfig, DefaultValue(true)]
        public partial bool TemplateRefFromRegionCenter { get; set; }

        partial void OnTemplateRefFromRegionCenterChanged(bool value) => RefreshConfigState();

        /// <summary>
        /// 配置态预览图路径（**不参与运行**：运行只用端口）。
        /// 方案未指定预览图来源，取与「模板匹配」PreviewImagePath 同一模式（方案 §五 的"预览区（有图时）"）。
        /// </summary>
        [StepConfig, DefaultValue("")]
        public partial string PreviewImagePath { get; set; }

        partial void OnPreviewImagePathChanged(string value) => EnsurePreviewLoaded();

        #endregion

        #region 界面状态（不持久化）

        private HImage? _displayImage;
        /// <summary>叠加预览（底图 + 基准 ROI 绿 + 跟随 ROI 橙 + 参考点十字黄）；绑定 ImageEdit</summary>
        public HImage? DisplayImage
        {
            get => _displayImage;
            set
            {
                var old = _displayImage;
                if (ReferenceEquals(old, value)) return;
                if (SetProperty(ref _displayImage, value))
                    old?.Dispose();
            }
        }

        private string _statusMessage = string.Empty;
        /// <summary>状态消息（请用 SetStatus 写入：消息与级别必须成对更新）</summary>
        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetProperty(ref _statusMessage, value);
        }

        private StatusLevel _statusLevel = StatusLevel.Info;
        /// <summary>状态级别（驱动信息栏颜色）</summary>
        public StatusLevel StatusLevel
        {
            get => _statusLevel;
            private set => SetProperty(ref _statusLevel, value);
        }

        private string _calibrationInfoText = string.Empty;
        /// <summary>标定信息只读块（ToMechanical）：来源/相机/尺寸/时间/残差；失配时红字</summary>
        public string CalibrationInfoText
        {
            get => _calibrationInfoText;
            private set => SetProperty(ref _calibrationInfoText, value);
        }

        private StatusLevel _calibrationInfoLevel = StatusLevel.Info;
        /// <summary>标定信息级别（驱动只读块颜色）</summary>
        public StatusLevel CalibrationInfoLevel
        {
            get => _calibrationInfoLevel;
            private set => SetProperty(ref _calibrationInfoLevel, value);
        }

        private string _followedInfoText = string.Empty;
        /// <summary>跟随自检一行（配置态）：基准态自洽/面积变化；空 = 暂不可算</summary>
        public string FollowedInfoText
        {
            get => _followedInfoText;
            private set => SetProperty(ref _followedInfoText, value);
        }

        #endregion

        #region 命令

        /// <summary>浏览选择预览图（配置态）</summary>
        public DelegateCommand BrowsePreviewCommand { get; }

        /// <summary>取基准 ROI 中心填入模板参考点（一键填，避免手填两张数）</summary>
        public DelegateCommand TakeRefFromRegionCenterCommand { get; }

        #endregion

        #region 私有状态

        /// <summary>配置态预览底图（本插件持有；DisplayImage 是它的叠加副本）</summary>
        private HImage? _previewImage;

        /// <summary>已载入的预览图路径（与 PreviewImagePath 比对，避免重复读盘）</summary>
        private string _loadedPreviewPath = string.Empty;

        /// <summary>预览载入轮次号：过期结果只释放不赋值（同「图像采集」「标定」的守卫）</summary>
        private int _previewLoadId;

        /// <summary>配置视图是否挂接（决定试运行后要不要刷叠加图，避免无视图时白干活）</summary>
        private bool _viewAttached;

        /// <summary>配置实例是否已释放</summary>
        private volatile bool _disposed;

        #endregion

        public PoseTransformPlugin()
        {
            BrowsePreviewCommand = new DelegateCommand(BrowsePreview);
            TakeRefFromRegionCenterCommand = new DelegateCommand(TakeRefFromRegionCenter);

            // 端口/配置变化 → 配置态三联刷新（状态栏 / 标定信息 / 叠加预览）
            BaseRegion.ValueChanged += (_, _) => RefreshConfigState();
            TemplateRefRow.ValueChanged += (_, _) => RefreshConfigState();
            TemplateRefCol.ValueChanged += (_, _) => RefreshConfigState();
            PoseRow.ValueChanged += (_, _) => RefreshConfigState();
            PoseCol.ValueChanged += (_, _) => RefreshConfigState();
            PoseAngle.ValueChanged += (_, _) => RefreshConfigState();
            Transform.ValueChanged += (_, _) => RefreshConfigState();
            SrcImage.ValueChanged += (_, _) => RefreshConfigState();
            SourceSerial.ValueChanged += (_, _) => RefreshConfigState();
        }

        #region IPluginCustomViewProvider

        public object GetConfigView(IStepConfigData stepData)
        {
            return new PoseTransformView(stepData, this);
        }

        #endregion

        #region 配置视图挂接（叠加预览只在视图开着时刷新）

        /// <summary>视图挂接（构造视图时调用）：立刻刷新一次配置态显示</summary>
        public void OnViewAttached()
        {
            _viewAttached = true;
            RefreshConfigState();
        }

        /// <summary>视图卸载（对话框关闭）：停止配置态刷新（运行期的叠加重算不再发生）</summary>
        public void OnViewDetached()
        {
            _viewAttached = false;
        }

        #endregion

        #region 配置生命周期

        public override void Initialize(IStepConfigData stepData)
        {
            _disposed = false;
            base.Initialize(stepData);

            EnsurePreviewLoaded();
            RefreshConfigState();
        }

        public override void Dispose()
        {
            _viewAttached = false;
            _disposed = true;
            _previewLoadId++;

            DisplayImage = null;      // setter 释放旧叠加图
            SetPreviewImage(null);    // 释放预览底图

            base.Dispose();
        }

        #endregion

        #region 执行核心

        /// <summary>
        /// 运行入口：按模式分派。
        /// 开轮先把 double 端口清零——基类只管 IDisposable 输出（HImage/HRegion），
        /// double 端口的上一轮值会原样留下，失败时下游会读到脏数据（同 BlobDetect 的镜像 bug 教训）。
        /// </summary>
        public override void RunAlgorithm(IExecutionContext context)
        {
            // 默认成功 + 清空上一轮错误：与基类 Execute 的预置保持一致，
            // 也保证"绕过 Execute 直接调用 RunAlgorithm"（断言 / 试运行类框架）语义一致——
            // 否则上一轮的 ErrorMessage 会被当成本轮错误、Success 会停在上一轮的 false（假红/假绿都出现过）。
            Success.Value = true;
            ErrorMessage.Value = string.Empty;

            MechanicalX.Value = 0;
            MechanicalY.Value = 0;
            MechanicalAngle.Value = 0;
            PixelEchoRow.Value = 0;
            PixelEchoCol.Value = 0;

            switch (Mode)
            {
                case TransformMode.FollowRoi:
                    RunFollowRoi(context);
                    break;

                case TransformMode.ToMechanical:
                    RunToMechanical(context);
                    break;

                default:
                    Fail($"未知模式：{Mode}（请重新选择模式）");
                    break;
            }

            // 配置对话框开着时（试运行走的同一个实例）：叠加图跟着刷新
            if (_viewAttached)
                PostToUI(RefreshOverlay);
        }

        /// <summary>位姿跟随：基准 ROI × 当前位姿 → 跟随 ROI（可选输出对齐图）。</summary>
        private void RunFollowRoi(IExecutionContext context)
        {
            var baseRegion = BaseRegion.ActualValue;
            if (baseRegion == null || !baseRegion.IsInitialized())
            {
                Fail("基准 ROI 为空：请把 ROI 插件的 MaskRegion 接过来（并在模板图上画好基准）");
                return;
            }

            if (!TryRegionArea(baseRegion, out double baseArea, out _, out _) || baseArea <= 0)
            {
                Fail("基准 ROI 是空区域（面积为 0）：请检查基准 ROI 的绘制（要画在模板图上）");
                return;
            }

            // 模板参考点：默认由基准 ROI 中心派生；关闭派生才用端口值
            double refRow, refCol;
            if (TemplateRefFromRegionCenter)
            {
                TryRegionArea(baseRegion, out _, out refRow, out refCol);
            }
            else
            {
                refRow = TemplateRefRow.ActualValue;
                refCol = TemplateRefCol.ActualValue;
                if (!double.IsFinite(refRow) || !double.IsFinite(refCol))
                {
                    Fail("模板参考点无效（NaN/Inf）：请点「取基准 ROI 中心」或手工填写");
                    return;
                }
                if (refRow == 0 && refCol == 0)
                {
                    Fail("模板参考点为 (0,0)：请点「取基准 ROI 中心」或手工填写实际参考点");
                    return;
                }
            }

            double poseRow = PoseRow.ActualValue;
            double poseCol = PoseCol.ActualValue;
            double poseAngle = PoseAngle.ActualValue;
            if (!PoseTransformMath.IsValidPose(poseRow, poseCol, poseAngle, out string? poseError))
            {
                Fail(poseError!);
                return;
            }

            // 先把两个产物都算出来，全部成功才写输出端口——
            // 中途失败时端口保持开轮清零值（下游不读半成品，纪律同"失败不留残留"）
            HRegion? followed = null;
            HImage? followedImage = null;
            try
            {
                followed = PoseTransformHalcon.FollowRegion(baseRegion, refRow, refCol, poseRow, poseCol, poseAngle);

                // 可选：对齐图（把当前图反向重采样回模板参考位姿）
                if (EmitFollowedImage)
                {
                    var src = SrcImage.ActualValue;
                    if (src == null || !src.IsInitialized())
                    {
                        context.Logger?.Info($"{InstanceName} 未接 SrcImage：跳过输出对齐图");
                    }
                    else
                    {
                        followedImage = PoseTransformHalcon.FollowImageBack(src, refRow, refCol, poseRow, poseCol, poseAngle);
                    }
                }
            }
            catch (Exception ex)
            {
                followed?.Dispose();
                followedImage?.Dispose();
                Fail($"位姿跟随失败：{ex.Message}（请检查基准 ROI、SrcImage 与位姿是否有效）");
                context.Logger?.Error($"{InstanceName} {ErrorMessage.Value}");
                return;
            }

            FollowedRegion.TypedValue = followed!;   // 所有权移交端口，基类下一轮/Dispose 回收
            if (followedImage != null)
                FollowedImage.TypedValue = followedImage;

            TryRegionArea(followed!, out double followedArea, out _, out _);
            double areaChange = (followedArea - baseArea) / baseArea * 100.0;
            context.Logger?.Info(
                $"{InstanceName} 位姿跟随：基准面积 {baseArea:0.#}px² → 跟随面积 {followedArea:0.#}px²"
                + $"（{areaChange:+0.##;-0.##;0}%）");
        }

        /// <summary>像素 ↔ 机械：四类失配必须明确失败，成功时输出机械坐标 + 反向回显自校验。</summary>
        private void RunToMechanical(IExecutionContext context)
        {
            var t = Transform.ActualValue;

            // 失配第一/二类：未接标定、只有像素当量的标定当机械坐标用
            if (!PoseTransformMath.CheckCalibrationUsable(t, out string? calibError))
            {
                Fail(calibError!);
                return;
            }

            // 失配第三类：图像尺寸不符（接了 SrcImage 才可查；不接就不查——方案 §四）
            var src = SrcImage.ActualValue;
            if (src != null && src.IsInitialized())
            {
                try
                {
                    src.GetImageSize(out int width, out int height);
                    if (!PoseTransformMath.CheckImageSizeMatch(t!, width, height, out string? sizeError))
                    {
                        Fail(sizeError!);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Fail($"读取图像尺寸失败：{ex.Message}（SrcImage 是否有效？）");
                    return;
                }
            }

            // 失配第四类：相机身份不符（两端都填了序列号才查——离线/单相机场景不打扰）
            string imageSerial = (SourceSerial.ActualValue ?? string.Empty).Trim();
            string calibSerial = (t!.CameraSerial ?? string.Empty).Trim();
            if (imageSerial.Length > 0 && calibSerial.Length > 0 &&
                !string.Equals(imageSerial, calibSerial, StringComparison.OrdinalIgnoreCase))
            {
                Fail($"相机身份不符：这份标定属于「{calibSerial}」，但当前图像来自「{imageSerial}」；"
                    + "请换用该相机的标定文件（或检查 SourceSerial 接线）");
                return;
            }

            double pixelRow = PixelPointRow.ActualValue;
            double pixelCol = PixelPointCol.ActualValue;
            if (!PoseTransformMath.IsUsablePixelPoint(pixelRow, pixelCol, out string? pixelError))
            {
                Fail(pixelError!);
                return;
            }

            if (!PoseTransformMath.TryMapPixelToMechanical(t, pixelRow, pixelCol, out double x, out double y, out string? mapError))
            {
                Fail(mapError!);
                return;
            }

            if (!PoseTransformMath.TryMapMechanicalToPixel(t, x, y, out double echoRow, out double echoCol, out string? echoError))
            {
                Fail(echoError!);
                return;
            }

            // 反向回显自校验：正反两次换算必须回到原点（矩阵损坏/实现错的当场露馅）
            double echoDiff = Math.Max(Math.Abs(echoRow - pixelRow), Math.Abs(echoCol - pixelCol));
            if (echoDiff > 1e-6)
            {
                Fail($"反向回显不一致（偏差 {echoDiff:0.###}px）：标定矩阵可能已损坏，请重新标定");
                return;
            }

            // 角度：接了（或有非零值/NaN）才换算输出；NaN 走"无效"失败而不是静默跳过
            double pixelAngle = PixelAngle.ActualValue;
            bool hasAngle = PixelAngle.LinkedSource != null || pixelAngle != 0.0 || !double.IsFinite(pixelAngle);
            double? mechanicalAngle = null;
            if (hasAngle)
            {
                if (!PoseTransformMath.TryPixelAngleToMechanical(t, pixelAngle, pixelRow, pixelCol, out double ma, out string? angleError))
                {
                    Fail(angleError!);
                    return;
                }
                mechanicalAngle = ma;
            }

            // 统一写出所有端口（只有在所有检查都通过后才会到这里）
            MechanicalX.Value = x;
            MechanicalY.Value = y;
            PixelEchoRow.Value = echoRow;
            PixelEchoCol.Value = echoCol;
            if (mechanicalAngle.HasValue)
                MechanicalAngle.Value = mechanicalAngle.Value;

            context.Logger?.Info(
                $"{InstanceName} 像素 ({pixelRow:0.##}, {pixelCol:0.##}) → 机械 ({x:0.###}, {y:0.###})mm"
                + (hasAngle ? $"，机械角 {MechanicalAngle.Value:0.###}°" : "")
                + (t!.Kind is CalibrationKind.Perspective or CalibrationKind.Mesh
                    ? $"（{CalibrationKindText(t.Kind)}）" : ""));
        }

        #endregion

        #region 配置态：状态 / 标定信息 / 叠加预览

        /// <summary>配置态三联刷新（状态栏 + 标定信息 + 叠加图）；可从任意线程调用。</summary>
        private void RefreshConfigState()
        {
            if (_disposed) return;
            PostToUI(() =>
            {
                if (_disposed) return;
                RefreshStatus();
                RefreshCalibrationInfo();
                RefreshOverlay();
            });
        }

        /// <summary>状态栏一行：当前模式"还差什么 / 已就绪"。</summary>
        private void RefreshStatus()
        {
            if (Mode == TransformMode.ToMechanical)
            {
                var t = Transform.ActualValue;
                if (t == null)
                {
                    SetStatus(PoseTransformMath.MissingCalibrationMessage, StatusLevel.Warning);
                    return;
                }
                if (!PoseTransformMath.CheckCalibrationUsable(t, out string? calibError))
                {
                    SetStatus(calibError, StatusLevel.Warning);
                    return;
                }
                SetStatus($"就绪：标定已接入（{CalibrationKindText(t.Kind)}），运行输出机械坐标（含反向回显自校验）", StatusLevel.Info);
                return;
            }

            // FollowRoi
            var region = BaseRegion.ActualValue;
            if (region == null || !region.IsInitialized())
            {
                SetStatus("基准 ROI 未接：请把 ROI 插件的 MaskRegion 接过来（画在模板图上）", StatusLevel.Warning);
                return;
            }
            if (!PoseTransformMath.IsValidPose(PoseRow.ActualValue, PoseCol.ActualValue, PoseAngle.ActualValue, out string? poseError))
            {
                SetStatus($"位姿未就绪：{poseError}", StatusLevel.Warning);
                return;
            }
            SetStatus("就绪：跟随参数完整（试运行可看叠加效果）", StatusLevel.Info);
        }

        /// <summary>标定信息只读块（ToMechanical）：来源/相机/尺寸/时间/残差；与预览图尺寸不符时红字。</summary>
        private void RefreshCalibrationInfo()
        {
            if (Mode != TransformMode.ToMechanical)
            {
                CalibrationInfoText = string.Empty;
                CalibrationInfoLevel = StatusLevel.Info;
                return;
            }

            var t = Transform.ActualValue;
            if (t == null)
            {
                CalibrationInfoText = PoseTransformMath.MissingCalibrationMessage;
                CalibrationInfoLevel = StatusLevel.Error;
                return;
            }
            if (!PoseTransformMath.CheckCalibrationUsable(t, out string? calibError))
            {
                CalibrationInfoText = calibError ?? string.Empty;
                CalibrationInfoLevel = StatusLevel.Error;
                return;
            }

            string size = t.SourceImageWidth > 0
                ? $"{t.SourceImageWidth}×{t.SourceImageHeight}"
                : "未记录";
            string serial = string.IsNullOrWhiteSpace(t.CameraSerial) ? "（未填）" : t.CameraSerial;
            string tag = string.IsNullOrWhiteSpace(t.SourceTag) ? "（未填）" : t.SourceTag;

            CalibrationInfoText =
                $"类型 {CalibrationKindText(t.Kind)} ｜ 来源 {tag} ｜ 相机 {serial} ｜ 标定图 {size} ｜ 创建 {t.CreatedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm}"
                + $" ｜ 残差 RMS {t.ResidualRmsPx:0.###}px / 最大 {t.MaxResidualPx:0.###}px";

            // 与已载入的预览图比对尺寸（运行期由 SrcImage 在 RunToMechanical 里把关）
            if (t.SourceImageWidth > 0 && _previewImage != null && _previewImage.IsInitialized())
            {
                _previewImage.GetImageSize(out int pw, out int ph);
                if (pw != t.SourceImageWidth || ph != t.SourceImageHeight)
                {
                    CalibrationInfoText = $"当前图 {pw}×{ph} ≠ 标定图 {t.SourceImageWidth}×{t.SourceImageHeight}——换分辨率/换相机后需重新标定。{CalibrationInfoText}";
                    CalibrationInfoLevel = StatusLevel.Error;
                    return;
                }
            }

            // 与「标定」插件默认阈值同口径（1px）：残差偏大给橙字提醒（阈值本身在标定侧把关）
            CalibrationInfoLevel = t.ResidualRmsPx > 1.0 ? StatusLevel.Warning : StatusLevel.Info;
        }

        /// <summary>
        /// 叠加预览：底图 + 基准 ROI（绿）+ 跟随 ROI（橙）+ 模板参考点十字（黄）。
        /// 底图优先用配置态载入的预览图，其次用已接的 SrcImage（"有图时"就画）；
        /// 跟随轮廓只在位姿/参考点都有效时画——画不出就只显示基准，不误导。
        /// </summary>
        private void RefreshOverlay()
        {
            if (_disposed) return;

            var src = (_previewImage != null && _previewImage.IsInitialized())
                ? _previewImage
                : SrcImage.ActualValue;
            if (src == null || !src.IsInitialized())
            {
                DisplayImage = null;
                FollowedInfoText = string.Empty;
                return;
            }

            var temp = new List<IDisposable>();
            try
            {
                // 1) 底图 → RGB（彩色叠加必须画在 3 通道图上；不动来源图，先拷/合一份）
                HOperatorSet.CountChannels(src, out HTuple channelTuple);
                int channels = channelTuple.Length > 0 ? channelTuple[0].I : 1;

                HObject current;
                if (channels >= 3)
                    HOperatorSet.CopyImage(src, out current);
                else
                    HOperatorSet.Compose3(src, src, src, out current);
                temp.Add(current);

                // 2) 基准 ROI（绿轮廓）+ 模板参考点（黄十字）
                var region = BaseRegion.ActualValue;
                bool hasRegion = region != null && region.IsInitialized()
                                 && TryRegionArea(region, out double baseArea, out _, out _) && baseArea > 0;

                double refRow = 0, refCol = 0;
                bool hasRef = hasRegion && TryGetTemplateRef(region!, out refRow, out refCol);

                if (hasRegion)
                {
                    HOperatorSet.GenContourRegionXld(region!, out HObject baseContour, "border");
                    temp.Add(baseContour);
                    current = PaintXldOn(current, baseContour, 0, 255, 0, temp);
                }

                // 3) 跟随 ROI（橙轮廓）：位姿 + 参考点都有效才画
                bool hasFollowed = false;
                if (hasRef
                    && PoseTransformMath.IsValidPose(PoseRow.ActualValue, PoseCol.ActualValue, PoseAngle.ActualValue, out _))
                {
                    try
                    {
                        using var followed = PoseTransformHalcon.FollowRegion(
                            region!, refRow, refCol,
                            PoseRow.ActualValue, PoseCol.ActualValue, PoseAngle.ActualValue);

                        HOperatorSet.GenContourRegionXld(followed, out HObject followedContour, "border");
                        temp.Add(followedContour);
                        current = PaintXldOn(current, followedContour, 255, 128, 0, temp);
                        hasFollowed = true;

                        TryRegionArea(followed, out double followedArea, out _, out _);
                        TryRegionArea(region!, out double baseArea2, out _, out _);
                        double change = baseArea2 > 0 ? (followedArea - baseArea2) / baseArea2 * 100.0 : 0;
                        FollowedInfoText = $"跟随面积 {followedArea:0.#}px²（相对基准 {change:+0.##;-0.##;0}%）";
                    }
                    catch (Exception ex)
                    {
                        FollowedInfoText = $"跟随预览失败：{ex.Message}";
                    }
                }
                else
                {
                    FollowedInfoText = hasRegion
                        ? "跟随 ROI 未画：位姿未就绪（给 PosRow/Col/Angle 一个有效值或试运行一次）"
                        : string.Empty;
                }

                // 4) 参考点十字（黄）画在最上层
                if (hasRef)
                {
                    HOperatorSet.GenCrossContourXld(out HObject cross, refRow, refCol, 24, 0);
                    temp.Add(cross);
                    current = PaintXldOn(current, cross, 255, 255, 0, temp);
                }

                // 5) 交给显示（DisplayImage 持有副本，temp 里的中间对象统一释放）
                DisplayImage = new HImage(current);

                if (!hasFollowed && !hasRegion)
                    SetStatus("预览已载入，但基准 ROI 未接：叠加图只有底图", StatusLevel.Warning);
            }
            catch (Exception ex)
            {
                SetStatus($"叠加预览失败：{ex.Message}", StatusLevel.Warning);
            }
            finally
            {
                foreach (var o in temp)
                {
                    try { o?.Dispose(); }
                    catch { /* 中间对象释放失败不阻断 */ }
                }
            }
        }

        /// <summary>模板参考点（配置态用）：派生开启时取基准 ROI 中心，否则取端口值；无效返回 false。</summary>
        private bool TryGetTemplateRef(HRegion region, out double refRow, out double refCol)
        {
            refRow = 0;
            refCol = 0;

            if (TemplateRefFromRegionCenter)
                return TryRegionArea(region, out _, out refRow, out refCol);

            refRow = TemplateRefRow.ActualValue;
            refCol = TemplateRefCol.ActualValue;
            return double.IsFinite(refRow) && double.IsFinite(refCol) && !(refRow == 0 && refCol == 0);
        }

        /// <summary>区域面积/中心（空区域/未初始化返回 false，不抛）。</summary>
        private static bool TryRegionArea(HRegion region, out double area, out double row, out double col)
        {
            area = 0;
            row = 0;
            col = 0;
            try
            {
                HOperatorSet.AreaCenter(region, out HTuple a, out HTuple r, out HTuple c);
                if (a.Length == 0 || r.Length == 0 || c.Length == 0)
                    return false;

                area = a[0].D;
                row = r[0].D;
                col = c[0].D;
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>把 XLD 画到图上（RGB 彩色），返回新图并登记中间对象。</summary>
        private static HObject PaintXldOn(HObject image, HObject xld, double r, double g, double b, List<IDisposable> temp)
        {
            HOperatorSet.PaintXld(xld, image, out HObject painted, new HTuple(r, g, b));
            temp.Add(painted);
            return painted;
        }

        /// <summary>标定类型的人话名（状态栏/信息块共用；未知类型原样显示）。</summary>
        private static string CalibrationKindText(CalibrationKind kind) => kind switch
        {
            CalibrationKind.PixelScale => "像素当量",
            CalibrationKind.NinePoint => "九点",
            CalibrationKind.Perspective => "透视",
            CalibrationKind.Mesh => "网格（分段仿射）",
            _ => kind.ToString()
        };

        private void SetStatus(string? message, StatusLevel level = StatusLevel.Info)
        {
            StatusMessage = message ?? string.Empty;
            StatusLevel = level;
        }

        #endregion

        #region 配置态：预览图载入

        private void BrowsePreview()
        {
            var dlg = new OpenFileDialog
            {
                Filter = "图像文件|*.bmp;*.jpg;*.jpeg;*.png;*.tif;*.tiff|所有文件|*.*",
                Title = "选择预览图（配置态叠加显示用；不参与运行）"
            };

            if (dlg.ShowDialog() == true)
                PreviewImagePath = dlg.FileName;   // setter → EnsurePreviewLoaded
        }

        private void TakeRefFromRegionCenter()
        {
            var region = BaseRegion.ActualValue;
            if (region == null || !region.IsInitialized() || !TryRegionArea(region, out double area, out double row, out double col) || area <= 0)
            {
                SetStatus("基准 ROI 还没接（或为空）：先在流程里把 ROI 插件的 MaskRegion 接到本步骤", StatusLevel.Warning);
                return;
            }

            TemplateRefRow.Value = row;
            TemplateRefCol.Value = col;
            SetStatus($"已取基准 ROI 中心（{row:0.##}, {col:0.##}）填入模板参考点", StatusLevel.Info);
        }

        /// <summary>路径与已载入的不一致就载图（手工输入/浏览都汇到这里）</summary>
        private void EnsurePreviewLoaded()
        {
            var path = PreviewImagePath;
            if (string.IsNullOrWhiteSpace(path))
            {
                _loadedPreviewPath = string.Empty;
                SetPreviewImage(null);
                DisplayImage = null;
                ++_previewLoadId; // 丢弃任何在途的预览载入，防止旧图"复活"
                return;
            }

            if (string.Equals(path, _loadedPreviewPath, StringComparison.OrdinalIgnoreCase))
                return;   // 已载入同一张，不重复读盘

            LoadPreview(path);
        }

        /// <summary>
        /// 载入预览图（后台读盘 + 回 UI 线程赋值）。
        /// 与「图像采集」「标定」同一套守卫：轮次号丢弃过期结果、_disposed 拒绝写已关闭窗口。
        /// </summary>
        private void LoadPreview(string path)
        {
            var loadId = ++_previewLoadId;

            if (!File.Exists(path))
            {
                _loadedPreviewPath = string.Empty;
                SetPreviewImage(null);
                DisplayImage = null;
                SetStatus($"预览图不存在: {path}", StatusLevel.Error);
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
                        _loadedPreviewPath = string.Empty;
                        SetPreviewImage(null);
                        DisplayImage = null;
                        SetStatus($"加载预览图失败: {error}", StatusLevel.Error);
                        return;
                    }

                    SetPreviewImage(loaded);
                    _loadedPreviewPath = path;
                    RefreshConfigState();
                });
            });
        }

        private void SetPreviewImage(HImage? image)
        {
            var old = _previewImage;
            _previewImage = image;
            old?.Dispose();
        }

        #endregion

        /// <summary>把动作切回 UI 线程（配置态异步读图/运行期叠加刷新的回程；与「图像采集」「标定」同一实现）</summary>
        private static void PostToUI(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
                action();
            else
                dispatcher.Invoke(action);
        }
    }
}
