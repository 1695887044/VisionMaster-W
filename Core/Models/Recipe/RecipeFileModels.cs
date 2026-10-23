using System;
using Newtonsoft.Json;

namespace VisionMaster.Models
{
    /// <summary>
    /// `.vmrecipe` 交换文件（v2）。只带配方定义，不带任何设备/连接/地址字段；
    /// 导入只建定义、不应用，运行期仍以 .vms 内嵌配方为唯一权威。
    /// </summary>
    public sealed class RecipeFileDto
    {
        public const int CurrentFormatVersion = 1;

        public int FormatVersion { get; set; } = CurrentFormatVersion;

        public DateTime ExportedAtUtc { get; set; } = DateTime.UtcNow;

        public string ExportedBy { get; set; } = string.Empty;

        public Guid SourceSolutionId { get; set; }

        public string SourceSolutionName { get; set; } = string.Empty;

        /// <summary>内容指纹（导出时对 Recipe 序列化文本取 SHA-256；导入只展示，不参与信任判断）。</summary>
        public string ContentHash { get; set; } = string.Empty;

        public RecipeDefinition? Recipe { get; set; }
    }
}
