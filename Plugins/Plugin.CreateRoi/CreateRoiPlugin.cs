using Core.Events;
using Core.Interfaces;
using Core.Interfaces.Result;
using HalconDotNet;
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
        #region //输入参数
        public InputPort<HImage> SrcImage { get; } = new InputPort<HImage>("输入图像");
        #endregion

        #region 视图参数
        private HImage _previewImage = new();
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
                ViewIndex = 2,
                Image = PreviewImage
            });
        }
    }
}