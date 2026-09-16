using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VM.Start.Common.Helper;

namespace VM.Start.Core
{
    public interface ILaserDevice
    {
        object LaserControl { get; }

        string LaserName { get; set; }

        /// <summary>
        /// 激光图档路径
        /// </summary>
        string LaserFilePath { get; set; }

        /// <summary>
        /// 激光器索引
        /// </summary>
        int LaserIndex { set; get; }

        /// <summary>
        /// 是否初始化完成
        /// </summary>
        bool IsInit { set; get; }

        /// <summary>
        /// 激光完成事件
        /// </summary>
        Action<int> MarkEnd { set; get; }

        /// <summary>
        /// 是否在激光中
        /// </summary>
        bool IsMarking { get; }

        /// <summary>
        /// 初始化MarkingMate
        /// </summary>
        bool Initial();

        /// <summary>
        /// 释放MarkingMate资源
        /// </summary>
        bool Finish();

        /// <summary>
        /// 加载图档
        /// </summary>
        /// <param name="filePath">图档路径</param>
        bool LoadFile(string filePath);

        /// <summary>
        /// 运行MarkingMate进行编辑
        /// </summary>
        bool GoEdit(string filePath);

        /// <summary>
        /// 对图档进行锁定，降低交互时间，自动生产时需要对图档进行锁定
        /// </summary>
        /// <returns></returns>
        bool MarkDataLock();

        /// <summary>
        /// 解锁图档，在对图档进行修改时需要先进行解锁，非自动生产时可以进行解锁
        /// </summary>
        /// <returns></returns>
        bool MarkDataUnLock();

        /// <summary>
        /// 开始激光工艺(预载雕刻)，该函数必须配合MarkData_Lock，MarkData_UnLock配合使用，缺一不可
        /// </summary>
        /// <param name="offsetX">x方向偏移</param>
        /// <param name="offsetY">y方向偏移</param>
        /// <param name="angle">旋转角度</param>
        /// <param name="iMode">雕刻模式，1：阻断式雕刻， 4：非阻断式雕刻</param>
        /// <returns></returns>
        bool StartMarkExt(double offsetX = 0, double offsetY = 0, double angle = 0, int iMode = 4);

        /// <summary>
        /// 图档旋转偏移
        /// </summary>
        /// <param name="offsetX">x方向偏移</param>
        /// <param name="offsetY">y方向偏移</param>
        /// <param name="angle">旋转角度</param>
        /// <returns></returns>
        bool MarkDataRotate(double offsetX, double offsetY, double angle);
    }
}
