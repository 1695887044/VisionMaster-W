using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HalconDotNet;
using VisionMaster.Helpers;

namespace VisionMaster.Models
{
    /// <summary>
    /// 变量节点模型
    /// 用于在变量绑定面板中展示变量的树形结构
    /// </summary>
    public class VariableNode : BindableBase
    {
        /// <summary>
        /// 原始变量模型引用
        /// </summary>
        public IVariable OriginalModel { get; set; }

        private bool _isNetwork;
        /// <summary>
        /// 是否为网络变量（VariableType.Communication）
        /// </summary>
        public bool IsNetwork
        {
            get => _isNetwork;
            set => SetProperty(ref _isNetwork, value);
        }

        private string? _sourceLabel;
        /// <summary>
        /// 来源标签：本地变量="本地"，网络变量=所属连接名（UI 徽章显示用）
        /// </summary>
        public string? SourceLabel
        {
            get => _sourceLabel;
            set => SetProperty(ref _sourceLabel, value);
        }

        private string? _address;
        /// <summary>
        /// 设备地址（网络变量才有，如 40100 / MW10 / DB1.DBW0）
        /// </summary>
        public string? Address
        {
            get => _address;
            set => SetProperty(ref _address, value);
        }

        private bool _isConnected;
        /// <summary>
        /// 连接在线状态（网络变量；本地变量恒 true）
        /// </summary>
        public bool IsConnected
        {
            get => _isConnected;
            set => SetProperty(ref _isConnected, value);
        }

        private string _scanGroupText = string.Empty;
        /// <summary>
        /// 扫描组显示文案（仅网络变量根节点用）：
        /// 模型里空组名表示"默认组"，界面上要显示成"默认组"而不是空白，故由 VM 抄写时归一化。
        /// </summary>
        public string ScanGroupText
        {
            get => _scanGroupText;
            set
            {
                if (SetProperty(ref _scanGroupText, value))
                    ScanGroupChanged?.Invoke(this);
            }
        }

        /// <summary>
        /// 该变量所属连接的可选扫描组名（含"默认组"，恒在首位）。
        /// 本地变量为空集合 → 列上不渲染下拉框。
        /// </summary>
        public ObservableCollection<string> ScanGroupOptions { get; } = new();

        /// <summary>
        /// 扫描组变更回调：由 VM 注入。
        /// 节点在 Core 工程、不认识通信管理器，"回写模型 + 重注册轮询"只能由上层做。
        /// </summary>
        public Action<VariableNode>? ScanGroupChanged { get; set; }

        /// <summary>
        /// 是否显示「扫描组」列内容（仅网络变量的根节点；数组子节点不显示）
        /// </summary>
        public bool ShowScanGroup => IsRootNode && IsNetwork;

        // ==================================================================
        //  显示分组（方案级分类；只做归类与筛选，不参与采集/轮询/写入）
        // ==================================================================

        /// <summary>显示分组 Id（与网络变量模型的 GroupId 同步；Guid.Empty = 未分组）</summary>
        public Guid GroupId { get; set; }

        /// <summary>
        /// 该变量可选的分组（「未分组」恒在首位）。本地/系统变量为空集合 → 列上不渲染下拉框。
        /// </summary>
        public ObservableCollection<VariableGroupOption> GroupOptions { get; } = new();

        private VariableGroupOption? _selectedGroup;

        /// <summary>
        /// 当前选中的显示分组。setter 触发 <see cref="GroupChanged"/>，由 VM 回写模型并刷新计数。
        /// </summary>
        public VariableGroupOption? SelectedGroup
        {
            get => _selectedGroup;
            set
            {
                if (SetProperty(ref _selectedGroup, value))
                    GroupChanged?.Invoke(this);
            }
        }

        /// <summary>
        /// 显示分组变更回调：由 VM 注入。节点在 Core 工程、不认识方案模型，
        /// "回写模型 + 刷新来源计数"只能由上层做。
        /// </summary>
        public Action<VariableNode>? GroupChanged { get; set; }

        /// <summary>
        /// 是否显示「分组」列内容（仅网络变量的根节点；数组子节点不显示）
        /// </summary>
        public bool ShowGroup => IsRootNode && IsNetwork;

        /// <summary>
        /// 变量名称
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 数据类型
        /// </summary>
        public Type DataType { get; set; }

        /// <summary>
        /// 类型名称（字符串表示）
        /// </summary>
        public string TypeName { get; set; }

        /// <summary>
        /// 变量描述
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// 原始值
        /// </summary>
        public object RawValue { get; set; }

        /// <summary>
        /// 显示值（格式化后）
        /// </summary>
        public string DisplayValue
        {
            get
            {
                if (RawValue is Array arr) return $"Array [{arr.Length}]";
                return RawValue?.ToString() ?? "Null";
            }
        }

        /// <summary>
        /// 数组子节点默认值
        /// </summary>
        public object ChildDefaultValue { get; set; }

        /// <summary>
        /// 数组子节点值
        /// </summary>
        public object ChildValue { get; set; }

        /// <summary>
        /// 子节点集合（用于数组类型展开）
        /// </summary>
        public ObservableCollection<VariableNode> Children { get; } = new();

        /// <summary>
        /// 是否为容器节点（有子节点）
        /// </summary>
        public bool IsContainer => Children.Count > 0;

        /// <summary>
        /// 层级（0为根节点，1为子节点）
        /// </summary>
        public int Level { get; set; }

        /// <summary>
        /// 是否为根节点
        /// </summary>
        public bool IsRootNode => Level == 0;

        /// <summary>
        /// 是否为子节点
        /// </summary>
        public bool IsChildNode => Level == 1;

        /// <summary>
        /// 是否为可编辑节点（根节点、非数组、且初始值能用文本表达）
        ///
        /// 图像（HImage）也满足"根节点、非数组"，但它的初始值没法用文本写：
        /// 位图不是一段字符串，<see cref="DefaultValueText"/> 的 setter 会走
        /// Convert.ChangeType 而必然抛异常。所以这里一并排除——变量表里那一格就变成只读，
        /// 而不是等操作员输入完再弹一个他看不懂的异常（图像的来源本就是流程，不是手填）。
        ///
        /// 系统变量（运动/IO 等）同样排除：它们的初始值由设备/宿主决定，改这一格没有落点
        /// （<c>DefaultValue</c> 写进去也不会回灌到设备），只能给"可编辑"的假象。
        /// </summary>
        public bool IsEditableNode => Level == 0 && DataType != null && !DataType.IsArray
            && !IsImageType(DataType) && !IsSystemOwned;

        /// <summary>系统维护的变量（系统变量 / IO 点变量）：值可读，定义不可编辑。</summary>
        public bool IsSystemOwned =>
            OriginalModel?.VariableType is VariableType.System or VariableType.Io;

        /// <summary>是不是图像变量（位图类，其初始值不可文本化）</summary>
        private static bool IsImageType(Type type) => typeof(HImage).IsAssignableFrom(type);

        /// <summary>
        /// 是否为数组根节点
        /// </summary>
        public bool IsArrayRootNode => Level == 0 && DataType != null && DataType.IsArray;

        /// <summary>
        /// 网络数组是否可整组写入：只读存储区（离散输入 / S7 输入映像 I）不给入口。
        /// <para>元素行始终只读——整组写是唯一写语义，单元素写会与"固定跨度"的配置脱节。</para>
        /// </summary>
        public bool CanWriteNetworkArray =>
            IsArrayRootNode && OriginalModel is NetworkVariableModel && !IsValueReadOnly;

        /// <summary>
        /// "初始值"列的类型安全代理（A1）：
        /// 旧绑定直写 IVariable.DefaultValue(object)，任何字符串都被原样塞进模型——
        /// 给 int 变量填 "abc" 也"保存成功"，恢复初始值时污染值流入算子端口才爆炸。
        /// 现按节点 DataType 转换：合法才写模型，非法抛异常交由绑定引擎标记校验失败（模型保持干净）。
        /// 数组类型不走此入口（走"编辑集合"对话框）。
        /// </summary>
        public string DefaultValueText
        {
            get => OriginalModel?.DefaultValue?.ToString() ?? "";
            set
            {
                var model = OriginalModel;
                if (model == null || DataType == null) return;

                Type t = Nullable.GetUnderlyingType(DataType) ?? DataType;
                if (t.IsArray) return; // 数组初始值只走"编辑集合"

                object? converted;
                if (string.IsNullOrWhiteSpace(value))
                    converted = t.IsValueType ? Activator.CreateInstance(t) : null;
                else
                {
                    try { converted = Convert.ChangeType(value, t); }
                    catch (Exception ex)
                    {
                        throw new ArgumentException($"'{value}' 不是有效的 {t.Name} 值：{ex.Message}");
                    }
                }

                model.DefaultValue = converted;
                ChildDefaultValue = converted;
                RaisePropertyChanged(nameof(ChildDefaultValue));
                RaisePropertyChanged(nameof(IsValueDeviated));
            }
        }

        // ==================================================================
        //  当前值就地编辑（2026-10-11 交互收敛：写值进"当前值"单元格，
        //  "恢复初始值"从操作列挪进单元格、仅偏离基准时出现）
        // ==================================================================
        //
        //  提交语义（与"初始值"列刻意不同）：
        //  - 只写 Value（经 IWritableVariable.TryWrite：本地=内存赋值，网络=下发设备），
        //    绝不触碰 DefaultValue——基准值是"恢复初始值"的锚点，若就地编辑悄悄改掉它，
        //    "恢复"就恢复不到用户认定的基准了；
        //  - 文本转换统一走 VariableValueConverter.TryConvert（布尔认 1/0/是/否/on/off、
        //    不变文化、数组/图像明确拒绝），与组态动作 WriteVariable 共享同一份规则；
        //  - 编辑期间（IsEditingValue）VM 的值推送暂停：轮询周期里的镜像更新不许冲掉正在输入的草稿。
        //  交互由 VisionMaster.Helpers.CellValueEditBehavior 驱动（获得焦点=进入编辑、
        //  回车/失焦=提交、Esc=放弃、失败=红框保持编辑态）。

        /// <summary>是否处于单元格编辑态：为 true 时模型值推送暂停（防止轮询冲掉输入中的草稿）</summary>
        public bool IsEditingValue { get; private set; }

        /// <summary>
        /// 编辑结束（提交成功或 Esc 取消）后触发。
        /// <para>为什么需要：编辑期间模型值推送会跳过"数组长度变化 → 重建子行"，
        /// 若外部在编辑中换了数组长度、用户又按 Esc 取消，就没有任何模型事件能把那次重建补上。
        /// VM 收到本事件后按 <c>ShouldRebuildArrayRows</c> 复查一次。</para>
        /// </summary>
        public event Action<VariableNode>? ValueEditCompleted;

        /// <summary>进入编辑态（由单元格行为在获得键盘焦点时调用；幂等）</summary>
        public void BeginEditValue()
        {
            if (IsEditingValue) return;
            IsEditingValue = true;
        }

        /// <summary>放弃草稿：退出编辑态并把界面文本回读为模型值</summary>
        public void CancelEditValue()
        {
            if (!IsEditingValue) return;
            IsEditingValue = false;
            RaisePropertyChanged(nameof(CurrentValueText));
            ValueEditCompleted?.Invoke(this);
        }

        /// <summary>
        /// 提交编辑文本。成功返回 null（退出编辑态、界面回读模型值）；
        /// 失败返回可直接展示的中文原因（保持编辑态，模型未被污染，由调用方标红）。
        /// 根行与数组元素行自动分派：根行走 <see cref="IWritableVariable.TryWrite"/>，
        /// 元素行就地写父数组当前值的对应下标（见 <see cref="CommitChildElementEdit"/>）。
        /// </summary>
        public string? CommitEditValue(string? text)
        {
            var model = OriginalModel;
            if (model == null || DataType == null) return "变量模型不可用";

            if (ElementIndex >= 0)
                return CommitChildElementEdit(model, text);

            if (!VariableValueConverter.TryConvert(text, DataType, out var converted, out var convertError))
                return convertError;

            if (model is not IWritableVariable writable)
                return $"变量 [{model.Name}] 不支持写入";

            if (!writable.TryWrite(converted, out var writeError))
                return writeError;

            EndEditValue();
            return null;
        }

        /// <summary>
        /// 数组元素行的提交：就地写父数组【当前值】的第 <see cref="ElementIndex"/> 项，只动 Value、不碰 DefaultValue——
        /// 基准由父行的「编辑数组…」弹窗按需维护（勾选「同时写入初始值」才写）。
        /// 数组只有本地变量（网络侧创建时已过滤数组），无设备 I/O 语义，直接写内存数组。
        /// <para>引用未变 → Value setter 的 SetProperty 短路 → 必须显式广播一次 ValueChanged，
        /// 否则监视栏 / SCADA 绑定 / 触发服务都看不到这次修改（见 IValueChangeNotifier）。</para>
        /// </summary>
        private string? CommitChildElementEdit(IVariable model, string? text)
        {
            // 网络数组的元素是设备轮询镜像，不是本地内存数组：在这里改只会改镜像，
            // 下一拍就被设备值覆盖（"改了但没进设备"的典型假象）。只读，写入走后续版本的整组写。
            if (model is NetworkVariableModel)
                return "网络数组的元素由设备轮询提供，不能在此修改";

            if (model.Value is not Array current || ElementIndex >= current.Length)
                return "当前值数组不可用（请用父行的「编辑数组…」重建）";

            if (!VariableValueConverter.TryConvert(text, DataType, out var converted, out var convertError))
                return convertError;

            current.SetValue(converted, ElementIndex);
            ChildValue = converted;
            EndEditValue();
            RaisePropertyChanged(nameof(ChildValue));
            RaisePropertyChanged(nameof(IsElementDeviated));
            (model as IValueChangeNotifier)?.NotifyValueChanged();
            return null;
        }

        /// <summary>
        /// 把本元素恢复为基准数组（DefaultValue）对应下标的值（元素行 ↺ 的模型路径）。
        /// 只写当前值；基准比本元素短（例如流程写入了更长的数组）时无事可做，返回 false。
        /// </summary>
        public bool ResetElementValue()
        {
            var model = OriginalModel;
            if (model == null || ElementIndex < 0) return false;
            if (model is NetworkVariableModel) return false; // 网络数组只读，没有"元素级恢复基准"
            if (model.DefaultValue is not Array baseline || ElementIndex >= baseline.Length) return false;
            if (model.Value is not Array current || ElementIndex >= current.Length) return false;

            // 提交失败会保持编辑态并标红；点 ↺ 时先把那次草稿放弃掉，
            // 否则编辑态与红框会残留，后续值推送继续被暂停（2026-10-21 审查抓出）。
            CancelEditValue();

            object? restored = baseline.GetValue(ElementIndex);
            current.SetValue(restored, ElementIndex);
            ChildValue = restored;
            RaisePropertyChanged(nameof(ChildValue));
            RaisePropertyChanged(nameof(CurrentValueText));
            RaisePropertyChanged(nameof(IsElementDeviated));
            RaisePropertyChanged(nameof(IsValueDeviated));
            (model as IValueChangeNotifier)?.NotifyValueChanged();
            return true;
        }

        private void EndEditValue()
        {
            IsEditingValue = false;
            RaisePropertyChanged(nameof(CurrentValueText));
            RaisePropertyChanged(nameof(IsValueDeviated));
            RaisePropertyChanged(nameof(IsElementDeviated));
            ValueEditCompleted?.Invoke(this);
        }

        /// <summary>
        /// 模型值变化时的就地刷新（VM 在 UI 线程调用，取代"值变化打全树重建"）：
        /// 编辑中不打扰草稿，其余同步文本、偏离标记与数组元素行快照。
        /// </summary>
        public void RefreshValueDisplay()
        {
            if (!IsEditingValue)
                RaisePropertyChanged(nameof(CurrentValueText));
            RaisePropertyChanged(nameof(IsValueDeviated));
            RaisePropertyChanged(nameof(IsElementDeviated));
            RaisePropertyChanged(nameof(ArraySummary));
            RaisePropertyChanged(nameof(ArraySummaryTooltip));

            if (ElementIndex >= 0 && OriginalModel?.Value is Array current && ElementIndex < current.Length)
            {
                ChildValue = current.GetValue(ElementIndex);
                RaisePropertyChanged(nameof(ChildValue));
            }
        }

        /// <summary>
        /// 当前值显示文本（根行与数组元素行同格复用）：元素行读父数组当前值的对应下标。
        /// 编辑中草稿由 TextBox 自己持有，getter 始终反映模型；数值用不变文化格式化，
        /// 与 VariableValueConverter 的解析方向对齐（避免小数点随区域漂移）。
        /// </summary>
        public string CurrentValueText
        {
            get
            {
                var model = OriginalModel;
                if (model == null) return "";

                object? raw = ElementIndex >= 0
                    ? (model.Value is Array current && ElementIndex < current.Length
                        ? current.GetValue(ElementIndex)
                        : null)
                    : model.Value;

                // 网络数组：元素行按该元素的换算档案显示（小数位 + 单位），
                // 与「写入数组」弹窗里的工程值同一口径；本地数组/标量走原样格式化。
                if (ElementIndex >= 0 && model is NetworkVariableModel network)
                    return network.FormatElement(ElementIndex, raw);

                return Convert.ToString(raw, System.Globalization.CultureInfo.InvariantCulture) ?? "";
            }
        }

        /// <summary>
        /// 当前值是否偏离初始值：决定单元格内 ↺（回到基准）是否出现。
        /// 数组必须逐元素比较——数组是引用类型，<see cref="object.Equals(object,object)"/> 比的是引用：
        /// 「编辑数组」弹窗写的是新数组实例，按引用比会让内容一致的两组也永远显示"已偏离"；
        /// 反过来，新建数组时 DefaultValue 与 Value 曾是同一实例，按引用比又永远判"未偏离"。
        /// </summary>
        public bool IsValueDeviated
        {
            get
            {
                var model = OriginalModel;
                if (model == null) return false;

                if (model.Value is Array || model.DefaultValue is Array)
                    return !ArrayContentEquals(model.Value as Array, model.DefaultValue as Array);

                return !Equals(model.Value, model.DefaultValue);
            }
        }

        /// <summary>
        /// 数组元素行是否偏离基准（该下标在基准与当前值里都存在、且值不同才为真）。
        /// 基准比当前值短（典型：流程写入的条码数组，基准是 0 项）时不给 ↺——
        /// 那种"恢复"会把元素清空，不是用户想要的动作；
        /// 当前值比基准短（基准更长）时多出来的行同样不给 ↺：复位需要写当前值的该下标，
        /// 写不进去的按钮就是"点了没反应"的死按钮（2026-10-21 审查抓出）。
        /// </summary>
        public bool IsElementDeviated
        {
            get
            {
                var model = OriginalModel;
                if (model == null || ElementIndex < 0) return false;
                if (model.Value is not Array current || ElementIndex >= current.Length) return false;
                if (model.DefaultValue is not Array baseline || ElementIndex >= baseline.Length) return false;

                return !Equals(current.GetValue(ElementIndex), baseline.GetValue(ElementIndex));
            }
        }

        /// <summary>
        /// 数组根行的当前值摘要（不展开也能看出这组值是什么）：
        /// 前 2 项用 " · " 连接，超过 2 项补 " …"；空集合显示"空集合"，无值显示"（无值）"。
        /// 数值一律不变文化格式化，与 <see cref="CurrentValueText"/> 同口径。
        /// </summary>
        public string ArraySummary
        {
            get
            {
                var model = OriginalModel;
                if (model?.Value is not Array current) return "（无值）";

                // 网络数组的配置长度是唯一真相源（导入可改大/改小）；旧值可能更长，
                // 摘要与子行必须按配置长度显示，否则 5→3 时摘要/工具提示还列着 5 项。
                int length = (model as NetworkVariableModel)?.AddressConfig?.Length ?? current.Length;
                if (length == 0) return current.Length == 0 ? "空集合" : "（无值）";

                var parts = new List<string>(Math.Min(2, length));
                for (int i = 0; i < Math.Min(2, length); i++)
                    parts.Add(i < current.Length
                        ? Convert.ToString(current.GetValue(i), System.Globalization.CultureInfo.InvariantCulture) ?? ""
                        : "—");

                return string.Join(" · ", parts) + (length > 2 ? " …" : "");
            }
        }

        /// <summary>摘要的完整版本（ToolTip 用）：全部元素一行</summary>
        public string ArraySummaryTooltip
        {
            get
            {
                var model = OriginalModel;
                if (model?.Value is not Array current) return "";
                int length = (model as NetworkVariableModel)?.AddressConfig?.Length ?? current.Length;
                if (length == 0) return current.Length == 0 ? "空集合" : "";

                var parts = new string[length];
                for (int i = 0; i < length; i++)
                    parts[i] = i < current.Length
                        ? Convert.ToString(current.GetValue(i), System.Globalization.CultureInfo.InvariantCulture) ?? ""
                        : "—";
                return string.Join(" · ", parts);
            }
        }

        /// <summary>逐元素比较两个数组的内容（null 与空数组视为不同；长度不同直接判不同）</summary>
        private static bool ArrayContentEquals(Array? left, Array? right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left == null || right == null) return false;
            if (left.Length != right.Length) return false;

            for (int i = 0; i < left.Length; i++)
            {
                if (!Equals(left.GetValue(i), right.GetValue(i))) return false;
            }
            return true;
        }

        /// <summary>
        /// 当前值是否只读（Modbus 离散输入 1x / S7 输入映像 I 等只读存储区）：
        /// 编辑框转只读展示，物理上写不进去的区不再给可编辑的假象。
        /// </summary>
        public bool IsValueReadOnly => (OriginalModel as IVariableDisplaySource)?.IsReadOnly == true;

        private bool _isExpanded;
        /// <summary>
        /// 是否展开（用于树形控件）
        /// </summary>
        public bool IsExpanded
        {
            get => _isExpanded;
            set => SetProperty(ref _isExpanded, value);
        }

        /// <summary>
        /// 本节点是父数组里的第几个元素（非数组元素节点为 -1）。
        ///
        /// 为什么由上层显式赋值而不是自己算：节点是"每次 RefreshTree 重建"的展示对象，
        /// 它与父数组的对应关系只有创建它的那一处（AddArrayChildren 的循环变量）知道，
        /// 在这里靠 Name（"[3]"）反解字符串是把展示格式当数据用，改名格式就崩。
        /// </summary>
        public int ElementIndex { get; set; } = -1;
    }

    /// <summary>
    /// 变量显示分组的候选项（ComboBox 用）：Id 是落库引用，Name 是显示文案。
    /// 「未分组」是内置项（Id = Guid.Empty），不可改名/删除。
    /// </summary>
    public sealed class VariableGroupOption
    {
        public Guid Id { get; init; }

        public string Name { get; init; } = string.Empty;

        public override string ToString() => Name;
    }
}
