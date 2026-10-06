using Emgu.CV;
using RealmStudioShapeRenderingLib;

namespace RealmStudioImageAnalysisLib
{
    public sealed class LandformAnalysisResult
    {
        public int ImageWidth { get; init; }

        public int ImageHeight { get; init; }

        public Mat RepairedScaffold { get; init; } = null!;

        public IReadOnlyList<ImportRegion> ImportRegions { get; init; } = [];
    }
}
