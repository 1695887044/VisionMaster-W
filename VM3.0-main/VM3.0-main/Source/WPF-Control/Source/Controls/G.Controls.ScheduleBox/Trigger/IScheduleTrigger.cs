using Quartz;

namespace G.Controls.ScheduleBox
{
    public interface IScheduleTrigger
    {

        ITrigger Build(IScheduleJob scheduleJob);
    }
}
