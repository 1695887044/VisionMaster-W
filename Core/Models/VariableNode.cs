﻿﻿﻿﻿﻿using System;
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
        /// </summary>
        public bool IsEditableNode => Level == 0 && DataType != null && !DataType.IsArray && !IsImageType(DataType);

        /// <summary>是不是图像变量（位图类，其初始值不可文本化）</summary>
        private static bool IsImageType(Type type) => typeof(HImage).IsAssignableFrom(type);

        /// <summary>
        /// 是否为数组根节点
        /// </summary>
        public bool IsArrayRootNode => Level == 0 && DataType != null && DataType.IsArray;

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
        /// 基准随父行「强制覆盖集合...」整体维护（那里DefaultValue 与 Value 一起换）。元素级只写当前值。
        /// 数组只有本地变量（网络侧创建时已过滤数组），无设备 I/O 语义，直接写内存数组。
        /// </summary>
        private string? CommitChildElementEdit(IVariable model, string? text)
        {
            if (model.Value is not Array current || ElementIndex >= current.Length)
                return "当前值数组不可用（请用父行的「强制覆盖集合...」重建）";

            if (!VariableValueConverter.TryConvert(text, DataType, out var converted, out var convertError))
                return convertError;

            current.SetValue(converted, ElementIndex);
            ChildValue = converted;
            EndEditValue();
            RaisePropertyChanged(nameof(ChildValue));
            return null;
        }

        private void EndEditValue()
        {
            IsEditingValue = false;
            RaisePropertyChanged(nameof(CurrentValueText));
            RaisePropertyChanged(nameof(IsValueDeviated));
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
                return Convert.ToString(raw, System.Globalization.CultureInfo.InvariantCulture) ?? "";
            }
        }

        /// <summary>当前值是否偏离初始值：决定单元格内 ↺（回到基准）是否出现</summary>
        public bool IsValueDeviated
        {
            get
            {
                var model = OriginalModel;
                if (model == null) return false;
                return !Equals(model.Value, model.DefaultValue);
            }
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

        /// <summary>
        /// 数组元素初始值的可编辑代理（A2）：让"初始值"列能在表格里直接改单个元素，
        /// 不必为了改一个数字打开整个"编辑集合"弹窗。
        ///
        /// 写回路径与 DefaultValueText 同一套类型安全转换（合法才落地，非法抛异常给绑定引擎标红），
        /// 区别只在落点是父数组的第 <see cref="ElementIndex"/> 项 —— 数组是引用类型，
        /// SetValue 就地改的就是模型里那一份，不需要再给 Variable.DefaultValue 重新赋值。
        ///
        /// DefaultValue（初始值）与 Value（当前值）一起改：这与"编辑集合"弹窗
        /// （它整体 gv.Value = newArray.Clone()）保持同一语义，免得出现
        /// "表格里改了初始值、当前值还是旧的"这种两处不一致。
        /// </summary>
        public string ChildDefaultValueText
        {
            get => ChildDefaultValue?.ToString() ?? "";
            set
            {
                var model = OriginalModel;
                if (model == null || ElementIndex < 0 || DataType == null) return;

                Type t = Nullable.GetUnderlyingType(DataType) ?? DataType;

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

                if (model.DefaultValue is Array def && ElementIndex < def.Length)
                    def.SetValue(converted, ElementIndex);
                if (model.Value is Array cur && ElementIndex < cur.Length)
                    cur.SetValue(converted, ElementIndex);

                ChildDefaultValue = converted;
                ChildValue = converted;
                RaisePropertyChanged(nameof(ChildDefaultValue));
                RaisePropertyChanged(nameof(ChildValue));
            }
        }
    }
}
