using Core.Interfaces;
using HalconDotNet;
using System.ComponentModel.DataAnnotations;

namespace Plugin.ImageAssign
{
    /// <summary>
    /// 图像赋值插件：把上游图像写入指定的全局图像变量。
    ///
    /// 它是"视觉流程 → 全局变量 → 组态画面"这条链路里唯一的写入口：
    /// 流程每跑一张图，画面上的图像显示图元就能立刻刷出这张图。
    /// 之所以必须新增这个插件，是因为在此之前写全局变量在代码里根本没有通路——
    /// 连线方向只有"读全局变量"，插件拿不到任何写入正门。
    /// </summary>
    [Display(
        Name = "图像赋值",
        GroupName = "图像处理",
        Description = "把输入图像写入指定的全局图像变量（配合组态图像显示图元使用）",
        ShortName = "\uf03e"
    )]
    public class ImageAssignPlugin : VisionPluginBase
    {
        /// <summary>
        /// 要写入的图像。未链接上游时为空，运行会失败并提示——这是设计使然：
        /// 图像只能由视觉流程产生，界面上没有"手填一张图"这回事。
        /// </summary>
        public InputPort<HImage> InputImage { get; } = new("InputImage", null, "要写入的图像") { IsRequired = true };

        /// <summary>
        /// 目标全局变量名（文本输入端口）。
        /// 用文本端口而不是枚举选择：既能手填，也能被上游连线驱动
        /// （例如由上游节点决定"这一轮写哪个变量"），与"变量赋值"插件保持同一手感。
        /// </summary>
        public InputPort<string> TargetVariableName { get; } = new("VariableName", "", "目标全局图像变量名") { IsRequired = true };

        // Success / ErrorMessage 复用基类端口（基类已声明同名输出端口，并提供 Fail() 失败契约），
        // 本类不要重复声明：重复声明会影子隐藏基类成员，插件写派生端口、引擎读基类端口，
        // 表现为"永远报失败且错误信息为空"（CS0108 教训）

        /// <summary>
        /// 执行写入。空图与空变量名都显式失败；写入结果按 TryWrite 的真实返回值如实反馈，
        /// 不做"无条件报成功"——变量不存在、类型不符这些情况必须让操作员看得见原因。
        /// </summary>
        /// <param name="context">执行上下文</param>
        public override void RunAlgorithm(IExecutionContext context)
        {
            var image = InputImage.GetTypedValue();
            if (image == null || !image.IsInitialized())
            {
                Fail("输入图像为空或未初始化");
                return;
            }

            var targetName = TargetVariableName.GetTypedValue();
            if (string.IsNullOrWhiteSpace(targetName))
            {
                Fail("目标变量名不能为空");
                return;
            }

            var writer = context?.GlobalVariables;
            if (writer == null)
            {
                Fail("当前执行环境不支持写入全局变量");
                return;
            }

            if (!writer.TryWrite(targetName, image, out var error))
            {
                var reason = string.IsNullOrWhiteSpace(error)
                    ? $"写入全局变量「{targetName}」失败"
                    : error;
                Fail(reason);
                context.Logger?.Error($"{InstanceName} {reason}");
                return;
            }

            context.Logger?.Info($"{InstanceName} 已把图像写入全局变量 {targetName.Trim()}");
        }

        public override void Initialize() { }
    }
}
