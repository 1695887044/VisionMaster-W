using System.Windows;
using System.Windows.Media.Animation;

namespace G.Extensions.StoryBoard.EasingFunction;

/// <summary> 指定次幂 </summary>
public class PowerEasingFunction : EasingFunctionBase
{
    public PowerEasingFunction()
        : base()
    {

    }

    public int Pow { get; set; } = 7;

    protected override double EaseInCore(double normalizedTime)
    {
        return Math.Pow(normalizedTime, this.Pow);
    }

    protected override Freezable CreateInstanceCore()
    {
        return new PowerEasingFunction();
    }
}
