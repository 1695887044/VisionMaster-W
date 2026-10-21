using Prism.Mvvm;
using UI.Attributes;
using VisionMaster.Communications;

namespace VisionMaster.ViewModels.DialogViewModels
{
    /// <summary>
    /// 数组元素换算的编辑草稿（<see cref="UI.CustomControl.EasyDialog.ShowPropertyGridSync"/> 的绑定对象）。
    ///
    /// <para>属性面板是就地编辑、取消不回滚，所以这里用草稿：确认才把
    /// <see cref="ElementConversion"/> 交给调用方，取消 = 整份丢弃。</para>
    ///
    /// <para>只暴露四个真正影响换算的字段：缩放系数、工程偏移、小数位、单位。
    /// 换算公式与 <see cref="ArrayConversion"/> 同源——草稿只负责收集，不复制一套公式。</para>
    /// </summary>
    public sealed class ArrayConversionDraft : BindableBase
    {
        private double _scale = 1.0;
        private double _offset;
        private int _decimalPlaces = 2;
        private string _unit = string.Empty;

        [SuperDisplay(
            Name = "缩放系数",
            Group = new[] { "换算" },
            Order = 0,
            Description = "原始值 × 本系数 + 工程偏移 = 工程值；不缩放填 1（0 非法，无法反算）")]
        public double Scale
        {
            get => _scale;
            set => SetProperty(ref _scale, value);
        }

        [SuperDisplay(
            Name = "工程偏移",
            Group = new[] { "换算" },
            Order = 1,
            Description = "缩放之后相加；反算时先减再除。不需要填 0")]
        public double Offset
        {
            get => _offset;
            set => SetProperty(ref _offset, value);
        }

        [SuperDisplay(
            Name = "小数位",
            Group = new[] { "换算" },
            Order = 2,
            Description = "读回值显示保留的小数位（0~10）")]
        public int DecimalPlaces
        {
            get => _decimalPlaces;
            set => SetProperty(ref _decimalPlaces, value);
        }

        [SuperDisplay(
            Name = "工程单位",
            Group = new[] { "换算" },
            Order = 3,
            Description = "仅用于显示，如 ℃ / MPa；留空 = 不显示")]
        public string Unit
        {
            get => _unit;
            set => SetProperty(ref _unit, value ?? string.Empty);
        }

        [SuperDisplay(
            Name = "说明",
            Group = new[] { "换算" },
            Order = 4,
            IsReadOnly = true)]
        public string Hint =>
            "每个元素可以有自己的换算；「整组换算」会覆盖全部元素，个别项再用行内 Σ 调整。"
            + " 修改后需点弹窗的写入按钮才落回变量。";

        /// <summary>从现有档案造草稿（null = 默认：×1 +0，无单位）</summary>
        public static ArrayConversionDraft From(ElementConversion? conversion)
        {
            if (conversion == null) return new ArrayConversionDraft();

            return new ArrayConversionDraft
            {
                Scale = conversion.Scale,
                Offset = conversion.Offset,
                DecimalPlaces = conversion.DecimalPlaces,
                Unit = conversion.Unit ?? string.Empty,
            };
        }

        /// <summary>
        /// 生成档案：Scale=1/Offset=0/无单位视为"不换算"（返回 null）。
        /// <para>Scale=0 明确失败——写入时无法反算，静默接受只会在写设备时炸。</para>
        /// </summary>
        public bool TryBuild(out ElementConversion? conversion, out string error)
        {
            conversion = null;
            error = string.Empty;

            if (double.IsNaN(Scale) || double.IsInfinity(Scale)
                || double.IsNaN(Offset) || double.IsInfinity(Offset))
            {
                error = "换算参数必须是有限数（不允许 NaN / Infinity）。";
                return false;
            }

            if (Scale == 0)
            {
                error = "缩放系数不能为 0：工程值无法反算成原始值，请填 1 或实际比例。";
                return false;
            }

            int decimals = DecimalPlaces < 0 ? 0 : DecimalPlaces > 10 ? 10 : DecimalPlaces;
            string unit = (Unit ?? string.Empty).Trim();

            if (Scale == 1.0 && Offset == 0 && unit.Length == 0)
                return true; // identity = 不换算

            conversion = new ElementConversion
            {
                Scale = Scale,
                Offset = Offset,
                DecimalPlaces = decimals,
                Unit = unit.Length == 0 ? null : unit,
            };
            return true;
        }
    }
}
