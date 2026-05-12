using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace UI.CustomControl
{
    public class StatusIndicator : ContentControl
    {
        static StatusIndicator()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(StatusIndicator), new FrameworkPropertyMetadata(typeof(StatusIndicator)));
        }

        #region 核心属性

        public bool IsActive
        {
            get => (bool)GetValue(IsActiveProperty);
            set => SetValue(IsActiveProperty, value);
        }

        public static readonly DependencyProperty IsActiveProperty =
            DependencyProperty.Register("IsActive", typeof(bool), typeof(StatusIndicator),
                new PropertyMetadata(false, OnIsActiveChanged));

        public Brush ActiveBrush
        {
            get => (Brush)GetValue(ActiveBrushProperty);
            set => SetValue(ActiveBrushProperty, value);
        }

        public static readonly DependencyProperty ActiveBrushProperty =
            DependencyProperty.Register("ActiveBrush", typeof(Brush), typeof(StatusIndicator), new PropertyMetadata(Brushes.LimeGreen));

        public Brush InactiveBrush
        {
            get => (Brush)GetValue(InactiveBrushProperty);
            set => SetValue(InactiveBrushProperty, value);
        }

        public static readonly DependencyProperty InactiveBrushProperty =
            DependencyProperty.Register("InactiveBrush", typeof(Brush), typeof(StatusIndicator), new PropertyMetadata(Brushes.LightGray));

        public bool IsPulsing
        {
            get => (bool)GetValue(IsPulsingProperty);
            set => SetValue(IsPulsingProperty, value);
        }

        public static readonly DependencyProperty IsPulsingProperty =
            DependencyProperty.Register("IsPulsing", typeof(bool), typeof(StatusIndicator), new PropertyMetadata(false, OnPulsingChanged));

        // 5. 圆角 (用于正方形/圆角矩形样式)
        public CornerRadius CornerRadius
        {
            get => (CornerRadius)GetValue(CornerRadiusProperty);
            set => SetValue(CornerRadiusProperty, value);
        }

        public static readonly DependencyProperty CornerRadiusProperty =
            DependencyProperty.Register("CornerRadius", typeof(CornerRadius), typeof(StatusIndicator), new PropertyMetadata(new CornerRadius(0)));

        #endregion

        #region 逻辑处理

        private static void OnIsActiveChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (StatusIndicator)d;
            control.UpdateVisualState(true);
        }

        private static void OnPulsingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (StatusIndicator)d;
            control.UpdateVisualState(true);
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            UpdateVisualState(false);
        }

        private void UpdateVisualState(bool useTransitions)
        {
            // 利用 VisualStateManager 切换状态
            if (IsActive)
            {
                if (IsPulsing)
                    VisualStateManager.GoToState(this, "ActivePulsing", useTransitions);
                else
                    VisualStateManager.GoToState(this, "Active", useTransitions);
            }
            else
            {
                VisualStateManager.GoToState(this, "Inactive", useTransitions);
            }
        }

        #endregion
    }
}
