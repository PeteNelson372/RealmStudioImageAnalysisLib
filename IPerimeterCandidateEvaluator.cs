using RealmStudioShapeRenderingLib;

namespace RealmStudioImageAnalysisLib
{
    public interface IPerimeterCandidateEvaluator
    {
        PerimeterCandidateEvaluation Evaluate(
            ImportRegion candidate,
            PerimeterPipelineData data);
    }
}
