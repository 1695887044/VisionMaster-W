

namespace Core.Interfaces
{
    public interface IPort
    {
        string Name { get; } // 参数名称，如 "InputImage", "ScoreThreshold"
        Type DataType { get; } // 数据类型，如 typeof(HObject), typeof(double)
        object Value { get; set; } // 实际的参数值

        string Description { get; set; }
    }
    public interface IOutputPort : IPort
    {
        event EventHandler ValueChanged;
    }
    public interface IInputPort : IPort
    {
        bool IsRequired { get; set; }// 是否为必填项，编译时会检查未绑定的必填端口
        IOutputPort LinkedSource { get; set; }

        object GetActualValue();
    }
}
