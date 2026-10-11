using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace UI.CustomControl
{
    public enum ScrollDirection
    {
        LeftToRight,
        RightToLeft
    }
    [TemplatePart(Name = "PART_Translate", Type = typeof(TranslateTransform))]
    [TemplatePart(Name = "PART_ItemsHost", Type = typeof(FrameworkElement))]
    public class AlarmBar : ItemsControl
    {
        private TranslateTransform _translate;
        private FrameworkElement _itemsHost;
        private double _currentX = 0;
        private bool _isTemplateApplied = false;

        static AlarmBar()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(AlarmBar), new FrameworkPropertyMetadata(typeof(AlarmBar)));
        }

        // 1. 速度属性
        public double Speed
        {
            get => (double)GetValue(SpeedProperty);
            set => SetValue(SpeedProperty, value);
        }
        public static readonly DependencyProperty SpeedProperty =
            DependencyProperty.Register("Speed", typeof(double), typeof(AlarmBar), new PropertyMetadata(1.5));

        // 2. 方向属性 (不需要绑定支持，但在代码中可配)
        public ScrollDirection Direction
        {
            get => (ScrollDirection)GetValue(DirectionProperty);
            set => SetValue(DirectionProperty, value);
        }
        public static readonly DependencyProperty DirectionProperty =
            DependencyProperty.Register("Direction", typeof(ScrollDirection), typeof(AlarmBar), new PropertyMetadata(ScrollDirection.RightToLeft));

        public bool HasAlarms
        {
            get => (bool)GetValue(HasAlarmsProperty);
            private set => SetValue(HasAlarmsProperty, value);
        }
        public static readonly DependencyProperty HasAlarmsProperty =
            DependencyProperty.Register("HasAlarms", typeof(bool), typeof(AlarmBar), new PropertyMetadata(false));

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            _translate = GetTemplateChild("PART_Translate") as TranslateTransform;
            _itemsHost = GetTemplateChild("PART_ItemsHost") as FrameworkElement;
            _isTemplateApplied = true;

            CompositionTarget.Rendering -= OnRendering;
            CompositionTarget.Rendering += OnRendering;
        }

        private void OnRendering(object sender, EventArgs e)
        {
            if (!_isTemplateApplied || _translate == null || _itemsHost == null || !HasAlarms || IsMouseOver)
                return;

            double contentWidth = _itemsHost.ActualWidth;
            double viewWidth = this.ActualWidth;

            if (contentWidth < 1) return;

            // 根据方向计算位移逻辑
            if (Direction == ScrollDirection.LeftToRight)
            {
                _currentX += Speed;
                if (_currentX > viewWidth)
                {
                    _currentX = -contentWidth;
                }
            }
            else
            {
                _currentX -= Speed;
                if (_currentX < -contentWidth)
                {
                    _currentX = viewWidth;
                }
            }

            _translate.X = _currentX;
        }

        protected override void OnItemsChanged(NotifyCollectionChangedEventArgs e)
        {
            base.OnItemsChanged(e);
            HasAlarms = Items.Count > 0;

            if (!HasAlarms)
            {
                _currentX = 0;
                if (_translate != null) _translate.X = 0;
            }
        }
    }
}
