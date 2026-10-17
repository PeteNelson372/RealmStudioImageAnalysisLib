using RealmStudioShapeRenderingLib;
using SkiaSharp;
using System.Diagnostics;

namespace RealmStudioImageAnalysisLib;

public sealed class PerimeterResultEvaluator
{
    private const int MinimumPerimeterPointCount = 5;
    private const double MinimumCandidateArea = 25.0;

    public static PerimeterEvaluationResult Evaluate(
        PerimeterPipelineResult result,
        int imageWidth,
        int imageHeight)
    {
        ArgumentNullException.ThrowIfNull(result);

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

        int usableCandidateCount = 0;
        int bestPointCount = 0;
        double largestCandidateArea = 0.0;

        Debug.WriteLine(
            "--------------------------------------------------");
        Debug.WriteLine(
            "Perimeter extraction evaluation");
        Debug.WriteLine(
            $"Candidates: {result.ImportRegions.Count}");

        foreach (ImportRegion region in result.ImportRegions)
        {
            bool usable = IsUsableCandidate(
                region,
                out int pointCount,
                out bool isClosed);

            if (usable)
            {
                usableCandidateCount++;
                bestPointCount = Math.Max(
                    bestPointCount,
                    pointCount);

                largestCandidateArea = Math.Max(
                    largestCandidateArea,
                    region.Area);
            }

            Debug.WriteLine(
                $"Candidate {region.Index}: " +
                $"points={pointCount}, " +
                $"area={region.Area:F0}, " +
                $"closed={isClosed}, " +
                $"usable={usable}");
        }

        Dictionary<string, double> metrics = new()
        {
            ["CandidateCount"] = result.ImportRegions.Count,
            ["UsableCandidateCount"] = usableCandidateCount,
            ["BestPointCount"] = bestPointCount,
            ["LargestUsableCandidateArea"] = largestCandidateArea,
            ["ImageWidth"] = imageWidth,
            ["ImageHeight"] = imageHeight
        };

        Debug.WriteLine(
            $"Usable candidates: {usableCandidateCount}");

        // Reject only when no candidate has plausible perimeter geometry.
        if (usableCandidateCount == 0)
        {
            return new PerimeterEvaluationResult
            {
                Accepted = false,
                Score = 0,
                FailureMode = "NoUsableCandidates",
                Reason =
                    "No candidates have a closed, usable perimeter path.",
                Metrics = metrics
            };
        }

        // This evaluator is a sanity check, not a quality ranking system.
        // The user will inspect the proposed perimeters in the UI.
        return new PerimeterEvaluationResult
        {
            Accepted = true,
            Score = 100.0,
            FailureMode = string.Empty,
            Reason =
                $"{usableCandidateCount} of " +
                $"{result.ImportRegions.Count} candidates have usable geometry.",
            Metrics = metrics
        };
    }

    private static bool IsUsableCandidate(
        ImportRegion region,
        out int pointCount,
        out bool isClosed)
    {
        pointCount = 0;
        isClosed = false;

        SKPath? path = region.PerimeterPath;

        if (path is null || path.IsEmpty)
            return false;

        pointCount = path.PointCount;

        if (pointCount < 3)
            return false;

        SKRect bounds = path.Bounds;

        if (bounds.Width <= 0 ||
            bounds.Height <= 0 ||
            !float.IsFinite(bounds.Left) ||
            !float.IsFinite(bounds.Top) ||
            !float.IsFinite(bounds.Right) ||
            !float.IsFinite(bounds.Bottom))
        {
            return false;
        }

        // Require every contour in the path to be closed.
        using SKPathMeasure measure = new(path, false);

        bool hasContour = false;
        isClosed = true;

        do
        {
            hasContour = true;

            if (!measure.IsClosed)
            {
                isClosed = false;
                break;
            }
        }
        while (measure.NextContour());

        if (!hasContour || !isClosed)
            return false;

        // Either sufficient path detail or a reasonable area is enough.
        bool hasEnoughGeometry =
            pointCount > MinimumPerimeterPointCount ||
            (double.IsFinite(region.Area) &&
             region.Area >= MinimumCandidateArea);

        return hasEnoughGeometry;
    }
}