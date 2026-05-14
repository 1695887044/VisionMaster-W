﻿﻿﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VisionMaster.Models
{
    /// <summary>
    /// 本地变量项
    /// 用于条件步骤中的局部变量定义
    /// </summary>
    public class LocalVariableItem : BindableBase
    {
        /// <summary>
        /// 变量唯一标识
        /// </summary>
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>
        /// 变量名称
        /// </summary>
        public string Name { get => field; set => SetProperty(ref field, value); }

        /// <summary>
        /// 数据类型名称（如 "System.Double"）
        /// </summary>
        public string DataTypeName { get => field; set => SetProperty(ref field, value); } = "System.Double";
    }
}
