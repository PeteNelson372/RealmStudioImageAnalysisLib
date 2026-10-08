using Emgu.CV;

namespace RealmStudioImageAnalysisLib.StageDefinitions
{
    public sealed class CannyEdgesStage : IPerimeterStage
    {
        public string Id => "CannyEdges";

        private const double LandformCannyThreshold1 = 40.0;
        private const double LandformCannyThreshold2 = 100.0;

        public PerimeterStageResult Process(
            PerimeterPipelineData data,
            PerimeterStageContext context)
        {
            ArgumentNullException.ThrowIfNull(data);
            ArgumentNullException.ThrowIfNull(context);

            context.CancellationToken.ThrowIfCancellationRequested();

            Mat blurred = data.Get<Mat>("Blurred");

            Mat cannyEdges = new();

            CvInvoke.Canny(
                blurred,
                cannyEdges,
                LandformCannyThreshold1,
                LandformCannyThreshold2);

            data.Set("CannyEdges", cannyEdges);

            context.Progress?.Report(
                new PerimeterAlgorithmProgress
                {
                    Stage = Id,
                    Progress = 1.0,
                    Status = "Canny edges detected."
                });

            return new PerimeterStageResult
            {
                Succeeded = true
            };
        }
    }
}
