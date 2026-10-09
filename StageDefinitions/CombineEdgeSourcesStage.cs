using Emgu.CV;

namespace RealmStudioImageAnalysisLib.StageDefinitions
{
    public sealed class CombineEdgeSourcesStage : IPerimeterStage
    {
        public string Id => "CombineEdgeSources";

        public PerimeterStageResult Process(
            PerimeterPipelineData data,
            PerimeterStageContext context)
        {
            ArgumentNullException.ThrowIfNull(data);
            ArgumentNullException.ThrowIfNull(context);

            context.CancellationToken.ThrowIfCancellationRequested();

            List<string> inputNames = [.. context.InputArtifacts];

            // this stage requires exactly two input artifact.
            if (inputNames.Count != 2)
            {
                throw new InvalidOperationException(
                    $"Stage '{Id}' requires exactly two input artifact, but {inputNames.Count} were provided.");
            }

            // Retrieve the declared inputs by name.
            // This stage assumes the first input is the dilated scaffold, and the second input is the list of accepted joins.
            // The input names are expected to be provided in the order they are declared in the stage configuration.
            // If the input names are not provided in the expected order, an exception will be thrown.

            Mat cannyEdges = data.Get<Mat>(context.GetInputArtifact(inputNames[0]));
            Mat whiteBoundary = data.Get<Mat>(context.GetInputArtifact(inputNames[1]));

            Mat combinedEdges = new();

            CvInvoke.BitwiseOr(
                cannyEdges,
                whiteBoundary,
                combinedEdges);

            // context.OutputArtifact is the name of the output artifact to store the result in.
            data.Set(context.OutputArtifact
                    ?? throw new InvalidOperationException($"{Id} requires an output artifact."),
                combinedEdges);

            context.Progress?.Report(
                new PerimeterAlgorithmProgress
                {
                    Stage = Id,
                    Progress = 1.0,
                    Status = "Edge sources combined."
                });

            return new PerimeterStageResult
            {
                Succeeded = true
            };
        }
    }
}
