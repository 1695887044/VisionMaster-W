using HalconDotNet;

namespace Core.Halcon.Extensions
{
    public static class HObjectExtension
    {
        public static HObject ReduceDomain(this HObject image, HObject region)
        {
            HOperatorSet.ReduceDomain(image, region, out HObject template);
            return template;
        }

        public static HObject CropDomain(this HObject image)
        {
            HOperatorSet.CropDomain(image, out HObject template);
            return template;
        }

        public static HObject ReduceDomain(this HObject image, double x1, double y1, double x2, double y2)
        {
            HOperatorSet.GenRectangle1(out HObject rectangle, y1, x1, y2, x2);
            HOperatorSet.ReduceDomain(image, rectangle, out HObject template);
            return template;
        }
        public static HObject Rgb1ToGray(this HObject image)
        {
            HOperatorSet.Rgb1ToGray(image, out HObject ho_GrayImage);
            return ho_GrayImage;
        }
        public static int[] GetImageSize(this HObject image)
        {
            int width, height;
            HImage img = new HImage();
            HobjectToHimage(image, ref img);
            img.GetImageSize(out width, out height);
            return new int[] { width, height };

            static void HobjectToHimage(HObject hobject, ref HImage image)
            {
                using (HDevDisposeHelper dh = new HDevDisposeHelper())
                {
                    HTuple p, t, w, h;
                    HOperatorSet.GetImagePointer1(hobject, out p, out t, out w, out h);
                    image.GenImage1(t, w, h, p);
                }
            }
        }
        public static int[] GetImageSize(this HImage image)
        {
            int width, height;
            image.GetImageSize(out width, out height);
            return new int[] { width, height };
        }
        public static HImage ToHimage(this HObject hobject)
        {
            HImage img = new HImage();
            using (HDevDisposeHelper dh = new HDevDisposeHelper())
            {
                HTuple p, t, w, h;
                HOperatorSet.GetImagePointer1(hobject, out p, out t, out w, out h);
                img.GenImage1(t, w, h, p);
            }
            return img;
        }
    }
}
