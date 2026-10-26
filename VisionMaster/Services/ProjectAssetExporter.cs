using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Windows.Media.Imaging;
using VisionMaster.Models;
using VisionMaster.Models.Assets;

namespace VisionMaster.Services
{
    /// <summary>
    /// 资产打包导出：把资产元数据 + 文件本体打成 zip。
    /// 只读方案，不改任何状态；目标文件已存在时先写临时文件再替换。
    /// </summary>
    public sealed class ProjectAssetExporter
    {
        private static readonly string[] PreviewExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif" };

        private readonly IReadOnlyWorkspaceContext _workspace;

        public ProjectAssetExporter(IReadOnlyWorkspaceContext workspace)
        {
            _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        }

        /// <summary>
        /// 导出一份资产包。`assetIds` 为空 = 当前页签全部资产（用 `kindFilter` 限类型）。
        /// 返回实际写进包的文件数与清单。
        /// </summary>
        public bool TryExport(
            IEnumerable<Guid>? assetIds,
            string targetZipPath,
            out int fileCount,
            out string manifest,
            out string error)
        {
            fileCount = 0;
            manifest = string.Empty;
            error = string.Empty;

            var solution = _workspace.CurrentSolution;
            if (solution == null)
            {
                error = "当前没有打开方案";
                return false;
            }

            if (string.IsNullOrWhiteSpace(targetZipPath))
            {
                error = "导出路径为空";
                return false;
            }

            var wanted = new HashSet<Guid>(assetIds ?? Enumerable.Empty<Guid>());
            var root = SolutionPaths.GetSolutionRoot(solution.SolutionFilePath);
            var entries = new List<(string Kind, Guid Id, string Name, string RelativePath, string Hash, int Version)>();

            foreach (var asset in solution.TemplateAssets)
                Add("template", asset);
            foreach (var asset in solution.ModelAssets)
                Add("model", asset);
            foreach (var asset in solution.ImageAssets)
                Add("image", asset);

            void Add(string kind, SolutionAssetBase asset)
            {
                if (wanted.Count > 0 && !wanted.Contains(asset.AssetId)) return;
                entries.Add((kind, asset.AssetId, asset.Name, asset.RelativePath, asset.ContentHash, asset.Version));
            }

            // 文本配方没有文件，但内容就在方案里 —— 导出必须带上，
            // 否则"导出资产包"会把配方悄悄漏掉（标定是纯数值，仍不参与打包）。
            var textRecipes = solution.RecipesOrEmpty()
                .Where(r => !string.IsNullOrWhiteSpace(r.TextContent))
                .Where(r => wanted.Count == 0 || wanted.Contains(r.RecipeId))
                .ToList();

            if (entries.Count == 0 && textRecipes.Count == 0)
            {
                error = "没有可导出的资产（标定是纯数值，没有文件也不打包）";
                return false;
            }

            var builder = new StringBuilder();
            builder.AppendLine("项目资产包");
            builder.AppendLine("方案: " + solution.SolutionName);
            builder.AppendLine("导出时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            builder.AppendLine("资产数: " + (entries.Count + textRecipes.Count));
            builder.AppendLine();
            builder.AppendLine("类型\t名称\tId\t版本\t文件\tSHA-256");

            foreach (var recipe in textRecipes)
            {
                builder.AppendLine(string.Join("\t",
                    "recipe", recipe.Name, recipe.RecipeId, "v" + Math.Max(1, recipe.ContentVersion),
                    "（内联文本）", string.Empty));
            }

            try
            {
                var directory = Path.GetDirectoryName(Path.GetFullPath(targetZipPath));
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);

                var temp = targetZipPath + ".tmp";
                if (File.Exists(temp)) File.Delete(temp);

                using (var archive = ZipFile.Open(temp, ZipArchiveMode.Create))
                {
                    foreach (var entry in entries)
                    {
                        builder.AppendLine(string.Join("\t",
                            entry.Kind, entry.Name, entry.Id, "v" + Math.Max(1, entry.Version),
                            entry.RelativePath, entry.Hash));

                        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(entry.RelativePath))
                            continue;

                        var absolute = Path.GetFullPath(Path.Combine(root!, entry.RelativePath));
                        if (!File.Exists(absolute))
                            continue;

                        // zip 内部路径分隔符必须是 '/'：Path.Combine 在 Windows 给的是 '\'，
                        // 写成反斜杠后解压工具会把它当文件名的一部分（一个叫 "files\image\x.png" 的条目）。
                        var entryName = "files/" + entry.Kind + "/"
                            + entry.Id.ToString("N") + Path.GetExtension(absolute);
                        archive.CreateEntryFromFile(absolute, entryName, CompressionLevel.Optimal);
                        fileCount++;
                    }

                    foreach (var recipe in textRecipes)
                    {
                        var entry = archive.CreateEntry(
                            "files/recipe/" + recipe.RecipeId.ToString("N") + ".txt",
                            CompressionLevel.Optimal);
                        using var recipeWriter = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                        recipeWriter.Write(recipe.TextContent);
                        fileCount++;
                    }

                    var manifestEntry = archive.CreateEntry("manifest.tsv", CompressionLevel.Optimal);
                    using var writer = new StreamWriter(manifestEntry.Open(), new UTF8Encoding(false));
                    writer.Write(builder.ToString());
                }

                if (File.Exists(targetZipPath))
                {
                    try { File.Delete(targetZipPath); }
                    catch (IOException) { File.Replace(temp, targetZipPath, null); }
                }
                else
                {
                    File.Move(temp, targetZipPath);
                }

                manifest = builder.ToString();
                return true;
            }
            catch (Exception ex)
            {
                error = "导出失败：" + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// 读缩略图：图像/模板（图片）直接解码；其它返回 null（调用方显示图标）。
        /// 用 OnLoad 一次性解码，不锁文件 —— 资产文件随时可能在外部被替换。
        /// </summary>
        public static BitmapSource? TryLoadThumbnail(string? absolutePath, int decodePixelWidth = 240)
        {
            if (string.IsNullOrWhiteSpace(absolutePath) || !File.Exists(absolutePath))
                return null;

            if (!PreviewExtensions.Contains(Path.GetExtension(absolutePath), StringComparer.OrdinalIgnoreCase))
                return null;

            try
            {
                using var stream = File.OpenRead(absolutePath);
                var decoder = BitmapDecoder.Create(
                    stream,
                    BitmapCreateOptions.PreservePixelFormat,
                    BitmapCacheOption.OnLoad);

                var frame = decoder.Frames[0];
                if (frame.PixelWidth <= decodePixelWidth)
                    return frame;

                var scaled = new TransformedBitmap(
                    frame,
                    new System.Windows.Media.ScaleTransform(
                        (double)decodePixelWidth / frame.PixelWidth,
                        (double)decodePixelWidth / frame.PixelWidth));
                scaled.Freeze();
                return scaled;
            }
            catch
            {
                return null;
            }
        }
    }
}
