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

        /// <summary>装一个类型键进拖放负载</summary>
        public static DataObject CreatePayload(string typeKey)
        {
            if (string.IsNullOrWhiteSpace(typeKey))
                throw new ArgumentException("拖放必须带上图元类型键", nameof(typeKey));

            var data = new DataObject();
            data.SetData(ElementTypeKeyFormat, typeKey);

            return data;
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
