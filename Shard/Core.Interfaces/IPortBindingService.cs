using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Core.Interfaces
{
    public interface IPortBindingService
    {
        /// <summary>
        /// 呼出主界面的绑定弹窗，让用户选择上游变量
        /// </summary>
        /// <param name="targetInput">当前需要被绑定的输入端口</param>
        /// <returns>用户最终选中的上游输出端口（如果用户点击了取消，则返回 null）</returns>
        IOutputPort RequestBinding(IInputPort targetInput);
    }
}
