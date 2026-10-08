using Emgu.CV;
using Emgu.CV.Structure;

namespace RealmStudioImageAnalysisLib
{
    public sealed class MobileSamMaskSet : IDisposable
    {
        public IReadOnlyList<Image<Gray, byte>> Masks { get; }

        public MobileSamMaskSet(
            IReadOnlyList<Image<Gray, byte>> masks)
        {
            ArgumentNullException.ThrowIfNull(masks);

            Masks = masks;
        }

        public void Dispose()
        {
            foreach (Image<Gray, byte> mask in Masks)
            {
                mask.Dispose();
            }
        }
    }
}
