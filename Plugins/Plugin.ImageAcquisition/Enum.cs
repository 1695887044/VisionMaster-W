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

        /// <summary>相机实时采集（预留接口）</summary>
        [Display(Name = "相机采集")]
        Camera
    }

    #endregion
}
