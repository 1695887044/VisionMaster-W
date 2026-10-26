using System;
using System.IO;

namespace VisionMaster.Services
{
    /// <summary>
    /// 方案自带资产的目录约定：每个方案用自己的文件夹维护图片、报警历史、生产历史与日志。
    ///
    /// 目录形状（以 <c>D:\Lines\Line1\Line1.vms</c> 为例）：
    /// <code>
    /// Line1\
    ///   Line1.vms
    ///   Image\                  ← 方案图片（Hmi.Picture 等）
    ///   Alarms\
    ///     AlarmHistory.json     ← 历史面板恢复快照
    ///     AlarmHistory-*.csv    ← 可读的报警流水
    ///   History\                ← 生产历史 CSV 账本（数据表格「历史源」读它）
    ///   Logs\                   ← 方案级日志（与全局日志双写）
    /// </code>
    ///
    /// 命名与根目录解析的唯一事实源是 <see cref="SolutionPaths"/>；本类只负责“另存为时搬资产”。
    ///
    /// “另存为”时把旧方案目录下的 <c>Image</c> / <c>Alarms</c> / <c>History</c> / <c>Logs</c> 一起带到新目录：
    /// 用户复制的是“这个方案”，不是只复制一个 .vms 文件。目标已有同名文件时不覆盖——
    /// 另存为到已有方案目录时，静默盖掉对方的资产比不复制更危险。
    /// </summary>
    public static class SolutionAssetFolders
    {
        /// <summary>方案目录下随方案走的资产子目录（与 <see cref="SolutionPaths"/> 同一份名字）</summary>
        public static readonly string[] AssetFolderNames =
        {
            SolutionPaths.ImageFolderName,
            SolutionPaths.AssetsFolderName,
            SolutionPaths.AlarmsFolderName,
            SolutionPaths.HistoryFolderName,
            SolutionPaths.LogsFolderName
        };

        /// <summary>
        /// 另存为后把旧方案目录的资产复制到新方案目录。任一目录为空/相同则不做任何事。
        /// 返回复制失败的项（空 = 全部成功或无需复制）；失败只报给调用方，不抛。
        /// </summary>
        public static string[] CopyAlongside(string? previousSolutionPath, string? newSolutionPath)
        {
            if (string.IsNullOrWhiteSpace(previousSolutionPath)
                || string.IsNullOrWhiteSpace(newSolutionPath))
                return Array.Empty<string>();

            var oldRoot = SolutionPaths.GetSolutionRoot(previousSolutionPath);
            var newRoot = SolutionPaths.GetSolutionRoot(newSolutionPath);
            if (string.IsNullOrWhiteSpace(oldRoot) || string.IsNullOrWhiteSpace(newRoot))
                return Array.Empty<string>();

            if (string.Equals(Path.GetFullPath(oldRoot!), Path.GetFullPath(newRoot!), StringComparison.OrdinalIgnoreCase))
                return Array.Empty<string>();

            var failures = new System.Collections.Generic.List<string>();

            foreach (var folderName in AssetFolderNames)
            {
                var source = Path.Combine(oldRoot, folderName);
                if (!Directory.Exists(source))
                    continue;

                var target = Path.Combine(newRoot, folderName);
                try
                {
                    CopyDirectory(source, target, overwrite: false);
                }
                catch (Exception ex)
                {
                    failures.Add($"{folderName}: {ex.Message}");
                }
            }

            return failures.ToArray();
        }

        private static void CopyDirectory(string source, string target, bool overwrite)
        {
            Directory.CreateDirectory(target);

            foreach (var file in Directory.GetFiles(source))
            {
                var destination = Path.Combine(target, Path.GetFileName(file));
                File.Copy(file, destination, overwrite);
            }

            foreach (var directory in Directory.GetDirectories(source))
            {
                CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)), overwrite);
            }
        }
    }
}
