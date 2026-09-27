using System;

namespace Core.Interfaces
{
    /// <summary>
    /// 单个轴的运行状态快照（由驱动在轮询里刷新）。
    ///
    /// 为什么是"快照"而不是活的对象：轴状态是**每一帧都在变**的东西，
    /// 让界面/流程直接读卡（每次都发一次通信）会把带宽吃光，而且读到的是"每次都不一样的时刻"。
    /// 快照由轮询线程统一刷新，读的人拿到的是"最近一次采样的同一份数据"，
    /// 既省通信，也让同一次判断里的多个字段互相自洽。
    /// </summary>
    public class AxisStatus
    {
        /// <summary>物理轴号</summary>
        public int PhysicalIndex { get; set; }

        /// <summary>当前位置（mm，已按脉冲当量换算）</summary>
        public double PositionMm { get; set; }

        /// <summary>当前速度（mm/s；读不到的卡给 0，不要编）</summary>
        public double VelocityMmPerS { get; set; }

        /// <summary>是否已到位（卡侧 InPosition 信号）</summary>
        public bool InPosition { get; set; }

        /// <summary>是否正在运动（卡侧 Busy）</summary>
        public bool Moving { get; set; }

        /// <summary>是否已使能（伺服上电）</summary>
        public bool Enabled { get; set; }

        /// <summary>正限位被触发</summary>
        public bool PositiveLimit { get; set; }

        /// <summary>负限位被触发</summary>
        public bool NegativeLimit { get; set; }

        /// <summary>原点开关被触发</summary>
        public bool Origin { get; set; }

        /// <summary>急停输入被触发</summary>
        public bool EmergencyStop { get; set; }

        /// <summary>轴报警（伺服报警 / 跟随误差超限等，卡侧状态位的汇总）</summary>
        public bool Alarm { get; set; }

        /// <summary>报警说明（驱动给出的可读文案，如"伺服报警"）</summary>
        public string AlarmMessage { get; set; } = string.Empty;

        /// <summary>该快照的采样时刻</summary>
        public DateTime Timestamp { get; set; } = DateTime.Now;

        /// <summary>
        /// 是否有任何"必须立刻停"的信号（急停输入 / 限位 / 报警）。
        /// 抽成一个只读属性，是为了让"安全检查"只有一处判断 ——
        /// 各个调用点各写一遍 `Alarm || PositiveLimit || ...` 必然漏掉一两项。
        /// </summary>
        public bool HasCriticalSignal => EmergencyStop || PositiveLimit || NegativeLimit || Alarm;

        /// <summary>状态的一句话描述（日志/界面直接用）</summary>
        public string Describe()
        {
            if (Alarm) return $"报警：{AlarmMessage}";
            if (EmergencyStop) return "急停输入被触发";
            if (PositiveLimit) return "正限位被触发";
            if (NegativeLimit) return "负限位被触发";
            if (Moving) return $"运动中（{PositionMm:F3} mm）";
            if (!Enabled) return "未使能";
            return InPosition ? $"已到位（{PositionMm:F3} mm）" : $"{PositionMm:F3} mm";
        }
    }
}
