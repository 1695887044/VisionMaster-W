using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using VisionMaster.Models;

namespace VisionMaster.Services
{
    /// <summary>草稿转存结果</summary>
    public enum DraftCaptureStatus
    {
        /// <summary>不满足转存条件（没有方案 / 方案还没落过盘），没写任何文件</summary>
        Skipped,

        /// <summary>内存里的方案与磁盘文件逐字一致——没有未保存改动，没写任何文件</summary>
        NoChange,

        /// <summary>确实有未保存改动，已写出草稿</summary>
        Captured,

        /// <summary>序列化或写盘失败（磁盘满/无权限）</summary>
        Failed
    }

    /// <summary>草稿转存结果（<paramref name="Path"/> 仅在 <see cref="DraftCaptureStatus.Captured"/> 时非空）</summary>
    public sealed record DraftCaptureResult(DraftCaptureStatus Status, string? Path, string? Detail);

    /// <summary>
    /// 方案草稿转存与现场恢复（S13-f 打包与交付）。
    ///
    /// 做什么：退出时（正常关闭 <b>与</b> 严重异常退出都会走退出链）把"内存里有、磁盘上没有"
    /// 的方案内容另存一份到程序目录 <c>Autosave\</c>；下次启动时提示用户一次，
    /// 并把提示过的草稿归档到 <c>Autosave\recovered\</c>（<b>不删除</b>，用户可自行用"打开方案"载入）。
    ///
    /// <b>为什么不用"脏标记"（IsDirty）</b>
    /// ---------
    /// 脏标记需要在每一处编辑入口置位（SCADA 图元、流程步骤、变量、通讯配置…），
    /// 漏一处的表现是"改完不提示、退出就丢"，而漏在哪一处极难自证。
    /// 这里改用一个不需要任何埋点的判据：<b>把当前方案按落盘口径序列化一次，
    /// 与磁盘上的文件内容逐字比较</b>——不一致就是有未保存改动。
    /// 判据唯一、不可能漏，代价只是退出时多一次序列化（工业方案的量级是毫秒级）。
    ///
    /// 已知偏差：若磁盘上的文件由<b>别的版本</b>写过（缩进/字段顺序不同），逐字比较会判成
    /// "有改动"从而多存一份草稿。方向是安全的（宁可多存），故不为此引入 JSON 规范化比较。
    /// </summary>
    public class SolutionDraftService
    {
        /// <summary>草稿目录名（程序目录下）</summary>
        public const string DirectoryName = "Autosave";

        /// <summary>已提示草稿的归档子目录名</summary>
        public const string RecoveredDirectoryName = "recovered";

        /// <summary>草稿保留份数上限，超出丢最早的</summary>
        public const int DefaultKeep = 10;

        private readonly string _directory;

        /// <param name="directory">测试用：指定草稿目录；null 用 程序目录\Autosave</param>
        public SolutionDraftService(string? directory = null)
            => _directory = string.IsNullOrWhiteSpace(directory)
                ? Path.Combine(AppContext.BaseDirectory, DirectoryName)
                : directory!;

        /// <summary>草稿目录（未提示的草稿就在这一层）</summary>
        public string DirectoryPath => _directory;

        /// <summary>
        /// 转存草稿。任何情况下都不抛异常（退出链上不允许有异常逃逸）。
        /// </summary>
        public DraftCaptureResult Capture(SolutionModel? solution)
        {
            if (solution == null)
                return new DraftCaptureResult(DraftCaptureStatus.Skipped, null, "当前没有打开的方案");

            var sourcePath = solution.SolutionFilePath;
            if (string.IsNullOrWhiteSpace(sourcePath))
                return new DraftCaptureResult(DraftCaptureStatus.Skipped, null, "方案尚未保存到磁盘，无对照物");

            try
            {
                if (!File.Exists(sourcePath))
                    return new DraftCaptureResult(DraftCaptureStatus.Skipped, null, $"方案文件已不存在：{sourcePath}");

                var json = SolutionService.Serialize(solution);
                var onDisk = File.ReadAllText(sourcePath);
                if (string.Equals(onDisk, json, StringComparison.Ordinal))
                    return new DraftCaptureResult(DraftCaptureStatus.NoChange, null, null);

                Directory.CreateDirectory(_directory);
                var name = Sanitize(Path.GetFileNameWithoutExtension(sourcePath));
                var file = Path.Combine(_directory, $"{name}-{DateTime.Now:yyyyMMdd-HHmmss}.vms");
                File.WriteAllText(file, json, Encoding.UTF8);

                PruneDrafts(DefaultKeep);
                return new DraftCaptureResult(DraftCaptureStatus.Captured, file, null);
            }
            catch (Exception ex)
            {
                return new DraftCaptureResult(DraftCaptureStatus.Failed, null, ex.Message);
            }
        }

        /// <summary>列出未提示过的草稿（新 → 旧）；目录不存在返回空集合</summary>
        public IReadOnlyList<string> ListDrafts()
        {
            try
            {
                if (!Directory.Exists(_directory)) return Array.Empty<string>();

                // 文件名里是定宽时间戳（yyyyMMdd-HHmmss），按名倒序即按时间倒序
                return Directory.GetFiles(_directory, "*.vms")
                    .OrderByDescending(f => Path.GetFileName(f), StringComparer.Ordinal)
                    .ToList();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        /// <summary>最新一份未提示的草稿；没有则 null</summary>
        public string? LatestDraft() => ListDrafts().FirstOrDefault();

        /// <summary>
        /// 取出所有未提示的草稿并归档到 <c>recovered\</c>，返回归档后的路径（新 → 旧）。
        /// 归档而不是删除：草稿是用户唯一的一份未保存内容，提示过不等于用户已经处理了它。
        /// 移动失败的（被占用）保留在原处并照常返回原路径——宁可下次再提示一次，也不能把内容弄丢。
        /// </summary>
        public IReadOnlyList<string> TakePendingDrafts()
        {
            var drafts = ListDrafts();
            if (drafts.Count == 0) return drafts;

            var moved = new List<string>(drafts.Count);
            try
            {
                var archive = Path.Combine(_directory, RecoveredDirectoryName);
                Directory.CreateDirectory(archive);

                foreach (var draft in drafts)
                {
                    var target = Path.Combine(archive, Path.GetFileName(draft));
                    try
                    {
                        File.Move(draft, target);
                        moved.Add(target);
                    }
                    catch
                    {
                        moved.Add(draft);
                    }
                }
            }
            catch
            {
                return drafts;
            }

            return moved;
        }

        /// <summary>只保留最近 keep 份草稿，返回删除份数（只动未提示的，归档区由用户自己管）</summary>
        public int PruneDrafts(int keep = DefaultKeep)
        {
            try
            {
                var drafts = ListDrafts();
                var removed = 0;
                for (var i = Math.Max(keep, 0); i < drafts.Count; i++)
                {
                    try { File.Delete(drafts[i]); removed++; }
                    catch { /* 单个删不掉不影响其余 */ }
                }
                return removed;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>方案名里可能带 <c>:</c> <c>\</c> 等非法字符（用户可自由命名），落盘前先净化</summary>
        private static string Sanitize(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "solution";

            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(name.Length);
            foreach (var ch in name)
                sb.Append(Array.IndexOf(invalid, ch) >= 0 ? '_' : ch);

            return sb.ToString();
        }
    }
}
