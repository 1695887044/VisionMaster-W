using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace UI.CustomControl
{
    /// <summary>
    /// 数值输入框：**上下键/滚轮按步进加减、越界自动钳到 [Minimum, Maximum]、单位后缀、
    /// 非法输入回退上一个有效值并标红**。
    ///
    /// 【为什么要有它】属性网格里的数值原先走 <c>StructValueGenerator</c> 的**裸 TextBox**：
    /// 没有钳制、没有小数位、没有单位，解析还跟着区域设置走 —— 小数点分隔符不是 '.' 的机器上，
    /// 用户按习惯敲 "1.5" 会静默失败（值不变、也没有任何提示）。
    ///
    /// 【解析策略】先按当前区域设置、再按不变区域设置各试一次：
    /// zh-CN 下 "1.5" 两种都成立；de-DE 下用户敲 "1,5"（当前区域）与 "1.5"（不变区域）都能认。
    /// 两边都不成立才算非法 —— 那时**不改值**，把文本退回上一个有效值并置 <see cref="IsInvalid"/>。
    ///
    /// 【钳制】走 <see cref="ValueProperty"/> 的 CoerceValueCallback（不是提交时再夹一道）：
    /// 于是"外部设了个越界值"与"用户敲了个越界值"走同一条路，代理属性读回来一定是夹过的值。
    /// </summary>
    [TemplatePart(Name = "PART_ContentHost", Type = typeof(ScrollViewer))]
    public class NumericBox : TextBox
    {
        /// <summary>Value → Text 期间不要再把 Text 解析回 Value（防回环）</summary>
        private bool _isSyncingText;

        /// <summary>上一个有效值：非法输入时回退用</summary>
        private double _lastValidValue;

        static NumericBox()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(NumericBox), new FrameworkPropertyMetadata(typeof(NumericBox)));
        }

        #region 依赖属性

        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register(nameof(Value), typeof(double), typeof(NumericBox),
                new FrameworkPropertyMetadata(double.NaN,
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    OnValueChanged, CoerceValue));

        /// <summary>当前值（钳制后）。NaN = 无值（清空输入时不报错，显示为空）</summary>
        public double Value
        {
            get => (double)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        public static readonly DependencyProperty MinimumProperty =
            DependencyProperty.Register(nameof(Minimum), typeof(double), typeof(NumericBox),
                new PropertyMetadata(double.MinValue, OnRangeChanged));

        public double Minimum
        {
            get => (double)GetValue(MinimumProperty);
            set => SetValue(MinimumProperty, value);
        }

        public static readonly DependencyProperty MaximumProperty =
            DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(NumericBox),
                new PropertyMetadata(double.MaxValue, OnRangeChanged));

        public double Maximum
        {
            get => (double)GetValue(MaximumProperty);
            set => SetValue(MaximumProperty, value);
        }

        /// <summary>小数位：-1 = 不强制（按输入原样保留，整数类型用 0）。</summary>
        public static readonly DependencyProperty DecimalPlacesProperty =
            DependencyProperty.Register(nameof(DecimalPlaces), typeof(int), typeof(NumericBox),
                new PropertyMetadata(-1, OnValueChanged));

        public int DecimalPlaces
        {
            get => (int)GetValue(DecimalPlacesProperty);
            set => SetValue(DecimalPlacesProperty, value);
        }

        /// <summary>上下键 / 滚轮 / 未来加按钮时的步进量</summary>
        public static readonly DependencyProperty StepProperty =
            DependencyProperty.Register(nameof(Step), typeof(double), typeof(NumericBox),
                new PropertyMetadata(1.0));

        public double Step
        {
            get => (double)GetValue(StepProperty);
            set => SetValue(StepProperty, value);
        }

        /// <summary>单位后缀（"ms" / "mm/pixel" …）；空串不占位</summary>
        public static readonly DependencyProperty SuffixProperty =
            DependencyProperty.Register(nameof(Suffix), typeof(string), typeof(NumericBox),
                new PropertyMetadata(string.Empty));

        public string Suffix
        {
            get => (string)GetValue(SuffixProperty);
            set => SetValue(SuffixProperty, value);
        }

        private static readonly DependencyPropertyKey IsInvalidPropertyKey =
            DependencyProperty.RegisterReadOnly(nameof(IsInvalid), typeof(bool), typeof(NumericBox),
                new PropertyMetadata(false));

        public static readonly DependencyProperty IsInvalidProperty = IsInvalidPropertyKey.DependencyProperty;

        /// <summary>上次提交的文本不合法（模板里据此把边框标红）。任何一次成功提交或输入变化都会清掉。</summary>
        public bool IsInvalid => (bool)GetValue(IsInvalidProperty);

        public static readonly DependencyProperty RevertOnInvalidProperty =
            DependencyProperty.Register(nameof(RevertOnInvalid), typeof(bool), typeof(NumericBox),
                new PropertyMetadata(true));

        /// <summary>
        /// 非法输入时是否把文本退回上一个有效值（默认 true = 回退）。
        /// 置 false = **保留用户敲的文本**、只置 <see cref="IsInvalid"/> 标红 —— 给"校验与提示由上层负责"的表单用：
        /// SCADA 属性面板就是这一种（它自己维护 IsValid/ErrorText，并要留住用户输入好让他在原处改）。
        /// </summary>
        public bool RevertOnInvalid
        {
            get => (bool)GetValue(RevertOnInvalidProperty);
            set => SetValue(RevertOnInvalidProperty, value);
        }

        #endregion

        public NumericBox()
        {
            // 只有"输入"一种意图：Enter 提交、Esc 回退
            PreviewKeyDown += OnPreviewKeyDown;
            PreviewMouseWheel += OnPreviewMouseWheel;
            LostKeyboardFocus += (_, _) => Commit();
            TextChanged += (_, _) =>
            {
                if (!_isSyncingText)
                    SetValue(IsInvalidPropertyKey, false);
            };
        }

        #region 同步与提交

        private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((NumericBox)d).SyncTextFromValue();

        private static void OnRangeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var box = (NumericBox)d;
            // 范围变了要重新夹一次现值：把 200 的上限收紧到 100，界面上不能还留着 200
            box.CoerceValue(ValueProperty);
            box.SyncTextFromValue();
        }

        private static object CoerceValue(DependencyObject d, object baseValue)
        {
            var box = (NumericBox)d;
            if (baseValue is not double v) return baseValue;

            // NaN = 无值：不参与钳制（否则会被夹成 Min 或 Max，清空输入就变成了"最小值"）
            if (double.IsNaN(v)) return v;
            if (v < box.Minimum) return box.Minimum;
            if (v > box.Maximum) return box.Maximum;
            return v;
        }

        /// <summary>把 Value 刷到文本上（外部改值、范围变化、提交后归位都走它）</summary>
        private void SyncTextFromValue()
        {
            _lastValidValue = Value;

            _isSyncingText = true;
            try
            {
                Text = double.IsNaN(Value) ? string.Empty : Format(Value);
            }
            finally
            {
                _isSyncingText = false;
            }
        }

        private string Format(double v) => DecimalPlaces >= 0
            ? v.ToString("F" + DecimalPlaces, CultureInfo.CurrentCulture)
            // G15：够表达 double 的有效位，又不会把 0.1+0.2 印成 0.30000000000000004
            : v.ToString("G15", CultureInfo.CurrentCulture);

        /// <summary>提交文本：解析成功就夹一次写回，失败就退回上一个有效值并标红。</summary>
        private void Commit()
        {
            if (IsReadOnly) return;

            var text = Text?.Trim() ?? string.Empty;

            // 空 = 清空（显式允许：调用方若要"必填"，用它自己的 [Required] 校验去管）
            if (text.Length == 0)
            {
                SetValue(IsInvalidPropertyKey, false);
                Value = double.NaN;
                return;
            }

            if (TryParse(text, out var parsed))
            {
                SetValue(IsInvalidPropertyKey, false);
                Value = parsed;              // 越界由 CoerceValue 夹
                SyncTextFromValue();         // 夹完 / 格式化后再刷一次文本（"007" → "7"）
                return;
            }

            // 非法：值不动、边框标红；文本是否退回上一个有效值由 RevertOnInvalid 决定
            // （false = 留住用户敲的内容只标红 —— 上层 VM 自己负责报错时用它）
            SetValue(IsInvalidPropertyKey, true);
            if (RevertOnInvalid)
                SyncTextFromValue();
        }

        private bool TryParse(string text, out double value)
        {
            const NumberStyles styles = NumberStyles.Float | NumberStyles.AllowThousands;

            // 先当前区域（de-DE 的 "1,5"），再不变区域（"1.5" 在任何机器上都认）
            return double.TryParse(text, styles, CultureInfo.CurrentCulture, out value)
                   || double.TryParse(text, styles, CultureInfo.InvariantCulture, out value);
        }

        #endregion

        #region 键盘与滚轮

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Enter:
                    Commit();
                    e.Handled = true;
                    // 提交后把焦点交给下一个控件（表格里连续录入时省一次 Tab）
                    MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                    break;

                case Key.Escape:
                    SyncTextFromValue();     // 放弃本次输入
                    SetValue(IsInvalidPropertyKey, false);
                    e.Handled = true;
                    break;

                case Key.Up:
                    Nudge(+Step);
                    e.Handled = true;
                    break;

                case Key.Down:
                    Nudge(-Step);
                    e.Handled = true;
                    break;
            }
        }

        /// <summary>
        /// 滚轮：只有**焦点在本框内**时才改值。
        /// 不做这个限制的话，用户滚页面路过这里就会把参数悄悄改掉 —— 表单里最讨厌的一种误操作。
        /// </summary>
        private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (!IsKeyboardFocusWithin || IsReadOnly || e.Delta == 0) return;

            Nudge(e.Delta > 0 ? +Step : -Step);
            e.Handled = true;
        }

        private void Nudge(double delta)
        {
            if (IsReadOnly) return;

            // 从"当前文本"起步而不是从 Value：用户敲了 12 还没提交就按上键，应该得到 13 而不是 Value+1
            if (!TryParse(Text?.Trim() ?? string.Empty, out var basis))
                basis = double.IsNaN(Value) ? 0 : Value;

            Value = basis + delta;   // 越界由 CoerceValue 夹
            SyncTextFromValue();
        }

        #endregion
    }
}
