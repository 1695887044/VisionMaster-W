using HalconDotNet;
using Core.Halcon.Models;


namespace Core.Halcon.Extensions
{
    public static class HDrawingObjectExtensions
    {
        public static HTuple[] GetTuples(this HDrawingObject hDrawingObject, string type)
        {
            HTuple[] hTuples = null;
            switch (type)
            {
                // rectangle2 绘制对象：中心 + 角度 + 半长/半宽 5 参数（可旋转矩形，与 GenRectangle2/RoiItem.Params 语义一致）
                case "rectangle2":
                    {
                        hTuples = new HTuple[5];
                        hTuples[0] = hDrawingObject.GetDrawingObjectParams("row");
                        hTuples[1] = hDrawingObject.GetDrawingObjectParams("column");
                        hTuples[2] = hDrawingObject.GetDrawingObjectParams("phi");
                        hTuples[3] = hDrawingObject.GetDrawingObjectParams("length1");
                        hTuples[4] = hDrawingObject.GetDrawingObjectParams("length2");
                        break;
                    }
                case "circle":
                    {
                        hTuples = new HTuple[3];
                        hTuples[0] = hDrawingObject.GetDrawingObjectParams("row");
                        hTuples[1] = hDrawingObject.GetDrawingObjectParams("column");
                        hTuples[2] = hDrawingObject.GetDrawingObjectParams("radius");
                        break;
                    }
                // ellipse 绘制对象的参数名是 radius1/radius2（已实测：length1/length2 会抛 HALCON #1302）
                case "ellipse":
                    {
                        hTuples = new HTuple[5];
                        hTuples[0] = hDrawingObject.GetDrawingObjectParams("row");
                        hTuples[1] = hDrawingObject.GetDrawingObjectParams("column");
                        hTuples[2] = hDrawingObject.GetDrawingObjectParams("phi");
                        hTuples[3] = hDrawingObject.GetDrawingObjectParams("radius1");
                        hTuples[4] = hDrawingObject.GetDrawingObjectParams("radius2");
                        break;
                    }
            }
            return hTuples;
        }
        public static HTuple[] GetDrawObjectCenter(this DrawingObjectInfo hDrawingObject)
        {
            HTuple[] hTuples = null;
            if(hDrawingObject.ShapeType == DrawShapeType.Rectangle)
            {
                hTuples = new HTuple[2];
                hTuples[0] = hDrawingObject.HTuples[0] + hDrawingObject.HTuples[3] / 2;
                hTuples[1] = hDrawingObject.HTuples[1] + hDrawingObject.HTuples[4] / 2;
            }
            return hTuples;
        }
    }
  
}
