﻿﻿﻿﻿﻿using System;
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
    /// VariableBindingView.xaml 的交互逻辑
    /// </summary>
    public partial class VariableBindingView : UserControl
    {
        public VariableBindingView()
        {
            InitializeComponent();
        }

        private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var viewModel = DataContext as ViewModels.VariableBindingViewModel;
            if (viewModel != null && sender is TabControl tabControl)
            {
                viewModel.ConstantValue = string.Empty;
            }
        }
    }
}
