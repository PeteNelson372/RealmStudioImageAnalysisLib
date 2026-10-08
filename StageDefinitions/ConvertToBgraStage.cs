using SkiaSharp;

namespace RealmStudioImageAnalysisLib.StageDefinitions
{
    public sealed class ConvertToBgraStage : IPerimeterStage
    {
        public string Id => "ConvertToBgra";

        public PerimeterStageResult Process(
            PerimeterPipelineData data,
            PerimeterStageContext context)
        {
            ArgumentNullException.ThrowIfNull(data);
            ArgumentNullException.ThrowIfNull(context);

            context.CancellationToken.ThrowIfCancellationRequested();

            SKBitmap bgraBitmap =
                data.OriginalBitmap.Copy(SKColorType.Bgra8888)
                ?? throw new InvalidOperationException(
                    "Unable to convert image to BGRA8888.");

            data.Set("BgraBitmap", bgraBitmap);

            context.Progress?.Report(
                new PerimeterAlgorithmProgress
                {
                    Stage = Id,
                    Progress = 1.0,
                    Status = "Image converted to BGRA8888."
                });

            return new PerimeterStageResult
            {
                Succeeded = true
            };
        }
    }
}
