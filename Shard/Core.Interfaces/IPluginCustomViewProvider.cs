using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Core.Interfaces
{
    public interface IPluginCustomViewProvider
    {


        /// <summary>
        /// 获取自定义视图实例
        /// </summary>
        /// <returns>UI 控件实例</returns>
        object GetCustomView();
    }
}
