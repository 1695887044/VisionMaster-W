using EventMgrLib;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VM.Start.Attributes;
using VM.Start.Common.Enums;
using VM.Start.Common.Provide;
using VM.Start.Core;
using VM.Start.Events;
using VM.Start.ViewModels;

namespace Plugin.WaitLaserFinish.ViewModels
{
    [Category("激光工具")]
    [DisplayName("等待激光结束")]
    [ModuleImageName("Delay")]
    [Serializable]
    public class WaitLaserFinishViewModel : ModuleBase
    {
        [NonSerialized]
        private AutoResetEvent m_finishLaserSignal = new AutoResetEvent(false);

        public override bool ExeModule()
        {
            Stopwatch.Restart();
            try
            {
                if (m_finishLaserSignal == null)
                    m_finishLaserSignal = new AutoResetEvent(false);
                m_finishLaserSignal.Reset();
                m_finishLaserSignal.WaitOne();
                ChangeModuleRunStatus(eRunStatus.OK);
                return true;
            }
            catch (Exception ex)
            {
                ChangeModuleRunStatus(eRunStatus.NG);
                Logger.GetExceptionMsg(ex);
                return false;
            }
        }

        public ObservableCollection<ILaserDevice> LaserModels { get; set; } =
            LaserSetViewModel.Ins.LaserModels;

        ILaserDevice _SelectedLaserModel;

        public ILaserDevice SelectedLaserModel
        {
            get => _SelectedLaserModel;
            set
            {
                _SelectedLaserModel = value;
                _SelectedLaserModel.MarkEnd -= OnLaserMarkEnd;
                _SelectedLaserModel.MarkEnd += OnLaserMarkEnd;
                RaisePropertyChanged();
            }
        }

        public WaitLaserFinishViewModel()
        {
            EventMgr.Ins.GetEvent<ProjStopEvent>().Subscribe(OnProjStop, true);
        }

        private void OnProjStop()
        {
            if (SelectedLaserModel != null && SelectedLaserModel.IsMarking)
            {
                StopWaitLaserFinsihSignal();
                ChangeModuleRunStatus(eRunStatus.NG);
            }
        }

        /// <summary>
        /// 激光结束
        /// </summary>
        /// <param name="obj"></param>
        private void OnLaserMarkEnd(int obj)
        {
            if (m_finishLaserSignal == null)
                m_finishLaserSignal = new AutoResetEvent(false);
            m_finishLaserSignal.Set(); //停止阻塞
        }

        /// <summary>
        /// 停止等待激光结束信号
        /// </summary>
        public void StopWaitLaserFinsihSignal()
        {
            lock (this)
            {
                if (m_finishLaserSignal == null)
                    m_finishLaserSignal = new AutoResetEvent(false);
                m_finishLaserSignal.Set(); //停止阻塞 当项目停止的时候 停止阻塞
            }
        }
    }
}
