using SkiaSharp;

namespace RealmStudioImageAnalysisLib
{
    public sealed class PerimeterPipelineData
    {
        public required SKBitmap OriginalBitmap { get; init; }

        public SKBitmap? WorkingBitmap { get; set; }

        public SKBitmap? Mask { get; set; }

        public IReadOnlyList<SKPath>? Contours { get; set; }

        public List<PerimeterCandidate> Candidates { get; } = [];
    }
}
