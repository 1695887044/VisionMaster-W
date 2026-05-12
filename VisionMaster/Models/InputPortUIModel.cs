using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VisionMaster.Services;

namespace VisionMaster.Models
{
    public class InputPortUIModel : BindableBase
    {
        // 原始的端口定义信息
        public PortSchema Schema { get; }

        // 动态的连线地址 (如 "Global.CT" 或 "Delay_0.OutTime")
        private string _linkedAddress;
        public string LinkedAddress
        {
            get => _linkedAddress;
            set
            {
                if (SetProperty(ref _linkedAddress, value))
                {
                    RaisePropertyChanged(nameof(IsBound)); // 地址改变时，自动通知界面刷新绑定状态
                }
            }
        }

        // 是否已绑定
        public bool IsBound => !string.IsNullOrEmpty(LinkedAddress);

        public InputPortUIModel(PortSchema schema, string linkedAddress)
        {
            Schema = schema;
            LinkedAddress = linkedAddress;
        }
    }
}
