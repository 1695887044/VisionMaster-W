//using CPublicDefine;
using HalconDotNet;
using Plugin.PerProcessing.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VM.Start.Models;
//using VisionCore.core;

namespace Plugin.PerProcessing.ViewModels
{
    [Serializable]
    public  class PretreatHelp
    {
        #region 图像调整
        /// <summary>
        /// 彩色图转灰度图
        /// </summary>
        public void TransImage(HImage inImage, out HImage outImage, eTransImageType ImageType, eTransImageChannel Channel)
        {
            outImage = new HImage();
            HObject Temp1 = new HObject(); Temp1.GenEmptyObj(); Temp1.Dispose();
            HObject Temp2 = new HObject(); Temp2.GenEmptyObj(); Temp2.Dispose();
            HObject Temp3 = new HObject(); Temp3.GenEmptyObj(); Temp3.Dispose();
            HObject Image1 = new HObject(); Image1.GenEmptyObj(); Image1.Dispose();
            HObject Image2 = new HObject(); Image2.GenEmptyObj(); Image2.Dispose();
            HObject Image3 = new HObject(); Image3.GenEmptyObj(); Image3.Dispose();
            switch (ImageType)
            {
                //case eTransImageType.通用比例转换:
                //    HOperatorSet.Decompose3(inImage, out Temp1, out Temp2, out Temp3);
                //    HOperatorSet.TransFromRgb(Temp1, Temp2, Temp3, out Image1, out Image2, out Image3, "cielab");
                //    break;
                case eTransImageType.通用比例转换:
                case eTransImageType.RGB:
                    HOperatorSet.Decompose3(inImage, out Image1, out Image2, out Image3);
                    break;
                case eTransImageType.HSV:
                    HOperatorSet.Decompose3(inImage, out Temp1, out Temp2, out Temp3);
                    HOperatorSet.TransFromRgb(Temp1, Temp2, Temp3,out Image1,out Image2,out Image3,"hsv");
                    break;
                case eTransImageType.HSI:
                case eTransImageType.YUV:
                    HOperatorSet.Decompose3(inImage, out Temp1, out Temp2, out Temp3);
                    HOperatorSet.TransFromRgb(Temp1, Temp2, Temp3, out Image1, out Image2, out Image3, "hsi"); 
                    break;
                //case eTransImageType.YUV:
                //    HOperatorSet.Decompose3(inImage, out Temp1, out Temp2, out Temp3);
                //    HOperatorSet.TransFromRgb(Temp1, Temp2, Temp3, out Image1, out Image2, out Image3, "'yuv'");
                //    break;
                default:
                    break;
            }
            switch (Channel)
            {
                case eTransImageChannel.第一通道:
                    outImage = new HImage(Image1);
                    break;
                case eTransImageChannel.第二通道:
                    outImage = new HImage(Image2);
                    break;
                case eTransImageChannel.第三通道:
                    outImage = new HImage(Image3);
                    break;
                default:
                    break;
            }
            Temp1.Dispose();
            Temp2.Dispose();
            Temp3.Dispose();
            Image1.Dispose();
            Image2.Dispose();
            Image3.Dispose();
        }
        /// <summary>
        ///图像镜像
        /// </summary>
        /// mirror_image(Image : ImageMirror : Mode : )
        /// rotate_image (Image, ImageRotate, -OrientationAngle / rad(180) * 180, 'constant')
        /// List of values: 'bicubic', 'bilinear', 'constant', 'nearest_neighbor', 'weighted'
        public void MirrorImage(HImage inImage, out HImage outImage, eMirrorImageType W)
        {
            //HOperatorSet.MirrorImage(inImage, out HObject imageMirror, W);

            String mode = "";
            switch (W)
            {
                case eMirrorImageType.水平镜像:
                    mode = "column";
                    break;
                case eMirrorImageType.垂直镜像:
                    mode = "row";
                    break;
                case eMirrorImageType.对角镜像:
                    mode = "diagonal";
                    break;
            }
            outImage = inImage.MirrorImage(mode);
        }
        /// <summary>
        /// 图像旋转
        /// </summary>
        /// rotate_image(Image : ImageRotate : Phi, Interpolation : )
        public void RotateImage(HImage inImage, out HImage outImage, eRotateImageAngle angle)
        {
            //HOperatorSet.RotateImage(inImage, out HObject imageMirror, angle.ToString(), "constant");
            double a = 0;
            switch (angle)
            {
                case eRotateImageAngle._90:
                    a = 90;
                    break;
                case eRotateImageAngle._180:
                    a = 180;
                    break;
                case eRotateImageAngle._270:
                    a = 270;
                    break;
            }
            outImage = inImage.RotateImage(a, "constant");
        }
        /// <summary>
        /// 修改图像尺寸
        /// </summary>
        /// change_format(Image : ImagePart : Width, Height : )
        public void ChangeFormat(HImage inImage, out HImage outImage, int W, int H)
        {
            //HOperatorSet.ChangeFormat(inImage, out HObject imageMirror, W, H);
            outImage = inImage.ChangeFormat(W, H);
        }
        #endregion
        #region 滤波
        /// <summary>
        /// 均值滤波
        /// </summary>
        /// mean_image(Image : ImageMean : MaskWidth, MaskHeight : )
        public void MeanImage(HImage inImage, out HImage outImage, int W, int H)
        {
            //HOperatorSet.MeanImage( inImage, out HObject region, W, H);
            outImage = inImage.MeanImage(W, H);
        }
        /// <summary>
        /// 中值滤波
        /// </summary>
        /// median_image(Image : ImageMedian : MaskType, Radius, Margin : )
        public void MedianImage(HImage inImage, out HImage outImage, int radius, int margin)
        {
            //HOperatorSet.MedianImage(inImage, out HObject imageMedian, "square", radius, margin);
            outImage = inImage.MedianImage("square",radius,margin);
        }
        /// <summary>
        /// 高斯滤波
        /// </summary>
        /// gauss_image(Image : ImageGauss : Size : )
        public void GaussImage(HImage inImage, out HImage outImage, int Size)
        {
            //HOperatorSet.GaussImage(inImage, out HObject ImageGauss, Size);
            outImage = inImage.GaussImage(Size);
        }
        #endregion
        #region 形态学运算
        /// <summary>
        /// 灰度膨胀 
        /// </summary>
        /// gray_dilation(Image,SE :ImageDilation : : ) 灰度值膨胀   对灰度值进行操作而不是区域结构操作
        public void GrayDilation(HImage inImage, out HImage outImage, int W, int H)
        {
            //HOperatorSet.GrayDilationRect(inImage, out HObject ImageGauss, W, H);
            outImage = inImage.GrayDilationRect(H, W);
        }
        /// <summary>
        /// 灰度腐蚀 
        /// </summary>
        /// gray_erosion(Image,SE :ImageErosion : : ) 灰度值腐蚀
        public void GrayErosion(HImage inImage, out HImage outImage,int W, int H)
        {
            //HOperatorSet.GrayErosionRect(inImage, out HObject ImageGauss, W, H);
            outImage = inImage.GrayErosionRect(H, W);
        }
        #endregion
        #region 图像增强
        /// <summary>
        /// 图像锐化 
        /// </summary>
        /// emphasize(Image : ImageEmphasize : MaskWidth, MaskHeight, Factor : )201
        public void EmphaSize(HImage inImage,out HImage outImage, int W, int H, double Comp)
        {
            //HOperatorSet.Emphasize(inImage, out HObject imageEmphasize, W, H, Comp);
            outImage = inImage.Emphasize(W, H, Comp);
        }
        /// <summary>
        /// 增加图像对比度
        /// </summary>
        ///illuminate(Image : ImageIlluminate : MaskWidth, MaskHeight, Factor : )
        public void Illuminate(HImage inImage, out HImage outImage, int W, int H, double Comp)
        {
            //HOperatorSet.Illuminate(inImage, out HObject ImageIlluminate, W, H, Comp);
            outImage = inImage.Illuminate(W, H, Comp);
        }
        /// <summary>
        /// 图像亮度调节
        /// </summary>
        /// min_max_gray(Regions, Image : : Percent : Min, Max, Range)确定区域最大最小灰度值
        ///scale_image (ImageReduced, ImageScaled, 255 / Range, -Min * 255 / Range)
        public void ScaleImage(HImage inImage, out HImage outImage, double mult, double add)
        {
            //HOperatorSet.ScaleImage(inImage, out HObject ImageIlluminate, W, H);
            outImage = inImage.ScaleImage(mult, add);
        }
        /// <summary>
        /// 灰度开运算
        /// </summary>
        /// opening_rectangle1(Region : RegionOpening : Width, Height : )打开区域
        public void Opening(HImage inImage, out HImage outImage, int W, int H)
        {
            //HOperatorSet.GrayOpeningRect(inImage, out HObject ImageGauss, W, H);
            outImage = inImage.GrayOpeningRect(H, W);
        }
        /// <summary>
        /// 灰度闭运算
        /// </summary>
        /// closing_rectangle1(Region : RegionClosing : Width, Height : )关闭一个区域。
        public void Closing(HImage inImage, out HImage outImage, int W, int H)
        {
            //HOperatorSet.GrayClosingRect(inImage, out HObject ImageGauss, W, H);
            outImage = inImage.GrayClosingRect(H, W);
        }
        /// <summary>
        /// 反转图像-颜色取反
        /// </summary>
        /// invert_image(Image : ImageInvert : : )
        public void InvertImage(HImage inImage, out HImage outImage, bool A)
        {
                if (!A)
                {
                    //  HOperatorSet.InvertImage(inImage, out imageInvert);
                    outImage = inImage.InvertImage();
                }
                else
                {
                    outImage = inImage;
                }
        }
        #endregion     
        #region 二值化
        /// <summary>
        /// 二值化
        /// </summary>
        /// <param name="LowThreshold">低阈值</param>
        /// <param name="HeighThreshold">高阈值</param>
        /// <param name="IsReverse">是否反色</param>
        /// <exception cref="ex">异常抛出</exception>
        public void Threshold(HImage inImage, out HImage outImage, double LowThreshold, double HeighThreshold, bool IsReverse)
        {
            try
            {
                //区域处理
                HObject ho_Regions, ho_Image_out1, ho_Image_out2;
                HTuple hv_Width = new HTuple(), hv_Height = new HTuple();
                HOperatorSet.Threshold(inImage, out ho_Regions, LowThreshold, HeighThreshold);
                if (IsReverse)
                {
                    HOperatorSet.GetImageSize(inImage, out hv_Width, out hv_Height);
                    HOperatorSet.GenImageConst(out ho_Image_out1, "byte", hv_Width, hv_Height);
                    HOperatorSet.OverpaintRegion(ho_Image_out1, ho_Regions, 255, "fill");
                    HOperatorSet.GenImageProto(ho_Image_out1, out ho_Image_out2, 255);
                    HOperatorSet.OverpaintRegion(ho_Image_out2, ho_Regions, 0, "fill");
                }
                else
                {
                    HOperatorSet.GetImageSize(inImage, out hv_Width, out hv_Height);
                    HOperatorSet.GenImageConst(out ho_Image_out1, "byte", hv_Width, hv_Height);
                    HOperatorSet.OverpaintRegion(ho_Image_out1, ho_Regions, 0, "fill");
                    HOperatorSet.GenImageProto(ho_Image_out1, out ho_Image_out2, 0);
                    HOperatorSet.OverpaintRegion(ho_Image_out2, ho_Regions, 255, "fill");
                }
                outImage = new HImage(ho_Image_out2);
                return;
            }
            catch (Exception ex)
            {
                outImage = inImage;
                Debug.Write(ex.Message);
            }
        }
        /// <summary>
        /// 均值二值化
        /// </summary>
        /// <param name="Width">宽度</param>
        /// <param name="Height">高度</param>
        /// <param name="Skew">偏移</param>
        /// <param name="Type">类型</param>
        /// <exception cref="ex"> 
        ///var_threshold(Image : Region : MaskWidth, MaskHeight, StdDevScale, AbsThreshold, LightDark : )
        public void VarThreshold(HImage inImage, out HImage outImage, double VarWidth, double VarHeight, double VarSkew, eVarThresholdType varType)
        {
            try
            {
                HObject ho_Regions, ho_Image_out1, ho_Image_out2;
                HTuple hv_Width = new HTuple(), hv_Height = new HTuple();
                string VarType = "";
                switch (varType)
                {
                    case eVarThresholdType.等于:
                        VarType = "equal";
                        break;
                    case eVarThresholdType.不等于:
                        VarType = "not_equal";
                        break;
                    case eVarThresholdType.小于等于:
                        VarType = "dark";
                        break;
                    case eVarThresholdType.大于等于:
                        VarType = "light";
                        break;
                }
                HOperatorSet.VarThreshold(inImage, out ho_Regions, VarWidth, VarHeight, VarSkew / 100, 30, VarType);
                HOperatorSet.GetImageSize(inImage, out hv_Width, out hv_Height);
                HOperatorSet.GenImageConst(out ho_Image_out1, "byte", hv_Width, hv_Height);
                HOperatorSet.OverpaintRegion(ho_Image_out1, ho_Regions, 0, "fill");
                HOperatorSet.GenImageProto(ho_Image_out1, out ho_Image_out2, 0);
                HOperatorSet.OverpaintRegion(ho_Image_out2, ho_Regions, 255, "fill");
                outImage = new HImage(ho_Image_out2);
            }
            catch (Exception ex)
            {
                outImage = inImage;
                Debug.Write(ex.Message);
            }
        }
        #endregion
    }
}
