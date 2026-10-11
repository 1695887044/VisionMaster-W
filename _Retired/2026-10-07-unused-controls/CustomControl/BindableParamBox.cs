using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace UI.CustomControl
{
    public class BindableParamBox : ContentControl
    {
        static BindableParamBox()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(BindableParamBox),
                new FrameworkPropertyMetadata(typeof(BindableParamBox)));
        }

        #region 依赖属性

        public static readonly DependencyProperty LabelProperty =
            DependencyProperty.Register(nameof(Label), typeof(string), typeof(BindableParamBox), new PropertyMetadata("参数"));

        public string Label
        {
            get => (string)GetValue(LabelProperty);
            set => SetValue(LabelProperty, value);
        }

        public static readonly DependencyProperty SourcePathProperty =
            DependencyProperty.Register(nameof(SourcePath), typeof(string), typeof(BindableParamBox),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSourcePathChanged));

        public string? SourcePath
        {
            get => (string?)GetValue(SourcePathProperty);
            set => SetValue(SourcePathProperty, value);
        }

        public static readonly DependencyProperty IsLinkedProperty =
            DependencyProperty.Register(nameof(IsLinked), typeof(bool), typeof(BindableParamBox), new PropertyMetadata(false));

        public bool IsLinked
        {
            get => (bool)GetValue(IsLinkedProperty);
            private set => SetValue(IsLinkedProperty, value);
        }

        public static readonly DependencyProperty DataTypeBadgeProperty =
            DependencyProperty.Register(nameof(DataTypeBadge), typeof(string), typeof(BindableParamBox), new PropertyMetadata("ANY"));

        public string DataTypeBadge
        {
            get => (string)GetValue(DataTypeBadgeProperty);
            set => SetValue(DataTypeBadgeProperty, value);
        }

        // 当前上游推送的实时值（供解绑时固化使用）
        public static readonly DependencyProperty CurrentRuntimeValueProperty =
            DependencyProperty.Register(nameof(CurrentRuntimeValue), typeof(object), typeof(BindableParamBox), new PropertyMetadata(null));

        public object? CurrentRuntimeValue
        {
            get => GetValue(CurrentRuntimeValueProperty);
            set => SetValue(CurrentRuntimeValueProperty, value);
        }

        public static readonly DependencyProperty LinkCommandProperty =
            DependencyProperty.Register(nameof(LinkCommand), typeof(ICommand), typeof(BindableParamBox), new PropertyMetadata(null));

        public ICommand LinkCommand
        {
            get => (ICommand)GetValue(LinkCommandProperty);
            set => SetValue(LinkCommandProperty, value);
        }

        public static readonly DependencyProperty UnlinkCommandProperty =
            DependencyProperty.Register(nameof(UnlinkCommand), typeof(ICommand), typeof(BindableParamBox), new PropertyMetadata(null));

        public ICommand UnlinkCommand
        {
            get => (ICommand)GetValue(UnlinkCommandProperty);
            set => SetValue(UnlinkCommandProperty, value);
        }

        #endregion

        private static void OnSourcePathChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is BindableParamBox box)
            {
                box.IsLinked = !string.IsNullOrWhiteSpace(e.NewValue as string);
            }
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            if (GetTemplateChild("PART_UnlinkBtn") is Button unlinkBtn)
            {
                unlinkBtn.Click += (s, e) =>
                {
                    // 触发解绑命令（外部可传参数）
                    UnlinkCommand?.Execute(CurrentRuntimeValue);
                    SourcePath = null;
                };
            }
        }
    }
}
