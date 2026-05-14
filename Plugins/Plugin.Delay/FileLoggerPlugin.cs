using Core.Interfaces;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Plugin.Delay
{
    [Display(
        Name = "Write File",
        GroupName = "系统工具",
        Description = "将文本内容追加写入到指定的文本或 CSV 文件中",
        ShortName = "\uf0c7" // 保存软盘图标
    )]
    public class FileLoggerPlugin : VisionPluginBase
    {
        // 文件路径，默认写在程序运行目录
        public InputPort<string> FilePath { get; } = new InputPort<string>("File Path", "D:\\DataLog\\Result.csv", "保存的文件绝对路径");

        // 要写入的内容（配合 StringFormat 插件食用极佳）
        public InputPort<string> LogContent { get; } = new InputPort<string>("Content", "", "需要写入的文本行");

        // 是否自动换行
        public InputPort<bool> AutoNewLine { get; } = new InputPort<bool>("Auto NewLine", true, "是否在末尾自动添加换行符");

        public OutputPort<bool> IsSuccess { get; } = new OutputPort<bool>("Success", "是否写入成功");

        public override void RunAlgorithm(IExecutionContext context)
        {
            string path = FilePath.GetTypedValue();
            string content = LogContent.GetTypedValue();
            bool newLine = AutoNewLine.GetTypedValue();

            try
            {
                // 确保文件夹存在
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                // 写入文件（Append 模式，不会覆盖原有数据）
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

        public override void Initialize() { }
        public override void Dispose() { }
    }
}
