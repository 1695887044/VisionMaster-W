using System.Reflection;

namespace G.Extensions.Behvaiors.DataGrids;

//public class mydatagrid:DataGrid
//{
//    protected override void OnAutoGeneratingColumn(DataGridAutoGeneratingColumnEventArgs e)
//    {
//        base.OnAutoGeneratingColumn(e);
//    }
//}

public interface IDataGridColumn
{
    string PropertyPath { get; set; }
    Type Template { get; set; }
    DataGridLength Width { get; set; }

    DataGridColumn GetDataGridColumn(PropertyInfo propertyInfo);
}
