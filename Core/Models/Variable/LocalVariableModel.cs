using Core.Interfaces;
using HalconDotNet;
using VisionMaster.Communications;
using System;
using System.Globalization;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VisionMaster.Helpers;
namespace VisionMaster.Models
{
    /// <summary>
    /// 本地变量模型
    /// 实现 IOutputPort 接口，可作为数据端口被其他步骤引用
    /// </summary>
    public class LocalVariableModel : BindableBase, IVariable, IVariableDisplaySource, IRenameableVariable, IWritableVariable, IArrayElementWritable, IValueChangeNotifier, IVariableValueOwner
    {
        private string _dataTypeString;
        private Type _dataType;
        private object? _value;

        /// <summary>
        /// 本变量**自己拷贝出来**的那份 HImage（= 当前值，若当前值是图像）。
        ///
        /// 只记自己拷的：外部直接 <c>Value = 借用图</c> 时它是 null，于是
        /// <see cref="ReleaseValues"/> 不会误杀别人拥有的图。所有权判定只看这一条，
        /// 不靠"Value 是不是 HImage"。
        /// </summary>
        private HImage? _ownedImage;

        /// <summary>
        /// 上一份已退役、等下一次写入时才释放的副本（延迟一代）。
        ///
        /// 为什么不当场释放：读图不一定在写入线程上——Web 运行态 <c>/img</c> 经 UI 线程按需读，
        /// 当场 Dispose 会与"正在做像素→位图转换的那张"撞车（HALCON #4060 / 静默丢弃）。
        /// 晚一代释放保证任何读者抓到的图至少还活一整轮，且不需要锁或线程封送。
        /// </summary>
        private HImage? _retiredImage;

        /// <summary>只保护两个图像字段的交换；不罩拷贝（毫秒级全图 memcpy），也不罩事件</summary>
        private readonly object _imageGate = new();

        /// <summary>
        /// 变量名称
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 变量稳定身份（GUID）：不随改名变化，供连线/监视项/SCADA 绑定寻址。
        /// 默认为新 GUID；可写仅用于反序列化与旧方案迁移（历史数据无此字段时由持久化层补发）
        /// </summary>
        public Guid VariableId { get; set; } = Guid.NewGuid();

        public VariableType VariableType { get; set; } = VariableType.Local;
        public string? ConnectionName { get; set; }

        public DeviceAddressBase? AddressConfig { get; set; }

        /// <summary>
        /// 数据类型（用于序列化）
        /// </summary>
        public string DataTypeString
        {
            get => _dataTypeString ?? TypeCache.GetTypeKey(DataType);
            set
            {
                _dataTypeString = value;
                _dataType = TypeCache.GetType(value);
            }
        }

        /// <summary>
        /// 数据类型
        /// </summary>
        [JsonIgnore]
        public Type DataType
        {
            get
            {
                if (_dataType == null && !string.IsNullOrEmpty(_dataTypeString))
                {
                    _dataType = TypeCache.GetType(_dataTypeString);
                }
                return _dataType ?? typeof(string);
            }
            set
            {
                _dataType = value;
                _dataTypeString = TypeCache.GetTypeKey(value);
            }
        }

        /// <summary>
        /// 变量描述
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// 默认值
        /// </summary>
        public object? DefaultValue { get; set; }

        /// <summary>
        /// 当前值
        /// </summary>
        public object? Value
        {
            get => _value;
            set
            {
                if (SetProperty(ref _value, value))
                {
                    if (VariableType == VariableType.Local)
                    {
                        _valueChanged?.Invoke(this, EventArgs.Empty);
                    }
                }
            }
        }

        /// <summary>
        /// 值变更事件
        /// </summary>
        [JsonIgnore]
        private EventHandler? _valueChanged;
        
        public event EventHandler ValueChanged
        {
            add => _valueChanged += value;
            remove => _valueChanged -= value;
        }

        /// <summary>
        /// <see cref="IWritableVariable"/> 认领：本地变量没有设备侧，写值就是内存赋值。
        /// 走 <see cref="Value"/> setter 而不是直接改字段，
        /// 是为了保住"值变化 → ValueChanged"这条通知链（运行态订阅靠它刷新控件）。
        /// 同值写入会被 <c>SetProperty</c> 短路——对本地变量这是对的：值没变就没有刷新可言。
        ///
        /// <b>图像例外：写入即拷贝。</b>调用方交来的 HImage 是**它自己的对象**（插件端口上的图、
        /// 脚本里 new 的图），所有权不在本变量——上游下一轮开始就会 Dispose 它
        /// （<c>VisionPluginBase.DisposePreviousRoundOutputs</c>）。存引用等于把"活不过一轮"的图
        /// 挂到变量上：释放后读者拿到的是死句柄，Web 运行态表现为图像周期性 404。
        /// 所以这里拷一份，变量只持有自己那份副本；释放口径见 <see cref="ReleaseValues"/>
        /// 与 <see cref="_retiredImage"/> 的延迟一代说明。
        /// </summary>
        public bool TryWrite(object? value, out string? error)
        {
            error = null;

            if (value is HImage source)
                return TryWriteImage(source, out error);

            Value = value;
            return true;
        }

        /// <summary>
        /// 图像写入：守空句柄 → 拷副本 → 换代 → 延迟释放上一代。
        /// 拷贝失败按写入失败返回（不静默存引用），原因带变量名，可直接上屏。
        /// </summary>
        private bool TryWriteImage(HImage source, out string? error)
        {
            error = null;

            HImage copy;
            try
            {
                if (!source.IsInitialized())
                {
                    error = $"变量 [{Name}] 收到未初始化的图像，已拒绝写入";
                    return false;
                }

                copy = source.CopyImage();
            }
            catch (Exception ex)
            {
                error = $"变量 [{Name}] 拷贝图像失败：{ex.Message}";
                return false;
            }

            HImage? retired;
            lock (_imageGate)
            {
                retired = _retiredImage;
                _retiredImage = _ownedImage; // 上一代退役，等下一次写入时才真释放
                _ownedImage = copy;
            }

            Value = copy; // 事件在锁外发：订阅方会在回调里同步做整图转换

            DisposeQuietly(retired);
            return true;
        }

        /// <summary>
        /// <see cref="IVariableValueOwner"/> 实现：释放当前副本与待释放的上一代，并把当前值置空。
        /// 由 <c>VariableRegistry</c> 在"变量被移出集合 / 集合被 Clear"时经
        /// <see cref="VariableValueOwner.Release"/> 调用。
        /// </summary>
        public void ReleaseValues()
        {
            HImage? current, retired;
            lock (_imageGate)
            {
                current = _ownedImage;
                retired = _retiredImage;
                _ownedImage = null;
                _retiredImage = null;
            }

            // 先摘值再释放：事件在 setter 里同步派发，读者可能当场取像素
            if (current != null && ReferenceEquals(Value, current))
                Value = null;

            DisposeQuietly(current);
            DisposeQuietly(retired);
        }

        /// <summary>释放一份自己拷的图；失败只吞掉，不打断写入 / 清理</summary>
        private static void DisposeQuietly(HImage? image)
        {
            if (image == null)
                return;

            try { image.Dispose(); }
            catch { /* 已释放 / 句柄异常都不该打断调用方 */ }
        }

        /// <summary>
        /// 就地写数组元素：不替换数组引用，只改这一项，然后显式广播一次
        /// <see cref="IValueChangeNotifier.NotifyValueChanged"/>。
        ///
        /// 为什么不能走 <see cref="TryWrite"/>：数组引用没变，<see cref="Value"/> setter 的
        /// <c>SetProperty</c> 会短路同引用，订阅方一个事件都收不到——画面上的数值不会刷新。
        /// </summary>
        public bool TryWriteElement(int index, object? value, out string? error)
        {
            error = null;

            if (Value is not Array array)
            {
                error = $"变量 [{Name}] 当前值不是数组，无法按下标写入";
                return false;
            }

            if (index < 0 || index >= array.Length)
            {
                error = $"下标 {index} 超出数组 [{Name}] 的长度 {array.Length}";
                return false;
            }

            Type? elementType = DataType.GetElementType() ?? array.GetType().GetElementType();

            if (elementType == null)
            {
                error = $"变量 [{Name}] 的数组元素类型未知，无法写入";
                return false;
            }

            if (!TryCoerceElement(value, elementType, out var converted, out error))
                return false;

            try
            {
                array.SetValue(converted, index);
            }
            catch (Exception ex)
            {
                error = $"变量 [{Name}] 的第 {index} 项写入失败：{ex.Message}";
                return false;
            }

            // 引用没变，必须手动广播（见 IValueChangeNotifier 的注释）
            NotifyValueChanged();
            return true;
        }

        /// <summary>
        /// 元素值归一：文本按元素类型解析（复用 <see cref="VariableValueConverter"/> 的宽进严出规则），
        /// 已是目标类型的值直通，其余按不变区域性转一次。
        /// </summary>
        private static bool TryCoerceElement(object? value, Type elementType, out object? converted, out string? error)
        {
            converted = null;
            error = null;

            if (value == null)
            {
                if (elementType.IsValueType && Nullable.GetUnderlyingType(elementType) == null)
                {
                    error = $"数组元素类型 {elementType.Name} 不接受空值";
                    return false;
                }

                return true;
            }

            if (elementType.IsInstanceOfType(value))
            {
                converted = value;
                return true;
            }

            if (value is string text)
                return VariableValueConverter.TryConvert(text, elementType, out converted, out error);

            try
            {
                converted = Convert.ChangeType(value, elementType, CultureInfo.InvariantCulture);
                return true;
            }
            catch (Exception ex)
            {
                error = $"「{value}」无法转换为数组元素类型 {elementType.Name}：{ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// 重置为默认值。
        ///
        /// 先放开本变量持有的副本：默认值**不是我们拷的**（<see cref="DefaultValue"/> 可能是
        /// 新建变量时 <c>Activator.CreateInstance(typeof(HImage))</c> 出来的空图实例），
        /// 所以这里只释放自己那份，绝不碰 DefaultValue——否则"恢复初始值"会把已释放的对象
        /// 重新放回模型（功能上等价于空图，但正是本仓明令避免的状态）。
        /// </summary>
        public void ResetToDefault()
        {
            HImage? current;
            lock (_imageGate)
            {
                current = _ownedImage;
                _ownedImage = null;
            }

            if (DefaultValue is Array arr)
            {
                Value = arr.Clone();
            }
            else
            {
                Value = DefaultValue;
            }

            DisposeQuietly(current);
        }

        /// <summary>
        /// 就地改数组元素后补一次值变更广播（见 <see cref="IValueChangeNotifier"/>）。
        /// 引用没变，<see cref="Value"/> setter 的 SetProperty 不会触发，只能显式广播。
        /// </summary>
        public void NotifyValueChanged() => _valueChanged?.Invoke(this, EventArgs.Empty);

        #region IVariableDisplaySource（HMI 画布组态预留，显式实现避免与模型接口冲突）

        string IVariableDisplaySource.BindingKey => Name;

        string IVariableDisplaySource.DisplayLabel => string.IsNullOrEmpty(Description) ? Name : Description;

        Type IVariableDisplaySource.DataType => DataType;

        object? IVariableDisplaySource.Value => Value;

        string IVariableDisplaySource.SourceLabel => "本地";

        bool IVariableDisplaySource.IsReadOnly => false; // 本地变量内存读写，无只读场景

        void IVariableDisplaySource.WriteValue(object? newValue) => Value = newValue;

        #endregion
    }




}
