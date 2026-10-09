using RealmStudioShapeRenderingLib;

namespace RealmStudioImageAnalysisLib
{
    public sealed class PerimeterExtractionResult
    {
        public bool Succeeded { get; init; }

        public string? SelectedPipeline { get; init; }

        public IReadOnlyList<ImportRegion> ImportRegions { get; init; } = [];

        public IReadOnlyList<PerimeterPipelineAttempt> Attempts { get; init; } = [];

        public string? FailureReason { get; init; }
    }
}
