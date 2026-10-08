using SkiaSharp;

namespace RealmStudioImageAnalysisLib
{
    public sealed class LandformEndpoint
    {
        public SKPoint Position { get; init; }

        public SKPoint Direction { get; init; }

        public int NeighborCount { get; init; }
    }
}
