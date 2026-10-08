using RealmStudioShapeRenderingLib;
using SkiaSharp;

namespace RealmStudioImageAnalysisLib
{
    public sealed class PerimeterPipelineResult
    {
        public string PipelineName { get; init; } = string.Empty;

        public bool Succeeded { get; init; }

        public IReadOnlyList<ImportRegion> ImportRegions { get; init; } = [];

        public IReadOnlyList<SKPath> RefinedPerimeters { get; init; } = [];

        public string? FailureReason { get; init; }
    }
}
