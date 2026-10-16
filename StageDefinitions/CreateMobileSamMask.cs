
using Emgu.CV;
using Emgu.CV.CvEnum;
using System.Runtime.InteropServices;

namespace RealmStudioImageAnalysisLib.StageDefinitions
{
    public sealed class CreateMobileSamMask : IPerimeterStage
    {
        public string Id => "CreateMobileSamMask";

        public float Threshold { get; set; } = 0.0f;

        public PerimeterStageResult Process(
            PerimeterPipelineData data,
            PerimeterStageContext context)
        {
            ArgumentNullException.ThrowIfNull(data);
            ArgumentNullException.ThrowIfNull(context);
            context.CancellationToken.ThrowIfCancellationRequested();

            List<string> inputNames = [.. context.InputArtifacts];

            if (inputNames.Count != 1)
            {
                throw new InvalidOperationException(
                    $"Stage '{Id}' requires exactly one input artifact, but {inputNames.Count} were provided.");
            }

            SegmentationResult result =
                data.Get<SegmentationResult>(
                    context.GetInputArtifact(inputNames[0]));

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
            int maskSize = checked(width * height);

            if (result.Masks.Length < maskSize)
            {
                throw new InvalidOperationException(
                    "MobileSAM returned a mask buffer smaller than expected.");
            }

            using Mat mask = new(
                height,
                width,
                DepthType.Cv8U,
                1);

            byte[] pixels = new byte[maskSize];

            for (int i = 0; i < maskSize; i++)
            {
                pixels[i] = result.Masks[i] > Threshold
                    ? (byte)255
                    : (byte)0;
            }

            // Copy the managed pixel buffer into the Mat.
            // A newly allocated, continuous Mat has tightly packed rows.
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

            data.Set(
                context.OutputArtifact
                    ?? throw new InvalidOperationException(
                        $"{Id} requires an output artifact."),
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
