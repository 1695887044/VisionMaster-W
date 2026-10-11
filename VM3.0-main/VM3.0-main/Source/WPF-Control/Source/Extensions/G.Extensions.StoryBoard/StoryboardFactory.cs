using System.Windows.Media.Animation;

namespace G.Extensions.StoryBoard;

public static class StoryboardFactory
{
    public static Storyboard Create()
    {
        Storyboard storyboard = new Storyboard();
        Timeline.SetDesiredFrameRate(storyboard, StoryboardSetting.DesiredFrameRate);
        return storyboard;
    }

    public static DoubleAnimation CreateDoubleAnimation()
    {
        DoubleAnimation storyboard = new DoubleAnimation();
        Timeline.SetDesiredFrameRate(storyboard, StoryboardSetting.DesiredFrameRate);
        return storyboard;
    }
}
