using System.Windows.Controls;
using HalconDotNet;

namespace Core.Halcon.Controls
{
    public class ImageDisplay : HalconBase
    {
        /// <summary>
        /// 鼠标右键方法注册：标准「视图/图像」两子菜单（含打开图片）
        /// </summary>
        protected override void RegisterMouseMethods()
        {
            ContextMenu = new ContextMenu();
            BuildStandardMenus(includeOpenImage: true);
        }
    }
}
