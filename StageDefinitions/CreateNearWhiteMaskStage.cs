using Emgu.CV;
using Emgu.CV.CvEnum;
using System.Runtime.InteropServices;

namespace RealmStudioImageAnalysisLib.StageDefinitions
{
    public sealed class CreateNearWhiteMaskStage : IPerimeterStage
    {
        public string Id => "CreateNearWhiteMask";
        private const double LandformWhiteColorDistance = 25.0;

        public PerimeterStageResult Process(
            PerimeterPipelineData data,
            PerimeterStageContext context)
        {
            ArgumentNullException.ThrowIfNull(data);
            ArgumentNullException.ThrowIfNull(context);

            context.CancellationToken.ThrowIfCancellationRequested();

            List<string> inputNames = [.. context.InputArtifacts];

            // this stage requires exactly one input artifact.
            if (inputNames.Count != 1)
            {
                throw new InvalidOperationException(
                    $"Stage '{Id}' requires exactly one input artifact, but {inputNames.Count} were provided.");
            }

            Mat sourceMat =
                data.Get<Mat>(
                    context.GetInputArtifact(inputNames[0])
                        ?? throw new InvalidOperationException(
                            $"{Id} requires an input artifact."));

            Mat nearWhiteMask = CreateNearWhiteMask(
                sourceMat,
                sourceMat.Width,
                sourceMat.Height,
                LandformWhiteColorDistance);

            data.Set(
                    context.OutputArtifact
                        ?? throw new InvalidOperationException(
                            $"{Id} requires an output artifact."),
                    nearWhiteMask);

            context.Progress?.Report(
                new PerimeterAlgorithmProgress
                {
                    Stage = Id,
                    Progress = 1.0,
                    Status = "Near-white mask created."
                });

            return new PerimeterStageResult
            {
                Succeeded = true
            };
        }

        private static Mat CreateNearWhiteMask(Mat source, int width, int height, double maximumColorDistance)
        {
            byte[] pixels =
                new byte[width * height * 4];

            Marshal.Copy(
                source.DataPointer,
                pixels,
                0,
                pixels.Length);

            byte[] mask =
                new byte[width * height];

            double maximumDistanceSquared =
                maximumColorDistance *
                maximumColorDistance;

            for (int y = 0; y < height; y++)
            {
                int rowOffset =
                    y * width * 4;

                int maskOffset =
                    y * width;

                for (int x = 0; x < width; x++)
                {
                    int offset =
                        rowOffset + x * 4;

                    // BGRA ordering.
                    int b = pixels[offset];
                    int g = pixels[offset + 1];
                    int r = pixels[offset + 2];

                    double dr = 255.0 - r;
                    double dg = 255.0 - g;
                    double db = 255.0 - b;

                    double distanceSquared =
                        dr * dr +
                        dg * dg +
                        db * db;

                    if (distanceSquared <=
                        maximumDistanceSquared)
                    {
                        mask[maskOffset + x] = 255;
                    }
                }
            }

            Mat result =
                new(
                    height,
                    width,
                    DepthType.Cv8U,
                    1);

            Marshal.Copy(
                mask,
                0,
                result.DataPointer,
                mask.Length);

            return result;
        }
    }
}
