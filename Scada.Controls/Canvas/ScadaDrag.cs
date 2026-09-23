using System;
using System.Windows;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// 画布与外部面板之间的拖放协议：拖的就是"要放哪一类图元"这一个字符串。
    ///
    /// 为什么传类型键而不是 <see cref="ScadaElement"/> 实例：
    /// ① 拖拽是"意图"，不是"结果"——DragOver 每帧都可能被询问，此时还不该产生模型对象，
    ///    否则用户拖一半取消，内存里就多了一个没人要、还带着 Guid 的图元；
    /// ② 实例方案会让 <see cref="IDataObject"/> 里装着可写模型，任何一段处理代码都能顺手改它，
    ///    而字符串是不可变的，落点校验与"造模型"只能发生在 Drop 那一处。
    ///
    /// 格式名刻意带上产品前缀：拖放数据在同一个桌面会话里跨进程可见（比如从资源管理器
    /// 拖文件进来），用一个足够特殊的键，才不会跟别人家的 "Text"/"FileDrop" 撞车。
    /// </summary>
    public static class ScadaDrag
    {
        /// <summary>拖放负载的格式名（值是图元的 TypeKey）</summary>
        public const string ElementTypeKeyFormat = "VisionMaster.Scada.ElementTypeKey";

        /// <summary>
        /// 拖放负载的格式名（值是模板的 TemplateId）。
        ///
        /// 与类型键<b>分成两种格式</b>而不是"一种格式 + 一个区分字段"：
        /// 两者的语义根本不同——类型键说的是"放下要造哪一类图元"（造一个空壳），
        /// 模板 Id 说的是"放下要把库里的哪一份内容搬出来"（带属性、绑定、事件的一整块）。
        /// 混在一种格式里，画布就得先解包再分派；分成两种，画布各自 <c>GetDataPresent</c>
        /// 一问就知道该走哪条路，也顺带把"外部拖进来一个同名字符串"的误判挡在门外。
        /// </summary>
        public const string TemplateIdFormat = "VisionMaster.Scada.TemplateId";

        /// <summary>装一个类型键进拖放负载</summary>
        public static DataObject CreatePayload(string typeKey)
        {
            if (string.IsNullOrWhiteSpace(typeKey))
                throw new ArgumentException("拖放必须带上图元类型键", nameof(typeKey));

            var data = new DataObject();
            data.SetData(ElementTypeKeyFormat, typeKey);

            return data;
        }

        /// <summary>装一个模板 Id 进拖放负载</summary>
        public static DataObject CreateTemplatePayload(Guid templateId)
        {
            if (templateId == Guid.Empty)
                throw new ArgumentException("拖放必须带上模板 Id", nameof(templateId));

            var data = new DataObject();

            // 存字符串而不是 Guid：拖放数据在同一个桌面会话里跨进程可见，
            // 字符串是两边都认得、也看得懂的形态（调试时把负载打印出来就能直接读）。
            data.SetData(TemplateIdFormat, templateId.ToString("D"));

            return data;
        }

        /// <summary>从拖放负载里取模板 Id；不是模板负载则返回 false</summary>
        public static bool TryGetTemplateId(IDataObject? data, out Guid templateId)
        {
            templateId = Guid.Empty;

            if (data is null || !data.GetDataPresent(TemplateIdFormat))
                return false;

            return data.GetData(TemplateIdFormat) is string text
                   && Guid.TryParse(text, out templateId)
                   && templateId != Guid.Empty;
        }

        /// <summary>
        /// 从拖放负载里取类型键；不是本画布的负载则返回 false。
        ///
        /// 只认自己那一种格式，故意不做"实在不行就当纯文本读"的兜底：
        /// 兜底会让用户从记事本拖一段文字也落在画布上生出一个图元，那时先怀疑的一定是画布。
        /// </summary>
        public static bool TryGetTypeKey(IDataObject? data, out string typeKey)
        {
            typeKey = string.Empty;

            if (data is null || !data.GetDataPresent(ElementTypeKeyFormat))
                return false;

            if (data.GetData(ElementTypeKeyFormat) is not string key || string.IsNullOrWhiteSpace(key))
                return false;

            typeKey = key;
            return true;
        }
    }
}
