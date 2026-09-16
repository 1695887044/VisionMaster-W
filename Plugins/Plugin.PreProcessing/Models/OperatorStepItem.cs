using System.Collections.Generic;

namespace Plugin.PreProcessing.Models
{
    /// <summary>
    /// 算子链中"一步"的存盘快照（DTO）。
    ///
    /// 为什么要有这么一层弱类型快照，而不是直接把算子对象序列化进方案文件？
    /// 1. 算子类里带 HImage、事件、反射缓存，JSON 一序列化就炸或者存出一堆垃圾；
    /// 2. 写 CLR 类型名（Newtonsoft 的 TypeNameHandling）等于把类名和命名空间焊死在客户现场，
    ///    以后改名/挪程序集，老方案就打不开了 —— 基类注释里那条硬约定就是冲这个来的；
    /// 3. 参数用"属性名 → 字符串"字典存放，读的时候由算子自己解析：
    ///    某个字段解析失败只丢这一个参数（退回默认值），不会连累整份方案。
    ///
    /// 所以加载流程是：先按 <see cref="OperatorKey"/> 造出算子（拿到的是默认参数），
    /// 再用 <see cref="Params"/> 覆盖；<see cref="Enabled"/> 单独存，因为它不属于算子参数。
    /// </summary>
    public sealed class OperatorStepItem
    {
        /// <summary>算子的存盘标识，来自 [PreprocessOperator(Key)]，与类名无关</summary>
        public string OperatorKey { get; set; } = string.Empty;

        /// <summary>该步是否启用（关掉 = 图像原样透传）</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>算子参数快照：属性名 → 字符串值</summary>
        public Dictionary<string, string> Params { get; set; } = new();
    }
}
