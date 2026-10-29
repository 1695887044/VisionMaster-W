using System;
using VisionMaster.Scada;

namespace VisionMaster.Services
{
    /// <summary>
    /// 报警静音出口：把画面上的「静音」动作接到宿主的报警声音服务上。
    ///
    /// 三条必须遵守的口径（都是 <see cref="ScadaSoundAlarmService"/> 的真实行为，不是猜测）
    /// ---------
    /// ① <c>Mute()</c> <b>没有参数</b>：它读的是服务的 <c>MuteMinutes</c> 属性。所以这里要
    ///   "先改时长、再 Mute()、再把时长写回"——写回不影响本次静音（截止时间在 Mute() 里已经算好），
    ///   但能避免"动作静音 30 分钟"把面板上那个静音按钮的配置时长也一起改掉。
    /// ② <c>Mute()</c> 返回 false 只有一种原因：存在未确认的<b>紧急</b>报警（紧急不可静音）。
    ///   这不是程序故障，是业务拒绝，原因要写成人话。
    /// ③ 声音服务是<b>每次运行新建</b>的（<c>ScadaMonitoringHost</c> 持有、停会话即 Dispose），
    ///   而分发器是应用级单例——所以<b>每次调用都要现取</b>，绝不能缓存实例，
    ///   否则重开运行后静音的是已经 Dispose 掉的那一个（表现为"静音了但还在响"，且不报错）。
    /// </summary>
    public sealed class ScadaAlarmMuteAdapter : IScadaAlarmMute
    {
        private readonly ScadaMonitoringHost _monitoring;

        public ScadaAlarmMuteAdapter(ScadaMonitoringHost monitoring)
            => _monitoring = monitoring ?? throw new ArgumentNullException(nameof(monitoring));

        public bool IsMuted => _monitoring.SoundAlarm?.IsMuted ?? false;

        public int MuteMinutes => _monitoring.SoundAlarm?.MuteMinutes ?? 0;

        public bool TryMute(int minutes, out string? error)
        {
            error = null;

            var service = _monitoring.SoundAlarm;
            if (service == null)
            {
                error = "运行态未启动（报警声音未启用）";
                return false;
            }

            var previous = service.MuteMinutes;
            try
            {
                service.MuteMinutes = minutes;
                if (service.Mute())
                    return true;

                error = "当前有紧急级未确认报警，紧急报警不可静音";
                return false;
            }
            catch (Exception ex)
            {
                error = $"静音失败：{ex.Message}";
                return false;
            }
            finally
            {
                // 恢复"配置时长"，让面板上那个静音按钮保持用户原来的设置
                try { service.MuteMinutes = previous; } catch { /* 恢复失败不影响本次静音 */ }
            }
        }

        public bool TryUnmute(out string? error)
        {
            error = null;

            var service = _monitoring.SoundAlarm;
            if (service == null)
            {
                error = "运行态未启动（报警声音未启用）";
                return false;
            }

            try
            {
                service.Unmute();
                return true;
            }
            catch (Exception ex)
            {
                error = $"取消静音失败：{ex.Message}";
                return false;
            }
        }
    }
}
