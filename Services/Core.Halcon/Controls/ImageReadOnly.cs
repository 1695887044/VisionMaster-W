using System.Windows.Controls;
using HalconDotNet;

namespace Core.Halcon.Controls
{
    public class ImageReadOnly : HalconBase
    {
        /// <summary>
        /// 鼠标右键方法注册：标准「视图/图像」两子菜单（只读画布不含打开图片）
        /// </summary>
        protected override void RegisterMouseMethods()
        {
            ContextMenu = new ContextMenu();
            BuildStandardMenus(includeOpenImage: false);
        }
    }
}
