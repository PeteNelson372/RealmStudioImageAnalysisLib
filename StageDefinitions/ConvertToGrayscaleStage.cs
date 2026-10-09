using Emgu.CV;
using Emgu.CV.CvEnum;

namespace RealmStudioImageAnalysisLib.StageDefinitions
{
    public sealed class ConvertToGrayscaleStage : IPerimeterStage
    {
        public string Id => "ConvertToGrayscale";

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

            Mat gray = new();

            CvInvoke.CvtColor(
                sourceMat,
                gray,
                ColorConversion.Bgra2Gray);

            // context.OutputArtifact is the name of the output artifact to store the result in.
            data.Set(
                context.OutputArtifact
                    ?? throw new InvalidOperationException(
                        $"{Id} requires an output artifact."),
                gray);

            context.Progress?.Report(
                new PerimeterAlgorithmProgress
                {
                    Stage = Id,
                    Progress = 1.0,
                    Status = "Image converted to grayscale."
                });

            return new PerimeterStageResult
            {
                Succeeded = true
            };
        }
    }
}
