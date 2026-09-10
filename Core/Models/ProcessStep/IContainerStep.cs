using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VisionMaster.Models
{
    /// <summary>
    /// 容器步骤接口
    /// 定义了包含子步骤集合的步骤类型
    /// </summary>
    public interface IContainerStep
    {
        /// <summary>
        /// 子步骤集合
        /// </summary>
        ObservableCollection<StepCollection> Children { get; }
    }
}
