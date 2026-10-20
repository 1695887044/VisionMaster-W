using System.Windows.Media;
using System.Windows.Shapes;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// 源生成器的活体探针：**故意留在仓库里的最小图元**，用来证明 [ScadaProperty] 这条链真的通。
    ///
    /// 它不是给人用的图元（不进 <see cref="BuiltInElements"/> 清单、不出现在工具箱）。
    /// 它存在的唯一理由：生成器是"静默失效"型机制——生成没生效时编译照样过，
    /// 只是属性少了一个、面板上少一行、绑定引擎找不到它。把一条最小声明放进常规构建路径，
    /// 任何一次"生成器坏了/没挂上"都会在这里变成编译错误，而不是等着现场发现。
    ///
    /// 断言侧由 <c>ScadaChecks</c> 的 <c>GeneratorContractChecks</c> 钉住（形状 + 默认值 + 描述符 + 钩子）。
    /// </summary>
    internal partial class GeneratorProbeElement : ScadaElementBase
    {
        /// <summary>颜色型：验证字符串默认值 → Brush，且 Freezable 被冻结</summary>
        [ScadaProperty(Key = "ProbeColor", DisplayName = "探针颜色", Kind = ElementPropertyKind.Color,
                       Group = ScadaPropertyGroups.State, DefaultValue = "#FF2D7DD2", IsBindable = true,
                       Description = "生成器探针：证明 Color 型属性三件套齐备")]
        public partial Brush? ProbeColor { get; set; }

        /// <summary>数值型：验证 Min/Max 与默认值</summary>
        [ScadaProperty(Key = "ProbeSize", DisplayName = "探针尺寸", Kind = ElementPropertyKind.Number,
                       Group = ScadaPropertyGroups.State, DefaultValue = "3", Min = 0, Max = 20)]
        public partial double ProbeSize { get; set; }

        /// <summary>枚举型：验证非内建类型的默认值解析</summary>
        [ScadaProperty(Key = "ProbeShape", DisplayName = "探针形状", Kind = ElementPropertyKind.Choice,
                       Group = ScadaPropertyGroups.State, DefaultValue = "Square",
                       Choices = new[] { "Circle", "Square" })]
        public partial IndicatorShape ProbeShape { get; set; }

        /// <summary>副作用钩子：生成器声明、这里实现（验证钩子在值变化后被调到）</summary>
        public int ColorChangeCount { get; private set; }

        partial void OnProbeColorPropertyChanged() => ColorChangeCount++;

        /// <summary>模板部件挂钩点（验证生成器不干扰既有 OnApplyTemplate 写法）</summary>
        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            SetPartVisible("ProbeCircle", true);
        }
    }
}
