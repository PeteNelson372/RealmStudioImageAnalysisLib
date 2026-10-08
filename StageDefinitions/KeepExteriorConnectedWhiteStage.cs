using Emgu.CV;
using Emgu.CV.CvEnum;
using System.Runtime.InteropServices;

namespace RealmStudioImageAnalysisLib.StageDefinitions
{
    public sealed class KeepExteriorConnectedWhiteStage : IPerimeterStage
    {
        public string Id => "KeepExteriorConnectedWhite";

        public PerimeterStageResult Process(
            PerimeterPipelineData data,
            PerimeterStageContext context)
        {
            ArgumentNullException.ThrowIfNull(data);
            ArgumentNullException.ThrowIfNull(context);

            context.CancellationToken.ThrowIfCancellationRequested();

            Mat nearWhiteMask =
                data.Get<Mat>("NearWhiteMask");

            Mat connectedWhite = CreateConnectedWhiteMask(
                nearWhiteMask,
                nearWhiteMask.Width,
                nearWhiteMask.Height);

            data.Set(
                "ConnectedWhite",
                connectedWhite);

            context.Progress?.Report(
                new PerimeterAlgorithmProgress
                {
                    Stage = Id,
                    Progress = 1.0,
                    Status = "Exterior-connected white regions identified."
                });

            return new PerimeterStageResult
            {
                Succeeded = true
            };
        }

        private static Mat CreateConnectedWhiteMask(Mat whiteMask, int width, int height)
        {
            byte[] source =
                new byte[width * height];

            Marshal.Copy(
                whiteMask.DataPointer,
                source,
                0,
                source.Length);

            byte[] connected =
                new byte[width * height];

            Queue<int> queue = new();

            void AddSeed(int x, int y)
            {
                int index =
                    y * width + x;

                if (source[index] == 0 ||
                    connected[index] != 0)
                {
                    return;
                }

                connected[index] = 255;
                queue.Enqueue(index);
            }

            // Top and bottom edges.
            for (int x = 0; x < width; x++)
            {
                AddSeed(x, 0);
                AddSeed(x, height - 1);
            }

            // Left and right edges.
            for (int y = 0; y < height; y++)
            {
                AddSeed(0, y);
                AddSeed(width - 1, y);
            }

            // 8-connected flood fill.
            int[] dx =
            {
            -1, 0, 1,
            -1,     1,
            -1, 0, 1
        };

            int[] dy =
            {
            -1, -1, -1,
             0,      0,
             1,  1,  1
        };

            while (queue.Count > 0)
            {
                int index =
                    queue.Dequeue();

                int x =
                    index % width;

                int y =
                    index / width;

                for (int i = 0; i < 8; i++)
                {
                    int nx =
                        x + dx[i];

                    int ny =
                        y + dy[i];

                    if (nx < 0 ||
                        nx >= width ||
                        ny < 0 ||
                        ny >= height)
                    {
                        continue;
                    }

                    int neighbor =
                        ny * width + nx;

                    if (source[neighbor] == 0 ||
                        connected[neighbor] != 0)
                    {
                        continue;
                    }

                    connected[neighbor] = 255;
                    queue.Enqueue(neighbor);
                }
            }

            Mat result =
                new(
                    height,
                    width,
                    DepthType.Cv8U,
                    1);

            Marshal.Copy(
                connected,
                0,
                result.DataPointer,
                connected.Length);

            return result;
        }
    }
}
