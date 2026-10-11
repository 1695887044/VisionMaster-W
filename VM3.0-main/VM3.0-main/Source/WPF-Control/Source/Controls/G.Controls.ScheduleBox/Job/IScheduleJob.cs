using Quartz;
using System.Collections.ObjectModel;

namespace G.Controls.ScheduleBox
{
    public interface IScheduleJob
    {
        string GroupName { get; set; }
        ObservableCollection<IScheduleTrigger> Triggers { get; }
        void Schedule(IScheduler scheduler);
        string Message { get; set; }
        bool IsRunning { get; set; }
        DateTime? NextFireTime { get; set; }
    }
}
