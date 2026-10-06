namespace RealmStudioImageAnalysisLib
{
    public sealed class PerimeterCandidateEvaluation
    {
        public double Score { get; init; }

        public bool IsUsable { get; init; }

        public string? Reason { get; init; }
    }
}
