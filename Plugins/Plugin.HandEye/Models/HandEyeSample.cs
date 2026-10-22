using Core.Interfaces;
using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations;

namespace Plugin.HandEye.Models
{
    /// <summary>
    /// 一组成对数据：机器人侧位姿 + 相机侧位姿，各 6 个分量（X/Y/Z mm，Rx/Ry/Rz 度，ZYX 欧拉）。
    ///
    /// 为什么是"列表里的一行"而不是"CSV 文件里的一行"
    /// ---------
    /// CSV 只是**传输格式**，不是数据的家。数据必须住在步骤配置里，理由三条：
    ///   · 方案（.vms）要能独立拷走 —— 路径指向的 csv 一换机器就断；
    ///   · 现场最常见的操作是"改一个错数字 / 删一行离群 / 补一组"，这些不该要求用户去 Excel 里改完再回来；
    ///   · 采集时逐组"加一行"本身就是台账，表格是采集现场的第一现场。
    /// 所以：表格是主，CSV 是导入/导出的扩展功能（见 <see cref="HandEyeCsv"/>）。
    ///
    /// 纯数据类（无算法）：一行 → <see cref="HandEyeMath.Sample"/> 的换算是全仓唯一入口，
    /// CSV 导入与直接求解共用它，避免两条路径对欧拉约定理解不一致。
    /// </summary>
    public class HandEyeSample : ObservableObject
    {
        private double _robotX;
        private double _robotY;
        private double _robotZ;
        private double _robotRx;
        private double _robotRy;
        private double _robotRz;

        private double _cameraX;
        private double _cameraY;
        private double _cameraZ;
        private double _cameraRx;
        private double _cameraRy;
        private double _cameraRz;

        /// <summary>机器人侧 X（mm）</summary>
        [Display(Name = "机器人 X")]
        public double RobotX { get => _robotX; set => SetProperty(ref _robotX, value); }

        /// <summary>机器人侧 Y（mm）</summary>
        [Display(Name = "机器人 Y")]
        public double RobotY { get => _robotY; set => SetProperty(ref _robotY, value); }

        /// <summary>机器人侧 Z（mm）</summary>
        [Display(Name = "机器人 Z")]
        public double RobotZ { get => _robotZ; set => SetProperty(ref _robotZ, value); }

        /// <summary>机器人侧 Rx（度，ZYX 欧拉的 X 分量）</summary>
        [Display(Name = "机器人 Rx")]
        public double RobotRx { get => _robotRx; set => SetProperty(ref _robotRx, value); }

        /// <summary>机器人侧 Ry（度）</summary>
        [Display(Name = "机器人 Ry")]
        public double RobotRy { get => _robotRy; set => SetProperty(ref _robotRy, value); }

        /// <summary>机器人侧 Rz（度）</summary>
        [Display(Name = "机器人 Rz")]
        public double RobotRz { get => _robotRz; set => SetProperty(ref _robotRz, value); }

        /// <summary>相机侧 X（mm）</summary>
        [Display(Name = "相机 X")]
        public double CameraX { get => _cameraX; set => SetProperty(ref _cameraX, value); }

        /// <summary>相机侧 Y（mm）</summary>
        [Display(Name = "相机 Y")]
        public double CameraY { get => _cameraY; set => SetProperty(ref _cameraY, value); }

        /// <summary>相机侧 Z（mm）</summary>
        [Display(Name = "相机 Z")]
        public double CameraZ { get => _cameraZ; set => SetProperty(ref _cameraZ, value); }

        /// <summary>相机侧 Rx（度）</summary>
        [Display(Name = "相机 Rx")]
        public double CameraRx { get => _cameraRx; set => SetProperty(ref _cameraRx, value); }

        /// <summary>相机侧 Ry（度）</summary>
        [Display(Name = "相机 Ry")]
        public double CameraRy { get => _cameraRy; set => SetProperty(ref _cameraRy, value); }

        /// <summary>相机侧 Rz（度）</summary>
        [Display(Name = "相机 Rz")]
        public double CameraRz { get => _cameraRz; set => SetProperty(ref _cameraRz, value); }

        /// <summary>
        /// 这一行是不是"加了但还没填"。
        /// 判据：12 个值全为 0。真实的测量行不可能 12 个分量全为 0
        /// （机器人六轴全在零位、相机侧也恰好全零，概率上不存在）。
        /// 用来把"忘了填"与"数据退化"分开报错 —— 前者是操作问题，后者是数据问题。
        /// </summary>
        [JsonIgnore]
        public bool IsBlank
            => _robotX == 0 && _robotY == 0 && _robotZ == 0
            && _robotRx == 0 && _robotRy == 0 && _robotRz == 0
            && _cameraX == 0 && _cameraY == 0 && _cameraZ == 0
            && _cameraRx == 0 && _cameraRy == 0 && _cameraRz == 0;

        /// <summary>转成数学层样本（全仓唯一换算入口：CSV 导入与直接求解共用）</summary>
        public HandEyeMath.Sample ToMathSample() => new(
            HandEyeMath.PoseFromXyzEulerZyxDeg(_robotX, _robotY, _robotZ, _robotRx, _robotRy, _robotRz),
            HandEyeMath.PoseFromXyzEulerZyxDeg(_cameraX, _cameraY, _cameraZ, _cameraRx, _cameraRy, _cameraRz));

        /// <summary>按 CSV 列序取 12 个值（导出用）</summary>
        public double[] ToRow() => new[]
        {
            _robotX, _robotY, _robotZ, _robotRx, _robotRy, _robotRz,
            _cameraX, _cameraY, _cameraZ, _cameraRx, _cameraRy, _cameraRz
        };

        /// <summary>按 CSV 列序写 12 个值（导入用）</summary>
        public void SetRow(double[] v)
        {
            _robotX = v[0]; _robotY = v[1]; _robotZ = v[2];
            _robotRx = v[3]; _robotRy = v[4]; _robotRz = v[5];
            _cameraX = v[6]; _cameraY = v[7]; _cameraZ = v[8];
            _cameraRx = v[9]; _cameraRy = v[10]; _cameraRz = v[11];
            // 整行替换：逐个 OnPropertyChanged，避免 12 次 setter 触发 12 次重算
            OnPropertyChanged(nameof(RobotX));
            OnPropertyChanged(nameof(RobotY));
            OnPropertyChanged(nameof(RobotZ));
            OnPropertyChanged(nameof(RobotRx));
            OnPropertyChanged(nameof(RobotRy));
            OnPropertyChanged(nameof(RobotRz));
            OnPropertyChanged(nameof(CameraX));
            OnPropertyChanged(nameof(CameraY));
            OnPropertyChanged(nameof(CameraZ));
            OnPropertyChanged(nameof(CameraRx));
            OnPropertyChanged(nameof(CameraRy));
            OnPropertyChanged(nameof(CameraRz));
            OnPropertyChanged(nameof(IsBlank));
        }
    }
}
