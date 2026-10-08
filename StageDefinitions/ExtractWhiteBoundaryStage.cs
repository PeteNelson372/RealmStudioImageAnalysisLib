using Emgu.CV;
using Emgu.CV.CvEnum;
using System.Runtime.InteropServices;

namespace RealmStudioImageAnalysisLib.StageDefinitions
{
    public sealed class ExtractWhiteBoundaryStage : IPerimeterStage
    {
        public string Id => "ExtractWhiteBoundary";

        public PerimeterStageResult Process(
            PerimeterPipelineData data,
            PerimeterStageContext context)
        {
            ArgumentNullException.ThrowIfNull(data);
            ArgumentNullException.ThrowIfNull(context);

            context.CancellationToken.ThrowIfCancellationRequested();

            Mat connectedWhite = data.Get<Mat>("ConnectedWhite");

            Mat whiteBoundary = CreateMaskBoundary(
                connectedWhite,
                connectedWhite.Width,
                connectedWhite.Height);

            data.Set(
                "WhiteBoundary",
                whiteBoundary);

            context.Progress?.Report(
                new PerimeterAlgorithmProgress
                {
                    Stage = Id,
                    Progress = 1.0,
                    Status = "Exterior white boundary extracted."
                });

            return new PerimeterStageResult
            {
                Succeeded = true
            };
        }

        private static Mat CreateMaskBoundary(Mat connectedMask, int width, int height)
        {
            byte[] source =
                new byte[width * height];

            Marshal.Copy(
                connectedMask.DataPointer,
                source,
                0,
                source.Length);

            byte[] boundary =
                new byte[width * height];

            for (int y = 1; y < height - 1; y++)
            {
                int rowOffset =
                    y * width;

                for (int x = 1; x < width - 1; x++)
                {
                    int index =
                        rowOffset + x;

                    if (source[index] == 0)
                        continue;

                    bool isBoundary =
                        source[index - width - 1] == 0 ||
                        source[index - width] == 0 ||
                        source[index - width + 1] == 0 ||

                        source[index - 1] == 0 ||
                        source[index + 1] == 0 ||

                        source[index + width - 1] == 0 ||
                        source[index + width] == 0 ||
                        source[index + width + 1] == 0;

                    if (isBoundary)
                    {
                        boundary[index] = 255;
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
                boundary,
                0,
                result.DataPointer,
                boundary.Length);

            return result;
        }
    }
}
