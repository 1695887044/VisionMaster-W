//namespace G.Controls.ScheduleBox
//{
//    public class JobDetail : SelectViewModel<IJobDetail>
//    {
//        public JobDetail(IJobDetail jobDetail) : base(jobDetail)
//        {
//            this.Job = Activator.CreateInstance(jobDetail.JobType) as IJob;
//        }

//        private ObservableCollection<ITrigger> _triggers = new ObservableCollection<ITrigger>();
//        /// <summary> 说明  </summary>
//        public ObservableCollection<ITrigger> Triggers
//        {
//            get { return _triggers; }
//            set
//            {
//                _triggers = value;
//                RaisePropertyChanged();
//            }
//        }

//        private IJob _job;
//        /// <summary> 说明  </summary>
//        public IJob Job
//        {
//            get { return _job; }
//            set
//            {
//                _job = value;
//                RaisePropertyChanged();
//            }
//        }
//    }

//}
