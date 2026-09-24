using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Plugin.ImageAcquisition
{
    #region 枚举

    /// <summary>
    /// 采集模式
    /// </summary>
    public enum AcquisitionMode
    {
        /// <summary>指定单张图像文件</summary>
        [Display(Name = "指定图像")]
        SingleFile,

        /// <summary>文件夹批量采集（按索引读取）</summary>
        [Display(Name = "文件目录")]
        Folder,

        /// <summary>网络推送：图像由外部（HTTP 收图服务）推入流程槽，本步骤从槽里取一帧</summary>
        [Display(Name = "网络推送")]
        Hub
    }

    /// <summary>
    /// 状态消息级别：驱动配置界面信息栏的颜色（绿 / 橙 / 红）。
    /// 只描述"这条消息有多严重"，不参与任何流程逻辑。
    /// </summary>
    public enum StatusLevel
    {
        /// <summary>正常信息（绿）：操作成功、路径有效等</summary>
        Info,

        /// <summary>警告（橙）：还能继续，但需要用户注意（如尚未选择路径）</summary>
        Warning,

        /// <summary>错误（红）：当前配置不可用（如路径不存在、读图失败）</summary>
        Error
    }

    #endregion
}
