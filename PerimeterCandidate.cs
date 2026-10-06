using SkiaSharp;

namespace RealmStudioImageAnalysisLib
{
    public sealed class PerimeterCandidate
    {
        public SKPath Path { get; init; } = new();

        public string Source { get; init; } = "";

        /// <summary>
        /// How confident we are that this represents the desired object.
        /// </summary>
        public double ObjectConfidence { get; init; }

        /// <summary>
        /// How closely we believe the current path follows the desired perimeter.
        /// </summary>
        public double BoundaryConfidence { get; init; }

        // Useful diagnostic information
        public double Area { get; init; }
        public double AreaRatio { get; init; }
        public double Solidity { get; init; }
        public double Compactness { get; init; }
    }
}
