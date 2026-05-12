using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VisionMaster.Services;

namespace VisionMaster.Models
{
    public  class FlowSession : BindableBase
    {
        // 每个流程的唯一 ID
        public string SessionID { get; } = Guid.NewGuid().ToString("N");

        public string FlowName
        {
            get { return field; }
            set {SetProperty(ref field, value); }
        }
        public bool IsRunning
        {
            get { return field; }
            set { SetProperty(ref field, value); }
        }
        public SessionState State
        {
            get { return field; }
            set { SetProperty(ref field, value); }
        }
        public int CompiledVersion { get; set; }
        public ObservableCollection<StepModel> Blueprints { get; } = new();

        public ManualResetEventSlim PauseLock { get; } = new ManualResetEventSlim(true);
        public CompiledFlow ExecutionEngine { get; set; }

        public CancellationTokenSource CancellationTokenSource { get; set; }
    }
}
