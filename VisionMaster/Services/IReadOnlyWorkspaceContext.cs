﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Core.Events;
using VisionMaster.EventModel;
using VisionMaster.Models;

namespace VisionMaster.Services
{
    /// <summary>
    /// 只读工作区上下文接口
    /// 提供对当前工作区状态的只读访问
    /// </summary>
    public interface IReadOnlyWorkspaceContext
    {
        /// <summary>
        /// 当前方案
        /// </summary>
        SolutionModel CurrentSolution { get; }

        /// <summary>
        /// 当前流程
        /// </summary>
        FlowModel CurrentFlow { get; }

        /// <summary>
        /// 当前步骤
        /// </summary>
        StepModel CurrentStep { get; }

        /// <summary>
        /// 监视项集合（用于调试时查看变量值）
        /// </summary>
        ObservableCollection<WatchItemModel> WatchItems { get; }
    }

    /// <summary>
    /// 工作区管理器接口
    /// 继承自只读接口，增加状态切换能力
    /// </summary>
    public interface IWorkspaceManager : IReadOnlyWorkspaceContext
    {
        /// <summary>
        /// 全局变量集合
        /// </summary>
        public ObservableCollection<IVariable> GlobalVariables { get; set; }

        /// <summary>
        /// 切换当前方案
        /// </summary>
        void SwitchSolution(SolutionModel solution);

        /// <summary>
        /// 切换当前流程
        /// </summary>
        void SwitchFlow(FlowModel flow);

        /// <summary>
        /// 切换当前步骤
        /// </summary>
        void SwitchStep(StepModel step);
    }

    /// <summary>
    /// 工作区上下文实现类
    /// 管理方案、流程、步骤的切换，并维护全局变量
    /// </summary>
    public class WorkspaceContext : BindableBase, IWorkspaceManager
    {
        /// <summary>
        /// 全局变量集合
        /// </summary>
        public ObservableCollection<IVariable> GlobalVariables { get; set; } = new();

        private SolutionModel _currentSolution;
        /// <summary>
        /// 当前方案（只读）
        /// </summary>
        public SolutionModel CurrentSolution => _currentSolution;

        private FlowModel _currentFlow;
        /// <summary>
        /// 当前流程（只读）
        /// </summary>
        public FlowModel CurrentFlow => _currentFlow;

        private StepModel _currentStep;
        /// <summary>
        /// 当前步骤（只读）
        /// </summary>
        public StepModel CurrentStep => _currentStep;

        /// <summary>
        /// 监视项集合
        /// </summary>
        public ObservableCollection<WatchItemModel> WatchItems => CurrentSolution?.WatchItems;

        /// <summary>
        /// 初始化工作区上下文
        /// </summary>
        public WorkspaceContext()
        {
            InitializeCommonVariables();
            GlobalEventBus.Subscribe<StepRenamedMessage>(OnStepRenamed);
        }

        /// <summary>
        /// 切换当前方案
        /// </summary>
        public void SwitchSolution(SolutionModel solution)
        {
            SetProperty(ref _currentSolution, solution, nameof(CurrentSolution));
            SwitchFlow(null);
        }

        /// <summary>
        /// 切换当前流程
        /// </summary>
        public void SwitchFlow(FlowModel flow)
        {
            if (flow != null)
            {
                if (_currentSolution == null)
                    throw new InvalidOperationException("必须在当前 Solution 存在时才能设置 Flow");

                if (!_currentSolution.Flows.Contains(flow))
                    throw new InvalidOperationException("指定的 Flow 不属于当前 Solution");
            }
            SetProperty(ref _currentFlow, flow, nameof(CurrentFlow));
            SwitchStep(null);
        }

        /// <summary>
        /// 切换当前步骤
        /// </summary>
        public void SwitchStep(StepModel step)
        {
            if (step != null)
            {
                if (_currentFlow == null)
                    throw new InvalidOperationException("必须在当前 Flow 存在时才能设置 Step");

                if (!ContainsStepRecursively(_currentFlow.Steps, step))
                    throw new InvalidOperationException($"指定的 Step [{step.StepID}] 不属于当前 Flow，或已被删除！");
            }

            SetProperty(ref _currentStep, step, nameof(CurrentStep));
        }

        /// <summary>
        /// 处理步骤重命名事件
        /// 更新所有引用旧名称的连线地址和表达式
        /// </summary>
        private void OnStepRenamed(StepRenamedMessage args)
        {
            if (CurrentFlow == null) return;
            UpdateReferencesRecursively(CurrentFlow.Steps, args.OldName, args.NewName);
        }

        /// <summary>
        /// 递归更新步骤引用
        /// 更新连线地址和条件表达式中的步骤名称引用
        /// </summary>
        private void UpdateReferencesRecursively(IEnumerable<StepModel> steps, string oldName, string newName)
        {
            foreach (var step in steps)
            {
                var keysToUpdate = step.LinkedSources.Keys.ToList();
                foreach (var key in keysToUpdate)
                {
                    var linkedAddress = step.LinkedSources[key];

                    if (linkedAddress != null && linkedAddress.DisplayAddress.StartsWith(oldName + "."))
                    {
                        step.LinkedSources[key].DisplayAddress = linkedAddress.DisplayAddress.Replace(oldName + ".", newName + ".");
                    }
                }

                if (step is ConditionStep conditionNode)
                {
                    foreach (var branch in conditionNode.Children)
                    {
                        if (!string.IsNullOrWhiteSpace(branch.Expression))
                        {
                            string pattern = $@"\b{Regex.Escape(oldName)}\b";
                            branch.Expression = Regex.Replace(branch.Expression, pattern, newName);
                        }

                        UpdateReferencesRecursively(branch.Steps, oldName, newName);
                    }
                }
            }
        }

        /// <summary>
        /// 递归检查步骤是否在步骤集合中（包括嵌套容器）
        /// </summary>
        private bool ContainsStepRecursively(IEnumerable<StepModel> steps, StepModel targetStep)
        {
            if (steps == null) return false;

            foreach (var step in steps)
            {
                if (step == targetStep)
                    return true;
                if (step is IContainerStep container && container.Children != null)
                {
                    foreach (var branch in container.Children)
                    {
                        if (ContainsStepRecursively(branch.Steps, targetStep))
                            return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// 初始化常用全局变量
        /// </summary>
        public void InitializeCommonVariables()
        {
            GlobalVariables = new ObservableCollection<IVariable>
            {
                VariableFactory.CreateLocal("RecipeName", typeof(string), "当前加载的产品模型配方名称", "Product_Type_A"),
                VariableFactory.CreateLocal("ProductCode", typeof(string),  "当前识别到的条码或二维码信息", "QR202310240001"),
                VariableFactory.CreateLocal("TotalCount", typeof(int),"设备运行以来的累计生产总数", 1500),
                VariableFactory.CreateLocal("OKCount", typeof(int),"累计检测良品总数", 1485),
                VariableFactory.CreateLocal("NGCount", typeof(int),"累计检测不良品总数", 15),
                VariableFactory.CreateLocal("ScoreThreshold", typeof(double),"模板匹配的最小及格分数 (0-100)", 85.5),
                VariableFactory.CreateLocal("Exposure_Time", typeof(double),"主相机的曝光时间 (ms)", 25.0),
                VariableFactory.CreateLocal("YieldRate", typeof(double), "当前的实时良率 (%)",99.0),
                VariableFactory.CreateLocal("PLC_Ready", typeof(bool), "外部PLC通讯握手信号", true),
                VariableFactory.CreateLocal("BarcodeResults", typeof(string[]), "单次触发读取到的所有条码集合",new string[] { "SN2026-A01", "SN2026-A02", "SN2026-B01" }),
                VariableFactory.CreateLocal("HoleCoordinatesX", typeof(double[]),"所有定位孔的 X 坐标集合 (mm)", new double[] { 12.5, 45.2, 88.9, 120.0 }),
                VariableFactory.CreateLocal("CameraROI", typeof(int[]),"相机的动态检测区域 [X, Y, Width, Height]", new int[] { 100, 100, 800, 600 }),
            };
        }
    }
}
