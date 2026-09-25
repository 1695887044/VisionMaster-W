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
        Hub,

        /// <summary>
        /// 相机采集：从方案里已配置的相机消费一帧。
        ///
        /// 与 <see cref="Hub"/> 的分工（两者都是"图从外力来"，但语义完全不同）
        /// ---------
        ///   Hub    = 外部程序推一张图 → 立刻触发本流程跑一次（一请求一执行），本步骤只是把那张图取走；
        ///   相机   = 相机持续出图进帧队列，本步骤按自己的节奏消费一帧，每帧只会被取到一次。
        /// 把相机做成 Hub 的一个变体会踩两个坑：一是"每帧只被检测一次"无法保证（Hub 只留最新帧），
        /// 二是丢帧/掉线/在线未触发这些状态全都无法表达——而那正是现场排障最需要的三件事。
        /// </summary>
        [Display(Name = "相机采集")]
        Camera
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
