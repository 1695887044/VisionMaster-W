using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Core.Interfaces;

namespace VisionMaster.Helpers
{
    /// <summary>
    /// Case 匹配值的统一转换与校验口径 —— 编译器（Engine/FlowCompiler）与条件编辑器
    /// （VisionMaster/DialogViewModels/ConditionEditorViewModel）**共用同一个实现**。
    ///
    /// 为什么不各写一份：编辑器放行的值、编译器又拦下来（或反过来），用户会看到
    /// "弹窗里保存成功、一编译就报错"的自相矛盾行为（与 TypeHelper.CanBindTo 的单一判据同一理由）。
    ///
    /// 支持范围与条件变量白名单对齐（TypeHelper.IsSafeExpressionType）：字符串 + 值类型；
    /// object（类型未知）与引用类型不支持匹配——比较结果不可预期，宁可编译期说清楚。
    /// </summary>
    public static class CaseValueHelper
    {
        /// <summary>
        /// 判据类型是否支持"等于某值"的匹配。
        /// </summary>
        public static bool IsSupportedJudgeType(Type judgeType)
        {
            if (judgeType == null || judgeType == typeof(object))
                return false;

            return TypeHelper.IsSafeExpressionType(judgeType);
        }

        /// <summary>
        /// 把匹配值文本按判据声明类型归一。
        /// 失败时 error 是面向操作人员的一句话（编辑器/编译器直接展示，不再翻译）。
        /// </summary>
        public static bool TryConvert(string rawText, Type judgeType, out object value, out string error)
        {
            value = null;
            error = null;

            string text = (rawText ?? string.Empty).Trim();
            if (text.Length == 0)
            {
                error = "匹配值不能为空";
                return false;
            }

            if (!IsSupportedJudgeType(judgeType))
            {
                error = $"判据类型 [{judgeType?.Name ?? "未知"}] 不支持匹配（仅支持数值 / 布尔 / 字符串等值类型）";
                return false;
            }

            try
            {
                // 与端口灌值同一条转换链路（枚举名字串、数值族互转都在里面），
                // 保证"能填的值"与"能连的线"是同一套转换能力
                value = ValueConverter.Convert(text, judgeType);
                return true;
            }
            catch (Exception ex)
            {
                error = $"匹配值 '{text}' 无法转换为 {judgeType.Name}：{ex.Message}";
                return false;
            }
        }
    }
}
