using Emgu.CV;

namespace RealmStudioImageAnalysisLib
{
    public sealed class MobileSamMaskSet : IDisposable
    {
        public IReadOnlyList<Mat> Masks { get; }

        public MobileSamMaskSet(
            IReadOnlyList<Mat> masks)
        {
            ArgumentNullException.ThrowIfNull(masks);

            Masks = masks;
        }

        public void Dispose()
        {
            foreach (Mat mask in Masks)
            {
                mask.Dispose();
            }
        }
    }
}
