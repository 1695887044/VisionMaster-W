using Core.Interfaces.Core;
using Newtonsoft.Json;

namespace Plugin.Calibration.Models
{
    /// <summary>
    /// 标定表的一行：一组 (机械坐标 ↔ 图像点)。
    ///
    /// 为什么是 ObservableObject
    /// ---------
    /// 表格里每一格都可编辑；在画布上拖动标记时也要实时回写到对应行并刷新残差——
    /// 没有变更通知，界面就只会显示"拖之前"的值。
    ///
    /// "填空"约定（**null = 没填；0 = 合法坐标**）
    /// ---------
    /// · 四个坐标全为 null → **空行**（不参与求解）；
    /// · 四个坐标全非 null → **有效点**（0 是合法值：机械回零位、工件原点、图像边缘都可能取到 0）；
    /// · 其余 → **半填**：运行时**失败**并指明行号——静默忽略会把"漏填一格"变成"标定悄悄算歪"。
    ///
    /// 为什么不再拿 0 当"没填"（2026-10-04 修正）
    /// ---------
    /// 旧实现用 0 当哨兵值，于是"机械 X/Y 恰为 0"的**合法**标定点会被误判成半填（运行直接失败，
    /// 用户四格都填了却被告知漏填），或与图像 0 一起被当空行**静默剔除**——现场回零位取点必踩。
    /// 现在"填没填"由 null 表达，与"值是多少"彻底分离。
    ///
    /// 旧数据迁移：<see cref="NormalizeLegacyAllZero"/> 把"四值全 0"的行还原成 null。
    /// 旧实现本来就把这种行当空行（不参与求解），所以迁移**不丢任何真实标定点**。
    /// 注意"半填且含 0"的旧行（如机械 X=0、图像 Row=0）迁移后会被当成有效点——
    /// 这类点在旧版是报错行，新版若位置不对会被残差闸门拦下（不会静默放行）。
    /// </summary>
    public class CalibPointRow : ObservableObject
    {
        private string _name = "P";
        /// <summary>行名（同时用作画布标记名：九点模式 P1…PN；像素当量模式 A / B）</summary>
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        private double? _machineX;
        /// <summary>机械坐标 X（mm）；null = 未填</summary>
        public double? MachineX
        {
            get => _machineX;
            set { if (SetProperty(ref _machineX, value)) NotifyState(); }
        }

        private double? _machineY;
        /// <summary>机械坐标 Y（mm）；null = 未填</summary>
        public double? MachineY
        {
            get => _machineY;
            set { if (SetProperty(ref _machineY, value)) NotifyState(); }
        }

        private double? _imageRow;
        /// <summary>图像点 Row（像素，向下）；null = 未填</summary>
        public double? ImageRow
        {
            get => _imageRow;
            set { if (SetProperty(ref _imageRow, value)) NotifyState(); }
        }

        private double? _imageCol;
        /// <summary>图像点 Col（像素，向右）；null = 未填</summary>
        public double? ImageCol
        {
            get => _imageCol;
            set { if (SetProperty(ref _imageCol, value)) NotifyState(); }
        }

        private double _residualPx;
        /// <summary>该点残差（像素）：求解后由插件写入；未参与求解时为 0</summary>
        public double ResidualPx
        {
            get => _residualPx;
            set => SetProperty(ref _residualPx, value);
        }

        /// <summary>空行（四格都没填）：不参与求解</summary>
        [JsonIgnore]
        public bool IsEmpty => MachineX is null && MachineY is null && ImageRow is null && ImageCol is null;

        /// <summary>有效点（四格都填了；**0 也是合法值**）</summary>
        [JsonIgnore]
        public bool IsFilled => MachineX is not null && MachineY is not null && ImageRow is not null && ImageCol is not null;

        /// <summary>半填（填了但不齐）：运行时必须报失败并指出这一行</summary>
        [JsonIgnore]
        public bool IsPartial => !IsEmpty && !IsFilled;

        /// <summary>行状态文字（表格里点名的提示）</summary>
        [JsonIgnore]
        public string StateText => IsEmpty ? "空" : IsFilled ? "有效" : "半填";

        /// <summary>
        /// 旧方案迁移：把"四值全 0"的行还原成空行（null）。
        /// 旧实现把"四值全 0"当空行，故这种行在旧数据里本就不参与求解——迁移不丢真实点，
        /// 只是让 0 从此可以当合法坐标用。已经是 null 的行是空操作。
        /// </summary>
        /// <returns>是否发生了迁移（调用方据此决定要不要提示"已按旧格式还原空行"）</returns>
        public bool NormalizeLegacyAllZero()
        {
            // 注意：null != 0 为 true（提升比较），所以"已经是 null 的行"在这里直接返回 false
            if (MachineX != 0 || MachineY != 0 || ImageRow != 0 || ImageCol != 0)
                return false;

            MachineX = MachineY = ImageRow = ImageCol = null;
            return true;
        }

        private void NotifyState()
        {
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(IsFilled));
            OnPropertyChanged(nameof(IsPartial));
            OnPropertyChanged(nameof(StateText));
        }
    }
}
