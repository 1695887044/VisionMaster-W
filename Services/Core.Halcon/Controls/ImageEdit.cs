using System.Windows;
using System.Windows.Controls;
using HalconDotNet;

namespace Core.Halcon.Controls
{
    /// <summary>
    /// 可交互编辑画布：右键含「区域」（新建/删除/清空 ROI）+ 基类标准「视图/图像」菜单。
    /// </summary>
    [TemplatePart(Name = "PART_Halcon", Type = typeof(HSmartWindowControlWPF))]
    public class ImageEdit : HalconBase
    {
        private MenuItem? _roiMenu;
        private MenuItem? _deleteSelectedMenu;

        protected override void RegisterMouseMethods()
        {
            ContextMenu = new ContextMenu();

            // ROI 子菜单：新建/删除/清空（ImageEdit 独有）
            _roiMenu = new MenuItem { Header = "区域" };
            _roiMenu.Items.Add(CreateMenu("新建矩形", (s, e) => CreateRoi(DrawShapeType.Rectangle)));
            _roiMenu.Items.Add(CreateMenu("新建圆形", (s, e) => CreateRoi(DrawShapeType.Circle)));
            _roiMenu.Items.Add(CreateMenu("新建椭圆", (s, e) => CreateRoi(DrawShapeType.Ellipse)));
            _roiMenu.Items.Add(new Separator());
            _deleteSelectedMenu = CreateMenu("删除选中区域", (s, e) => DeleteSelectedRoi());
            _roiMenu.Items.Add(_deleteSelectedMenu);
            _roiMenu.Items.Add(CreateMenu("清空全部区域", (s, e) => ClearAllRois()));

            ContextMenu.Items.Add(_roiMenu);
            ContextMenu.Items.Add(BuildViewMenu());
            ContextMenu.Items.Add(BuildImageMenu(includeOpenImage: true));

            // 打开菜单时同步：区域计数 / 删除置灰（无选中时）/ 取点模式收敛
            ContextMenu.Opened += (s, e) =>
            {
                SyncMenuStateOnOpen();
                int count = DrawObjectList?.Count ?? 0;
                _roiMenu.Header = count > 0 ? $"区域 ({count})" : "区域";
                _deleteSelectedMenu.IsEnabled = ActiveRoi != null;
                // 取点模式收敛：新建/清空会打断取点流程（Calibration 实测的误操作路径）
                bool picking = IsPickMode;
                foreach (var item in _roiMenu.Items.OfType<MenuItem>())
                    if (item.Header is string h && h.StartsWith("新建"))
                        item.IsEnabled = !picking;
            };
        }
    }
}
