using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using VisionMaster.Scada;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// 运行计时 / 计数器图元（TypeKey = <c>Hmi.RunMeter</c>）。
    ///
    /// 两种模式共用一个 TypeKey：
    /// · <b>RunTime</b>：<see cref="Source"/> 为真时累计秒数（停机暂停、恢复继续，不补算停机时间）；
    /// · <b>Counter</b>：<see cref="Source"/> 的每个上升沿加一（计数脉冲）。
    ///
    /// 节拍来自 <see cref="ScadaRuntimeContext.Beat"/>（全画面统一节拍，不自己起定时器）；
    /// 累计值每 5 秒回写模型一次（落 .vms），掉电最多丢 5 秒——设计基线写的是 1 分钟，
    /// 这里取 5 秒是“写盘频率 vs 精度”的折中，后续可配。
    ///
    /// <b>运行期写模型</b>与“写控件不写模型”的纪律有一处刻意例外：累计值是**状态**不是设计值，
    /// 不落盘就会每次打开都从 0 开始。写入只碰本图元的两个状态属性，并在写回期间抑制回读，
    /// 避免“写模型 → 属性变更 → Refresh → 覆盖内存值”的循环。
    /// </summary>
    [TemplatePart(Name = PartValue, Type = typeof(TextBlock))]
    [TemplatePart(Name = PartSub, Type = typeof(TextBlock))]
    public partial class RunMeterElement : ScadaElementBase
    {
        public const string PartValue = "MeterValue";
        public const string PartSub = "MeterSub";

        public const string ModeRunTime = "RunTime";
        public const string ModeCounter = "Counter";

        [ScadaProperty(Key = "Mode", DisplayName = "模式", Kind = ElementPropertyKind.Choice,
                       Group = ScadaPropertyGroups.Data, DefaultValue = ModeRunTime,
                       Choices = new[] { ModeRunTime, ModeCounter },
                       Description = "RunTime = 按运行信号累计时长；Counter = 按计数脉冲加一")]
        public partial string? Mode { get; set; }

        [ScadaProperty(Key = "Format", DisplayName = "显示格式", Kind = ElementPropertyKind.Choice,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "hms",
                       Choices = new[] { "hms", "hours", "dh", "integer", "thousands" },
                       Description = "RunTime：时:分:秒 / 小数小时 / 天+时:分；Counter：整数 / 千分位")]
        public partial string? Format { get; set; }

        [ScadaProperty(Key = "Source", DisplayName = "运行信号 / 计数脉冲", Kind = ElementPropertyKind.Bool,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "False", IsBindable = true,
                       Description = "RunTime：为真时计时；Counter：每个上升沿加一。绑一根 BOOL 变量")]
        public partial bool Source { get; set; }

        [ScadaProperty(Key = "Target", DisplayName = "目标值", Kind = ElementPropertyKind.Number,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "0", Min = 0,
                       Description = "仅 Counter：进度条与百分比的目标；0 = 不显示进度")]
        public partial double Target { get; set; }

        [ScadaProperty(Key = "AccumulatedSeconds", DisplayName = "累计秒数", Kind = ElementPropertyKind.Number,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "0", Min = 0, IsBindable = false,
                       Description = "运行期自动回写的累计值（状态，不是设计参数）")]
        public partial double AccumulatedSeconds { get; set; }

        [ScadaProperty(Key = "Count", DisplayName = "计数值", Kind = ElementPropertyKind.Number,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "0", Min = 0, IsBindable = false,
                       Description = "运行期自动回写的计数（状态，不是设计参数）")]
        public partial double Count { get; set; }

        [ScadaProperty(Key = "Label", DisplayName = "标签", Kind = ElementPropertyKind.Text,
                       Group = ScadaPropertyGroups.Text, DefaultValue = "运行时长",
                       Description = "数值上方的一行说明字")]
        public partial string? Label { get; set; }

        private TextBlock? _valueText;
        private TextBlock? _subText;

        private double _seconds;
        private double _count;
        private bool _lastSource;
        private TimeSpan _lastElapsed;
        private DateTime _lastPersistUtc = DateTime.MinValue;
        private bool _persisting;

        static RunMeterElement()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(RunMeterElement),
                new FrameworkPropertyMetadata(typeof(RunMeterElement)));
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            _valueText = GetTemplateChild(PartValue) as TextBlock;
            _subText = GetTemplateChild(PartSub) as TextBlock;
            UpdateDisplay();
        }

        protected override void OnRuntimeContextChanged(ScadaRuntimeContext? oldContext, ScadaRuntimeContext? newContext)
        {
            if (oldContext != null)
                oldContext.Beat.Beat -= OnBeat;

            if (newContext != null)
                newContext.Beat.Beat += OnBeat;

            _lastElapsed = TimeSpan.Zero;
        }

        protected override void OnElementRefreshed()
        {
            base.OnElementRefreshed();

            if (_persisting)
                return;

            _seconds = Math.Max(0, AccumulatedSeconds);
            _count = Math.Max(0, Count);
            UpdateDisplay();
        }

        partial void OnSourcePropertyChanged()
        {
            var current = Source;

            if (IsCounter && current && !_lastSource)
            {
                _count += 1;
                UpdateDisplay();
                PersistIfDue(force: false);
            }

            _lastSource = current;
        }

        partial void OnModePropertyChanged() => UpdateDisplay();
        partial void OnFormatPropertyChanged() => UpdateDisplay();
        partial void OnTargetPropertyChanged() => UpdateDisplay();
        partial void OnLabelPropertyChanged() => UpdateDisplay();

        private bool IsCounter
            => string.Equals(Mode, ModeCounter, StringComparison.OrdinalIgnoreCase);

        private void OnBeat(TimeSpan elapsed)
        {
            var delta = _lastElapsed == TimeSpan.Zero ? TimeSpan.Zero : elapsed - _lastElapsed;
            _lastElapsed = elapsed;

            if (!IsCounter && Source && delta > TimeSpan.Zero)
            {
                _seconds += delta.TotalSeconds;
                UpdateDisplay();
                PersistIfDue(force: false);
            }
        }

        /// <summary>每 5 秒把累计值写回模型（掉电最多丢 5 秒）；写回期间抑制回读。</summary>
        private void PersistIfDue(bool force)
        {
            var now = DateTime.UtcNow;
            if (!force && (now - _lastPersistUtc).TotalSeconds < 5)
                return;

            if (Element == null)
                return;

            _lastPersistUtc = now;
            _persisting = true;

            try
            {
                Element.SetProperty("AccumulatedSeconds", _seconds.ToString("0.###", CultureInfo.InvariantCulture));
                Element.SetProperty("Count", _count.ToString("0.###", CultureInfo.InvariantCulture));
            }
            finally
            {
                _persisting = false;
            }
        }

        private void UpdateDisplay()
        {
            if (_valueText != null)
                _valueText.Text = IsCounter ? FormatCount(_count, Format) : FormatValue(_seconds, Format);

            if (_subText == null)
                return;

            if (IsCounter)
            {
                if (Target > 0)
                {
                    var percent = Math.Min(100, _count / Target * 100);
                    _subText.Text = $"目标 {FormatCount(Target, "integer")} · {percent:0.#}%";
                }
                else
                {
                    _subText.Text = "累计计数";
                }
            }
            else
            {
                _subText.Text = Source ? "计时中" : "已暂停";
            }
        }

        /// <summary>时长格式化（hms / hours / dh）；负数按 0 处理。</summary>
        public static string FormatValue(double seconds, string? format)
        {
            seconds = Math.Max(0, seconds);

            switch ((format ?? "hms").ToLowerInvariant())
            {
                case "hours":
                    return (seconds / 3600d).ToString("0.0", CultureInfo.InvariantCulture) + " h";

                case "dh":
                {
                    var days = (int)(seconds / 86400d);
                    var rest = seconds - days * 86400d;
                    var h = (int)(rest / 3600d);
                    var m = (int)((rest - h * 3600d) / 60d);
                    return $"{days} 天 {h:00}:{m:00}";
                }

                default:
                {
                    var total = (long)Math.Floor(seconds);
                    var h = total / 3600;
                    var m = total % 3600 / 60;
                    var s = total % 60;
                    return $"{h:00}:{m:00}:{s:00}";
                }
            }
        }

        /// <summary>计数格式化（integer / thousands）。</summary>
        public static string FormatCount(double count, string? format)
        {
            count = Math.Max(0, count);

            return string.Equals(format, "thousands", StringComparison.OrdinalIgnoreCase)
                ? count.ToString("#,0", CultureInfo.InvariantCulture)
                : ((long)Math.Round(count)).ToString(CultureInfo.InvariantCulture);
        }
    }
}
