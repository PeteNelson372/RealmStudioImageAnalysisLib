using RealmStudioShapeRenderingLib;
using System.Diagnostics;

namespace RealmStudioImageAnalysisLib
{
    public sealed class PerimeterResultEvaluator
    {
        public static PerimeterEvaluationResult Evaluate(PerimeterPipelineResult result, int imageWidth, int imageHeight)
        {
            if (!result.Succeeded)
            {
                return new PerimeterEvaluationResult
                {
                    Accepted = false,
                    Score = 0,
                    FailureMode = "PipelineFailed",
                    Reason = result.FailureReason ??
                             "The pipeline failed."
                };
            }

            if (result.ImportRegions.Count == 0)
            {
                return new PerimeterEvaluationResult
                {
                    Accepted = false,
                    Score = 0,
                    FailureMode = "NoCandidates",
                    Reason = "The pipeline produced no candidate regions."
                };
            }

            List<ImportRegion> candidates =
                [.. result.ImportRegions
                .OrderByDescending(
                    r => r.Area)];

            ImportRegion best =
                candidates[0];

            double totalArea =
                candidates.Sum(r => r.Area);

            double imageArea =
                (double)imageWidth * imageHeight;

            double largestAreaFraction =
                imageArea > 0
                    ? best.Area / imageArea
                    : 0.0;

            double largestAreaDominance =
                totalArea > 0
                    ? best.Area / totalArea
                    : 0.0;

            double bestScaffoldSupport =
                candidates.Max(
                    r => r.ScaffoldPerimeterSupport);

            double bestConfidence =
                candidates.Max(
                    r => r.Confidence ?? 0.0);

            Dictionary<string, double> metrics = new()
            {
                ["CandidateCount"] = candidates.Count,
                ["LargestAreaFraction"] = largestAreaFraction,
                ["LargestAreaDominance"] = largestAreaDominance,
                ["BestScaffoldSupport"] = bestScaffoldSupport,
                ["BestConfidence"] = bestConfidence
            };

            Debug.WriteLine(
                "--------------------------------------------------");
            Debug.WriteLine(
                "Perimeter extraction evaluation");
            Debug.WriteLine(
                $"Candidates: {candidates.Count}");
            Debug.WriteLine(
                $"Largest area fraction: {largestAreaFraction:P1}");
            Debug.WriteLine(
                $"Largest area dominance: {largestAreaDominance:P1}");
            Debug.WriteLine(
                $"Best scaffold support: {bestScaffoldSupport:P1}");
            Debug.WriteLine(
                $"Best confidence: {bestConfidence:F1}");

            // ------------------------------------------------------------
            // Obvious failure: candidates exist, but none has meaningful
            // support from the scaffold.
            // ------------------------------------------------------------

            if (bestScaffoldSupport < 0.50)
            {
                return new PerimeterEvaluationResult
                {
                    Accepted = false,
                    Score = 0,
                    FailureMode = "InsufficientPerimeterSupport",
                    Reason =
                        $"Best candidate has only " +
                        $"{bestScaffoldSupport:P1} scaffold support.",
                    Metrics = metrics
                };
            }

            // ------------------------------------------------------------
            // At this point we have a technically plausible result.
            //
            // DO NOT reject it merely because ImportRegion.Confidence
            // is below 75. Confidence is a ranking signal, not a
            // pipeline-success signal.
            // ------------------------------------------------------------

            double score =
                CalculateScore(
                    candidates,
                    largestAreaFraction,
                    largestAreaDominance,
                    bestScaffoldSupport);

            bool accepted =
                score >= 50.0;

            return new PerimeterEvaluationResult
            {
                Accepted = accepted,
                Score = score,
                FailureMode =
                    accepted
                        ? string.Empty
                        : "LowOverallQuality",
                Reason =
                    accepted
                        ? "Candidate set passed extraction evaluation."
                        : "Candidate set did not meet overall quality criteria.",
                Metrics = metrics
            };
        }

        private static double CalculateScore(
            IReadOnlyList<ImportRegion> candidates,
            double largestAreaFraction,
            double largestAreaDominance,
            double scaffoldSupport)
        {
            double score = 0;

            // Scaffold support is currently our strongest direct evidence.
            score += scaffoldSupport * 40.0;

            // A large candidate is generally more useful than a collection
            // of tiny fragments.
            score +=
                Math.Clamp(
                    largestAreaFraction / 0.25,
                    0.0,
                    1.0) * 30.0;

            // If one candidate dominates the result, that's generally
            // preferable to a collection of similarly-sized fragments.
            score +=
                largestAreaDominance * 20.0;

            // Number of candidates is only a weak signal.
            //
            // We deliberately do NOT reject multiple candidates because
            // maps can legitimately contain multiple landforms.
            if (candidates.Count <= 3)
                score += 10.0;
            else if (candidates.Count <= 10)
                score += 5.0;

            return Math.Clamp(score, 0.0, 100.0);
        }
    }
}
