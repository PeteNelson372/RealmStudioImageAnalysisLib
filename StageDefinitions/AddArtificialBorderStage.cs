using Emgu.CV;
using SkiaSharp;

namespace RealmStudioImageAnalysisLib.StageDefinitions
{
    public sealed class AddArtificialBorderStage : IPerimeterStage
    {
        public string Id => "AddArtificialBorder";

        private const int LandformArtificialBorderSize = 2;

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

            // Retrieve the declared input by name.
            SKBitmap bitmap =
                data.Get<SKBitmap>( context.GetInputArtifact(inputNames[0])
                        ?? throw new InvalidOperationException($"{Id} requires an input artifact."));

            SKBitmap paddedBitmap = CreateArtificialBorderBitmap(bitmap, LandformArtificialBorderSize);

            data.Set(context.OutputArtifact
                    ?? throw new InvalidOperationException(
                        $"{Id} requires an output artifact."),
                paddedBitmap);

            context.Progress?.Report(
                new PerimeterAlgorithmProgress
                {
                    Stage = Id,
                    Progress = 1.0,
                    Status = "Artificial border added."
                });

            return new PerimeterStageResult
            {
                Succeeded = true
            };
        }

        private static SKBitmap CreateArtificialBorderBitmap(SKBitmap source, int borderSize)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(borderSize, 1);

            int width =
                source.Width +
                borderSize * 2;

            int height =
                source.Height +
                borderSize * 2;

            SKBitmap padded =
                new(
                    width,
                    height,
                    SKColorType.Bgra8888,
                    SKAlphaType.Premul);

            using SKCanvas canvas =
                new(padded);

            // The artificial exterior of the map.
            canvas.Clear(SKColors.White);

            // Put the original image inside the artificial border.
            canvas.DrawBitmap(
                source,
                borderSize,
                borderSize,
                SKSamplingOptions.Default);

            return padded;
        }
    }
}
