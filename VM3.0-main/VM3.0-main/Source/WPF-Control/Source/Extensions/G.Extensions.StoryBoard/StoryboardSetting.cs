using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace G.Extensions.StoryBoard;

public static class StoryboardSetting
{
    [DefaultValue(20)]
    [Range(0, 60)]
    public static int DesiredFrameRate { get; set; } = 25;
}
