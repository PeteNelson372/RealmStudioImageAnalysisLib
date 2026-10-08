using Emgu.CV;
using Emgu.CV.Structure;

namespace RealmStudioImageAnalysisLib.StageDefinitions
{
    public sealed class CreateMobileSamMasks : IPerimeterStage
    {
        public string Id => "CreateMobileSamMasks";

        public PerimeterStageResult Process(
            PerimeterPipelineData data,
            PerimeterStageContext context)
        {
            ArgumentNullException.ThrowIfNull(data);
            ArgumentNullException.ThrowIfNull(context);
            context.CancellationToken.ThrowIfCancellationRequested();

            IReadOnlyList<SegmentationResult> results =
                data.Get<IReadOnlyList<SegmentationResult>>(
                    "MobileSamSegmentationResults");

            List<Image<Gray, byte>> masks = [];

            for (int i = 0; i < results.Count; i++)
            {
                context.CancellationToken.ThrowIfCancellationRequested();

                SegmentationResult result =
                    results[i];

                //
                // The single-mask decoder returns:
                //
                //     [1, 1, height, width]
                //
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

                //
                // The decoder has already resized the mask to
                // orig_im_size. Therefore these coordinates are
                // already in source-image/map coordinates.
                //
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

                masks.Add(mask);

                context.Progress?.Report(
                    new PerimeterAlgorithmProgress
                    {
                        Stage = Id,
                        Progress =
                            (double)(i + 1) /
                            Math.Max(1, results.Count),
                        Status =
                            $"Created MobileSAM mask " +
                            $"{i + 1} of {results.Count}."
                    });
            }

            data.Set(
                "MobileSamMasks",
                new MobileSamMaskSet(masks));

            return new PerimeterStageResult
            {
                Succeeded = true
            };
        }
    }
}
