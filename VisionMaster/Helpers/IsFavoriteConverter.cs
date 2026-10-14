using System;
using System.Globalization;
using System.Windows.Data;
using VisionMaster.Models;
using VisionMaster.ViewModels;

namespace VisionMaster.Helpers
{
    /// <summary>
    /// (条目, 工具箱 VM, FavoritesVersion) → 该条目是否已收藏。
    ///
    /// 为什么要三路 MultiBinding、而不给 <c>ToolItemModel</c> 加个 <c>IsFavorite</c> 属性：
    /// 条目绑定的必须是**原始 ToolItemModel 实例**（拖拽负载的硬约束，见 ToolboxGroupViewModel 注释），
    /// 所以不能为了星标态包一层条目 VM；而 ToolItemModel 是 Core 里的 record、不带通知，
    /// 加个可变属性也不会让界面刷新。折中是"收藏态留在 VM 里 + 用一个会变的版本号当刷新信号"：
    /// 第三路 <c>FavoritesVersion</c> 一变，MultiBinding 重算，星标跟着变。
    /// 它也是**唯一**的刷新驱动——只绑前两路的话，切换收藏后星标不会重绘。
    /// </summary>
    public sealed class IsFavoriteConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            // values[2]（版本号）不参与计算，只用来触发重算——这里刻意不读它
            if (values is { Length: >= 2 } && values[0] is ToolItemModel item && values[1] is ToolViewModel vm)
                return vm.IsFavorite(item);

            return false;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
