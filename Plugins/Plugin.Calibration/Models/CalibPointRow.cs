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
    /// "填空"约定（写死，避免半填行被当成有效点静默参与求解）
    /// ---------
    /// · 四值全为 0 → **空行**（不参与求解）；
    /// · 四值全为非 0 → **有效点**；
    /// · 其余 → **半填**：运行时**失败**并指明行号——静默忽略会把"漏填一格"变成"标定悄悄算歪"。
    /// （实践里机械 X/Y 与图像 Row/Col 都不会是 0，所以"0 = 没填"是可预期的约定。）
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

        private double _machineX;
        /// <summary>机械坐标 X（mm）</summary>
        public double MachineX
        {
            get => _machineX;
            set { if (SetProperty(ref _machineX, value)) NotifyState(); }
        }

        private double _machineY;
        /// <summary>机械坐标 Y（mm）</summary>
        public double MachineY
        {
            get => _machineY;
            set { if (SetProperty(ref _machineY, value)) NotifyState(); }
        }

        private double _imageRow;
        /// <summary>图像点 Row（像素，向下）</summary>
        public double ImageRow
        {
            get => _imageRow;
            set { if (SetProperty(ref _imageRow, value)) { NotifyState(); OnPropertyChanged(nameof(ImageCol)); } }
        }

        private double _imageCol;
        /// <summary>图像点 Col（像素，向右）</summary>
        public double ImageCol
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

        /// <summary>空行（四值全 0）：不参与求解</summary>
        [JsonIgnore]
        public bool IsEmpty => MachineX == 0 && MachineY == 0 && ImageRow == 0 && ImageCol == 0;

        /// <summary>有效点（四值全非 0）</summary>
        [JsonIgnore]
        public bool IsFilled => MachineX != 0 && MachineY != 0 && ImageRow != 0 && ImageCol != 0;

        /// <summary>半填（非空且非有效）：运行时必须报失败并指出这一行</summary>
        [JsonIgnore]
        public bool IsPartial => !IsEmpty && !IsFilled;

        /// <summary>行状态文字（表格最后一列旁边的提示）</summary>
        [JsonIgnore]
        public string StateText => IsEmpty ? "空" : IsFilled ? "有效" : "半填";

        private void NotifyState()
        {
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(IsFilled));
            OnPropertyChanged(nameof(IsPartial));
            OnPropertyChanged(nameof(StateText));
        }
    }
}
