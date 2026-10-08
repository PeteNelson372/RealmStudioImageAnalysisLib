using Emgu.CV.Util;

namespace RealmStudioImageAnalysisLib
{
    public sealed class MobileSamContourSet : IDisposable
    {
        public IReadOnlyList<VectorOfVectorOfPoint> Contours { get; }

        public MobileSamContourSet(
            IReadOnlyList<VectorOfVectorOfPoint> contours)
        {
            ArgumentNullException.ThrowIfNull(contours);

            Contours = contours;
        }

        public void Dispose()
        {
            foreach (VectorOfVectorOfPoint contourSet in Contours)
            {
                contourSet.Dispose();
            }
        }
    }
}
