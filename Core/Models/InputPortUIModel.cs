﻿﻿﻿﻿﻿﻿﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VisionMaster.Services;

namespace VisionMaster.Models
{
    /// <summary>
    /// 输入端口 UI 模型
    /// 用于在界面上显示和编辑端口绑定
    /// </summary>
    public class InputPortUIModel : BindableBase
    {
        /// <summary>
        /// 原始的端口定义信息
        /// </summary>
        public PortDefinition Definition { get; }

        /// <summary>
        /// 动态的连线地址
        /// 如 "Global.CT" 或 "Delay_0.OutTime"
        /// </summary>
        private string _linkedAddress;
        public string LinkedAddress
        {
            get => _linkedAddress;
            set
            {
                if (SetProperty(ref _linkedAddress, value))
                {
                    RaisePropertyChanged(nameof(IsBound)); // 地址改变时通知绑定状态变化
                }
            }
        }

        /// <summary>
        /// 是否已绑定
        /// </summary>
        public bool IsBound => !string.IsNullOrEmpty(LinkedAddress);

        /// <summary>
        /// 本端口当前绑定的**变量身份**（仅变量来源的连线有；步骤端口/常量/运行时变量为
        /// <see cref="Guid.Empty"/>）。
        ///
        /// 「已绑定」标记必须按它判定：显示串只含「节点名.变量名」，两台设备的同名点位会拼出
        /// 同一个地址——按字符串比较会让两行同时亮起，用户看不出自己绑的是哪一台。
        /// </summary>
        public Guid BoundVariableId { get; }

        /// <summary>
        /// 创建输入端口 UI 模型
        /// </summary>
        /// <param name="schema">端口定义</param>
        /// <param name="linkedAddress">已绑定连线的显示地址（空 = 未绑定）</param>
        /// <param name="boundVariableId">已绑定连线的目标变量身份（非变量来源传 Guid.Empty）</param>
        public InputPortUIModel(PortDefinition schema, string linkedAddress, Guid boundVariableId = default)
        {
            Definition = schema;
            BoundVariableId = boundVariableId;
            LinkedAddress = linkedAddress;
        }
    }
}
