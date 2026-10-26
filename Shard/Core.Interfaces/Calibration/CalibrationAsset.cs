using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.Interfaces
{
    /// <summary>
    /// 方案级标定资产（随 .vms 落盘）。
    ///
    /// 商业机器视觉软件的标定不是"每件重算的一个步骤"，而是一份可命名、可版本化、可被多个下游
    /// 工具引用的工程资产。本类就是那份资产的契约层表示：
    ///   · <see cref="CalibrationId"/> / <see cref="Name"/>：稳定引用键（改名不断链的入口是 Id）；
    ///   · <see cref="Transform"/>：一次求解得到的完整标定结果（含相机序列号/分辨率/时间/残差）；
    ///   · <see cref="IsLocked"/>：锁定后只读，防现场误改；
    ///   · <see cref="Version"/>：每次内容真正变化时递增，供报表/审计回答"这份标定是哪一版"。
    ///
    /// 为什么放在 Core.Interfaces 而不是 Models：Calibration 插件与宿主都要读写它，
    /// 插件只能引用契约程序集；与 <see cref="CalibrationTransform"/> 同一条边界。
    /// 本类只包含可 JSON 往返的基元/数组，不引用 HALCON/WPF，因此可随方案安全落盘。
    /// </summary>
    public sealed class CalibrationAsset
    {
        /// <summary>稳定身份：下游按它引用，改名不打断</summary>
        public Guid CalibrationId { get; set; } = Guid.NewGuid();

        /// <summary>显示名（下游也可按名字兜底引用；重名时以 Id 优先）</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>说明（标定板型号、镜头、工位等，供人核对）</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>锁定后禁止覆盖；加载仍然允许</summary>
        public bool IsLocked { get; set; }

        /// <summary>内容版本：只在标定结果真正变化时 +1</summary>
        public int Version { get; set; } = 1;

        /// <summary>标定结果（一次求解的完整产物）</summary>
        public CalibrationTransform Transform { get; set; } = new();
    }

    /// <summary>
    /// 方案级标定资产目录：运行期插件只认这个契约，不认 SolutionModel/文件系统。
    /// 与 IGlobalVariableWriter / ICameraProvider 同一范式：由 ExecutionContext 递送，
    /// 没有方案工作区时回落到 <see cref="NullCalibrationCatalog"/>（调用即失败并说明原因）。
    /// </summary>
    public interface ICalibrationCatalog
    {
        /// <summary>当前方案的全部标定资产（只读视图）</summary>
        IReadOnlyList<CalibrationAsset> All { get; }

        /// <summary>按稳定 Id 查找</summary>
        bool TryGet(Guid calibrationId, out CalibrationAsset? asset);

        /// <summary>按名字查找（重名时返回第一条；正式引用建议用 Id）</summary>
        bool TryGetByName(string name, out CalibrationAsset? asset);

        /// <summary>
        /// 保存/更新一份标定资产。已存在同 Id 时原地更新；否则追加。
        /// 失败不抛异常，由调用方决定是记警告还是判失败。
        /// </summary>
        bool Save(CalibrationAsset asset, out string error);
    }

    /// <summary>没有方案工作区时的空目录：所有读取失败、写入失败并给出原因。</summary>
    public sealed class NullCalibrationCatalog : ICalibrationCatalog
    {
        public static readonly NullCalibrationCatalog Instance = new();

        private NullCalibrationCatalog() { }

        public IReadOnlyList<CalibrationAsset> All => Array.Empty<CalibrationAsset>();

        public bool TryGet(Guid calibrationId, out CalibrationAsset? asset)
        {
            asset = null;
            return false;
        }

        public bool TryGetByName(string name, out CalibrationAsset? asset)
        {
            asset = null;
            return false;
        }

        public bool Save(CalibrationAsset asset, out string error)
        {
            error = "当前执行环境没有方案工作区，无法保存标定资产";
            return false;
        }
    }
}
