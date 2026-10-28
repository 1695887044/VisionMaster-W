using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Core.Interfaces;
using Core.Interfaces.Assets;
using HalconDotNet;
using Newtonsoft.Json;
using Plugin.DeformableMatching;
using VisionMaster.Models;
using VisionMaster.Models.Assets;
using VisionMaster.Services;
using static FlowCanvasChecks.Program;
using ExecutionContext = VisionMaster.Services.ExecutionContext;

namespace FlowCanvasChecks
{
    /// <summary>
    /// 可形变模板匹配（Plugin.DeformableMatching）断言。
    ///
    /// 真值策略：同进程 HALCON 真算子做参考实现，插件结果逐项对拍；
    /// 局部用合成棋盘格（确定性、可笔算），平面用 Image\images\brake_disk 的 11 张真图。
    /// 反向断言：0/0 角度范围必须 0 命中，±180 必须命中 —— 防止“两边都没跑”的假绿。
    /// </summary>
    internal static class DeformableMatchingChecks
    {
        private const double CheckerRow = 199.5;
        private const double CheckerCol = 199.5;

        public static void Run()
        {
            Section("[Deformable] 可形变模板匹配（HALCON 真算子对拍）");

            Safe("端口契约", RunPortContract);
            Safe("局部非刚性", RunLocalParity);
            Safe("平面透视", RunPlanarParity);
            Safe("反向断言", RunReverseAssertion);
            Safe("序列化与缓存", RunSerializationAndCache);
            Safe("失败语义", RunFailureSemantics);
            Safe("方案资产与本地文件", RunExternalSources);
            Safe("样例流程", RunSampleFlows);
        }

        private static void Safe(string name, Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Check($"[{name}] 未捕获异常", false, ex.ToString());
            }
        }

        // ==================================================================
        //  端口契约
        // ==================================================================

        private static void RunPortContract()
        {
            Section("[Deformable] 端口契约");
            var plugin = new DeformableMatchingPlugin();

            Check("Image 端口存在且类型 HImage",
                plugin.Inputs.TryGetValue("Image", out var image) && image.DataType == typeof(HImage),
                DescribePort(plugin, "Image"));
            Check("SearchRegion 端口存在且类型 HRegion",
                plugin.Inputs.TryGetValue("SearchRegion", out var region) && region.DataType == typeof(HRegion),
                DescribePort(plugin, "SearchRegion"));
            Check("Image / SearchRegion 都是可选端口（IsRequired=false）",
                plugin.Inputs["Image"].IsRequired == false && plugin.Inputs["SearchRegion"].IsRequired == false,
                $"Image={plugin.Inputs["Image"].IsRequired} SearchRegion={plugin.Inputs["SearchRegion"].IsRequired}");

            Check("RectifiedImages 是 HObject（image array）",
                plugin.Outputs.TryGetValue("RectifiedImages", out var rectified) && rectified.DataType == typeof(HObject),
                DescribePort(plugin, "RectifiedImages"));
            Check("DeformedContours 是 HXLD",
                plugin.Outputs.TryGetValue("DeformedContours", out var contours) && contours.DataType == typeof(HXLD),
                DescribePort(plugin, "DeformedContours"));
            Check("HomMat2D 是 HTuple",
                plugin.Outputs.TryGetValue("HomMat2D", out var hom) && hom.DataType == typeof(HTuple),
                DescribePort(plugin, "HomMat2D"));
            Check("插件实现 IPluginCustomViewProvider", plugin is IPluginCustomViewProvider, plugin.GetType().FullName);
            Check("插件实现 IAssetIdConfig 且 AssetKind=template",
                plugin is IAssetIdConfig assetConfig && assetConfig.AssetKind == "template",
                plugin is IAssetIdConfig c ? c.AssetKind : "未实现");

            plugin.Dispose();
        }

        private static string DescribePort(DeformableMatchingPlugin plugin, string name)
        {
            if (plugin.Inputs.TryGetValue(name, out var input))
                return $"{name}: {input.DataType.Name} required={input.IsRequired}";
            if (plugin.Outputs.TryGetValue(name, out var output))
                return $"{name}: {output.DataType.Name}";
            return $"{name}: 不存在";
        }

        // ==================================================================
        //  局部非刚性
        // ==================================================================

        private static void RunLocalParity()
        {
            Section("[Deformable] 局部非刚性：同图 + 平移图对拍");

            using HObject template = MakeChecker(400, 400, 150, 150);
            var rect = new double[] { CheckerRow, CheckerCol, 0, 60, 45 };

            var plugin = new DeformableMatchingPlugin { InstanceName = "形变_局部" };
            plugin.Entry.Algorithm = DeformableAlgorithm.LocalDeformable;
            plugin.Entry.TemplateRect = (double[])rect.Clone();
            plugin.Entry.NumLevels = 0;
            plugin.Entry.Smoothness = 11;
            plugin.MinScore = 0.5;

            bool learned = plugin.LearnTemplateFromImage(template, out string learnError);
            Check("局部学习成功", learned, learnError);
            Check("局部学习后 Model 非空", !string.IsNullOrEmpty(plugin.Entry.Model),
                $"Model={plugin.Entry.Model.Length} chars");
            Check("局部学习后 LearnedAlgorithm=Local",
                plugin.Entry.LearnedAlgorithm == DeformableAlgorithm.LocalDeformable,
                plugin.Entry.LearnedAlgorithm.ToString());
            Check("局部学习后不是过期态", !plugin.IsModelStale, $"stale={plugin.IsModelStale}");

            HTuple refModel = CreateReferenceLocal(template, rect);

            using (HObject same = MakeChecker(400, 400, 150, 150))
            {
                plugin.Image.Value = new HImage(same);
                plugin.Execute(MakeContext(new StubLog()));
                Check("局部同图执行成功", plugin.Success.TypedValue is true, plugin.ErrorMessage.TypedValue);

                var reference = FindReferenceLocal(refModel, same, -30, 60, 0.5, 1);
                CompareLocal("局部同图", plugin, reference);
                reference.Dispose();
            }

            using (HObject translated = MakeChecker(500, 500, 250, 230))
            {
                plugin.Image.Value = new HImage(translated);
                plugin.Execute(MakeContext(new StubLog()));
                Check("局部平移图执行成功", plugin.Success.TypedValue is true, plugin.ErrorMessage.TypedValue);

                var reference = FindReferenceLocal(refModel, translated, -30, 60, 0.5, 1);
                CompareLocal("局部平移", plugin, reference);
                reference.Dispose();
            }

            HOperatorSet.ClearDeformableModel(refModel);
            plugin.Dispose();
        }

        private static void CompareLocal(string tag, DeformableMatchingPlugin plugin, LocalReference reference)
        {
            if (plugin.Success.TypedValue is false)
                return;

            Check($"{tag}：MatchCount 一致",
                plugin.MatchCount.TypedValue == reference.Scores.Length,
                $"plugin={plugin.MatchCount.TypedValue} ref={reference.Scores.Length}");

            if (plugin.MatchCount.TypedValue > 0 && reference.Scores.Length > 0)
            {
                Check($"{tag}：Score 差 ≤1e-6",
                    Math.Abs(plugin.Score.TypedValue - reference.Scores.DArr[0]) <= 1e-6,
                    $"plugin={plugin.Score.TypedValue:0.########} ref={reference.Scores.DArr[0]:0.########}");
                Check($"{tag}：Row 差 ≤1e-3 px",
                    Math.Abs(plugin.Row.TypedValue - reference.Rows.DArr[0]) <= 1e-3,
                    $"plugin={plugin.Row.TypedValue:0.######} ref={reference.Rows.DArr[0]:0.######}");
                Check($"{tag}：Column 差 ≤1e-3 px",
                    Math.Abs(plugin.Column.TypedValue - reference.Columns.DArr[0]) <= 1e-3,
                    $"plugin={plugin.Column.TypedValue:0.######} ref={reference.Columns.DArr[0]:0.######}");
            }

            if (plugin.DeformedContours.TypedValue is HObject pluginContourObj)
            {
                HOperatorSet.CountObj(pluginContourObj, out HTuple pluginContours);
                HOperatorSet.CountObj(reference.Contours, out HTuple refContours);
                Check($"{tag}：DeformedContours 段数一致",
                    pluginContours.I == refContours.I,
                    $"plugin={pluginContours.I} ref={refContours.I}（段数≠命中数）");
            }
            else
            {
                Check($"{tag}：DeformedContours 非空", false, "null");
            }

            if (plugin.RectifiedImages.TypedValue is HObject pluginRectifiedObj)
            {
                HOperatorSet.CountObj(pluginRectifiedObj, out HTuple pluginRectified);
                HOperatorSet.CountObj(reference.Rectified, out HTuple refRectified);
                Check($"{tag}：RectifiedImages 张数一致",
                    pluginRectified.I == refRectified.I,
                    $"plugin={pluginRectified.I} ref={refRectified.I}");
            }
            else
            {
                Check($"{tag}：RectifiedImages 非空", false, "null");
            }
        }

        // ==================================================================
        //  平面透视
        // ==================================================================

        private static void RunPlanarParity()
        {
            Section("[Deformable] 平面透视：刹车盘 11 图对拍");

            string repoRoot = Path.GetFullPath(Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\"));
            string dir = Path.Combine(repoRoot, "Image", "images", "brake_disk");
            string first = Path.Combine(dir, "brake_disk_bike_01.png");
            Check("平面参考图存在", File.Exists(first), first);
            if (!File.Exists(first))
                return;

            using HObject img01 = ReadImage(first);
            HOperatorSet.GetImageSize(img01, out HTuple width, out HTuple height);
            double row = height.D / 2.0;
            double col = width.D / 2.0;
            double half1 = width.D * 0.28;
            double half2 = height.D * 0.28;
            var rect = new double[] { row, col, 0, half1, half2 };

            var plugin = new DeformableMatchingPlugin { InstanceName = "形变_平面" };
            plugin.Entry.Algorithm = DeformableAlgorithm.PlanarUncalibDeformable;
            plugin.Entry.TemplateRect = (double[])rect.Clone();
            plugin.Entry.AngleStartDeg = -30;
            plugin.Entry.AngleExtentDeg = 60;
            plugin.Entry.NumLevels = 0;
            plugin.MinScore = 0.4;

            bool learned = plugin.LearnTemplateFromImage(img01, out string learnError);
            Check("平面学习成功", learned, learnError);
            Check("平面学习后 Model 非空", !string.IsNullOrEmpty(plugin.Entry.Model),
                $"Model={plugin.Entry.Model.Length} chars");
            Check("平面学习后 LearnedAlgorithm=Planar",
                plugin.Entry.LearnedAlgorithm == DeformableAlgorithm.PlanarUncalibDeformable,
                plugin.Entry.LearnedAlgorithm.ToString());

            HTuple refModel = CreateReferencePlanar(img01, rect);
            int consistent = 0;
            int images = 0;

            for (int i = 1; i <= 11; i++)
            {
                string path = Path.Combine(dir, $"brake_disk_bike_{i:00}.png");
                if (!File.Exists(path))
                {
                    Check($"平面图 {i:00} 存在", false, path);
                    continue;
                }

                images++;
                using HObject img = ReadImage(path);
                plugin.Image.Value = new HImage(img);
                plugin.Execute(MakeContext(new StubLog()));

                var reference = FindReferencePlanar(refModel, img, -30, 60, 0.4, 1);
                bool countEqual = plugin.MatchCount.TypedValue == reference.Scores.Length;
                if (countEqual)
                    consistent++;

                Check($"平面图 {i:00}：MatchCount 一致",
                    countEqual,
                    $"plugin={plugin.MatchCount.TypedValue} ref={reference.Scores.Length}");

                if (plugin.MatchCount.TypedValue > 0 && reference.Scores.Length > 0)
                {
                    Check($"平面图 {i:00}：Score 差 ≤1e-6",
                        Math.Abs(plugin.Score.TypedValue - reference.Scores.DArr[0]) <= 1e-6,
                        $"plugin={plugin.Score.TypedValue:0.########} ref={reference.Scores.DArr[0]:0.########}");

                    HTuple hom = plugin.HomMat2D.TypedValue as HTuple ?? new HTuple();
                    Check($"平面图 {i:00}：HomMat2D 长度 = 9×命中",
                        hom.Length == 9 * plugin.MatchCount.TypedValue,
                        $"len={hom.Length} count={plugin.MatchCount.TypedValue}");

                    if (hom.Length == reference.Hom.Length && hom.Length >= 9)
                    {
                        double maxDiff = 0;
                        for (int k = 0; k < hom.Length; k++)
                            maxDiff = Math.Max(maxDiff, Math.Abs(hom.DArr[k] - reference.Hom.DArr[k]));
                        Check($"平面图 {i:00}：HomMat2D 逐值 ≤1e-9",
                            maxDiff <= 1e-9,
                            $"maxDiff={maxDiff:E3}");
                    }

                    HOperatorSet.ProjectiveTransPixel(
                        reference.Hom, 0, 0, out HTuple refRow, out HTuple refCol);
                    Check($"平面图 {i:00}：Row 差 ≤1e-3 px",
                        Math.Abs(plugin.Row.TypedValue - refRow.D) <= 1e-3,
                        $"plugin={plugin.Row.TypedValue:0.######} ref={refRow.D:0.######}");
                    Check($"平面图 {i:00}：Column 差 ≤1e-3 px",
                        Math.Abs(plugin.Column.TypedValue - refCol.D) <= 1e-3,
                        $"plugin={plugin.Column.TypedValue:0.######} ref={refCol.D:0.######}");
                }

                reference.Dispose();
            }

            Check("平面：11 张图命中数全部一致", images == 11 && consistent == 11,
                $"images={images} consistent={consistent}");

            HOperatorSet.ClearDeformableModel(refModel);
            plugin.Dispose();
        }

        // ==================================================================
        //  反向断言
        // ==================================================================

        private static void RunReverseAssertion()
        {
            Section("[Deformable] 反向断言：角度 0/0 必须 0 命中，±180 必须命中");

            using HObject template = MakeChecker(400, 400, 150, 150);
            var rect = new double[] { CheckerRow, CheckerCol, 0, 60, 45 };

            var plugin = new DeformableMatchingPlugin { InstanceName = "形变_反向" };
            plugin.Entry.Algorithm = DeformableAlgorithm.LocalDeformable;
            plugin.Entry.TemplateRect = (double[])rect.Clone();
            plugin.Entry.NumLevels = 0;
            plugin.Entry.Smoothness = 11;
            plugin.MinScore = 0.5;
            Check("反向：学习成功", plugin.LearnTemplateFromImage(template, out string learnError), learnError);

            using HObject rotated = Rotate(template, 30, CheckerRow, CheckerCol);
            HTuple refModel = CreateReferenceLocal(template, rect);

            plugin.Entry.AngleStartDeg = 0;
            plugin.Entry.AngleExtentDeg = 0;
            plugin.Image.Value = new HImage(rotated);
            plugin.Execute(MakeContext(new StubLog()));
            Check("反向：插件 0/0 → 0 命中", plugin.MatchCount.TypedValue == 0,
                $"MatchCount={plugin.MatchCount.TypedValue} Score={plugin.Score.TypedValue:0.000}");

            var referenceZero = FindReferenceLocal(refModel, rotated, 0, 0, 0.5, 1);
            Check("反向：参考实现 0/0 → 0 命中", referenceZero.Scores.Length == 0,
                $"ref={referenceZero.Scores.Length}");
            referenceZero.Dispose();

            plugin.Entry.AngleStartDeg = -180;
            plugin.Entry.AngleExtentDeg = 360;
            plugin.Execute(MakeContext(new StubLog()));
            Check("反向：插件 ±180 → ≥1 命中", plugin.MatchCount.TypedValue >= 1,
                $"MatchCount={plugin.MatchCount.TypedValue} Score={plugin.Score.TypedValue:0.000}");

            var referenceFull = FindReferenceLocal(refModel, rotated, -180, 360, 0.5, 1);
            Check("反向：参考实现 ±180 → ≥1 命中", referenceFull.Scores.Length >= 1,
                $"ref={referenceFull.Scores.Length}");
            referenceFull.Dispose();

            HOperatorSet.ClearDeformableModel(refModel);
            plugin.Dispose();
        }

        // ==================================================================
        //  序列化与缓存
        // ==================================================================

        private static void RunSerializationAndCache()
        {
            Section("[Deformable] 序列化往返与模型缓存");

            using HObject template = MakeChecker(400, 400, 150, 150);
            using HObject search = MakeChecker(400, 400, 150, 150);
            var rect = new double[] { CheckerRow, CheckerCol, 0, 60, 45 };

            var plugin = new DeformableMatchingPlugin { InstanceName = "形变_序列化" };
            plugin.Entry.Algorithm = DeformableAlgorithm.LocalDeformable;
            plugin.Entry.TemplateRect = (double[])rect.Clone();
            plugin.Entry.NumLevels = 0;
            plugin.Entry.Smoothness = 11;
            plugin.MinScore = 0.5;
            Check("序列化：学习成功", plugin.LearnTemplateFromImage(template, out string learnError), learnError);

            plugin.Image.Value = new HImage(search);
            plugin.Execute(MakeContext(new StubLog()));
            int originalCount = plugin.MatchCount.TypedValue;
            double originalScore = plugin.Score.TypedValue;

            var step = new FakeStepData();
            plugin.OnConfirm(step);
            Check("序列化：InputValues 里有 TemplateJson",
                step.InputValues.TryGetValue("TemplateJson", out var jsonValue)
                && jsonValue is string json && json.Contains("Model"),
                jsonValue is string s ? $"len={s.Length}" : "missing");

            var runtime = new DeformableMatchingPlugin { InstanceName = "形变_重开" };
            runtime.ApplyConfigValues(step);
            Check("序列化：重开后 Model 非空", !string.IsNullOrEmpty(runtime.Entry.Model),
                $"Model={runtime.Entry.Model.Length} chars");
            Check("序列化：重开后 LearnedAlgorithm 保留",
                runtime.Entry.LearnedAlgorithm == DeformableAlgorithm.LocalDeformable,
                runtime.Entry.LearnedAlgorithm.ToString());

            runtime.Image.Value = new HImage(search);
            runtime.Execute(MakeContext(new StubLog()));
            Check("序列化：重开后命中数一致",
                runtime.MatchCount.TypedValue == originalCount,
                $"reopen={runtime.MatchCount.TypedValue} original={originalCount}");
            Check("序列化：重开后分数一致（≤1e-6）",
                Math.Abs(runtime.Score.TypedValue - originalScore) <= 1e-6,
                $"reopen={runtime.Score.TypedValue:0.########} original={originalScore:0.########}");

            runtime.Execute(MakeContext(new StubLog()));
            Check("缓存：连续两帧只读一次模型",
                runtime.ModelLoadCount == 1,
                $"ModelLoadCount={runtime.ModelLoadCount}");

            runtime.Dispose();
            plugin.Dispose();
        }

        // ==================================================================
        //  失败语义
        // ==================================================================

        private static void RunFailureSemantics()
        {
            Section("[Deformable] 失败语义");

            using HObject template = MakeChecker(400, 400, 150, 150);
            using HObject search = MakeChecker(400, 400, 150, 150);
            var rect = new double[] { CheckerRow, CheckerCol, 0, 60, 45 };

            var noModel = new DeformableMatchingPlugin { InstanceName = "形变_无模型" };
            noModel.Image.Value = new HImage(search);
            noModel.Execute(MakeContext(new StubLog()));
            Check("无模型：Success=false 且原因写「模板还没学习」",
                noModel.Success.TypedValue is false
                && (noModel.ErrorMessage.TypedValue as string ?? "").Contains("模板还没学习"),
                noModel.ErrorMessage.TypedValue);

            var noImage = new DeformableMatchingPlugin { InstanceName = "形变_无图" };
            noImage.Execute(MakeContext(new StubLog()));
            Check("无图：Success=false 且原因写「输入图像为空」",
                noImage.Success.TypedValue is false
                && (noImage.ErrorMessage.TypedValue as string ?? "").Contains("输入图像为空"),
                noImage.ErrorMessage.TypedValue);

            // 参考图挂了「图像资产」但解析不到 → 明确报错，**不静默退回** RefImagePath（2026-10-28）。
            // 静默退回的后果：界面上框的是资产那张图，学出来的模型却来自旧路径那张，且没有任何提示。
            var assetRef = new DeformableMatchingPlugin { InstanceName = "形变_参考图资产" };
            assetRef.SetConfigContext(new PluginConfigContext());
            assetRef.Entry.RefImagePath = Path.Combine(Path.GetTempPath(), "不存在_ref_" + Guid.NewGuid().ToString("N") + ".png");
            assetRef.Entry.RefImageAssetId = Guid.NewGuid().ToString();
            assetRef.LearnTemplate();
            Check("参考图资产解析不到：LearnTemplate 明确报「参考图无法解析」，不静默退回旧路径",
                assetRef.StatusMessage.Contains("参考图无法解析"), assetRef.StatusMessage);

            var learned = new DeformableMatchingPlugin { InstanceName = "形变_失败语义" };
            learned.Entry.Algorithm = DeformableAlgorithm.LocalDeformable;
            learned.Entry.TemplateRect = (double[])rect.Clone();
            learned.Entry.NumLevels = 0;
            learned.Entry.Smoothness = 11;
            learned.MinScore = 0.5;
            Check("失败语义：学习成功", learned.LearnTemplateFromImage(template, out string learnError), learnError);

            learned.Entry.Algorithm = DeformableAlgorithm.PlanarUncalibDeformable;
            learned.Image.Value = new HImage(search);
            learned.Execute(MakeContext(new StubLog()));
            Check("算法不符：Success=false 且原因要求重新学习",
                learned.Success.TypedValue is false
                && (learned.ErrorMessage.TypedValue as string ?? "").Contains("请重新学习"),
                learned.ErrorMessage.TypedValue);

            learned.Entry.Algorithm = DeformableAlgorithm.LocalDeformable;
            using (HObject black = MakeBlack(400, 400))
            {
                learned.Image.Value = new HImage(black);
                learned.Execute(MakeContext(new StubLog()));
                Check("无命中：Success=false 且 MatchCount=0",
                    learned.Success.TypedValue is false && learned.MatchCount.TypedValue == 0,
                    $"Success={learned.Success.TypedValue} MatchCount={learned.MatchCount.TypedValue}");
                Check("无命中：原因写「未找到目标」",
                    (learned.ErrorMessage.TypedValue as string ?? "").Contains("未找到目标"),
                    learned.ErrorMessage.TypedValue);
                Check("无命中：MeasureImage 仍输出原图",
                    learned.MeasureImage.TypedValue is HImage,
                    learned.MeasureImage.TypedValue?.GetType().Name ?? "null");
            }

            var corrupt = new DeformableMatchingPlugin { InstanceName = "形变_坏模型" };
            corrupt.Entry.Model = "not-base64";
            corrupt.Entry.Algorithm = DeformableAlgorithm.LocalDeformable;
            corrupt.Entry.LearnedAlgorithm = DeformableAlgorithm.LocalDeformable;
            corrupt.Image.Value = new HImage(search);
            corrupt.Execute(MakeContext(new StubLog()));
            Check("坏模型：Success=false 且原因写「模板加载失败」",
                corrupt.Success.TypedValue is false
                && (corrupt.ErrorMessage.TypedValue as string ?? "").Contains("模板加载失败"),
                corrupt.ErrorMessage.TypedValue);

            var badRoi = new DeformableMatchingPlugin { InstanceName = "形变_坏ROI" };
            badRoi.Entry.Algorithm = DeformableAlgorithm.LocalDeformable;
            badRoi.Entry.TemplateRect = new double[] { 0, 0, 0, 0, 0 };
            bool badLearned = badRoi.LearnTemplateFromImage(template, out string badRoiError);
            Check("ROI 退化：学习失败且点名半长/半宽",
                !badLearned && badRoiError.Contains("半长"),
                badRoiError);

            noModel.Dispose();
            noImage.Dispose();
            learned.Dispose();
            corrupt.Dispose();
            badRoi.Dispose();
        }

        // ==================================================================
        //  HALCON 参考实现
        // ==================================================================

        private static HTuple CreateReferenceLocal(HObject template, double[] rect)
        {
            HOperatorSet.GenRectangle2(out HObject roi, rect[0], rect[1], rect[2], rect[3], rect[4]);
            try
            {
                HOperatorSet.ReduceDomain(template, roi, out HObject reduced);
                try
                {
                    HOperatorSet.CopyImage(reduced, out HObject gray);
                    try
                    {
                        HOperatorSet.CreateLocalDeformableModel(
                            gray, new HTuple("auto"), new HTuple(), new HTuple(), "auto",
                            1, new HTuple(), "auto", 1, new HTuple(), "auto",
                            "none", "use_polarity", "auto", "auto",
                            new HTuple(), new HTuple(), out HTuple modelId);

                        HOperatorSet.AreaCenter(roi, out _, out HTuple cogRow, out HTuple cogCol);
                        HOperatorSet.SetDeformableModelOrigin(
                            modelId, rect[0] - cogRow.D, rect[1] - cogCol.D);
                        return modelId;
                    }
                    finally
                    {
                        gray.Dispose();
                    }
                }
                finally
                {
                    reduced.Dispose();
                }
            }
            finally
            {
                roi.Dispose();
            }
        }

        private static HTuple CreateReferencePlanar(HObject template, double[] rect)
        {
            HOperatorSet.GenRectangle2(out HObject roi, rect[0], rect[1], rect[2], rect[3], rect[4]);
            try
            {
                HOperatorSet.ReduceDomain(template, roi, out HObject reduced);
                try
                {
                    HOperatorSet.CopyImage(reduced, out HObject gray);
                    try
                    {
                        HOperatorSet.CreatePlanarUncalibDeformableModel(
                            gray, new HTuple("auto"), new HTuple(), new HTuple(), "auto",
                            1, new HTuple(), "auto", 1, new HTuple(), "auto",
                            "none", "use_polarity", "auto", "auto",
                            new HTuple(), new HTuple(), out HTuple modelId);

                        HOperatorSet.AreaCenter(roi, out _, out HTuple cogRow, out HTuple cogCol);
                        HOperatorSet.SetDeformableModelOrigin(
                            modelId, rect[0] - cogRow.D, rect[1] - cogCol.D);
                        return modelId;
                    }
                    finally
                    {
                        gray.Dispose();
                    }
                }
                finally
                {
                    reduced.Dispose();
                }
            }
            finally
            {
                roi.Dispose();
            }
        }

        private static LocalReference FindReferenceLocal(
            HTuple modelId, HObject image, double angleStartDeg, double angleExtentDeg,
            double minScore, int maxMatches)
        {
            HOperatorSet.FindLocalDeformableModel(
                image, out HObject rectified, out HObject vectorField, out HObject contours,
                modelId,
                angleStartDeg * Math.PI / 180.0,
                Math.Max(0, angleExtentDeg) * Math.PI / 180.0,
                1, 1, 1, 1,
                minScore, maxMatches, 0.5, 0, 0.9,
                new HTuple("deformed_contours", "image_rectified"),
                new HTuple("deformation_smoothness", "expand_border", "subpixel"),
                new HTuple(11, 0, 1),
                out HTuple scores, out HTuple rows, out HTuple cols);
            vectorField.Dispose();
            return new LocalReference(scores, rows, cols, contours, rectified);
        }

        private static PlanarReference FindReferencePlanar(
            HTuple modelId, HObject image, double angleStartDeg, double angleExtentDeg,
            double minScore, int maxMatches)
        {
            HOperatorSet.FindPlanarUncalibDeformableModel(
                image, modelId,
                angleStartDeg * Math.PI / 180.0,
                Math.Max(0, angleExtentDeg) * Math.PI / 180.0,
                1, 1, 1, 1,
                minScore, maxMatches, 0.5, 0, 0.9,
                new HTuple(), new HTuple(),
                out HTuple hom, out HTuple scores);
            return new PlanarReference(hom, scores);
        }

        private sealed class LocalReference : IDisposable
        {
            public HTuple Scores { get; }
            public HTuple Rows { get; }
            public HTuple Columns { get; }
            public HObject Contours { get; }
            public HObject Rectified { get; }

            public LocalReference(HTuple scores, HTuple rows, HTuple columns, HObject contours, HObject rectified)
            {
                Scores = scores;
                Rows = rows;
                Columns = columns;
                Contours = contours;
                Rectified = rectified;
            }

            public void Dispose()
            {
                try { Contours?.Dispose(); } catch { }
                try { Rectified?.Dispose(); } catch { }
            }
        }

        private sealed class PlanarReference : IDisposable
        {
            public HTuple Hom { get; }
            public HTuple Scores { get; }

            public PlanarReference(HTuple hom, HTuple scores)
            {
                Hom = hom;
                Scores = scores;
            }

            public void Dispose() { }
        }

        // ==================================================================
        //  合成图
        // ==================================================================

        private static HObject MakeBlack(int width, int height)
        {
            HOperatorSet.GenImageConst(out HObject image, "byte", width, height);
            return image;
        }

        private static HObject MakeChecker(int width, int height, int row, int col)
        {
            HOperatorSet.GenImageConst(out HObject image, "byte", width, height);
            HOperatorSet.GenCheckerRegion(out HObject checker, 100, 100, 10, 10);
            HOperatorSet.MoveRegion(checker, out HObject moved, row, col);
            HOperatorSet.PaintRegion(moved, image, out HObject painted, 255, "fill");
            checker.Dispose();
            moved.Dispose();
            image.Dispose();
            return painted;
        }

        private static HObject Rotate(HObject image, double angleDeg, double row, double col)
        {
            HOperatorSet.HomMat2dIdentity(out HTuple hom);
            HOperatorSet.HomMat2dRotate(
                hom, angleDeg * Math.PI / 180.0, row, col, out HTuple homRotate);
            HOperatorSet.AffineTransImage(
                image, out HObject rotated, homRotate, "constant", "false");
            return rotated;
        }

        private static HObject ReadImage(string path)
        {
            HOperatorSet.ReadImage(out HObject image, path);
            return image;
        }

        // ==================================================================
        //  方案资产 / 本地模板文件
        // ==================================================================

        private static void RunExternalSources()
        {
            Section("[Deformable] 方案资产 / 本地模板文件");

            using HObject template = MakeChecker(400, 400, 150, 150);
            var rect = new double[] { CheckerRow, CheckerCol, 0, 60, 45 };
            var learned = new DeformableMatchingPlugin { InstanceName = "形变_外部_基准" };
            learned.Entry.Algorithm = DeformableAlgorithm.LocalDeformable;
            learned.Entry.TemplateRect = (double[])rect.Clone();
            learned.Entry.NumLevels = 0;
            learned.Entry.Smoothness = 11;
            Check("外部来源：基准模板学习成功",
                learned.LearnTemplateFromImage(template, out string learnError), learnError);
            string entryJson = JsonConvert.SerializeObject(learned.Entry);
            learned.Dispose();

            string temp = Path.Combine(Path.GetTempPath(), "vm-deformable-ext-" + Guid.NewGuid().ToString("N"));
            try
            {
                string templateDir = Path.Combine(temp, "Assets", "Templates");
                Directory.CreateDirectory(templateDir);
                string assetRel = Path.Combine("Assets", "Templates", "local.json");
                string assetPath = Path.Combine(temp, assetRel);
                File.WriteAllText(assetPath, entryJson, new UTF8Encoding(false));

                var solution = new SolutionModel
                {
                    SolutionName = "DeformableExternalTest",
                    SolutionFilePath = Path.Combine(temp, "DeformableExternalTest.vms")
                };
                var assetId = Guid.NewGuid();
                solution.TemplateAssets.Add(new TemplateAsset
                {
                    AssetId = assetId,
                    Name = "形变模板_局部",
                    RelativePath = assetRel,
                    Version = 1,
                    Status = "有效",
                    ContentHash = "v1"
                });

                var workspace = new WorkspaceContext();
                workspace.SwitchSolution(solution);
                var context = new ExecutionContext(
                    new StubLog(), new FlowSession { FlowName = "形变_外部" }, workspace,
                    CancellationToken.None);

                double inlineScore;
                int inlineCount;
                using (HObject search = MakeChecker(400, 400, 150, 150))
                {
                    var inline = new DeformableMatchingPlugin { InstanceName = "形变_内嵌" };
                    var step = new FakeStepData();
                    step.SetInputValue("TemplateJson", entryJson);
                    step.SetInputValue("MinScore", 0.5);
                    inline.ApplyConfigValues(step);
                    inline.Image.Value = new HImage(search);
                    inline.Execute(MakeContext(new StubLog()));
                    Check("外部来源：内嵌基准执行成功",
                        inline.Success.TypedValue is true, inline.ErrorMessage.TypedValue);
                    inlineScore = inline.Score.TypedValue;
                    inlineCount = inline.MatchCount.TypedValue;
                    inline.Dispose();
                }

                using (HObject search = MakeChecker(400, 400, 150, 150))
                {
                    var plugin = new DeformableMatchingPlugin { InstanceName = "形变_资产" };
                    var step = new FakeStepData();
                    step.SetInputValue("TemplateJson", "{}");
                    step.SetInputValue("TemplateAssetId", assetId.ToString());
                    step.SetInputValue("MinScore", 0.5);
                    plugin.ApplyConfigValues(step);
                    plugin.Image.Value = new HImage(search);
                    plugin.Execute(context);
                    Check("方案资产：执行成功",
                        plugin.Success.TypedValue is true, plugin.ErrorMessage.TypedValue);
                    Check("方案资产：命中数与内嵌一致",
                        plugin.MatchCount.TypedValue == inlineCount,
                        $"asset={plugin.MatchCount.TypedValue} inline={inlineCount}");
                    Check("方案资产：分数与内嵌一致",
                        Math.Abs(plugin.Score.TypedValue - inlineScore) <= 1e-6,
                        $"asset={plugin.Score.TypedValue:0.########} inline={inlineScore:0.########}");
                    Check("方案资产：首次加载模型", plugin.ModelLoadCount == 1,
                        $"ModelLoadCount={plugin.ModelLoadCount}");

                    plugin.Execute(context);
                    Check("方案资产：连续两帧只读一次", plugin.ModelLoadCount == 1,
                        $"ModelLoadCount={plugin.ModelLoadCount}");

                    solution.TemplateAssets[0].Version = 2;
                    solution.TemplateAssets[0].ContentHash = "v2";
                    plugin.Execute(context);
                    Check("方案资产：升版后重新加载模型", plugin.ModelLoadCount == 2,
                        $"ModelLoadCount={plugin.ModelLoadCount}");
                    plugin.Dispose();
                }

                string localPath = Path.Combine(temp, "local-template.json");
                File.WriteAllText(localPath, entryJson, new UTF8Encoding(false));
                using (HObject search = MakeChecker(400, 400, 150, 150))
                {
                    var plugin = new DeformableMatchingPlugin { InstanceName = "形变_本地" };
                    var step = new FakeStepData();
                    step.SetInputValue("TemplateJson", "{}");
                    step.SetInputValue("TemplateFilePath", localPath);
                    step.SetInputValue("MinScore", 0.5);
                    plugin.ApplyConfigValues(step);
                    plugin.Image.Value = new HImage(search);
                    plugin.Execute(MakeContext(new StubLog()));
                    Check("本地文件：执行成功",
                        plugin.Success.TypedValue is true, plugin.ErrorMessage.TypedValue);
                    Check("本地文件：命中数与内嵌一致",
                        plugin.MatchCount.TypedValue == inlineCount,
                        $"file={plugin.MatchCount.TypedValue} inline={inlineCount}");
                    Check("本地文件：分数与内嵌一致",
                        Math.Abs(plugin.Score.TypedValue - inlineScore) <= 1e-6,
                        $"file={plugin.Score.TypedValue:0.########} inline={inlineScore:0.########}");
                    Check("本地文件：首次加载模型", plugin.ModelLoadCount == 1,
                        $"ModelLoadCount={plugin.ModelLoadCount}");

                    plugin.Execute(MakeContext(new StubLog()));
                    Check("本地文件：连续两帧只读一次", plugin.ModelLoadCount == 1,
                        $"ModelLoadCount={plugin.ModelLoadCount}");

                    File.SetLastWriteTimeUtc(localPath, DateTime.UtcNow.AddSeconds(2));
                    plugin.Execute(MakeContext(new StubLog()));
                    Check("本地文件：文件更新后重新加载模型", plugin.ModelLoadCount == 2,
                        $"ModelLoadCount={plugin.ModelLoadCount}");
                    plugin.Dispose();
                }

                var statePlugin = new DeformableMatchingPlugin { InstanceName = "形变_来源状态" };
                var stateStep = new FakeStepData();
                stateStep.SetInputValue("TemplateJson", "{}");
                stateStep.SetInputValue("TemplateAssetId", assetId.ToString());
                stateStep.SetInputValue("TemplateFilePath", localPath);
                statePlugin.ApplyConfigValues(stateStep);
                Check("来源状态：资产优先、学习禁用、本地文件禁用",
                    statePlugin.IsTemplateFromAsset
                    && !statePlugin.CanLearnTemplate
                    && !statePlugin.CanUseLocalTemplateFile,
                    $"asset={statePlugin.IsTemplateFromAsset} learn={statePlugin.CanLearnTemplate} local={statePlugin.CanUseLocalTemplateFile}");
                Check("来源状态：状态胶囊 = 方案资产",
                    statePlugin.TemplateStatusText == "方案资产", statePlugin.TemplateStatusText);
                Check("来源状态：清除按钮 = 清除内嵌",
                    statePlugin.ClearTemplateButtonText == "清除内嵌", statePlugin.ClearTemplateButtonText);
                Check("来源状态：外部模式学习被拒",
                    !statePlugin.LearnTemplateFromImage(template, out string externalLearnError)
                    && externalLearnError.Contains("取消外部来源"),
                    externalLearnError);
                statePlugin.OnConfirm(stateStep);
                var externalSnapshot = stateStep.InputValues.TryGetValue("TemplateJson", out var externalJsonValue)
                    ? JsonConvert.DeserializeObject<DeformableTemplateEntry>(externalJsonValue as string ?? "{}")
                    : null;
                Check("来源状态：外部模式 OnConfirm 不写模型",
                    externalSnapshot != null && string.IsNullOrEmpty(externalSnapshot.Model),
                    $"Model={(externalSnapshot?.Model.Length ?? -1)} chars");
                Check("来源状态：OnConfirm 保留资产 Id",
                    stateStep.InputValues.TryGetValue("TemplateAssetId", out var savedAssetId)
                    && string.Equals(savedAssetId as string, assetId.ToString(), StringComparison.OrdinalIgnoreCase),
                    savedAssetId?.ToString());
                statePlugin.ClearTemplate();
                Check("来源状态：清除内嵌后资产仍在",
                    statePlugin.IsTemplateFromAsset && string.IsNullOrEmpty(statePlugin.Entry.Model),
                    $"asset={statePlugin.IsTemplateFromAsset} model={statePlugin.Entry.Model.Length}");
                statePlugin.ClearLocalTemplate();
                Check("来源状态：清除本地文件后资产仍优先",
                    statePlugin.IsTemplateFromAsset && !statePlugin.IsTemplateFromFile,
                    $"asset={statePlugin.IsTemplateFromAsset} file={statePlugin.IsTemplateFromFile}");
                statePlugin.Dispose();

                var localState = new DeformableMatchingPlugin { InstanceName = "形变_本地状态" };
                var localStateStep = new FakeStepData();
                localStateStep.SetInputValue("TemplateJson", "{}");
                localStateStep.SetInputValue("TemplateFilePath", localPath);
                localState.ApplyConfigValues(localStateStep);
                Check("来源状态：本地文件模式禁用学习",
                    localState.IsTemplateFromFile
                    && !localState.CanLearnTemplate
                    && localState.TemplateStatusText == "本地模板",
                    $"file={localState.IsTemplateFromFile} learn={localState.CanLearnTemplate} status={localState.TemplateStatusText}");
                localState.Dispose();

                var planarEntry = JsonConvert.DeserializeObject<DeformableTemplateEntry>(entryJson)!;
                planarEntry.Algorithm = DeformableAlgorithm.PlanarUncalibDeformable;
                planarEntry.LearnedAlgorithm = DeformableAlgorithm.PlanarUncalibDeformable;
                string planarJson = JsonConvert.SerializeObject(planarEntry);
                string planarLocalPath = Path.Combine(temp, "planar-local.json");
                File.WriteAllText(planarLocalPath, planarJson, new UTF8Encoding(false));

                using (HObject search = MakeChecker(400, 400, 150, 150))
                {
                    var plugin = new DeformableMatchingPlugin { InstanceName = "形变_优先级" };
                    var step = new FakeStepData();
                    step.SetInputValue("TemplateJson", "{}");
                    step.SetInputValue("TemplateAssetId", assetId.ToString());
                    step.SetInputValue("TemplateFilePath", planarLocalPath);
                    step.SetInputValue("MinScore", 0.5);
                    plugin.ApplyConfigValues(step);
                    plugin.Image.Value = new HImage(search);
                    plugin.Execute(context);
                    Check("优先级：资产优先于本地文件（否则平面模板会算法不符）",
                        plugin.Success.TypedValue is true, plugin.ErrorMessage.TypedValue);
                    Check("优先级：加载的是资产里的局部模板",
                        plugin.Entry.LearnedAlgorithm == DeformableAlgorithm.LocalDeformable,
                        plugin.Entry.LearnedAlgorithm.ToString());
                    plugin.Dispose();
                }

                var modelAssetId = Guid.NewGuid();
                solution.ModelAssets.Add(new ModelAsset
                {
                    AssetId = modelAssetId,
                    Name = "错误类型_模型",
                    RelativePath = assetRel,
                    Version = 1,
                    Status = "有效"
                });
                using (HObject search = MakeChecker(400, 400, 150, 150))
                {
                    var plugin = new DeformableMatchingPlugin { InstanceName = "形变_错类型" };
                    var step = new FakeStepData();
                    step.SetInputValue("TemplateJson", "{}");
                    step.SetInputValue("TemplateAssetId", modelAssetId.ToString());
                    step.SetInputValue("MinScore", 0.5);
                    plugin.ApplyConfigValues(step);
                    plugin.Image.Value = new HImage(search);
                    plugin.Execute(context);
                    Check("资产类型不符：失败",
                        plugin.Success.TypedValue is false, plugin.ErrorMessage.TypedValue);
                    Check("资产类型不符：原因点名需要 template",
                        (plugin.ErrorMessage.TypedValue as string ?? "").Contains("需要 template"),
                        plugin.ErrorMessage.TypedValue);
                    plugin.Dispose();
                }

                var missingId = Guid.NewGuid();
                solution.TemplateAssets.Add(new TemplateAsset
                {
                    AssetId = missingId,
                    Name = "缺失模板",
                    RelativePath = Path.Combine("Assets", "Templates", "missing.json"),
                    Version = 1,
                    Status = "有效"
                });
                using (HObject search = MakeChecker(400, 400, 150, 150))
                {
                    var plugin = new DeformableMatchingPlugin { InstanceName = "形变_缺失" };
                    var step = new FakeStepData();
                    step.SetInputValue("TemplateJson", "{}");
                    step.SetInputValue("TemplateAssetId", missingId.ToString());
                    step.SetInputValue("MinScore", 0.5);
                    plugin.ApplyConfigValues(step);
                    plugin.Image.Value = new HImage(search);
                    plugin.Execute(context);
                    Check("资产文件缺失：失败",
                        plugin.Success.TypedValue is false, plugin.ErrorMessage.TypedValue);
                    Check("资产文件缺失：原因点名文件不存在",
                        (plugin.ErrorMessage.TypedValue as string ?? "").Contains("资产文件不存在"),
                        plugin.ErrorMessage.TypedValue);
                    plugin.Dispose();
                }

                var archivedId = Guid.NewGuid();
                solution.TemplateAssets.Add(new TemplateAsset
                {
                    AssetId = archivedId,
                    Name = "已归档模板",
                    RelativePath = assetRel,
                    Version = 1,
                    Status = "有效"
                });
                solution.AssetLifecycle.Add(new AssetLifecycleState
                {
                    AssetId = archivedId,
                    Kind = "template",
                    State = "已归档"
                });
                using (HObject search = MakeChecker(400, 400, 150, 150))
                {
                    var plugin = new DeformableMatchingPlugin { InstanceName = "形变_归档" };
                    var step = new FakeStepData();
                    step.SetInputValue("TemplateJson", "{}");
                    step.SetInputValue("TemplateAssetId", archivedId.ToString());
                    step.SetInputValue("MinScore", 0.5);
                    plugin.ApplyConfigValues(step);
                    plugin.Image.Value = new HImage(search);
                    plugin.Execute(context);
                    Check("资产已归档：失败",
                        plugin.Success.TypedValue is false, plugin.ErrorMessage.TypedValue);
                    Check("资产已归档：原因点名已归档",
                        (plugin.ErrorMessage.TypedValue as string ?? "").Contains("已归档"),
                        plugin.ErrorMessage.TypedValue);
                    plugin.Dispose();
                }

                string corruptRel = Path.Combine("Assets", "Templates", "corrupt.json");
                string corruptPath = Path.Combine(temp, corruptRel);
                File.WriteAllText(corruptPath, "{bad", new UTF8Encoding(false));
                var corruptId = Guid.NewGuid();
                solution.TemplateAssets.Add(new TemplateAsset
                {
                    AssetId = corruptId,
                    Name = "坏模板",
                    RelativePath = corruptRel,
                    Version = 1,
                    Status = "有效"
                });
                using (HObject search = MakeChecker(400, 400, 150, 150))
                {
                    var plugin = new DeformableMatchingPlugin { InstanceName = "形变_坏资产" };
                    var step = new FakeStepData();
                    step.SetInputValue("TemplateJson", "{}");
                    step.SetInputValue("TemplateAssetId", corruptId.ToString());
                    step.SetInputValue("MinScore", 0.5);
                    plugin.ApplyConfigValues(step);
                    plugin.Image.Value = new HImage(search);
                    plugin.Execute(context);
                    Check("资产 JSON 损坏：失败",
                        plugin.Success.TypedValue is false, plugin.ErrorMessage.TypedValue);
                    Check("资产 JSON 损坏：原因点名读取失败",
                        (plugin.ErrorMessage.TypedValue as string ?? "").Contains("读取模板资产失败"),
                        plugin.ErrorMessage.TypedValue);
                    plugin.Dispose();
                }

                using (HObject search = MakeChecker(400, 400, 150, 150))
                {
                    var plugin = new DeformableMatchingPlugin { InstanceName = "形变_本地缺失" };
                    var step = new FakeStepData();
                    step.SetInputValue("TemplateJson", "{}");
                    step.SetInputValue("TemplateFilePath", Path.Combine(temp, "missing-local.json"));
                    step.SetInputValue("MinScore", 0.5);
                    plugin.ApplyConfigValues(step);
                    plugin.Image.Value = new HImage(search);
                    plugin.Execute(MakeContext(new StubLog()));
                    Check("本地文件缺失：失败",
                        plugin.Success.TypedValue is false, plugin.ErrorMessage.TypedValue);
                    Check("本地文件缺失：原因点名文件不存在",
                        (plugin.ErrorMessage.TypedValue as string ?? "").Contains("本地模板文件不存在"),
                        plugin.ErrorMessage.TypedValue);
                    plugin.Dispose();
                }

                using (HObject search = MakeChecker(400, 400, 150, 150))
                {
                    var plugin = new DeformableMatchingPlugin { InstanceName = "形变_算法不符" };
                    var step = new FakeStepData();
                    step.SetInputValue("TemplateJson", "{}");
                    step.SetInputValue("TemplateFilePath", planarLocalPath);
                    step.SetInputValue("MinScore", 0.5);
                    plugin.ApplyConfigValues(step);
                    plugin.Image.Value = new HImage(search);
                    plugin.Execute(MakeContext(new StubLog()));
                    Check("本地文件算法不符：失败",
                        plugin.Success.TypedValue is false, plugin.ErrorMessage.TypedValue);
                    Check("本地文件算法不符：原因点名平面透视",
                        (plugin.ErrorMessage.TypedValue as string ?? "").Contains("平面透视"),
                        plugin.ErrorMessage.TypedValue);
                    plugin.Dispose();
                }

                string arrayRel = Path.Combine("Assets", "Templates", "library.json");
                File.WriteAllText(Path.Combine(temp, arrayRel), "[]", new UTF8Encoding(false));
                var arrayId = Guid.NewGuid();
                solution.TemplateAssets.Add(new TemplateAsset
                {
                    AssetId = arrayId,
                    Name = "Matching模板库",
                    RelativePath = arrayRel,
                    Version = 1,
                    Status = "有效"
                });
                using (HObject search = MakeChecker(400, 400, 150, 150))
                {
                    var plugin = new DeformableMatchingPlugin { InstanceName = "形变_错格式" };
                    var step = new FakeStepData();
                    step.SetInputValue("TemplateJson", "{}");
                    step.SetInputValue("TemplateAssetId", arrayId.ToString());
                    step.SetInputValue("MinScore", 0.5);
                    plugin.ApplyConfigValues(step);
                    plugin.Image.Value = new HImage(search);
                    plugin.Execute(context);
                    Check("选到模板库数组：失败",
                        plugin.Success.TypedValue is false, plugin.ErrorMessage.TypedValue);
                    Check("选到模板库数组：原因点名不是形变模板",
                        (plugin.ErrorMessage.TypedValue as string ?? "").Contains("模板库数组"),
                        plugin.ErrorMessage.TypedValue);
                    plugin.Dispose();
                }

                var flow = new FlowModel { FlowName = "形变资产引用测试" };
                var stepModel = new ActionStep(
                    "", "可形变模板匹配",
                    "Plugin.DeformableMatching.DeformableMatchingPlugin, Plugin.DeformableMatching",
                    "形变匹配");
                stepModel.InputValues["TemplateAssetId"] = assetId.ToString();
                flow.Steps.Add(stepModel);
                solution.Flows.Add(flow);

                var catalog = new SolutionProjectAssetCatalog(workspace).Build();
                var item = catalog.FirstOrDefault(
                    i => string.Equals(i.Id, assetId.ToString(), StringComparison.OrdinalIgnoreCase));
                Check("资产页：形变模板资产可见", item != null,
                    $"assets={catalog.Count}");
                Check("资产页：显示流程引用",
                    item != null && item.References.Any(
                        r => r.FlowName == flow.FlowName && r.StepName == stepModel.StepName),
                    item == null ? "(asset not found)" : $"refs={item.References.Count}");
            }
            finally
            {
                try { Directory.Delete(temp, true); } catch { }
            }
        }

        // ==================================================================
        //  样例流程（.vms 加载 → 编译 → 真插件执行）
        // ==================================================================

        private static void RunSampleFlows()
        {
            Section("[Deformable] 样例流程：加载 / 编译 / 执行");

            string repoRoot = Path.GetFullPath(Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\"));
            string vmsPath = Path.Combine(repoRoot, "解决方案", "视觉样例对拍", "视觉样例对拍.vms");
            Check("样例方案存在", File.Exists(vmsPath), vmsPath);
            if (!File.Exists(vmsPath))
                return;

            var loaded = new SolutionService().LoadAsync(vmsPath).GetAwaiter().GetResult();
            Check("样例方案可被生产加载器打开", loaded.Success && loaded.Data != null, loaded.Message);
            if (!loaded.Success || loaded.Data == null)
                return;

            var workspace = new WorkspaceContext();
            workspace.SwitchSolution(loaded.Data);

            CheckSampleFlow(loaded.Data, workspace, vmsPath, "形变匹配_局部非刚性", 0.5);
            CheckSampleFlow(loaded.Data, workspace, vmsPath, "形变匹配_平面透视", 0.4);
        }

        private static void CheckSampleFlow(
            SolutionModel solution, WorkspaceContext workspace, string vmsPath,
            string flowName, double minScore)
        {
            var flow = solution.Flows.FirstOrDefault(f => f.FlowName == flowName);
            Check($"{flowName}：流程存在", flow != null,
                $"流程={string.Join(",", solution.Flows.Select(f => f.FlowName))}");
            if (flow == null)
                return;

            var acq = flow.Steps.OfType<ActionStep>().FirstOrDefault(s => s.PluginName == "图像采集");
            var alg = flow.Steps.OfType<ActionStep>().FirstOrDefault(s => s.PluginName == "可形变模板匹配");
            var display = flow.Steps.OfType<ActionStep>().FirstOrDefault(s => s.PluginName == "结果显示");
            Check($"{flowName}：采集 / 可形变匹配 / 结果显示三步齐全",
                acq != null && alg != null && display != null,
                $"步骤={string.Join(" → ", flow.Steps.Select(s => s.StepName))}");
            if (acq == null || alg == null || display == null)
                return;

            Check($"{flowName}：结果显示接原始图 + 形变轮廓（DeformedContours）/ Score / Row / Column",
                display.LinkedSources.TryGetValue("SrcImage", out var src)
                && src.TargetPortName == "Image"
                && display.LinkedSources.TryGetValue("OverlayContours", out var overlay)
                && overlay.TargetPortName == "DeformedContours"
                && display.LinkedSources.TryGetValue("r1", out var score)
                && score.TargetPortName == "Score"
                && display.LinkedSources.TryGetValue("r2", out var row)
                && row.TargetPortName == "Row"
                && display.LinkedSources.TryGetValue("r3", out var col)
                && col.TargetPortName == "Column",
                $"SrcImage→{display.LinkedSources.GetValueOrDefault("SrcImage")?.TargetPortName} "
                + $"OverlayContours→{display.LinkedSources.GetValueOrDefault("OverlayContours")?.TargetPortName}");
            Check($"{flowName}：算法步骤只输出结果（DisplayViewIndex=0 且 KeepAnnotatedImage=false）",
                Convert.ToInt32(alg.InputValues.GetValueOrDefault("DisplayViewIndex") ?? 1) == 0
                && !Convert.ToBoolean(alg.InputValues.GetValueOrDefault("KeepAnnotatedImage") ?? true),
                $"View={alg.InputValues.GetValueOrDefault("DisplayViewIndex")} "
                + $"Keep={alg.InputValues.GetValueOrDefault("KeepAnnotatedImage")}");

            string? imagePath = acq.InputValues.TryGetValue("FilePath", out object? p) ? p as string : null;
            if (imagePath != null && !Path.IsPathRooted(imagePath))
                imagePath = Path.Combine(Path.GetDirectoryName(vmsPath)!, imagePath);
            Check($"{flowName}：图像路径存在", imagePath != null && File.Exists(imagePath),
                imagePath ?? "(null)");
            if (imagePath == null || !File.Exists(imagePath))
                return;

            var compiled = new FlowCompiler(workspace).Compile(flow.Steps.ToArray(), flow.FlowName);
            Check($"{flowName}：流程编译通过（端口名/连线类型）", compiled.Success,
                compiled.Success ? "" : string.Join(" | ", compiled.Errors.Select(e => e.Message)));
            if (!compiled.Success)
                return;

            HOperatorSet.ReadImage(out HObject raw, imagePath);
            using var image = new HImage(raw);
            raw.Dispose();

            var plugin = new DeformableMatchingPlugin { InstanceName = "样例_" + flowName };
            try
            {
                plugin.Initialize(alg);
                plugin.Image.LinkedSource = null;
                plugin.Image.Value = image;
                plugin.Execute(MakeContext(new StubLog()));

                Check($"{flowName}：真插件执行成功", plugin.Success.TypedValue is true,
                    plugin.ErrorMessage.TypedValue);
                Check($"{flowName}：至少 1 个命中", plugin.MatchCount.TypedValue >= 1,
                    $"MatchCount={plugin.MatchCount.TypedValue}");
                Check($"{flowName}：最高分 ≥ {minScore:0.0}", plugin.Score.TypedValue >= minScore,
                    $"Score={plugin.Score.TypedValue:0.######}");
                Check($"{flowName}：模型从 .vms 加载后只读一次",
                    plugin.ModelLoadCount == 1, $"ModelLoadCount={plugin.ModelLoadCount}");
                Check($"{flowName}：模型载荷非空",
                    !string.IsNullOrEmpty(plugin.Entry.Model) && plugin.Entry.Model.Length > 1000,
                    $"Model={plugin.Entry.Model.Length} chars");
            }
            finally
            {
                plugin.Dispose();
            }
        }

        // ==================================================================
        //  宿主最小实现
        // ==================================================================

        private static ExecutionContext MakeContext(ILogService log) =>
            new(log, new FlowSession { FlowName = "可形变匹配断言" }, new WorkspaceContext(),
                new System.Threading.CancellationTokenSource().Token);

        internal sealed class FakeStepData : IStepConfigData
        {
            public Guid StepId { get; } = Guid.NewGuid();
            public string Icon { get; } = "";
            public string StepName { get; } = "Fake";
            public string Description { get; } = "";
            public Dictionary<string, object> InputValues { get; } = new();
            public void SetInputValue(string key, object value) => InputValues[key] = value;
            public void RemoveInputValue(string key) => InputValues.Remove(key);
            public bool IsLinked(string inputPortName) => false;
            public string GetLinkedAddress(string inputPortName) => null;
            public LinkReference GetLink(string inputPortName) => null;
            public void SetLink(string inputPortName, LinkReference link) { }
            public void RemoveLink(string inputPortName) { }
            public List<DynamicPortInfo> OutputPortDefinitions { get; set; } = new();
        }
    }
}
