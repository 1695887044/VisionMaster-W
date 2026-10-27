using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Windows.Media.Imaging;
using Core.Interfaces;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VisionMaster.Models;
using VisionMaster.Models.Assets;
using VisionMaster.ViewModels;

namespace VisionMaster.Services
{
    /// <summary>
    /// 项目资产只读目录：把方案里的标定/模板/模型/配方/图像投影成资产页的展示模型。
    /// 这里只读，不改任何方案数据；模板/模型/图像的写入在 ProjectAssetImporter。
    /// </summary>
    public sealed class SolutionProjectAssetCatalog
    {
        private readonly IReadOnlyWorkspaceContext _workspace;

        public SolutionProjectAssetCatalog(IReadOnlyWorkspaceContext workspace)
        {
            _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        }

        public IReadOnlyList<ProjectAssetItem> Build()
        {
            var solution = _workspace.CurrentSolution;
            if (solution == null)
                return Array.Empty<ProjectAssetItem>();

            solution.TemplateAssets ??= new List<TemplateAsset>();
            solution.ModelAssets ??= new List<ModelAsset>();
            solution.ImageAssets ??= new List<ImageAsset>();

            var steps = CollectSteps(solution).ToList();
            var items = new List<ProjectAssetItem>();

            AddCalibrations(solution, steps, items);
            AddStoredAssets(solution, steps, items);
            AddTopLevelRecipes(solution, steps, items);
            AddEmbeddedRecipes(solution, steps, items);
            AddDerivedImages(solution, steps, items);

            return items
                .OrderBy(x => KindOrder(x.Kind))
                .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static int KindOrder(string kind) => kind switch
        {
            "calibration" => 0,
            "template" => 1,
            "model" => 2,
            "recipe" => 3,
            "image" => 4,
            _ => 9,
        };

        private void AddCalibrations(SolutionModel solution, List<StepInfo> steps, List<ProjectAssetItem> items)
        {
            foreach (var asset in solution.Calibrations)
            {
                var refs = new List<AssetReference>();
                var readFlows = new List<string>();
                var writeFlows = new List<string>();
                var reads = 0;
                var writes = 0;

                foreach (var step in steps.Where(s => s.Plugin.Contains("CalibrationPlugin", StringComparison.Ordinal)))
                {
                    var catalogId = Text(step, "CatalogAssetId");
                    var catalogName = Text(step, "CatalogAssetName");
                    var savedName = Text(step, "SavedCalibrationName");
                    var matches = (!string.IsNullOrWhiteSpace(catalogId)
                                   && string.Equals(catalogId, asset.CalibrationId.ToString(), StringComparison.OrdinalIgnoreCase))
                                  || string.Equals(catalogName, asset.Name, StringComparison.OrdinalIgnoreCase)
                                  || string.Equals(savedName, asset.Name, StringComparison.OrdinalIgnoreCase);
                    if (!matches) continue;

                    if (Bool(step, "UseSavedCalibration"))
                    {
                        reads++;
                        readFlows.Add(step.Flow);
                        refs.Add(new AssetReference("flow", step.Flow + " / " + step.Step, "读取", step.Flow, step.Step));
                    }

                    if (Bool(step, "SaveToCatalog"))
                    {
                        writes++;
                        writeFlows.Add(step.Flow);
                        refs.Add(new AssetReference("flow", step.Flow + " / " + step.Step, "写入", step.Flow, step.Step));
                    }
                }

                var t = asset.Transform ?? new CalibrationTransform();
                var scope = FriendlyFlowName(readFlows.FirstOrDefault() ?? writeFlows.FirstOrDefault() ?? "方案级");
                var baseline = t.MmPerPixel == 1.0
                               && t.ResidualRmsPx == 0
                               && (asset.Description ?? string.Empty).Contains("预置", StringComparison.Ordinal);

                var item = new ProjectAssetItem
                {
                    Name = asset.Name,
                    Id = asset.CalibrationId.ToString(),
                    ShortId = ShortId(asset.CalibrationId),
                    Kind = "calibration",
                    Ownership = "方案级",
                    TypeLabel = t.Kind switch
                    {
                        CalibrationKind.PixelScale => "像素当量",
                        CalibrationKind.NinePoint => "九点标定",
                        CalibrationKind.Perspective => "透视标定",
                        CalibrationKind.Mesh => "网格标定",
                        _ => "标定",
                    },
                    Scope = scope,
                    Camera = string.IsNullOrWhiteSpace(t.CameraSerial) ? "—" : t.CameraSerial,
                    Version = "v" + Math.Max(1, asset.Version),
                    Status = ApplyLifecycle(solution, asset.CalibrationId, "有效"),
                    IsFrozen = asset.IsLocked,
                    RefsSummary = RefSummary(reads, writes),
                    Updated = FormatTime(t.CreatedAtUtc),
                    UpdatedSub = "方案标定资产",
                    IconGlyph = IconForKind("calibration"),
                    PreviewTitle = "标定变换 · " + t.Kind,
                    PreviewSub = $"{t.MmPerPixel:0.0000} mm/px · {t.SourceImageWidth}×{t.SourceImageHeight}",
                    IsCalibration = true,
                    SourcePath = "SolutionModel.Calibrations[" + asset.CalibrationId + "]",
                };

                item.Overview.Add(new AssetFact("资产类型", "标定资产"));
                item.Overview.Add(new AssetFact("标定方式", t.Kind.ToString()));
                item.Overview.Add(new AssetFact("像素当量", $"{t.MmPerPixel:0.0000} mm/px"));
                item.Overview.Add(new AssetFact("源图尺寸", $"{t.SourceImageWidth} × {t.SourceImageHeight}"));
                item.Overview.Add(new AssetFact("相机", item.Camera));
                item.Overview.Add(new AssetFact("残差 RMS", $"{t.ResidualRmsPx:0.0000} px"));
                item.Overview.Add(new AssetFact("数据来源", "SolutionModel.Calibrations"));
                if (baseline)
                    item.Overview.Add(new AssetFact("注意", "预置基线，运行 Calib 流程后才更新"));

                item.Associations.Add(new AssetAssociation("工位 / 作用域", scope, "方案内引用"));
                item.Associations.Add(new AssetAssociation("相机", item.Camera, "设备视图"));
                item.Associations.Add(new AssetAssociation("流程", string.Join(" / ", readFlows.Concat(writeFlows).Distinct()), "按 Id 引用"));
                item.Associations.Add(new AssetAssociation("SCADA", "—", "未建立绑定"));

                item.TechGroups.Add(new AssetTechGroup("标定结果", t.Kind.ToString(), new[]
                {
                    new AssetFact("MmPerPixel", t.MmPerPixel.ToString("0.000000", CultureInfo.InvariantCulture)),
                    new AssetFact("Matrix", t.Matrix == null ? "—" : string.Join(", ", t.Matrix.Select(v => v.ToString("0.###", CultureInfo.InvariantCulture)))),
                    new AssetFact("SourceTag", string.IsNullOrWhiteSpace(t.SourceTag) ? "—" : t.SourceTag),
                    new AssetFact("CreatedAtUtc", FormatTime(t.CreatedAtUtc)),
                }));
                item.TechGroups.Add(new AssetTechGroup("资产信息", "方案级", new[]
                {
                    new AssetFact("CalibrationId", item.Id),
                    new AssetFact("Version", item.Version),
                    new AssetFact("IsLocked", asset.IsLocked ? "true" : "false"),
                    new AssetFact("Description", string.IsNullOrWhiteSpace(asset.Description) ? "—" : asset.Description),
                }));

                foreach (var reference in refs)
                    item.References.Add(reference);

                item.Versions.Add(new AssetVersion(item.Version, baseline ? "基线" : "当前", FormatTime(t.CreatedAtUtc), "方案文件", "来自 SolutionModel.Calibrations"));
                AddVersionHistory(solution, item);
                item.IsDefault = IsDefaultAsset(solution, asset.CalibrationId);
                items.Add(item);
            }
        }

        private void AddStoredAssets(SolutionModel solution, List<StepInfo> steps, List<ProjectAssetItem> items)
        {
            foreach (var asset in solution.TemplateAssets)
                AddStoredAsset(solution, steps, items, asset, "template", "模板");

            foreach (var asset in solution.ModelAssets)
                AddStoredAsset(solution, steps, items, asset, "model", "模型");

            foreach (var asset in solution.ImageAssets)
                AddStoredAsset(solution, steps, items, asset, "image", "图像");
        }

        private void AddStoredAsset(
            SolutionModel solution,
            List<StepInfo> steps,
            List<ProjectAssetItem> items,
            SolutionAssetBase asset,
            string kind,
            string typeLabel)
        {
            var refs = new List<AssetReference>();
            var reads = 0;

            // 强匹配（插件 + 已知键名）与反向索引（任何输入值里出现 Id/路径/文件名）取并集：
            // 前者能抓到"名字匹配"这类语义引用，后者能抓到"换了个插件、用路径引用"这类漏网。
            foreach (var step in steps)
            {
                if (!MatchesStoredAsset(step, asset, kind))
                    continue;

                refs.Add(new AssetReference("flow", step.Flow + " / " + step.Step, "读取", step.Flow, step.Step));
            }

            foreach (var reference in FindReferences(solution, steps, asset))
            {
                if (refs.Any(r => string.Equals(r.Title, reference.Title, StringComparison.Ordinal)))
                    continue;
                refs.Add(reference);
            }

            reads = refs.Count;

            var relative = NormalizeRelative(asset.RelativePath);
            var absolute = ResolvePath(solution, relative);
            var exists = !string.IsNullOrWhiteSpace(absolute) && File.Exists(absolute);
            var status = string.IsNullOrWhiteSpace(asset.Status) ? "有效" : asset.Status;
            if (!exists)
                status = "缺失";

            var item = new ProjectAssetItem
            {
                Name = asset.Name,
                Id = asset.AssetId.ToString(),
                ShortId = ShortId(asset.AssetId),
                Kind = kind,
                Ownership = "方案级",
                TypeLabel = typeLabel,
                Scope = string.IsNullOrWhiteSpace(asset.Scope) ? "整线" : asset.Scope,
                Camera = string.IsNullOrWhiteSpace(asset.Camera) ? "—" : asset.Camera,
                Version = "v" + Math.Max(1, asset.Version),
                Status = ApplyLifecycle(solution, asset.AssetId, status),
                IsFrozen = asset.IsFrozen,
                RefsSummary = RefSummary(reads, 0),
                Updated = FormatTime(asset.UpdatedAtUtc),
                UpdatedSub = "方案资产",
                IconGlyph = IconForKind(kind),
                PreviewTitle = string.IsNullOrWhiteSpace(relative) ? asset.Name : Path.GetFileName(relative),
                PreviewSub = string.IsNullOrWhiteSpace(relative) ? "未绑定文件" : relative,
                Thumbnail = ProjectAssetExporter.TryLoadThumbnail(absolute),
                SourcePath = "SolutionModel." + (kind switch
                {
                    "template" => "TemplateAssets",
                    "model" => "ModelAssets",
                    _ => "ImageAssets",
                }) + "[" + asset.AssetId + "]",
                FilePath = relative,
                ContentHash = asset.ContentHash,
            };

            item.Overview.Add(new AssetFact("资产类型", typeLabel + "资产"));
            item.Overview.Add(new AssetFact("作用域", item.Scope));
            item.Overview.Add(new AssetFact("相机", item.Camera));
            item.Overview.Add(new AssetFact("文件", string.IsNullOrWhiteSpace(relative) ? "未绑定" : relative));
            item.Overview.Add(new AssetFact("文件存在", exists ? "是" : "否"));
            item.Overview.Add(new AssetFact("版本", item.Version));
            item.Overview.Add(new AssetFact("数据来源", item.SourcePath));

            if (asset is TemplateAsset template)
            {
                item.Overview.Add(new AssetFact("模板类型", template.TemplateKind));
                item.TechGroups.Add(new AssetTechGroup("模板参数", template.TemplateKind, new[]
                {
                    new AssetFact("TemplateKind", template.TemplateKind),
                    new AssetFact("Preview", string.IsNullOrWhiteSpace(template.PreviewRelativePath) ? "—" : template.PreviewRelativePath),
                }));
            }
            else if (asset is ModelAsset model)
            {
                item.Overview.Add(new AssetFact("模型类型", model.ModelKind));
                item.Overview.Add(new AssetFact("输入尺寸", $"{model.InputWidth} × {model.InputHeight}"));
                item.TechGroups.Add(new AssetTechGroup("模型参数", model.ModelKind, new[]
                {
                    new AssetFact("ModelKind", model.ModelKind),
                    new AssetFact("InputSize", $"{model.InputWidth} × {model.InputHeight}"),
                    new AssetFact("Labels", string.IsNullOrWhiteSpace(model.Labels) ? "—" : model.Labels),
                }));
            }
            else if (asset is ImageAsset image)
            {
                item.Overview.Add(new AssetFact("分辨率", $"{image.Width} × {image.Height}"));
                item.Overview.Add(new AssetFact("图像类型", image.ImageKind));
                item.TechGroups.Add(new AssetTechGroup("图像参数", image.ImageKind, new[]
                {
                    new AssetFact("Width", image.Width.ToString(CultureInfo.InvariantCulture)),
                    new AssetFact("Height", image.Height.ToString(CultureInfo.InvariantCulture)),
                    new AssetFact("SourceCamera", string.IsNullOrWhiteSpace(image.SourceCamera) ? "—" : image.SourceCamera),
                }));
            }

            item.TechGroups.Add(new AssetTechGroup("文件契约", "相对路径", new[]
            {
                new AssetFact("RelativePath", string.IsNullOrWhiteSpace(relative) ? "—" : relative),
                new AssetFact("ContentHash", string.IsNullOrWhiteSpace(asset.ContentHash) ? "—" : asset.ContentHash),
                new AssetFact("FileSize", asset.FileSize > 0 ? FormatBytes(asset.FileSize) : "—"),
                new AssetFact("CreatedAtUtc", FormatTime(asset.CreatedAtUtc)),
                new AssetFact("UpdatedAtUtc", FormatTime(asset.UpdatedAtUtc)),
            }));
            item.TechGroups.Add(new AssetTechGroup("资产信息", "方案级", new[]
            {
                new AssetFact("AssetId", item.Id),
                new AssetFact("Version", item.Version),
                new AssetFact("CreatedBy", string.IsNullOrWhiteSpace(asset.CreatedBy) ? "—" : asset.CreatedBy),
                new AssetFact("UpdatedBy", string.IsNullOrWhiteSpace(asset.UpdatedBy) ? "—" : asset.UpdatedBy),
            }));

            item.Associations.Add(new AssetAssociation("工位 / 作用域", item.Scope, "方案内引用"));
            item.Associations.Add(new AssetAssociation("相机", item.Camera, "设备视图"));
            item.Associations.Add(new AssetAssociation("流程", refs.Count == 0 ? "—" : refs[0].Title, refs.Count == 0 ? "未引用" : "按路径/名称引用"));
            item.Associations.Add(new AssetAssociation("SCADA", "—", "未建立绑定"));

            foreach (var reference in refs)
                item.References.Add(reference);

            item.Versions.Add(new AssetVersion(item.Version, "当前", FormatTime(asset.UpdatedAtUtc), "方案资产", "来自 " + item.SourcePath));
            AddVersionHistory(solution, item);
            item.IsDefault = IsDefaultAsset(solution, asset.AssetId);
            items.Add(item);
        }

        private static bool MatchesStoredAsset(StepInfo step, SolutionAssetBase asset, string kind)
        {
            if (kind == "image")
            {
                if (!step.Plugin.Contains("ImageAcquisitionPlugin", StringComparison.Ordinal)) return false;
                if (Guid.TryParse(Text(step, "ImageAssetId"), out var imageId)
                    && imageId == asset.AssetId)
                    return true;
                return string.Equals(NormalizeRelative(Text(step, "FilePath")), NormalizeRelative(asset.RelativePath), StringComparison.OrdinalIgnoreCase);
            }

            if (kind == "template")
            {
                // 可形变模板是单条目 DeformableTemplateEntry，不是 Matching 的模板库数组；
                // 只按 TemplateAssetId 认引用，不要落到 TemplateLibraryJson 的文本匹配。
                if (step.Plugin.Contains("DeformableMatchingPlugin", StringComparison.Ordinal))
                {
                    return Guid.TryParse(Text(step, "TemplateAssetId"), out var deformableId)
                        && deformableId == asset.AssetId;
                }

                if (!step.Plugin.Contains("MatchingPlugin", StringComparison.Ordinal)) return false;
                if (Guid.TryParse(Text(step, "TemplateAssetId"), out var templateId)
                    && templateId == asset.AssetId)
                    return true;
                if (string.Equals(Text(step, "DefaultTemplateName"), asset.Name, StringComparison.OrdinalIgnoreCase)) return true;
                return Text(step, "TemplateLibraryJson").Contains(asset.Name, StringComparison.OrdinalIgnoreCase);
            }

            if (kind == "model")
            {
                if (!step.Plugin.Contains("OcrPlugin", StringComparison.Ordinal)
                    && !step.Plugin.Contains("YoloPlugin", StringComparison.Ordinal)
                    && !step.Plugin.Contains("Dl", StringComparison.Ordinal))
                    return false;
                if (Guid.TryParse(Text(step, "ModelAssetId"), out var modelId)
                    && modelId == asset.AssetId)
                    return true;
                var path = NormalizeRelative(Text(step, "ModelPath"));
                return !string.IsNullOrWhiteSpace(path)
                       && (string.Equals(path, NormalizeRelative(asset.RelativePath), StringComparison.OrdinalIgnoreCase)
                           || string.Equals(Path.GetFileName(path), Path.GetFileName(asset.RelativePath), StringComparison.OrdinalIgnoreCase));
            }

            return false;
        }

        /// <summary>
        /// 反向索引：**任何**步骤的**任何**输入值里出现该资产的 Id / 相对路径 / 文件名 / 绝对路径，
        /// 都算一条引用。
        ///
        /// 为什么不能只按"已知插件 + 已知键名"匹配：那等于把引用关系写死在宿主里，
        /// 新插件、旧插件换个键名、或者用路径而不是 AssetId 引用资产，全都会被漏掉，
        /// 界面上显示"未引用"——而它其实正在产线上被用着。
        /// </summary>
        private static List<AssetReference> FindReferences(
            SolutionModel solution, List<StepInfo> steps, SolutionAssetBase asset)
        {
            var id = asset.AssetId.ToString();
            var relative = NormalizeRelative(asset.RelativePath);
            var fileName = string.IsNullOrWhiteSpace(relative) ? string.Empty : Path.GetFileName(relative);
            var absolute = ResolvePath(solution, relative);

            var refs = new List<AssetReference>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var step in steps)
            {
                var hit = string.Empty;
                foreach (var pair in step.Inputs)
                {
                    var text = Text(pair.Value);
                    if (string.IsNullOrWhiteSpace(text)) continue;

                    if (text.Contains(id, StringComparison.OrdinalIgnoreCase))
                    {
                        hit = "资产 Id";
                        break;
                    }

                    if (!string.IsNullOrWhiteSpace(absolute)
                        && text.Contains(absolute!, StringComparison.OrdinalIgnoreCase))
                    {
                        hit = "绝对路径";
                        break;
                    }

                    if (!string.IsNullOrWhiteSpace(relative)
                        && NormalizeRelative(text).Contains(relative!, StringComparison.OrdinalIgnoreCase))
                    {
                        hit = "相对路径";
                        break;
                    }

                    if (!string.IsNullOrWhiteSpace(fileName)
                        && text.Contains(fileName, StringComparison.OrdinalIgnoreCase))
                    {
                        hit = "文件名";
                        break;
                    }
                }

                if (hit.Length == 0) continue;

                var key = step.Flow + "/" + step.Step;
                if (!seen.Add(key)) continue;

                refs.Add(new AssetReference("flow", step.Flow + " / " + step.Step, "读取 · " + hit,
                    step.Flow, step.Step));
            }

            return refs;
        }

        private void AddTopLevelRecipes(SolutionModel solution, List<StepInfo> steps, List<ProjectAssetItem> items)
        {
            foreach (var recipe in solution.RecipesOrEmpty())
            {
                var refs = RecipeReferences(steps, recipe.Name);
                var item = new ProjectAssetItem
                {
                    Name = recipe.Name,
                    Id = recipe.RecipeId.ToString(),
                    ShortId = ShortId(recipe.RecipeId),
                    Kind = "recipe",
                    Ownership = "方案级",
                    TypeLabel = "配方",
                    Scope = recipe.Scope.ToString(),
                    Camera = "—",
                    Version = "v" + Math.Max(1, recipe.ContentVersion),
                    Status = ApplyLifecycle(solution, recipe.RecipeId,
                        recipe.ValidateContent() == null ? "有效" : "配置冲突"),
                    IsFrozen = recipe.IsFrozen,
                    RefsSummary = RefSummary(refs.Count, 0),
                    Updated = FormatTime(recipe.UpdatedAtUtc ?? recipe.CreatedAtUtc),
                    UpdatedSub = "方案配方",
                    IconGlyph = IconForKind("recipe"),
                    PreviewTitle = "配方 · " + recipe.Code,
                    PreviewSub = string.IsNullOrWhiteSpace(recipe.TextContent)
                        ? $"{recipe.Entries.Count} 条目 · {recipe.Points.Count} 点位"
                        : $"{RecipeFormatLabel(recipe.ContentFormat, recipe.TextContent)} · {recipe.TextContent.Split('\n').Length} 行",
                    RecipeContent = recipe.TextContent ?? string.Empty,
                    RecipeContentFormat = string.IsNullOrWhiteSpace(recipe.ContentFormat)
                        ? (string.IsNullOrWhiteSpace(recipe.TextContent) ? string.Empty : "text")
                        : recipe.ContentFormat,
                    SourcePath = "SolutionModel.Recipes[" + recipe.RecipeId + "]",
                };

                item.Overview.Add(new AssetFact("资产类型", "配方资产"));
                item.Overview.Add(new AssetFact("配方代码", string.IsNullOrWhiteSpace(recipe.Code) ? "—" : recipe.Code));
                item.Overview.Add(new AssetFact("作用域", item.Scope));
                item.Overview.Add(new AssetFact("条目数", recipe.Entries.Count.ToString(CultureInfo.InvariantCulture)));
                item.Overview.Add(new AssetFact("点位数", recipe.Points.Count.ToString(CultureInfo.InvariantCulture)));
                item.Overview.Add(new AssetFact("数据来源", "SolutionModel.Recipes"));
                item.TechGroups.Add(new AssetTechGroup("配方内容", recipe.Code, new[]
                {
                    new AssetFact("RecipeId", item.Id),
                    new AssetFact("ContentVersion", recipe.ContentVersion.ToString(CultureInfo.InvariantCulture)),
                    new AssetFact("CreatedBy", string.IsNullOrWhiteSpace(recipe.CreatedBy) ? "—" : recipe.CreatedBy),
                    new AssetFact("Notes", string.IsNullOrWhiteSpace(recipe.Notes) ? "—" : recipe.Notes),
                }));
                item.Associations.Add(new AssetAssociation("工位 / 作用域", item.Scope, "方案内引用"));
                item.Associations.Add(new AssetAssociation("流程", refs.Count == 0 ? "—" : refs[0].Title, refs.Count == 0 ? "未引用" : "按名称引用"));
                item.Associations.Add(new AssetAssociation("设备", "—", "配方参数由步骤消费"));
                item.Associations.Add(new AssetAssociation("SCADA", "—", "未建立绑定"));

                foreach (var reference in refs) item.References.Add(reference);
                item.Versions.Add(new AssetVersion(item.Version, "当前", FormatTime(recipe.UpdatedAtUtc ?? recipe.CreatedAtUtc), "方案配方", "来自 SolutionModel.Recipes"));
                AddVersionHistory(solution, item);
                item.IsDefault = IsDefaultAsset(solution, recipe.RecipeId);
                items.Add(item);
            }
        }

        private void AddEmbeddedRecipes(SolutionModel solution, List<StepInfo> steps, List<ProjectAssetItem> items)
        {
            var topNames = new HashSet<string>(
                solution.RecipesOrEmpty().Select(r => r.Name),
                StringComparer.OrdinalIgnoreCase);

            foreach (var step in steps.Where(s => s.Plugin.Contains("BeadInspectPlugin", StringComparison.Ordinal)))
            {
                var raw = Text(step, "RecipeLibraryJson");
                if (string.IsNullOrWhiteSpace(raw)) continue;

                JArray library;
                try { library = JArray.Parse(raw); }
                catch { continue; }

                foreach (var token in library.OfType<JObject>())
                {
                    var name = (string)token["Name"];
                    if (string.IsNullOrWhiteSpace(name) || topNames.Contains(name)) continue;
                    topNames.Add(name);

                    var refImage = (string)token["RefImagePath"] ?? "—";
                    var noBead = (string)token["RefNoBeadImagePath"] ?? "—";
                    var points = 0;
                    var pointsRaw = (string)token["RefPointsJson"];
                    if (!string.IsNullOrWhiteSpace(pointsRaw))
                    {
                        try { points = JArray.Parse(pointsRaw).Count; } catch { }
                    }

                    var item = new ProjectAssetItem
                    {
                        Name = name,
                        Id = "step:" + step.Flow + "/" + step.Step + "/" + name,
                        ShortId = "内嵌配方",
                        Kind = "recipe",
                        Ownership = "派生",
                        TypeLabel = "内嵌配方",
                        Scope = FriendlyFlowName(step.Flow),
                        Camera = "—",
                        Version = "—",
                        Status = "内嵌",
                        RefsSummary = "读1",
                        Updated = "—",
                        UpdatedSub = "插件内嵌 JSON",
                        IconGlyph = IconForKind("recipe"),
                        PreviewTitle = "BeadInspect · " + name,
                        PreviewSub = "内嵌在步骤配置中，不是 RecipeDefinition",
                        SourcePath = "step:" + step.Flow + "/" + step.Step + "/RecipeLibraryJson",
                    };

                    item.Overview.Add(new AssetFact("资产类型", "内嵌配方"));
                    item.Overview.Add(new AssetFact("参考图", refImage));
                    item.Overview.Add(new AssetFact("无胶参考图", noBead));
                    item.Overview.Add(new AssetFact("参考点数", points.ToString(CultureInfo.InvariantCulture)));
                    item.Overview.Add(new AssetFact("目标宽度", TextOf(token, "TargetWidth", "—")));
                    item.Overview.Add(new AssetFact("极性", TextOf(token, "Polarity", "—")));
                    item.Overview.Add(new AssetFact("数据来源", item.SourcePath));
                    item.TechGroups.Add(new AssetTechGroup("配方内容", "BeadInspect", new[]
                    {
                        new AssetFact("Name", name),
                        new AssetFact("RefImagePath", refImage),
                        new AssetFact("RefNoBeadImagePath", noBead),
                        new AssetFact("RefPoints", points.ToString(CultureInfo.InvariantCulture)),
                        new AssetFact("TargetWidth", TextOf(token, "TargetWidth", "—")),
                        new AssetFact("WidthTolerance", TextOf(token, "WidthTolerance", "—")),
                        new AssetFact("PositionTolerance", TextOf(token, "PositionTolerance", "—")),
                    }));
                    item.Associations.Add(new AssetAssociation("工位 / 作用域", step.Flow, "插件步骤"));
                    item.Associations.Add(new AssetAssociation("流程", step.Flow + " / " + step.Step, "读取"));
                    item.Associations.Add(new AssetAssociation("设备", "—", "由步骤消费"));
                    item.Associations.Add(new AssetAssociation("SCADA", "—", "未建立绑定"));
                    item.References.Add(new AssetReference("flow", step.Flow + " / " + step.Step, "读取", step.Flow, step.Step));
                    items.Add(item);
                }
            }
        }

        private void AddDerivedImages(SolutionModel solution, List<StepInfo> steps, List<ProjectAssetItem> items)
        {
            var storedPaths = new HashSet<string>(
                solution.ImageAssets.Select(a => NormalizeRelative(a.RelativePath)),
                StringComparer.OrdinalIgnoreCase);

            var byPath = new Dictionary<string, List<StepInfo>>(StringComparer.OrdinalIgnoreCase);
            foreach (var step in steps.Where(s => s.Plugin.Contains("ImageAcquisitionPlugin", StringComparison.Ordinal)))
            {
                if (!string.IsNullOrWhiteSpace(Text(step, "ImageAssetId")))
                    continue;
                var path = NormalizeRelative(Text(step, "FilePath"));
                if (string.IsNullOrWhiteSpace(path) || storedPaths.Contains(path)) continue;
                if (!byPath.TryGetValue(path, out var list))
                    byPath[path] = list = new List<StepInfo>();
                list.Add(step);
            }

            foreach (var pair in byPath.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
            {
                var path = pair.Key;
                var refs = pair.Value;
                var absolute = ResolvePath(solution, path);
                var exists = !string.IsNullOrWhiteSpace(absolute) && File.Exists(absolute);
                var size = exists ? new FileInfo(absolute!).Length : 0;
                var hash = exists ? HashFile(absolute!) : "—";
                var width = 0;
                var height = 0;
                if (exists)
                {
                    try
                    {
                        using var stream = File.OpenRead(absolute!);
                        var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
                        width = frame.PixelWidth;
                        height = frame.PixelHeight;
                    }
                    catch { }
                }

                var name = Path.GetFileName(path);
                var item = new ProjectAssetItem
                {
                    Name = name,
                    Id = "file:" + path.Replace('\\', '/'),
                    ShortId = "文件引用",
                    Kind = "image",
                    Ownership = "派生",
                    TypeLabel = path.Contains("ref", StringComparison.OrdinalIgnoreCase) ? "参考图" : "采集图",
                    Scope = FriendlyFlowName(refs[0].Flow),
                    Camera = "—",
                    Version = "—",
                    Status = exists ? "派生" : "缺失",
                    RefsSummary = "读" + refs.Count,
                    Updated = exists ? FormatTime(File.GetLastWriteTimeUtc(absolute!)) : "—",
                    UpdatedSub = "流程采集引用",
                    IconGlyph = IconForKind("image"),
                    PreviewTitle = name,
                    PreviewSub = path,
                    Thumbnail = ProjectAssetExporter.TryLoadThumbnail(absolute),
                    SourcePath = "ImageAcquisitionPlugin.FilePath",
                    FilePath = path,
                    ContentHash = hash,
                };

                item.Overview.Add(new AssetFact("资产类型", "派生图像"));
                item.Overview.Add(new AssetFact("文件", path));
                item.Overview.Add(new AssetFact("存在", exists ? "是" : "否"));
                item.Overview.Add(new AssetFact("大小", size > 0 ? FormatBytes(size) : "—"));
                item.Overview.Add(new AssetFact("分辨率", width > 0 ? $"{width} × {height}" : "—"));
                item.Overview.Add(new AssetFact("SHA-256", hash));
                item.Overview.Add(new AssetFact("来源", "ImageAcquisitionPlugin.FilePath"));
                item.TechGroups.Add(new AssetTechGroup("文件契约", "相对方案目录", new[]
                {
                    new AssetFact("RelativePath", path),
                    new AssetFact("ContentHash", hash),
                    new AssetFact("FileSize", size > 0 ? FormatBytes(size) : "—"),
                    new AssetFact("LastWriteUtc", exists ? FormatTime(File.GetLastWriteTimeUtc(absolute!)) : "—"),
                }));
                item.Associations.Add(new AssetAssociation("工位 / 作用域", item.Scope, "流程采集步骤"));
                item.Associations.Add(new AssetAssociation("流程", refs[0].Flow + " / " + refs[0].Step, "读取"));
                item.Associations.Add(new AssetAssociation("相机", "—", "未绑定设备"));
                item.Associations.Add(new AssetAssociation("SCADA", "—", "未建立绑定"));
                foreach (var step in refs)
                    item.References.Add(new AssetReference("flow", step.Flow + " / " + step.Step, "读取", step.Flow, step.Step));

                items.Add(item);
            }
        }

        private static List<AssetReference> RecipeReferences(List<StepInfo> steps, string name)
        {
            var refs = new List<AssetReference>();
            foreach (var step in steps.Where(s => s.Plugin.Contains("BeadInspectPlugin", StringComparison.Ordinal)))
            {
                if (!string.Equals(Text(step, "DefaultRecipeName"), name, StringComparison.OrdinalIgnoreCase)
                    && !Text(step, "RecipeLibraryJson").Contains(name, StringComparison.OrdinalIgnoreCase))
                    continue;
                refs.Add(new AssetReference("flow", step.Flow + " / " + step.Step, "读取", step.Flow, step.Step));
            }
            return refs;
        }

        private static List<StepInfo> CollectSteps(SolutionModel solution)
        {
            var list = new List<StepInfo>();
            foreach (var flow in solution.Flows)
                Walk(flow.FlowName, flow.Steps, list);
            return list;
        }

        private static void Walk(string flowName, IEnumerable<StepModel> steps, List<StepInfo> list)
        {
            foreach (var step in steps)
            {
                list.Add(new StepInfo(
                    flowName,
                    step.StepName,
                    (step.PluginTypeName ?? string.Empty).Split(',')[0],
                    step.InputValues ?? new Dictionary<string, object>()));
                if (step is IContainerStep container)
                    foreach (var branch in container.Children)
                        Walk(flowName, branch.Steps, list);
            }
        }

        private static string ResolvePath(SolutionModel solution, string relative)
        {
            if (string.IsNullOrWhiteSpace(relative)) return string.Empty;
            var root = SolutionPaths.GetSolutionRoot(solution.SolutionFilePath);
            if (string.IsNullOrWhiteSpace(root)) return string.Empty;
            try { return Path.GetFullPath(Path.Combine(root!, relative)); }
            catch { return string.Empty; }
        }

        private static string NormalizeRelative(string path)
            => string.IsNullOrWhiteSpace(path) ? string.Empty : path.Trim().Replace('/', '\\').TrimStart('.', '\\');

        private static bool IsDefaultAsset(SolutionModel solution, Guid assetId)
            => solution.AssetDefaults?.Any(p => p.AssetId != Guid.Empty && p.AssetId == assetId) == true;

        /// <summary>生命周期状态优先于资产自身 Status（有效/已归档）；没有记录 = 有效。</summary>
        private static string ApplyLifecycle(SolutionModel solution, Guid assetId, string fallback)
        {
            var state = solution.AssetLifecycle?.FirstOrDefault(s => s.AssetId == assetId);
            return state == null || string.IsNullOrWhiteSpace(state.State) ? fallback : state.State;
        }

        private static void AddVersionHistory(SolutionModel solution, ProjectAssetItem item)
        {
            if (!Guid.TryParse(item.Id, out var assetId))
                return;

            // 走 AssetVersionLog.Of：**库优先、.vms 兜底**的合并读。
            // 直接遍历 solution.AssetVersionLog 只有 .vms 里那 50 条，
            // 库里多出来的历史（最多 200 条）在界面上看不到。
            foreach (var record in AssetVersionLog.Of(solution, assetId))
            {
                item.Versions.Add(new AssetVersion(
                    "v" + Math.Max(1, record.Version),
                    record.Action,
                    FormatTime(record.CreatedAtUtc),
                    string.IsNullOrWhiteSpace(record.Actor) ? "—" : record.Actor,
                    string.IsNullOrWhiteSpace(record.Note) ? "—" : record.Note)
                {
                    RecordId = record.RecordId.ToString(),
                });
            }
        }

        private static string RecipeFormatLabel(string? format, string? textContent)
        {
            if (string.IsNullOrWhiteSpace(textContent)) return "参数配方";
            return (format ?? string.Empty).ToLowerInvariant() switch
            {
                "bead" => "胶路配方",
                "text" or "" => "文本配方",
                _ => format + " 配方",
            };
        }

        private static string FriendlyFlowName(string flow) => flow switch
        {
            "ST1_LoadCode" => "ST1 · 上料读码",
            "ST2_Position" => "ST2 · 视觉定位",
            "ST3_Measure" => "ST3 · 精密测量",
            "ST4_Surface" => "ST4 · 表面检测",
            "ST5_Assembly" => "ST5 · 装配压装",
            "ST6_Trace" => "ST6 · 识别追溯",
            _ => flow,
        };

        private static string Text(StepInfo step, string key)
            => step.Inputs.TryGetValue(key, out var value) ? Text(value) : string.Empty;

        private static string Text(object value)
        {
            switch (value)
            {
                case null: return string.Empty;
                case string s: return s;
                case JValue j: return j.ToString(CultureInfo.InvariantCulture);
                case JArray a: return a.ToString(Formatting.None);
                case JObject o: return o.ToString(Formatting.None);
                case IFormattable f: return f.ToString(null, CultureInfo.InvariantCulture);
                default: return value.ToString() ?? string.Empty;
            }
        }

        private static bool Bool(StepInfo step, string key)
            => step.Inputs.TryGetValue(key, out var value)
               && (value is bool b ? b : bool.TryParse(Text(value), out var parsed) && parsed);

        private static string TextOf(JObject token, string key, string fallback)
        {
            var value = token[key];
            return value == null || value.Type == JTokenType.Null ? fallback : value.ToString();
        }

        private static string RefSummary(int reads, int writes)
            => reads == 0 && writes == 0 ? "未引用" : $"读{reads} · 写{writes}";

        private static string ShortId(Guid id)
        {
            var text = id.ToString("N");
            return text.Length <= 8 ? text : text[..8] + "…";
        }

        private static string FormatTime(DateTime value)
            => value == default ? "—" : value.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

        private static string FormatTime(DateTime? value)
            => value.HasValue ? FormatTime(value.Value) : "—";

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " KB";
            return (bytes / 1024.0 / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
        }

        private static string HashFile(string path)
        {
            try
            {
                using var stream = File.OpenRead(path);
                using var sha = SHA256.Create();
                return Convert.ToHexString(sha.ComputeHash(stream));
            }
            catch { return "—"; }
        }

        private static string IconForKind(string kind) => kind switch
        {
            "template" => "\uf0c5",
            "model" => "\uf1b2",
            "recipe" => "\uf0f6",
            "image" => "\uf03e",
            _ => "\uf0ad",
        };

        private sealed class StepInfo
        {
            public StepInfo(string flow, string step, string plugin, Dictionary<string, object> inputs)
            {
                Flow = flow;
                Step = step;
                Plugin = plugin;
                Inputs = inputs;
            }

            public string Flow { get; }
            public string Step { get; }
            public string Plugin { get; }
            public Dictionary<string, object> Inputs { get; }
        }
    }
}
