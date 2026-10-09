using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using RealmStudioShapeRenderingLib;

namespace RealmStudioImageAnalysisLib.StageDefinitions
{
    public sealed class AddAcceptedJoinsStage : IPerimeterStage
    {
        public string Id => "AddAcceptedJoins";

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

            Mat dilatedScaffold = data.Get<Mat>(context.GetInputArtifact(inputNames[0]));

            IReadOnlyList<LandformEndpointJoin> acceptedJoins =
                data.Get<IReadOnlyList<LandformEndpointJoin>>(context.GetInputArtifact(inputNames[1]));

            Mat repairedScaffold = dilatedScaffold.Clone();

            foreach (LandformEndpointJoin join in acceptedJoins)
            {
                context.CancellationToken.ThrowIfCancellationRequested();

                CvInvoke.Line(
                    repairedScaffold,

                    new System.Drawing.Point(
                        (int)Math.Round(join.Start.X),
                        (int)Math.Round(join.Start.Y)),

                    new System.Drawing.Point(
                        (int)Math.Round(join.End.X),
                        (int)Math.Round(join.End.Y)),

                    new MCvScalar(255),

                    1,

                    LineType.EightConnected,

                    0);
            }

            // Publish the result using the configured output name.
            if (string.IsNullOrWhiteSpace(context.OutputArtifact))
            {
                throw new InvalidOperationException($"Stage '{Id}' has no output artifact configured.");
            }

            data.Set(context.OutputArtifact, repairedScaffold);

            context.Progress?.Report(
                new PerimeterAlgorithmProgress
                {
                    Stage = Id,
                    Progress = 1.0,
                    Status =
                        $"Added {acceptedJoins.Count} accepted joins."
                });

            return new PerimeterStageResult
            {
                Succeeded = true
            };
        }
    }
}

