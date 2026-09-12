using EventMgrLib;
using VM.Start.Common.Enums;
using VM.Start.Core;
using VM.Start.Models;

namespace VM.Start.Events
{
    public class AddCameraEvent : PubSubEvent<AddCameraEventParamModel>
    {
    }
    public class AddCameraEventParamModel
    {
        public CameraBase Camera { get; set; }
        public eOperateType OperateType { get; set; }
    }
}
