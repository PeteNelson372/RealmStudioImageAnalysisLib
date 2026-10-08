using Emgu.CV.Util;
using System.Drawing;

namespace RealmStudioImageAnalysisLib
{
    public sealed class MobileSamSelectedContour
    {
        public VectorOfPoint Contour { get; }

        public double Area { get; }

        public Rectangle Bounds { get; }

        public MobileSamSelectedContour(
            VectorOfPoint contour,
            double area,
            Rectangle bounds)
        {
            ArgumentNullException.ThrowIfNull(contour);

            Contour = contour;
            Area = area;
            Bounds = bounds;
        }
    }
}
