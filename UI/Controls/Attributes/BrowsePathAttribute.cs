

namespace UI.Attributes
{
    [AttributeUsage(AttributeTargets.Property)]
    public class BrowsePathAttribute : Attribute
    {
        /// <summary>
        /// 文件筛选器，例如："ONNX 模型|*.onnx|所有文件|*.*"
        /// </summary>
        public string FileFilter { get; set; } = "所有文件|*.*";

        /// <summary>
        /// 是否为选择文件夹模式
        /// </summary>
        public bool IsFolder { get; set; } = false;
    }
}
