using System;
using System.Windows.Media;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// 页面背景图三个属性的“模型字符串 → WPF 类型”换算。
    ///
    /// 模型层（VM.Scada）不引用 WPF，所以 <see cref="ScadaPage.BackgroundStretch"/> 存的是
    /// 成员名；编辑器与运行窗口都要做同一份换算，放在这里只写一遍
    /// （R141：同一视觉结构第二次出现就下沉，避免两处各写一套 switch）。
    /// </summary>
    public static class ScadaBackgroundStretch
    {
        /// <summary>成员名 → <see cref="Stretch"/>；未知值回落到 Uniform（适应、不裁切）</summary>
        public static Stretch Parse(string? value)
            => value switch
            {
                "UniformToFill" => Stretch.UniformToFill,
                "Fill" => Stretch.Fill,
                "Tile" => Stretch.None, // 平铺由 PageBackgroundTile 单独表达，Stretch 不参与
                _ => Stretch.Uniform,
            };

        /// <summary>是否平铺</summary>
        public static bool IsTile(string? value)
            => string.Equals(value, "Tile", StringComparison.OrdinalIgnoreCase);

        /// <summary>模型不透明度（10~100）→ 0.1~1.0</summary>
        public static double ToOpacity(double modelValue)
            => Math.Clamp(double.IsNaN(modelValue) ? 100d : modelValue / 100d, 0.1d, 1d);
    }
}
