using Emgu.CV;
using System.Runtime.InteropServices;

namespace RealmStudioImageAnalysisLib.StageDefinitions
{
    public sealed class ValidateJoinsStage : IPerimeterStage
    {
        public string Id => "ValidateJoins";

        // Radius around each endpoint that is allowed to contain
        // scaffold. This prevents the validation from rejecting the
        // actual endpoint neighborhood.
        private const int LandformJoinEndpointClearance = 4;

        // Sample spacing along a proposed repair line.
        private const double LandformJoinSampleSpacing = 1.0;

        // A proposed join should not pass through a substantial amount
        // of existing scaffold. This prevents long-range "shortcuts"
        // through unrelated terrain boundaries.
        private const double LandformJoinMaximumScaffoldFraction = 0.35;

        public PerimeterStageResult Process(
            PerimeterPipelineData data,
            PerimeterStageContext context)
        {
            ArgumentNullException.ThrowIfNull(data);
            ArgumentNullException.ThrowIfNull(context);

            context.CancellationToken.ThrowIfCancellationRequested();

            IReadOnlyList<LandformEndpointJoin> proposedJoins =
                data.Get<IReadOnlyList<LandformEndpointJoin>>(
                    "ProposedJoins");

            Mat dilatedScaffold =
                data.Get<Mat>("DilatedScaffold");

            List<LandformEndpointJoin> acceptedJoins = [];

            foreach (LandformEndpointJoin join in proposedJoins)
            {
                context.CancellationToken.ThrowIfCancellationRequested();

                if (IsScaffoldAwareJoinValid(
                    dilatedScaffold,
                    join))
                {
                    join.Accepted = true;
                    acceptedJoins.Add(join);
                }
            }

            data.Set("AcceptedJoins", acceptedJoins);

            context.Progress?.Report(
                new PerimeterAlgorithmProgress
                {
                    Stage = Id,
                    Progress = 1.0,
                    Status =
                        $"Validated joins: {acceptedJoins.Count} accepted " +
                        $"of {proposedJoins.Count} proposed."
                });

            return new PerimeterStageResult
            {
                Succeeded = true
            };
        }

        private static bool IsScaffoldAwareJoinValid(Mat scaffold, LandformEndpointJoin join)
        {
            double dx =
                join.End.X -
                join.Start.X;

            double dy =
                join.End.Y -
                join.Start.Y;

            double distance =
                Math.Sqrt(
                    dx * dx +
                    dy * dy);

            if (distance < 1.0)
                return false;

            int sampleCount =
                Math.Max(
                    2,
                    (int)Math.Ceiling(
                        distance /
                        LandformJoinSampleSpacing));

            int scaffoldSamples = 0;

            int totalSamples = 0;

            int width =
                scaffold.Cols;

            int height =
                scaffold.Rows;

            byte[] pixels =
                new byte[width * height];

            Marshal.Copy(
                scaffold.DataPointer,
                pixels,
                0,
                pixels.Length);

            for (int i = 0;
                 i <= sampleCount;
                 i++)
            {
                double t =
                    (double)i /
                    sampleCount;

                double x =
                    join.Start.X +
                    dx * t;

                double y =
                    join.Start.Y +
                    dy * t;

                int ix =
                    (int)Math.Round(x);

                int iy =
                    (int)Math.Round(y);

                if (ix < 0 ||
                    ix >= width ||
                    iy < 0 ||
                    iy >= height)
                {
                    return false;
                }

                // Don't count the endpoint neighborhoods against
                // the "existing scaffold" test. Those areas are
                // supposed to contain scaffold.
                if (i <= LandformJoinEndpointClearance ||
                    i >= sampleCount -
                         LandformJoinEndpointClearance)
                {
                    continue;
                }

                totalSamples++;

                if (pixels[
                        iy * width + ix] != 0)
                {
                    scaffoldSamples++;
                }
            }

            if (totalSamples <= 0)
                return true;

            double scaffoldFraction =
                (double)scaffoldSamples /
                totalSamples;

            return
                scaffoldFraction <=
                LandformJoinMaximumScaffoldFraction;
        }
    }
}
