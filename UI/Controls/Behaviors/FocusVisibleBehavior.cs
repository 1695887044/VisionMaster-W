using System.Windows;
using System.Windows.Input;

namespace UI.Behaviors
{
    /// <summary>
    /// WPF 版 <c>:focus-visible</c>：焦点环只在"焦点来自键盘"时亮，鼠标点击不亮。
    ///
    /// 【为什么需要它】WPF 没有 CSS 的 <c>:focus-visible</c> —— 鼠标点击按钮同样会落键盘焦点，
    /// 若模板里用 <c>IsKeyboardFocused</c> 触发器点亮焦点环，用户每次点击都会看到一圈环；
    /// 对开关这种"蓝色轨道 + 蓝色焦点环"的控件，两圈蓝叠在一起会被看成"画坏了"
    /// （现场截图：整体像一团蓝环套蓝环，用户以为是容器把它撑坏了）。
    ///
    /// 【判据】<see cref="InputManager.MostRecentInputDevice"/>：焦点落到控件上的那一刻，
    /// 它记录的是最近一次输入设备 —— 键盘（Tab）来的焦点是 <see cref="KeyboardDevice"/>，
    /// 鼠标点击是 <see cref="MouseDevice"/>。这正是浏览器实现 :focus-visible 的同款思路。
    /// 另外鼠标在控件上按下时即使焦点还在（先 Tab 后点击），环也立即熄灭 —— 与浏览器行为一致。
    ///
    /// 【与模板的契约】直接驱动模板里名为 <c>FocusRing</c> 的元素（Opacity 1/0），
    /// 所以模板里<b>不要再放</b> IsKeyboardFocused 触发器，环只归本行为管。
    ///
    /// 【用法】控件创建后：<c>FocusVisibleBehavior.SetEnabled(control, true);</c>
    /// </summary>
    public static class FocusVisibleBehavior
    {
        /// <summary>焦点环部件在模板里的名字（两个开关模板都遵守）</summary>
        private const string RingPartName = "FocusRing";

        /// <summary>挂到控件上开启行为</summary>
        public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
            "Enabled", typeof(bool), typeof(FocusVisibleBehavior), new PropertyMetadata(false, OnEnabledChanged));

        public static bool GetEnabled(DependencyObject obj) => (bool)obj.GetValue(EnabledProperty);
        public static void SetEnabled(DependencyObject obj, bool value) => obj.SetValue(EnabledProperty, value);

        /// <summary>防重复挂事件（Enabled 可能被反复赋同一值）</summary>
        private static readonly DependencyProperty HookedProperty = DependencyProperty.RegisterAttached(
            "Hooked", typeof(bool), typeof(FocusVisibleBehavior), new PropertyMetadata(false));

        private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not UIElement element) return;

            bool want = (bool)e.NewValue;
            if ((bool)d.GetValue(HookedProperty) == want) return;
            d.SetValue(HookedProperty, want);

            if (want)
            {
                element.GotKeyboardFocus += OnGotKeyboardFocus;
                element.LostKeyboardFocus += OnLostKeyboardFocus;
                element.PreviewMouseDown += OnPreviewMouseDown;
            }
            else
            {
                element.GotKeyboardFocus -= OnGotKeyboardFocus;
                element.LostKeyboardFocus -= OnLostKeyboardFocus;
                element.PreviewMouseDown -= OnPreviewMouseDown;
                ShowRing(d, false);
            }
        }

        private static void OnGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            // 取不到输入管理器（极端时机）按"键盘焦点"处理：多亮一次环比永远不亮安全
            bool fromKeyboard = InputManager.Current?.MostRecentInputDevice is not MouseDevice;
            ShowRing(sender as DependencyObject, fromKeyboard);
        }

        private static void OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
            => ShowRing(sender as DependencyObject, false);

        private static void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
            => ShowRing(sender as DependencyObject, false);

        /// <summary>直接点亮/熄灭模板里的焦点环（模板触发器不再管这件事）</summary>
        private static void ShowRing(DependencyObject? d, bool visible)
        {
            // Template 在 Control 上（FrameworkElement 没有），开关恰好都是控件
            if (d is System.Windows.Controls.Control fe
                && fe.Template?.FindName(RingPartName, fe) is FrameworkElement ring)
            {
                ring.Opacity = visible ? 1 : 0;
            }
        }
    }
}
