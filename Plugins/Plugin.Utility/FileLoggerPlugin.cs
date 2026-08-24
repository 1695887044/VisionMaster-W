using Core.Interfaces;
using System;
using System.ComponentModel.DataAnnotations;
using System.IO;

namespace VisionMaster.Plugins.Utility
{
    /// <summary>
    /// 文件日志插件
    /// 将文本内容追加写入到指定的文本或CSV文件中
    /// </summary>
    [Display(
        Name = "文件写入",
        GroupName = "系统工具",
        Description = "将文本内容追加写入到指定的文本或CSV文件中",
        ShortName = "\uf0c7"
    )]
    public class FileLoggerPlugin : VisionPluginBase
    {
        /// <summary>
        /// 文件路径输入端口
        /// </summary>
        public InputPort<string> FilePath { get; } = new InputPort<string>("FilePath", "D:\\DataLog\\Result.csv", "保存的文件绝对路径") { IsRequired = true };

        /// <summary>
        /// 要写入的内容输入端口
        /// </summary>
        public InputPort<string> LogContent { get; } = new InputPort<string>("Content", "", "需要写入的文本行");

        /// <summary>
        /// 是否自动换行输入端口
        /// </summary>
        public InputPort<bool> AutoNewLine { get; } = new InputPort<bool>("AutoNewLine", true, "是否在末尾自动添加换行符");

        /// <summary>
        /// 写入成功状态输出端口
        /// </summary>
        public OutputPort<bool> IsSuccess { get; } = new OutputPort<bool>("Success", "是否写入成功");

        /// <summary>
        /// 执行文件写入
        /// </summary>
        /// <param name="context">执行上下文</param>
        public override void RunAlgorithm(IExecutionContext context)
        {
            string path = FilePath.GetTypedValue();
            string content = LogContent.GetTypedValue();
            bool newLine = AutoNewLine.GetTypedValue();

            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                string textToWrite = newLine ? content + Environment.NewLine : content;
                File.AppendAllText(path, textToWrite);

                IsSuccess.Value = true;
                context.Logger.Info($"{InstanceName} 数据已成功写入文件: {Path.GetFileName(path)}");
            }
            catch (Exception ex)
            {
                IsSuccess.Value = false;
                context.Logger.Error($"{InstanceName} 写入文件失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 初始化插件
        /// </summary>
        public override void Initialize() { }

        /// <summary>
        /// 释放资源
        /// </summary>
        public override void Dispose() { }
    }
}
