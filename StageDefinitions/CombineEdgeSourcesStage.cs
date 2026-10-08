using Emgu.CV;

namespace RealmStudioImageAnalysisLib.StageDefinitions
{
    public sealed class CombineEdgeSourcesStage : IPerimeterStage
    {
        public string Id => "CombineEdgeSources";

        public PerimeterStageResult Process(
            PerimeterPipelineData data,
            PerimeterStageContext context)
        {
            ArgumentNullException.ThrowIfNull(data);
            ArgumentNullException.ThrowIfNull(context);

            context.CancellationToken.ThrowIfCancellationRequested();

            Mat cannyEdges = data.Get<Mat>("CannyEdges");
            Mat whiteBoundary = data.Get<Mat>("WhiteBoundary");

            Mat combinedEdges = new();

            CvInvoke.BitwiseOr(
                cannyEdges,
                whiteBoundary,
                combinedEdges);

            data.Set("CombinedEdges", combinedEdges);

            context.Progress?.Report(
                new PerimeterAlgorithmProgress
                {
                    Stage = Id,
                    Progress = 1.0,
                    Status = "Edge sources combined."
                });

            return new PerimeterStageResult
            {
                Succeeded = true
            };
        }
    }
}
