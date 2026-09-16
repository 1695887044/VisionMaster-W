using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using UI.Attributes;
using UI.CustomControl.PropertyGrid;

namespace UI.CustomControl
{
    public class FlatPropertyGrid : Control
    {
        private const string DefaultName = "默认分组";

        public List<IControlGenerator> Generators { get; } = new();
        public List<IControlProcessor> Processors { get; } = new();

        // 生命周期与防爆字段
        private Action _cleanupActions = () => { };
        private INotifyPropertyChanged? _currentNotifier;
        private bool _isRefreshPending = false;

        public static readonly DependencyProperty BindingObjectProperty =
            DependencyProperty.Register(
                nameof(BindingObject),
                typeof(object),
                typeof(FlatPropertyGrid),
                new PropertyMetadata(null, OnBindingObjectChanged)
            );

        public object BindingObject
        {
            get => GetValue(BindingObjectProperty);
            set => SetValue(BindingObjectProperty, value);
        }

        static FlatPropertyGrid()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(FlatPropertyGrid), new FrameworkPropertyMetadata(typeof(FlatPropertyGrid)));
        }

        public FlatPropertyGrid()
        {
            Generators.Add(new NestedPropertyGridGenerator());
            Generators.Add(new EnumGenerator());
            Generators.Add(new StructValueGenerator());
            Generators.Add(new BoolStateGenerator());
            Generators.Add(new TypeGenerator());
        }

        protected override Size MeasureOverride(Size constraint)
        {
            if (double.IsInfinity(constraint.Height))
            {
                double targetHeight = SystemParameters.WorkArea.Height - 300;
                var mainWindow = Application.Current?.MainWindow;
                if (mainWindow != null && mainWindow.ActualHeight > 100)
                    targetHeight = mainWindow.ActualHeight - 250;
                else
                {
                    var window = Window.GetWindow(this);
                    if (window != null && window.ActualHeight > 100) targetHeight = window.ActualHeight - 250;
                }
                constraint = new Size(constraint.Width, Math.Max(200, targetHeight));
            }
            return base.MeasureOverride(constraint);
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            UpdatePropertyGrid();
        }

        private static void OnBindingObjectChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is FlatPropertyGrid control)
            {
                if (control._currentNotifier != null)
                    control._currentNotifier.PropertyChanged -= control.OnBindingObjectPropertyChanged;

                if (e.NewValue is INotifyPropertyChanged newNotifier)
                {
                    newNotifier.PropertyChanged += control.OnBindingObjectPropertyChanged;
                    control._currentNotifier = newNotifier;
                }
                else
                {
                    control._currentNotifier = null;
                }

                control.UpdatePropertyGrid();
            }
        }

        // 🌟 注意：方法签名加上了 async 关键字！
        private async void OnBindingObjectPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // 【线程兜底】这里是"直接订阅 INotifyPropertyChanged"，不是 WPF 绑定引擎——
            // 事件在**属性变更源线程**上执行。而属性变更可能来自后台线程：
            //   ① 连接专属线程改 CommunicationConfig.State（连接/断开/重连）
            //   ② 轮询线程改 NetworkVariableModel 的镜像值
            //   ③ 流程引擎/插件线程改算子参数（PreProcessingView 的 BindingObject）
            // 属性值本身可以跨线程读，但 BindingObject 是 DependencyProperty，
            // 有严格的线程亲和性：只有创建本控件的 UI 线程才能 GetValue，否则
            // Dispatcher.VerifyAccess 直接抛 InvalidOperationException。
            // 所以统一先封送回 UI 线程再重入本方法，把三条来源的隐患一次抹平。
            if (!Dispatcher.CheckAccess())
            {
                VisionMaster.Helpers.SafeDispatch.BeginInvoke(() => OnBindingObjectPropertyChanged(sender, e));
                return;
            }

            if (BindingObject == null || string.IsNullOrEmpty(e.PropertyName)) return;

            var propInfo = BindingObject.GetType().GetProperty(e.PropertyName, BindingFlags.Public | BindingFlags.Instance);
            var displayAttr = propInfo?.GetCustomAttribute<SuperDisplayAttribute>();

            // 拦截：只有明确标记 RequireRefresh = true 的属性，才触发全体重绘
            if (displayAttr != null && displayAttr.RequireRefresh)
            {
                if (_isRefreshPending) return;
                _isRefreshPending = true;

                await Task.Delay(50);
                // 异步投递而非同步 Invoke：关闭软件时 Dispatcher 取消挂起操作，
                // 同步 Invoke 会把 TaskCanceledException 抛回属性变更源线程（插件/引擎），改 SafeDispatch 静默兜底
                VisionMaster.Helpers.SafeDispatch.BeginInvoke(() =>
                {
                    try
                    {
                        UpdatePropertyGrid();
                    }
                    finally
                    {
                        _isRefreshPending = false;
                    }
                });
            }
        }

        #region ====== Tab 生成核心逻辑 (解决 Tab 显示) ======
        private void UpdatePropertyGrid()
        {
            if (BindingObject == null || !(GetTemplateChild("PART_TabControl") is TabControl tabControl)) return;

            // 清理僵尸事件，防止内存泄漏
            _cleanupActions.Invoke();
            _cleanupActions = () => { };

            tabControl.Items.Clear();

            var properties = BindingObject.GetType()
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => Attribute.IsDefined(p, typeof(SuperDisplayAttribute)))
                .ToList();

            if (!properties.Any()) return;

            // 按 GroupPath 的第一部分进行分组
            var groupedProperties = properties
                .OrderBy(p => p.GetCustomAttribute<SuperDisplayAttribute>()?.Order ?? 0)
                .GroupBy(p => p.GetCustomAttribute<SuperDisplayAttribute>()?.GroupPath?.Split('/').FirstOrDefault() ?? DefaultName)
                .OrderBy(g => g.First().GetCustomAttribute<SuperDisplayAttribute>()?.GroupOrder ?? "0");

            var flatTabItemStyle = Application.Current.TryFindResource("LightFlatTabItemStyle") as Style;

            foreach (var group in groupedProperties)
            {
                var tabItem = new TabItem { Header = group.Key };

                if (flatTabItemStyle != null)
                {
                    tabItem.Style = flatTabItemStyle;
                }

                // 生成分组内部的扁平控件列表
                tabItem.Content = CreateFlatGridContent(group.ToList());
                tabControl.Items.Add(tabItem);
            }

            if (tabControl.Items.Count > 0 && tabControl.SelectedIndex == -1)
                tabControl.SelectedIndex = 0;
        }
        #endregion

        private UIElement CreateFlatGridContent(List<PropertyInfo> properties)
        {
            var outerBorder = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(229, 229, 229)),
                BorderThickness = new Thickness(1, 1, 1, 0),
                CornerRadius = new CornerRadius(4, 4, 0, 0),
                Margin = new Thickness(0, 10, 0, 10)
            };

            var mainStack = new StackPanel();
            Grid.SetIsSharedSizeScope(mainStack, true);

            foreach (var prop in properties)
            {
                var display = prop.GetCustomAttribute<SuperDisplayAttribute>();
                var isNested = prop.GetCustomAttribute<PropertyItemAttribute>() != null;

                var control = isNested
                    ? CreateControl(prop, prop.GetValue(BindingObject)!)
                    : CreateControl(prop, BindingObject, display!.IsReadOnly);

                var context = new ControlContext
                {
                    Property = prop,
                    BindingSource = BindingObject,
                    Control = control,
                    WrapPanel = new StackPanel(),
                    RootCellGrid = new Grid(),
                    RegisterCleanup = action => _cleanupActions += action
                };

                var pipeline = new List<IControlProcessor>
                {
                    new FlatLayoutProcessor(display!),
                    new CommandProcessor(),
                    new ValidationProcessor(),
                    new PermissionProcessor()
                };
                pipeline.AddRange(Processors);

                foreach (var processor in pipeline) processor.Execute(context);

                mainStack.Children.Add(context.RootCellGrid);
            }

            outerBorder.Child = mainStack;

            return new ScrollViewer
            {
                Content = outerBorder,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(12)
            };
        }

        public FrameworkElement CreateControl(PropertyInfo prop, object bindingSource, bool readOnly = false)
        {
            var generator = Generators
                .OrderByDescending(g => g.Priority)
                .FirstOrDefault(g => g.CanProcess(prop, prop.PropertyType, readOnly));

            return generator != null
                ? generator.Create(prop, bindingSource, readOnly)
                : new TextBlock { Text = $"Unsupported", Foreground = Brushes.Red };
        }
    }
}