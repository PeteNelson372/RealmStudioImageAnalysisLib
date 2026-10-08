using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;

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

            Mat dilatedScaffold =
                data.Get<Mat>("DilatedScaffold");

            IReadOnlyList<LandformEndpointJoin> acceptedJoins =
                data.Get<IReadOnlyList<LandformEndpointJoin>>(
                    "AcceptedJoins");

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

            data.Set("RepairedScaffold", repairedScaffold);

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

