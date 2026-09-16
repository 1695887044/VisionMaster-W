using Core.Interfaces;
using HalconDotNet;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using UI.Attributes;

namespace Plugin.PreProcessing.Models
{
    /// <summary>
    /// 图像预处理算子基类：一个算子 = 一组参数 + 一次"输入图 → 新图"的变换。
    ///
    /// 三条硬约定（新加算子的人只要照着抄就行）：
    /// 1. 参数属性一律贴 [SuperDisplay]，界面完全交给 FlatPropertyGrid 反射生成，插件不写一行参数控件代码；
    /// 2. 派生类只实现 Process()：输入图归调用方所有。要改图就返回一张"新"图；
    ///    本算子对当前输入无从下手（比如对灰度图做彩色转换）时，允许把入参原样返回表示"透传"，
    ///    上层靠引用比对识别，不会重复释放。唯独不允许在异常路径上偷偷返回入参 ——
    ///    参考实现就是这么干的，结果上层释放输出时顺手把上游的图也释放了；
    /// 3. 存盘走 "算子 Key + 参数字典(字符串)" 的弱类型快照，不写 CLR 类型名 ——
    ///    以后改类名、换命名空间、挪程序集，客户现场的旧方案照样能打开。
    /// </summary>
    public abstract class PreprocessOperator : ObservableObject
    {
        private const BindingFlags Flags = BindingFlags.Public | BindingFlags.Instance;

        /// <summary>每种算子类型的"可存盘参数属性"反射缓存（属性面板每次刷新都要用，不能反复反射）</summary>
        private static readonly ConcurrentDictionary<Type, PropertyInfo[]> ParamCache = new();

        private bool _enabled = true;

        protected PreprocessOperator()
        {
            var attr = GetType().GetCustomAttribute<PreprocessOperatorAttribute>();
            if (attr == null)
            {
                throw new InvalidOperationException(
                    $"{GetType().Name} 没有贴 [PreprocessOperator] 特性，无法作为算子使用");
            }
            Key = attr.Key;
            DisplayName = attr.DisplayName;
            Category = attr.Category;
            Description = attr.Description;
        }

        #region 元数据（来自类上的特性，不由用户提供，故不贴 [SuperDisplay]）

        /// <summary>存盘标识，来自 [PreprocessOperator]，永不随类名变化</summary>
        public string Key { get; }

        public string DisplayName { get; }

        public string Category { get; }

        public string Description { get; }

        #endregion

        #region 公共参数（所有算子都有）

        /// <summary>
        /// 是否启用。
        /// 关掉 = 图像原样流到下一个算子（透传）。
        /// 原参考实现这里有个坑：算子被禁用时不给输出赋值，下一个算子拿到的是"上上级"的陈旧图像，
        /// 现象就是"勾掉了某个算子但画面没恢复"，非常难查。透传语义在 PreProcessingPlugin 里统一保证。
        /// </summary>
        [SuperDisplay(Name = "启用", GroupPath = "算子", GroupOrder = "0", Order = 0,
            Description = "取消勾选后，图像原样流向下一个算子")]
        public bool Enabled
        {
            get => _enabled;
            set
            {
                if (SetProperty(ref _enabled, value))
                {
                    OnPropertyChanged(nameof(IsMuted));
                }
            }
        }

        /// <summary>列表项灰显用</summary>
        public bool IsMuted => !Enabled;

        #endregion

        #region 显示

        /// <summary>
        /// 参数摘要，显示在算子列表里（形如 "5 × 5"）。
        /// 取代原参考实现里那个几百行的 m_value 巨型 switch —— 那个写法每加一个算子都要改它，
        /// 而且靠 "m_value = \"\"" 这种赋值来骗界面刷新。
        /// </summary>
        public virtual string Summary => string.Empty;

        #endregion

        #region 执行

        /// <summary>
        /// 执行入口（上层只调用它）。输入图由调用方持有，本方法返回一张新图。
        /// </summary>
        public HImage Apply(HImage input)
        {
            if (input == null || !input.IsInitialized())
            {
                throw new InvalidOperationException($"{DisplayName}：输入图像为空或未初始化");
            }
            Normalize();
            return Process(input);
        }

        /// <summary>真正干活的地方，派生类实现</summary>
        protected abstract HImage Process(HImage input);

        /// <summary>
        /// 参数纠偏：把界面上手滑填出来的非法值收敛回合法区间。
        /// 原参考实现把 "3-11 奇数" 这类约束只写在注释里，运行时根本不校验，填个偶数直接让 Halcon 报错。
        /// </summary>
        protected virtual void Normalize()
        {
        }

        #endregion

        #region 存盘读写（反射，新增算子零改动）

        /// <summary>取某种算子类型的可存盘参数属性（不含基类的"启用"）</summary>
        internal static PropertyInfo[] GetParamProps(Type operatorType) =>
            ParamCache.GetOrAdd(operatorType, t =>
                t.GetProperties(Flags)
                    .Where(p => p.GetCustomAttribute<SuperDisplayAttribute>() != null
                                && p.CanRead && p.CanWrite
                                && p.DeclaringType != typeof(PreprocessOperator))
                    .ToArray());

        /// <summary>把当前参数导出成"属性名 → 字符串值"</summary>
        public Dictionary<string, string> SaveParams()
        {
            var result = new Dictionary<string, string>();
            foreach (var prop in GetParamProps(GetType()))
            {
                result[prop.Name] = ToText(prop.GetValue(this));
            }
            return result;
        }

        /// <summary>
        /// 从快照回填参数。单个参数解析失败只跳过该项、保留默认值，
        /// 绝不让一个脏字段把整份方案卡死打不开。
        /// </summary>
        public void LoadParams(IDictionary<string, string>? values)
        {
            if (values == null || values.Count == 0) return;
            foreach (var prop in GetParamProps(GetType()))
            {
                if (!values.TryGetValue(prop.Name, out var text)) continue;
                try
                {
                    prop.SetValue(this, FromText(text, prop.PropertyType));
                }
                catch
                {
                    // 宽容处理：类型不匹配就留默认值
                }
            }
            // 参数是反射灌进来的，不会触发 setter 里的通知，这里统一补一次
            OnPropertyChanged(string.Empty);
            OnPropertyChanged(nameof(Summary));
        }

        private static string ToText(object? value)
        {
            if (value == null) return string.Empty;
            if (value is bool b) return b ? "1" : "0";
            if (value is Enum e) return System.Convert.ToInt64(e).ToString(CultureInfo.InvariantCulture);
            if (value is double d) return d.ToString("R", CultureInfo.InvariantCulture);
            if (value is float f) return f.ToString("R", CultureInfo.InvariantCulture);
            if (value is decimal m) return m.ToString(CultureInfo.InvariantCulture);
            return System.Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        private static object? FromText(string text, Type target)
        {
            if (target == typeof(string)) return text;

            if (target.IsEnum)
            {
                // 存的是数字，兼容手工改成成员名的情况
                return int.TryParse(text, out var num)
                    ? System.Convert.ChangeType(num, Enum.GetUnderlyingType(target))
                    : Enum.Parse(target, text, true);
            }

            if (target == typeof(bool)) return text == "1" || bool.TryParse(text, out var bp) && bp;

            var underlying = Nullable.GetUnderlyingType(target) ?? target;
            return System.Convert.ChangeType(text, underlying, CultureInfo.InvariantCulture);
        }

        #endregion

        #region 派生类小工具

        /// <summary>收敛成正奇数（Halcon 的模板半径/尺寸大多要求奇数）</summary>
        protected static int OddAtLeast(int value, int min)
        {
            if (value < min) value = min;
            return value % 2 == 0 ? value + 1 : value;
        }

        /// <summary>收敛成正整数</summary>
        protected static int AtLeast(int value, int min) => value < min ? min : value;

        /// <summary>夹到 [min, max]</summary>
        protected static double Clamp(double value, double min, double max) =>
            value < min ? min : value > max ? max : value;

        /// <summary>参数赋值：除了通知本属性，还顺手刷新摘要文本，列表里的显示才会跟着变</summary>
        protected bool SetParam<T>(ref T storage, T value,
            [System.Runtime.CompilerServices.CallerMemberName] string propertyName = null!)
        {
            if (SetProperty(ref storage, value, propertyName))
            {
                OnPropertyChanged(nameof(Summary));
                return true;
            }
            return false;
        }

        /// <summary>
        /// 取枚举成员上的 [Description] 文本，用于拼参数摘要。
        /// 没贴 Description 就退回成员名。
        /// </summary>
        protected static string Desc(Enum value)
        {
            var name = value.ToString();
            var field = value.GetType().GetField(name);
            var attr = field?.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>();
            return attr?.Description ?? name;
        }

        #endregion
    }
}
