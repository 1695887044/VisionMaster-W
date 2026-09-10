using System.Windows.Controls;
using Core.Halcon.Controls;

namespace Core.Halcon.Controls
{
    public class ImageReadOnly : HalconBase
    {
        /// <summary>
        /// 鼠标右键方法注册
        /// </summary>
        protected override void RegisterMouseMethods()
        {
            ContextMenu = new ContextMenu();
            ContextMenu.Items.Add(BuildInfoMenu(includeOpenImage: false));
        }
    }
}
