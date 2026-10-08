using Emgu.CV;

namespace RealmStudioImageAnalysisLib.StageDefinitions
{
    public sealed class GaussianBlurStage : IPerimeterStage
    {
        public string Id => "GaussianBlur";

        private const int LandformBlurSize = 7;

        public PerimeterStageResult Process(
            PerimeterPipelineData data,
            PerimeterStageContext context)
        {
            ArgumentNullException.ThrowIfNull(data);
            ArgumentNullException.ThrowIfNull(context);

            context.CancellationToken.ThrowIfCancellationRequested();

            Mat gray = data.Get<Mat>("Gray");

            Mat blurred = new();

            CvInvoke.GaussianBlur(
                gray,
                blurred,
                new System.Drawing.Size(
                    LandformBlurSize,
                    LandformBlurSize),
                0);

            data.Set("Blurred", blurred);

            context.Progress?.Report(
                new PerimeterAlgorithmProgress
                {
                    Stage = Id,
                    Progress = 1.0,
                    Status = "Gaussian blur applied."
                });

            return new PerimeterStageResult
            {
                Succeeded = true
            };
        }
    }
}
