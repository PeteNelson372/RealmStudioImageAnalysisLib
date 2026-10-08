namespace RealmStudioImageAnalysisLib
{
    public sealed class PerimeterStageContext
    {
        public CancellationToken CancellationToken { get; init; }

        public IProgress<PerimeterAlgorithmProgress>? Progress { get; init; }

        public IReadOnlyDictionary<string, string> Parameters { get; init; } = new Dictionary<string, string>();

        public bool SaveOutput { get; init; }
        public string? OutputArtifact { get; init; }
        public string? OutputName { get; init; }
    }
}
