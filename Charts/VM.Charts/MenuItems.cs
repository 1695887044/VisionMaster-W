using System.Windows.Input;

namespace VM.Charts
{
    /// <summary>
    /// 图表右键菜单的业务项:绑到 <see cref="ChartView.MenuItemsSource"/>。
    /// VM 定义菜单项,点击走命令,与图表自身功能零耦合。
    /// </summary>
    public class ChartMenuItemVm
    {
        public ChartMenuItemVm(string label, ICommand command, object commandParameter = null)
        {
            Label = label;
            Command = command;
            CommandParameter = commandParameter;
        }

        public string Label { get; private set; }
        public ICommand Command { get; private set; }
        public object CommandParameter { get; private set; }
    }

    /// <summary>
    /// 右键"导入数据..."解析出的 CSV 内容。
    /// 由宿主订阅 <see cref="ChartView.CsvImportRequested"/> 决定把数据送往哪个 VM 系列
    /// (例如:按列名匹配 Key,Append/SetData 到对应系列)。
    /// </summary>
    public class CsvImportData
    {
        public string FilePath { get; set; }

        /// <summary>表头(通常第 0 列是时间)</summary>
        public string[] ColumnNames { get; set; }

        /// <summary>每列一个数组,行数一致;第 0 列通常是时间轴</summary>
        public double[][] Columns { get; set; }
    }
}
