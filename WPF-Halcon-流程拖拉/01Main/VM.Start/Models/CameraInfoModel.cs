using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VM.Start.Models
{
    [Serializable]
    public class CameraInfoModel
    {
        [NonSerialized]
        /// <summary>扩展信息 </summary>
        public object ExtInfo;
        /// <summary>相机名称 </summary>
        public string CamName { set; get; }
        /// <summary>相机编号 </summary>
        public string SerialNO { set; get; }
        /// <summary>相机IP </summary>
        public string CameraIP { set; get; }
        /// <summary>相机备注 </summary>
        public string MaskName { set; get; }
        /// <summary>相机链接 </summary>
        public bool Connected { set; get; }

    }
}
