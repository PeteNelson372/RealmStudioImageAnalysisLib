using Emgu.CV;
using SkiaSharp;
using System.Runtime.InteropServices;

namespace RealmStudioImageAnalysisLib.StageDefinitions
{
    public sealed class FindEndpointsStage : IPerimeterStage
    {
        public string Id => "FindEndpoints";

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

            Mat combinedEdges =
                data.Get<Mat>(context.GetInputArtifact(inputNames[0])) ?? throw new InvalidOperationException(
                    $"{Id} requires an input artifact.");

            IReadOnlyList<LandformEndpoint> endpoints = FindLandformEndpoints(combinedEdges);

            data.Set(
                context.OutputArtifact
                    ?? throw new InvalidOperationException($"{Id} requires an output artifact."),
                endpoints);

            context.Progress?.Report(
                new PerimeterAlgorithmProgress
                {
                    Stage = Id,
                    Progress = 1.0,
                    Status = $"Found {endpoints.Count} endpoints."
                });

            return new PerimeterStageResult
            {
                Succeeded = true
            };
        }

        private static List<LandformEndpoint> FindLandformEndpoints(Mat edges)
        {
            int width = edges.Cols;
            int height = edges.Rows;

            byte[] pixels =
                new byte[width * height];

            Marshal.Copy(
                edges.DataPointer,
                pixels,
                0,
                pixels.Length);

            List<LandformEndpoint> endpoints = [];

            for (int y = 1; y < height - 1; y++)
            {
                for (int x = 1; x < width - 1; x++)
                {
                    int index =
                        y * width + x;

                    if (pixels[index] == 0)
                        continue;

                    int neighborCount = 0;

                    int neighborXSum = 0;
                    int neighborYSum = 0;

                    for (int offsetY = -1;
                         offsetY <= 1;
                         offsetY++)
                    {
                        for (int offsetX = -1;
                             offsetX <= 1;
                             offsetX++)
                        {
                            if (offsetX == 0 &&
                                offsetY == 0)
                            {
                                continue;
                            }

                            int nx =
                                x + offsetX;

                            int ny =
                                y + offsetY;

                            if (pixels[
                                    ny * width + nx] == 0)
                            {
                                continue;
                            }

                            neighborCount++;

                            neighborXSum += offsetX;
                            neighborYSum += offsetY;
                        }
                    }

                    // A true endpoint normally has exactly one
                    // neighboring edge pixel.
                    if (neighborCount != 1)
                        continue;

                    double length =
                        Math.Sqrt(
                            neighborXSum * neighborXSum +
                            neighborYSum * neighborYSum);

                    if (length < 0.001)
                        continue;

                    // Direction points AWAY from the neighboring pixel.
                    SKPoint direction =
                        new(
                            (float)(-neighborXSum / length),
                            (float)(-neighborYSum / length));

                    endpoints.Add(
                        new LandformEndpoint
                        {
                            Position =
                                new SKPoint(x, y),

                            Direction =
                                direction,

                            NeighborCount =
                                neighborCount
                        });
                }
            }

            return endpoints;
        }
    }
}
