namespace RealmStudioImageAnalysisLib
{
    public interface IPerimeterCandidateEvaluator
    {
        PerimeterCandidateEvaluation Evaluate(
            PerimeterCandidate candidate,
            PerimeterPipelineData data);
    }
}
