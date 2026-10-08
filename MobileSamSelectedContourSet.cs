namespace RealmStudioImageAnalysisLib
{
    public sealed class MobileSamSelectedContourSet : IDisposable
    {
        public IReadOnlyList<MobileSamSelectedContour?> Contours { get; }

        public MobileSamSelectedContourSet(
            IReadOnlyList<MobileSamSelectedContour?> contours)
        {
            ArgumentNullException.ThrowIfNull(contours);

            Contours = contours;
        }

        public void Dispose()
        {
        }
    }
}
