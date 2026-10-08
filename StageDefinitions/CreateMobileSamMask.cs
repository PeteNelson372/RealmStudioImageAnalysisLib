using Emgu.CV;
using Emgu.CV.Structure;

namespace RealmStudioImageAnalysisLib.StageDefinitions
{
    public sealed class CreateMobileSamMask : IPerimeterStage
    {
        public string Id => "CreateMobileSamMask";

        public PerimeterStageResult Process(
            PerimeterPipelineData data,
            PerimeterStageContext context)
        {
            ArgumentNullException.ThrowIfNull(data);
            ArgumentNullException.ThrowIfNull(context);
            context.CancellationToken.ThrowIfCancellationRequested();

            SegmentationResult result =
                data.Get<SegmentationResult>(
                    "MobileSamSegmentationResult");

            if (result.MaskDimensions.Length != 4)
            {
                throw new InvalidOperationException(
                    "Unexpected MobileSAM mask dimensions: " +
                    $"[{string.Join(
                        ", ",
                        result.MaskDimensions)}]");
            }

            if (result.MaskDimensions[0] != 1 ||
                result.MaskDimensions[1] != 1)
            {
                throw new InvalidOperationException(
                    "The single-mask MobileSAM decoder " +
                    "returned unexpected dimensions: " +
                    $"[{string.Join(
                        ", ",
                        result.MaskDimensions)}]");
            }

            int height =
                result.MaskDimensions[2];

            int width =
                result.MaskDimensions[3];

            int maskSize =
                width * height;

            if (result.Masks.Length < maskSize)
            {
                throw new InvalidOperationException(
                    "MobileSAM returned a mask buffer " +
                    "smaller than expected.");
            }

            Image<Gray, byte> mask =
                new(
                    width,
                    height);

            const float threshold = 0.0f;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float value =
                        result.Masks[
                            y * width + x];

                    mask.Data[y, x, 0] =
                        value > threshold
                            ? (byte)255
                            : (byte)0;
                }
            }

            data.Set(
                "MobileSamMask",
                mask);

            context.Progress?.Report(
                new PerimeterAlgorithmProgress
                {
                    Stage = Id,
                    Progress = 1.0,
                    Status = "MobileSAM mask created."
                });

            return new PerimeterStageResult
            {
                Succeeded = true
            };
        }
    }
}
