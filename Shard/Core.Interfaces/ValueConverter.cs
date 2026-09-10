using System;
using Newtonsoft.Json.Linq;

namespace Core.Interfaces
{
    /// <summary>
    /// 配置值宽容转换（JSON 反序列化值 → 目标类型）
    /// 统一处理 JToken（Newtonsoft.Json 反序列化 object 的产物）、
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

            // 流程 JSON 加载后 InputValues 的值是 JToken（Newtonsoft object 槽位产物），需先拆包
            if (rawValue is JToken token)
            {
                if (token.Type == JTokenType.Null)
                    return null;

                // 枚举优先：JToken 直转不认枚举，按名字串/数字手动解析
                if (targetType.IsEnum)
                    return System.Enum.Parse(targetType, token.ToString());

                switch (token.Type)
                {
                    case JTokenType.Boolean:
                    case JTokenType.Integer:
                    case JTokenType.Float:
                    case JTokenType.String:
                    case JTokenType.Date:
                        // 标量：JToken.ToObject 直转 CLR（int/double/string/bool 等）
                        return token.ToObject(targetType);

                    case JTokenType.Array:
                    case JTokenType.Object:
                        // 复杂配置类型（如 List<RoiItem>）：按 JSON 原文反序列化还原
                        return token.ToObject(targetType);
                }

                return token.ToString();
            }

            if (targetType.IsEnum && rawValue is string strValue)
                return System.Enum.Parse(targetType, strValue);

            return System.Convert.ChangeType(rawValue, targetType);
        }
    }
}
