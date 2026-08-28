using Core.Interfaces;
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
        public InputPort<HImage> SrcImage { get; } =new InputPort<HImage>("输入图像");
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
            throw new NotImplementedException();
        }
    }
}
