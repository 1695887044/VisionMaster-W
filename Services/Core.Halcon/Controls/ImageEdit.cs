using HalconDotNet;
using System.Windows;
using System.Windows.Controls;
using Core.Halcon.Base;

namespace Core.Halcon.Controls
{
    [TemplatePart(Name = "PART_Halcon", Type = typeof(HSmartWindowControlWPF))]
    public class ImageEdit : HalconBase
    {
        protected override void RegisterMouseMethods()
        {

            this.ContextMenu = new ContextMenu();
            MenuItem InfoMenu = new MenuItem();
            InfoMenu.Header = "信息";
            MenuItem RoiMenu = new MenuItem();
            RoiMenu.Header = "区域";
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
            InfoMenu.Items.Add(menu1);
            InfoMenu.Items.Add(menu2);
            InfoMenu.Items.Add(menu3);
            InfoMenu.Items.Add(CreateMenu("保存原始图像", (s, e) => {
                this.SaveImage();
            }));
            InfoMenu.Items.Add(CreateMenu("保存缩略图像", (s, e) => {
                this.SaveWindowDump();
            }));
            InfoMenu.Items.Add(CreateMenu("打开图片", (s, e) => {
                this.OpenImage();
            }));
            RoiMenu.Items.Add(CreateMenu("矩形",(s,e)=> DrawShape(DrawShapeType.Rectangle)));
            RoiMenu.Items.Add(CreateMenu("圆形", (s, e) => DrawShape(DrawShapeType.Circle)));
            RoiMenu.Items.Add(CreateMenu("椭圆", (s, e) => DrawShape(DrawShapeType.Ellipse)));
            RoiMenu.Items.Add(CreateMenu("区域", (s, e) => DrawShape(DrawShapeType.Region)));
            RoiMenu.Items.Add(CreateMenu("掩膜", (s, e) => DrawShape(DrawShapeType.Mask)));
            ContextMenu.Items.Add(RoiMenu);
            ContextMenu.Items.Add(InfoMenu);
        }



    }
}
