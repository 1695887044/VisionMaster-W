using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace VisionMaster.Views
{
    /// <summary>
    /// ProcessView.xaml 的交互逻辑
    /// </summary>
    public partial class ProcessView : UserControl
    {
        public ProcessView()
        {
            InitializeComponent();
        }
        private void moduleTree_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            //获取鼠标位置的TreeViewItem 然后选中
            Point pt = e.GetPosition(StepTree);
            HitTestResult result = VisualTreeHelper.HitTest(StepTree, pt);
            if (result == null)
                return;
            TreeViewItem selectedItem = FindVisualParent<TreeViewItem>(
                result.VisualHit
            );

            if (selectedItem != null)
            {
                selectedItem.Focus();
            }
            else
            {
                e.Handled = true;
            }
        }
        public  T FindVisualParent<T>(DependencyObject obj) where T : class
        {
            while (obj != null)
            {
                if (obj is T)
                    return obj as T;

                obj = VisualTreeHelper.GetParent(obj);
            }

            return null;
        }
    }
}
