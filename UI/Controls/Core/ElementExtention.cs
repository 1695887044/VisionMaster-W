using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace UI.Core
{
    /// <summary>
    /// WPF UI 元素扩展工具类。
    /// 提供对视觉树/逻辑树遍历、跨线程 UI 访问、命中测试、装饰器管理等底层操作的封装。
    /// </summary>
    public static class ElementExtention
    {
        #region 命令绑定 (Command Binding)

        /// <summary>
        /// 为 UI 元素绑定路由命令及执行动作。
        /// 工业应用场景：快捷绑定诸如“急停”、“复位”等通用快捷键命令。
        /// </summary>
        public static void BindCommand(this UIElement @ui, ICommand com, Action<object, ExecutedRoutedEventArgs> call)
        {
            CommandBinding bind = new CommandBinding(com);
            bind.Executed += new ExecutedRoutedEventHandler(call);
            @ui.CommandBindings.Add(bind);
        }

        /// <summary>
        /// 为 UI 元素绑定路由命令（无具体执行动作，通常用于配合外部命令接管）。
        /// </summary>
        public static void BindCommand(this UIElement @ui, ICommand com)
        {
            CommandBinding bind = new CommandBinding(com);
            @ui.CommandBindings.Add(bind);
        }

        #endregion

        #region 可见性控制 (Visibility)
        // 这些方法极大简化了代码，避免了每次都要写 Visibility.Visible 的繁琐。

        /// <summary>显示该元素</summary>
        public static void Visible(this UIElement @ui) => @ui.Visibility = Visibility.Visible;

        /// <summary>根据布尔值控制显示或折叠（不占空间）</summary>
        public static void VisibilityWith(this UIElement @ui, bool from)
        {
            if (from) @ui.Visible();
            else ui.Collapsed();
        }

        /// <summary>隐藏该元素（但仍然占据布局空间）</summary>
        public static void Hidden(this UIElement @ui) => @ui.Visibility = Visibility.Hidden;

        /// <summary>折叠该元素（不显示且不占布局空间）</summary>
        public static void Collapsed(this UIElement @ui) => @ui.Visibility = Visibility.Collapsed;

        #endregion

        #region 树形结构遍历 (Visual Tree & Logical Tree Traversal)
        /* 老师科普：
         * WPF 中有两棵树。Logical Tree（逻辑树）是你在 XAML 里写的结构；
         * Visual Tree（视觉树）是最终渲染的结构（比如一个 Button 内部其实包含了 Border 和 TextBlock）。
         * 在封装自定义控件时，我们通常遍历视觉树。
         */

        /// <summary>
        /// 深度优先搜索，获取视觉树中符合条件的第一个子元素。
        /// </summary>
        /// <typeparam name="T">要查找的目标控件类型（如 TextBox）</typeparam>
        /// <param name="p_element">父元素</param>
        /// <param name="p_func">过滤条件（可选）</param>
        public static T GetChild<T>(this DependencyObject p_element, Func<T, bool> p_func = null) where T : UIElement
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(p_element); i++)
            {
                UIElement child = VisualTreeHelper.GetChild(p_element, i) as FrameworkElement;
                if (child == null) continue;

                // 判断当前子节点是否是我们想要的类型
                if (child is T t && (p_func == null || p_func(t)))
                    return (T)child;

                // 递归往更深层查找
                T grandChild = child.GetChild(p_func);
                if (grandChild != null)
                    return grandChild;
            }
            return null;
        }

        /// <summary>
        /// 获取视觉树中所有符合条件的子元素集合（使用 yield return 实现延迟加载，内存更优）。
        /// </summary>
        public static IEnumerable<T> GetChildren<T>(this DependencyObject p_element, Func<T, bool> p_func = null, bool filterContain = false) where T : UIElement
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(p_element); i++)
            {
                UIElement child = VisualTreeHelper.GetChild(p_element, i) as FrameworkElement;
                if (child == null) continue;

                if (child is T t)
                {
                    // 先递归获取子层级的匹配项
                    foreach (T c in child.GetChildren(p_func, filterContain)) yield return c;

                    // 判断当前节点是否符合过滤条件
                    if (p_func != null && !p_func(t))
                    {
                        if (filterContain) yield return t;
                        continue;
                    }
                    yield return t;
                }
                else
                {
                    // 继续往下找
                    foreach (T c in child.GetChildren(p_func, filterContain)) yield return c;
                }
            }
        }

        /// <summary>
        /// 向上遍历视觉树，寻找指定类型的父/祖先元素。
        /// 工业应用场景：在子控件的点击事件中，寻找最外层的主容器或 Window。
        /// </summary>
        public static T GetParent<T>(this DependencyObject element) where T : DependencyObject
        {
            if (element == null) return null;
            DependencyObject parent = VisualTreeHelper.GetParent(element);

            while (parent != null && !(parent is T))
            {
                DependencyObject newVisualParent = VisualTreeHelper.GetParent(parent);
                if (newVisualParent != null)
                {
                    parent = newVisualParent;
                }
                else
                {
                    // 如果视觉树断了，尝试通过逻辑树（Parent属性）继续往上找
                    if (parent is FrameworkElement fe) parent = fe.Parent;
                    else break;
                }
            }
            return parent as T;
        }

        /// <summary>
        /// 向上遍历视觉树，寻找符合条件的父/祖先元素。
        /// </summary>
        public static T GetParent<T>(this DependencyObject element, Func<T, bool> p_func) where T : DependencyObject
        {
            if (element == null) return null;
            DependencyObject parent = VisualTreeHelper.GetParent(element);
            while (parent != null && (!(parent is T) || !p_func(parent as T)))
            {
                if (parent == null) break;
                DependencyObject newVisualParent = VisualTreeHelper.GetParent(parent);
                if (newVisualParent != null)
                {
                    parent = newVisualParent;
                }
                else
                {
                    if (parent is FrameworkElement fe) parent = fe.Parent;
                    else break;
                }
            }
            return parent as T;
        }

        /// <summary>
        /// 获取控件模板 (ControlTemplate) 内部具有指定名称的元素。
        /// 工业应用场景：自定义复杂控件（如带进度条的加工按钮），需要在后台获取模板里的内部组件时。
        /// </summary>
        public static T GetTemplateByName<T>(this Control control, string name) where T : FrameworkElement
        {
            ControlTemplate template = control.Template;
            if (template != null)
                return template.FindName(name, control) as T;
            return null;
        }

        #endregion

        #region 特殊元素查找 (Popup处理)

        /// <summary>
        /// 获取当前树下第一个指定类型的元素，特别处理了 Popup 弹窗。
        /// 老师科普：Popup 控件的内容是不在主视觉树里的，所以原生的 VisualTreeHelper 找不到里面的东西，这里做了专门处理。
        /// </summary>
        public static T GetElement<T>(this DependencyObject element) where T : FrameworkElement
        {
            if (element is T correctlyTyped) return correctlyTyped;

            if (element != null)
            {
                int numChildren = VisualTreeHelper.GetChildrenCount(element);
                for (int i = 0; i < numChildren; i++)
                {
                    T child = (VisualTreeHelper.GetChild(element, i) as FrameworkElement).GetElement<T>();
                    if (child != null) return child;
                }

                // 重点：跨越 Popup 的边界去寻找
                if (element is Popup popup)
                    return (popup.Child as FrameworkElement).GetElement<T>();
            }
            return null;
        }

        /// <summary>获取包含 Popup 内容在内的所有指定类型子元素。</summary>
        public static IEnumerable<T> GetElements<T>(this DependencyObject element) where T : FrameworkElement
        {
            if (element is T correctlyTyped) yield return correctlyTyped;

            if (element != null)
            {
                int numChildren = VisualTreeHelper.GetChildrenCount(element);
                for (int i = 0; i < numChildren; i++)
                {
                    foreach (T item in (VisualTreeHelper.GetChild(element, i) as FrameworkElement).GetElements<T>())
                    {
                        yield return item;
                    }
                }

                if (element is Popup popup)
                {
                    foreach (T item in (popup.Child as FrameworkElement).GetElements<T>())
                    {
                        yield return item;
                    }
                }
            }
        }
        #endregion

        #region Visual State (视觉状态)

        /// <summary>尝试获取视觉状态组，用于代码触发 UI 的动画状态切换。</summary>
        public static VisualStateGroup TryGetVisualStateGroup(this DependencyObject d, string groupName)
        {
            FrameworkElement root = d.GetImplementationRoot();
            if (root == null) return null;

            return VisualStateManager
                .GetVisualStateGroups(root)?
                .OfType<VisualStateGroup>()
                .FirstOrDefault(group => string.CompareOrdinal(groupName, group.Name) == 0);
        }

        public static FrameworkElement GetImplementationRoot(this DependencyObject d)
        {
            return 1 == VisualTreeHelper.GetChildrenCount(d)
                ? VisualTreeHelper.GetChild(d, 0) as FrameworkElement
                : null;
        }

        #endregion

        #region 树形打印与递归 (Tree Iteration)

        /// <summary>将整个视觉树扁平化输出为集合，便于 Linq 查询。</summary>
        public static IEnumerable<DependencyObject> PrintVisualTree(this DependencyObject obj)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(obj); i++)
            {
                List<DependencyObject> from = VisualTreeHelper.GetChild(obj, i).PrintVisualTree().ToList();
                foreach (DependencyObject item in from) yield return item;
            }
            yield return obj;
        }

        /// <summary>将整个逻辑树扁平化输出。</summary>
        public static IEnumerable<DependencyObject> PrintLogicalTree(this DependencyObject obj)
        {
            foreach (object v in LogicalTreeHelper.GetChildren(obj))
            {
                if (v is DependencyObject depObj)
                {
                    IEnumerable<DependencyObject> from = depObj.PrintLogicalTree();
                    foreach (DependencyObject item in from) yield return item;
                }
            }
            yield return obj;
        }

        public static IEnumerable<T> FindAllVisualChild<T>(this DependencyObject obj, Predicate<T> match) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(obj); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(obj, i);

                if (child != null && child is T t)
                {
                    if (match(t)) yield return t;
                }

                IEnumerable<T> from = child.FindAllVisualChild(match);
                foreach (T item in from) yield return item;
            }
        }

        /// <summary>获取所有祖先节点。</summary>
        public static IEnumerable<DependencyObject> Ancestors(this DependencyObject dependencyObject)
        {
            DependencyObject parent = dependencyObject;
            while (true)
            {
                parent = parent.GetParent();
                if (parent != null) yield return parent;
                else break;
            }
        }

        /// <summary>获取包含自身的所有祖先节点。</summary>
        public static IEnumerable<DependencyObject> AncestorsAndSelf(this DependencyObject dependencyObject)
        {
            if (dependencyObject == null) throw new ArgumentNullException(nameof(dependencyObject));

            DependencyObject parent = dependencyObject;
            while (true)
            {
                if (parent != null) yield return parent;
                else break;
                parent = parent.GetParent();
            }
        }

        /// <summary>
        /// 获取父节点的高级实现。
        /// 老师科普：WPF 中有一些特殊的元素（如 ContentElement，通常用于 FlowDocument 文字排版），它们不在传统视觉树上。此方法兼容了这些特殊元素的寻找。
        /// </summary>
        public static DependencyObject GetParent(this DependencyObject dependencyObject)
        {
            if (dependencyObject == null) throw new ArgumentNullException(nameof(dependencyObject));

            if (dependencyObject is ContentElement ce)
            {
                DependencyObject parent = ContentOperations.GetParent(ce);
                if (parent != null) return parent;

                return ce is FrameworkContentElement fce ? fce.Parent : null;
            }

            return VisualTreeHelper.GetParent(dependencyObject);
        }

        public static DependencyObject GetParent(this DependencyObject fe, Type lookForType)
        {
            fe = VisualTreeHelper.GetParent(fe);
            while (fe != null)
            {
                if (lookForType.IsInstanceOfType(fe)) return fe;
                fe = VisualTreeHelper.GetParent(fe);
            }
            return null;
        }

        #endregion

        #region 跨线程安全访问 (Thread-Safe UI Access)
        /* 🚨 工业核心重点：
         * 相机采图（如 Halcon 回调）、运动卡轴状态刷新通常在后台子线程。
         * WPF 严格规定：非 UI 线程绝对不能直接修改界面元素！
         * 这两个方法自动判断当前线程，帮你安全地跨线程读写属性。
         */

        /// <summary>线程安全地获取依赖属性的值。</summary>
        public static T GetValueSync<T>(this DependencyObject obj, DependencyProperty property)
        {
            if (obj.CheckAccess()) // 如果是在 UI 线程，直接获取
                return (T)obj.GetValue(property);
            else // 如果在后台线程，交给 Dispatcher 调度到 UI 线程获取
                return (T)obj.Dispatcher.Invoke(new Func<object>(() => obj.GetValue(property)));
        }

        /// <summary>线程安全地设置依赖属性的值。</summary>
        public static void SetValueSync<T>(this DependencyObject obj, DependencyProperty property, T value)
        {
            if (obj.CheckAccess())
                obj.SetValue(property, value);
            else
                obj.Dispatcher.Invoke(new Action(() => obj.SetValue(property, value)));
        }

        #endregion

        #region 装饰器操作 (Adorners)
        /* 老师科普：什么是 Adorner？
         * 装饰器是悬浮在 UI 控件上方的一个独立图层。
         * 在视觉软件中，常常用它来实现用户鼠标框选 ROI（感兴趣区域），或者给故障设备控件画一个红色的外框。
         */

        /// <summary>获取元素上的装饰器集合。</summary>
        public static IEnumerable<Adorner> GetAdorners(this UIElement element, Predicate<Adorner> predicate = null)
        {
            AdornerLayer layer = AdornerLayer.GetAdornerLayer(element);
            if (layer == null) return null;
            return layer.GetAdorners(element)?.Where(l => predicate?.Invoke(l) != false);
        }

        /// <summary>获取元素上的指定装饰器。</summary>
        public static Adorner GetAdorner(this UIElement element, Predicate<Adorner> predicate = null)
        {
            return element.GetAdorners(predicate)?.FirstOrDefault();
        }

        /// <summary>为元素添加装饰器。</summary>
        public static bool AddAdorner(this UIElement element, Adorner adorner)
        {
            AdornerLayer layer = AdornerLayer.GetAdornerLayer(element);
            if (layer == null) return false;
            layer.Add(adorner);
            return true;
        }

        /// <summary>清除元素上符合条件的装饰器。</summary>
        public static bool ClearAdorner(this UIElement element, Predicate<Adorner> predicate = null)
        {
            AdornerLayer layer = AdornerLayer.GetAdornerLayer(element);
            if (layer == null) return false;

            IEnumerable<Adorner> adorners = element.GetAdorners(predicate);
            if (adorners == null) return true;

            foreach (Adorner item in adorners)
            {
                layer.Remove(item);
            }
            return true;
        }

        #endregion

        #region 命中测试 (Hit Testing)
        /* 命中测试用于检测在给定的坐标点（Point）或区域下，有哪些 UI 元素。
         * 常用于拖拽功能（Drag and Drop）或者在画布上判断鼠标点中了哪根线/哪个模块。
         */

        /// <summary>基于坐标点的高级命中测试。</summary>
        public static T HitTest<T>(this UIElement element, Point point, Predicate<T> predicate = null) where T : DependencyObject
        {
            T result = null;

            // 过滤器：忽略不需要检测的层（提升性能）
            HitTestFilterCallback hitTestFilterCallback = x =>
            {
                Type type = x.GetType();
                if (type.Name == element.GetType().Name)
                    return HitTestFilterBehavior.ContinueSkipSelf;
                if (x is T t && predicate?.Invoke(t) != false)
                {
                    result = t;
                }
                return HitTestFilterBehavior.Continue;
            };

            // 结果收集器：命中目标后停止或继续
            HitTestResultCallback hitTestResultCallback = x =>
            {
                if (x.VisualHit is T t && predicate?.Invoke(t) != false)
                {
                    return HitTestResultBehavior.Stop;
                }
                return HitTestResultBehavior.Continue;
            };

            PointHitTestParameters parameters = new PointHitTestParameters(point);
            VisualTreeHelper.HitTest(element, hitTestFilterCallback, hitTestResultCallback, parameters);
            return result;
        }

        /// <summary>基于坐标点的简单命中测试。</summary>
        public static T HitTest<T>(this UIElement element, Point point) where T : DependencyObject
        {
            HitTestResult result = VisualTreeHelper.HitTest(element, point);
            return result?.VisualHit as T; // 增加判空保护
        }

        /// <summary>基于几何图形(Geometry)的命中测试。</summary>
        public static T HitTest<T>(this UIElement element, Geometry geometry, Predicate<T> predicate = null) where T : DependencyObject
        {
            T result = null;
            // 注意：这里获取鼠标位置可能会在非鼠标事件中抛异常，实际使用时需要确保上下文
            Point point = Mouse.GetPosition(element);

            HitTestFilterCallback hitTestFilterCallback = x =>
            {
                Type type = x.GetType();
                if (type.Name == element.GetType().Name)
                    return HitTestFilterBehavior.ContinueSkipSelf;
                if (x is T t && predicate?.Invoke(t) != false)
                {
                    result = t;
                }
                return HitTestFilterBehavior.Continue;
            };

            HitTestResultCallback hitTestResultCallback = x =>
            {
                if (x.VisualHit is T t && predicate?.Invoke(t) != false)
                {
                    return HitTestResultBehavior.Stop;
                }
                return HitTestResultBehavior.Continue;
            };

            VisualTreeHelper.HitTest(element, hitTestFilterCallback, hitTestResultCallback, new GeometryHitTestParameters(geometry));
            return result;
        }

        #endregion

        #region 数据绑定获取 (Data Extraction)

        /// <summary>获取元素绑定的 ViewModel (DataContext)。</summary>
        public static object GetDataContext(this UIElement element)
        {
            return element is FrameworkElement framework ? framework.DataContext : null;
        }

        /// <summary>获取内容控件 (ContentControl) 内部的实际内容。</summary>
        public static object GetContent(this UIElement element)
        {
            if (element is ContentPresenter cp) return cp.Content;
            if (element is ContentControl cc) return cc.Content;
            if (element is FrameworkElement fe) return fe.DataContext;
            return null;
        }

        /// <summary>获取列表控件（如 ListBox, DataGrid）绑定的数据源集合。</summary>
        public static T GetItemsSource<T>(this UIElement element) where T : IEnumerable
        {
            if (element is ItemsControl items) return (T)items.ItemsSource;
            return default;
        }

        #endregion

        #region 根节点获取
        /// <summary>
        /// 一直向上回溯，获取整个界面的最外层根节点（通常是 Window 对象）。
        /// </summary>
        public static T GetVisualRoot<T>(this DependencyObject child) where T : DependencyObject
        {
            while (true)
            {
                var parentObject = VisualTreeHelper.GetParent(child);
                if (parentObject == null)
                {
                    return child as T;
                }
                child = parentObject;
            }
        }
        #endregion
    }
}