using System;
using System.Globalization;

namespace VisionMaster.Helpers
{
    /// <summary>
    /// 把**文本**值按目标声明类型解析一次。
    ///
    /// 【为什么需要它】配置界面只能产出文本：手填常量永远是 <see cref="string"/>。
    /// 若写入侧按声明类型硬守门，"给 Int32 变量填 21"这类最普通的用法会永远失败
    /// （真机 2026-10-10 反馈：「全局变量 OKCount 的类型是 Int32，无法写入 String」）。
    /// 所以口径是**宽进严出**：文本能转就转，转不了明确报错并把被拒的原值带进消息。
    ///
    /// 【两条硬口径】
    ///   · **不变区域性**：换机器/换区域设置时同一个方案必须解析出同一个数；
    ///   · **数值不许带千分位分隔符**：默认 Convert 会把 <c>2,5</c> 读成 25 ——
    ///     用户想写 2.5 而系统静默写成 25，属于"应当报错却改了值"的一类。
    ///
    /// 【只放宽文本】类型化的值（double 塞进 int 变量、HImage 塞进 string）不走这里，
    /// 由调用方按原样报错 —— 那多半是上游接线错了，静默截断会把错因推迟到很远的地方。
    ///
    /// 同一份实现同时服务「变量赋值」的全局变量写入口与「立刻写」的通讯变量写入口，
    /// 避免两处各写一套解析规则（本仓已经因为"两套口径"踩过多次）。
    /// </summary>
    public static class ValueTextCoercion
    {
        /// <summary>
        /// 尝试把文本按 <paramref name="declared"/> 解析。
        /// 成功返回 true，<paramref name="coerced"/> 是解析后的值；
        /// 失败返回 false（调用方负责给出带原值的错误消息）。
        /// </summary>
        public static bool TryCoerce(object? value, Type declared, out object? coerced)
        {
            coerced = value;

            if (value is not string raw)
                return false;

            var text = raw.Trim();
            var target = Nullable.GetUnderlyingType(declared) ?? declared;

            if (text.Length == 0)
                return false;

            if (TypeHelper.IsNumericType(target) && text.IndexOf(',') >= 0)
                return false;

            try
            {
                coerced = Convert.ChangeType(text, target, CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                // 解析不了就交给调用方报错（错误信息里带上原值，用户才知道是哪个值被拒了）
                return false;
            }
        }
    }
}
