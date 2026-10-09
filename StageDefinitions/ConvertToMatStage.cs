using Emgu.CV;
using Emgu.CV.CvEnum;
using SkiaSharp;
using System.Runtime.InteropServices;

namespace RealmStudioImageAnalysisLib.StageDefinitions
{
    public sealed class ConvertToMatStage : IPerimeterStage
    {
        public string Id => "ConvertToMat";

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

            SKBitmap bitmap =
                data.Get<SKBitmap>(
                    context.GetInputArtifact(inputNames[0])
                        ?? throw new InvalidOperationException(
                            $"{Id} requires an input artifact."));

            Mat sourceMat = CreateMat(bitmap);

            data.Set(
                context.OutputArtifact
                    ?? throw new InvalidOperationException(
                        $"{Id} requires an output artifact."),
                sourceMat);

            context.Progress?.Report(
                new PerimeterAlgorithmProgress
                {
                    Stage = Id,
                    Progress = 1.0,
                    Status = "Bitmap converted to Mat."
                });

            return new PerimeterStageResult
            {
                Succeeded = true
            };
        }

        private static Mat CreateMat(SKBitmap source)
        {
            using SKBitmap bitmap = source.Copy(SKColorType.Bgra8888)
                ?? throw new InvalidOperationException(
                    "Unable to convert image to BGRA8888.");

            Mat mat = new(
                bitmap.Height,
                bitmap.Width,
                DepthType.Cv8U,
                4);

            int byteCount = bitmap.RowBytes * bitmap.Height;

            byte[] pixels = bitmap.Bytes;

            Marshal.Copy(
                pixels,
                0,
                mat.DataPointer,
                byteCount);

            return mat;
        }
    }
}
