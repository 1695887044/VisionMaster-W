using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UI.Attributes
{
    public abstract class ValidationBaseAttribute : Attribute
    {
        public string ErrorMessage { get; set; } = "输入值无效";

        // 核心逻辑：子类实现此方法进行校验
        public abstract bool IsValid(object? value);
    }
}
