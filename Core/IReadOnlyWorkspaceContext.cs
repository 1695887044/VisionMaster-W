﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Core.Events;
using Core.Interfaces;
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
        /// 处理步骤重命名：按 StepId 精确定位引用，重算连线显示地址。
        ///
        /// 相对旧实现的三点修正：
        /// 1. 遍历整个方案的所有流程，而不是只 CurrentFlow —— 跨流程引用同样需要更新；
        /// 2. 判据由「DisplayAddress 前缀等于旧名」改为「Kind==StepPort 且 TargetStepId==被改名步骤」，
        ///    彻底消除同名步骤误伤（步骤名在不同流程里可以合法重复）；
        /// 3. 不再改写分支条件表达式 —— 表达式里的标识符是用户自起的变量别名
        ///    （LocalVariableItem.Name），与步骤名无关。旧实现用 \b旧名\b 正则替换，
        ///    一旦别名恰好等于步骤名，就会把表达式改成编译不过的样子，属于自造语法错误。
        /// </summary>
        private void OnStepRenamed(StepRenamedMessage args)
        {
            if (args == null || CurrentSolution?.Flows == null) return;

            foreach (var flow in CurrentSolution.Flows)
                RefreshStepPortDisplayAddresses(flow.Steps, args);
        }

        /// <summary>
        /// 递归刷新引用了被改名步骤的连线显示地址（含所有嵌套容器分支）
        /// </summary>
        private static void RefreshStepPortDisplayAddresses(IEnumerable<StepModel> steps, StepRenamedMessage args)
        {
            foreach (var step in steps)
            {
                foreach (var link in step.LinkedSources.Values)
                {
                    if (link == null) continue;

                    // 只有真实步骤输出的地址串里含步骤名；
                    // 全局变量 / 运行时变量 / 常量的地址与步骤名无关，一律不碰。
                    // NormalizeKind 顺带把旧工程缺失的 Kind 补齐，避免按前缀误判
                    if (link.NormalizeKind() != LinkKind.StepPort) continue;
                    if (link.TargetStepId != args.StepId) continue;

                    link.DisplayAddress = $"{args.NewName}.{link.TargetPortName}";
                }

                if (step is IContainerStep container && container.Children != null)
                {
                    foreach (var branch in container.Children)
                        RefreshStepPortDisplayAddresses(branch.Steps, args);
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
