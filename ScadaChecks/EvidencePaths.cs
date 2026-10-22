using System;
using System.IO;

namespace ScadaChecks
{
    /// <summary>
    /// 断言宿主产出的证据图统一落到 <c>artifacts\render\&lt;yyyyMMdd&gt;\&lt;页面&gt;\</c>。
    ///
    /// 历史上这些图直接写进 <c>docs\UI设计基线\**\evidence\</c> —— 那是受 git 跟踪的目录。
    /// 后果：每跑一次 gate 都会改字节（同一屏两次渲染不可能逐像素相同），工作区周期性变脏，
    /// 每轮收尾都得 <c>git checkout --</c> 还原。证据是"这次跑出来的产物"，不是文档。
    ///
    /// 文档里已经提交的旧图保留不动（它们是当时那一次的记录）；新跑出来的落这里。
    /// </summary>
    internal static class EvidencePaths
    {
        /// <summary>取（并创建）本页今天的证据目录。</summary>
        public static string Dir(string repoRoot, string page)
        {
            string dir = Path.Combine(repoRoot, "artifacts", "render", DateTime.Now.ToString("yyyyMMdd"), page);
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
}
