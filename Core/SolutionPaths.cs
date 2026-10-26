using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace VisionMaster.Services
{
    /// <summary>
    /// 方案文件夹的目录约定（唯一事实源）。
    ///
    /// 一个方案 = 一个以方案命名的文件夹，配置、图片、报警、日志都在里面：
    /// <code>
    /// &lt;保存位置&gt;\方案A\
    ///   方案A.vms                  ← 配置（沿用既有 JSON 格式，字段不变）
    ///   Image\                     ← 方案图片（.vms 里只存相对路径）
    ///   Alarms\                    ← 报警历史快照与 CSV 流水
    ///   History\                   ← 生产历史 CSV 账本
    ///   Logs\                      ← 这份方案运行期间的日志（与全局日志双写）
    ///   Users\                     ← 方案级账号本（ScadaUsers/ScadaGroups/ScadaPermissions.json）
    /// </code>
    ///
    /// 为什么把规则收在一个静态类里
    /// ---------
    /// “方案根目录”有五个消费者：保存（建目录）、打开（按 .vms 所在目录解析资产）、
    /// 日志（写 &lt;根&gt;\Logs）、报警历史（写 &lt;根&gt;\Alarms）、账号本（写 &lt;根&gt;\Users）。五处各写一遍
    /// <c>Path.GetDirectoryName</c> 与目录名常量，改一处漏三处的症状是“资产找不到”，
    /// 而那时已经过去几个版本。这里只留一份命名与解析规则。
    ///
    /// 兼容
    /// ---------
    /// 打开老方案（.vms 直接放在某个目录里，没有同名文件夹）仍然可用：
    /// 方案根 = .vms 所在目录。文件夹化只约束“新保存”的落点，不改变读取口径。
    /// </summary>
    public static class SolutionPaths
    {
        /// <summary>方案配置文件的扩展名（含点）</summary>
        public const string ConfigExtension = ".vms";

        /// <summary>方案图片子目录名</summary>
        public const string ImageFolderName = "Image";

        /// <summary>方案内容资产子目录名（模板 / 模型）。</summary>
        public const string AssetsFolderName = "Assets";

        /// <summary>报警历史子目录名</summary>
        public const string AlarmsFolderName = "Alarms";

        /// <summary>
        /// 生产历史（CSV 账本）子目录名。数据表格的「历史源」从这里读，数据记录插件也可以指到这里。
        /// 与报警历史分开：报警是事件流水，生产记录是逐件结果，两者的保留期与查看入口都不同。
        /// </summary>
        public const string HistoryFolderName = "History";

        /// <summary>方案级日志子目录名</summary>
        public const string LogsFolderName = "Logs";

        /// <summary>
        /// 方案级账号本子目录名。里面有三个文件（B3）：
        /// <c>ScadaUsers.json</c>（账号：Name/Salt/Hash/Iterations/GroupId，<b>没有 Role</b>）、
        /// <c>ScadaGroups.json</c>（组 + Permissions）、<c>ScadaPermissions.json</c>（权限目录，≤32）。
        /// 放子目录而不是根文件：另存为只复制目录，手工在资源管理器里整目录复制方案时账号会跟着走。
        /// 刻意<b>不</b>进 SolutionAssetFolders.AssetFolderNames —— 账号必须由存储层按「当前内存态」写，
        /// 盲目复制旧文件会让「另存为后新旧方案共用一份哈希」这种分叉悄悄发生（见 SF18/SF19 保持 4 项）。
        /// </summary>
        public const string UsersFolderName = "Users";

        /// <summary>方案名不可用时的兜底名</summary>
        public const string FallbackName = "未命名方案";

        /// <summary>保存时自动创建的子目录（顺序即展示顺序）</summary>
        public static readonly string[] StructureFolderNames =
        {
            ImageFolderName,
            AssetsFolderName,
            AlarmsFolderName,
            HistoryFolderName,
            LogsFolderName,
            UsersFolderName
        };

        /// <summary>
        /// Windows 保留设备名：<c>CON\PRN\AUX\NUL\COM1..9\LPT1..9</c>。
        /// 它们不能作为文件夹名（<c>CreateDirectory</c> 抛 IOException），必须前置下划线。
        /// </summary>
        private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

        /// <summary>
        /// 方案名 → 可安全用作文件夹/文件名的名字。
        /// 非法字符换成 <c>_</c>；去掉首尾空白与结尾的空格/点（Windows 会静默吞掉结尾的空格与点，
        /// 导致“界面显示方案A.、磁盘上却是方案A”）；空名/保留设备名给兜底。
        /// </summary>
        public static string SanitizeName(string? solutionName)
        {
            var raw = (solutionName ?? string.Empty).Trim();
            if (raw.Length == 0)
                return FallbackName;

            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(raw.Length);
            foreach (var ch in raw)
                sb.Append(Array.IndexOf(invalid, ch) >= 0 ? '_' : ch);

            // TrimEnd 传多个字符时按“任意一个”反复剥离：方案A . → 方案A
            var cleaned = sb.ToString().Trim().TrimEnd(' ', '.');
            if (cleaned.Length == 0)
                return FallbackName;

            // 保留设备名按“第一个点之前的词干”判：CON.txt 同样不能作文件名。
            var stem = cleaned.Split('.')[0];
            if (ReservedNames.Contains(cleaned) || ReservedNames.Contains(stem))
                cleaned = "_" + cleaned;

            return cleaned;
        }

        /// <summary>方案文件夹全路径：<c>&lt;保存位置&gt;\&lt;净化后的方案名&gt;</c></summary>
        public static string GetSolutionFolder(string parentDirectory, string solutionName)
        {
            if (string.IsNullOrWhiteSpace(parentDirectory))
                throw new ArgumentException("保存位置不能为空", nameof(parentDirectory));

            return Path.Combine(Path.GetFullPath(parentDirectory), SanitizeName(solutionName));
        }

        /// <summary>方案配置文件全路径：<c>&lt;保存位置&gt;\&lt;方案名&gt;\&lt;方案名&gt;.vms</c></summary>
        public static string GetConfigFilePath(string parentDirectory, string solutionName)
            => Path.Combine(GetSolutionFolder(parentDirectory, solutionName), SanitizeName(solutionName) + ConfigExtension);

        /// <summary>
        /// 方案根目录 = 配置文件所在目录。空路径/无效路径返回 null。
        /// 这是读取侧的唯一口径：图片、报警、日志都相对它解析。
        /// </summary>
        public static string? GetSolutionRoot(string? solutionFilePath)
        {
            if (string.IsNullOrWhiteSpace(solutionFilePath))
                return null;

            try
            {
                var root = Path.GetDirectoryName(Path.GetFullPath(solutionFilePath!));
                return string.IsNullOrWhiteSpace(root) ? null : root;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>方案根目录下的某个资产子目录；没有方案根时返回 null</summary>
        public static string? GetAssetDirectory(string? solutionFilePath, string folderName)
        {
            if (string.IsNullOrWhiteSpace(folderName))
                return null;

            var root = GetSolutionRoot(solutionFilePath);
            return root == null ? null : Path.Combine(root, folderName);
        }

        /// <summary>方案级日志目录：<c>&lt;方案根&gt;\Logs</c></summary>
        public static string? GetLogDirectory(string? solutionFilePath)
            => GetAssetDirectory(solutionFilePath, LogsFolderName);

        /// <summary>生产历史目录：<c>&lt;方案根&gt;\History</c>（数据表格历史源从这里读 CSV）</summary>
        public static string? GetHistoryDirectory(string? solutionFilePath)
            => GetAssetDirectory(solutionFilePath, HistoryFolderName);

        /// <summary>
        /// 这个配置文件是否已经符合文件夹约定：<c>&lt;父&gt;\&lt;方案名&gt;\&lt;方案名&gt;.vms</c>。
        ///
        /// 用途：打开散装老方案后点“保存”，不能继续写回那个散装文件——那等于把
        /// “一个方案一个文件夹”这条约定从第一次保存就破掉。判否时保存走“另存为”，
        /// 由用户选一个保存位置，落成规范文件夹。读侧不受影响（老文件照样能打开）。
        /// </summary>
        public static bool IsFolderLayout(string? solutionFilePath, string solutionName)
        {
            var root = GetSolutionRoot(solutionFilePath);
            if (root == null)
                return false;

            var expected = SanitizeName(solutionName);
            var folderName = Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var fileName = Path.GetFileNameWithoutExtension(solutionFilePath);

            return string.Equals(folderName, expected, StringComparison.OrdinalIgnoreCase)
                   && string.Equals(fileName, expected, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 校验“保存到 &lt;保存位置&gt;\&lt;方案名&gt;\&lt;方案名&gt;.vms”是否可行。
        /// 三条拒绝：保存位置为空 / 同名路径被文件占用 / 目标文件夹里已有**另一个**方案的 .vms。
        /// 同一方案的重复保存是正常覆盖，不拒绝。
        /// </summary>
        public static bool TryValidateSaveTarget(
            string parentDirectory,
            string solutionName,
            out string solutionFolder,
            out string configFilePath,
            out string error)
        {
            solutionFolder = string.Empty;
            configFilePath = string.Empty;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(parentDirectory))
            {
                error = "没有选择保存位置";
                return false;
            }

            try
            {
                solutionFolder = GetSolutionFolder(parentDirectory, solutionName);
                configFilePath = GetConfigFilePath(parentDirectory, solutionName);
            }
            catch (Exception ex)
            {
                error = "保存路径无效：" + ex.Message;
                return false;
            }

            // 长路径：MAX_PATH 260 扣掉文件名与 .tmp 后缀的余量。超过就明确拒绝，
            // 否则会在写盘阶段抛“路径过长”，用户只看到一句系统异常。
            if (configFilePath.Length > 240)
            {
                error = $"保存路径过长（{configFilePath.Length} 字符），请换一个更短的方案名或保存位置：{configFilePath}";
                return false;
            }

            if (File.Exists(solutionFolder))
            {
                error = $"保存位置被同名文件占用：{solutionFolder}";
                return false;
            }

            if (Directory.Exists(solutionFolder))
            {
                try
                {
                    var targetFull = Path.GetFullPath(configFilePath);
                    var conflict = Directory
                        .GetFiles(solutionFolder, "*" + ConfigExtension)
                        .FirstOrDefault(file => !string.Equals(Path.GetFullPath(file), targetFull, StringComparison.OrdinalIgnoreCase));

                    if (conflict != null)
                    {
                        error = $"文件夹「{Path.GetFileName(solutionFolder)}」里已经有另一个方案"
                                + $"「{Path.GetFileNameWithoutExtension(conflict)}」。"
                                + "请换一个方案名，或选择其它保存位置（一个文件夹只放一个方案）。";
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    // 目录存在但读不了（权限 / 网络盘掉线）：给出中文原因，别让异常从保存命令里逃出去。
                    error = $"无法访问保存位置「{solutionFolder}」：{ex.Message}";
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 删除一个空目录（保存失败后的清理）。非空/不存在/删不掉都返回 false，绝不抛。
        /// </summary>
        public static bool TryDeleteEmptyFolder(string? folder)
        {
            if (string.IsNullOrWhiteSpace(folder))
                return false;

            try
            {
                if (!Directory.Exists(folder))
                    return false;

                if (Directory.EnumerateFileSystemEntries(folder).Any())
                    return false;

                Directory.Delete(folder);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 建出方案文件夹骨架（根 + Image / Alarms / History / Logs）。保存成功后调用。
        /// 失败只返回原因，不抛：配置已经写成功了，不能因为建空目录失败就报“保存失败”。
        /// </summary>
        public static bool EnsureStructure(string configFilePath, out string error)
        {
            error = string.Empty;

            var root = GetSolutionRoot(configFilePath);
            if (root == null)
            {
                error = "方案路径无效：" + configFilePath;
                return false;
            }

            try
            {
                Directory.CreateDirectory(root);
                foreach (var folderName in StructureFolderNames)
                    Directory.CreateDirectory(Path.Combine(root, folderName));
                return true;
            }
            catch (Exception ex)
            {
                error = $"创建方案目录结构失败：{ex.Message}";
                return false;
            }
        }
    }
}
