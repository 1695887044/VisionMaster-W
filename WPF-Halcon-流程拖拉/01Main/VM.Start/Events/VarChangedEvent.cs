using EventMgrLib;
using VM.Start.Models;

namespace VM.Start.Events
{
    public class VarChangedEvent : PubSubEvent<VarChangedEventParamModel> { }

    public class VarChangedEventParamModel
    {
        /// <summary>
        /// 定义插件发送名
        /// </summary>
        public string SendName { get; set; }
        public string LinkName { get; set; }
        public string Name { get; set; }
        public string DataType { get; set; }
        public bool IsAdd { get; set; }

        public string Note { get; set; }
    }
}
