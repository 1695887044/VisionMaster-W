using Core.Halcon;
using Core.Interfaces.Core;
using Newtonsoft.Json;
using System.Collections.ObjectModel;

namespace Plugin.CaliperMeasure.Models
{
    /// <summary>
    /// 一个卡尺搜索区（随方案持久化）。
    ///
    /// 参数 Params 与 Halcon GenRectangle2 一一对应：[中心行, 中心列, 角度Phi, 半长L1, 半宽L2]。
    /// 用"参数数组"而不是 Halcon 对象存储，保证可序列化、可复现（运行时按参数重建）。
    ///
    /// 几何语义（与算法严格一致，已用真实 HALCON 实证，务必看清）：
    ///   · 半长 L1 方向（Phi 方向）—— 每条卡尺"沿此方向扫描"，检出的边缘垂直于它；
    ///   · 半宽 L2 方向（垂直于 Phi）—— 卡尺阵列"沿此方向均布"，N 把卡尺等分这段宽度。
    /// 也就是说：矩形要"横跨"被测的边/缝（让 L1 方向与边垂直），卡尺才扫得到边；
    /// 而 L2 方向是阵列排布方向（沿边的方向），用来在多个位置重复取边、取平均抗噪。
    /// 实证（gen_measure_rectangle2 真实行为）：Phi=0 时 L1 沿水平方向——把 L1=100/L2=10 的矩形
    /// 放在竖直亮带上能测出宽度；把 L1 缩到 10（扫描线够不到边）或把 Phi 转 90°（扫描方向变竖直）
    /// 都检不到边。所以"扫描方向 = L1 轴"是硬事实，阵列必须沿 L2 轴均布。
    ///
    /// 与 Plugin.CreateRoi.Models.RoiItem 是同一套写法（形状参数 + ParamEntries 微调行 + ParamEdited 回调），
    /// 差异只在命名与用途，方便维护者一眼看出两个插件共用同一范式。
    /// </summary>
    public class CaliperRegion : ObservableObject
    {
        private string _name = "卡尺";
        /// <summary>搜索区名称（列表显示/算法按名回写）</summary>
        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayText)); }
        }

        private DrawShapeType _shapeType = DrawShapeType.Rectangle;
        /// <summary>形状类型（算法只接受 Rectangle；其它形状是画布右键误建，会在运行期明确报错）</summary>
        public DrawShapeType ShapeType
        {
            get => _shapeType;
            set
            {
                if (_shapeType == value) return;
                _shapeType = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayText));
                RebuildParamEntries();
                OnPropertyChanged(nameof(ParamEntries));
            }
        }

        private double[] _params = Array.Empty<double>();
        /// <summary>形状参数（含义见类注释；与 Params 下标一一对应）</summary>
        public double[] Params
        {
            get => _params;
            set
            {
                _params = value ?? Array.Empty<double>();
                OnPropertyChanged();
                // 长度变化时重建集合；长度不变时通知每个 entry 刷新（拖拽高频场景的关键路径）
                if (_paramEntries.Count != ExpectedEntryCount)
                    RebuildParamEntries();
                else
                    foreach (var e in _paramEntries) e.RaiseValueChanged();
                OnPropertyChanged(nameof(ParamEntries));
            }
        }

        /// <summary>列表显示文本</summary>
        [JsonIgnore]
        public string DisplayText => $"{Name}  [{ShapeType}]";

        /// <summary>参数中文名（按形状类型；与 Params 下标一一对应）</summary>
        [JsonIgnore]
        public IReadOnlyList<string> ParamNames => ShapeType switch
        {
            DrawShapeType.Rectangle => new[] { "中心行(R)", "中心列(C)", "角度(Phi)", "半长(L1)", "半宽(L2)" },
            DrawShapeType.Circle => new[] { "圆心行(R)", "圆心列(C)", "半径(Radius)" },
            DrawShapeType.Ellipse => new[] { "圆心行(R)", "圆心列(C)", "旋转角(Phi)", "长半轴(Ra)", "短半轴(Rb)" },
            _ => Array.Empty<string>()
        };

        /// <summary>参数编辑回调（ViewModel 注入：数值框改值后同步画布并刷新预览）</summary>
        public event Action<CaliperRegion>? ParamEdited;

        /// <summary>参数编辑入口（由 ParamEntry.Value setter 调用）</summary>
        public void NotifyParamEdited() => ParamEdited?.Invoke(this);

        /// <summary>期望的 entry 数量（Params 长度与 ParamNames 的最小值）</summary>
        private int ExpectedEntryCount => Math.Min(_params.Length, ParamNames.Count);

        /// <summary>持久参数微调集合（XAML ItemsControl 绑定源；避免每次访问新建集合导致 WPF 刷新不可靠）</summary>
        private readonly ObservableCollection<CaliperParamEntry> _paramEntries = new();

        /// <summary>
        /// 参数微调行（持久集合，XAML 绑定源）
        /// </summary>
        /// <remarks>
        /// [JsonIgnore] 必须：CaliperParamEntry 无无参构造且持有 owner 引用（纯 UI 包装，不持久化）。
        /// Newtonsoft 默认会填充 get-only 集合属性，不加此特性反序列化会抛
        /// "Unable to find a constructor to use for type CaliperParamEntry"。
        /// </remarks>
        [JsonIgnore]
        public ObservableCollection<CaliperParamEntry> ParamEntries
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
                _paramEntries.Add(new CaliperParamEntry(this, i, ParamNames[i]));
        }
    }

    /// <summary>
    /// 参数微调行包装（MVVM）：TextBox 双向绑定 Value，写回 CaliperRegion.Params（单一数据源）并触发编辑回调
    /// </summary>
    public class CaliperParamEntry : ObservableObject
    {
        private readonly CaliperRegion _owner;
        private readonly int _index;

        public CaliperParamEntry(CaliperRegion owner, int index, string name)
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
                if (Math.Abs(_owner.Params[_index] - value) < double.Epsilon) return;
                _owner.Params[_index] = value;
                OnPropertyChanged();
                _owner.NotifyParamEdited();
            }
        }

        /// <summary>
        /// Params 数组被整体替换后（拖拽场景），由 CaliperRegion 调用以触发 Value 的 INPC 通知，
        /// 让绑定到 Value 的 TextBox 直接刷新，不依赖 WPF 重新读取整个 ParamEntries 集合
        /// </summary>
        public void RaiseValueChanged() => OnPropertyChanged(nameof(Value));
    }
}
