using System;
using HalconDotNet;
using System.Xml.Serialization;

namespace VM.Halcon.Model
{
	/// <summary>
	/// This class demonstrates one of the possible implementations for a 
	/// circular ROI. ROICircle inherits from the base class ROI and 
	/// implements (besides other auxiliary methods) all virtual methods 
	/// defined in ROI.cs.
	/// </summary>
    [Serializable]
    public class ROIPoint : ROI
    {
        public double midC, midR;

        public double phi;

        //auxiliary variables
        HTuple rowsInit;
        HTuple colsInit;
        HTuple rows;
        HTuple cols;

        HHomMat2D hom2D, tmp;

        public double zoomWndFactor { get; private set; }

        public ROIPoint()
        {
            NumHandles = 2; // 
            ActiveHandleId = 0;
        }
        public ROIPoint(double beginRow, double beginCol)
        {
            CreatePoint(beginRow, beginCol);
        }
        public override void CreatePoint(double midY, double midX)
        {
            midR = midY;
            midC = midX;
            phi = 0.0;
            rowsInit = new HTuple(new double[] { 0.0, 0.0 });
            colsInit = new HTuple(new double[] { 0.0, 50 });
            //order        mp, arrowMidpoint
            hom2D = new HHomMat2D();
            tmp = new HHomMat2D();
            updateHandlePos(zoomWndFactor = 1.0);
        }

        /// <summary>Paints the ROI into the supplied window</summary>
        /// <param name="window">HALCON window</param>
        public override void Draw(HWindow window)
        {
            window.DispCross(midR, midC, 120, 0); 
            window.SetColor("cyan");
            window.SetDraw("fill");
            window.DispRectangle2(midR, midC, -phi, 4, 4);
            window.SetColor("cyan");
            window.SetDraw("margin");//"margin"
            this.zoomWndFactor = zoomWndFactor;
        }

        /// <summary> 
        /// Returns the distance of the ROI handle being
        /// closest to the image point(x,y)
        /// </summary>
        /// <param name="x">x (=column) coordinate</param>
        /// <param name="y">y (=row) coordinate</param>
        /// <returns> 
        /// Distance of the closest ROI handle.
        /// </returns>
        public override double DistToClosestHandle(double x, double y)
        {
            double max = 10000;
            double[] val = new double[NumHandles];
            for (int i = 0; i < NumHandles; i++)
                val[i] = HMisc.DistancePp(y, x, rows[i].D, cols[i].D);

            for (int i = 0; i < NumHandles; i++)
            {
                if (val[i] < max)
                {
                    max = val[i];
                    ActiveHandleId = i;
                }
            }
            return val[ActiveHandleId];
        }
        /// <summary> 
        /// Paints the active handle of the ROI object into the supplied window
        /// </summary>
        /// <param name="window">HALCON window</param>
        public override void DisplayActive(HWindow window)
        {
            switch (ActiveHandleId)
            {
                case 0:
                    window.DispRectangle2(midR, midC, 0, zoomWndFactor * 5, zoomWndFactor * 5);
                    break;
                case 1:
                    window.DispArrow(midR, midC,
                                midR + (Math.Sin(phi) * zoomWndFactor * 50 * 1.2),
                                midC + (Math.Cos(phi) * zoomWndFactor * 50 * 1.2),
                                zoomWndFactor * 1.5);
                    break;

            }

            this.zoomWndFactor = zoomWndFactor;
        }
        /// <summary>Gets the HALCON region described by the ROI</summary>
        public override HRegion GetRegion()
        {
            HRegion region = new HRegion();
            region.GenRegionPolygonFilled(new HTuple(midR), new HTuple(midC));
            return (HRegion)region;
        }
        /// <summary>
        /// Gets the model information described by 
        /// the interactive ROI
        /// </summary> 
        public override HTuple GetModelData()
        {
            return new HTuple(new HTuple[] { midR, midC, -phi });
        }

        /// <summary> 
        /// Recalculates the shape of the ROI instance. Translation is 
        /// performed at the active handle of the ROI object 
        /// for the image coordinate (x,y)
        /// </summary>
        /// <param name="newX">x mouse coordinate</param>
        /// <param name="newY">y mouse coordinate</param>
        public override void moveByHandle(double newX, double newY)
        {
            double vX, vY;
            switch (ActiveHandleId)
            {
                case 0:
                    midC = newX;
                    midR = newY;
                    break;
                case 1:
                    vY = newY - rows[0].D;
                    vX = newX - cols[0].D;
                    phi = Math.Atan2(vY, vX);
                    break;
            }
            updateHandlePos(zoomWndFactor);


        }//end of method
        private void updateHandlePos(double zoomWndFactor)
        {
            colsInit[1] = zoomWndFactor * 50;
            hom2D.HomMat2dIdentity();
            hom2D = hom2D.HomMat2dTranslate(midC, midR);
            hom2D = hom2D.HomMat2dRotateLocal(phi);
            //tmp = hom2D.HomMat2dScaleLocal(Length1, Length2);
            cols = hom2D.AffineTransPoint2d(colsInit, rowsInit, out rows);
        }
    }//end of class
}//end of namespace
