using Core.Interfaces;
using HalconDotNet;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using UI.CustomControl;

namespace Plugin.ImageAlign
{
    /// <summary>
    /// 通用图像对齐：把当前图搬回基准帧，输出 AlignedImage / 正逆变换 / 有效区。
    ///
    /// v1 边界（评审 2026-10-27）：
    ///  · 模板位姿只**消费上游**（HomMat2D 或 PoseRow/Column/Angle），不自己跑模板匹配；
    ///  · 点对模式支持 2 点刚性 / 3 点仿射 / 4 点透视；手动四点必须正好 4 对，少于 4 点直接失败；
    ///  · 4 点透视无冗余、残差恒 0；要检测错点请填“验证点”（第 5 对点）；
    ///  · 不做判定：`IsOk` 只是可选阈值结论，不改变 `Success`；拦截交给下游；
    ///  · 不写全局变量、不做局部可变形（v2）。
    /// </summary>
    [Display(Name = "图像对齐", GroupName = "图像处理",
        Description = "2~4 点/手动四点/上游位姿 → 仿射/透视搬图，输出 AlignedImage、正逆变换与有效区",
        ShortName = "\uf0c4")]
    [ParallelSafe]
    public partial class ImageAlignPlugin : VisionPluginBase, IPluginCustomViewProvider
    {
        // ── 输入端口 ───────────────────────────────────────────────────────────

        /// <summary>当前图（待搬回基准帧）。必接。</summary>
        public InputPort<HImage> Image { get; } = new(
            "Image", description: "当前图（待搬回基准帧）。必接。")
        { IsRequired = true };

        /// <summary>基准图（可选）：只用于确定输出尺寸与有效区域，不参与像素比较。必须与 Image 同尺寸。</summary>
        public InputPort<HImage> ReferenceImage { get; } = new(
            "ReferenceImage", description: "基准图（可选）：只用于输出尺寸/有效区域；必须与 Image 同尺寸")
        { IsRequired = false };

        /// <summary>参考点（可选）：交错 [r1,c1,r2,c2,...]。接了优先于 RefPointsText。</summary>
        public InputPort<HTuple> RefPoints { get; } = new(
            "RefPoints", description: "参考点（可选）：交错 r,c,r,c,...；接了优先于 RefPointsText")
        { IsRequired = false };

        /// <summary>当前点（可选）：交错 [r1,c1,r2,c2,...]。接了优先于 CurPointsText。</summary>
        public InputPort<HTuple> CurPoints { get; } = new(
            "CurPoints", description: "当前点（可选）：交错 r,c,r,c,...；接了优先于 CurPointsText")
        { IsRequired = false };

        /// <summary>上游 HomMat2D（可选）：参考→当前（6 或 9 元）；仅 TemplatePose 模式使用。</summary>
        public InputPort<HTuple> UpstreamHomMat2D { get; } = new(
            "HomMat2D", description: "上游 HomMat2D（可选）：参考→当前，6/9 元；仅上游位姿模式使用")
        { IsRequired = false };

        public InputPort<double> PoseRow { get; } = new(
            "PoseRow", 0.0, "上游位姿 Row（像素）；仅上游位姿模式使用")
        { IsRequired = false };

        public InputPort<double> PoseColumn { get; } = new(
            "PoseColumn", 0.0, "上游位姿 Column（像素）；仅上游位姿模式使用")
        { IsRequired = false };

        public InputPort<double> PoseAngle { get; } = new(
            "PoseAngle", 0.0, "上游位姿 Angle（弧度）；仅上游位姿模式使用")
        { IsRequired = false };

        public InputPort<double> PoseScore { get; } = new(
            "PoseScore", 1.0, "上游位姿分数（0~1，可选）；仅上游位姿模式使用")
        { IsRequired = false };

        // ── 输出端口 ───────────────────────────────────────────────────────────

        /// <summary>对齐后的图（与 Image 同尺寸；画幅外填 0）。</summary>
        public OutputPort<HImage> AlignedImage { get; } = new(
            "AlignedImage", description: "对齐后的图（与 Image 同尺寸；画幅外填 0）");

        /// <summary>有效像素区（黑边之外）；下游用它限制检测区。</summary>
        public OutputPort<HRegion> ValidRegion { get; } = new(
            "ValidRegion", description: "有效像素区（黑边之外）；下游用它限制检测区");

        /// <summary>当前图 → 基准帧的 9 元变换（直接喂 projective_trans_image 得到 AlignedImage）。</summary>
        public OutputPort<HTuple> HomMat2D { get; } = new(
            "HomMat2D", description: "当前图→基准帧的 9 元变换（warp 用的就是它）");

        /// <summary>基准帧 → 当前图的 9 元逆变换（下游把结果坐标搬回原图）。</summary>
        public OutputPort<HTuple> InverseHomMat2D { get; } = new(
            "InverseHomMat2D", description: "基准帧→当前图的 9 元逆变换（结果坐标搬回原图用）");

        /// <summary>分数 0~1：有验证点时按残差归一化；上游位姿模式取 PoseScore。</summary>
        public OutputPort<double> Score { get; } = new(
            "Score", description: "分数 0~1：验证点残差归一化 / 上游 PoseScore");

        /// <summary>验证点残差（像素）；无验证点或位姿模式为 0。</summary>
        public OutputPort<double> Residual { get; } = new(
            "Residual", description: "验证点残差（像素）；无验证点/位姿模式为 0");

        /// <summary>有效区面积占画幅比例（0~1）。</summary>
        public OutputPort<double> ValidAreaRatio { get; } = new(
            "ValidAreaRatio", description: "有效区面积占画幅比例（0~1）");

        /// <summary>参与拟合的点对数（2/3/4；位姿模式为 0）。</summary>
        public OutputPort<int> UsedPointCount { get; } = new(
            "UsedPointCount", description: "参与拟合的点对数（2~12；位姿模式为 0）");

        /// <summary>RANSAC 内点数（≤4 点时等于总点数）。</summary>
        public OutputPort<int> InlierCount { get; } = new(
            "InlierCount", description: "RANSAC 内点数（≤4 点时等于总点数）");

        /// <summary>RANSAC 外点数（被剔除的错配点）。</summary>
        public OutputPort<int> OutlierCount { get; } = new(
            "OutlierCount", description: "RANSAC 外点数（被剔除的错配点）");

        /// <summary>可选阈值结论；默认 true。不改变 Success。</summary>
        public OutputPort<bool> IsOk { get; } = new(
            "IsOk", description: "可选阈值结论（默认 true）。不改变 Success");

        public OutputPort<string> Reason { get; } = new(
            "Reason", description: "阈值结论原因；未启用阈值时为 metrics-only");

        public OutputPort<string[]> Details { get; } = new(
            "Details", description: "明细：模式/点数/残差/分数/有效区/矩阵");

        // ── 步骤参数 ───────────────────────────────────────────────────────────

        [StepConfig, DefaultValue(AlignMode.PointPairs)]
        [Display(Name = "对齐模式", Description = "基准点（2~4 点）/ 手动四点（必须 4 对）/ 上游位姿（消费 HomMat2D 或 Pose）")]
        public partial AlignMode Mode { get; set; }

        [StepConfig, DefaultValue(AlignInterpolation.Bilinear)]
        [Display(Name = "插值方式", Description = "双线性（推荐）/ 最近邻（标签、二值图）")]
        public partial AlignInterpolation Interpolation { get; set; }

        [StepConfig, DefaultValue("")]
        [Display(Name = "参考点文本", Description = "格式 r,c; r,c; ...（基准帧坐标）；RefPoints 端口接了则以端口为准")]
        public partial string RefPointsText { get; set; }

        [StepConfig, DefaultValue("")]
        [Display(Name = "当前点文本", Description = "格式 r,c; r,c; ...（当前图坐标）；CurPoints 端口接了则以端口为准")]
        public partial string CurPointsText { get; set; }

        [StepConfig, DefaultValue("")]
        [Display(Name = "验证点文本", Description = "格式 refR,refC,curR,curC；4 点透视无冗余，填第 5 对点才能发现错点")]
        public partial string ValidationPointText { get; set; }

        [StepConfig, DefaultValue(0.0)]
        [Display(Name = "模板参考 Row", Description = "上游位姿模式：模板参考点 Row（像素）")]
        public partial double TemplateRefRow { get; set; }

        [StepConfig, DefaultValue(0.0)]
        [Display(Name = "模板参考 Column", Description = "上游位姿模式：模板参考点 Column（像素）")]
        public partial double TemplateRefColumn { get; set; }

        [StepConfig, DefaultValue(5.0)]
        [Display(Name = "残差上限(px)", Description = "验证点残差超过它直接失败；0 = 不做硬校验")]
        public partial double ResidualLimit { get; set; }

        [StepConfig, DefaultValue(2.0)]
        [Display(Name = "分数参考(px)", Description = "Score = 1 − 残差/该值（夹取 0~1）")]
        public partial double ScoreRef { get; set; }

        [StepConfig, DefaultValue(2.0)]
        [Display(Name = "RANSAC 内点阈值(px)", Description = ">4 对点时，残差 ≤ 该值的点算内点；默认 2px")]
        public partial double InlierThreshold { get; set; }

        [StepConfig, DefaultValue(false)]
        [Display(Name = "启用阈值判定", Description = "关闭时只输出指标；打开后按 MinScore / 残差上限 / 有效区比例判 IsOk")]
        public partial bool EnableThreshold { get; set; }

        [StepConfig, DefaultValue(0.0)]
        [Display(Name = "最低分数", Description = "Score 下限（0~1）；仅启用阈值时生效")]
        public partial double MinScore { get; set; }

        [StepConfig, DefaultValue(0.0)]
        [Display(Name = "最低有效区比例", Description = "ValidAreaRatio 下限（0~1）；仅启用阈值时生效")]
        public partial double MinValidAreaRatio { get; set; }

        // ── 参数夹取 ───────────────────────────────────────────────────────────

        partial void OnResidualLimitChanging(ref double value) => value = Math.Max(0.0, value);

        partial void OnScoreRefChanging(ref double value) => value = Math.Max(0.01, value);

        partial void OnInlierThresholdChanging(ref double value) => value = Math.Max(0.01, value);

        partial void OnMinScoreChanging(ref double value) => value = Math.Clamp(value, 0.0, 1.0);

        partial void OnMinValidAreaRatioChanging(ref double value) => value = Math.Clamp(value, 0.0, 1.0);

        // ── 配置视图（画布点选） ─────────────────────────────────────────────

        /// <summary>点对行（画布/表格共用）。</summary>
        public ObservableCollection<AlignPointRow> PointRows { get; } = new();

        private AlignPointRow? _selectedPointRow;
        public AlignPointRow? SelectedPointRow
        {
            get => _selectedPointRow;
            set => SetProperty(ref _selectedPointRow, value);
        }

        private bool _isPicking;
        public bool IsPicking
        {
            get => _isPicking;
            set => SetProperty(ref _isPicking, value);
        }

        private bool _pickReference = true;
        public bool PickTargetIsReference
        {
            get => _pickReference;
            set => SetProperty(ref _pickReference, value);
        }

        private bool _showReferenceImage;
        public bool ShowReferenceImage
        {
            get => _showReferenceImage;
            set
            {
                if (SetProperty(ref _showReferenceImage, value))
                    UpdateDisplayImage();
            }
        }

        private HImage? _currentImage;
        private HImage? _referenceImage;

        /// <summary>画布显示图：默认当前图；勾选“显示基准图”且有基准图时显示基准图。</summary>
        public HImage? DisplayImage =>
            ShowReferenceImage && _referenceImage != null ? _referenceImage : _currentImage;

        public bool HasReferenceImage => _referenceImage != null;

        private string _viewStatus = "在左侧图上取点，或在右侧表格里直接改坐标。";
        public string ViewStatus
        {
            get => _viewStatus;
            set => SetProperty(ref _viewStatus, value);
        }

        public object GetConfigView(IStepConfigData stepData)
        {
            Initialize(stepData);
            return new ImageAlignView { DataContext = this };
        }

        public override void Initialize(IStepConfigData stepData)
        {
            base.Initialize(stepData);
            RebuildPointRows();
        }

        public override void OnConfirm(IStepConfigData stepData)
        {
            RefPointsText = SerializeRows(true);
            CurPointsText = SerializeRows(false);
            base.OnConfirm(stepData);
        }

        /// <summary>视图 Loaded 后回填输入图（配置态上游图可能已由宿主注入）。</summary>
        public void OnViewLoaded()
        {
            var current = Image.ActualValue;
            _currentImage = current is { } c && c.IsInitialized() ? c : null;
            var reference = ReferenceImage.ActualValue;
            _referenceImage = reference is { } r && r.IsInitialized() ? r : null;
            OnPropertyChanged(nameof(HasReferenceImage));
            UpdateDisplayImage();
            if (_currentImage == null && _referenceImage == null)
                ViewStatus = "配置时未拿到图；仍可在右侧手填/粘贴点对，或先在流程里接图。";
        }

        public void StartPick(bool reference)
        {
            PickTargetIsReference = reference;
            IsPicking = true;
            ViewStatus = reference
                ? "取参考点：在左侧图上点击目标位置（一次一个点）"
                : "取当前点：在左侧图上点击目标位置（一次一个点）";
        }

        /// <summary>画布点击回调：写入当前选中行；没有选中行时新增一行。</summary>
        public void ApplyPickedPoint(double row, double col)
        {
            if (!IsPicking) return;
            var target = SelectedPointRow;
            if (target == null)
            {
                target = new AlignPointRow { Index = PointRows.Count + 1 };
                PointRows.Add(target);
                SelectedPointRow = target;
            }
            if (PickTargetIsReference)
            {
                target.RefRow = row;
                target.RefCol = col;
            }
            else
            {
                target.CurRow = row;
                target.CurCol = col;
            }
            IsPicking = false;
            ViewStatus = FormattableString.Invariant(
                $"已取{(PickTargetIsReference ? "参考" : "当前")}点：R={row:F2}, C={col:F2}");
        }

        public void DeleteSelectedPoint()
        {
            var target = SelectedPointRow;
            if (target == null) return;
            int index = PointRows.IndexOf(target);
            PointRows.Remove(target);
            Reindex();
            SelectedPointRow = PointRows.Count == 0
                ? null
                : PointRows[Math.Min(index, PointRows.Count - 1)];
            ViewStatus = $"已删除 1 行；当前 {PointRows.Count} 行。";
        }

        public void ClearPoints()
        {
            PointRows.Clear();
            SelectedPointRow = null;
            ViewStatus = "已清空点对。";
        }

        public void OpenParameters()
        {
            var model = new ImageAlignConfigModel(this);
            if (EasyDialog.ShowPropertyGridSync("图像对齐参数", model))
            {
                model.ApplyTo(this);
                ViewStatus = "参数已更新。";
            }
        }

        private void UpdateDisplayImage() => OnPropertyChanged(nameof(DisplayImage));

        private void Reindex()
        {
            for (int i = 0; i < PointRows.Count; i++)
                PointRows[i].Index = i + 1;
        }

        private string SerializeRows(bool reference)
        {
            if (PointRows.Count == 0) return string.Empty;
            return string.Join("; ", PointRows.Select(r => reference
                ? FormattableString.Invariant($"{r.RefRow},{r.RefCol}")
                : FormattableString.Invariant($"{r.CurRow},{r.CurCol}")));
        }

        private void RebuildPointRows()
        {
            PointRows.Clear();
            SelectedPointRow = null;

            double[] refs = TryParsePoints(RefPointsText, out string? refError);
            double[] curs = TryParsePoints(CurPointsText, out string? curError);
            if (refError != null || curError != null)
            {
                ViewStatus = refError ?? curError ?? "点文本解析失败。";
                return;
            }

            int n = Math.Max(refs.Length, curs.Length) / 2;
            for (int i = 0; i < n; i++)
            {
                PointRows.Add(new AlignPointRow
                {
                    Index = i + 1,
                    RefRow = i * 2 + 1 < refs.Length ? refs[i * 2] : 0,
                    RefCol = i * 2 + 1 < refs.Length ? refs[i * 2 + 1] : 0,
                    CurRow = i * 2 + 1 < curs.Length ? curs[i * 2] : 0,
                    CurCol = i * 2 + 1 < curs.Length ? curs[i * 2 + 1] : 0,
                });
            }
            if (PointRows.Count > 0) SelectedPointRow = PointRows[0];
            if (refs.Length != curs.Length)
                ViewStatus = "参考点/当前点数量不一致，请补齐或删除多余行。";
            else if (PointRows.Count == 0)
                ViewStatus = "还没有点对：点「取参考点/取当前点」，或在表格里新增。";
        }

        private static double[] TryParsePoints(string text, out string? error)
        {
            try
            {
                error = null;
                return ParsePointPairs(text, "点对");
            }
            catch (InvalidOperationException ex)
            {
                error = ex.Message;
                return Array.Empty<double>();
            }
        }

        // ── 算法 ───────────────────────────────────────────────────────────────

        public override void RunAlgorithm(IExecutionContext context)
        {
            HImage? image = Image.ActualValue;
            if (image is null || !image.IsInitialized())
            {
                Fail("输入图像未连接（Image 端口为空）。请接「采图」或「读图」步骤的输出。");
                return;
            }

            HImage? reference = ReferenceImage.ActualValue;
            if (reference is not null && !reference.IsInitialized()) reference = null;

            string interpolation = Interpolation == AlignInterpolation.NearestNeighbor
                ? "nearest_neighbor"
                : "bilinear";

            ImageAlignService.AlignFit fit;
            try
            {
                if (Mode == AlignMode.TemplatePose)
                {
                    HTuple? upstream = UpstreamHomMat2D.ActualValue;
                    if (upstream is { Length: 0 }) upstream = null;
                    fit = ImageAlignService.FitTemplatePose(
                        TemplateRefRow, TemplateRefColumn,
                        PoseRow.ActualValue, PoseColumn.ActualValue, PoseAngle.ActualValue,
                        upstream);
                }
                else
                {
                    double[] refPoints = GetPoints(RefPoints, RefPointsText, "参考点");
                    double[] curPoints = GetPoints(CurPoints, CurPointsText, "当前点");
                    if (refPoints.Length != curPoints.Length)
                    {
                        throw new InvalidOperationException(
                            $"参考点与当前点数量不一致：参考 {refPoints.Length / 2} 对，当前 {curPoints.Length / 2} 对。");
                    }
                    if (refPoints.Length < 4 || refPoints.Length % 2 != 0)
                    {
                        throw new InvalidOperationException(
                            "点对数量不足：至少 2 对（r,c; r,c），v1 最多 12 对。");
                    }

                    int n = refPoints.Length / 2;
                    if (n > 12)
                    {
                        throw new InvalidOperationException($"点对数量 {n} 超过 v1 上限 12 对。");
                    }

                    var refRows = new double[n];
                    var refCols = new double[n];
                    var curRows = new double[n];
                    var curCols = new double[n];
                    for (int i = 0; i < n; i++)
                    {
                        refRows[i] = refPoints[i * 2];
                        refCols[i] = refPoints[i * 2 + 1];
                        curRows[i] = curPoints[i * 2];
                        curCols[i] = curPoints[i * 2 + 1];
                    }

                    double[]? validation = ParseValidationPoint(ValidationPointText);
                    fit = ImageAlignService.FitPointPairs(
                        refRows, refCols, curRows, curCols,
                        Mode == AlignMode.ManualFourPoint,
                        validation, ResidualLimit, InlierThreshold);
                }

                var warp = ImageAlignService.Warp(image, reference, fit.HomMat2D, interpolation);

                AlignedImage.Value = warp.Aligned;
                ValidRegion.Value = warp.ValidRegion;
                HomMat2D.Value = fit.HomMat2D;
                InverseHomMat2D.Value = fit.InverseHomMat2D;
                ValidAreaRatio.Value = warp.ValidAreaRatio;
                UsedPointCount.Value = fit.UsedPointCount;
                InlierCount.Value = fit.InlierCount;
                OutlierCount.Value = fit.OutlierCount;

                double residual = double.IsNaN(fit.Residual) ? 0.0 : fit.Residual;
                double score = double.IsNaN(fit.Residual)
                    ? Clamp01(PoseScore.ActualValue)
                    : Clamp01(1.0 - fit.Residual / Math.Max(ScoreRef, 0.01));

                Residual.Value = residual;
                Score.Value = score;

                var reasons = new List<string>();
                if (EnableThreshold)
                {
                    if (MinScore > 0 && score < MinScore)
                        reasons.Add(string.Format(CultureInfo.InvariantCulture,
                            "Score {0:F3} < 下限 {1:F3}", score, MinScore));
                    if (ResidualLimit > 0 && residual > ResidualLimit)
                        reasons.Add(string.Format(CultureInfo.InvariantCulture,
                            "残差 {0:F2}px > 上限 {1:F2}px", residual, ResidualLimit));
                    if (MinValidAreaRatio > 0 && warp.ValidAreaRatio < MinValidAreaRatio)
                        reasons.Add(string.Format(CultureInfo.InvariantCulture,
                            "有效区比例 {0:F3} < 下限 {1:F3}", warp.ValidAreaRatio, MinValidAreaRatio));
                }

                bool ok = reasons.Count == 0;
                IsOk.Value = ok;
                Reason.Value = EnableThreshold
                    ? (ok ? "全部启用项在阈值内" : string.Join("；", reasons))
                    : "未启用阈值判定（metrics-only）";

                var details = new List<string>
                {
                    string.Format(CultureInfo.InvariantCulture,
                        "模式: {0}；点数: {1}；插值: {2}", fit.ModeText, fit.UsedPointCount,
                        Interpolation == AlignInterpolation.NearestNeighbor ? "nearest_neighbor" : "bilinear"),
                    string.Format(CultureInfo.InvariantCulture,
                        "残差: {0:F3}px；分数: {1:F3}；有效区比例: {2:F3}",
                        residual, score, warp.ValidAreaRatio),
                    string.Format(CultureInfo.InvariantCulture,
                        "点对: 使用 {0} / 内点 {1} / 外点 {2}",
                        fit.UsedPointCount, fit.InlierCount, fit.OutlierCount),
                    "HomMat2D: " + HomText(fit.HomMat2D),
                    "InverseHomMat2D: " + HomText(fit.InverseHomMat2D),
                };

                if (Mode != AlignMode.TemplatePose && fit.UsedPointCount <= 3)
                {
                    details.Add("提示: 2/3 点是精确拟合，残差恒≈0，无法自检错点；要自检请用 4 点 + 验证点，或 5+ 点 RANSAC。");
                }
                else if (Mode != AlignMode.TemplatePose && fit.UsedPointCount == 4 && double.IsNaN(fit.Residual))
                {
                    details.Add("提示: 4 点透视无冗余，残差恒 0；要检测错点请在「验证点文本」填第 5 对点，或改填 5+ 点走 RANSAC。");
                }
                else if (Mode != AlignMode.TemplatePose && fit.UsedPointCount > 4)
                {
                    details.Add(string.Format(CultureInfo.InvariantCulture,
                        "RANSAC: 内点 {0} / 外点 {1}（阈值 {2:F2}px）；外点已从拟合中剔除。",
                        fit.InlierCount, fit.OutlierCount, InlierThreshold));
                }
                if (EnableThreshold) details.Add("阈值: 启用；Score 下限=" + MinScore.ToString("F3", CultureInfo.InvariantCulture)
                    + "；残差上限=" + ResidualLimit.ToString("F2", CultureInfo.InvariantCulture)
                    + "；有效区下限=" + MinValidAreaRatio.ToString("F3", CultureInfo.InvariantCulture));

                Details.Value = details.ToArray();
            }
            catch (InvalidOperationException ex)
            {
                Fail(ex.Message);
            }
        }

        // ── 解析 ───────────────────────────────────────────────────────────────

        private static double[] GetPoints(InputPort<HTuple> port, string text, string name)
        {
            HTuple? actual = port.ActualValue;
            if (actual is { Length: > 0 })
            {
                var values = new double[actual.Length];
                for (int i = 0; i < actual.Length; i++)
                {
                    double v = actual[i].D;
                    if (!double.IsFinite(v))
                        throw new InvalidOperationException($"{name} 端口第 {i + 1} 个值不是有限数。");
                    values[i] = v;
                }
                if (values.Length % 2 != 0)
                    throw new InvalidOperationException($"{name} 端口长度 {values.Length} 不是偶数（应为 r,c,r,c,...）。");
                return values;
            }
            return ParsePointPairs(text, name);
        }

        private static double[] ParsePointPairs(string text, string name)
        {
            if (string.IsNullOrWhiteSpace(text)) return Array.Empty<double>();

            var items = text.Split(new[] { ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            var values = new List<double>();
            int index = 0;
            foreach (string raw in items)
            {
                index++;
                string item = raw.Trim();
                if (item.Length == 0) continue;
                string[] parts = item.Split(',');
                if (parts.Length != 2)
                {
                    throw new InvalidOperationException(
                        $"{name} 第 {index} 项「{item}」格式不对：应为「行,列」。");
                }
                if (!double.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double r)
                    || !double.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double c))
                {
                    throw new InvalidOperationException(
                        $"{name} 第 {index} 项「{item}」不是两个数字（行,列）。");
                }
                if (!double.IsFinite(r) || !double.IsFinite(c))
                {
                    throw new InvalidOperationException($"{name} 第 {index} 项「{item}」含非有限数。");
                }
                values.Add(r);
                values.Add(c);
            }
            return values.ToArray();
        }

        private static double[]? ParseValidationPoint(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            string[] parts = text.Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 4)
            {
                throw new InvalidOperationException(
                    "验证点格式不对：应为「refRow,refCol,curRow,curCol」四个数字。");
            }
            var values = new double[4];
            for (int i = 0; i < 4; i++)
            {
                if (!double.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out values[i])
                    || !double.IsFinite(values[i]))
                {
                    throw new InvalidOperationException($"验证点第 {i + 1} 个值「{parts[i].Trim()}」不是有限数字。");
                }
            }
            return values;
        }

        private static string HomText(HTuple hom)
        {
            var parts = new string[hom.Length];
            for (int i = 0; i < hom.Length; i++)
                parts[i] = hom[i].D.ToString("F6", CultureInfo.InvariantCulture);
            return string.Join(",", parts);
        }

        private static double Clamp01(double value)
        {
            if (double.IsNaN(value)) return 0.0;
            if (value < 0.0) return 0.0;
            if (value > 1.0) return 1.0;
            return value;
        }
    }
}
