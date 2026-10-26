using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;
using VisionMaster.Models;
using VisionMaster.Models.Assets;

namespace VisionMaster.Services
{
    /// <summary>
    /// 模板/模型/图像资产的导入器：文件复制到方案目录，写元数据到 SolutionModel。
    /// 不负责保存 .vms；调用方刷新目录并提示“保存方案后落盘”。
    /// </summary>
    public sealed class ProjectAssetImporter
    {
        private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif" };
        private static readonly string[] TextRecipeExtensions = { ".txt", ".csv", ".tbl", ".json" };
        private static readonly string[] TemplateExtensions = { ".json", ".tpl", ".tmpl", ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".xml", ".yaml", ".yml", ".txt" };
        private static readonly string[] ModelExtensions = { ".onnx", ".pt", ".pth", ".bin", ".model", ".json", ".engine", ".trt" };

        private readonly IReadOnlyWorkspaceContext _workspace;

        public ProjectAssetImporter(IReadOnlyWorkspaceContext workspace)
        {
            _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        }

        public bool TryImport(string sourcePath, string kind, out Guid assetId, out string error)
        {
            assetId = Guid.Empty;
            error = string.Empty;

            var solution = _workspace.CurrentSolution;
            if (solution == null)
            {
                error = "当前没有打开方案";
                return false;
            }

            if (string.IsNullOrWhiteSpace(solution.SolutionFilePath))
            {
                error = "请先保存方案，再导入资产（资产文件要放进方案目录）";
                return false;
            }

            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                error = "源文件不存在";
                return false;
            }

            var root = SolutionPaths.GetSolutionRoot(solution.SolutionFilePath);
            if (string.IsNullOrWhiteSpace(root))
            {
                error = "方案路径无效";
                return false;
            }

            solution.TemplateAssets ??= new List<TemplateAsset>();
            solution.ModelAssets ??= new List<ModelAsset>();
            solution.ImageAssets ??= new List<ImageAsset>();

            var extension = Path.GetExtension(sourcePath);
            if (!IsAllowed(kind, extension))
            {
                error = $"不支持的文件类型「{extension}」";
                return false;
            }

            // 文本配方：内容进方案（RecipeDefinition.TextContent），**不拷文件**。
            // 拷一份文件的话同一份配方会有"方案里的文本"和"磁盘上的文件"两个真相，
            // 冻结/版本/审计全要同时管两处 —— 而插件运行期只读方案里那一份。
            if (kind == "recipe")
                return TryImportTextRecipe(solution, sourcePath, out assetId, out error);

            var folder = kind switch
            {
                "template" => Path.Combine(root!, SolutionPaths.AssetsFolderName, "Templates"),
                "model" => Path.Combine(root!, SolutionPaths.AssetsFolderName, "Models"),
                "image" => Path.Combine(root!, SolutionPaths.ImageFolderName),
                _ => string.Empty,
            };
            if (string.IsNullOrWhiteSpace(folder))
            {
                error = "当前页签不支持导入；请切换到模板 / 模型 / 图像";
                return false;
            }

            try
            {
                Directory.CreateDirectory(folder);
                var hash = HashFile(sourcePath);
                var existing = FindByHash(solution, kind, hash);
                if (existing != null)
                {
                    error = $"相同内容已存在：{existing.Name}";
                    return false;
                }

                var targetName = MakeUniqueFileName(folder, Path.GetFileName(sourcePath));
                var targetPath = Path.Combine(folder, targetName);
                var tempPath = Path.Combine(folder, ".tmp-" + Guid.NewGuid().ToString("N") + extension);
                try
                {
                    File.Copy(sourcePath, tempPath, overwrite: true);
                    File.Move(tempPath, targetPath);
                }
                finally
                {
                    if (File.Exists(tempPath))
                    {
                        try { File.Delete(tempPath); } catch { }
                    }
                }

                var relative = Path.GetRelativePath(root!, targetPath);
                var info = new FileInfo(targetPath);
                var now = DateTime.UtcNow;
                var user = Environment.UserName;

                // ONNX 元数据：导入时读一次，资产页就能显示输入尺寸与标签；
                // 读不出来不算导入失败（非 ONNX、模型缺 metadata 都合法）。
                var onnx = kind == "model" && extension.Equals(".onnx", StringComparison.OrdinalIgnoreCase)
                    ? ReadOnnxMetadata(targetPath)
                    : null;

                SolutionAssetBase asset = kind switch
                {
                    "template" => new TemplateAsset
                    {
                        Name = UniqueAssetName(solution.TemplateAssets.Select(a => a.Name), Path.GetFileNameWithoutExtension(targetName)),
                        TemplateKind = extension.Equals(".json", StringComparison.OrdinalIgnoreCase) ? "模板库" : "模板文件",
                    },
                    "model" => new ModelAsset
                    {
                        Name = UniqueAssetName(solution.ModelAssets.Select(a => a.Name), Path.GetFileNameWithoutExtension(targetName)),
                        ModelKind = extension.Equals(".onnx", StringComparison.OrdinalIgnoreCase) ? "ONNX" : "模型文件",
                        InputWidth = onnx?.InputWidth ?? 0,
                        InputHeight = onnx?.InputHeight ?? 0,
                        Labels = onnx?.Labels ?? string.Empty,
                    },
                    _ => new ImageAsset
                    {
                        Name = UniqueAssetName(solution.ImageAssets.Select(a => a.Name), Path.GetFileNameWithoutExtension(targetName)),
                        ImageKind = "标准图",
                        Width = ReadImageWidth(targetPath),
                        Height = ReadImageHeight(targetPath),
                    },
                };

                asset.RelativePath = relative;
                asset.ContentHash = hash;
                asset.FileSize = info.Length;
                asset.Scope = "整线";
                asset.Camera = "—";
                asset.Version = 1;
                asset.Status = "有效";
                asset.CreatedAtUtc = now;
                asset.UpdatedAtUtc = now;
                asset.CreatedBy = user;
                asset.UpdatedBy = user;
                asset.Description = "由项目资产页导入";

                switch (asset)
                {
                    case TemplateAsset template:
                        solution.TemplateAssets ??= new List<TemplateAsset>();
                        solution.TemplateAssets.Add(template);
                        break;
                    case ModelAsset model:
                        solution.ModelAssets ??= new List<ModelAsset>();
                        solution.ModelAssets.Add(model);
                        break;
                    case ImageAsset image:
                        solution.ImageAssets ??= new List<ImageAsset>();
                        solution.ImageAssets.Add(image);
                        break;
                }

                assetId = asset.AssetId;

                // 每个版本都在 Versions/{kind}/{id}/v{n}{ext} 留一份文件本体：
                // 回滚就是把那份复制回活动路径。只存哈希的话"回滚"只能是空话。
                ArchiveVersionFile(root!, kind, asset.AssetId, asset.Version, targetPath);
                return true;
            }
            catch (Exception ex)
            {
                error = "导入资产失败：" + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// 给已有资产导入一个新版本：旧文件进版本库，活动文件换成新的，版本号 +1。
        /// 内容与当前版本相同时直接拒绝（"重导一次没变化"不该白占一个版本号）。
        /// </summary>
        public bool TryImportNewVersion(Guid assetId, string sourcePath, out int newVersion, out string error)
        {
            newVersion = 0;
            error = string.Empty;

            var solution = _workspace.CurrentSolution;
            if (solution == null) { error = "当前没有打开方案"; return false; }
            if (string.IsNullOrWhiteSpace(solution.SolutionFilePath))
            {
                error = "请先保存方案，再导入资产（资产文件要放进方案目录）";
                return false;
            }
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                error = "源文件不存在";
                return false;
            }

            var root = SolutionPaths.GetSolutionRoot(solution.SolutionFilePath);
            if (string.IsNullOrWhiteSpace(root)) { error = "方案路径无效"; return false; }

            var asset = FindStored(solution, assetId);
            if (asset == null)
            {
                error = "只有模板 / 模型 / 图像资产支持导入新版本";
                return false;
            }

            var kind = KindOf(solution, asset);
            var extension = Path.GetExtension(sourcePath);
            if (!IsAllowed(kind, extension))
            {
                error = $"不支持的文件类型「{extension}」";
                return false;
            }

            try
            {
                var hash = HashFile(sourcePath);
                if (string.Equals(hash, asset.ContentHash, StringComparison.OrdinalIgnoreCase))
                {
                    error = "新文件与当前版本内容相同，没有产生新版本";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(asset.RelativePath))
                {
                    error = "资产没有绑定活动文件，无法替换";
                    return false;
                }

                var activePath = Path.GetFullPath(Path.Combine(root!, asset.RelativePath));
                if (!File.Exists(activePath))
                {
                    error = "活动文件不存在：" + asset.RelativePath;
                    return false;
                }

                // 先把旧文件收进版本库（失败就不动活动文件，避免"旧的没了新的没进来"）。
                if (!ArchiveVersionFile(root!, kind, asset.AssetId, asset.Version, activePath, out var archiveError))
                {
                    error = archiveError;
                    return false;
                }

                var tempPath = activePath + ".tmp-" + Guid.NewGuid().ToString("N");
                try
                {
                    File.Copy(sourcePath, tempPath, overwrite: true);
                    File.Move(tempPath, activePath, overwrite: true);
                }
                finally
                {
                    if (File.Exists(tempPath))
                    {
                        try { File.Delete(tempPath); } catch { }
                    }
                }

                var info = new FileInfo(activePath);
                var now = DateTime.UtcNow;
                newVersion = Math.Max(1, asset.Version) + 1;

                asset.Version = newVersion;
                asset.ContentHash = hash;
                asset.FileSize = info.Length;
                asset.UpdatedAtUtc = now;
                asset.UpdatedBy = Environment.UserName;
                asset.Status = "有效";

                ArchiveVersionFile(root!, kind, asset.AssetId, newVersion, activePath);
                return true;
            }
            catch (Exception ex)
            {
                error = "导入新版本失败：" + ex.Message;
                return false;
            }
        }

        /// <summary>版本文件相对路径：`Assets/Versions/{kind}/{assetId}/v{n}{ext}`。</summary>
        public static string VersionFileRelativePath(string kind, Guid assetId, int version, string extension)
            => string.Join("/",
                SolutionPaths.AssetsFolderName,
                SolutionPaths.AssetVersionsFolderName,
                kind,
                assetId.ToString("N"),
                "v" + Math.Max(1, version) + extension);

        /// <summary>把某版本的文件放进版本库；已存在同内容就跳过（重复导入不报错）。</summary>
        private static bool ArchiveVersionFile(string root, string kind, Guid assetId, int version, string sourceFile)
            => ArchiveVersionFile(root, kind, assetId, version, sourceFile, out _);

        private static bool ArchiveVersionFile(
            string root, string kind, Guid assetId, int version, string sourceFile, out string error)
        {
            error = string.Empty;
            try
            {
                var relative = VersionFileRelativePath(kind, assetId, version, Path.GetExtension(sourceFile));
                var target = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
                var folder = Path.GetDirectoryName(target);
                if (!string.IsNullOrWhiteSpace(folder)) Directory.CreateDirectory(folder!);

                if (File.Exists(target))
                {
                    var existing = HashFile(target);
                    var incoming = HashFile(sourceFile);
                    if (string.Equals(existing, incoming, StringComparison.OrdinalIgnoreCase)) return true;
                }

                var temp = target + ".tmp-" + Guid.NewGuid().ToString("N");
                try
                {
                    File.Copy(sourceFile, temp, overwrite: true);
                    File.Move(temp, target, overwrite: true);
                }
                finally
                {
                    if (File.Exists(temp))
                    {
                        try { File.Delete(temp); } catch { }
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                error = "归档旧版本失败：" + ex.Message;
                return false;
            }
        }

        internal static SolutionAssetBase? FindStored(SolutionModel solution, Guid assetId)
        {
            foreach (var item in solution.TemplateAssets.Cast<SolutionAssetBase>())
                if (item.AssetId == assetId) return item;
            foreach (var item in solution.ModelAssets.Cast<SolutionAssetBase>())
                if (item.AssetId == assetId) return item;
            foreach (var item in solution.ImageAssets.Cast<SolutionAssetBase>())
                if (item.AssetId == assetId) return item;
            return null;
        }

        internal static string KindOf(SolutionModel solution, SolutionAssetBase asset)
        {
            if (solution.TemplateAssets.Contains(asset as TemplateAsset)) return "template";
            if (solution.ModelAssets.Contains(asset as ModelAsset)) return "model";
            if (solution.ImageAssets.Contains(asset as ImageAsset)) return "image";
            return string.Empty;
        }

        private static bool IsAllowed(string kind, string extension)
        {
            var exts = kind switch
            {
                "image" => ImageExtensions,
                "template" => TemplateExtensions,
                "model" => ModelExtensions,
                "recipe" => TextRecipeExtensions,
                _ => Array.Empty<string>(),
            };
            return exts.Contains(extension, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 导入配方：
        /// - `.json` → 先按胶路配方库（`BeadRecipeEntry` 数组）试解析，成功则 ContentFormat=bead；
        /// - 其它（.txt/.csv/.tbl）→ 文本配方 ContentFormat=text。
        /// 两种都是"内容进方案、不拷文件"。
        /// </summary>
        private static bool TryImportTextRecipe(
            SolutionModel solution, string sourcePath, out Guid assetId, out string error)
        {
            assetId = Guid.Empty;
            error = string.Empty;

            try
            {
                var text = File.ReadAllText(sourcePath, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(text))
                {
                    error = "配方文件是空的";
                    return false;
                }

                var name = Path.GetFileNameWithoutExtension(sourcePath);
                var recipes = solution.EnsureRecipes();
                if (recipes.Any(r => string.Equals(r.TextContent, text, StringComparison.Ordinal)))
                {
                    error = "相同内容的配方已存在";
                    return false;
                }

                var format = Path.GetExtension(sourcePath).Equals(".json", StringComparison.OrdinalIgnoreCase)
                             && LooksLikeBeadLibrary(text)
                    ? "bead"
                    : "text";

                var now = DateTime.UtcNow;
                var recipe = new RecipeDefinition
                {
                    Name = UniqueRecipeName(recipes.Select(r => r.Name), name),
                    Code = UniqueRecipeName(recipes.Select(r => r.Code), name),
                    TextContent = text,
                    ContentFormat = format,
                    CreatedAtUtc = now,
                    CreatedBy = Environment.UserName,
                };
                recipes.Add(recipe);
                assetId = recipe.RecipeId;
                return true;
            }
            catch (Exception ex)
            {
                error = "导入配方失败：" + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// 判"是不是胶路配方库"：JSON 数组，且每条都有 Name + RefPointsJson。
        /// 不深解析整个 schema（那是插件的事），只认足以区分格式的形状。
        /// </summary>
        private static bool LooksLikeBeadLibrary(string text)
        {
            try
            {
                var token = Newtonsoft.Json.Linq.JToken.Parse(text);
                if (token is not Newtonsoft.Json.Linq.JArray array || array.Count == 0) return false;

                return array.OfType<Newtonsoft.Json.Linq.JObject>()
                    .All(o => o["Name"] != null && o["RefPointsJson"] != null);
            }
            catch
            {
                return false;
            }
        }

        private static string UniqueRecipeName(IEnumerable<string> existing, string name)
        {
            var taken = new HashSet<string>(existing ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            var candidate = string.IsNullOrWhiteSpace(name) ? "文本配方" : name.Trim();
            if (!taken.Contains(candidate)) return candidate;

            for (var i = 2; i < 1000; i++)
            {
                var next = candidate + " (" + i + ")";
                if (!taken.Contains(next)) return next;
            }

            return candidate + " (" + Guid.NewGuid().ToString("N")[..6] + ")";
        }

        private static SolutionAssetBase FindByHash(SolutionModel solution, string kind, string hash)
        {
            return kind switch
            {
                "template" => solution.TemplateAssets.FirstOrDefault(a => string.Equals(a.ContentHash, hash, StringComparison.OrdinalIgnoreCase)),
                "model" => solution.ModelAssets.FirstOrDefault(a => string.Equals(a.ContentHash, hash, StringComparison.OrdinalIgnoreCase)),
                "image" => solution.ImageAssets.FirstOrDefault(a => string.Equals(a.ContentHash, hash, StringComparison.OrdinalIgnoreCase)),
                _ => null,
            };
        }

        private static string MakeUniqueFileName(string folder, string fileName)
        {
            var name = string.IsNullOrWhiteSpace(fileName) ? "asset" : fileName;
            var target = Path.Combine(folder, name);
            if (!File.Exists(target)) return name;

            var stem = Path.GetFileNameWithoutExtension(name);
            var ext = Path.GetExtension(name);
            for (var i = 2; i < 10000; i++)
            {
                var candidate = $"{stem} ({i}){ext}";
                if (!File.Exists(Path.Combine(folder, candidate)))
                    return candidate;
            }
            return Guid.NewGuid().ToString("N") + ext;
        }

        private static string UniqueAssetName(IEnumerable<string> existing, string name)
        {
            var used = new HashSet<string>(existing ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            if (!used.Contains(name)) return name;
            for (var i = 2; i < 10000; i++)
            {
                var candidate = $"{name} ({i})";
                if (!used.Contains(candidate)) return candidate;
            }
            return name + " " + Guid.NewGuid().ToString("N")[..6];
        }

        private static string HashFile(string path)
        {
            using var stream = File.OpenRead(path);
            using var sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(stream));
        }

        /// <summary>
        /// 读 ONNX 输入尺寸与标签。**用反射调用 ONNX Runtime**，不建立编译期依赖：
        /// 断言宿主 / 精简部署里没有 Microsoft.ML.OnnxRuntime 时，这里静默返回 null，
        /// 不让"读元数据"把导入弄挂（模型文件本身是好的，只是没人能读它的形状）。
        /// </summary>
        private static (int InputWidth, int InputHeight, string Labels)? ReadOnnxMetadata(string path)
        {
            try
            {
                var runtimeType = Type.GetType(
                    "Microsoft.ML.OnnxRuntime.InferenceSession, Microsoft.ML.OnnxRuntime", throwOnError: false);
                if (runtimeType == null) return null;

                var optionsType = Type.GetType(
                    "Microsoft.ML.OnnxRuntime.SessionOptions, Microsoft.ML.OnnxRuntime", throwOnError: false);
                var options = optionsType == null ? null : Activator.CreateInstance(optionsType);

                object session;
                try
                {
                    session = options == null
                        ? Activator.CreateInstance(runtimeType, path)!
                        : Activator.CreateInstance(runtimeType, path, options)!;
                }
                finally
                {
                    (options as IDisposable)?.Dispose();
                }

                try
                {
                    var inputMeta = runtimeType.GetProperty("InputMetadata")?.GetValue(session) as System.Collections.IDictionary;
                    var dims = Array.Empty<int>();
                    if (inputMeta != null)
                    {
                        foreach (System.Collections.DictionaryEntry entry in inputMeta)
                        {
                            var meta = entry.Value;
                            var shape = meta?.GetType().GetProperty("Dimensions")?.GetValue(meta) as int[];
                            if (shape != null && shape.Length > 0) { dims = shape; break; }
                        }
                    }

                    var spatial = dims.Where(d => d > 1).OrderByDescending(d => d).ToArray();
                    var width = spatial.Length > 0 ? spatial[0] : 0;
                    var height = spatial.Length > 1 ? spatial[1] : width;

                    var labels = string.Empty;
                    var modelMeta = runtimeType.GetProperty("ModelMetadata")?.GetValue(session);
                    var custom = modelMeta?.GetType().GetProperty("CustomMetadataMap")?.GetValue(modelMeta)
                                 as IDictionary<string, string>;
                    if (custom != null)
                    {
                        foreach (var key in new[] { "names", "classes", "labels" })
                        {
                            if (!custom.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
                                continue;

                            // 常见两种写法：{0: 'person', 1: 'car'} 或 ['person', 'car']
                            var quoted = System.Text.RegularExpressions.Regex
                                .Matches(raw, "'([^']+)'|\"([^\"]+)\"")
                                .Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value)
                                .ToArray();
                            labels = quoted.Length > 0 ? string.Join(",", quoted) : raw.Trim();
                            break;
                        }
                    }

                    return (width, height, labels);
                }
                finally
                {
                    (session as IDisposable)?.Dispose();
                }
            }
            catch
            {
                return null;
            }
        }

        private static int ReadImageWidth(string path)        {
            try
            {
                using var stream = File.OpenRead(path);
                return BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0].PixelWidth;
            }
            catch { return 0; }
        }

        private static int ReadImageHeight(string path)
        {
            try
            {
                using var stream = File.OpenRead(path);
                return BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0].PixelHeight;
            }
            catch { return 0; }
        }
    }
}
