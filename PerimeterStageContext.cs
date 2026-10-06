namespace RealmStudioImageAnalysisLib
{
    public sealed class PerimeterStageContext
    {
        public CancellationToken CancellationToken { get; init; }

        public IProgress<PerimeterAlgorithmProgress>? Progress { get; init; }

        public IReadOnlyDictionary<string, string> Parameters { get; init; }
            = new Dictionary<string, string>();
    }
}
