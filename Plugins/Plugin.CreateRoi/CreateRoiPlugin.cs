using Core.Events;
using Core.Interfaces;
using HalconDotNet;
using System.ComponentModel.DataAnnotations;

namespace Plugin.CreateRoi
{
    /// <summary>
    /// 精简插件示范：
    /// - Initialize / Dispose 按需重写，本插件无资源无初始化，不需要写
    /// - 端口免名声明：new InputPort&lt;HImage&gt;()，框架自动以属性名 SrcImage 命名
    /// - [StepConfig] 自动属性：纯配置项不需要 INPC 样板
    /// - 视图参数 PreviewImage 保留 INPC（试运行后界面绑定需要实时刷新）
    /// </summary>
    [Display(
    Name = "ROI",
    GroupName = "常用工具",
    Description = "从图像中创建ROI,区域裁剪",
    ShortName = "\uf1c5"
)]
    public class CreateRoiPlugin : VisionPluginBase, IPluginCustomViewProvider
    {
        #region 配置参数
        private int _displayViewIndex = 0;
        /// <summary>
        /// 显示窗口索引：采集图像发布到主界面几号视图窗口（1~9），0=不显示
        /// </summary>
        [StepConfig]
        public int DisplayViewIndex
        {
            get => _displayViewIndex;
            set => SetProperty(ref _displayViewIndex, value);
        }
        #endregion

        #region 输入参数
        /// <summary>
        /// 输入图像（端口名自动取属性名 SrcImage）
        /// </summary>
        public InputPort<HImage> SrcImage { get; } = new();
        #endregion

        #region 视图参数
        private HImage _previewImage;
        /// <summary>
        /// 预览图像（试运行后在配置窗口实时显示）
        /// </summary>
        public HImage PreviewImage
        {
            get => _previewImage;
            set => SetProperty(ref _previewImage, value);
        }
        #endregion

        public object GetConfigView(IStepConfigData stepData)
        {
            Initialize(stepData);
            return new CreateRoiView() { DataContext = this };
        }

        public override void RunAlgorithm(IExecutionContext context)
        {
            PreviewImage = SrcImage.ActualValue;
            this.PublishPreview(PreviewImage, DisplayViewIndex + 1);
        }
    }
}
