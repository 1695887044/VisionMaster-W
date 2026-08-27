using HalconDotNet;
using Core.Halcon.Models;
using static HalconDotNet.HDrawingObject;

namespace Core.Halcon.Extensions
{
    public static class HControlViewExtensions
    {

        public static void TransformAndDisplay(
            this HWindow src,
            HObject dispObj,
            HTuple row1,
            HTuple column1,
            HTuple angle1,
            HTuple row2,
            HTuple column2,
            HTuple angle2
        )
        {
            // 检查源窗口和显示对象是否为空
            if (src == null || dispObj == null)
                return ;
            
            // 计算从原始位置到目标位置的仿射变换矩阵
            HOperatorSet.VectorAngleToRigid(
                row1,
                column1,
                angle1,
                row2,
                column2,
                angle2,
                out var tempMat2D
            );
            
            // 应用仿射变换到轮廓对象
            HOperatorSet.AffineTransContourXld(
                   dispObj,
                   out HObject transformedContours,
                   tempMat2D
               );
            
            // 在窗口中显示变换后的轮廓
            src.DispObj(transformedContours);
        }


        public static async Task<DrawingObjectInfo> DrawShapeAsync( this HWindow window, HDrawingObjectType shapeType= HDrawingObject.HDrawingObjectType.RECTANGLE2)
        {
           return await Task.Run(() =>
            {
                HObject drawObj;
                HOperatorSet.GenEmptyObj(out drawObj);
                HOperatorSet.SetColor(window, "blue");
                var hTuples = new HTuple[5];
                HOperatorSet.DrawRectangle2(
                    window,
                    out hTuples[0],
                    out hTuples[1],
                    out hTuples[2],
                    out hTuples[3],
                    out hTuples[4]
                );
                drawObj = hTuples.GenRectangle2();
               return new DrawingObjectInfo(
                    DrawShapeType.Rectangle2,
                    drawObj,
                    hTuples
                );
            });
        }
    }
}
