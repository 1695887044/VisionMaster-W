using Core.Events;
using Core.Halcon.Extensions;
using Core.Interfaces;
using Core.Interfaces.Result;
using HalconDotNet;
using Microsoft.Win32;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Plugin.CreateRoi
{
    [Display(
    Name = "ROI",
    GroupName = "常用工具",
    Description = "从图像中创建ROI,区域裁剪",
    ShortName = "\uf1c5"
)]
    public class CreateRoiPlugin : VisionPluginBase, IPluginCustomViewProvider
    {
        #region 配置参数
        private int _displayViewIndex = 1;
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
        public InputPort<HImage> SrcImage { get; } = new InputPort<HImage>("输入图像");
        #endregion

        #region 视图参数
        private HImage _previewImage;
        /// <summary>
        /// 预览图像
        /// </summary>
        public HImage PreviewImage
        {
            get => _previewImage;
            set => SetProperty(ref _previewImage, value);
        }
        #endregion
        public override void Dispose()
        {
            throw new NotImplementedException();
        }

        public object GetConfigView(IStepConfigData stepData)
        {
            Initialize(stepData);
            return new CreateRoiView() { DataContext = this };
        }

        public override void Initialize()
        {
            throw new NotImplementedException();
        }

        public override void RunAlgorithm(IExecutionContext context)
        {
            PreviewImage = SrcImage.ActualValue;
            GlobalEventBus.PublishOnUIThread(new ImageDisplayEvent<HImage>
            {
                ViewIndex = DisplayViewIndex + 1,
                Image = PreviewImage
            });
        }
    }
}