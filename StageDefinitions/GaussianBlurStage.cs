using Emgu.CV;
using Emgu.CV.Structure;

namespace RealmStudioImageAnalysisLib.StageDefinitions
{
    public sealed class GaussianBlurStage : IPerimeterStage
    {
        public string Id => "GaussianBlur";

        private const int LandformBlurSize = 7;

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

            Mat source =
                data.Get<Mat>(
                    context.GetInputArtifact(inputNames[0])
                        ?? throw new InvalidOperationException(
                            $"{Id} requires an input artifact."));

            Mat blurred = new();

            CvInvoke.GaussianBlur(
                source,
                blurred,
                new System.Drawing.Size(
                    LandformBlurSize,
                    LandformBlurSize),
                0);

            data.Set(
                context.OutputArtifact
                    ?? throw new InvalidOperationException(
                        $"{Id} requires an output artifact."),
                blurred);

            context.Progress?.Report(
                new PerimeterAlgorithmProgress
                {
                    Stage = Id,
                    Progress = 1.0,
                    Status = "Gaussian blur applied."
                });

            return new PerimeterStageResult
            {
                Succeeded = true
            };
        }
    }
}
