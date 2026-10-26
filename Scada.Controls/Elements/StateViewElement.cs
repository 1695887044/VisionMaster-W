using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// 状态显示 / 状态操作图元（TypeKey = <c>Hmi.StateView</c>）：用一张方案级文本列表把
    /// 一个整数翻译成“文字 / 图片 / 文字+图片”，按需支持点击切换或写定值。
    ///
    /// 三个常见预设共用这一个类型：
    /// · 字指示灯：Presentation=Text、Interaction=None
    /// · 图形 IO 域：Presentation=Image、Interaction=None
    /// · 图形开关：Presentation=Image、Interaction=Toggle
    ///
    /// 数据源与 ComboBox 同一份 <see cref="ScadaStateList"/>：列表缺失时不静默回退 Options，
    /// 而是显示“未找到文本列表”；值无匹配时用列表的 DefaultText / DefaultImage。
    /// </summary>
    public partial class StateViewElement : ScadaElementBase
    {
        public const string PresentationText = "Text";
        public const string PresentationImage = "Image";
        public const string PresentationBoth = "TextAndImage";

        public const string InteractionNone = "None";
        public const string InteractionToggle = "Toggle";
        public const string InteractionWriteValue = "WriteValue";

        private const string ValueKey = "Value";

        [ScadaProperty(Key = "Value", DisplayName = "当前值", Kind = ElementPropertyKind.Number,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "0", IsBindable = true,
                       Description = "运行态把工程变量的整数绑到这里；列表按这个值找文字/图片/状态色")]
        public partial int Value { get; set; }

        [ScadaProperty(Key = "StateListId", DisplayName = "文本列表", Kind = ElementPropertyKind.StateList,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "", IsBindable = false,
                       Description = "方案级共享文本列表：一份定义、多个状态图元引用；列表改名不打断引用")]
        public partial string? StateListId { get; set; }

        [ScadaProperty(Key = "Presentation", DisplayName = "呈现方式", Kind = ElementPropertyKind.Choice,
                       Group = ScadaPropertyGroups.General, DefaultValue = "TextAndImage",
                       Choices = new[] { "Text", "Image", "TextAndImage" }, AffectsRender = false,
                       Description = "Text = 字指示灯；Image = 图形 IO / 图形开关；TextAndImage = 图文")]
        public partial string? Presentation { get; set; }

        [ScadaProperty(Key = "Interaction", DisplayName = "交互", Kind = ElementPropertyKind.Choice,
                       Group = ScadaPropertyGroups.General, DefaultValue = "None",
                       Choices = new[] { "None", "Toggle", "WriteValue" }, AffectsRender = false,
                       Description = "None = 只读；Toggle = 点击在 0/1 间切换；WriteValue = 点击写 ClickValue")]
        public partial string? Interaction { get; set; }

        [ScadaProperty(Key = "ClickValue", DisplayName = "点击写入值", Kind = ElementPropertyKind.Number,
                       Group = ScadaPropertyGroups.General, DefaultValue = "0", AffectsRender = false,
                       Description = "Interaction=WriteValue 时点击写入的整数值")]
        public partial int ClickValue { get; set; }

        [ScadaProperty(Key = "ImageStretch", DisplayName = "图片缩放", Kind = ElementPropertyKind.Choice,
                       Group = ScadaPropertyGroups.Image, DefaultValue = "Uniform",
                       Choices = new[] { "Uniform", "UniformToFill", "Fill" }, AffectsRender = false,
                       Description = "图片呈现时的缩放规则；默认等比缩放不变形")]
        public partial string? ImageStretch { get; set; }

        private static readonly DependencyPropertyKey CurrentTextPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(CurrentText), typeof(string), typeof(StateViewElement), new PropertyMetadata(string.Empty));
        public static readonly DependencyProperty CurrentTextProperty = CurrentTextPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey CurrentImagePropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(CurrentImage), typeof(ImageSource), typeof(StateViewElement), new PropertyMetadata(null));
        public static readonly DependencyProperty CurrentImageProperty = CurrentImagePropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey CurrentColorPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(CurrentColor), typeof(Brush), typeof(StateViewElement), new PropertyMetadata(Brushes.White));
        public static readonly DependencyProperty CurrentColorProperty = CurrentColorPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey CurrentStretchPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(CurrentStretch), typeof(Stretch), typeof(StateViewElement), new PropertyMetadata(Stretch.Uniform));
        public static readonly DependencyProperty CurrentStretchProperty = CurrentStretchPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey StateListMissingPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(StateListMissing), typeof(bool), typeof(StateViewElement), new PropertyMetadata(false));
        public static readonly DependencyProperty StateListMissingProperty = StateListMissingPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey ShowTextPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(ShowText), typeof(bool), typeof(StateViewElement), new PropertyMetadata(true));
        public static readonly DependencyProperty ShowTextProperty = ShowTextPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey ShowImagePropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(ShowImage), typeof(bool), typeof(StateViewElement), new PropertyMetadata(false));
        public static readonly DependencyProperty ShowImageProperty = ShowImagePropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey ImageMissingPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(ImageMissing), typeof(bool), typeof(StateViewElement), new PropertyMetadata(false));
        public static readonly DependencyProperty ImageMissingProperty = ImageMissingPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey CanOperatePropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(CanOperate), typeof(bool), typeof(StateViewElement), new PropertyMetadata(false));
        public static readonly DependencyProperty CanOperateProperty = CanOperatePropertyKey.DependencyProperty;

        public static readonly DependencyProperty WriteErrorProperty = DependencyProperty.Register(
            nameof(WriteError), typeof(string), typeof(StateViewElement), new PropertyMetadata(string.Empty));

        static StateViewElement()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(StateViewElement), new FrameworkPropertyMetadata(typeof(StateViewElement)));
        }

        public StateViewElement()
        {
            Focusable = false;
            Loaded += (_, _) => UpdateVisual();
            Unloaded += (_, _) => UnsubscribeStateList();
            UpdateVisual();
        }

        public string? CurrentText => (string?)GetValue(CurrentTextProperty);
        public ImageSource? CurrentImage => (ImageSource?)GetValue(CurrentImageProperty);
        public Brush? CurrentColor => (Brush?)GetValue(CurrentColorProperty);
        public Stretch CurrentStretch => (Stretch)GetValue(CurrentStretchProperty);
        public bool StateListMissing => (bool)GetValue(StateListMissingProperty);
        public bool ShowText => (bool)GetValue(ShowTextProperty);
        public bool ShowImage => (bool)GetValue(ShowImageProperty);
        public bool ImageMissing => (bool)GetValue(ImageMissingProperty);
        public bool CanOperate => (bool)GetValue(CanOperateProperty);
        public string? WriteError
        {
            get => (string?)GetValue(WriteErrorProperty);
            set => SetValue(WriteErrorProperty, value ?? string.Empty);
        }

        private ScadaStateList? _subscribedStateList;
        private bool _pressed;

        protected override void OnRuntimeContextChanged(ScadaRuntimeContext? oldContext, ScadaRuntimeContext? newContext)
            => UpdateVisual();

        protected override void OnStateListResolverChanged() => UpdateVisual();

        protected override void OnImageAssetResolverChanged() => UpdateVisual();

        protected override void OnElementRefreshed() => UpdateVisual();

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            UpdateVisual();
        }

        partial void OnValuePropertyChanged() => UpdateVisual();
        partial void OnStateListIdPropertyChanged() => UpdateVisual();
        partial void OnPresentationPropertyChanged()
        {
            NormalizePresentation();
            UpdateVisual();
        }
        partial void OnInteractionPropertyChanged()
        {
            NormalizeInteraction();
            UpdateVisual();
        }
        partial void OnClickValuePropertyChanged() => UpdateVisual();
        partial void OnImageStretchPropertyChanged()
        {
            NormalizeImageStretch();
            UpdateVisual();
        }

        protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnPreviewMouseLeftButtonDown(e);
            if (e.Handled || !CanOperate)
                return;

            _pressed = true;
        }

        protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnPreviewMouseLeftButtonUp(e);

            bool activate = _pressed && CanOperate;
            _pressed = false;

            if (activate)
                Activate();
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            base.OnMouseLeave(e);
            _pressed = false;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (!CanOperate)
                return;

            if (e.Key == Key.Enter || e.Key == Key.Space)
            {
                Activate();
                e.Handled = true;
            }
        }

        /// <summary>执行一次用户激活（点击/键盘/断言共用）；不满足条件时静默不写</summary>
        public void Activate()
        {
            if (!CanOperate || RuntimeContext?.Writer is not { } writer || Element is not { } element)
                return;

            int target = IsToggle
                ? (Value == 1 ? 0 : 1)
                : ClickValue;

            WriteError = string.Empty;

            if (!writer.TryWriteText(element, ValueKey, target.ToString(CultureInfo.InvariantCulture), out var error))
            {
                WriteError = string.IsNullOrWhiteSpace(error) ? "写入失败" : error;
                return;
            }

            RaiseScadaEvent(ScadaEventType.ObjectChanged);

            if (target == 0)
                RaiseScadaEvent(ScadaEventType.Closed);
            else if (target == 1)
                RaiseScadaEvent(ScadaEventType.Opened);
        }

        private bool IsToggle => string.Equals(Interaction, InteractionToggle, StringComparison.OrdinalIgnoreCase);

        private bool IsReadOnly => string.Equals(Interaction, InteractionNone, StringComparison.OrdinalIgnoreCase);

        private void UpdateVisual()
        {
            var list = FindStateList();
            SubscribeStateList(list);

            bool missing = list == null && !string.IsNullOrWhiteSpace(StateListId);
            string text = list != null
                ? list.DisplayText(Value)
                : missing ? "未找到文本列表" : "—";
            string imagePath = list?.DisplayImage(Value) ?? string.Empty;
            string colorText = list?.DisplayColor(Value) ?? string.Empty;
            var image = string.IsNullOrWhiteSpace(imagePath) ? null : ResolveImageAsset(imagePath);
            var color = ParseBrush(colorText) ?? Foreground ?? Brushes.White;

            bool showText = !string.Equals(Presentation, PresentationImage, StringComparison.OrdinalIgnoreCase);
            bool wantsImage = !string.Equals(Presentation, PresentationText, StringComparison.OrdinalIgnoreCase);
            bool hasImagePath = !string.IsNullOrWhiteSpace(imagePath);
            bool showImage = wantsImage && (hasImagePath || string.Equals(Presentation, PresentationImage, StringComparison.OrdinalIgnoreCase));

            SetValue(CurrentTextPropertyKey, text);
            SetValue(CurrentImagePropertyKey, image);
            SetValue(CurrentColorPropertyKey, color);
            SetValue(CurrentStretchPropertyKey, ParseStretch(ImageStretch));
            SetValue(StateListMissingPropertyKey, missing);
            SetValue(ShowTextPropertyKey, showText);
            SetValue(ShowImagePropertyKey, showImage);
            SetValue(ImageMissingPropertyKey, showImage && image == null);

            bool canOperate = !IsReadOnly
                              && list != null
                              && IsEnabled
                              && RuntimeContext?.Writer is not null
                              && Element?.FindBinding(ValueKey) is { IsEnabled: true }
                              && RuntimeContext?.AccessPolicy.CanElement(Element) == true;

            SetValue(CanOperatePropertyKey, canOperate);
            Focusable = canOperate;
            Cursor = canOperate ? Cursors.Hand : Cursors.Arrow;
        }

        private ScadaStateList? FindStateList()
        {
            var list = ResolveStateList(StateListId);
            if (list != null)
                return list;

            if (string.IsNullOrWhiteSpace(StateListId)
                || !Guid.TryParse(StateListId, out var id))
                return null;

            return RuntimeContext?.StateLists?.FirstOrDefault(item => item.StateListId == id);
        }

        private void SubscribeStateList(ScadaStateList? list)
        {
            if (ReferenceEquals(_subscribedStateList, list))
                return;

            if (_subscribedStateList != null)
                _subscribedStateList.PropertyChanged -= OnStateListChanged;

            _subscribedStateList = list;

            if (list != null)
                list.PropertyChanged += OnStateListChanged;
        }

        private void UnsubscribeStateList()
        {
            if (_subscribedStateList == null)
                return;

            _subscribedStateList.PropertyChanged -= OnStateListChanged;
            _subscribedStateList = null;
        }

        private void OnStateListChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ScadaStateList.Version))
                UpdateVisual();
        }

        private void NormalizePresentation()
        {
            string canonical = string.Equals(Presentation, PresentationText, StringComparison.OrdinalIgnoreCase)
                ? PresentationText
                : string.Equals(Presentation, PresentationImage, StringComparison.OrdinalIgnoreCase)
                    ? PresentationImage
                    : PresentationBoth;

            if (!string.Equals(Presentation, canonical, StringComparison.Ordinal))
                Presentation = canonical;
        }

        private void NormalizeInteraction()
        {
            string canonical = string.Equals(Interaction, InteractionToggle, StringComparison.OrdinalIgnoreCase)
                ? InteractionToggle
                : string.Equals(Interaction, InteractionWriteValue, StringComparison.OrdinalIgnoreCase)
                    ? InteractionWriteValue
                    : InteractionNone;

            if (!string.Equals(Interaction, canonical, StringComparison.Ordinal))
                Interaction = canonical;
        }

        private void NormalizeImageStretch()
        {
            string canonical = string.Equals(ImageStretch, "UniformToFill", StringComparison.OrdinalIgnoreCase)
                ? "UniformToFill"
                : string.Equals(ImageStretch, "Fill", StringComparison.OrdinalIgnoreCase)
                    ? "Fill"
                    : "Uniform";

            if (!string.Equals(ImageStretch, canonical, StringComparison.Ordinal))
                ImageStretch = canonical;
        }

        private static Stretch ParseStretch(string? value)
            => string.Equals(value, "UniformToFill", StringComparison.OrdinalIgnoreCase)
                ? Stretch.UniformToFill
                : string.Equals(value, "Fill", StringComparison.OrdinalIgnoreCase)
                    ? Stretch.Fill
                    : Stretch.Uniform;

        private static Brush? ParseBrush(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            try
            {
                if (new BrushConverter().ConvertFromString(text) is Brush brush)
                {
                    if (brush.CanFreeze)
                        brush.Freeze();
                    return brush;
                }
            }
            catch
            {
                // 坏颜色不打断整页：按“没配颜色”处理，由调用方回落到文字色。
            }

            return null;
        }
    }
}
