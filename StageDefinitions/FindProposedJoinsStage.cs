using Emgu.CV;
using SkiaSharp;

namespace RealmStudioImageAnalysisLib.StageDefinitions
{
    public sealed class FindProposedJoinsStage : IPerimeterStage
    {
        public string Id => "FindProposedJoins";

        // Maximum allowed angle between an endpoint's local direction
        // and the direction toward the other endpoint.
        private const double LandformEndpointMaxJoinAngleDegrees = 65.0;

        // Maximum distance between endpoints considered for repair.
        private const double LandformEndpointMaxJoinDistance = 18.0;

        public PerimeterStageResult Process(
            PerimeterPipelineData data,
            PerimeterStageContext context)
        {
            ArgumentNullException.ThrowIfNull(data);
            ArgumentNullException.ThrowIfNull(context);

            context.CancellationToken.ThrowIfCancellationRequested();

            List<LandformEndpoint> endpoints = data.Get<List<LandformEndpoint>>("Endpoints");

            Mat sourceMat = data.Get<Mat>("SourceMat");

            IReadOnlyList<LandformEndpointJoin> proposedJoins =
                FindProposedEndpointJoins(
                    endpoints,
                    sourceMat.Width,
                    sourceMat.Height);

            data.Set("ProposedJoins", proposedJoins);

            context.Progress?.Report(
                new PerimeterAlgorithmProgress
                {
                    Stage = Id,
                    Progress = 1.0,
                    Status = $"Found {proposedJoins.Count} proposed joins."
                });

            return new PerimeterStageResult
            {
                Succeeded = true
            };
        }

        private static List<LandformEndpointJoin> FindProposedEndpointJoins(List<LandformEndpoint> endpoints, int imageWidth, int imageHeight)
        {
            List<LandformEndpointJoin> joins = [];

            bool[] used =
                new bool[endpoints.Count];

            List<(double Distance, int A, int B)> candidates = [];

            for (int i = 0;
                 i < endpoints.Count;
                 i++)
            {
                for (int j = i + 1;
                     j < endpoints.Count;
                     j++)
                {
                    double dx =
                        endpoints[j].Position.X -
                        endpoints[i].Position.X;

                    double dy =
                        endpoints[j].Position.Y -
                        endpoints[i].Position.Y;

                    double distance =
                        Math.Sqrt(
                            dx * dx +
                            dy * dy);

                    if (distance >
                        LandformEndpointMaxJoinDistance)
                    {
                        continue;
                    }

                    if (distance < 1.0)
                        continue;

                    candidates.Add(
                        (distance, i, j));
                }
            }

            // Examine the closest endpoint pairs first.
            candidates.Sort(
                (a, b) =>
                    a.Distance.CompareTo(b.Distance));

            foreach (var candidate in candidates)
            {
                int indexA =
                    candidate.A;

                int indexB =
                    candidate.B;

                if (used[indexA] ||
                    used[indexB])
                {
                    continue;
                }

                LandformEndpoint endpointA =
                    endpoints[indexA];

                LandformEndpoint endpointB =
                    endpoints[indexB];

                SKPoint toB =
                    Normalize(
                        new SKPoint(
                            endpointB.Position.X -
                            endpointA.Position.X,
                            endpointB.Position.Y -
                            endpointA.Position.Y));

                SKPoint toA =
                    Normalize(
                        new SKPoint(
                            endpointA.Position.X -
                            endpointB.Position.X,
                            endpointA.Position.Y -
                            endpointB.Position.Y));

                double angleA =
                    AngleBetween(
                        endpointA.Direction,
                        toB);

                double angleB =
                    AngleBetween(
                        endpointB.Direction,
                        toA);

                if (angleA >
                    LandformEndpointMaxJoinAngleDegrees)
                {
                    continue;
                }

                if (angleB >
                    LandformEndpointMaxJoinAngleDegrees)
                {
                    continue;
                }

                joins.Add(
                    new LandformEndpointJoin
                    {
                        Start =
                            endpointA.Position,

                        End =
                            endpointB.Position,

                        Distance =
                            candidate.Distance,

                        AngleDegrees =
                            Math.Max(
                                angleA,
                                angleB)
                    });

                // Preserve the one-join-per-endpoint rule.
                used[indexA] = true;
                used[indexB] = true;
            }

            return joins;
        }

        private static SKPoint Normalize(SKPoint point)
        {
            // TODO: there is at least one Normalize method already in one of the libraries
            float length =
                MathF.Sqrt(
                    point.X * point.X +
                    point.Y * point.Y);

            if (length < 0.0001f)
                return new SKPoint(0, 0);

            return new SKPoint(
                point.X / length,
                point.Y / length);
        }

        private static double AngleBetween(SKPoint first, SKPoint second)
        {
            // TODO: thre might be an AngleBetween implementation already in one of the libraries
            first = Normalize(first);
            second = Normalize(second);

            float dotProduct =
                first.X * second.X +
                first.Y * second.Y;

            dotProduct =
                Math.Clamp(
                    dotProduct,
                    -1.0f,
                    1.0f);

            return
                Math.Acos(dotProduct) *
                180.0 /
                Math.PI;
        }
    }
}
