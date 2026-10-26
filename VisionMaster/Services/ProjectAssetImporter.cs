using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
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
                return true;
            }
            catch (Exception ex)
            {
                error = "导入资产失败：" + ex.Message;
                return false;
            }
        }

        private static bool IsAllowed(string kind, string extension)
        {
            var exts = kind switch
            {
                "image" => ImageExtensions,
                "template" => TemplateExtensions,
                "model" => ModelExtensions,
                _ => Array.Empty<string>(),
            };
            return exts.Contains(extension, StringComparer.OrdinalIgnoreCase);
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
