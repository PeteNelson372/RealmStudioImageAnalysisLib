using SkiaSharp;

namespace RealmStudioImageAnalysisLib
{
    public sealed class DetectedRegion
    {
        public int Id { get; set; }
        public int Area { get; init; }
        public SKRect Bounds { get; init; }
        public SKPoint Centroid { get; init; }
        public SKPath Boundary { get; init; } = new();
        public List<SKPath> Holes { get; } = [];
        public bool IsSelected { get; set; }
    }
}
