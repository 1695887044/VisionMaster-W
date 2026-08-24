using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;


namespace UI.AttachedProperties
{
    public static class PasswordBoxHelper
    {

        // ==========================================
        // 补充：控制密码明文/密文切换的附加属性
        // ==========================================
        public static readonly DependencyProperty IsPasswordVisibleProperty =
            DependencyProperty.RegisterAttached(
                "IsPasswordVisible",
                typeof(bool),
                typeof(PasswordBoxHelper),
                new PropertyMetadata(false));

        public static void SetIsPasswordVisible(DependencyObject dp, bool value) => dp.SetValue(IsPasswordVisibleProperty, value);
        public static bool GetIsPasswordVisible(DependencyObject dp) => (bool)dp.GetValue(IsPasswordVisibleProperty);
        // 1. 定义附加依赖属性：BindablePassword
        public static readonly DependencyProperty BindablePasswordProperty =
            DependencyProperty.RegisterAttached(
                "BindablePassword",
                typeof(string),
                typeof(PasswordBoxHelper),
                new FrameworkPropertyMetadata(string.Empty, OnBindablePasswordChanged));

        // 2. 定义是否启用的属性（防止重复挂载事件）
        public static readonly DependencyProperty AttachProperty =
            DependencyProperty.RegisterAttached(
                "Attach",
                typeof(bool),
                typeof(PasswordBoxHelper),
                new PropertyMetadata(false, OnAttachChanged));

        // 用于内部标记，防止在同步过程中产生死循环
        private static readonly DependencyProperty IsUpdatingProperty =
            DependencyProperty.RegisterAttached(
                "IsUpdating", typeof(bool), typeof(PasswordBoxHelper), new PropertyMetadata(false));

        public static void SetBindablePassword(DependencyObject dp, string value) => dp.SetValue(BindablePasswordProperty, value);
        public static string GetBindablePassword(DependencyObject dp) => (string)dp.GetValue(BindablePasswordProperty);

        public static void SetAttach(DependencyObject dp, bool value) => dp.SetValue(AttachProperty, value);
        public static bool GetAttach(DependencyObject dp) => (bool)dp.GetValue(AttachProperty);

        // 当 Attach 属性在 XAML 中设为 True 时，挂载 UI 事件
        private static void OnAttachChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
        {
            if (!(sender is PasswordBox passwordBox)) return;

            if ((bool)e.OldValue) passwordBox.PasswordChanged -= PasswordBox_PasswordChanged;
            if ((bool)e.NewValue) passwordBox.PasswordChanged += PasswordBox_PasswordChanged;
        }

        // UI 层密码改变时，同步给附加属性（进而同步给 ViewModel）
        private static void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            PasswordBox passwordBox = sender as PasswordBox;
            SetIsUpdating(passwordBox, true);
            SetBindablePassword(passwordBox, passwordBox.Password);
            SetIsUpdating(passwordBox, false);
        }

        // 当附加属性（ViewModel 里的值）改变时，同步给 UI 层
        private static void OnBindablePasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is PasswordBox passwordBox)
            {
                // 如果是 UI 触发的更新，则不再反向操作，避免死循环
                if ((bool)GetIsUpdating(passwordBox)) return;

                passwordBox.PasswordChanged -= PasswordBox_PasswordChanged;
                passwordBox.Password = e.NewValue?.ToString();
                passwordBox.PasswordChanged += PasswordBox_PasswordChanged;
            }
        }

        private static bool GetIsUpdating(DependencyObject dp) => (bool)dp.GetValue(IsUpdatingProperty);
        private static void SetIsUpdating(DependencyObject dp, bool value) => dp.SetValue(IsUpdatingProperty, value);
    }
}
