namespace RealmStudioImageAnalysisLib
{
    public sealed class PerimeterAlgorithmContext
    {
        public CancellationToken CancellationToken { get; init; }

        public IProgress<PerimeterAlgorithmProgress>? Progress { get; init; }
    }
}
