

using System.Windows.Controls;
using Core.Halcon.Controls;

namespace Core.Halcon.Controls
{
    public class ImageReadOnly: HalconBase
    {
        /// <summary>
        /// 鼠标右键方法注册
        /// </summary>
        protected override void RegisterMouseMethods()
        {

            this.ContextMenu = new ContextMenu();
            MenuItem menu1 = new MenuItem();
            menu1.Header = "适应图片/窗口";
            menu1.Click += (s, e) =>
            {
                menu1.IsChecked = !menu1.IsChecked;
                ResetWindow(menu1.IsChecked);
            };
            MenuItem menu2 = new MenuItem();
            menu2.Header = "显示/隐藏图像信息";
            menu2.Click += (s, e) =>
            {
                menu2.IsChecked = !menu2.IsChecked;
                ShowImageInfo(menu2.IsChecked);
            };
            MenuItem menu3 = new MenuItem();
            menu3.Header = "显示/隐藏十字";
            menu3.Click += (s, e) =>
            {
                menu3.IsChecked = !menu3.IsChecked;
                this.ShowImageCross(menu3.IsChecked);
            };
            ContextMenu.Items.Add(menu1);
            ContextMenu.Items.Add(menu2);
            ContextMenu.Items.Add(menu3);
            ContextMenu.Items.Add(CreateMenu("保存原始图像", (s, e) => {
                this.SaveImage();
            }));
            ContextMenu.Items.Add(CreateMenu("保存缩略图像", (s, e) => {
                this.SaveWindowDump();
            }));

        }
    }
}
