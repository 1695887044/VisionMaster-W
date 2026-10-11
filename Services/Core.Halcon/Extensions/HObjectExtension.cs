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
        /// <summary>
        /// ⚠ 仅适用于**灰度（单通道）**图：内部走 GetImagePointer1，对多通道图只取通道 1
        /// （且是取指针而非 AccessChannel 的引用计数语义）。彩色图请用 HImage.GetImageSize。
        /// （2026-10-08 审查补注；当前全仓只有 UIThemeSmokeTest 在用，均为灰度场景）
        /// </summary>
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
        /// <summary>
        /// ⚠ 仅适用于**灰度（单通道）**图：内部走 GetImagePointer1，对多通道图只取通道 1。
        /// 彩色 HObject 转 HImage 请用 AccessChannel 逐通道构建。
        /// （2026-10-08 审查补注；当前全仓只有 UIThemeSmokeTest 在用，均为灰度场景）
        /// </summary>
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
