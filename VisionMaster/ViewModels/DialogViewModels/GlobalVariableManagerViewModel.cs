using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;
using UI.CustomControl;
using VisionMaster.Models;
using VisionMaster.Services;

namespace VisionMaster.ViewModels.DialogViewModels
{
    public class DataTypeOption
    {
        public string DisplayName { get; set; }
        public Type ActualType { get; set; }
    }

    public class GlobalVariableManagerViewModel : BindableBase, IDialogAware
    {
        public string Title => "全局变量管理";
        private readonly IWorkspaceManager _workspace;

        // UI 绑定的监控列表
        public ObservableCollection<GlobalVariableModel> GlobalVariables { get; } = new();

        public ObservableCollection<DataTypeOption> AvailableTypes { get; } =
            new()
            {
                new DataTypeOption { DisplayName = "整数 (int)", ActualType = typeof(int) },
                new DataTypeOption { DisplayName = "小数 (double)", ActualType = typeof(double) },
                new DataTypeOption { DisplayName = "文本 (string)", ActualType = typeof(string) },
                new DataTypeOption { DisplayName = "布尔 (bool)", ActualType = typeof(bool) },
                new DataTypeOption { DisplayName = "整数数组 (int[])", ActualType = typeof(int[]) },
                new DataTypeOption
                {
                    DisplayName = "小数数组 (double[])",
                    ActualType = typeof(double[]),
                },
                new DataTypeOption
                {
                    DisplayName = "文本数组 (string[])",
                    ActualType = typeof(string[]),
                },
                new DataTypeOption
                {
                    DisplayName = "布尔数组 (bool[])",
                    ActualType = typeof(bool[]),
                },
            };

        public string NewVarName
        {
            get => field;
            set => SetProperty(ref field, value);
        }
        public string NewVarDescription
        {
            get => field;
            set => SetProperty(ref field, value);
        }
        public DataTypeOption SelectedType
        {
            get => field;
            set => SetProperty(ref field, value);
        }

        public DelegateCommand AddCommand { get; }
        public DelegateCommand<GlobalVariableModel> DeleteCommand { get; }
        public DelegateCommand<GlobalVariableModel> EditArrayCommand { get; }
        public DelegateCommand<GlobalVariableModel> ResetCommand { get; }

        public GlobalVariableManagerViewModel(IWorkspaceManager workspace)
        {
            _workspace = workspace;
            SelectedType = AvailableTypes.First();
            LoadExistingVariables();

            AddCommand = new DelegateCommand(AddVariable);
            DeleteCommand = new DelegateCommand<GlobalVariableModel>(DeleteVariable);
            ResetCommand = new DelegateCommand<GlobalVariableModel>(ResetVariable);
            EditArrayCommand = new DelegateCommand<GlobalVariableModel>(ExecuteEditArray);
        }

        private void LoadExistingVariables()
        {
            GlobalVariables.Clear();
            if (_workspace.GlobalVariables == null)
            {
                _workspace.GlobalVariables = new();
            }
            foreach (var kvp in _workspace.GlobalVariables)
            {
                GlobalVariables.Add(kvp);
            }
        }

        private async void ExecuteEditArray(GlobalVariableModel gv)
        {
            if (gv == null || !gv.DataType.IsArray)
                return;

            // 1. 获取数组的内部元素类型 (比如 double[] 拿到 double)
            Type elementType = gv.DataType.GetElementType();

            // 2. 将真实的数组，包装成供 UI 编辑的字符串列表 ObservableCollection<ArrayItemWrapper>
            // 因为所有类型都可以转成 string 在 TextBox 里编辑
            var editList = new ObservableCollection<ArrayItemWrapper>();
            if (gv.DefaultValue is Array arr)
            {
                foreach (var item in arr)
                {
                    var wrapper = new ArrayItemWrapper { StringValue = item?.ToString() ?? "" };
                    // 注入：当命令触发时，让外层的集合把自己删掉！
                    wrapper.RemoveCommand = new DelegateCommand(() => editList.Remove(wrapper));
                    editList.Add(wrapper);
                }
            }

            // 3. 使用纯 C# 动态构建一个带增删功能的 DataGrid 弹窗界面
            var editorControl = BuildArrayEditorUI(editList, elementType.Name);

            // 4. 呼叫核武器 EasyDialog 弹出它！
            bool isConfirmed = await EasyDialog.ShowCustomAsync(
                $"高级集合编辑 - {gv.Name}",
                editorControl,
                isModal: true
            );

            // 5. 如果用户点击了确定，进行拆箱：把 String 列表强转回真实类型的 Array
            if (isConfirmed)
            {
                try
                {
                    // 动态创建一个指定类型和长度的数组
                    Array newArray = Array.CreateInstance(elementType, editList.Count);

                    for (int i = 0; i < editList.Count; i++)
                    {
                        // 黑科技：Convert.ChangeType 能完美把 "12.5" 转成 double，"1" 转成 int
                        object realValue = Convert.ChangeType(editList[i].StringValue, elementType);
                        newArray.SetValue(realValue, i);
                    }

                    // 同步到底层和当前 UI
                    gv.DefaultValue = newArray;
                    gv.Value = newArray.Clone(); // 当前值也同步过去
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"数据格式转换失败，请检查输入内容！\n{ex.Message}",
                        "保存失败"
                    );
                }
            }
        }

        private void AddVariable()
        {
            if (string.IsNullOrWhiteSpace(NewVarName))
            {
                EasyDialog.ShowSync("变量名不能为空！", "提示");
                return;
            }

            if (_workspace.GlobalVariables.Any(s => s.Name.Equals(NewVarName)))
            {
                EasyDialog.ShowSync("底层引擎已存在同名变量，请更换名称！", "提示");
                return;
            }

            object initValue;
            Type targetType = SelectedType.ActualType;

            if (targetType.IsArray)
            {
                initValue = Array.CreateInstance(targetType.GetElementType(), 0);
            }
            else if (targetType == typeof(string))
            {
                initValue = string.Empty;
            }
            else
            {
                initValue = Activator.CreateInstance(targetType);
            }

            var newVar = new GlobalVariableModel
            {
                Name = NewVarName,
                DataType = targetType,
                Description = NewVarDescription,
                DefaultValue = initValue,
                Value = initValue,
            };

            _workspace.GlobalVariables.Add(newVar);
            GlobalVariables.Add(newVar);

            // 清空表单
            NewVarName = string.Empty;
        }

        private void DeleteVariable(GlobalVariableModel gv)
        {
            if (gv == null)
                return;

            if (
                EasyDialog.ShowSync(
                    $"确定要删除变量 [{gv.Name}] 吗？\n警告：可能会导致引用它的算子报错！",
                    "删除确认"
                )
            )
            {
                _workspace.GlobalVariables.Remove(gv);
                GlobalVariables.Remove(gv);
            }
        }

        private void ResetVariable(GlobalVariableModel gv)
        {
            // 内存对象的操作，因为是指针引用，UI 和 底层是同一个对象，直接调就行，无需同步字典。
            if (gv != null)
                gv.ResetToDefault();
        }

        public DialogCloseListener RequestClose { get; }

        public bool CanCloseDialog() => true;

        public void OnDialogClosed() { }

        public void OnDialogOpened(IDialogParameters parameters) { }

        public class ArrayItemWrapper : BindableBase
        {
            public string StringValue
            {
                get => field;
                set => SetProperty(ref field, value);
            }

            public DelegateCommand RemoveCommand { get; set; }
        }

        // 动态生成集合编辑器的纯 C# UI 代码
        private FrameworkElement BuildArrayEditorUI(
            ObservableCollection<ArrayItemWrapper> editList,
            string elementTypeName
        )
        {
            var grid = new Grid
            {
                Height = 350,
                Width = 380,
                Margin = new Thickness(10),
            };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }
            );

            // 1. 顶部工具栏 (添加按钮)
            var btnAdd = new Button
            {
                Content = $"+ 添加 {elementTypeName} 元素",
                Margin = new Thickness(0, 0, 0, 15),
                Height = 36,
                Background = new SolidColorBrush(
                    (Color)ColorConverter.ConvertFromString("#409EFF")
                ),
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
            };

            // 用 XamlReader 赋予按钮完美的圆角
            btnAdd.Template = (ControlTemplate)
                XamlReader.Parse(
                    @"
                <ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'>
                    <Border Background='{TemplateBinding Background}' CornerRadius='6'>
                        <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
                    </Border>
                </ControlTemplate>"
                );

            // 🌟 动态注入新增项的“自毁命令”
            btnAdd.Click += (s, e) =>
            {
                var newItem = new ArrayItemWrapper { StringValue = "0" };
                newItem.RemoveCommand = new DelegateCommand(() => editList.Remove(newItem));
                editList.Add(newItem);
            };

            Grid.SetRow(btnAdd, 0);
            grid.Children.Add(btnAdd);

            // 2. 中间的 DataGrid
            var dataGrid = new DataGrid
            {
                ItemsSource = editList,
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                HeadersVisibility = DataGridHeadersVisibility.None,
                Background = Brushes.White,
                RowHeight = 40, // 加大行高，增加呼吸感
                BorderBrush = new SolidColorBrush(
                    (Color)ColorConverter.ConvertFromString("#EBEEF5")
                ),
                SelectionMode = DataGridSelectionMode.Single,
            };

            // 🌟 剥夺刺眼的系统蓝色高亮，改为透明
            dataGrid.CellStyle = (Style)
                XamlReader.Parse(
                    @"
                <Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' 
                       xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' 
                       TargetType='DataGridCell'>
                    <Setter Property='BorderThickness' Value='0'/>
                    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
                    <Setter Property='Background' Value='Transparent'/>
                </Style>"
                );

            // 3. 第一列：文本输入框 (完全复用我们刚写的胶囊边框样式)
            var textCol = new DataGridTemplateColumn
            {
                Width = new DataGridLength(1, DataGridLengthUnitType.Star),
            };
            textCol.CellTemplate = (DataTemplate)
                XamlReader.Parse(
                    @"
                <DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
                    <Border Height='30' Margin='5,0' Background='#FAFAFA' BorderBrush='#DCDFE6' BorderThickness='1' CornerRadius='4'>
                        <TextBox Text='{Binding StringValue, UpdateSourceTrigger=PropertyChanged}' 
                                 Background='Transparent' BorderThickness='0' Style='{x:Null}' Foreground='#606266'
                                 VerticalAlignment='Stretch' VerticalContentAlignment='Center' Padding='10,0' Margin='0'/>
                    </Border>
                </DataTemplate>"
                );

            // 4. 第二列：删除按钮 (利用命令绑定，彻底抛弃 Click 事件挂载)
            var delCol = new DataGridTemplateColumn { Width = new DataGridLength(50) };
            delCol.CellTemplate = (DataTemplate)
                XamlReader.Parse(
                    @"
                <DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>
                    <Button Background='Transparent' BorderThickness='0' Cursor='Hand' ToolTip='删除元素' Command='{Binding RemoveCommand}'>
                        <TextBlock Text='✖' FontSize='14' FontWeight='Bold' Foreground='#F56C6C' HorizontalAlignment='Center' VerticalAlignment='Center' Margin='0,0,0,2'/>
                    </Button>
                </DataTemplate>"
                );

            dataGrid.Columns.Add(textCol);
            dataGrid.Columns.Add(delCol);

            Grid.SetRow(dataGrid, 1);
            grid.Children.Add(dataGrid);

            return grid;
        }
    }
}
