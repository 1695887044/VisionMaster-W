using Core.Halcon;
using Core.Interfaces.Core;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Plugin.CreateRoi.Models
{
    /// <summary>
    /// 参数化 ROI 配置项（随方案持久化）
    /// 形状参数 Params 与 Halcon Gen* 算子参数一一对应：
    /// - Rectangle: [row, col, phi, length1, length2]（可旋转矩形）→ GenRectangle2
    /// - Circle:    [row, col, radius]                       → GenCircle
    /// - Ellipse:   [row, col, phi, ra, rb]                  → GenEllipse
    /// 用参数而非 Halcon 对象存储，保证 ROI 可序列化、可复现（运行时按参数重建区域）
    /// </summary>
    public class RoiItem : ObservableObject
    {
        private string _name = "ROI";
        /// <summary>ROI 名称（列表显示/下游按名引用）</summary>
        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayText)); }
        }

        private DrawShapeType _shapeType = DrawShapeType.Rectangle;
        /// <summary>形状类型（第一版支持 Rectangle/Circle/Ellipse 三种参数化形状）</summary>
        public DrawShapeType ShapeType
        {
            get => _shapeType;
            set
            {
                if (_shapeType == value) return;
                _shapeType = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayText));
                // 形状变化 → ParamNames 变化 → 重建 entry 集合
                RebuildParamEntries();
                OnPropertyChanged(nameof(ParamEntries));
            }
        }

        private double[] _params = System.Array.Empty<double>();
        /// <summary>形状参数（含义见类注释）</summary>
        public double[] Params
        {
            get => _params;
            set
            {
                _params = value ?? System.Array.Empty<double>();
                OnPropertyChanged();
                // 长度变化时重建集合；长度不变时通知每个 entry 的 Value 刷新（拖拽高频场景的关键路径）
                if (_paramEntries.Count != ExpectedEntryCount)
                    RebuildParamEntries();
                else
                    foreach (var e in _paramEntries) e.RaiseValueChanged();
                OnPropertyChanged(nameof(ParamEntries));
            }
        }

        /// <summary>列表显示文本</summary>
        public string DisplayText => $"{Name}  [{ShapeType}]";

        /// <summary>参数中文名（按形状类型；与 Params 下标一一对应）</summary>
        public IReadOnlyList<string> ParamNames => ShapeType switch
        {
            DrawShapeType.Rectangle => new[] { "中心行(R)", "中心列(C)", "角度(Phi)", "半长(L1)", "半宽(L2)" },
            DrawShapeType.Circle => new[] { "圆心行(R)", "圆心列(C)", "半径(Radius)" },
            DrawShapeType.Ellipse => new[] { "圆心行(R)", "圆心列(C)", "旋转角(Phi)", "长半轴(Ra)", "短半轴(Rb)" },
            _ => System.Array.Empty<string>()
        };

        /// <summary>
        /// 参数编辑回调（ViewModel 注入：数值框改值后同步画布/掩膜；拖拽重建面板时视图重读 ParamEntries）
        /// </summary>
        public event Action<RoiItem>? ParamEdited;

        /// <summary>参数编辑入口（由 ParamEntry.Value setter 调用）</summary>
        public void NotifyParamEdited() => ParamEdited?.Invoke(this);

        /// <summary>期望的 entry 数量（Params 长度与 ParamNames 的最小值）</summary>
        private int ExpectedEntryCount => System.Math.Min(_params.Length, ParamNames.Count);

        /// <summary>持久参数微调集合（XAML ItemsControl 绑定源；避免每次访问新建集合导致 WPF 刷新不可靠）</summary>
        private readonly ObservableCollection<RoiParamEntry> _paramEntries = new();

        /// <summary>
        /// 参数微调行（持久集合，XAML ItemsControl 绑定源）
        /// - 首次访问或长度/形状不匹配时重建
        /// - Params 同长度替换时（拖拽场景）不重建，由 entry 的 Value INPC 通知刷新数值
        /// </summary>
        public ObservableCollection<RoiParamEntry> ParamEntries
        {
            get
            {
                if (_paramEntries.Count != ExpectedEntryCount)
                    RebuildParamEntries();
                return _paramEntries;
            }
        }

        /// <summary>按当前 Params 与 ParamNames 重建 entry 集合（形状/长度变化时调用）</summary>
        private void RebuildParamEntries()
        {
            _paramEntries.Clear();
            for (int i = 0; i < _params.Length && i < ParamNames.Count; i++)
                _paramEntries.Add(new RoiParamEntry(this, i, ParamNames[i]));
        }
    }

    /// <summary>
    /// 参数微调行包装（MVVM）：TextBox 双向绑定 Value，写回 RoiItem.Params（单一数据源）并触发编辑回调
    /// </summary>
    public class RoiParamEntry : ObservableObject
    {
        private readonly RoiItem _owner;
        private readonly int _index;

        public RoiParamEntry(RoiItem owner, int index, string name)
        {
            _owner = owner;
            _index = index;
            Name = name;
        }

        /// <summary>参数中文名</summary>
        public string Name { get; }

        /// <summary>当前值（实时读 Params；编辑后写回 Params）</summary>
        public double Value
        {
            get
            {
                if (_index >= _owner.Params.Length) return 0;
                return _owner.Params[_index];
            }
            set
            {
                if (_index >= _owner.Params.Length) return;
                if (System.Math.Abs(_owner.Params[_index] - value) < double.Epsilon) return;
                _owner.Params[_index] = value;
                OnPropertyChanged();
                _owner.NotifyParamEdited();
            }
        }

        /// <summary>
        /// Params 数组被整体替换后（拖拽场景），由 RoiItem 调用以触发 Value 的 INPC 通知，
        /// 让绑定到 Value 的 TextBox 直接刷新，不依赖 WPF 重新读取整个 ParamEntries 集合
        /// </summary>
        public void RaiseValueChanged() => OnPropertyChanged(nameof(Value));
    }
}
