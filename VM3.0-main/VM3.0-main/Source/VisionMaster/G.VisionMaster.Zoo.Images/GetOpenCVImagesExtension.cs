using System;

namespace G.VisionMaster.Zoo.Images;

public class GetOpenCVImagesExtension : System.Windows.Markup.MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return OpenCVImages.GetImageSources();
    }
}
