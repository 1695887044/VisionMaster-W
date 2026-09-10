using System;
using System.ComponentModel;
using Prism.Mvvm;
using UI.Attributes;

namespace VisionMaster.Communications
{

    public enum AlarmLevel
    {
        /// <summary>数据在正常范围内</summary>
        Normal,

        /// <summary>上上限报警（红色），数据过高</summary>
        HighHigh,

        /// <summary>上限警告（橙色），数据偏高</summary>
        High,

        /// <summary>下限警告（橙色），数据偏低</summary>
        Low,

        /// <summary>下下限报警（红色），数据过低</summary>
        LowLow
    }


    public class DataPointAlarm : BindableBase
    {
        #region 私有字段

        /// <summary>是否启用报警</summary>
        private bool _isEnabled = false;

        /// <summary>上上限值</summary>
        private double _highHigh = double.NaN;

        /// <summary>上限值</summary>
        private double _high = double.NaN;

        /// <summary>下限值</summary>
        private double _low = double.NaN;

        /// <summary>下下限值</summary>
        private double _lowLow = double.NaN;

        /// <summary>是否启用上上限报警</summary>
        private bool _highHighEnabled = false;

        /// <summary>是否启用上限报警</summary>
        private bool _highEnabled = false;

        /// <summary>是否启用下限报警</summary>
        private bool _lowEnabled = false;

        /// <summary>是否启用下下限报警</summary>
        private bool _lowLowEnabled = false;

        #endregion

        #region 启用设置

        /// <summary>
        /// <para>获取或设置是否启用报警功能。</para>
        /// <para>当设置为false时，Check方法始终返回Normal。</para>
        /// </summary>
        /// <value>启用返回true，否则返回false</value>
        [Category("报警配置"), SuperDisplay(Name = "启用报警")]
        public bool IsEnabled
        {
            get => _isEnabled;
            set => SetProperty(ref _isEnabled, value);
        }

        #endregion

        #region 上限报警

        /// <summary>
        /// <para>获取或设置是否启用上上限报警。</para>
        /// <para>上上限是最高级别的报警阈值，用于保护设备安全。</para>
        /// </summary>
        /// <value>启用返回true</value>
        /// <remarks>
        /// <para>典型应用场景：</para>
        /// <list type="bullet">
        ///   <item>温度超过设备允许的最高温度</item>
        ///   <item>压力超过安全阀值</item>
        ///   <item>电机电流异常升高</item>
        /// </list>
        /// </remarks>
        [Category("上限报警"), SuperDisplay(Name = "启用上上限")]
        public bool HighHighEnabled
        {
            get => _highHighEnabled;
            set => SetProperty(ref _highHighEnabled, value);
        }

        /// <summary>
        /// <para>获取或设置上上限阈值。</para>
        /// <para>当数据值大于或等于此值时触发报警。</para>
        /// </summary>
        /// <value>上上限阈值</value>
        /// <remarks>
        /// <para>设置注意事项：</para>
        /// <list type="bullet">
        ///   <item>应大于High（上限）值</item>
        ///   <item>应小于设备的绝对最大允许值</item>
        ///   <item>应考虑测量误差和波动</item>
        /// </list>
        /// </remarks>
        [Category("上限报警"), SuperDisplay(Name = "上上限值")]
        public double HighHigh
        {
            get => _highHigh;
            set => SetProperty(ref _highHigh, value);
        }

        /// <summary>
        /// <para>获取或设置是否启用上限报警。</para>
        /// <para>上限报警用于提醒操作人员注意设备运行状态。</para>
        /// </summary>
        /// <value>启用返回true</value>
        [Category("上限报警"), SuperDisplay(Name = "启用上限")]
        public bool HighEnabled
        {
            get => _highEnabled;
            set => SetProperty(ref _highEnabled, value);
        }

        /// <summary>
        /// <para>获取或设置上限阈值。</para>
        /// <para>当数据值大于或等于此值时触发警告。</para>
        /// </summary>
        /// <value>上限阈值</value>
        /// <remarks>
        /// <para>应大于Low（下限）值。</para>
        /// </remarks>
        [Category("上限报警"), SuperDisplay(Name = "上限值")]
        public double High
        {
            get => _high;
            set => SetProperty(ref _high, value);
        }

        #endregion

        #region 下限报警

        /// <summary>
        /// <para>获取或设置是否启用下限报警。</para>
        /// <para>下限报警用于提醒数据可能过低。</para>
        /// </summary>
        /// <value>启用返回true</value>
        [Category("下限报警"), SuperDisplay(Name = "启用下限")]
        public bool LowEnabled
        {
            get => _lowEnabled;
            set => SetProperty(ref _lowEnabled, value);
        }

        /// <summary>
        /// <para>获取或设置下限阈值。</para>
        /// <para>当数据值小于或等于此值时触发警告。</para>
        /// </summary>
        /// <value>下限阈值</value>
        /// <remarks>
        /// <para>应小于High（上限）值。</para>
        /// </remarks>
        [Category("下限报警"), SuperDisplay(Name = "下限值")]
        public double Low
        {
            get => _low;
            set => SetProperty(ref _low, value);
        }

        /// <summary>
        /// <para>获取或设置是否启用下下限报警。</para>
        /// <para>下下限是最低级别的报警阈值。</para>
        /// </summary>
        /// <value>启用返回true</value>
        [Category("下限报警"), SuperDisplay(Name = "启用下下限")]
        public bool LowLowEnabled
        {
            get => _lowLowEnabled;
            set => SetProperty(ref _lowLowEnabled, value);
        }

        /// <summary>
        /// <para>获取或设置下下限阈值。</para>
        /// <para>当数据值小于或等于此值时触发报警。</para>
        /// </summary>
        /// <value>下下限阈值</value>
        /// <remarks>
        /// <para>应小于Low（下限）值。</para>
        /// </remarks>
        [Category("下限报警"), SuperDisplay(Name = "下下限值")]
        public double LowLow
        {
            get => _lowLow;
            set => SetProperty(ref _lowLow, value);
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// <para>检查给定值是否触发报警。</para>
        /// </summary>
        /// <param name="value">要检查的数值</param>
        /// <returns>报警级别</returns>
        /// <remarks>
        /// <para>检查优先级（从高到低）：</para>
        /// <list type="number">
        ///   <item>HighHigh（上上限）</item>
        ///   <item>High（上限）</item>
        ///   <item>Low（下限）</item>
        ///   <item>LowLow（下下限）</item>
        /// </list>
        /// </remarks>
        /// <example>
        /// <code>
        /// var alarmLevel = alarm.Check(currentTemperature);
        /// switch (alarmLevel)
        /// {
        ///     case AlarmLevel.HighHigh:
        ///         // 紧急处理
        ///         EmergencyStop();
        ///         break;
        ///     case AlarmLevel.High:
        ///         // 警告
        ///         ShowWarning();
        ///         break;
        /// }
        /// </code>
        /// </example>
        public AlarmLevel Check(double value)
        {
            // 如果未启用，直接返回正常
            if (!IsEnabled) return AlarmLevel.Normal;

            // 按优先级检查
            if (HighHighEnabled && !double.IsNaN(HighHigh) && value >= HighHigh)
                return AlarmLevel.HighHigh;

            if (HighEnabled && !double.IsNaN(High) && value >= High)
                return AlarmLevel.High;

            if (LowEnabled && !double.IsNaN(Low) && value <= Low)
                return AlarmLevel.Low;

            if (LowLowEnabled && !double.IsNaN(LowLow) && value <= LowLow)
                return AlarmLevel.LowLow;

            return AlarmLevel.Normal;
        }

        /// <summary>
        /// <para>检查给定值是否触发报警（对象重载）。</para>
        /// </summary>
        /// <param name="value">要检查的值（会被转换为double）</param>
        /// <returns>报警级别</returns>
        /// <exception cref="FormatException">
        /// 当value无法转换为double时抛出。
        /// </exception>
        public AlarmLevel Check(object? value)
        {
            if (value == null) return AlarmLevel.Normal;

            try
            {
                double dValue = Convert.ToDouble(value);
                return Check(dValue);
            }
            catch
            {
                return AlarmLevel.Normal;
            }
        }

        /// <summary>
        /// <para>获取给定值的报警消息。</para>
        /// </summary>
        /// <param name="value">当前值</param>
        /// <returns>
        /// <para>如果触发报警返回描述性消息。</para>
        /// <para>如果未触发报警返回空字符串。</para>
        /// </returns>
        /// <example>
        /// <code>
        /// var message = alarm.GetAlarmMessage(105);
        /// // 返回: "上上限报警: 105 >= 100"
        /// </code>
        /// </example>
        public string GetAlarmMessage(double value)
        {
            var level = Check(value);

            return level switch
            {
                AlarmLevel.HighHigh => $"上上限报警: {value} >= {HighHigh}",
                AlarmLevel.High => $"上限报警: {value} >= {High}",
                AlarmLevel.Low => $"下限报警: {value} <= {Low}",
                AlarmLevel.LowLow => $"下下限报警: {value} <= {LowLow}",
                _ => string.Empty
            };
        }

        /// <summary>
        /// <para>获取当前配置的摘要信息。</para>
        /// </summary>
        /// <returns>配置摘要字符串</returns>
        public string GetSummary()
        {
            var parts = new List<string>();

            if (IsEnabled)
            {
                parts.Add("报警已启用");
            }
            else
            {
                parts.Add("报警已禁用");
                return string.Join(", ", parts);
            }

            if (HighHighEnabled)
                parts.Add($"HH={HighHigh}");
            if (HighEnabled)
                parts.Add($"H={High}");
            if (LowEnabled)
                parts.Add($"L={Low}");
            if (LowLowEnabled)
                parts.Add($"LL={LowLow}");

            return string.Join(", ", parts);
        }

        #endregion
    }

    /// <summary>
    /// <para>数据点报警事件参数。</para>
    /// </summary>
    public class DataPointAlarmEventArgs : EventArgs
    {
        /// <summary>
        /// <para>数据点的名称。</para>
        /// </summary>
        public string DataPointName { get; set; } = "";

        /// <summary>
        /// <para>所属连接的名称。</para>
        /// </summary>
        public string ConnectionName { get; set; } = "";

        /// <summary>
        /// <para>触发的报警级别。</para>
        /// </summary>
        public AlarmLevel Level { get; set; }

        /// <summary>
        /// <para>触发报警时的数据值。</para>
        /// </summary>
        public double Value { get; set; }

        /// <summary>
        /// <para>报警消息描述。</para>
        /// </summary>
        public string Message { get; set; } = "";

        /// <summary>
        /// <para>报警发生的时间戳。</para>
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }
}
