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
                case "rectangle1":
                    {
                        hTuples = new HTuple[4];
                        hTuples[0] = hDrawingObject.GetDrawingObjectParams("row1");
                        hTuples[1] = hDrawingObject.GetDrawingObjectParams("column1");
                        hTuples[2] = hDrawingObject.GetDrawingObjectParams("row2");
                        hTuples[3] = hDrawingObject.GetDrawingObjectParams("column2");
                        break;
                    }
            }
            return hTuples;
        }
        public static HTuple[] GetDrawObjectCenter(this DrawingObjectInfo hDrawingObject)
        {
            HTuple[] hTuples = null;
            if(hDrawingObject.ShapeType == DrawShapeType.Rectangle2)
            {
                hTuples = new HTuple[2];
                hTuples[0] = hDrawingObject.HTuples[0] + hDrawingObject.HTuples[2] / 2;
                hTuples[1] = hDrawingObject.HTuples[1] + hDrawingObject.HTuples[3] / 2;
            }
            return hTuples;
        }
    }
  
}
