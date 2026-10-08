using SkiaSharp;

namespace RealmStudioImageAnalysisLib
{
    public sealed class LandformEndpointJoin
    {
        public SKPoint Start { get; init; }

        public SKPoint End { get; init; }

        public double Distance { get; init; }

        public double AngleDegrees { get; init; }

        public bool Accepted { get; set; }
    }
}
