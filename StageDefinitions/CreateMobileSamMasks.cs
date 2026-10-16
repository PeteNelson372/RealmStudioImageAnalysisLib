
using Emgu.CV;
using Emgu.CV.CvEnum;
using System.Runtime.InteropServices;

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

            List<Mat> masks = [];

            const float threshold = 0.0f;

            try
            {
                for (int i = 0; i < results.Count; i++)
                {
                    context.CancellationToken.ThrowIfCancellationRequested();

                    SegmentationResult result = results[i];

                    // Expected dimensions: [1, 1, height, width].
                    if (result.MaskDimensions.Length != 4 ||
                        result.MaskDimensions[0] != 1 ||
                        result.MaskDimensions[1] != 1)
                    {
                        throw new InvalidOperationException(
                            "Unexpected MobileSAM mask dimensions: " +
                            $"[{string.Join(", ", result.MaskDimensions)}]");
                    }

                    int height = result.MaskDimensions[2];
                    int width = result.MaskDimensions[3];

                    if (height <= 0 || width <= 0)
                    {
                        throw new InvalidOperationException(
                            "MobileSAM returned invalid mask dimensions.");
                    }

                    int maskSize = checked(width * height);

                    if (result.Masks.Length < maskSize)
                    {
                        throw new InvalidOperationException(
                            "MobileSAM returned a mask buffer " +
                            "smaller than expected.");
                    }

                    // Create an 8-bit, single-channel grayscale Mat.
                    Mat mask = new(
                        height,
                        width,
                        DepthType.Cv8U,
                        1);

                    try
                    {
                        byte[] pixels = new byte[maskSize];

                        for (int j = 0; j < maskSize; j++)
                        {
                            pixels[j] = result.Masks[j] > threshold
                                ? (byte)255
                                : (byte)0;
                        }

                        // Copy the managed buffer into the Mat.
                        if (!mask.IsContinuous)
                        {
                            throw new InvalidOperationException(
                                "The allocated mask matrix is not continuous.");
                        }

                        Marshal.Copy(
                            pixels,
                            0,
                            mask.DataPointer,
                            pixels.Length);

                        masks.Add(mask);
                    }
                    catch
                    {
                        mask.Dispose();
                        throw;
                    }

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

                // Ownership of the masks has been transferred to
                // MobileSamMaskSet / pipeline data.
                return new PerimeterStageResult
                {
                    Succeeded = true
                };
            }
            catch
            {
                // Dispose any masks created if processing fails.
                foreach (Mat mask in masks)
                {
                    mask.Dispose();
                }

                throw;
            }
        }
    }
}
