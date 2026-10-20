using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using VisionMaster.Models;

namespace VisionMaster.Views.DialogViews
{
    /// <summary>
    /// 变量类型缩写：变量名单元格左侧 28×28 图标块里的 3 字母标识。
    /// 只做显示，不参与任何数据/持久化判断；未知类型回退 "VAR"，null 不崩。
    /// </summary>
    public sealed class TypeAbbreviationConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string name = value as string ?? string.Empty;
            if (name.EndsWith("[]", StringComparison.Ordinal)) return "ARR";
            return name switch
            {
                "Int32" => "INT",
                "Double" => "DBL",
                "String" => "STR",
                "Boolean" => "BOOL",
                "HImage" => "IMG",
                _ => string.IsNullOrWhiteSpace(name) ? "VAR" : name.Length <= 3 ? name.ToUpperInvariant() : "VAR",
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// 类型色：类型图标块背景 / 文字 / 色点。
    /// parameter = "Soft"（浅底）/ "Text"（深字）/ "Strong"（色点）。
    /// int/double/bool/string/image 复用既有语义族，数组用本批新增的 DialogTypeArray*。
    /// </summary>
    public sealed class DataTypeBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string name = value as string ?? string.Empty;
            string role = parameter as string ?? "Strong";
            string key = name.EndsWith("[]", StringComparison.Ordinal)
                ? role switch
                {
                    "Soft" => "DialogTypeArraySoftBrush",
                    "Text" => "DialogTypeArrayTextBrush",
                    _ => "DialogTypeArrayBrush",
                }
                : name switch
                {
                    "Int32" => role switch { "Soft" => "DialogAccentLightBrush", "Text" => "DialogAccentTextBrush", _ => "DialogAccentBrush" },
                    "Double" => role switch { "Soft" => "LogicCaseSoftBrush", "Text" => "LogicCaseTextBrush", _ => "LogicCaseBrush" },
                    "Boolean" => role switch { "Soft" => "DialogWarningSubtleBrush", "Text" => "DialogWarningTextBrush", _ => "DialogWarningBrush" },
                    "String" => role switch { "Soft" => "DialogSuccessSubtleBrush", "Text" => "DialogSuccessTextBrush", _ => "DialogSuccessBrush" },
                    "HImage" => role switch { "Soft" => "DialogRowHoverBrush", "Text" => "DialogTextSecondaryBrush", _ => "DialogTextMutedBrush" },
                    _ => role switch { "Soft" => "DialogRowHoverBrush", "Text" => "DialogTextSecondaryBrush", _ => "DialogTextMutedBrush" },
                };

            return Application.Current?.TryFindResource(key) as Brush ?? Brushes.Transparent;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// 当前值单元格六态互斥选择器。
    /// 优先级：系统 > 图像 > 只读存储区 > 本地数组根 > 子行 > 可编辑根。
    /// 输出 Visibility；parameter 是目标态名。
    /// </summary>
    public sealed class GlobalVariableValueModeConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            bool isChild = AsBool(values, 1);
            bool isArrayRoot = AsBool(values, 2);
            bool isEditable = AsBool(values, 3);
            bool isReadOnly = AsBool(values, 4);
            bool isSystemOwned = AsBool(values, 5);
            string typeName = values.Length > 6 ? values[6] as string ?? string.Empty : string.Empty;
            object? originalModel = values.Length > 7 ? values[7] : null;

            bool isImage = string.Equals(typeName, "HImage", StringComparison.Ordinal);
            bool isLocalArrayRoot = isArrayRoot && originalModel is LocalVariableModel;

            string mode = isSystemOwned
                ? "ReadOnlyRoot"
                : isImage
                    ? "Image"
                    : isReadOnly
                        ? "ReadOnlyRoot"
                        : isLocalArrayRoot
                            ? "ArrayRoot"
                            : isChild
                                ? "EditableChild"
                                : isEditable && !isReadOnly
                                    ? "EditableRoot"
                                    : "None";

            return string.Equals(mode, parameter as string, StringComparison.Ordinal)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private static bool AsBool(object[] values, int index)
            => values.Length > index && values[index] is bool b && b;

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// 空态 / 搜索无结果互斥显示：
    /// 输入 (FilteredDisplayNodes.Count, SearchText)，parameter = "Empty" 或 "Search"。
    /// 空来源 + 空搜索 → Empty；空来源 + 有搜索 → Search；有结果 → 两者都 Collapsed。
    /// </summary>
    public sealed class EmptyStateVisibilityConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            int count = values.Length > 0 && values[0] is int i ? i : 0;
            string search = values.Length > 1 ? values[1] as string ?? string.Empty : string.Empty;
            bool isEmpty = count == 0;
            bool hasSearch = !string.IsNullOrWhiteSpace(search);

            bool show = string.Equals(parameter as string, "Search", StringComparison.Ordinal)
                ? isEmpty && hasSearch
                : isEmpty && !hasSearch;

            return show ? Visibility.Visible : Visibility.Collapsed;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>非空字符串 → Visible；null/空白 → Collapsed（变量描述第二行用）。</summary>
    public sealed class StringNotEmptyToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
