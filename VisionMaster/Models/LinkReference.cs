using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VisionMaster.Models
{
    public class LinkReference
    {
        /// <summary>
        /// 目标步骤的ID
        /// </summary>
        public Guid TargetStepId { get; set; }
        /// <summary>
        /// 目标步骤的输出端口名称
        /// </summary>
        public string TargetPortName { get; set; }
        /// <summary>
        /// 显示给用户的地址，如 "Global.CT" 或 "Delay_0.OutTime"
        /// </summary>
        public string DisplayAddress { get; set; }

        public LinkReference() { }

        public LinkReference(Guid targetId, string targetPort, string displayAddress)
        {
            TargetStepId = targetId;
            TargetPortName = targetPort;
            DisplayAddress = displayAddress;
        }
    }
}
