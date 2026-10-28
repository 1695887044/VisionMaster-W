using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Plugin.DeformableMatching
{
    /// <summary>
    /// 可形变匹配算法：局部非刚性 / 平面透视（未标定单应）。
    /// 值写进方案时仍是成员名；界面显示名走 [Display(Name)]（R40/R84/R85）。
    /// </summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum DeformableAlgorithm
    {
        [Display(Name = "局部非刚性")]
        LocalDeformable,

        [Display(Name = "平面透视")]
        PlanarUncalibDeformable,
    }

    /// <summary>算法卡片的视图模型：值仍是枚举成员名，显示名走 ForEnum。</summary>
    public sealed class AlgorithmOption
    {
        public string Value { get; init; } = string.Empty;
        public string Display { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
    }
}
