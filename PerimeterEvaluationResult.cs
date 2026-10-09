namespace RealmStudioImageAnalysisLib
{
    public sealed class PerimeterEvaluationResult
    {
        public bool Accepted { get; init; }

        public double Score { get; init; }

        public string FailureMode { get; init; } = string.Empty;

        public string Reason { get; init; } = string.Empty;

        public IReadOnlyDictionary<string, double> Metrics { get; init; } = new Dictionary<string, double>();
    }
}
