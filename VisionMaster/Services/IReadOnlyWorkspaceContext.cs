using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VisionMaster.Models;

namespace VisionMaster.Services
{
    public interface IReadOnlyWorkspaceContext
    {
        SolutionModel CurrentSolution { get; }
        FlowModel CurrentFlow { get; }
        StepModel CurrentStep { get; }
    }

    public interface IWorkspaceManager : IReadOnlyWorkspaceContext
    {
        public List<GlobalVariableModel> GlobalVariables { get; set; }
        void SwitchSolution(SolutionModel solution);
        void SwitchFlow(FlowModel flow);
        void SwitchStep(StepModel step);
    }

    public class WorkspaceContext : BindableBase, IWorkspaceManager
    {
        public List<GlobalVariableModel> GlobalVariables { get; set; } = new();
        private SolutionModel _currentSolution;
        public SolutionModel CurrentSolution => _currentSolution; // 实现只读属性

        private FlowModel _currentFlow;
        public FlowModel CurrentFlow => _currentFlow;

        private StepModel _currentStep;
        public StepModel CurrentStep => _currentStep;

        public WorkspaceContext()
        {
            InitializeCommonVariables();
        }

        public void SwitchSolution(SolutionModel solution)
        {
            SetProperty(ref _currentSolution, solution, nameof(CurrentSolution));
            SwitchFlow(null);
        }

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
        private bool ContainsStepRecursively(IEnumerable<StepModel> steps, StepModel targetStep)
        {
            if (steps == null) return false;

            foreach (var step in steps)
            {
                // 1. 表面找到了，直接返回 true
                if (step == targetStep)
                    return true;

                // 2. 如果当前算子是个容器，钻进去找
                if (step is ConditionStep conditionStep && conditionStep.Children != null)
                {
                    foreach (var branch in conditionStep.Children)
                    {
                        // 递归调用自己，去分支的 Steps 里找
                        if (ContainsStepRecursively(branch.Steps, targetStep))
                        {
                            return true;
                        }
                    }
                }
            }

            // 所有的表面和深层都找过了，还是没有
            return false;
        }
        public void InitializeCommonVariables()
        {
            GlobalVariables = new List<GlobalVariableModel>
            {
                // ================== 1. 生产与配方 (String) ==================
                new GlobalVariableModel
                {
                    Name = "RecipeName",
                    DataType = typeof(string),
                    Description = "当前加载的产品模型配方名称",
                    DefaultValue = "Product_Type_A",
                    Value = "Product_Type_A",
                },
                new GlobalVariableModel
                {
                    Name = "ProductCode",
                    DataType = typeof(string),
                    Description = "当前识别到的条码或二维码信息",
                    DefaultValue = "",
                    Value = "QR202310240001",
                },
                // ================== 2. 统计数据 (Int32) ==================
                new GlobalVariableModel
                {
                    Name = "TotalCount",
                    DataType = typeof(int),
                    Description = "设备运行以来的累计生产总数",
                    DefaultValue = 0,
                    Value = 1500,
                },
                new GlobalVariableModel
                {
                    Name = "OKCount",
                    DataType = typeof(int),
                    Description = "累计检测良品总数",
                    DefaultValue = 0,
                    Value = 1485,
                },
                new GlobalVariableModel
                {
                    Name = "NGCount",
                    DataType = typeof(int),
                    Description = "累计检测不良品总数",
                    DefaultValue = 0,
                    Value = 15,
                },
                // ================== 3. 视觉参数与阈值 (Double) ==================
                new GlobalVariableModel
                {
                    Name = "ScoreThreshold",
                    DataType = typeof(double),
                    Description = "模板匹配的最小及格分数 (0-100)",
                    DefaultValue = 80.0,
                    Value = 85.5,
                },
                new GlobalVariableModel
                {
                    Name = "Exposure_Time",
                    DataType = typeof(double),
                    Description = "主相机的曝光时间 (ms)",
                    DefaultValue = 20.0,
                    Value = 25.0,
                },
                new GlobalVariableModel
                {
                    Name = "YieldRate",
                    DataType = typeof(double),
                    Description = "当前的实时良率 (%)",
                    DefaultValue = 100.0,
                    Value = 99.0,
                },
                // ================== 4. 系统状态与 IO (Boolean) ==================
                new GlobalVariableModel
                {
                    Name = "IsSystemAuto",
                    DataType = typeof(bool),
                    Description = "系统是否处于自动运行模式",
                    DefaultValue = false,
                    Value = true,
                },
                new GlobalVariableModel
                {
                    Name = "SafetyDoorState",
                    DataType = typeof(bool),
                    Description = "安全门限位开关状态 (True为关闭)",
                    DefaultValue = true,
                    Value = true,
                },
                new GlobalVariableModel
                {
                    Name = "PLC_Ready",
                    DataType = typeof(bool),
                    Description = "外部PLC通讯握手信号",
                    DefaultValue = false,
                    Value = true,
                },
                // ================== 5. 机械坐标补偿 (Double) ==================
                new GlobalVariableModel
                {
                    Name = "OffsetX",
                    DataType = typeof(double),
                    Description = "机械手 X 轴位置补偿量 (mm)",
                    DefaultValue = 0.0,
                    Value = 1.25,
                },
                new GlobalVariableModel
                {
                    Name = "OffsetY",
                    DataType = typeof(double),
                    Description = "机械手 Y 轴位置补偿量 (mm)",
                    DefaultValue = 0.0,
                    Value = -0.42,
                },
                new GlobalVariableModel
                {
                    Name = "BarcodeResults",
                    DataType = typeof(string[]),
                    Description = "单次触发读取到的所有条码集合",
                    DefaultValue = new string[0],
                    Value = new string[] { "SN2026-A01", "SN2026-A02", "SN2026-B01" },
                },
                new GlobalVariableModel
                {
                    Name = "HoleCoordinatesX",
                    DataType = typeof(double[]),
                    Description = "所有定位孔的 X 坐标集合 (mm)",
                    DefaultValue = new double[0],
                    Value = new double[] { 12.5, 45.2, 88.9, 120.0 },
                },
                new GlobalVariableModel
                {
                    Name = "DefectAreas",
                    DataType = typeof(double[]),
                    Description = "表面检测发现的瑕疵面积列表 (px²)",
                    DefaultValue = new double[0],
                    Value = new double[] { 150.5, 45.2, 12.0 },
                },
                new GlobalVariableModel
                {
                    Name = "CameraROI",
                    DataType = typeof(int[]),
                    Description = "相机的动态检测区域 [X, Y, Width, Height]",
                    DefaultValue = new int[] { 0, 0, 1920, 1080 },
                    Value = new int[] { 100, 100, 800, 600 },
                },
            };
        }
    }
}
