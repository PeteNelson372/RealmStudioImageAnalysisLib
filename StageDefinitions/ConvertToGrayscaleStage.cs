using Emgu.CV;
using Emgu.CV.CvEnum;

namespace RealmStudioImageAnalysisLib.StageDefinitions
{
    public sealed class ConvertToGrayscaleStage : IPerimeterStage
    {
        public string Id => "ConvertToGrayscale";

        public PerimeterStageResult Process(
            PerimeterPipelineData data,
            PerimeterStageContext context)
        {
            ArgumentNullException.ThrowIfNull(data);
            ArgumentNullException.ThrowIfNull(context);

            context.CancellationToken.ThrowIfCancellationRequested();

            Mat sourceMat = data.Get<Mat>("SourceMat");

            Mat gray = new();

            CvInvoke.CvtColor(
                sourceMat,
                gray,
                ColorConversion.Bgra2Gray);

            data.Set("Gray", gray);

            context.Progress?.Report(
                new PerimeterAlgorithmProgress
                {
                    Stage = Id,
                    Progress = 1.0,
                    Status = "Image converted to grayscale."
                });

            return new PerimeterStageResult
            {
                Succeeded = true
            };
        }
    }
}
