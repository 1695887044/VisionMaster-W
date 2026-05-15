﻿﻿﻿﻿﻿using System;
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
        /// 创建输入端口 UI 模型
        /// </summary>
        public InputPortUIModel(PortDefinition schema, string linkedAddress)
        {
            Definition = schema;
            LinkedAddress = linkedAddress;
        }
    }
}
