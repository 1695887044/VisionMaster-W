using HalconDotNet;
using System.Windows;
using System.Windows.Controls;
using Core.Halcon.Controls;

namespace Core.Halcon.Controls
{
    [TemplatePart(Name = "PART_Halcon", Type = typeof(HSmartWindowControlWPF))]
    public class ImageEdit : HalconBase
    {
        protected override void RegisterMouseMethods()
        {
            ContextMenu = new ContextMenu();

            // ROI 子菜单：新建/删除（ImageEdit 独有）
            var roiMenu = new MenuItem { Header = "区域" };
            roiMenu.Items.Add(CreateMenu("新建矩形", (s, e) => CreateRoi(DrawShapeType.Rectangle)));
            roiMenu.Items.Add(CreateMenu("新建圆形", (s, e) => CreateRoi(DrawShapeType.Circle)));
            roiMenu.Items.Add(CreateMenu("新建椭圆", (s, e) => CreateRoi(DrawShapeType.Ellipse)));
            roiMenu.Items.Add(CreateMenu("删除选中区域", (s, e) => DeleteSelectedRoi()));

            ContextMenu.Items.Add(roiMenu);
            ContextMenu.Items.Add(BuildInfoMenu(includeOpenImage: true));
        }
    }
}
