namespace RealmStudioImageAnalysisLib
{
    public sealed class PerimeterPipelineAttempt
    {
        public string PipelineName { get; init; } = string.Empty;

        public int Priority { get; init; }

        public PerimeterPipelineResult Result { get; init; } = null!;

        public PerimeterEvaluationResult Evaluation { get; init; } = null!;
    }
}
