using System;
using System.Text.Json;

namespace Core.Interfaces
{
    /// <summary>
    /// 配置值宽容转换（JSON 反序列化值 → 目标类型）
    /// 统一处理 JsonElement（System.Text.Json 反序列化 object 的产物）、
    /// 枚举字符串/数字转换、系统类型转换和复杂类型（List/对象配置）反序列化
    /// InputPort 端口灌值与 [StepConfig] 配置属性灌值共用
    /// </summary>
    public static class ValueConverter
    {
        public static object Convert(object rawValue, Type targetType)
        {
            if (rawValue == null)
                return null;

            if (Nullable.GetUnderlyingType(targetType) != null)
                targetType = Nullable.GetUnderlyingType(targetType);

            // 流程 JSON 加载后 InputValues 的值是 JsonElement，需先拆包
            if (rawValue is JsonElement el)
            {
                switch (el.ValueKind)
                {
                    case JsonValueKind.String:
                        var str = el.GetString();
                        if (targetType.IsEnum)
                            return Enum.Parse(targetType, str);
                        return System.Convert.ChangeType(str, targetType);

                    case JsonValueKind.Number:
                        if (el.TryGetInt64(out var l))
                            return targetType.IsEnum
                                ? Enum.ToObject(targetType, l)
                                : System.Convert.ChangeType(l, targetType);
                        return System.Convert.ChangeType(el.GetDouble(), targetType);

                    case JsonValueKind.True:
                    case JsonValueKind.False:
                        return el.GetBoolean();

                    case JsonValueKind.Array:
                    case JsonValueKind.Object:
                        // 复杂配置类型（如 List<RoiItem>）：用 JSON 原文反序列化还原
                        return JsonSerializer.Deserialize(el.GetRawText(), targetType);

                    default:
                        return null;
                }
            }

            if (targetType.IsEnum && rawValue is string strValue)
                return Enum.Parse(targetType, strValue);

            return System.Convert.ChangeType(rawValue, targetType);
        }
    }
}
