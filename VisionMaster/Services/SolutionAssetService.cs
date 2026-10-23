using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VisionMaster.Services
{
    /// <summary>
    /// 方案自带图片资产：拖入即复制进 <c>&lt;方案目录&gt;\Image\</c>，<c>.vms</c> 只记相对路径。
    ///
    /// <b>为什么不做全局图片库</b>：现场交接的是方案文件夹，不是一台机器；全局库会让
    /// “拷到另一台机器”缺图，而且多方案互相覆盖。目录约定与
    /// <see cref="SolutionAssetFolders"/> 保持一致（Image / Alarms 随方案走）。
    ///
    /// <b>命名与查重</b>：
    /// ① 同名同内容（SHA-256 相同）→ 复用已有文件，不产生副本；
    /// ② 同名不同内容 → <c>name (2).ext</c>，绝不覆盖（覆盖会悄悄改掉别的页面正在用的图）；
    /// ③ 不同名但内容相同 → 也复用（同一张图拖两次不该占两份空间）。
    ///
    /// <b>写入</b>：先写同目录临时文件再 <see cref="File.Move(string, string)"/>，
    /// 避免“复制到一半断电”留下半张图；失败时删掉临时文件。
    ///
    /// <b>格式</b>：png / jpg / jpeg / bmp / gif（WPF 原生可解码）。
    /// SVG 不在 v1 支持范围（WPF 没有内置 SVG 解码器，需要额外依赖；先让用户导出位图）。
    /// </summary>
    public static class SolutionAssetService
    {
        /// <summary>方案目录下随方案走的图片子目录（与 <see cref="SolutionPaths"/> 同名）</summary>
        public const string ImageFolderName = SolutionPaths.ImageFolderName;

        private static readonly string[] SupportedExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif" };

        /// <summary>解码缓存：键 = 绝对路径 + 最后写入时间，避免切页/重绘反复解码大图</summary>
        private static readonly Dictionary<string, (DateTime Stamp, ImageSource Image)> Cache = new(StringComparer.OrdinalIgnoreCase);

        private static readonly object CacheGate = new();

        /// <summary>可选的对话框扩展名过滤器（设计器选图用）</summary>
        public static string FileDialogFilter =>
            "图片 (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif|所有文件 (*.*)|*.*";

        /// <summary>该扩展名是否受支持（含点，大小写不敏感）</summary>
        public static bool IsSupportedExtension(string? extension)
            => !string.IsNullOrWhiteSpace(extension)
               && SupportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 把“方案配置文件路径”或“方案根目录”统一成方案根目录。
        ///
        /// 为什么两种都收：调用方手里不一定有 `.vms` 路径——
        /// 运行窗口与属性面板只有方案目录（<c>SolutionRoot</c>），编辑器画布与保存链有文件路径。
        /// 只按“是不是 .vms”判，不靠 <c>Directory.Exists</c>：方案还没保存时目录可能不存在，
        /// 那种情况下仍然要把图片导进 `<根>\Image`。
        /// </summary>
        public static string? NormalizeSolutionRoot(string? solutionFilePathOrRoot)
        {
            if (string.IsNullOrWhiteSpace(solutionFilePathOrRoot))
                return null;

            try
            {
                return string.Equals(Path.GetExtension(solutionFilePathOrRoot), SolutionPaths.ConfigExtension,
                           StringComparison.OrdinalIgnoreCase)
                    ? SolutionPaths.GetSolutionRoot(solutionFilePathOrRoot)
                    : Path.GetFullPath(solutionFilePathOrRoot!);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 把一张外部图片导入当前方案，返回写进 <c>.vms</c> 的相对路径（形如 <c>Image\pid.png</c>）。
        ///
        /// <paramref name="solutionFilePath"/> 既可以是方案配置文件路径，也可以是方案根目录
        /// （见 <see cref="NormalizeSolutionRoot"/>）。
        ///
        /// 方案还没保存（没有路径）时拒绝导入：没有方案目录就
        /// 没有“跟着方案走”的落点，硬塞到软件目录只会让下一次另存为漏掉它。
        /// </summary>
        public static bool TryImport(
            string? solutionFilePath,
            string sourceFilePath,
            out string relativePath,
            out string error)
        {
            relativePath = string.Empty;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(solutionFilePath))
            {
                error = "方案还没保存：先保存方案，再选择图片（图片要放进方案自己的 Image 目录）";
                return false;
            }

            if (string.IsNullOrWhiteSpace(sourceFilePath) || !File.Exists(sourceFilePath))
            {
                error = "源图片不存在";
                return false;
            }

            var extension = Path.GetExtension(sourceFilePath);
            if (!IsSupportedExtension(extension))
            {
                error = $"不支持的图片格式「{extension}」；支持 png / jpg / jpeg / bmp / gif";
                return false;
            }

            var solutionRoot = NormalizeSolutionRoot(solutionFilePath);
            if (string.IsNullOrWhiteSpace(solutionRoot))
            {
                error = "方案路径无效";
                return false;
            }

            var imageDirectory = Path.Combine(solutionRoot!, ImageFolderName);

            try
            {
                Directory.CreateDirectory(imageDirectory);

                var sourceHash = ComputeHash(sourceFilePath);

                // ① 内容相同 → 复用（先按大小筛，避免每张图都读一遍）
                var sourceSize = new FileInfo(sourceFilePath).Length;
                foreach (var candidate in Directory.EnumerateFiles(imageDirectory))
                {
                    if (!IsSupportedExtension(Path.GetExtension(candidate)))
                        continue;

                    var info = new FileInfo(candidate);
                    if (info.Length != sourceSize)
                        continue;

                    if (!string.Equals(ComputeHash(candidate), sourceHash, StringComparison.OrdinalIgnoreCase))
                        continue;

                    relativePath = ImageFolderName + "\\" + Path.GetFileName(candidate);
                    return true;
                }

                // ② 选一个不冲突的名字
                var targetName = Path.GetFileName(sourceFilePath);
                var targetPath = Path.Combine(imageDirectory, targetName);

                if (File.Exists(targetPath))
                {
                    var stem = Path.GetFileNameWithoutExtension(targetName);
                    var ext = Path.GetExtension(targetName);
                    for (var index = 2; index < 10000; index++)
                    {
                        var candidateName = $"{stem} ({index}){ext}";
                        var candidatePath = Path.Combine(imageDirectory, candidateName);
                        if (!File.Exists(candidatePath))
                        {
                            targetName = candidateName;
                            targetPath = candidatePath;
                            break;
                        }
                    }
                }

                // ③ 临时文件 + Move：半写不会留下一个“看起来存在、其实坏掉”的文件
                var tempPath = Path.Combine(imageDirectory, ".tmp-" + Guid.NewGuid().ToString("N") + extension);
                try
                {
                    File.Copy(sourceFilePath, tempPath, overwrite: true);
                    File.Move(tempPath, targetPath);
                }
                finally
                {
                    if (File.Exists(tempPath))
                    {
                        try { File.Delete(tempPath); } catch { /* 清理失败不影响主流程 */ }
                    }
                }

                relativePath = ImageFolderName + "\\" + targetName;
                return true;
            }
            catch (Exception ex)
            {
                error = "复制图片失败：" + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// 相对路径 → 绝对路径。拒绝绝对路径、盘符与 <c>..</c> 越界，
        /// 保证 <c>.vms</c> 里的引用永远落在方案目录内。
        ///
        /// <paramref name="solutionFilePath"/> 既可以是方案配置文件路径，也可以是方案根目录。
        /// </summary>
        public static bool TryResolve(
            string? solutionFilePath,
            string? relativePath,
            out string absolutePath,
            out string error)
        {
            absolutePath = string.Empty;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(solutionFilePath))
            {
                error = "方案还没保存";
                return false;
            }

            if (string.IsNullOrWhiteSpace(relativePath))
            {
                error = "没有设置图片";
                return false;
            }

            if (Path.IsPathRooted(relativePath))
            {
                error = "只允许方案内相对路径";
                return false;
            }

            var solutionRoot = NormalizeSolutionRoot(solutionFilePath);
            if (string.IsNullOrWhiteSpace(solutionRoot))
            {
                error = "方案路径无效";
                return false;
            }

            var imageRoot = Path.GetFullPath(Path.Combine(solutionRoot!, ImageFolderName));
            var candidate = Path.GetFullPath(Path.Combine(solutionRoot!, relativePath));

            if (!candidate.StartsWith(imageRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(candidate, imageRoot, StringComparison.OrdinalIgnoreCase))
            {
                error = "图片必须位于方案的 Image 目录内";
                return false;
            }

            absolutePath = candidate;
            return true;
        }

        /// <summary>
        /// 加载方案图片（带缓存）。失败返回 false + 中文原因，调用方负责回退占位/背景色。
        /// 必须在 UI 线程调用（<see cref="BitmapImage"/> 创建后可 Freeze，跨线程读取安全）。
        /// </summary>
        public static bool TryLoad(
            string? solutionFilePath,
            string? relativePath,
            out ImageSource? image,
            out string error)
        {
            image = null;

            if (!TryResolve(solutionFilePath, relativePath, out var absolutePath, out error))
                return false;

            if (!File.Exists(absolutePath))
            {
                error = "图片文件不存在：" + relativePath;
                return false;
            }

            try
            {
                var stamp = File.GetLastWriteTimeUtc(absolutePath);

                lock (CacheGate)
                {
                    if (Cache.TryGetValue(absolutePath, out var cached) && cached.Stamp == stamp)
                    {
                        image = cached.Image;
                        return true;
                    }
                }

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad; // 立刻读进内存，不锁文件
                bitmap.UriSource = new Uri(absolutePath, UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze();

                lock (CacheGate)
                    Cache[absolutePath] = (stamp, bitmap);

                image = bitmap;
                return true;
            }
            catch (Exception ex)
            {
                error = "图片解码失败：" + ex.Message;
                return false;
            }
        }

        /// <summary>清缓存（方案关闭/删除图片后调用；断言里也用它隔离用例）</summary>
        public static void ClearCache()
        {
            lock (CacheGate)
                Cache.Clear();
        }

        private static string ComputeHash(string path)
        {
            using var stream = File.OpenRead(path);
            using var sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(stream));
        }
    }
}
