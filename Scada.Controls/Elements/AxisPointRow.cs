using System;
using System.Globalization;
using Prism.Mvvm;
using VisionMaster.Scada;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// 轴点位表的一行（轴控台与旧点位表共用）。
    ///
    /// 速度是**可编辑**的：`SpeedText` 的 setter 在用户提交（LostFocus）时回调宿主写回模型；
    /// 由 <c>AxisPanelElement.RefreshRows</c> 重建时用 `_suppressCommit` 挡住回调，
    /// 避免"刷新一遍就写一次"。
    /// </summary>
    public sealed class AxisPointRow : BindableBase
    {
        private readonly Action<AxisPointRow, string>? _commitSpeed;
        private bool _suppressCommit;
        private string _speedText;

        public AxisPointRow(ScadaAxisPointInfo point, Action<AxisPointRow, string>? commitSpeed)
        {
            Index = point.Index;
            Caption = point.Caption;
            PositionMm = point.PositionMm;
            SpeedMmPerS = point.SpeedMmPerS;
            _speedText = point.SpeedMmPerS.ToString("F2", CultureInfo.InvariantCulture);
            _commitSpeed = commitSpeed;
        }

        public int Index { get; }
        public string Caption { get; }
        public double PositionMm { get; }
        public double SpeedMmPerS { get; }

        /// <summary>位置文本（三位小数，等宽显示）</summary>
        public string PositionText => PositionMm.ToString("F3", CultureInfo.InvariantCulture);

        /// <summary>速度文本（两位小数；编辑提交后回调宿主写回模型）</summary>
        public string SpeedText
        {
            get => _speedText;
            set
            {
                string text = value ?? string.Empty;
                if (!SetProperty(ref _speedText, text) || _suppressCommit)
                    return;

                _commitSpeed?.Invoke(this, text);
            }
        }
    }
}
