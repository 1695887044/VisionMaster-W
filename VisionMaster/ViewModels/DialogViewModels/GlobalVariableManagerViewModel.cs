using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using Prism.Commands;
using UI.CustomControl;
using UI.Helper;
using VisionMaster.Models;
using VisionMaster.Services;

namespace VisionMaster.ViewModels.DialogViewModels
{
    public class DataTypeOption
    {
        public string DisplayName { get; set; }
        public Type ActualType { get; set; }
    }

    public class GlobalVariableManagerViewModel : GlobalVariableViewModelBase, IDialogAware
    {
        public string Title => "全局变量管理";

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

        private string _newVarName;
        public string NewVarName
        {
            get => _newVarName;
            set => SetProperty(ref _newVarName, value);
        }

        private string _newVarDescription;
        public string NewVarDescription
        {
            get => _newVarDescription;
            set => SetProperty(ref _newVarDescription, value);
        }

        private DataTypeOption _selectedType;
        public DataTypeOption SelectedType
        {
            get => _selectedType;
            set => SetProperty(ref _selectedType, value);
        }
        private string _searchText;
        public string SearchText
        {
            get => _searchText;
            set
            {
                SetProperty(ref _searchText, value);
                UpdateFilteredList();
            }
        }

        private ObservableCollection<VariableNode> _filteredDisplayNodes = new();
        public ObservableCollection<VariableNode> FilteredDisplayNodes
        {
            get => _filteredDisplayNodes;
            set => SetProperty(ref _filteredDisplayNodes, value);
        }

        protected override void UpdateFlatList()
        {
            base.UpdateFlatList();
            UpdateFilteredList();
        }

        private void UpdateFilteredList()
        {
            FilteredDisplayNodes.Clear();

            if (string.IsNullOrWhiteSpace(SearchText))
            {
                foreach (var node in DisplayNodes)
                {
                    FilteredDisplayNodes.Add(node);
                }
            }
            else
            {
                foreach (
                    var node in DisplayNodes.Where(n =>
                        n.Name.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) >= 0
                        || n.Description?.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase)
                            >= 0
                    )
                )
                {
                    FilteredDisplayNodes.Add(node);
                }
            }
        }

        public DelegateCommand AddCommand { get; }
        public DelegateCommand<GlobalVariableModel> DeleteCommand { get; }
        public DelegateCommand<GlobalVariableModel> EditArrayCommand { get; }
        public DelegateCommand<GlobalVariableModel> ResetCommand { get; }

        public GlobalVariableManagerViewModel(IWorkspaceManager workspace)
            : base(workspace)
        {
            SelectedType = AvailableTypes.First();

            AddCommand = new DelegateCommand(AddVariable);
            DeleteCommand = new DelegateCommand<GlobalVariableModel>(DeleteVariable);
            ResetCommand = new DelegateCommand<GlobalVariableModel>(ResetVariable);
            EditArrayCommand = new DelegateCommand<GlobalVariableModel>(ExecuteEditArray);

            // 挂载变量值变化监听
            foreach (var gv in _workspace.GlobalVariables)
            {
                gv.ValueChanged += OnVariableValueChanged;
            }

            RefreshTree();
        }

        private void OnVariableValueChanged(object sender, EventArgs e)
        {
            Application.Current.Dispatcher.Invoke(() => RefreshTree());
        }

        protected override VariableNode CreateRootNode(GlobalVariableModel gv)
        {
            return new VariableNode
            {
                OriginalModel = gv,
                Name = gv.Name,
                DataType = gv.DataType,
                TypeName = gv.DataType?.Name,
                Description = gv.Description,
                ChildDefaultValue = gv.DefaultValue,
                ChildValue = gv.Value,
                Level = 0,
            };
        }

        protected override void CreateChildNodes(GlobalVariableModel gv, VariableNode parentNode)
        {
            var defArray = gv.DefaultValue as Array;
            var valArray = gv.Value as Array;
            int len = Math.Max(defArray?.Length ?? 0, valArray?.Length ?? 0);
            var elementType = gv.DataType.GetElementType();

            for (int i = 0; i < len; i++)
            {
                parentNode.Children.Add(
                    new VariableNode
                    {
                        Name = $"[{i}]",
                        DataType = elementType,
                        TypeName = elementType?.Name,
                        ChildDefaultValue =
                            defArray != null && i < defArray.Length ? defArray.GetValue(i) : null,
                        ChildValue =
                            valArray != null && i < valArray.Length ? valArray.GetValue(i) : null,
                        Level = 1,
                    }
                );
            }
        }

        protected override void OnGlobalVariablesCollectionChanged(
            object sender,
            System.Collections.Specialized.NotifyCollectionChangedEventArgs e
        )
        {
            // 处理变量值变化事件的订阅/取消订阅
            if (e.OldItems != null)
            {
                foreach (GlobalVariableModel old in e.OldItems)
                {
                    old.ValueChanged -= OnVariableValueChanged;
                }
            }

            if (e.NewItems != null)
            {
                foreach (GlobalVariableModel newItem in e.NewItems)
                {
                    newItem.ValueChanged += OnVariableValueChanged;
                }
            }

            base.OnGlobalVariablesCollectionChanged(sender, e);
        }

        #region 业务逻辑方法
        private async void ExecuteEditArray(GlobalVariableModel gv)
        {
            if (gv == null || !gv.DataType.IsArray)
                return;
            Type elementType = gv.DataType.GetElementType();
            var editList = new ObservableCollection<ArrayItemWrapper>();

            if (gv.DefaultValue is Array arr)
            {
                foreach (var item in arr)
                {
                    var wrapper = new ArrayItemWrapper { StringValue = item?.ToString() ?? "" };
                    wrapper.RemoveCommand = new DelegateCommand(() => editList.Remove(wrapper));
                    editList.Add(wrapper);
                }
            }

            var editorControl = BuildArrayEditorUI(editList, elementType.Name);
            bool isConfirmed = await EasyDialog.ShowCustomAsync(
                $"高级集合编辑 - {gv.Name}",
                editorControl,
                isModal: true
            );

            if (isConfirmed)
            {
                try
                {
                    Array newArray = Array.CreateInstance(elementType, editList.Count);
                    for (int i = 0; i < editList.Count; i++)
                    {
                        object realValue = Convert.ChangeType(editList[i].StringValue, elementType);
                        newArray.SetValue(realValue, i);
                    }
                    gv.DefaultValue = newArray;
                    gv.Value = newArray.Clone();
                    var clonedGv = CloneHelper.ShallowCopy(gv);
                    int index = _workspace.GlobalVariables.IndexOf(gv);
                    _workspace.GlobalVariables[index] = clonedGv;
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

            if (
                _workspace.GlobalVariables.Any(s =>
                    s.Name.Equals(NewVarName, StringComparison.Ordinal)
                )
            )
            {
                EasyDialog.ShowSync("底层引擎已存在同名变量，请更换名称！", "提示");
                return;
            }

            object initValue;
            Type targetType = SelectedType.ActualType;

            if (targetType.IsArray)
                initValue = Array.CreateInstance(targetType.GetElementType(), 0);
            else if (targetType == typeof(string))
                initValue = string.Empty;
            else
                initValue = Activator.CreateInstance(targetType);

            var newVar = new GlobalVariableModel
            {
                Name = NewVarName,
                DataType = targetType,
                Description = NewVarDescription,
                DefaultValue = initValue,
                Value = initValue,
            };

            _workspace.GlobalVariables.Add(newVar);
            NewVarName = string.Empty;
            NewVarDescription = string.Empty;
        }

        private void DeleteVariable(GlobalVariableModel gv)
        {
            if (
                gv != null
                && EasyDialog.ShowSync(
                    $"确定要删除变量 [{gv.Name}] 吗？\n警告：可能会导致引用它的算子报错！",
                    "删除确认"
                )
            )
            {
                _workspace.GlobalVariables.Remove(gv);
            }
        }

        private void ResetVariable(GlobalVariableModel gv)
        {
            if (gv != null)
                gv.ResetToDefault();
        }
        #endregion

        #region IDialogAware实现
        public DialogCloseListener RequestClose { get; }

        public bool CanCloseDialog() => true;

        public void OnDialogClosed() => Dispose();

        public void OnDialogOpened(IDialogParameters parameters) { }
        #endregion

        #region 内部类与UI构建
        public class ArrayItemWrapper : BindableBase
        {
            private string _stringValue;
            public string StringValue
            {
                get => _stringValue;
                set => SetProperty(ref _stringValue, value);
            }
            public DelegateCommand RemoveCommand { get; set; }
        }

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
            btnAdd.Template = (ControlTemplate)
                XamlReader.Parse(
                    @"
                <ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'>
                    <Border Background='{TemplateBinding Background}' CornerRadius='6'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border>
                </ControlTemplate>"
                );
            btnAdd.Click += (s, e) =>
            {
                var newItem = new ArrayItemWrapper { StringValue = "0" };
                newItem.RemoveCommand = new DelegateCommand(() => editList.Remove(newItem));
                editList.Add(newItem);
            };
            Grid.SetRow(btnAdd, 0);
            grid.Children.Add(btnAdd);

            var dataGrid = new DataGrid
            {
                ItemsSource = editList,
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                HeadersVisibility = DataGridHeadersVisibility.None,
                Background = Brushes.White,
                RowHeight = 40,
                BorderBrush = new SolidColorBrush(
                    (Color)ColorConverter.ConvertFromString("#EBEEF5")
                ),
                SelectionMode = DataGridSelectionMode.Single,
            };
            dataGrid.CellStyle = (Style)
                XamlReader.Parse(
                    @"
                <Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='DataGridCell'>
                    <Setter Property='BorderThickness' Value='0'/><Setter Property='FocusVisualStyle' Value='{x:Null}'/><Setter Property='Background' Value='Transparent'/>
                </Style>"
                );

            var textCol = new DataGridTemplateColumn
            {
                Width = new DataGridLength(1, DataGridLengthUnitType.Star),
            };
            textCol.CellTemplate = (DataTemplate)
                XamlReader.Parse(
                    @"
                <DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
                    <Border Height='30' Margin='5,0' Background='#FAFAFA' BorderBrush='#DCDFE6' BorderThickness='1' CornerRadius='4'>
                        <TextBox Text='{Binding StringValue, UpdateSourceTrigger=PropertyChanged}' Background='Transparent' BorderThickness='0' Style='{x:Null}' Foreground='#606266' VerticalAlignment='Stretch' VerticalContentAlignment='Center' Padding='10,0' Margin='0'/>
                    </Border>
                </DataTemplate>"
                );

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
        #endregion

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // 取消所有变量值变化事件订阅
                foreach (var gv in _workspace.GlobalVariables)
                {
                    gv.ValueChanged -= OnVariableValueChanged;
                }
            }

            base.Dispose(disposing);
        }
    }
}
