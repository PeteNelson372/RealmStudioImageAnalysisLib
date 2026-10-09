using Emgu.CV;

namespace RealmStudioImageAnalysisLib.StageDefinitions
{
    public sealed class CannyEdgesStage : IPerimeterStage
    {
        public string Id => "CannyEdges";

        private const double LandformCannyThreshold1 = 40.0;
        private const double LandformCannyThreshold2 = 100.0;

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
            Mat blurred = data.Get<Mat>(
                    context.GetInputArtifact(inputNames[0])
                        ?? throw new InvalidOperationException(
                            $"{Id} requires an input artifact."));

            Mat cannyEdges = new();

            CvInvoke.Canny(
                blurred,
                cannyEdges,
                LandformCannyThreshold1,
                LandformCannyThreshold2);

            // context.OutputArtifact is the name of the output artifact to store the result in.
            data.Set(context.OutputArtifact
                    ?? throw new InvalidOperationException($"{Id} requires an output artifact."),
                cannyEdges);

            context.Progress?.Report(
                new PerimeterAlgorithmProgress
                {
                    Stage = Id,
                    Progress = 1.0,
                    Status = "Canny edges detected."
                });

            return new PerimeterStageResult
            {
                Succeeded = true
            };
        }
    }
}
