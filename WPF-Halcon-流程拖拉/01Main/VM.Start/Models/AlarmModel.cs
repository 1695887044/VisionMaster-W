using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VM.Start.Common.Enums;

namespace VM.Start.Models
{
    public class AlarmModel
    {
        /// <summary>
        /// 报警内容
        /// </summary>
        public string Content { get; set; }
        /// <summary>
        /// 报警次数
        /// </summary>
        public int Count { get; set; } = 0;
        /// <summary>
        /// 报警说明(解决报警的办法)
        /// </summary>
        public string Note { get; set; }
        /// <summary>
        /// 报警状态
        /// </summary>
        public eAlarmState AlmState { get; set; } = eAlarmState.NoActive;
        /// <summary>
        /// 报警触发时间
        /// </summary>
        public DateTime TimeRaised { get; set; }
        /// <summary>
        /// 报警清除时间
        /// </summary>
        public DateTime TimeCleared { get; set; }
    }
}
